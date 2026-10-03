#!/usr/bin/env python3
"""Verify and recompute PR26 historical statistics using only archived evidence.

Run with Python 3.9+ and no third-party packages:
    python3 reanalyze.py
    python3 reanalyze.py --json

The default checks every inventory entry, every manifest-bound raw process file,
and every field of every historical statistical summary. Archives are read in
memory and are never extracted. Original absolute paths in manifests are data,
not filesystem locations to open. This script never runs a benchmark, invokes a
historical analyzer, imports project code, or writes files.

Formula references: benchmark/analyze.py, analyze_factor.py,
analyze_bridge_factor.py, and diagnostics/sorted-history/analyze.py. The formulas
and raw-data consistency checks are restated explicitly below. Historical DLLs,
compiler bindings, dependencies and source trees are deliberately not required:
their recorded hashes can be compared internally, but their provenance cannot
be revalidated here. A successful result is NOT a new performance run, a claim
that a diagnostic prerequisite passed, or validation of omitted build inputs.
"""

import argparse
import csv
import hashlib
import io
import json
import math
from pathlib import Path, PurePosixPath
import statistics
import sys
import tarfile


FINAL_NAMES = tuple(
    "final-" + group + "-" + backend
    for group in ("query", "builder", "mixed")
    for backend in ("default", "nohw")
)
FACTOR_NAMES = (
    "bridge-split-factor-default", "bridge-split-factor-nohw",
    "factor-v3-default", "factor-v3-nohw",
)
DIAGNOSTIC = "benchmark/diagnostics/sorted-history/results"
TARGET = "builder/cluster/4096/build/sorted/same/0"
VERDICTS = (
    "clear-improvement", "clear-regression", "uncertain-or-small", "invalid-sample",
)
BUDGET = 64 * 1024 * 1024
EXPECTED_ROWS = {"query": 37, "builder": 30, "mixed": 75}
MATCH_FIELDS = (
    "id", "group", "shape", "count", "peer_count", "flavor", "op", "peer",
    "iterations", "unit", "input_hash", "peer_hash", "input_length",
    "peer_input_length", "expected_checksum_per_iteration", "output_count",
    "left_layout", "right_layout", "output_layout", "input_payload_bytes",
    "peer_payload_bytes", "reserved_capacity", "output_payload_bytes",
)
BRIDGE_MATCH_FIELDS = (
    "id", "group", "op", "count", "case_name", "iterations", "unit",
    "input_hash", "expected_checksum_per_iteration", "query_nodes", "query_ranges",
    "legacy_storage", "direct_storage", "direct_reserved_capacity", "direct_payload_bytes",
)
RUNTIME_FIELDS = (
    "layout", "group", "runtime", "architecture", "vector_accelerated",
    "vector_uint_lanes", "avx2", "tiered_compilation", "hw_intrinsic", "gc_latency",
    "gc_server", "ready_to_run", "tiered_pgo", "gc_concurrent", "registry_count",
)
BRIDGE_RUNTIME_FIELDS = (
    "layout", "group", "runtime", "architecture", "registry_count",
    "vector_accelerated", "avx2", "gc_latency",
)
LIMITATION = (
    "Statistics and archived raw-evidence integrity only. Historical omitted DLLs, "
    "source trees, dependencies, generated harnesses and compiler/source bindings "
    "are NOT revalidated. Recorded identity checks establish internal consistency "
    "only. Inventory hashes are not an independent authenticity attestation. "
    "No benchmark was run. Single-host x64 .NET evidence does not establish Unity, "
    "Mono, IL2CPP, Burst or ARM64/device performance."
)
median = statistics.median


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def load_json(data, label):
    """Reject ambiguous duplicate object keys and nonstandard NaN/Infinity."""
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, label + ": duplicate JSON key " + key)
            result[key] = value
        return result

    def invalid_constant(value):
        raise ValueError(label + ": nonfinite JSON constant " + value)

    return json.loads(data, object_pairs_hook=pairs, parse_constant=invalid_constant)


def safe_relative(value, label):
    require(isinstance(value, str) and value and "\\" not in value,
            label + ": invalid relative path")
    parts = value.split("/")
    require(not value.startswith("/") and all(p not in ("", ".", "..") for p in parts),
            label + ": unsafe relative path " + value)
    return value


