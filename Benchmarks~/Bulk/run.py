#!/usr/bin/env python3
"""Compile identical bulk workloads against selected Runtime trees and alternate samples.

Uses the installed .NET 8 SDK's Roslyn directly, without MSBuild, NuGet or Unity.
The default is 14 independent processes per variant. Each process records one
sample per case; before/after process order reverses on odd rounds.
"""
import argparse
import csv
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import statistics
import subprocess
import sys
from datetime import datetime, timezone


def version_key(path):
    return tuple(int(part) for part in path.name.split("-")[0].split(".") if part.isdigit())


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def command_output(command):
    try:
        result = subprocess.run(command, check=True, text=True, capture_output=True)
        return result.stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        return None


def manifest(root, paths):
    files = {path.relative_to(root).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
             for path in sorted(paths)}
    definition = "UTF-8 lines sorted by relative path: <path>\\0<SHA256 lowercase hex>\\n"
    combined = "".join(path + "\0" + digest + "\n" for path, digest in sorted(files.items()))
    return {"Definition": definition, "CombinedSHA256": hashlib.sha256(combined.encode("utf-8")).hexdigest(), "Files": files}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--before", required=True, type=Path, help="frozen before Runtime checkout/archive")
    parser.add_argument("--after", type=Path, help="after Runtime checkout; omit for baseline-only collection")
    parser.add_argument("--candidate", action="append", default=[], metavar="LABEL=PATH",
                        help="additional selection candidate; same harness and process rounds")
    parser.add_argument("--dotnet", default=shutil.which("dotnet"), help="installed .NET host")
    parser.add_argument("--samples", type=int, default=14, help="independent samples per variant (default: 14)")
    parser.add_argument("--out", required=True, type=Path, help="NEW result directory; existing results are never overwritten")
    parser.add_argument("--quick", action="store_true", help="divide loop lengths by eight for smoke testing")
    parser.add_argument("--filter", help="case ID substring, e.g. n1024, copy, or depth128")
    parser.add_argument("--baseline-commit", default="3f10ba49433d8b5fdd394a7ac2f1dce32800fd7c",
                        help="documented before revision; per-file hashes remain authoritative")
    args = parser.parse_args()
    repository = Path(__file__).resolve().parents[2]
    workload_directory = Path(__file__).resolve().parent
    result_directory = args.out.resolve()
    if args.samples < 1:
        parser.error("--samples must be positive")
    if result_directory.exists() and any(result_directory.iterdir()):
        parser.error("--out must be absent or empty: raw samples must not be overwritten")
    if not args.dotnet:
        parser.error("pass --dotnet /path/to/dotnet or install the .NET 8 SDK")
    variants = {"before": args.before.resolve()}
    if args.after:
        variants["after"] = args.after.resolve()
    for candidate in args.candidate:
        if "=" not in candidate:
            parser.error("--candidate must be LABEL=PATH")
        label, path = candidate.split("=", 1)
        if not label or not all(c.isalnum() or c in "_-" for c in label) or label in variants:
            parser.error("candidate labels must be unique alphanumeric/underscore/hyphen names")
        variants[label] = Path(path).resolve()
    for label, source in variants.items():
        if not (source / "Runtime/GameplayTagContainer.cs").is_file():
            parser.error(label + " lacks Runtime/GameplayTagContainer.cs")

    dotnet = Path(args.dotnet).resolve()
    sdk_root = dotnet.parent
    sdks = sorted((sdk_root / "sdk").glob("*"), key=version_key)
    packs = sorted((sdk_root / "packs/Microsoft.NETCore.App.Ref").glob("8.*"), key=version_key)
    runtimes = sorted((sdk_root / "shared/Microsoft.NETCore.App").glob("8.*"), key=version_key)
    if not sdks or not packs or not runtimes:
        parser.error("installed SDK plus .NET 8 runtime and reference pack required")
    csc = sdks[-1] / "Roslyn/bincore/csc.dll"
    reference_files = sorted((packs[-1] / "ref/net8.0").glob("*.dll"))
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    build_directory = repository / "artifacts/bulk-benchmarks" / stamp
    result_directory.mkdir(parents=True, exist_ok=True)
    workload_files = [workload_directory / name for name in ("Program.cs", "GameplayTags.BulkBenchmarks.csproj", "run.py")]
    workload_manifest = manifest(repository, workload_files)
    source_files = {label: [path for path in sorted((source / "Runtime").glob("**/*.cs"))
                            if path.name != "GameObjectGameplayTagContainer.cs"] for label, source in variants.items()}
    source_manifests = {label: manifest(source, sorted((source / "Runtime").glob("**/*.cs")))
                        for label, source in variants.items()}
    assemblies = {}
    compile_commands = []
    for label, source in variants.items():
        directory = build_directory / label
        directory.mkdir(parents=True, exist_ok=True)
        assembly = directory / "GameplayTags.BulkBenchmarks.dll"
        flags = ["-nologo", "-optimize+", "-langversion:9.0", "-nullable:disable", "-deterministic+", "-target:exe",
                 '-out:"' + str(assembly) + '"']
        flags += ['-r:"' + str(path) + '"' for path in reference_files]
        flags += ['"' + str(path) + '"' for path in source_files[label] + [workload_directory / "Program.cs"]]
        response = directory / "compile.rsp"
        response.write_text("\n".join(flags), encoding="utf-8")
        compile_command = [str(dotnet), str(csc), "@" + str(response)]
        subprocess.run(compile_command, check=True)
        compile_commands.append(compile_command)
        options = {"tfm": "net8.0", "framework": {"name": "Microsoft.NETCore.App", "version": runtimes[-1].name},
                   "configProperties": {"System.Runtime.TieredCompilation": False, "System.GC.Server": False}}
        write_json(assembly.with_suffix(".runtimeconfig.json"), {"runtimeOptions": options})
        assemblies[label] = assembly
        print("Compiled " + label + " from " + str(source), flush=True)

    cpu = command_output(["lscpu", "--json"])
    try:
        cpu = json.loads(cpu) if cpu else None
    except json.JSONDecodeError:
        pass
    environment = {
        "Schema": "gameplaytags-bulk-comparison-v1", "StartedUTC": stamp,
        "Purpose": "Same-machine 3f10ba4 bulk implementation comparison; not Unity or Alex reproduction",
        "BaselineCommit": args.baseline_commit, "SamplesPerVariant": args.samples,
        "Sampling": "One process per variant per round; forward variant order on even rounds, reverse on odd; identical rotating case order within round",
        "Unit": "one public API call, except remove_add_after_copy is one RemoveTag+AddTag pair; row Unit is authoritative",
        "Framework": ".NET " + runtimes[-1].name, "SDK": sdks[-1].name, "ReferencePack": packs[-1].name,
        "Architecture": platform.machine(), "Platform": platform.platform(), "CPU": cpu,
        "VisibleCPUCount": os.cpu_count(), "TieredCompilation": False, "ServerGC": False,
        "Quick": args.quick, "Filter": args.filter, "Command": [sys.executable, *sys.argv],
        "CompileCommands": compile_commands, "CompileFlags": ["optimize+", "langversion:9.0", "nullable:disable", "deterministic+"],
        "ExcludedRuntimeFiles": ["Runtime/GameObjectGameplayTagContainer.cs"],
        "WorkloadManifest": workload_manifest,
        "Variants": {label: {"Source": str(source), "SourceManifest": source_manifests[label],
                              "AssemblySHA256": hashlib.sha256(assemblies[label].read_bytes()).hexdigest()}
                     for label, source in variants.items()},
        "RunCommands": [], "RoundOrder": []
    }
    write_json(result_directory / "environment.json", environment)
    samples = {label: [] for label in variants}
    labels = list(variants)
    for round_index in range(args.samples):
        order = labels if round_index % 2 == 0 else list(reversed(labels))
        environment["RoundOrder"].append({"Round": round_index, "Variants": order})
        for label in order:
            output = result_directory / "raw" / (label + "-" + str(round_index).zfill(2) + ".json")
            command = [str(dotnet), str(assemblies[label]), "--variant", label, "--round", str(round_index), "--out", str(output)]
            if args.quick:
                command.append("--quick")
            if args.filter:
                command += ["--filter", args.filter]
            subprocess.run(command, check=True)
            environment["RunCommands"].append(command)
            samples[label].append(json.loads(output.read_text(encoding="utf-8")))
        print("Completed paired round " + str(round_index + 1) + "/" + str(args.samples) + ": " + ", ".join(order), flush=True)
        write_json(result_directory / "environment.json", environment)

    if manifest(repository, workload_files) != workload_manifest:
        raise RuntimeError("Workload files changed during sampling; retain raw files but do not publish this comparison")
    for label, source in variants.items():
        if manifest(source, sorted((source / "Runtime").glob("**/*.cs"))) != source_manifests[label]:
            raise RuntimeError(label + " Runtime sources changed during sampling; raw files retained for diagnosis")
    summaries = validate_and_summarize(samples)
    for label, summary in summaries.items():
        write_json(result_directory / (label + ".json"), summary)
    compare_and_write(result_directory, summaries)
    environment["CompletedUTC"] = datetime.now(timezone.utc).isoformat()
    environment["Validation"] = "All per-case name/closure/input/checksum checks passed; paired fixture metadata and source hashes match"
    write_json(result_directory / "environment.json", environment)
    print("Saved validated raw samples, medians, comparison and source manifests to " + str(result_directory), flush=True)


