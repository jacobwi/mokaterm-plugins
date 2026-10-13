"""The gallery's steps, one command each. CI runs them in .github/workflows; docs/building.md shows them run by hand.

  mokaterm-ref        prints the Mokaterm ref to check out: mokaterm.json's ref, else v<version>
  validate            checks every listing, gallery/withdrawn.json and what they say about published releases
  toolchain           builds the load check against a Mokaterm checkout (after build/mokaterm_feed.py)
  build ID            builds one listed plugin, runs our tests, checks the package and loads it
  check-package FILE  checks and loads any .mokaplugin, as if it were listed under --id
  check               what a pull request needs: validate, then build and check every listing it touches
  plan                lists the listings whose current version is not released yet (the publish matrix), or every
                      listing while mokaterm.json builds against a checkout (a ref), when nothing is published
  release             re-checks built packages, writes their metadata and makes one release per version
  index               builds index.json from every release's metadata and gallery/withdrawn.json
  sign / verify       signs index.json with Ed25519 and checks the signature
  keygen              makes a throwaway signing key for local runs

--releases takes github:<owner/name> (GitHub Releases; GH_TOKEN for writes) or dir:<folder> (a local stand-in, the
default: .work/releases).
"""

import argparse
import glob
import json
import os
import shutil
import sys

sys.dont_write_bytecode = True

import gallery_build  # noqa: E402
import gallery_index  # noqa: E402
import gallery_listings  # noqa: E402
import gallery_package  # noqa: E402
import gallery_releases  # noqa: E402
import gallery_signing  # noqa: E402
from gallery_common import (  # noqa: E402
    DEFAULT_REPOSITORY, GALLERY, PLUGINS, PUBLIC_KEY, WORK, GalleryError, error, group, info, is_commit, metadata_name,
    read_json, read_mokaterm_config, relative, repository_url, run, tag_for, utc_now, warning, write_json,
)

# Changes here can change how every plugin builds, so a pull request that makes one rebuilds them all.
INFRASTRUCTURE = ("build/", ".github/", "gallery/schema.json", "mokaterm.json", "nuget.config", "global.json",
                  "Directory.Build.props", "Directory.Packages.props", ".editorconfig")


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(errors="replace")
        sys.stderr.reconfigure(errors="replace")
    parser = _parser()
    args = parser.parse_args()
    try:
        return args.handler(args) or 0
    except GalleryError as problem:
        error(str(problem))
        return 1


