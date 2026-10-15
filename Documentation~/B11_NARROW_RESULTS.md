# B1.1 factory-narrowing repair results

One fixed repair experiment ran on 2026-10-13, after preserving the complete adverse first-screen source and datasets. It addresses allocation policy for the fresh exact-intersection factory; it does not erase the original screen or establish the cause of its latency result.

## Result

- The predicted allocation accounting is verified. At n=1024, existing half-overlap fresh intersection is B1 / First / Narrow **8,304 / 8,408 / 8,272 B/op**, and whole lifecycle is **41,424 / 41,496 / 41,360 B/op**. Narrow saves 64 B per lifecycle versus B1, and 136 B versus First.
- The newly covered n=1024 full-overlap factory is **8,304 / 16,624 / 8,272 B/op**. This confirms First’s multiple growth arrays for dense new output and Narrow’s restored single reserved backing array.
- The tradeoff is substantial for overlapping sparse fresh output. At n=1024 with one or two matches, First is **112 B/op allocated / 32 B retained array payload**, while Narrow is **8,272 B/op / 8,192 B retained**. Narrow intentionally restores minimum-input reservation for overlapping ranges. It does not preserve First’s fresh sparse allocation win.
- Disjoint fresh output remains **56 B/op / zero retained array payload** in First and Narrow, versus B1 8,304 B/op / 8,192 B retained at n=1024. Reused sparse targets remain zero allocation and 16 B retained in First and Narrow.
- Whole-lifecycle Narrow medians improve versus B1 for these four sizes, but small intersection regressions remain and several First/Narrow comparisons move adversely. No overall speedup or stable causal latency claim follows.

## Protocol and identity checks

- Exact B1 is `dac77e7e7d5bad1ba7e77ed9fca7824d50187a72`. First is Baselines/B11First; all seven files match the original first-screen Runtime hashes. Narrow is current Runtime, with only GameplayTagContainer.cs changed from First; the only source change is the fresh factory’s pre-reservation rule.
- Separate BenchmarkNarrow.cs preserves the original Benchmark.cs and both earlier datasets unchanged. Its 44 cells are four sizes (0,8,64,1024) × four existing whole-public cells, four fresh factory geometries (0/1/2/full matches), and three reused sparse geometries (0/1/2 matches). At n=0 these geometries coincide as empty inputs.
- Calibration ran once on B1, targeting 200 ms. Each cell’s fixed count is shared by every version/round. Four orders: B1/First/Narrow; Narrow/First/B1; First/B1/Narrow; Narrow/B1/First. Pairwise precedence is 2:2. Absolute positions are not balanced; Narrow never occupies the middle position.
- All three build-only variants passed in 20.28 s. The single measured run completed in **187.46 s** under its outer 600-second limit. No rerun or candidate-specific recalibration.
- **528/528 samples PASS**, 44 cells × 3 sources × 4 rounds. Shared counts and before/after source inventories verified. Exact B1 is unchanged; First’s seven files match the original source inventory.
- SDK 8.0.425 / .NET 8.0.31 / Debian 13 / reported processor count 9 / 1 GHz Stopwatch / tiered compilation disabled. No forced GC. Unity stubs: no Unity, Mono, or IL2CPP performance claim.

The table below reports actual measured durations and GC counts. Whole-run wall time also includes builds, calibration, warmup, preparation, validation, and census work. Summed measured seconds are a run ledger, not an application-throughput metric.

| Version | Samples | Measured seconds | Batch ms min / median / max | Short (<200 ms) | Capped | GC0 / GC1 / GC2 |
| --- | --- | --- | --- | --- | --- | --- |
| B1 | 176 | 56.315 | 157.349 / 306.258 / 896.838 | 25 | 16 | 8516 / 88 / 36 |
| B11First | 176 | 44.168 | 0.404 / 248.103 / 788.911 | 62 | 16 | 5616 / 88 / 44 |
| B11Narrow | 176 | 43.466 | 0.367 / 256.512 / 810.949 | 64 | 16 | 5988 / 96 / 44 |

The four capped cells are the n=0 reused half-overlap intersection and n=0 reused sparse 0/1/2-result intersections, each at 16,777,216 iterations. Four rounds yield 16 capped samples per version. All other cells are uncapped. Faster disjoint batches can still be very short: n=1024 reused disjoint Narrow is 0.367–1.116 ms; fresh disjoint Narrow is 1.298–3.320 ms. Exact speed ratios for these rows deserve caution.

## Whole lifecycle medians and spread

These are measured complete operations, with the same query, mutation, enumeration, fresh union/intersection, CopyFrom, Clear, and observers as the first screen. No component timing is subtracted. All lifecycle batches exceed 200 ms; none is capped. Times below are µs/op.

