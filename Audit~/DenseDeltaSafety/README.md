# Delta count safety and bounded streaming batches

Base PR22 head `7ca87b196a1eb88b282c7160781b3ff98fa73d06`; fixed PR20 controls are hash-verified. This is NOT another whole-record stack-cache implementation and does not alter PR21/23 parallel experiments. Production Runtime/Editor/Samples/Tests/package remain unchanged.

## Separate questions / five compiled alternatives

- `monotone`: unchanged simple PR20 forward target search, per-record insertion.
- `stream`: unchanged PR20 prefix-aware reverse merge.
- `delta32`: unchanged PR22 missing-key cache for source Count<=32; larger source uses stream.
- `safe32`: change only cached Count updates. Matching-bit additions are committed immediately; buffered missing bits become Count only after successful insertion. Cached geometry and >32 fallback unchanged. This isolates the correctness repair and its normal-path cost.
- `batch32`: no source-count guard/fallback. Reuse one32-entry stack buffer across the entire forward source scan. Update matching keys/Count in place; buffer absent keys; on the 33rd missing key merge the previous batch then continue, never reread Dense. This tests whether the source-count cliff can be removed without an unbounded scratch array or rescanning Dense.

32 is a fixed128-byte Micro16 workspace budget, not a fitted CPU threshold. There is exactly one stackalloc per nonempty helper invocation and none in loops or recursion. NoInlining bounds its lifetime. Total stack includes the frame, saved registers, helper calls and Span state and must be reported from actual disassembly. No instance/public iterator fields, pool, shared result, COW, deferred Count or persistent alternate representation.

## Basic exception guarantee (not transactional rollback)

Minimal old failure: Micro{0}, Dense{1,16}, one-record target capacity. delta32 writes bit1 to the existing record, then growth for bit16 may throw before its final Count update. State can contain2members but Count1.

safe32 synchronizes Count with each matching record write; pending missing members are not counted until the successful merge. Allocation is completed before any tail movement. If growth throws, sorted membership and Count remain consistent, input is unchanged, and retry is possible. Some matched additions may already be retained, and capacity may have grown: NOT all-or-nothing rollback.

batch32 maintains the same guarantee at each completed batch. Already committed batches remain visible after later growth failure; the failing batch's missing keys are not counted/inserted. The source scan can have committed later same-key bits before a buffered missing key, so its partial subset is not promised identical to repeated single-tag insertion.

Safe guarantee scope: cached nonempty safe32 helper only; batch32 all nonempty mixed append. Empty target goes through unchanged old CopyCore, and safe32 >32 uses unchanged stream. Those errors are recorded separately, NOT asserted repaired. No claim about native OOM or stack exhaustion recovery. Faults are injected managed OutOfMemoryException at the exact array allocation expression in SEPARATE executables, never in timed builds.

## Batch invariants and tradeoffs

Target/source keys are strictly increasing. Buffered keys were absent when discovered; all are lower than the next source key. Flushing n absent keys shifts the current target search index by exactly n. Same-key modifications survive tail copies and merge. Missing keys never compare equal to old keys, so reverse merge needs no equality branch. Copy the old suffix after the last missing key once, then merge only the preceding records and initialized buffer prefix. Reverse write>=read protects unread data. Source is independent, single-threaded and stable.

Growth follows the ORIGINAL ReserveEntries(EntryCapacity+1) ladder. Total normal B/op, final capacity and eager result Count must equal controls. No temporary heap storage. Unlike a full two-pass merge, batches can repeatedly move the unprocessed old tail: worst target-suffix traffic O(m*ceil(s/32)) for m original records and s absent source records, in addition to forward O(W+m+r) and required growth copies. This is NOT a universally linear algorithm. Large interleaved datasets may still prefer stream. Do not claim fewer Dense reads means total CPU improvement.

## Planned execution / acceptance

Fault preflight first: reproduce old delta32 minimal mismatch, fail every actual growth in both alias orientations and AppendTags, check sorted valid partial membership, exact Count, source unchanged and continued retry/remove in scoped corrected helpers. Record old empty/fallback errors rather than hiding them under a zero-new-failure summary.

All five run inherited masks, original Micro, protected Runtime, cache boundaries, allocation-ladder tests; add batch-flush boundaries. Batch32 additionally runs no-hardware and separate Standard2.1 library hosted in net8. Full timing per variant includes original2520 public cases +192 paid both-input lifecycle +48 append shapes +320 cache/unequal cases +96 preregistered new-domain batch holdout cases. Same binary A/A,7samples x3 shuffled rounds, all GC/slow samples retained; no per-row selection. SDK8.0.425/.NET8 runtime pinned in metadata, one CPU affinity, tiering/ReadyToRun off. Every-round5% screen is not a confidence interval.

```sh
python3 'Audit~/DenseDeltaSafety/run.py' --output artifacts/results --rounds 3
python3 'Audit~/DenseDeltaSafety/review.py' --root artifacts/results
```

Full history checkout required for exact inherited source generation. Same-name alternatives compile separately, ZIP is not a Unity import package. Source/tree/generated bytes/binaries/medians/Count/capacity/allocations/fault injection provenance checked; collector redownloads both original artifacts and repeats archived review. Fault hooks/counters never enter timing.

At submission, results are NOT yet claimed complete. No Unity/IL2CPP/Burst/phone/cold-actor/peak-memory/full-history-competitor acceptance, production migration, release, merge or all-four-operation-win claim.
