# Private PR26 refinement measurements

This harness compares the selected PR26 experiment (measured commit 98311092b7e8892e5632c2863a391187f4367cf8, guarded capacity plus empty-source Clear/return, without NoInlining) with narrowly attributed private variants. It does not use shipped RuntimeTagSet as the baseline. It adds no Block256 integration.

## Scope and design

- Query gate: 37 fixed rows per requested Micro/Dense/Auto mode. Counts 0/1/2/8/32/64; identical first/middle/last hit locations; same-record missing-bit and low/high record misses; balanced 32-hit/32-miss batches. Empty input has only miss probes. Every logical query operation includes exactly 64 public HasTagExact calls and checks the consumed hit count.
- Builder gate: 30 fixed rows per mode. Empty/singleton/two-member sorted and unordered controls; 8/64/4096 distinct members in clustered/scattered patterns; sorted, shuffled, reversed and 2x-duplicate unordered inputs. Duplicate rows report distinct-member count separately from input count encoded in the input hash. Allocation and timing include the entire factory, owned object/arrays, sorting, deduplication and allocation.
- Mixed/lifecycle stage: 75 rows per mode. Equal 8/4096 clustered/scattered union, restore+append, restore+remove and copy; 4096:8 and 8:4096 union/append/remove; same and opposite peer storage; clean and one-bit-dirty empty copy with 0/8/4096 destination reservations; charged two-unordered-builds plus output allocation plus 64 unions.
- Optional attributed popcount gate: 16 rows per mode, union and restore+remove at 8/4096 clustered/scattered IDs with same/opposite peer storage.
- All inputs are strict handles belonging to one 262,144-member registry. Numeric placement, shuffle seed (773), member count and peers are fixed. The regular scatter pattern spaces IDs by 32; offset-16 peers have disjoint record sets. Equal-sized cluster peers overlap by 50%. These are named shapes, not arbitrary scatter. The later mixed group also includes 4096-member seeded-random sets (seed 20261002) with exactly 50% overlap and charged lifecycle. Query rows represent record count because members are 32 IDs apart. Input generation and the registry are outside measured work.
- Each sampled final output is enumerated and compared to an independent sorted-distinct oracle outside timing, before subsequent work can mask it. Input/peer immutability and consumed query/operation checksums are checked. Factory reservation and prepared layout/reservation preservation are checked. Prepared warmed calls must allocate zero bytes.
- Sorted/unordered direct public API loops are separately selected outside iteration. Per-operation setup costs are explicit: restore+append/remove includes CopyFrom; dirty empty-copy includes a successful one-bit AddTag and empty CopyFrom; charged lifecycle includes both unordered factories, the destination constructor and 64 unions. No workload is silently normalized to make a candidate favorable.

## Sampling

Each group/mode/backend calibrates both variants toward 5ms and uses the larger iteration count for all baseline, candidate and A/A measurements in all three rounds, limited by the stricter per-variant 64MiB sample-allocation ceiling. An allocating row with one or more actual samples below 1 ms at the budget-capped fixed count remains invalid rather than changing its workload or omitting it. Every raw sample records elapsed time, B/op, total allocated bytes, collection counts and validity. Minimum valid actual sample duration is 1ms, independently checked for every sample.

Each round runs baseline A, candidate B and the identical baseline binary AA; labels reverse in round 2. All processes run sequentially on one permitted CPU. Concurrent GC, ReadyToRun, tiered compilation and tiered PGO are disabled uniformly so tier transitions do not contaminate small samples. Each process warms all measured operations. Default and explicitly disabled hardware intrinsics are separate backends. Calibration, results, manifests, source hashes, DLL hashes, machine/runtime metadata and process commands are retained. A separate fresh process reports the very first shuffled 8-member Micro factory's allocation, including any one-time generic comparer initialization; this is distinct from warmed B/op.

The report describes each candidate process median divided by the geometric mean of its two bracketing baseline medians, and also retains every B/A and B/AA crossing plus p95/max batch-tail ratios. A descriptive 'clear' effect requires all six B/A and B/AA crossings to exceed the same direction's threshold, which is the larger of 3% or the worst measured A/A drift for that row. This is a conservative admission diagnostic, not a statistical significance test or universal dominance claim. A/A drift and invalid rows stay visible.

## Commands

From this directory, after reserving the host CPU from other validation/build work:

    python3 build.py baseline=../baseline query-mask=../variants/query-mask owned-builder=../variants/owned-builder combined=../variants/combined
    python3 run.py --candidate query-mask --group query --backend default --name query-default
    python3 analyze.py query-default
    python3 run.py --candidate owned-builder --group builder --backend default --name builder-default
    python3 analyze.py builder-default

Repeat the gates with backend nohw and unique run names. Only after attribution is accepted, run the combined candidate's admitted query/builder cells and mixed stage. All source snapshots are frozen before build; the runner verifies source and DLL hashes. No external writes or publication occur.

Results are same-host .NET 8 measurements of an experimental type. They do not establish Unity/Mono/IL2CPP/Burst performance. Payload bytes exclude object headers; measured B/op includes object and array allocation. Prior short-duration/AA-drifting comparison materials remain historical diagnostics only.

## Later attributed and integrated passes

Keep Stage1's Probe.cs and run.py frozen. Probe.Later.cs and run_later.py add the safety-path group documented in LATER_SCOPE.md. Compile a separate baseline with the same later harness as the selected integrated candidate, for example:

    python3 build.py --harness Probe.Later.cs baseline-later=../baseline selected-v1=../variants/selected-v1
    python3 run_later.py --baseline baseline-later --candidate selected-v1 --group growth --layouts Micro --backend default --name selected-growth-default

The original harness already contains the optional isolated popcount group; it may be compared against the original-harness baseline without changing Stage1. Later integrated query, builder and mixed controls use the exact selected source and matched later baseline/harness. No query-mask result is inherited into the integrated source. Unchanged fast Dense controls with one or more actual samples below 1 ms at the budget-capped fixed count remain unranked; a larger-budget confirmation, if undertaken, must be separately labeled and cannot replace original-protocol data.
