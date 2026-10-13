"""Signs index.json with Ed25519 through OpenSSL and checks the signature against the public key the app carries.

index.json.sig is base64 of the 64-byte signature over the exact bytes of index.json, on one line. The private key is
a PEM PKCS#8 Ed25519 key; in CI it comes from the GALLERY_SIGNING_KEY secret and only ever lives in a temporary file
that is deleted when the signature is made.
"""

import base64
import os
import shutil
import subprocess
import tempfile

from gallery_common import GalleryError, info, relative

KEY_VARIABLE = "GALLERY_SIGNING_KEY"
MAX_SIGNATURE_BYTES = 1024


def openssl():
    candidates = [os.environ.get("OPENSSL"), shutil.which("openssl")]
    git = shutil.which("git")
    if os.name == "nt" and git:
        # Git for Windows carries OpenSSL even where nothing else does; git.exe sits in cmd/ or bin/ of its install.
        install = os.path.dirname(os.path.dirname(os.path.realpath(git)))
        candidates += [os.path.join(install, "usr", "bin", "openssl.exe"), os.path.join(install, "mingw64", "bin", "openssl.exe")]
    for candidate in candidates:
        if candidate and os.path.isfile(candidate):
            return candidate
    raise GalleryError("OpenSSL 3 is needed to sign and verify; put openssl on PATH or point OPENSSL at it.")


def generate_key(folder):
    """A throwaway key pair for local runs: <folder>/gallery-test.pem and gallery-test.pub.pem."""
    os.makedirs(folder, exist_ok=True)
    private = os.path.join(folder, "gallery-test.pem")
    public = os.path.join(folder, "gallery-test.pub.pem")
    _openssl(["genpkey", "-algorithm", "ed25519", "-out", private])
    _openssl(["pkey", "-in", private, "-pubout", "-out", public])
    return private, public


def sign(index_path, public_key, key_path=None):
    """Writes <index>.sig with the key at key_path, or from GALLERY_SIGNING_KEY, then verifies it with public_key."""
    signature_path = index_path + ".sig"
    with tempfile.TemporaryDirectory(prefix="gallery-sign-") as temp:
        if key_path is None:
            pem = os.environ.get(KEY_VARIABLE, "")
            if "PRIVATE KEY" not in pem:
                raise GalleryError(f"{KEY_VARIABLE} holds no PEM private key.")
            key_path = os.path.join(temp, "key.pem")
            # Owner only from the first byte; the folder goes, key included, when the signature is made.
            descriptor = os.open(key_path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(descriptor, "w", encoding="ascii", newline="\n") as file:
                file.write(pem.strip() + "\n")
        raw = os.path.join(temp, "index.sig.bin")
        _openssl(["pkeyutl", "-sign", "-rawin", "-inkey", key_path, "-in", index_path, "-out", raw])
        with open(raw, "rb") as file:
            signature = file.read()
    if len(signature) != 64:
        raise GalleryError(f"OpenSSL made a {len(signature)}-byte signature; an Ed25519 signature is 64 bytes. Is the key Ed25519?")
    with open(signature_path, "w", encoding="ascii", newline="\n") as file:
        file.write(base64.b64encode(signature).decode("ascii") + "\n")
    verify(index_path, signature_path, public_key)
    return signature_path


def verify(index_path, signature_path, public_key):
    """Fails unless signature_path holds the signature of index_path's exact bytes under public_key."""
    if os.path.getsize(signature_path) > MAX_SIGNATURE_BYTES:
        raise GalleryError(f"{relative(signature_path)} is larger than the app reads.")
    with open(signature_path, encoding="ascii") as file:
        text = file.read()
    line = text[:-1] if text.endswith("\n") else text
    if len(line) != 88:
        raise GalleryError(f"{relative(signature_path)} is not one line of 88 base64 characters.")
    try:
        signature = base64.b64decode(line, validate=True)
    except ValueError:
        raise GalleryError(f"{relative(signature_path)} is not base64.") from None
    with tempfile.TemporaryDirectory(prefix="gallery-verify-") as temp:
        raw = os.path.join(temp, "index.sig.bin")
        with open(raw, "wb") as file:
            file.write(signature)
        result = subprocess.run(
            [openssl(), "pkeyutl", "-verify", "-rawin", "-pubin", "-inkey", public_key, "-in", index_path, "-sigfile", raw],
            capture_output=True, text=True)
    if result.returncode != 0 or "Signature Verified Successfully" not in result.stdout:
        raise GalleryError(f"The signature of {relative(index_path)} does not verify with {relative(public_key)}.")
    info(f"{relative(index_path)} verifies with {relative(public_key)}.")


def _openssl(arguments):
    result = subprocess.run([openssl(), *arguments], capture_output=True, text=True)
    if result.returncode != 0:
        # OpenSSL's errors name files and algorithms, never key material.
        raise GalleryError(f"openssl {arguments[0]} failed: {(result.stderr or result.stdout).strip()}")
