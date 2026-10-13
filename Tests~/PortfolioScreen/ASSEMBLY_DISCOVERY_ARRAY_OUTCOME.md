# Assembly discovery array: completed portability screen and hold

The [fixed portability run 38328088548](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38328088548) is terminal; both original jobs failed their required gates. The separate [Mono collector repair run 38329543382](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38329543382) has now passed the complete semantic/metadata gates with no measurements. The exact candidate remains **closed/held for the original .NET allocation failure**, and qualification remains **0/10**. No Mono timing, further source variant, sampling or full qualification is queued. The successful collector repair cannot waive the .NET decision or establish Mono performance.

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

## Original Mono run: preserved failures and incomplete semantic gate

Mono 6.8.0.105/x64, reporting EnvironmentVersion 4.0.30319.42000, completed four successful builds and all four scheduled semantic attempts. Every semantic target exited 1 with known code **102 = System.InvalidOperationException**. The finite reports identify Semantic Main, Empty/Retry, Snapshot and Value frames, plus unclassified stderr. They do not retain the exception message, assertion text or every stack frame.

| Mode | Original/candidate exits | Partial snapshot bytes | Matching snapshot SHA256 | Trace bytes |
|---|---|---:|---|---:|
| empty | 1 / 1 | 2470 | 3b43a04b691ccb577b5c388d9a11d6da57c7174fc3348b63ecfb47841fad703f | 11 |
| retry | 1 / 1 | 10407 | e3d0647e81c69df23d3c4f3ef8519c47a399657d566731dfe2e8aaeb016290b7 | 453 |

Partial snapshots and traces match within each mode, but neither mode reached a completion marker. All four metadata artifacts were absent; that original run established no Mono metadata or full semantic equivalence. All 24 cold slots are explicitly unattempted and contain no numerical samples. Zero reported Mono slower pairs therefore means no measurements, not good performance. No integrity, deadline or finalization failure was reported.

## Source-supported collector diagnosis, with explicit limits

Read-only source inspection found a real error in the harness's Mono dictionary assumptions:

