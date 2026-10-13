import os
import tempfile
import unittest
from unittest import mock

import support  # noqa: F401
import gallery_signing
from gallery_common import PUBLIC_KEY, GalleryError


def openssl_available():
    try:
        gallery_signing.openssl()
        return True
    except GalleryError:
        return False


@unittest.skipUnless(openssl_available(), "OpenSSL is not installed")
class SigningTests(unittest.TestCase):
    def setUp(self):
        self._temp = tempfile.TemporaryDirectory()
        self.folder = self._temp.name
        self.private, self.public = gallery_signing.generate_key(self.folder)
        self.index = os.path.join(self.folder, "index.json")
        with open(self.index, "wb") as file:
            file.write(b'{"format": 1, "generated": "2026-10-13T09:00:00Z", "plugins": []}\n')

    def tearDown(self):
        self._temp.cleanup()

    def test_a_signature_verifies_and_is_one_base64_line(self):
        signature = gallery_signing.sign(self.index, self.public, self.private)
        with open(signature, encoding="ascii") as file:
            text = file.read()
        self.assertEqual(89, len(text))
        self.assertTrue(text.endswith("==\n"))
        gallery_signing.verify(self.index, signature, self.public)

    def test_the_key_can_come_from_the_secret(self):
        with open(self.private, encoding="ascii") as file:
            pem = file.read()
        with mock.patch.dict(os.environ, {gallery_signing.KEY_VARIABLE: pem}):
            gallery_signing.sign(self.index, self.public)

    def test_a_changed_index_or_another_key_fails(self):
        signature = gallery_signing.sign(self.index, self.public, self.private)
        with self.assertRaises(GalleryError):
            gallery_signing.verify(self.index, signature, PUBLIC_KEY)
        with open(self.index, "ab") as file:
            file.write(b" ")
        with self.assertRaises(GalleryError):
            gallery_signing.verify(self.index, signature, self.public)

    def test_signing_refuses_to_publish_what_the_public_key_does_not_accept(self):
        with self.assertRaises(GalleryError):
            gallery_signing.sign(self.index, PUBLIC_KEY, self.private)

    def test_no_key_at_all_is_an_error(self):
        with mock.patch.dict(os.environ, {gallery_signing.KEY_VARIABLE: ""}):
            with self.assertRaises(GalleryError):
                gallery_signing.sign(self.index, self.public)


if __name__ == "__main__":
    unittest.main()
