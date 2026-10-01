# Integer runtime architecture and migration

## Fixed scope

This branch replaces the 2.x name-container combat path, not the serialized authoring format. Baselines are Z main `934b14a47ddf0b6367bf6720be58c18cbcb09896`, Z optimize `a99f53d4697df6f577961ff1ee1bad14caa221d1`, and Alex-Rachel `28e4218f459ecf0e9fb4f0bca90805c05eb962c2`. The new runtime must win through less work, never through wrong outputs, excluded counts or different operation sequences.

## Data ownership

- GameplayTag / GameplayTagContainer: stable names, serialized fields and editor operations. ToRuntime is an explicit allocating load boundary.
- TagRegistry: canonical name dictionary (including resolved aliases), DFS name array, parent array, exclusive subtree-end array and a read-only ordinal authoring view. Settings changes cannot mutate these arrays. DFS construction is iterative, not bounded by managed recursion depth.
- RuntimeTag: registry reference plus local integer ID. It is deliberately not a serialized struct. Set member arrays store only IDs or bits, not full handles.
- RuntimeTagSet: one live member array. Sparse IDs and compressed key/mask records use int[]; dense uses ulong[]. Count is maintained eagerly. The mode is fixed at construction; there is no global pool, hidden scratch buffer, parent cache, lazy repair, copy-on-write or automatic representation churn.
- FrozenGameplayTagQuery: a registry reference plus immutable node, child-index and integer-range arrays. Source validation/reduction uses temporary builder data, discarded after freezing.

The manager is a loading/editor service, not a runtime lookup dependency. A game context captures CurrentRegistry and owns its sets/bindings. Rebuild publishes another snapshot; old contexts can finish with old data. Foreign valid handles/sets/queries are rejected at public boundaries. Internal loops do not repeat registry checks. Default handles test false and do not add members. Old snapshots remain alive while referenced; repeated rebuilds with leaked contexts therefore retain memory. No implicit disposal/repair hides that ownership error.

## Algorithms

A true tree DFS assigns contiguous half-open intervals [id,end) to every subtree. Sibling names are ordered deterministically before traversal. This differs from simply sorting complete strings: punctuation such as A!x must not split A's subtree. A tag-to-tag hierarchy test is an integer interval check. Sparse set hierarchy lookup uses lower-bound; dense lookup checks masked boundary words and any full words between them. The latter is O(interval word count) worst case, not O(1).

Sparse exact membership is O(log n). Dense exact membership is O(1). Dense union/intersection/difference are O(ceil(U/64)); dense-versus-sparse mutation visits sparse IDs. Sparse union uses sorted merges and reverse in-place merging for aliases. Sparse pair kernels use monotone merge scans, range rejection and bounded searches for highly unequal operands. Dense operations use an allocation-free shared backend: .NET 8+ can select AVX2/ARM64 intrinsics, while Unity/.NET Standard 2.1 uses portable SWAR scalar code. GAMEPLAYTAGS_FORCE_PORTABLE forces that source path in managed benchmark hosts. Hardware and portable results must be reported separately; neither proves native Unity/IL2CPP performance.

Independent UnionInto output is constructed directly. Input aliases route to their proper in-place operations. Sparse output counts first only when its capacity cannot hold the input-count upper bound, so existing actual-result-sized output does not unexpectedly allocate. Integer growth is amortized. Allocating convenience APIs reserve a documented upper bound; this is reported as memory, not silently excluded.

Count includes explicit members only. Tail bits remain zero because no public operation can inject an out-of-universe ID or raw bitmap. Compound operations require identical registry identity before touching output. Mixing storage modes is supported without changing either input's representation.

## Representation selection and memory

For U active tags including implicit ancestors and prepared capacity C, sparse member payload is 4*C bytes and bitmap payload is 8*ceil(U/64). Auto uses this data-buffer crossover at creation; it is a transparent memory policy, not a universal CPU threshold. Explicit Sparse/Dense overrides permit workload tuning. Capacity is bounded by U. A sparse set never converts merely because later insertions cross the initial estimate.

