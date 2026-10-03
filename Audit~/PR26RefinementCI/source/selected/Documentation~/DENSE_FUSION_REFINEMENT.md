# Dense fusion refinement — implemented and verified on x64 and ARM64

## Decision

The continuation is implemented, committed, executed on real Linux x64 and ARM64 runners, and independently rechecked. **The full all-workloads requirement remains unmet; production Runtime is unchanged.** These are .NET 8 managed runs, not Unity, Burst, Android or iOS IL2CPP acceptance.

Retain the specialized DenseFusion backend and ordered SIMD query as bounded candidates. Prefer **FusedKernelSet** (query + backend only) as the simpler integration candidate for further work. **FusedBitmapSet** adds a fresh-Dense-Union shortcut; keep it as measured experimental evidence, not a selected production feature. Its extra branch has small/mixed incremental gains rather than a demonstrated consistent cross-platform advantage. No raw per-member ID fallback, pool, COW or delayed Count was introduced.

The original fresh scattered-Union warning was not reproduced in this run, including in the unchanged previous-vector control. Do NOT credit its disappearance entirely to this refactor or claim its original cause has been established.

## Commits and evidence

- Measured source: `b46ef771ac832135b6cc8336303aabaa49e7523f`.
- Measured tree: `d60f42a6f1902a5b9c92006aef554b4fe743df73`.
- First kernel commit: `2981f978f7671436020286c01dae1aeb6b6ec0c5`.
- b46 changes test-only friend-assembly setup, not the kernels or timing fixture.
- Independent reviewer: `f5acd71e4925479bc9c41dbd75cb054a38cfd278`.
- Fixed controls: Auto `84729cc`, Micro `9be85fc`, previous triad `5cb16ca`.

