# Assembly discovery array: completed portability screen and hold

The [fixed portability run 38328088548](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38328088548) is terminal; both jobs failed their required gates. The exact candidate is **closed/held for allocation failure**, and qualification remains **0/10**. No further timing or full qualification is authorized by this report. The separate Mono collector repair below requests semantic validation only and cannot waive the .NET decision.

Source package seal: `470305cba602b6378c495959aaa24faf6d98986cd4a9c1c778dcdef4405f1840`. Its files and exact inventory remain immutable. The candidate still differs from authenticated f7 only in the automatic-discovery foreach input, using the exact two-argument static Attribute.GetCustomAttributes API and typed array. Explicit Initialize is a narrower operation and was not used as the comparator.

## .NET: full behavior passed, allocation rule rejected

.NET 8.0.31/x64 completed all four builds, four semantic targets and all 24 first-discovery calls. Both full semantic snapshot/trace pairs matched. The complete Runtime metadata gate covered 38 types and 367 methods, with zero unrelated metadata/IL differences. All 12 cold pairs passed their independent full registry/state and strict assembly-inventory checks. GC0, GC1 and GC2 were zero in every sample. No integrity, deadline or finalization failure was reported.

The first two n32 pairs each allocated **6,128 B more** in the candidate. Either increase triggers the frozen close rule. The status field uses the generic Reason `semantics`, but `AllSemanticPairsPass=true` and `AnyPairedAllocationIncrease=true` establish that this .NET rejection was allocation, not a functional mismatch. Eleven of twelve latency pairs were faster; that does not waive allocation failure or establish a latency pass.

All pairs are retained below. A is original, B is candidate. Pair numbering is zero-based. Bytes are the complete measured interval's per-thread allocation; milliseconds derive directly from saved ticks/frequencies. No samples or unexplained excursions are subtracted.

| Shape | Pair/order | A bytes | B bytes | B−A bytes | A ms | B ms | B/A latency |
|---|---|---:|---:|---:|---:|---:|---:|
| n0 | 0 AB | 17400 | 11176 | -6224 | 5.525199 | 5.334131 | 0.965418802 |
| n0 | 1 BA | 11208 | 11176 | -32 | 5.476603 | 5.324177 | 0.972167784 |
| n0 | 2 BA | 11208 | 11176 | -32 | 5.469580 | 5.259025 | 0.961504357 |
| n0 | 3 AB | 17400 | 17368 | -32 | 5.529403 | 5.295704 | 0.957735220 |
| n1 | 0 AB | 12224 | 12160 | -64 | 7.267879 | 7.038930 | 0.968498512 |
| n1 | 1 BA | 12224 | 12160 | -64 | 7.222385 | 7.039260 | 0.974644802 |
| n1 | 2 BA | 12224 | 12160 | -64 | 7.264924 | 7.040703 | 0.969136498 |
| n1 | 3 AB | 12224 | 12160 | -64 | 7.304899 | 7.075548 | 0.968603125 |
| n32 | 0 AB | 31672 | 37800 | +6128 | 7.373468 | 7.466715 | 1.012646288 |
| n32 | 1 BA | 31672 | 37800 | +6128 | 7.542247 | 7.310521 | 0.969276265 |
| n32 | 2 BA | 37864 | 37800 | -64 | 7.504897 | 7.251120 | 0.966185146 |
| n32 | 3 AB | 37864 | 31608 | -6256 | 7.547750 | 7.346749 | 0.973369415 |

The n0 and n32 per-variant values include **6,192 B excursions**. Their cause is not established by saved evidence. They are not labeled as noise, a cache, JIT, instrumentation or algorithmic allocation, and are not normalized away. Both nonempty shapes still contain one attributed assembly; n32 is not 32 enumerators. The empty 32-byte difference also remains unattributed.

Both .NET declaration DLL hashes exactly match the prior local persisted stage: n1 `56cbd7e9b15f1f97435a3488009ec5c73cb28974241531d64c92a17970c3c7c0`; n32 `c70fcbf50c3b524f4353fe1a90ec899b4ff5e62d0b8d39cef7ec2c357d7c8427`. The combined portable harness/header is nevertheless a distinct process context from the preserved local native/cold stages.

## Mono: complete attempted diagnostics, incomplete semantic gate

Mono 6.8.0.105/x64, reporting EnvironmentVersion 4.0.30319.42000, completed four successful builds and all four scheduled semantic attempts. Every semantic target exited 1 with known code **102 = System.InvalidOperationException**. The finite reports identify Semantic Main, Empty/Retry, Snapshot and Value frames, plus unclassified stderr. They do not retain the exception message, assertion text or every stack frame.

| Mode | Original/candidate exits | Partial snapshot bytes | Matching snapshot SHA256 | Trace bytes |
|---|---|---:|---|---:|
| empty | 1 / 1 | 2470 | 3b43a04b691ccb577b5c388d9a11d6da57c7174fc3348b63ecfb47841fad703f | 11 |
| retry | 1 / 1 | 10407 | e3d0647e81c69df23d3c4f3ef8519c47a399657d566731dfe2e8aaeb016290b7 | 453 |

