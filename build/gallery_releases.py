"""Where published versions live: GitHub Releases of this repository, or a folder that stands in for them locally.

Every listed version is one release tagged <id>-v<version> holding the .mokaplugin and <id>-<version>.json, the metadata
the index is built from. The index never needs anything else, so the repository needs no commits from a bot.
"""

import json
import os
import re
import shutil
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass, field
from typing import Optional

from gallery_common import (
    CATEGORIES, GalleryError, info, is_commit, is_valid_id, is_valid_version, metadata_name, package_name, parse_json,
    relative, run,
)

METADATA_FORMAT = 1
_TAG = re.compile(r"(?P<id>[a-z0-9]+(?:[.-][a-z0-9]+)*)-v(?P<version>[0-9].*)")
_SHA256 = re.compile(r"[0-9a-f]{64}")
_TIME = re.compile(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z")
_SOURCE = re.compile(r"https://github\.com/[A-Za-z0-9](?:-?[A-Za-z0-9])*/[A-Za-z0-9._-]+")
_MODULE_ID = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]{0,99}")
_MAX_METADATA_BYTES = 256 * 1024


@dataclass
class Asset:
    name: str
    size: int
    url: str
    id: Optional[int] = None
    path: Optional[str] = None


@dataclass
class Release:
    tag: str
    draft: bool
    id: Optional[int] = None
    assets: dict = field(default_factory=dict)


def open_backend(spec, repository):
    """github:<owner/name> for GitHub Releases, dir:<folder> for a local stand-in."""
    kind, _, value = spec.partition(":")
    if kind == "github" and value:
        return GitHubReleases(value)
    if kind == "dir" and value:
        return DirectoryReleases(value, repository)
    raise GalleryError(f"--releases takes github:<owner/name> or dir:<folder>, not {spec!r}.")


class DirectoryReleases:
    """Releases as folders: <folder>/<tag>/ with the assets and a release.json. URLs point where GitHub would put them."""

    def __init__(self, folder, repository):
        self.folder = os.path.abspath(folder)
        self.repository = repository

    def describe(self):
        return relative(self.folder)

    def list(self):
        releases = []
        if not os.path.isdir(self.folder):
            return releases
        for tag in sorted(os.listdir(self.folder)):
            path = os.path.join(self.folder, tag)
            if not os.path.isfile(os.path.join(path, "release.json")):
                continue
            assets = {}
            for name in sorted(os.listdir(path)):
                if name == "release.json":
                    continue
                file = os.path.join(path, name)
                url = f"https://github.com/{self.repository}/releases/download/{urllib.parse.quote(tag)}/{urllib.parse.quote(name)}"
                assets[name] = Asset(name=name, size=os.path.getsize(file), url=url, path=file)
            releases.append(Release(tag=tag, draft=False, assets=assets))
        return releases

    def read_asset(self, asset):
        with open(asset.path, "rb") as file:
            return file.read()

    def create(self, tag, title, notes, target, files):
        path = os.path.join(self.folder, tag)
        if os.path.exists(path):
            raise GalleryError(f"{relative(path)} already exists.")
        os.makedirs(path)
        for file in files:
            shutil.copyfile(file, os.path.join(path, os.path.basename(file)))
        with open(os.path.join(path, "release.json"), "w", encoding="utf-8", newline="") as file:
            json.dump({"tag": tag, "title": title, "notes": notes, "target": target}, file, indent=2)
            file.write("\n")

    def delete(self, release):
        shutil.rmtree(os.path.join(self.folder, release.tag), ignore_errors=True)


class GitHubReleases:
    """Reads through the REST API (a token from GH_TOKEN or GITHUB_TOKEN when there is one) and writes through gh."""

    def __init__(self, repository):
        if not re.fullmatch(r"[A-Za-z0-9-]+/[A-Za-z0-9._-]+", repository):
            raise GalleryError(f"{repository!r} is not an owner/name repository.")
        self.repository = repository
        self.token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN")

    def describe(self):
        return f"https://github.com/{self.repository}/releases"

    def list(self):
        releases = []
        page = 1
        while True:
            url = f"https://api.github.com/repos/{self.repository}/releases?per_page=100&page={page}"
            items = parse_json(self._get(url, api=True).decode("utf-8"), url)
            for item in items:
                assets = {
                    asset["name"]: Asset(name=asset["name"], size=asset["size"], url=asset["browser_download_url"], id=asset["id"])
                    for asset in item.get("assets", [])
                }
                releases.append(Release(tag=item["tag_name"], draft=bool(item.get("draft")), id=item["id"], assets=assets))
            if len(items) < 100:
                return releases
            page += 1

    def read_asset(self, asset):
        # The download address redirects to a storage host, which must not see the token; the repository is public.
        return self._get(asset.url, api=False)

    def create(self, tag, title, notes, target, files):
        run(["gh", "release", "create", tag, *files, "--repo", self.repository, "--target", target, "--title", title,
             "--notes", notes, "--latest=false"])

    def delete(self, release):
        run(["gh", "api", "--method", "DELETE", f"repos/{self.repository}/releases/{release.id}"])

    def _get(self, url, api):
        headers = {"User-Agent": "mokaterm-plugins-gallery"}
        if api:
            headers["Accept"] = "application/vnd.github+json"
            headers["X-GitHub-Api-Version"] = "2022-11-28"
            if self.token:
                headers["Authorization"] = f"Bearer {self.token}"
        last = None
        for attempt in range(4):
            try:
                with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=60) as response:
                    return response.read()
            except urllib.error.HTTPError as problem:
                if problem.code < 500 and problem.code != 429:
                    raise GalleryError(f"GitHub answered {problem.code} for {url}.") from None
                last = problem
            except (urllib.error.URLError, TimeoutError, ConnectionError) as problem:
                last = problem
            time.sleep(2 ** attempt)
        raise GalleryError(f"Could not reach {url}: {last}.")