1. The pinned [Mono corlib source manifest, line 1760](https://github.com/mono/mono/blob/82fffe0749720025ec5f7a72eae7329295ea15a4/mcs/class/corlib/corlib.dll.sources#L1760) selects the external CoreFX Dictionary implementation.
2. The [external/corefx gitlink](https://github.com/mono/mono/tree/82fffe0749720025ec5f7a72eae7329295ea15a4/external/corefx) pins commit `c30d255aec6b7de633d1c67b1a7f2ec121250359`.
3. The selected [Dictionary fields, lines 49–58](https://github.com/mono/corefx/blob/c30d255aec6b7de633d1c67b1a7f2ec121250359/src/Common/src/CoreLib/System/Collections/Generic/Dictionary.cs#L49) are `_buckets`, `_entries`, `_count`, `_freeList`, `_freeCount`, `_version`, `_comparer`, `_keys`, `_values`, `_syncRoot`.

The original encoder allowlist instead came from a Reference Source Dictionary file that is present in the repository but not selected by that manifest. It expected unprefixed fields such as buckets/comparer/keys/values. Both the field allowlist and reference-field predicate are therefore incompatible with the selected upstream source.

This makes the field-inventory assertion a strong source-supported explanation for the observed snapshot/value failure. It is **not proof of the exact observed throw site**: the sanitized report omits that assertion/message, an unsupported reflected value can also throw code102, and the provenance does not attest every deployed Mono GAC/framework binary. No complete loaded Dictionary field inventory was captured by this failed run. Equal partial snapshots cannot resolve that uncertainty.

The separate [AssemblyDiscoveryArrayMonoSerializerRepair](AssemblyDiscoveryArrayMonoSerializerRepair/README.md) changes exactly the Mono sorted-field allowlist and reference-field predicate in both encoders. It preserves every physical scalar/array slot, lazy-reference null/type identity and unknown-field failure. Runtime, declaration fixtures, header, counters and metadata comparison are unchanged. Its completed validation used only four builds/four semantic calls/zero cold calls under the fixed 120-second deadline. No timing continuation is included, and the .NET allocation CLOSE remains in force. The repair success below supports the collector correction, while the exact historical exception message remains unavailable.

## Completed Mono repair: full semantic/metadata pass, no performance samples

The [semantic-only repair run 38329543382](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38329543382) ran from commit `a3fe2cdc5453285f0d7d8b67a7335ac927f0557b` with repair seal `d080c9b7925472c1c0a211edf66f85261c0aa36a670aded20f2410c500ec40a1` and the same immutable source-package seal above. Its actual header reports Mono 6.8.0.105, EnvironmentVersion 4.0.30319.42000 and 8-byte pointers.

All four compiler attempts succeeded. Original and candidate both completed empty and retry semantics with exit 0, the expected completion/public-caller markers, complete snapshots/traces and metadata present. Both modes pass the full functional comparison. The strict metadata gate covers 38 Runtime types and 367 methods, with zero unchanged metadata differences and zero unrelated raw-IL failures. The only allowed Runtime method-body change remains InitializeIfNeeded. No integrity, deadline, finalization or diagnostic-projection failure was reported; the exact four-build/four-semantic/zero-cold stop receipt was validated.

| Mode | Original/candidate exits | Complete snapshot bytes | Matching snapshot SHA256 | Trace bytes | Matching trace SHA256 |
|---|---|---:|---|---:|---|
| empty | 0 / 0 | 5008 | af4c78ef6180b026ae33ee6943cb4ccf75cdf93252f8e94e085069e5e3cc51fd | 237 | 2e3ac39846d0873722068bcddb9ea3c4405a1be5268cabfd6fe2ffa70c89035f |
| retry | 0 / 0 | 40532 | c202fc5459035313489bf2cafd2f0f81b79576a8869eec240e79faed5f83bef7 | 1250 | 567b62d7f2ae565cb3a1d0a6c52af15d7af207c2fc6d6afc305a17f2fd0985cf |

These are full outputs from the repaired collector, not extensions or replacements of the original failed run's partial outputs. The original failures and their hashes remain above. Successful traversal with the corrected fail-closed field inventory provides new evidence for the collector correction; it does not reveal the original omitted assertion text.

**No Mono performance measurement ran.** Cold targets, completed cold calls and numeric rows are all zero; all 24 fixed cold slots are explicitly unattempted with null measurements. Warm, pilot, C# measurement samples and native captures are also zero. The summary's incomplete cold-pair flags and zero slower/increased-allocation pair counts describe missing samples, not a cold pass or noninferiority. No portable allocation or latency claim follows from this semantic-only success.

## Artifact identities

Only the six allowlisted JSON outputs for each original backend and the Mono repair were downloaded. Each archive's six members was checked byte-for-byte against the saved JSON files. All 24 .NET numerical records and all 12 summary deltas/ratios were independently recomputed; no raw private logs, commands, paths, snapshots or metadata dumps are published here.

Original portability-run archive SHA256:
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

The Mono repair artifact is ID `11781704824` from run `38329543382`. Its six archive members also match the saved files byte-for-byte. Snapshot/trace sizes and fingerprints above are the saved diagnostic records; their raw bytes are not in the six-file archive and were not independently rehashed during this closeout. Archive SHA256: `2c64f7bb10729a1b0ab1baad54509431aaa103f1afbbc40cf1a1bd29be903a2e`.

| Repair output | SHA256 |
|---|---|
| status.json | 5d6b34ba7d4081dea6acc990e23c828ff8db7aabfe29843e3d3d1c947e012ca8 |
| provenance.json | ae9dcd91239e5d6496b41a37427e89ac156d3126cd2759d637e3b0c1682839a2 |
| config.json | f6ab086a4466984286a28ce079b5bc3ed73f299cfa38bef3d7478df33c8641f9 |
| correctness.json | 535e17dddafd08443f3168ec8f4dfbd2410c92ab88b4dde5103f578771de53a1 |
| raw/cold.json | 5ad4bbde5042f2b591505837cf05dc41881c486da5bf1005cfa1ba99d6dfe330 |
| summary.json | 21cc201f15d762ed596d34fc99069c3542efc4717d335f1f9c25fdeb92e0c03b |

The preserved [local evidence](AssemblyDiscoveryArray/LOCAL_EVIDENCE.md) still records complete natural .NET native evidence, 70-byte Init code reductions, unchanged other native contexts, the earlier stable -32/-64/-64-byte observations, mixed latency/adverse pairs and original outliers. Neither those observations, the selected-source proof nor the now-completed Mono semantic repair overrides this completed .NET allocation rejection. Mono allocation and latency remain unmeasured.

## Final disposition and documentation-only closeout

The exact candidate is terminal **CLOSE/HOLD** under the frozen .NET paired-allocation rule. Its emitted native simplification and scoped semantic success remain real evidence, but they do not satisfy portable allocation/latency qualification. No further timing, source variant, sampling or full qualification is queued. Root Runtime is unchanged and the qualified count remains **0/10**.

This closeout changes only this outcome document and the common PortfolioScreen README. Both immutable source/repair packages and the workflow remain unchanged. It is intended for a documentation-only `[skip ci]` commit to avoid rerunning the completed experiment. A skipped or pending check is not a passed check, optimization acceptance or merge readiness; the completed run links above identify the actual executed evidence.
