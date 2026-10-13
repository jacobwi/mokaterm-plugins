"""Shared by the gallery script tests: puts build/ on the path and makes packages and metadata to test with."""

import io
import json
import os
import sys
import zipfile

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

SDK = "0.2"
RUNTIME = ["System.Runtime", "System.Text.Json", "System.Private.CoreLib", "netstandard", "mscorlib"]


def manifest(**changes):
    data = {
        "id": "acme.tools",
        "name": "Acme tools",
        "version": "1.2.0",
        "publisher": "Acme",
        "description": "Tools for things.",
        "sdk": SDK,
        "assembly": "Acme.Tools.dll",
        "hosts": ["desktop", "web"],
    }
    for key, value in changes.items():
        if value is None:
            data.pop(key, None)
        else:
            data[key] = value
    return data


def code_files(**manifest_changes):
    return {
        "plugin.json": json.dumps(manifest(**manifest_changes)).encode(),
        "lib/Acme.Tools.dll": b"MZ plugin",
        "lib/Acme.Tools.pdb": b"pdb",
        "lib/Acme.Tools.deps.json": b"{}",
        "lib/de/Acme.Tools.resources.dll": b"MZ satellite",
        "lib/YamlDotNet.dll": b"MZ private dependency",
        "lib/System.Security.Cryptography.Pkcs.dll": b"MZ not part of .NET itself",
        "wwwroot/Acme.Tools.bundle.scp.css": b".x{}",
        "wwwroot/js/tools.js": b"export {}",
        "themes/night.json": b"{}",
    }


def data_files(**manifest_changes):
    changes = {"assembly": None, **manifest_changes}
    return {
        "plugin.json": json.dumps(manifest(**changes)).encode(),
        "themes/night.json": b"{}",
        "themes/day.itermcolors": b"<plist/>",
    }


def write_package(folder, files, name="acme.tools-1.2.0.mokaplugin", method=zipfile.ZIP_DEFLATED):
    path = os.path.join(folder, name)
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w", method) as archive:
        for entry, data in files.items():
            archive.writestr(entry, data)
    with open(path, "wb") as file:
        file.write(buffer.getvalue())
    return path


def metadata(plugin_id="acme.tools", version="1.2.0", **changes):
    data = {
        "format": 1,
        "id": plugin_id,
        "version": version,
        "sdk": SDK,
        "name": "Acme tools",
        "publisher": "Acme",
        "description": "Tools for things.",
        "hosts": ["desktop", "web"],
        "hasCode": True,
        "assembly": "Acme.Tools.dll",
        "categories": ["utilities"],
        "license": "MIT",
        "source": "https://github.com/acme/tools",
        "commit": "a" * 40,
        "tag": None,
        "verified": False,
        "package": f"{plugin_id}-{version}.mokaplugin",
        "sha256": "b" * 64,
        "size": 1234,
        "published": "2026-10-13T09:00:00Z",
        "mokatermVersion": "0.2.0",
        "mokatermCommit": None,
        "moduleId": "acme-tools",
        "galleryCommit": "c" * 40,
        "url": f"https://github.com/jacobwi/mokaterm-plugins/releases/download/{plugin_id}-v{version}/{plugin_id}-{version}.mokaplugin",
    }
    data.update(changes)
    return data
