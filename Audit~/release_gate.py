#!/usr/bin/env python3
"""Fail closed when release evidence is missing. Does not modify GitHub settings or publish anything."""
import argparse
import json
import pathlib
import sys

from checks import source_digest

ROOT = pathlib.Path(__file__).resolve().parent.parent


def evaluate(managed, native, review, digest):
    missing = []
    if managed.get("managed_checks") != "passed" or managed.get("source_digest") != digest:
        missing.append("Passing managed checks for this source digest")
    def passed(platform, prefix=None):
        return any(r.get("status") == "passed" and r.get("source_digest") == digest and
                   r.get("requested_platform") == platform and r.get("executed_tests", 0) > 0 and
                   (prefix is None or r.get("unity_version", "").startswith(prefix)) for r in native)
    for platform, prefix in (("EditMode", "2021.3."), ("EditMode", "6000."), ("PlayMode", None),
                             ("Android", None), ("iOS", None)):
        if not passed(platform, prefix):
            missing.append("Native " + platform + (" / Unity " + prefix if prefix else ""))
    if not (review.get("source_digest") == digest and review.get("status") == "approved" and
            review.get("reviewer") and review.get("target_reports") and review.get("rationale")):
        missing.append("Reviewed target-device CPU/memory comparisons and explicit regression decisions")
    return {"source_digest": digest, "release_approved": not missing, "missing": missing}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--managed", required=True, help="Managed summary.json")
    parser.add_argument("--native", action="append", default=[], help="native.json (repeat for each platform)")
    parser.add_argument("--performance-review", help="Human-reviewed JSON; format is documented in RELEASE.md")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    report = None
    try:
        read = lambda path: json.loads(pathlib.Path(path).read_text(encoding="utf-8"))
        report = evaluate(read(args.managed), [read(path) for path in args.native],
                          read(args.performance_review) if args.performance_review else {}, source_digest(ROOT))
    except (OSError, ValueError) as error:
        report = {"release_approved": False, "missing": [str(error)]}
    path = pathlib.Path(args.output)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0 if report["release_approved"] else 2


if __name__ == "__main__":
    sys.exit(main())