class ArchivedEvidence:
    """Load archive members into memory; no extract(), subprocess or path fallback."""

    def __init__(self, directory):
        self.directory = directory.resolve()
        inventory = load_json((self.directory / "inventory.json").read_bytes(), "inventory.json")
        files = inventory["files"]
        require(isinstance(files, dict) and files, "Empty inventory")
        archives = inventory.get("archives", {})
        grouped = {}
        for original, entry in files.items():
            safe_relative(original, "Original key")
            archive = safe_relative(entry["archive"], "Archive")
            require("/" not in archive and archive.endswith(".tar.gz"), "Unexpected archive name")
            member = safe_relative(entry["member"], "Member")
            grouped.setdefault(archive, {})
            require(member not in grouped[archive], "Duplicate inventory member: " + member)
            grouped[archive][member] = (original, entry)
        if archives:
            require(set(archives) == set(grouped), "Archive inventory coverage differs")
        self.data = {}
        self.total_bytes = 0
        for archive_name, members in grouped.items():
            archive_path = self.directory / archive_name
            require(not archive_path.is_symlink(), "Archive must not be a symlink")
            blob = archive_path.read_bytes()
            if archives:
                meta = archives[archive_name]
                require(len(blob) == meta["size"], "Archive size mismatch: " + archive_name)
                require(digest(blob) == meta["sha256"], "Archive hash mismatch: " + archive_name)
                require(len(members) == meta["file_count"], "Archive file count mismatch")
            seen = set()
            with tarfile.open(fileobj=io.BytesIO(blob), mode="r:gz") as archive:
                for member in archive:
                    safe_relative(member.name, "Tar member")
                    require(member.isfile(), "Nonregular archive member: " + member.name)
                    require(member.name in members, "Uninventoried archive member: " + member.name)
                    require(member.name not in seen, "Duplicate tar member: " + member.name)
                    seen.add(member.name)
                    original, entry = members[member.name]
                    require(member.size == entry["size"], "Member size mismatch: " + original)
                    stream = archive.extractfile(member)
                    require(stream is not None, "Unreadable archive member: " + original)
                    with stream:
                        content = stream.read()
                    require(len(content) == entry["size"], "Content size mismatch: " + original)
                    require(digest(content) == entry["sha256"], "Content hash mismatch: " + original)
                    self.data[original] = content
                    self.total_bytes += len(content)
            require(seen == set(members), "Missing archive members: " + archive_name)
            if archives and "uncompressed_file_bytes" in archives[archive_name]:
                require(sum(entry["size"] for _, entry in members.values())
                        == archives[archive_name]["uncompressed_file_bytes"],
                        "Archive uncompressed size total differs: " + archive_name)
        require(set(self.data) == set(files), "Incomplete archive inventory")
        self.archive_count = len(grouped)
        self.verified_raw_files = 0
        self.verified_processes = 0

    def read(self, key):
        require(key in self.data, "Required archived evidence missing: " + key)
        return self.data[key]

    def json(self, key):
        return load_json(self.read(key), key)

    def manifest_runs(self, root, manifest):
        require(manifest.get("completed_utc"), root + ": incomplete manifest")
        seen = set()
        for meta in manifest["runs"]:
            label = safe_relative(meta["label"], "Run label")
            require("/" not in label and label not in seen, root + ": duplicate/invalid run label")
            seen.add(label)
            require(meta["returncode"] == 0, root + ": historical failed process " + label)
            for extension, field in (("json", "result_sha256"), ("stdout", "stdout_sha256")):
                key = root + "/" + label + "." + extension
                require(digest(self.read(key)) == meta[field], "Manifest raw hash mismatch: " + key)
                self.verified_raw_files += 1
            if "stderr_sha256" in meta:
                key = root + "/" + label + ".stderr"
                require(digest(self.read(key)) == meta["stderr_sha256"], "Manifest stderr hash mismatch: " + key)
                self.verified_raw_files += 1
            data = self.json(root + "/" + label + ".json")
            require(data == self.json(root + "/" + label + ".stdout"),
                    root + "/" + label + ": JSON/stdout disagreement")
            if "dll_sha256" in meta:
                require(meta["dll_sha256"] == manifest["identities"][meta["variant"]]["dll_sha256"],
                        "Recorded host identity differs from manifest")
            self.verified_processes += 1
            yield meta, data


def percentile(values, probability):
    values = sorted(values)
    position = (len(values) - 1) * probability
    low = int(position)
    high = min(low + 1, len(values) - 1)
    return values[low] + (values[high] - values[low]) * (position - low)


def check_row(row):
    samples = row["samples"]
    require(len(samples) == 7, "Expected seven samples")
    require([s["sample"] for s in samples] == list(range(7)), "Sample duplication/order")
    require(type(row["iterations"]) is int and row["iterations"] > 0, "Invalid iteration count")
    valid = True
    for sample in samples:
        for field in ("elapsed_ms", "ns_per_op", "bytes_per_op", "total_bytes"):
            value = sample[field]
            require(type(value) in (float, int) and math.isfinite(value), "Invalid numeric " + field)
            require(value > 0 if field in ("elapsed_ms", "ns_per_op") else value >= 0,
                    "Invalid sign: " + field)
        require(math.isclose(sample["ns_per_op"], sample["elapsed_ms"] * 1e6 / row["iterations"], rel_tol=1e-10),
                "Elapsed/iteration disagreement")
        require(math.isclose(sample["bytes_per_op"], sample["total_bytes"] / row["iterations"], rel_tol=1e-10),
                "Allocation/iteration disagreement")
        require(len(sample["gcs"]) == 3 and all(type(x) is int and x >= 0 for x in sample["gcs"]),
                "Invalid collection count")
        if row["op"] not in ("build", "charged-build-union64", "fresh-micro-union"):
            require(sample["total_bytes"] == 0, "Prepared allocation violation")
        valid &= sample["elapsed_ms"] >= 1 and sample["total_bytes"] <= BUDGET
        require(type(sample["valid"]) is bool and sample["valid"] == (sample["elapsed_ms"] >= 1),
                "Sample validity flag differs")
    require(type(row["valid"]) is bool and row["valid"] == all(s["elapsed_ms"] >= 1 for s in samples),
            "Row validity flag differs")
    return valid


