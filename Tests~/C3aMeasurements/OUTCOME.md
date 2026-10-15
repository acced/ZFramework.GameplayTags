# C3a experiment: not accepted for production

C3a is retained as an experimental source snapshot and complete measurement record. Production Runtime stays at current792 (`792ce83f1c2a9aeaadc045c192d70081d866d903`). The narrower compare-then-tail union loop did not establish an overall public-workflow gain and repeatedly lost targeted union comparisons. Passing semantics alone did not make it an accepted optimization.

## Direct comparison and disposition

This is a direct current792→C3a comparison, not a reconstruction from old B1 ratios. Exactly248 formal samples (31 cells×4 paired rounds×2 versions),67 new current792-only calibration pilots, and62 untimed benchmark-fixture invocations completed. Every numeric row validated; every child exited0. Four pairs ran AB, BA, BA, AB. All31 final baseline count checks reached250ms and no count hit its cap. No target or timer was rerun during packaging.

Important observed median paired C3a/current792 ratios:

- Reused nonalias SetUnion n1024/full:1.0731, all4 pairs slower, paired range1.0124–1.1303. Per-version median66.785→72.099µs.
- Reused nonalias SetUnion n1/full:1.0264, all4 slower. New Union n8/half:1.0275, all4 slower.
- New Union n1024/half:1.0164; full:1.0255. Neither supports a general targeted union improvement.
- Full lifecycle1024:0.9688,3/4 faster, broad paired range0.8194–1.0393; per-version median302.647→293.153µs. Lifecycle1:0.9302,3/4 faster. These noisy mixed results do not override the targeted losses.
- Exact lifecycle-shaped retained CopyFrom1024←512:1.0245,3/4 slower. Its restored workflow includes CopyFrom(seed), with both source/target capacity1024; it is not a pure copy kernel.
- Source-unchanged C2 right-alias guards are adverse: new1.0645 (3/4 slower), retained1.0343 (all4 slower). This is not a C2 code change or evidence that C3a fixed C2. Copy/alias drift also constrains attribution of small effects to the changed union kernel.

Ratios below1 favor C3a. The median of paired ratios is deliberately reported separately from each version's absolute median; those two calculations need not agree on very small movements. All raw observations remain in [raw-flat.csv](Evidence/raw-flat.csv), all pairs in [paired.csv](Evidence/paired.csv), and absolute ranges in [absolute-summary.csv](Evidence/absolute-summary.csv).

## All31 formal cells

The table gives each version's median ns/op and the median/min/max of its four paired ratios. Faster counts are C3a-faster pairs out of4; no row is trimmed. Exact keys, calls, durations, bytes, GC and capacities are in the CSVs.

