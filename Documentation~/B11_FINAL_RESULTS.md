# B1.1 final conservative-core results

Measured 2026-10-13. This report describes the final conservative core, compared with exact B1. Earlier First and Narrow policies are preserved experiments, not this production source. No further policy variants or timing repeats were performed after this comparison.

## Conclusion

**Recommendation: allocation-focused draft review only. This dataset does not support broad performance acceptance.** Leading adverse medians versus B1 are n=1 lifecycle **+47.2%**, n=1 copy **+60.1%**, n=1024 fresh intersection **+25.7%**, and the restored n=1024 two-match target control **+92.6%**. All remain in the report and raw data.

- The retained changes have concrete allocation benefits: warmed nonempty append and alias-union cycles allocate zero B/op, capacity construction saves 32 B/op, and the measured whole lifecycle saves 64 B/op at every tested size.
- Time results are mixed. At n=1024, append-after is 181.612→79.724 µs, new union 165.272→109.440 µs, and lifecycle 686.127→544.507 µs in this run. The run has substantial round variation; these are observations, not universal speed guarantees.
- **Adverse n=1 lifecycle median: 383.986→565.161 ns (+47.2%)**, with Core rounds 379.510 / 674.634 / 1,491.671 / 455.688 ns. **n=1 copy: 37.428→59.917 ns (+60.1%)**. Both remain in the result; the core is not uniformly faster.
- B1 intersection/difference policies are restored. The experimental sparse-growth, disjoint-bound and skew-dispatch policies are not in this core. Timing movement in restored controls does not establish a source-level kernel improvement or regression.
- This is one bounded 77-cell B1/Core comparison, not a new complete 151-cell B0/B1/Core benchmark. Unity Editor/Player, Mono, and IL2CPP remain unmeasured.

## Source and run integrity

- Baseline: exact B1 `dac77e7e7d5bad1ba7e77ed9fca7824d50187a72`.
- Core GameplayTagContainer.cs SHA-256: `cde7d94d624ab6f5b1b6305a21e82b8371cf59895a879834fa3677b1ee4d6866`.
- Core GameplayTagContainerAlgorithms.cs SHA-256: `3a98190d288ca445fcb0146b3d100a88f2a875f3e335c38d01395ea77326a7ba`. The source-identity audit pins all seven Runtime files and exact B1 inputs.
- Separate BenchmarkCore.cs reuses the original public case bodies and validators. It rejects calibration mode. Original harnesses and historical result directories remain intact.
- 25 cells use the original full-final map and 50 ms short target; 52 use the original focused-final map and 100 ms target. Every raw row records its map and target. The 77 selected counts are copied from their original maps, with no pilot or new calibration.
- Full map SHA-256 `85ef89dff332434c9d2822405e60212a88f313853380b3428a63ba543e4c9d99`; focused map SHA-256 `de212c784634a48f01a39083ad62b9012ce0ed1c78f7066fc1eaf355664a23df`.
- Four fixed, pairwise and position-balanced orders: B1/Core; Core/B1; Core/B1; B1/Core.
- **616/616 samples PASS**: 77 cells × two sources × four rounds. Shared counts, row provenance, 50/100 ms short labels, source/result assertions, and before/after source hashes all verified.
- One run completed in **106.52 s** under an external 600-second timeout. No favorable resampling or performance threshold.
- SDK 8.0.425 / .NET 8.0.31 / Debian GNU/Linux 13 / reported processor count 9 / Stopwatch 1 GHz / tiered compilation disabled. No forced GC.

## Allocation and retained-capacity accounting

- At n=8, append removes 88 B/op; at n=1024, 8,216 B/op. These correspond to the removed temporary incoming arrays. The timing includes CopyFrom restoration and the Count observer.
- Alias union removes 32 B/op at n=0, 120 at n=8, and 8,248 at n=1024, consistent with removing the temporary cloned List and backing array. All 96 warmed Core append/alias samples record zero B/op.
- Capacity construction saves 32 B/op at n=0,8,1024, with unchanged backing-array payload. New union and fresh exact intersection also save 32 B/op versus B1 because the redundant List construction is removed.
- Lifecycle allocation is B1/Core 232/168, 360/296, 616/552, 2,928/2,864, and 41,424/41,360 B/op at n=0,1,8,64,1024: exactly 64 B/op saved in the recorded medians.
- Unlike First/Narrow, Core reused sparse intersection reserves B1’s min-input capacity again. At n=1024 with 0/1/2 matches, B1 and Core retain 8,192 B of backing-array payload; both allocate zero during warmed timing. The earlier 16 B sparse-target result is not a Core result.
- Pre-reserved append/alias targets have the same retained capacity in B1 and Core. These cases demonstrate temporary-allocation elimination, not an additional measured capacity-growth reduction.

