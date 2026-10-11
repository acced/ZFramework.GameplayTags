# Closed-state, storage-sort adaptive Union: semantic and storage proof

Scope: successfully completed, ordinary nonconcurrent calls through the existing APIs, including derived/proxy views and publicly reachable legacy counted/overflow states. No overflow policy, callback contract, source dispatch, factory, output API or storage field is changed. Rejected4df/3af evidence is not treated as accepted behavior.

## Actual difference from rejected versions

4df and3af have byte-identical `AddUnion` and ordered-sweep bodies.3af additionally changed tiny CopyFrom and workspace contribution loops.4df already put sorting/prefix propagation in a separate helper, but initialized its division/revolution budget in every >8-explicit AddUnion.

The new AddUnion executes the original ingestion and first FIFO revolution, then hands off only unfinished work to a NoInlining remainder helper. The helper owns adaptive budgeting, validation and sweep dispatch. Let Q be the actual rented queue length and R=max(2,source.Count/Q). First revolution plus R−1 helper revolutions processes exactly R·Q entries before the first sweep opportunity, matching4df. Each wrap decision follows both processing the last popped entry and queuing its parent. If no work remains, no handoff/sweep occurs. No particular ArrayPool bucket implementation is assumed.

At each completed revolution, head is zero and live queue entries occupy queue[0..queued), including a completely full queue. Tail is passed unchanged. Thus no rotation/copy is needed. Source storage is not mutated by the original callers, so reading source.Count at handoff gives the same budget. The unchanged <=8 path and ingestion prefix are checked byte-for-byte in source-proof.json.

## Newly discovered old-sweep failure

The existing sweep's premise, 'the queue owns every unfinished contribution', is false for some supported derived normal views. Such a view can expose counted storage. CopyFrom preserves its high ExplicitCount bits; FIFO can then stop propagation at an unqueued ancestor, whereas the old sweep traverses through it.

The public counterexample in PublicCounterexample.cs uses only valid registered IDs and positive amount3, no overflow or private-state mutation. Its root ends Count3/ExplicitCount129 on f7/f53 but62/11 on4df/3af. Three normal leaf removals make the root absent on accepted sources but still present on rejected adaptive sources. Both local runtimes reproduce this. The candidate's guarded fallback matches accepted output byte-for-byte.

## Newly discovered stale explicit-slot failure

The first ownership/mass gate was also insufficient. A positive public wrap/re-add/remove/drain history can leave a stale explicit slot beyond the live dense prefix. When ordered propagation inserts missing implicit ancestors in a different dense order, that stale slot can become a different explicit enumerated name. StalePublic.cs preserves a 1,023-filler public history: accepted f7/f53 enumerate hierarchy level40, while the failed storage-sort candidate enumerates level943, on both backends. The prior blanket explicit-order proof is withdrawn for that candidate.

The revised gate checks both directions before any markers: every explicit slot must reference a live low-bit-explicit entry whose ExplicitIndex equals that slot, and every live low-bit-explicit entry must reference an in-range explicit slot pointing back to it. Duplicate slots, stale/out-of-live slots, missing reverse mappings, and slots pointing at implicit entries reject to FIFO. No assumption that a normal-container view exposes normalized storage is used.

## Public continuation failure and complete-closure admission

The mapping-only candidate remains insufficient even when its immediate explicit sequence agrees. ContinuationPublic.cs builds a healthy output with partial-closure input, then copies a legacy stale-slot source into that reused output. CopyFrom leaves old dense tail entries intact; the later stale slot exposes hierarchy level40 on f7/f53 versus943 on the mapping candidate. This public successful continuation is reproduced on both backends. The prior proof based only on immediate names/counts is withdrawn.

The revised gate additionally checks the direct parent of every live entry. Any nonzero missing parent permanently rejects to FIFO. With valid immutable registry hierarchy, direct-parent closure implies all ancestors of every queued entry are already live. The sweep therefore cannot insert any entry. This is a generic storage-closure predicate; no depth, benchmark name or special fixture ID is tested.

## Sufficient gate conditions

Immediately before sorting, gate success establishes:

1. ExplicitIndices[0..ExplicitCount) and live entries with the explicit low bit set are a bijection in both directions. All live ExplicitIndex values were nonnegative before marking.
2. Every live queue slot contains a valid, unique dense index.
3. Each queued entry has positive unsigned pending weight, extracted as `(uint)ExplicitCount >> 1`.
4. Every entry with nonzero pending high bits is represented exactly once in the queue.
5. Sum of all queued pending weights is at most int.MaxValue, checked with a remaining int budget before subtraction.
6. Every live entry's nonzero direct parent is already present, so no propagation can insert a missing ancestor.

The total-mass bound is needed for a self-contained proof. An abstract pair of queued sibling weights2^30 can otherwise merge to zero modulo2^31 in FIFO, whereas an unrestricted sweep adds2^31 at the parent. This is preserved as a synthetic required rejection fixture, not misrepresented as a separately demonstrated public history.

Given the gate conditions, FIFO only moves one positive weight to its parent, merges it with an existing queued weight, or drops it at a root. Total in-flight mass never increases. Every merge is bounded by initial total<=int.MaxValue, so pending arithmetic cannot wrap to zero, cancel, or create duplicate enqueueing. The queue remains the sole owner of pending fields until completion.

The prefix sweep therefore adds the same sum of descendant pending weights to every ancestor and clears the same pending fields. Preorder IDs delimit subtree intervals; subtracting prefix upon entering a path and adding it when leaving accumulates exactly that subtree's queued mass. Arbitrary preexisting signed Count values remain valid under the existing unchecked arithmetic: both methods add identical values modulo2^32. Neither method removes entries or branches on Count.

## Exact restoration and failure behavior

Before writing, the gate validates every explicit slot and every live entry in both mapping directions and scans for negative ExplicitIndex. An invalid mapping returns before any writes. The gate does not require positive Count values or a normalized seed type. For a queued slot, it validates index bounds, pending weight, marker sign and remaining budget before complementing ExplicitIndex. Complement maps a nonnegative int reversibly to a negative int. A duplicate is detected before its second complement.

The marked-prefix count includes only unique, successfully marked slots. The finally block complements exactly that prefix, including failures during partial marking or the subsequent coverage scan. Queue contents, other Entry fields, Count, ExplicitCount, ExplicitIndices, buckets, active mask and capacities are never modified by validation. No allocations, resizing or callbacks occur while markers or local entry references exist. Targeted tests compare all arrays, scalar fields, queue values and array identities before/after every true/false gate result.

When validation returns false, revolutions stays zero. The remainder helper resumes the original FIFO with unchanged queue order/tail and does not try to reorder later. Existing pending cancellation or unusual seed bits are therefore left to exactly the accepted algorithm. As with existing mutating APIs, asynchronous process/thread interruption and concurrent external mutation are not new atomicity guarantees.

## Conservative limits

Ordinary bulk builders can leave implicit-root ExplicitIndex=-1 and parent links. The gate explicitly rejects negatives rather than assuming a universal invariant. Such states may remain quadratic; this is not a universal complexity fix. Any legacy mass/ownership anomaly also retains FIFO.

On an eligible path, the cold validation performs O(current destination Count + destination ExplicitCount + queued) scalar checks plus one parent-index lookup per live entry, followed by in-place queue sorting and a weighted ancestor sweep. A very large disjoint destination can make the validation scan material. This preparation does not claim all shapes become faster. Ordinary calls finishing before the first physical queue revolution pay neither the division nor validation scan. Full fixed timing gates remain necessary.

For admitted states, the full owned final storage state is equal to FIFO, not merely the unordered tag set. The gate completes after source ingestion, before ordering. Every queued ancestor is already present by transitive direct-parent closure, and every such ID is found at its original dense index by the existing coherent-index invariant. Both algorithms update only Count and queued ExplicitCount fields. Weighted sums are equal modulo2^32 and the same high bits clear; all other entries are unchanged. Entry order/IDs/ExplicitIndex, global counts, ExplicitIndices, buckets/active mask, full array capacities and stale retained tails are unchanged. Marker restoration finishes before either algorithm resumes. No growth, copying, index rebuilding or explicit-index insertion occurs in the admitted sweep.

