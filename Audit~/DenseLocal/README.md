# Dense-only traversal in operation-local helpers

This experiment continues PR15 (`5d863777ac5c7a546785105ac0c4740fa4360ad5`) without editing production Runtime, Editor, Samples or Tests.

## Fixed alternatives

- `direct`: exactly PR15's direct Dense-result candidate, generated from the pinned PR14 source and unchanged PR15 transform.
- `shared`: exactly PR15's rejected combined candidate, retained as a comparator, not a recommendation.
- `split`: `direct` plus a private Dense-only record cursor in mixed CopyFrom, Micro<-Dense Append, and mixed Union with Micro output.

`split` leaves the existing Records and public Enumerator source and layout unchanged. It does not improve public Dense foreach directly. No new collection fields, owned buffers, pool, shared mutable result, query cache, online counters or implicit representation conversion. The new temporary cursor is used only in helpers and its managed value size is measured separately. The candidate is not approved until its whole matrix and adverse results are reviewed.

## Admission, ownership and invariants

Existing public registration/owner checks and alias guards stay before the helpers. The new cursor constructor is private and only receives a proven Dense source. New helpers are called only when the destination is Micro. Union helper is reached only for an independent output and exactly one Dense input. There is no public way to construct or inject this private cursor. CopyFrom and Union must overwrite dirty output; old contents are cleared only after admission. Count stays eager and exact. Independent results remain independently mutable.

Each cursor loads whole words, skips zero words, and emits the same ascending nonzero Micro16 records. Every set bit belongs to one disjoint aligned 16-bit slice; after emission that slice is cleared from the private pending word. Tail bits outside the registry are zero by existing validated set invariants. Source inputs cannot be concurrently modified during operations; this is unchanged.

Existing reserved-capacity and growth algorithms remain in PutRecord/ReserveEntries. Same emitted order means output growth, Count and allocation match the direct control. Micro<-Dense Append still performs binary search and possible suffix movement for each emitted record; this experiment does NOT claim to solve its worst-case insertion complexity.

The inherited Micro16 bound is 2^20 actual IDs. The existing uint-only SIMD code in the fixed generated core is not extended to MICRO_WIDE. No Micro32, Unity IL2CPP, Burst or mobile backend completion is claimed.

## Verification and timing

Pinned SDK8.0.425, net8, Release, x64 and ARM64. All variants run the prior complete Micro suite and prior mixed correctness suite, plus exhaustive masks through Copy/Union/Append, long zero spans, high offsets, final-word boundaries, dirty output, both alias directions, sustained mutation and prepared allocation checks. Split is also tested without hardware intrinsics and via a separately compiled netstandard2.1 library in a net8 host. Production's unchanged original suite is run, not misrepresented as full Micro/FrozenQuery integration.

Public benchmark keeps PR15's dimensions/operations; adds prepared CopyFrom, retains both operand orders, all same-format paths and public enumeration. Three independently shuffled process rounds, seven samples each, same-binary A/A, all allocation/GC and slow samples retained. All variants now run the input-pair lifecycle including both operands' paid preparation/conversion. Comparisons use this run's fixed counterparts; historical absolute ns are not mixed. A 5% repeated screen is not a confidence interval.

Build and run from a full-history checkout:

```sh
mkdir -p artifacts
git archive --format=zip HEAD > artifacts/source.zip
python3 'Audit~/DenseLocal/run.py' --output artifacts/results --rounds 3
python3 'Audit~/DenseLocal/review.py' --root artifacts
```

The source ZIP is not a full Git checkout; pin history when regenerating. Generated alternatives contain the same class name and must be compiled separately, not imported together. Native Unity/IL2CPP/phone, cold-actor rotation, all historical competitors, lifetime peak memory and a unified representation selector remain unverified.
