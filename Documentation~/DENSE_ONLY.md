# Dense-only indexed bitmap — 4.0 prerelease

Base: `84729cc3b6e1b4681328806df2d7c8a1d8f568f6`. This branch does not merge or publish either earlier PR. The continuation and measurement protocol are described in [DENSE_RESUMPTION.md](DENSE_RESUMPTION.md).

## Contract

`TagSetStorage`, Sparse storage, Auto selection and storage-selector parameters are removed. Each prepared set owns one `ulong[]`: dense member words followed by 64-way nonzero-word summary levels. There is no integer member list, page dictionary, object pool, copy-on-write or lazy query compilation. Immutable DFS registry and frozen-query semantics are retained.

A zero-capacity empty set has no bitmap. `EnsureCapacity(positive)`, a positive constructor capacity, or the first successful addition allocates the complete bitmap. Once prepared it never grows or changes representation. `Clear` retains capacity; a new copy copies contents, not unused reservation. Queries never allocate. Handles and operands are checked against the registry at public boundaries, including empty operands. Foreign inputs cannot partially modify an output.

## Algorithms and invariants

1. **64-way occupancy hierarchy.** Level zero stores real members. Each following bit indicates whether a word in the preceding level is nonzero. All levels share one array, ending in one root word. Single-member updates propagate only zero/nonzero transitions.
2. **Indexed successor/range search.** Ascend past empty groups, then descend to a nonzero child. DFS interval queries test boundary masks and use the hierarchy for the middle. Public enumeration caches a block mask and a member word, rather than performing a successor query for each word.
3. **Set-bit iteration.** `mask &= mask - 1` clears the lowest set bit; a De Bruijn multiply-and-table scan locates it. All 64 positions and member/summary boundaries are tested.
4. **Eager cardinality.** Portable SWAR counts changed words; sufficiently large linear kernels use portable Harley-Seal carry-save reduction. Count is complete before the operation returns and stays inside measured operations. No hardware-intrinsic dependency is required. Attribution and the BSD notice are in `THIRD_PARTY_NOTICES.md`.
5. **Summary union identity.** A member word of `a | b` is nonzero exactly when either input word is nonzero. Linear union/append can OR summaries directly. This identity does not apply to intersection or difference, whose summaries derive from actual result words.
6. **Density-dependent kernels, not storage.** At least one-eighth of member words occupied selects a linear kernel; otherwise indexed kernels traverse active blocks. The threshold is an engineering candidate, not a hardware-independent optimum. Earlier one-quarter and per-word implementations remain fixed comparison references.
7. **Block transactions and direct overwrite.** Indexed operations process up to 64 member words per directory mask and publish that mask once. Copy merges old and incoming occupied-block streams; union merges both inputs and the output's previous blocks. Old-only words are cleared and new words are written directly. This avoids clearing a directory path and immediately recreating it. Dense old outputs can use native array clearing first. Cursor masks describe only not-yet-visited blocks; public invariants hold again before return. Single-owner operations invoke no callback in the intermediate phase.
8. **Explicit output aliases.** Union/exact intersection support either input as output. Hierarchy filtering may overwrite the source, but rejects overwriting separate conditions before any mutation. Independent output also removes old words outside both input ranges.

## Algorithm references

- Hierarchical 64-way bitmap successor structure: https://hitonanode.github.io/cplib-cpp/data_structure/fast_set.hpp.html
- LLVM BitVector tradeoffs: https://llvm.org/docs/ProgrammersManual.html
- Bit iteration, population count and bit scans: https://graphics.stanford.edu/~seander/bithacks.html
- DFS ancestor intervals: https://cp-algorithms.com/graph/depth-first-search.html
- Carry-save adaptation and retained license: `THIRD_PARTY_NOTICES.md`.

The block layout, mutation protocol and registry boundary are project-specific implementations. This is not a claim of new algorithms or a wholesale port of these libraries.

## Cost boundary

Let `W = ceil(U/64)`. A prepared payload contains `W + ceil(W/64) + ... + 1` words, stopping at the first 1; a one-word bitmap needs no summary. The directory adds approximately 1/63 of the membership payload for large U, excluding alignment and object/array headers.

Skipping empty words reduces work on an existing bitmap. It does not remove allocation and initialization of a fresh U-bit array. Large-universe/small-member allocating workloads can remain slower and larger than old Sparse/Auto. All non-winners remain in the reports. Universal CPU/memory dominance is a test target, not a guarantee made by this implementation.

Never replace an allocating benchmark with a reused output or a fused copy/mutation. The common matrix keeps main, optimize, Alex-Rachel, previous Auto, previous forced Dense and this candidate. Prepared throughput is an additional, separately named matrix.

## Migration

```csharp
TagRegistry registry = GameplayTagManager.CurrentRegistry;
var owned = new RuntimeTagSet(registry, 1); // reserves the complete Dense payload
RuntimeTag burning = registry.Resolve("State.Debuff.Burning");
owned.AddTag(burning);
var output = new RuntimeTagSet(registry, 1);
RuntimeTagSet.UnionInto(owned, owned, output);
```

Remove the third `TagSetStorage` argument. Serialized stable names, registry-bound generated instances and `Freeze(registry)` remain. `BufferBytes` includes all summary levels. Native Unity 2021.3 / Unity 6 and Android/iOS IL2CPP acceptance remains required; hosted .NET timings cannot approve release.

## Evidence

`dense_audit.py` produces fixed source inventories, exact fixture adaptations, whole-project/docs/split-assembly checks, the semantics suite migrated to one Dense dimension, index invariants and every timing/allocation sample. `dense_block_check.py` adds multi-level dirty-output/alias and actual bit-count-kernel checks. `dense_resume.py` compares this continuation with fixed incoming `3939cb8`; initial and intermediate Dense comparisons remain separate.

`dense_protocol.py` requires `settled-setup-v2` identity, positive allocation controls, complete samples and zero allocation in every prepared candidate sample. Preparation is identical for every implementation and happens before the timer; allocating results are still created inside timing. See the continuation document for the reproduced no-op control and why raw old-protocol samples are not mixed with new timings. No subtraction, outlier removal or gate exemption is used.

The 3.x workflow is replaced only on this Dense branch; historical audit scripts and reference commits remain. No CI result merges a branch, publishes a stable release or grants native/device approval.
