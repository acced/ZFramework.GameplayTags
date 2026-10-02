# Copy count commit points — isolated correctness/cost experiment

Parent c4794476af257f9230d6908fa48bf5b0e88ac5ea (PR24). No production promotion. Controls regenerate the exact PR24 batch32 core; only private CopyDenseToMicro differs between the three builds. No new append algorithm, representation, instance/iterator field, capacity policy, pool, scratch allocation, catch/default-return or public API.

## Alternatives

control: inherited copy clears target, appends ascending records, and sets Count only after success. Injected growth failure can expose partial members with Count0.

eager: immediately count each successfully published record. At every collection-buffer growth site, member count is exact. Extra Pop per source record is part of the timed operation.

checkpoint: remember the number of records already counted, and count the newly written prefix only when the next record requires growth. Update Count BEFORE ReserveEntries can fail. No record is counted twice. After successful return, unchanged CopyCore assigns the known source Count. Capacity-sufficient path does not need prefix popcounts, but pays an additional capacity comparison and a local index. This is not an exception-time repair scan. Prefix count may be temporarily stale between allocation checkpoints; no concurrent mutation, callbacks, reentry or observations inside this private operation are supported. Only normal completion and escaping collection-buffer allocation failures are guaranteed. Do not generalize to arbitrary asynchronous CLR failure or stack exhaustion.

Both fixes retain the same one-pass Dense traversal, ascending record writes and original growth allocations/capacity. Copy replacement has the BASIC guarantee, not transactional rollback: an old target can be cleared and replaced by a valid prefix of source before failure. Source remains unchanged. Homogeneous Micro copy still reserves before replacing and is included as a control. Unrelated mixed-union OOM and other old experiment failures are outside this fix.

## Proof

Clear sets used/count0. Each successful PutRecord publishes exactly one unique, nonzero ascending record; failure is only before writing, in ReserveEntries's array allocation. Eager counts that completed record immediately. Checkpoint has count=sum(Pop(Read(i)&Mask),i<committed); before growth it extends committed to used, so count matches every already published record when the exception can escape. A successful operation uses exact source.count. The checkpoint never reads unused array storage, and inline-to-array transitions preserve the prefix. Capacity checks are for allocation state, not revalidation of an input fact.

## Actual tests and fair comparison

Separate injected-fault executables replace only the next-array allocation site. Every real growth index is injected, with public CopyFrom, empty-target Append, both result-alias directions and same/empty input UnionInto shortcuts. Dirty and empty outputs, high positions, multiple bits per record, repeated growth, source ownership, retry/removal/reuse and homogeneous Micro copy are covered. Old control must reproduce the minimal defect; fixed candidates must have zero invalid states in this scope. Injection does not exhaust actual machine memory. Runtime timed binaries contain no hooks.

Inherited mixed/mask/local/bulk/ownership/capacity suites and original Micro+protected Runtime suites are also run. Checkpoint is tested with hardware disabled and in an independent netstandard2.1 library hosted by net8. This is NOT Unity Mono/IL2CPP/Burst or a phone run.

Performance: all2520 prior public-operation cases plus192 predeclared copy/empty/same-input cases per build. Copy fixtures: domains10000/262144,source0/1/2/8/32/33/128/4096,contiguous/scattered; new default-capacity copy and actual empty-append, prepared overwrite/empty-append and union shortcuts. Ordinary Clone remains unchanged and is NOT replaced by a conversion API in ranking. Every build has identical-binary A/A,7samples×3 shuffled process rounds,oneCPU affinity,.5ms calibration,8MiB allocation cap,GC counts/all slow samples retained.5% every-round screen is not a confidence interval.

```sh
python3 'Audit~/CopyCommit/run.py' --output artifacts/results --rounds 3
python3 'Audit~/CopyCommit/review.py' --root artifacts/results
```

Requires full-history checkout and SDK8.0.425. Collector redownloads both archived originals, verifies SHA/tree/code/binaries/all samples/Count/capacity/Bop and reruns each archived verifier, requiring equal structured results and cross-architecture generated sources. Both good and bad timings must be reported. Public-pair lifecycle, previous batch holdout and all historical main/Alex comparisons are NOT rerun by this scoped experiment.

Local preflight is separately labeled SDK8.0.423/runtime8.0.29 snapshot evidence, never spliced into pinned CI timings. It reproduced3093old invalid states across3931injectedgrowthpoints; both fixes passed all3931 and3873normal fault-fixture configurations. This is not the status of CI, which is pending at submission.
