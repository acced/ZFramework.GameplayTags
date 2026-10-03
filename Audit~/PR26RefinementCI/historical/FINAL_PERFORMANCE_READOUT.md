# Final selected Runtime performance readout

Candidate DLL SHA256: 1c032a39cf571ba4ef80ff22c12dd99049b01e1ce0a2cd34040868d899188fa3
Baseline DLL SHA256: 1dd0309a59eb38834eed5da8edfd4e959d9b584457976ed5c69d61fcc2a4d834

This is the one full reference-host cohort on the selected hardware-gated-count plus split-bridge Runtime DLL. The comparison baseline is the selected PR26 exact kernel/builders in the matched Runtime integration scaffold. Shared checked-guard repairs and the original additive bridge are documented as integration code; they are not attributed to upstream PR26. Prior rejected/attribution/targeted cohorts remain separately identified.

## Full median/batch-throughput matrix

Every cell uses three rounds with two identical baseline processes bracketing the candidate, seven samples per process, matched fixed iteration counts, exact output/source/input validation and observed loaded-DLL binding. A median win/loss requires all six B/A and B/AA crossings to exceed max(3%, the cell’s largest observed A/A drift). This is descriptive throughput evidence, not a significance test or tail guarantee.

| Group | Backend | Cells | Median wins | Regressions | Uncertain/small | Invalid |
|---|---|---:|---:|---:|---:|---:|
|query|default|111|0|2|109|0|
|query|nohw|111|0|0|111|0|
|builder|default|90|15|0|72|3|
|builder|nohw|90|4|1|82|3|
|mixed|default|225|48|0|177|0|
|mixed|nohw|225|1|1|223|0|

## Allocation and validity

- final-query-default: 0 allocation-reducing cells; minimum actual sample 5.262ms; 0 invalid cells retained
- final-query-nohw: 0 allocation-reducing cells; minimum actual sample 5.056ms; 0 invalid cells retained
- final-builder-default: 36 allocation-reducing cells; minimum actual sample 0.291ms; 3 invalid cells retained
- final-builder-nohw: 36 allocation-reducing cells; minimum actual sample 0.307ms; 3 invalid cells retained
- final-mixed-default: 10 allocation-reducing cells; minimum actual sample 4.024ms; 0 invalid cells retained
- final-mixed-nohw: 10 allocation-reducing cells; minimum actual sample 3.832ms; 0 invalid cells retained

All prepared operations are required to allocate0B and passed that condition. Builder/fresh lifecycle B/op includes objects, arrays and any sorting workspace. Their elapsed timing also includes collections that occur inside the batch. Payload/reservation contracts match across variants. The three invalid Dense controls repeated on each backend are empty sorted, empty shuffled and singleton sorted. They have one or more actual samples below 1 ms at the budget-capped fixed count; those observed failures remain visible and are not ranked. Separate first-use Micro8 allocation is 192 B baseline versus 136 B candidate on both backends, compared with warmed 168 B versus 112 B. The 24 B first-use premium is equal for both.

| Unordered Micro example | Baseline B/op | Candidate B/op |
|---|---:|---:|
|8 unique members|168|112|
|64 unique members|616|336|
|4096 unique members|32872|16464|

## Every final median regression

These observations remain admission limits. No case is removed merely because its source path was unchanged or a previous diagnostic had a different result.

| Run | Mode | Cell | Candidate/baseline | Max A/A drift | Max batch-tail ratio |
|---|---|---|---:|---:|---:|
|final-query-default|Dense|query/scatter/8/query64/balanced/same/0|1.040|0.014|1.484|
|final-query-default|Auto|query/scatter/8/query64/miss-bit/same/0|1.108|0.038|1.570|
|final-builder-nohw|Dense|builder/cluster/4096/build/sorted/same/0|1.265|0.027|1.378|
|final-mixed-nohw|Micro|mixed/random/4096/restore-remove/equal/same/4096|1.119|0.072|1.217|

## Tail limits on median wins

P95/max batch tails are retained separately and do not gate the median classification. The largest adverse max-batch crossings among median wins are shown below; they are not estimates of individual-operation tail latency.

| Run | Mode | Cell | Median ratio | Max p95 batch ratio | Max batch ratio |
|---|---|---|---:|---:|---:|
|final-builder-default|Auto|builder/scatter/8/build/shuffled/same/0|0.833|1.566|1.860|
|final-mixed-default|Dense|mixed/scatter/8/restore-remove/small-large/opposite/4096|0.776|1.485|1.489|
|final-mixed-default|Dense|mixed/cluster/4096/restore-remove/equal/opposite/4096|0.817|1.313|1.376|
|final-builder-default|Auto|builder/scatter/8/build/reversed/same/0|0.848|1.173|1.292|
|final-builder-default|Micro|builder/cluster/8/build/shuffled/same/0|0.882|1.185|1.251|
|final-mixed-default|Dense|mixed/scatter/4096/restore-remove/equal/opposite/4096|0.733|1.123|1.231|
|final-mixed-default|Dense|mixed/cluster/4096/union/equal/opposite/4096|0.738|1.129|1.212|
|final-mixed-default|Auto|mixed/scatter/8/restore-append/small-large/opposite/4096|0.942|1.180|1.200|
|final-mixed-default|Dense|mixed/cluster/8/restore-remove/small-large/opposite/4096|0.848|1.112|1.191|
|final-mixed-default|Micro|mixed/scatter/8/restore-remove/small-large/opposite/4096|0.843|1.088|1.175|
|final-mixed-default|Auto|mixed/cluster/4096/restore-remove/large-small/opposite/8|0.689|1.014|1.139|
|final-builder-default|Auto|builder/cluster/8/build/reversed/same/0|0.900|1.042|1.103|

## Separate bridge and targeted evidence

The selected split bridge has its own three-way54-cell/backend comparison, documented in BRIDGE_SPLIT_READOUT.md. That fixed-actor/hot-query scope compares legacy public matching, old Direct matching and split Direct matching, including known true/false, empty/constant and charged mutation cases. It has zero allocation in every sampled operation. Representation-dependent mutation gains are not described as pure evaluator gains.

V3_FACTOR_READOUT.md, NATIVE_READOUT.md and the preserved earlier cohort documents record the staged attribution, failed v1 growth path, nohw fallback finding, context-dependent skew-append observations and bounded unchanged-control diagnostic. None is silently relabeled as this final cohort.

## Practical limits

These are same-host .NET8.0.31 X64 measurements with a separate Unity test facade, not Unity player/Mono/IL2CPP/Burst results. nohw disables managed hardware intrinsics in the net8 binary; actual checked Standard coverage is correctness evidence, not a Standard timing claim. Hot fixed fixtures do not establish universal application performance. The final recommendation must retain the regressions, invalid cells and tail limits above.

Raw result JSON/stdout/stderr, commands, calibrations, source/host/Runtime/facade hashes, per-round medians, crossings, GC counts and batch tails are preserved under results/. Native captures are untimed diagnostics and never enter these timing summaries.
