# B1.1 dispatch attribution result

## Outcome

Keep merge for balanced inputs. Smaller-side binary search is strongly supported for the tested interleaved/overlapping skew cases, but separated-range checks must come first. Difference needs directional treatment: source-side binary search when the source is tiny; descending find/remove when only a few removal keys are supplied. This screen does **not** establish a universal ratio or production crossover.

A minimal candidate for public-API verification is:

1. Empty/self cases and an ordinal separated-range check. Intersection becomes empty; difference copies lhs to a distinct output or leaves an aliased lhs untouched.
2. Singleton removal: one binary lookup and one `RemoveAt` on a hit; no mutation on a miss. This is a complexity-based cheap case; singleton timing was outside the final audited grid.
3. Keep general merge. Optionally evaluate `smallCount * ceil(log2(largeCount)) < lhsCount + rhsCount` as an explicitly labeled comparison-work heuristic for intersection; for difference apply source-side lookup only when lhs is the smaller side. Arithmetic must avoid overflow. This is a candidate heuristic, not a measured boundary.
4. Repeated find/remove is promising for the measured four removal keys, but its shifting work grows with `removeCount * lhsCount`; this grid does not justify generalizing to an arbitrary remove-side ratio threshold. Keep general merge there unless integrated evidence supports a bounded alternative.

## Representative results

Median per-call wall times below include output writes. All measured cells recorded zero allocated bytes per operation and zero collections in every generation.

| Operation and shape | B1 merge | Candidate | Candidate time |
|---|---:|---|---:|
| Intersection,1024×1024 interleaved misses |25.40µs |smaller-side binary |168.36µs |
| Difference output,1024×1024 all overlap |34.24µs |source binary / find-remove |138.21µs /109.53µs |
| Intersection,4×1024 25% overlap |8.71µs |smaller-side binary |0.378µs |
| Intersection,4×100000 25% overlap |799.52µs |smaller-side binary |0.643µs |
| Difference output,4×100000 25% overlap |742.39µs |source binary |0.714µs |
| Difference output,100000×4 25% overlap |2.224ms |copy then descending find-remove |66.17µs |
| Difference reset+in-place,100000×4 all overlap |2.165ms |descending find-remove |86.34µs |
| Intersection,4×100000 separated misses |55ns |unguarded binary / range-guarded binary |591ns /20ns |
| Difference output,4×100000 separated misses |111ns |source binary |590ns |

The separated case demonstrates why a size-only dispatch can regress already-cheap work. The single endpoint-check wrapper measured here applies to intersection; applying the equivalent correct no-op/copy behavior to difference is a source-level recommendation requiring public verification.

## Validation and retained evidence

- Exact preserved B1 Runtime compiled under `B1Source`; source hash manifest retained.
- A separate untimed full-grid run passed2,445,444 member/reference checks before the measured run.
- Measured run passed12,227,220 checks,720 timing rows, exit0. Every ordered result member, uniqueness, and the lhs Name string object's identity was checked independently against ordinal HashSet membership, outside timers.
- Both runs produced the same validation ledger SHA256:`fb8e2510ab5337d57e939cb53597eb5d732119570a7fb42bbf9364e3d321ddd7`.
- Files:`Evidence/raw.csv`, `Evidence/summary.csv`, `Evidence/validation.csv`, `results.log`, `validation/validation.csv`, `validation.log`.
- Every raw numeric row was flushed before post-sample validation. No failures and no repeat runs.
- Total measured sample time:12.931 seconds. Per-sample duration10.664µs–54.575ms; baseline merge samples9.545–45.170ms. Short contender samples result from reusing the slower baseline's call count. Very large speed ratios are only coarse attribution evidence.
- Actual runtime:.NET8.0.31,Linux,9 reported processors,1GHz stopwatch. Tiered compilation disabled; exclusive execution window. This is not Unity Mono/IL2CPP evidence.
-20 directed cells,60 operation groups,180 algorithm cells,4 alternating samples each. Candidate-specific pilots capped work; this is not an equal-call-count public-API comparison.
- External wall-time limit180s with5s kill grace; finished normally. README and harness document calibration and limits.

## Minimal public verification to follow

Use public intersection target/factory APIs and in-place difference, covering both skew directions and intersection lhs/rhs aliases. Retain4↔1024 and4↔100000 with separated misses, interleaved misses,25% overlap, and100% smaller-set overlap. Keep a balanced control in the historical matrix. Add1/8/64 only as needed around the actual candidate dispatch, plus clustered-near-start input where merge can terminate early. Validate full result values and lhs representatives there too. Do not claim an exact crossover from this narrow screen.
