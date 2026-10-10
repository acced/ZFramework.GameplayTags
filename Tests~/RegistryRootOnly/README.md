# Fixed root-only registration supplement

This additive audit keeps the existing two root-only fixtures unchanged. It does
not replace any of the four mandatory `Tests~/Evolution/Registry` rows or any
public-operation, allocation, retained-memory or backend/hardware gate.

## Frozen fixtures

- `registry-root-only/unique-roots4096-name8`: 4,096 distinct root names, exactly
  eight characters each, with no period separator.
- `registry-root-only/unique-roots4096-name256`: the same 4,096 distinct eight-character
  prefixes plus 248 padding characters, giving exactly 256 characters per name.
- Normal mode: **128 complete registry builds per sample**, unchanged.
- Quick mode: three one-build samples for smoke only, as in the original harness.

Input names are constructed outside the measured region. Both fixtures have the
same registry cardinality and frozen-array shapes. The independent name/metadata,
ordinal DFS, hierarchy-path and subtree-interval oracle runs in every process.
The longer names expose additional full-name scans or hashing; there is no
candidate-specific workload gating.

`Program.cs` is byte-for-byte the original prepared root-only supplement:

```
Program.cs SHA-256
606106b608f79e21a1fa418a20f8b0d9a72a7c6c7fdcdbac31560b869c936106
run.py SHA-256
96ee17de2808721ec5b837174cae8dd946ac634c498adcd511a71f5dade01ee2
paired.py SHA-256
4bccea40cff0b328c3da2ed0f117fdd57f8448c01a03a71c33e80fe0a12cddb5
```

Only the Python drivers' provenance/validation behavior was strengthened for
publication. No fixture, iteration count, oracle, measured operation or C# source
was changed.

## Standard paired CLI

Run one backend at a time during an exclusive measurement window. Use separate
output directories for .NET and Mono. Every output directory must be new or empty.

```sh
python3 Tests~/RegistryRootOnly/paired.py \
  --before "$BEFORE_SNAPSHOT" \
  --after "$AFTER_SNAPSHOT" \
  --before-revision "$BEFORE_SHA" \
  --after-revision "$AFTER_SHA" \
  --dotnet "$DOTNET_HOST" \
  --nuget "$NUGET_PACKAGES" \
  --out "$OUTPUT_ROOT/root-only-dotnet" \
  --rounds 14

python3 Tests~/RegistryRootOnly/paired.py \
  --before "$BEFORE_SNAPSHOT" \
  --after "$AFTER_SNAPSHOT" \
  --before-revision "$BEFORE_SHA" \
  --after-revision "$AFTER_SHA" \
  --dotnet "$DOTNET_HOST" \
  --mono "$MONO_HOST" \
  --nuget "$NUGET_PACKAGES" \
  --out "$OUTPUT_ROOT/root-only-mono" \
  --rounds 14
```

`BEFORE_SHA` and `AFTER_SHA` must be full **lowercase 40-character commit SHAs**.
Abbreviations and free-form revision labels fail before compilation or output
creation. A supplied SHA is a caller-declared provenance label, not proof that an
arbitrary archive matches it; exact source hashes remain authoritative. Omit the
corresponding revision option for an uncommitted isolated snapshot rather than
inventing a commit ID. Workflow comparisons should supply both real full SHAs.

For a quick compile/oracle/runner smoke, add `--quick --rounds 2`. Quick runs are
explicitly marked `smoke-only-no-timing-acceptance`, even if someone requests 14
quick rounds. Their short raw timing and retained-memory values must not be used
to accept or reject a performance candidate.

`run.py` also supports `--source`, `--revision`, `--out`, `--variant`, `--dotnet`,
`--mono`, `--nuget` and `--compile-only` for an external coordinator that needs to
interleave original/predecessor/candidate processes. A `--revision`, when supplied,
has the same full-SHA validation. Executable, source, NuGet and output paths are
resolved, including a symlinked dotnet host.

## Provenance and raw evidence

Both variants compile before any paired measurement. A separate metadata-only
probe process records the actual host executable and loaded framework assembly
locations. It does not invoke the benchmark and cannot warm a later benchmark
process. The benchmark C# remains unchanged.

The manifests retain:

- Exact Runtime source, C# harness and runner SHA-256 hashes.
- Main assembly, copied package assemblies, response files, runtime configuration
  and probe artifact hashes; compile and run commands.
- Compiler/SDK directory and version, `dotnet --info`, actual host version, actual
  host/framework binary hashes, and selected .NET native runtime hashes.
- Reference assembly/package hashes, CPU model, machine/platform, affinity,
  Python version and runtime settings.
- Caller-declared full revisions, plus Git HEAD/status/runtime-diff information
  only when the selected source is itself the exact Git top level. An ancestor
  repository's HEAD is never used to label an archive.

The coordinator alternates AB/BA processes, saves every unmodified report and
stdout/stderr, and rechecks frozen sources, toolchain binaries and artifacts.
CPU/affinity must agree between compilations. Runtime/settings, fixture IDs,
input/output checksums, declaration/tag counts, iterations, retained cohort and
operation checksums must match before comparisons are emitted.

Timing intervals require at least 14 pairs. Short-batch, allocation, frozen-payload
and unreliable retained-GC flags are review signals, not blanket acceptance.
Frozen array payload is computed with managed `Unsafe.SizeOf` and excludes array
headers, strings, dictionary and wrapper. Retained GC deltas are approximate;
standalone Mono results do not establish Unity Mono or IL2CPP behavior.

## Publication preparation smoke

The publication copy was checked against exact
`f7c99546dbf029d70896c0f05e5119b19ffd6d26` and the isolated f7-based declared-duplicate
registration candidate (patch SHA-256
`dc8f8a3ed6db86a1b78fe39d024ce130e6b459c8542e2bd65138f993d357565a`). The uncommitted
candidate had no after-revision label; its complete source hashes were retained.

Two quick AB/BA rounds passed on .NET 8.0.31 and standalone Mono 6.12.0.199, with
SDK 8.0.425. The fixture byte identity, source/runtime/artifact hashes, CPU and
configuration checks passed. Allocated bytes and frozen payload were equal across
variants for both rows. The full manifest includes the actual Mono binary and
implementation mscorlib hash, not merely its launcher or reference assembly.

These are smoke results only. The complete mandatory Registry matrix and real
paired CI measurements remain necessary before acceptance.
