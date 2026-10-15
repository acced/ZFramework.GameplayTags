# Copy / Union / explicit Intersection benchmark

This harness compares the same public API workload compiled against two selected
Runtime source trees. It was added after `3f10ba4` to investigate bulk operations.
The earlier report in `Documentation~/results/` remains a separate experiment.
The new report belongs in `Documentation~/results/bulk/`.

This is a standalone .NET 8 experiment. It does **not** reproduce measurements from
Unity 6000.3.14f1 Editor, Mono, IL2CPP, or an Alex implementation whose source and
fixture have not been supplied. Treat the results as evidence for selecting this
library's implementation on the recorded machine, then measure the target Unity
backend with the application's real tag distribution.

## Reproduce the paired run

Freeze the before revision without changing the working checkout:

```sh
mkdir -p /tmp/gameplaytags-pre-bulk
git archive 3f10ba49433d8b5fdd394a7ac2f1dce32800fd7c | tar -x -C /tmp/gameplaytags-pre-bulk
python3 Benchmarks~/Bulk/run.py \
  --before /tmp/gameplaytags-pre-bulk \
  --after . \
  --samples 14 \
  --out artifacts/bulk-results \
  --dotnet /path/to/dotnet
```

The direct compiler runner needs an installed .NET 8 SDK beside the `dotnet` host.
It reads its reference assemblies and invokes SDK Roslyn without MSBuild, NuGet,
network access, Unity, dependency shims, or modifications to either Runtime tree.
Both versions use the **same current** `Benchmarks~/Bulk/Program.cs`. It compiles
both before running any timing processes, so compilation does not overlap samples.

With a working SDK CLI the equivalent single-process workload can also run via:

```sh
dotnet run -c Release --project Benchmarks~/Bulk/GameplayTags.BulkBenchmarks.csproj \
  -p:RuntimeSourceRoot=/absolute/path/to/runtime-checkout \
  -- --variant standalone --round 0 --out artifacts/bulk-single.json
```

The project and direct runner disable tiered compilation and server GC. These
settings are recorded in each raw process report. For a short correctness smoke,
add `--samples 1 --quick`. For selection among implementation candidates, add
`--candidate kahn=/absolute/path/to/candidate` to the Python command. `--filter`
selects a case ID substring such as `n1024`, `copy`, or `depth128`. Omitting
`--after` collects only the before implementation. The runner refuses to overwrite
a nonempty output directory; preliminary experiments should use separate paths.

## What is measured

Each bulk operation is **one call** to `GameplayTagContainer.Copy(destination, source)`,
`Union(left, right)`, `Intersection(left, right)`, or the existing
`Intersection(destination, left, right)` overload. New Union and Intersection
results must be independent, mutable containers; their allocation is inside the
timer. Copy and output-reuse Intersection reserve their destination outside the
timer and measure steady reuse. For these cases, source construction, destination
reservation and warmup allocation are excluded.

The main matrix has 1,024 explicit shallow leaves spread across 32 groups, each
three levels deep. A source therefore contains 1,057 distinct tags with ancestors.
Operands are inserted in different deterministic, unsorted permutations. Their
explicit-set overlap is 0%, 50%, or 100%; the 100% cases use distinct objects.
Separate cases test the same object as both operands. At 0% overlap, the union has
2,048 explicit leaves and 2,081 distinct tags; at 50%, it has 1,536 and 1,569.
Capacity boundary effects are therefore part of the recorded result, not assumed
to be identical for all overlaps.

Other cases cover:

- Copy to a destination reserved for the source's total tag count and one reserved
  for eight times that count. The recorded capacity is the public reservation
  request; private array capacity is deliberately not inspected.
- Copy that alternates two disjoint sources of equal size into the same destination.
- A counted source with multiplicity 2–4, whose output must be normalized to a set.
- New and reused Intersection, no overlap, complete overlap, and empty operands.
- Narrow 16- and 128-explicit-tag samples, plus 128-level deep paths.
- Deep counted Copy with one explicit leaf, and tiny Intersection in which a deep
  leaf is excluded from a shallow result, in both operand orders.
- Diagnostics after Copy to a destination reserved at 1× or 8× the source's total
  tag count: `HasTagExact` with 64 cached probes (50% hits, 50% misses), and
  `RemoveTag` followed by `AddTag` using 64 cached present leaves. Their timed
  regions exclude Copy itself. Query cost is one call; mutation cost is one pair.
  Query checksums also consume the boolean result and are checked against the
  fixture's expected hits. These rows guard query/mutation behavior after Copy
  adopts a smaller active hash-table mask in an oversized backing array.

`ExplicitTagCount` in metadata is the fixture's nominal size; actual left, right,
result, and closure counts have their own fields. `HierarchyDepth` is the maximum
path depth in that fixture. Empty and same-instance cases are labeled separately
because their short fast paths should not be generalized to the populated cases.

## Timing, allocations and independent correctness checks

The runner launches one process per version per round. With two versions the order
is before/after in even rounds and after/before in odd rounds. The case order
rotates identically in both processes each round. Registration, fixture building,
semantic preflight and 64-call warmup run before the timer. Full GC also runs before
each timed batch; collections that occur during the batch are recorded.

Each timed iteration reads the result's explicit and total counts into an observed
checksum and publishes its reference with `Volatile.Write`. A final checksum also
escapes the batch. The timed loop calls the public generic APIs directly; it does
not use reflection, per-call delegates, name lookups, fixture mutation, or a model
oracle. For very short empty/self cases, consumption and loop overhead become a
material part of the measurement; no empty-loop subtraction is applied.

Before and after timing, a separate `HashSet<string>` model compares full explicit
sets and their string-prefix ancestor closure. Order-independent hashes record
the actual names rather than just cardinality. Input hashes include counted-source
multiplicities. A sentinel mutation checks source/result independence; consecutive
new results must also be different objects with independent contents. Removing
each explicit result tag exactly once must leave zero explicit and total tags.
Alternating Copy preflight checks both source contents even though their counts
are equal. These checks are outside the measured region.

The runner checks all paired fixture metadata, checksum values, runtime settings,
case IDs, and iteration counts before computing ratios. It verifies that source
and workload files did not change during sampling. It reports the median of 14
independent samples by default; for an even sample count, that is the mean of the
two central sorted values. Minimum, maximum, raw times, allocated bytes and GC
collection counts are preserved. Allocation means
`GC.GetAllocatedBytesForCurrentThread()` during warmed operations, not retained
heap size or total process memory. No performance assertion is a CI gate.

Run the regression suite separately, outside a timing window:

```sh
python3 Tests~/Bulk/run.py --dotnet /path/to/dotnet --source /absolute/path/to/runtime-checkout
```

## Files emitted

- `raw/<variant>-<round>.json`: untouched per-process measurements and checksums.
- `<variant>.json`: medians and all samples for every case.
- `comparison.csv` and `comparison.md`: joined by unique case ID, with hierarchy,
  overlap, cardinalities, time ratios and allocation data.
- `environment.json`: exact commands, runtime/SDK/OS/CPU, sampling order, baseline
  revision, assembly hashes, and individual SHA-256 hashes of Runtime and harness
  files. Runtime files excluded from compilation are named explicitly.

Close other CPU-heavy work during measurements. Machine load, CPU scheduling,
runtime version, GC settings and data distribution all affect results; process
alternation controls ordering bias without making this a universal ranking.
