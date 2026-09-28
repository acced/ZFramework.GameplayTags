# Dense-only indexed bitmap — 4.0 prerelease

Base: `84729cc3b6e1b4681328806df2d7c8a1d8f568f6`. This branch does not merge or publish either earlier PR.

## Contract

`TagSetStorage`, Sparse storage, Auto selection and all storage-selector arguments are removed. This is intentionally not source compatible with Sparse callers. Every prepared set owns one `ulong[]`: dense member words followed by nonzero-word summary levels. There is no integer member list, page dictionary, object pool, copy-on-write or lazy query compilation. The immutable DFS registry and frozen-query semantics are retained.

A zero-capacity empty set has no bitmap. `EnsureCapacity(positive)`, a positive constructor capacity, or the first successful addition allocates the complete bitmap. Once prepared it never grows or changes representation. `Clear` keeps reservation; a new copy copies contents, not unused capacity. Queries never allocate. Exact handles and operands are checked against the immutable registry at the public boundary, including empty operands. Foreign inputs cannot partially change a result.

## Algorithms and invariants

1. **64-way occupancy hierarchy.** Level zero is the real membership bitmap. Each subsequent bit says whether a word in the preceding level is nonzero. All levels share one array, ending in one root word. No member is stored twice. Updating one member changes ancestors only when a word transitions to/from zero.
2. **Indexed successor/range search.** Ascend past empty groups and descend to their first nonzero child. DFS subtree interval queries test boundary masks and use the hierarchy for the middle, instead of scanning the entire subtree. Ordered enumeration uses the same successor.
3. **Set-bit iteration.** Clear the least significant set bit with `x &= x - 1`; use a De Bruijn scan to locate it. All 64 positions and word/summary boundaries are tested.
4. **Eager cardinality.** Portable SWAR counts result or changed bits inside the measured operation. `Count` never defers work until after timing. Dense append/union use two independent count accumulators to shorten the dependency chain.
5. **Union of summaries.** A word of `a | b` is nonzero exactly when either input word is nonzero. Consequently summary levels can also be ORed, without scanning the result again. For low occupancy the combined directory drives direct output generation. This identity does NOT hold for intersection/difference; those update occupancy from actual result words.
6. **Density-dependent kernels, not storage.** Copy/append/clear use a linear kernel when at least one-eighth of member words are nonzero; otherwise they visit occupied words. Difference uses a direct loop at high occupancy and updates ancestors only for newly empty words. The threshold is a measured candidate policy, not a universal hardware constant. The initial one-quarter implementation is kept as a fixed comparison.
7. **Whole-directory replacement.** Low-occupancy CopyFrom clears only old members, copies occupied source words and copies the directory once. Old summaries are used only to traverse not-yet-cleared words inside that single-owner operation; the correct directory and Count are installed before return. No callback or public observation occurs in between.
8. **Explicit aliases.** Union/exact intersection support either input as output; hierarchy filter supports source alias but rejects a different condition set as output before mutation. Independent outputs overwrite old data, including old words outside the new input range.

## Algorithm references

- LLVM Programmer's Manual, BitVector data-structure tradeoffs: https://llvm.org/docs/ProgrammersManual.html
- Boost Dynamic Bitset (block operations and find-next interface): https://www.boost.org/doc/libs/latest/libs/dynamic_bitset/doc/html/dynamic_bitset/index.html
- Sean Eron Anderson, Bit Twiddling Hacks (set-bit iteration, parallel population count, multiply-and-lookup bit scans): https://graphics.stanford.edu/~seander/bithacks.html
- CP-Algorithms DFS ancestry background (existing registry design): https://cp-algorithms.com/graph/depth-first-search.html

The hierarchy layout and boundary/alias protocol are project-specific implementations, not a claim of a new algorithm or a wholesale port of these projects.

## The unavoidable Dense cost

Let `W = ceil(U/64)`. A prepared payload holds `W + ceil(W/64) + ... + 1` words, with the last sum stopping at the first 1 (a one-word payload needs no summary). Directory overhead is approximately 1/63 of the member bitmap for large U, excluding alignment and array/object headers.

Skipping empty words reduces work on an existing bitmap. It does not make allocating and zeroing a new U-bit bitmap independent of U. Therefore large registries with few members can remain slower and larger than the old Sparse/Auto array on allocating copy/union. Those measurements must remain visible. Removing Sparse and simultaneously claiming universal CPU/memory dominance would require evidence this branch does not have.

Do not replace an allocating benchmark by a reused output or fused copy+operation. The benchmark keeps main, optimize, Alex-Rachel, previous Auto and previous forced-Dense sources immutable, plus this candidate. Prepared throughput is an additional comparison, not a substitute.

## Migration

```csharp
TagRegistry registry = GameplayTagManager.CurrentRegistry;
var owned = new RuntimeTagSet(registry, 1); // reserves the whole Dense payload once
RuntimeTag burning = registry.Resolve("State.Debuff.Burning");
owned.AddTag(burning);
var output = new RuntimeTagSet(registry, 1);
RuntimeTagSet.UnionInto(owned, owned, output);
```

Remove the old third `TagSetStorage` parameter. Stable serialized names, generated registry-bound instances and query `Freeze(registry)` remain unchanged. `BufferBytes` includes all summary levels. Native acceptance is still required on Unity 2021.3 / Unity 6 and Android/iOS IL2CPP; hosted .NET measurements do not approve release.

## Evidence and workflow routing

`Audit~/dense_audit.py` produces immutable source inventories, exact fixture adaptations, whole-project and documentation compilation, the legacy semantics suite migrated to a single Dense dimension, added hierarchy-index invariants, allocation controls and every benchmark row. `dense_results.py` rejects failed/incomplete benchmark data and measures the first Dense implementation against the current version. Reports include non-winners, not only gains.

The incompatible 3.x integer-runtime workflow is removed only on this branch; Dense acceptance is routed for the relevant future target branches. Historical audit scripts and reference commits remain immutable. Neither CI success nor a performance win approves a stable release or merges any branch.
