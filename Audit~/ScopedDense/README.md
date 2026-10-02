# Scoped Dense materialization experiment

Base: PR15 documentation commit `5d863777ac5c7a546785105ac0c4740fa4360ad5`. The fixed control is its **direct Dense-output** build, not the rejected shared-cursor combination. PR14 core and builder hashes are checked before regenerating this control.

## Variants

- control: exact PR15 direct Dense-result branch.
- union: adds a private DenseRecordCursor and specialized independent `Dense+Micro -> Micro` union (both input orders). Uses known input counts minus overlapping bits.
- copy: adds the same private cursor only to `CopyFrom(Dense)` when target is Micro.
- combined: the two changes together, a real independent build.

Public `Records` and `Enumerator`, lookup, AppendCore, RemoveTags, ordinary clone, public validation, capacity growth, set fields and homogeneous kernels remain unchanged. Generator compares their actual method bodies; tests require 16-byte Records and 32-byte Enumerator on the tested 64-bit hosts. The private cursor's own stack/value size is reported separately; zero managed allocation is not zero cost.

UnionCore resolves owner, output aliases, empty/same inputs first. In-place aliases still take the original AppendCore and are NOT newly optimized. Two Dense sources producing Micro also retain the original path. A nonalias mixed-to-Micro result is cleared, ordered records are directly merged and appended using the existing capacity policy. The result count is `full.Count + local.Count - duplicateBits`; no delayed counting. Copy clears target, visits nonzero Dense words, appends occupied 16-ID slices and retains the source's exact Count. Empty copy resets members without shrinking storage. No temporary ID array or online representation conversion.

The iterator loads each Dense word once. It finds the first occupied 16-ID slice, emits its address/mask and clears that slice from local pending bits. Tail bits are already zero by the existing set invariant. No concurrent source modification is supported. The additional private state lives only in these materialization calls, not in public enumeration or persistent set instances.

## Measurement

`python3 Audit~/ScopedDense/run.py --output artifacts/results --rounds 3`

`python3 Audit~/ScopedDense/review.py --root artifacts`

A full-history checkout at the measured commit is required for inherited fixed-source generation. SDK8.0.425 is pinned; fresh allocation and accurate counting are included. The PR15 full public-operation and pair-lifecycle fixture is retained and extended with independent create+CopyFrom and prepared CopyFrom across each source/target representation. Prepared output capacity is held constant; no aliases/fusion replace the public contracts. Same-binary A/A, all slow samples, GC collection counts and generated caller code are retained.

Additional regression tests exercise all65536 masks through the new materialization paths, high-word and tail offsets, dirty outputs, exact record capacity, both source orders, same-source copy, output aliases and independent state. Full Micro and protected Runtime suites, no-HW and netstandard2.1-host checks are rerun. Assertions include loops, not distinct test cases.

## Scope / costs

No production files are edited, no new membership format or selector exists, and no universal performance claim is made. Dense-to-Micro general public enumeration stays unchanged; its previous slow sparse enumeration is not silently claimed fixed. MICRO_WIDE's prior uint-specific SIMD compile limitation is unchanged. Real Unity/IL2CPP/Burst/mobile validation remains separate from Linux .NET results. Local executors were unavailable while preparing this patch; actual compilation/results must come from CI, not assumed success.
