# B1.1 first-screen public-operation results

Measured on 2026-10-13. This is one focused run and one full run, with no favorable resampling, candidate-specific recalibration, source changes between stages, or performance acceptance threshold. Both completed within their separate 600-second outer limits.

## Conclusion

- B1.1 removes the measured temporary allocations from warmed append/alias-union cycles and reduces sparse-intersection backing capacity. Large new union and tail/spread/mixed skew operations improve in this run.
- The result is not uniformly faster. Head-skew intersection and small-result intersection regress. The n=1024 full-lifecycle median is **703.707 µs versus B1 465.445 µs (+51.2%)**, with a wide B1.1 range of 452.042–725.175 µs. Preserve this adverse observation; this finite run does not isolate its cause or establish a stable effect.
- Half-overlap n=1024 fresh intersection allocates **8,408 versus 8,304 B/op (+104)**. The associated full lifecycle allocates **41,496 versus 41,424 B/op (+72)**. Smaller retained output capacity therefore does not imply smaller transient allocation.
- These are .NET/CoreCLR measurements with Unity stubs. Unity Editor/Player, Mono, and IL2CPP performance remain unverified.

## Identities and execution

- B0: original `db37fed890647338a3664918071eb428f3d4514a` Runtime.
- B1: exact PR #4 `dac77e7e7d5bad1ba7e77ed9fca7824d50187a72` Runtime retained under Baselines/B1.
- B11: the first-screen Runtime preserved under Baselines/B11First, retaining name-sorted List storage. No P1 integer representation. Public B1/B11 HasTagExact and enumerator logic are unchanged.
- SDK 8.0.425; runtime .NET 8.0.31; Debian GNU/Linux 13; reported processor count 9; Stopwatch frequency 1 GHz; tiered compilation disabled. No forced GC.
- All-version build-only passed in 17.34 s. A preceding wall-recorder launch could not find `/usr/bin/time` and never launched dotnet; that failure is preserved separately. The successful finite runs use a Python wall recorder with `timeout 600s`.
- Focus: B1-only calibration targeting 100 ms, 55 cells, four fixed orders B1/B11, B11/B1, B11/B1, B1/B11. Whole-run wall time 98.61 s.
- Full: B0-only calibration targeting 50 ms, 151 cells, three fixed orders B0/B1/B11, B11/B1/B0, B1/B0/B11. These positions are not fully balanced. Whole-run wall time 189.80 s.
- Focus 440/440 and full 1,359/1,359 samples passed post-batch source/result assertions. Every version used its stage’s identical iteration map. Before/after source inventories match; all 24 common inventoried files also match across stages.

Calibration counts are shared, not recalibrated for the faster candidate. “Short” means a measured batch fell below the stage target; “capped” means its fixed count reached the harness cap. A cap can coexist with a batch above the target. Whole-run wall durations include builds, preparation, warmup, calibration, checks, and censuses. The next table sums measured batches only; it is not an application-throughput score.

| Stage | Version | Samples | Measured seconds | Batch ms min / median / max | Short | Capped | GC0 / GC1 / GC2 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Focus | B1 | 220 | 31.837 | 39.506 / 138.922 / 661.884 | 44 | 0 | 3088 / 44 / 16 |
| Focus | B11 | 220 | 21.277 | 0.183 / 76.977 / 422.418 | 138 | 0 | 2576 / 16 / 0 |
| Full | B0 | 453 | 34.808 | 0.010 / 78.598 / 472.260 | 79 | 108 | 1519 / 118 / 43 |
| Full | B1 | 453 | 28.371 | 0.006 / 69.166 / 260.646 | 133 | 108 | 1421 / 108 / 41 |
| Full | B11 | 453 | 30.346 | 0.008 / 71.749 / 185.317 | 133 | 108 | 1254 / 110 / 42 |

The full-stage 36 capped cells produce 108 capped samples per version. They comprise 12 capped empty/singleton loop cells and 24 input-pool cells; all case-level labels remain in the detailed CSVs. No focused count hit its cap.

## Focused cost attribution

All append and alias-union measurements include CopyFrom restoration and the Count observer inside the timed/allocation batch. Inputs are validated on each call. These numbers do not isolate a merge kernel or price each internal branch independently.

