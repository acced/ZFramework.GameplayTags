# Registry startup and retained-state audit

This fixed workload supplements the public-operation Evolution matrix. Compile
Runtime sources together with the startup-only internal builder so each process
can build independent registries without replacing a live published registry.
The timed operation is all declarations registered plus all frozen state built.
Assembly discovery and input-string construction are outside the timer.

Four fixtures are fixed before selecting registry candidates:

- 4,096 shallow leaves across four roots and 64 groups.
- A shared depth-127 trunk with 256 leaves, each at depth 128.
- One chain with depth 3,000.
- 1,024 depth-eight leaves, each declared eight times with merging descriptions
  and flags.

An independent string-prefix model checks ordinal DFS order, every immediate
parent, every hierarchy slot, intervals, flags and descriptions before and after
each process's timing. Deterministic fixture/output checksums and iteration
counts must agree between versions. The count checksum escapes every batch.

## Paired runs

```
python3 Tests~/Evolution/Registry/paired.py \
  --before /absolute/frozen-before --after /absolute/frozen-after \
  --dotnet /path/to/dotnet --out artifacts/registry-pair --rounds 14
```

Add `--mono /path/to/mono --nuget /path/to/restored/packages` for the separate
standalone Mono run. This uses .NET Framework 4.7.2 references and the repository's
System.Memory/System.Buffers dependencies. It does not download packages. Neither
runtime is evidence of execution in Unity Editor, IL2CPP or a player build.

Both assemblies compile before any timed processes. Process order alternates
AB/BA and fixture order rotates identically by round. `--quick` runs only a smoke
test and is never selection evidence. Keep other builds and tests idle during
paired runs. All raw reports, source/reference hashes, commands, settings and
comparison flags are retained. Timing classifications use a paired-log ratio
with a conservative approximate 99% interval; they are review flags, not a claim
that every unchanged row has passed a formal noninferiority test.

For archived source trees, optional `--before-revision` and `--after-revision`
arguments record caller-supplied provenance labels. These never substitute for
the source hashes or infer a revision from an enclosing checkout. Executable and
package paths are resolved before locating SDK reference packs, including a
`dotnet` path reached through a symbolic link.

`run.py` can compile/run one version; use `--compile-only` to create the executable
and its manifest without timing. Full batches use fixed counts across versions;
the comparison flags batches below 50 ms for review.

## Allocation and memory boundaries

Allocated bytes come from the runtime's per-thread allocation counter. Reflection
binds that counter once outside all measurements for Mono/.NET compatibility.
Input declarations, verification and report creation are not timed or counted.
Each result includes the discarded builder's allocation because startup is the
operation being measured.

Frozen-array payload is an exact managed element-count/element-size calculation.
It excludes array headers, dictionary storage, strings and the snapshot wrapper.
It can establish a structural array-memory improvement independently of GC noise.

Retained bytes are separate full-GC heap deltas over four simultaneous frozen
registries, repeated three times. Input declarations and the preallocated holder
array are excluded. A separate no-inline fill frame limits stale stack roots.
These remain approximate managed-heap measurements, particularly on conservative
Mono GC. Any sample below its known frozen-array payload is explicitly flagged
unreliable and must not support a retained-memory claim. No process-RSS or Unity
native-memory claim is made.
