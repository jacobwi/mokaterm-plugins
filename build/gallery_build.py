"""Builds plugins from source and checks what comes out.

Ours build from plugins/<id>/src inside this repository. A community plugin is fetched at the entry's exact commit into
.work/community/<id>/repo and built there. Both build against the Mokaterm packages build/mokaterm_feed.py put in .feed,
whatever version their project names (build/MokatermGallery.targets), restore through this repository's nuget.config
into .work/packages, and write into .work/artifacts/<id>, so neither a checkout nor the user's NuGet cache is touched.
"""

import glob
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys

import gallery_package
from gallery_common import (
    NO_SERVERS, OVERRIDES_TARGETS, ROOT, TOOLCHAIN, WORK, GalleryError, dotnet_environment, feed_version, group, info,
    read_json, read_mokaterm_config, relative, restore_properties, run, sdk_of, warning, write_json,
)
from gallery_listings import OURS

LOADCHECK_PROJECT = os.path.join(ROOT, "build", "loadcheck", "Mokaterm.Gallery.LoadCheck.csproj")
LOADCHECK_NAME = "Mokaterm.Gallery.LoadCheck"
LOADCHECK_DLL = os.path.join(TOOLCHAIN, "loadcheck", LOADCHECK_NAME + ".dll")
TOOLCHAIN_FILE = os.path.join(TOOLCHAIN, "toolchain.json")

# A community build's own repository holds whatever it likes at its root; the folder above it holds these, so MSBuild,
# NuGet and the analyzers stop looking before they reach this repository's own build files.
_FENCE = {
    "Directory.Build.props": "<Project>\r\n</Project>\r\n",
    "Directory.Build.targets": "<Project>\r\n</Project>\r\n",
    "Directory.Packages.props": "<Project>\r\n</Project>\r\n",
    ".editorconfig": "root = true\r\n",
}


# ---- the toolchain: the load check and what the Mokaterm checkout says about itself

def mokaterm_source(source=None):
    path = os.path.abspath(source or os.environ.get("MOKATERM_SOURCE") or os.path.join(os.path.dirname(ROOT), "Mokaterm"))
    if not os.path.isfile(os.path.join(path, "src", "Mokaterm.Core", "Mokaterm.Core.csproj")):
        raise GalleryError(f"No Mokaterm checkout at {path}. Pass --source or set MOKATERM_SOURCE.")
    return path


def build_toolchain(source=None):
    """Builds the load check against a Mokaterm checkout and records which Mokaterm it is."""
    source = mokaterm_source(source)
    version = feed_version()
    expected_sdk = sdk_of(version)
    output = os.path.join(WORK, "toolchain-build")
    with group("Building the load check"):
        run(["dotnet", "build", LOADCHECK_PROJECT, "-c", "Release", f"-p:MokatermSource={source}",
             "-p:UseArtifactsOutput=true", f"-p:ArtifactsPath={output}", *restore_properties(), *NO_SERVERS],
            env=dotnet_environment())
    built = os.path.join(output, "bin", LOADCHECK_NAME, "release")
    if not os.path.isfile(os.path.join(built, LOADCHECK_NAME + ".dll")):
        raise GalleryError(f"The load check build wrote nothing to {relative(built)}.")
    shutil.rmtree(os.path.join(TOOLCHAIN, "loadcheck"), ignore_errors=True)
    shutil.copytree(built, os.path.join(TOOLCHAIN, "loadcheck"))

    host_sdk = run(["dotnet", LOADCHECK_DLL, "--host-sdk"], capture=True, quiet=True, env=dotnet_environment()).strip()
    if host_sdk != expected_sdk:
        raise GalleryError(
            f"The Mokaterm checkout at {source} is Mokaterm {host_sdk}, but mokaterm.json builds plugins for {expected_sdk}. "
            "Check out the ref mokaterm.json names, or correct its version.")

    modules = builtin_module_ids(source)
    runtime = run(["dotnet", LOADCHECK_DLL, "--runtime-assemblies"], capture=True, quiet=True, env=dotnet_environment()).split()
    if len(runtime) < 50:
        raise GalleryError("The load check found almost no .NET runtime assemblies, which cannot be right.")
    _, ref = read_mokaterm_config()
    toolchain = {
        "mokatermVersion": version,
        "sdk": expected_sdk,
        "mokatermRef": ref,
        "mokatermCommit": _git_head(source),
        "builtinModules": modules,
        "runtimeAssemblies": sorted(runtime, key=str.lower),
    }
    write_json(TOOLCHAIN_FILE, toolchain)
    info(f"Load check ready: Mokaterm {version} (sdk {expected_sdk}), built-in modules {', '.join(modules)}.")
    return toolchain