- Capacity construction saves exactly 32 B/op at n=0, 8, and 1024, consistent with removing the redundant List allocation. Backing-array payload is unchanged.
- Append saves 88 B/op at n=8 and 8,216 B/op at n=1024 for every tested nonempty shape, consistent with removing the incoming tag array. Both versions use pre-reserved targets here: the focused census cannot demonstrate an additional capacity-growth saving for containment.
- Alias union saves 32 B/op at n=0, 120 at n=8, and 8,248 at n=1024, consistent with eliminating the cloned List and, for nonempty inputs, its backing array. Every warmed B1.1 append and alias-union cell records zero B/op.
- Sparse-result reused intersections also record zero B/op in both versions after warmup. Their retained backing payload is 16 B for B1.1 versus 64 B at n=8 or 8,192 B at n=1024 for B1. This is reservation/growth behavior, not a measured per-operation allocation reduction.
- Disjoint-range and tail-search gains often have much shorter B1.1 intervals. For example n=1024 no-match intersection has only 0.183–0.334 ms per B1.1 batch despite B1’s 120.924–141.516 ms. Treat the exact nanosecond ratio cautiously.
- Head-skew intersection is adverse even with longer intervals: large-left 47.233→73.944 ns, small-left 48.628→74.131 ns. B1.1 large-left batches are 290.656–337.771 ms versus B1 188.846–272.366 ms. Small-left head difference, including CopyFrom, is 211.345→263.132 ns. The source-known dispatch checks do not guarantee an end-to-end head-case win.

### All focused cell medians

Times are ns/op, bytes are allocation B/op. The ratio column is B1.1’s change from B1. No statistical significance threshold is applied.

