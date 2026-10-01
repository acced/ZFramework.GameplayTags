# Retained kernel integration — verification pending

This continuation of PR #7 leaves all production Runtime, Editor, samples, tests and package version untouched. It does not restore Robin Hood as the selected small-set representation.

## Changes under examination

- A specialized one-pass DenseFusion backend with separate OR/AND/AND-NOT loops, eager exact cardinality and full output writes. AVX2 lookup/masks are loaded before the loop; two independent vector accumulators reduce a serial accumulation chain. No new per-set fields or secondary member representation.
- An ARM64 AdvSimd/NEON path using byte population counts and widening sums. This is a .NET 8 backend, not a claim of Unity/Burst/IL2CPP support.
- A .NET Standard 2.1 scalar SWAR backend. The workflow compiles it separately and executes the full public candidate suite against that library, rather than counting a compilation as execution.
- A literal generated FusedKernelSet combining the already-tested ordered SIMD query with the new Dense kernels. A second FusedBitmapSet additionally specializes independently allocated, nonempty Dense union, leaving input/output aliases and mixed encodings to the existing paths. Generated files and source hashes are archived.
- Separate measurements of original fixture contracts and same-assembly generic-caller diagnostics. The latter compare fresh union, copy-only and empty-Dense construction under per-case and per-sample GC settling and record all collection counts. Their results are not subtracted from or blended with the historical fixture.

## Validation rules

Immutable controls: Auto 84729cc, Micro 9be85fc, previous triad 5cb16ca. Original source bytes are checked. New candidates never store raw member IDs. The narrow micro encoding remains limited to 2^20 registered IDs; no larger-universe claim is added.

All new-result allocation, initialization, copying, mutation, output writes and Count remain inside timing. Prepared outputs must allocate zero. No pool, COW, shared mutable output, delayed Count, discarded slower samples or automatic performance approval.

The workflow uses two separately reported native GitHub-hosted CPU architectures (Linux x64 and Linux ARM64). These are managed .NET runs, NOT Unity/Android/iOS acceptance. Portability of the Standard 2.1 scalar path does not imply hardware acceleration inside Unity.

At submission, compilation, correctness and timing of this continuation are pending. After completion, consult the run artifacts and reviewed results, not merely a green job. Neither this document nor a successful workflow authorizes replacing production Runtime or merging the PR.
