# Published final-candidate CI evidence

## Decision

**Do not treat this candidate as an accepted all-workload performance upgrade.** The exact published Runtime improves several hot operations, but the remote Mono matrix has two material warm regression flags as well as repeated first-use costs. The local report's absence of detected material warm regressions does not supersede these new observations. PR #3 remains draft; original PR #1 Runtime and its branch are unchanged.

Frozen baseline: `7037e76a43127939ab9545b2e1056441202961e6`.
Published Runtime: `ecfd03c08a731f147acd04810ca4afee0a2ecc65`.
This document changes no Runtime source.

## Source and artifact verification

The publication job verified the frozen patch and every Runtime source digest before tests and commit creation. Both resulting evidence archives were downloaded, SHA256-checked and inspected. All 21 C# Runtime hashes and four harness hashes match the final local files. Both variants completed the independent 646,194-assertion supplement, and every warm/cold paired observable checksum/state agrees.

Publication and evidence run: https://github.com/acced/ZFramework.GameplayTags/actions/runs/38235003046

- .NET artifact `11739938595`, `final-published-net-ecfd03c08a731f147acd04810ca4afee0a2ecc65`, SHA256 `3a7abfb8c3f5b557ceaa329f4ff971a90ac85f9a27ac4438f12300d969a2e333`.
- Mono artifact `11740393064`, `final-published-mono-ecfd03c08a731f147acd04810ca4afee0a2ecc65`, SHA256 `c1596aca87148f4f0af7efddd6c02e3b7aeddfd0903fa07620ac4848dae93420`.

The .NET host is AMD EPYC 9V45 with .NET 8.0.31; the separate Mono host is AMD EPYC 7763 with standalone Mono 6.8.0.105. Each backend compares baseline and candidate on its own same machine/CPU affinity. Absolute times between the two backends must not be compared as a runtime ranking.

Each matrix contains 56 fixed public-API workloads, 14 alternating independent process pairs, a single baseline-only 50 ms pilot and common iteration counts. All raw samples, allocation/GC counts, order, compiler/runtime identities and source/binary hashes are retained. Faster candidates can finish sooner than the pilot target; minimum batch durations are included and some are only 10–30 ms. Approximate 99% intervals are per-row paired-log estimates, not simultaneous guarantees. No samples were dropped.

## Representative warm results

Microseconds are medians of the complete named operation. Speedup is paired geometric mean and therefore need not equal the ratio of the displayed medians. The intersection fixture has 50% explicit overlap except `full`, which uses independent equal inputs.

| Operation | .NET before / after us | Paired speedup | Mono before / after us | Paired speedup |
|---|---:|---:|---:|---:|
| 1024 exact hit | 0.003056 / 0.002617 | 1.179x | 0.012333 / 0.010495 | 1.181x |
| 1024 explicit enumeration | 0.787 / 0.664 | 1.177x | 7.602 / 4.444 | 1.712x |
| 1024 remove/add pair | 0.024946 / 0.023783 | 1.062x | 0.201958 / 0.180940 | 1.119x |
| 1024 fresh item-by-item build | 26.473 / 23.927 | 1.097x | 163.892 / 168.102 | 0.975x |
| 1024 reused Copy | 0.299 / 0.309 | 0.994x | 1.622 / 1.626 | 0.997x |
| 1024 new Union | 10.548 / 10.776 | 1.015x | 65.183 / 65.460 | 1.003x |
| 1024 reused Union | 5.986 / 6.012 | 1.049x | 30.363 / 29.881 | 1.042x |
| 1024 Copy + AddTags | 14.379 / 11.616 | 1.256x | 74.423 / 69.720 | 1.096x |
| 1024 reused general intersection | 6.096 / 6.115 | 0.996x | 47.742 / 45.617 | 1.086x |
| 1024 workspace intersection | 5.225 / 5.138 | 1.002x | 33.479 / 32.433 | 1.048x |
| 1024 full-hit intersection | 3.265 / 2.186 | 1.499x | 20.937 / 13.580 | 1.537x |
| 1024 Copy + Clear | 1.281 / 0.306 | 4.186x | 4.631 / 1.626 | 2.872x |
| 1024 Clear + refill | 15.455 / 13.053 | 1.192x | 88.098 / 87.002 | 1.023x |
| depth 3000 fresh single tag | 26.386 / 13.415 | 1.979x | 194.935 / 77.994 | 2.506x |
| dense depth-128 Buff dispatch | 4.048 / 4.071 | 0.995x | 14.703 / 14.662 | 1.000x |