def expected_keys(group):
    out = set()

    def add(shape, operation, count, flavor, peer="same", peer_count=0):
        out.add(f"{group}/{shape}/{count}/{operation}/{flavor}/{peer}/{peer_count}")

    if group == "query":
        for n in (0, 1, 2, 8, 32, 64):
            flavors = ("miss-low", "miss-high") if n == 0 else (
                "hit-first", "hit-middle", "hit-last", "miss-bit", "miss-low", "miss-high", "balanced")
            for flavor in flavors:
                add("scatter", "query64", n, flavor)
    elif group == "builder":
        for n in (0, 1, 2):
            for flavor in ("sorted", "shuffled"):
                add("scatter", "build", n, flavor)
        for n in (8, 64, 4096):
            for shape in ("cluster", "scatter"):
                for flavor in ("sorted", "shuffled", "reversed", "duplicates"):
                    add(shape, "build", n, flavor)
    elif group == "mixed":
        for n in (8, 4096):
            for shape in ("cluster", "scatter"):
                for peer in ("same", "opposite"):
                    for operation in ("union", "restore-append", "restore-remove", "copy"):
                        add(shape, operation, n, "equal", peer, n)
        for shape in ("cluster", "scatter"):
            for peer in ("same", "opposite"):
                for operation in ("union", "restore-append", "restore-remove"):
                    for reverse in (False, True):
                        add(shape, operation, 8 if reverse else 4096,
                            "small-large" if reverse else "large-small", peer, 4096 if reverse else 8)
        for peer in ("same", "opposite"):
            for operation in ("union", "restore-append", "restore-remove", "copy"):
                add("random", operation, 4096, "equal", peer, 4096)
        add("random", "charged-build-union64", 4096, "shuffled", "same", 4096)
        for flavor in ("clean", "dirty"):
            for n in (0, 8, 4096):
                add("scatter", "empty-copy", n, flavor)
        for n in (8, 4096):
            for shape in ("cluster", "scatter"):
                add(shape, "charged-build-union64", n, "shuffled", "same", n)
    else:
        raise ValueError("Unexpected broad cohort group: " + group)
    return out


def check_recorded_identity(data, identity):
    """Only compare recorded strings; do not open any recorded absolute path."""
    require(data["runtime_assembly_sha256"] == identity["runtime_dll_sha256"],
            "Recorded Runtime hash differs")
    expected = str(PurePosixPath(identity["dll"]).parent / "GameplayTags.dll")
    require(data["runtime_assembly_path"] == expected, "Recorded Runtime path differs")


def check_runtime(data, backend, bridge=False, manifest=None):
    require(data["gc_latency"] == "Batch", "Wrong recorded GC latency")
    if bridge:
        if backend == "nohw":
            require(not data["vector_accelerated"] and not data["avx2"], "Wrong nohw backend")
        else:
            require(manifest["env"].get("DOTNET_EnableHWIntrinsic") is None, "Default backend overridden")
    else:
        require(all(data[k] == "0" for k in ("tiered_compilation", "ready_to_run", "tiered_pgo", "gc_concurrent")),
                "Recorded runtime controls differ")
        if backend == "nohw":
            require(not data["vector_accelerated"] and not data["avx2"] and data["hw_intrinsic"] == "0",
                    "Wrong nohw backend")
        else:
            require(data["hw_intrinsic"] is None, "Default backend overridden")


def verdict(crossings, threshold, valid):
    if not valid:
        return "invalid-sample"
    if all(x < 1 - threshold for x in crossings):
        return "clear-improvement"
    if all(x > 1 + threshold for x in crossings):
        return "clear-regression"
    return "uncertain-or-small"


def count_verdicts(rows, field="verdict"):
    return {value: sum(row[field] == value for row in rows) for value in VERDICTS}


def compare(actual, expected, path="summary"):
    """Exact structures/integers/booleans; float tolerance 1e-12 relative/absolute."""
    if isinstance(expected, dict):
        require(isinstance(actual, dict) and set(actual) == set(expected), path + ": keys differ")
        for key in expected:
            compare(actual[key], expected[key], path + "." + key)
    elif isinstance(expected, list):
        require(isinstance(actual, list) and len(actual) == len(expected), path + ": length differs")
        for index, (left, right) in enumerate(zip(actual, expected)):
            compare(left, right, f"{path}[{index}]")
    elif type(expected) is float:
        require(type(actual) in (int, float) and math.isfinite(actual) and math.isfinite(expected)
                and math.isclose(actual, expected, rel_tol=1e-12, abs_tol=1e-12),
                f"{path}: recomputed {actual!r}, historical {expected!r}")
    else:
        require(type(actual) is type(expected) and actual == expected,
                f"{path}: recomputed {actual!r}, historical {expected!r}")


