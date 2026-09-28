# Dense-only continuation: algorithms, measurement and limits

## Fixed incoming implementation

This pass continues PR #3 / `refactor/dense-only-indexed-20260928` from
`3939cb8cb55746d37d5db0c6ab9c7483415a26f8` (tree
`789cb0e6eaf5680f4366732a90fad9d941190199`). It does not modify or merge main,
old optimize, or the Auto/Sparse branch.

## Runtime changes

The Dense member array and 64-way nonzero-word summary hierarchy remain the
only member storage. A stack-local cursor reads a nonzero first-level mask
once, then iterates its set bits with `mask &= mask - 1`. Indexed bulk paths
publish a whole 64-word block's occupancy once instead of doing a successor
search and ancestor propagation for every member word. Small-population copy
and union do not copy or OR the entire summary suffix. Full-array kernels
remain for sufficiently occupied inputs; the earlier portable Harley-Seal
count kernel and eager Count maintenance remain inside public operations.

No new persistent RuntimeTagSet field, array of IDs, Sparse/Auto API, pool,
copy-on-write, shared output or runtime dependency was added. The enumerator
has more scalar cursor state than before; this is a stack/value-type cost,
not an additional persistent member representation. Two targeted inlining
hints keep checked exact membership in the caller; foreign registry checks
remain. They are subject to the same public-API benchmark as other changes.

Algorithm background (not a claim of platform-optimal thresholds):
- Hierarchical 64-way bitmap successor structure:
  https://hitonanode.github.io/cplib-cpp/data_structure/fast_set.hpp.html
- Clear-lowest-set-bit iteration and portable bit operations:
  https://graphics.stanford.edu/~seander/bithacks.html
- The retained carry-save count adaptation and license are documented in
  `THIRD_PARTY_NOTICES.md`. No new third-party code is imported by this pass.

## Why the earlier allocation failure was not waived

The incoming audit failed when combined timing/allocation samples reported
nonzero bytes inside already-prepared queries. Merely separating Stopwatch
calls or moving the fixture to a worker did not resolve it. Diagnostic run
36397902855 preserves that unsuccessful hypothesis.

Run 36399050455 then replaced exact-query actions with a primitive integer
increment. The negative control still reproduced nonzero allocation (about
6.6 KB in a 300,000-iteration sample), without calling any tag-set method.
All samples remain in its artifact. The same diagnostic used two full GC
collections with a finalizer wait between them after setup/warmup and before
timing. Both real and negative-control fixtures recorded zero allocation in
both full repetitions of all prepared scenarios with that preparation.

This isolates a setup/host effect from the lookup body, but does not identify
the exact allocating runtime component. We do not call the previous nonzero
samples zero, subtract their bytes, discard outliers or disable the counter.

The common fixture now labels its protocol `settled-setup-v2` and applies
that identical preparation to ALL libraries and operations. Allocating-result
constructors and real copy/mutation calls stay inside the clock; there is no
collection inside a measured action loop. Every process includes a real
positive allocation control. The strict prepared-Dense check still rejects
any nonzero sample. Historical unsettlement diagnostics are retained; new
samples must not be combined with old-protocol timing. This is controlled
microbenchmark preparation, not an instruction to call GC.Collect in games.

Reference for controlled GC in benchmarking:
https://benchmarkdotnet.org/articles/configs/jobs.html
Reference for the allocation counter's scope:
https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread

## Acceptance and unavoidable costs

`dense_block_check.py` runs additional multi-level/dirty-output/alias tests
and the actual shipping CountWords oracle before benchmarks. `dense_resume.py`
compares current code to the exact incoming commit under the same common
fixture. Earlier initial-Dense, Auto, old optimize, main and Alex comparisons
remain; empty and non-winning cases are not removed.

A flat Dense buffer still needs space proportional to the registry size,
not the current population. A newly allocated independent container must
allocate and initialize this storage. Summary traversal reduces repeated
CPU scans, not this allocation. Prepared-object comparisons are additional
results, never replacements for allocating comparisons. The request for
universal wins is an acceptance target, not an implemented guarantee.

All native Unity/IL2CPP/device gates remain pending. Draft prerelease only;
no stable release approval follows from these managed tests.
