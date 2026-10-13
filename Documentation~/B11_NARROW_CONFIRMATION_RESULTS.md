# B1.1 narrowed-policy confirmation experiment

Measured on 2026-10-13 after one principled factory-allocation narrowing. This is the tested experimental policy, not the subsequently selected conservative core. This is one additional, predeclared full confirmation, using the original fixed iteration map with no calibration or pilot. All earlier results remain separate and intact.

## Bottom line

- Narrow B1.1 saves 32 B per fresh union and fresh overlapping exact-intersection factory versus exact B1, and about 64 B per measured lifecycle. At n=1024, lifecycle allocation is **41,360 versus 41,424 B/op**.
- Lifecycle medians improve versus B1 at n=0, 8, 64, and 1024 in this confirmation. **n=1 is adverse: 533.176 versus 446.190 ns/op (+19.5%)**, and every tested Narrow round is slower than every B1 round. This is not a uniformly faster implementation.
- Larger union improves, but small copy/intersection costs remain. n=1024 fresh intersection is **16.707 versus 15.379 µs (+8.6%)** while allocating 32 B less. Allocation savings do not guarantee time savings.
- The preserved first-screen source had **703.707 versus 465.445 µs (+51.2%)** at n=1024 lifecycle. Narrow confirmation is **522.122 versus 543.258 µs (−3.9%)**. The 44-cell repair and this confirmation establish allocation outcomes; neither resolves the cause of the original timing observation.
- CoreCLR with Unity stubs only. No Unity Editor/Player, Mono, or IL2CPP performance claim, statistical significance claim, or global speedup claim.

## Fixed identities and verification

- B0: original `db37fed890647338a3664918071eb428f3d4514a` Runtime.
- B1: exact PR #4 `dac77e7e7d5bad1ba7e77ed9fca7824d50187a72` Runtime.
- Narrow: B11Narrow, the same Runtime measured in the 44-cell repair. GameplayTagContainer.cs SHA-256 `883bc1c5a25973f3c34248ebd00f1275fbb6a8a135784e0741166f3efe5f8777`; the source-identity audit lists all verified files.
- Unchanged original Benchmark.cs SHA-256 `fafd8cb73a2813d6ed2547a8dd1bf6bec8063abadad815fa3c642c9819808c23`. Original B0/B1 sources, project, stubs, and configuration also checked against the first-screen inventory before building.
- Exact original full iteration map SHA-256 `85ef89dff332434c9d2822405e60212a88f313853380b3428a63ba543e4c9d99`. Originally calibrated once on B0 for a 50 ms target; copied byte-for-byte without a new pilot or calibration.
- Same three orders: B0/B1/Narrow; Narrow/B1/B0; B1/B0/Narrow. These are not fully position-balanced.
- **1,359/1,359 samples PASS**, all 151 cells × three versions × three rounds. Source/result assertions run after every batch; count maps are shared and source-before/source-after inventories match.
- The single run completed in **186.01 s** under an external 600-second limit, including its builds and untimed setup/validation. No resampling or performance threshold.
- SDK 8.0.425; runtime .NET 8.0.31; Debian GNU/Linux 13; reported processor count 9; Stopwatch 1 GHz; tiered compilation disabled; no forced GC.

## Lifecycle: every measured size

The complete public cycle builds shuffled input, performs an exact query, delete/add, ordered enumeration, fresh union and exact intersection, CopyFrom, Clear, and the harness observers. Times below are µs/op; bytes are allocation per complete operation.

| n | B0 µs | B1 µs | Narrow µs | Narrow vs B1 | Narrow vs B0 | B/op B0 / B1 / Narrow |
| --- | --- | --- | --- | --- | --- | --- |
| 0 | 0.143 | 0.159 | 0.146 | -8.2% | +2.0% | 200.000 / 232.000 / 168.000 |
| 1 | 0.366 | 0.446 | 0.533 | +19.5% | +45.5% | 352.000 / 360.000 / 296.000 |
| 8 | 2.796 | 2.278 | 1.886 | -17.2% | -32.5% | 552.004 / 616.049 / 552.000 |
| 64 | 27.467 | 19.694 | 15.228 | -22.7% | -44.6% | 2,936.000 / 2,928.000 / 2,864.000 |
| 1024 | 866.913 | 543.258 | 522.122 | -3.9% | -39.8% | 41,528.000 / 41,424.000 / 41,360.000 |