| Workflow | current792 median ns | C3a median ns | Paired median | Paired min–max | Faster/4 | Short samples |
|---|---:|---:|---:|---:|---:|---:|
| Union n0 both-empty new | 31.200 | 30.634 | 0.9837 | 0.9642–1.0483 | 3 | 0 |
| SetUnion n0 both-empty retained | 20.011 | 17.944 | 0.8987 | 0.8849–0.9324 | 4 | 0 |
| Union n1 lhs-before-rhs new | 77.327 | 77.950 | 1.0176 | 0.9767–1.1157 | 2 | 0 |
| SetUnion n1 lhs-before-rhs retained | 96.411 | 93.915 | 0.9786 | 0.8642–1.0030 | 3 | 0 |
| Union n1 full-overlap new | 74.875 | 77.095 | 1.0138 | 0.9898–1.0582 | 1 | 0 |
| SetUnion n1 full-overlap retained | 95.200 | 98.300 | 1.0264 | 1.0125–1.0950 | 0 | 0 |
| Union n8 half-overlap new | 398.762 | 411.434 | 1.0275 | 1.0153–1.0443 | 0 | 0 |
| SetUnion n8 half-overlap retained | 554.510 | 543.461 | 0.9857 | 0.9260–1.0682 | 2 | 2 |
| Union n8 full-overlap new | 429.871 | 435.674 | 1.0087 | 0.9712–1.0560 | 2 | 0 |
| SetUnion n8 full-overlap retained | 542.273 | 519.953 | 0.9758 | 0.9351–1.0106 | 3 | 0 |
| Union n8 lhs-before-rhs new | 410.202 | 442.750 | 1.0715 | 0.9893–1.2054 | 1 | 0 |
| SetUnion n8 lhs-before-rhs retained | 530.501 | 538.263 | 1.0074 | 0.9757–1.1110 | 2 | 0 |
| Union n8 rhs-before-lhs new | 414.184 | 399.967 | 0.9776 | 0.9245–0.9908 | 4 | 0 |
| SetUnion n8 rhs-before-lhs retained | 535.130 | 550.950 | 1.0398 | 0.9921–1.1113 | 1 | 0 |
| Union n1024 half-overlap new | 50594.498 | 51919.469 | 1.0164 | 0.9105–1.0993 | 1 | 1 |
| SetUnion n1024 half-overlap retained | 66457.105 | 68240.933 | 0.9959 | 0.9656–1.1155 | 2 | 0 |
| Union n1024 full-overlap new | 46217.724 | 47654.218 | 1.0255 | 0.9541–1.0625 | 1 | 0 |
| SetUnion n1024 full-overlap retained | 66784.853 | 72099.266 | 1.0731 | 1.0124–1.1303 | 0 | 0 |
| Union n1024 lhs-before-rhs new | 55393.731 | 57276.386 | 0.9778 | 0.9296–1.1739 | 2 | 0 |
| SetUnion n1024 lhs-before-rhs retained | 72059.985 | 72567.187 | 0.9976 | 0.9356–1.1145 | 2 | 0 |
| Union n1024 rhs-before-lhs new | 55786.199 | 53991.399 | 0.9524 | 0.9259–1.0320 | 3 | 0 |
| SetUnion n1024 rhs-before-lhs retained | 73994.348 | 74012.233 | 0.9844 | 0.9601–1.0616 | 3 | 0 |
| Lifecycle n1 full-overlap new | 254.242 | 236.516 | 0.9302 | 0.8936–1.0170 | 3 | 0 |
| Lifecycle n1024 half-overlap new | 302646.959 | 293152.891 | 0.9688 | 0.8194–1.0393 | 3 | 0 |
| CopyFrom 1024←512 lifecycle-intersection-refs retained | 29957.261 | 30772.107 | 1.0245 | 0.9039–1.0617 | 1 | 0 |
| CopyFrom 0←0 same-names retained | 13.973 | 14.162 | 1.0165 | 0.9766–1.1026 | 2 | 0 |
| CopyFrom 1←1 same-names retained | 66.120 | 68.803 | 1.0492 | 0.9973–1.0736 | 1 | 0 |
| AppendTags 1024←1023 last new | 63047.428 | 60875.451 | 0.9798 | 0.9459–1.0342 | 3 | 0 |
| AppendTags 1024←1023 last retained | 47828.808 | 47901.129 | 0.9997 | 0.9787–1.0553 | 2 | 0 |
| Right-alias SetUnion 1024←1023 last new | 82123.837 | 85696.278 | 1.0645 | 0.9966–1.1031 | 1 | 0 |
| Right-alias SetUnion 1024←1023 last retained | 68809.045 | 72381.506 | 1.0343 | 1.0067–1.0715 | 0 | 0 |

## Actual durations and short samples

Three formal C3a samples were below250ms and remain short:
- Retained SetUnion n8/half, pair0:243.990506ms; pair3:245.309523ms.
- New Union n1024/half, pair1:247.557191ms.

The other245 formal samples reached250ms. There were36 short early calibration pilots; all31 final pilot durations were adequate. Neither faster contenders nor short samples were discarded or recounted. All actual Stopwatch ticks, frequencies, durations, counts and short flags are retained. Calibration took24.005s and formal comparison124.601s of runner wall time; these are stage durations, not operation timings or a causal performance explanation.

## Allocation, GC and actual capacity

Every measured allocation and retained capacity matched across versions for every formal cell. There were no fractional allocation-per-operation rows in any of315 numeric records. All retained SetUnion/copy/C2 workflows measured0 B/op and no collections inside counters. This is bounded measured behavior, not a universal zero-allocation guarantee.

