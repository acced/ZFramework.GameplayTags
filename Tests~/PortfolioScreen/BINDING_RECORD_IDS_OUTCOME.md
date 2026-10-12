# Compact binding records: first CI screen held

Qualified versions remain **0/10**. This is a bounded rejection/portability screen, not final qualification. Root Runtime was unchanged by the test-package commit.

- Exact test commit: `9a39654f5899592fdae7b35c3653420021de8bef`.
- [Completed run 38279828100](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38279828100). .NET completed two semantic targets, 32 cold calls and 56 warm calls. Mono completed two builds and stopped at the original semantic stage after one attempted target, before candidate semantics or any cold/warm calls.
- .NET artifact `11760731516`, ZIP SHA-256 `258d22acad1500bc8df541bf20a0afff96c06242cb76ea296b8e80fc99f89d6b`.
- Mono artifact `11760766701`, ZIP SHA-256 `1feb5f83422848e9eab4c00a17482f897355df24113fb2c8a39d7862b4c80389`.
- Both ZIP identities and all eight allowlisted files per backend were independently reviewed. .NET actual CLR 8.0.31, x64; SDK configured as 8.0.425. Mono runtime/layout was not validated before its stop. Installing a runtime is not evidence that the semantic target passed.

## .NET memory and behavior

Functional snapshots match SHA-256 `730eaa6d941f0fd91f88ec5478741cfd4284754cf1c48c4fd2a8cfe296b87ea0`; the metadata gate allows only the intended private field and two method-body changes. Actual managed record size is 24→16 bytes. The removal of unused mutable-name retention is intentional: arbitrary name WeakReference lifetime/private graphs can differ. Both original delegate constructions remain.

Each of the twelve single-bind cold pairs saves exactly 32 allocated bytes and 32 bytes of final record-array payload. All four five-Bind growth pairs save 96 allocated bytes across growth and 64 bytes of final payload (192→128). Payload excludes array headers, other objects and whole-heap retention. Cold samples have zero GC.

Fresh warm cycles save exactly 32 bytes (576→544 B/cycle). Retained group cycles have equal allocation. All 56 warm batches exceed 250 ms (minimum 286.83 ms); 40 have Gen0 collections and none have Gen1/2. These GC costs are included, not subtracted. Direct controls have zero bytes/GC.

## All paired timing ratios

Numbers below are candidate/original, in the frozen AB/BA/BA/AB pair order. Values above 1 are adverse. No sample is discarded, and four pairs establish neither statistical noninferiority nor universal causal effects.

| Cold shape | Pair 0 | Pair 1 | Pair 2 | Pair 3 |
|---|---:|---:|---:|---:|
| absent | 0.992282 | 0.982082 | 0.979739 | 1.000452 |
| present | 1.011839 | 0.979942 | 0.971911 | 0.964273 |
| external | 1.005160 | 1.028671 | 1.014568 | 0.987656 |
| growth-five | 1.018970 | 0.971149 | 0.822683 | 1.006602 |

| Warm cycle/control | Pair 0 | Pair 1 | Pair 2 | Pair 3 |
|---|---:|---:|---:|---:|
| fresh-single | 0.986519 | 0.916405 | 0.982880 | 0.968504 |
| retained-absent | 0.950248 | 0.940684 | 0.939831 | 0.945457 |
| retained-present | 0.958601 | 0.962765 | 0.986583 | 0.975803 |
| distinct-eight | 0.931320 | 0.926591 | 0.994485 | 0.925274 |
| external-multicast | 0.954447 | 0.979544 | 0.985829 | 0.962596 |
| direct-subscription | 1.026465 | 1.008687 | 1.021525 | 1.014709 |
| direct-dispatch | 1.006034 | 1.000595 | 0.984853 | 0.958735 |

The five changed warm rows improve in every pair, but the unchanged direct-subscription control is slower in every pair by 0.9–2.6% (geometric ratio about 1.0178, at least 296.68 ms, zero bytes/GC). External-listener first-use is slower in three of four pairs. Those adverse observations remain unresolved and are not offset by memory savings. Local mixed times, 39/56 short warm batches, allocation and GC observations remain in [the frozen local evidence](BindingRecordIds/LOCAL_EVIDENCE.md).

## Decision and Mono failure

**HOLD.** No new .NET timing, source-layout variant or full qualification follows this screen. The first Mono original semantic attempt did not produce a validated semantic/runtime/layout result. Existing public artifacts and job logs do not identify its cause: child diagnostics were private, while public status only records the stage and counts. No Runtime defect, compiler defect, GC cause or environment cause is inferred.

A separately reviewed original-only Mono semantic diagnostic may report fixed phase/error codes while preserving exact Runtime and harness bytes. It must run no candidate semantic target, cold call, warm call or pilot; a successful diagnostic would be non-reproduction, not an explanation of the original failure. The failed run remains evidence. No actual Unity, IL2CPP, 32-bit or comprehensive acceptance claim is made.

The same-group API is the direct comparator. A fixed manual subscription can avoid group ownership costs and is a narrower public alternative; unchanged direct controls are not semantically equivalent replacements for dynamic shared ownership. No result claims superiority over every manual subscription route.