## Whole lifecycle: every tested size

Times below are µs/op. Each operation includes shuffled construction, exact query, delete/add, enumeration, fresh union/intersection, CopyFrom, Clear, and the harness observers. No component timing is subtracted.

| n | B1 µs | Core µs | Core vs B1 | B/op B1 → Core |
| --- | --- | --- | --- | --- |
| 0 | 0.148 | 0.135 | -8.8% | 232 → 168 |
| 1 | 0.384 | 0.565 | +47.2% | 360 → 296 |
| 8 | 2.177 | 2.047 | -6.0% | 616 → 552 |
| 64 | 19.974 | 17.369 | -13.0% | 2,928 → 2,864 |
| 1024 | 686.127 | 544.507 | -20.6% | 41,424 → 41,360 |

Every lifecycle cell is uncapped. The n=64 lifecycle has three of four short samples in each version; all other lifecycle samples exceed their original 50 ms target. Ranges and GC totals retain all four rounds.

| n | Version | Round range µs | Batch range ms | Short / 4 | GC0 / GC1 / GC2 |
| --- | --- | --- | --- | --- | --- |
| 0 | B1 | 0.141–0.253 | 73.980–132.416 | 0 | 112 / 4 / 0 |
| 0 | B11Core | 0.129–0.152 | 67.396–79.923 | 0 | 80 / 0 / 0 |
| 1 | B1 | 0.372–0.686 | 97.467–179.868 | 0 | 88 / 0 / 0 |
| 1 | B11Core | 0.380–1.492 | 99.486–391.033 | 0 | 72 / 8 / 4 |
| 8 | B1 | 1.970–4.954 | 64.565–162.330 | 0 | 16 / 4 / 0 |
| 8 | B11Core | 1.956–2.971 | 64.094–97.357 | 0 | 12 / 0 / 0 |
| 64 | B1 | 15.530–32.012 | 31.806–65.561 | 3 | 4 / 0 / 0 |
| 64 | B11Core | 14.650–28.363 | 30.003–58.087 | 3 | 4 / 0 / 0 |
| 1024 | B1 | 498.478–1,326.409 | 63.805–169.780 | 0 | 4 / 0 / 0 |
| 1024 | B11Core | 484.968–612.494 | 62.076–78.399 | 0 | 4 / 4 / 0 |

At n=1024, B1 spans 498.478–1,326.409 µs and Core 484.968–612.494 µs. At n=1, B1 spans 371.808–686.142 ns and Core 379.510–1,491.671 ns. These broad ranges are part of the evidence; no observation is removed as an outlier or assigned an unverified cause.

## All 77 cells: absolute medians, including losses

Times are ns/op; allocation is B/op. Each cell’s raw durations, counts, short flag, provenance, GC, and retained payload are in the linked detailed CSVs. These medians do not imply statistical significance.