Warm allocation never increases in these 56 workloads. Existing zero-byte paths remain zero, including both versions' already-present reused Union and Copy + AddTags alternatives. Deep single-tag fresh allocation is .NET 196,952 -> 98,648 B and Mono 197,112 -> 98,680 B. This is cumulative construction allocation, not an across-the-board retained-memory reduction.

## Warm losses and uncertainty

.NET: 26 rows classified as >3% improvement, 30 inconclusive, no material-regression flag; 38/56 intervals bound possible slowdown below 3%.

Mono: 23 >3% improvement, 31 inconclusive, **2 material-regression flags**; 44/56 intervals bound possible slowdown below 3%.

The two Mono blockers are:

| Operation | Before / after ns | Paired speedup | Approx. 99% interval | Slower pairs |
|---|---:|---:|---:|---:|
| 4-item workspace intersection | 275.608 / 355.906 | 0.7967x | 0.7384–0.8596 | 14/14 |
| 4-item Clear + refill | 361.838 / 384.454 | 0.9420x | 0.9144–0.9704 | 13/14 |

Both are zero-allocation, with minimum batches above 45 ms. They cannot be dismissed as cold/JIT cost or outweighed by larger-case wins. Other Mono adverse intervals include 4-item Copy, 4-item full intersection, and 1024-item fresh build. The fresh build is slower in all 14 pairs (paired speedup 0.9745, interval 0.9644–0.9847). A classifier named `inconclusive` means it did not meet the chosen >3% threshold; it does not mean there is no evidence of a smaller loss.

The .NET general and workspace intersections are essentially unresolved/neutral on this host; the local small gains do not establish a cross-host guarantee. Dense Buff algorithms are unchanged and have no established improvement here. No overall average is used to hide losing rows.

## First-use losses

Each backend retains 64 fresh processes: 8 fixture kinds x 4 alternating pairs x 2 versions. The operation being measured is not warmed first. Paired explicit counts, closure counts and event counts agree; none of the sampled allocation byte counts increases.

After/before latency ratios, all four samples retained:

| First-use operation | .NET ratios | Mono ratios |
|---|---|---|
| first AddTag, one shallow tag | 1.388, 1.267, 1.382, 1.447 | 1.155, 1.143, 1.169, 1.143 |
| first deep-128 tag | 1.227, 1.240, 1.193, 1.241 | 1.131, 1.135, 1.109, 1.066 |
| first general intersection, 1024 | 1.186, 1.201, 1.144, 1.186 | 1.104, 1.130, 1.108, 1.099 |

Cold Clear and Copy improve in these samples, but they do not cancel the above costs. Four pairs do not provide a global cold-latency guarantee. Allocated bytes alone are not a latency metric. Mono first Clear records 80 B of runtime initialization on both versions, separate from the warm zero-byte path.

## Validation and limitations

The publication validation passes portable runtime build, the original 7,595,855 core assertions, 31,738,094 Bulk assertions, 16,101,616 intersection assertions, 646,194 new assertions, registry/3000-deep tests, Unity conditional compilation, pinned source-generator project and package metadata checks. An initial publishing attempt used a legacy helper that chose SDK 10 Roslyn against .NET 8 references; only that invocation was corrected, no Runtime digest changed.

PR-triggered runtime validation also completed successfully: https://github.com/acced/ZFramework.GameplayTags/actions/runs/38235479884 . PR-triggered evidence completion is https://github.com/acced/ZFramework.GameplayTags/actions/runs/38235479887 ; the numeric tables in THIS report deliberately refer only to the verified publication artifacts above, not a mixture of runs.

No Unity 6000.3.14f1 Editor/Player, IL2CPP, 32-bit or Alex measurement was executed. The new 4-byte storage hint may increase 32-bit object size. All existing historical evidence remains; neither green CI nor warm allocation equality overrides the explicit failed performance objective.
