# Mixed operations: contract and correctness argument

Primary experiment commit: `10d076a635eb46a5df5f0f7eb12279cd8c81e51c`.
This document does not modify or approve the timed implementation.

## Direct Dense result

The added branch is reached only after `UnionCore` handles output aliases, identical inputs, empty inputs, and the all-Dense case. The branch condition then implies exactly one input is Dense and the other has local Micro16 records. Registry identity was already checked at the public boundary. All arrays are private and independently owned; reflection and concurrent mutation are outside the contract.

`Array.Copy` overwrites every destination word, including zeros. Thus no previous output membership survives, and an additional Clear would repeat work. This is valid for both a fresh zero-initialized result and a dirty caller-owned output. It is not an in-place-input case: those cases take the preexisting AppendCore paths.

Initialize the result cardinality from the Dense input. For each local record with member mask m and the corresponding prior destination word b, add popcount(m & ~b_slice), then OR m into that slice. The added bits are exactly the members absent from the current result. Different records have disjoint 16-ID slices, including when several slices occupy one ulong. The final Count is therefore exact without recounting all output words. No lazy cardinality or cached second set is introduced.

Existing same-input/empty behavior, public exceptions, capacity and source preservation remain unchanged. In particular, a foreign registry cannot bypass validation by using the new branch. Existing complete Micro tests plus the new differential tests cover all eight input/output layout combinations, both alias directions, dirty destinations, same input, independent output and actual-result-capacity behavior.

## Word cursor

For a Dense input, at points to the next unread ulong. pending contains only the not-yet-emitted local records of the current word. Skip zero words. For a nonzero pending value, align its least-set-bit position down to a local-record boundary, emit that nonzero mask, then clear exactly that slice from pending. Words and slices are emitted in increasing order, preserving DFS-ID order.

Low is called only on nonzero pending. Tail bits outside Registry.Count are already zero by the set's mutation/build invariants. Count==0 at construction implies there are no member bits, so the cursor starts exhausted. Mutation while enumerating is not supported. Copying the enumerator copies its pending state; copied-cursor and repeated-exhaustion behavior are tested.

The cursor adds one ulong to a temporary Records value, not to the stored collection. Its actual managed sizes are measured in the test process. Zero managed allocation does not mean the extra stack/value-copy cost is free. Dense heavily occupied words and Micro-only enumeration remain adverse performance controls.

## Scope and measurement boundaries

The fixed DirectArray selected by PR14 remains Micro16 with its existing 2^20-ID limit. MICRO_WIDE currently fails in the unchanged uint-only query code of the fixed control too; broadening that unrelated query API is not part of this experiment.

Array/Dense production Runtime already has a copy-then-append mixed Dense output path and is not modified here. The original Runtime/Query suite checks protected production files, not a completed integration of Micro with every production API. The new algorithms are confined to the Micro/Dense experiment family.

Fresh Union returns an independent result. Copy-append/remove performs a real capacity-preserving copy and then the existing modifying operation. Caller-owned output benchmarks include the operation against its retained storage and use sufficient capacity; no new pool or shared scratch exists. Pair lifecycles pay for both input sets, and for both conversions when requested. Independent repeated Unions are not an evolving chain or a trained selector.

The primary CI retains all samples and A/A comparisons. Local SDK8.0.423/runtime8.0.29 checks are separate diagnostics; pinned remote SDK8.0.425 results must not be mixed with those timings. Unity, IL2CPP, Burst and mobile-device acceptance remain separate requirements.
