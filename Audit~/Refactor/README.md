# Actual runtime refactor measurements

This is a C# execution harness. It does not import the lifecycle simulator's predictions as timings. The same harness is compiled against frozen original and candidate Runtime source trees. No production files are modified by the runner.

## Reproduce

Requires an installed .NET 8 SDK; no NuGet packages or network access are needed after installation.

```sh
python3 -m unittest discover -s Audit~/Refactor -p 'test_*.py' -v
python3 Audit~/Refactor/run.py \
  --original /path/to/unmodified/source \
  --candidate . --output /path/to/new-evidence-directory \
  --dotnet /path/to/dotnet --suite full --rounds 3 --samples 11 --target-ms 1 \
  --aa --portable --legacy --alex-source /path/to/alex/git-repository
```

For a quick correctness/performance smoke use `--suite smoke --rounds 1 --samples 7`. Output directories must be new: existing evidence is never silently replaced. SourceRoot can also be passed directly to the project, e.g. `dotnet build Audit~/Refactor/Refactor.csproj -c Release -p:SourceRoot=/absolute/source`. Set `-p:RefactorApi=true` only for a source that includes the new `ToStorage` and `DifferenceExact` APIs.

The runner snapshots both Runtime trees and the C# harness, records SHA-256 inventories and environment/tool versions, compiles optimized Release assemblies, and executes original/candidate × Auto/Sparse/Dense sequentially. `--aa` adds independently labeled fresh processes of the exact original executable to quantify A/A host noise. `--portable` builds identical candidate source with `GAMEPLAYTAGS_FORCE_PORTABLE`, enabling a managed-host comparison of the portable kernels used by .NET Standard builds; it remains a CoreCLR measurement, not a Unity measurement. Independent rounds rotate and reverse process order. Avoid other CPU-heavy jobs during the timing window. Tiered compilation and ReadyToRun are disabled consistently for every build. On Linux, the runner and all child processes are pinned to the first CPU in the process’s permitted affinity set; the original set, selected core and verified final affinity are recorded. On unsupported OSes that limitation is explicit. `dotnet --version` and `dotnet --info` record the actual selected SDK; the repository’s global.json pins it for commands run at the repository root. Original and candidate input digests must match for every row.

## What is actually timed

- Registry validation/build for both grouped and shuffled registration order, including hierarchy and immutable lookup tables, with pre-existing settings/name strings excluded
- Building a new set from already-resolved tags in sorted and reverse order
- Authoring-name conversion, plus an adversarial punctuation fixture where ordinal authoring order disagrees with DFS runtime IDs
- All four directional Sparse/Dense layout conversions through public `new + CopyFrom`, including destination ownership and allocation
- Mixed-hit, all-hit and all-miss resolved exact probes; parent-hierarchy probes; frozen query hit/miss/nested evaluation and query freeze
- Repeated add/remove with pre-reserved capacity; enumeration
- Allocating/reused union and exact intersection, aliased intersection, and copy/remove difference; allocating/reused append
- Actual end-to-end mini-lifecycles: build both operands, allocate destination, then execute 1 or 32 rounds of union + intersection + difference + 32 exact probes + frozen query. Registry building, pre-resolved tag arrays and pre-frozen query are outside these timed lifecycle bodies and are measured separately. No weighted sum is reported as a measured lifecycle
- Candidate-only `FromTags` sorted/reverse bulk construction, direct difference and `ToStorage`, in a separate `candidate_only_rows` collection because the original has no equivalent API. These execute in separate fresh processes after the paired matrix, so their cache/allocation/GC pressure cannot perturb only the candidate side. They are not inserted into paired performance rankings

The full common matrix has 2,633 rows per mode/process. Registries have 65,536 or 262,144 explicit leaf definitions plus implicit parents. Memberships include 0, 1, 7/8/9, 32, 128, 1,024, 4,096, 16,384 and 32,768 at the declared registry coordinates. Distributions are contiguous, four clusters and scattered. Bulk overlap is 0%, 50% or 100% of the smaller operand. Unequal comparisons include 16,384/32,768 versus 1/8/128, in both argument orders, under contiguous and scattered distributions. Shared keys are spread through the large operand, not only concentrated in its first words. The smoke common matrix has 135 rows per mode/process.

Correctness checks against LINQ set-membership oracles occur outside timed regions before and after each row. Results must have exact Count, sorted unique explicit members and unchanged inputs. Probe and query expectations are checked separately. Reused operations, probes, frozen evaluation, enumeration and reserved mutation assert zero measured per-thread allocation. An allocating positive control checks the allocation counter. Assertions are additional checks, not a replacement for package semantic tests.

## Timing and memory interpretation

Calibration doubles iterations toward `--target-ms`, with an 8 MiB allocation budget per measured batch and a fixed cap. Registry construction uses one build per batch. Every raw sample, batch duration and GC count is retained, including slow samples. Operation times are ns/operation; the 256-probe loops are normalized to ns/probe. Delegate/loop overhead is present equally and is not subtracted. Very small nanosecond differences should not be treated as decisive.

`median_ns` is a median of process medians. `p95_batch_mean_ns` and `max_batch_mean_ns` are tails of calibrated batch means, NOT individual-operation latency percentiles. Multiple samples in one process are not independent replications. No significance test or confidence interval is inferred from them. `round_medians_ns` makes process variability visible.

Allocated bytes are cumulative managed allocation on the current thread, not retained/peak/native memory. `BufferBytes` reports member-array payload only, excluding headers and the shared registry. Conversion includes a new independent destination. Prepared reuse preserves destination capacity and reports zero allocation only when actually observed in every measured sample.

## Historical competitors

`--legacy` additionally rebuilds pinned ZFramework main `934b14a47ddf0b6367bf6720be58c18cbcb09896` and optimize `a99f53d4697df6f577961ff1ee1bad14caa221d1` from local Git objects. `--alex-source` also adds pinned Alex `28e4218f459ecf0e9fb4f0bca90805c05eb962c2` from a local Git repository. It executes available competitors plus frozen original/candidate through the existing FourWay C# harness with identical fixtures and validates input digests. These results are separate in `legacy-comparison.json`; that older protocol measures resolved exact lookup and allocating union/copy-append/copy-remove only. It must not be mixed with the richer matrix or presented as hierarchy/query/lifecycle comparison. Omitted Alex source is not silently represented by archived timings. Pinned commits and archive hashes are recorded separately.

## Scope and acceptance limits

These are managed CoreCLR/Linux measurements, not Unity, Mono, IL2CPP, Burst or physical-mobile evidence. The current machine's architecture is captured; another architecture must be run separately. No universal fastest layout, production-policy fit, or shipping approval is implied by a geometric/median summary. Inspect operation groups, original/candidate absolute ns, allocations and worst rows; keep regressions visible. A finite synthetic matrix is not a probability model of real gameplay traffic.
