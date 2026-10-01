# Robin Hood / ordered SIMD / Dense vector: executed results

## Decision — 2026-10-01

The three experiments have been implemented, compiled and measured. The complete performance requirement is **not met**. No production migration, merge, release, or claim of universal superiority is authorized.

- **Robin Hood micro bitmap:** reject as the general small-set replacement under the current complete-cost requirement. Lookup improves, but independent construction and prepared batch operations do not justify adoption.
- **Ordered SIMD micro bitmap:** retain as a query-only candidate. Only HasTagExact changed. Changes in unchanged bulk-operation timings are not additional algorithmic gains.
- **Dense fused vector/count:** retain for further integration work. In this x64 run, all 54 prepared bulk cases have a >=5% margin against both fixed Dense controls in every process round; 53/54 allocating bulk cases meet that screen. One complete fresh-union case regresses and prevents full acceptance.

The last sentence is an experimental screening result, not statistical proof, an all-library ranking, or ARM/Unity evidence.

## Exact source and evidence

Measured commit: `8b547544ca0dbca1f55e166114b4ebc89208eb34`.
Measured tree: `7cf0c50077a775cf6d4129ad5f45fa0a20c3f6c2`.
Branch: `experiment/robin-simd-dense-20261001`; PR #7.

Controls: Auto `84729cc3b6e1b4681328806df2d7c8a1d8f568f6` and Micro `9be85fcd1f8d1f594ceb5bc98f2ce234b398e0e0`.

- Final benchmark CI: https://github.com/acced/ZFramework.GameplayTags/actions/runs/36826435341
- Exact source and raw data: https://github.com/acced/ZFramework.GameplayTags/actions/runs/36826435341/artifacts/11145647467
- Artifact SHA256: `f55b8906c7f612aecedae7d0a8aed2e86f5de0cdb0ad3b5fcccce43586a1e24a`.
- Additional **untimed** codegen CI: https://github.com/acced/ZFramework.GameplayTags/actions/runs/36829016735
- Codegen artifact: https://github.com/acced/ZFramework.GameplayTags/actions/runs/36829016735/artifacts/11145889812
- Codegen artifact SHA256: `04029a5529abbe7f9bcdb6d1b95c8413acdf9db0849b0daad107e10436ced7c5`.

The later review commits add only an untimed probe harness/workflow and this report. They do not change the timed methods. The supplemental harness re-exports the measured commit and generates the exact same SIMD source, checked byte-for-byte after download. No new timings are claimed for the review commits.

Both reference Git trees, the measured source.zip Git tree, and 64 production files were independently checked after download. Runtime, Editor, Samples~, Tests, and package.json remain identical to the Auto baseline.

## What was actually compared

Robin uses 32-bit address+16-member-mask records, multiplicative hashing, maximum 3/4 slot load, inline one record and backward-shift deletion. Member storage has no raw ID list, tombstones, global pool or COW. Table copies copy empty slots too. Its enumeration is slot order, not the full production DFS-order contract. It is a micro-only experiment, with an explicit 2^20 registered-ID limit.

Ordered SIMD is a literal clone of the original Micro source except for the exact-query method and type/import adaptations. It compares valid Vector<uint> lanes for 8-to-32-ish record counts (the lower bound is the actual vector lane count), with scalar tails and the original fallback. It does not change union, copy, append or removal algorithms.

The three Dense variants replace only DenseUnion and DenseDifference in cloned Micro types. They compare scalar hardware population count, fused AVX2 nibble-table/shuffle/SAD counting, and vector output followed by a separate count pass. The independent kernel suite additionally tests intersection. Output writes and eager count are always part of the timed operation.

## Correctness and compilation

| Check | Executed result |
|---|---|
| Triad tests | 7 groups, 5,575,108 assertions, 0 failures |
| Same suite with hardware intrinsics disabled | 5,575,108 assertions, 0 failures; repeated configurations, not new unique cases |
| Full original Micro tests for SIMD, hardware, fused, two-pass variants | Each 5 groups, 5,392,728 assertions, 0 failures |
| Reserved-table-to-inline-copy regression | Old literal Robin source throws NullReferenceException; fixed source passes 120 assertions |
| Prepared arbitrary placement / mutation / result reuse | 0 B with an effective 152 B allocation positive control |
| Portable build | Robin and ordered SIMD plus dependencies compile as .NET Standard 2.1 |
| Dense intrinsics build | .NET 8 build; NOT a portable Unity backend |
| Supplemental query-codegen harness | 81,940 assertions, 0 failures in each of hardware-enabled and disabled runs |
| Native Unity, Android/iOS IL2CPP, ARM64 | NOT executed |