def _parser():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--repository", default=os.environ.get("GITHUB_REPOSITORY") or DEFAULT_REPOSITORY,
                        help="the gallery repository, owner/name (default: GITHUB_REPOSITORY, else %(default)s)")
    parser.add_argument("--plugins-dir", default=PLUGINS, help=argparse.SUPPRESS)
    parser.add_argument("--gallery-dir", default=GALLERY, help=argparse.SUPPRESS)
    commands = parser.add_subparsers(required=True, metavar="command")

    def command(name, handler, help_text, releases=False):
        sub = commands.add_parser(name, help=help_text)
        sub.set_defaults(handler=handler)
        if releases:
            sub.add_argument("--releases", default=f"dir:{os.path.join(WORK, 'releases')}",
                             help="github:<owner/name> or dir:<folder> (default: .work/releases)")
        return sub

    command("mokaterm-ref", _mokaterm_ref, "print the Mokaterm ref and version to check out")
    command("validate", _validate, "check listings and withdrawn.json", releases=True)

    sub = command("toolchain", _toolchain, "build the load check against a Mokaterm checkout")
    sub.add_argument("--source", help="the Mokaterm checkout (default: MOKATERM_SOURCE, else ../Mokaterm)")

    sub = command("build", _build, "build, test, check and load one listed plugin", releases=True)
    sub.add_argument("id")
    sub.add_argument("--out", help="where the package goes (default: .work/out/<id>)")
    sub.add_argument("--no-tests", action="store_true", help="skip our plugins' test projects")
    sub.add_argument("--clone-from", help=argparse.SUPPRESS)

    sub = command("check-package", _check_package, "check and load a .mokaplugin", releases=True)
    sub.add_argument("package")
    sub.add_argument("--id", required=True, help="the id it is listed under")

    sub = command("check", _check, "check a pull request: validate, then build every listing it touches", releases=True)
    which = sub.add_mutually_exclusive_group(required=True)
    which.add_argument("--base", help="the ref the pull request merges into, such as origin/main")
    which.add_argument("--all", action="store_true", help="build every listing")
    sub.add_argument("--clone-from", nargs=2, action="append", metavar=("ID", "FOLDER"), help=argparse.SUPPRESS)

    sub = command("plan", _plan, "list what publish has to build", releases=True)
    sub.add_argument("--github-output", help="append matrix= and count= to this file (GITHUB_OUTPUT)")

    sub = command("release", _release, "release built packages", releases=True)
    sub.add_argument("--built", required=True, help="a folder holding one folder per built plugin (package-<id> or <id>)")
    sub.add_argument("--commit", required=True, help="this repository's commit the packages were built from")

    sub = command("index", _index, "build index.json from the releases", releases=True)
    sub.add_argument("--out", required=True, help="the folder index.json goes into")

    sub = command("sign", _sign, "sign index.json (the key from --key, else GALLERY_SIGNING_KEY)")
    sub.add_argument("--index", required=True)
    sub.add_argument("--key", help="a PEM Ed25519 private key file; for local runs only")
    sub.add_argument("--public", default=PUBLIC_KEY, help="the public key to verify with (default: signing/gallery-2026.pub.pem)")

    sub = command("verify", _verify, "verify index.json.sig")
    sub.add_argument("--index", required=True)
    sub.add_argument("--signature", help="default: <index>.sig")
    sub.add_argument("--public", default=PUBLIC_KEY)

    sub = command("keygen", _keygen, "make a throwaway signing key pair for local runs")
    sub.add_argument("--out", default=os.path.join(WORK, "keys"))
    return parser


# ---- commands

def _mokaterm_ref(_):
    version, ref = read_mokaterm_config()
    print(f"ref={ref or 'v' + version}")
    print(f"version={version}")


def _validate(args):
    listings = _listings(args, strict=False)
    problems = list(listings.problems)
    published = _published(args)
    problems += gallery_listings.check_against_published(listings, published)
    _finish(problems, f"{len(listings.items)} listings and {len(listings.withdrawn)} withdrawn versions are in order.")


def _toolchain(args):
    gallery_build.build_toolchain(args.source)


def _build(args):
    listings = _listings(args, strict=False)
    listing = _listing(listings, args.id)
    published = _published(args)
    out = args.out or os.path.join(WORK, "out", listing.id)
    report, _ = _build_and_check(listing, published, out, not args.no_tests, args.clone_from)
    _summary([report])


def _check_package(args):
    published = _published(args)
    listing = _listings(args, strict=False).items.get(args.id)
    gallery_build.check_built(os.path.abspath(args.package), args.id, gallery_build.taken_module_ids(published, args.id),
                              os.path.join(WORK, "out", args.id), verified=listing is not None and listing.verified)
    info(f"{args.package} can be listed as {args.id}.")


def _check(args):
    listings = _listings(args, strict=False)
    problems = list(listings.problems)
    published = _published(args)
    problems += gallery_listings.check_against_published(listings, published)
    clone_from = dict(args.clone_from or [])

    if args.all:
        touched, everything, sources = set(listings.items), True, set()
    else:
        files = run(["git", "diff", "--name-only", "--no-renames", f"{args.base}...HEAD"], capture=True, quiet=True).splitlines()
        touched, everything, sources = changed_listings(files, listings)
        if everything:
            info("The build itself changed, so every listing is built.")
    released_commits = {(meta["id"], meta["commit"]) for meta in published}

    built = []
    for plugin_id in sorted(touched):
        listing = listings.items.get(plugin_id)
        if listing is None:
            info(f"{plugin_id} is no longer listed; its releases stay, and gallery/withdrawn.json is how to warn about one.")
            continue
        if listing.kind == gallery_listings.COMMUNITY and (plugin_id, listing.commit) in released_commits and not everything:
            info(f"{plugin_id} at {listing.commit} is already released; only its entry changed.")
            continue
        try:
            report, _ = _build_and_check(listing, published, os.path.join(WORK, "out", plugin_id), True,
                                         clone_from.get(plugin_id), src_changed=plugin_id in sources)
            built.append(report)
        except GalleryError as problem:
            problems.append(f"{plugin_id}: {problem}")
    _summary(built)
    _finish(problems, f"Checked {len(built)} plugin(s); everything is in order.")


