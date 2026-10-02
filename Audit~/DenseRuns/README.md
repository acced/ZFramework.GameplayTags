# Owned-run append: one Dense read, bounded temporary storage

Continue PR20 measured `0d097a1546c51ea3676ef35817428f346da46293`. No production migration. Four fixed separately compiled alternatives: unchanged `monotone`, unchanged `stream`, `rotate` (no member scratch), `buffer32` (one shared32-record/128B stack workspace). No per-row oracle, fitted size threshold, extra set fields or second persistent membership representation.

## Algorithm

Entry guards already establish Micro output, stable Dense input, registry identity and aliases. During one forward Dense scan, search ONLY the original destination prefix monotonically. OR matching keys in place; append missing keys to the owned array tail; increment accurate Count by new bits. New keys are ascending and disjoint from original keys. Merge the two runs inside that SAME array.

No Dense word is reread; no insertion shifts an old suffix. This does not remove every move: rotations are extra writes. For m original records, r input records, s new records and W Dense words, gathering is O(W+m+r); binary-cut/rotation merging is O((m+s)log(m+s)) worst-case, NOT a linear-time in-place merge. Smaller-output recursion and larger-output iteration bound live recursion depth to O(log(m+s)), at most17 merge calls with existing Micro16's65536-record bound. Actual stack includes native frames.

Already ordered tail and empty/subset cases avoid merge scratch. Otherwise buffer32 allocates128B ONCE before recursion and passes the same Span throughout. If either subrun fits, copy it to scratch and merge safely forward/backward. No stackalloc in a loop or recursive method; all scratch reads follow writes. Rotate removes buffered branches/Span plumbing entirely.

The original insertion-time capacity ladder, growth allocation sizes/count, total managed bytes and final capacity are preserved. No source-count upper-bound reservation, pools, shared result, deferred Count or implicit encoding conversion. Clone+append still clones first, preserving original capacity. Adequate actual-result capacity still means0B.

## Failure boundaries

Gathering temporarily leaves two sorted runs. A necessary finally merges them before an allocation exception propagates; exceptions are not swallowed and admission facts are not revalidated. The changed nonempty-target helper must preserve the scalar-insert partial membership, Count and capacity, NOT rollback.

Separate untimed instrumented builds inject failures at ReserveEntries growth. All225cases per variant are retained, but only180nonempty-target cases exercise the changed helper. Old empty-target CopyCore can retain inconsistent partial Count after growth failure; old stream also does not guarantee correct partial Count after failure. These findings are explicitly reported, not silently called passing. New helpers must have0changed-path failures. Merge is also tested directly on65536unique records spanning the full Micro16 key range.

## Tests and precise measurement scope

Reuse complete inherited mixed/stream/bulk/local correctness, original Micro and protected Runtime suites. Add630public-operation cases covering asymmetric lengths, same-key different bits, actual capacity, repeated append/aliases,31/32/33 scratch boundaries and later mutation. Buffer32 also runs with intrinsics disabled and as independent netstandard2.1 library hosted in net8.

Timings are targeted, NOT the previous2520public-operation suite:120original main append rows(same two domains,seeds,counts,50%overlap,capacities,clone/reuse contract);48original shape rows;64predeclared held-out rows(newdomains65537/524289,counts7/33/257/2048,seeds,ratios1:1/4:1). No unchanged query/union/remove or paid-pair timing rerun. Full semantic regressions still run. Do not infer overall four-metric/release acceptance from these232cases.

Four versions plus same-binary A/A,7samples x3shuffled process rounds; inherited0.5mscalibration/8MiBallocationcap/singleCPU/tiering and ReadyToRun off. Retain slow/GC samples. Every-round5% is a screen, not statistical confidence. All compared allocation bytes, Count and capacity must match. Scratch budget and held-out inputs were fixed before observing performance, not fitted to a winner table.

```sh
python3 'Audit~/DenseRuns/run.py' --output artifacts/results --rounds 3
python3 'Audit~/DenseRuns/review.py' --root artifacts/results
python3 'Audit~/DenseRuns/fault.py' artifacts/results
```

CI pins SDK8.0.425 x64/ARM64; artifact collection redownloads originals and reruns archived review. Snapshot mode explicitly accepts verified PR17 source provenance and an installed SDK; its head/tree stay null and its runtime/timings stay separate from CI. Generated same-name alternatives compile separately. Git regeneration requires full history.

## Primary references and cost

Specialized independently written C# adjacent-run merge. LLVM's primary implementation illustrates binary partition, rotation, shorter-side recursion and optional buffering; its C++ results do not establish Unity speed:
https://github.com/llvm/llvm-project/blob/main/libcxx/include/__algorithm/inplace_merge.h

Stackalloc lifetime/limits, avoiding allocations inside loops:
https://learn.microsoft.com/dotnet/csharp/language-reference/operators/stackalloc

Public Records/Enumerator and set fields remain unchanged. Added recursion, exception regions, code and optional128B scratch have real costs. Report rotation/stack/frame sizes and all regressions, not only avoided scans. Existing Micro16/wide-query restrictions stay; no Micro32 extension. Unity/IL2CPP/Burst/phones/cold actors/retained memory/fullhistorical main-Alex leaderboard remain unverified.
