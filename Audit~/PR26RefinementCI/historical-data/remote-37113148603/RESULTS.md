# PR26 exact-commit remote performance results

Run 37113148603; exact commit `fc555ca1ce637c5d04274945ce90734afc89f2f1`. [GitHub run](https://github.com/acced/ZFramework.GameplayTags/actions/runs/37113148603), attempt 1. All 16 performance artifacts pass raw/statistical checks and independent source/build/deployed-DLL audits. The analysis reran no benchmarks.

Each shard ran on its own hosted VM. Ratios and sample distributions are not pooled across hosts, architectures, groups or backends. Counts enumerate workload classifications only. A green execution/integrity job does not mean performance superiority. There are 1,920 measured cells and 125,496 timed samples across the 16 shards. [results-summary.json](results-summary.json) records per-shard identities, artifact digests, audits, statistics, all regression/invalid rows and artifact expiration metadata. [README.md](README.md) documents the retained raw subset and verification limits.

## Hardware-enabled/default main results

These are the main query, builder and mixed matrices with hardware intrinsics enabled: **426 cells per architecture**. The table adds classification counts from the three separately measured shards; it does not average or pool their timings. Bridge comparisons have different baselines and remain separate.

| Architecture | Cells | Improvements | Regressions | Uncertain/small | Invalid |
|---|---:|---:|---:|---:|---:|
| x64 default | 426 | 126 | 31 | 265 | 4 |
| ARM64 default | 426 | 68 | 3 | 353 | 2 |

Hardware-enabled bridge split versus old Direct is 15 improvements / 2 regressions / 37 uncertain on x64, and 17 / 0 / 37 on ARM64. Against legacy public matching, it is 36 / 4 / 14 on x64 and 31 / 5 / 18 on ARM64; all bridge cells are valid.

### Hardware-disabled controls

`nohw` deliberately disables .NET hardware intrinsics. These controls are reported separately from the default main results and are not netstandard timing measurements.

| Architecture | Cells | Improvements | Regressions | Uncertain/small | Invalid |
|---|---:|---:|---:|---:|---:|
| x64 nohw control | 426 | 14 | 9 | 399 | 4 |
| ARM64 nohw control | 426 | 31 | 2 | 389 | 4 |

## Findings

- Every architecture/backend repeats the same unordered Micro allocation examples: 168→112 B/op at 8 members, 616→336 at 64, and 32,872→16,464 at 4,096. Each builder shard has 36 allocation-reducing cells, each mixed shard has 10, no cell increases allocation, and all prepared samples remain 0 B
- Throughput is mixed. Default x64 has substantial gains and losses in both query and mixed workloads. ARM64 query has no classified median changes on either backend, while ARM64 mixed retains three default losses and one nohw loss
- Bridge split improves many cells against old Direct, but two default x64 split/old-Direct losses remain. Against legacy public matching, empty-query losses remain on both architectures; the full named loss lists follow
- Fourteen remote builder controls fail the 1 ms duration gate and remain unranked: four on each x64 backend, two on ARM64 default, four on ARM64 nohw. These do not replace the six historical local invalid controls
- Six cells classified as selected-versus-baseline improvements have a maximum batch-tail ratio above 1.10. A/A drift and batch tails are reported per shard and per cell; no architecture or overall timing average is formed

## Per-shard results

| Architecture | Backend | Group | Cells | Improvements | Regressions | Uncertain | Invalid | Allocation reductions | Max A/A drift |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|
| arm64 | default | bridge | 54 | 31 | 5 | 18 | 0 | 0 | 36.9% |
| arm64 | nohw | bridge | 54 | 30 | 6 | 18 | 0 | 0 | 39.4% |
| arm64 | default | builder | 90 | 27 | 0 | 61 | 2 | 36 | 16.5% |
| arm64 | nohw | builder | 90 | 29 | 1 | 56 | 4 | 36 | 28.6% |
| arm64 | default | mixed | 225 | 41 | 3 | 181 | 0 | 10 | 11.8% |
| arm64 | nohw | mixed | 225 | 2 | 1 | 222 | 0 | 10 | 16.1% |
| arm64 | default | query | 111 | 0 | 0 | 111 | 0 | 0 | 1.7% |
| arm64 | nohw | query | 111 | 0 | 0 | 111 | 0 | 0 | 1.8% |
| x64 | default | bridge | 54 | 36 | 4 | 14 | 0 | 0 | 10.4% |
| x64 | nohw | bridge | 54 | 10 | 7 | 37 | 0 | 0 | 53.0% |
| x64 | default | builder | 90 | 21 | 2 | 63 | 4 | 36 | 23.5% |
| x64 | nohw | builder | 90 | 14 | 7 | 65 | 4 | 36 | 33.4% |
| x64 | default | mixed | 225 | 50 | 10 | 165 | 0 | 10 | 76.5% |
| x64 | nohw | mixed | 225 | 0 | 2 | 223 | 0 | 10 | 123.5% |
| x64 | default | query | 111 | 55 | 19 | 37 | 0 | 0 | 72.6% |
| x64 | nohw | query | 111 | 0 | 0 | 111 | 0 | 0 | 18.5% |

Bridge table counts compare selected split Direct against legacy public matching. Its old-Direct comparison counts and every row are retained separately in results-summary.json and derived-results.tar.gz. Representation costs are part of the charged bridge workload.

## Allocation and batch-tail checks

| Shard | Prepared max bytes | Allocating cells with increased bytes | Median/max A/A drift | Largest valid p95 batch ratio | Largest valid max batch ratio | Median gains with max batch ratio >1.10 |
|---|---:|---:|---|---:|---:|---:|
| ci-arm64-bridge-default | 0 | 0 | 0.60% / 36.86% | 1.554821 | 1.554298 | 0 |
| ci-arm64-bridge-nohw | 0 | 0 | 0.64% / 39.44% | 1.136667 | 1.136943 | 0 |
| ci-arm64-builder-default | N/A | 0 | 1.85% / 16.51% | 1.266984 | 1.383982 | 0 |
| ci-arm64-builder-nohw | N/A | 0 | 2.02% / 28.58% | 1.279160 | 1.278853 | 0 |
| ci-arm64-mixed-default | 0 | 0 | 0.64% / 11.79% | 1.210662 | 1.211731 | 0 |
| ci-arm64-mixed-nohw | 0 | 0 | 0.85% / 16.13% | 1.269634 | 1.294076 | 0 |
| ci-arm64-query-default | 0 | 0 | 0.13% / 1.72% | 1.029698 | 1.030657 | 0 |
| ci-arm64-query-nohw | 0 | 0 | 0.18% / 1.75% | 1.029672 | 1.039618 | 0 |
| ci-x64-bridge-default | 0 | 0 | 0.67% / 10.39% | 1.225405 | 1.234033 | 3 |
| ci-x64-bridge-nohw | 0 | 0 | 6.54% / 53.01% | 1.904356 | 1.934405 | 0 |
| ci-x64-builder-default | N/A | 0 | 1.45% / 23.54% | 1.693348 | 1.924111 | 0 |
| ci-x64-builder-nohw | N/A | 0 | 1.19% / 33.37% | 1.701462 | 1.816924 | 3 |
| ci-x64-mixed-default | 0 | 0 | 5.59% / 76.53% | 2.069703 | 2.049757 | 0 |
| ci-x64-mixed-nohw | 0 | 0 | 0.79% / 123.52% | 2.417581 | 2.444188 | 0 |
| ci-x64-query-default | 0 | 0 | 0.51% / 72.55% | 1.239067 | 1.243683 | 0 |
| ci-x64-query-nohw | 0 | 0 | 0.84% / 18.47% | 1.370608 | 1.525280 | 0 |

Tail maxima enumerate valid cells within each shard; p95 and maximum columns may refer to different cells. N/A means the builder-only shard has no prepared-operation rows. Full per-cell crossings and derived JSON/CSV rows are in derived-results.tar.gz. Original timing/calibration/stdout/stderr/manifest bytes are in raw-*.tar.gz, with complete mapping and hashes in raw-inventory.json.

Unordered Micro allocation examples (bytes per operation):

| Shard | Members | Baseline | Selected |
|---|---:|---:|---:|
| ci-arm64-builder-default | 8 | 168 | 112 |
| ci-arm64-builder-default | 64 | 616 | 336 |
| ci-arm64-builder-default | 4096 | 32872 | 16464 |
| ci-arm64-builder-nohw | 8 | 168 | 112 |
| ci-arm64-builder-nohw | 64 | 616 | 336 |
| ci-arm64-builder-nohw | 4096 | 32872 | 16464 |
| ci-x64-builder-default | 8 | 168 | 112 |
| ci-x64-builder-default | 64 | 616 | 336 |
| ci-x64-builder-default | 4096 | 32872 | 16464 |
| ci-x64-builder-nohw | 8 | 168 | 112 |
| ci-x64-builder-nohw | 64 | 616 | 336 |
| ci-x64-builder-nohw | 4096 | 32872 | 16464 |

## Four historical local regressions mapped to remote coordinates

These are separate observations at matching workload coordinates, not evidence of a cross-host fix or regression cause. The original local ratios/verdicts remain unchanged.

| Historical cohort/layout/cell | Local ratio | Remote shard | Remote ratio | Remote verdict |
|---|---:|---|---:|---|
| final-builder-nohw / Dense / `builder/cluster/4096/build/sorted/same/0` | 1.264773 | ci-x64-builder-nohw | 0.759543 | uncertain-or-small |
| final-builder-nohw / Dense / `builder/cluster/4096/build/sorted/same/0` | 1.264773 | ci-arm64-builder-nohw | 0.999494 | uncertain-or-small |
| final-mixed-nohw / Micro / `mixed/random/4096/restore-remove/equal/same/4096` | 1.119471 | ci-x64-mixed-nohw | 1.000134 | uncertain-or-small |
| final-mixed-nohw / Micro / `mixed/random/4096/restore-remove/equal/same/4096` | 1.119471 | ci-arm64-mixed-nohw | 0.808691 | clear-improvement |
| final-query-default / Dense / `query/scatter/8/query64/balanced/same/0` | 1.040139 | ci-x64-query-default | 0.876602 | clear-improvement |
| final-query-default / Dense / `query/scatter/8/query64/balanced/same/0` | 1.040139 | ci-arm64-query-default | 0.999371 | uncertain-or-small |
| final-query-default / Auto / `query/scatter/8/query64/miss-bit/same/0` | 1.107940 | ci-x64-query-default | 0.998571 | uncertain-or-small |
| final-query-default / Auto / `query/scatter/8/query64/miss-bit/same/0` | 1.107940 | ci-arm64-query-default | 0.999343 | uncertain-or-small |

## Every selected-versus-baseline median regression

- ci-arm64-bridge-default, Auto, `bridge/8/empty-query`: ratio 1.061435, max A/A drift 0.84%, max batch ratio 1.058331
- ci-arm64-bridge-default, Auto, `bridge/4096/empty-query`: ratio 1.059900, max A/A drift 2.40%, max batch ratio 1.077579
- ci-arm64-bridge-default, Dense, `bridge/8/empty-query`: ratio 1.076415, max A/A drift 1.79%, max batch ratio 1.079624
- ci-arm64-bridge-default, Micro, `bridge/8/empty-query`: ratio 1.056707, max A/A drift 0.75%, max batch ratio 1.061715
- ci-arm64-bridge-default, Micro, `bridge/4096/empty-query`: ratio 1.059397, max A/A drift 2.42%, max batch ratio 1.085952
- ci-arm64-bridge-nohw, Auto, `bridge/8/empty-query`: ratio 1.122542, max A/A drift 1.10%, max batch ratio 1.125765
- ci-arm64-bridge-nohw, Auto, `bridge/4096/empty-query`: ratio 1.058618, max A/A drift 0.50%, max batch ratio 1.065555
- ci-arm64-bridge-nohw, Dense, `bridge/8/empty-query`: ratio 1.092827, max A/A drift 1.09%, max batch ratio 1.094875
- ci-arm64-bridge-nohw, Dense, `bridge/4096/empty-query`: ratio 1.065623, max A/A drift 0.93%, max batch ratio 1.112617
- ci-arm64-bridge-nohw, Micro, `bridge/8/empty-query`: ratio 1.122588, max A/A drift 0.76%, max batch ratio 1.121276
- ci-arm64-bridge-nohw, Micro, `bridge/4096/empty-query`: ratio 1.120864, max A/A drift 1.70%, max batch ratio 1.136943
- ci-arm64-builder-nohw, Dense, `builder/scatter/2/build/shuffled/same/0`: ratio 1.044740, max A/A drift 2.62%, max batch ratio 1.223586
- ci-arm64-mixed-default, Micro, `mixed/random/4096/union/equal/same/4096`: ratio 1.115416, max A/A drift 1.31%, max batch ratio 1.146321
- ci-arm64-mixed-default, Micro, `mixed/random/4096/restore-remove/equal/opposite/4096`: ratio 1.118694, max A/A drift 0.92%, max batch ratio 1.127324
- ci-arm64-mixed-default, Micro, `mixed/random/4096/charged-build-union64/shuffled/same/4096`: ratio 1.192936, max A/A drift 1.53%, max batch ratio 1.211731
- ci-arm64-mixed-nohw, Micro, `mixed/random/4096/restore-remove/equal/opposite/4096`: ratio 1.119911, max A/A drift 1.60%, max batch ratio 1.131891
- ci-x64-bridge-default, Dense, `bridge/8/empty-query`: ratio 1.209642, max A/A drift 0.49%, max batch ratio 1.222460
- ci-x64-bridge-default, Dense, `bridge/4096/empty-query`: ratio 1.205330, max A/A drift 0.89%, max batch ratio 1.213905
- ci-x64-bridge-default, Micro, `bridge/8/empty-query`: ratio 1.212101, max A/A drift 0.97%, max batch ratio 1.216330
- ci-x64-bridge-default, Micro, `bridge/4096/empty-query`: ratio 1.215643, max A/A drift 0.81%, max batch ratio 1.225200
- ci-x64-bridge-nohw, Auto, `bridge/8/empty-query`: ratio 1.887465, max A/A drift 15.18%, max batch ratio 1.934405
- ci-x64-bridge-nohw, Auto, `bridge/4096/empty-query`: ratio 1.848052, max A/A drift 16.43%, max batch ratio 1.852518
- ci-x64-bridge-nohw, Dense, `bridge/8/empty-query`: ratio 1.245086, max A/A drift 3.28%, max batch ratio 1.312687
- ci-x64-bridge-nohw, Dense, `bridge/4096/empty-query`: ratio 1.219172, max A/A drift 7.43%, max batch ratio 1.387774
- ci-x64-bridge-nohw, Micro, `bridge/4096/exact-hit`: ratio 1.051495, max A/A drift 1.01%, max batch ratio 1.069961
- ci-x64-bridge-nohw, Micro, `bridge/4096/nested-pass`: ratio 1.062205, max A/A drift 1.49%, max batch ratio 1.064688
- ci-x64-bridge-nohw, Micro, `bridge/4096/nested-fail`: ratio 1.063285, max A/A drift 2.80%, max batch ratio 1.211953
- ci-x64-builder-default, Micro, `builder/scatter/0/build/shuffled/same/0`: ratio 1.055702, max A/A drift 0.43%, max batch ratio 1.039515
- ci-x64-builder-default, Auto, `builder/scatter/0/build/shuffled/same/0`: ratio 1.056677, max A/A drift 0.62%, max batch ratio 1.069125
- ci-x64-builder-nohw, Micro, `builder/scatter/1/build/sorted/same/0`: ratio 1.043708, max A/A drift 0.43%, max batch ratio 1.056699
- ci-x64-builder-nohw, Micro, `builder/cluster/8/build/sorted/same/0`: ratio 1.059717, max A/A drift 0.86%, max batch ratio 1.050157
- ci-x64-builder-nohw, Micro, `builder/cluster/8/build/shuffled/same/0`: ratio 1.073359, max A/A drift 0.51%, max batch ratio 1.112367
- ci-x64-builder-nohw, Micro, `builder/cluster/8/build/duplicates/same/0`: ratio 1.052403, max A/A drift 0.46%, max batch ratio 1.058649
- ci-x64-builder-nohw, Micro, `builder/scatter/8/build/shuffled/same/0`: ratio 1.037739, max A/A drift 0.43%, max batch ratio 1.057546
- ci-x64-builder-nohw, Micro, `builder/scatter/8/build/duplicates/same/0`: ratio 1.047259, max A/A drift 1.07%, max batch ratio 1.354063
- ci-x64-builder-nohw, Auto, `builder/cluster/8/build/shuffled/same/0`: ratio 1.071850, max A/A drift 1.21%, max batch ratio 1.156010
- ci-x64-mixed-default, Micro, `mixed/scatter/8/restore-remove/equal/same/8`: ratio 1.200100, max A/A drift 4.30%, max batch ratio 1.252585
- ci-x64-mixed-default, Micro, `mixed/scatter/4096/union/equal/same/4096`: ratio 1.391802, max A/A drift 2.71%, max batch ratio 1.527200
- ci-x64-mixed-default, Micro, `mixed/scatter/4096/restore-remove/equal/same/4096`: ratio 1.485927, max A/A drift 6.07%, max batch ratio 1.512274
- ci-x64-mixed-default, Micro, `mixed/scatter/8/restore-remove/small-large/same/4096`: ratio 1.267814, max A/A drift 7.13%, max batch ratio 1.377124
- ci-x64-mixed-default, Micro, `mixed/random/4096/union/equal/same/4096`: ratio 1.058431, max A/A drift 1.03%, max batch ratio 1.071014
- ci-x64-mixed-default, Micro, `mixed/random/4096/restore-remove/equal/same/4096`: ratio 1.187152, max A/A drift 2.40%, max batch ratio 1.566510
- ci-x64-mixed-default, Dense, `mixed/cluster/8/union/equal/opposite/8`: ratio 1.092033, max A/A drift 1.25%, max batch ratio 1.146246
- ci-x64-mixed-default, Dense, `mixed/cluster/4096/copy/equal/opposite/4096`: ratio 1.088148, max A/A drift 2.47%, max batch ratio 1.088457
- ci-x64-mixed-default, Dense, `mixed/scatter/4096/copy/equal/opposite/4096`: ratio 1.151882, max A/A drift 7.52%, max batch ratio 1.184181
- ci-x64-mixed-default, Auto, `mixed/scatter/8/restore-remove/small-large/same/4096`: ratio 1.094373, max A/A drift 3.35%, max batch ratio 1.160412
- ci-x64-mixed-nohw, Micro, `mixed/cluster/8/charged-build-union64/shuffled/same/8`: ratio 1.098032, max A/A drift 0.62%, max batch ratio 0.950102
- ci-x64-mixed-nohw, Auto, `mixed/cluster/8/charged-build-union64/shuffled/same/8`: ratio 1.110112, max A/A drift 1.12%, max batch ratio 0.955220
- ci-x64-query-default, Micro, `query/scatter/32/query64/hit-middle/same/0`: ratio 1.048464, max A/A drift 0.35%, max batch ratio 1.061950
- ci-x64-query-default, Micro, `query/scatter/32/query64/hit-last/same/0`: ratio 1.066739, max A/A drift 1.37%, max batch ratio 1.066462
- ci-x64-query-default, Micro, `query/scatter/32/query64/miss-bit/same/0`: ratio 1.089887, max A/A drift 0.21%, max batch ratio 1.095590
- ci-x64-query-default, Micro, `query/scatter/32/query64/miss-low/same/0`: ratio 1.091635, max A/A drift 0.49%, max batch ratio 1.240932
- ci-x64-query-default, Micro, `query/scatter/32/query64/miss-high/same/0`: ratio 1.090568, max A/A drift 0.66%, max batch ratio 1.102342
- ci-x64-query-default, Micro, `query/scatter/32/query64/balanced/same/0`: ratio 1.045739, max A/A drift 0.58%, max batch ratio 1.066330
- ci-x64-query-default, Auto, `query/scatter/0/query64/miss-low/same/0`: ratio 1.092766, max A/A drift 0.79%, max batch ratio 1.108581
- ci-x64-query-default, Auto, `query/scatter/0/query64/miss-high/same/0`: ratio 1.095697, max A/A drift 0.44%, max batch ratio 1.117970
- ci-x64-query-default, Auto, `query/scatter/1/query64/hit-first/same/0`: ratio 1.175528, max A/A drift 0.72%, max batch ratio 1.184333
- ci-x64-query-default, Auto, `query/scatter/1/query64/hit-middle/same/0`: ratio 1.174474, max A/A drift 0.60%, max batch ratio 1.179123
- ci-x64-query-default, Auto, `query/scatter/1/query64/hit-last/same/0`: ratio 1.172265, max A/A drift 0.33%, max batch ratio 1.195218
- ci-x64-query-default, Auto, `query/scatter/1/query64/miss-bit/same/0`: ratio 1.173817, max A/A drift 0.40%, max batch ratio 1.191008
- ci-x64-query-default, Auto, `query/scatter/1/query64/miss-low/same/0`: ratio 1.092453, max A/A drift 0.24%, max batch ratio 1.112796
- ci-x64-query-default, Auto, `query/scatter/1/query64/miss-high/same/0`: ratio 1.102340, max A/A drift 0.18%, max batch ratio 1.107084
- ci-x64-query-default, Auto, `query/scatter/1/query64/balanced/same/0`: ratio 1.092054, max A/A drift 0.82%, max batch ratio 1.131204
- ci-x64-query-default, Auto, `query/scatter/2/query64/hit-first/same/0`: ratio 1.083499, max A/A drift 0.29%, max batch ratio 1.101826
- ci-x64-query-default, Auto, `query/scatter/2/query64/miss-low/same/0`: ratio 1.091143, max A/A drift 0.72%, max batch ratio 1.101818
- ci-x64-query-default, Auto, `query/scatter/2/query64/balanced/same/0`: ratio 1.041890, max A/A drift 0.15%, max batch ratio 1.049402
- ci-x64-query-default, Auto, `query/scatter/64/query64/balanced/same/0`: ratio 1.204972, max A/A drift 1.72%, max batch ratio 1.243683

## Separate bridge split versus old Direct comparison

| Shard | Improvements | Regressions | Uncertain | Invalid |
|---|---:|---:|---:|---:|
| ci-arm64-bridge-default | 17 | 0 | 37 | 0 |
| ci-arm64-bridge-nohw | 18 | 0 | 36 | 0 |
| ci-x64-bridge-default | 15 | 2 | 37 | 0 |
| ci-x64-bridge-nohw | 16 | 0 | 38 | 0 |

Every clear regression in that separate comparison:

- ci-x64-bridge-default, Dense, `bridge/8/empty-query`: split/old-Direct median 1.214164, max A/A drift 0.49%, max batch ratio 1.216787
- ci-x64-bridge-default, Dense, `bridge/4096/empty-query`: split/old-Direct median 1.215973, max A/A drift 0.89%, max batch ratio 1.210818

## Invalid and incomplete observations

- ci-arm64-builder-default, Dense, `builder/scatter/0/build/sorted/same/0`: minimum 0.560461 ms; unranked
- ci-arm64-builder-default, Dense, `builder/scatter/0/build/shuffled/same/0`: minimum 0.560845 ms; unranked
- ci-arm64-builder-nohw, Dense, `builder/scatter/0/build/sorted/same/0`: minimum 0.593590 ms; unranked
- ci-arm64-builder-nohw, Dense, `builder/scatter/0/build/shuffled/same/0`: minimum 0.575407 ms; unranked
- ci-arm64-builder-nohw, Dense, `builder/scatter/1/build/sorted/same/0`: minimum 0.206420 ms; unranked
- ci-arm64-builder-nohw, Dense, `builder/scatter/2/build/sorted/same/0`: minimum 0.617444 ms; unranked
- ci-x64-builder-default, Dense, `builder/scatter/0/build/sorted/same/0`: minimum 0.776640 ms; unranked
- ci-x64-builder-default, Dense, `builder/scatter/0/build/shuffled/same/0`: minimum 0.763849 ms; unranked
- ci-x64-builder-default, Dense, `builder/scatter/1/build/sorted/same/0`: minimum 0.725915 ms; unranked
- ci-x64-builder-default, Dense, `builder/scatter/2/build/sorted/same/0`: minimum 0.805814 ms; unranked
- ci-x64-builder-nohw, Dense, `builder/scatter/0/build/sorted/same/0`: minimum 0.827686 ms; unranked
- ci-x64-builder-nohw, Dense, `builder/scatter/0/build/shuffled/same/0`: minimum 0.765183 ms; unranked
- ci-x64-builder-nohw, Dense, `builder/scatter/1/build/sorted/same/0`: minimum 0.767066 ms; unranked
- ci-x64-builder-nohw, Dense, `builder/scatter/2/build/sorted/same/0`: minimum 0.802401 ms; unranked

## Interpretation limits

Broad and legacy-relative bridge median classifications require all six baseline crossings to exceed max(3%, observed A/A drift). The separate split/old-Direct bridge classification uses its three within-round median ratios against that same drift threshold. This is descriptive throughput evidence, not a significance claim. P95/max are batch tails, not individual-operation tail latency. Noisy or invalid cells remain visible. All prepared-allocation totals and any allocating-workload changes are retained in results-summary.json.

Managed Linux x64/ARM64 observations do not establish Unity player, Mono, IL2CPP, Burst or device performance. Hardware-disabled .NET 8 runs are not netstandard timing measurements.

Received shards: 16/16; completed statistical shards: 16/16; independent identity audits matched: 16/16. Pending coordinates: none.

This additive publication retains the complete timing result folders and limited receipts as original bytes, all derived JSON/CSV rows, and nonbinary correctness records from all six separate correctness jobs (136 process summaries/logs plus source/DLL bindings and SDK/feature metadata). It omits the full GitHub artifact binaries, source copies and build/dependency record tree. Original full GitHub ZIPs have the expiration dates recorded in the JSON companion; preserve those ZIPs for any later binary/provenance reanalysis. The historical local x64 data and its four losses remain unchanged.