def changed_listings(files, listings):
    """(ids to build, whether the build itself changed, ids whose plugin source changed) for a list of changed paths."""
    touched = set()
    sources = set()
    everything = False
    plugins = relative(listings.plugins_dir) + "/"
    gallery = relative(listings.gallery_dir) + "/"
    for path in files:
        path = path.strip().replace("\\", "/")
        if not path:
            continue
        if path.startswith(INFRASTRUCTURE) or path in INFRASTRUCTURE:
            everything = True
        elif path.startswith(plugins):
            parts = path[len(plugins):].split("/")
            if len(parts) > 1:
                touched.add(parts[0])
                if parts[1] == "src":
                    sources.add(parts[0])
        elif path.startswith(gallery):
            name = path[len(gallery):]
            if "/" not in name and name.endswith(".json") and name not in (gallery_listings.SCHEMA_FILE, gallery_listings.WITHDRAWN_FILE):
                touched.add(name[:-len(".json")])
    if everything:
        touched |= set(listings.items)
    return touched, everything, sources


def _plan(args):
    listings = _listings(args, strict=True)
    published = _published(args)
    problems = gallery_listings.check_against_published(listings, published, moves_allowed=True)
    if problems:
        raise GalleryError("\n".join(problems))
    mokaterm_version, ref = read_mokaterm_config()
    publishing = ref is None
    released = {(meta["id"], meta["version"]) for meta in published}
    released_commits = {(meta["id"], meta["commit"]) for meta in published}
    include = []
    for listing in sorted(listings.items.values(), key=lambda item: item.id):
        if not publishing:
            info(f"{listing.id} is built and checked; nothing is published while mokaterm.json names a ref.")
        elif listing.kind == gallery_listings.OURS:
            version = gallery_build.plugin_version(listing)
            if (listing.id, version) in released:
                info(f"{listing.id} {version} is released.")
                continue
            info(f"{listing.id} {version} is new.")
        else:
            if (listing.id, listing.commit) in released_commits:
                info(f"{listing.id} at {listing.commit[:12]} is released.")
                continue
            info(f"{listing.id} at {listing.commit[:12]} is new.")
        include.append({"id": listing.id})
    matrix = {"include": include}
    lines = [f"matrix={json.dumps(matrix, separators=(',', ':'))}", f"count={len(include)}",
             f"publish={'true' if publishing else 'false'}"]
    if publishing:
        _write_summary(f"Building against Mokaterm {mokaterm_version} from nuget.org: {len(include)} new version(s) to release, "
                       "then the index is rebuilt, signed and deployed.")
    else:
        notice = (f"mokaterm.json builds against Mokaterm {mokaterm_version} packed from a checkout at {ref}, not a published "
                  f"version, so this run builds, tests, checks and loads all {len(include)} listed plugin(s), creates no "
                  "release and deploys no index. Publishing starts once mokaterm.json names a version on nuget.org "
                  "(no ref).")
        warning(notice)
        _write_summary(f"**Nothing is published by this run.** {notice}")
    if args.github_output:
        with open(args.github_output, "a", encoding="utf-8", newline="\n") as file:
            file.write("\n".join(lines) + "\n")
    print("\n".join(lines))


