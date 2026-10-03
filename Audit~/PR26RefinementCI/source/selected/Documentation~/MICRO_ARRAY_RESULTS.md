# Micro array builders — executed results, 2026-10-01

## Decision

Implementation, full managed regression tests, three-round benchmarks on x64 AND ARM64 Linux, and independent artifact/raw-sample review have completed successfully. This does not meet the original all-workload superiority or native Unity release gate.

- Retain **DirectArraySet** as the simpler bounded candidate for further integration: resolve inline/array storage outside micro batch loops, use local buffers, and block-copy unprocessed tails. It shows useful small-set batch gains without new member fields or allocations.
- **Do not adopt DirectGrowthSet's extra merge-on-growth branch.** It does remove one copy on that path, but the measured full copy+append sequence does not earn the extra branch across platforms. Relative to DirectArraySet, only 3/66 x64 small fresh copy+append cases pass the every-round 5% improvement screen, with 3/66 regressions under the same screen. On ARM64 there are 0/66 such improvements and 17/66 regressions. This is a concrete implementation decision, not a proof about all growth algorithms.
- Preserve both measured candidates and all raw samples as experiments. Production Runtime/Editor/Samples/Tests/package are unchanged, no branch is merged, and no release is approved.

## Exact source and reproducibility

Base: reviewed Fusion `d45f21ffc5858a7ce9be68af9322924a774e40b4`.
Measured commit: `4556d5dcde04afe32f07b7755b244762682ae93a`.
Measured Git tree: `c33d844538e6bde2e5f615782943567e71649690`.
Independent review-only commit: `92fa6477183c222b387252af77bacdb1656401bf`.
Later documentation does not change timed source.

