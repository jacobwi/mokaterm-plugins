"""What every gallery script shares: the repository's places, its rules, running tools and talking to the CI log."""

import contextlib
import datetime
import json
import os
import re
import subprocess
import xml.etree.ElementTree as ElementTree

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WORK = os.path.join(ROOT, ".work")
FEED = os.path.join(ROOT, ".feed")
PLUGINS = os.path.join(ROOT, "plugins")
GALLERY = os.path.join(ROOT, "gallery")
SCHEMA = os.path.join(GALLERY, "schema.json")
PUBLIC_KEY = os.path.join(ROOT, "signing", "gallery-2026.pub.pem")
NUGET_CONFIG = os.path.join(ROOT, "nuget.config")
OVERRIDES_TARGETS = os.path.join(ROOT, "build", "MokatermGallery.targets")
TOOLCHAIN = os.path.join(WORK, "toolchain")

# The repository that publishes the index; CI passes its own name, which is the same unless the repository moves.
DEFAULT_REPOSITORY = "jacobwi/mokaterm-plugins"
MOKATERM_REPOSITORY = "jacobwi/Mokaterm"

CATEGORIES = ("protocols", "server-tools", "docker", "utilities", "security", "themes")

# The app's plugin id rule (PluginManifestReader.IsValidId).
ID_PATTERN = re.compile(r"[a-z0-9]+(?:[.-][a-z0-9]+)*")
MAX_ID_LENGTH = 64

