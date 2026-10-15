# C1-removal diagnostic: mixed results, cause unconfirmed

Production Runtime remains exact R. This bounded comparison did not reproduce a consistent lifecycle benefit from removing C1. Lifecycle 1024 and retained restore-plus-copy each had three slower pairs and one faster pair after removal. Prepared single-copy had three faster pairs and one slower pair, with all eight samples below the minimum duration. The predeclared stopping rule applies: the cause remains unconfirmed and this investigation round ends. No rollback, new variant, favorable rerun, profiling, repeated A/A, or production promotion follows.

## Exact scope and completed gates

R is the complete 16-file Runtime snapshot at `792ce83f1c2a9aeaadc045c192d70081d866d903`. RMinusC1 changes only the `CopyValidated` body to the exact implementation from `352d304299ecbb7486e2c6a56ba71874c90fe21b`, independently matched against the existing PR4 baseline. C2 and every surrounding Runtime byte remain unchanged. See the [source proof](source-proof.json), [exact patch](CopyValidated-removal.patch), and [source/semantic receipts](SOURCE_AND_SEMANTIC_RECEIPTS.md).

Before measurement, both sources passed 1,002,001 Roadmap assertions, 197,333 C12 assertions, equal transcripts in both suites, and .NET Standard 2.1 / C# 9 compilation. [Semantic receipts](Evidence/Semantics/summary.json) preserve those results. This is a new `C1RemovalDiagnostic` assembly/harness context comparing these two snapshots now; it does not reconstruct historical C1 effects.

The collection completed with 145 fresh-process target invocations: 24 untimed fixtures, 25 fresh R-only calibration pilots, and 96 formal samples across 12 cells. All invocations exited zero and validated; all stderr was empty. All 121 numerical records remain available. Source/binary/harness inventories and fixed counts passed their collection checks. The fixed-count SHA256 is `c7e5938ed3615aedc65d827cecf735a10b3c6be3f6e705b03fd86b8faeaeda88`. The environment was .NET SDK 8.0.425, Microsoft.NETCore.App 8.0.31, Linux x86_64, with tiered compilation disabled. Completion is not performance acceptance.

## Main public-workflow boundaries

Ratios are RMinusC1/R. Below 1 means faster after removing C1; above 1 means slower. Each cell uses the predefined AB, BA, BA, AB pair order with A = R and B = RMinusC1. Displayed values are rounded; [paired.csv](Evidence/paired.csv) and [raw-flat.csv](Evidence/raw-flat.csv) retain full values, individual signs, elapsed durations, and counters.

| Boundary | Pair 0 (AB) | Pair 1 (BA) | Pair 2 (BA) | Pair 3 (AB) | Ratio min / median / max | Faster / slower |
|---|---:|---:|---:|---:|---:|---:|
| Lifecycle 1024 | 1.164571 | 1.041119 | 1.102397 | 0.889318 | 0.889318 / 1.071758 / 1.164571 | 1 / 3 |
| Retained restore + copy 1024←512 | 1.070271 | 0.869431 | 1.023648 | 1.151658 | 0.869431 / 1.046960 / 1.151658 | 1 / 3 |

| Boundary | Calls per sample | R ns/op min / median / max | RMinusC1 ns/op min / median / max | All formal durations, ms min–max |
|---|---:|---:|---:|---:|
| Lifecycle 1024 | 1,024 | 362186.430 / 376660.674 / 417218.473 | 371040.004 / 399952.174 / 429159.807 | 370.878904–439.459642 |
| Retained restore + copy 1024←512 | 7,837 | 37835.906 / 40740.389 / 44511.335 | 38699.520 / 42634.947 / 43574.023 | 296.519999–348.835335 |

The complete lifecycle includes shuffled Build, lookup, removal/reinsertion, enumeration, public union, public exact intersection, CopyFrom(intersection), and final Clear. Its input counts are 1024; the resulting intersection has 512 members. The separate retained copy workflow includes CopyFrom(seed) restoration, CopyFrom(source), and its consumer on every operation. It is not the lifecycle call alone. Neither boundary has four faster removal pairs. Their median ratios cannot replace the mixed signs or establish causal attribution.

## Prepared single-copy diagnostic, reported separately

| Boundary | Pair 0 (AB) | Pair 1 (BA) | Pair 2 (BA) | Pair 3 (AB) | Ratio min / median / max | Faster / slower |
|---|---:|---:|---:|---:|---:|---:|
| Prepared single copy 1024←512 | 0.969159 | 0.905779 | 0.990519 | 1.008309 | 0.905779 / 0.979839 / 1.008309 | 3 / 1 |

