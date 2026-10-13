"""Reads a .mokaplugin the way Mokaterm does, and holds it to the gallery's rules on top.

The limits and path rules are the app's (Mokaterm.Core/Plugins/PluginPackage.cs and PluginManifestReader.cs), so a
package that passes here is one the app reads. The gallery adds its own: publisher, description and sdk are required,
the version is SemVer, the sdk is the one the gallery builds for, and lib carries nothing Mokaterm ships.
"""

import hashlib
import json
import os
import zipfile
from dataclasses import dataclass, field

from gallery_common import is_valid_id, is_valid_version

MAX_PACKAGE_BYTES = 64 * 1024 * 1024
MAX_FILE_BYTES = 64 * 1024 * 1024
MAX_TOTAL_BYTES = 128 * 1024 * 1024
MAX_ENTRIES = 4096
MAX_PATH_LENGTH = 240
MAX_MANIFEST_BYTES = 64 * 1024
MAX_SHORT_TEXT = 100
MAX_DESCRIPTION = 1000
# The app reads at most this many themes and skips any bigger than this, so either would be a theme that never shows.
MAX_THEME_FILES = 64
MAX_THEME_BYTES = 512 * 1024

TOP_LEVEL_FOLDERS = ("lib", "wwwroot", "themes")
MANIFEST = "plugin.json"
MANIFEST_FIELDS = ("id", "name", "version", "publisher", "description", "sdk", "assembly", "hosts")
HOSTS = ("desktop", "web")

# Assembly names kept for Mokaterm and its own plugins: a community plugin named like one would look like ours on the
# Plugins page and in _content/<assembly>/, and could clash with an assembly Mokaterm adds later.
RESERVED_ASSEMBLY_PREFIXES = ("mokaterm.", "moka.")

# What every Mokaterm host ships: Mokaterm's contracts and what they stand on. A copy in lib is never the one that runs,
# because the host hands out its own. System.* is judged by name against .NET's own assemblies instead of by prefix:
# packages such as System.Security.Cryptography.Pkcs are not part of .NET, and the desktop app does not ship them.
HOST_PREFIXES = ("mokaterm.", "moka.red.", "microsoft.aspnetcore.", "microsoft.extensions.", "microsoft.jsinterop.")

_STORED = 0
_DEFLATED = 8
_UTF8_FLAG = 0x800
_ENCRYPTED_FLAG = 0x1


@dataclass
class PackageReport:
    path: str
    size: int = 0
    sha256: str = ""
    manifest: dict = field(default_factory=dict)
    files: list = field(default_factory=list)
    problems: list = field(default_factory=list)

    @property
    def ok(self):
        return not self.problems

    @property
    def has_code(self):
        return bool(self.manifest.get("assembly"))

    @property
    def theme_count(self):
        return sum(1 for name in self.files if name.startswith("themes/"))


def check(path, expected_id, expected_sdk, runtime_assemblies=(), verified=False):
    """Every problem with the package at path, plus what it holds. Never raises for a bad package.

    runtime_assemblies names the assemblies of .NET itself (the toolchain reads them from the runtime it runs on).
    verified is true for our own plugins (plugins/<id>/), which may use the assembly names kept for Mokaterm.
    """
    report = PackageReport(path=path)
    try:
        report.size = os.path.getsize(path)
        with open(path, "rb") as file:
            data = file.read(MAX_PACKAGE_BYTES + 1)
    except OSError as error:
        report.problems.append(f"Cannot read the package: {error.strerror}.")
        return report
    if len(data) > MAX_PACKAGE_BYTES:
        report.problems.append(f"The package is larger than {MAX_PACKAGE_BYTES // (1024 * 1024)} MiB, which the app refuses.")
        return report
    report.sha256 = hashlib.sha256(data).hexdigest()

    contents = _read_entries(path, report)
    if contents is None:
        return report
    report.files = sorted(contents)

    manifest = _read_manifest(contents, report.problems)
    if manifest is None:
        return report
    report.manifest = manifest

    if manifest["id"] != expected_id:
        report.problems.append(f"plugin.json has the id {manifest['id']}, but the plugin is listed as {expected_id}. MokatermPluginId has to be {expected_id}.")
    if manifest.get("sdk") != expected_sdk:
        report.problems.append(
            f"plugin.json says the plugin was built for Mokaterm {manifest.get('sdk') or '(nothing)'}, but the gallery builds for {expected_sdk}.")

    lib = [name for name in report.files if name.startswith("lib/")]
    assembly = manifest.get("assembly")
    if assembly:
        if f"lib/{assembly}" not in contents:
            report.problems.append(f"plugin.json names {assembly}, which is not in the package's lib folder.")
        if not verified and assembly.lower().startswith(RESERVED_ASSEMBLY_PREFIXES):
            report.problems.append(
                f"The plugin's assembly is {assembly}. Assembly names starting with Mokaterm. or Moka. are kept for Mokaterm "
                "and its own plugins, so a community plugin needs another one (AssemblyName in the project; it also names "
                "_content/<assembly>/, where the plugin's page files are served).")
        report.problems.extend(_host_files(lib, assembly, {name.lower() for name in runtime_assemblies}))
    elif lib:
        report.problems.append("plugin.json names no assembly, so the plugin carries no code, but the package has a lib folder.")

    themes = [name for name in report.files if name.startswith("themes/")]
    nested = [name for name in themes if name.count("/") > 1]
    if nested:
        report.problems.append(f"The app reads themes straight from themes/, not from folders inside it: {', '.join(nested[:5])}.")
    if len(themes) > MAX_THEME_FILES:
        report.problems.append(f"The package holds {len(themes)} themes; the app reads the first {MAX_THEME_FILES} and skips the rest.")
    for name in themes:
        if len(contents[name]) > MAX_THEME_BYTES:
            report.problems.append(f"{name} is larger than {MAX_THEME_BYTES // 1024} KiB, which the app skips.")
    return report


