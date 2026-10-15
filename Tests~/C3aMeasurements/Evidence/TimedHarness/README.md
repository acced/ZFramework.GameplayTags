# Prepared current792 versus C3a comparison

Status: preparation and static checks only. No build, fixture execution, calibration, or formal sample has been run by this preparation worker. No Runtime edits. Parent source/harness audit and timing authorization are required before the commands below.

- `PROTOCOL.md`: exact measured boundary, geometry, identity, count/overflow rules, finite budget, cleanup and interpretation.
- `cells.json`:31 ordered cells,22 public nonalias union workflows and9 guards.
- `source_pins.json` and `verify_sources.py`: exact baseline commit and working/snapshot candidate bytes.
- `Program.cs`: C12 timed harness plus narrow union/exact-copy geometry and postvalidation-capacity additions.
- `build_snapshots.sh`: two build-only sources; exact compilation/source manifest.
- `run_comparison.py`: protected fresh-process freeze,62 untimed fixtures, at most124 current792-only pilots,248 paired formal samples. There is no A/A action.
- `summarize.py`: all numeric records, including shorts/failed validation/GC/allocation anomalies, exact pairs and descriptive summaries.
- `PREPARATION_IDENTITY.json`, `SOURCE_REVIEW.md`, `SHA256SUMS`: origin, static checks and prepared artifact fingerprints.

## Read-only preparation check

From repository root:

    H="$PWD/Tests~/C3aMeasurements"
    python3 "$H/verify_sources.py"
    python3 "$H/run_comparison.py" --help

This does not execute .NET. `prepare_protocol.py` only regenerates declared cells; do not run it after freeze unless content remains byte-identical. Never edit Runtime, Program, protocol, grid or counts during collection.

## Proposed finite execution, only after parent approval

Prerequisites: reviewed exact source identities; both source versions passed their separate semantic suite; no competing build/test/benchmark process; installed .NET8 SDK. The supplied SDK can be selected through DOTNET, without download or installation.

Use new absolute artifact directories. The example uses one outer895s TERM cutoff with5s cleanup, so builds, fixtures, calibration and formal samples share one900s ceiling. It assumes parent approval for the entire predetermined sequence, including continuing with flagged short/capped baseline counts. If parent instead requests a calibration review gate, stop after calibration and deduct actual consumed active wall time from the same900s total before starting compare. Do not reset the full budget.

    export DOTNET="/absolute/path/to/installed/dotnet"
    H="$PWD/Tests~/C3aMeasurements"
    B="$PWD/../c3a-public-build"
    O="$PWD/../c3a-public-evidence"
    timeout --signal=TERM --kill-after=5s 895s bash -c '
      set -euo pipefail
      bash "$1/build_snapshots.sh" "$2"
      python3 "$1/run_comparison.py" freeze --manifest "$2/manifest.json" --out "$3"
      python3 "$1/run_comparison.py" fixtures --manifest "$2/manifest.json" --out "$3"
      python3 "$1/run_comparison.py" calibrate --manifest "$2/manifest.json" --out "$3" --reviewed
      exec python3 "$1/run_comparison.py" compare --manifest "$2/manifest.json" --out "$3" --reviewed
    ' _ "$H" "$B" "$O"

A timeout, failure, missing validation, changed source/harness/binary/count hash, or existing evidence aborts. Preserve raw and terminal receipts. No automatic retries or source repair within this comparison. Outputs are local evidence; external publication needs its own review.

Once the collection's disposition is settled, flatten once (also works for an incomplete calibration with no counts.json):

    python3 "$H/summarize.py" "$O"

This produces raw-flat.csv, paired.csv, paired-summary.csv, absolute-summary.csv and allocation-anomalies.csv when corresponding rows exist. Empty tables are omitted. Durations below250ms remain short even when a contender is faster; no ratio multiplication from old B1 results is used. All248 formal samples are expected only on a completed stage. Diagnostic-only copy/first-use/disassembly collection belongs elsewhere and must not run concurrently or be merged into these ratios.
