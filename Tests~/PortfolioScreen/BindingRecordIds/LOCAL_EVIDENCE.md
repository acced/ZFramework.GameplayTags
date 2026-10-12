# Preserved local evidence and limits

These are completed local .NET 8.0.31 x64 observations, not results of the portable package. Qualification remains 0/10. All original records and adverse/short/GC observations remain preserved. No replacement samples or candidate variants were run.

## Untimed gate

The original unexecuted preparation had runner-only issues; its narrow repaired derivative passed two builds/two semantic targets. An initial missing-seal CLI invocation failed before creating output or launching a compiler/target; the correct invocation completed once. Core assertions 7,595,855, binding checks 70, supplemental frames 13, and exact snapshot/stdout equality passed. Snapshot SHA-256: `730eaa6d941f0fd91f88ec5478741cfd4284754cf1c48c4fd2a8cfe296b87ea0`.

Actual managed sizes: pointer 8, GameplayTag 16, private BindData 24 → 16 bytes. All 38 Runtime types/367 methods passed the exact metadata/unrelated-IL gate. Local .NET FullOpts native code was descriptive: Bind 547 → 534 bytes, one checked-reference barrier removed; UnbindAll 184 → 137 bytes, no 24-byte record temporary, smaller stack reservation. Both original delegate allocations remain. This is emitted-work evidence, not a speed guarantee. Native results and .NET layouts do not establish Mono behavior.

## Constructor-inclusive cold: all four pairs per shape

Exactly two builds and 32 target calls completed, AB/BA/BA/AB. All semantic pairs pass and measured GC deltas are zero. Ratios below are candidate/original latency; values above 1 are adverse and none is waived.

| Shape | Whole-call allocated bytes A → B | Retained owned-array payload A → B | Four latency ratios |
|---|---:|---:|---|
| absent | 432 → 400 | 96 → 64 | 1.221875649, 0.917535788, 1.003420013, 0.935711569 |
| present | 432 → 400 | 96 → 64 | 0.479775370, 0.963011135, 0.950987354, 1.034923683 |
| external listener | 416 → 384 | 96 → 64 | 1.049518372, 0.884279355, 0.725754674, 0.989797802 |
| five-Bind growth | 1472 → 1376 | 192 → 128 | 0.808089794, 0.998317511, 1.175845459, 1.029576098 |

Single rows save 32 allocated bytes in every pair; growth saves 96. Retained payload saves 32 at capacity four and 64 at capacity eight. Payload is capacity × actual managed record size only, excluding headers/other objects. Transient growth-array allocation is distinct from final retained payload. Cold latency remains mixed/adverse.

Cold observation SHA-256: `4d3957bb4c5a552d6e605e004bf6c8f727c3c851ea79ee5f7ad2b43e1daf22c7`.

## Warm: all 28 paired directions / 56 observations

Exactly two builds, one original-only seven-row pilot and 56 fixed fresh-process targets completed. Counts were frozen before candidate timing using a 200-ms baseline pilot target. All 28 semantic pairs pass; no allocation increase occurred. Values in the allocation/GC columns repeated in every pair of that row.

| Row | Bytes/cycle A → B | Gen0/batch A → B | Batches below 200 ms | Four latency ratios B/A |
|---|---:|---:|---:|---|
| fresh-single | 576 → 544 | 82 → 78 | 8/8 | 1.083404449, 1.070068661, 0.942640439, 0.912188947 |
| retained-absent | 152 → 152 | 95 → 95 | 3/8 | 1.271478553, 0.894579199, 1.058298094, 0.864788112 |
| retained-present | 152 → 152 | 91 → 91 | 8/8 | 1.009552511, 0.903417494, 1.030617662, 1.062525555 |
| retained distinct-eight | 1216 → 1216 | 84 → 84 | 6/8 | 0.949162041, 1.030106349, 0.909956539, 0.977143084 |
| external-multicast | 376 → 376 | 109 → 109 | 0/8 | 1.084608321, 0.959262659, 0.870977601, 1.079755872 |
| direct-subscription | 0 → 0 | 0 → 0 | 8/8 | 0.928396920, 0.879477152, 0.980944210, 1.007275185 |
| direct-dispatch | 0 → 0 | 0 → 0 | 6/8 | 0.867645394, 1.072724913, 1.009395417, 1.117974520 |

Fresh-single saves exactly 32 bytes/cycle, or 19,203,264 bytes in each 600,102-cycle batch. Retained cycles do not replace their record array, so smaller owned payload does not save allocation on each retained Bind. Allocation/GC time was not subtracted: 40/56 batches have Gen0 collections; all Gen1/2 deltas are zero. Direct controls allocate zero bytes and collect zero times.

39/56 batches missed 200 ms; the shortest was 84.882808 ms, none below 50 ms. Counts were not increased after observing this. Retained-present and unchanged direct-dispatch each worsen in three of four pairs. Retained-absent pair 0 worsens 27.147855% with both batches above 200 ms. External-multicast has two adverse pairs despite every batch exceeding 200 ms. These are unresolved observations, not a diagnosed noise effect. Favorable pairs likewise do not establish a universal benefit or timing nonregression.

Warm observation SHA-256: `3d9518351ce9a1410baf83f67a7a4f0ff7a2a5cc913fc94a73d2fc368f842934`; original local count map SHA-256: `e224694d67783403573285a08e5de44491e9505cbb4359cd8b5b356ded9bdc94`.

## Scope kept for portable review

The small owned-record footprint and fresh-call allocation saving are real local measurements. Private arbitrary-name lifetimes may change intentionally and no whole-heap retention claim follows. Same-group API and caller-manual subscription comparisons retain the limitations described in the README. Retained distinct-eight does not time growth. Selected-row fresh processes differ from prior full-traversal CI, and the combined portable assembly adds a new context. Portable counts use only preserved original observations, are fixed for both backends, and cannot be adjusted after seeing candidate or Mono timings. No Mono, Unity/IL2CPP, checked build, complete root-workload matrix or statistical qualification is established by the local stages.