def compare_csv(evidence, root, rows):
    """The historical CSV is the same ordered rows with per-round details omitted."""
    fields = [field for field in rows[0] if field != "rounds"]
    reader = csv.DictReader(io.StringIO(evidence.read(root + "/summary.csv").decode("utf-8")))
    require(reader.fieldnames == fields, root + ": CSV columns differ")
    expected = list(reader)
    require(len(expected) == len(rows), root + ": CSV row count differs")
    for index, (row, saved) in enumerate(zip(rows, expected)):
        require(set(saved) == set(fields), root + ": malformed CSV row")
        for field in fields:
            actual = row[field]
            if type(actual) is float:
                compare(actual, float(saved[field]), f"{root}/summary.csv[{index}].{field}")
            else:
                require(str(actual) == saved[field], f"{root}/summary.csv[{index}].{field}: value differs")


def broad_summary(evidence, name):
    root = "benchmark/results/" + name
    manifest = evidence.json(root + "/manifest.json")
    args = manifest["args"]
    layouts = args["layouts"].split(",")
    require(layouts == ["Micro", "Dense", "Auto"] and args["rounds"] == 3,
            name + ": unexpected historical cohort dimensions")
    group = args["group"]
    require(name.endswith(f"-{group}-{args['backend']}"), "Cohort name/arguments differ")
    plans = {layout: evidence.json(root + f"/iterations-{layout}.json") for layout in layouts}
    for plan in plans.values():
        require(len(plan) == EXPECTED_ROWS[group] and set(plan) == expected_keys(group), "Wrong workload plan")
    records, runtimes = {}, {}
    processes = samples_count = 0
    for meta, data in evidence.manifest_runs(root, manifest):
        label = meta["label"]
        if not label.startswith("r"):
            continue
        round_label, layout, arm = label.split("-")
        round_number = int(round_label[1:])
        require(layout in layouts and round_number in range(3) and arm in ("A", "AA", "B"), "Unexpected process arm")
        variant = args["candidate"] if arm == "B" else args["baseline"]
        require(meta["variant"] == variant, "Wrong variant arm")
        require(data["layout"] == layout and data["group"] == group, "Process layout/group differs")
        identity = manifest["identities"][variant]
        if identity.get("cross_assembly_calls"):
            check_recorded_identity(data, identity)
        require(len(data["rows"]) == EXPECTED_ROWS[group], "Wrong process row count")
        seen = set()
        for row in data["rows"]:
            require(row["id"] in plans[layout] and row["id"] not in seen, "Unexpected/duplicate row")
            seen.add(row["id"])
            require(row["iterations"] == plans[layout][row["id"]], "Fixed iteration plan differs")
            check_row(row)
            values = records.setdefault((layout, row["id"]), {})
            require((round_number, arm) not in values, "Duplicate numerical round/arm")
            values[(round_number, arm)] = row
            samples_count += len(row["samples"])
        require(seen == set(plans[layout]), "Row coverage differs")
        check_runtime(data, args["backend"])
        runtime = {key: data[key] for key in RUNTIME_FIELDS}
        require(layout not in runtimes or runtimes[layout] == runtime, "Cross-process runtime differs")
        runtimes[layout] = runtime
        processes += 1
    rows = []
    expected_arms = {(r, a) for r in range(3) for a in ("A", "AA", "B")}
    for (layout, row_id), values in records.items():
        require(set(values) == expected_arms, "Incomplete rounds/arms")
        ratios, crossings, drifts, tails, p95s, allocations_a, allocations_b, minimums, details = ([] for _ in range(9))
        valid = True
        for r in range(3):
            a, aa, b = (values[(r, arm)] for arm in ("A", "AA", "B"))
            require(all(a[key] == aa[key] == b[key] for key in MATCH_FIELDS), "Input/output contract differs: " + row_id)
            ats, aats, bts = ([s["ns_per_op"] for s in row["samples"]] for row in (a, aa, b))
            at, aat, bt = map(median, (ats, aats, bts))
            ratio, drift = bt / math.sqrt(at * aat), aat / at
            ba, baa = bt / at, bt / aat
            p95a, p95aa = percentile(bts, .95) / percentile(ats, .95), percentile(bts, .95) / percentile(aats, .95)
            maxa, maxaa = max(bts) / max(ats), max(bts) / max(aats)
            ratios.append(ratio)
            crossings.extend((ba, baa))
            drifts.append(drift)
            tails.extend((maxa, maxaa))
            p95s.extend((p95a, p95aa))
            details.append(dict(round=r, baseline_ns=at, aa_ns=aat, candidate_ns=bt, ratio=ratio,
                                candidate_over_a=ba, candidate_over_aa=baa, aa_ratio=drift,
                                p95_over_a=p95a, p95_over_aa=p95aa, max_over_a=maxa, max_over_aa=maxaa))
            for row in (a, aa, b):
                valid &= check_row(row)
                minimums.extend(s["elapsed_ms"] for s in row["samples"])
            allocations_a.extend(s["bytes_per_op"] for row in (a, aa) for s in row["samples"])
            allocations_b.extend(s["bytes_per_op"] for s in b["samples"])
        drift = max(abs(x - 1) for x in drifts)
        rows.append(dict(layout=layout, id=row_id, valid=valid, minimum_sample_ms=min(minimums),
                         candidate_ratio_median=median(ratios), paired_ratio_min=min(ratios), paired_ratio_max=max(ratios),
                         crossing_min=min(crossings), crossing_max=max(crossings), aa_drift_max=drift,
                         p95_tail_ratio_max=max(p95s), max_tail_ratio_max=max(tails),
                         baseline_bytes=median(allocations_a), candidate_bytes=median(allocations_b),
                         capacity_equal=True, payload_equal=True, verdict=verdict(crossings, max(.03, drift), valid),
                         rounds=details))
    require(len(rows) == EXPECTED_ROWS[group] * len(layouts), "Incomplete overall matrix")
    summary = dict(name=name, backend=args["backend"], group=group, candidate=args["candidate"], rows=rows,
                   counts=count_verdicts(rows), limitations=[
                       "Single host .NET8 experiment; does not establish Unity/IL2CPP/Burst performance",
                       "No significance claim; three paired process rounds with baseline A/A",
                       "Payload excludes object and array headers; allocations include them",
                       "All B/A and B/AA crossings and p95/max batch tails retained; geometric bracketing is descriptive only",
                   ])
    compare(summary, evidence.json(root + "/summary.json"), name)
    compare_csv(evidence, root, rows)
    return summary, {"processes": processes, "samples": samples_count}