def _read_entries(path, report):
    try:
        archive = zipfile.ZipFile(path)
    except (zipfile.BadZipFile, OSError):
        report.problems.append("The package is not a readable zip.")
        return None
    contents = {}
    seen = set()
    total = 0
    with archive:
        entries = archive.infolist()
        if len(entries) > MAX_ENTRIES:
            report.problems.append(f"The package holds more than {MAX_ENTRIES} entries.")
            return None
        for entry in entries:
            name = entry.filename
            if name.endswith("/") or name.endswith("\\"):
                continue
            problem = _path_problem(entry)
            if problem:
                report.problems.append(problem)
                continue
            path_name = name.replace("\\", "/")
            # The app keeps files in a case-insensitive map, so two names that differ only in case are one file twice.
            if path_name.lower() in seen:
                report.problems.append(f"The package holds \"{path_name}\" twice.")
                continue
            seen.add(path_name.lower())
            # The sizes in a zip's directory are whatever its author wrote, so bytes are counted as they come out.
            limit = min(MAX_FILE_BYTES, MAX_TOTAL_BYTES - total)
            try:
                with archive.open(entry) as stream:
                    data = stream.read(limit + 1)
            except (zipfile.BadZipFile, OSError, EOFError, RuntimeError, NotImplementedError):
                report.problems.append(f"The package is damaged: \"{path_name}\" could not be unpacked.")
                return None
            if len(data) > limit:
                report.problems.append(
                    f"\"{path_name}\" is larger than {MAX_FILE_BYTES // (1024 * 1024)} MiB unpacked." if limit == MAX_FILE_BYTES
                    else f"The package unpacks to more than {MAX_TOTAL_BYTES // (1024 * 1024)} MiB.")
                return None
            total += len(data)
            contents[path_name] = data
    if report.problems:
        return None
    return contents


def _path_problem(entry):
    name = entry.filename
    path = name.replace("\\", "/")
    segments = path.split("/")
    if (not path or len(path) > MAX_PATH_LENGTH or path.startswith("/") or ":" in path
            or any(_is_control(char) for char in path) or any(segment in ("", ".", "..") for segment in segments)):
        return f"The package holds a path the app refuses: \"{_short(name)}\"."
    if not path.isascii() and not entry.flag_bits & _UTF8_FLAG:
        return f"\"{_short(name)}\" has a name that is not ASCII and not marked as UTF-8, so the app could read it differently."
    if entry.flag_bits & _ENCRYPTED_FLAG:
        return f"\"{_short(path)}\" is encrypted."
    if entry.compress_type not in (_STORED, _DEFLATED):
        return f"\"{_short(path)}\" is compressed with a method the app cannot unpack; use deflate."
    if len(segments) == 1:
        if path != MANIFEST:
            return f"The package holds \"{_short(path)}\". A plugin package has plugin.json, lib, wwwroot and themes, and nothing else."
    elif segments[0] not in TOP_LEVEL_FOLDERS:
        return f"The package holds \"{_short(path)}\". A plugin package has plugin.json, lib, wwwroot and themes, and nothing else."
    return None


