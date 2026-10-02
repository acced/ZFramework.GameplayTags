# Empty-source boundary and private copy call isolation

Parent PR25 `01bf2353401adb1877e33a585c9b3074339bde4a`. This is a new experiment, not a relabeled old acceptance run. Production Runtime/Editor/Samples/Tests/package stay unchanged. Same-name generated implementations must compile separately.

## Two independent factors, six fixed builds

- control: exact defective PR24 copy baseline, retained only to reproduce fault and quantify old normal work.
- eager: exact correct PR25 eager copy baseline.
- guarded: exact PR25 capacity-proof baseline.
- isolated: guarded plus NoInlining on CopyDenseToMicro, no body changes.
- empty: guarded plus source.Count==0 -> Clear/return at the start of private CopyCore.
- boundary: both factors together.

The first three SHA256s are pinned to the downloaded PR25 measured artifacts. All candidates use the same inputs, capacity/growth ladder, ownership and eager observable Count. We do not use old timings or choose a winner per row. No new set/iterator fields, caches, scratch arrays, representation selector, user-desktop dependency or numeric threshold.

## Correctness and scope

Public Require and self-copy checks remain before the private core. An empty source from another registry must still throw without modifying the target. Empty source means the final set is empty; Clear preserves capacity and clears Dense storage only when existing Count requires it. The early return is internal; the source may not mutate concurrently. No attempt to repair invalid Count or inspect stale unused array slots.

Nonempty paths retain guarded's capacity proof `EntryCapacity >= min(source.Count, MaxEntries)` and eager Count commits on possible growth. All three new builds repair the same specified escaping array-allocation failures as guarded. Dirty replacement may leave a valid prefix on failure; no strong rollback, async interruption, actual host OOM or generic mixed-union exception-safety guarantee is introduced.

NoInlining is a single-variable experiment in caller/code-layout interaction, not an assertion that inlining caused every old regression, and not a blanket library-wide policy. An explicit call may itself cost more. The empty test adds a branch to nonempty calls, whose cost is retained in the complete matrix.

## Tests and measurement

Original mixed/local/bulk masks, aliases, capacity and allocation tests; original full Micro and protected Runtime suites; 3931 inherited actual allocation-site fault injections plus 3873 normal fault-fixture configurations per build. Controls must reproduce the old defect; all other variants must pass. Boundary runs also execute with hardware intrinsics disabled and as a separate netstandard2.1 library hosted by net8.

Add96 correctness configurations (0/1/17/257-ID registries, both source/target layouts, reservations, dirty states). Check foreign empty registry, null input, self-copy, alias shortcut, dirty final word, capacity preservation and reuse.

Per build: original2520public-operation+192copy-contract cases, plus96new empty-source cases. The new empty cases cover4domains,4layout pairs, clean/dirty output, CopyFrom/Clear+Append/same-sourceUnionInto. Dirty setup AddTag is INCLUDED in each timed iteration, not provided free. Prepared paths must allocate0B. We retain all slow samples and the correct eager baseline rather than allowing faulty code to win acceptance.

Six variants each include same-binary A/A;7samples x3 shuffled processes, SDK8.0.425, x64/ARM64, tiering/ReadyToRunoff, singleCPUaffinity, original0.5mscalibration/8MiBcap. Actual CopyCore/CopyDenseToMicro/CopyDenseGrowing/CopyFrom/Clear and caller disassembly is requested; absence or inlining must be reported, not replaced by unrelated assembly.

Reviewer reconstructs source/tree/generated code/binary hashes, all raw medians, allocation and capacity contracts and A/A. Collect redownloads both original ZIPs and repeats their archived reviewers with identical structured outputs. Three-round5% is a screening rule, NOT a confidence interval.

```sh
python3 'Audit~/CopyBoundary/run.py' --output artifacts/results --rounds 3
python3 'Audit~/CopyBoundary/review.py' --root artifacts/results
```

Full-history checkout required for baseline regeneration. No native Unity/IL2CPP/Burst/phone, cold multi-actor or long-lived peak-memory acceptance is claimed. No four-metric universal win, merge or release is authorized by merely compiling this experiment.
