"""Runs the whole gallery once on this machine, without GitHub: build, check, release, index, sign, verify.

Two fixtures are made from Mokaterm's sample plugin (samples/Mokaterm.Plugins.Sample in the Mokaterm checkout):

    moka.sample    one of ours, as plugins/<id>/ would hold it
    acme.sample    a community plugin in a git repository of its own, with two commits (1.0.0 and 1.1.0), a tag,
                   Mokaterm package versions pinned to a release that does not exist and a nuget.config naming a feed
                   that does not exist, which is what proves the gallery builds against its own packages and sources

Everything lives in .work/e2e, which is emptied first, and the real plugins/ and gallery/ are never read. Releases go to
a folder instead of GitHub, and the index is signed with a throwaway key. Both ways mokaterm.json can be written are
tried, through MOKATERM_CONFIG: with a ref, where publish builds everything and releases nothing, and with a published
version, where it releases. Run build/mokaterm_feed.py and build/gallery.py toolchain first.
"""

import hashlib
import json
import os
import re
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True

from gallery_common import DEFAULT_REPOSITORY, PUBLIC_KEY, ROOT, WORK, package_name  # noqa: E402

E2E = os.path.join(WORK, "e2e")
PLUGINS = os.path.join(E2E, "plugins")
GALLERY = os.path.join(E2E, "gallery")
SOURCES = os.path.join(E2E, "sources")
RELEASES = os.path.join(E2E, "releases")
SITE = os.path.join(E2E, "site")
KEYS = os.path.join(E2E, "keys")
GALLERY_PY = os.path.join(ROOT, "build", "gallery.py")
PREVIEW_CONFIG = os.path.join(E2E, "mokaterm-ref.json")
RELEASE_CONFIG = os.path.join(E2E, "mokaterm-release.json")
FAKE_GALLERY_COMMIT = "1" * 40

GIT = ["git", "-c", "user.name=Acme", "-c", "user.email=dev@acme.invalid", "-c", "commit.gpgsign=false",
       "-c", "tag.gpgsign=false", "-c", "core.autocrlf=false", "-c", "init.defaultBranch=main"]


