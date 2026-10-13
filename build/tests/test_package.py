import os
import tempfile
import unittest
from unittest import mock

import support
import gallery_package


class PackageCheckTests(unittest.TestCase):
    def setUp(self):
        self._temp = tempfile.TemporaryDirectory()
        self.folder = self._temp.name

    def tearDown(self):
        self._temp.cleanup()

    def check(self, files, plugin_id="acme.tools", verified=False, **kwargs):
        path = support.write_package(self.folder, files, **kwargs)
        return gallery_package.check(path, plugin_id, support.SDK, support.RUNTIME, verified)

    def assertProblem(self, report, text):
        self.assertTrue(any(text in problem for problem in report.problems), f"no problem mentions {text!r}: {report.problems}")

    def test_code_plugin_built_by_the_sdk_passes(self):
        report = self.check(support.code_files())
        self.assertEqual([], report.problems)
        self.assertTrue(report.has_code)
        self.assertEqual(1, report.theme_count)
        self.assertEqual(["desktop", "web"], report.manifest["hosts"])
        self.assertEqual(64, len(report.sha256))

    def test_data_plugin_passes(self):
        report = self.check(support.data_files())
        self.assertEqual([], report.problems)
        self.assertFalse(report.has_code)

    def test_anything_beside_the_four_top_level_names_is_refused(self):
        self.assertProblem(self.check({**support.code_files(), "README.md": b"hi"}), "nothing else")
        self.assertProblem(self.check({**support.code_files(), "docs/a.txt": b"hi"}), "nothing else")

    def test_paths_that_leave_the_folder_are_refused(self):
        for name in ("lib/../evil.dll", "/lib/evil.dll", "lib/c:evil.dll", "lib//evil.dll", "lib/./evil.dll", "lib/a\x01.dll"):
            with self.subTest(name=name):
                self.assertProblem(self.check({**support.code_files(), name: b"x"}), "refuses")

    def test_two_names_that_differ_only_in_case_are_one_file_twice(self):
        files = {**support.code_files(), "wwwroot/js/Tools.js": b"x"}
        self.assertProblem(self.check(files), "twice")

    def test_files_mokaterm_ships_are_refused(self):
        for name in ("lib/Mokaterm.Abstractions.dll", "lib/Moka.Red.Core.dll", "lib/Microsoft.AspNetCore.Components.dll",
                     "lib/Microsoft.Extensions.Logging.dll", "lib/Microsoft.JSInterop.dll", "lib/System.Text.Json.dll",
                     "lib/fr/System.Text.Json.resources.dll"):
            with self.subTest(name=name):
                self.assertProblem(self.check({**support.code_files(), name: b"MZ"}), name)

    def test_system_packages_that_are_not_part_of_dotnet_may_ship(self):
        report = self.check(support.code_files())
        self.assertNotIn("lib/System.Security.Cryptography.Pkcs.dll", " ".join(report.problems))

    def renamed(self, assembly):
        name = assembly[:-len(".dll")]
        return {key.replace("Acme.Tools", name): value for key, value in support.code_files(assembly=assembly).items()}

    def test_our_plugins_may_name_their_assembly_like_mokaterm(self):
        for assembly in ("Mokaterm.Plugins.Tools.dll", "Moka.Certificates.dll"):
            with self.subTest(assembly=assembly):
                self.assertEqual([], self.check(self.renamed(assembly), verified=True).problems)

    def test_community_plugins_may_not_name_their_assembly_like_mokaterm(self):
        for assembly in ("Mokaterm.Plugins.Tools.dll", "Moka.Certificates.dll", "moka.tools.dll", "MOKATERM.X.dll"):
            with self.subTest(assembly=assembly):
                self.assertProblem(self.check(self.renamed(assembly)), "kept for Mokaterm")
        self.assertEqual([], self.check(self.renamed("Mokatermfan.Tools.dll")).problems)

    def test_id_and_sdk_must_be_the_listed_ones(self):
        self.assertProblem(self.check(support.code_files(), plugin_id="acme.other"), "listed as acme.other")
        self.assertProblem(self.check(support.code_files(sdk="0.1")), "builds for 0.2")

    def test_the_gallery_needs_publisher_description_and_sdk(self):
        self.assertProblem(self.check(support.code_files(publisher=None)), "MokatermPluginPublisher")
        self.assertProblem(self.check(support.code_files(description=None)), "MokatermPluginDescription")
        self.assertProblem(self.check(support.data_files(sdk=None)), "(sdk)")

    def test_versions_are_semver_without_build_metadata(self):
        for version in ("1.0", "1.0.0+build.5", "01.0.0", "v1.0.0"):
            with self.subTest(version=version):
                self.assertProblem(self.check(support.code_files(version=version)), "version")
        self.assertEqual([], self.check(support.code_files(version="1.0.0-beta.2")).problems)

    def test_manifest_fields_the_gallery_does_not_know_are_refused(self):
        self.assertProblem(self.check(support.code_files(homepage="https://acme.invalid")), "homepage")

    def test_hosts_are_desktop_and_web_only(self):
        self.assertProblem(self.check(support.code_files(hosts=["mobile"])), "mobile")
        self.assertProblem(self.check(support.code_files(hosts=["web", "web"])), "twice")
        self.assertEqual(["web"], self.check(support.code_files(hosts=["web"])).manifest["hosts"])

    def test_assembly_must_be_in_lib(self):
        files = support.code_files()
        del files["lib/Acme.Tools.dll"]
        self.assertProblem(self.check(files), "not in the package's lib folder")

    def test_data_plugin_with_lib_is_refused(self):
        self.assertProblem(self.check({**support.data_files(), "lib/Acme.Tools.dll": b"MZ"}), "carries no code")

    def test_themes_the_app_would_skip_are_refused(self):
        self.assertProblem(self.check({**support.data_files(), "themes/more/deep.json": b"{}"}), "straight from themes/")
        self.assertProblem(self.check({**support.data_files(), "themes/big.json": b"x" * (512 * 1024 + 1)}), "512 KiB")

    def test_unpacked_sizes_are_counted_while_reading(self):
        with mock.patch.object(gallery_package, "MAX_FILE_BYTES", 1000), mock.patch.object(gallery_package, "MAX_TOTAL_BYTES", 5000):
            self.assertProblem(self.check({**support.data_files(), "themes/big.json": b"x" * 1001}), "unpacked")
            files = {**support.data_files(), **{f"wwwroot/{index}.js": b"x" * 900 for index in range(6)}}
            self.assertProblem(self.check(files), "unpacks to more than")

    def test_a_file_that_is_not_a_zip_is_refused(self):
        path = os.path.join(self.folder, "broken.mokaplugin")
        with open(path, "wb") as file:
            file.write(b"not a zip")
        self.assertProblem(gallery_package.check(path, "acme.tools", support.SDK), "not a readable zip")

    def test_compression_the_app_cannot_read_is_refused(self):
        import zipfile
        self.assertProblem(self.check(support.data_files(), method=zipfile.ZIP_BZIP2), "deflate")


if __name__ == "__main__":
    unittest.main()