| n | Public cell | B1 ns | B1.1 ns | Change | B/op B1 → B1.1 |
| --- | --- | --- | --- | --- | --- |
| 0 | append-after | 21.544 | 23.331 | +8.3% | 0.000 → 0.000 |
| 0 | append-before | 22.029 | 29.341 | +33.2% | 0.000 → 0.000 |
| 0 | append-contained | 21.247 | 24.196 | +13.9% | 0.000 → 0.000 |
| 0 | append-near-subset | 22.407 | 23.078 | +3.0% | 0.000 → 0.000 |
| 0 | append-overlap | 21.367 | 26.166 | +22.5% | 0.000 → 0.000 |
| 0 | copy | 10.593 | 15.314 | +44.6% | 0.000 → 0.000 |
| 0 | intersection-small-result-0 | 11.719 | 6.829 | -41.7% | 0.000 → 0.000 |
| 0 | intersection-small-result-1 | 10.410 | 6.484 | -37.7% | 0.000 → 0.000 |
| 0 | intersection-small-result-2 | 11.979 | 6.432 | -46.3% | 0.000 → 0.000 |
| 0 | new-capacity | 39.756 | 29.692 | -25.3% | 88.000 → 56.000 |
| 0 | union-alias-left | 44.349 | 27.999 | -36.9% | 32.000 → 0.000 |
| 0 | union-alias-right | 44.445 | 26.608 | -40.1% | 32.000 → 0.000 |
| 0 | union-alias-self | 39.919 | 22.729 | -43.1% | 32.000 → 0.000 |
| 8 | append-after | 1,127.836 | 594.403 | -47.3% | 88.000 → 0.000 |
| 8 | append-before | 947.266 | 605.576 | -36.1% | 88.000 → 0.000 |
| 8 | append-contained | 917.100 | 518.183 | -43.5% | 88.000 → 0.000 |
| 8 | append-near-subset | 901.596 | 657.025 | -27.1% | 88.000 → 0.000 |
| 8 | append-overlap | 1,021.364 | 673.388 | -34.1% | 88.000 → 0.000 |
| 8 | copy | 244.281 | 263.336 | +7.8% | 0.000 → 0.000 |
| 8 | intersection-small-result-0 | 80.753 | 13.970 | -82.7% | 0.000 → 0.000 |
| 8 | intersection-small-result-1 | 89.657 | 106.619 | +18.9% | 0.000 → 0.000 |
| 8 | intersection-small-result-2 | 89.133 | 110.775 | +24.3% | 0.000 → 0.000 |
| 8 | new-capacity | 58.945 | 46.763 | -20.7% | 176.000 → 144.000 |
| 8 | union-alias-left | 1,420.390 | 859.709 | -39.5% | 120.000 → 0.000 |
| 8 | union-alias-right | 1,307.769 | 827.168 | -36.7% | 120.000 → 0.000 |
| 8 | union-alias-self | 1,214.349 | 469.518 | -61.3% | 120.000 → 0.000 |
| 1024 | append-after | 135,738.079 | 72,157.340 | -46.8% | 8,216.000 → 0.000 |
| 1024 | append-before | 155,633.600 | 78,542.962 | -49.5% | 8,216.000 → 0.000 |
| 1024 | append-contained | 125,103.496 | 76,316.857 | -39.0% | 8,216.000 → 0.000 |
| 1024 | append-near-subset | 121,854.037 | 82,896.251 | -32.0% | 8,216.000 → 0.000 |
| 1024 | append-overlap | 139,008.062 | 83,233.769 | -40.1% | 8,216.000 → 0.000 |
| 1024 | copy | 39,070.897 | 34,303.891 | -12.2% | 0.000 → 0.000 |
| 1024 | intersection-small-result-0 | 8,066.635 | 13.255 | -99.8% | 0.000 → 0.000 |
| 1024 | intersection-small-result-1 | 8,485.983 | 9,258.502 | +9.1% | 0.000 → 0.000 |
| 1024 | intersection-small-result-2 | 7,911.860 | 8,506.555 | +7.5% | 0.000 → 0.000 |
| 1024 | new-capacity | 710.093 | 674.033 | -5.1% | 8,304.000 → 8,272.000 |
| 1024 | skew-difference-large-left-head | 45,265.582 | 37,311.826 | -17.6% | 0.000 → 0.000 |
| 1024 | skew-difference-large-left-mixed | 61,535.126 | 34,789.562 | -43.5% | 0.000 → 0.000 |
| 1024 | skew-difference-large-left-spread | 61,204.050 | 39,281.190 | -35.8% | 0.000 → 0.000 |
| 1024 | skew-difference-large-left-tail | 59,255.194 | 35,707.257 | -39.7% | 0.000 → 0.000 |
| 1024 | skew-difference-small-left-head | 211.345 | 263.132 | +24.5% | 0.000 → 0.000 |
| 1024 | skew-difference-small-left-mixed | 9,882.621 | 336.408 | -96.6% | 0.000 → 0.000 |
| 1024 | skew-difference-small-left-spread | 8,995.230 | 534.733 | -94.1% | 0.000 → 0.000 |
| 1024 | skew-difference-small-left-tail | 8,235.166 | 332.843 | -96.0% | 0.000 → 0.000 |
| 1024 | skew-intersection-large-left-head | 47.233 | 73.944 | +56.6% | 0.000 → 0.000 |
| 1024 | skew-intersection-large-left-mixed | 8,588.337 | 234.214 | -97.3% | 0.000 → 0.000 |
| 1024 | skew-intersection-large-left-spread | 8,192.765 | 340.944 | -95.8% | 0.000 → 0.000 |
| 1024 | skew-intersection-large-left-tail | 8,577.085 | 188.286 | -97.8% | 0.000 → 0.000 |
| 1024 | skew-intersection-small-left-head | 48.627 | 74.131 | +52.4% | 0.000 → 0.000 |
| 1024 | skew-intersection-small-left-mixed | 8,663.454 | 219.076 | -97.5% | 0.000 → 0.000 |
| 1024 | skew-intersection-small-left-spread | 9,569.370 | 358.697 | -96.3% | 0.000 → 0.000 |
| 1024 | skew-intersection-small-left-tail | 9,732.960 | 191.888 | -98.0% | 0.000 → 0.000 |
| 1024 | union-alias-left | 191,592.175 | 115,478.866 | -39.7% | 8,248.000 → 0.000 |
| 1024 | union-alias-right | 192,028.492 | 120,144.382 | -37.4% | 8,248.000 → 0.000 |
| 1024 | union-alias-self | 171,984.649 | 73,436.715 | -57.3% | 8,248.000 → 0.000 |

## Full lifecycle: all sizes

The operation builds shuffled input, performs an exact query, delete/add, enumeration, fresh union, fresh exact intersection, CopyFrom, Clear, and the documented observers. It does not exclude the restoration or observer work. These are direct operation measurements, not a sum of individually benchmarked kernels.

| n | B0 µs | B1 µs | B1.1 µs | vs B1 | vs B0 | B/op B0 / B1 / B1.1 |
| --- | --- | --- | --- | --- | --- | --- |
| 0 | 0.133 | 0.147 | 0.174 | +18.2% | +30.2% | 200.000 / 232.000 / 168.000 |
| 1 | 0.347 | 0.382 | 0.371 | -3.0% | +6.9% | 352.000 / 360.000 / 320.000 |
| 8 | 2.439 | 2.064 | 1.874 | -9.2% | -23.2% | 552.004 / 616.053 / 520.166 |
| 64 | 33.036 | 17.798 | 15.067 | -15.3% | -54.4% | 2,936.000 / 2,928.000 / 2,904.000 |
| 1024 | 997.438 | 465.445 | 703.707 | +51.2% | -29.4% | 41,528.000 / 41,424.000 / 41,496.000 |