# SemVer without build metadata: a version names a release tag and an asset, where "+" is not safe.
VERSION_PATTERN = re.compile(
    r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-((?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)"
    r"(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*))?"
)
MAX_VERSION_LENGTH = 64

COMMIT_PATTERN = re.compile(r"[0-9a-f]{40}")

IN_CI = os.environ.get("GITHUB_ACTIONS") == "true"


class GalleryError(Exception):
    """A problem the person running the script has to fix; the message says what and where."""


def is_valid_id(value):
    return isinstance(value, str) and len(value) <= MAX_ID_LENGTH and ID_PATTERN.fullmatch(value) is not None


def is_valid_version(value):
    return isinstance(value, str) and len(value) <= MAX_VERSION_LENGTH and VERSION_PATTERN.fullmatch(value) is not None


def is_commit(value):
    return isinstance(value, str) and COMMIT_PATTERN.fullmatch(value) is not None


def repository_url(repository):
    return f"https://github.com/{repository}"


def tag_for(plugin_id, version):
    return f"{plugin_id}-v{version}"


def package_name(plugin_id, version):
    return f"{plugin_id}-{version}.mokaplugin"


def metadata_name(plugin_id, version):
    return f"{plugin_id}-{version}.json"


# SemVer 2.0 precedence. Release versions sort above their prereleases, numeric identifiers below alphanumeric ones.
def version_key(version):
    match = VERSION_PATTERN.fullmatch(version)
    if match is None:
        raise GalleryError(f"\"{version}\" is not a version such as 1.2.0.")
    core = tuple(int(part) for part in match.group(1, 2, 3))
    if match.group(4) is None:
        return core, (1,)
    identifiers = []
    for identifier in match.group(4).split("."):
        identifiers.append((0, int(identifier), "") if identifier.isdigit() else (1, 0, identifier))
    return core, (0, tuple(identifiers))


def sdk_of(version):
    """The major.minor a plugin built against this Mokaterm version records as its sdk."""
    match = re.match(r"(\d+)\.(\d+)", version or "")
    if match is None:
        raise GalleryError(f"\"{version}\" is not a Mokaterm version.")
    return f"{int(match.group(1))}.{int(match.group(2))}"


def utc_now():
    return datetime.datetime.now(datetime.timezone.utc).replace(microsecond=0).strftime("%Y-%m-%dT%H:%M:%SZ")


def relative(path):
    try:
        return os.path.relpath(path, ROOT).replace("\\", "/")
    except ValueError:
        return path


# Two copies of one key would let a reviewer read one value while the scripts take the other.
def _no_duplicate_keys(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"the key \"{key}\" appears twice")
        result[key] = value
    return result


def parse_json(text, where):
    try:
        return json.loads(text, object_pairs_hook=_no_duplicate_keys)
    except ValueError as error:
        raise GalleryError(f"{where} is not valid JSON: {error}.") from None


def read_json(path):
    try:
        with open(path, encoding="utf-8-sig") as file:
            text = file.read()
    except OSError as error:
        raise GalleryError(f"Cannot read {relative(path)}: {error.strerror}.") from None
    return parse_json(text, relative(path))


def write_json(path, data, crlf=False):
    """Writes UTF-8 without a byte order mark. Repository files take CRLF; published and scratch files keep LF."""
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    text = json.dumps(data, indent=2, ensure_ascii=False) + "\n"
    if crlf:
        text = text.replace("\n", "\r\n")
    with open(path, "w", encoding="utf-8", newline="") as file:
        file.write(text)


def read_mokaterm_config():
    """mokaterm.json: the Mokaterm version plugins build against, and the ref its packages are built from, if any.

    A ref means the packages were packed from a Mokaterm checkout rather than published, and then nothing is released.
    MOKATERM_CONFIG names another file instead, which only build/e2e_local.py uses, to try both cases.
    """
    config = read_json(os.environ.get("MOKATERM_CONFIG") or os.path.join(ROOT, "mokaterm.json"))
    version = config.get("version") if isinstance(config, dict) else None
    if not isinstance(version, str) or not version.strip():
        raise GalleryError("mokaterm.json needs a version.")
    ref = config.get("ref")
    return version.strip(), ref.strip() if isinstance(ref, str) and ref.strip() else None


def feed_version():
    """The exact Mokaterm package version build/mokaterm_feed.py made available."""
    path = os.path.join(FEED, "mokaterm.props")
    if not os.path.isfile(path):
        raise GalleryError("Run build/mokaterm_feed.py first: there is no .feed/mokaterm.props yet.")
    node = ElementTree.parse(path).getroot().find("./PropertyGroup/MokatermVersion")
    if node is None or not (node.text or "").strip():
        raise GalleryError(".feed/mokaterm.props names no MokatermVersion; run build/mokaterm_feed.py again.")
    return node.text.strip()


# ---- the CI log

def _annotate(kind, message):
    if IN_CI:
        # Annotations are one line; the rest of a long message still reaches the log below it.
        first = message.splitlines()[0] if message else ""
        print(f"::{kind}::{first.replace('%', '%25').replace(chr(13), '%0D').replace(chr(10), '%0A')}", flush=True)
        if "\n" in message:
            print(message, flush=True)
    else:
        print(f"{kind}: {message}", flush=True)


def info(message):
    print(message, flush=True)


def warning(message):
    _annotate("warning", message)


def error(message):
    _annotate("error", message)


@contextlib.contextmanager
def group(title):
    if IN_CI:
        print(f"::group::{title}", flush=True)
    else:
        print(f"== {title}", flush=True)
    try:
        yield
    finally:
        if IN_CI:
            print("::endgroup::", flush=True)


def run(command, cwd=None, env=None, capture=False, quiet=False):
    """Runs a tool and fails with its exit code. Captured output comes back as text."""
    if not quiet:
        info("> " + " ".join(_quote(part) for part in command))
    merged = dict(os.environ)
    if env:
        merged.update(env)
    try:
        result = subprocess.run(
            command,
            cwd=cwd or ROOT,
            env=merged,
            stdout=subprocess.PIPE if capture else None,
            stderr=subprocess.PIPE if capture else None,
            text=capture,
            encoding="utf-8" if capture else None,
            errors="replace" if capture else None,
        )
    except FileNotFoundError:
        raise GalleryError(f"{command[0]} is not installed or not on PATH.") from None
    if result.returncode != 0:
        detail = ""
        if capture:
            detail = "\n" + "\n".join(part.strip() for part in (result.stdout, result.stderr) if part and part.strip())
        raise GalleryError(f"{os.path.basename(command[0])} failed with exit code {result.returncode}.{detail}")
    return result.stdout if capture else None


def _quote(part):
    return f"\"{part}\"" if " " in part else part


def dotnet_environment():
    return {
        "DOTNET_NOLOGO": "1",
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
        "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
    }


def restore_properties():
    """Every dotnet call restores through this repository's nuget.config into .work/packages, never the user's cache."""
    return [
        f"-p:RestoreConfigFile={NUGET_CONFIG}",
        f"-p:RestorePackagesPath={os.path.join(WORK, 'packages')}",
    ]


# No MSBuild node or compiler server outlives a build: one would keep files under .work open, and on a runner that built
# a community plugin it would be that plugin's code still running.
NO_SERVERS = ["-nodeReuse:false", "-p:UseSharedCompilation=false", "-nologo"]