U=10,000 gives a 1,256-byte bitmap, compared with 4,096 bytes for 1,024 sparse IDs. U=65,536 gives an 8,192-byte bitmap. These numbers exclude object headers, registry structures and retained authoring objects. Small sparse sets avoid allocating the entire universe. Dense copies always pay for the universe, even when almost empty. Results and benchmark tables must show this tradeoff.

There is no k-th runtime member indexer: a bitmap does not provide array-style random rank selection for free. Enumeration follows DFS IDs. Use authoring data or an explicitly created name projection for editor/persistence order. This is a breaking API choice, not an unreported optimization of the old indexer.

## Query correctness

All active source nodes are checked before constant folding: undefined types, null payloads/children, unknown names, cycles, paths above 64 levels and more than 65,536 expanded node/tag visits fail. Work accounting counts shared paths, not only distinct DAG nodes. Snapshot binding is explicit.

Any/No tag conditions merge overlapping or adjacent subtree intervals. All keeps the most specific requirements and must retain separate sibling requirements. Any/All group flattening and constants are reduced only after complete validation. No is not flattened as if associative. Packed runtime arrays share previously built DAG nodes and execute with short-circuiting, without runtime graph checks or string matching. Empty source queries remain false; empty All/No conditions are true.

## API migration

Keep serialized m_Name / m_GameplayTags / query fields and existing .meta GUIDs. GameplayTagContainer now supplies source editing, equality and loading methods. Move combat HasTag/HasAny/HasAll, Union/Intersection/Into and mutations to RuntimeTagSet, passing RuntimeTag handles. Convert with definition.ToRuntime(registry, expectedCapacity, storage). Freeze with query.Freeze(registry). Converting two definitions must use the same captured registry.

Regenerate the C# API. Instead of process-static readonly name/runtime fields, instantiate the generated class once with a registry. This prevents static runtime IDs from surviving a registry reset. The instance fields retain scoped handles; copies are small values. Rebuild requires a new context/binding/query preparation. No migration step stores runtime IDs on disk.

The original authoring null/serialization rules remain; runtime operations require non-null set arguments. Into preserves both aliases for exact set operations. Hierarchy FilterInto supports source alias but rejects a separate condition-output alias. Count and returned objects remain correct before return, not on the next read.

## Audit and release

integer_audit.py is the 3.x entry. The 2.x scripts remain historical and cannot be used to approve this branch. The four-way fixture initializes the same registered names and explicit member sets in every process, using common seed/distributions. Alex's unchanged registration context is installed into its manager only during setup because its public initializer consumes assembly attributes. The selected benchmark dependency sources are unmodified; absent app Log/GenericPool services have fail-fast test facades and are not invoked in the timed four operations. Full source snapshots and hashes are archived.

Exact uses a resolved handle. Union allocates independent output. Copy+append and copy+remove perform a real copy then the original public mutation, not a fused replacement. Output membership, explicit count and unchanged inputs are validated before/after samples. Failed reference rows are retained and are ineligible for rank. Candidate failures fail the audit. Main headline is Auto at n=1,024, U=10,000 explicit definitions in BOTH contiguous/scattered distributions; small and larger-universe rows remain visible, as do forced modes. Five fresh-process rounds retain seven raw samples each; ratios are not significance tests on shared runners.

Native Unity 2021.3/Unity 6, asset round-trips, Undo/PlayMode and Android/iOS IL2CPP must be run separately with the same source digest. A managed success or a four-way speed winner does not approve stable release. Initial construction, sparse growth, Freeze, registry memory and enumeration are separate costs; a bitset improves the four requested paths at the price of universe-width work and a different ordered-access API.

## Algorithm sources

- LLVM Programmer's Manual, set-like containers / BitVector: https://www.llvm.org/docs/ProgrammersManual.html
- CP-Algorithms, DFS ancestry via traversal entry/exit: https://cp-algorithms.com/graph/depth-first-search.html
- Roaring official format, why a full adaptive 16-bit block system is not being imported: https://github.com/RoaringBitmap/RoaringFormatSpec

These are design references, not benchmark evidence for this implementation.

## Iterative runtime refactor (experimental branch)

