# Bounded R versus RMinusC1 removal diagnostic

Preparation-only protocol. No benchmark binary has been built or invoked as part of preparation. The approved collection is one finite new evidence set with 12 cells, not the previous 31-cell sweep. Production Runtime is preserved. Source authority is `source_pins.json`, `source-proof.json`, `CopyValidated-removal.patch`, and `verify_sources.py`; the candidate removes only the C1 CopyValidated implementation by restoring its exact historical body. No helper, visibility, instrumentation, C2, or other Runtime change is allowed.

## Identity and scope

- A = R, the exact pinned PR4/current implementation snapshot.
- B = RMinusC1, the same snapshot except the one proven CopyValidated body replacement.
- Both snapshots compile against the same newly prepared C1RemovalDiagnostic harness and assembly name. This is a new harness/build context relative to C3a and earlier measurements. It directly compares R versus RMinusC1 now; it does not reconstruct or back-calculate historical C1 effects.
- The separate existing semantic, representative-identity, compatibility, and differential suites must pass for both sources before any measurement build. Fixture checks below supplement those suites.
- No repeated A/A, disassembly, tracing, profiling, private-kernel variant, production edit, favorable resampling, or follow-on variant is included. Historical first A/A spread is context, never a numerical waiver, acceptance cutoff, or replacement for current evidence.

## Exact ordered grid: 12 cells

`cells.json` is authoritative. Source and target numbers below describe counts before the operation; actual capacities are separately validated/reported. All strings are flat registered T plus five decimal digits. Equal-name strings are independently allocated unless the lifecycle-representative relationship is explicit.

1. Primary lifecycle1024, unchanged operation sequence: Build(shuffled,0); HasTagExact; RemoveTag/AddTag; full enumeration; public new Union; public new IntersectionExact; CopyFrom(intersection); final Clear; historical consumer. Inputs each have1024 names. Lhs/target names are evens0..2046; rhs starts1024, giving union1536/capacity2048 and intersection512/capacity1024. The lifecycle target ends empty/capacity1024. The metadata capacity1024 describes input setup; each measured Build starts at capacity0 and includes growth. No stage is removed or replaced.
2. Primary retained restoration-plus-copy: target1024/capacity1024, source512/capacity1024. Seed names are evens0..2046. Source is seed positions512..1023 (evens1024..2046), sharing exactly those lhs Name representatives as a lifecycle intersection would. Every measured operation includes CopyFrom(seed) restoration, CopyFrom(source), and count/last-name consumer. This is a full public copy cycle, not the lifecycle call alone or a pure copy kernel.
3. Separately classified preprepared single-copy diagnostic, same1024←512 geometry and capacity1024 for both. Prepared targets are independently constructed before the counters and used exactly once. Each measured operation is CopyFrom(source) plus the same consumer. There is no restoration, target construction, copying of a target, inner stopwatch, or allocation-byte accounting within the operation loop. Source is unchanged. The loop/delegate/index/consumer cost is included, never subtracted.
4. Retained copy0←0, capacity0.
5. Retained copy1←1, capacity1; equal names with distinct source representatives.
6. Retained copy1←0, capacity1; a genuinely nonempty destination is cleared.
7. Retained copy2←1, capacity2; shrink to singleton and clear the spare reference slot.
   All four small guards include CopyFrom(seed) restoration inside every measured operation, then CopyFrom(source) and consumer. Source/seed are unchanged. The empty-to-empty guard alone makes no nonempty-clear claim.
8. C2 late append, new target: source1023, target1024/capacity1024.
9. C2 late append, retained target capacity2048.
10. C2 late right-alias SetUnion(source,target), new target capacity1024.
11. C2 late right-alias SetUnion(source,target), retained target capacity2048.
   C2 target names are evens0..2046; source is evens2..2044 plus2045, missing only sorted source position1022. Result count1025. New target construction/growth remains inside counters, expected16496B/op and actual result capacity1025. Retained restoration remains inside counters, retaining capacity2048. Append keeps target representatives; right alias keeps explicit-left/source representatives. These allocation/capacity checks fail after preserving the numeric record if violated.
