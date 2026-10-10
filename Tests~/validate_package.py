#!/usr/bin/env python3
"""Check Unity package metadata without importing or modifying the package.

Run from any directory with Python 3. Hidden paths, Unity's ~ folders, and local
.NET build files/outputs are not package assets. Imported asset GUIDs must remain
stable across commits: this checker never creates or regenerates them.
"""
from pathlib import Path
import re
import sys


REPOSITORY = Path(__file__).resolve().parent.parent
BUILD_DIRECTORIES = {"artifacts", "bin", "obj", "Build", "Builds"}
BUILD_EXTENSIONS = {".csproj", ".sln", ".slnx", ".props", ".targets", ".user", ".suo"}


def ignored(path):
    parts = path.relative_to(REPOSITORY).parts
    return any(part.startswith(".") or part.endswith("~") or part in BUILD_DIRECTORIES for part in parts) or path.suffix in BUILD_EXTENSIONS


def asset_paths():
    return sorted((path for path in REPOSITORY.rglob("*")
                   if not ignored(path) and path.suffix != ".meta"), key=lambda path: path.as_posix())


def importer_for(path):
    if path.is_dir():
        return "DefaultImporter"
    if path.suffix == ".cs":
        return "MonoImporter"
    if path.suffix == ".asmdef":
        return "AssemblyDefinitionImporter"
    if path.suffix in {".md", ".json", ".txt"}:
        return "TextScriptImporter"
    return "DefaultImporter"


def main():
    assets = asset_paths()
    errors = []
    guids = {}
    for asset in assets:
        meta = Path(str(asset) + ".meta")
        relative = meta.relative_to(REPOSITORY).as_posix()
        if not meta.is_file():
            errors.append("Missing: " + relative)
            continue
        text = meta.read_text(encoding="utf-8")
        matches = re.findall(r"^guid: ([0-9a-f]{32})$", text, re.MULTILINE)
        if len(matches) != 1 or matches[0] == "0" * 32:
            errors.append("Invalid GUID: " + relative)
        elif matches[0] in guids:
            errors.append("Duplicate GUID: " + relative + " and " + guids[matches[0]])
        else:
            guids[matches[0]] = relative
        if not text.startswith("fileFormatVersion: 2\n"):
            errors.append("Invalid metadata version: " + relative)
        if not re.search(r"^" + importer_for(asset) + r":$", text, re.MULTILINE):
            errors.append("Wrong importer: " + relative)
        if asset.is_dir() and not re.search(r"^folderAsset: yes$", text, re.MULTILINE):
            errors.append("Missing folderAsset: " + relative)
        if asset.suffix == ".cs" and "  serializedVersion: 2\n" not in text:
            errors.append("Missing MonoImporter serializedVersion: " + relative)
    for meta in REPOSITORY.rglob("*.meta"):
        if not ignored(meta) and not meta.with_suffix("").exists():
            errors.append("Orphan: " + meta.relative_to(REPOSITORY).as_posix())
    if errors:
        print("Unity package metadata validation failed:")
        for error in errors:
            print("  " + error)
        return 1
    print(f"Unity package metadata passed: {len(assets)} imported assets, {len(guids)} unique GUIDs, 0 missing or orphan metadata files.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