| Boundary | Calls per sample | R ns/op min / median / max | RMinusC1 ns/op min / median / max | All formal durations, ms min–max |
|---|---:|---:|---:|---:|
| Prepared single copy 1024←512 | 16,384 | 13341.617 / 13896.226 / 14383.668 | 13028.424 / 13434.124 / 13818.724 | 213.457698–235.662011 |

All eight formal samples are short: 213.457698–235.662011 ms, below 250 ms. Calibration reached the fixed 16,384-target cap while still short; the count was not increased or replaced. This boundary therefore cannot rescue the mixed lifecycle/retained-copy result, even though its descriptive median ratio is below 1. Its pair signs also disagree with a uniformly faster removal interpretation.

Each target began with 1,024 members/capacity 1,024 and received the 512-member source once. Target construction was outside counters; each prepared target was copied once inside, with no restoration or prior copy. The measured operation included CopyFrom(source), delegate/index/loop overhead, and the consumer. All 16,384 output targets and spare tails were validated after the numeric record. No costs were subtracted.

Every formal pool had actual backing reference-array payload 134,217,728 B (128 MiB), plus a 131,072 B outer reference-vector payload. Setup allocation ranged 135,659,952–135,674,168 B, below the 150,994,944 B ceiling. Each formal setup recorded GC0/1/2 deltas of 24/13/3; timed copies recorded 0/0/0 and 0 B/op. Setup had no stopwatch. Those collection counts describe memory-pressure context and do not establish pause durations. They do not include all process memory or support an RSS/peak-memory claim.

Warmup used 64 separate disposable targets before sequential pool construction. The first measured target was the oldest prepared target. This is neither a cache-cold experiment nor a first-JIT-call measurement. Pool memory/cache context differs from repeatedly restored hot-target copies; medians must not be subtracted or combined across these boundaries.

## Every guard, including removal losses

| Boundary | Pair 0 (AB) | Pair 1 (BA) | Pair 2 (BA) | Pair 3 (AB) | Ratio min / median / max | Faster / slower |
|---|---:|---:|---:|---:|---:|---:|
| Retained copy 0←0 | 2.290639 | 2.070768 | 1.672857 | 2.224070 | 1.672857 / 2.147419 / 2.290639 | 0 / 4 |
| Retained copy 1←1 | 1.371585 | 1.259272 | 1.323987 | 1.201771 | 1.201771 / 1.291629 / 1.371585 | 0 / 4 |
| Retained copy 1←0 | 1.333440 | 1.483618 | 1.580976 | 1.549012 | 1.333440 / 1.516315 / 1.580976 | 0 / 4 |
| Retained copy 2←1 | 1.143032 | 0.979190 | 1.192539 | 1.171476 | 0.979190 / 1.157254 / 1.192539 | 1 / 3 |
| C2 late append, new | 0.959525 | 1.096272 | 1.130274 | 0.942889 | 0.942889 / 1.027899 / 1.130274 | 2 / 2 |
| C2 late append, retained | 0.992269 | 0.908264 | 0.930470 | 1.131173 | 0.908264 / 0.961369 / 1.131173 | 3 / 1 |
| C2 late right-alias union, new | 1.104698 | 0.994874 | 1.029686 | 0.985009 | 0.985009 / 1.012280 / 1.104698 | 2 / 2 |
| C2 late right-alias union, retained | 0.836021 | 1.054036 | 0.886244 | 0.973940 | 0.836021 / 0.930092 / 1.054036 | 3 / 1 |
| Lifecycle 1 | 1.187412 | 1.008026 | 0.942210 | 1.090465 | 0.942210 / 1.049246 / 1.187412 | 1 / 3 |

