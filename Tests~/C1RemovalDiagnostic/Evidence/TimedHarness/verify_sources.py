#!/usr/bin/env python3
"""Read-only, archive-portable proof of the exact two-source C1-removal delta."""
import argparse
import difflib
import hashlib
import json
from pathlib import Path
import subprocess

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
SIGNATURE = b"        internal static void CopyValidated(GameplayTagContainer source, GameplayTagContainer result)\n"


def sha(data):
    return hashlib.sha256(data).hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def files(root):
    require(root.is_dir(), "Missing Runtime: " + str(root))
    paths = sorted(root.rglob("*"))
    require(not any(p.is_symlink() for p in paths), "Symlink in Runtime: " + str(root))
    return {p.relative_to(root).as_posix(): p.read_bytes() for p in paths if p.is_file()}


def hashes(contents):
    return {name: sha(data) for name, data in sorted(contents.items())}


def method_span(data):
    """The pinned method contains no brace-bearing strings or comments."""
    require(data.count(SIGNATURE) == 1, "Expected exactly one CopyValidated signature")
    start = data.index(b"{", data.index(SIGNATURE) + len(SIGNATURE))
    depth = 0
    for end in range(start, len(data)):
        token = data[end:end + 1]
        if token == b"{":
            depth += 1
        elif token == b"}":
            depth -= 1
            if depth == 0:
                return start, end + 1
    raise ValueError("Unbalanced CopyValidated body")


def inventory():
    pins = json.loads((HERE / "source_pins.json").read_text())
    require(set(pins["versions"]) == {"R", "RMinusC1"}, "Exactly two versions are required")
    contents = {}
    result = {}
    for version, entry in pins["versions"].items():
        root = (REPO / entry["runtime_path"]).resolve()
        require(root == HERE / "Sources" / version / "Runtime", "Unexpected source path: " + version)
        contents[version] = files(root)
        actual = hashes(contents[version])
        require(actual == entry["files"], "Source pin mismatch: " + version)
        result[version] = {"runtime_path": str(root), "source_id": entry["source_id"], "files": actual}
    require(files(REPO / "Runtime") == contents["R"], "Production Runtime differs from pinned R")
    baseline = files(REPO / pins["historical_baseline_path"])
    require(hashes(baseline) == pins["historical_baseline_files"], "Historical PR4 baseline changed")
    name = pins["changed_file"]
    require(name == "GameplayTagContainerAlgorithms.cs", "Unexpected changed file")
    require(pins["changed_method"] == "CopyValidated", "Unexpected changed method")
    r = contents["R"]
    minus = contents["RMinusC1"]
    require(set(r) == set(minus), "Runtime file sets differ")
    require([n for n in sorted(r) if r[n] != minus[n]] == [name], "Delta must affect exactly one file")
    start, end = method_span(r[name])
    old_start, old_end = method_span(baseline[name])
    old_body = baseline[name][old_start:old_end]
    require(minus[name] == r[name][:start] + old_body + r[name][end:],
            "RMinusC1 must replace only CopyValidated's body with exact PR4 bytes")
    require(pins["method_body_sha256"] == {"R": sha(r[name][start:end]), "RMinusC1": sha(old_body)},
            "Method body hashes differ")
    patch = "".join(difflib.unified_diff(
        r[name].decode().splitlines(keepends=True), minus[name].decode().splitlines(keepends=True),
        fromfile="R/Runtime/" + name, tofile="RMinusC1/Runtime/" + name))
    require((HERE / "CopyValidated-removal.patch").read_text() == patch, "Recorded exact delta changed")
    return result


def history_proof():
    """Optional git-object check; default inventory works without git history."""
    inventory()
    pins = json.loads((HERE / "source_pins.json").read_text())

    def git(*args):
        return subprocess.check_output(["git", *args], cwd=REPO)

    reference = pins["reference_commit"]
    historical = pins["removed_method_origin_commit"]
    paths = git("ls-tree", "-r", "--name-only", reference, "Runtime").decode().splitlines()
    exact_r = {p[len("Runtime/"):]: git("show", reference + ":" + p) for p in paths}
    require(files(HERE / "Sources/R/Runtime") == exact_r, "R does not equal its historical Runtime tree")
    baseline = files(REPO / pins["historical_baseline_path"])
    for name, data in baseline.items():
        require(data == git("show", historical + ":Runtime/" + name), "PR4 differs from git: " + name)
    reference_tree = git("rev-parse", reference + ":Runtime").decode().strip()
    historical_tree = git("rev-parse", historical + ":Runtime").decode().strip()
    require(reference_tree == pins["reference_runtime_tree"], "Reference git tree differs")
    require(historical_tree == pins["historical_runtime_tree"], "Historical git tree differs")
    return {
        "status": "PASS",
        "reference_commit": reference,
        "reference_runtime_tree": reference_tree,
        "reference_runtime_file_count": len(exact_r),
        "R_equals_git_reference_runtime": True,
        "production_Runtime_equals_R": True,
        "historical_commit": historical,
        "historical_runtime_tree": historical_tree,
        "historical_PR4_baseline_files_verified_against_git": sorted(baseline),
        "RMinusC1_method_body_equals_exact_historical_CopyValidated": True,
        "only_method_body_changed": True,
        "C2_and_all_other_Runtime_bytes_unchanged": True,
        "changed_file": pins["changed_file"],
        "changed_method": pins["changed_method"],
        "method_body_sha256": pins["method_body_sha256"],
        "builds_or_harness_runs_performed_by_source_verification": False,
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--history", action="store_true", help="also require exact historical git objects")
    args = parser.parse_args()
    print(json.dumps(history_proof() if args.history else inventory(), indent=2))