def read_toolchain():
    if not os.path.isfile(TOOLCHAIN_FILE) or not os.path.isfile(LOADCHECK_DLL):
        raise GalleryError("Run build/gallery.py toolchain first: the load check is not built yet.")
    toolchain = read_json(TOOLCHAIN_FILE)
    if toolchain.get("mokatermVersion") != feed_version():
        raise GalleryError("The load check was built for another Mokaterm than .feed holds; run build/gallery.py toolchain again.")
    return toolchain


_MODULE_INFO = re.compile(r"ModuleInfo\s+Info\s*\{\s*get;\s*\}\s*=\s*new\(\s*(\"([^\"]+)\"|([A-Za-z_][A-Za-z0-9_]*))")
_CONSTANT = r"const\s+string\s+{0}\s*=\s*\"([^\"]+)\""


def builtin_module_ids(source):
    """The module ids Mokaterm registers itself, read from its source: the app refuses a plugin that takes one."""
    ids = set()
    for path in glob.glob(os.path.join(source, "src", "**", "*.cs"), recursive=True):
        with open(path, encoding="utf-8-sig") as file:
            text = file.read()
        if not re.search(r":\s*IMokatermModule\b", text):
            continue
        match = _MODULE_INFO.search(text)
        if match is None:
            raise GalleryError(f"Cannot read the module id in {path}; build/gallery_build.py needs to learn its new shape.")
        if match.group(2):
            ids.add(match.group(2))
            continue
        constant = re.search(_CONSTANT.format(re.escape(match.group(3))), text)
        if constant is None:
            raise GalleryError(f"Cannot find the constant {match.group(3)} that names the module in {path}.")
        ids.add(constant.group(1))
    if not ids:
        raise GalleryError(f"Found no modules in {source}/src; is it a Mokaterm checkout?")
    return sorted(ids)


# ---- building

def build_listing(listing, out_dir, run_tests=True, clone_from=None):
    """Builds one listing into out_dir and returns the package path. Ours run their tests too."""
    version = feed_version()
    artifacts = os.path.join(WORK, "artifacts", listing.id)
    shutil.rmtree(artifacts, ignore_errors=True)
    if listing.kind == OURS:
        project = listing.project
        commit = _git_head(ROOT)
    else:
        repo = fetch_community(listing, clone_from)
        project = os.path.join(repo, *listing.project_in_source.split("/"))
        if not os.path.isfile(project):
            raise GalleryError(f"{listing.source} has no {listing.project_in_source} at {listing.commit}.")
        commit = listing.commit

    properties = [
        "-c", "Release",
        "-p:MokatermPluginPackOnBuild=true",
        "-p:ContinuousIntegrationBuild=true",
        f"-p:MokatermVersion={version}",
        f"-p:MokatermGalleryVersion={version}",
        f"-p:CustomAfterMicrosoftCommonTargets={OVERRIDES_TARGETS}",
        "-p:UseArtifactsOutput=true",
        f"-p:ArtifactsPath={artifacts}",
        *restore_properties(),
        *NO_SERVERS,
    ]
    with group(f"Building {listing.id}"):
        run(["dotnet", "build", project, *properties], env=dotnet_environment())

    packages = [path for path in glob.glob(os.path.join(artifacts, "bin", "**", "*.mokaplugin"), recursive=True)
                if os.path.basename(path).startswith(listing.id + "-")]
    if len(packages) != 1:
        raise GalleryError(
            f"Building {listing.id} wrote {len(packages)} packages named {listing.id}-*.mokaplugin. Is MokatermPluginId {listing.id}, "
            "and does the project reference Mokaterm.Plugin.Sdk?")

    if run_tests and listing.kind == OURS:
        for test in listing.tests:
            with group(f"Testing {relative(test)}"):
                run_tests_of(test, properties)

    os.makedirs(out_dir, exist_ok=True)
    for old in glob.glob(os.path.join(out_dir, "*.mokaplugin")):
        os.remove(old)
    package = os.path.join(out_dir, os.path.basename(packages[0]))
    shutil.copyfile(packages[0], package)
    write_json(os.path.join(out_dir, "build.json"), {
        "id": listing.id,
        "kind": listing.kind,
        "package": os.path.basename(package),
        "commit": commit,
        "mokatermVersion": version,
    })
    return package