All lifecycle cells are uncapped. n=64 has three short B1 samples and two short tested Narrow samples under the original 50 ms target. No other lifecycle sample is short. The range and GC table retains all three rounds.

| n | Version | Round range µs | Batch range ms | Short / 3 | GC0 / GC1 / GC2 |
| --- | --- | --- | --- | --- | --- |
| 0 | B0 | 0.132–0.160 | 69.223–83.952 | 0 | 72 / 3 / 0 |
| 0 | B1 | 0.149–0.160 | 78.304–84.133 | 0 | 84 / 3 / 0 |
| 0 | B11Narrow | 0.133–0.157 | 69.570–82.387 | 0 | 61 / 3 / 0 |
| 1 | B0 | 0.363–0.427 | 95.288–111.963 | 0 | 64 / 3 / 0 |
| 1 | B1 | 0.446–0.452 | 116.841–118.603 | 0 | 67 / 6 / 1 |
| 1 | B11Narrow | 0.486–0.545 | 127.306–142.911 | 0 | 54 / 5 / 0 |
| 8 | B0 | 2.374–6.828 | 77.786–223.746 | 0 | 13 / 5 / 2 |
| 8 | B1 | 2.217–2.340 | 72.639–76.677 | 0 | 17 / 6 / 3 |
| 8 | B11Narrow | 1.769–2.674 | 57.962–87.636 | 0 | 11 / 5 / 2 |
| 64 | B0 | 26.780–35.588 | 54.845–72.884 | 0 | 3 / 0 / 0 |
| 64 | B1 | 18.864–19.869 | 38.634–40.692 | 3 | 3 / 0 / 0 |
| 64 | B11Narrow | 14.304–24.712 | 29.294–50.610 | 2 | 3 / 0 / 0 |
| 1024 | B0 | 834.353–984.872 | 106.797–126.064 | 0 | 2 / 0 / 0 |
| 1024 | B1 | 541.693–588.034 | 69.337–75.268 | 0 | 3 / 0 / 0 |
| 1024 | B11Narrow | 499.058–620.976 | 63.879–79.485 | 0 | 2 / 0 / 0 |

The n=1 adverse result is 485.632–545.160 ns in tested Narrow versus B1 445.712–452.435 ns. Narrow n=1024 rounds are 522.122 / 499.058 / 620.976 µs versus B1 588.034 / 543.258 / 541.693 µs. The −3.9% median has overlapping ranges and is not a proof of a stable gain.

## New/reused set operations: absolute medians

This table includes all sizes for the core set operations, including losses. Times are ns/op. The complete 151-cell CSVs also preserve construction orders, all legal B0 target compositions, enumeration, exact/hierarchical queries, binding, serialization, and registry rebuild.