| Boundary | Calls per sample | R ns/op min / median / max | RMinusC1 ns/op min / median / max | All formal durations, ms min–max |
|---|---:|---:|---:|---:|
| Retained copy 0←0 | 21,043,979 | 14.757 / 15.850 / 18.839 | 31.516 / 33.768 / 34.237 | 310.538830–720.483007 |
| Retained copy 1←1 | 4,198,615 | 80.487 / 83.809 / 92.244 | 106.563 / 109.947 / 111.138 | 337.932503–466.623928 |
| Retained copy 1←0 | 7,083,238 | 46.988 / 49.838 / 53.028 | 70.709 / 73.240 / 79.023 | 332.826978–559.740066 |
| Retained copy 2←1 | 2,785,233 | 113.665 / 118.826 / 143.099 | 129.922 / 137.374 / 146.363 | 316.582324–407.655072 |
| C2 late append, new | 3,715 | 75076.200 / 79358.663 / 81767.904 | 76389.833 / 81657.524 / 85180.897 | 278.908084–316.447032 |
| C2 late append, retained | 4,128 | 62623.942 / 67989.407 / 74179.721 | 61364.235 / 68431.214 / 70838.495 | 253.311562–306.213887 |
| C2 late right-alias union, new | 2,904 | 96827.331 / 104457.451 / 114221.600 | 101571.537 / 108335.765 / 112509.306 | 281.186569–331.699525 |
| C2 late right-alias union, retained | 2,971 | 104523.217 / 109965.122 / 112813.702 | 90837.963 / 100889.900 / 117288.024 | 269.879588–348.462720 |
| Lifecycle 1 | 973,578 | 319.859 / 327.467 / 385.908 | 329.408 / 356.201 / 389.647 | 311.408084–379.352156 |

The 0←0, 1←1, and 1←0 copy guards were slower after removal in all four pairs. The 2←1 guard had three slower pairs and one faster pair. Their median paired ratios were 2.147419, 1.291629, 1.516315, and 1.157254 respectively. These adverse results are retained. Each small-copy workflow includes restoration; 1←0 is a genuinely nonempty destination cleared by an empty source, and 2←1 checks shrinking to singleton. The empty-to-empty row makes no nonempty-clear claim.

All four C2 timing guards and lifecycle 1 were mixed. C2 source code is unchanged. A changing whole-workflow timing observation is not proof that C2 changed, nor does a favorable guard median resolve the main result.

## Allocation, storage, GC, and sample quality

- Lifecycle 1024 allocated 41,360 B/op in both versions. Union was 1,536 members/capacity 2,048; intersection was 512/capacity 1,024; final target was empty/capacity 1,024. Each formal sample recorded timed GC deltas 9/1/0.
- Retained restore-plus-copy, prepared single-copy, and all four small-copy guards allocated 0 B/op in both versions with timed GC deltas 0/0/0. The main copy boundaries retained target and source capacities of 1,024.
- Both new-target C2 guards allocated 16,496 B/op and ended with 1,025 members/capacity 1,025. Timed GC deltas were 15/1/0 for new append and 11/1/0 for new right-alias union. Both retained-target C2 guards allocated 0 B/op, retained capacity 2,048, and recorded timed GC deltas 0/0/0. These values held in both versions.
- Lifecycle 1 allocated 296 B/op in both versions and recorded timed GC deltas 68/1/0 per formal sample.
- All source-invariance, representative-identity, content/order, capacity, and cleared-tail checks passed. No fractional allocation rows were observed. For transient outputs, validation covers the final output and full lifecycle results; it does not claim every discarded transient object was individually inspected.
- Eleven baseline cell calibrations attained 250 ms. Eight formal samples were short, all prepared-copy; the other 88 formal samples reached 250 ms. Fourteen short pilot rows are also preserved. See [all short samples](Evidence/short-samples.csv), [storage/GC context](Evidence/storage-gc-context.csv), and [quality receipt](Evidence/QUALITY.json).

## Interpretation and evidence limits

There was no new physical-binary A/A collection, retry, trimming, normalization, alternate count choice, or favorable resampling. Earlier first A/A spread remains background context only. It is neither an acceptance cutoff nor a noise allowance that can be subtracted from this new harness. Four unanimous paired signs would have two-sided sign-test p = 0.125 before multiplicity; the primary observations were mixed. Descriptive medians and four pairs cannot establish a robust speedup or historical cause.

Copy-only benefit would not explain the lifecycle observation. Here the retained and prepared copy boundaries also differ in their observed directions and measurement context, and every prepared sample is short. The predefined condition for proposing further isolation was not met. The recorded conclusion is cause unconfirmed, stop this round, production unchanged.

The [call index](Evidence/call-index.jsonl) preserves every invocation and identifies exact original stdout, terminal, and stderr byte ranges with hashes. Six raw shards plus terminal/stderr shards retain the original bytes; [identity](Evidence/IDENTITY.json) records sources, binaries, compilation inputs, and sanitized provenance. [Frozen measured inputs](Evidence/TimedHarness/) retain their original bytes, including preparation-era README/protocol text. Publication documentation is a separately recorded post-measurement change, not a retroactive alteration of timed inputs. Existing earlier reports remain intact.

The [README](README.md) gives read-only evidence and separate correctness commands. No further targets were invoked to write this report. .NET 8 Linux results do not certify Unity Editor/Player, Mono, IL2CPP, or device performance.