- New Union:56 B/op at n0,96 at n1,208 at n8,16,464 at n1024. Both empty results Count0/capacity0; n1 full Count1/capacity2 and disjoint Count2/capacity2; n8 results capacity16; n1024 results capacity2048. Full/half/disjoint result counts are respectively n,1.5n,2n for n8/n1024. Retained union results have the same actual count/capacity, while their already-allocated storage is outside counters.
- Lifecycle1:296 B/op; final cleared target Count0/capacity4, union/intersection Count1/capacity2 and Count1/capacity1.
- Lifecycle1024:41,360 B/op; final cleared target Count0/capacity1024, union Count1536/capacity2048, intersection Count512/capacity1024.
- Lifecycle-shaped copy:Count512/capacity1024; source Count512/capacity1024. Empty/singleton copy sentinels retain Count/capacity0/0 and1/1.
- New late-C2 append/right-alias workflows:16,496 B/op, final Count1025/capacity1025. Retained variants:0 B/op, Count1025/capacity2048. C2 avoids reserving the2047-input upper bound for a new result; it does not shrink an already2048-capacity array.

Allocating formal rows recorded GC0/GC1 collections; GC2 was0 in every formal row. Full individual GC deltas and capacity/payload census are in raw-flat.csv, with distinct per-cell/version values in [storage-gc-summary.csv](Evidence/storage-gc-summary.csv). Collection counts neither measure pause durations nor establish why a timing changed. No GC-causal claim, subtraction of reset/observer medians, or whole-heap claim is made. Array payload counts only backing reference slots.

## Exact path attribution and verification

The lifecycle1024 inputs each contain1024 values, with512 overlap. Union creates a distinct result and enters the changed nonalias UnionSorted path; it never executes C2's AppendAlreadyValidated alias path. Intersection produces512 lhs representatives, reserved capacity1024. The later copy is target1024←source512 before Clear. Actual new Union, reused nonalias SetUnion, lifecycle, copy, and C2 alias/new/retained rows are distinct public workflows, not interchangeable kernel timings.

Both exact source snapshots passed existing Roadmap(1,002,001 assertions), C12 supplement(197,333 assertions), differential transcript equality, and netstandard2.1/C#9 compatibility compilation. [Sanitized semantic transcripts](Evidence/Semantics) preserve those original results. The portable correctness.sh added during packaging reuses the same pinned input suites; it was syntax/hash checked, not locally rerun. CI can run it separately without benchmarks.

The timed Program SHA256 is `daac57a934fdb4338103c93fcc1d64316816d1ae7eac4850416d40fc152f9962`; cells SHA256 is `3375e9515c3a6aaf698c057624b3a47e690ccdee89a6fdecc7184e22baec76bb`. Sources, binary hashes, all315 numeric rows,62 fixture logs,377 terminal receipts and common-count hash are retained. Public receipts omit machine-local paths, launch commands, inherited environment values and build logs; numerical CSVs and indexed raw/terminal byte ranges are byte-identical to collected records. The six deterministic raw shards and terminal shard retain every original byte, mapped by offset/length/hash in Evidence/call-index.jsonl; read_evidence.py verifies and reads them without invoking targets. Original timed harness files and preregistered protocol remain under Evidence/TimedHarness; packaging identity records portability-only edits.

## Limits and historical context

Four pairs cannot establish precise universal speedups. Four unanimous signs have two-sided sign-test p=0.125 before multiplicity. No current physical-binary A/A repeat was performed. Existing [C12 A/A evidence](../C12Measurements/README.md) remains historical context; it was neither multiplied into these ratios nor treated as same-day calibration or proof of present build stability. No favorable resampling, post-hoc threshold, normalization or control subtraction occurred.

These measurements use .NET8.0.31 Linux x64 with SDK8.0.425 and tiering disabled. They do not certify Unity Mono/IL2CPP, devices, or causality. Separate [diagnostic evidence](../C3aDiagnostics/README.md) is descriptive and does not enter formal ratios. C3a remains an unaccepted experiment; no production-performance claim follows from this publication.
