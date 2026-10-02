# Direct bulk construction experiment

Base: PR11 `0587201567aa21edb60055acf5028faf8d659b39`. This stage changes preparation only, before fitting a representation policy. Production Runtime/Editor/Query/serialization and all standalone set-operation methods remain unchanged.

## Public candidate APIs

`FromSortedUnique(registry, ReadOnlySpan<RuntimeTag>, capacity, storage)` validates all ownership and strict ordering, then writes one independently owned representation. `ResetFromSortedUnique(span)` validates before clearing/growing/writing the output. Capacity-sufficient resets must allocate zero bytes. `FromUnordered` validates resolved handles, owns a temporary integer workspace, sorts/deduplicates, and materializes a separate result. Its workspace and sorting remain inside timing. Unlike legacy AddTag, the new strict entry points reject default handles; this is an explicit new API contract, not a change to existing AddTag behavior.

The input cannot be modified concurrently. No permanent validated wrapper refers to mutable input. Sorted input length is the exact member count; no PopCount is required to rediscover that count. Dense groups a word before writing it; Micro groups a local key; Array fills IDs directly. No cache, pool, COW, borrowed result, extra per-instance field, trimming, or implicit representation conversion is added.

Reservation matches pre-sized repeated Add: max(requested capacity, raw input length), clamped to registry count. Micro retains its existing arbitrary-distribution reservation policy and narrow 2^20 registry limit. Micro copy is normalized by the fixed PR11 generator, not silently changed again. General input pays an extra workspace; sorted-input results must match control allocation sizes exactly.

## Experiments and boundaries

Forced Array/Dense/Micro; repeated Add versus new APIs on identical input. Three registry sizes, seven counts, contiguous/four-cluster/scattered, two new seeds. Sorted, reversed, shuffled and duplicated inputs. Fresh construction, prepared overwrite, and build-plus-1/16-unchanged-unions are distinct contracts. These composites do not replace the original four runtime benchmarks. Full same-binary A/A applies to construction cases, with all raw samples retained.

SDK8.0.425 is pinned through global.json in the working build root and verified by the CLI. net8.0 Release, ReadyToRun/tiering off, CPU affinity. Per-case calibration targets 1ms with a16MiB allocation cap, seven samples and three shuffled process rounds. A 5% every-round screen is not a statistical confidence interval. Absolute historical scores are not combined with this run.

Every new class executes strict-input, 16-bit-mask, capacity, dirty-output, independence and post-build mutation tests. Original full Runtime/Query tests and normalized Micro tests are also run. Separately compiled netstandard2.1 libraries are called from a net8 host; this is not Unity/Mono/IL2CPP validation. Generated C#, exact checkout, actual caller disassembly, raw data, independent reconstruction and CPU-specific reports are archived.

Run: `python3 Audit~/BulkConstruction/run.py --output artifacts/results --rounds 3`, after archiving HEAD as `artifacts/source.zip` and recording SOURCE_COMMIT/TREE. Then `python3 Audit~/BulkConstruction/review.py --root artifacts`. The GitHub workflow does this and independently redownloads both artifacts.

Background mechanisms: LLVM Programmer's Manual sorted-vector bulk construction (https://llvm.org/docs/ProgrammersManual.html); .NET Array.Sort documentation (https://learn.microsoft.com/dotnet/api/system.array.sort). These support implementation mechanisms, not a project-specific speed claim.

Status at submission: source implemented, execution pending. No adaptive policy, Run container, production migration, merge, release, or claim that all performance points win.