12. Singleton lifecycle guard, unchanged sequence through final Clear, union1/intersection1. It is not substituted for lifecycle1024.

## Prepared pool: memory, cache context, and fail-stop bounds

This diagnostic requires a64-bit process. Pool construction is sequential, after a separate fixture check and64 warmup single copies on64 separately constructed disposable targets. None of those objects enters the measured pool. Each pool target begins count1024/capacity1024 with seed references. Targets are consumed once in ascending index order; no target is restored or copied before its measured use. The first measured target is the oldest prepared target. Its storage was fully written during Build and subsequently left untouched by copy while later pool targets were prepared. This is not a cache-cold or first-JIT-invocation claim. A shared source remains readable throughout.

- Maximum16384 targets per process. Maximum backing reference-array payload =16384×1024×8 =134217728B =128MiB.
- Outer pool reference-vector payload at maximum =16384×8 =131072B =128KiB. These are payloads, not full object/array sizes.
- Pool setup current-thread allocation, from just before outer vector allocation through all target Builds, has a hard150994944B =144MiB ceiling. This includes actual allocated vector, container/list/backing-array object overhead during pool construction. If exceeded, stop before counters. Never lower a frozen count, replace the sample, or retry to evade the limit.
- Setup allocation bytes and GC0/1/2 deltas are recorded separately outside the stopwatch. There is no setup timer and no forced GC. Setup GC counts describe context, not inferred pauses.
- Registry, JIT, source/seed/check/warmup objects, reflection/postvalidation allocation, runtime native memory and whole-process RSS are outside this pool budget. No whole-process peak-memory claim is made.
- After numeric output is flushed, validate every prepared target, its exact representative identity/order, and all spare backing slots. Each actual target capacity must still be1024. Record total actual pool capacity/backing payload, actual source and seed capacities, and actual setup allocation. Postvalidation may allocate and is outside counters.
- The pool's different cache and memory-pressure context is reported separately from the repeated hot retained copy cycle and lifecycle. No subtraction of medians, timer normalization, or component synthesis is permitted.

## Stages, fresh calibration, and hard launch budget

Timing requires explicit source/harness/protocol review and parent approval. `--reviewed` is only an operator assertion that those approvals and the two-source semantic pass exist; it grants no authority itself.

1. Two Release/net8.0 builds with separate output/intermediate directories and identical inputs. `build_snapshots.sh` creates an output-local global.json pinned to DOTNET_SDK_VERSION (default8.0.425), rollForward disabled, and runs all dotnet commands there. Host default SDK selection is never assumed. Builds do not invoke the target. Record SDK/runtime and hash host, source inventory, compilation inputs and SDK selection/receipt.
2. Freeze full source identities, all binary-directory files, source pins, build manifest, harness/protocol/grid, and environment before any fixture or pilot.
3. Exactly24 fresh-process untimed fixtures:12 cells×2 versions,64 warmup operations and one checked validation operation per process. The prepared fixture uses separate warmup objects and one independently prepared target.
4. R-only fresh calibration. Start min(1024,cap) calls. At most4 new pilots per cell, at most48 pilots total. If duration is at least250ms, stop selection. Otherwise scale count toward300ms. Stop at cap or fourth pilot even when short. Never inspect RMinusC1 speed, historical counts/ratios, or use a contender-dependent count. The prepared cell retains its16384 cap even if it cannot attain250ms.
5. Freeze one common count per cell and counts SHA256; no further adjustment.
6. Exactly96 formal fresh-process invocations:12 cells×4 pairs×2 versions. Per-cell pair order is AB, BA, BA, AB. Each version has two first and two second positions. One cell and one numeric sample per process, never competing .NET workloads.

Maximum24 fixtures +48 pilots +96 formal =168 target launches, up to144 numerical samples. No retries or resume in the same evidence directory. Source/fixture/build failures, timeouts, interruption, allocation/capacity anomalies, and short durations remain visible; they do not authorize replacements or extra variants.

