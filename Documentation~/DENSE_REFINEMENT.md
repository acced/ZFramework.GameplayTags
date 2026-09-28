# Dense refinement after the first measured candidate

The first candidate is pinned at `5b151da228ddebbd8119a2975e674ad6d28cb424`. Its complete, unfavorable evidence remains at https://github.com/acced/ZFramework.GameplayTags/actions/runs/36391142613 . That run passed managed semantics, but won only 30/160 allocating-matrix points and 53/160 prepared-matrix points against all included references. It did not satisfy universal performance dominance.

## Changes prompted by that result

- Exact membership now checks the already-loaded bitmap array's length rather than separately loading cardinality. An unallocated empty buffer remains safe; allocated empty buffers contain zero words. Registry identity is still checked first.
- Low-occupancy copying clears only old member words, copies occupied input words, then copies the compact directory once. It no longer rebuilds ancestor bits individually for each copied word. The directory is transiently old only inside this non-callback, single-owner operation and is replaced before return.
- Independent low-occupancy union combines directory levels first, then visits exactly the union's occupied words. It no longer copies one input and separately mutates it with the other.
- Dense append and union process two words with independent population-count accumulators. Count remains eager and part of the timed operation.
- Dense deletion uses a direct member-word loop. Difference cannot create a nonzero word, so only newly empty words update ancestors. The first candidate's repeated indexed successor/StoreWord calls are avoided for this case.
- Linear kernels start at one-eighth occupied member words rather than one-quarter. This is a performance policy, not a storage mode. `DENSE_ONLY.md`'s initial one-quarter description is historical; the current source's one-eighth choice supersedes it. Both old/new policies are compared with unchanged inputs, and the old production source remains a reference.

These are candidates until the current commit completes CI. They do not remove the U-bit allocation requirement. Large-registry/small-member allocating results may remain much slower than Auto's integer arrays; that outcome must stay visible.

`dense_results.py` independently rejects benchmark correctness failures, missing rows, non-finite samples, input identity mismatches and any prepared candidate allocation. It retains all 160 rows per variant and every losing performance point. It also builds the pinned initial Dense source and the current source against the same generated fixture, alternates fresh processes for both allocating and prepared workloads, and retains all raw samples and any new regressions. A green job is not a stable release approval.
