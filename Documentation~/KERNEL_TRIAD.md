# Robin Hood / ordered SIMD / Dense vector pilot

Base: Auto `84729cc3b6e1b4681328806df2d7c8a1d8f568f6`; immutable Micro control `9be85fcd1f8d1f594ceb5bc98f2ce234b398e0e0`. This branch adds only experimental files and workflow. Runtime, Editor, Samples, Tests and package.json are checked against Auto and remain unchanged.

## Three isolated questions

1. Does a flat Robin Hood table of 16+16-bit address/mask records reduce full small-set costs? The independent micro-only class uses 3/4 maximum load, multiplicative mixing, backward-shift deletion, no tombstones, one inline record, and eager Count. Copying preserves table layout (including empty slots); construction and rehash are inside timing. There is no raw member-ID storage, secondary sorted member copy, page table, pool or COW. Enumeration is slot order, not a production-compatible DFS-order API. The narrow encoding rejects more than 2^20 registered IDs.
2. Does SIMD probing improve the existing ordered layout without changing its builders? The runner copies the fixed Micro source to a NEW experimental class and replaces only HasTagExact. System.Numerics.Vector compares complete valid uint records when between one vector and 32 records are used; it never reads unused padding. Binary fallback remains for other sizes and non-accelerated hosts. Every generated source and digest is archived; the old source is not modified.
3. Does vectorized writing plus eager population count improve Dense operations? Four independent raw-buffer kernels: the four-way SWAR control; BitOperations hardware-assisted scalar; AVX2 nibble shuffle + SAD fused into output; and vector output followed by a separately measured full count pass. Exact write/count work is always timed. A scalar fallback is tested with hardware intrinsics disabled. These .NET 8 kernels do not claim Unity or ARM64 support/performance. Three NEW experimental Micro clones replace only DenseUnion and DenseDifference to test full fresh/prepared public APIs; selection is explicitly forced Dense in that stage. Auto-auto is also retained as a distinct control.

## Measurement

Use `python3 Audit~/Triad/run_triad.py --output artifacts/triad --rounds 3` in a full clone with .NET SDK 8.0.425. No external package is required. Git objects for both fixed controls must exist.

Small stage: 10,000/65,536/262,144 explicit definitions plus implicit ancestors; members 0,1,7,8,9,31,32,33,127,128,129; contiguous/scattered distribution; exact hit/miss/mixed 128-probe batch and fresh union, actual copy+append, actual copy+remove. Prepared output is measured separately. Controls are literal Auto, literal ordered Micro, and its existing scan8 build. New candidates are Robin and SIMD. This is a focused same-input comparison, NOT a rerun against main/Alex/all prior experiments.

Large stage: the same definition sizes, members 128/1,024/4,096, both distributions. Forced-Dense Micro control versus three kernel transforms; literal Auto auto-select and forced Dense are separate controls. Do not combine per-row winners or describe forced Dense as an adaptive product.

Raw Dense stage: 0/1/3/4/7/8/16/32/159/1,041/4,161/15,625 words; clustered/random bits; OR/AND/AND-NOT; prepared/fresh/actual clone+mutate. This isolates kernels and is not interchangeable with end-to-end collection time.

Three rotating/reversed process rounds; seven raw samples per case; CPU affinity fixed to an allowed CPU. Warmup then GC/WaitForPendingFinalizers/GC applied uniformly. Allocation, initialization, copies and Count stay in time; prepared allocations must be zero. Results/inputs validated outside timing. Old fixture inputs retained, sample budgets uniformly increased. Preserve all raw samples and slower rows. Every-round 5% comparisons are screening rules, not statistical confidence intervals.

## Tests

Robin differential set-pairs, real wraparound collision clusters/backshift, stale capacity, foreign scope/defaults, all 65,536 16-bit masks, random churn, capacity boundaries and arbitrary-placement prepared allocation with positive control. SIMD tests include valid-lane boundaries and stale slots. Dense tests include zero/one bits, partial vectors, aliases, dirty outputs and exact Count. Existing Micro semantics are run against each source transform. Hardware-disabled tests and a separate untimed JIT-disassembly run are archived. Counts include repeated assertions, not independent test-case counts.

## Not included

No production migration, hierarchy/FrozenQuery integration for Robin, deterministic sorted enumeration for Robin, native Unity/Burst/IL2CPP execution, ARM64 benchmark, dynamic SIMD/Burst portability claim, large real registry timing above 2^20, comprehensive long-lived/peak-memory accounting or publication. Allocation bytes include real managed allocations; BufferBytes is only payload. Correct CI execution is not a performance or release approval.

## Primary algorithm references

- tinyset implementation: https://github.com/droundy/tinyset (bitmap values in a Robin Hood table; not importing its raw-integer/difference modes).
- Microsoft SIMD documentation: https://learn.microsoft.com/en-us/dotnet/standard/simd
- Microsoft Vector.IsHardwareAccelerated: https://learn.microsoft.com/en-us/dotnet/api/system.numerics.vector.ishardwareaccelerated

This pilot provides independently implemented C# mechanisms and tests, not copied benchmark claims.
