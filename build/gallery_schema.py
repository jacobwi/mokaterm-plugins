"""Checks JSON against gallery/schema.json.

The standard library has no JSON Schema validator and the scripts install nothing, so this reads the part of JSON Schema
the gallery's schema uses. A keyword it does not know is an error rather than something skipped, so the schema can
never say more than is checked.
"""

import re

from gallery_common import SCHEMA, GalleryError, read_json

_ANNOTATIONS = {"$schema", "$id", "$comment", "title", "description", "examples", "default"}
_KEYWORDS = _ANNOTATIONS | {
    "$ref", "$defs", "type", "required", "properties", "additionalProperties", "pattern", "minLength", "maxLength",
    "enum", "const", "items", "minItems", "maxItems", "uniqueItems",
}
_TYPES = {
    "object": lambda value: isinstance(value, dict),
    "array": lambda value: isinstance(value, list),
    "string": lambda value: isinstance(value, str),
    "integer": lambda value: isinstance(value, int) and not isinstance(value, bool),
    "number": lambda value: isinstance(value, (int, float)) and not isinstance(value, bool),
    "boolean": lambda value: isinstance(value, bool),
    "null": lambda value: value is None,
}

_cache = {}


def load(path=SCHEMA):
    if path not in _cache:
        schema = read_json(path)
        _check_keywords(schema, "#")
        _cache[path] = schema
    return _cache[path]


def problems(value, pointer="#", path=SCHEMA):
    """Every way value breaks the schema at pointer ("#" for an entry, "#/$defs/listing", ...), as sentences."""
    root = load(path)
    found = []
    _validate(root, _resolve(root, pointer), value, "", found)
    return found


def _check_keywords(schema, where):
    if not isinstance(schema, dict):
        raise GalleryError(f"gallery/schema.json: {where} is not a schema object.")
    unknown = set(schema) - _KEYWORDS
    if unknown:
        raise GalleryError(f"gallery/schema.json uses {', '.join(sorted(unknown))} at {where}, which build/gallery_schema.py does not check.")
    for name in ("properties", "$defs"):
        for key, child in schema.get(name, {}).items():
            _check_keywords(child, f"{where}/{name}/{key}")
    if "items" in schema:
        _check_keywords(schema["items"], f"{where}/items")


def _resolve(root, pointer):
    if pointer == "#":
        return root
    if not pointer.startswith("#/"):
        raise GalleryError(f"Only references inside gallery/schema.json are read, not {pointer}.")
    node = root
    for part in pointer[2:].split("/"):
        if not isinstance(node, dict) or part not in node:
            raise GalleryError(f"gallery/schema.json has nothing at {pointer}.")
        node = node[part]
    return node


def _validate(root, schema, value, where, found):
    if "$ref" in schema:
        _validate(root, _resolve(root, schema["$ref"]), value, where, found)
    label = where or "the file"

    if "type" in schema:
        types = schema["type"] if isinstance(schema["type"], list) else [schema["type"]]
        if not any(_TYPES[name](value) for name in types):
            found.append(f"{label} must be {' or '.join(types)}.")
            return

    if "const" in schema and value != schema["const"]:
        found.append(f"{label} must be {schema['const']!r}.")
    if "enum" in schema and value not in schema["enum"]:
        found.append(f"{label} is {value!r}; it must be one of {', '.join(map(str, schema['enum']))}.")

    if isinstance(value, str):
        if "minLength" in schema and len(value) < schema["minLength"]:
            found.append(f"{label} is shorter than {schema['minLength']} characters.")
        if "maxLength" in schema and len(value) > schema["maxLength"]:
            found.append(f"{label} is longer than {schema['maxLength']} characters.")
        if "pattern" in schema and not _matches(schema["pattern"], value):
            found.append(f"{label} ({_short(value)}) does not have the form the gallery takes.")

    if isinstance(value, list):
        if "minItems" in schema and len(value) < schema["minItems"]:
            found.append(f"{label} needs at least {schema['minItems']} item(s).")
        if "maxItems" in schema and len(value) > schema["maxItems"]:
            found.append(f"{label} holds more than {schema['maxItems']} items.")
        if schema.get("uniqueItems") and len({_hashable(item) for item in value}) != len(value):
            found.append(f"{label} names something twice.")
        if "items" in schema:
            for index, item in enumerate(value):
                _validate(root, schema["items"], item, f"{where}[{index}]" if where else f"item {index}", found)

    if isinstance(value, dict):
        for name in schema.get("required", []):
            if name not in value:
                found.append(f"{_member(where, name)} is missing.")
        properties = schema.get("properties", {})
        for name, item in value.items():
            if name in properties:
                _validate(root, properties[name], item, _member(where, name), found)
            elif schema.get("additionalProperties") is False:
                found.append(f"{_member(where, name)} is not a field the gallery knows.")


# JSON Schema patterns are unanchored ECMA-262 regexes. The gallery's patterns are written to mean the same in Python,
# except that Python's $ also matches before a final newline, which ECMA's does not.
def _matches(pattern, value):
    if pattern.endswith("$") and value.endswith("\n"):
        return False
    return re.search(pattern, value) is not None


def _member(where, name):
    return f"{where}.{name}" if where else name


def _hashable(value):
    if isinstance(value, (dict, list)):
        return repr(value)
    return value


def _short(value):
    return repr(value if len(value) <= 60 else value[:60] + "...")
