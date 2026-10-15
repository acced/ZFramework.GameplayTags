# C1 removal diagnostic: prepared, not measured

This directory prepares one bounded comparison of R against RMinusC1. Production Runtime stays unchanged. The candidate restores only the historical CopyValidated body; `source-proof.json`, the exact patch and `verify_sources.py` prove that source scope. New measurement assembly context is explicit; this does not reconstruct historical effects.

The approved plan has12 cells: lifecycle1024, its exact retained restore-plus-copy geometry, a separately classified preprepared single-copy diagnostic, four small copy guards, four C2 late-miss guards, and lifecycle1. See [PROTOCOL.md](PROTOCOL.md) and [cells.json](cells.json). Pool construction is outside counters, at most16384 targets/128MiB backing, with a144MiB setup-allocation fail-stop ceiling; every prepared target is used once and postvalidated. Four AB/BA/BA/AB pairs, current R-only fresh counts, at most168 target invocations, one600s combined budget. Shorts remain flagged; no A/A repetition or profiling.

## Review and run only with approval

First run the separate source verifier and two-source existing-suite correctness script using a new absolute output directory. Its command and SDK selection are documented by the source owner. Then review the exact frozen measurement files before invoking:

    H="$PWD/Tests~/C1RemovalDiagnostic"
    DOTNET=/absolute/path/to/dotnet DOTNET_SDK_VERSION=8.0.425 \
      bash "$H/run_bounded.sh" --reviewed "/new/absolute/build" "/new/absolute/evidence"

The wrapper builds both snapshots without invoking a target, freezes source/binary/harness identity, checks24 fixtures, runs at most48 fresh R-only pilots, freezes counts, and collects96 formal samples. All dotnet build commands use output-local global.json, never the host default SDK. The600s total includes5s protected cleanup. No concurrent build/test/benchmark/diagnostic is allowed. Partial or failed stages are preserved and stop; do not retry, resume, overwrite, or change counts.

After the single collection, including if partial, flatten once without invoking .NET:

    python3 "$H/summarize.py" "/absolute/evidence"

The summaries include individual signs/ratios, absolute values, all durations/shorts, storage/GC context, and a separate single-copy diagnostic table. No normalization, median subtraction, historical-ratio synthesis, or production promotion follows. Mixed/non-reproduction leaves the cause unconfirmed and ends this round. Consistency can at most support proposing a future isolation study.