Coverage includes exhaustive 4,096 set pairs, dirty results, ownership and supported aliases, wraparound collision clusters and backshift deletion, random churn, capacity boundaries, all 65,536 masks, SIMD valid tails, foreign/default handles, dense tail words, and output/count invariants. Counts include repeated assertions, not millions of unique cases.

The first submitted Robin version had a copy defect when a source retained an allocated table but had only one live record. The corrected CopyCore extracts that record into an inline destination rather than calling table-only PlaceUnique. Final timings use the fixed version exclusively; earlier artifacts are not blended in.

## Measurement contract

Only final run 36826435341 is used below: .NET 8.0.31, SDK 8.0.425, Linux x64, Release, tiered compilation disabled, one allowed CPU selected. AVX2 and POPCNT are available and Vector<uint> has eight lanes. A pinned shared runner is not an isolated target device.

Three independent process rounds with rotated/reversed order; seven retained samples per case per round. Report median of the three process medians. Preparation is uniform: setup/warmup, collect/wait-finalizers/collect. No subtraction of loop cost, no deletion of losers, no borrowed mutable outputs or delayed count.

Fresh union creates an independent mutable result. Copy+append/remove performs a real copy followed by the public operation. Allocation and copying remain in timing. Prepared outputs are measured separately. Exact handles are prepared equally for all variants. Extra missed/mixed probes are distinct; mixed128 means a 128-query batch, not one lookup.

Small sizes: 0,1,7,8,9,31,32,33,127,128,129. Large sizes: 128,1024,4096. Both use 10,000/65,536/262,144 explicit definitions plus implicit ancestors, with contiguous and fixed-seed scattered members. The timed bulk relationship is half overlap/deletion of an existing half subset. Disjoint/full-overlap/churn are correctness-covered; a complete timing matrix for those patterns has not been collected here.

This is a focused Auto/Micro comparison. Main, older optimize, Alex, indexed Dense and packed bitmap were NOT rerun, so no claim of beating all earlier libraries is made.

### Important control distinction

Small gates compare Auto's adaptive mode and original Micro's adaptive mode.

Large gates compare Auto **forced Dense** and original Micro **forced Dense**; adaptive Auto is an additional separate row. These are not interchangeable controls. Do not label the large gate as an adaptive end-to-end product ranking.

## Small scattered sets: complete costs

262,144 definitions / 8 scattered members; fresh results; ns/op.

| Operation | Auto | Original Micro | Robin Hood | Ordered SIMD |
|---|---:|---:|---:|---:|
| Exact lookup | 6.809 | 7.000 | 3.562 | 4.082 |
| Independent union | 66.319 | 60.475 | 140.089 | 61.551 |
| Actual copy + append | 71.235 | 82.898 | 132.659 | 82.035 |
| Actual copy + remove | 42.594 | 49.307 | 79.070 | 49.453 |

Allocations in the same case, B/op:

| Operation | Auto | Original Micro | Robin Hood | Ordered SIMD |
|---|---:|---:|---:|---:|
| Exact | 0 | 0 | 0 | 0 |
| Union | 136 | 144 | 200 | 144 |
| Copy + append | 192 | 200 | 136 | 200 |
| Copy + remove | 104 | 112 | 136 | 112 |

Robin's copy+append allocates fewer bytes than Auto but takes more time: probe/merge/repair work still matters. For this workload, faster lookup does not pay for the whole requested contract. Ordered SIMD changes only lookup; small bulk differences relative to Micro cannot be credited to a rewritten batch algorithm.

### Small full gate, split by operation

Relative to both fixed controls; all-round margin means at least 5% in every process round.

| Candidate / contract | Exact-query margin / 66 | Bulk margin / 198 |
|---|---:|---:|
| Robin fresh | 61 | 9 |
| Robin prepared | 58 | 0 |
| Ordered SIMD fresh | 10 | 5 |
| Ordered SIMD prepared | 10 | 7 |

SIMD only targets a subset of sizes/layouts; unchanged methods can show timing fluctuations. Its apparent bulk wins are not separately claimed gains. No candidate passes all 264 primary cases.

## Dense: full container operations

### 10,000 definitions / 1,024 contiguous members

ns/op; fresh contract. Auto below is **forced Dense**.

