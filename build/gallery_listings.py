"""The plugins this repository lists: ours under plugins/<id>/, community ones under gallery/<id>.json."""

import glob
import os
from dataclasses import dataclass, field
from typing import Optional

import gallery_schema
from gallery_common import (
    GALLERY, PLUGINS, GalleryError, is_valid_id, read_json, relative, repository_url, version_key,
)

OURS = "ours"
COMMUNITY = "community"

# Files in gallery/ that are not entries. Entry ids always hold a dot, so neither name can ever be one.
SCHEMA_FILE = "schema.json"
WITHDRAWN_FILE = "withdrawn.json"


@dataclass
class Listing:
    id: str
    kind: str
    path: str
    categories: list
    license: str
    # Ours: the plugin project and its test projects.
    project: Optional[str] = None
    tests: list = field(default_factory=list)
    # Community: where and what to build.
    source: Optional[str] = None
    commit: Optional[str] = None
    tag: Optional[str] = None
    project_in_source: Optional[str] = None

    @property
    def verified(self):
        return self.kind == OURS


@dataclass
class Listings:
    plugins_dir: str = PLUGINS
    gallery_dir: str = GALLERY
    items: dict = field(default_factory=dict)
    withdrawn: dict = field(default_factory=dict)
    problems: list = field(default_factory=list)

    def get(self, plugin_id):
        listing = self.items.get(plugin_id)
        if listing is None:
            raise GalleryError(f"Nothing lists {plugin_id}: there is no plugins/{plugin_id}/ and no gallery/{plugin_id}.json.")
        return listing


def load(repository, plugins_dir=PLUGINS, gallery_dir=GALLERY):
    """Reads every listing and withdrawn.json. Problems are collected, not raised, so one run reports all of them."""
    result = Listings(plugins_dir=plugins_dir, gallery_dir=gallery_dir)
    ours = _load_ours(plugins_dir, result.problems)
    community = _load_community(gallery_dir, repository, result.problems)
    for plugin_id in sorted(set(ours) & set(community)):
        result.problems.append(f"{plugin_id} is listed twice, as plugins/{plugin_id}/ and as gallery/{plugin_id}.json.")
    result.items = {**community, **ours}
    result.withdrawn = _load_withdrawn(gallery_dir, result.problems)
    return result


def check_against_published(listings, published, moves_allowed=False):
    """Rules that need what was already released: who owns an id, and that withdrawn versions exist.

    A community plugin that moved to another repository fails the pull request check until a maintainer has confirmed
    the author and merged it anyway; publishing (moves_allowed) then takes what main says.
    """
    found = []
    by_id = {}
    for meta in published:
        by_id.setdefault(meta["id"], []).append(meta)
    for listing in listings.items.values():
        metas = by_id.get(listing.id, [])
        if listing.kind != COMMUNITY or not metas:
            continue
        if any(meta["verified"] for meta in metas):
            found.append(f"gallery/{listing.id}.json takes the id of one of our own plugins.")
            continue
        # The newest version says where the plugin lives now, so a move that was confirmed and published stays settled.
        newest = max(metas, key=lambda meta: version_key(meta["version"]))
        if not moves_allowed and newest["source"].lower() != listing.source.lower():
            found.append(
                f"gallery/{listing.id}.json builds from {listing.source}, but {listing.id} {newest['version']} was "
                f"published from {newest['source']}. Moving a plugin to another repository needs a maintainer to "
                "confirm it is the same author.")
    released = {(meta["id"], meta["version"]) for meta in published}
    for plugin_id, version in sorted(listings.withdrawn):
        if (plugin_id, version) not in released:
            found.append(f"gallery/withdrawn.json names {plugin_id} {version}, which was never released.")
    return found


def _load_ours(plugins_dir, problems):
    found = {}
    if not os.path.isdir(plugins_dir):
        return found
    for name in sorted(os.listdir(plugins_dir)):
        folder = os.path.join(plugins_dir, name)
        if not os.path.isdir(folder) or name.startswith("."):
            continue
        where = relative(folder)
        if not is_valid_id(name):
            problems.append(f"{where}: a plugin folder is named after the plugin id (lower-case letters and digits joined by dots or dashes).")
            continue
        listing_path = os.path.join(folder, "listing.json")
        if not os.path.isfile(listing_path):
            problems.append(f"{where} has no listing.json.")
            continue
        try:
            data = read_json(listing_path)
        except GalleryError as error:
            problems.append(str(error))
            continue
        bad = gallery_schema.problems(data, "#/$defs/listing")
        if bad:
            problems.extend(f"{relative(listing_path)}: {problem}" for problem in bad)
            continue
        projects = sorted(glob.glob(os.path.join(folder, "src", "*.csproj")))
        if len(projects) != 1:
            problems.append(f"{where}/src should hold one .csproj, the plugin's, and holds {len(projects)}.")
            continue
        tests = sorted(glob.glob(os.path.join(folder, "tests", "**", "*.csproj"), recursive=True))
        found[name] = Listing(
            id=name, kind=OURS, path=listing_path, categories=list(data["categories"]), license=data["license"],
            project=projects[0], tests=tests)
    return found


def _load_community(gallery_dir, repository, problems):
    found = {}
    if not os.path.isdir(gallery_dir):
        return found
    own_source = repository_url(repository).lower()
    for name in sorted(os.listdir(gallery_dir)):
        path = os.path.join(gallery_dir, name)
        if name in (SCHEMA_FILE, WITHDRAWN_FILE) or os.path.isdir(path):
            continue
        where = relative(path)
        if not name.endswith(".json"):
            problems.append(f"{where}: gallery/ holds one <id>.json per plugin, schema.json and withdrawn.json, and nothing else.")
            continue
        try:
            data = read_json(path)
        except GalleryError as error:
            problems.append(str(error))
            continue
        bad = gallery_schema.problems(data)
        if bad:
            problems.extend(f"{where}: {problem}" for problem in bad)
            continue
        plugin_id = data["id"]
        if name != f"{plugin_id}.json":
            problems.append(f"{where}: the file is named after the plugin's id, so it should be gallery/{plugin_id}.json.")
            continue
        if data["source"].lower() == own_source:
            problems.append(f"{where}: plugins built from this repository live under plugins/, not gallery/.")
            continue
        found[plugin_id] = Listing(
            id=plugin_id, kind=COMMUNITY, path=path, categories=list(data["categories"]), license=data["license"],
            source=data["source"], commit=data["commit"], tag=data.get("tag"), project_in_source=data["project"])
    return found


def _load_withdrawn(gallery_dir, problems):
    path = os.path.join(gallery_dir, WITHDRAWN_FILE)
    if not os.path.isfile(path):
        return {}
    try:
        data = read_json(path)
    except GalleryError as error:
        problems.append(str(error))
        return {}
    bad = gallery_schema.problems(data, "#/$defs/withdrawn")
    if bad:
        problems.extend(f"{relative(path)}: {problem}" for problem in bad)
        return {}
    withdrawn = {}
    for item in data:
        key = (item["id"], item["version"])
        if key in withdrawn:
            problems.append(f"{relative(path)} names {key[0]} {key[1]} twice.")
        withdrawn[key] = item["reason"]
    return withdrawn
