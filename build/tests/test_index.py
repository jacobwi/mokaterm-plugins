import json
import unittest

import support
import gallery_index
from gallery_common import GalleryError, version_key
from gallery_listings import Listing


def listing(plugin_id, categories):
    return Listing(id=plugin_id, kind="community", path="", categories=categories, license="MIT")


class VersionOrderTests(unittest.TestCase):
    def test_semver_precedence(self):
        ordered = ["1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11",
                   "1.0.0-rc.1", "1.0.0", "1.0.1", "1.2.0", "1.10.0", "2.0.0"]
        self.assertEqual(ordered, sorted(reversed(ordered), key=version_key))

    def test_a_version_that_is_not_semver_is_an_error(self):
        with self.assertRaises(GalleryError):
            version_key("1.0")


class IndexTests(unittest.TestCase):
    def build(self, published, listings=None, withdrawn=None):
        return gallery_index.build(published, listings or {}, withdrawn or {}, "2026-10-13T09:00:00Z")

    def test_plugins_by_id_and_versions_newest_first(self):
        published = [
            support.metadata("zeta.tool", "1.0.0"),
            support.metadata("acme.tools", "1.2.0"),
            support.metadata("acme.tools", "1.10.0", name="Acme tools 10"),
            support.metadata("acme.tools", "1.11.0-beta.1"),
        ]
        index = self.build(published)
        self.assertEqual({"format": 1, "generated": "2026-10-13T09:00:00Z"}, {k: index[k] for k in ("format", "generated")})
        self.assertEqual(["acme.tools", "zeta.tool"], [plugin["id"] for plugin in index["plugins"]])
        acme = index["plugins"][0]
        self.assertEqual(["1.11.0-beta.1", "1.10.0", "1.2.0"], [version["version"] for version in acme["versions"]])
        self.assertEqual(
            ["id", "name", "publisher", "description", "categories", "source", "license", "verified", "hasCode", "hosts", "versions"],
            list(acme))
        self.assertEqual(
            ["version", "sdk", "url", "sha256", "size", "commit", "published", "withdrawn"], list(acme["versions"][0]))
        self.assertIsNone(acme["versions"][0]["withdrawn"])

    def test_the_newest_version_not_withdrawn_describes_the_plugin(self):
        published = [support.metadata("acme.tools", "1.0.0", name="Old"), support.metadata("acme.tools", "2.0.0", name="New")]
        withdrawn = {("acme.tools", "2.0.0"): "Deletes the wrong files. Use 1.0.0."}
        acme = self.build(published, withdrawn=withdrawn)["plugins"][0]
        self.assertEqual("Old", acme["name"])
        self.assertEqual("Deletes the wrong files. Use 1.0.0.", acme["versions"][0]["withdrawn"])
        withdrawn[("acme.tools", "1.0.0")] = "Also broken, in a different way."
        self.assertEqual("New", self.build(published, withdrawn=withdrawn)["plugins"][0]["name"])

    def test_categories_follow_the_current_listing(self):
        published = [support.metadata("acme.tools", "1.0.0", categories=["utilities"])]
        self.assertEqual(["utilities"], self.build(published)["plugins"][0]["categories"])
        moved = {"acme.tools": listing("acme.tools", ["docker", "server-tools"])}
        self.assertEqual(["docker", "server-tools"], self.build(published, moved)["plugins"][0]["categories"])

    def test_the_apps_limits_are_held(self):
        too_many = [support.metadata("acme.tools", f"1.0.{patch}") for patch in range(101)]
        with self.assertRaises(GalleryError):
            self.build(too_many)
        with self.assertRaises(GalleryError):
            self.build([support.metadata(description="x" * 1001)])
        with self.assertRaises(GalleryError):
            self.build([support.metadata(url="http://example.invalid/acme.tools-1.2.0.mokaplugin")])
        self.build([support.metadata(f"acme.tool{number}", "1.0.0") for number in range(2000)])
        with self.assertRaises(GalleryError):
            self.build([support.metadata(f"acme.tool{number}", "1.0.0") for number in range(2001)])

    def test_serialized_bytes_are_utf8_without_a_byte_order_mark(self):
        index = self.build([support.metadata(description="Réseau, 网络 and ✓.")])
        data = gallery_index.serialize(index)
        self.assertFalse(data.startswith(b"\xef\xbb\xbf"))
        self.assertIn("网络".encode(), data)
        self.assertEqual(index, json.loads(data.decode("utf-8")))


if __name__ == "__main__":
    unittest.main()
