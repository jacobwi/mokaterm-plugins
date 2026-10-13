"""Builds index.json, the file the app reads, from the published versions (docs/index-format.md is the contract)."""

import json

from gallery_common import GalleryError, is_valid_id, version_key

INDEX_FORMAT = 1
MAX_INDEX_BYTES = 4 * 1024 * 1024
MAX_PLUGINS = 2000
MAX_VERSIONS = 100
MAX_TEXT = 1000


def build(published, listings, withdrawn, generated):
    """published: release metadata (each with url). listings: id -> current Listing. withdrawn: (id, version) -> reason."""
    by_id = {}
    for meta in published:
        by_id.setdefault(meta["id"], []).append(meta)

    plugins = []
    for plugin_id in sorted(by_id):
        versions = sorted(by_id[plugin_id], key=lambda meta: version_key(meta["version"]), reverse=True)
        seen = set()
        for meta in versions:
            if meta["version"] in seen:
                raise GalleryError(f"{plugin_id} {meta['version']} is published twice.")
            seen.add(meta["version"])

        # The plugin is described by the newest version people can still install; once every version is withdrawn,
        # by the newest one, so the app can still warn about it.
        current = next((meta for meta in versions if (plugin_id, meta["version"]) not in withdrawn), versions[0])
        listing = listings.get(plugin_id)
        plugins.append({
            "id": plugin_id,
            "name": current["name"],
            "publisher": current["publisher"],
            "description": current["description"],
            # Categories describe where to show a plugin rather than what a version is, so the listing's current ones win.
            "categories": list(listing.categories if listing is not None else current["categories"]),
            "source": current["source"],
            "license": current["license"],
            "verified": current["verified"],
            "hasCode": current["hasCode"],
            "hosts": list(current["hosts"]),
            "versions": [
                {
                    "version": meta["version"],
                    "sdk": meta["sdk"],
                    "url": meta["url"],
                    "sha256": meta["sha256"],
                    "size": meta["size"],
                    "commit": meta["commit"],
                    "published": meta["published"],
                    "withdrawn": withdrawn.get((plugin_id, meta["version"])),
                }
                for meta in versions
            ],
        })

    index = {"format": INDEX_FORMAT, "generated": generated, "plugins": plugins}
    check_limits(index)
    return index


def serialize(index):
    """The exact bytes that are signed and published: UTF-8 without a byte order mark."""
    data = (json.dumps(index, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
    if len(data) > MAX_INDEX_BYTES:
        raise GalleryError(f"index.json would be {len(data)} bytes; the app reads at most {MAX_INDEX_BYTES}.")
    return data


def check_limits(index):
    """The limits the app holds the index to, so a listing the app would refuse never goes out."""
    problems = []
    plugins = index["plugins"]
    if len(plugins) > MAX_PLUGINS:
        problems.append(f"The index lists {len(plugins)} plugins; the app reads at most {MAX_PLUGINS}.")
    for plugin in plugins:
        plugin_id = plugin["id"]
        if not is_valid_id(plugin_id):
            problems.append(f"{plugin_id!r} is not a plugin id.")
        if len(plugin["versions"]) > MAX_VERSIONS:
            problems.append(f"{plugin_id} has {len(plugin['versions'])} versions; the app reads at most {MAX_VERSIONS}. Delete the oldest releases.")
        texts = [plugin["name"], plugin["publisher"], plugin["description"], plugin["source"], plugin["license"], *plugin["categories"], *plugin["hosts"]]
        for version in plugin["versions"]:
            texts += [version["version"], version["sdk"], version["url"], version["commit"], version["published"]]
            if version["withdrawn"] is not None:
                texts.append(version["withdrawn"])
            if not version["url"].startswith("https://"):
                problems.append(f"{plugin_id} {version['version']} downloads from {version['url']}, which is not https.")
        for text in texts:
            if not isinstance(text, str) or len(text) > MAX_TEXT:
                problems.append(f"{plugin_id} has a field longer than {MAX_TEXT} characters or not text.")
                break
    if problems:
        raise GalleryError("The index breaks the app's limits:\n  " + "\n  ".join(problems))
