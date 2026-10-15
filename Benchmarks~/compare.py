#!/usr/bin/env python3
"""Compare matching benchmark JSON reports without applying performance gates."""
import argparse
import csv
import json
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("baseline", type=Path)
parser.add_argument("rewrite", type=Path)
parser.add_argument("--csv", type=Path)
args = parser.parse_args()
baseline = json.loads(args.baseline.read_text())
rewrite = json.loads(args.rewrite.read_text())
for field in ("Mode", "Samples", "Framework", "OS", "Architecture", "TieredCompilation", "ServerGC",
              "RegisteredLeafUniverse", "QuerySequenceLength", "StopwatchFrequency"):
    if baseline[field] != rewrite[field]:
        raise SystemExit("Reports use different measurement configurations: " + field)
if baseline["Checksum"] != rewrite["Checksum"]:
    raise SystemExit("Observed benchmark checksums differ; investigate before interpreting timings")

def key(row):
    return row["ExplicitTagCount"], row["HierarchyDepth"], row["Operation"]

old = {key(row): row for row in baseline["Measurements"]}
new = {key(row): row for row in rewrite["Measurements"]}
if old.keys() != new.keys():
    raise SystemExit("Reports contain different benchmark cases")

rows = []
print("| Explicit | Depth | Operation | Unit | Baseline ns | Rewrite ns | Baseline / rewrite | Old B/op | New B/op |")
print("| ---: | ---: | --- | --- | ---: | ---: | ---: | ---: | ---: |")
for identity in old:
    a, b = old[identity], new[identity]
    if a["Unit"] != b["Unit"] or a["IterationsPerSample"] != b["IterationsPerSample"]:
        raise SystemExit("Benchmark unit or iteration count differs for " + str(identity))
    ratio = a["MedianNanoseconds"] / b["MedianNanoseconds"]
    record = {
        "explicit_tags": identity[0], "depth": identity[1], "operation": identity[2], "unit": a["Unit"],
        "baseline_ns": a["MedianNanoseconds"], "rewrite_ns": b["MedianNanoseconds"],
        "baseline_over_rewrite": ratio,
        "baseline_allocated_bytes": a["AllocatedBytesPerOperation"], "rewrite_allocated_bytes": b["AllocatedBytesPerOperation"],
        "baseline_min_ns": a["MinNanoseconds"], "baseline_max_ns": a["MaxNanoseconds"],
        "rewrite_min_ns": b["MinNanoseconds"], "rewrite_max_ns": b["MaxNanoseconds"]
    }
    rows.append(record)
    print(f"| {identity[0]} | {identity[1]} | {identity[2]} | {a['Unit']} | {a['MedianNanoseconds']:.3f} | "
          f"{b['MedianNanoseconds']:.3f} | {ratio:.2f}x | {a['AllocatedBytesPerOperation']:.0f} | {b['AllocatedBytesPerOperation']:.0f} |")

print("\nRetained live memory (median of three GC snapshots):\n")
print("| Explicit | Depth | Baseline B/container | Rewrite B/container | Rewrite / baseline |")
print("| ---: | ---: | ---: | ---: | ---: |")
old_memory = {(r["ExplicitTagCount"], r["HierarchyDepth"]): r for r in baseline["RetainedMemory"]}
for b in rewrite["RetainedMemory"]:
    identity = b["ExplicitTagCount"], b["HierarchyDepth"]
    a = old_memory[identity]
    old_bytes, new_bytes = a["MedianRetainedBytesPerContainer"], b["MedianRetainedBytesPerContainer"]
    print(f"| {identity[0]} | {identity[1]} | {old_bytes:.1f} | {new_bytes:.1f} | {new_bytes / old_bytes:.2f}x |")

if args.csv:
    args.csv.parent.mkdir(parents=True, exist_ok=True)
    with args.csv.open("w", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]), lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)
