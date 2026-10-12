# Binding clarification: one fixed diagnostic

This supplements the binding screen at commit `4cc968fe868d18f8f17540182b69d22d302950ed`. It preserves that screen and its original evidence. It does not clear the adverse original-context .NET dispatch or first-empty observations, and it is not qualification. All result summaries set `AcceptanceEvidence=false` and `OriginalContextCleared=false`.

## Fixed design

For each backend, run four pairs in each comparison for each of five fixtures: the two unchanged warm controls (`direct-subscription`, `direct-dispatch`) and all three original first-use fixtures (`first-empty`, `first-present`, `first-external-multicast`). This is exactly 80 measured fresh processes. There is no pilot, count adjustment, replacement sample, extension or follow-on gate.

AA assigns both labelled slots to the same physical original screen assembly and the same dependencies. AB assigns left=original and right=candidate. Within each fixture, pair order is left/right, right/left, left/right, right/left. Comparison order alternates AA/AB, AB/AA, AA/AB, AB/AA. Warm fixtures run first, then the three cold fixtures, in the order listed in configuration. Every ordinary result is retained and the complete declared sequence runs before diagnosis. Integrity failure, interruption or the single 600-second overall deadline may stop it.

Warm iteration counts are copied unchanged from the original CI artifacts. .NET: subscription 7,979,098; dispatch 3,812,423. Mono: subscription 1,485,640; dispatch 718,093. Configuration retains both complete original nine-row maps, their original map hashes and the iteration-artifact hashes. The unchanged full map is recreated and rechecked throughout; only the two selected control counts are passed to the launcher.

## Exact reuse and different context

The frozen original coordinator is invoked with `--preflight-only`, in the same Python process and with the remaining whole-run deadline. It materializes the exact original/candidate sources, builds all six original binaries and runs its existing policy, core, focused and fixture checks. It performs no cold calls, pilot or timing. Its output remains in the private work area. The supplement then compiles one separately named, tiny launcher using the original helper's pinned compiler and references. All compilation finishes before measured calls.

The original `Screen.cs`, runtime sources, compiled before/after screen assemblies, their configuration files, and operation bodies are untouched. The launcher loads one existing screen assembly, invokes its existing `Init`, selects existing `Make(7)` or `Make(8)`, then invokes existing `Warm` with the original count. Reflection exceptions are unwrapped and reported privately; they produce nonzero exit status. Loading, reflection lookup, registry initialization, fixture construction, the reflection invocation itself and launcher argument parsing occur outside Warm's existing internal timer/allocation-counter boundary. Warm's original probes, 1,024 warmup cycles, operation, restoration, timer, counters, event checks and final-state checks remain unchanged. Untimed launcher fixture results must exactly match the original preflight results.

This is a distinct fresh-process warm-control context with no preceding allocating benchmark rows. The launcher adds assembly-loading/reflection/setup context outside measurement, and the original nine-row map-file parsing is replaced by fixed launcher arguments. The supplement is not represented as a rerun of the original full-row process or proof that context was neutral.

Every cold sample invokes the original screen executable's own entry point directly with its original `cold` arguments. No launcher, reflection bridge or first-use timing boundary changes are involved. The original first-Bind bridge and its first JIT cost remain as originally measured.

SDK 8.0.425, CLR/reference pack 8.0.31, CI Mono 6.8.0.105, C#9 optimized deterministic build, ReadyToRun=1, tiering=0, server GC=0 and disabled roll-forward are unchanged. The runtimeconfig is copied byte-for-byte to the separately named launcher. Actual runtime fields remain in every observation. Original source, binary/dependency, helper, toolchain, map, launcher and configuration hashes are checked before/after each invocation and at finalization. The whole 600-second deadline starts at entry and includes original preflight, launcher build and all samples; it is never restarted for a nested phase. Child process groups are killed and reaped on every exit path.

## Reporting and stopping

Record each invocation's fixture, family, comparison, pair, labelled slot, source, ordinal, exit code and complete allowlisted numerical observation, including every byte/tick/frequency/GC sample. Keep valid numerical rows even when semantics fail. AA right/left drift and AB candidate/original ratios are shown separately. Never subtract AA from AB, discard a negative/short row, or infer acceptance from an AA explanation. Allocation differences, timing direction, short warm batches, GC and semantic agreement are visible.

`--preflight-only` builds and runs untimed semantic/launcher checks and validates the 80-call command plan. It does not execute any cold or warm measurement. Full execution omits this flag. CLI matches the existing portfolio workflow: `--backend`, `--dotnet`, `--mono`, `--nuget`, `--git-root`, `--out`, `--work`. Both output/work directories must be new, separate and non-nested.

Only the six JSON paths in `upload-allowlist.txt` may be published. No commands, local paths, environment dumps, raw stdout/stderr, build logs, response files, private evidence, PDBs or binaries enter public output. Existing screen sources and prior evidence must not be edited or replaced.