| n | B1 µs | First µs | Narrow µs | Narrow vs B1 | Narrow vs First | B/op B1 / First / Narrow |
| --- | --- | --- | --- | --- | --- | --- |
| 0 | 0.158 | 0.125 | 0.145 | -7.9% | +16.1% | 232.000 / 168.000 / 168.000 |
| 8 | 2.313 | 1.693 | 1.833 | -20.7% | +8.3% | 616.000 / 520.000 / 552.000 |
| 64 | 20.339 | 16.037 | 15.202 | -25.3% | -5.2% | 2,928.000 / 2,904.000 / 2,864.000 |
| 1024 | 528.454 | 523.313 | 451.760 | -14.5% | -13.7% | 41,424.000 / 41,496.000 / 41,360.000 |

| n | Version | Round range µs | Batch range ms | GC0 / GC1 / GC2 |
| --- | --- | --- | --- | --- |
| 0 | B1 | 0.144–0.168 | 302.681–351.337 | 464 / 4 / 0 |
| 0 | B11First | 0.121–0.154 | 254.361–323.471 | 332 / 4 / 0 |
| 0 | B11Narrow | 0.133–0.155 | 278.333–324.632 | 332 / 4 / 0 |
| 8 | B1 | 1.961–5.302 | 256.988–694.990 | 76 / 0 / 0 |
| 8 | B11First | 1.668–1.871 | 218.577–245.215 | 60 / 4 / 4 |
| 8 | B11Narrow | 1.798–2.630 | 235.619–344.707 | 64 / 4 / 4 |
| 64 | B1 | 16.962–42.450 | 277.911–695.502 | 44 / 0 / 0 |
| 64 | B11First | 14.136–19.786 | 231.608–324.169 | 44 / 0 / 0 |
| 64 | B11Narrow | 13.244–18.859 | 216.988–308.987 | 40 / 0 / 0 |
| 1024 | B1 | 494.798–634.492 | 253.336–324.860 | 16 / 0 / 0 |
| 1024 | B11First | 440.460–930.214 | 225.515–476.270 | 16 / 0 / 0 |
| 1024 | B11Narrow | 436.466–480.394 | 223.471–245.962 | 16 / 4 / 0 |

At n=1024, First’s 442.646 / 930.214 / 603.980 / 440.460 µs rounds are visibly variable, while Narrow is 480.394 / 464.126 / 439.394 / 436.466 µs. B1 is 496.560 / 560.347 / 494.798 / 634.492 µs. This comparison does not prove that growing arrays caused the original +51.2% median; that original observation and its raw spread remain in the first-screen report.

## All 44 cells: time, allocation, and retained capacity

Times are median ns/op. Allocation and retained array payload are bytes. Retained figures exclude object headers, string payload, whole heap, and peak transient memory. The lifecycle retained column is a newly built input-container proxy, not the measured output tuple.

