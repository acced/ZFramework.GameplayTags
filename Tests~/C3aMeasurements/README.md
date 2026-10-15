# C3a public-workflow experiment

C3a was not accepted for production. The comparison was mixed, with repeated losses in targeted public Union workflows. Production Runtime remains exact current792 (`792ce83f1c2a9aeaadc045c192d70081d866d903`). The experimental change is retained only as a reproducible snapshot and evidence. See [OUTCOME.md](OUTCOME.md) for all31 cells and adverse results.

## Complete collected evidence

- 248 formal samples:31 cells×4 pairs×2 versions, in AB/BA/BA/AB order.
- 67 new current792-only pilots, with all31 final counts ≥250ms and uncapped.
- 62 untimed fresh-process fixture invocations; all315 numeric rows and377 invocations passed validation/exited0.
- Three short C3a formal rows remain visible:243.990506ms,245.309523ms,247.557191ms.36 early pilot rows were short. There were no fractional allocation rows.
- No A/A repeat, old-ratio multiplication, favorable resampling, discarded slower results, or diagnostic timings inside formal evidence.

[Evidence/raw-flat.csv](Evidence/raw-flat.csv) retains all315 numerical records, including durations, allocation, GC, capacities and validation. [Evidence/call-index.jsonl](Evidence/call-index.jsonl) locates all377 original logs in six deterministic raw shards and one terminal-record shard. Each byte offset/length/SHA256 identifies the exact original per-call contents, including explicit empty stderr and launch/binary identity. Counts, paired/absolute summaries, short-row quality record, source/binary identity and sanitized semantic transcripts are alongside them. Every indexed raw/terminal byte range and every numerical CSV is byte-identical to its original. Run `python3 Tests~/C3aMeasurements/read_evidence.py` to verify all indexed records without executing any target; `--label` prints one original raw log. Public receipts omit machine paths, launch commands, inherited environment values, binaries and build logs.

## Source and packaging identity

- `Sources/current792/Runtime`: exact baseline source at792ce83.
- `Sources/C3a/Runtime`: exact measured C3a snapshot; only the nonalias UnionSorted compare/tail loop differs.
- `source_pins.json` and `verify_sources.py`: both complete Runtime snapshots, checked before/after work.
- `Program.cs`, project, stubs, NuGet config and `cells.json`: unchanged from the measured builds. Timed Program SHA256 `daac57a934fdb4338103c93fcc1d64316816d1ae7eac4850416d40fc152f9962`; cells SHA256 `3375e9515c3a6aaf698c057624b3a47e690ccdee89a6fdecc7184e22baec76bb`.
- `PROTOCOL.md`, `SOURCE_REVIEW.md` and `PREPARATION_IDENTITY.json` retain their precollection status/wording as historical preparation records; this README and OUTCOME describe completed collection and disposition.
- `Evidence/TimedHarness` preserves original precollection harness files, scripts and source pins. `PACKAGING_IDENTITY.json` distinguishes post-run portability changes: build/source resolution now uses vendored snapshots, independently of production Runtime. No measurement was recollected.
- `correctness.sh`: portable existing-suite replay added after collection; static checks only during packaging. The original exact sources already passed the saved semantic/compatibility runs.

## Portable correctness, without benchmarks

From repository root, with Python3 and installed .NET SDK8.0.425 on PATH (or DOTNET set):

    H="$PWD/Tests~/C3aMeasurements"
    python3 "$H/verify_sources.py"
    timeout --signal=TERM --kill-after=5s 175s bash "$H/correctness.sh" "$PWD/../c3a-fresh-correctness"

Use a new absolute output directory. The script creates an output-local global.json pinned to8.0.425 with rollForward disabled, then runs all dotnet commands from that directory. This selects the required installed SDK even if the host defaults to a newer SDK; it changes no repository SDK settings. This narrow CI recovery leaves measured inputs and evidence untouched. This reuses Roadmap with phase APIs/optimization probes, C12Correctness, and netstandard2.1/C#9 compilation for both pinned snapshots. It checks identical differential transcripts, exact source/input hashes and exclusive output. The180s ceiling includes cleanup. It does not invoke the measurement harness. Existing semantic logs are in Evidence/Semantics; newly run results are separate evidence.

## Fresh comparison, only when explicitly requested

A new collection is a new evidence set. Do not reuse published counts as newly calibrated values for another machine. Both snapshots must first pass the separate correctness suite and the exact source/harness/protocol must be reviewed. Run no competing build/test/benchmark/diagnostic workload. The original measured SDK was8.0.425/runtime8.0.31 on Linux x64, tiering disabled; the script records the actual installed toolchain for future runs.

Set new absolute build/evidence paths. This one outer895s TERM cutoff plus5s cleanup gives a900s combined build+fixtures+pilots+comparison ceiling. It includes62 fixture invocations, at most124 baseline-only pilots and248 formal samples. A partial stage is preserved and incomplete; no automatic retry, count revision or overwrite occurs. If a separate calibration review is requested, deduct consumed active wall time before continuing rather than resetting900s.

    H="$PWD/Tests~/C3aMeasurements"
    B="$PWD/../c3a-fresh-build"
    O="$PWD/../c3a-fresh-evidence"
    timeout --signal=TERM --kill-after=5s 895s bash -c '
      set -euo pipefail
      bash "$1/build_snapshots.sh" "$2"
      python3 "$1/run_comparison.py" freeze --manifest "$2/manifest.json" --out "$3"
      python3 "$1/run_comparison.py" fixtures --manifest "$2/manifest.json" --out "$3"
      python3 "$1/run_comparison.py" calibrate --manifest "$2/manifest.json" --out "$3" --reviewed
      exec python3 "$1/run_comparison.py" compare --manifest "$2/manifest.json" --out "$3" --reviewed
    ' _ "$H" "$B" "$O"

`--reviewed` records operator verification and authorization; it is not itself permission or performance acceptance. Both sources use identical harness inputs and distinct build directories. All measured restoration/build/growth costs are retained. Count/version ceilings, raw-before-validation behavior and protected process cleanup are specified in the original protocol.

After disposition, flatten once, including incomplete runs if needed:

    python3 "$H/summarize.py" "$O"

Empty summary tables are omitted, so no allocation-anomalies.csv exists when no fractional rows occurred. Copies of the original numerical summaries are already published under Evidence. No .NET8 result certifies Unity Mono/IL2CPP/device performance. Existing [C12 A/A context](../C12Measurements/README.md) remains historical; separate [C3aDiagnostics](../C3aDiagnostics/README.md) targets B11/C1/current792 and is not included in these formal ratios.