def _release(args):
    toolchain = read_json(gallery_build.TOOLCHAIN_FILE)
    listings = _listings(args, strict=True)
    backend = gallery_releases.open_backend(args.releases, args.repository)
    _, ref = read_mokaterm_config()
    # Packages built from an unreleased Mokaterm never reach the real Releases page: their version numbers would be
    # spent on builds for a Mokaterm nobody runs. A local folder stands in for testing.
    if ref is not None and isinstance(backend, gallery_releases.GitHubReleases):
        raise GalleryError(
            f"mokaterm.json builds against a Mokaterm checkout (ref {ref}), so nothing is released to GitHub. "
            "Name a version published on nuget.org in mokaterm.json first.")
    releases = backend.list()
    published = gallery_releases.read_published(backend)
    if not is_commit(args.commit):
        raise GalleryError(f"--commit {args.commit!r} is not a full commit.")

    failures = []
    folders = sorted(path for path in glob.glob(os.path.join(args.built, "*")) if os.path.isdir(path))
    if not folders:
        info("Nothing was built, so nothing is released.")
    for folder in folders:
        name = os.path.basename(folder)
        plugin_id = name[len("package-"):] if name.startswith("package-") else name
        try:
            with group(f"Releasing {plugin_id}"):
                _release_one(listings.get(plugin_id), folder, toolchain, backend, releases, published, args.commit, args.repository)
        except GalleryError as problem:
            failures.append(f"{plugin_id}: {problem}")
    _finish(failures, f"Released what was built ({len(folders)} plugin(s)).")


def _release_one(listing, folder, toolchain, backend, releases, published, commit, repository):
    packages = glob.glob(os.path.join(folder, "*.mokaplugin"))
    if len(packages) != 1:
        raise GalleryError(f"{relative(folder)} holds {len(packages)} packages, not one.")
    package = packages[0]

    # The packages come from jobs that ran plugin code, so everything that matters is worked out again here, from the
    # bytes and from this repository, and nothing the build job wrote is taken on trust but the module id.
    report = gallery_package.check(package, listing.id, toolchain["sdk"], toolchain["runtimeAssemblies"], listing.verified)
    if not report.ok:
        raise GalleryError("The package fails the checks:\n  " + "\n  ".join(report.problems))
    clash = gallery_build.assembly_clash(report.manifest, published)
    if clash:
        raise GalleryError(clash)
    version = report.manifest["version"]
    tag = tag_for(listing.id, version)

    same = [meta for meta in published if meta["id"] == listing.id and meta["version"] == version]
    if same:
        if same[0]["sha256"] == report.sha256:
            info(f"{tag} is already released with these bytes.")
            return
        raise GalleryError(f"{listing.id} {version} is already released from {same[0]['commit']}. A change needs a new version.")
    if listing.kind == gallery_listings.COMMUNITY:
        earlier = [meta for meta in published if meta["id"] == listing.id and meta["commit"] == listing.commit]
        if earlier:
            raise GalleryError(f"{listing.commit} is already released as {listing.id} {earlier[0]['version']}.")

    for release in releases:
        if release.tag == tag:
            if not release.draft:
                raise GalleryError(f"There is already a release {tag} that is not a complete plugin release; delete it by hand.")
            warning(f"Deleting the draft {tag} a run that did not finish left behind.")
            backend.delete(release)

    loadcheck_path = os.path.join(folder, "loadcheck.json")
    loadcheck = read_json(loadcheck_path) if os.path.isfile(loadcheck_path) else {}
    source_commit = commit if listing.verified else listing.commit
    metadata = gallery_releases.build_metadata(
        listing, report, loadcheck, toolchain, source_commit, repository_url(repository), utc_now(), commit)
    metadata_path = os.path.join(WORK, "release", metadata_name(listing.id, version))
    write_json(metadata_path, metadata)
    package_copy = os.path.join(WORK, "release", os.path.basename(package))
    if os.path.abspath(package) != os.path.abspath(package_copy):
        shutil.copyfile(package, package_copy)

    title = f"{metadata['name']} {version}"
    notes = (
        f"{metadata['name']} {version} for Mokaterm {metadata['sdk']}, by {metadata['publisher']}.\n\n"
        f"Built from {metadata['source']} at {source_commit} against Mokaterm {toolchain['mokatermVersion']}.\n\n"
        f"SHA-256 of {os.path.basename(package)}: `{report.sha256}`\n\n"
        "Install it from the Plugins page in Mokaterm, which checks this file against the signed gallery index.")
    backend.create(tag, title, notes, commit, [package_copy, metadata_path])
    info(f"Released {tag} ({report.size} bytes, sha256 {report.sha256}).")


