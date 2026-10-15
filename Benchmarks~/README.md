# Reproducible performance comparison

This harness compares the same workload source against the unmodified original
runtime and the rewrite. Results describe this workload, runtime and machine;
they are not a ranking against other tag libraries or data structures.

## Run the rewrite

```sh
dotnet run --project Benchmarks~/GameplayTags.Benchmarks.csproj -c Release -- --out rewrite.json
```

Use `--quick` only for a short smoke check. Full runs use seven measurement
samples per case, take the median, and preserve all raw sample values in JSON.
JIT tiering and server GC are disabled by the benchmark project and direct-runner
runtime configuration. Both variants use the same settings.

## Run the frozen baseline

Extract the original commit into a separate temporary directory. Do not patch its
runtime methods to make the result look comparable.

```sh
mkdir -p /tmp/gameplaytags-baseline
git archive da8a6d2dbe6fa9124a70051c2f5585a1b4e51583 | tar -x --no-same-owner -C /tmp/gameplaytags-baseline
dotnet run --project Benchmarks~/GameplayTags.Benchmarks.csproj -c Release \
  -p:RuntimeSourceRoot=/tmp/gameplaytags-baseline -p:Baseline=true \
  -p:BaseIntermediateOutputPath=obj/baseline/ -p:OutputPath=bin/baseline/ \
  -- --out baseline.json
python3 Benchmarks~/compare.py baseline.json rewrite.json --csv comparison.csv
```

The baseline repository references unprovided `Log`, `ObjectPool`, `GenericPool`
and `ListPool` dependencies. `BaselineDependencies.cs` supplies only these missing
compile dependencies. It uses `Stack<T>` pools and value-type leases and warms
them before measurement. Valid mutation inputs avoid the disabled logging path.
The shim is an explicit limitation of the comparison: this measures the baseline
algorithms with these supplied dependencies, not an unknown external pool
implementation from the original author's environment.

For an environment that needs direct Roslyn invocation:

```sh
python3 Tests~/run.py benchmark --dotnet /path/to/dotnet \
  --source /tmp/gameplaytags-baseline --baseline \
  --output-dir artifacts/baseline --json baseline.json
python3 Tests~/run.py benchmark --dotnet /path/to/dotnet \
  --output-dir artifacts/rewrite --json rewrite.json
python3 Benchmarks~/compare.py baseline.json rewrite.json --csv comparison.csv
```

Run the two timed processes sequentially, with other builds and tests idle.
`compare.py` rejects different workload checksums or measurement configurations;
it does not set speed thresholds or fail CI because a timing is slow.

## Workload and units

The registry contains 4,096 three-level benchmark leaves in 32 groups. Container
sizes are 4, 16, 128 and 1,024 explicit leaves. A fixed permutation spreads
selected leaves through the registry; disjoint even/odd selections provide
membership hits/misses and disjoint parent-group hits/misses. The benchmark
precomputes 4,096-entry cached-handle query sequences with fixed seeds. Neither
string lookup, attribute registration nor random-number generation is timed.
Measured booleans and enumeration values feed an observable integer checksum.
The root used by `IsParentOf/hit` has 4,128 descendants (4,096 leaves plus 32
groups), regardless of the displayed holder size. The sampled hit distribution
changes with holder size. Parent-membership reads touch at most 16 distinct
parent groups in this fixture. These locality assumptions are part of the
workload and should not be generalized to arbitrary parent fan-out or cache sizes.

| Operation | Reported unit |
| --- | --- |
| Exact hit/miss/mixed membership; parent membership; strict relationships | One call |
| `RemoveAdd/existing` | One successful removal followed by restoring the same tag |
| `AddRemove/absent` | One successful temporary insertion followed by its removal |
| `Count/AddRemove-existing` | One increment followed by one decrement of an already explicit tag |
| `Enumerate/explicit` | One full traversal of the explicit tags |
| `Construct/fill` | Creating and filling one fresh container with the specified number of leaves |

The hot mutation pairs vary the selected cached handle and preserve the fixture
after every pair. They do not divide pair time by two or claim to isolate an
individual remove instruction. Storage and pools are warmed before timing, and
semantic preflight/postflight checks verify membership, closure and counts.
No event subscribers are attached to the counted-pair benchmark; callback
dispatch cost is outside this performance comparison.

Separate cases use one explicit leaf with depth 32 or 128. They measure exact
membership, one explicit-tag traversal, a remove/add pair and a singleton exact
requirement. The singleton baseline `HasAllExact` path is correct and is checked
before timing. General multi-tag `HasAny`/`HasAll`, intersection and event paths
have known original semantic defects and are not used for speedup claims.

## Allocation and memory interpretation

`AllocatedBytesPerOperation` uses `GC.GetAllocatedBytesForCurrentThread` around
each warmed timed batch. Delegate construction, query arrays and registration
are outside the measured region. Construction rows intentionally include all
container and growth-array allocations. Zero warmed allocations do not mean a
container consumes no memory.

Retained live memory is reported separately. A cohort of 128 populated containers
is retained across full GC snapshots; the holder array is created before the
baseline snapshot and excluded from the delta. The report records the median,
minimum and maximum of three such measurements. This is an approximate managed
heap delta, not resident process memory, a native-memory measurement or a claim
about Unity's object layout. The flat hash/dense-entry rewrite can use more
memory than the original compact integer lists, especially with few explicit
tags and many implied ancestors.

Nanosecond results include normal loop and result-consumption overhead. Very
small differences, especially single-digit-nanosecond reads, may vary with CPU
scheduling, caches, JIT decisions and machine load. Review min/max and raw samples
before treating small ratios as significant. The included .NET 8 measurements
do not replace a workload benchmark under the target Unity Mono/IL2CPP backend.