def main():
    sample = os.path.join(os.environ.get("MOKATERM_SOURCE") or os.path.join(os.path.dirname(ROOT), "Mokaterm"),
                          "samples", "Mokaterm.Plugins.Sample")
    if not os.path.isdir(sample):
        sys.exit(f"No sample plugin at {sample}; set MOKATERM_SOURCE to a Mokaterm checkout.")
    remove(E2E)
    os.makedirs(GALLERY)
    with open(os.path.join(ROOT, "mokaterm.json"), encoding="utf-8-sig") as file:
        version = json.load(file)["version"]
    write(PREVIEW_CONFIG, json.dumps({"version": version, "ref": "main"}) + "\n")
    write(RELEASE_CONFIG, json.dumps({"version": version}) + "\n")

    step("Making the fixtures")
    make_ours(sample)
    commit_a, commit_b = make_community(sample)
    write(os.path.join(GALLERY, "withdrawn.json"), "[]\n")
    entry(commit_a, None)

    step("Validating the listings")
    gallery("validate")

    step("Checking every listing, as a pull request that changes the build would")
    gallery("check", "--all", "--clone-from", "acme.sample", os.path.join(SOURCES, "acme-sample"))

    step("Planning while mokaterm.json names a ref: everything is built, nothing is released")
    plan, publish = plan_ids(PREVIEW_CONFIG)
    expect(plan == ["acme.sample", "moka.sample"] and not publish, f"a ref builds every listing and publishes nothing ({plan}, {publish})")
    out = gallery("release", "--built", os.path.join(E2E, "nothing"), "--commit", FAKE_GALLERY_COMMIT,
                  "--releases", f"github:{DEFAULT_REPOSITORY}", config=PREVIEW_CONFIG, capture=True, check=False)
    expect("nothing is released to GitHub" in out, "a ref refuses to release to GitHub before anything is sent")

    step("Building what publish would build once mokaterm.json names a published version")
    plan, publish = plan_ids()
    expect(plan == ["acme.sample", "moka.sample"] and publish, f"the first plan builds both fixtures, not {plan}")
    built = os.path.join(E2E, "built-1")
    for plugin_id in plan:
        build(plugin_id, built)

    step("Releasing them")
    gallery("release", "--built", built, "--commit", FAKE_GALLERY_COMMIT)
    expect(plan_ids()[0] == [], "nothing is left to build once both are released")
    out = gallery("release", "--built", built, "--commit", FAKE_GALLERY_COMMIT, capture=True)
    expect(out.count("is already released with these bytes") == 2, "releasing the same bytes again changes nothing")

    step("Moving the community entry to its next commit, which carries 1.1.0 and a tag")
    entry(commit_b, "v1.1.0")
    expect(plan_ids()[0] == ["acme.sample"], "only the community plugin has something new")
    built = os.path.join(E2E, "built-2")
    build("acme.sample", built)
    gallery("release", "--built", built, "--commit", FAKE_GALLERY_COMMIT)

    step("Refusing what must be refused")
    entry(commit_b, "v1.0.0")
    failed = gallery("build", "acme.sample", "--no-tests", "--out", os.path.join(E2E, "refused"),
                     "--clone-from", os.path.join(SOURCES, "acme-sample"), capture=True, check=False)
    expect("points at" in failed, "a tag that names another commit is refused")
    entry(commit_b, "v1.1.0")
    bad = os.path.join(E2E, "bad.mokaplugin")
    shutil.copyfile(os.path.join(RELEASES, "moka.sample-v1.0.0", package_name("moka.sample", "1.0.0")), bad)
    failed = gallery("check-package", bad, "--id", "acme.sample", capture=True, check=False)
    expect("listed as acme.sample" in failed, "a package under another id is refused")

    step("Withdrawing acme.sample 1.0.0 and building the index")
    write(os.path.join(GALLERY, "withdrawn.json"), json.dumps(
        [{"id": "acme.sample", "version": "1.0.0", "reason": "Shows the wrong subnet mask for /31 prefixes. Use 1.1.0."}], indent=2) + "\n")
    gallery("validate")
    gallery("index", "--out", SITE)
    check_index()

    step("Signing it with a throwaway key and verifying it")
    gallery("keygen", "--out", KEYS)
    private, public = os.path.join(KEYS, "gallery-test.pem"), os.path.join(KEYS, "gallery-test.pub.pem")
    index = os.path.join(SITE, "index.json")
    gallery("sign", "--index", index, "--key", private, "--public", public)
    gallery("verify", "--index", index, "--public", public)
    with open(index + ".sig", encoding="ascii") as file:
        signature = file.read()
    expect(re.fullmatch(r"[A-Za-z0-9+/]{86}==\n", signature) is not None, "the signature is one line of 88 base64 characters")

    out = gallery("verify", "--index", index, "--public", PUBLIC_KEY, capture=True, check=False)
    expect("does not verify" in out, "the real gallery key does not accept a test signature")
    tampered = os.path.join(SITE, "tampered.json")
    with open(index, "rb") as source, open(tampered, "wb") as target:
        target.write(source.read().replace(b'"Acme"', b'"Evil"', 1))
    shutil.copyfile(index + ".sig", tampered + ".sig")
    out = gallery("verify", "--index", tampered, "--public", public, capture=True, check=False)
    expect("does not verify" in out, "a changed index fails the signature")

    step("End to end: every step passed")
    print(f"index: {os.path.relpath(index, ROOT)}\nsignature: {os.path.relpath(index + '.sig', ROOT)}")


def check_index():
    with open(os.path.join(SITE, "index.json"), "rb") as file:
        raw = file.read()
    expect(not raw.startswith(b"\xef\xbb\xbf"), "index.json has no byte order mark")
    index = json.loads(raw)
    with open(os.path.join(WORK, "toolchain", "toolchain.json"), encoding="utf-8") as file:
        sdk = json.load(file)["sdk"]
    expect(index["format"] == 1 and re.fullmatch(r"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ", index["generated"]), "format and generated")
    ids = [plugin["id"] for plugin in index["plugins"]]
    expect(ids == ["acme.sample", "moka.sample"], f"plugins sorted by id: {ids}")
    acme, ours = index["plugins"]
    expect([version["version"] for version in acme["versions"]] == ["1.1.0", "1.0.0"], "versions newest first")
    expect(acme["versions"][0]["withdrawn"] is None and "Use 1.1.0" in acme["versions"][1]["withdrawn"], "1.0.0 is withdrawn")
    expect(acme["verified"] is False and ours["verified"] is True, "ours verified, community not")
    expect(acme["source"] == "https://github.com/acme/mokaterm-sample", "community source")
    expect(ours["source"] == "https://github.com/jacobwi/mokaterm-plugins", "our source")
    expect(acme["publisher"] == "Acme" and acme["hosts"] == ["desktop", "web"] and acme["hasCode"] is True, "plugin.json fields")
    expect(acme["categories"] == ["utilities", "server-tools"], "categories come from the listing")
    for plugin in index["plugins"]:
        for version in plugin["versions"]:
            tag = f"{plugin['id']}-v{version['version']}"
            name = package_name(plugin["id"], version["version"])
            expect(version["url"] == f"https://github.com/jacobwi/mokaterm-plugins/releases/download/{tag}/{name}", f"url of {tag}")
            path = os.path.join(RELEASES, tag, name)
            with open(path, "rb") as file:
                data = file.read()
            expect(version["sha256"] == hashlib.sha256(data).hexdigest() and version["size"] == len(data), f"sha256 and size of {tag}")
            expect(version["sdk"] == sdk, f"sdk of {tag}")
            expect(re.fullmatch(r"[0-9a-f]{40}", version["commit"]) is not None, f"commit of {tag}")
    print(f"index.json: {len(raw)} bytes, {len(ids)} plugins, every field as the contract says.")