REPORT_KEYS = ("Schema", "Framework", "Architecture", "OperatingSystem", "ServerGC", "TieredCompilation", "Quick",
               "RegistryLeafCount", "RegistryTagCount", "RegistryShallowGroupCount", "StopwatchFrequency")
ROW_KEYS = ("Id", "Operation", "Unit", "ExplicitTagCount", "LeftExplicitTagCount", "RightExplicitTagCount",
            "LeftTotalTagCount", "RightTotalTagCount", "ResultExplicitTagCount", "ResultTotalTagCount", "HierarchyDepth",
            "OverlapPercent", "CapacityMultiplier", "RequestedCapacity", "LeftKind", "SameInstance", "Iterations",
            "ObservedChecksum", "ExplicitSetChecksum", "ClosureSetChecksum", "AlternateExplicitSetChecksum",
            "LeftInputChecksum", "RightInputChecksum", "ProbeChecksum")


def validate_and_summarize(samples):
    reference = samples["before"][0]
    reference_rows = {row["Id"]: row for row in reference["Measurements"]}
    before_by_round = {sample["Round"]: sample for sample in samples["before"]}
    summaries = {}
    for label, reports in samples.items():
        rows_by_id = {case_id: [] for case_id in reference_rows}
        for report in reports:
            for key in REPORT_KEYS:
                if report[key] != reference[key]:
                    raise RuntimeError("Configuration mismatch " + label + ": " + key)
            if report["ObservedChecksum"] != before_by_round[report["Round"]]["ObservedChecksum"]:
                raise RuntimeError("Whole-process observed checksum differs in round " + str(report["Round"]))
            rows = {row["Id"]: row for row in report["Measurements"]}
            if set(rows) != set(reference_rows):
                raise RuntimeError("Case IDs differ in " + label)
            for case_id, row in rows.items():
                for key in ROW_KEYS:
                    if row[key] != reference_rows[case_id][key]:
                        raise RuntimeError("Fixture/checksum mismatch " + label + " " + case_id + ": " + key)
                rows_by_id[case_id].append(row)
        aggregate_rows = []
        for case_id, rows in sorted(rows_by_id.items()):
            aggregate = {key: rows[0][key] for key in ROW_KEYS}
            times = [row["NanosecondsPerOperation"] for row in rows]
            allocations = [row["AllocatedBytesPerOperation"] for row in rows]
            aggregate.update({"Samples": len(rows), "MedianNanosecondsPerOperation": statistics.median(times),
                              "MinNanosecondsPerOperation": min(times), "MaxNanosecondsPerOperation": max(times),
                              "MedianAllocatedBytesPerOperation": statistics.median(allocations),
                              "NanosecondsPerOperationSamples": times, "AllocatedBytesPerOperationSamples": allocations,
                              "Gen0CollectionsSamples": [row["Gen0Collections"] for row in rows],
                              "Gen1CollectionsSamples": [row["Gen1Collections"] for row in rows],
                              "Gen2CollectionsSamples": [row["Gen2Collections"] for row in rows]})
            aggregate_rows.append(aggregate)
        summaries[label] = {**{key: reference[key] for key in REPORT_KEYS}, "Variant": label,
                            "SamplesPerCase": len(reports), "MedianDefinition": "arithmetic mean of two center samples when N is even",
                            "Measurements": aggregate_rows}
    return summaries


