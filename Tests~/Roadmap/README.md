# B1.1 core compatibility checks

Run `bash Tests~/Roadmap/run.sh` with .NET 8 SDK on PATH, or set `DOTNET` to its executable. The offline NuGet configuration uses installed SDK reference packs. The default variants are preserved B0, exact B1 (`dac77e7`), and current B1.1 core. To select a subset, pass version names, for example `bash Tests~/Roadmap/run.sh B1 current`.

The runner compares deterministic public-behavior transcripts, then compiles all selected variants for .NET Standard 2.1 / C# 9. B0 does not provide B1's target-writing APIs. The test adapter emulates those added APIs with public operations and serialized-field snapshots; it is not a claim that B0 natively supports them. Alias snapshots preserve the original tag values, and only aliased inputs are snapshotted before clearing the target. Existing tests of B0-native factory, logger, and registry-initialization behavior remain separate and unchanged.

Coverage includes randomized add/remove/copy/merge, null/self/target aliases, serialized unknown-name policy, ordered output/hash/index, redirects, mutable queries, warning callbacks that observe/mutate/throw, live enumeration, and registry-event initialization ordering.

Additional finite B1.1 regressions cover:

- Append with empty, ordered/reverse-disjoint, interleaved, overlapping, subset, equal-content, and large inputs; a matched-prefix/later-miss containment fallback; self append and existing target unknowns.
- Union across distinct targets, left/right target aliases, shared sources, and all-self targets, including unknown filtering and warning callbacks that mutate or throw.
- Distinct string objects with equal names, checking destination identity for append and left-hand identity for union and intersection, including right-target aliases and highly skewed input sizes.
- Negative/zero capacity, null/empty copy construction, negative `EnsureCapacity`, and noninitializing empty/invalid operations.
- Sparse/disjoint/interleaved-empty/singleton intersections with factory, filter, and reused-target output checks.
- Difference-kernel null, empty, self, disjoint, singleton, balanced, small-source, and small-remove cases across distinct/left/right/all-self target aliases; source tag identity and noninitializing unknown handling. B1/current call the internal kernel in the test assembly; B0 uses a labeled serialized-snapshot/public-`RemoveTags` adapter for this added target contract.

Current-only `B11_OPTIMIZATIONS` probes inspect the existing serialized list to check the final conservative core policy: intersection, difference, and fresh factory implementations retain exact B1 behavior. Fresh factory/`FilterExact` and distinct reused intersection targets reserve the smaller input count, including ordinal-separated, empty interleaved, and singleton-match cases. Exact-fit contained appends and both union target aliases retain capacity and source identities. These allocation-policy assertions emit a separate receipt and do not alter the shared public-behavior transcript. The runner enables them only for `current`.

Latest local run of the final conservative core: B0 981,930 assertions; exact B1 978,409; current B1.1 core 1,002,001. Shared transcript: `6EA12A348CF620CDC4FC89C09BEC0C8933A7B22A8542A2214F0CB4CE011A948F`. Both current-only allocation-policy receipts and all three compatibility compiles passed. Assertion-count differences reflect B0 adapter reflection checks and current-only probes, not missing shared semantic cases.

Unity stubs allow portable correctness checks; these do not prove Unity 2021.3 Editor/Player, Mono, IL2CPP or Inspector integration. CI runs correctness and compatibility compilation only, not noisy performance thresholds.
