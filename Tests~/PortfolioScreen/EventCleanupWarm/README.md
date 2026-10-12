# Event cleanup: one warm rejection screen

The portable .NET untimed preflight completed: both sources passed the reused 7,601,762-assertion correctness suite and all twelve fixtures. No pilot or warm timing has run locally; standalone Mono remains pending CI. It follows the separate cold-only review without rerunning cold samples or clearing the retained uncertainty in unchanged controls. No optimization is accepted; qualification remains 0/10.

## Exact reuse

Baseline is f7c99546dbf029d70896c0f05e5119b19ffd6d26. The candidate changes only the FlushEvents finally block to skip Array.Clear when remaining is zero, keeping all three state resets unconditional. Candidate patch SHA-256 remains 805651f029a280cb3be2e8e8c10edd4fa208db981852895fc118625080a91090, identical to the cold-screen source at 8784e5f9e6e96a5eb596790ae100e5b26e2424b8. No callback-search, binding or other revision is combined.

Harness/Screen.cs is byte-for-byte the original twelve-row PortfolioScreen/Callbacks Screen.cs. Registry preparation, Row, Make, mutation pairs, observer work, warmup, timer/allocation boundary, fixed row order, pilot seeds, checksums and empty-state checks are unchanged. There is no launcher, reflection bridge, substitute benchmark or cold mode.

Harness/Correctness.cs is byte-for-byte the already-passing EventCleanup harness. Both variants must pass exactly 7,601,762 assertions with zero failed groups and identical full output. Existing callback order, subscription changes, reentrancy, resized append/throw, final-event throw, pending-reference release and post-exception recovery remain covered. The original twelve fixture rows retain their 78 checks.

## Fixed twelve rows

All original rows stay in their original order: dense-path/d64, exhausted-path/d64, silent/d1, global-only/d1, global-only/d4, one-hit/d1, one-miss/d4, huge-gaps/d4, huge-midpoint/d1, huge-late-miss/d1, clear-cycle/d4 and bulk-reset/8xd4.

The labels describe existing fixtures, not a callback-search optimization. Dense and exhausted paths generate 64 and one events per mutation respectively; empty-listener and miss controls can return before the changed cleanup. Clear measures an AddTag/Clear cycle. Bulk measures AddTags/RemoveTags over eight depth-four leaves, with 32 callbacks. All large-table midpoint/gap/miss controls remain, including 8,192 callback entries and four actual groups of 2,048 unrelated gap keys.

## Fixed measurement protocol

Reuse the existing callback pilot and four-pair coordinator:

- Build all four exact-source correctness and Screen binaries before targets.
- Complete both full correctness suites and both twelve-row fixture preflights.
- Run one baseline-only pilot process, one batch per row after 1,024 warmup pairs. No candidate pilot.
- For each row freeze max(seed, ceil(seed * 200 / baseline pilot milliseconds)). Persist the original pilot and shared map before any candidate timing. Never reduce, recalibrate or replace it.
- Run four process pairs in AB, BA, AB, BA order. Every process measures all twelve rows in fixed order using the same frozen map. There are eight comparison processes, each with twelve rows.
- Require zero warm allocated bytes, exact expected event/checksum totals and valid final state. Preserve every schema-valid numerical row before checking invariants, including a failing row, and retain private stdout/stderr in all cases.
- Preserve actual batch lengths, all three GC counts, every ratio and every adverse result. A fast candidate can create sub-200 ms batches; flag them without changing counts, extending the run or resampling.

Summary retains the original geometric-speedup regression signal (<0.97), plus every slower pair and an any-adverse-pair review flag. That inherited signal is descriptive, not permission for a 3% regression and not an acceptance threshold. No threshold is relaxed; smaller adverse observations and uncertainty remain material. A green job means completion only. No full qualification or additional sampling follows automatically.

## Runtime, integrity and bounds

Use unchanged Hierarchical/build_support.py and References.csproj. SDK is 8.0.425; CLR/reference pack is 8.0.31; standalone CI Mono is 6.8.0.105. C#9 builds are optimized and deterministic. Roll-forward is disabled. ReadyToRun is requested=1, tiered compilation=0 and server GC=0 via both common runtime environment prefixes, with the existing runtimeconfig settings unchanged. Compiler/runtime/reference versions and hashes are recorded; the unchanged Screen does not add per-row runtime-reporting work. No result covers actual Unity Editor/Player or IL2CPP.

The coordinator is the existing Callbacks runner with this candidate/harness's source identities and assertion total, plus the already-used cold-screen integrity/cleanup safeguards: frozen manifest, source tree, compiler/runtime/reference, built artifact and shared map checks around every target and during finalization; fixed runtime environment; process-group cleanup; strict source materialization; schema-valid failure retention; explicit public allowlist. No new build framework is introduced.

One whole-driver cap of at most 600 seconds begins at entry, including archive materialization, setup, builds, correctness, fixtures, pilot and comparisons. No nested phase restarts it. Deadline, interruption, failed process or broken invariant leaves an incomplete screen with retained evidence; owned process groups are killed and reaped. No retry, replacement sample or cap extension exists.

## Invocation after separate authorization

Use new, separate, non-nested public/private output directories. Untimed validation:

    python3 Tests~/PortfolioScreen/EventCleanupWarm/run.py --git-root . \
      --backend net --dotnet "$DOTNET" --out "$PUBLIC" --work "$PRIVATE" \
      --preflight-only --deadline-seconds 300

Only after timing authorization, omit --preflight-only and use the default 600-second cap. For standalone Mono add --backend mono --mono "$MONO" --nuget "$PACKAGES". This preparation edits no workflow or earlier EventCleanup package/evidence.

## Public boundary

Only the eight JSON paths in upload-allowlist.txt may be uploaded: status.json, provenance.json, config.json, correctness.json, raw/pilot.json, iterations.json, raw/timing.json and summary.json. Their contents are fixed configuration, sanitized status, numeric rows, versions and repository-relative hashes. AcceptanceEvidence is always false; the directory is checked against the exact allowlist.

Never upload the private work directory, absolute command paths, raw stdout/stderr, error traces, environment dumps, compiler response files, built binaries/PDBs or materialized Runtime source. No private local result is included in this package.