def factor_summary(evidence, name):
    root = "benchmark/results/" + name
    manifest = evidence.json(root + "/manifest.json")
    args = manifest["args"]
    bridge = name.startswith("bridge-") or "-bridge-" in name
    planned = {(c["group"], c["layout"], c["id"]): c["role"] for c in manifest["plan"]["cells"]}
    require(len(planned) == len(manifest["plan"]["cells"]) == (54 if bridge else 18), "Wrong factor plan")
    require(name.endswith("-" + args["backend"]), "Factor name/backend differs")
    records, runtimes = {}, {}
    processes = samples_count = 0
    for meta, data in evidence.manifest_runs(root, manifest):
        if not meta["label"].startswith("r"):
            continue
        rn, group, layout, arm = meta["label"].split("-")
        r = int(rn[1:])
        require(r in range(3) and arm in ("A", "AA", "B", "C"), "Wrong factor round/arm")
        variant = args["baseline"] if arm in ("A", "AA") else args["v2"] if arm == "B" else args["v3"]
        require(meta["variant"] == variant, "Wrong factor variant arm")
        check_recorded_identity(data, manifest["identities"][variant])
        require(data["group"] == group and data["layout"] == layout, "Wrong group/layout")
        check_runtime(data, args["backend"], bridge, manifest)
        if bridge:
            require(data["caller"] == ("legacy" if arm in ("A", "AA") else "direct"), "Wrong public caller")
        runtime = {key: data[key] for key in (BRIDGE_RUNTIME_FIELDS if bridge else RUNTIME_FIELDS)}
        require((group, layout) not in runtimes or runtimes[(group, layout)] == runtime, "Factor runtime differs")
        runtimes[(group, layout)] = runtime
        expected = {row_id for g, l, row_id in planned if g == group and l == layout}
        require(expected and len(data["rows"]) == len(expected), "Wrong factor row count")
        counts = evidence.json(root + f"/iterations-{group}-{layout}.json")
        require(set(counts) == expected, "Wrong factor iteration plan")
        seen = set()
        for row in data["rows"]:
            require(row["id"] in counts and row["id"] not in seen, "Unexpected/duplicate factor row")
            seen.add(row["id"])
            require(row["iterations"] == counts[row["id"]], "Factor fixed iteration differs")
            check_row(row)
            values = records.setdefault((group, layout, row["id"]), {})
            require((r, arm) not in values, "Duplicate numerical factor round/arm")
            values[(r, arm)] = row
            samples_count += len(row["samples"])
        require(seen == expected, "Wrong factor row identities")
        processes += 1
    require(set(records) == set(planned), "Incomplete factor matrix")
    rows = []
    for key, values in records.items():
        require(set(values) == {(r, a) for r in range(3) for a in ("A", "AA", "B", "C")}, "Incomplete factor arms")
        b_ratios, c_ratios, cb, bcross, ccross, drifts, tails, p95s, cbtails, cbp95s, details = ([] for _ in range(11))
        valid, minimum = True, math.inf
        for r in range(3):
            a, aa, b, c = (values[(r, arm)] for arm in ("A", "AA", "B", "C"))
            require(all(a[f] == aa[f] == b[f] == c[f] for f in (BRIDGE_MATCH_FIELDS if bridge else MATCH_FIELDS)),
                    "Factor input/output contracts differ")
            samples = [[s["ns_per_op"] for s in row["samples"]] for row in (a, aa, b, c)]
            at, aat, bt, ct = map(median, samples)
            br, cr, cbr, drift = bt / math.sqrt(at * aat), ct / math.sqrt(at * aat), ct / bt, aat / at
            b_ratios.append(br)
            c_ratios.append(cr)
            cb.append(cbr)
            bcross.extend((bt / at, bt / aat))
            ccross.extend((ct / at, ct / aat))
            drifts.append(abs(drift - 1))
            tails.extend(max(samples[3]) / max(xs) for xs in samples[:2])
            p95s.extend(percentile(samples[3], .95) / percentile(xs, .95) for xs in samples[:2])
            cbtail, cbp95 = max(samples[3]) / max(samples[2]), percentile(samples[3], .95) / percentile(samples[2], .95)
            cbtails.append(cbtail)
            cbp95s.append(cbp95)
            for row in (a, aa, b, c):
                valid &= check_row(row)
                minimum = min(minimum, min(s["elapsed_ms"] for s in row["samples"]))
            details.append(dict(round=r, baseline_ns=at, aa_ns=aat, v2_ns=bt, v3_ns=ct,
                                v2_ratio=br, v3_ratio=cr, v3_over_v2=cbr, v3_over_v2_p95=cbp95,
                                v3_over_v2_max=cbtail, v2_over_a=bt / at, v2_over_aa=bt / aat,
                                v3_over_a=ct / at, v3_over_aa=ct / aat, aa_ratio=drift))
        threshold = max(.03, max(drifts))
        group, layout, row_id = key
        rows.append(dict(group=group, layout=layout, id=row_id, role=planned[key], valid=valid,
                         minimum_sample_ms=minimum, v2_ratio_median=median(b_ratios), v3_ratio_median=median(c_ratios),
                         v3_over_v2_median=median(cb), v3_crossing_min=min(ccross), v3_crossing_max=max(ccross),
                         aa_drift_max=max(drifts), v3_p95_tail_ratio_max=max(p95s), v3_max_tail_ratio_max=max(tails),
                         v3_v2_p95_tail_ratio_max=max(cbp95s), v3_v2_max_tail_ratio_max=max(cbtails),
                         v2_verdict=verdict(bcross, threshold, valid), v3_verdict=verdict(ccross, threshold, valid),
                         versus_v2_verdict=verdict(cb, threshold, valid),
                         baseline_bytes=median(s["bytes_per_op"] for s in values[(0, "A")]["samples"]),
                         v2_bytes=median(s["bytes_per_op"] for s in values[(0, "B")]["samples"]),
                         v3_bytes=median(s["bytes_per_op"] for s in values[(0, "C")]["samples"]), rounds=details))
    scope = (
        "54-cell fixed-actor/hot-query bridge factor; baseline A/A is legacy public API, v2 is old-v3 Direct bridge, v3 is split Direct bridge. Representation differences remain explicit."
        if bridge else "Targeted18-cell admission diagnostic; v2 and v3 bracketed by baseline A/A. Does not replace broad results."
    )
    summary = dict(name=name, backend=args["backend"], scope=scope, rows=rows,
                   counts={field: count_verdicts(rows, field) for field in ("v2_verdict", "v3_verdict", "versus_v2_verdict")})
    compare(summary, evidence.json(root + "/summary.json"), name)
    compare_csv(evidence, root, rows)
    return summary, {"processes": processes, "samples": samples_count}


