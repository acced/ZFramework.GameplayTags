# C1/C2 isolated-process measurements

See [OUTCOME.md](OUTCOME.md) for the complete results and limitations, [PROTOCOL.md](PROTOCOL.md) for measured boundaries, and [SOURCE_REVIEW.md](SOURCE_REVIEW.md) for the implementation argument. This is a finite .NET8 Linux screen, not a universal performance pass or Unity certification.

## Published evidence and chronology

- `Evidence/Original`: all90 B1 pilots and64 physical-B11-binary A/A samples, frozen original counts, exact numerical CSVs, validation status, terminal receipts and sanitized identities.
- `Evidence/Repaired`: four fixed B1 control checks,32 control A/A samples,240 C1 samples and480 C2 samples. Includes all756 numerical rows, repaired counts, paired values, C1/C2 review JSON and CSV tables.
- Total910 numerical samples, all retained. Four original read-only control counts were inadequate because an unnecessary List-mutation cap limited them. They are not erased or relabelled. The repair derives four fixed counts solely from saved B1 pilots, then checks them once. The original32 mutation-sentinel A/A samples remain the mutation controls; repaired32 control A/A samples supplement them. No fake combined run is claimed.
- Repaired historical counts are69,505,702 /54,183,010 /396,032 /343,290 (scan1/enumeration1/scan1024/enumeration1024). Four actual B1 checks were310.806 /294.049 /276.137 /312.884ms. Counts metadata deliberately retains `adequate_baseline=null`; actual checks are separate raw records.
- `ALLOCATION_ANOMALIES.csv` preserves every fractional allocation-per-operation row, including pilots. Cold-process single-call allocation includes first invocation costs and is not a steady-state allocation guarantee. No fractional value is rounded to an expected array size.

Raw CSVs and review JSONs are byte-identical to collected numerical files. Published receipts contain no inherited environment values, machine-local paths, launch commands, binaries, build logs or private launch/provenance metadata. The identity receipts preserve SDK/runtime, hashes, source inventories, validation and sanitized compilation settings. Retained capacity/payload are incomplete storage measures, not whole-heap usage. C1 review CSV capacity fields are blank because its intermediate JSON did not include those values; the raw-flat CSV contains actual postvalidation capacities.

## Exact measurement input versus packaging changes

`Program.cs`, project, Unity stubs, NuGet configuration and `cells.json` are byte-identical to the four measured builds. Timed Program SHA256 is `62638366804b87989b49a15d14fc15daf1dce8a7d93ef7fb0698825d77ac14d7`; grid SHA256 is `b91f1711405110eb6bf44cd0dc70e1ba0db6a76c3a62d553a9a91975b2959521`.

The Python/shell launchers have packaging-only portability edits: installed SDK discovery; repository-relative vendored source lookup; sanitized future environment receipts; explicit prior-output argument rather than a historical absolute-path provenance file. New helpers provide untimed fixture validation and saved-pilot arithmetic. These packaged scripts were syntax-checked, not used to recollect this evidence. No new build or timing was run during packaging. Source inventories and `PACKAGING_IDENTITY.json` distinguish timed originals from published scripts.

B1 uses the existing Roadmap snapshot; B11 uses C12Correctness/Baselines/PR4; C1 uses C12Correctness/Baselines/C1; final uses root Runtime. Root Runtime is the code under test, so a future edited checkout is a new source identity. No SDK download or network access is required when .NET8 is installed.

## Fresh reproduction

Start from repository root with Python3, GNU timeout and .NET8 SDK on PATH, or set DOTNET to its executable. Do not reuse published counts for a different machine as if newly calibrated. Use fresh external output directories; never overwrite evidence. Run no competing benchmark/build/test workload during timers.

1. Build the four sources without invoking targets:

       H="$PWD/Tests~/C12Measurements"
       B="$PWD/../c12-fresh-build"
       O="$PWD/../c12-fresh-original"
       R="$PWD/../c12-fresh-repaired"
       timeout --signal=TERM --kill-after=5s 115s bash "$H/build_snapshots.sh" "$B"

2. Run the separate C12Correctness semantic suite as documented there. Then validate all44 benchmark fixtures on all4 builds without timers:

       timeout --signal=TERM --kill-after=5s 115s python3 "$H/validate_fixtures.py" "$B/manifest.json" "$B/fixture-validation"

3. Freeze the original protocol/binaries, then collect B1-only calibration and64 physical-binary A/A samples. `--reviewed` means the operator has checked source identities, semantics and protocol; it is not a performance pass. For a strict combined900s preparation budget, put build, validation, calibration and A/A inside one outer timeout, rather than resetting900s between them.

       python3 "$H/run_original.py" freeze --manifest "$B/manifest.json" --out "$O"
       timeout --signal=TERM --kill-after=5s 755s bash -c 'set -e; python3 "$1/run_original.py" calibrate --manifest "$2/manifest.json" --out "$3" --reviewed; python3 "$1/run_original.py" aa --manifest "$2/manifest.json" --out "$3" --reviewed' _ "$H" "$B" "$O"

4. Review all original durations/A/A; retain shortcomings. To reproduce the saved-pilot repair, freeze the repaired runner with explicit original-output provenance and derive four counts from that new run's B1 pilots. Forty other counts remain unchanged. The fresh counts need not equal this publication's historical numbers. The runner pins hashes of original raw/terminal/count/completion evidence in its fresh freeze; no unpublished absolute-path dependency is needed.

       python3 "$H/run_repaired.py" freeze --prior "$O" --manifest "$B/manifest.json" --out "$R"
       python3 "$H/derive_controls.py" "$O" "$R"
       timeout --signal=TERM --kill-after=5s 115s bash -c 'set -e; python3 "$1/run_repaired.py" baseline-checks --prior "$3" --manifest "$2/manifest.json" --out "$4" --reviewed; python3 "$1/run_repaired.py" aa --prior "$3" --manifest "$2/manifest.json" --out "$4" --reviewed' _ "$H" "$B" "$O" "$R"

   Exactly4 fixed B1 checks plus32 repaired control A/A calls. No adaptive repair or count revision. Review actual durations and variability before proceeding.

5. If the evidence is adequate for the intended interpretation, collect C1, inspect its complete results, then separately decide whether to collect C2. These are separately gated finite stages, not an automatic pipeline:

       timeout --signal=TERM --kill-after=5s 895s python3 "$H/run_repaired.py" C1 --prior "$O" --manifest "$B/manifest.json" --out "$R" --reviewed
       timeout --signal=TERM --kill-after=5s 895s python3 "$H/run_repaired.py" C2 --prior "$O" --manifest "$B/manifest.json" --out "$R" --reviewed

   C1 launches240 samples; C2 launches480. All use the same frozen counts and existing binaries. Every target gets a fresh process. TERM is handled with protected child spawn/assignment and protected kill/reap;5s cleanup precedes the hard900s limit. Never alter counts or retry failed/short cells to improve results.

6. Once the final stage disposition is settled, flatten once per output. Outputs refuse overwrite:

       python3 "$H/summarize.py" "$O"
       python3 "$H/summarize.py" "$R"

The summaries are descriptive paired ratios, not statistical acceptance. Keep adverse lifecycle results, allocation anomalies, short samples and unchanged-source/A/A variation visible. Do not subtract medians or controls. Four paired samples cannot establish precise universal speedups.
