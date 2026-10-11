# Version 1 evidence: reusable Union output

Historically accepted as version 1 on 2026-10-11 under the original fixed matrix; broader qualification was reopened later the same day. **Currently 0 versions are fully qualified toward the requested total.** The historical acceptance audit and positive results remain preserved. Runtime commit `f53c5fb94c22c2750f54c33dcfdee443a72bf595`, compared with original and then-previous accepted `f7c99546dbf029d70896c0f05e5119b19ffd6d26`.

## Qualification reopened: new supported alias regressions

The [stronger existing-API comparison](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38141813971) adds supported interface/delegate fixtures. Its .NET artifact `11703896488` (SHA256 `9516e1bdd298c2a5b3428afa7530868fc71f719f67e90fbb6b3c6fce8a23786b`) reports alias-left factory+Copy 2457.7 -> output 2752.0 ns, approximate 99% speedup 0.8803–0.9241; alias-right 2445.6 -> 2616.7 ns, 0.9225–0.9696. Same-revision factory controls are also adverse. Batches are about 67–81 ms. These new regressions reopen version 1; the earlier acceptance is not an unconditional current pass.

Fresh-process allocation also exposes a tradeoff. For independent 1024-tag inputs with 0/50/100% overlap, all three .NET samples allocate exactly 5,344 B more with output Union than old left/seed chains (165,232 vs 159,888; 66,880 vs 61,536; 58,664 vs 53,320 B). Both are 0 B warm. Alias output uses 10,672 B on the first call versus seed-aware 1,048 B, then 7,864 B versus 0 B warm. These counts exclude documented delegate preparation; no allocating object or cause is inferred.

Healthy seed-aware old alias chains use 0 warm bytes but take about 3.49/3.60 us, versus 7864 bytes for the new output path. Independent 1024/50% old left/seed chains take about 28.8–28.9 us versus 11.9 us for the new API, all with 0 warm bytes, but the new row's 30–31 ms batches remain explicitly short. These are scoped tradeoffs and preliminary duration-limited results, not a universal best-strategy win. Both backend artifacts have now completed independent provenance and raw-result review. Repair is required before this version is counted as fully qualified again; any longer replication includes adverse and favorable rows.

### Completed stronger-comparator Mono evidence

The same run's Mono artifact `11703329836` (SHA256 `7b4ca67c54631de44548ea8ea8e8c3eeb1abaf222168363779b118362ebdba80`) ran on AMD EPYC 9V74. All 42 Runtime source hashes, shared iteration map, source/binary immutability checks and 1,353 fresh-process cold records were verified. Each variant/fixture has identical allocation counts in all three samples.

For independent 1024-tag inputs with 0/50/100% overlap, output Union allocates **12,872 B more on the first call** than old left/seed chains: 172,808 vs 159,936; 74,440 vs 61,568; and 66,216 vs 53,344 B. Both use 0 B warm. Healthy alias output uses 18,240 B first-call and 7,896 B warm, versus the seed-aware chain's 1,056 B and 0 B. These are supported-domain cold and warm allocation costs requiring repair, not a global zero-allocation result.

No nondeep output-versus-original timing interval is wholly adverse on this Mono host. The two alias factory comparisons remain inconclusive at about 167–170 ms per batch, with approximate 99% speedup intervals spanning roughly 0.968–1.006. This does not clear the .NET alias regressions or prove zero overhead. Independent output minimum durations are 52.7/35.9/22.4 ms at 0/50/100% overlap; only the 0% row clears the existing 50 ms duration threshold (about 2.00x versus old left-chain, both 0 B warm). The other speedups remain descriptive, short-batch evidence.

## Comparator and statistical scope correction (2026-10-11)

The reported speedups and 61.6 KB-to-zero warm allocation reductions are **only versus factory Union followed by Copy**. For healthy ordinary sets and independent output, the preexisting `Copy(output, left); output.AddTags(right)` chain is semantically usable and already capable of zero warm allocation. This report does **not** establish that the new overload beats that stronger existing-API alternative. The separately frozen stronger comparison above now measures scoped warm gains alongside cold-allocation and alias regressions; all original measurements remain intact.

Acceptance used a predeclared rule for detecting reproduced material regressions, **not a proof of global statistical noninferiority**. In the original version-1 root evidence, 45/131 .NET and 25/131 Mono approximate 99% speedup intervals still permit more than 3% slowdown. No flag is not proof that every workload stays within 3%. The matching-host right-alias interval below bounds that particular row only. The original scoped acceptance remains in the audit trail, while current broader qualification is reopened; the comparator gap and wider row uncertainty are explicit.

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
