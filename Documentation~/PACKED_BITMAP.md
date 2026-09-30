# Bitmap-only packed storage: implementation contract

## Base and scope

Base commit: `84729cc3b6e1b4681328806df2d7c8a1d8f568f6` (old Auto). The previous full-universe Dense branch is not the source of RuntimeTagSet. All members are bits, not integer member lists. Omitted empty blocks make this a compressed bitmap, not a fully materialized Dense vector.

## Concrete layout

Each occupied 4096-ID block has a 16-byte directory record `(Key, Offset, Mask)`. Mask identifies nonzero 64-bit member words; `popcount(Mask & (bit - 1))` ranks a logical word in the packed payload. Directory records are sorted by block address. Payload words are sorted within each directory. Directories never refer to zero member words. Count is the popcount of all active words.

One directory record and one word can be stored inline. Larger sets own a directory array and a member-word array; neither is shared. The inline slot is inactive when its corresponding array is used. BufferBytes counts the inline payload even then, not just active words; it excludes object and array headers. A permanently inline prefix layout was tested separately and NOT adopted because of additional query/append regressions.

Extra directory objects per recursive tree level are intentionally avoided. This departs from the previously proposed fully recursive upper bitmap: the current kernel is a simpler sorted directory. No claim of universal superiority is attached to this choice.

Find is binary search over occupied blocks plus rank and a bit test. Query intervals use DFS indices, select relevant directory bits and test masked word ranges. These are not O(1) full-bitmap addressing. For G occupied blocks and W occupied words, fresh results require O(G+W) active storage rather than O(U/64) full-universe member storage. Empty/single-word results avoid extra arrays.

## Algorithms

- Empty/single-word constructors avoid general capacity setup. One-word copies create independent inline data, never a shared mutable result.
- Union sizes output directory masks, then writes backward. This makes superset writes safe for either input alias. Same-mask blocks use contiguous word loops; disjoint blocks use block copies. For differing masks, reverse each directory once and merge low bits with descending word cursors, replacing a highest-bit scan per member word. Duplicate cardinality is computed from AND while writing OR. Allocation, sizing and counting remain inside fresh-operation timing.
- Intersection/difference compact forward with paired word cursors. A nonzero directory intersection is only a candidate: actual word results determine output bits. Output capacity is checked when a write reaches the reserved limit, rather than calling the full growth routine per word.
- Single-word addition/removal shifts packed words and adjusts later directory offsets. This is a real O(G+W) update cost; it is measured, not described as constant time.
- Clear resets active lengths; slots beyond those lengths are never read. These value-type arrays contain no object references.
- Copies duplicate only active layout. Existing target reserves never shrink. New copies do not inherit unused reserve.
- Growth must preserve partially written builder prefixes before logical counts publish. The first CI reproduced failure here; the fix copies existing slots during buffer growth. The failing tests remain.

## Capacity and error boundaries

EnsureCapacity(n) reserves enough directory/word slots for arbitrary placement of n members: min(n,total blocks) directories and min(n,total words) member words. This can use more memory than data-dependent preparation. It is an explicit cost of the zero-allocation placement guarantee. Copies/unions allocate for actual occupied storage instead.

Foreign registry handles/sets are rejected before mutation. Default handles do not match or mutate. All set parameters must be non-null. Public supported aliases remain. No internal per-element identity validation, pool, shared scratch storage, COW, deferred Count or Sparse selector is introduced.

The words beyond active length are not a serialization format. Export names at the persistence boundary. The snapshot is immutable; old scopes remain valid and cannot mix with new ones.

## Tests and measurements

New regressions cover inline high IDs, cross-word/page aliases, output growth, rank/offset/tail invariants, random mutation/repositioning, independent ownership, ranges, pre-reserved mutation and a working positive allocation control. A separate word-layout transition suite covers 0/1/63/64/65/127/128/129/4095/4096/4097/8193 registry sizes, deletion/restoration, full word occupancy, reverse-order insertion and subsequent mutation. The prior query/editor/configuration suite is adapted only in a test copy, with transformations recorded.

Use all original fixed implementations, old Auto, forced Dense and this candidate with identical name arrays and operation sequences. Distinguish allocation-returning operations from prepared Into. Use settled setup/warmup, collect/wait-finalizers/collect uniformly; no allocation subtraction or best-sample selection. Shared runner medians are observations, not statistical or mobile-platform guarantees.

The cursor candidate was tested in isolation before adoption. `packed_acceptance.py` checks its literal shipping source against the tested transformation of fixed commit `057261a` (ignoring only whitespace/comments, and deleting the unused Highest helper), then compiles the actual shipping file. No experimental source is substituted into performance builds.

Historical isolated experiments are reproducible by checking out `3686178`, not by reapplying their source transformations to the already-optimized Runtime. Their original source files, all samples and warnings remain in Actions artifacts. Source-only or experiment-only green jobs are not release acceptance.

Sources motivating the layout, not benchmark evidence for this implementation:
- Hierarchical nonzero bitmap blocks: https://github.com/tower120/hi_sparse_bitset
- Compressed bitmap-indexed nodes: https://api.flutter.dev/flutter/foundation/PersistentHashMap-class.html
- Portable bit counting and scanning: https://graphics.stanford.edu/~seander/bithacks.html
- Data-structure tradeoffs: https://llvm.org/docs/ProgrammersManual.html

No third-party source is copied from these references. Existing project MIT licensing and asset metadata are preserved. Native Unity/IL2CPP execution and stable release approval remain separate and unperformed until real reports exist. All-workload performance leadership has not been established.
