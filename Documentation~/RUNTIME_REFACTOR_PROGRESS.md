# Runtime refactor: implementation and retained evidence

This is an ongoing experimental runtime refactor, with published checkpoints and newer local changes. It is not a universal performance claim or native Unity release approval.

## Published source and retained remote runs

- Initial runtime commit: `152030dd301ad113970931048acb5a66e60d2928`
- Workflow-order correction, identical runtime: `405ff36367dcd97f205f9f1591c4ff54f7c5d7ee`
- Completed dual-architecture run: https://github.com/acced/ZFramework.GameplayTags/actions/runs/36872338908
- First workflow failure retained: https://github.com/acced/ZFramework.GameplayTags/actions/runs/36871641939

The first run failed before compilation because the GUID audit included a nested original-source checkout. Running the unchanged audit before materializing comparisons fixed that packaging issue.

The second run completed managed correctness on physical x64 and ARM64 Linux runners, including execution against the actual .NET Standard 2.1 DLL. Both architectures finished the full lifecycle and historical-competitor benchmark and uploaded raw artifacts. The overall gate failed because prepared-operation allocation assertions fired, including in unchanged original controls. Those failed rows and runs remain failed evidence, not accepted performance wins. Their older timed-output verifier also predates the strengthened v2 protocol.

## Implemented changes

- Independent capacity-preserving copy construction
- Ordered sparse insertion, balanced/skew-aware pair kernels and disjoint-range rejection
- Resolve/sort/deduplicate loading and explicit storage conversion
- Direct difference with both aliases; reserved right-output alias uses its own destination buffer
- Dense OR/AND/AND-NOT with eager count, managed AVX2/ARM64 and portable scalar backends
- Ordered hierarchy-filter interval sweep and registry prefix-path reuse
- A third, explicit compressed storage mode: one sorted 32-bit record per occupied 16-ID block, supporting U <= 2^20
- Explicit bulk preparation/result policies; conversion occurs at an allocating boundary, never as hidden query/mutation work

There is one owned member buffer, no automatic representation churn, no pool, no shared mutable output and no deferred count repair.

## Allocation-counter investigation

A library-free diagnostic on .NET 8.0.31 reproduced apparent allocation in a preallocated Array.Copy loop: 93 of 576 background-GC intervals reported bytes. The identical DLL with `DOTNET_gcConcurrent=0` reported zero in all 576 intervals; all 96 positive allocation controls remained exact. Some anomalous intervals had no collection-count delta, so dropping only samples with collections would not fix the accounting problem.

The behavior agrees with the runtime's abandoned allocation-context accounting investigation: https://github.com/dotnet/runtime/issues/134724 and https://github.com/dotnet/runtime/pull/134855 . This diagnoses a measurement problem, not permission to subtract unexplained bytes. New comparisons explicitly use Batch GC for every implementation and record the observed latency mode. Background-GC timings remain a separate historical cohort; no old row is silently corrected or combined with the new protocol.

## Why the representation direction changed

The retained DirectArrayMicro kernel still substantially beat the earlier two-format Auto on localized larger memberships. For U=262144, n=4096, half overlap, the ordinary Auto copy+append path remained much slower than the packed prototype. Dense won many scattered bulk workloads but paid universe-width allocation/copy cost for localized data. Therefore a sorted-ID/dense selector alone was not accepted as comprehensive leadership.

The first actual compressed Runtime implementation passed 20,595,984 expanded original assertions. An independent three-mode suite passed 175,751,665 assertions in each of default and forced-portable builds, under Batch GC. It covers mixed outputs/aliases, exact counts, enumeration, ownership, hierarchy/frozen queries, zero-allocation prepared operations and packed-key boundaries through 2^20−1. High-ID numeric registries are explicitly synthetic format-boundary fixtures, not million-name registry-build evidence.

Its first 1-round frontier smoke retained 3,976 rows with zero failures. Generic record-iterator loops still lost to the direct-array prototype, especially on scattered data, so direct packed-array kernels are the next measured iteration. The first generic implementation is frozen; later improvements cannot be attributed retrospectively to it.

## Other adverse evidence remains relevant

The one-object-buffer experiment reduced an eight-member set's construction allocation but caused broad query/operation regressions; it was rejected. Tiny mixed-hit exact queries still require further comparison against the earlier Vector<T> prototype. A mixed dense-copy placement investigation demonstrated same-method address/placement sensitivity; rotating 64 actors did not reproduce a candidate-specific twofold dense regression. Sparse allocating append still had adverse rows, so placement is not a blanket explanation.