def compare_and_write(directory, summaries):
    before = {row["Id"]: row for row in summaries["before"]["Measurements"]}
    columns = ["Variant", "Id", "Operation", "Unit", "ExplicitTagCount", "HierarchyDepth", "OverlapPercent", "ResultExplicitTagCount",
               "ResultTotalTagCount", "BeforeMedianUs", "AfterMedianUs", "BeforeOverAfter", "BeforeMedianBytes", "AfterMedianBytes", "Samples"]
    markdown = ["# Bulk-operation paired measurements", "",
                "Standalone .NET 8, same workload, one public API call per operation; remove_add_after_copy reports one RemoveTag+AddTag pair. This does not reproduce Unity Editor or an unavailable Alex implementation.", "",
                "Median of independent process samples. Raw samples, fixture checksums and source hashes are alongside this table. Fresh-result allocation is included; reused outputs are preallocated and warmed.", "",
                "| Variant | Case | Before µs | After µs | Before / after | Before B/op | After B/op |", "|---|---|---:|---:|---:|---:|---:|"]
    with (directory / "comparison.csv").open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=columns)
        writer.writeheader()
        for label, summary in summaries.items():
            if label == "before":
                continue
            for row in summary["Measurements"]:
                prior = before[row["Id"]]
                prior_ns = prior["MedianNanosecondsPerOperation"]
                after_ns = row["MedianNanosecondsPerOperation"]
                ratio = prior_ns / after_ns
                output = {"Variant": label, "Id": row["Id"], "Operation": row["Operation"], "Unit": row["Unit"],
                          "ExplicitTagCount": row["ExplicitTagCount"], "HierarchyDepth": row["HierarchyDepth"],
                          "OverlapPercent": row["OverlapPercent"], "ResultExplicitTagCount": row["ResultExplicitTagCount"],
                          "ResultTotalTagCount": row["ResultTotalTagCount"], "BeforeMedianUs": prior_ns / 1000,
                          "AfterMedianUs": after_ns / 1000, "BeforeOverAfter": ratio,
                          "BeforeMedianBytes": prior["MedianAllocatedBytesPerOperation"],
                          "AfterMedianBytes": row["MedianAllocatedBytesPerOperation"], "Samples": row["Samples"]}
                writer.writerow(output)
                markdown.append("| " + label + " | `" + row["Id"] + "` | " + format(prior_ns / 1000, ".3f") +
                                " | " + format(after_ns / 1000, ".3f") + " | " + format(ratio, ".2f") +
                                "× | " + format(prior["MedianAllocatedBytesPerOperation"], ".0f") + " | " +
                                format(row["MedianAllocatedBytesPerOperation"], ".0f") + " |")
    (directory / "comparison.md").write_text("\n".join(markdown) + "\n", encoding="utf-8")


if __name__ == "__main__":
    try:
        main()
    except subprocess.CalledProcessError as error:
        sys.exit(error.returncode if error.returncode > 0 else 1)