## Original-only Mono diagnostic

[Run 38281761432](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38281761432), exact commit `755f046208eee25bf582286837074dd89375926c`, completed its fixed two-build/one-original-target diagnostic. Artifact `11761576669` has ZIP SHA-256 `34625fcdfcd40dbd812182dc0d65d458c294ac96c4b3e6ca9f3947366d777d2e`; its sole `diagnostic.json` member was independently verified.

The original target reported Mono 6.8.0.105, x64, completed the None/core/binding-70 markers, and wrote the same 1,824-byte functional snapshot (`730eaa6d941f0fd91f88ec5478741cfd4284754cf1c48c4fd2a8cfe296b87ea0`). Original record size was 24 bytes. It then exited 1 during metadata capture, with recognized frames in `CaptureBody` and `SignatureDecoder`. The supplemental completion marker is printed after metadata capture; its absence does not contradict the completed snapshot.

This is a failed metadata gate, not a complete Mono semantic pass. The bounded exception projection did not recognize the exception type, so neither the exact failing operation nor its cause is established. No candidate target, cold/warm call or pilot ran. The next proposed diagnostic adds only guarded numeric token/cursor/stage fields and hashes at that first signature failure, then rethrows; it cannot skip a signature or convert metadata failure into success. It publishes no signature bytes, arbitrary exception names/messages or private paths. A fixed two-build/one-original-target, 120-second limit remains. Root Runtime, the held decision and **0/10** are unchanged.

## Numeric signature localization

[Run 38284300928](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38284300928), exact commit `5e78f5308f247e3d397bc2d2aa5fa7728f3755e8`, completed two builds and one original target within its fixed bound. Artifact `11761804471` has ZIP SHA-256 `f9411e9c3c05e2cad8b070d74881366226fae5b0987bfc281139fc65c068424f`. Its single validated diagnostic records a failure at `ResolveType`: method token `0x060000c2`, generic-instance element kind 21, decoded TypeRef `0x0100001d`, offset 8 of an 11-byte local signature, and fixed exception code 3 (`BadImageFormatException`). The type-argument context has length zero; the method context is null. The signature SHA-256 is `65a34972975a1a779d01d8866fc4156508c245b01a3c88b0cbda59cafe52ca86`. Functional snapshot, original layout and stdout hashes still match the preceding diagnostic. No candidate or performance target ran.

Mono 6.8 source inspection supplies a concrete compatibility hypothesis: a nonnull empty generic context is represented as a zero-arity instantiation, while a null context does not request that inflation. Resolving an open generic head with the former can fail. This matches the recorded operation/context/exception, but the exact Mono TypeRef name has not been independently extracted from its binary. The signature hash matches the preserved local-signature shape containing `TagStorage`, `ReadOnlySpan<int>` and `int` after the reference-table index difference; this is corroboration, not an independent Mono table read.

A separately frozen semantic-only collector repair normalizes only empty generic-context arrays to null; all nonempty contexts, decoder rules and metadata comparisons stay intact. It starts from the original collector, removing diagnostic instrumentation. The planned two backend jobs each permit at most two builds and two semantic targets under 120 seconds, stopping before any cold, warm or pilot stage. Passing would establish portable metadata/functional validation for this candidate, not resolve the existing .NET timing hold or count a successful optimization. Prior failed gates remain preserved.

## Empty-context repair validation

[Semantic-only run 38286398168](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38286398168), exact commit `3215411dc91ad6942f84ee90411605270594388f`, completed both .NET targets and the original Mono target within the fixed budgets. No cold/warm/pilot samples ran.

- .NET artifact `11763950732`, ZIP SHA-256 `392544a132547787e8f5d33e8790859b01a1dcd57fc5af869c8748bc8f349a16`: both targets pass the complete semantic/metadata gate, identical functional snapshot `730eaa6d...`, measured 24→16 B layout, 38 types/367 methods, zero unrelated metadata/raw-IL differences.
- Mono artifact `11763048993`, ZIP SHA-256 `b45afe31963514290f027359e7df07dd43bfd2ac99a8d8e291192a8312d910a7`: original exits 1 after the same snapshot/core/binding markers. The new failure frames identify event capture with fixed exception code 111 (`ArgumentNullException`); the candidate is unattempted. Complete metadata validation remains failed.

Official Mono 6.8 source represents absent event “other methods” as null, and `GetOtherMethods(true)` returns that field directly. Passing it to the collector's LINQ `Select` can produce the observed exception. All six Runtime events in the saved .NET metadata have empty other-method lists. This is a source-backed explanation, while the bounded report alone does not identify the precise throwing expression.

The next one-expression collector repair normalizes that null collection to empty while retaining every nonempty method identity, add/remove/raise accessor, attribute and comparison. It retains the empty-context repair and the same semantic-only dual-runtime bounds. Earlier frozen packages and failures remain evidence. The Runtime candidate is unchanged; the original .NET control/cold costs, lack of Mono candidate/performance evidence and **0/10** hold remain.