# ---- the metadata each release carries

def build_metadata(listing, report, loadcheck, toolchain, commit, repository_source, published, gallery_commit):
    manifest = report.manifest
    module_id = loadcheck.get("moduleId") if isinstance(loadcheck, dict) else None
    return {
        "format": METADATA_FORMAT,
        "id": manifest["id"],
        "version": manifest["version"],
        "sdk": manifest["sdk"],
        "name": manifest["name"],
        "publisher": manifest["publisher"],
        "description": manifest["description"],
        "hosts": manifest["hosts"],
        "hasCode": report.has_code,
        "assembly": manifest.get("assembly"),
        "categories": listing.categories,
        "license": listing.license,
        "source": repository_source if listing.verified else listing.source,
        "commit": commit,
        "tag": listing.tag,
        "verified": listing.verified,
        "package": package_name(manifest["id"], manifest["version"]),
        "sha256": report.sha256,
        "size": report.size,
        "published": published,
        "mokatermVersion": toolchain["mokatermVersion"],
        "mokatermCommit": toolchain.get("mokatermCommit"),
        "moduleId": module_id if isinstance(module_id, str) and _MODULE_ID.fullmatch(module_id) else None,
        "galleryCommit": gallery_commit,
    }


def read_published(backend):
    """Every published version, from its release's metadata, with the package's download address as url."""
    published = []
    problems = []
    for release in backend.list():
        match = _TAG.fullmatch(release.tag)
        if match is None or not is_valid_id(match.group("id")) or not is_valid_version(match.group("version")):
            info(f"Skipping the release {release.tag}: its tag is not <id>-v<version>.")
            continue
        if release.draft:
            continue
        plugin_id, version = match.group("id"), match.group("version")
        package = release.assets.get(package_name(plugin_id, version))
        metadata = release.assets.get(metadata_name(plugin_id, version))
        if package is None or metadata is None:
            problems.append(f"The release {release.tag} lacks {package_name(plugin_id, version) if package is None else metadata_name(plugin_id, version)}.")
            continue
        if metadata.size > _MAX_METADATA_BYTES:
            problems.append(f"{release.tag}: {metadata.name} is too large to be metadata.")
            continue
        try:
            data = parse_json(backend.read_asset(metadata).decode("utf-8-sig"), f"{release.tag}/{metadata.name}")
        except (GalleryError, UnicodeDecodeError) as problem:
            problems.append(str(problem))
            continue
        bad = metadata_problems(data, plugin_id, version, package.size)
        if bad:
            problems.extend(f"{release.tag}/{metadata.name}: {item}" for item in bad)
            continue
        published.append({**data, "url": package.url})
    if problems:
        raise GalleryError("Published releases do not add up:\n  " + "\n  ".join(problems))
    return published


def metadata_problems(data, plugin_id, version, package_size):
    if not isinstance(data, dict):
        return ["it is not a JSON object."]
    found = []

    def text(key, limit=1000, required=True):
        value = data.get(key)
        if value is None and not required:
            return
        if not isinstance(value, str) or not value or len(value) > limit:
            found.append(f"{key} is missing or not a string of at most {limit} characters.")

    if data.get("format") != METADATA_FORMAT:
        found.append(f"format is {data.get('format')!r}; these scripts read {METADATA_FORMAT}.")
    if data.get("id") != plugin_id or data.get("version") != version:
        found.append(f"it describes {data.get('id')} {data.get('version')}, not {plugin_id} {version}.")
    for key in ("name", "publisher", "description", "license", "package", "mokatermVersion"):
        text(key)
    text("assembly", required=False)
    text("tag", 100, required=False)
    if not isinstance(data.get("sdk"), str) or not re.fullmatch(r"\d+\.\d+", data["sdk"]):
        found.append("sdk is not a version such as 0.2.")
    if not isinstance(data.get("source"), str) or not _SOURCE.fullmatch(data["source"]):
        found.append("source is not a https://github.com/owner/name address.")
    if not is_commit(data.get("commit")):
        found.append("commit is not a full commit.")
    if not isinstance(data.get("sha256"), str) or not _SHA256.fullmatch(data["sha256"]):
        found.append("sha256 is not 64 lower-case hex characters.")
    if not isinstance(data.get("size"), int) or isinstance(data.get("size"), bool) or data["size"] != package_size:
        found.append(f"size says {data.get('size')!r}, but the package on the release is {package_size} bytes.")
    if not isinstance(data.get("published"), str) or not _TIME.fullmatch(data["published"]):
        found.append("published is not a UTC time such as 2026-10-13T09:00:00Z.")
    for key in ("verified", "hasCode"):
        if not isinstance(data.get(key), bool):
            found.append(f"{key} is not true or false.")
    hosts = data.get("hosts")
    if not isinstance(hosts, list) or any(host not in ("desktop", "web") for host in hosts) or len(set(hosts)) != len(hosts):
        found.append("hosts is not a list of desktop and web.")
    categories = data.get("categories")
    if (not isinstance(categories, list) or not categories or any(item not in CATEGORIES for item in categories)
            or len(set(categories)) != len(categories)):
        found.append(f"categories is not a list drawn from {', '.join(CATEGORIES)}.")
    return found