Partial snapshots and traces match within each mode, but neither mode reached a completion marker. All four metadata artifacts are absent; no Mono metadata or full semantic equivalence was established. All 24 cold slots are explicitly unattempted and contain no numerical samples. Zero reported Mono slower pairs therefore means no measurements, not good performance. No integrity, deadline or finalization failure was reported.

## Source-supported collector diagnosis, with explicit limits

Read-only source inspection found a real error in the harness's Mono dictionary assumptions:

1. The pinned [Mono corlib source manifest, line 1760](https://github.com/mono/mono/blob/82fffe0749720025ec5f7a72eae7329295ea15a4/mcs/class/corlib/corlib.dll.sources#L1760) selects the external CoreFX Dictionary implementation.
2. The [external/corefx gitlink](https://github.com/mono/mono/tree/82fffe0749720025ec5f7a72eae7329295ea15a4/external/corefx) pins commit `c30d255aec6b7de633d1c67b1a7f2ec121250359`.
3. The selected [Dictionary fields, lines 49–58](https://github.com/mono/corefx/blob/c30d255aec6b7de633d1c67b1a7f2ec121250359/src/Common/src/CoreLib/System/Collections/Generic/Dictionary.cs#L49) are `_buckets`, `_entries`, `_count`, `_freeList`, `_freeCount`, `_version`, `_comparer`, `_keys`, `_values`, `_syncRoot`.

The original encoder allowlist instead came from a Reference Source Dictionary file that is present in the repository but not selected by that manifest. It expected unprefixed fields such as buckets/comparer/keys/values. Both the field allowlist and reference-field predicate are therefore incompatible with the selected upstream source.

This makes the field-inventory assertion a strong source-supported explanation for the observed snapshot/value failure. It is **not proof of the exact observed throw site**: the sanitized report omits that assertion/message, an unsupported reflected value can also throw code102, and the provenance does not attest every deployed Mono GAC/framework binary. No complete loaded Dictionary field inventory was captured by this failed run. Equal partial snapshots cannot resolve that uncertainty.

The separate [AssemblyDiscoveryArrayMonoSerializerRepair](AssemblyDiscoveryArrayMonoSerializerRepair/README.md) changes exactly the Mono sorted-field allowlist and reference-field predicate in both encoders. It preserves every physical scalar/array slot, lazy-reference null/type identity and unknown-field failure. Runtime, declaration fixtures, header, counters and metadata comparison are unchanged. It requests only four builds/four semantic calls/zero cold calls under 120 seconds. Preparation or publication is not execution or semantic success. No timing continuation is included, and the .NET allocation CLOSE remains in force.

## Artifact identities

Only the six allowlisted JSON outputs per backend were downloaded. Each archive's six members was checked byte-for-byte against the saved JSON files. All 24 .NET numerical records and all 12 summary deltas/ratios were independently recomputed; no raw private logs, commands, paths, snapshots or metadata dumps are published here.

Archive SHA256:
- .NET: `3f81da6e855a71b8e4a1851dd7cb8f5ba80b6c2058aa3ab53b183bee3b5c65dd`
- Mono: `0685e5e74aa3849cb146df3778530f73df111785d7c8fefa4321a103dbd8ca5c`

| Output | .NET SHA256 | Mono SHA256 |
|---|---|---|
| status.json | b91aacf16faac4dbd326bd522c67c0737b04ab0fb929bb50648d2b61a5347f6d | 012a83577e70ccca39723844954c369d248504a38fc28efd867d395df97a9c69 |
| provenance.json | 636e33e79d13f1a48bb5c2455cdb856e97b7edd7713958bfd0347527313d3660 | 7fad9ed074823ace8474803d4e8a844f3251871222541cfd04f06a2dd120f9c5 |
| config.json | f4f3d4f466687bbe6742fb289b42021fd38f99629fe9d7c1beace08f7c8c7f3b | c10366e99a47a8ab3e50b8c7877a8684c448421d5fa40237d38df87c4ec5d9ac |
| correctness.json | 3c53e477f7f3a79320db71b0cb989c30f22d51b45115613d5fa7cd81104dd08c | 81f6f13a8f9b88a5d4f7860dd7f957b0d23ec4aa7e4bc7659238682b8f4bbbed |
| raw/cold.json | d2b6d5da830a4b17dbca57a1cf7dc3d97e4864e62f0f8a5f91e37dc3935d13ab | 5ad4bbde5042f2b591505837cf05dc41881c486da5bf1005cfa1ba99d6dfe330 |
| summary.json | 2674c0dc6de1544d1279eb3a7e19141901b20c287ddae9652ebd75e2d163849d | d6e7eb39f750794c2489989a3ea81c3827c245e5cbc3ed843aa31468b0799bd0 |

The preserved [local evidence](AssemblyDiscoveryArray/LOCAL_EVIDENCE.md) still records complete natural .NET native evidence, 70-byte Init code reductions, unchanged other native contexts, the earlier stable -32/-64/-64-byte observations, mixed latency/adverse pairs and original outliers. Neither those observations nor the selected-source proof overrides this completed allocation rejection or turns the incomplete Mono gate into a pass.
