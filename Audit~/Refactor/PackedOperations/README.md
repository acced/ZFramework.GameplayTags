# Prepared packed public operations

This standalone runner compares the exact local Git commit
`2fc862b91f9c53362e3fb63c7c086f7501c54ba9` with a snapshot of a checkout's
current `Runtime` directory. Uncommitted and untracked Runtime files are included
in the candidate snapshot and its SHA-256 inventory. It does not modify Runtime
or use sources from other experiment directories.

The fixed matrix has 372 rows: clustered, scattered and contiguous membership;
0/5/50/100% overlap; large and skewed operands; forced Compressed/Sparse/Dense
and prepared Bulk layouts; independent results, reset-plus-mutation and both
alias orientations; member reservations, exact record reservations and smaller
filter-output fallbacks. All 16 actor instances are prepared before timing.

## Reproduce

Python 3.9+, Git with the baseline commit available locally, and .NET SDK 8.0.425
are required. Staging and Python tests do not invoke .NET. The runner snapshots
an SDK pin and the repository's offline NuGet configuration; execution needs no
package downloads after the SDK is installed.

Run the source/oracle tests from the checkout root:

```sh
python3 -m unittest discover -s Audit~/Refactor/PackedOperations -p 'test_*.py' -v
```

The default action only stages sources. Choose a new evidence directory outside
Runtime; earlier evidence is never silently replaced:

```sh
python3 Audit~/Refactor/PackedOperations/run_packed.py \
  --repo . --output /path/to/new-packed-evidence
```

After the candidate's separate correctness verification and when a quiet CPU
window is available, run the frozen script. The option acknowledges CPU
coordination; it does not request another user approval:

```sh
python3 /path/to/new-packed-evidence/sources/run_packed.py run \
  --output /path/to/new-packed-evidence \
  --dotnet /path/to/dotnet --timing-owner-released
```

`--repo` defaults to the source script's checkout. `--dotnet` defaults to the
installed `dotnet` command on PATH. On platforms with process affinity, `--cpu`
selects an allowed CPU; otherwise the first allowed CPU is used. Unsupported
platforms explicitly record that no affinity was established. Do not overlap
other benchmarks or heavy compilation with the timing window.

Each of baseline/candidate has hardware and forced-portable builds. Every
source/backend reuses one executable for its A/A label. Three rotated rounds and
both A/A labels give 24 fresh measurement processes, seven samples per row,
and a 3 ms calibration target. A `PAUSE` file in the evidence directory pauses
between complete processes. Remove it to continue. An admitted cohort cannot
be restarted; preserve an interrupted directory and stage a new one if needed.

To revalidate completed raw data without executing .NET:

```sh
python3 /path/to/new-packed-evidence/sources/run_packed.py summarize \
  --output /path/to/new-packed-evidence
```

## Evidence and interpretation

The C# fixture, operation, measurement and actual-output validation bodies are
the same 372-row protocol used for the packed-operation study. Into calls charge
public Count; append/remove and all alias rows also charge the CopyFrom reset.
Every batch validates the actual final membership of all 16 actors and each
immutable input before the next reset. Earlier operations contribute a weighted
Count checksum. Full physical shapes and output capacities are independently
reconstructed by the Python validator. Every prepared sample requires zero
measured thread allocation, with positive and negative allocation controls.

`manifest.json` records source inventories, baseline Git archive provenance,
candidate worktree provenance, support hashes, SDK, environment and executable
attribution. Raw per-process JSON and logs retain samples, duration, allocation,
GC counts, calibration metadata and declared failures. `summary.json` and
`comparison.json` are produced only after the strict aggregation step. Missing
processes, failed rows, invalid metadata, output mismatches, changed artifacts or
inconsistent host/backend cohorts prevent successful rankings.

For each row/backend, the comparison retains both source A/A ratios and all four
candidate/baseline label pairings, including paired round medians. Read adverse
rows and A/A variability together. A 3 ms calibration target does not guarantee
every subsequent sample lasts 3 ms; actual durations and iteration limits remain
visible. Batch-mean tails are not individual-call latency percentiles.

Prepared Bulk chooses layout before timing, so it is separate from forced
layouts and cannot establish a construction or lifecycle win. Forced portable
disables explicit intrinsics; System.Numerics and BCL acceleration may remain.
This managed-host evidence does not establish Unity/Mono/IL2CPP performance or
authorize production adoption by itself.
