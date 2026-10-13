import json
import os
import tempfile
import unittest

import support
import gallery_releases
from gallery_common import GalleryError


class MetadataTests(unittest.TestCase):
    def test_good_metadata_has_no_problems(self):
        self.assertEqual([], gallery_releases.metadata_problems(support.metadata(), "acme.tools", "1.2.0", 1234))

    def test_metadata_must_match_its_release(self):
        problems = " ".join(gallery_releases.metadata_problems(
            support.metadata(sha256="XYZ", commit="short", categories=["games"]), "acme.other", "1.2.0", 999))
        for text in ("not acme.other", "sha256", "commit", "size", "categories"):
            self.assertIn(text, problems)


class DirectoryReleaseTests(unittest.TestCase):
    def setUp(self):
        self._temp = tempfile.TemporaryDirectory()
        self.folder = self._temp.name
        self.backend = gallery_releases.DirectoryReleases(os.path.join(self.folder, "releases"), "jacobwi/mokaterm-plugins")

    def tearDown(self):
        self._temp.cleanup()

    def release(self, plugin_id, version, size=1234, with_metadata=True):
        package = os.path.join(self.folder, f"{plugin_id}-{version}.mokaplugin")
        with open(package, "wb") as file:
            file.write(b"x" * size)
        files = [package]
        if with_metadata:
            meta = os.path.join(self.folder, f"{plugin_id}-{version}.json")
            data = support.metadata(plugin_id, version, size=size)
            del data["url"]
            with open(meta, "w", encoding="utf-8") as file:
                json.dump(data, file)
            files.append(meta)
        self.backend.create(f"{plugin_id}-v{version}", "title", "notes", "c" * 40, files)

    def test_published_versions_come_from_metadata(self):
        self.release("acme.tools", "1.2.0")
        self.backend.create("not-a-plugin", "title", "notes", "c" * 40, [])
        published = gallery_releases.read_published(self.backend)
        self.assertEqual(1, len(published))
        self.assertEqual(
            "https://github.com/jacobwi/mokaterm-plugins/releases/download/acme.tools-v1.2.0/acme.tools-1.2.0.mokaplugin",
            published[0]["url"])

    def test_a_release_without_its_metadata_is_an_error(self):
        self.release("acme.tools", "1.2.0", with_metadata=False)
        with self.assertRaises(GalleryError):
            gallery_releases.read_published(self.backend)

    def test_backend_specs(self):
        self.assertIsInstance(gallery_releases.open_backend("github:jacobwi/mokaterm-plugins", "x/y"), gallery_releases.GitHubReleases)
        self.assertIsInstance(gallery_releases.open_backend("dir:.work/releases", "x/y"), gallery_releases.DirectoryReleases)
        with self.assertRaises(GalleryError):
            gallery_releases.open_backend("s3:bucket", "x/y")


if __name__ == "__main__":
    unittest.main()
