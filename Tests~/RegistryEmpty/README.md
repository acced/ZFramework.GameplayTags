# Empty registry allocation/graph coordinator

This frozen coordinator supplements the existing CI matrix. This is **byte/graph diagnostic evidence, never timing acceptance**.

## CLI

```sh
python3 run.py \
  --before /path/to/before-source \
  --after /path/to/after-source \
  --before-revision FULL_LOWERCASE_40_CHARACTER_COMMIT_SHA \
  --after-revision FULL_LOWERCASE_40_CHARACTER_COMMIT_SHA \
  --dotnet /path/to/dotnet \
  --mono /path/to/mono \
  --nuget /path/to/nuget-packages \
  --out /path/to/new-output-directory
```

`--mono` is optional; when supplied, the same run includes both .NET and Mono. .NET SDK/runtime/reference pack 8.x must already be installed. Mono additionally requires the existing net472 reference package and System.Memory 4.5.5, System.Buffers 4.5.1, Unsafe 4.5.3 and Vectors 4.5.0 packages. Nothing is downloaded or installed. Paths are resolved, including dotnet/Mono symlinks.

Revision labels are mandatory full lowercase 40-hex SHAs. For a source root that is itself a git checkout, HEAD must match and Runtime must be clean. Archives retain caller-declared provenance; exact file/tree hashes are authoritative. A parent repository's HEAD is never inferred. Do not label an uncommitted candidate with its base commit's SHA.

The output directory must be new or empty. `--compile-only` compiles every requested backend/version/driver without executing any probe assembly. The normal run also completes **all compilation before any driver execution**.

## Frozen workload

`fixture-provenance.json` pins complete, byte-identical driver copies from the independently checked empty-build candidate. The coordinator refuses changed driver bytes. No fixture body, measured interval, oracle or calibration logic was edited.

On each backend, six fresh before/after process pairs are run for:

- First public Initialize: empty, one root, fixed 4,096-declaration forest.
- Internal Build: empty, one root, the same fixed forest.

Before/after order alternates each round. Only the existing small-internal driver measures an immediate second Build, so empty and one-root repeated allocations are reported separately. Public Initialize cannot repeat successfully; the frozen forest driver is first-build-only. Missing repeated measurements are not represented as zero. The nine-fixture driver is reused unchanged, selecting only its original forest fixture.

The public driver performs its existing retained-output graph inspection after its measured interval. One additional fresh process per source/backend/fixture captures its existing 16-build internal retained cohort. No new calibration, elapsed-time measurement, forced preloading or warmup Build is added.

With both backends, the run produces 144 allocation processes plus 12 graph-only processes. These three selected cases supplement, and do not replace, the four mandatory original Registry timing rows or broader correctness/operation gates.

## Evidence

- `compilation-complete.json`: exact source roots/revision labels and completed compilation boundary.
- `build/`: response files, compile stdout/stderr/exit status, per-assembly source/reference hashes and commands.
- `environment.json`: SDK/runtime versions, actual loaded framework implementation locations, CPU/affinity and recorded runtime-relevant environment. Unrelated CI secrets are not dumped.
- `frozen-inputs.json`: SHA-256 hashes of Runtime files, coordinator/package files, assemblies, references, executables and loaded framework implementations. .NET native runtime/JIT libraries are included when present.
- `raw/`: unchanged stdout/stderr, process metadata including failures, and parsed results with exact source-tree/assembly/environment/frozen-manifest hashes. Every invocation is a new process.
- `summary.json`: all six raw byte samples and paired byte savings; first versus immediate-repeat distinction; graph comparisons, sample stability, fixture/configuration/oracle checks and immutable-input verification.

The runtime-info helper reads versions and loaded implementation locations only; it does not calibrate or benchmark anything. All measured bytes come from the original drivers' per-thread GC allocation counters. Retained graphs preserve the drivers' original scope: output arrays and dictionary identities, public cached empty arrays, and existing typed-array calibration. They are not total-GC-retained-memory measurements.

Allocation increases and graph differences remain visible in the summary rather than being waived or converted into a blanket pass. Invalid fixture/configuration/oracle results, compilation/process failures, or changed immutable inputs fail the coordinator and preserve available evidence. A completed diagnostic run does not establish startup speed or unrelated hot-path neutrality.