def diagnostic_summary(evidence, final_builder):
    manifest = evidence.json(DIAGNOSTIC + "/manifest.json")
    counts = evidence.json("benchmark/diagnostics/sorted-history/iterations.json")
    require(counts == evidence.json("benchmark/results/final-builder-nohw/iterations-Dense.json"), "Diagnostic count plan differs")
    require(list(counts)[22] == TARGET and counts[TARGET] == 973, "Saved diagnostic target differs")
    frozen = evidence.json("benchmark/results/final-builder-nohw/r0-Dense-A.json")["rows"]
    frozen_rows = {row["id"]: row for row in frozen}
    expected = {(r, c, h, a) for r in range(3) for c in ("work", "sorted")
                for h in ("first", "prefix") for a in ("A", "B", "AA")}
    records, runtime, mvid = {}, None, {}
    sample_count = 0
    require(set(manifest["identities"]) == {"baseline", "selected"}, "Diagnostic identity coverage differs")
    for meta, data in evidence.manifest_runs(DIAGNOSTIC, manifest):
        key = (meta["round"], meta["caller"], meta["history"], meta["arm"])
        require(key in expected and key not in records, "Duplicate/unexpected diagnostic arm")
        variant = "selected" if key[3] == "B" else "baseline"
        require(meta["variant"] == variant, "Wrong diagnostic variant arm")
        identity = manifest["identities"][variant]
        check_recorded_identity(data, identity)
        require(data["caller"] == meta["caller"] and data["history"] == meta["history"], "Caller/history differs")
        require(data["layout"] == "Dense" and data["target_id"] == TARGET and data["target_iterations"] == 973, "Diagnostic target differs")
        require(data["prefix_rows"] == (22 if data["history"] == "prefix" else 0), "Prefix size differs")
        check_runtime(data, "nohw")
        require(not data["gc_server"] and data["registry_count"] == 262144 and data["architecture"] == "X64", "Diagnostic runtime fixture differs")
        require(data["minimum_sample_ms"] == 1 and data["allocation_budget"] == BUDGET, "Protocol limits differ")
        planned = list(counts)[:23] if data["history"] == "prefix" else [TARGET]
        require([row["id"] for row in data["rows"]] == planned, "Prefix/target order differs")
        for row in data["rows"]:
            require(row["iterations"] == counts[row["id"]], "Diagnostic fixed iteration differs")
            check_row(row)
            require(all(row[f] == frozen_rows[row["id"]][f] for f in MATCH_FIELDS), "Diagnostic/frozen fixture differs")
            require(all(s["total_bytes"] <= BUDGET for s in row["samples"]), "Diagnostic allocation ceiling")
        row = data["rows"][-1]
        require(row["id"] == TARGET and row["iterations"] == 973, "Wrong target count")
        require(all(s["bytes_per_op"] == 32848 for s in row["samples"]), "Target allocation contract differs")
        records[key] = row
        sample_count += sum(len(row["samples"]) for row in data["rows"])
        now = {key: data[key] for key in RUNTIME_FIELDS if key != "group"}
        require(runtime is None or runtime == now, "Diagnostic cross-process runtime differs")
        runtime = now
        require(variant not in mvid or mvid[variant] == data["runtime_assembly_mvid"], "Recorded diagnostic MVID differs")
        mvid[variant] = data["runtime_assembly_mvid"]
    require(set(records) == expected, "Missing diagnostic arms/cells")
    require(all(all(row[f] == frozen[22][f] for f in MATCH_FIELDS) for row in records.values()), "Target differs from frozen cohort")
    rows = []
    for caller in ("work", "sorted"):
        for history in ("first", "prefix"):
            details, crossings, ratios, drifts, tails, p95s = ([] for _ in range(6))
            valid = True
            for r in range(3):
                arms = {a: records[(r, caller, history, a)] for a in ("A", "B", "AA")}
                samples = {a: [s["ns_per_op"] for s in row["samples"]] for a, row in arms.items()}
                medians = {a: median(s) for a, s in samples.items()}
                comparisons = {a: medians["B"] / medians[a] for a in ("A", "AA")}
                p95 = {a: percentile(samples["B"], .95) / percentile(samples[a], .95) for a in ("A", "AA")}
                tail = {a: max(samples["B"]) / max(samples[a]) for a in ("A", "AA")}
                ratio, drift = medians["B"] / math.sqrt(medians["A"] * medians["AA"]), medians["AA"] / medians["A"]
                ratios.append(ratio)
                drifts.append(abs(drift - 1))
                crossings.extend(comparisons.values())
                p95s.extend(p95.values())
                tails.extend(tail.values())
                valid &= all(check_row(row) for row in arms.values())
                details.append(dict(round=r, median_ns=medians, candidate_over_a=comparisons["A"],
                                    candidate_over_aa=comparisons["AA"], aa_ratio=drift, ratio=ratio,
                                    p95_over_a=p95["A"], p95_over_aa=p95["AA"], max_over_a=tail["A"], max_over_aa=tail["AA"],
                                    gcs={a: [s["gcs"] for s in row["samples"]] for a, row in arms.items()}))
            threshold = max(.03, max(drifts))
            rows.append(dict(caller=caller, history=history, ratio=median(ratios), crossing_min=min(crossings),
                             crossing_max=max(crossings), aa_drift_max=max(drifts), threshold=threshold,
                             verdict=verdict(crossings, threshold, valid), p95_tail_ratio_max=max(p95s), max_tail_ratio_max=max(tails),
                             minimum_ms=min(s["elapsed_ms"] for r in range(3) for a in ("A", "B", "AA")
                                            for s in records[(r, caller, history, a)]["samples"]), rounds=details))
    reproduced = next(row for row in rows if row["caller"] == "work" and row["history"] == "prefix")["verdict"] == "clear-regression"
    historical_ratio = next(row for row in final_builder["rows"] if row["layout"] == "Dense" and row["id"] == TARGET)["candidate_ratio_median"]
    result = dict(processes=len(records), samples=sample_count, target_samples=len(records) * 7,
                  work_prefix_reproduced=reproduced, contrast_interpretation_allowed=reproduced,
                  historical_final_work_prefix_ratio=historical_ratio, rows=rows,
                  scope="Context diagnostic only; no frozen verdict recategorized. Association is not proof of placement/GC mechanism. No further experiment without parent direction.")
    compare(result, evidence.json(DIAGNOSTIC + "/summary.json"), "sorted-history")
    return result


