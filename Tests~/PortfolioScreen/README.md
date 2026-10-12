# Small independent rejection screens

These four-pair screens decide whether further qualification is worthwhile. A green job is completion, never optimization acceptance. Current fully qualified count remains **0/10**.

- **DenseClear:** dense active hash indexes use bulk zeroing, while small and sparse indexes retain occupied-slot clearing. A fixed twelve-row screen covers empty, mask0, threshold63/64/65, density127/128/129 and retained sparse states.
- **Callbacks:** counted notification lookup carries a fresh sorted-table cursor through each root-to-leaf mutation. Twelve fixed rows include dense/exhausted paths, global-only and silent controls, 8,192-entry gap/midpoint/miss guards, Clear and descending bulk reset. Original f7 is the comparator.
- **Registry:** new immediate-parent lookup for unique leaves beneath an existing trunk. Missing-parent construction retains the original prefix order and reuses its known-missing prefix. No new retained fields. Original f7 is the comparison source.
- **Hierarchical:** no-parent, non-alias bulk ingestion avoids the retained snapshot array after bounds prevalidation. Parent, self-alias and anomaly behavior remain covered by the focused correctness harness.

Each backend builds both exact sources on one runner, runs focused correctness, then four alternating before/after rounds. Registry and Hierarchical counts are frozen from the fastest observations among all four prior **baseline-only** local samples, targeting 200 ms and never reducing a count. Callbacks and DenseClear each use one original-only twelve-row pilot, then freezes one shared backend map before any candidate timing, also targeting 200 ms. Local screens and their adverse/uncertain results remain historical evidence. Short batches in the new run remain short; no confidence or global noninferiority claim is inferred from four rounds.

The SDK is 8.0.425, CoreCLR is 8.0.31 and the separate Mono job records its actual standalone Mono version. Unity Editor/Player, IL2CPP and Alex are not covered. Full correctness, cold allocation, retained-state and unrelated hot-path qualification remain mandatory for any surviving candidate.

Public artifacts contain only source/assembly hashes, runtime/compiler versions, CPU model, fixed configuration, numeric raw results and bounded status records. Private compiler output, absolute command paths, temporary files and environment dumps are not uploaded. Failed commands retain status, exit type and stream hashes without publishing arbitrary diagnostics.

The ten-minute internal limit and fifteen-minute job limit are hard bounds. An interrupted screen remains incomplete. No extra rounds, discarded samples or automatic full qualification is performed. The callback and dense-Clear pilots are single-shot and baseline-only; its actual short batches remain disclosed.

## Current screen selection

DenseClear is selected for one bounded wrapper repair. Its [first screen](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38200701706) showed roughly 4–8% gains on several dense refill + Clear cycles, but .NET empty Clear was consistently slower. The exact source is held. Local untimed native inspection found a larger Clear register-saving frame, while both versions still called it out-of-line; this does not establish exclusive timing causality. See [the complete first decision](DenseClear/FIRST_SCREEN.md).

The repair keeps a tiny reset wrapper and outlines bucket work, preserving both count resets and the original constructor metadata. It passed the same 25 threshold/capacity cases and twelve untimed fixtures on both runtimes. The same fixed twelve-row screen will decide whether further qualification is worthwhile. Nonempty rows measure refill + Clear, not isolated Clear. No further revision or full matrix is queued if substantive losses persist. Root Runtime remains unchanged.

Both callback screens are held after their declared bounded repair. The [first screen](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38199196551) had single-node large-table hit/miss costs on both runtimes. The [one repair](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38199660773) retained four adverse .NET late-miss pairs and four roughly 11% adverse Mono sparse-gap pairs. No further callback revision, sampling or full qualification is queued. See [the first callback decision](Callbacks/FIRST_SCREEN.md).

The first [Registry and Hierarchical screen](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38196036334) completed. Hierarchical is held after small/empty-operation costs and weak large warm gains. Registry retained large shared-prefix gains but also adverse guard signals. See [the first screen decision](FIRST_CI_SCREEN.md).

The [split-only Registry revision](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38196715389) was slower on .NET implicit promotion in all four pairs by 16–32%. The [final conservative gate](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38197256601) still cost 4.1–7.8% in all four .NET promotion pairs, with identical allocation and GC counts. Mono promotion was near neutral, but duplicate-depth8 became 0.4–1.9% slower in four long batches. That bounded registration series is stopped; no full qualification or additional sampling is queued. All earlier sources, maps and results remain historical evidence. Fully qualified count stays **0/10**.
