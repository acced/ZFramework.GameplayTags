# Micro array builder continuation

Base: `d45f21ffc5858a7ce9be68af9322924a774e40b4`. Fixed Auto: `84729cc`; Micro: `9be85fc`.

The prior Fusion experiment already tested Dense constructors and x64/ARM64 kernels. This continuation does not reapply its rejected extra fresh-Dense shortcut. It addresses remaining small-set batch work.

Only experiment scripts, tests and this workflow are added. Runtime, Editor, samples, native test sources and package version are untouched. No merge or release is implied.

## Independent candidates

`KernelControlSet` reconstructs the previously retained ordered SIMD lookup plus unchanged DenseFusion backend. `DirectArraySet` resolves inline/array representation outside micro batch loops, uses local arrays for record counting and merging, and block-copies remaining tails. `DirectGrowthSet` additionally merges directly into a replacement array when Append must grow, avoiding an initial prefix copy followed by another merge. All generated literal C# is retained, along with the transformation source and fixed controls. No raw member-ID array, pool, COW, new per-instance state, runtime representation switching or deferred count is introduced. Micro records remain address-plus-membership-mask records, not an uncompressed full-universe Dense representation.

The extra growth path preserves the old capacity formula. Both public aliases, default/foreign-registry behavior, result ownership, actual-result-capacity Into contract, numeric enumeration and narrow 2^20-ID limit remain. Scope validation stays at public boundaries. Internal direct-buffer helpers trust the already-established destination size and alias invariants.

## Acceptance

First run the original full Micro suite, hardware-disabled variants, targeted overlap/disjoint/different-bits-in-same-record, retained-array/inline transitions, arbitrary-placement prepared capacity, exact result capacity, and sustained random churn. The Standard 2.1 scalar backend is separately compiled and its full public suite executed through a temporary test-only friend declaration.

Then run three fresh-process rounds on each of x64 and ARM64 hosted Linux, using the unchanged historical fixture's half-overlap/half-subset relationships. Sizes and longer sample budgets match the prior Fusion continuation. Auto and Micro remain controls; the last retained kernel is an immediate control. Fresh results include allocation, initialization, real copying, writes and exact Count. Into is separate; all prepared allocation samples must be zero with a positive control. All slow rows and seven samples per case/round are kept. Queries and Dense kernels are unchanged: timing fluctuations there are not advertised as algorithmic improvements. Direct-array and growth variants are not spliced into per-row winners.

An independently written second process reconstructs source/reference Git trees and recomputes every summary and ranking from raw samples. Its report is `results/RESULTS.md`, the complete matrix is `results/comparison.csv`, and its verification is `results/independent-review.json`.

This is not a rerun of every main/Alex/indexed/packed historical library. Full overlap/disjoint/churn are correctness-tested; their full timing matrix, retained/peak memory, actual Unity Editor and mobile IL2CPP/Burst remain separate gates. ARM64 .NET execution is not mobile Unity validation. Never treat a green workflow as evidence of universal performance superiority.

Reproduce after cloning the branch: `python3 Audit~/ArrayBuild/run.py --output artifacts/results --rounds 3`. The workflow additionally archives exact source and three controls and invokes `recheck.py --root artifacts`.

Status at initial submission: implementation written; compilation and timings are pending. No speedup claim has been made.
