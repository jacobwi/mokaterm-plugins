import json
import os
import tempfile
import unittest

import support  # noqa: F401
import gallery_schema
from gallery_common import GalleryError


def entry(**changes):
    data = {
        "id": "acme.proxmox",
        "source": "https://github.com/acme/mokaterm-proxmox",
        "commit": "9f2c4e1" + "0" * 33,
        "tag": "v1.2.0",
        "project": "src/Acme.Proxmox/Acme.Proxmox.csproj",
        "license": "MIT",
        "categories": ["server-tools"],
    }
    for key, value in changes.items():
        if value is None:
            data.pop(key, None)
        else:
            data[key] = value
    return data


class EntrySchemaTests(unittest.TestCase):
    def assertValid(self, data):
        self.assertEqual([], gallery_schema.problems(data))

    def assertInvalid(self, data, field):
        found = gallery_schema.problems(data)
        self.assertTrue(any(field in problem for problem in found), f"nothing about {field!r} in {found}")

    def test_a_complete_entry_is_valid(self):
        self.assertValid(entry())
        self.assertValid(entry(tag=None, license="MIT OR Apache-2.0", categories=["docker", "utilities"]))

    def test_community_ids_have_a_publisher_and_never_take_ours(self):
        self.assertInvalid(entry(id="proxmox"), "id")
        self.assertInvalid(entry(id="moka.proxmox"), "id")
        self.assertInvalid(entry(id="moka-tools.proxmox"), "id")
        self.assertInvalid(entry(id="mokaterm.proxmox"), "id")
        self.assertInvalid(entry(id="Acme.Proxmox"), "id")
        self.assertInvalid(entry(id="acme..proxmox"), "id")
        self.assertInvalid(entry(id="acme.proxmox\n"), "id")
        self.assertValid(entry(id="mokatermfan.proxmox"))
        self.assertValid(entry(id="acme-labs.proxmox-ve"))

    def test_sources_are_github_repositories_written_one_way(self):
        for source in ("http://github.com/acme/x", "https://gitlab.com/acme/x", "https://github.com/acme/x.git",
                       "https://github.com/acme/x/", "https://github.com/acme", "https://github.com/-acme/x"):
            with self.subTest(source=source):
                self.assertInvalid(entry(source=source), "source")

    def test_commits_are_full(self):
        self.assertInvalid(entry(commit="9f2c4e1"), "commit")
        self.assertInvalid(entry(commit="9F2C4E1" + "0" * 33), "commit")

    def test_projects_stay_inside_the_repository(self):
        for project in ("../x/X.csproj", "/src/X.csproj", "src/../X.csproj", "src\\X.csproj", "X.fsproj", ".hidden/X.csproj"):
            with self.subTest(project=project):
                self.assertInvalid(entry(project=project), "project")
        self.assertValid(entry(project="Acme Proxmox/Acme.Proxmox.csproj"))

    def test_categories_come_from_the_list(self):
        self.assertInvalid(entry(categories=[]), "categories")
        self.assertInvalid(entry(categories=["games"]), "categories[0]")
        self.assertInvalid(entry(categories=["docker", "docker"]), "twice")

    def test_unknown_and_missing_fields(self):
        self.assertInvalid(entry(homepage="https://acme.invalid"), "homepage")
        self.assertInvalid(entry(commit=None), "commit is missing")
        self.assertInvalid([], "must be object")

    def test_listing_and_withdrawn_definitions(self):
        self.assertEqual([], gallery_schema.problems({"categories": ["security"], "license": "MIT"}, "#/$defs/listing"))
        self.assertTrue(gallery_schema.problems({"categories": ["security"]}, "#/$defs/listing"))
        withdrawn = [{"id": "acme.proxmox", "version": "1.2.0", "reason": "Deletes the wrong container. Use 1.2.1."}]
        self.assertEqual([], gallery_schema.problems(withdrawn, "#/$defs/withdrawn"))
        self.assertTrue(gallery_schema.problems([{**withdrawn[0], "reason": "bad\nline"}], "#/$defs/withdrawn"))
        self.assertTrue(gallery_schema.problems([{**withdrawn[0], "reason": "short"}], "#/$defs/withdrawn"))


class ValidatorTests(unittest.TestCase):
    def test_a_keyword_the_validator_does_not_check_is_an_error(self):
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "schema.json")
            with open(path, "w", encoding="utf-8") as file:
                json.dump({"type": "object", "properties": {"source": {"type": "string", "format": "uri"}}}, file)
            with self.assertRaises(GalleryError):
                gallery_schema.problems({}, path=path)


if __name__ == "__main__":
    unittest.main()