| n | Cell | B0 ns | B1 ns | Narrow ns | Narrow vs B1 | B/op B1 → Narrow |
| --- | --- | --- | --- | --- | --- | --- |
| 0 | new-union | 71.194 | 63.734 | 47.439 | -25.6% | 88.000 → 56.000 |
| 0 | new-exact-intersection | 36.149 | 54.410 | 35.006 | -35.7% | 88.000 → 56.000 |
| 0 | target-copy-nonempty | 9.953 | 10.498 | 15.553 | +48.2% | 0.000 → 0.000 |
| 0 | target-intersection-nonempty | 36.111 | 10.303 | 6.823 | -33.8% | 0.000 → 0.000 |
| 1 | new-union | 136.416 | 175.495 | 131.282 | -25.2% | 128.000 → 96.000 |
| 1 | new-exact-intersection | 62.335 | 72.800 | 102.695 | +41.1% | 120.000 → 88.000 |
| 1 | target-copy-nonempty | 41.934 | 39.666 | 60.782 | +53.2% | 0.000 → 0.000 |
| 1 | target-intersection-nonempty | 75.782 | 24.317 | 42.770 | +75.9% | 0.000 → 0.000 |
| 8 | new-union | 1,166.559 | 1,188.360 | 909.108 | -23.5% | 240.000 → 208.000 |
| 8 | new-exact-intersection | 299.732 | 191.713 | 221.169 | +15.4% | 176.000 → 144.000 |
| 8 | target-copy-nonempty | 448.034 | 243.113 | 283.114 | +16.5% | 0.000 → 0.000 |
| 8 | target-intersection-nonempty | 305.122 | 99.129 | 139.590 | +40.8% | 0.000 → 0.000 |
| 64 | new-union | 14,213.637 | 9,974.180 | 5,402.940 | -45.8% | 1,136.000 → 1,104.000 |
| 64 | new-exact-intersection | 3,923.052 | 1,189.192 | 1,098.577 | -7.6% | 624.000 → 592.000 |
| 64 | target-copy-nonempty | 6,255.105 | 2,170.407 | 2,454.027 | +13.1% | 0.000 → 0.000 |
| 64 | target-intersection-nonempty | 4,094.361 | 768.710 | 891.281 | +15.9% | 0.000 → 0.000 |
| 1024 | new-union | 334,433.109 | 148,657.996 | 97,492.840 | -34.4% | 16,496.000 → 16,464.000 |
| 1024 | new-exact-intersection | 115,789.747 | 15,379.171 | 16,707.245 | +8.6% | 8,304.000 → 8,272.000 |
| 1024 | target-copy-nonempty | 164,873.441 | 40,879.025 | 42,928.182 | +5.0% | 0.000 → 0.000 |
| 1024 | target-intersection-nonempty | 103,450.882 | 10,661.345 | 11,773.900 | +10.4% | 0.000 → 0.000 |

- n=1 target exact intersection: 24.317→42.770 ns (+75.9%); all batches are short at 25.171–26.200 ms for B1 and 41.870–49.746 ms for tested Narrow.
- n=8 target exact intersection: 99.129→139.590 ns (+40.8%), with B1 25.356–29.994 ms and tested Narrow 29.825–38.486 ms batches. The separately labeled registered composition measures the same B1/tested Narrow native target API and remains adverse; it is not an independent mechanism.
- n=1 new exact intersection: 72.800→102.695 ns (+41.1%), despite allocation 120→88 B/op. Narrow n=1 copy is also adverse, 39.666→60.782 ns (+53.2%).
- n=1024 new union: 148.658→97.493 µs (−34.4%), allocation 16,496→16,464 B/op. Both variants have short batches under the frozen counts: B1 37.795–44.340 ms, tested Narrow 22.576–28.673 ms.
- n=1024 new half-overlap intersection: 15.379→16.707 µs (+8.6%), allocation 8,304→8,272 B/op. Narrow reserves 8,192 B of backing payload again; the earlier First source retained 4,096 B for this half-overlap shape while incurring extra transient allocation.
- B0 has no identical overwrite API. The allocating exact-result substitute and two registered public compositions retain their adapter labels; none is claimed universally strongest.

## Whole-run duration and GC ledger

Short means below the original 50 ms batch target. Capped means the shared count hit its finite cap, whether or not the actual batch was short. Totals sum measured batches only and are not an application-throughput score.

| Version | Samples | Measured seconds | Batch ms min / median / max | Short | Capped | GC0 / GC1 / GC2 |
| --- | --- | --- | --- | --- | --- | --- |
| B0 | 453 | 36.017 | 0.011 / 83.059 / 223.746 | 73 | 108 | 1520 / 115 / 44 |
| B1 | 453 | 29.744 | 0.006 / 73.873 / 152.883 | 127 | 108 | 1422 / 110 / 45 |
| B11Narrow | 453 | 32.044 | 0.010 / 74.740 / 325.939 | 124 | 108 | 1241 / 111 / 39 |

The original 36 capped cells remain 108 capped samples per version: 12 empty/singleton loop cells plus 24 input-pool cells. Very short binding/serialization batches remain in the raw data and should not support precise ratio claims.

