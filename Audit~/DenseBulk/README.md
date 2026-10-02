# Fixed slices and genuine in-place mixed append

Research branch continuing PR17, measured baseline `b23ce46f8b74033f18456734f4615b56bae90ede`.
No production migration, merge or release. Generated same-name alternatives must be compiled separately.

## Five fixed compiled alternatives

|Name|Forward Dense cursor|Micro <- Dense append|
|---|---|---|
|direct|PR15 unchanged generic traversal|PR15 repeated find/insert|
|split|PR17 unchanged Low/alignment cursor|PR17 repeated find/insert|
|fixed|fixed 16-bit slices after skipping zero words|unchanged PR17 insert|
|bulk|unchanged PR17 forward cursor|exact-length pass and backward merge|
|combined|fixed slices|exact-length pass and backward merge|

Compare fixed/split and bulk/split independently, combined/fixed and combined/bulk for interactions, and all against the fixed direct baseline. No per-row winner, density threshold, layout selector or timed input relabeling.

The generator verifies the exact PR15/17 generated source SHA256s. Public Records, Enumerator, exact lookup, remove, ordinary clone, instance fields, homogeneous kernels and builders stay unchanged. The new private cursors are operation-local values, not fields or additional membership storage. The inherited Micro16 bound and uint-only SIMD/wide compilation limitation are not extended.

## Fixed traversal invariant

A nonzero 64-bit word is loaded once and consumed in at most four fixed 16-bit shifts; zero words are skipped. `nextId` identifies the next original slice. When pending becomes zero, rounding up to the next word prevents re-reading the last word. Reverse traversal mirrors the invariant, consuming high slices first and rounding down before stepping to the preceding word. Only fixed 16-bit shifts are used, never a shift of 64. C# masks ulong shift counts to six bits: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/bitwise-and-shift-operators#shift-count-of-the-shift-operators

## Genuine mixed append and capacity

Admission, same-registry and alias checks stay before helpers. The source Dense array and output Micro records cannot alias. Count is always eager and exact. The first ascending pass counts missing record keys with two monotone indexes. The second pass merges the source backwards into the destination, decrementing duplicate bits from the input count sum. If m is the old record count, r is the source record count and W is its dense word count, the merge/traversal is O(W+m+r), with O(1) operation-local storage excluding required output growth. It does not repeatedly binary-search and shift the destination suffix.

During backward merging `write >= read`; no unread left record is overwritten. Once the source is exhausted the remaining left prefix is already correctly placed.

Importantly, growth still walks the old capacity ladder by calling ReserveEntries(currentCapacity+1), until the exact result fits. It does NOT reserve an input-cardinality upper bound, change array sizes, or allocate scratch arrays. This intentionally preserves total managed allocation bytes and final capacity of repeated scalar insert. Allocation-failure partial-mutation timing is not promised to match the former algorithm.

## Tests and measurement

Reuse complete prior mixed/LocalCursor/Micro suites and protected production suite. Add all 65536 masks in each of the four word slices through CopyFrom and changed mixed append, 1920 scalar-insertion growth/allocation comparisons, exact record-count reservation, high tails, both alias directions from inherited tests, subset and continued mutation checks. Combined is also tested with hardware intrinsics disabled and as a separately compiled netstandard2.1 library hosted in net8. Native Unity/IL2CPP/Burst/phone is not covered.

Full prior public-operation matrix (2520 cases), both-input paid lifecycle (192 cases), plus prepend/tail/subset/interleaved append scaling (48 cases) per variant. Fresh copy+append really clones first; prepared copy+append resets via CopyFrom. All variants use identical inputs and allocations. Each has same-binary A/A, seven samples and three shuffled process rounds, fixed 0.5ms calibration, single CPU affinity, tiering/ReadyToRun off, retained GC and slow samples. The repeated 5% screen is NOT a confidence interval or causal attribution.

```sh
python3 'Audit~/DenseBulk/run.py' --output artifacts/results --rounds 3
python3 'Audit~/DenseBulk/review.py' --root artifacts/results
```

CI pins SDK 8.0.425 on x64 and ARM64. Full-history Git checkout is required for automatic baseline regeneration. The runner archives the actual Git commit and checks protected production bytes. The reviewer reconstructs the Git tree, generated variants, medians, allocations, capacities and A/A. The collect job downloads both original ZIPs, verifies their digests and reruns their archived reviewer, requiring equal structured results and cross-architecture sources.

For offline verification only, `--snapshot <previous-platform-artifact-root> --sdk <installed-version>` reads the already verified PR17 generated baseline and checks all protected source hashes. This produces explicitly labeled local snapshot evidence, not a Git/CI/Unity claim. `--smoke` reduces only the old bench/pair dimensions; the full correctness suite and append scaling still execute. Local SDK/runtime results must never be spliced into pinned CI timings.
