# Crossover research — executed results, 2026-10-01

This experiment executes the four requested independent investigations. Production Runtime, Editor, serialized assets and Query are unchanged. PR remains draft, no merge/release, no claim that every workload wins.

## Exact versions and completed runs

- Base PR10: `40696c95b6c53e95ef6300c995f01c9b443f0a3e`; original Auto RuntimeTagSet blob `d19c0c635e8a2ecc9b5408f9e3df8b53440a176c`.
- Primary measured commit: `c3ef276e3fb10760875cf83a29cd27b2ff63d331`, tree `aacb505bf38931372adccdd48923f87f0ce46595`.
- Span supplement measured commit: `7f390c2fa3afb141c5b1683a7c55f1cdeb6c5c22`, tree `6504c6c35430e7abec942722558c6032cef4a396`. It does not change the primary source.
- [Primary x64/ARM64 CI](https://github.com/acced/ZFramework.GameplayTags/actions/runs/36858242259).
- [Span x64/ARM64 CI](https://github.com/acced/ZFramework.GameplayTags/actions/runs/36859050373).
- [Primary x64 source/raw evidence](https://github.com/acced/ZFramework.GameplayTags/actions/runs/36858242259/artifacts/11161430322).
- [Primary ARM64 source/raw evidence](https://github.com/acced/ZFramework.GameplayTags/actions/runs/36858242259/artifacts/11161675308).
- [Span x64 source/raw evidence](https://github.com/acced/ZFramework.GameplayTags/actions/runs/36859050373/artifacts/11161330446).
- [Span ARM64 source/raw evidence](https://github.com/acced/ZFramework.GameplayTags/actions/runs/36859050373/artifacts/11161305558).

Artifact SHA256 in that order: `dc93e8f0c9e69809be81f53ba45c1b188a63bfc17e5ca32cc2de834833046e04`, `a260c02b6b119147ea9946bff2ce0b999b3d248e79d916d39083b564588ad993`, `13ca555c775427aa2d07c5e1135f2883eef837adef04d2589c4eddeebefebad5`, `9517f51939b1d071d65ed9836ba74c18e0d14d8222b855672fde3fb08306bd82`.

## Actual environment, not the intended SDK setting

Ubuntu 24.04, x64 and ARM64; net8.0, actual runtime .NET 8.0.31. **The CLI selected SDK 10.0.401.** The workflow installed 8.0.425 but had no global.json, so this is NOT an SDK8.0.425-pinned measurement. All within-run candidates and controls used the same actual SDK. For exact reproduction, pin 10.0.401 in a separate worktree and verify dotnet --version before running; changing the SDK requires all controls to be rebuilt and measured together.

Tiered compilation and ReadyToRun disabled; one allowed CPU. Three process rounds, seven retained samples each. Calibration targets ~1ms subject to a 16MiB/sample allocation budget and repeat limit; raw elapsed sample durations remain visible. GC preparation is outside timing; allocation, all generation collection deltas, output writing and accurate Count are inside complete-operation timing. No slow samples were deleted.

The new fixture is flat B.T... definitions with an implicit B ancestor, not the prior grouped hierarchy fixture. **Do not compare its absolute ns with historical PR scores.** Local Debian x64 uses SDK8.0.423/runtime8.0.29 and is archived separately as diagnostic-only because A/A drift is large. No actual Unity/Mono/IL2CPP/Burst or mobile hardware validation.

## 1. Public query crossover and actual caller code

All variants retain direct independent copy and DenseFusion, except the literal original control. Sparse boolean lookup alone changes: binary, linear, vector, and a predeclared bounded rule (<=2 linear; one to two vector widths vector; otherwise binary). Insertion and DFS lower-bound behavior stay unchanged. QueryLoop is the actual timed public HasTagExact caller, not an isolated comparison.

21 sizes 0..512 with vector-width neighbors, two seeds, fixed middle/random hit/mixed/high-miss probes. Each batch has 256 probes; reported ns are normalized per public call. Spare capacity contains a removed ID and cannot be searched as live membership.

Primary ARM64, mixed probes, seed20261001, ns/call:

| Members | Binary | Linear | Vector | Bounded |
|---|---:|---:|---:|---:|
| 1 | 4.248 | 2.087 | 1.992 | 2.236 |
| 8 | 9.005 | 4.156 | 3.506 | 3.768 |
| 16 | 11.850 | 5.627 | 5.164 | 12.365 |
| 32 | 16.138 | 9.500 | 8.560 | 16.468 |
| 64 | 19.899 | 18.796 | 14.646 | 20.169 |
| 128 | 23.492 | 35.891 | 27.487 | 24.004 |
| 512 | 31.524 | 127.958 | 106.202 | 32.217 |

Across all168 queries, relative to BOTH binary and byte-identical binaryAA, >=5% faster every round / >5% slower every round:

| CPU | Linear | Vector | Bounded |
|---|---:|---:|---:|
| x64 | 77 / 78 | 115 / 29 | 109 / 0 |
| ARM64 | 102 / 56 | 103 / 54 | 52 / 34 |

These are screening counts, not confidence intervals. **Bounded is not approved cross-platform.** The crossover depends on hit position/probe pattern as well as count; odd-sized fixed-middle probes may hit binary search immediately. No universal threshold was inferred by stitching fastest rows.

Actual primary QueryLoop code size: x64 binary261/vector387/bounded527 bytes; ARM64 356/476/696 bytes. Vector comparison is inlined and contains VPCMPEQD/VPTEST or CMEQ. x64 binary still calls IndexOf. Thus codegen/inlining and algorithm differ; do not attribute all gains solely to SIMD.

Actual OperationLoop plus called DenseFusion Union/Difference were captured: x64 VPSHUFB/VPSADBW and ARM64 CNT/UADDLP/ADDV. This is current-source machine code, not screenshots borrowed from previous candidates.

### Simpler BCL competitor

Span.IndexOf, span32 and span64 are separate supplement builds with fresh controls. They also fail the complete query gate. x64 all168 query counts (fast/slow relative to both same-run controls): Span32/91, span32 9/124, span64 17/115. ARM64: Span67/91, span32 49/102, span64 60/90.

The actual Span caller retains a call to ContainsId; internal SpanHelpers were not fully disassembled in this pass. This does not prove BCL search is universally slow or identify a unique causal instruction. Keep the simpler API as a comparison, not an unmeasured claim of equivalence to the hand-written inlined path.

## 2. Forced representations and complete ownership costs

Same global IDs under Array/Dense/Micro; no restricted local tag domain. Universes10000/65536/262144, sizes0/1/8/32/128/1024/4096, contiguous/four-cluster/scattered, two seeds, union overlap0/50/100%. Deletion is an existing-half subset. Build, fresh result, actual Copy+Append/Remove and reuse contracts are separate.

Array and Dense use the same Runtime implementation with fixed representation parameter. Micro is PR8 DirectArray with an explicit copy-capacity normalization: source physical record capacity is preserved rather than trimmed. Its patch is archived. Inputs reserve for arbitrary member distribution; new Union retains its own existing record-capacity calculation, fully timed.

ARM64,262144 definitions,seed20261001,50% union overlap, ns/complete operation:

| Members/distribution/operation | Array | Dense | Micro normalized |
|---|---:|---:|---:|
| 8 scattered new Union | 48.52 | 2957.95 | 57.03 |
| 8 scattered Copy+Append | 69.84 | 3615.96 | 73.41 |
| 8 scattered Copy+Remove | 37.19 | 3510.16 | 43.90 |
| 4096 contiguous new Union | 6482.46 | 2954.77 | 652.15 |
| 4096 contiguous Copy+Append | 14823.06 | 3620.30 | 1577.35 |
| 4096 contiguous Copy+Remove | 5963.37 | 3654.45 | 1290.31 |
| 4096 four-cluster new Union | 8926.74 | 2979.57 | 834.51 |
| 4096 four-cluster Copy+Append | 16429.08 | 3623.46 | 1744.01 |
| 4096 four-cluster Copy+Remove | 5930.87 | 3592.02 | 1315.32 |
| 4096 scattered new Union | 11944.17 | 2945.47 | 13091.48 |
| 4096 scattered Copy+Append | 28653.31 | 3612.49 | 21502.12 |
| 4096 scattered Copy+Remove | 5931.38 | 3654.64 | 10147.73 |

All costs include independent buffers and exact Count. Fresh copy then mutation is never replaced by fused Union/Difference. Prepared rows are separate and0B.

Target8 scattered fresh B/op: Array136/192/104; Dense32848/32848/32848; Micro144/200/112. At4096 contiguous, normalized Micro copy+append/remove is16464B, not the much smaller historical compact-copy figure. Its new Union is2144B. Capacity normalization is not hidden.

x64 full evidence also retains unfavorable results: target8 scattered Copy+Append Array123.16ns versus Micro86.85ns; Array Union process medians57.90/121.06/57.21ns. No x64 all-workload stability is claimed.

A different frontier exists for initial input construction. ARM644096 contiguous, already-resolved public AddTag build Array/Dense/Micro is81.443/13.351/46.945 microseconds; subsequent Union favors Micro. Preparation and repeated operations therefore cannot share a naive byte-only rule.

**Forced_Mode_Frontier.csv is diagnostic, NOT a measured adaptive policy.** No selection scan, representation conversion or mixed-representation dispatcher was implemented by picking these rows. The next proposed selector needs N, universe words and occupied local blocks, declared operation/lifecycle assumptions, separately timed preparation/conversion, one active member representation and held-out validation. Four separated clusters show span alone is not enough.

## 3. Finite inline storage: actual allocation cost

Layout prototypes only, not complete mutable tag-set implementations. B/constructed object including headers and overflow arrays, identical on both CPUs:

| Members | Inline0 | Inline4 | Inline8 |
|---|---:|---:|---:|
| 0 | 48 | 64 | 80 |
| 1 | 80 | 64 | 80 |
| 4 | 88 | 64 | 80 |
| 5 | 96 | 112 | 80 |
| 8 | 104 | 120 | 80 |
| 9 | 112 | 128 | 144 |
| 32 | 200 | 216 | 232 |
| 128 | 584 | 600 | 616 |

Inline8 saves24B at8members but adds32B to every empty/overflow object. Inline4 adds16B.10,000 independent instances are retained through full collection in separate accounting; this is owned allocated storage, not process RSS. Overflow, mutability, sorting, aliases and Dense layout costs require full implementation before adoption. Two8-member inputs may produce more than8members, so a small-copy result cannot establish small-Union savings.

## 4. Unequal sorted arrays: independent intersection/difference

Full eager result output, accurate count, prepared0B; not a substitute for tag-set Copy+Remove. ARM64,50% overlap, ns:

| Large:small | Intersection merge | SIMD intersection | Gallop intersection | Difference merge | Gallop difference |
|---|---:|---:|---:|---:|---:|
| 32:32 | 71.42 | 69.84 | 99.72 | 67.65 | 259.81 |
| 512:32 | 405.38 | 151.20 | 300.34 | 561.11 | 438.29 |
| 8192:32 | 6055.83 | 1604.98 | 574.73 | 8681.07 | 1115.57 |

Fresh-output versions,0/50/100% overlap and all requested sizes/actual capped ratios are retained. A balanced difference should not automatically gallop. No SIMD Union or complete published Roaring algorithm suite is claimed.

## Correctness and independent recheck

On EACH CPU, each primary class passes413443 targeted assertions; relevant vector/bounded/Micro configurations repeat with hardware disabled. Four changed Runtime classes pass the original9-group/10808631-assertion IntegerTests, including DFS, Query, identity, capacity and aliases. The supplement adds three Span classes with the same full suites and disabled-hardware targeted checks. All failures0. Assertion counts include repeated builds and loops, not unique cases.

Ratio correctness performs1000 independent pairs ×5algorithms before timing. Normalized Micro's full original Micro suite was additionally run LOCAL ONLY with hardware on/off:5groups,5392728assertions,0failures; remote Micro has targeted checks. No false claim that the extra complete Micro suite ran remotely.

Primary EACH CPU:9042aggregate/27126raw rows/189882timing samples/99981zero-allocation samples. Supplement EACH CPU:1512aggregate/4536raw/31752timing samples, all query allocation samples0B. Downloaded artifacts, trees, unchanged production files, generated source equality across CPU, raw medians and all output CSVs independently reconstructed and match stored results.

Query-only identical-binary A/A:

| Phase/CPU | Rows | Every round within5% | Same-direction >5% all rounds |
|---|---:|---:|---:|
| Primary x64 |168|109|0|
| Primary ARM64 |168|165|0|
| Span x64 |168|48|1|
| Span ARM64 |168|155|0|

This does not validate stability of all bulk measurements. Local primary A/A is poor (9/168 within5%,6persistent shifts), hence local timing is diagnostic only and never mixed with remote winners. All slow samples remain visible.

## Bounded decision

Retain direct copy and Dense backend. Do not promote the current bounded/Span query thresholds across CPUs. Do not permanently add eight inline ints to every object, or apply galloping to balanced sets. Locality-aware forced curves now have real evidence, but a deterministic selector including its scan/conversion costs is NOT implemented or accepted yet. No production release, no universal winner.

References describe algorithm ideas, not project speed: [Roaring2017](https://arxiv.org/abs/1709.07821), [SIMD intersection2014](https://arxiv.org/abs/1401.6399), [Algorithmica](https://en.algorithmica.org/hpc/data-structures/s-tree/), [Span API](https://learn.microsoft.com/en-us/dotnet/api/system.memoryextensions.indexof?view=netstandard-2.1).
