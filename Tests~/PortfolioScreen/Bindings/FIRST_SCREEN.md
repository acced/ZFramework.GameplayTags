# Binding delegate reuse: completed first screen

Status: promising allocation/work reduction, unresolved timing costs, **0/10 qualified versions**.

[Run 38211823639](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38211823639) completed both backends at commit `4cc968fe868d18f8f17540182b69d22d302950ed`. The tested source is original `f7c99546dbf029d70896c0f05e5119b19ffd6d26` plus this package's Binds-only patch, not the branch-root Runtime composition.

## Preserved evidence

- .NET artifact `11728769255`, 31,849 bytes, SHA-256 `73e348c909cfb1f4f7ebfd9b95f24f99b36c8d48981addbd76d637b46ab2ca9f`.
- Mono artifact `11729314786`, 25,394 bytes, SHA-256 `b3ff26a61909bfb30ccc8ae106512b90dfe5e195d78a9d1290a23a1843424ec0`.
- SDK 8.0.425; actual CLR 8.0.31 and standalone Mono 6.8.0.105. Requested ReadyToRun is enabled, tiering/server GC disabled; Mono applicability is recorded separately.
- Each backend retains all 18 first-use calls and eight warm processes covering nine rows in four alternating pairs. Independent review matched all 36 warm pair identities, iteration counts, checksums, events and final states per backend.
- Core and focused correctness, actual delegate-constructor count, and runtime field/type-layout checks passed. The compiled Bind body changes from 182 to 171 IL bytes and from two delegate constructions to one.

## Benefits and unresolved costs

All seven modified-operation warm rows improve in all four pairs on each backend. Every Bind saves exactly 64 bytes on .NET and 128 bytes on Mono. These are Bind/Unbind cycles, including their stated allocation and multicast costs, not isolated Bind timing. Fast candidate batches remain below the original 200 ms target; no acceptance interval is inferred from four pairs.

The unchanged .NET direct-dispatch control is slower in all four pairs. Candidate/original ratios are 1.239577, 1.061280, 1.001899 and 1.043035. Direct-subscription ratios are 1.128403, 0.970273, 1.012474 and 1.042205. Both controls report zero managed bytes and zero measured GC. They follow seven allocating rows in the original process; differing earlier allocation history is a possible confound, not an established explanation or waiver.

.NET first-empty Bind is slower in all three fresh-process pairs:

| Original ns | Candidate ns | Candidate/original |
|---:|---:|---:|
| 1,663,867 | 1,750,394 | 1.052004 |
| 1,495,610 | 1,525,769 | 1.020165 |
| 1,502,484 | 1,557,828 | 1.036835 |

The other two .NET cold fixtures are mixed. All three save exactly 64 bytes per pair. Mono first-empty improves in three pairs; its other cold fixtures and unchanged controls are mixed or near-neutral, with exactly 128 bytes saved per cold Bind. Allocation savings do not establish first-call latency non-regression. The first-use boundary includes the original NoInlining bridge's first invocation and is not numerically interchangeable with earlier local direct-call measurements.

## Decision

Do not promote directly to full qualification. Prepare one predeclared bounded clarification using the same physical baseline binary for A/A and original/candidate binaries for A/B. Isolate both unchanged controls in fresh processes without preceding allocating rows, retain the existing fixed counts, and include all three original cold scenarios. Preserve every original adverse sample. An inconclusive clarification does not establish universal non-regression or qualify a version; no repeated sampling until favorable is authorized by this decision.

The runtime patch remains unchanged. Real Unity Editor/Player and IL2CPP have not been measured.

## Completed bounded clarification: hold

[Run 38213539126](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38213539126), exact commit `4eb0cda81a0915562f22c49977746ae5073fe746`, completed all 80 declared fresh processes on each backend. Independent review verified the unchanged runtime patch, same-physical-before-binary A/A identity, paired semantics and zero measured GC.

- .NET artifact `11730173567`, 29,260 bytes, SHA-256 `2c0beabcda7520b8e9dbb2d75e64db1945d8fad492da71c17a010558cbcf2a93`.
- Mono artifact `11729644701`, 22,760 bytes, SHA-256 `7e6f30ca7d06d9142d3fdece768f3616e9a8b0affa157d2e1ee536b9e2abe351`.

The isolated .NET direct-dispatch A/B ratios are 1.003425, 1.024261, 0.999203 and 1.001799; A/A ratios are 0.998668, 0.952215, 0.927328 and 1.009694. The earlier large shift does not repeat in this different process context, but no cause for the original result is established. Subscription is mixed. Mono controls are mixed and remain short under the fixed original counts.

.NET first-empty Bind is again slower in all four A/B pairs: 1.024657, 1.013390, 1.001858 and 1.016373, corresponding to 3.2–41.5 microseconds. A/A is mixed at 1.023703, 0.973158, 1.030355 and 0.996828. This overlaps the observed scale, so it does not establish causality and is not subtracted from A/B. The repeated adverse direction follows the original three adverse pairs. Other .NET cold cases and Mono mostly improve; exact 64/128-byte savings persist.

**Decision: hold this exact candidate unqualified and close the bounded clarification.** Preserve its real warm/allocation gains together with the unresolved repeated first-call cost. No further sampling or expensive full qualification is queued for this shape. Fully qualified count remains **0/10**.
