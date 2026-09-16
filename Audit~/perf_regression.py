#!/usr/bin/env python3
"""Reproduce nine historical alerts with longer fixed batches and both frozen references.

This supplements the unchanged full audit matrix. Every round and row is retained;
no success here grants Unity/device release approval.
"""
import argparse
import hashlib
import io
import json
import math
import os
from pathlib import Path
import shutil
import statistics
import subprocess
import tarfile
import tempfile
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parent.parent
REFERENCES = {
    "previous": "35c5b35da32fb6503771a7849d7436c1578976d4",
    "before": "364eac88e474bbf147d34b8df39c7e9ac9a3c573",
}
WATCHED = [("exact." + kind + ".d" + str(depth), 0)
           for depth in (1, 4, 8) for kind in ("empty", "miss")]
WATCHED += [("filter.exact", 128), ("filter.exact", 512), ("hierarchy.miss.d4", 8)]


def compare(reference, candidate):
    def collect(paths):
        data = {}
        for path in paths:
            payload = json.loads(path.read_text())
            if payload.get("passed") is not True:
                raise RuntimeError("Focused correctness failed: " + str(path))
            seen = set()
            for row in payload["rows"]:
                key = (row["name"], row["size"])
                if key in seen or len(row["ns"]) != 7 or any(not math.isfinite(x) or x <= 0 for x in row["ns"]):
                    raise RuntimeError("Duplicate row or invalid timing samples: " + str(path))
                seen.add(key)
                item = data.setdefault(key, {"medians": [], "samples": [], "bytes": [], "capacities": []})
                item["medians"].append(statistics.median(row["ns"]))
                item["samples"].append(row["ns"])
                item["bytes"].append(row["allocated_bytes"])
                item["capacities"].append(row["retained_capacity"])
            if not set(WATCHED) <= seen:
                raise RuntimeError("A round omitted a watched scenario: " + str(path))
        return data
    baseline, current = collect(reference), collect(candidate)
    if set(baseline) != set(current) or not set(WATCHED) <= set(current):
        raise RuntimeError("Missing or mismatched benchmark rows")
    rows = []
    for key in sorted(current):
        b, c = baseline[key], current[key]
        bn, cn = statistics.median(b["medians"]), statistics.median(c["medians"])
        if bn <= 0 or cn <= 0:
            raise RuntimeError("Invalid timing measurement")
        rows.append({"name": key[0], "size": key[1], "watched": key in WATCHED,
                     "reference_ns": bn, "candidate_ns": cn, "ratio": cn / bn,
                     "reference": b, "candidate": c})
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--output", default="../gameplaytags-performance")
    parser.add_argument("--rounds", type=int, default=6)
    args = parser.parse_args()
    if args.rounds < 2:
        parser.error("at least two alternating rounds are required")
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ, DOTNET_TieredCompilation="0", DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    def run(command, cwd, log):
        proc = subprocess.run(list(map(str, command)), cwd=cwd, env=env, text=True,
                              stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=300)
        (output / log).write_text(proc.stdout, encoding="utf-8")
        print(proc.stdout, flush=True)
        if proc.returncode:
            raise RuntimeError(log + " failed: " + str(proc.returncode))
    versions = [line.split()[0] for line in subprocess.check_output(
        [args.dotnet, "--list-sdks"], text=True).splitlines()
        if line.startswith("8.0.") and "-" not in line.split()[0]]
    if not versions:
        raise RuntimeError("A .NET 8 SDK is required")
    sdk = max(versions, key=lambda v: tuple(map(int, v.split("."))))
    head = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    with tempfile.TemporaryDirectory(prefix="gameplaytags-perf-nine-") as directory:
        work = Path(directory)
        (work / "global.json").write_text(json.dumps({"sdk": {"version": sdk, "rollForward": "disable"}}))
        sources = {}
        for name, ref in REFERENCES.items():
            source = work / (name + "-source")
            source.mkdir()
            archive = subprocess.check_output(["git", "archive", ref], cwd=ROOT)
            with tarfile.open(fileobj=io.BytesIO(archive)) as tar:
                tar.extractall(source, filter="data")
            sources[name] = source
        sources["candidate"] = ROOT
        builds, hashes = {}, {}
        for name, source in sources.items():
            host = work / name
            host.mkdir()
            for file in ("PerfRegression.cs", "UnityStubs.cs", "NuGet.Config"):
                shutil.copy2(ROOT / "Audit~" / file, host / file)
            hashes[name] = {str(p.relative_to(source)): hashlib.sha256(p.read_bytes()).hexdigest()
                            for p in sorted((source / "Runtime").glob("*.cs"))}
            project = host / "Perf.csproj"
            project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                '<OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>9.0</LangVersion>'
                '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize>'
                '</PropertyGroup><ItemGroup><Compile Include="' + escape(str(source / "Runtime/**/*.cs")) + '"/>'
                '<Compile Include="UnityStubs.cs"/><Compile Include="PerfRegression.cs"/></ItemGroup></Project>')
            run([args.dotnet, "build", project, "-c", "Release", "-o", host / "out"], host, name + "-build.log")
            builds[name] = (host / "out/Perf.dll", host)
        for number in range(args.rounds):
            order = list(builds) if number % 2 == 0 else list(reversed(builds))
            for name in order:
                dll, host = builds[name]
                run([args.dotnet, "exec", dll, output / f"{name}-{number}.json"], host, f"{name}-{number}.log")
    results = {}
    for name in REFERENCES:
        rows = compare([output / f"{name}-{i}.json" for i in range(args.rounds)],
                       [output / f"candidate-{i}.json" for i in range(args.rounds)])
        (output / (name + "-comparison.json")).write_text(json.dumps(rows, indent=2))
        results[name] = {"reference": REFERENCES[name],
            "watched": [{k: r[k] for k in ("name", "size", "reference_ns", "candidate_ns", "ratio")}
                        for r in rows if r["watched"]],
            "all_alerts_over_5_percent": [{"name": r["name"], "size": r["size"], "ratio": r["ratio"]}
                                         for r in rows if r["ratio"] > 1.05],
            "watched_alerts_over_5_percent": [{"name": r["name"], "size": r["size"], "ratio": r["ratio"]}
                                             for r in rows if r["watched"] and r["ratio"] > 1.05]}
    summary = {"head": head, "sdk": sdk, "rounds": args.rounds, "comparisons": results,
               "runtime_source_sha256": hashes, "release_approved": False,
               "note": "Longer fixed batches, six forward/reverse rounds by default. Raw seven-sample arrays, allocations and capacities retained. Shared-host timing alerts are not statistical tests or device approval."}
    (output / "summary.json").write_text(json.dumps(summary, indent=2))
    print(json.dumps(summary, indent=2), flush=True)


if __name__ == "__main__":
    main()
