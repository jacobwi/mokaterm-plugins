import json
import os
import tempfile
import unittest

import support
import gallery
import gallery_listings
from gallery_common import WORK, relative

REPOSITORY = "jacobwi/mokaterm-plugins"


class ListingTests(unittest.TestCase):
    def setUp(self):
        # Under the repository, because pull request paths are relative to it.
        os.makedirs(WORK, exist_ok=True)
        self._temp = tempfile.TemporaryDirectory(dir=WORK)
        self.plugins = os.path.join(self._temp.name, "plugins")
        self.gallery = os.path.join(self._temp.name, "gallery")
        os.makedirs(self.plugins)
        os.makedirs(self.gallery)

    def tearDown(self):
        self._temp.cleanup()

    def ours(self, plugin_id, listing=None, projects=("Moka.X.csproj",)):
        folder = os.path.join(self.plugins, plugin_id)
        os.makedirs(os.path.join(folder, "src"), exist_ok=True)
        os.makedirs(os.path.join(folder, "tests", "Moka.X.Tests"), exist_ok=True)
        for project in projects:
            open(os.path.join(folder, "src", project), "w").close()
        open(os.path.join(folder, "tests", "Moka.X.Tests", "Moka.X.Tests.csproj"), "w").close()
        self.write(os.path.join(folder, "listing.json"), listing or {"categories": ["security"], "license": "MIT"})

    def entry(self, plugin_id, name=None, **changes):
        data = {
            "id": plugin_id,
            "source": f"https://github.com/acme/{plugin_id}",
            "commit": "1" * 40,
            "project": "src/X.csproj",
            "license": "MIT",
            "categories": ["utilities"],
            **changes,
        }
        self.write(os.path.join(self.gallery, name or f"{plugin_id}.json"), data)

    def write(self, path, data):
        with open(path, "w", encoding="utf-8") as file:
            file.write(data if isinstance(data, str) else json.dumps(data))

    def load(self):
        return gallery_listings.load(REPOSITORY, self.plugins, self.gallery)

    def test_ours_and_community_listings_are_read(self):
        self.ours("moka.certificates")
        self.entry("acme.proxmox", tag="v1.0.0")
        self.write(os.path.join(self.gallery, "withdrawn.json"), [{"id": "acme.proxmox", "version": "1.0.0", "reason": "Breaks on Proxmox 9; use 1.0.1."}])
        listings = self.load()
        self.assertEqual([], listings.problems)
        ours = listings.get("moka.certificates")
        self.assertTrue(ours.verified)
        self.assertEqual(1, len(ours.tests))
        community = listings.get("acme.proxmox")
        self.assertFalse(community.verified)
        self.assertEqual("v1.0.0", community.tag)
        self.assertEqual({("acme.proxmox", "1.0.0"): "Breaks on Proxmox 9; use 1.0.1."}, listings.withdrawn)

    def test_problems_are_collected(self):
        self.ours("Moka.Bad")
        self.ours("moka.two", projects=("A.csproj", "B.csproj"))
        self.ours("moka.nolisting")
        os.remove(os.path.join(self.plugins, "moka.nolisting", "listing.json"))
        self.entry("acme.named", name="acme.other.json")
        self.entry("acme.own", source="https://github.com/jacobwi/mokaterm-plugins")
        self.write(os.path.join(self.gallery, "notes.md"), "hello")
        problems = " | ".join(self.load().problems)
        for text in ("plugin folder is named", "should hold one .csproj", "has no listing.json", "gallery/acme.named.json",
                     "live under plugins/", "notes.md"):
            self.assertIn(text, problems)

    def test_an_id_listed_twice_is_a_problem(self):
        self.ours("acme.proxmox")
        self.entry("acme.proxmox")
        self.assertIn("listed twice", " ".join(self.load().problems))

    def test_published_versions_bind_ids_to_sources(self):
        self.entry("acme.proxmox")
        listings = self.load()
        moved = support.metadata("acme.proxmox", "1.0.0", source="https://github.com/someone-else/proxmox")
        self.assertIn("published from", " ".join(gallery_listings.check_against_published(listings, [moved])))
        self.assertEqual([], gallery_listings.check_against_published(listings, [moved], moves_allowed=True))
        settled = support.metadata("acme.proxmox", "1.1.0", source="https://github.com/acme/acme.proxmox")
        self.assertEqual([], gallery_listings.check_against_published(listings, [moved, settled]))
        ours = support.metadata("acme.proxmox", "1.0.0", verified=True, source=f"https://github.com/{REPOSITORY}")
        self.assertIn("our own plugins", " ".join(gallery_listings.check_against_published(listings, [ours])))
        same = support.metadata("acme.proxmox", "1.0.0", source="https://github.com/Acme/acme.proxmox")
        self.assertEqual([], gallery_listings.check_against_published(listings, [same]))

    def test_withdrawn_versions_must_exist(self):
        self.entry("acme.proxmox")
        self.write(os.path.join(self.gallery, "withdrawn.json"), [{"id": "acme.proxmox", "version": "9.9.9", "reason": "Never existed at all."}])
        listings = self.load()
        self.assertIn("never released", " ".join(gallery_listings.check_against_published(listings, [])))

    def test_changed_files_pick_what_a_pull_request_builds(self):
        self.ours("moka.certificates")
        self.ours("moka.kubernetes")
        self.entry("acme.proxmox")
        listings = self.load()
        plugins = relative(self.plugins)
        gallery_dir = relative(self.gallery)
        touched, everything, sources = gallery.changed_listings(
            [f"{plugins}/moka.certificates/src/Tool.razor", f"{plugins}/moka.kubernetes/listing.json",
             f"{gallery_dir}/acme.proxmox.json", f"{gallery_dir}/withdrawn.json", "docs/submitting.md"], listings)
        self.assertEqual({"moka.certificates", "moka.kubernetes", "acme.proxmox"}, touched)
        self.assertFalse(everything)
        self.assertEqual({"moka.certificates"}, sources)
        touched, everything, _ = gallery.changed_listings(["build/gallery_build.py"], listings)
        self.assertTrue(everything)
        self.assertEqual({"moka.certificates", "moka.kubernetes", "acme.proxmox"}, touched)
        self.assertTrue(gallery.changed_listings(["Directory.Packages.props"], listings)[1])


if __name__ == "__main__":
    unittest.main()
