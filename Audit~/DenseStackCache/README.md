# Dense small-source stack cache: independent candidate, not an accepted selector

Base: PR20 measured `0d097a1546c51ea3676ef35817428f346da46293`; branch parent `48f08b280e9a92b25c868ad247a6de9b8bd6cd24`.

Four fixed separate builds: PR20 `fixed`, `monotone`, `stream`, and new `cache32`. The three controls are SHA256-checked against the exact measured generated source. No per-row winner or post-hoc tuning. The 32-member cutoff is a predeclared memory ceiling, not a measured optimal performance crossover.

## What changes

Only Micro <- Dense append changes. Public admission/registry/alias guards still run first. Source with at most32 members uses at most32 nonzero Micro records. Read Dense once, write initialized records into a fixed `Span<Entry>` allocated with stackalloc, count exact output records, preserve the scalar capacity-growth ladder, and merge backwards from the stack. Never read the unwritten part of the stack span. At most128B Micro16 raw payload, not the complete stack frame. Cache helper is NoInlining to bound its frame lifetime in repeated calls. More than32 members calls the unchanged stream method body under a new private name.

The stack buffer is method-owned and never escapes; no heap scratch, pool, COW, stored membership replica, delayed Count, domain restriction or persistent usage counter. Set fields, public Records16B/Enumerator32B, original query/remove/clone/builders and same-representation kernels stay unchanged. Private forward/reverse cursors remain24B. No claim of wide/Micro32 support; inherited wide query compilation limit remains.

Normal stack contents are uninitialized: this implementation writes every record that it reads and does not require Clear. Source inputs cannot mutate concurrently. C# stackalloc guidance: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/stackalloc

## Correctness and costs

Every nonzero record contains at least one member, hence records<=Count<=32. The first pass counts each new key once with a monotone destination index. Reverse write>=read protects unread destination data. Source records are independent of the destination array. Count=oldCount+sourceCount-overlapPopCount; same key with different member bits is not a duplicate member. Inherited exact growth sizes/totalBop/finalBufferBytes and actual-result-capacity zero allocation remain testable. OOM partial mutation timing is not strengthened.

Cached work: one Dense pass plus reverse record merge; removes a second Dense scan but adds cache stores/loads, Span bounds work and a call/frame. Tail/subset can lose against stream's single-pass path. Large-left/small-right can lose when merge reads many destination records. Larger inputs still pay wrapper/fallback calling cost. No performance win is asserted before measurement. Stack memory is explicitly a cost even if measured GC bytes remain zero.

## Executed protocol requested by CI

Each platform uses one host/CPU for all four builds plus same-binaryA/A, SDK8.0.425/Release, tiering/ReadyToRun off. Preserve the complete PR20 public2520/pair192/append-shape48 matrix. Add180 boundary cases (2universes,9source sizes7/8/15/16/31/32/33/63/64,5shapes,2fresh/reused contracts). In boundary rows n means requested SOURCE size; shape.leftMembers/rightMembers disambiguate large_left4096. Existing main matrix and historic ns are never spliced. Keep all seven samples, GC counts, three shuffled process rounds and all regressions; a5%screen is not a confidence interval.

Inherited originalMicro/mixed/local/bulk/stream tests, plus780 new combinations with780growth-byte checks,31/32/33 boundary, packed-count/record differences, late tag/tail positions, same-key new bits, aliases, independent mutation, repeated cache/fallback use and large-left/small-right. Cache32 is also compiled and tested with hardware intrinsics disabled and as a separate netstandard2.1 library hosted in net8. NativeUnity/IL2CPP/Burst/mobile/cold rotating actors/long-lived peak memory are NOT covered.

```sh
python3 'Audit~/DenseStackCache/run.py' --output artifacts/results --rounds 3
python3 'Audit~/DenseStackCache/review.py' --root artifacts/results
```

Full-history Git checkout is required for baseline regeneration. Source/tree/binary/generated transform/input/median/allocation/capacity/A/A are independently reconstructed. Collect downloads both original ZIPs, checks digest and reruns each archived reviewer, requiring identical structured output and cross-architecture source. Same-name generated alternatives must be compiled separately, not imported together into Unity. ProductionRuntime/Editor/Samples/Tests/package are protected and not migrated. Local snapshot preflight, if used, has explicitly different SDK/runtime and is not CI performance evidence.
