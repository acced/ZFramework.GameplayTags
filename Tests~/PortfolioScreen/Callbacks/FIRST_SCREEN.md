# First callback screen: rejected

Exact candidate commit: `73b4d5d693bc9622867021e0de71a8612b72ee15`.
[Both backend jobs completed](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38199196551).
Each retained twelve rows, one original-only pilot and four alternating paired rounds. Completion is not acceptance. Fully qualified count remains **0/10**.

.NET showed geometric speedups around 1.20× on the dense depth64 path, 1.16× on the large-table gaps, and 1.12× on descending bulk. However, large-table single-node midpoint and late-miss rows were slower in all four pairs: medians approximately 75.84→78.34 ns and 73.07→75.74 ns. The latter includes a retained 80.39 ns candidate sample. The one-entry missing callback row was also slower in all four pairs by about 0.8%.

Standalone Mono's midpoint and late-miss rows were likewise slower in all four pairs, approximately 114.92→119.80 ns and 84.81→89.00 ns. Its one-entry miss was about 136.77→141.09 ns, also four adverse pairs. Dense depth64 was mixed, with only small gap/bulk gains. These adverse rows used roughly 192–220 ms batches and zero measured allocation and GC. All raw samples, including variation and sub-200 ms flags, remain in the artifacts.

The exact shape is rejected before cold/full qualification. One bounded code repair is being evaluated: use the original notification loop for a single hierarchy node or at most one callback entry, where cross-node search narrowing cannot reduce meaningful search work. The additional admission branch and larger methods can still cost time; this is a hypothesis, not a claimed fix. The same twelve-row screen must retain every previous adverse row. No additional sampling of the rejected code is planned.

Artifact identities:
- .NET artifact `11723819419`, ZIP SHA-256 `459485bc55a1caa7ce0b2ab91eb72e26e3cca651e968b43549411831388c5218`
- Mono artifact `11724703070`, ZIP SHA-256 `37d545855dda304e4aa81e2e676ba5a874d69db7122b6b4d68f1d4b21356db15`

These are standalone .NET/Mono screens. Actual Unity Editor/Player, IL2CPP and Alex performance is unmeasured.
