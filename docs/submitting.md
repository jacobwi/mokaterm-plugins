# Listing a plugin in the gallery

The gallery lists plugins that Mokaterm's Plugins page can install. Nothing in it is a binary someone uploaded: every
package is built here, from public source at one exact commit, and the index the app reads is signed. This page is for
authors of community plugins. Our own plugins live under `plugins/` and are described in `building.md`.

## What your repository needs

- A public repository on GitHub with an open source license file at its root (`LICENSE`, `LICENSE.md` or `COPYING`).
- A plugin project made with the Mokaterm plugin SDK, the way `dotnet new mokaterm-plugin` makes one: it references
  `Mokaterm.UI.Common` (unless it carries only themes) and `Mokaterm.Plugin.Sdk`.
- A plugin id with a publisher part, such as `acme.proxmox`: lower-case letters and digits joined by dots or dashes, at
  most 64 characters, with at least one dot. Ids starting with `moka.` or `mokaterm.` are ours. `MokatermPluginId` must
  be exactly the id you list.
- `MokatermPluginName`, `MokatermPluginPublisher` and `MokatermPluginDescription` set, since the Browse tab shows all
  three, and `MokatermPluginHosts` if the plugin only works on the desktop or only on the web host.
- A version of the form `1.2.0` or `1.2.0-beta.1`. Build metadata (`+abc`) is not taken, because the version names a
  release and a file.
- An assembly name of your own: not starting with `Mokaterm.` or `Moka.`, which are kept for Mokaterm and its own
  plugins, and not used by another listed plugin. It also names `_content/<assembly>/`, where your page files are
  served. Set `AssemblyName` if the project's name does not fit.
- A module id (`ModuleInfo.Id`) that neither Mokaterm nor another listed plugin uses. Settings must be under
  `plugin.<your id>`, as the app requires.

## How the gallery builds it

The build runs on `ubuntu-latest` with the .NET 10 SDK, in a fresh clone of your repository at the commit you list:

```
dotnet build <project> -c Release -p:MokatermPluginPackOnBuild=true ...
```

- **Mokaterm packages.** The gallery builds every plugin against the Mokaterm it currently lists for (`mokaterm.json`
  here), whatever version your project names. It replaces the version of `Mokaterm.Abstractions`,
  `Mokaterm.UI.Common` and `Mokaterm.Plugin.Sdk` at build time, with central package management or without, so a
  project made by the template builds unchanged. Your plugin has to compile against that version; one written for
  Mokaterm 0.2 builds against any 0.2.x. Do not use `VersionOverride` for these three packages.
- **Other packages** come from nuget.org only. Your `nuget.config` is not used, so a private feed does not work.
- **Your build files are used as they are**: `Directory.Build.props`, `Directory.Packages.props`, analyzers and
  warnings as errors all apply. Nothing above your repository's root reaches the build.
- Submodules are fetched if you have them. Nothing in the build may need a network login or a secret.
- One entry is one project. A repository with several plugins lists each one in its own entry.
- Your test projects do not run. The gallery builds the plugin project and checks and loads what it makes; testing
  the plugin is your repository's job.

You can run the same build yourself: clone this repository beside a Mokaterm checkout, add your entry under `gallery/`
and run `python build/gallery.py build <your id>` after the steps in `building.md`.

## The entry

One file per plugin, `gallery/<id>.json`, added or changed by a pull request:

```json
{
  "id": "acme.proxmox",
  "source": "https://github.com/acme/mokaterm-proxmox",
  "commit": "9f2c4e1b7a0d3c5e8f6a1b2c3d4e5f60718293a4",
  "tag": "v1.2.0",
  "project": "src/Acme.Proxmox/Acme.Proxmox.csproj",
  "license": "MIT",
  "categories": ["server-tools"]
}
```

| Field | What it is |
| --- | --- |
| `id` | The plugin id, also the file's name. |
| `source` | The repository, as `https://github.com/owner/name`, with no `.git` and no slash at the end. |
| `commit` | The full 40-character commit to build. |
| `tag` | Optional, for people reading the entry. When it is there, it has to point at `commit`. |
| `project` | The plugin's `.csproj` inside the repository, with forward slashes. |
| `license` | An SPDX identifier, or a simple expression such as `MIT OR Apache-2.0`. |
| `categories` | One or more of `protocols`, `server-tools`, `docker`, `utilities`, `security`, `themes`. |

