#!/usr/bin/env python3
"""Build a deterministic, allowlisted UPM candidate from the selected implementation.

Uses only the Python standard library and Git. Never publishes, changes the index,
executes benchmarks, or labels portable checks as Unity validation.
"""
from __future__ import annotations

import argparse
import gzip
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import tarfile

ROOT_FILES = {"package.json", "package.json.meta", "README.md", "README.md.meta",
              "README_CN.md", "README_CN.md.meta", "LICENSE", "CHANGELOG.md",
              "CHANGELOG.md.meta", "RELEASE.md", "RELEASE.md.meta",
              "Runtime.meta", "Editor.meta", "Tests.meta"}
ROOT_DIRS = {"Runtime", "Editor", "Samples~", "Tests", "Documentation~"}
REPOSITORY = "https://github.com/acced/ZFramework.GameplayTags"


def git(root: Path, *args: str) -> bytes:
    return subprocess.check_output(["git", "-C", str(root), *args], stderr=subprocess.PIPE)


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def tracked(root: Path) -> dict[str, tuple[str, str]]:
    result = {}
    for record in git(root, "ls-tree", "-r", "-z", "HEAD").split(b"\0"):
        if not record:
            continue
        header, name = record.split(b"\t", 1)
        mode, kind, oid = header.decode("ascii").split()
        path = name.decode("utf-8")
        if kind != "blob" or mode not in {"100644", "100755"}:
            raise ValueError("Unsupported tracked entry: " + path)
        result[path] = (mode, oid)
    return result


def checked_read(root: Path, name: str, oid: str) -> bytes:
    path = root / name
    if path.is_symlink() or not path.is_file():
        raise ValueError("Missing or symlinked input: " + name)
    # A symlinked parent must not redirect reads outside the checkout either.
    if path.resolve() != (root.resolve() / name):
        raise ValueError("Redirected input: " + name)
    data = path.read_bytes()
    blob = hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data).hexdigest()
    if blob != oid:
        raise ValueError("Uncommitted change in release input: " + name)
    return data


def selected(name: str) -> bool:
    path = PurePosixPath(name)
    if path.is_absolute() or ".." in path.parts:
        raise ValueError("Unsafe path: " + name)
    return name in ROOT_FILES or (len(path.parts) > 1 and path.parts[0] in ROOT_DIRS)


def rewrite_doc(name: str, data: bytes, names: set[str], commit: str) -> bytes:
    """Keep packaged documentation links usable without shipping experimental trees."""
    if not name.endswith(".md"):
        return data
    import posixpath
    text = data.decode("utf-8").replace("./LICENSE.md", "./LICENSE")
    def replace(match: re.Match[str]) -> str:
        target = match.group(1)
        if target.startswith(("#", "http:", "https:", "mailto:")) or "://" in target:
            return match.group(0)
        clean, separator, anchor = target.partition("#")
        resolved = posixpath.normpath(posixpath.join(posixpath.dirname(name), clean))
        exists = resolved in names or any(n.startswith(resolved.rstrip("/") + "/") for n in names)
        if exists or resolved.startswith("../"):
            return match.group(0)
        url = REPOSITORY + "/blob/" + commit + "/" + resolved
        return "](" + url + (separator + anchor if separator else "") + ")"
    return re.sub(r"\]\(([^\s)]+)\)", replace, text).encode("utf-8")