| n | Whole-public cell | ns B1 / First / Narrow | Narrow vs B1 | Narrow vs First | Allocated B1 / First / Narrow | Retained B1 / First / Narrow |
| --- | --- | --- | --- | --- | --- | --- |
| 0 | focus-intersection-small-result-0 | 10.360 / 6.894 / 7.282 | -29.7% | +5.6% | 0 / 0 / 0 | 16 / 16 / 16 |
| 0 | focus-intersection-small-result-1 | 10.292 / 7.251 / 7.125 | -30.8% | -1.7% | 0 / 0 / 0 | 16 / 16 / 16 |
| 0 | focus-intersection-small-result-2 | 10.032 / 6.869 / 7.569 | -24.6% | +10.2% | 0 / 0 / 0 | 16 / 16 / 16 |
| 0 | fresh-exact-intersection-0 | 47.082 / 30.691 / 34.656 | -26.4% | +12.9% | 88 / 56 / 56 | 0 / 0 / 0 |
| 0 | fresh-exact-intersection-1 | 46.608 / 29.397 / 32.335 | -30.6% | +10.0% | 88 / 56 / 56 | 0 / 0 / 0 |
| 0 | fresh-exact-intersection-2 | 49.401 / 28.029 / 34.538 | -30.1% | +23.2% | 88 / 56 / 56 | 0 / 0 / 0 |
| 0 | fresh-exact-intersection-full | 48.668 / 28.599 / 32.572 | -33.1% | +13.9% | 88 / 56 / 56 | 0 / 0 / 0 |
| 0 | full-lifecycle | 157.675 / 125.042 / 145.183 | -7.9% | +16.1% | 232 / 168 / 168 | 0 / 0 / 0 |
| 0 | new-exact-intersection | 49.248 / 31.966 / 32.944 | -33.1% | +3.1% | 88 / 56 / 56 | 0 / 0 / 0 |
| 0 | new-union | 56.942 / 43.576 / 46.541 | -18.3% | +6.8% | 88 / 56 / 56 | 0 / 0 / 0 |
| 0 | target-intersection-nonempty | 9.742 / 7.717 / 7.450 | -23.5% | -3.5% | 0 / 0 / 0 | 0 / 0 / 0 |
| 8 | focus-intersection-small-result-0 | 83.551 / 14.238 / 15.672 | -81.2% | +10.1% | 0 / 0 / 0 | 64 / 16 / 16 |
| 8 | focus-intersection-small-result-1 | 88.893 / 101.091 / 113.912 | +28.1% | +12.7% | 0 / 0 / 0 | 64 / 16 / 16 |
| 8 | focus-intersection-small-result-2 | 89.041 / 102.053 / 106.865 | +20.0% | +4.7% | 0 / 0 / 0 | 64 / 16 / 16 |
| 8 | fresh-exact-intersection-0 | 140.579 / 39.618 / 54.971 | -60.9% | +38.8% | 176 / 56 / 56 | 64 / 0 / 0 |
| 8 | fresh-exact-intersection-1 | 145.287 / 140.736 / 173.671 | +19.5% | +23.4% | 176 / 112 / 144 | 64 / 32 / 64 |
| 8 | fresh-exact-intersection-2 | 143.369 / 151.317 / 166.638 | +16.2% | +10.1% | 176 / 112 / 144 | 64 / 32 / 64 |
| 8 | fresh-exact-intersection-full | 127.125 / 185.561 / 139.789 | +10.0% | -24.7% | 176 / 200 / 144 | 64 / 64 / 64 |
| 8 | full-lifecycle | 2,312.599 / 1,693.218 / 1,833.125 | -20.7% | +8.3% | 616 / 520 / 552 | 64 / 64 / 64 |
| 8 | new-exact-intersection | 161.371 / 159.528 / 173.488 | +7.5% | +8.8% | 176 / 112 / 144 | 64 / 32 / 64 |
| 8 | new-union | 983.433 / 624.270 / 644.894 | -34.4% | +3.3% | 240 / 208 / 208 | 128 / 128 / 128 |
| 8 | target-intersection-nonempty | 118.456 / 116.746 / 119.115 | +0.6% | +2.0% | 0 / 0 / 0 | 64 / 64 / 64 |
| 64 | focus-intersection-small-result-0 | 622.048 / 18.206 / 15.347 | -97.5% | -15.7% | 0 / 0 / 0 | 512 / 16 / 16 |
| 64 | focus-intersection-small-result-1 | 660.363 / 597.307 / 654.348 | -0.9% | +9.5% | 0 / 0 / 0 | 512 / 16 / 16 |
| 64 | focus-intersection-small-result-2 | 657.837 / 594.856 / 671.532 | +2.1% | +12.9% | 0 / 0 / 0 | 512 / 16 / 16 |
| 64 | fresh-exact-intersection-0 | 716.486 / 52.535 / 69.394 | -90.3% | +32.1% | 624 / 56 / 56 | 512 / 0 / 0 |
| 64 | fresh-exact-intersection-1 | 731.357 / 633.967 / 723.697 | -1.0% | +14.2% | 624 / 112 / 592 | 512 / 32 / 512 |
| 64 | fresh-exact-intersection-2 | 729.501 / 672.220 / 769.962 | +5.5% | +14.5% | 624 / 112 / 592 | 512 / 32 / 512 |
| 64 | fresh-exact-intersection-full | 611.498 / 852.911 / 598.322 | -2.2% | -29.8% | 624 / 1,168 / 592 | 512 / 512 / 512 |
| 64 | full-lifecycle | 20,339.367 / 16,036.999 / 15,202.150 | -25.3% | -5.2% | 2,928 / 2,904 / 2,864 | 512 / 512 / 512 |
| 64 | new-exact-intersection | 890.108 / 943.634 / 847.596 | -4.8% | -10.2% | 624 / 632 / 592 | 512 / 256 / 512 |
| 64 | new-union | 8,241.975 / 4,848.220 / 5,055.988 | -38.7% | +4.3% | 1,136 / 1,104 / 1,104 | 1,024 / 1,024 / 1,024 |
| 64 | target-intersection-nonempty | 802.936 / 763.384 / 794.400 | -1.1% | +4.1% | 0 / 0 / 0 | 512 / 512 / 512 |
| 1024 | focus-intersection-small-result-0 | 8,754.032 / 15.091 / 12.776 | -99.9% | -15.3% | 0 / 0 / 0 | 8,192 / 16 / 16 |
| 1024 | focus-intersection-small-result-1 | 8,533.046 / 8,055.914 / 7,899.449 | -7.4% | -1.9% | 0 / 0 / 0 | 8,192 / 16 / 16 |
| 1024 | focus-intersection-small-result-2 | 8,932.560 / 7,617.747 / 8,662.327 | -3.0% | +13.7% | 0 / 0 / 0 | 8,192 / 16 / 16 |
| 1024 | fresh-exact-intersection-0 | 9,230.775 / 34.310 / 61.233 | -99.3% | +78.5% | 8,304 / 56 / 56 | 8,192 / 0 / 0 |
| 1024 | fresh-exact-intersection-1 | 9,308.671 / 7,869.699 / 8,936.646 | -4.0% | +13.6% | 8,304 / 112 / 8,272 | 8,192 / 32 / 8,192 |
| 1024 | fresh-exact-intersection-2 | 9,562.735 / 8,325.469 / 8,916.526 | -6.8% | +7.1% | 8,304 / 112 / 8,272 | 8,192 / 32 / 8,192 |
| 1024 | fresh-exact-intersection-full | 8,984.047 / 9,233.952 / 8,355.999 | -7.0% | -9.5% | 8,304 / 16,624 / 8,272 | 8,192 / 8,192 / 8,192 |
| 1024 | full-lifecycle | 528,453.906 / 523,313.418 / 451,760.160 | -14.5% | -13.7% | 41,424 / 41,496 / 41,360 | 8,192 / 8,192 / 8,192 |
| 1024 | new-exact-intersection | 12,015.528 / 12,356.358 / 11,547.316 | -3.9% | -6.5% | 8,304 / 8,408 / 8,272 | 8,192 / 4,096 / 8,192 |
| 1024 | new-union | 141,279.562 / 89,768.133 / 87,447.152 | -38.1% | -2.6% | 16,496 / 16,464 / 16,464 | 16,384 / 16,384 / 16,384 |
| 1024 | target-intersection-nonempty | 11,563.847 / 12,474.244 / 11,045.918 | -4.5% | -11.5% | 0 / 0 / 0 | 8,192 / 8,192 / 8,192 |