| n | Whole-public cell | B1 ns | Core ns | Core vs B1 | B/op B1 → Core |
| --- | --- | --- | --- | --- | --- |
| 0 | focus-append-after | 20.604 | 21.475 | +4.2% | 0 → 0 |
| 0 | focus-append-before | 21.215 | 22.445 | +5.8% | 0 → 0 |
| 0 | focus-append-contained | 20.197 | 23.919 | +18.4% | 0 → 0 |
| 0 | focus-append-near-subset | 21.386 | 20.418 | -4.5% | 0 → 0 |
| 0 | focus-append-overlap | 20.581 | 22.230 | +8.0% | 0 → 0 |
| 0 | focus-intersection-small-result-0 | 10.662 | 11.317 | +6.1% | 0 → 0 |
| 0 | focus-intersection-small-result-1 | 10.736 | 10.956 | +2.1% | 0 → 0 |
| 0 | focus-intersection-small-result-2 | 10.692 | 12.395 | +15.9% | 0 → 0 |
| 0 | focus-new-capacity | 39.883 | 27.035 | -32.2% | 88 → 56 |
| 0 | focus-union-alias-left | 43.353 | 26.449 | -39.0% | 32 → 0 |
| 0 | focus-union-alias-right | 42.303 | 26.502 | -37.4% | 32 → 0 |
| 0 | focus-union-alias-self | 40.136 | 21.064 | -47.5% | 32 → 0 |
| 0 | full-lifecycle | 148.310 | 135.241 | -8.8% | 232 → 168 |
| 0 | new-exact-intersection | 51.462 | 40.259 | -21.8% | 88 → 56 |
| 0 | new-union | 57.141 | 49.668 | -13.1% | 88 → 56 |
| 0 | target-copy-nonempty | 10.245 | 15.351 | +49.9% | 0 → 0 |
| 0 | target-intersection-nonempty | 9.913 | 10.775 | +8.7% | 0 → 0 |
| 1 | full-lifecycle | 383.986 | 565.161 | +47.2% | 360 → 296 |
| 1 | new-exact-intersection | 69.763 | 68.078 | -2.4% | 120 → 88 |
| 1 | new-union | 161.537 | 128.553 | -20.4% | 128 → 96 |
| 1 | target-copy-nonempty | 37.428 | 59.917 | +60.1% | 0 → 0 |
| 1 | target-intersection-nonempty | 22.355 | 25.463 | +13.9% | 0 → 0 |
| 8 | focus-append-after | 1,014.235 | 550.001 | -45.8% | 88 → 0 |
| 8 | focus-append-before | 939.381 | 735.892 | -21.7% | 88 → 0 |
| 8 | focus-append-contained | 1,065.582 | 554.910 | -47.9% | 88 → 0 |
| 8 | focus-append-near-subset | 920.866 | 663.586 | -27.9% | 88 → 0 |
| 8 | focus-append-overlap | 968.378 | 793.030 | -18.1% | 88 → 0 |
| 8 | focus-intersection-small-result-0 | 88.431 | 79.764 | -9.8% | 0 → 0 |
| 8 | focus-intersection-small-result-1 | 89.898 | 100.701 | +12.0% | 0 → 0 |
| 8 | focus-intersection-small-result-2 | 104.351 | 99.114 | -5.0% | 0 → 0 |
| 8 | focus-new-capacity | 55.895 | 48.112 | -13.9% | 176 → 144 |
| 8 | focus-union-alias-left | 1,287.290 | 869.630 | -32.4% | 120 → 0 |
| 8 | focus-union-alias-right | 1,259.042 | 960.573 | -23.7% | 120 → 0 |
| 8 | focus-union-alias-self | 1,179.452 | 468.128 | -60.3% | 120 → 0 |
| 8 | full-lifecycle | 2,177.422 | 2,046.777 | -6.0% | 616 → 552 |
| 8 | new-exact-intersection | 166.584 | 227.043 | +36.3% | 176 → 144 |
| 8 | new-union | 1,156.178 | 822.782 | -28.8% | 240 → 208 |
| 8 | target-copy-nonempty | 241.748 | 272.255 | +12.6% | 0 → 0 |
| 8 | target-intersection-nonempty | 99.992 | 97.733 | -2.3% | 0 → 0 |
| 64 | full-lifecycle | 19,974.174 | 17,368.590 | -13.0% | 2,928 → 2,864 |
| 64 | new-exact-intersection | 1,420.843 | 1,072.567 | -24.5% | 624 → 592 |
| 64 | new-union | 11,695.141 | 5,627.862 | -51.9% | 1,136 → 1,104 |
| 64 | target-copy-nonempty | 2,679.021 | 2,089.825 | -22.0% | 0 → 0 |
| 64 | target-intersection-nonempty | 769.480 | 765.302 | -0.5% | 0 → 0 |
| 1024 | focus-append-after | 181,612.029 | 79,724.154 | -56.1% | 8,216 → 0 |
| 1024 | focus-append-before | 162,404.045 | 123,877.140 | -23.7% | 8,216 → 0 |
| 1024 | focus-append-contained | 154,075.938 | 85,069.137 | -44.8% | 8,216 → 0 |
| 1024 | focus-append-near-subset | 129,033.642 | 85,636.073 | -33.6% | 8,216 → 0 |
| 1024 | focus-append-overlap | 148,780.639 | 92,281.948 | -38.0% | 8,216 → 0 |
| 1024 | focus-intersection-small-result-0 | 9,101.908 | 12,662.068 | +39.1% | 0 → 0 |
| 1024 | focus-intersection-small-result-1 | 8,417.664 | 8,884.010 | +5.5% | 0 → 0 |
| 1024 | focus-intersection-small-result-2 | 7,633.040 | 14,702.174 | +92.6% | 0 → 0 |
| 1024 | focus-new-capacity | 1,182.112 | 977.364 | -17.3% | 8,304 → 8,272 |
| 1024 | focus-skew-difference-large-left-head | 64,474.093 | 54,767.392 | -15.1% | 0 → 0 |
| 1024 | focus-skew-difference-large-left-mixed | 55,874.515 | 59,545.823 | +6.6% | 0 → 0 |
| 1024 | focus-skew-difference-large-left-spread | 68,068.120 | 71,097.796 | +4.5% | 0 → 0 |
| 1024 | focus-skew-difference-large-left-tail | 71,321.654 | 62,331.244 | -12.6% | 0 → 0 |
| 1024 | focus-skew-difference-small-left-head | 224.456 | 322.387 | +43.6% | 0 → 0 |
| 1024 | focus-skew-difference-small-left-mixed | 7,912.600 | 8,216.262 | +3.8% | 0 → 0 |
| 1024 | focus-skew-difference-small-left-spread | 8,920.785 | 10,649.407 | +19.4% | 0 → 0 |
| 1024 | focus-skew-difference-small-left-tail | 8,324.285 | 8,688.672 | +4.4% | 0 → 0 |
| 1024 | focus-skew-intersection-large-left-head | 71.364 | 54.054 | -24.3% | 0 → 0 |
| 1024 | focus-skew-intersection-large-left-mixed | 7,709.760 | 8,921.535 | +15.7% | 0 → 0 |
| 1024 | focus-skew-intersection-large-left-spread | 8,875.847 | 11,671.697 | +31.5% | 0 → 0 |
| 1024 | focus-skew-intersection-large-left-tail | 8,400.569 | 9,057.414 | +7.8% | 0 → 0 |
| 1024 | focus-skew-intersection-small-left-head | 47.576 | 64.470 | +35.5% | 0 → 0 |
| 1024 | focus-skew-intersection-small-left-mixed | 8,701.436 | 8,868.092 | +1.9% | 0 → 0 |
| 1024 | focus-skew-intersection-small-left-spread | 9,367.661 | 9,827.869 | +4.9% | 0 → 0 |
| 1024 | focus-skew-intersection-small-left-tail | 9,942.763 | 11,554.951 | +16.2% | 0 → 0 |
| 1024 | focus-union-alias-left | 176,679.291 | 139,012.992 | -21.3% | 8,248 → 0 |
| 1024 | focus-union-alias-right | 205,830.593 | 170,195.321 | -17.3% | 8,248 → 0 |
| 1024 | focus-union-alias-self | 210,184.767 | 146,980.666 | -30.1% | 8,248 → 0 |
| 1024 | full-lifecycle | 686,126.844 | 544,506.750 | -20.6% | 41,424 → 41,360 |
| 1024 | new-exact-intersection | 12,760.275 | 16,035.659 | +25.7% | 8,304 → 8,272 |
| 1024 | new-union | 165,272.383 | 109,440.177 | -33.8% | 16,496 → 16,464 |
| 1024 | target-copy-nonempty | 39,440.054 | 36,429.096 | -7.6% | 0 → 0 |
| 1024 | target-intersection-nonempty | 12,370.914 | 12,144.414 | -1.8% | 0 → 0 |

