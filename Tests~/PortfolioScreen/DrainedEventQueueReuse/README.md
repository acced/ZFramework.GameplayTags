# Drained event queue reuse: bounded portability screen

Preparation only. This is one unaccepted optimization against exact f7c99546dbf029d70896c0f05e5119b19ffd6d26. A completed job is evidence for review, never acceptance. Qualification remains 0/10. Publication and execution each need their own authorization after exact-seal review.

## Exact source mechanism and scope

Only Enqueue's existing full-array slow branch changes. When next!=0 and next==count, all queued slots have already been cleared and the current notification is held in the dispatcher's local value. Resetting both counters before append preserves the logical pending suffix and public reentrant order. A nonempty suffix, including exactly one pending notification, retains the original resize. No other Runtime method, field, constructor, helper, attribute or layout changes. No general compaction.

Multicast delegate snapshots, later handlers, enqueue-then-throw recovery, hierarchy fanout, nested batching/discard and subsequent reuse remain covered. Private event indices, capacity and identity intentionally differ. Avoiding resize also changes allocation-failure/GC behavior; full graph/resource-failure equivalence is not claimed. Exact original public callback-driven behavior is the comparator; a manual nonreentrant outer loop changes notification order and is not equivalent.

## Fixed sequence per backend

Exactly two builds, then two untimed semantic targets, then 40 fresh cold targets, then eight fresh warm processes containing four rows each. Total measured rows: 40 cold + 32 warm. No pilot, adaptation, retries, replacement samples or extra builds. Total coordinator deadline 300 seconds, including archive materialization, builds, validation and finalization. The selected deadline is exactly 300 seconds; no extension or alternate budget.

Both .NET 8.0.31 and standalone Mono 6.8.0.105 use the same frozen map and AB/BA/BA/AB pair orders. Actual target version/header and 8-byte pointer size are validated, not inferred from installation. SDK 8.0.425/reference pack 8.0.31 are required; Mono uses the established net472 reference set and System.Memory/Buffers/Unsafe/Numerics.Vectors dependencies. Unity Editor/Player and IL2CPP are not covered.

Run interface: run.py --backend {net, mono} --dotnet <host> --mono <host> --nuget <packages> --git-root <repository> --out <new-public-directory> --work <new-private-directory> --expected-seal <frozen.json SHA256> [--deadline-seconds 300]. New public/private directories must be separate, nonnested and outside this package.

The original Runtime is materialized from a safe git archive of exact f7 and authenticated against all 44 file hashes. Candidate overlays only GameplayTagCountContainer.cs and rechecks the complete 44-file tree. Root Runtime is not edited. Before/after every subprocess, frozen package/source/tool/build inputs remain checked; the count map is part of that frozen inventory. Process names/attempt budgets prevent extra runs. Signal-safe process-group cancellation and reap retain exit status even on failure.

## Semantic and metadata gate

The unchanged core suite executes 7,595,855 assertions. The focused fixture retains all finite chains 7/8/9/17/1025, captured multicast handlers, subscription edits, throw fanout 1/10 and recovery, exactly-one-pending fallback, nested active/inactive discard, three-level hierarchy fanout, and ordinary add/remove/bulk/Clear call contexts. Complete stdout and logical-state snapshot bytes compare within each backend. Every consumed/inactive event cell is checked default; raw queue capacity/indices are kept separate and logical pending order is exact.

MetadataGate.cs is the already validated R2 collector, unchanged. The portable comparator retains every existing metadata/IL/queue check through the local comparator's native boundary: sole Enqueue body allowance, no existing definition-token movement, unchanged unrelated normalized/raw IL, identical local/exception-region shape, identical resize/notification-constructor calls, exactly the intended event-count/next stores, and no unexpected allocation/boxing/indirect call/static store. The literal representation declaration remains required. Actual managed sizeof of GameplayTag and DeferredTagChangedDelegate is measured with DynamicMethod only after untimed semantics; both layouts must be positive, same within the backend and have 8-byte pointers. No .NET array-size assumption enters Mono counters.

This portable stage has no native capture. Its omission is explicit, not a waived new native gate. The prior complete local natural captures remain bound and described in LOCAL_EVIDENCE.md; no natural-inlining or native-code portability claim is made. Combining fixtures into one assembly changes code context; new .NET measurements are a separate replication and do not replace local observations. Collector scope still excludes compiler-global PrivateImplementationDetails, PE RVAs/MVID and absent conditional configurations.