def _read_manifest(contents, problems):
    raw = contents.get(MANIFEST)
    if raw is None:
        problems.append("The package has no plugin.json, so it is not a Mokaterm plugin.")
        return None
    if len(raw) > MAX_MANIFEST_BYTES:
        problems.append("plugin.json is too large to be a manifest.")
        return None
    try:
        data = json.loads(raw.decode("utf-8-sig"))
    except (UnicodeDecodeError, ValueError):
        problems.append("plugin.json is not valid UTF-8 JSON.")
        return None
    if not isinstance(data, dict):
        problems.append("plugin.json is not a JSON object.")
        return None

    found = []
    unknown = [key for key in data if key not in MANIFEST_FIELDS]
    if unknown:
        found.append(f"plugin.json holds {', '.join(unknown)}, which the gallery does not know. Build it with the Mokaterm plugin SDK.")
    for key in MANIFEST_FIELDS:
        if key in data and key != "hosts" and not isinstance(data[key], str):
            found.append(f"plugin.json's {key} is not a string.")
    if found:
        problems.extend(found)
        return None

    manifest = {key: data[key].strip() for key in MANIFEST_FIELDS if key != "hosts" and isinstance(data.get(key), str) and data[key].strip()}
    if not is_valid_id(manifest.get("id")):
        found.append(f"plugin.json's id {manifest.get('id')!r} is not a plugin id (lower-case letters and digits joined by dots or dashes, up to 64 characters).")
    for key, limit in (("name", MAX_SHORT_TEXT), ("publisher", MAX_SHORT_TEXT), ("description", MAX_DESCRIPTION)):
        value = manifest.get(key)
        if value is None:
            found.append(f"plugin.json has no {key}; the gallery shows it (MokatermPlugin{key.capitalize()}).")
        elif len(value) > limit:
            found.append(f"plugin.json's {key} is longer than {limit} characters.")
        elif any(_is_control(char) and char not in ("\n", "\t") for char in value) or (key != "description" and any(_is_control(char) for char in value)):
            found.append(f"plugin.json's {key} holds control characters.")
    if not is_valid_version(manifest.get("version")):
        found.append(f"plugin.json's version {manifest.get('version')!r} is not a version the gallery takes: MAJOR.MINOR.PATCH with an optional -label, no +build part.")
    sdk = manifest.get("sdk")
    if sdk is None:
        found.append("plugin.json does not say which Mokaterm the plugin was built for (sdk).")
    elif not _is_sdk(sdk):
        found.append(f"plugin.json's sdk {sdk!r} is not a version such as 0.2.")
    assembly = manifest.get("assembly")
    if assembly is not None and (
            len(assembly) > MAX_SHORT_TEXT or "/" in assembly or "\\" in assembly or not assembly.lower().endswith(".dll")
            or any(char in assembly for char in '<>:"|?*') or any(_is_control(char) for char in assembly)):
        found.append("plugin.json's assembly must be a .dll file name, without a folder.")

    hosts = data.get("hosts") or []
    if not isinstance(hosts, list) or not all(isinstance(host, str) for host in hosts):
        found.append("plugin.json's hosts is not a list of names.")
        hosts = []
    for host in hosts:
        if host not in HOSTS:
            found.append(f"plugin.json names the host {host!r}; a plugin runs on \"desktop\", \"web\" or both.")
    if len(set(hosts)) != len(hosts):
        found.append("plugin.json names a host twice.")
    manifest["hosts"] = [host for host in HOSTS if host in hosts]

    if found:
        problems.extend(found)
        return None
    return manifest


def _host_files(lib, assembly, runtime):
    own = assembly[:-4].lower()
    own_files = {f"lib/{own}{suffix}" for suffix in (".dll", ".pdb", ".deps.json", ".xml")}
    found = []
    for name in lib:
        lowered = name.lower()
        if lowered in own_files:
            continue
        file_name = lowered.rsplit("/", 1)[-1]
        # Satellite assemblies sit in lib/<culture>/ and carry the name of the assembly they belong to.
        stem = file_name[:-len(".resources.dll")] if file_name.endswith(".resources.dll") else file_name.rsplit(".", 1)[0]
        if stem == own:
            continue
        if file_name.startswith(HOST_PREFIXES):
            found.append(f"{name} is something Mokaterm ships, so the plugin would get the host's copy; the plugin SDK leaves these out.")
        elif stem in runtime:
            found.append(f"{name} is part of .NET itself, which every host has; the plugin would get the host's copy.")
    return found


def _is_sdk(text):
    parts = text.split(".")
    return len(parts) == 2 and all(part.isdigit() and part.isascii() for part in parts)


# .NET's char.IsControl: C0, DEL and C1.
def _is_control(char):
    code = ord(char)
    return code < 0x20 or 0x7F <= code <= 0x9F


def _short(value):
    return value if len(value) <= 60 else value[:60] + "..."