# ---- fixtures

def make_ours(sample):
    target = os.path.join(PLUGINS, "moka.sample")
    copy_sample(sample, os.path.join(target, "src"))
    write(os.path.join(target, "listing.json"), '{ "categories": ["utilities", "themes"], "license": "MIT" }\n')
    write(os.path.join(target, "src", "Mokaterm.Plugins.Sample.csproj"), """<Project Sdk="Microsoft.NET.Sdk.Razor">

	<PropertyGroup>
		<RootNamespace>Mokaterm.Plugins.Sample</RootNamespace>
		<Version>1.0.0</Version>
		<MokatermPluginId>moka.sample</MokatermPluginId>
		<MokatermPluginName>Sample plugin</MokatermPluginName>
		<MokatermPluginPublisher>Moka</MokatermPluginPublisher>
		<MokatermPluginDescription>Shows that a plugin's view, script, styles and themes load.</MokatermPluginDescription>
		<MokatermPluginHosts>desktop;web</MokatermPluginHosts>
	</PropertyGroup>

	<ItemGroup>
		<PackageReference Include="Mokaterm.UI.Common" />
		<PackageReference Include="Mokaterm.Plugin.Sdk" PrivateAssets="all" />
		<MokatermPluginTheme Include="themes/*.json" />
	</ItemGroup>

</Project>
""")


def make_community(sample):
    repo = os.path.join(SOURCES, "acme-sample")
    project = os.path.join(repo, "Acme.Sample")
    copy_sample(sample, project)
    replace(os.path.join(project, "SampleModule.cs"), 'new("sample-plugin",', 'new("acme-sample",')
    replace(os.path.join(project, "SampleSettings.cs"), "plugin.moka.sample", "plugin.acme.sample")
    replace(os.path.join(project, "SamplePluginTool.razor.cs"), "_content/Mokaterm.Plugins.Sample/", "_content/Acme.Sample/")
    os.replace(os.path.join(project, "themes", "moka-sample.json"), os.path.join(project, "themes", "acme-night.json"))
    write(os.path.join(repo, ".gitignore"), "bin/\nobj/\n")
    write(os.path.join(repo, "LICENSE"), "MIT License\n\nCopyright (c) 2026 Acme\n")
    write(os.path.join(repo, "Directory.Build.props"), """<Project>
	<PropertyGroup>
		<TargetFramework>net10.0</TargetFramework>
		<Nullable>enable</Nullable>
		<ImplicitUsings>enable</ImplicitUsings>
	</PropertyGroup>
</Project>
""")
    # A feed that does not exist: the gallery restores through its own nuget.config, so this is never asked.
    write(os.path.join(repo, "nuget.config"), """<?xml version="1.0" encoding="utf-8"?>
<configuration>
	<packageSources>
		<clear />
		<add key="acme-private" value="https://nuget.acme.invalid/v3/index.json" />
	</packageSources>
</configuration>
""")
    commit_a = commit_version(repo, project, "1.0.0", init=True)
    commit_b = commit_version(repo, project, "1.1.0")
    run([*GIT, "-C", repo, "tag", "-a", "v1.1.0", "-m", "1.1.0", commit_b])
    run([*GIT, "-C", repo, "tag", "v1.0.0", commit_a])
    return commit_a, commit_b