## Adverse cells and restored-control interpretation

- Copy remains adverse at small sizes: n=0 10.245→15.352 ns (+49.9%); n=1 37.428→59.917 ns (+60.1%); n=8 241.748→272.255 ns (+12.6%). n=64 is 2,679.022→2,089.825 ns and n=1024 39,440.055→36,429.096 ns, with short fixed-count intervals at those larger sizes.
- Fresh exact intersection is adverse at n=8 (166.584→227.043 ns, +36.3%) and n=1024 (12,760.276→16,035.659 ns, +25.7%), despite saving 32 B/op.
- Restored sparse controls vary strongly: n=1024 two-match target intersection is 7,633.040→14,702.175 ns (+92.6%), with batch ranges 119.357–171.363 ms versus 145.931–358.993 ms. The restored policy does not justify discarding this timing movement.
- Skew head intersection differs by operand direction: large-left 71.364→54.054 ns, small-left 47.576→64.470 ns. Source-restored kernels are not a performance control that guarantees identical timings in one finite run.
- Difference rows are complete CopyFrom + ExceptWithExact cycles. Their CopyFrom component changed even though the difference policy was restored. Small-left head difference is 224.456→322.387 ns (+43.6%); do not attribute that entire difference to the Except kernel.
- Empty append-contained is 20.197→23.919 ns (+18.4%). Late subset-miss append remains explicitly included, with its extra possible containment scan. No row is excluded because a different shape improves.