The new APIs are `RuntimeTagSet.FromTags(registry, handles, capacity, storage)` for bulk resolved loading, `set.ToStorage(storage, capacity)` for an explicit independent conversion, and `DifferenceExact` / `DifferenceExactInto` for direct difference output. Bulk loading sorts once when input DFS order is not strictly increasing; ordinary incremental AddTag retains ordered-set semantics. Authoring loading now resolves once and sorts once instead of repeatedly shifting arbitrary DFS IDs. Registry construction reuses the previous definition's prefix path locally; shuffled input retains the canonical dictionary fallback.

Direct difference is a separate contract from copy+remove. Into supports both input aliases. A dense right-output alias reads exclusion words before overwriting them, without scratch allocation. A sparse right-output alias first counts the difference, preserves the old right IDs in its own destination tail, and writes the result prefix. The disjoint regions need |A\B|+|B|=|A union B| capacity, bounded by the registry size. Reserve this capacity to guarantee zero allocation on that alias; otherwise the one destination buffer may grow. This capacity/extra-pass cost must be measured, not hidden. Independent and left-output aliases retain their actual-output-capacity guarantees.

A sparse-condition hierarchy filter can sweep ordered DFS intervals and carry their maximum endpoint. The parent-chain path remains for comparatively large condition sets. All counts are exact before return; no automatic storage conversion, hidden cache, pool, shared mutable output or deferred repair is added.

See `Audit~/Refactor/` for actual C# lifecycle benchmarks and source snapshots. The offline Lifecycle simulator remains historical research and is not evidence that these runtime edits passed native acceptance.

## Locality-compressed bulk preparation

The explicit Compressed mode stores sorted 16-ID blocks, one int per occupied block. Low 16 bits hold member presence; the upper bits hold a biased block key so signed comparisons preserve order across the entire supported U <= 2^20 universe. Empty records are removed, Count is updated eagerly, and enumeration expands masks in increasing ID order. Mixed-layout operators use monotone record readers and a buffered dense writer. Neither a second canonical representation nor a cached summary is retained.

FromTagsForBulk validates and counts already ordered handles, then materializes its chosen representation directly. Unordered handles use the resolve/sort/deduplicate loading fallback. It chooses compression for at least 64 actual members with at least four members per occupied block; otherwise a sufficiently large membership may choose Dense when WordCount <= 2*Count. Those are explicit experimental preparation rules, not a universal performance claim. The ordinary constructor's Auto remains the documented payload-memory policy. UnionForBulk chooses the newly allocated result; no existing set changes its layout.

RecordCapacity is the allocated record budget. ReservedMemberCapacity is the conservative capacity for arbitrary membership placement; Capacity is max(Count, ReservedMemberCapacity). Thus Clear can lower Capacity without releasing memory. EnsureCapacity(n) reserves min(n, ceil(U/16)) records, sufficient for any n-member membership. A compressed right-output difference temporarily retains only actual exclusions A∩B, then reverse-merges A into that same record buffer; a budget of occupied records in A is sufficient. Independent output still grows only when actual emitted records do not fit.

This representation is under measured iteration. Native Unity/IL2CPP execution, end-to-end conversion amortization, tiny-instance object cost and retained competitor tails are separate required gates.

The newer bulk policy compares registry words to occupied records times the dense backend width (4 for AVX2, 2 for ARM64, 1 for the portable scalar backend). This threshold is an experimental workload hint, not a performance theorem. SelectBulkStorage performs no allocation or mutation; a cardinality lower bound can prove dense selection without a shape scan. ToStorageForBulk always allocates an independent result and streams members directly. The benchmark's existing-set workflow explicitly retains the input when the recommendation already matches, and otherwise charges both selection and conversion. It does not confuse that contract with an always-copy API.

Tiny boolean sparse queries use scalar0–2, a short scalar loop below the host Vector<int> width, vector equality through32 when accelerated, and binary search above32. Ordered insertion/lower-bound semantics remain unchanged. The larger scalar0–7 switch was rejected after shuffled heterogeneous actor counts exposed major regressions. The compact kernel also has recorded fixed-count and tail losses; ARM hardware performance remains a separate gate. The portable Vector API can still accelerate on CoreCLR in GAMEPLAYTAGS_FORCE_PORTABLE builds; that symbol controls the explicit dense backend, and a no-hardware process separately exercises the scalar query fallback.