def reanalyze(evidence):
    summaries, dimensions = {}, {}
    for name in FINAL_NAMES:
        summaries[name], dimensions[name] = broad_summary(evidence, name)
    for name in FACTOR_NAMES:
        summaries[name], dimensions[name] = factor_summary(evidence, name)
    diagnostic = diagnostic_summary(evidence, summaries["final-builder-nohw"])
    final_manifest = evidence.json("benchmark/results/final-query-default/manifest.json")
    args = final_manifest["args"]
    candidate_hash = final_manifest["identities"][args["candidate"]]["runtime_dll_sha256"]
    baseline_hash = final_manifest["identities"][args["baseline"]]["runtime_dll_sha256"]
    for name in FINAL_NAMES:
        manifest = evidence.json("benchmark/results/" + name + "/manifest.json")
        require(manifest["identities"][manifest["args"]["candidate"]]["runtime_dll_sha256"] == candidate_hash,
                "Final cohort recorded candidate identities differ")
        require(manifest["identities"][manifest["args"]["baseline"]]["runtime_dll_sha256"] == baseline_hash,
                "Final cohort recorded baseline identities differ")
    diagnostic_identities = evidence.json(DIAGNOSTIC + "/manifest.json")["identities"]
    require(diagnostic_identities["selected"]["runtime_dll_sha256"] == candidate_hash
            and diagnostic_identities["baseline"]["runtime_dll_sha256"] == baseline_hash,
            "Diagnostic and final cohort recorded Runtime identities differ")
    final_rows = [row for name in FINAL_NAMES for row in summaries[name]["rows"]]
    aggregate = dict(candidate_runtime_sha256=candidate_hash, baseline_runtime_sha256=baseline_hash,
                     runs=list(FINAL_NAMES), rows=len(final_rows), samples=sum(dimensions[n]["samples"] for n in FINAL_NAMES),
                     median_regressions=sum(row["verdict"] == "clear-regression" for row in final_rows),
                     invalid_cells=sum(not row["valid"] for row in final_rows))
    compare(aggregate, evidence.json("benchmark/final-performance-summary.json"), "final-performance-summary")
    return dict(status="verified", scope=LIMITATION, provenance_revalidated=False,
                statistical_rows_compared=sum(len(s["rows"]) for s in summaries.values()) + len(diagnostic["rows"]),
                inventory=dict(archives=evidence.archive_count, files=len(evidence.data), uncompressed_bytes=evidence.total_bytes,
                               manifest_bound_raw_files=evidence.verified_raw_files, manifest_processes=evidence.verified_processes),
                final_cohort=aggregate, dimensions=dimensions, summaries=summaries,
                constructor_diagnostic=diagnostic,
                diagnostic_prerequisite="passed" if diagnostic["work_prefix_reproduced"] else "FAILED: work/prefix regression was not reproduced; contrast interpretation remains disallowed")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--data-dir", type=Path, default=Path(__file__).resolve().parent,
                        help="Directory containing inventory.json and archives (default: script directory)")
    parser.add_argument("--json", action="store_true", help="Print full recomputed summaries to stdout; never writes files")
    args = parser.parse_args()
    try:
        report = reanalyze(ArchivedEvidence(args.data_dir))
    except (OSError, ValueError, KeyError, TypeError, IndexError, tarfile.TarError) as error:
        print("FAIL: " + str(error), file=sys.stderr)
        print(LIMITATION, file=sys.stderr)
        return 1
    if args.json:
        print(json.dumps(report, indent=2, allow_nan=False))
    else:
        inventory = report["inventory"]
        print(f"PASS: {inventory['archives']} archives / {inventory['files']} files verified against inventory")
        print(f"PASS: {inventory['manifest_bound_raw_files']} raw files checked against historical run manifests; "
              f"{inventory['manifest_processes']} JSON/stdout process pairs agree")
        for name, summary in report["summaries"].items():
            counts = report["dimensions"][name]
            print(f"PASS: {name}: all {len(summary['rows'])} rows and counts match; "
                  f"{counts['processes']} timed processes / {counts['samples']} samples; " + json.dumps(summary["counts"]))
        cohort = report["final_cohort"]
        print(f"PASS: final aggregate: {cohort['rows']} rows / {cohort['samples']} samples / "
              f"{cohort['median_regressions']} median regressions / {cohort['invalid_cells']} invalid cells")
        diagnostic = report["constructor_diagnostic"]
        print(f"PASS: constructor target diagnostic: all 4 rows match; {diagnostic['processes']} processes / "
              f"{diagnostic['samples']} total samples / {diagnostic['target_samples']} target samples")
        print("Historical diagnostic prerequisite: " + report["diagnostic_prerequisite"])
        print(LIMITATION)
    return 0


if __name__ == "__main__":
    sys.exit(main())