def _index(args):
    listings = _listings(args, strict=True)
    published = _published(args)
    problems = gallery_listings.check_against_published(listings, published, moves_allowed=True)
    if problems:
        raise GalleryError("\n".join(problems))
    index = gallery_index.build(published, listings.items, listings.withdrawn, utc_now())
    data = gallery_index.serialize(index)
    os.makedirs(args.out, exist_ok=True)
    path = os.path.join(args.out, "index.json")
    with open(path, "wb") as file:
        file.write(data)
    versions = sum(len(plugin["versions"]) for plugin in index["plugins"])
    info(f"Wrote {relative(path)}: {len(index['plugins'])} plugin(s), {versions} version(s), {len(data)} bytes.")


def _sign(args):
    path = gallery_signing.sign(os.path.abspath(args.index), os.path.abspath(args.public),
                                os.path.abspath(args.key) if args.key else None)
    info(f"Wrote {relative(path)}.")


def _verify(args):
    index = os.path.abspath(args.index)
    gallery_signing.verify(index, os.path.abspath(args.signature or index + ".sig"), os.path.abspath(args.public))


def _keygen(args):
    private, public = gallery_signing.generate_key(os.path.abspath(args.out))
    info(f"Wrote {relative(private)} and {relative(public)}. They sign local test indexes only; the app does not trust them.")


# ---- helpers

def _listings(args, strict):
    listings = gallery_listings.load(args.repository, os.path.abspath(args.plugins_dir), os.path.abspath(args.gallery_dir))
    if strict and listings.problems:
        raise GalleryError("The listings are not in order:\n  " + "\n  ".join(listings.problems))
    return listings


def _listing(listings, plugin_id):
    if plugin_id not in listings.items:
        own = [problem for problem in listings.problems if plugin_id in problem]
        if own:
            raise GalleryError("\n".join(own))
    return listings.get(plugin_id)


def _published(args):
    backend = gallery_releases.open_backend(args.releases, args.repository)
    published = gallery_releases.read_published(backend)
    info(f"{len(published)} version(s) published in {backend.describe()}.")
    return published


def _build_and_check(listing, published, out, run_tests, clone_from=None, src_changed=True):
    package = gallery_build.build_listing(listing, out, run_tests=run_tests, clone_from=clone_from)
    report, result = gallery_build.check_built(package, listing.id, gallery_build.taken_module_ids(published, listing.id), out,
                                               verified=listing.verified)
    clash = gallery_build.assembly_clash(report.manifest, published)
    if clash:
        raise GalleryError(clash)
    version = report.manifest["version"]
    for meta in published:
        if meta["id"] != listing.id or meta["version"] != version:
            continue
        if listing.kind == gallery_listings.COMMUNITY and meta["commit"] != listing.commit:
            raise GalleryError(f"{listing.id} {version} is already released from {meta['commit']}. The new commit needs a new version.")
        if listing.kind == gallery_listings.OURS and src_changed:
            warning(f"{listing.id} {version} is already released, so this change is not published until its version goes up.")
    build = read_json(os.path.join(out, "build.json"))
    build.update({"version": version, "sha256": report.sha256, "size": report.size, "moduleId": result.get("moduleId")})
    write_json(os.path.join(out, "build.json"), build)
    info(f"{listing.id} {version} is ready to list: {relative(package)}")
    return report, result


def _summary(reports):
    if not reports:
        return
    lines = ["| Plugin | Version | Hosts | Code | Size | SHA-256 |", "| --- | --- | --- | --- | --- | --- |"]
    for report in reports:
        manifest = report.manifest
        lines.append(f"| {manifest['id']} | {manifest['version']} | {', '.join(manifest['hosts']) or 'all'} | "
                     f"{'yes' if report.has_code else 'no'} | {report.size} | `{report.sha256}` |")
    _write_summary("\n".join(lines))


def _write_summary(text):
    """Appends Markdown to the job summary GitHub shows on the run's page; does nothing outside Actions."""
    path = os.environ.get("GITHUB_STEP_SUMMARY")
    if path:
        with open(path, "a", encoding="utf-8") as file:
            file.write(text + "\n\n")


def _finish(problems, success):
    if problems:
        for problem in problems:
            error(problem)
        raise GalleryError(f"{len(problems)} problem(s).")
    info(success)


if __name__ == "__main__":
    sys.exit(main())
