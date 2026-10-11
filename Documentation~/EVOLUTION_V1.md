# Version 1 evidence: reusable Union output

Accepted as version 1 on 2026-10-11 after independent gate review. Runtime commit `f53c5fb94c22c2750f54c33dcfdee443a72bf595`, compared with original and then-previous accepted `f7c99546dbf029d70896c0f05e5119b19ffd6d26`.

## Comparator and statistical scope correction (2026-10-11)

The reported speedups and 61.6 KB-to-zero warm allocation reductions are **only versus factory Union followed by Copy**. For healthy ordinary sets and independent output, the preexisting `Copy(output, left); output.AddTags(right)` chain is semantically usable and already capable of zero warm allocation. This report does **not** establish that the new overload beats that stronger existing-API alternative. A separately frozen time/allocation comparison is pending; all original measurements remain intact.

Acceptance used a predeclared rule for detecting reproduced material regressions, **not a proof of global statistical noninferiority**. In the original version-1 root evidence, 45/131 .NET and 25/131 Mono approximate 99% speedup intervals still permit more than 3% slowdown. No flag is not proof that every workload stays within 3%. The matching-host right-alias interval below bounds that particular row only. Version 1 remains recorded under its original scoped gate; the comparator gap and wider row uncertainty are explicit.

The new overload replaces an existing output:

```csharp
var output = new GameplayTagContainer();
GameplayTagContainer.Union(output, left, right);
```

The measured baseline is `Copy(output, Union(left, right))`, not the isolated factory. The existing factory remains unchanged. Distinct output reuses storage; aliases preserve factory seeding/normalization and captured source getters. Same-storage normal inputs avoid a temporary while retaining the exact clear/sparse-index compaction effects. Both-nonset proxies retain normalization fallback.

## Fixed paired evidence

Full CI: https://github.com/acced/ZFramework.GameplayTags/actions/runs/38123047785

Both full backends ran on AMD EPYC 7763. Each timing comparison used 14 independent alternating process pairs, identical workloads and iteration counts, source/binary hashes and semantic checksums. Existing root, legacy normal/padded, registry, cold, owned-layout and .NET Bulk matrices were retained.

| Reused 1024-tag, 50% overlap replacement | Original median | Candidate median | Paired geometric speedup | Warm bytes/op |
|---|---:|---:|---:|---:|
| .NET 8.0.31 | 15.814 us | 12.000 us | 1.302x | 61,624 -> 0 |
| Standalone Mono 6.8 | 68.270 us | 29.831 us | 2.258x | 61,656 -> 0 |

Self-reused medians were .NET 94.54 -> 15.89 ns and Mono 216.82 -> 16.53 ns, with 296/312 -> 0 warm bytes. These very fast candidate batches retain duration caveats and are not the sole qualifying benefit. First-use allocation is measured separately; new-output and pool initialization may still allocate. No inference of pool equivalence comes from absent/unobserved events.

The fixed root and Union timing matrices have no adverse 99% interval in the full runs. All 24 owned root graphs match. The self-state supplement compares all retained array elements, including inactive tails, across 1,026 five-phase cases and 54 five-phase overflow guards per source/backend. True-first-use self and original Union capacity/state comparisons pass. The .NET registry trunk fractional allocation flag has mixed paired variation (99% mean-delta -25.22..+27.16 B), not established growth; its raw flag remains.

The 3% material timing threshold was already present at lines 82–83 of [the first Union supplement](https://github.com/acced/ZFramework.GameplayTags/blob/c643acbdb5c308176f7f3e63a03bf607573e1b9f/Tests~/UnionOutput/run.py). It was not introduced after these results.

## Matching-host alias review

Earlier candidates had a small repeatable Mono right-alias cost on AMD EPYC 9V74. After the alias-specific change, two fixed matching-host slots were declared; one was ineligible and produced only metadata. The matching slot preserved all 28 original rows, original warmup/cohorts and the frozen iteration map. It ran 14 same-physical-binary A/A pairs and 14 original/candidate A/B pairs.

Diagnostic: https://github.com/acced/ZFramework.GameplayTags/actions/runs/38127134433

Right-alias A/B was slower in 7/14 pairs, versus 9/14 for A/A; the two order groups do not show a consistent adverse direction. Paired speedup is 1.00946, approximate 99% interval 0.97525–1.04486. The interval still permits the former subpercent cost. It bounds slowdown at about 2.53%, below the predeclared 3% material threshold. This is evidence of no reproduced material regression after the code change, not proof of zero overhead or universal superiority. All samples and both slot outcomes remain preserved.

## Raw artifact integrity

- Full .NET artifact 11696523246: `a83d6cccf4b4066436cc1ca772e7fd4dad62e0ef3a61d748661914167cf5b1ab`
- Full Mono artifact 11697920732: `32923a254b2853c613cc1d12520400078247aeb0997bd2dbeca69116d1bdc8c9`
- Matched diagnostic artifact 11698156227: `d98deafc24dddbcc60d1249d51c8fe7e5bed16e1c5cf0ee02fa86f69011dca5e`
- Ineligible slot artifact 11696808994: `52a7dea372adb4059633323d21a30da806a44f0e06e4caeddd3339a6160fbc3e`

## Limits and preserved observations

Earlier unrelated candidate runs had one internal registry cold sample +6,192 B. A fixed 48-process same-binary control on exact saved assemblies did not reproduce it on a different local host, which remains inconclusive. All current f53 sampled registry cold counts match; prior observations are not erased or assigned an invented mechanism.

Owned managed storage/capacity equivalence does not measure global ArrayPool residency, JIT code, process/OS memory or arbitrary concurrent pool activity. The alias-specific expansion grew selected native code by 225 B on local CoreCLR and 626 B on local Mono. Whole-GC retained estimates that are negative or physically implausible are preserved as diagnostics, not memory savings or proof of growth.

Unity 6000.3.14f1 Editor/Player, IL2CPP and Alex were not executed. Unity compile checks and standalone Mono measurements do not establish those results. Unsupported concurrent mutation and invalid-tag histories are outside the existing contract. No claim that every workload is faster is made.