[Completed dual-architecture performance CI](https://github.com/acced/ZFramework.GameplayTags/actions/runs/36842179080)

[Completed cross-architecture artifact review](https://github.com/acced/ZFramework.GameplayTags/actions/runs/36842959497)

[Download exact source, generated candidates, both raw archives, full CSV and Chinese report](https://github.com/acced/ZFramework.GameplayTags/actions/runs/36842959497/artifacts/11152241298)

Artifact SHA256: `c2c0e0b8534d27cc5b7106b3adc45247aaca464bffd4ed24f3f4f119149d1f73`.
Nested combined source/evidence ZIP SHA256: `9d69f11db880da4c683595f15bbdebdf174280a72a7a7858be1c8c8cabc1041b`.

Raw x64 artifact: `11152690808`, SHA256 `e54ff72622e956da45457f70a9c705ef2c5b23d00ed34b63e8ce884e83786b11`.
Raw ARM64 artifact: `11152546608`, SHA256 `9b16e67b6b8b44cb6255a04e88c21da165166fe13274ce4276d8a0dc922ec2a0`.

The combined deliverable contains `deliverables/GameplayTags_MicroArray_TestedSource_4556d5d.zip`, generated `CandidateSources`, `All_Architectures.csv`, `CrossArchitecture_Review.json` and `GameplayTags_MicroArray_验证报告.md`. The source archive is an experiment checkout, NOT an already migrated replacement package.

## What changed, and what did not

The control reconstructs the retained ordered SIMD query and unchanged DenseFusion backend. DirectArraySet changes CountRecords, the array-only union merge, reverse array append, and forward array removal. It resolves the array-vs-inline choice once per batch instead of through every Read/Write. Source aliases are handled before the independent merge helper; the helper trusts established capacity. DirectGrowthSet adds only a separate append-growth merge into newly allocated storage, with the same capacity formula.

The cross-review confirms identical generated sources between CPUs, no additional object fields, unchanged exact-query and Dense bodies, and equal allocated bytes to the immediate kernel control on EVERY candidate benchmark row. Metadata validation remains at public boundaries. Count is eager; outputs are independent; supported aliases and actual-result-capacity Into behavior remain tested. No raw member-ID buffer, pool, COW, global scratch, implicit encoding switch, or delayed counting was added.

Small membership remains compressed address-plus-mask records, not a fully materialized full-universe Dense array. The existing narrow-format limit of 2^20 registered IDs remains explicit.

## Executed tests

On EACH CPU, for control/arrays/growth separately:

- Original full Micro suite: 5 groups, **5,392,728 assertions, 0 failures**.
- The same suite with hardware intrinsics disabled: passes again; a repeated configuration, not extra unique cases.
- Added builder suite: **211,119 assertions, 0 failures** per class. This checks identical/disjoint/partial-overlap inputs, different member bits in one record, retained-array one-record transitions, both aliases, independently mutable output, unchanged growth formula, actual-output-capacity handling, 5,000 random batch churn steps, and preallocated arbitrary placement.
- Prepared builder loop allocation: **0 B**, positive control **152 B**.
- A separately compiled .NET Standard 2.1 scalar library executes the full public Micro suite through a temporary test-only friend assembly: **5,392,728 assertions, 0 failures**.

These counts include loops and repeated configurations; they are not millions of independent test scenarios. Tests and compilation ran in GitHub Actions, not a local Unity Editor.

Independent recomputation on EACH architecture verified three reference Git trees, the measured source tree, 64 unchanged production files, **5,256 aggregate rows**, **15,768 raw container rows**, **110,376 retained timing samples**, and **55,188 prepared allocation samples at 0 B**. A separate review job downloaded the exact ZIPs, checked their SHA256 values, reran the raw checker, and checked cross-CPU source equality. No aggregation or ranking discrepancy was found.

## Measurement contract

Pinned .NET SDK 8.0.425; Linux x64 and ARM64; Release; tiered compilation disabled; one allowed CPU per process. Three rotated/reversed fresh-process rounds, seven retained samples per case. Tables report median of process medians. Shared hosted machines are not isolated mobile devices; every-round 5% screening is not a statistical confidence interval.

Small sizes: 0,1,7,8,9,31,32,33,127,128,129. Large sizes: 128,1024,4096. Explicit definitions: 10,000/65,536/262,144, plus implicit ancestors. Continuous and fixed-seed scattered inputs. Timed batch relations retain the historical half-overlap and existing half-subset cases. More relation/churn cases are correctness-tested but their complete timing matrix remains unmeasured.

New union allocates an independent result. Copy+append/remove perform a real copy then mutation. Allocation, initialization, writes and eager count remain inside timing. Prepared output is separate and must report zero bytes in every sample. Negative and mixed128 query batches are supplementary; mixed128 is NOT one query.

Small controls are adaptive Auto, original adaptive Micro and the immediate SIMD/Fusion kernel control. Large comparisons additionally include forced Dense Auto; Micro/kernel/arrays/growth run forced Dense. All original controls remain. This is not an all-history rerun of main/Alex/indexed/packed. Query and Dense method bodies were unchanged, so their timing variation is NOT a newly implemented optimization.

## Large registry / eight scattered members

262,144 explicit definitions. ns/op. Keep CPU results separate. The immediate kernel already includes retained SIMD and Dense changes; these are not credited to the new array work.

### x64 — new independent results

| Operation | Auto | Original Micro | Kernel control | DirectArray | DirectGrowth |
|---|---:|---:|---:|---:|---:|
| Exact | 9.254 | 7.459 | 4.656 | 4.685 | 4.657 |
| Union | 75.814 | 77.651 | 78.572 | 70.168 | 69.567 |
| Copy+append | 99.792 | 107.795 | 118.158 | 87.626 | 90.189 |
| Copy+remove | 66.318 | 65.358 | 66.253 | 52.285 | 48.249 |

DirectArray has lower medians than Auto on all four rows in THIS x64 fresh shape. This is not a claim that every row clears the 5% per-round screen or that it wins prepared/ARM64 cases.

### ARM64 — new independent results

| Operation | Auto | Original Micro | Kernel control | DirectArray | DirectGrowth |
|---|---:|---:|---:|---:|---:|
| Exact | 6.415 | 6.971 | 5.519 | 5.528 | 5.531 |
| Union | 53.976 | 67.231 | 67.400 | 59.955 | 59.937 |
| Copy+append | 84.360 | 91.431 | 90.015 | 76.388 | 77.402 |
| Copy+remove | 47.497 | 55.392 | 55.547 | 43.677 | 41.645 |

ARM64 allocating union still loses to Auto. Both new candidates retain 144/200/112 B for union/copy+append/copy+remove; Auto is 136/192/104 B. Exact is 0 B. There is no allocation reduction versus Micro/kernel in this pass.

### Prepared output — do not substitute for fresh results

All rows are 0 B.

| CPU / operation | Auto | Kernel control | DirectArray | DirectGrowth |
|---|---:|---:|---:|---:|
| x64 union_into | 29.589 | 49.574 | 41.946 | 42.724 |
| x64 copy+append reuse | 66.957 | 75.389 | 57.005 | 58.656 |
| x64 copy+remove reuse | 32.278 | 53.043 | 39.765 | 40.545 |
| ARM64 union_into | 28.278 | 39.227 | 35.469 | 35.435 |
| ARM64 copy+append reuse | 51.997 | 65.987 | 51.021 | 52.823 |
| ARM64 copy+remove reuse | 26.009 | 47.018 | 33.435 | 33.290 |

Prepared union and removal remain slower than Auto in this target shape. The all-workload requirement is still not met.

## Complete small-batch screening

Each row contains 198 cases (three batch operations, eleven sizes, three registry sizes, two distributions). Compare to all three same-run controls. Exact query is excluded because this pass did not change it.

| CPU / candidate / contract | Lower medians | >=5% in every round |
|---|---:|---:|
| x64 DirectArray fresh | 91/198 | 51/198 |
| x64 DirectArray prepared | 81/198 | 30/198 |
| x64 DirectGrowth fresh | 104/198 | 53/198 |
| x64 DirectGrowth prepared | 96/198 | 34/198 |
| ARM64 DirectArray fresh | 88/198 | 55/198 |
| ARM64 DirectArray prepared | 75/198 | 34/198 |
| ARM64 DirectGrowth fresh | 77/198 | 42/198 |
| ARM64 DirectGrowth prepared | 65/198 | 33/198 |

The complete large-path matrix remains in CSV and is not used to advertise new Dense improvements. No per-row mixing of the two candidates is allowed. DirectGrowth's changes to unrelated deletion/union timing cannot be credited to its growth branch.

## Remaining gates

- The requested all-scenario and all-reference performance target remains unmet.
- Retain the direct-array work as a candidate, reject the additional direct-growth branch for promotion, and leave production untouched.
- Narrow-format wider registry behavior, complete relation/conversion/churn timing, retained/peak memory and real Unity Editor/Mono/IL2CPP/Burst tests remain outstanding.
- ARM64 Linux .NET execution is real ARM64 evidence, not Android/iOS Unity acceptance.

The earlier status text in MICRO_ARRAY_BUILDERS.md describes initial submission; this document records the completed run and decision. All less favorable rows and experiments remain available.
