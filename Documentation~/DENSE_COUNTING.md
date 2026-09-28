# Portable bulk population counting

## Why this was added

The unchanged-runtime diagnostic run 36393233057 measured the preceding portable two-accumulator SWAR fused union against a union pass followed by Harley-Seal carry-save counting. This was an experiment, not a production result. It showed a substantial benefit at 64+ words, but substantial losses at 1 and 8 words. The shipping version therefore uses carry-save only at 64+ member words; short inputs, indexed updates and directory counts retain scalar SWAR. `Documentation~/DENSE_ONLY.md`'s earlier two-accumulator explanation is superseded for linear bulk operations by this implementation.

The sixteen-word reduction replaces repeated full population counts with Boolean carry-save arithmetic. It keeps four bit planes (ones, twos, fours, eights) in local variables; their weighted population counts plus the accumulated sixteens produce the exact total. The leftover words are counted separately. There are no persistent counters, extra buffers or platform intrinsics. The same source compiles for .NET Standard 2.1 and the hosted tests.

Linear union/append first write the actual result words and then count them; linear deletion updates occupancy for newly zero words and then counts the actual result. The extra pass and eager Count remain inside each public operation's timing. Low-occupancy kernels and all public identity/alias checks remain. No assertion of a full-API speedup follows from the isolated kernel numbers: the full comparison and an additional same-run comparison with `08c27ff39ce7c4fae70320ccdac07d14f0f6fd06` decide that.

Exact membership also combines the empty-buffer check with its unsigned array-index bound using one local array reference. The public registry-identity check is retained, so this does not accept foreign handles to improve a timing result.

## Sources and license

- Original portable implementation: https://github.com/WojciechMula/sse-popcount/blob/master/popcnt-harley-seal.cpp
- Research background: https://arxiv.org/abs/1611.07612
- Redistribution notice: [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md)

The research paper includes hardware-specific SIMD results. This library does not claim those results, does not require AVX2 and does not use a faster audit-only implementation. Kernel experiments remain labeled as experiments; all old unfavorable full-API data remains in Actions.