`gallery/schema.json` is the exact rule; editors that read JSON Schema check the file as you type if you add
`"$schema": "./schema.json"`.

## What is checked

The pull request runs `.github/workflows/check.yml`, which:

1. Checks the entry against the schema, that the id is not taken by one of ours or by a plugin listed from another
   repository, and that the file is named after the id.
2. Clones your repository at the commit, checks that the tag points there and that there is a license file.
3. Builds the project as above.
4. Checks the package the way the app reads one: only `plugin.json`, `lib`, `wwwroot` and `themes` at the top, no path
   that climbs out or is rooted, the app's size limits (64 MiB file, 64 MiB a file, 128 MiB unpacked, 4096 entries),
   the id, a SemVer version, publisher and description present, `sdk` equal to the Mokaterm the gallery builds for,
   an assembly name not starting with `Mokaterm.` or `Moka.`, and nothing in `lib` that Mokaterm ships (`Mokaterm.*`, `Moka.Red.*`, `Microsoft.AspNetCore.*`,
   `Microsoft.Extensions.*`, `Microsoft.JSInterop` and .NET's own assemblies). Themes must sit straight in `themes/`,
   at most 64 of them and 512 KiB each.
5. Loads the package through Mokaterm's own plugin loader, for every host it names: from a web host's operator folder
   and, for the desktop, installed and trusted through the Plugins page's own code and loaded at the next start. The
   plugin has to come up enabled, with its module id free, its settings under its own name and every theme readable.
6. Refuses a version that is already published from another commit.

The checks are there so a review can concentrate on what matters. They do not replace it.

## The review

A code plugin runs inside Mokaterm with Mokaterm's own access: the vault while it is unlocked, every open session, the
user's files. So a maintainer reads the source at the listed commit before merging: all of it for a new plugin, the
diff since the last listed commit for an update. The review looks at what the plugin sends over the network and where,
what it reads and writes on disk, how it handles credentials, any process it starts, native libraries, anything
obfuscated or binary in the repository, and what runs at build time (MSBuild targets and props, source generators,
analyzers). New dependencies are looked at too. A theme pack runs nothing and gets a lighter look.

A pull request is merged only when the check passes and the review is done. Merging is what publishes: the
`publish.yml` workflow builds the version again on main, releases it on this repository's Releases page and adds it
to the signed index.

Before Mokaterm 0.2 is released, the gallery builds against Mokaterm's main branch (`mokaterm.json` carries a `ref`).
In that time every listed plugin is built, checked and loaded on each merge, but nothing is released and no index is
published, so no version number is spent on a package for an unreleased Mokaterm. Entries merged then are published
by the first run after `mokaterm.json` names the released version.

## Updates

Change `commit` (and `tag`) in your entry to a commit whose plugin carries a new version, and open a pull request.
After the merge the new version is published next to the old ones; the app offers the newest one built for the
Mokaterm it is running. A commit that is already published, or a version number that is already published from
another commit, is refused: one version number is one package.

Changing only `categories` needs no new version.

Moving the plugin to another repository is a change of hands as far as the gallery can tell, so the check fails on
it: an id stays with the repository it was first published from. Say in the pull request why the plugin moved; a
maintainer confirms it is the same author (for example from the old repository pointing at the new one) and merges it
over the failing check. After that the new repository is the plugin's source.

When Mokaterm moves to a new minor version (0.2 to 0.3), plugins need a new version built for it. The gallery never
rebuilds a version that is already published.

## Withdrawing a version

Add the version to `gallery/withdrawn.json` with a sentence the app shows:

```json
[
  { "id": "acme.proxmox", "version": "1.2.0", "reason": "Stops the wrong container when two share a name. Use 1.2.1." }
]
```

The app stops offering a withdrawn version and warns people who have it installed. The package stays downloadable,
so the warning can name what they have. For a security problem, report it privately through this repository's
Security tab first rather than in a public pull request.
