"""Where the release step finds what the build jobs made."""

import json
import os
import tempfile
import unittest

import support  # noqa: F401  (puts build/ on the path)
import gallery_build


def _built(folder, plugin_id):
    os.makedirs(folder, exist_ok=True)
    with open(os.path.join(folder, "build.json"), "w", encoding="utf-8") as file:
        json.dump({"id": plugin_id, "package": f"{plugin_id}-1.0.0.mokaplugin"}, file)
    with open(os.path.join(folder, f"{plugin_id}-1.0.0.mokaplugin"), "wb") as file:
        file.write(b"zip")


class BuiltFoldersTests(unittest.TestCase):
    def test_one_folder_per_artifact(self):
        with tempfile.TemporaryDirectory() as root:
            _built(os.path.join(root, "package-moka.b"), "moka.b")
            _built(os.path.join(root, "package-moka.a"), "moka.a")
            found = gallery_build.built_folders(root)
            self.assertEqual(["moka.a", "moka.b"], [plugin_id for _, plugin_id in found])

    def test_a_single_artifact_unpacked_straight_into_the_path(self):
        # What actions/download-artifact does when the pattern matches one artifact: no folder named after it.
        with tempfile.TemporaryDirectory() as root:
            _built(root, "moka.certificates")
            self.assertEqual([(root, "moka.certificates")], gallery_build.built_folders(root))

    def test_the_id_comes_from_build_json_not_the_folder_name(self):
        with tempfile.TemporaryDirectory() as root:
            _built(os.path.join(root, "whatever"), "moka.certificates")
            self.assertEqual(["moka.certificates"], [plugin_id for _, plugin_id in gallery_build.built_folders(root)])

    def test_nothing_built(self):
        with tempfile.TemporaryDirectory() as root:
            self.assertEqual([], gallery_build.built_folders(root))


if __name__ == "__main__":
    unittest.main()