[Dual-architecture benchmark CI](https://github.com/acced/ZFramework.GameplayTags/actions/runs/36832367838)

[Independent source/raw-result recheck](https://github.com/acced/ZFramework.GameplayTags/actions/runs/36833333004)

[Complete reviewed source, generated candidates, raw data, CSV and Chinese report](https://github.com/acced/ZFramework.GameplayTags/actions/runs/36833333004/artifacts/11147617397)

Reviewed artifact SHA256: `fa696b613b4fade86265e09e39a7da0bbc4e8ec590ac3de04491cd350d066f14`.
Original x64 artifact: `2c21139c810f16b03bfbcfbc74812d12cf874e7341aefe05b5ad85a0edbfa308`.
Original ARM64 artifact: `adb2ce87de10056d36f110cdd114490dff01eb788d6a658658fdeecf96bec25e`.

The independent reviewer reconstructs all source/reference Git trees, checks 64 production files against Auto, verifies generated candidates are identical across architectures, and recomputes all medians, per-round rankings, allocation samples, input digests and GC diagnostic totals. **Per architecture:** 4,464 aggregate container rows; 13,392 raw rows; 93,744 timing samples; 46,872 prepared allocation samples, all zero; 60 separate diagnostic aggregates. Samples/assertions are not independent test-case counts.

No existing baseline branch or main was modified, merged, tagged or released. All changes are under Audit~, Documentation~ and dedicated CI workflows. Source.zip in the archive is an experimental checkout, not a production replacement package.

## Actual changes

1. Dedicated OR, AND and AND-NOT loops: no per-word operation selector. Writes and accurate cardinality are completed in one pass.
2. AVX2: hoist nibble lookup/mask values outside the loop; use two independent accumulators and process eight ulong words per main iteration. Tail handling remains exact.
3. ARM64: implement AdvSimd/NEON byte population counts, pairwise widening sums and final reduction. Its selection and emitted instructions are checked on an actual ARM64 process.
4. .NET Standard 2.1: preserve a scalar SWAR implementation. Compile it into a separate library and run the full candidate tests against that compiled library.
5. Combine the already-tested ordered SIMD HasTagExact with the new backend in a literal generated FusedKernelSet. Generate FusedBitmapSet separately, adding only the independent nonempty Dense Union shortcut. Both are archived; no per-row winner substitution.
6. Add a same-assembly generic-caller diagnostic for independent union, copy-only and empty-Dense construction, with per-case and per-sample settling and Gen0/1/2 counts. This is separate from the historical fixture and never subtracted from full-operation times.

## Correctness executed on EACH architecture

| Check | Result |
|---|---|
| New Dense output/count/alias/tail suite | 4,864,106 assertions, 0 failures |
| Same Dense suite with HW intrinsics disabled | 4,864,106 assertions, 0 failures; repeated configuration |
| FusedKernelSet original Micro full suite | 5 groups, 5,392,728 assertions, 0 failures |
| FusedBitmapSet original Micro full suite | 5 groups, 5,392,728 assertions, 0 failures |
| Both generated candidates with HW intrinsics disabled | Same suites pass again; do not double-count coverage |
| Separately compiled Standard 2.1 scalar library | Full 5,392,728-assertion suite actually executed, 0 failures |
| Prepared backend/container allocation | 0 B; 152 B positive allocation control |
| Actual selected backend | AVX2 on X64; NEON on Arm64 |
| Native Unity / Android / iOS / Burst | NOT executed |

First run 36832013621 compiled and passed the kernels and in-assembly suites, then failed the separately compiled portable test host because internal settings.ReplaceAll was inaccessible. b46 adds temporary test-library InternalsVisibleTo metadata only. Production API visibility is unchanged. Failed-run artifacts are retained; no timing from the failed run is mixed into final tables.

x64 disassembly contains real VPSHUFB/VPSADBW/POPCNT; ARM64 contains CNT/UADDLP/ADDV. Substring marker counts are only search aids (for example 'cnt ' also occurs in x64 'popcnt '), not independent instruction evidence.

## Measurement contracts

.NET 8.0.31, SDK 8.0.425, Release, tiered compilation disabled, one allowed CPU per process. Three independent rotated/reversed rounds, seven samples each, median of process medians. Shared runners are not isolated target devices and the every-round 5% rule is a screening criterion, not a formal confidence interval.

Original fixture retained: 10,000/65,536/262,144 explicit definitions plus implicit ancestors. Small member counts 0/1/7/8/9/31/32/33/127/128/129; large 128/1024/4096; contiguous and fixed-seed scattered members. Main bulk relations are half-overlap union and existing-half-subset deletion. Fresh output includes allocation, initialization, actual copy, mutation, stores and eager Count; prepared output is separate. Copy+append/remove are not fused substitutes. Exact-query changes are not inferred from timing changes in unchanged methods.

Small comparison uses adaptive Auto/Micro. Large comparison uses forced-Dense Auto, Micro and previous vector; adaptive Auto is also a separately reported control. **Main/optimize/Alex/indexed/packed were not all rerun**, so this is not a universal cross-library ranking.

## Large fresh results: X64

10,000 definitions, 1,024 members, ns/op. All columns in this table use Dense storage. Kernel-only is the preferred smaller candidate; extra shortcut remains an experiment.

| Distribution / operation | Auto Dense | Micro Dense | Previous vector | New kernel only | New + fresh shortcut |
|---|---:|---:|---:|---:|---:|
| contiguous union | 337.284 | 272.249 | 92.750 | 86.729 | 83.274 |
| contiguous copy+append | 356.267 | 297.674 | 130.912 | 131.215 | 128.611 |
| contiguous copy+remove | 362.111 | 277.311 | 136.080 | 130.461 | 128.982 |
| scattered union | 771.225 | 691.702 | 94.797 | 85.457 | 81.991 |
| scattered copy+append | 359.626 | 302.128 | 128.963 | 127.750 | 127.045 |
| scattered copy+remove | 364.109 | 274.863 | 130.960 | 122.845 | 125.546 |

The small incremental differences between new variants are not proof that the extra fresh shortcut is worth its branch. Their copy/mutate implementations are identical; changes in those timing rows are not attributed to the constructor shortcut.

## Large fresh results: ARM64

Same logical fixture, separately measured machine. Previous vector has no ARM vector backend and falls back; it is not an AVX2 execution on ARM.

| Distribution / operation | Auto Dense | Micro Dense | Previous fallback | New kernel only | New + fresh shortcut |
|---|---:|---:|---:|---:|---:|
| contiguous union | 576.436 | 323.284 | 541.358 | 156.500 | 148.095 |
| contiguous copy+append | 606.190 | 400.652 | 613.674 | 231.976 | 215.180 |
| contiguous copy+remove | 600.365 | 391.988 | 591.396 | 219.769 | 209.878 |
| scattered union | 579.353 | 331.129 | 546.346 | 151.723 | 154.436 |
| scattered copy+append | 603.368 | 394.577 | 593.788 | 207.688 | 212.818 |
| scattered copy+remove | 598.801 | 386.725 | 586.782 | 208.556 | 211.691 |

Fresh nonempty results at this size continue to allocate 1,352 B for Micro-based candidates, 1,344 B for Auto. New backend introduces no extra per-set fields or second representation.

## Entire large bulk matrix, stricter controls

Each cell compares against the fastest of Auto Dense, Micro Dense and previous vector at the same case. Query rows are separate and are NOT included in these 54 bulk rows.

| Architecture / variant / contract | Lower median | At least 5% in every round |
|---|---:|---:|
| x64 kernel-only fresh | 50/54 | 30/54 |
| x64 kernel-only prepared | 54/54 | 50/54 |
| x64 +fresh-shortcut fresh | 53/54 | 30/54 |
| x64 +fresh-shortcut prepared | 54/54 | 53/54 |
| ARM64 kernel-only fresh | 54/54 | 54/54 |
| ARM64 kernel-only prepared | 54/54 | 54/54 |
| ARM64 +fresh-shortcut fresh | 54/54 | 54/54 |
| ARM64 +fresh-shortcut prepared | 54/54 | 54/54 |

This shows actual ARM64 gains, not completion of the original all-workload requirement. All slower rows remain in architecture-specific CSV and raw JSON.

## Small scattered sets still fail the overall goal

262,144 explicit definitions / 8 scattered members, fresh contract, ns/op. The integrated column is the actually measured FusedBitmapSet, not an unmeasured synthetic combination.

| Architecture / operation | Auto | Micro | SIMD query-only | Integrated |
|---|---:|---:|---:|---:|
| x64 exact | 4.080 | 4.409 | 2.497 | 2.456 |
| x64 union | 53.020 | 41.035 | 40.898 | 40.247 |
| x64 copy+append | 53.183 | 55.296 | 57.004 | 55.066 |
| x64 copy+remove | 32.168 | 35.170 | 34.890 | 34.362 |
| ARM64 exact | 6.367 | 6.960 | 5.529 | 5.540 |
| ARM64 union | 52.431 | 66.379 | 66.302 | 66.525 |
| ARM64 copy+append | 82.934 | 89.746 | 89.176 | 89.991 |
| ARM64 copy+remove | 47.271 | 54.179 | 54.264 | 54.849 |

Small micro batch algorithms were not reworked. Do not credit their small timing differences to Dense or query SIMD changes. ARM64 integrated small bulk achieves 0/198 every-round 5% margins in both contracts. No general small-set promotion is justified.

## Investigating the earlier scattered Union anomaly

The unchanged previous-vector implementation is already below the unchanged Micro baseline in the new historical fixture (x64 94.797 vs 691.702 ns for scattered n1024 Union). Therefore this rerun alone cannot establish a specific source bug or a new fix for the former reversal.

A separate common-caller diagnostic gives the following full allocating Union values. These values are NOT blended with the historical tables:

| Architecture / candidate / distribution | Settle once per case | Settle before each sample |
|---|---:|---:|
| x64 Micro contiguous | 267.212 ns | 257.968 ns |
| x64 Micro scattered | 260.494 ns | 250.019 ns |
| x64 previous vector contiguous | 76.296 ns | 70.022 ns |
| x64 previous vector scattered | 71.393 ns | 67.674 ns |
| x64 kernel-only contiguous | 64.768 ns | 61.073 ns |
| x64 kernel-only scattered | 63.862 ns | 63.347 ns |
| x64 integrated contiguous | 63.719 ns | 58.910 ns |
| x64 integrated scattered | 60.876 ns | 58.708 ns |
| ARM64 Micro contiguous | 334.917 ns | 323.452 ns |
| ARM64 Micro scattered | 329.359 ns | 322.888 ns |
| ARM64 kernel-only contiguous | 154.741 ns | 150.457 ns |
| ARM64 kernel-only scattered | 155.566 ns | 149.760 ns |
| ARM64 integrated contiguous | 149.973 ns | 147.477 ns |
| ARM64 integrated scattered | 151.984 ns | 147.702 ns |

Across 3 rounds x 7 samples, each listed per-case-setting Union row records 12 Gen0 collections on x64 and 3 on ARM64; per-sample-setting rows record zero timed collections. This establishes that allocation preparation can change the observation, **not that GC alone caused the historical anomaly**. Caller/code layout/process variation remain unisolated factors. No time or allocation is subtracted; zero timed collection is not zero allocation.

## Deliverable layout and reproduction

Reviewed archive includes `TestedSource_b46ef77.zip`, `IntegratedCandidate/FusedKernelSet.cs`, `IntegratedCandidate/FusedBitmapSet.cs`, `IntegratedCandidate/DenseFusion.cs`, per-architecture recomputed CSV, `GatesByOperation.csv`, `IndependentRecheck.json`, Chinese full report and both original raw artifact directories. Generated classes depend on the retained baseline registry/authoring API; do not copy only one file and assume it is a complete package.

Run `python3 Audit~/Fusion/refine.py --output artifacts/fusion --rounds 3` on the experimental checkout with the pinned Git history and .NET SDK available. Run on x64 and ARM64 independently. The dedicated workflow documents exact commands and source exports. The independent reviewer expects the archived measured b46 tree, intentionally not arbitrary future source.

**Remaining work:** resolve the small-set complete-cost deficit; validate a final, single selection rule and end-to-end integration; implement/test actual Unity backend behavior, assets/lifecycles and mobile IL2CPP before publication. Physical micro storage is compressed bitmap records, not a fully materialized global Dense array. The source audit remains a draft and no universal speed or release approval is claimed.
