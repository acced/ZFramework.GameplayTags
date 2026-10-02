# Preparation v2: strict builders and explicit conversion

This is an isolated, measured candidate based on PR12 (`6c7e18dadf6b384046249ba951227109e8d36fde`, documentation head `ea8f6921d8bd1f9d4bf144015ca8eee4107e4f5c`). No production Runtime, Editor, Query, serialization or ordinary set-operation method is changed.

## What is being tested

1. Empty input follows the existing empty constructor/Clear path. It still validates registry/options for new construction.
2. One admitted handle writes the single value directly. Micro already owns one inline record; it does not need capacity growth for that case.
3. Fresh sorted construction validates owner and strict ordering while writing its own unpublished storage. No result is returned on failure. No mutable caller buffer is retained. This removes a second pass, not admission checks.
4. Reset keeps its stronger existing-output guarantee: validate the complete input before any clear, write or capacity growth. Late invalid input leaves membership and capacity unchanged.
5. Unordered Dense validates handles and sets bits directly, eagerly counting only first-time bits. No sorting or temporary ID array is needed.
6. Unordered Array stages IDs in its final independently owned buffer, sorts only the active raw prefix and compacts it in place. When raw duplicate input exceeds registry size, the old capacity contract forces a separate workspace; that fallback is retained and tested.
7. Micro unordered input still sorts an owned ID workspace, except empty/singleton. This change does not speculate about a new packed sorting representation.
8. `CopyAsStorage(target, capacity=0)` creates an independent result in an explicit target; Auto is rejected. Reservation is `min(registry.Count, max(Count, capacity))`, NOT source Capacity. The normal clone contract is unchanged. Conversion admission is at the explicit call; registry ownership and valid stored IDs are already established internally.

## Scope and limitations

Array/Dense and Micro/Dense are two independently compiled families. This is not yet a unified Array/Micro/Dense class, cross-family conversion, fitted selector, Run container or production migration. Micro16 keeps its existing 2^20-ID limitation, while Array/Dense do not acquire it.

One live member representation per instance; zero added instance fields; no pool, COW, borrowed buffer, runtime statistics, implicit conversion, lazy cardinality or scoped registry restriction. All methods are single-owner and disallow concurrent input mutation. New APIs require explicit adoption; they do not speed up unchanged callers by themselves.

## Validation movement audit

| Previous protection/work | New guarantee | Bypass risk | Regression |
|---|---|---|---|
| Validate fresh sorted input before allocation and then read again | Validate each owner/order while filling an unpublished object | No callback or result publication until success; caller concurrent mutation remains unsupported | Late foreign/default/duplicate throws; caller/source unchanged |
| Full order loop for 0/1 elements | Empty and singleton ordering/uniqueness is vacuous; singleton owner check remains | Registry-issued handles establish valid IDs | All modes, empty registry, singleton final word, invalid singleton |
| EnsureCapacity for Micro singleton | Every valid Micro object has at least one inline record | Private fields; no public buffer mutation | Dirty array and inline resets, copied and emptied objects |
| Sort all unordered representations | Dense represents duplicates by the same bit and counts first insertion | Each input handle still checked; eager final Count | Duplicate streams, all 16-bit masks, mixed word edges |
| Extra Array sorting workspace | Final buffer is private and large enough for raw input | If raw length exceeds final capacity, use explicit workspace fallback | Oversized duplicate stream, spare capacity, caller array unchanged |
| Revalidate source identity in conversion loops | Target is constructed with this exact Registry | No foreign destination is accepted by CopyAsStorage | Independent mutation both ways, invalid target/capacity, portable execution |

Fresh error paths can now allocate the result buffer before detecting an invalid late element. This is a deliberate error-path cost, not a weakened success contract. Error allocation and missing cold-initialization/device measurements must be disclosed.

## Reproduction

Run `python3 'Audit~/PreparationV2/run.py' --output artifacts/results --rounds 3` after `git archive --format=zip HEAD > artifacts/source.zip`; then `python3 'Audit~/PreparationV2/review.py' --root artifacts`. SDK 8.0.425 is pinned with global.json in the build root and explicitly checked.

The construction matrix uses two full registries, three localities, two seeds and sizes 0/1/2/8/128/4096. PR12 bulk, v2 bulk and pre-sized repeated Add are all rerun in this same build protocol. Each version/layout has same-binary A/A processes; timings are never replaced by earlier PR numbers.

Conversion covers both directions and same-kind copying in each family, independent output, prepared CopyFrom, and conversion plus 1/16 unchanged independent Unions. Peer preparation is outside timing equally for both strategies. No 'keep current' lifecycle winner or automatic policy is inferred from the isolated conversion curves.

Three shuffled rounds, seven samples per row, same calibration/GC settling, per-sample allocation and collection counters, no sample deletion. The reviewer reconstructs every aggregate, verifies source Git tree and archived binary hashes, and records both favorable and unfavorable rows. The collection job re-downloads both CPU archives and repeats the verification.

Linux .NET x64/ARM64 is not Unity 2021.3, Mono, IL2CPP, Burst or physical mobile acceptance. Status at submission: implementation added; compilation/tests/performance not yet reviewed. No universal performance claim.