Any semantic/metadata/layout failure stops before cold timing. Both semantic targets are required for success; an earlier failed semantic target fails closed. Complete all 40 scheduled cold observations before the cold state/allocation gate. Invalid/missing state or any paired allocation increase blocks every warm target; cold latency is preserved without an automatic pass or rejection claim.

## Cold boundary: five unchanged shapes

Each shape has four fresh original/candidate pairs: chain-1, chain-8, chain-9, chain-1025, full-with-pending. Holder, registry, tags, callback, trace and source setup stay outside counters; there is no prior target mutation or target-state reflection. The common NoInlining first AddTag boundary includes the complete synchronous reentrant chain and observer work. Numeric output is flushed before reflection/semantic validation, even for catchable operation failure. No forced GC.

For full-with-pending, the seventh of eight hierarchy callbacks adds Z while the eighth is still pending, requiring the original full-array resize on both variants. Capacity expectations are original 8/8/16/2048/16 and candidate 8/8/8/8/16. Counts, exact event sequence, callbacks, immutable source, storage tails and logical queue state compare completely. Raw capacity is structural state, not total retained-heap bytes. Allocated-byte amounts are measured per backend; no fixed .NET byte count is imposed on Mono.

## Warm boundary: already-grown original

Each measured process traverses single, chain9, chain1025, pending after one registry initialization. It uses four separate reused holders. Each gets 128 complete setup cycles using the same public callback workflow, so original naturally retains 8/16/2048/16 event slots while candidate retains 8/8/8/16. No fields are rewritten to force capacity.

A complete chain cycle adds through counts 1..N, then RemoveTag(A, N) emits its count 0 restoration notification. Events per cycle are 2, 10, 1026. Pending performs nine add notifications, then eight leaf-removal notifications and one Z-removal notification: 18 total. Its warm capacity is 16 on both; it is an ordering/workflow control, not repeated growth. Every cycle finishes empty without accumulating counts. All restoration callbacks and observer/order/checksum work remain inside the common NoInlining cycle/batch, without subtraction. Reflection, source snapshots, oracle preparation and pre/post state encoding are outside counters.

Every callback is checked against a prepared exact sequence. Full storage/callback/source state, final-cycle trace, queue/batch/dispatch scalars and retained array identities compare pre/post and across variants. Physical event capacity is recorded separately; all retained event cells must be default. Maximum explicit counts remain 1/9/1025/1. The fixed existing caps keep callback totals below Int32.MaxValue and long checksums below Int64.MaxValue. TagStorage has no version field; no invented version check is substituted.

The only warm fixture portability adapter replaces direct allocation-counter calls with the established cached Func<long> delegate bound to the actual GC API. Initialization and counter call positions stay outside the interval, identical for original/candidate. Core, focused test bodies and cold fixture remain unchanged; the focused success wording simply says no portable native capture. The entrypoint rejects all pilot modes.

## Original-only count derivation

count-map.json retains all four original measured ticks, frequency, original count and source hashes. Shared new counts are 3,655,224; 906,602; 9,309; 547,456. Formula uses integer ceiling:

max(old count, ceil(old count * 250 * frequency / (1000 * minimum original ticks))).

No candidate value enters count selection. Counts remain within original fixed caps. 250 ms is a target, not a guarantee on another machine/runtime. Keep every short batch, GC/byte observation and adverse value; no recalibration or replacement.

## Public reports and failures

Only the eight explicit upload-allowlist.txt reports may be uploaded: status, provenance, configuration, correctness, count map, cold numeric rows, warm numeric rows, summary. Public values are fixed labels, validated runtime versions, booleans, numbers and hashes. No raw stdout/stderr, command arguments, absolute paths, arbitrary exceptions, environment dumps, binaries, metadata or snapshot files are uploaded. R2 diagnostic projection retains attempted/exit status, fixed pass-marker flags, known exception/frame codes and artifact lengths/hashes even when semantic execution fails. Numerical rows are projected independently from postmeasurement semantic rows; incomplete records remain invalid rather than silently disappearing.

The inherited tool helper hashes .NET compiler/runtime/reference trees, Mono host and the four copied package DLLs. It does not enumerate every installed Mono framework/GAC/native dependency. This is an input-integrity check, not complete machine/environment attestation. Raw private files remain private.

Incomplete semantic evidence or any warm allocation increase closes. A row slower in at least three of four warm pairs closes for repeated observed cost under the predetermined descriptive rule. Other outcomes request review only; no automatic latency pass or statistical/causal claim. Preserve all earlier cold uncertainty, including the 3/4 adverse shapes and large individual ratios. Memory gains and warm signs cannot cancel cold costs. No automatic next stage, source variant, publication or qualification.
