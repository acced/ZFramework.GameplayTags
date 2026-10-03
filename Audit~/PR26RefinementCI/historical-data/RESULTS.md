# Recorded performance results

Historical Linux x64 .NET 8.0.31 data only. These descriptive classifications use three paired process rounds, seven samples per process, and all six baseline crossings beyond max(3%, measured A/A drift). They are not statistical significance claims. Raw samples and all batch-tail fields are retained in the archives.

## Exact final DLL: six cohorts

| Cohort | Cells | Median improvements | Median regressions | Uncertain/small | Invalid |
|---|---:|---:|---:|---:|---:|
| final-query-default | 111 | 0 | 2 | 109 | 0 |
| final-query-nohw | 111 | 0 | 0 | 111 | 0 |
| final-builder-default | 90 | 15 | 0 | 72 | 3 |
| final-builder-nohw | 90 | 4 | 1 | 82 | 3 |
| final-mixed-default | 225 | 48 | 0 | 177 | 0 |
| final-mixed-nohw | 225 | 1 | 1 | 223 | 0 |

Total: **852 cells, 53,676 timed samples; 68 improvements, 4 regressions, 774 uncertain/small, 6 invalid**. Calibrations and first-use probes are retained separately and are not counted in those timed samples.

## All four final median regressions

| Cohort | Layout | Cell | Candidate/baseline median | Maximum batch-tail ratio |
|---|---|---|---:|---:|
| final-query-default | Dense | `query/scatter/8/query64/balanced/same/0` | 1.040139 | 1.484035 |
| final-query-default | Auto | `query/scatter/8/query64/miss-bit/same/0` | 1.107940 | 1.569775 |
| final-builder-nohw | Dense | `builder/cluster/4096/build/sorted/same/0` | 1.264773 | 1.377960 |
| final-mixed-nohw | Micro | `mixed/random/4096/restore-remove/equal/same/4096` | 1.119471 | 1.216815 |

## All six invalid controls

These allocating Dense controls have at least one sample below the predeclared 1 ms minimum at their budget-capped fixed count. All raw observations remain visible, but their performance is unranked.

| Cohort | Layout | Cell | Minimum observed sample (ms) |
|---|---|---|---:|
| final-builder-default | Dense | `builder/scatter/0/build/sorted/same/0` | 0.620946 |
| final-builder-default | Dense | `builder/scatter/0/build/shuffled/same/0` | 0.600005 |
| final-builder-default | Dense | `builder/scatter/1/build/sorted/same/0` | 0.290849 |
| final-builder-nohw | Dense | `builder/scatter/0/build/sorted/same/0` | 0.630040 |
| final-builder-nohw | Dense | `builder/scatter/0/build/shuffled/same/0` | 0.601528 |
| final-builder-nohw | Dense | `builder/scatter/1/build/sorted/same/0` | 0.306763 |

## Allocation and tails

Unordered Micro construction falls from 168 to 112 B/op at 8 unique members, 616 to 336 B/op at 64, and 32,872 to 16,464 B/op at 4,096. Each backend has 36 allocation-reducing builder cells and 10 allocation-reducing mixed/lifecycle cells. All prepared operations allocate 0 B/op. First-use Micro8 is separately recorded at 192 versus 136 B; it is not the warmed allocation figure.

Median gains do not promise tail gains: default Auto8 shuffled construction has a 0.833 median ratio and a 1.860 maximum batch-tail ratio. Batch p95/max ratios are not estimates of individual-operation latency percentiles.

## Separate factor matrices

Bridge split: all 54 cells per backend are retained. Against old Direct, default has 10 improvements/0 regressions/44 uncertain and nohw 19/0/35. Against legacy public matching, default has 14/0/40 and nohw 25/2/27. The two nohw legacy-relative losses remain visible: Micro8 and Auto8 exact-hit. These are fixed-actor/hot-query observations, and charged mutation results include representation costs.

Count helper v3: all 18 cells per backend are retained. Default v3/baseline has 8 improvements/0 regressions/10 uncertain; direct v3/v2 is entirely uncertain. Nohw v3/baseline is entirely uncertain, while direct v3/v2 has 4 improvements/4 regressions/10 uncertain. This targeted factor does not replace the broad matrix or support a universal v3 speedup.

## Constructor diagnostic: prerequisite failed

The 36-process follow-up retained 3,024 samples, including 252 target samples. Every target uses 973 operations and 32,848 B/op. The original Work + prefix cell failed its predeclared reproduction gate. Its ratio is 1.172 with 10.6% maximum A/A drift, versus the original final cohort’s 1.265. SortedOnly + prefix has a separate 1.230 clear median regression; both target-first cells are uncertain. The follow-up does not establish the cause of the original loss or change its verdict.

## Source and validation interpretation

The performance DLL is the final bridge split (`1c032a39…`), with matched baseline (`1dd0309a…`). Historical `runtime-final-*` validation directories refer to an earlier v2 Runtime (`f98c4538…` in normal net8), despite their historical names. `runtime-v3-*` refers to the hardware-gated count revision (`54655535…` normal net8), and `runtime-bridge-split-*` to the final selected range split. Incremental semantic scopes and their compiler/source/DLL receipts stay separate. The final split gate contains 18 bridge/mutation/interop processes; the preceding v3 focused correctness gate contains 36 semantic/count/fault/interop/mutation processes.

Historical binaries are omitted. Receipts record what was checked at the time, while the provided read-only reanalysis validates retained raw bytes and calculations. All results here remain x64 managed evidence; no native Unity, Mono, IL2CPP, Burst, ARM64 or device performance is established.
