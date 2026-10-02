# Reuse first-pass facts to bound reverse mixed append

Continue PR18 commit79d4dc4. Three fixed independently compiled variants: `fixed` (fixed forward slices, repeated insertion), `combined` (fixed slices, full-domain reverse merge), and `bounded` (combined with reverse bounds derived from its existing first pass). No thresholds, adaptive representation selector or per-row winner.

## Exact scope

The existing first pass already emits every source record to determine the exact result record count. Bounded also counts those emissions and retains the final emitted key. The private forward cursor's Current field explicitly remains unchanged when MoveNext returns false; this does not rely on an arbitrary IEnumerator guarantee.

Reverse traversal starts at the machine word containing that final key, not at the end of the global registry. After exactly the first-pass number of source records is consumed, the caller stops without a final scan toward the beginning of the registry. Stable, nonempty Dense input is guaranteed by existing admission and alias handling. Each consumed source record decrements the local counter exactly once. Remaining destination-only prefix is already in place under the original backward-merge invariant.

No new stored-set or cursor fields. Only operation-local integer bookkeeping changes. Both private cursors retain their previous24B size; Records16B and public Enumerator32B stay unchanged. No additional output/scratch allocation, no changed source or output ownership, no Count deferral. Capacity growth still follows the original staircase and is tested against scalar insertion.

Forward traversal still scans the entire Dense domain once. Reverse traversal still scans empty gaps BETWEEN source records. This is not sparse metadata, a new occupancy index, or elimination of the first pass. It adds a per-record counter update/termination check; it is not assumed universally faster.

## Reuse existing tests and measurement unchanged

Small audit wrappers reuse the pinned PR18 runner and verifier, selecting three candidates and the bounded implementation for hardware-disabled/netstandard tests. They do not alter timed loops, sample counts, dataset seeds or expected allocation contracts. The inherited suite tests all65536 masks in each word slice, high tails, aliases, sustained mutations,1920 exact growth/allocation comparisons and actual-result-capacity zero-allocation.

Run from full-history checkout with SDK8.0.425:

```
python3 'Audit~/DenseBounded/run.py' --output artifacts/results --rounds 3
python3 'Audit~/DenseBounded/review.py' --root artifacts/results
```

Full2520 public-operation cases,192 both-input-paid lifecycle cases,48 append-shape cases; three rounds of seven samples, same-binary A/A for each implementation, all slow/GC samples retained. The5% screen is not a statistical confidence guarantee. Collect redownloads and verifies hashes/source tree/binaries, reruns the archived reviewer and requires equal structured results.

At submission: bounded locally compiled and passed36,631,683 inherited mixed/bulk assertions,0failures on SDK8.0.423/.NET8.0.29. Local whole-run timeout is NOT a full local-run completion claim. No new CI results at submission, no production migration, Unity/IL2CPP/Burst/mobile result or all-metric-win claim. PR18 full CI results belong to PR18's separate artifact and are not attributed to bounded.
