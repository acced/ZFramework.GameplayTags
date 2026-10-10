# Bounded workspace regression diagnostic

Supplemental investigation of the unchanged `n4/overlap50/workspace-reused` regression in exact candidate 4df49f4. It cannot replace the full frozen matrix, discard its unfavorable samples, or establish broad acceptance by itself.

## Protocol

The runner compiles unchanged Runtime snapshots into distinct identities alongside generated copies of frozen Evolution/Program.cs. Only Measure and final reporting are adapted. Original fixture construction, traversal order, Action bodies, preflight/postflight and the original 64-object retained cohort procedure remain present. Retention deltas here are not evidence of retained-memory changes.

Six predeclared controls are timed: n4 × overlap0/50/100 × intersection-reused/workspace-reused. All other 125 Action rows execute65 calls outside the selected timers, preserving semantic traversal but not the original full-matrix allocation volume. Thus this is deliberately a different diagnostic process context, which must be considered if a regression disappears.

Two modes:
- `aa`: one loaded before assembly/producer; the exact same Action delegate and fixture instance are invoked in both reporting slots. The labels before/after denote slots, not different sources. Manifest SlotSources records this explicitly.
- `ab`: separately named before/after source assemblies, each with its own fixture and static registry. Both producers park before either delegate is warmed/timed; no producer preparation runs during a selected timer. Host invokes the original Action directly with no reflection or adapter call per operation.

Per case,64 deterministic warmup calls per slot; GC before each measurement; raw wall/CPU/allocated bytes/GC and checksum deltas. Slot order alternates by round and selected case. Fourteen independently launched host processes are statistical units; all raw samples are retained. Iterations.tsv uses10million calls for each selected row, shared between variants and modes, and retains all131 original IDs for traversal validation. Batch flags remain visible if the actual machine is faster than expected.

A separate untimed inspection process runs before the timing processes. It captures relevant method raw IL hashes, token-resolved/assembly-identity-normalized IL hashes/text, locals/method flags, and .NET JIT disassembly. Dynamic native addresses are not bytecode semantics; compare native instruction text separately with addresses normalized. Inspection hashes and JIT output must not be treated as the exact native address layout of later timing processes. Methods of Container, TagStorage, Program and selected delegates are recorded. Static registry data and private user data are not inspected.

The original private fixture code is compiled into each assembly. Running preflight and postflight still validates explicit names, ancestor closure, alias paths, counted inputs, removals and registry checksums. A/A and A/B final aggregate checksums intentionally need not match one another because A/A shares a checksum accumulator for both selected slots; within each mode they must match across all rounds.

## Commands

From repository root, run sequentially, each with a fresh output directory:

`python Tests~/GuardRecheck/run.py --before ORIGINAL_SNAPSHOT --after CANDIDATE_SNAPSHOT --before-revision EXACT_ORIGINAL_SHA --after-revision EXACT_CANDIDATE_SHA --mode aa --rounds 14 --iterations-file Tests~/GuardRecheck/iterations.tsv --out AA_OUTPUT --dotnet DOTNET_PATH`

Repeat with `--mode ab --out AB_OUTPUT`. Build all mode-specific assemblies before the mode's sampling starts. `--compile-only` prepares commands without execution. `--rounds 1` is a smoke only. `--skip-inspection` is explicit and should not be used for the CI evidence collection requested here. Standalone Mono is supported through `--mono PATH`, but native disassembly capture is a .NET diagnostic.

Source, generated-source, reference, assembly and map hashes, declared revisions, runtime/SDK, CPU and affinity are recorded. Raw source hashes are authoritative for archived or dirty snapshots. No user-facing acceptance count is changed by this runner.
