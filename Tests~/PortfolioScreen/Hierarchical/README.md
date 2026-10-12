# Hierarchical bulk: portable cheap rejection screen

This is static CI preparation, not a successful CI run or an accepted optimization. It preserves the original 35 timing rows, focused correctness, first-bulk allocation checks and four alternating AB/BA process pairs. No full acceptance gate is included. The candidate is not promoted by a passing job.

## Fixed inputs

- Baseline: `f7c99546dbf029d70896c0f05e5119b19ffd6d26`, materialized from the supplied Git object database with `git archive`.
- Candidate: replace only `Runtime/GameplayTagHierarchicalContanier.cs` with `candidate/GameplayTagHierarchicalContanier.cs`. Both complete Runtime hash maps and the review patch hash are in `source-pins.json`; no duplicate Runtime trees are committed here.
- Compiler SDK 8.0.425; CLR and reference pack 8.0.31. Mono jobs require 6.8.0.105. Actual compiler/runtime versions are recorded. CLR patch roll-forward is disabled. The earlier local Mono screen used 6.12; this is a separately labeled backend run.
- `iterations/net.tsv` and `iterations/mono.tsv` contain all 35 fixed counts. Each count is the ceiling of original count × 200 ms / fastest of four saved baseline-only batch times, never below the original count. Candidate timings were not consulted. `baseline-calibration.json` retains all four baseline batch times used for each row. These maps are shared by before/after and all four pairs. No adaptive pilot, row pruning or resampling is allowed.

The only harness change is an optional `bench --iterations-file <file>` override. Source semantics, fixture sizes, action bodies, checksum, correctness and allocation boundaries remain the same. Cached reflection still calls the same runtime allocation counter on both backends.

## Invocation

The shared workflow explicitly runs this entry point and owns checkout and tool/package installation. Run each backend in a separate job on an otherwise quiet runner:

    python3 Tests~/PortfolioScreen/Hierarchical/run.py \
      --backend net --dotnet /path/to/dotnet --git-root . \
      --out /path/to/public-hierarchical-net --work /path/to/private-hierarchical-net

For Mono use `--backend mono --mono /path/to/mono --nuget /path/to/packages`. `--dotnet` is still needed for the pinned Roslyn compiler. The output and private work directories must be new, separate and non-nested. The runner permits a maximum 600-second internal deadline; a workflow may also impose a 15-minute job cap. It does not extend or retry a timeout. Owned process groups are killed and reaped after timeout, nonzero exit or interruption; a deadline exceeded during finalization cannot be labeled completed.

The shared restore project must supply these official pinned NuGet packages:

- Microsoft.NETFramework.ReferenceAssemblies.net472 1.0.3
- System.Memory 4.5.5
- System.Buffers 4.5.1
- System.Runtime.CompilerServices.Unsafe 4.5.3
- System.Numerics.Vectors 4.5.0

Per backend there are two direct-csc builds, two correctness processes, two allocation processes and eight timed processes, plus small untimed archive/version commands. At exactly 200 ms per row, timed batches total 56 seconds per backend. Actual time may be higher; this is a fixed planning target, not calibration on the CI host. Actual batch lengths and all losses remain in the numeric results.

## Public evidence boundary

Upload only the eight paths in `upload-allowlist.txt`, relative to `--out`. That directory contains only sanitized status/provenance/configuration, correctness, allocation and raw numeric row JSON. Source paths in provenance are repository-relative. Toolchain and compiled-artifact hashes use fixed logical relative names; their absolute locations are never published. Those files are checked before and after each target call. Expected versions, actual versions, compiler flags, fixed counts, all raw samples and regression flags are included.

Never upload `--work`, its command log, response files, generated Runtime trees, compiler output, stack traces or raw process stdout/stderr. No environment dump is produced. Do not use an upload pattern that can reach the private work directory. Failure details stay private; the public status identifies only the fixed stage at which the run stopped.

A complete screen can still show regression signals or noisy/short batches. `AcceptanceEvidence` always remains false. First-bulk allocation is warmed-code, cold-snapshot evidence, not process/JIT-cold or retained-heap evidence. Standalone .NET/Mono results are not Unity Editor Mono or IL2CPP validation.