## Duration, short intervals, caps, and GC

Measured seconds below sum operation batches and are not an application-throughput metric. Short means below that row’s original target (50 ms for full-map rows, 100 ms for focused-map rows). There are no capped samples in this selected matrix.

| Version | Samples | Measured seconds | Batch ms min / median / max | Short | Capped | GC0 / GC1 / GC2 |
| --- | --- | --- | --- | --- | --- | --- |
| B1 | 308 | 41.997 | 9.578 / 128.358 / 463.063 | 74 | 0 | 3924 / 96 / 40 |
| B11Core | 308 | 36.896 | 10.327 / 104.957 / 415.186 | 113 | 0 | 3176 / 60 / 12 |

Short split: B1 has 40/100 full-map samples and 34/208 focused-map samples; Core has 49/100 full-map and 64/208 focused-map samples. B1’s measured round totals are 8.331 / 9.268 / 9.215 / 15.183 s; Core’s are 8.062 / 8.364 / 11.121 / 9.349 s. This demonstrates substantial variation in the finite run, without identifying its cause.

## Coverage gaps and evidence boundaries

- No original direct append/alias/capacity-only cells exist at n=1 or n=64. Those sizes use the original full copy/union/intersection/lifecycle workloads. Nonalias union is not alias coverage.
- Difference-specific controls exist only at n=1024. No missing-cell counts were invented.
- This final 77-cell matrix does not remeasure all 151 full-public cells, B0, isolated exact lookup/enumeration/serialization/rebuild, or fresh full-overlap intersection. Earlier experimental full matrices must not be relabeled as Core results.
- Public B1 exact lookup and enumerator logic remain name-based and unchanged. No P1 integer representation or integer-mapping attribution applies.
- Allocation and GC share the timing batch. Restoration and observers stay inside measurement; warmup/preparation and source/result checks stay outside. Retained array payload excludes headers, strings, whole heap, and peak transient memory. Lifecycle retention is a representative-input proxy, not the measured output tuple.

## Experimental history remains preserved

The original First screen retains its n=1024 lifecycle +51.2% median (465.445→703.707 µs), including its spread and extra allocation. The Narrow repair and 151-cell confirmation retain their own adverse n=1/small-intersection observations and capacity tradeoffs. Those reports describe discarded intersection/difference-policy experiments; none establishes a causal resolution of the original latency result.

The final source restores B1’s intersection/difference policies and keeps the reviewed batching/append/union/copy/constructor core. This scope decision is distinct from a claim that reverting a policy eliminated every timing loss. The Core dataset above includes remaining losses and restored-control variation.

## Evidence and reproduction

- [Final core protocol](../Tests~/Roadmap/CORE_BENCHMARK.md), [all 616 raw samples](../Tests~/Roadmap/Evidence/core-final/raw.csv), [all 77 comparisons](../Tests~/Roadmap/Evidence/core-final/comparison.csv), [detailed duration/GC/payload](../Tests~/Roadmap/Evidence/core-final/detailed-summary.csv), [audit](../Tests~/Roadmap/Evidence/core-final/audit.json), [source/count identity audit](../Tests~/Roadmap/Evidence/core-final/source-identity-audit.json), [count provenance](../Tests~/Roadmap/Evidence/core-final/count-provenance.csv).
- Experimental history: [First screen](B11_PUBLIC_RESULTS.md), [Narrow repair](B11_NARROW_RESULTS.md), [Narrow confirmation](B11_NARROW_CONFIRMATION_RESULTS.md).
- Each stage keeps raw rounds, environment logs, maps, source-before/source-after hashes, SDK version, and wall record. Baselines/B11First and Baselines/B11Narrow preserve the experimental Runtime sources. The executed core-b11.sh is unchanged; replay requires restoring the archived historical maps/inventory paths specified in the protocol.
