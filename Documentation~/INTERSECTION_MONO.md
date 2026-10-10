# General intersection: allocation-free warm paths and optional sort-free workspace

This change is based on `21e2d93d85380fb9267284c5469fb21d31d21a78`.
The reported Unity 6000.3.14f1 segment `Array.Sort<int>` allocation is the reason
for removing that call from runtime bulk construction. No statement here treats
.NET or standalone Mono as a measurement of Unity's specific Mono/BCL fork.

## Default API (no migration)

```csharp
GameplayTagContainer.Intersection(output, left, right);
```

A reused non-aliased output gathers selected IDs during its first membership pass.
It also detects strictly increasing selected IDs and skips sorting in that case.
Otherwise an integer-only introsort performs direct comparisons, insertion sort
for small partitions and a heap fallback, without comparer/delegate/heap scratch.

Fresh outputs retain the count-first path, allocating for selected cardinality
instead of the entire smaller input. Output aliases retain a safe two-pass fallback
to preserve full-match/copy-minus-missing shortcuts before overwriting source
indices. Neither fallback calls the BCL sorter.

The sorted builder stores the entering explicit-prefix count in each temporary
node. On subtree exit, `processed - entering` is its final contribution count.
It does not run a separate bottom-up count pass. Only actual ancestor paths are
created; subtree intervals are exit tests, never ranges of descendants to include.
A reused entry buffer is filled directly, growing only when needed. Fresh empty
entry buffers still pre-count the closure to reduce construction allocations.
The hash index is built once after the closure is complete.

## Explicit workspace API

```csharp
// After registry initialization. Reserve selected tag count, not closure count.
var workspace = new GameplayTagIntersectionWorkspace(1024);
var output = new GameplayTagContainer();
// Prewarm with a representative maximum closure (or output.EnsureCapacity(...)).
GameplayTagContainer.Intersection(output, left, right, workspace);

// Repeated calls, with capacities already sufficient:
GameplayTagContainer.Intersection(output, left, right, workspace);
// In-place alias and allocating-result overloads are also available:
output.IntersectWith(right, workspace);
var independentResult = GameplayTagContainer.Intersection(left, right, workspace);
```

Workspace selection is single-pass even for aliases, using its own selected-ID
buffer. General results are constructed without sorting. Each selected ID adds
only the missing part of its parent path, stopping at the first existing node.
Sparse-set validation checks a retained position against this build's live prefix
and matching ID, so stale entries need neither clearing nor generation stamps.

Temporary parent links and unfinished-direct-child counts live in the existing
four-integer `Entry` fields. Each result tree edge contributes once; last-child
completion can finish its parent immediately, with no heap queue or recursion.
Temporary state is restored before publishing the result and building its index.

The workspace holds one `int[registeredTagCount + 1]` plus a geometrically sized
selected-ID buffer. The zero-argument constructor allocates neither buffer nor
initializes the registry; `EnsureCapacity(explicitCapacity)` reserves after startup.
It retains roughly `4 * (N + 1 + selectedCapacity)` bytes plus object/array headers.
A million registered tags costs about 4 MB for its sparse positions. This cost is
per caller-owned workspace, NOT per ordinary container. There is no global static
scratch and no registry-sized scan/clear per call. Concurrent operations must own
different workspaces; input containers must remain immutable during the call.

## Contracts and tradeoffs

Both APIs return independent mutable sets with full ancestor counts. Counted
sources contribute existence, not multiplicity; sources are never modified unless
explicitly passed as the output. Enumeration is still unordered. Copy, ordinary
query/add/remove and the normal-set union seed logic are not changed.

Warm, sufficient-capacity calls contain no intentional managed allocations. First
use, explicit/entry/index growth, workspace reservation, new result creation and
name serialization can allocate. A tiny first result does not promise enough
capacity for a later larger result. Capacity growth preserves the current build's
written prefix and recreates the active hash prefix; old tail buckets are ignored.

Default expected work is `O(S + K log K + U)` (ordered results skip sorting); the
workspace path is expected `O(S + U)` under normal hashing, with `S` smaller input
explicit count, `K` selected explicit count and `U` full result closure. Neither is
a worst-case guarantee for hashing or capacity growth. The workspace path's extra
random loads, retained memory and final index cost can lose on small or already
ordered sets. It is opt-in rather than automatically imposed on every operation.

## Validation and reproduction

Run `Tests~/Intersection/GameplayTags.Intersection.Tests.csproj` for the direct
C# implementation checks. It compares name-derived closure and contribution counts,
then exercises mutation and complete deletion, with randomized layouts, counted
sources, both aliases, workspace cross-output reuse and 3000-level mixed ancestors.
Integer sorting is tested on subranges including sentinels and adversarial patterns.

CI runs ordinary regressions, direct allocation tests on .NET and standalone Mono,
and AB/BA benchmarks at the fixed baseline and current revision. Every sample,
allocation delta and median is retained as the `intersection-runtime-evidence`
artifact and in job logs. No timing threshold controls correctness.
The larger-registry diagnostic fixes selected workload while adding 50,000 unused
tags; it is a sensitivity check, not evidence for all registry sizes or platforms.

For the actual Unity target, use `Samples~/IntersectionDiagnostics` and the user's
original benchmark. The sample reports warm per-thread bytes and creates Profiler
markers for GC.Alloc event/call-stack diagnosis. A compile-only stub check is NOT a
Unity Editor, Mono player, IL2CPP or ARM run. Allocation counters count bytes, not
events; event counts must be inspected in the target Profiler.

Unity reference: https://docs.unity3d.com/6000.3/Documentation/Manual/performance-track-garbage-collection.html
