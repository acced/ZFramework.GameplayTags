# Bounded stack record cache: isolated mixed-append experiment

Base: PR20 results commit48f08b280e9a92b25c868ad247a6de9b8bd6cd24. Timed PR20 controls are regenerated and SHA256-checked. No production migration or universal speed claim.

## Five separately compiled alternatives

- fixed: PR18 one Dense traversal with Find/insertion.
- monotone: PR20 one Dense traversal with monotone destination index.
- stream: PR20 prefix/tail handling plus bounded reverse merge for internal gaps.
- dispatch: same member-count guard and helper boundary as cache32; both branches still execute the original stream algorithm. The small helper is NoInlining just like the cache helper. This diagnostic control duplicates code, not membership.
- cache32: source Count<=32 uses a128-byte Micro16 stack payload, scans Dense once into initialized prefix records, counts exact output records, then reverse-merges from that prefix. Larger inputs use the exact stream body renamed AppendDenseUncached.

32 is a conservative experimental storage ceiling, NOT an empirically accepted runtime crossover. Source records<=source members proves the initialized prefix fits. No record-overflow fallback, unbounded stack buffer, raw pointers, pool, permanent fields or second persistent representation. NoInlining keeps stack allocation lifetime within one append, not repeated inside caller loops. No SkipLocalsInit and no attempt to remove Span bounds checks. Stack payload128B is not total stack frame; disassembly must account for locals, spills and security checks. Source count>32 with only a few records is conservatively not cached.

Source reference on bounded stackalloc and initialized data: https://learn.microsoft.com/dotnet/csharp/language-reference/operators/stackalloc

## Contracts and invariant proof

Original AppendCore admits common registry, correct representations, nonempty distinct source/target. Cache helpers trust those checks, do not repeat them. Source cannot mutate during an operation. The first pass stores unique increasing keys and increases result length once for each absent target key. A source key occurring at the same target key can add distinct member bits, not necessarily a duplicate member.

Backward write>=read protects unread target records. Source records are independent operation-local values. Only initialized records[0..n) are read. The left prefix remaining after cached-source exhaustion is already correctly placed. Eager Count is originalCount+sourceCount-Pop(intersection). No borrowed result or delayed count. Existing public aliases and independent mutable result contracts remain.

ReserveEntries progresses by the original capacity staircase until the ACTUAL required record count fits; final capacity and all allocated bytes must match scalar insertion. Growth failure partial-state behavior is not strengthened. Initial stack space can fail under unusual caller stack pressure; target Unity stack budget still needs native testing.

## New validation

544 growth/ownership/continued mutation cases, Counts0/1/2/7/8/15/16/17/31/32/33/63/64/65/127/128/129, global scattered/clustered/tail/same-key-new-bit inputs, source/destination capacity boundaries, exact-result reservation, 10000 repeated prepared calls. Inherit1175 PR20 prefix cases,1920 PR18 allocation ladder cases, all65536masks in four slices, original Micro/Runtime suites and no-hardware/independent Standard2.1 host checks.

Same-binary A/A, same input digests, full prior public2520 + both-input lifecycle192 + shape48 + boundary192 cases per implementation,7 samples x3 shuffled processes, SDK8.0.425, one CPU affinity, tiering/ReadyToRun off. New boundary matrix uses both10000/262144 universes; Counts1/2/7/8/15/16/17/31/32/33/64/65; four distributions; fresh and prepared contracts. Original matrix stays byte-for-byte in the inherited fixture. Prepared0B checked with positive allocation control; fresh truly clones before append. No favorable sample removal or per-row winner selection. CPU values and uncertainty are reported separately from work counts and source reasoning.

```sh
python3 'Audit~/DenseCache/run.py' --output artifacts/results --rounds 3
python3 'Audit~/DenseCache/review.py' --root artifacts/results
```

Full Git history required for baseline regeneration; archive includes measured commit/tree, generated C#, binaries, raw samples, allocations, capacities, GC data, disassembly and reviewers. Collector redownloads original platform artifacts and requires repeated structured verification equality. Frozen sources, inputs, output Count and BufferBytes must agree. Native Unity/IL2CPP/Burst/phones, rotating cold actors, long-lived memory and all historical competitors are NOT covered by this .NET experiment. Same-name generated alternatives must compile separately.
