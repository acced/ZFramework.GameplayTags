# C1/C2 semantic supplement

Correctness-only supplement to `../Roadmap`. It links the unchanged Roadmap harness and Unity stubs, reusing serialized fixtures, public-state checks, string representative checks, callback regressions and deterministic transcripts. It does not measure performance or establish Unity Mono/IL2CPP integration.

## Run from the repository root

Install .NET SDK 8.0.425, or set `DOTNET` to its executable. No packages or SDK are downloaded by this runner.

```sh
sha256sum -c Tests~/C12Correctness/SOURCE_SHA256SUMS
bash Tests~/C12Correctness/run.sh \
  "B1=$PWD/Tests~/Roadmap/Baselines/B1/Runtime" \
  "PR4=$PWD/Tests~/C12Correctness/Baselines/PR4/Runtime" \
  "C1=$PWD/Tests~/C12Correctness/Baselines/C1/Runtime" \
  "current=$PWD/Runtime"
```

Each source is compiled separately; all four semantic transcripts must agree. Builds, intermediate outputs and logs remain isolated by label. Existing per-label outputs cause a refusal: use a new label for a deliberate rerun, preserving earlier evidence. Existing Roadmap correctness and .NET Standard 2.1 / C# 9 compatibility checks remain separate CI gates.

## Coverage

- Independent source and target counts 0, 1, 2, 4, 8, 16, 32, 64; target capacity count+0, count+1, count+128; equal names in distinct string objects and disjoint names. Includes empty source into populated target, singleton into large target, retained large capacity, growth and equal-size replacement.
- Copy source-side representative identity, unchanged sources, self/null input and cleared trailing references.
- Initialized/lazy-initialized copies with singleton/bulk unknown inputs; warning callbacks observe partial results, mutate target/source or throw. Includes live-index traversal after source mutation, initialization observation after target clear, and noninitializing empty/null/self copy.
- Reused Roadmap observable, mutating and union-alias callback regressions.
- First/middle/last/multiple interior missing members and fully contained inputs at sizes 4, 8, 32, 64, crossed with append/left-union-alias/right-union-alias and growth/spare capacity. Includes unmatched destination prefixes before the first missing source member and representative checks before/after containment failure.
- Empty, singleton, disjoint, interleaved, equal, source-larger, lower/upper-boundary, null and self cases.

The reference-retention diagnostic inspects .NET List<T> backing-array slots outside Count. It asserts no stale string references, not exact capacity growth, private List version or portable internal layout. Different capacities may produce different assertion totals; shared semantic transcripts must match.

## Provenance and publication

`Baselines/ORIGIN.md` identifies immutable snapshots. `SOURCE_SHA256SUMS` contains repository-relative hashes for the supplement's executable inputs and three baseline Runtime trees. Current Runtime is deliberately not pinned by that manifest: CI validates the checked-out candidate. No extra C1+C2 snapshot is required.

Generated bin/obj/results, logs and status files are excluded. The workflow retains its original full Roadmap step, then runs this four-source supplement. C1 and C2 implementation commits remain independent; publication wiring is separate. No performance results are claimed here.
