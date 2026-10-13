# Gallery index format

The gallery publishes one file the app reads, `index.json`, and its signature, `index.json.sig`, both on GitHub Pages:

- `https://jacobwi.github.io/mokaterm-plugins/index.json`
- `https://jacobwi.github.io/mokaterm-plugins/index.json.sig`

## Signature

`index.json.sig` is the Ed25519 signature over the exact bytes of `index.json`, written as base64 on one line (88
characters, a final newline allowed). The private key is the repository secret `GALLERY_SIGNING_KEY` (a PEM PKCS#8
Ed25519 key); its public half is built into Mokaterm. The app verifies the signature before it reads a single field and
refuses an index that fails, so neither GitHub Pages nor anyone between it and the app can change what is listed.

Mokaterm carries a list of public keys, so a new key can be added in one release and the old one dropped in a later
one. Signing with a key the app does not carry fails every install until the app is updated.

## index.json

UTF-8, no byte order mark. Field names are camelCase. Every field below is required unless it says optional.

```json
{
  "format": 1,
  "generated": "2026-10-13T09:00:00Z",
  "plugins": [
    {
      "id": "moka.certificates",
      "name": "Certificates",
      "publisher": "Moka",
      "description": "Read, make, convert, sign and encrypt with certificates and keys, and check the ones a server uses.",
      "categories": ["security", "utilities", "server-tools"],
      "source": "https://github.com/jacobwi/mokaterm-plugins",
      "license": "MIT",
      "verified": true,
      "hasCode": true,
      "hosts": ["desktop", "web"],
      "versions": [
        {
          "version": "1.0.0",
          "sdk": "0.2",
          "url": "https://github.com/jacobwi/mokaterm-plugins/releases/download/moka.certificates-v1.0.0/moka.certificates-1.0.0.mokaplugin",
          "sha256": "64 lower-case hex characters",
          "size": 123456,
          "commit": "40 lower-case hex characters",
          "published": "2026-10-13T09:00:00Z",
          "withdrawn": null
        }
      ]
    }
  ]
}
```

- `format`: 1. The app refuses a higher number and says Mokaterm needs an update to read the gallery.
- `generated`: when the index was written, ISO 8601 UTC (`2026-10-13T09:00:00Z`).
- `plugins`: sorted by `id` (ordinal), each id once.
  - `id`, `name`, `publisher`, `description`, `hosts`, `hasCode`: as `plugin.json` says in the plugin's newest version
    that is not withdrawn, or in its newest version when every one is withdrawn (`hosts` empty means every host, and
    lists `desktop` before `web`; `hasCode` false is a data-only plugin such as a theme pack).
  - `categories`: one or more of `protocols`, `server-tools`, `docker`, `utilities`, `security`, `themes`, as the
    plugin's listing says now (`plugins/<id>/listing.json` or `gallery/<id>.json`), so moving a plugin to another
    category needs no new version; a plugin with no listing left keeps those of the version above.
  - `source`, `license`, `verified`: from the same version as `name`.
  - `source`: the https address of the repository the package was built from.
  - `license`: an SPDX identifier.
  - `verified`: true only for plugins whose source lives in this repository under `plugins/` (ours); false for
    community plugins listed through `gallery/`.
  - `versions`: newest first by SemVer precedence, every version still downloadable.
    - `version`: the plugin's version, as in its `plugin.json`: SemVer `MAJOR.MINOR.PATCH` with an optional
      `-prerelease`, never `+build`.
    - `sdk`: the major.minor of Mokaterm the package was built against, as in its `plugin.json`. The app offers only
      versions whose `sdk` it loads.
    - `url`: an https address of the `.mokaplugin`, on this repository's GitHub Releases.
    - `sha256`, `size`: of the `.mokaplugin` file. The app refuses a download that differs in either.
    - `commit`: the full commit the package was built from, in `source`.
    - `published`: when the version was added, ISO 8601 UTC.
    - `withdrawn`: optional; null, or a sentence saying why the version should not be used. The app never offers a
      withdrawn version and warns about an installed one. The gallery always writes it, null included.

## Where it comes from

Every published version is a GitHub release of this repository tagged `<id>-v<version>`, holding the package
`<id>-<version>.mokaplugin` and its metadata `<id>-<version>.json` (the plugin.json fields, the listing's categories
and license, source, commit, sha256, size, published and the Mokaterm it was built against). `build/gallery.py index`
rebuilds `index.json` from every release's metadata and `gallery/withdrawn.json` on each push to `main`, so a release
deleted by hand drops out of the next index and nothing is ever committed back.

## Limits the app holds the index to

- `index.json` at most 4 MiB, the signature at most 1 KiB.
- At most 2000 plugins and 100 versions each.
- Every text field at most 1000 characters, and `id` by the plugin id rule (lower-case letters and digits joined by
  dots or dashes, at most 64 characters).
- A package at most 64 MiB, which is also the most the app installs from a file.
