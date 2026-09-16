# Follow-up for the nine CI alerts

## Scope and source identity

This is a targeted follow-up to `364eac88e474bbf147d34b8df39c7e9ac9a3c573` on PR #1.
The original nine alerts compared that commit with `35c5b35da32fb6503771a7849d7436c1578976d4`.
The watched keys remain the six empty exact-query cases at depths 1/4/8,
`filter.exact` at sizes 128/512, and `hierarchy.miss.d4` at size 8.

The query/search and intersection methods were unchanged between those two commits.
That matters when interpreting tiny shared-runner differences: the old alerts alone
cannot establish an algorithmic regression. They are not waived or deleted.

## Runtime changes

Only `GameplayTagContainer.HasTagExact` and the merge branch of
`IntersectionExactInto` change. No serialized fields, public signatures,
mutable caches, pools, dependencies, or registry/query semantics are added.

* An empty exact query returns before reading the query name or entering binary search.
* Exact intersection uses local list references and writes directly to the output
  list. For non-tiny inputs, binary positioning skips a prefix that cannot possibly
  intersect the other input. Inputs of eight or fewer elements retain a direct scan;
  the supplementary matrix checks 1/2/4/8/9/16/32/128/512, rather than assuming
  that the threshold is optimal on every backend.
* The existing asymmetric-input algorithm and input/output alias contracts remain.
  Prefix positioning happens before the first output write. Compaction never
  overwrites a not-yet-read input element.
* Output allocation remains proportional to actual writes. No input-sized buffer
  is reserved for empty or sparse intersections. A trial that preallocated the
  smaller input size was rejected because it increases sparse-result retention.
* A trial adding an aggressive-inlining hint to the search helper was rejected;
  the final change contains no inlining attribute or unsafe access.

The hierarchy-miss implementation itself is unchanged. A better repeat timing for
that case must not be described as a new hierarchy-search algorithm.

## Reproduction and evidence

Keep the original full audit, including every warning:

```sh
python3 'Audit~/run.py' --output ../full-audit --rounds 3
```

Supplement it with fixed longer batches on the same machine:

```sh
python3 'Audit~/perf_regression.py' --output ../perf-nine --rounds 6
```

Both commands require a Git checkout containing the pinned history and .NET 8.
The workflow pins SDK 8.0.425. The focused fixture is compiled identically against
both frozen references and the current source. Six rounds alternate forward/reverse
process order. Each process retains all seven raw samples, allocation measurements,
and result capacity. Empty queries use 3,000,000 calls per sample, rather than the
original 30,000; the original full-matrix measurements are not replaced.

The focused fixture adds a fixed-seed differential suite covering prefix orientation,
tiny-size boundaries, both output aliases, empty/non-empty transitions, sparse
retention, actual-result-sized Into buffers and a positive allocation control.
Its extra benchmark rows cover tiny through large overlapping sets, interleaved
empty/one-element results and disjoint ranges. All extra rows are reported, not only
improvements. Invalid or missing watched rows fail the comparison checker.

GitHub Actions uploads the full audit, focused audit, exact source ZIP and commit
identity together. Runtime source SHA256 values accompany the focused summary.
All final quantitative claims must refer to that run's actual output, not an older
run or exploratory local timing. A ratio above 1.05 remains an alert; a ratio below
it is not a statistical proof, native-platform result, or stable release approval.

## Unchanged release boundary

The PR remains a prerelease. Actual Unity/IL2CPP, target-device CPU and native/peak
memory evidence and human review are still required by `Documentation~/RELEASE.md`.
No performance patch, green managed job, or improved host sample changes that gate.