def run_tests_of(test, properties):
    """Builds a test project and runs its test application directly.

    `dotnet test` on Microsoft.Testing.Platform reported "Zero tests ran" for a test project built into the artifacts
    layout, while the same build's own executable found and ran every test. Our tests are xunit v3 applications, so the
    application is the runner: it exits non-zero when a test fails, and nothing in between can lose them.
    """
    run(["dotnet", "build", test, *properties], env=dotnet_environment())
    msbuild = ["-p:Configuration=Release" if part == "Release" and index and properties[index - 1] == "-c" else part
               for index, part in enumerate(properties) if part != "-c"]
    target = run(["dotnet", "msbuild", test, "-getProperty:TargetPath", *msbuild],
                 env=dotnet_environment(), capture=True, quiet=True).strip()
    if not target.endswith(".dll") or not os.path.isfile(target):
        raise GalleryError(f"Could not find the test application {relative(test)} built ({target or 'nothing'}).")
    run(["dotnet", target], env=dotnet_environment())


def fetch_community(listing, clone_from=None):
    """Checks out the entry's commit in .work/community/<id>/repo. clone_from replaces the URL, for local tests only."""
    base = os.path.join(WORK, "community", listing.id)
    repo = os.path.join(base, "repo")
    os.makedirs(base, exist_ok=True)
    for name, text in _FENCE.items():
        with open(os.path.join(base, name), "w", encoding="utf-8", newline="") as file:
            file.write(text)

    url = os.path.abspath(clone_from) if clone_from else listing.source
    env = {"GIT_TERMINAL_PROMPT": "0", "GIT_LFS_SKIP_SMUDGE": "1"}
    git = ["git", "-c", "advice.detachedHead=false", "-c", "core.autocrlf=false"]
    with group(f"Fetching {listing.id} from {url} at {listing.commit}"):
        if _git_head(repo) != listing.commit or _git_dirty(repo):
            _remove_tree(repo)
            run([*git, "init", "-q", repo], env=env)
            run([*git, "-C", repo, "remote", "add", "origin", url], env=env)
            try:
                run([*git, "-C", repo, "fetch", "-q", "--depth", "1", "origin", listing.commit], env=env)
            except GalleryError:
                # A server that will not hand out a commit by name still gives the whole history.
                run([*git, "-C", repo, "fetch", "-q", "--tags", "origin"], env=env)
            run([*git, "-C", repo, "checkout", "-q", "--detach", listing.commit], env=env)
            if os.path.isfile(os.path.join(repo, ".gitmodules")):
                run([*git, "-C", repo, "submodule", "update", "-q", "--init", "--recursive", "--depth", "1"], env=env)
        else:
            # A checkout kept from an earlier run may have come from elsewhere; the tag is checked where the entry points.
            run([*git, "-C", repo, "remote", "set-url", "origin", url], env=env, quiet=True)
        head = _git_head(repo)
        if head != listing.commit:
            raise GalleryError(f"{url} checked out at {head}, not {listing.commit}.")
        if listing.tag:
            _check_tag(git, repo, listing, env)
        if not _has_license(repo):
            raise GalleryError(f"{listing.source} has no license file (LICENSE, LICENSE.md, COPYING) at its root; the gallery lists open source plugins only.")
    return repo


def _check_tag(git, repo, listing, env):
    lines = run([*git, "-C", repo, "ls-remote", "--tags", "origin", f"refs/tags/{listing.tag}", f"refs/tags/{listing.tag}^{{}}"],
                capture=True, env=env, quiet=True).splitlines()
    shas = {line.split()[1]: line.split()[0] for line in lines if line.strip()}
    pointed = shas.get(f"refs/tags/{listing.tag}^{{}}") or shas.get(f"refs/tags/{listing.tag}")
    if pointed is None:
        raise GalleryError(f"gallery/{listing.id}.json names the tag {listing.tag}, which {listing.source} does not have.")
    if pointed != listing.commit:
        raise GalleryError(f"gallery/{listing.id}.json names the tag {listing.tag}, which points at {pointed}, not at the entry's commit {listing.commit}.")


