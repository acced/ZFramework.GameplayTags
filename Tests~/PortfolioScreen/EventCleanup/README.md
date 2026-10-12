# Event cleanup: cold-first portable rejection screen

The portable .NET untimed preflight completed: both exact sources passed 7,601,762 assertions, all twelve existing fixtures and all four new first-operation state/schema checks. No cold latency/allocation samples or warm timing have run locally. Standalone Mono execution remains pending CI. The exact runtime hunk and earlier IL check were reviewed separately. No optimization is accepted; qualification remains 0/10.

## Exact candidate

Baseline is f7c99546dbf029d70896c0f05e5119b19ffd6d26, archived directly from Git. Candidate differs only in GameplayTagCountContainer.FlushEvents finally: compute remaining, call Array.Clear only when remaining > 0, then unconditionally reset event count, cursor and dispatching. No callback-search, binding or unrelated patch is included. Patch SHA-256 is 805651f029a280cb3be2e8e8c10edd4fa208db981852895fc118625080a91090.

Every dispatched slot is already defaulted before its callback. Normal fully drained dispatch and a final-event throw can skip zero-length clear. A reentrant callback that appends, resizes and throws still clears every pending reference in the current array. Supported single-threaded behavior is the scope; malformed private indices are not.

The prior bounded .NET check passed 7,601,762 assertions on each exact source and all twelve existing callback fixture rows. Emitted optimized C#9 FlushEvents IL grows 162 to 168 bytes: one static Array.Clear call remains, now conditionally reachable; all three reset stores stay unconditional. This is not a native-performance result. No local Mono or Unity result exists for this hunk.

## Fixed cold-only design

Exactly four existing shapes, in this order:

1. silent/d1: a root tag, no listeners, unchanged no-listener control.
2. one-miss/d4: a depth-four tag with one unrelated callback, unchanged empty-queue control.
3. one-hit/d1: one matching callback and one event, cheapest modified dispatch.
4. dense-path/d64: sixty-four matching ancestor callbacks, many-event dispatch.

Each shape has four fresh-process pairs in before/after, after/before, before/after, after/before order. That is 32 measured fresh processes per backend. No pilot, warm timing, replacement sample, extra round, A/A subtraction or count adjustment exists. The entire declared cold sequence runs before ordinary numerical or semantic conclusions; integrity failure, interruption or deadline can stop it, leaving an incomplete record. A completed job is never acceptance.

Cold setup reuses the callback registry, Row and Make implementation, immutable tags and observer. It selects only the declared row. It never calls Fixtures(), the row's Pair, AddTag, FlushEvents or the observer before the measured operation. The counted target is empty and all storage/event arrays have zero capacity; only the specified callback registrations are prepared. Existing registry setup and subscription allocation are outside measurement.

Counters and clock access are primed outside the boundary. Measurement surrounds exactly one call to a no-inline FirstAdd bridge that invokes AddTag on the selected leaf. It includes first compilation of that bridge/mutator path, storage and queue growth, first callback work and the final cleanup. It is the whole first AddTag, not isolated FlushEvents. Counter reads bracket the same operation in both variants; elapsed ticks, frequency, nanoseconds, allocated bytes and all three GC counts are retained.

Reflective state capture runs both before and after the measured operation, outside the counter boundary; precondition checks run before measurement and postcondition checks run afterward. Every row preserves expected and actual event/checksum values, before/after tag/explicit counts, storage/explicit/hash/callback/event capacities, callback counts, event count/cursor/dispatching, retained callback references, complete hierarchy count correctness and operation-exception status. Rows are emitted before any semantic-failure exception so valid numerical evidence survives ordinary failure. Exact expected geometry/capacity/checksum values are frozen in config.json and match the original fixtures and f7 growth rules. No state capacity or allocation saving is claimed for the runtime patch.

The same first-operation method has an explicit cold-preflight mode. It creates a fresh process/fixture and invokes the same FirstAdd without reading clocks, allocation or GC counters; measurement fields are zero. This verifies its output schema and semantics without collecting latency/allocation samples. Every full run first executes this untimed mode for both variants and all four shapes in separate fresh processes; these are distinct from the 32 measured calls.

## Correctness, integrity and runtime

The full already-passing focused harness is reused verbatim. It includes the main model suite, ordered callback snapshots, subscription changes, bulk/hierarchy transactions, pending reference checks, no-listener/empty queue, one/many dispatch, resized reentrant append, resized append+throw, and one-/three-event final-callback throws with exact exception identity and subsequent recovery. Every source must pass 7,601,762 assertions and exact output equality. The original twelve-row callback preflight and its 78 checks remain; no prior control is removed.

Use the existing Hierarchical/build_support.py and References.csproj without edits. SDK is 8.0.425, CLR/reference pack 8.0.31; standalone CI Mono is pinned to 6.8.0.105. Builds use optimized deterministic C#9; .NET roll-forward is disabled, ReadyToRun requested=1, tiering=0, server GC=0. Actual runtime/configuration is reported in every cold row. Mono reports inapplicable .NET switches as -1 and records its actual version. This establishes no Unity Editor/Player or IL2CPP result.

All four binaries are built before targets. The driver verifies frozen package, source, compiler/runtime/reference and built-output hashes around each target and during finalization. Baseline and candidate Runtime sources are materialized privately and checked against complete pinned maps. Raw commands, absolute paths, logs and compiler response files remain private. The unchanged compiler helper is imported with bytecode writes disabled.

One fixed whole-driver deadline, at most 600 seconds, starts at entry and includes archive materialization, toolchain checks, builds, correctness, fixtures and cold samples. No phase restarts the clock. Timeout/failure cleanup kills and reaps the owned child process group. No automatic retry, deadline extension or subsequent warm/full qualification is scheduled.

## Invocation, after separate authorization

Use new, separate, non-nested output/work directories. For untimed package/schema validation:

    python3 Tests~/PortfolioScreen/EventCleanup/run.py --git-root . \
      --backend net --dotnet "$DOTNET" --out "$PUBLIC" --work "$PRIVATE" \
      --preflight-only --deadline-seconds 300

After separate timing authorization, omit --preflight-only and use the default 600-second cap. For Mono use --backend mono --mono "$MONO" --nuget "$PACKAGES". There is no warm or pilot mode in this package. No workflow is changed by this preparation.

Every cold latency and allocation result must be reviewed before any warm screen. First-use allocation increases, recurring adverse latency or unresolved ambiguity stop progression; warm gains cannot offset them. No numerical threshold is relaxed. Summary flags are observations, not an acceptance test.

## Public evidence boundary

Only these six JSON files from --out may be uploaded: status.json, provenance.json, config.json, correctness.json, raw/cold.json, summary.json. upload-allowlist.txt lists them explicitly. The output directory is checked against the exact allowlist.

Public evidence contains fixed configuration, sanitized bounded status, numeric state/timing samples, runtime versions and repository-relative source/tool/build hashes. It always sets AcceptanceEvidence=false and ColdLatencyAcceptanceEstablished=false. Never upload private work directories, raw stdout/stderr, failure text, absolute commands, environment dumps, response files, binaries, PDBs or generated Runtime copies. No private local result is copied into this package.
