# First independent CI screen: completed, no accepted version

Exact commit: f66fd7edf40b9160a46b1496d54270019902def2. [Run and four artifacts](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38196036334). Four alternating pairs per backend; all correctness/checksum/count checks passed. This is rejection screening, not final qualification.

## Registry

Both backends preserve large shared-prefix and duplicate work reductions. Median trunk-depth128/leaves256 speedups are approximately 10.4× on .NET and 13.3× on standalone Mono; duplicate-depth8 is about 2.9×/3.0×. Several faster candidate batches are short, particularly ascending/descending prefix declarations, so those ratios remain descriptive.

.NET adverse signals remain: depth-two unique registration is slower in all four pairs (about 1.4% geometric); implicit-parent promotion is slower in all four (about 3.9%); long-root and fresh depth3000 medians are adverse with wider process dispersion. Mono root, shallow and fresh-chain controls are near-neutral with exact equal allocation, but cannot clear a separate .NET cost. Allocation counter differences on several .NET high-allocation rows vary across pairs; do not treat differences of medians as persistent additional objects.

Decision: refine actual root/fallback work and use the unchanged small screen before deciding on full qualification. No full validation is queued for the original shape.

## Hierarchical

Snapshot allocation reductions are real, and all four focused correctness reports match 468,567 checks and digest b76530c0ef95992e. However .NET empty operations add about 2.5–2.7 ns and the n1 add/remove pair about 7.7 ns; multiple rows are adverse in all four pairs while the empty manual control is approximately flat. Mono also has roughly 2–3 ns empty-path costs. Large warm-operation gains are weak.

Decision: hold this shape. Do not run full qualification or repeat its unchanged timing screen. A proposed size threshold remains unimplemented and has no measured crossover evidence.

All raw samples, short batches and unfavorable controls remain in the linked artifacts. Fully qualified count remains 0/10. No Unity Editor/Player or IL2CPP results are implied.
