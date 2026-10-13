"""Makes the Mokaterm packages plugins build against available to this repository.

mokaterm.json says which Mokaterm to build against:

    { "version": "0.2.0" }                       the packages published on nuget.org
    { "version": "0.1.9-dev", "ref": "main" }    packages built here from a Mokaterm checkout at that ref

Either way this writes .feed/mokaterm.props, which Directory.Packages.props imports for the exact version, and puts
Mokaterm.Abstractions, Mokaterm.UI.Common and Mokaterm.Plugin.Sdk in .feed, the one source nuget.config takes Mokaterm.*
packages from. Without a ref they are downloaded from nuget.org at that version. With a ref they are packed from the
checkout, stamped "<version>.<UTC time>" so a rebuild after a change in Mokaterm is a new version: NuGet keeps the first
package it saw for a version in its cache for good.

The checkout is --source, else MOKATERM_SOURCE, else ../Mokaterm beside this repository. The build goes to .work so the
checkout's own bin and obj are left alone.

A plugin built against a version records its major.minor, and Mokaterm only loads plugins built for its own, so the
packages must come from the Mokaterm the plugins are meant for.
"""

import argparse
import datetime
import json
import os
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FEED = os.path.join(ROOT, ".feed")
WORK = os.path.join(ROOT, ".work", "mokaterm-build")
# Restores go here rather than into the user's NuGet cache, the same folder nuget.config gives the plugins.
NUGET_PACKAGES = os.path.join(ROOT, ".work", "packages")
PACKAGES = [
    "src/Mokaterm.Abstractions/Mokaterm.Abstractions.csproj",
    "src/UI/Mokaterm.UI.Common/Mokaterm.UI.Common.csproj",
    "src/Mokaterm.Plugin.Sdk/Mokaterm.Plugin.Sdk.csproj",
]
PACKAGE_IDS = ["Mokaterm.Abstractions", "Mokaterm.UI.Common", "Mokaterm.Plugin.Sdk"]
NUGET_ORG = "https://api.nuget.org/v3-flatcontainer"


def read_config():
    with open(os.path.join(ROOT, "mokaterm.json"), encoding="utf-8") as file:
        config = json.load(file)
    version = config.get("version")
    if not isinstance(version, str) or not version.strip():
        sys.exit("mokaterm.json needs a version.")
    ref = config.get("ref")
    return version.strip(), ref.strip() if isinstance(ref, str) and ref.strip() else None


def write_props(version):
    os.makedirs(FEED, exist_ok=True)
    props = (
        "<Project>\r\n"
        "\t<!-- Written by build/mokaterm_feed.py from mokaterm.json; do not edit. -->\r\n"
        "\t<PropertyGroup>\r\n"
        f"\t\t<MokatermVersion>{version}</MokatermVersion>\r\n"
        "\t</PropertyGroup>\r\n"
        "</Project>\r\n"
    )
    with open(os.path.join(FEED, "mokaterm.props"), "w", encoding="utf-8", newline="") as file:
        file.write(props)


def clear_feed():
    # Old packages of other versions would only pile up; NuGet takes the exact version the props name.
    os.makedirs(FEED, exist_ok=True)
    for name in os.listdir(FEED):
        if name.endswith(".nupkg"):
            os.remove(os.path.join(FEED, name))


def download(version):
    clear_feed()
    for package in PACKAGE_IDS:
        name = f"{package.lower()}.{version.lower()}.nupkg"
        url = f"{NUGET_ORG}/{package.lower()}/{version.lower()}/{name}"
        print(f"Downloading {url}", flush=True)
        data = None
        for attempt in range(4):
            try:
                with urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": "mokaterm-plugins"}), timeout=120) as response:
                    data = response.read()
                break
            except urllib.error.HTTPError as error:
                if error.code == 404:
                    sys.exit(f"{package} {version} is not on nuget.org. Is mokaterm.json's version released, or does it need a ref?")
                if error.code < 500 and error.code != 429:
                    sys.exit(f"nuget.org answered {error.code} for {url}.")
            except (urllib.error.URLError, TimeoutError, ConnectionError):
                pass
            time.sleep(2 ** attempt)
        if data is None:
            sys.exit(f"Could not download {url}.")
        # NuGet checks the package's own nuget.org signature when it restores it from the folder.
        with open(os.path.join(FEED, f"{package}.{version}.nupkg"), "wb") as file:
            file.write(data)


def pack(source, version):
    for project in PACKAGES:
        path = os.path.join(source, *project.split("/"))
        if not os.path.isfile(path):
            sys.exit(f"{project} is not in {source}; is that a Mokaterm checkout at the right ref?")

    clear_feed()

    for project in PACKAGES:
        command = [
            "dotnet", "pack", os.path.join(source, *project.split("/")),
            "--configuration", "Release",
            "--output", FEED,
            "-nologo",
            f"-p:Version={version}",
            "-p:UseArtifactsOutput=true",
            f"-p:ArtifactsPath={WORK}",
            f"-p:RestorePackagesPath={NUGET_PACKAGES}",
            "-nodeReuse:false",
        ]
        print(" ".join(command), flush=True)
        subprocess.run(command, check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--source", help="a Mokaterm checkout (default MOKATERM_SOURCE, else ../Mokaterm)")
    args = parser.parse_args()

    version, ref = read_config()
    if ref is None:
        download(version)
        write_props(version)
        print(f"Building against Mokaterm {version} from nuget.org.")
        return

    source = args.source or os.environ.get("MOKATERM_SOURCE") or os.path.join(os.path.dirname(ROOT), "Mokaterm")
    source = os.path.abspath(source)
    if not os.path.isdir(source):
        sys.exit(f"No Mokaterm checkout at {source}. Pass --source or set MOKATERM_SOURCE.")

    stamped = f"{version}.{datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S')}"
    shutil.rmtree(WORK, ignore_errors=True)
    pack(source, stamped)
    write_props(stamped)
    print(f"Building against Mokaterm {stamped}, packed from {source} (mokaterm.json names ref {ref}).")


if __name__ == "__main__":
    main()