Earlier v2 smoke runs showed strong resolve/sort loading and hierarchy-filter gains but only near-parity direct difference in some prepared cases. Old reports with more favorable difference numbers remain labeled by their exact source/protocol; they are not current headline claims.

## Remaining acceptance gates

- Compare direct compressed kernels and explicit bulk preparation end-to-end, charging creation/conversion and h1/h32 work
- Preserve tiny-query, forced-layout, skew, overlap-position, allocation and tail regressions against retained research kernels
- Run a final exact source version on both x64 and ARM64, with Batch GC and actual-timed-output validation
- Execute real Unity asset/editor/play-mode and target-device IL2CPP tests; managed facades and ARM64 Linux do not replace them
- Package the exact tested source, hashes, raw evidence, reproduction commands and unfavorable outcomes

## Mixed-layout and small-character pivots

The first end-to-end bulk-policy gate measured 9,240 rows with no correctness/allocation-control failure, but exposed severe performance regressions: generic Dense/Compressed union expanded dense words into too many 16-ID records, and large/tiny pairs scanned too much input. The next direct mixed kernels reduced the illustrative U262144/n16384 union from 83.8 to 4.13 microseconds and intersection from 28.8 to 3.93 microseconds. The worst fresh 32-round policy ratio improved from 17.47x to 2.97x ordinary Auto. Across 30 lifecycle shapes, the second gate's median was still 1.079x on the hardware backend and 0.922x on portable, with 17 and 9 rows respectively exceeding a 5% regression. These were one-process-round diagnostic gates, not universal-win evidence.

That prompted a backend-aware word/record comparison and direct conversion. Existing-set preparation now explicitly asks SelectBulkStorage, retains an already matching source in the caller, and otherwise pays for ToStorageForBulk, which is always independent. The old handle-export conversion protocol remains in gates1/2; the new direct protocol is separately labeled v2. Gate3 and a separately frozen boundary holdout evaluate it without relabeling the old data.

For tiny queries, a larger scalar switch initially looked favorable under fixed membership counts. Shuffled heterogeneous character populations exposed up to roughly twofold regressions, so that recommendation was withdrawn before integration. A compact scalar/vector candidate is now integrated; it has better mixed-population results but still records fixed-count/tail losses. It is not represented as uniformly superior. The new policy, conversion, compact-query and empty-set paths passed independent default, portable and hardware-disabled validation, with approximately 266.8 million assertions each and stable production-source hashes. Native Unity remains unexecuted.

The boundary holdout was frozen before gate3 results: seed20261029, counts immediately below/above 2/4/8/16 times the actual registry word count, localized/scattered/mixed inputs, and spread/late overlap. These are selection bounds rather than exact occupancy thresholds because implicit parents affect record occupancy. Its results remain separate from the tuning matrix.

## Buffered records and aligned-range checkpoint

The three-round gate4 retained 16,896 rows and 118,272 samples with no correctness or prepared-allocation failure. Its fresh h32 policy median remained 1.114x ordinary Auto on hardware and 1.090x portable; existing-set h32 medians were 1.050x and 1.037x. A mixed 128-member packed/sparse reusable union remained about four times slower than Auto across all three rounds, beyond both same-DLL controls. Localized 4096-member h32 workloads were strong wins, but did not justify a global policy claim. Same-DLL controls also had material tails, which remain in the raw report.

The next implementation replaces mixed packed/sparse iterator dispatch with direct array grouping, adds contiguous occupied-block range detection without extra per-set state, and uses aligned SIMD/scalar record operations where both inputs prove consecutive block keys. Packed lookup can then directly index a proven contiguous key range. Dense occupied-quarter counting was also improved. None changes the ordinary Auto default or silently converts a live set.

A primitive 4096-record rotating-actor diagnostic measured aligned SIMD union near 1.10 microseconds versus 10.62 microseconds for the general merge. This is a kernel diagnostic, not an end-to-end workload result. Public API validation covers shifted/contained/touching/disjoint ranges, all aliases and representations, exact/insufficient capacities, zero-mask compaction, signed keys, and gap insertion/removal. Default, forced-portable and hardware-disabled independent runs passed approximately 281.3 million assertions each. The complete managed verification also passed against the actual netstandard2.1 DLL. A first Standard-host run used the host's AVX2 capability in its expected-policy oracle; its failed evidence was retained, and an explicit portable-runtime expectation corrected only the test host. The Runtime DLL was unchanged.

The next exact-commit architecture smoke and three-round whole-workload comparison will determine whether these kernel improvements close the remaining lifecycle deficits. Full matrices remain separate from diagnostic smoke runs. Real Unity execution remains an open release gate.