| Operation | Auto Dense | Micro SWAR | Hardware count | AVX2 fused | Vector then count |
|---|---:|---:|---:|---:|---:|
| Exact | 2.594 | 2.469 | 2.462 | 2.461 | 2.459 |
| Union | 1059.685 | 958.516 | 219.332 | 137.362 | 151.205 |
| Copy + append | 525.725 | 408.991 | 239.539 | 158.116 | 171.891 |
| Copy + remove | 567.695 | 385.877 | 250.082 | 160.564 | 169.776 |

Exact code is unchanged across the Micro Dense variants. Tiny exact-query differences are not an optimization claim. Fresh nonempty result allocation is 1,344 B for Auto and 1,352 B for all Micro variants.

### Same scale, scattered members — the remaining failure

| Operation | Auto Dense | Micro SWAR | Hardware count | AVX2 fused | Vector then count |
|---|---:|---:|---:|---:|---:|
| Union | 1009.639 | 387.033 | 751.812 | 662.294 | 717.480 |
| Copy + append | 530.258 | 406.680 | 233.419 | 157.675 | 165.743 |
| Copy + remove | 562.819 | 382.294 | 242.672 | 156.873 | 162.825 |

The new allocating union does NOT beat original Micro in this case, even though the independent vector kernel is faster. The cause is not yet isolated; allocation/JIT/call-site hypotheses must not be presented as measurements. No normalization or substitution with prepared-output numbers was applied.

For context, adaptive Auto in this same scattered case measures 999.898 / 1047.557 / 576.329 ns for union/copy+append/copy+remove. These additional controls remain distinct in raw CSV.

### Large full gate

For each of the three new Dense kernels: 53/54 allocating bulk cases and 54/54 prepared bulk cases have at least 5% lower latency than BOTH fixed Dense controls in every round. The query method is unchanged; none of its 18 rows establishes a new >=5% margin. These results support keeping the fused kernel for integration investigation, not declaring full Runtime acceptance.

## Independent Dense kernels and machine code

159 ulong words, random input, prepared output; each number includes bit operation, result writes and exact count; ns/op:

| Operation | SWAR | Hardware count | AVX2 fused | Vector then scalar count |
|---|---:|---:|---:|---:|
| OR | 290.299 | 138.596 | 57.874 | 70.133 |
| AND | 311.804 | 179.334 | 61.425 | 90.083 |
| AND-NOT | 300.629 | 148.541 | 61.223 | 71.582 |

Every prepared kernel sample has 0 B allocation. These isolated numbers are not added to or subtracted from full-container timings.

The separate untimed disassembly shows SWAR shifts/masks/multiply, scalar POPCNT, and real AVX2 vpshufb/vpsadbw plus bitwise/output operations in the fused kernel. The original log did not capture the aggressively inlined query method as a separate method. A new NoInlining wrapper around the UNCHANGED query method captured vpand, vpcmpeqd and vptest; the generated SIMD source matches the benchmark source byte-for-byte. Hardware-disabled correctness also passes. This is x64 codegen evidence, not Burst/Mono/IL2CPP/ARM64 evidence.

## Independent post-download audit

All 5,256 aggregate rows, all process medians, input digests, allocation summaries and rankings were recomputed from raw samples and match the submitted summaries. This covers 15,768 raw container rows / 110,376 retained timing samples; 55,188 prepared container allocation samples are all 0 B. The 864 independent Dense-kernel aggregate cases were separately recomputed from all three rounds. Benchmark and codegen artifact digests were checked.

Files in the benchmark artifact include source.zip, fixed reference ZIPs, triad/full/comparison.json, summary.json, decision.json, generated literal adapters, all per-process JSON, dense-kernel-summary.json, tests and disassembly logs. The codegen artifact is separate and introduces no new timing numbers.

## Next bounded work

Do not migrate Robin Hood into Runtime for the present requirements. Keep ordered SIMD as an isolated query candidate. Investigate the one full fresh-union regression using identical caller/allocation paths, then evaluate a Unity-compatible vector/count backend and real ARM64 behavior. Do not combine isolated per-row winners into an imaginary final implementation. The original all-workload target remains open and unmet.

中文结论：三条路线已经实际验证并独立复核。Robin Hood 查询快，但完整构造与批量操作没有过关；有序 SIMD 只保留查询收益；Dense 融合写出与计数的内核有明确收益，但新建 Union 仍有失败点，且尚无 Unity/ARM64 验收。保留实验和全部原始数据，不替换生产 Runtime，不合并、不发布。
