# General intersection tests and paired benchmark

.NET 8:

```sh
dotnet run --project Tests~/Intersection/GameplayTags.Intersection.Tests.csproj -c Release
```

The project compiles the real Runtime source into its test assembly to inspect
integer-sort ranges and internal ancestor contribution counts. The oracle derives
ancestors using name separators, not the runtime's DFS IDs. Coverage includes
random forests, mutated layouts, two output aliases, counted sources, proxy aliases,
dirty workspace reuse across different targets, independent worker workspaces,
3000-level mixed explicit ancestors, grow/shrink transitions, full deletion, sorter
sentinels/extremes/duplicates/reversed/organ-pipe inputs and heap fallback.

Allocation assertions use a cached per-thread GC counter, with delegates and 200
warmups outside the measured loop. The complete general intersection, both aliases
(including restoration), count normalization and integer sorter must report zero
warmed bytes. These assertions do not claim zero cold/growth allocation.

Standalone Mono (NOT Unity's fork):

```sh
dotnet build Tests~/Intersection/GameplayTags.Intersection.Tests.csproj -c Release -p:TargetFramework=net472 -o .artifacts/intersection-mono
mono .artifacts/intersection-mono/GameplayTags.Intersection.Tests.exe
```

`net472` uses pinned .NET Framework reference assemblies, System.Memory and
System.Buffers. No such packages are added to the portable runtime project.
A runtime without `GC.GetAllocatedBytesForCurrentThread` fails allocation tests,
rather than silently treating unmeasured allocation as zero.

The benchmark supports `--bench [unused_registry_tag_count]`. It reports TSV rows
with full-operation nanoseconds, bytes, and iteration counts; result creation is
inside new-result measurements. Source creation and workspace growth are prewarmed.
A 50%-overlap case excludes hundreds of explicit tags, so does not exercise only
the complete-match or copy-minus-eight shortcuts. The BCL segment-sort row is a
separate allocation control, not an assumption about every Mono revision.

`paired.py` runs before/after executables in AB/BA order in separate processes,
defaulting to 14 samples/version/case, saves every timing/allocation sample and
prints medians without asserting noisy timing thresholds. Pass `--padding 50000`
to repeat a fixed selected workload in a larger registry.

Compile baseline `21e2d93` using `-p:RuntimeRoot=<absolute baseline Runtime>` and
`-p:Baseline=true` into a separate output/intermediate directory. Baseline builds
omit workspace/specialized-sort calls and zero-allocation assertions, but still
report BCL allocation. Do not compare the baseline checksum to the current one:
the current executable deliberately runs additional workspace/sort cases.

The Unity probe in `Samples~/IntersectionDiagnostics` must also be run in the
actual target Editor. `-p:ProbeCompile=true` only checks its source against stubs;
it is not a Unity execution result.