def build(root: Path, output: Path) -> dict:
    root = root.resolve()
    entries = tracked(root)
    policy_name = "Tools~/Release/selection.json"
    policy = json.loads(checked_read(root, policy_name, entries[policy_name][1]))
    commit = git(root, "rev-parse", "HEAD").decode().strip()
    for folder, expected in policy["frozen_trees"].items():
        actual = git(root, "rev-parse", "HEAD:" + folder).decode().strip()
        if actual != expected:
            raise ValueError("Selected implementation changed: " + folder)
        # Unity would import untracked files in these folders, so do not ignore them.
        disk = {p.relative_to(root).as_posix() for p in (root / folder).rglob("*") if p.is_file() or p.is_symlink()}
        if disk != {p for p in entries if p.startswith(folder + "/")}:
            raise ValueError("Unexpected files under frozen tree: " + folder)
    evidence = policy["evidence_commit"]
    if git(root, "diff", "--name-only", evidence, "HEAD", "--", "Tests~").strip():
        raise ValueError("Historical evidence was changed")
    names = {n for n in entries if selected(n)}
    required = {"package.json", "LICENSE", "CHANGELOG.md", "RELEASE.md",
                "Tests/Runtime/GameplayTags.Tests.asmdef", "Tests/Runtime/ReleaseContractTests.cs"}
    if not required <= names:
        raise ValueError("Required package files missing: " + repr(sorted(required - names)))
    source = {n: checked_read(root, n, entries[n][1]) for n in sorted(names)}
    package = json.loads(source["package.json"])
    if package["name"] != "zframework.gameplaytag" or package["version"] != policy["version"]:
        raise ValueError("Package identity differs from release selection")
    if not re.fullmatch(r"\d+\.\d+\.\d+-rc\.\d+", package["version"]):
        raise ValueError("This gate produces RCs only; stable publication requires a separate reviewed gate")
    if package.get("unity") != "2021.3" or package.get("dependencies", {}):
        raise ValueError("Compatibility floor/dependencies changed")
    guids = {}
    for name, data in source.items():
        if name.endswith(".meta"):
            match = re.search(rb"^guid: ([0-9a-f]{32})\s*$", data, re.M)
            if match:
                guid = match[1].decode()
                if guid in guids:
                    raise ValueError("Duplicate Unity GUID: " + name + " / " + guids[guid])
                guids[guid] = name
        if name.endswith((".cs", ".asmdef")) and PurePosixPath(name).parts[0] in {"Runtime", "Editor", "Tests"}:
            if name + ".meta" not in names:
                raise ValueError("Missing Unity metadata: " + name)
    test_asm = json.loads(source["Tests/Runtime/GameplayTags.Tests.asmdef"])
    if test_asm["name"] != "GameplayTags.Tests" or "TestAssemblies" not in test_asm["optionalUnityReferences"]:
        raise ValueError("Tests must remain an opt-in Unity test assembly")
    for prefix in ("Runtime", "Editor"):
        asm = json.loads(next(v for k, v in source.items() if k.startswith(prefix + "/") and k.endswith(".asmdef")))
        if asm.get("allowUnsafeCode") or any("Test" in r for r in asm.get("references", [])):
            raise ValueError("Production assembly depends on test/unsafe code")
    payload = {n: rewrite_doc(n, data, names, commit) for n, data in source.items()}
    manifest = {"schema": 1, "package": package["name"], "version": package["version"],
                "source_commit": commit, "implementation_commit": policy["implementation_commit"],
                "frozen_trees": policy["frozen_trees"], "status": "release-candidate",
                "unity_validation": "not-run", "performance_acceptance": "not-established",
                "documentation_transform": "Fix license links; pin omitted evidence links to source commit",
                "files": {n: {"source_sha256": sha256(source[n]), "package_sha256": sha256(data)}
                          for n, data in payload.items()}}
    manifest_bytes = (json.dumps(manifest, ensure_ascii=False, sort_keys=True, indent=2) + "\n").encode()
    payload["Documentation~/PACKAGE_MANIFEST.json"] = manifest_bytes
    output.mkdir(parents=True, exist_ok=False)
    filename = package["name"] + "-" + package["version"] + ".tgz"
    archive = output / filename
    with archive.open("xb") as stream, gzip.GzipFile(filename="", mode="wb", fileobj=stream, mtime=0) as gz:
        with tarfile.open(fileobj=gz, mode="w", format=tarfile.USTAR_FORMAT) as tar:
            for name, data in sorted(payload.items()):
                item = tarfile.TarInfo("package/" + name)
                item.size = len(data)
                item.mode = 0o644
                item.mtime = 0
                item.uid = item.gid = 0
                item.uname = item.gname = ""
                tar.addfile(item, io.BytesIO(data))
    # Read back every byte and path, not just the gzip checksum.
    with tarfile.open(archive, "r:gz") as tar:
        actual = {m.name: tar.extractfile(m).read() for m in tar.getmembers() if m.isfile()}
    if actual != {"package/" + n: b for n, b in payload.items()}:
        raise ValueError("Archive readback differs")
    digest = sha256(archive.read_bytes())
    (output / (filename + ".sha256")).write_text(digest + "  " + filename + "\n", encoding="utf-8")
    (output / "manifest.json").write_bytes(manifest_bytes)
    summary = {"status": "PASS", "source_commit": commit, "files": len(payload),
               "archive": filename, "sha256": digest, "bytes": archive.stat().st_size,
               "unity_validation": "not-run", "performance_acceptance": "not-established"}
    (output / "qualification.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    return summary


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--output", type=Path, required=True, help="New output directory; existing output is never overwritten")
    args = parser.parse_args()
    try:
        print(json.dumps(build(args.root, args.output), indent=2))
    except (ValueError, OSError, KeyError, subprocess.CalledProcessError) as error:
        parser.exit(1, "RELEASE GATE FAILED: " + str(error) + "\n")