One combined600s hard wall budget includes both measurement builds, freeze, fixtures, calibration, and formal collection. `run_bounded.sh` sends TERM at595s and allows5s cleanup before forced kill. Each target child separately has a maximum60s wall timeout. The runner's local stage ceiling does not reset/extend the outer combined ceiling. No human-review pause is built into this single invocation. Failure or budget exhaustion preserves partial evidence and ends this round. Nominal formal measured time would be24s if all96 samples reached250ms; overhead and prepared-pool validation are additional.

Ordinary count cap =min(67108864, floor(2^30 / [8×(sourceCount+targetCount+1)])). This conservative bound remains below signed32-bit List version wrap including restoration, mutation, cleanup and64 warmups. Prepared count also caps at16384 and128MiB backing. Int64 sink remains far below overflow. No live enumerator spans mutation.

## Counters, validation, process hygiene, and evidence

No forced GC. Registry/setup/fixture check/warmup/preparation are outside counters. Retained restoration, new construction/growth, consumers, lifecycle union/intersection allocations and final Clear remain inside their specified boundaries. No hidden copy/timer, subtractive timing or measured Runtime instrumentation exists.

Each numeric record contains actual calls, Stopwatch ticks/frequency/duration, ns/op, total/per-operation allocated bytes, GC0/1/2 deltas, sink, geometry/context/classification and explicit short flag (<250ms). Flush this JSON before all postvalidation/reflection/capacity census. A later validation failure never erases the numeric sample. Validate content, sorted uniqueness, enumeration, Name identity, sources unchanged and cleared spare slots. For transient new/lifecycle outputs, postvalidate the final output and full lifecycle results; do not claim all discarded outputs were individually checked. For the prepared pool, validate every output.

Inherited DOTNET_/COMPlus_/CORECLR_/COR_ switches are removed, removed names recorded; both tiering switches are0. Remaining loader-variable names and machine/runtime identity are recorded. No profiling configuration enters formal children. Each target uses a new session/process group. Protected spawn/assignment and protected kill/reap/terminal-receipt sections handle SIGTERM/SIGINT races. An active target group is killed/reaped on failure or timeout; raw stdout/stderr and launch/terminal records use exclusive creation. Source/harness/binary inventory is checked at stage entry and successful exit, final counts again at comparison exit. A failed/incomplete stage cannot establish acceptance.

## Reporting and predeclared stopping interpretation

Flatten all available pilot/formal numeric rows, including failed validation and incomplete stages. Report actual absolute durations, each paired RMinusC1/R ratio and sign in pair order, per-version ns min/median/max, paired min/median/max, all shorts, allocation anomalies, GC and capacity context. Separate the prepared single-copy diagnostic from the restore-plus-copy workflow. Keep all small-copy, lifecycle1 and C2 guard results, including adverse results. Four unanimous paired signs have two-sided sign-test p=0.125 before multiplicity; medians alone establish no effect. Shorts remain inadequate-duration observations regardless of favorable signs.

- Report all four pair signs independently for lifecycle1024, retained restore+copy and preprepared single-copy. Direction is called consistent only when all four ratios are below1 for each relevant boundary; retain every ratio and quality limitation rather than applying a historical A/A percentage threshold.
- Consistent benefit from removing C1 in lifecycle1024 and both copy boundaries may support proposing a future isolation study. It never proves historical causality, authorizes a new variant/run, or justifies an automatic production change. The prepared diagnostic's short-duration and memory/cache caveats remain explicit.
- Copy-only benefit does not explain the lifecycle result. Disagreement between copy boundaries limits even a future-isolation inference.
- Mixed signs, no reproduced lifecycle benefit, or disagreement that prevents the above interpretation records the cause as unconfirmed and stops this investigation round. No additional variant, favorable rerun, profiler, or A/A repeat follows.
- Production stays R in every outcome. .NET8 Linux evidence does not certify Unity Mono, IL2CPP, or device performance.