All lifecycle cells are uncapped. The n=64 B1/B1.1 lifecycle intervals are all below 50 ms. n=1024 intervals are above 50 ms in every version. Absolute round values and GC counts follow; each row uses three samples.

| n | Version | Median µs | Round range µs | Batch range ms | Short / 3 | GC0 / GC1 / GC2 |
| --- | --- | --- | --- | --- | --- | --- |
| 0 | B0 | 0.133 | 0.132–0.134 | 69.456–70.313 | 0 | 72 / 3 / 0 |
| 0 | B1 | 0.147 | 0.141–0.182 | 74.089–95.234 | 0 | 84 / 3 / 0 |
| 0 | B11 | 0.174 | 0.130–0.236 | 68.385–123.706 | 0 | 61 / 3 / 0 |
| 1 | B0 | 0.347 | 0.345–0.412 | 90.470–108.081 | 0 | 64 / 3 / 0 |
| 1 | B1 | 0.382 | 0.380–0.403 | 99.532–105.742 | 0 | 66 / 4 / 0 |
| 1 | B11 | 0.371 | 0.359–0.394 | 94.232–103.414 | 0 | 57 / 5 / 0 |
| 8 | B0 | 2.439 | 2.211–2.464 | 72.462–80.747 | 0 | 12 / 5 / 2 |
| 8 | B1 | 2.064 | 1.907–2.285 | 62.480–74.876 | 0 | 17 / 6 / 3 |
| 8 | B11 | 1.874 | 1.684–2.055 | 55.173–67.329 | 0 | 13 / 6 / 3 |
| 64 | B0 | 33.036 | 25.545–33.234 | 52.316–68.064 | 0 | 3 / 0 / 0 |
| 64 | B1 | 17.798 | 15.711–24.230 | 32.175–49.623 | 3 | 3 / 0 / 0 |
| 64 | B11 | 15.067 | 13.936–17.557 | 28.540–35.958 | 3 | 3 / 0 / 0 |
| 1024 | B0 | 997.438 | 806.387–1,070.209 | 103.218–136.987 | 0 | 2 / 0 / 0 |
| 1024 | B1 | 465.445 | 452.717–571.326 | 57.948–73.130 | 0 | 2 / 0 / 0 |
| 1024 | B11 | 703.707 | 452.042–725.175 | 57.861–92.822 | 0 | 1 / 0 / 0 |

The n=1024 B1.1 round times are 725.175, 703.707, and 452.042 µs/op; B1 is 452.717, 571.326, and 465.445 µs/op. The allocation change is consistent across these rounds. The timing spread and non-balanced full orders limit causal attribution; the adverse median is retained rather than discarded or resampled.

## Selected full-operation comparisons

The complete 151-cell comparison, including all construction, serialization, rebuild, binding, hierarchy, exact query, enumeration, and legal B0 adapter rows, is in the linked CSVs. Selected absolute medians expose the main measured tradeoffs.

| n | Cell | B0 ns | B1 ns | B1.1 ns | vs B1 | B/op B1 → B1.1 |
| --- | --- | --- | --- | --- | --- | --- |
| 0 | new-union | 52.473 | 56.861 | 51.034 | -10.2% | 88.000 → 56.000 |
| 0 | new-exact-intersection | 32.551 | 49.004 | 27.774 | -43.3% | 88.000 → 56.000 |
| 0 | target-copy-nonempty | 7.565 | 10.285 | 15.376 | +49.5% | 0.000 → 0.000 |
| 0 | target-intersection-nonempty | 26.982 | 11.497 | 8.148 | -29.1% | 0.000 → 0.000 |
| 1 | new-union | 134.683 | 174.999 | 123.930 | -29.2% | 128.000 → 96.000 |
| 1 | new-exact-intersection | 58.467 | 66.998 | 76.161 | +13.7% | 120.000 → 112.000 |
| 1 | target-copy-nonempty | 46.373 | 38.194 | 56.401 | +47.7% | 0.000 → 0.000 |
| 1 | target-intersection-nonempty | 86.195 | 21.843 | 39.939 | +82.8% | 0.000 → 0.000 |
| 8 | new-union | 1,191.124 | 1,319.601 | 775.934 | -41.2% | 240.000 → 208.000 |
| 8 | new-exact-intersection | 280.316 | 206.517 | 167.011 | -19.1% | 176.000 → 112.000 |
| 8 | target-copy-nonempty | 437.180 | 218.241 | 259.339 | +18.8% | 0.000 → 0.000 |
| 8 | target-intersection-nonempty | 304.976 | 103.114 | 117.861 | +14.3% | 0.000 → 0.000 |
| 64 | new-union | 11,696.325 | 9,579.358 | 4,775.621 | -50.1% | 1,136.000 → 1,104.000 |
| 64 | new-exact-intersection | 3,943.692 | 1,043.312 | 1,018.295 | -2.4% | 624.000 → 632.000 |
| 64 | target-copy-nonempty | 5,250.926 | 1,841.138 | 1,779.451 | -3.4% | 0.000 → 0.000 |
| 64 | target-intersection-nonempty | 3,631.158 | 666.679 | 740.419 | +11.1% | 0.000 → 0.000 |
| 1024 | new-union | 387,839.754 | 148,506.613 | 84,549.730 | -43.1% | 16,496.000 → 16,464.000 |
| 1024 | new-exact-intersection | 116,064.584 | 14,408.483 | 15,443.259 | +7.2% | 8,304.000 → 8,408.000 |
| 1024 | target-copy-nonempty | 143,652.137 | 44,938.891 | 32,814.047 | -27.0% | 0.000 → 0.000 |
| 1024 | target-intersection-nonempty | 108,145.561 | 9,917.854 | 10,145.541 | +2.3% | 0.000 → 0.000 |
| 4096 | registry-force-rebuild | 2,857,207.781 | 2,849,542.344 | 2,961,108.219 | +3.9% | 1,948,680.000 → 1,948,680.000 |