def _has_license(repo):
    names = {name.lower() for name in os.listdir(repo)}
    return any(name in names for name in ("license", "license.md", "license.txt", "licence", "licence.md", "copying", "copying.md", "copying.txt"))


def _git_head(path):
    if not os.path.isdir(os.path.join(path, ".git")):
        return None
    try:
        result = subprocess.run(["git", "-C", path, "rev-parse", "HEAD"], capture_output=True, text=True)
    except FileNotFoundError:
        return None
    return result.stdout.strip() if result.returncode == 0 else None


def _git_dirty(path):
    result = subprocess.run(["git", "-C", path, "status", "--porcelain", "--untracked-files=no"], capture_output=True, text=True)
    return result.returncode != 0 or bool(result.stdout.strip())


def _remove_tree(path):
    # Git marks its objects read-only, which stops shutil on Windows.
    def make_writable(function, target, _):
        os.chmod(target, 0o700)
        function(target)

    if not os.path.isdir(path):
        return
    if sys.version_info >= (3, 12):
        shutil.rmtree(path, onexc=make_writable)
    else:
        shutil.rmtree(path, onerror=make_writable)


# ---- checking a built package

def check_built(package, plugin_id, taken_modules=(), out_dir=None, verified=False):
    """The static checks, then the load check. Returns (report, loadcheck result); raises when either fails."""
    toolchain = read_toolchain()
    report = gallery_package.check(package, plugin_id, toolchain["sdk"], toolchain["runtimeAssemblies"], verified)
    if not report.ok:
        raise GalleryError(f"{os.path.basename(package)} is not ready to list:\n  " + "\n  ".join(report.problems))
    info(f"{os.path.basename(package)}: {report.size} bytes, sha256 {report.sha256}, {len(report.files)} files, "
         f"{'code' if report.has_code else 'data only'}, hosts {', '.join(report.manifest['hosts']) or 'all'}.")

    taken = sorted(set(toolchain["builtinModules"]) | set(taken_modules))
    result_path = os.path.join(out_dir or os.path.dirname(package), "loadcheck.json")
    work = os.path.join(WORK, "loadcheck", plugin_id)
    command = ["dotnet", LOADCHECK_DLL, "--package", package, "--id", plugin_id, "--work", work, "--result", result_path]
    for module in taken:
        command += ["--taken-module", module]
    with group(f"Loading {plugin_id} through Mokaterm's plugin loader"):
        process = subprocess.run(command, cwd=ROOT, env={**os.environ, **dotnet_environment()})
    if not os.path.isfile(result_path):
        raise GalleryError(f"The load check stopped without a result (exit code {process.returncode}).")
    with open(result_path, encoding="utf-8") as file:
        result = json.load(file)
    for message in result.get("warnings", []):
        warning(f"{plugin_id}: {message}")
    if process.returncode != 0 or not result.get("ok"):
        raise GalleryError(f"{plugin_id} does not load in Mokaterm {result.get('hostSdk')}:\n  " + "\n  ".join(result.get("problems", [])))
    return report, result


def assembly_clash(manifest, published):
    """Another listed plugin with the same assembly name: the app loads only the first of two such plugins."""
    assembly = (manifest.get("assembly") or "").lower()
    if not assembly:
        return None
    for meta in published:
        if meta["id"] != manifest["id"] and (meta.get("assembly") or "").lower() == assembly:
            return f"{manifest['assembly']} is also the assembly of {meta['id']}, and the app loads only one of two plugins with one assembly name. Rename it (AssemblyName)."
    return None


def taken_module_ids(published, plugin_id):
    """Module ids other listed plugins registered: two plugins with one id cannot both be installed."""
    return sorted({meta["moduleId"] for meta in published if meta.get("moduleId") and meta["id"] != plugin_id})


def sha256_of(path):
    digest = hashlib.sha256()
    with open(path, "rb") as file:
        for chunk in iter(lambda: file.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def plugin_version(listing):
    """Our plugin's version as MSBuild works it out, without building: what the next package would carry."""
    if listing.kind != OURS:
        raise GalleryError("Only our own plugins have a version before they are built.")
    version = feed_version()
    output = run(["dotnet", "msbuild", listing.project, "-getProperty:Version", "-p:Configuration=Release",
                  f"-p:MokatermVersion={version}", *NO_SERVERS],
                 capture=True, quiet=True, env=dotnet_environment()).strip()
    if not output:
        raise GalleryError(f"MSBuild gives {relative(listing.project)} no Version.")
    return output
