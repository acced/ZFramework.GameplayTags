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

## Executed packed-range milestone and next measured deficits

The complete Runtime was published as `38970024952b296a449c2ac6091facf2652e3e70`. Its exact-commit run, https://github.com/acced/ZFramework.GameplayTags/actions/runs/36901049713 , passed all 13 managed verification suites on both physical x64 and ARM64 Linux runners, including execution against the separately compiled .NET Standard 2.1 assembly. The lifecycle and retained-frontier smoke shards also passed. The two bulk jobs failed at argument parsing because the workflow used `--aa` rather than the bulk runner's `--current-aa`; they did not measure or invalidate Runtime behavior. Workflow-only commit `4a5553cb8a9308b57117a09675686f964f1e92c8` reran correctness and only those bulk shards. Its run, https://github.com/acced/ZFramework.GameplayTags/actions/runs/36901645777 , completed successfully. Runtime and measurement sources are byte-identical across the two commits. All 14 artifacts, including the failed launch evidence, were downloaded and their ZIP hashes checked.

The unchanged local three-round gate5 passed 48 processes, 16,896 rows and 118,272 retained samples. Mixed128 fresh h32 improved from gate4's 1.452x/1.877x ordinary Auto to 0.948x/1.023x hardware/portable. Portable localized4096:8 improved from 1.46–1.53x to 0.987–1.032x. Localized4096 at U262144 measured 0.028x/0.112x Auto, with 5,440 versus 65,752 bytes allocated per lifecycle. These are exact workload comparisons, not a weighted or universal speedup.

The focused matrix's fresh h32 median was still 1.067x hardware and 0.994x portable; short horizons continued to lose. One hardware16K mixed suffix fixture lost against both Auto controls in all three rounds despite identical Dense/Dense/Dense storage and allocation. Its preparation took 42.92 microseconds versus 31.91 for Auto, while prepared union/intersection/difference did not show a consistent gap. This motivates reducing admission/materialization work without retuning the selector. Ordinary Auto exact-probe medians also remained 1.283x/1.098x previous405 in this matrix; explicit Bulk wins must not obscure that query/dispatch deficit.

The physical ARM64 smoke adds a different adverse result: localized/scattered16K at U262144 chose Compressed/Dense and fresh h32 was 1.99x Auto; union, intersection and copy-based mutation were all about twice as slow. A new word-grouped mixed kernel is therefore being tested separately. The next experiments preserve existing source/protocol evidence and measure creation fast paths, bounded tiny-query kernels, and grouping packed quarters before dense work. No new winner or full-matrix acceptance is claimed from these one-round architecture smoke samples.

## Checked-arithmetic portability correction

A separate production-library build with CheckForOverflowUnderflow enabled exposed real failures: negative cursor/range guards used unsigned reinterpretation without an explicit unchecked context, and one packed/dense filter intentionally narrowed a shifted64-bit word before masking. These were not failures of the ordinary unchecked compilation measured above. Published389 reproduced nine targeted failure groups against both checked net8 and checked Standard libraries. Explicit unchecked casts now preserve the intended guard/truncation semantics; the intermediate filter failure is retained too.

The corrected checked net8 and actual Standard libraries passed the complete26-group public suite, with299,388,197 and299,388,482 assertions respectively. Their oracle hosts are explicitly unchecked, keeping test checksum arithmetic separate from the production-library setting. The permanent verifier now includes both checked-library executions. Native Unity and IL2CPP remain unexecuted. Performance candidates from the admission, query and grouped-word studies remain isolated pending their own acceptance; this correction does not promote them.

Ordinary unchecked builds were independently compared before publication: all488 net8 methods and485 Standard methods have identical signatures, method IL, local signatures, maximum stack, flags and exception regions between3897002 and the ten-cast correction. This supports preserving the earlier default-build performance evidence as the same code behavior, rather than inventing a new speedup. The checked-library runtime setting is recorded separately from the unchecked oracle-host setting.