- At n=1024, the fresh half-overlap intersection retains 4,096 B of backing-array payload in B1.1 versus B1’s 8,192 B, while allocating 104 B/op more transiently. Geometric growth trades fewer retained slots for multiple intermediate arrays. New union saves 32 B/op, explaining the measured lifecycle’s net +72 B/op. At n=64 the corresponding +8 and −32 produce lifecycle −24 B/op.
- Fresh full-overlap intersection at n=1024 was not included in this fixed public run. Its source-known geometric-growth tradeoff is an unmeasured public coverage gap; do not present the half-overlap result as full-overlap evidence.
- B0 target intersection has no identical overwrite API. The harness retains an allocating exact-result substitute, a registered copy/remove composition, and a registered clear/scan/add composition with explicit adapter labels. No composition is claimed universally strongest.
- Tiny target intersection remains adverse versus B1: n=1 native-target median 21.843→39.939 ns; the registered-composition label measures the same B1/B1.1 native API separately and gives 21.989→41.840 ns. This is not evidence of two independent mechanisms.
- Several unchanged exact-query/enumerator cells also move: n=8 HasTagExact is 7.795→10.214 ns; n=1024 ordered enumeration is 1,218.179→1,349.878 ns. No integer backend exists in B1.1, so neither improvement nor regression in these cells is attributed to P1 or integer mapping.

## Retention and limits

The census reports array element payload, not object headers, referenced string payload, whole heap, process RSS, or transitive per-operation peak memory. Shared registry payload is separately recorded. The full-lifecycle retained-container census builds a representative input container outside measurement: it is a storage proxy, not the measured union/intersection/cleared tuple. Stale-snapshot diagnostics are zero for these name-backed versions and should not be interpreted as an integer-snapshot comparison.

Warming and preparing input pools are outside measured time/allocation. Restoration inside the operation and all harness observers are included. Post-batch source/result checks are outside measurement. Allocation and GC are recorded from the same batch as timing. No confidence interval, significance claim, Unity claim, or overall speedup follows from this finite dataset.

## Evidence files

- [Focused raw samples](../Tests~/Roadmap/Evidence/focused-final/raw.csv), [all focused comparisons](../Tests~/Roadmap/Evidence/focused-final/comparison.csv), [detailed focused durations/GC/payload](../Tests~/Roadmap/Evidence/focused-final/detailed-summary.csv), [focused audit](../Tests~/Roadmap/Evidence/focused-final/audit.json).
- [Full raw samples](../Tests~/Roadmap/Evidence/full-final/raw.csv), [all 151 full comparisons](../Tests~/Roadmap/Evidence/full-final/comparison.csv), [detailed full durations/GC/payload](../Tests~/Roadmap/Evidence/full-final/detailed-summary.csv), [full audit](../Tests~/Roadmap/Evidence/full-final/audit.json).
- Each stage directory preserves its iteration map, per-round CSVs/environment logs, SDK version, build logs, command, wall record, and before/after source inventory. [Cross-stage source audit](../Tests~/Roadmap/Evidence/public-source-audit.json).
- [Benchmark protocol](../Tests~/Roadmap/B11_BENCHMARK.md). The analysis script and finite-run wrapper are preserved in results; neither modifies benchmark source or performs resampling.