## Unchanged-method timing variation

Public exact lookup and enumeration remain B1’s name-based logic. There is no P1 integer backend or integer-mapping attribution. The measured movement in unchanged methods is retained rather than treated as optimization evidence.

| n | Cell | B0 ns | B1 ns | Narrow ns | Narrow vs B1 |
| --- | --- | --- | --- | --- | --- |
| 1 | has-exact-hit | 9.109 | 8.064 | 9.269 | +14.9% |
| 1 | ordered-enumeration | 5.310 | 5.553 | 5.799 | +4.4% |
| 8 | has-exact-hit | 9.435 | 8.564 | 9.409 | +9.9% |
| 8 | ordered-enumeration | 17.088 | 15.841 | 17.819 | +12.5% |
| 64 | has-exact-hit | 10.148 | 8.569 | 8.767 | +2.3% |
| 64 | ordered-enumeration | 95.163 | 91.832 | 132.720 | +44.5% |
| 1024 | has-exact-hit | 9.413 | 8.973 | 8.999 | +0.3% |
| 1024 | ordered-enumeration | 1,415.665 | 1,434.367 | 1,362.153 | -5.0% |
| 4096 | registry-force-rebuild | 2,896,776.500 | 3,209,661.406 | 3,473,521.719 | +8.2% |

For example n=64 enumeration moves 91.832→132.720 ns (+44.5%), whereas n=1024 moves 1,434.367→1,362.153 ns (−5.0%). No source-level enumerator speedup or regression is inferred from those observations.

## Preserve the complete evidence history

1. First screen: 55 focused cells and 151 full cells measured First. It shows the eliminated warmed append/alias allocations, sparse retained-capacity benefit, head-skew/tiny-operation losses, and the adverse n=1024 lifecycle median of +51.2%. Those data and First source remain intact.
2. One bounded repair: only the fresh intersection factory’s reservation policy changed. A separate 44-cell B1/First/Narrow experiment added full-overlap/disjoint factory coverage. It verified the dense allocation repair and the loss of First’s overlapping fresh sparse capacity benefit. Reused sparse targets stay small.
3. This confirmation: unchanged 151-cell harness and the original fixed count map now measure tested Narrow Narrow. It preserves remaining small-cell losses and timing variation, rather than selecting only favorable cells.

The detailed repair result remains important: at n=1024 fresh full overlap, B1/First/Narrow allocation is 8,304/16,624/8,272 B/op. At one/two overlapping fresh matches, First is 112 B/op with 32 B retained, while Narrow is 8,272 B/op with 8,192 B retained. At disjoint fresh output, both First and Narrow are 56 B/op with no retained backing array. These observations explain allocation policy, not the earlier lifecycle timing spread.

Restoration and all harness observers stay inside each measured operation. Warmup/preparation and post-batch checks stay outside; allocation and GC share the timing batch. Payload figures omit headers, strings, whole heap, and peak transient memory. Full-lifecycle retention is a representative-input proxy, not the measured output tuple. No measured stage is silently relabeled as another source. The conservative production core is a different source and requires its own comparison.

## Evidence and replay

- [Final confirmation protocol](../Tests~/Roadmap/FULL_CONFIRMATION.md), [all raw samples](../Tests~/Roadmap/Evidence/full-confirmation/raw.csv), [all 151 comparisons](../Tests~/Roadmap/Evidence/full-confirmation/comparison.csv), [detailed duration/GC/payload rows](../Tests~/Roadmap/Evidence/full-confirmation/detailed-summary.csv), [audit](../Tests~/Roadmap/Evidence/full-confirmation/audit.json), [count/source identity audit](../Tests~/Roadmap/Evidence/full-confirmation/source-identity-audit.json).
- [Preserved first-screen report](B11_PUBLIC_RESULTS.md) and [factory-narrowing repair report](B11_NARROW_RESULTS.md).
- Raw round CSVs, environment logs, original count map, source-before/source-after inventories, SDK version, wall duration, and build receipts are retained. The executed confirm-b11.sh is preserved verbatim and requires the archived historical count/source inventory paths described in the protocol.