## Remaining adverse observations and scope limits

- At n=8, Narrow reused sparse 1/2-result medians are 113.912 / 106.866 ns versus B1 88.893 / 89.041 ns (+28.1% / +20.0%). The reused-target algorithms are unchanged from First; the factory narrowing does not repair these costs.
- At n=8, Narrow fresh 1/2/full-match medians are 173.671 / 166.638 / 139.789 ns versus B1 145.287 / 143.369 / 127.125 ns (+19.5% / +16.2% / +10.0%), despite saving 32 B/op versus B1.
- At n=1024, Narrow fresh one-match output is 8,936.646 ns versus First 7,869.699 ns, and two-match is 8,916.527 versus 8,325.470 ns. The much larger retained reservation is preserved in the table. Reduced dense allocation is a bounded repair, not a universal improvement.
- n=0 lifecycle Narrow is 145.183 ns versus First 125.042 ns; n=8 lifecycle Narrow is 1,833.125 ns versus First 1,693.219 ns. No adverse row is omitted because another baseline looks favorable.
- The 151-cell full screen and 55-cell focus screen still describe First, not Narrow. This 44-cell repair run does not remeasure those entire matrices. Original head-skew costs, tiny operations outside this matrix, and non-targeted lifecycle/serialization/lookup behavior must not be relabeled as newly measured Narrow results.
- The source and byte accounting isolate a deliberate fresh-factory allocation policy change. They do not isolate timing causality. No confidence interval, significance threshold, or performance gate is applied.

## Evidence

- [Repair protocol](../Tests~/Roadmap/NARROW_BENCHMARK.md), [all raw samples](../Tests~/Roadmap/Evidence/narrow-final/raw.csv), [44-cell comparison](../Tests~/Roadmap/Evidence/narrow-final/comparison.csv), [durations/GC/payload detail](../Tests~/Roadmap/Evidence/narrow-final/detailed-summary.csv), [sample/count/source audit](../Tests~/Roadmap/Evidence/narrow-final/audit.json), [source identity audit](../Tests~/Roadmap/Evidence/narrow-final/source-identity-audit.json).
- [Preserved first-screen report](B11_PUBLIC_RESULTS.md), including the original adverse lifecycle observation and the unmodified 440 focused + 1,359 full samples.
- Per-round CSVs, iteration map, environment logs, SDK version, build logs, before/after hashes, command, and wall recorder result are preserved in narrow-final. No publication or full confirmation run is implied by this report.
