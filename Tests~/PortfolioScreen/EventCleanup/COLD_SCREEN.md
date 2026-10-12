# Completed first-use event-cleanup screen

Status: eligible for one small warm rejection screen; **not qualified, 0/10**.

[Run 38215978094](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38215978094) completed at exact commit `8784e5f9e6e96a5eb596790ae100e5b26e2424b8`. Tested Runtime is original `f7c99546dbf029d70896c0f05e5119b19ffd6d26` plus patch `805651f029a280cb3be2e8e8c10edd4fa208db981852895fc118625080a91090`. Branch-root Runtime is not this candidate.

- .NET artifact `11732342567`: 27,516 bytes; SHA-256 `02f08e862444455e69536dd3a73abc88b6ac556b15e9ea17fbafadd93358de63`.
- Mono artifact `11732007941`: 21,033 bytes; SHA-256 `57a7413e9671cf1766e84ca1a7464312a5afaf352d63310186aa4b29957fd494`.
- SDK 8.0.425, CLR 8.0.31 and standalone Mono 6.8.0.105. Requested settings and actual runtime fields are preserved in the artifacts. Unity was not run.

Both backends completed all 32 fixed fresh-process calls. Every paired allocated-byte value and declared state/capacity/event result agrees; every measured GC count is zero. Correctness and untimed fixture checks passed. This measures whole first AddTag through the declared NoInlining bridge, including first compilation, growth and callback work, not isolated FlushEvents.

## All latency pairs

Values below are candidate/original elapsed-time ratios; less than 1 is faster. No sample is removed.

| Backend | Fixture | Four ratios |
|---|---|---|
| .NET | silent/d1 | 1.013652, 0.893755, 0.964565, 0.980374 |
| .NET | one-miss/d4 | 1.035354, 1.003629, 0.987410, 1.020158 |
| .NET | one-hit/d1 | 0.996572, 1.000782, 0.988961, 0.986117 |
| .NET | dense-path/d64 | 1.008677, 0.997176, 1.016979, 0.995799 |
| Mono | silent/d1 | 0.970004, 0.990025, 0.984124, 0.972272 |
| Mono | one-miss/d4 | 1.005798, 1.070364, 0.985736, 0.985454 |
| Mono | one-hit/d1 | 0.964197, 1.017824, 0.977472, 0.952293 |
| Mono | dense-path/d64 | 0.978166, 0.972387, 0.987236, 0.943766 |

Equal byte counts per call are 208/208/360/5056 on .NET and 392/392/600/5368 on Mono, in the fixture order above. The patch has no allocation-saving claim.

The modified one-hit and dense paths do not show the repeated broad first-call slowdown that stopped the prior dense-Clear candidate. The unchanged miss control remains uncertain: .NET is adverse in three pairs, and Mono retains its approximately 7% adverse sample. Four pairs do not establish cold noninferiority, a cause for variation, or final acceptance.

## Next bounded step

Independent review supports one fixed twelve-row warm screen with the identical Runtime patch. Reuse every original callback fixture and unrelated control, a single original-only pilot, shared counts and four alternating pairs. Preserve short batches and all losses. No cold resampling, source revision, automatic full qualification or successful-version count follows from this decision.
