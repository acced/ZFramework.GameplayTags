# Fixed evolution acceptance audit

This suite supplements, never replaces, the existing main, Bulk, Registry,
DeepHierarchy, Intersection, generator and package checks. It uses only the
public runtime API and an ordinal name-prefix/depth oracle. No runtime source is
patched. Freeze these files before comparing optimization versions.

## Run

```
python3 Tests~/Evolution/run.py --dotnet /path/to/dotnet \
  --before /absolute/f7c99546-checkout --after /absolute/candidate-checkout \
  --out artifacts/evolution-v1 --samples 14
```

Use `--test-only` for correctness without timing. Output must be new/empty.
`--padding 50000` repeats the identical selected tag workload with a larger
registry, exposing caller-owned workspace retained-memory scaling. Compile and
run this suite only when other timing jobs are idle. The runner requires an
already installed .NET 8 SDK and reference packs; it installs nothing.

## Fixed matrix and semantics

Correctness includes explicit sizes 1, 4, 8, 9, 16, 128, 1024; 0/50/100 percent
nominal overlap (integer truncation for n1); fresh, deep-dirty reused and both
aliased intersection outputs; both operand orders; default and explicit
workspaces; counted-input normalization; source preservation; full result drain.
Existing Intersection tests retain proxy/storage alias and parallel-workspace
coverage, and must still run.

The deep matrix makes **every** node explicit at depth 128, 1024, 3000, in
ascending, descending and deterministic shuffled insertion order. Copy, union,
intersection, workspace alias, counted copy and counted intersection results are
removed in both depth directions. Each removal checks explicit/closure counts
and root/middle/leaf membership. Expected closure is derived from remaining
depths. Counted source reference counts are independently checked as suffix sums.

Timing covers n4/16/128/1024, each overlap, reused default/workspace intersection,
fresh intersection, cold-growth output+workspace intersection, fresh union and
both alias restore+intersection pairs. An alias row includes restoration and is
never presented as isolated intersection time. Cold-growth rows have warmed code
but create a new output and workspace on **each** measured call; this is cold
storage growth, not process/JIT startup. Fresh output allocation is included.
Source construction, registration, oracle checks and delegate construction are
outside the timer. Timed results escape and feed a matching observable checksum.

Retained-memory rows use 64 independently held results, or result+workspace bundles,
with holder arrays allocated before the full-GC baseline. The latter include a
two-reference bundle array per object. Values are approximate managed live-heap
deltas, not allocation, RSS, Unity memory or native memory. No warmed allocation
claim substitutes for retained-memory evidence. Registry startup/build measurements
belong to the separate fixed registry audit.

## Evidence and acceptance

Both runtimes compile before any tests/timing. Full correctness runs precede timing.
14 independent process pairs alternate AB/BA. Raw process JSON, test output,
runtime/assembly/source/harness hashes, commands and execution order are retained.
Source mutations invalidate the entire run. The selected fixtures, row IDs,
iteration counts, runtime and checksum must match before comparing results.

`comparison.json` supplies conservative review flags, **not automatic approval**:

- Allocation growth is flagged regardless of timing.
- Paired log speedup gets an approximate 99% t interval; less than 14 pairs is
  inconclusive. Changes exceeding 3% with the interval entirely beyond that band
  are timing improvement/regression flags. Smaller effects remain inconclusive.
- Retained increase above 5% plus 16 bytes/object is flagged for repeat inspection;
  small or noisy deltas are not proof of equal memory.
- These are investigation thresholds, not permission to accept a regression.
  Review complete distributions, GC noise, independent repeats and real workload
  significance. Do not count noisy/no-op variants as verified improvements.

Each counted optimization needs a distinct immutable commit, full existing tests,
an attributable repeatable benefit, fixed-matrix guard results, and comparison to
both f7c99546 and the previous accepted version. Startup/memory improvements may
qualify when measured by their fixed registry audit, with no material hot-path
regression. Preserve rejected versions as rejected; do not relabel failures.

All these results are standalone .NET 8. Stub compilation is not Unity execution.
Standalone Mono, Unity Editor Mono and IL2CPP each require separately labeled,
same-backend baseline/candidate comparisons. Missing Unity evidence remains an
explicit verification limit, even when all standalone checks pass.

### Guard rows and timing length

Additional fixed guards cover HasAny/HasAll exact and hierarchical predicates
with self, nonself and empty requirements at n16/n1024; AddTags self and a
restore+bulk-add pair; dense/sparse restore+Clear pairs. Deep union guards combine
all-explicit ascending/descending chains at depth128/1024/3000 with a disjoint
single explicit leaf whose 3001-node closure is larger than the source closure.
All guard semantics also execute in `--test-only`.

A before-only pilot calibrates each timed row to a 200ms target, then writes
`iterations.tsv`. Those per-row iteration counts are fixed and identical in all
subsequent baseline/candidate processes. `--batch-ms` changes this target; values
below50 are rejected. Rows report batch wall time, process CPU time and GC
collection deltas. Any actual batch shorter than50ms is flagged for a longer
rerun, including a candidate that becomes much faster. Pilot data is not counted
among the 14 measured pairs.

### Standalone Mono

Pass `--mono /path/to/mono` to use the included net472 project and the existing
.NET SDK/package cache for compilation. Dependencies match the repository's
existing Intersection Mono target (Microsoft.NETFramework.ReferenceAssemblies,
System.Memory and System.Buffers). JSON output has no serializer dependency, and
the per-thread allocation counter is resolved once by reflection; an unavailable
counter fails rather than reports zero. Run baseline and candidate on the same
Mono installation in a separate output directory. Never combine Mono and .NET
ratios. This remains standalone Mono, not Unity's fork.

`--before-revision` and `--after-revision` can label known frozen archives.
Provenance labels never override source hashes. Git HEAD is recorded only when
the selected runtime checkout is itself that repository root; nested archives
cannot borrow an ancestor repository's HEAD. Runtime dirty status and diff hash
are recorded for actual repository roots.

For a very large improvement whose candidate batches become short, copy the prior
complete iteration map, raise only the affected counts, then use
`--iterations-file /path/to/reviewed.tsv` for a new paired run. The complete map
must contain every timed row exactly once with positive counts, is copied into
that run, and is hashed. Both variants still run identical operation counts.
Raising all rows with `--batch-ms` is also supported. Review allocation flags
against all preserved raw samples; do not suppress persistent byte increases.

Mutation guards also measure warmed RemoveTag+AddTag pairs and duplicate insertion
for a 1024-leaf shallow source and depth128/3000 single-leaf sources, plus fresh
single-tag construction at those depths. The fresh row includes container/storage
creation and growth; successful removal plus restoration is one reported pair.

After the first accepted optimization, add `--previous /last/accepted/archive`
and optionally `--previous-revision SHA`. The runner compiles all three versions
before measurement, alternates forward/reverse process order, and reports
candidate-versus-original and candidate-versus-previous comparisons from the
same rounds. Reuse the first frozen iteration TSV for that backend in subsequent
versions, unless a separately documented longer shared-count rerun is needed.