Consequently later public operations start with identical owned storage state, including the stale tails that legacy Copy can expose. Supported future overflow, re-add/remove, copy, clear/regrow and proxy continuations need no additional equivalence assumption beyond the same inputs and existing API contract. If closure or any other gate condition fails, the call remains on original FIFO and also preserves full state. As usual, raw unspecified ArrayPool element contents are not part of container-owned state: the same rented array and request are used, but scratch contents can differ after return.

Partial-closure inputs are conservatively excluded even if a particular reordered result would have been harmless. This retains the demonstrated ancestor-first all-explicit chain case: ingestion has already inserted the entire hierarchy. It does not claim to accelerate every deep input or every otherwise valid normal/count/proxy state.

## Allocation, capacity and pool requests

The same single Rent(source.ExplicitCount) and finally Return(clearArray:false) surround the >8 path. Gate and sweep reuse the rented queue, entry fields and stack locals. Sorting now uses private in-place heap methods on the already-live TagStorage type, with O(q log q) work and O(1) stack. It sorts the same queue prefix ascending and leaves the tail unchanged. No comparer, delegate, closure, wrapper, managed scratch, new type or retained field is introduced.

The previous Int32Sort-calling candidate failed unchanged Mono first-use cold allocation by48B despite identical warm and owned capacity observations. Separate preserved diagnostics associated the increase with first touching the Int32Sort path/type; indexed gate locals did not remove it. No allocated object/stack was identified because no installed profiler was available. Diagnostic prewarming was not an acceptable repair and is not used here. The private heap is an implementation change, not code preparation; this exact revision must pass the original unchanged fresh-process cold matrix independently.

Ingestion and explicit-index growth are unchanged. An admitted sweep inserts nothing. Ingestion and any prior FIFO revolutions are exact originals, and all owned capacities and active-index state remain unchanged through the sweep. Therefore the full allocation/growth sequence before handoff and final owned arrays/capacities match FIFO. Existing factory seeding, CopyFrom compaction and final Copy are unchanged.

Equal request counts/bounds do not establish global pool residency or trim behavior. Actual pool observations and operation allocation counters are separate fresh-process diagnostics. No Mono absence-of-events inference is permitted. Code/metadata growth is separate from owned object fields and arrays; raw IL sizes are recorded, not converted into a performance claim.

## Supported-state boundary and remaining empirical gates

The mapping, complete-closure, ownership and mass gate conservatively admits a subset of successful public states. It does not attempt to repair legacy storage, infer validity from CLR type, or redefine overflow. General array-length/Count ownership is the existing storage invariant maintained by public methods, rather than an arbitrary private-field-corruption contract. Registered-ID and immutable-registry requirements remain those of existing operations. All mapping, negative-link, ownership, zero-pending, duplicate-queue and excess-mass rejections are permanent FIFO fallback for the call.

Neither equal final capacities nor a source-level allocation argument is acceptance evidence by itself. The frozen original cold matrix, gate restoration, both public semantic counterexamples, getter/overflow/state suites, Evolution and Main/Unity must finish on this exact source. No timing has been performed, no claim of universal depth complexity improvement is made, and local Mono6.12 does not stand in for actual CI Mono6.8.

The ordered sweep resolves queued IDs through Find, while FIFO carries dense indices. This requires the existing unique-live-ID and coherent-index invariant: Find(Entries[i].Id)==i for live entries. Successfully completed valid registered-tag histories maintain it; arbitrary private corruption and failed invalid-None workspace mutations are not covered as normalized states. This is a distinct premise from the new explicit mapping gate, not something the gate proves. Source alias handling and getter capture remain accepted f53 behavior: callers seed a distinct result before AddUnion or use its existing alias fallback, and source ingestion has completed before validation. No input getter or callback is invoked during gate markers or sweep.

## Managed code footprint and scope of measurement

Raw nongeneric IL is AddUnion486 bytes at f53 versus506 here; rejected4df used527. The new cold methods are DrainUnionRemainder188, CanOrderUnionContributions449, AddOrderedContributions256, SortUnionQueue61 and SiftUnionQueue58 bytes. This is1,032 added IL bytes including the20-byte AddUnion delta. No preexisting fields, storage types, managed scratch buffers or mutable static caches are added. Native code size, runtime metadata residency and global pool residency are not inferred from IL or allocation counters. No native timing or speed claim is made.