def commit_version(repo, project, version, init=False):
    # Pinned to a Mokaterm release that does not exist: the gallery replaces the version with its own.
    write(os.path.join(project, "Acme.Sample.csproj"), f"""<Project Sdk="Microsoft.NET.Sdk.Razor">

	<PropertyGroup>
		<AssemblyName>Acme.Sample</AssemblyName>
		<RootNamespace>Mokaterm.Plugins.Sample</RootNamespace>
		<Version>{version}</Version>
		<MokatermPluginId>acme.sample</MokatermPluginId>
		<MokatermPluginName>Acme sample</MokatermPluginName>
		<MokatermPluginPublisher>Acme</MokatermPluginPublisher>
		<MokatermPluginDescription>The Mokaterm sample plugin, built as a community plugin.</MokatermPluginDescription>
		<MokatermPluginHosts>desktop;web</MokatermPluginHosts>
		<MokatermPluginPackOnBuild>true</MokatermPluginPackOnBuild>
	</PropertyGroup>

	<ItemGroup>
		<PackageReference Include="Mokaterm.UI.Common" Version="0.0.1-missing" />
		<PackageReference Include="Mokaterm.Plugin.Sdk" Version="0.0.1-missing" PrivateAssets="all" />
		<MokatermPluginTheme Include="themes/*.json" />
	</ItemGroup>

</Project>
""")
    if init:
        run([*GIT, "init", "-q", repo])
    run([*GIT, "-C", repo, "add", "-A"])
    env = {"GIT_AUTHOR_DATE": "2026-10-01T12:00:00Z", "GIT_COMMITTER_DATE": "2026-10-01T12:00:00Z"}
    run([*GIT, "-C", repo, "commit", "-q", "-m", f"Acme sample {version}"], env=env)
    return run([*GIT, "-C", repo, "rev-parse", "HEAD"], capture=True).strip()


def entry(commit, tag):
    data = {
        "id": "acme.sample",
        "source": "https://github.com/acme/mokaterm-sample",
        "commit": commit,
        "project": "Acme.Sample/Acme.Sample.csproj",
        "license": "MIT",
        "categories": ["utilities", "server-tools"],
    }
    if tag:
        data["tag"] = tag
    write(os.path.join(GALLERY, "acme.sample.json"), json.dumps(data, indent=2) + "\n")


def copy_sample(sample, target):
    shutil.copytree(sample, target, ignore=shutil.ignore_patterns("bin", "obj", "*.csproj"))


# ---- running

def gallery(*arguments, capture=False, check=True, config=RELEASE_CONFIG):
    command = [sys.executable, "-B", GALLERY_PY, "--repository", DEFAULT_REPOSITORY, "--plugins-dir", PLUGINS,
               "--gallery-dir", GALLERY, *arguments]
    if arguments[0] in ("validate", "build", "check-package", "check", "plan", "release", "index") and "--releases" not in arguments:
        command += ["--releases", f"dir:{RELEASES}"]
    return run(command, capture=capture, check=check, env={"MOKATERM_CONFIG": config})


def build(plugin_id, built):
    arguments = ["build", plugin_id, "--out", os.path.join(built, f"package-{plugin_id}")]
    if plugin_id == "acme.sample":
        arguments += ["--clone-from", os.path.join(SOURCES, "acme-sample")]
    gallery(*arguments)


def plan_ids(config=RELEASE_CONFIG):
    """The ids publish would build, and whether it would release them."""
    out = gallery("plan", capture=True, config=config)
    matrix = re.search(r"^matrix=(.*)$", out, re.MULTILINE).group(1)
    publish = re.search(r"^publish=(true|false)$", out, re.MULTILINE).group(1)
    return [item["id"] for item in json.loads(matrix)["include"]], publish == "true"


def run(command, capture=False, check=True, env=None):
    print("> " + " ".join(_short(part) for part in command), flush=True)
    result = subprocess.run(command, cwd=ROOT, env={**os.environ, **(env or {})}, text=True, encoding="utf-8", errors="replace",
                            stdout=subprocess.PIPE if capture else None, stderr=subprocess.STDOUT if capture else None)
    if capture:
        print(result.stdout, end="", flush=True)
    if check and result.returncode != 0:
        sys.exit(f"Failed with exit code {result.returncode}: {' '.join(command)}")
    if not check and result.returncode == 0:
        sys.exit(f"Expected this to fail: {' '.join(command)}")
    return result.stdout if capture else None


def _short(part):
    if not os.path.isabs(part):
        return part
    try:
        return os.path.relpath(part, ROOT)
    except ValueError:
        return part


def expect(condition, what):
    if not condition:
        sys.exit(f"E2E FAILED: {what}")
    print(f"ok: {what}", flush=True)


def step(title):
    print(f"\n==== {title}", flush=True)


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="") as file:
        file.write(text.replace("\r\n", "\n").replace("\n", "\r\n"))


def replace(path, old, new):
    with open(path, encoding="utf-8") as file:
        text = file.read()
    if old not in text:
        sys.exit(f"{path} no longer holds {old!r}; the sample changed and this fixture needs to follow it.")
    with open(path, "w", encoding="utf-8", newline="") as file:
        file.write(text.replace(old, new))


def remove(path):
    def make_writable(function, target, _):
        os.chmod(target, 0o700)
        function(target)

    if os.path.isdir(path):
        if sys.version_info >= (3, 12):
            shutil.rmtree(path, onexc=make_writable)
        else:
            shutil.rmtree(path, onerror=make_writable)


if __name__ == "__main__":
    main()
