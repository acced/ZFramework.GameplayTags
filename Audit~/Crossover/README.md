# Query and representation crossover experiments

Baseline: PR10 head `40696c95b6c53e95ef6300c995f01c9b443f0a3e`; runtime is pinned Auto `84729cc`. No production files changed.

## Separately measured questions

1. Full public `HasTagExact` callers, not isolated comparisons: unchanged binary search, linear scan, vector scan, and a **predeclared experimental** rule (linear for <=2; vector for one to two vector widths; binary otherwise). No claim that this rule is optimal. Counts 0..512 with vector-width neighbors; fixed-middle, random-hit, mixed, high-miss probes; two seeds. An identical binary is rerun as binaryAA.
2. Same global ID inputs under forced Array, forced Dense with retained fusion kernel, normalized Micro DirectArray, and original Auto. Three universes, seven sizes, contiguous/four-cluster/scattered distributions, 0/50/100% union overlap, two seeds. Fresh independent Union, genuine Copy+Append, genuine Copy+Remove, reuse operations and public build cost are distinct. Micro copy preserves physical record capacity for this experiment; its normalization patch is archived rather than hidden. There is NO adaptive selector produced by row-wise picking.
3. Separate 0/4/8 integer-inline allocation-layout prototypes. Empty, in-capacity and overflow sizes, 10,000 independently owned retained instances. These classes are not complete mutable tag sets and are NOT production candidates yet.
4. Separate sorted-array intersection and difference with merge, galloping, and a simple SIMD intersection algorithm. 1:1/1:16/1:256 requested ratios (actual capped sizes recorded), 0/50/100% overlap. Full result output and accurate cardinality, fresh vs prepared. Not substituted for Copy+Remove; no SIMD-union implementation is claimed.

## Measurement and limitations

New calibrated protocol: ~1ms target, max 16 MiB allocated per sample, seven samples, three independent process rounds. Full GC outside timing then a short warmup; allocation/GC deltas retained, zero-allocation paths checked. All samples kept, including samples below target due to the budget. **Do not compare absolute numbers against earlier fixed-iteration PR reports.** Different SDK/CPU reports stay separate. The measured QueryLoop and OperationLoop are disassembled from the same executable; no borrowed codegen evidence.

The output `Forced_Mode_Frontier.csv` is a diagnostic lower-envelope, not a measured auto policy. Scan/conversion cost is not hidden in runtime: selection/conversion is not implemented. Build cost is measured with already-resolved IDs. No new domain restriction, pool, COW, deferred Count or permanent per-set telemetry.

Run: `python3 Audit~/Crossover/run.py --output artifacts/results --rounds 3`, then `python3 Audit~/Crossover/review.py artifacts/results`.

Original full API, DFS hierarchy, Query, ownership and capacity regression suite runs for changed Runtime variants. Additional active-prefix and stale-capacity tests run with hardware enabled/disabled. This pass does not execute Unity/IL2CPP/Burst or physical mobile devices. The Micro experiment retains the existing 2^20 registry limit; it does not narrow the Auto runtime's contract.

## Primary algorithm references

- Lemire et al., Roaring Bitmaps: Implementation of an Optimized Software Library (2017): https://arxiv.org/abs/1709.07821
- Lemire, Boytsov, Kurz, SIMD Compression and the Intersection of Sorted Integers (2014): https://arxiv.org/abs/1401.6399
- Algorithmica, Static B-Trees: https://en.algorithmica.org/hpc/data-structures/s-tree/

The prototypes borrow algorithmic ideas, not the papers' performance claims. No all-workload speed guarantee or release approval.
