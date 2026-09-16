#!/usr/bin/env python3
"""Run native tests in a NEW isolated Unity project; never edits a user's game project."""
import argparse
import datetime
import json
import os
import pathlib
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

from checks import source_digest, verify_package

ROOT = pathlib.Path(__file__).resolve().parent.parent


def verify_results(path, required):
    tree = ET.parse(path).getroot()
    cases = list(tree.iter("test-case"))
    if not cases or tree.get("result", "").lower() != "passed":
        raise RuntimeError("Unity did not report a passing, nonempty test run")
    if any(case.get("result", "").lower() != "passed" for case in cases):
        raise RuntimeError("Failed, skipped or inconclusive Unity cases are not release evidence")
    names = {case.get("name", "").split("(")[0] for case in cases}
    if not set(required).issubset(names):
        raise RuntimeError("Required test coverage is missing: " + str(set(required)-names))
    return len(cases), "\n".join((element.text or "") for element in tree.iter("output"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", default=os.environ.get("UNITY_EDITOR"))
    parser.add_argument("--project", required=True, help="New, disposable project directory (must not exist)")
    parser.add_argument("--output", required=True)
    parser.add_argument("--platform", choices=("EditMode", "PlayMode", "Android", "iOS"), default="EditMode")
    parser.add_argument("--test-framework", default="1.1.33")
    parser.add_argument("--settings", help="Optional Unity TestSettings JSON, e.g. signing settings; never logged")
    args = parser.parse_args()
    output = pathlib.Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    package = verify_package(ROOT)
    report = {"status": "blocked", "source_digest": package["source_digest"],
              "requested_platform": args.platform, "executed_tests": 0,
              "utc": datetime.datetime.now(datetime.timezone.utc).isoformat()}
    try:
        unity = pathlib.Path(args.unity).resolve() if args.unity else None
        if unity is None or not unity.is_file():
            raise RuntimeError("No Unity Editor executable supplied. Native tests were NOT run.")
        project = pathlib.Path(args.project).resolve()
        if project.exists() or ROOT == project or ROOT in project.parents or project in ROOT.parents:
            raise RuntimeError("Use a new disposable directory outside this repository")
        project.mkdir(parents=True)
        (project/"Assets").mkdir()
        (project/"ProjectSettings").mkdir()
        package_root = project/"Packages"/package["name"]
        shutil.copytree(ROOT, package_root, ignore=shutil.ignore_patterns(".git", "Audit~", "artifacts", "__pycache__"))
        (project/"Packages/manifest.json").write_text(json.dumps({
            "dependencies": {"com.unity.test-framework": args.test_framework},
            "testables": [package["name"]]}, indent=2), encoding="utf-8")
        (project/"GAMEPLAYTAGS_ACCEPTANCE_PROJECT").write_text(package["source_digest"], encoding="utf-8")

        def invoke(extra, log):
            command = [str(unity), "-batchmode", "-projectPath", str(project),
                       "-logFile", str(output/log), *extra]
            result = subprocess.run(command, timeout=3600)
            if result.returncode:
                raise RuntimeError(f"Unity exited {result.returncode}; inspect {log}")

        invoke(["-executeMethod", "GameplayTags.Tests.AcceptanceProjectSetup.Prepare", "-quit"], "prepare.log")
        prepare = (output/"prepare.log").read_text(encoding="utf-8", errors="replace")
        version = re.search(r"GAMEPLAYTAGS_UNITY_VERSION=(\d+\.\d+\.\S+)", prepare)
        if not version:
            raise RuntimeError("Unity preparation did not identify its version")
        report["unity_version"] = version[1]
        assembly = "GameplayTags.Tests" if args.platform == "EditMode" else "GameplayTags.Runtime.Tests"
        command = ["-runTests", "-testPlatform", args.platform, "-assemblyNames", assembly,
                   "-testResults", str(output/"tests.xml")]
        if args.platform in ("Android", "iOS"):
            settings = json.loads(pathlib.Path(args.settings).read_text()) if args.settings else {}
            settings["scriptingBackend"] = "IL2CPP"
            if args.platform == "Android":
                settings["architecture"] = 2
            settings_path = project/"TestSettings.json"
            settings_path.write_text(json.dumps(settings), encoding="utf-8")
            command += ["-testSettingsFile", str(settings_path)]
        invoke(command, "tests.log")  # No -quit: it can terminate the test runner before XML is written.
        required = (["StaticStateResetsWithDomainReloadDisabled", "SerializeReferenceAssetSurvivesDiskReload",
                     "NativeUndoRedoRestoresSettings", "NativeJsonRestoresSortedUniqueContainer"]
                    if args.platform == "EditMode" else
                    ["PlayerIdentity", "PlayerZeroAllocationWithPositiveControl",
                     "PlayerJsonAndRedirectResolution", "PlayerHierarchyFrozenQueryAndAliases"])
        count, captured = verify_results(output/"tests.xml", required)
        if source_digest(package_root) != report["source_digest"]:
            raise RuntimeError("Native project package changed during the run")
        report["executed_tests"] = count
        report["test_names_checked"] = required
        if args.platform != "EditMode":
            identity = re.search(r"GAMEPLAYTAGS_PLAYER=([^\r\n<]+)", captured)
            if not identity:
                raise RuntimeError("Missing actual Player platform/backend identity in test output")
            report["player_identity"] = identity[1]
            if args.platform in ("Android", "iOS"):
                platform = "Android" if args.platform == "Android" else "IPhonePlayer"
                if not identity[1].startswith(platform+";") or ";backend=IL2CPP;" not in identity[1] or ";pointerBits=64;" not in identity[1]:
                    raise RuntimeError("Test ran on the wrong Player/backend/architecture")
        report["status"] = "passed"
        report["performance_review"] = "not_run"  # These are semantic/allocation tests, NOT CPU/native-memory certification.
    except (OSError, RuntimeError, subprocess.TimeoutExpired, ET.ParseError, ValueError) as error:
        report["reason"] = str(error)
    (output/"native.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0 if report["status"] == "passed" else 2


if __name__ == "__main__":
    sys.exit(main())
