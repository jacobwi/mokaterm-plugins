# Building and testing plugins here

Our own plugins live under `plugins/<id>/`. Community plugins are entries under `gallery/` (see `submitting.md`).
Everything below runs the same on Windows and on the Linux runners: the steps are Python 3 scripts with no packages to
install.

## What you need

- The .NET 10 SDK and Python 3.10 or newer.
- A Mokaterm checkout beside this repository (`../Mokaterm`), or anywhere else with `MOKATERM_SOURCE` pointing at it,
  at the ref `mokaterm.json` names.
- Git, for community plugins, and OpenSSL 3 for signing (Git for Windows carries one; `OPENSSL` points at another).

## The first time

```
python build/mokaterm_feed.py
python build/gallery.py toolchain
```

`mokaterm_feed.py` puts the Mokaterm packages plugins build against into `.feed` and writes `.feed/mokaterm.props`.
With a `ref` in `mokaterm.json` it packs them from the checkout, stamped with the time, so run it again after pulling
Mokaterm. With only a `version` it downloads that release from nuget.org. `nuget.config` takes every `Mokaterm.*`
package from `.feed` and nothing else, and restores go to `.work/packages`, never to your own NuGet cache.

`gallery.py toolchain` builds the load check (`build/loadcheck`), a small console that loads a package through
Mokaterm's own plugin loader, from `Mokaterm.Core` and `Mokaterm.UI.Common` in the checkout. It refuses a checkout
whose version does not match `mokaterm.json`, and it records the module ids Mokaterm registers itself.

## Building one of ours

```
python build/gallery.py build moka.certificates
```

This builds `plugins/moka.certificates/src`, runs every test project under `plugins/moka.certificates/tests`, checks the
package and loads it through Mokaterm's plugin loader for each host it names. The package lands in
`.work/out/moka.certificates/`, ready to install from Settings, Plugins, in the desktop app. `--no-tests` skips the
tests.

For the inner loop, plain dotnet works as well, after the first-time steps:

```
dotnet build plugins/moka.certificates/src/Moka.Certificates.csproj
dotnet test --project plugins/<id>/tests/<TestProject>/<TestProject>.csproj
```

## Adding a plugin of ours

- `plugins/<id>/src/<Project>.csproj`, the only project in `src`, built with the plugin SDK: `MokatermPluginId` is the
  folder's name, and the Mokaterm packages are referenced without a version (`Directory.Packages.props` holds it).
- `plugins/<id>/tests/` for test projects, if any. They run in every check.
- `plugins/<id>/listing.json` with what `plugin.json` does not say:

  ```json
  { "categories": ["security", "utilities"], "license": "MIT" }
  ```

Ours are listed as verified. Their ids start with `moka.`, and they may name their assembly `Moka.*` or
`Mokaterm.*`; the gallery keeps both for this folder.

## Releasing

Nothing is released while `mokaterm.json` carries a `ref`. Then the packages come from a Mokaterm checkout rather than
a published Mokaterm, and a release would spend a version number on a package for a Mokaterm nobody runs. In that
state every push to `main` still builds, tests, checks and loads every listed plugin, and the run's summary says that
nothing was published; the release and index jobs are skipped, and `gallery.py release` refuses to write to GitHub on
its own as well. Publishing starts once `mokaterm.json` names only a version on nuget.org:

```json
{ "version": "0.2.0" }
```

From then on, a version is published when it reaches `main` with a version number that has no release yet. So: raise `<Version>`
in the project, merge, and `publish.yml` builds it, releases `<id>-v<version>` and updates the index. A change merged
without a new version is checked in its pull request, which warns about it, and is not published.

To withdraw a version, add it to `gallery/withdrawn.json` (see `submitting.md`).

## Checking a pull request the way CI does

```
python build/gallery.py validate
python build/gallery.py check --all
```

`check` builds every listing a pull request touches (`--base origin/main`) or all of them (`--all`): ours whose
folder changed and community entries that are new or point at a new commit. A change to `build/`, `.github/`,
`mokaterm.json`, `nuget.config` or the `Directory.*.props` files builds everything. `--releases github:jacobwi/mokaterm-plugins`
compares against what is published; without it, `.work/releases` stands in for the Releases page.

## Testing the gallery scripts

```
python -m unittest discover -s build/tests
python build/e2e_local.py
```

The unit tests cover the package checks, the schema, the listings, the release metadata, the index and the signing.
`e2e_local.py` runs the whole flow on this machine in `.work/e2e`: it makes one plugin of ours and one community
plugin (a git repository with two versions and a tag) from Mokaterm's sample plugin and checks both. It plans once as
if `mokaterm.json` carried a ref (everything built, nothing to release, GitHub refused) and once as if it named a
published version, then builds and releases both into a folder, moves the community entry to its next version,
withdraws the first one, builds the index, signs it with a throwaway key and verifies it. It needs the first-time steps
above.

Every step can also be run by hand against that folder:

```
python build/gallery.py release --built <folder of built packages> --commit <40 hex> --releases dir:.work/releases
python build/gallery.py index --out .work/site --releases dir:.work/releases
python build/gallery.py keygen
python build/gallery.py sign --index .work/site/index.json --key .work/keys/gallery-test.pem --public .work/keys/gallery-test.pub.pem
```

A test key never signs anything the app trusts: the app only accepts `signing/gallery-2026.pub.pem`.

## How publishing runs

`.github/workflows/publish.yml` runs on every push to `main` and by hand:

1. **Plan** (read-only): gets the Mokaterm packages `mokaterm.json` names (from nuget.org, or packed from the ref),
   builds the load check, and lists the listings whose current version has no release. With a ref it lists every
   listing instead, reports `publish=false` and writes in the job summary that nothing is published.
2. **Build**, one job per plugin (read-only, no secrets): builds, tests, checks and loads it, and keeps the package.
3. **Release** (may write releases; only with `publish=true`): checks every package again from its bytes, takes the id, source and commit from
   this repository rather than from the build job, writes `<id>-<version>.json` and makes the release.
4. **Index** (the signing key and Pages; only with `publish=true`, only on `main`): rebuilds `index.json` from every release's metadata and
   `gallery/withdrawn.json`, signs it, verifies the signature against `signing/gallery-2026.pub.pem`, and deploys
   `index.json` and `index.json.sig` to GitHub Pages.

Plugin code only runs in the build jobs, which hold no secret and cannot write anything. The index is rebuilt from the
releases every time, so the repository never needs a commit from a bot, and deleting a release by hand takes its
version out of the next index.

The repository needs GitHub Pages with "GitHub Actions" as its source, and the repository secret `GALLERY_SIGNING_KEY`
(the PEM PKCS#8 Ed25519 private key whose public half is `signing/gallery-2026.pub.pem`). The index job is the only
place that reads the secret, and it runs only for `main`.
