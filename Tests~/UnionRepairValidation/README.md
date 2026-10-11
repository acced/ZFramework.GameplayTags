# Frozen Union repair semantic CI validation

This package validates the isolated repaired snapshot. It never uses repository-root Runtime as the candidate, never runs a Stopwatch benchmark command, and does not repeat either prior cold matrix. All 38 C# fixture files retain their frozen bytes. Only filesystem, compiler, provenance and process orchestration are adapted. This public package excludes private preparation documents and machine-specific transcripts.

## Invocation

From a full checkout containing the committed candidate snapshot:

```sh
python3 Tests~/UnionRepairValidation/run.py \
  --dotnet "$(readlink -f "$(command -v dotnet)")" \
  --git-repository "$GITHUB_WORKSPACE" \
  --candidate-source "$GITHUB_WORKSPACE/Tests~/UnionRepairedStrategies/sources/repaired" \
  --candidate-snapshot-commit "$GITHUB_SHA" \
  --out "$GITHUB_WORKSPACE/.artifacts/union-repair-semantics"
```

For the separate Mono job, add `--mono "$(command -v mono)"`. That selects only Mono target-fixture execution; .NET remains the compiler and offline metadata-reader host. Use a new output directory for every job. Linux is required by the frozen exact-byte/LF contract. No normalization, retry, sample replacement or baseline refresh occurs.

Install SDK 8.0.425 (net8 runtime/reference pack 8.0.31) and the intended CI compiler SDK before invoking. The adapter selects the highest installed SDK compiler, so SDK 10.0.401 is supported with net8 references. The unchanged net8 generator fixture explicitly uses SDK 8.0.425 Roslyn API DLLs, even when SDK 10 supplies the compiler; missing compatible API DLLs fail clearly. Both identities and hashes are recorded. This differs from the local SDK 8.0.425 compiler evidence and must not be described as compiler-identical. The Mono job uses the supplied default Mono executable with no tuning flags. This is standalone Mono, not Unity player validation.

For Mono, the adapter restores six explicitly pinned NuGet packages from nuget.org into its output directory. `Dependencies.csproj` and `NuGet.Config` define the exact versions/feed. Python uses only its standard library; Git and installed SDK/Mono are prerequisites.

## Provenance and immutable file sets

- `package-manifest.json` authenticates the exact package file set, including runner, plan, fixture bytes and provenance. Extra/missing files or any hash change fail before compilation.
- `fixture-provenance.json` lists package-relative fixture identities and SHA256. It contains no local source-origin paths.
- Original Runtime is extracted from f7c99546dbf029d70896c0f05e5119b19ffd6d26; historical f53 Runtime from f53c5fb94c22c2750f54c33dcfdee443a72bf595. The latter is reopened/provisional, not currently qualified.
- Candidate Runtime must be a distinct subdirectory of the supplied Git worktree, present at the exact 40-hex `--candidate-snapshot-commit`. Tracked blob bytes and the supplied working snapshot must match all frozen final Runtime hashes. Root Runtime is explicitly rejected as the candidate source.
- Git blobs are extracted into fresh output snapshots; symlinks, submodules, unsafe paths and source file-set differences are rejected. The final patch is d8f2ffe730a3462c35808f5d1959a8eaa8597240a774ec589400443e7b00ba89.
- Source, fixture, compiler/reference and host hashes are verified again before success. Emitted assemblies, runtime configurations and copied runtime dependencies are checked before/after execution and again at completion. `evidence-summary.json` contains concise prior results and hash-only evidence references. These references do not replace any CI gate.

## Fixed execution coverage

.NET: 86 target fixture processes. Mono: 83. Build/Git/version commands and the three offline metadata-reader processes are recorded separately and are not counted as target fixtures.

Both backends run:
- 39 public full-state processes across original/f53/final: boundaries, 21,068-case getter fixture, counted/deep/legacy overflow, source packing, capacity, maximum pending and sparse continuation fixtures.
- Six final-only private helper diagnostics: 3,000 checks per backend. Historical admission labels are not claims of actual final route instrumentation.
- 15 exact legacy anomaly/full-state processes, including depth 257 and 769, pending barriers and stale/public Copy continuations.
- Three selected/opposite/arithmetic diagnostics. Synthetic private arithmetic bounds remain distinct from supported public histories.
- Two class/derived/stateful/throwing/mutating getter probes and two unchanged generic value-proxy probes.
- Three large selected-alias runs: 288 cases each, E=8/9/1024/1025/2048/3000, both aliases/orders, growth, retained buffers, actual sparse masks, full tails, cache/storage identity and public legacy continuations. Each produces 2,094,910,491 uncompressed state bytes; raw streams are never accumulated on disk.
- Focused, deep, Evolution `test 0` and API `test` for all three snapshots.

.NET additionally runs the existing Main, DeepHierarchy, Registry and Generator fixtures and compiles Unity-conditional code against stubs. Mono additionally runs Main directly. This reproduces the old 29 core target processes across the two jobs.

Every public/core fixture retains its own semantic assertions. Full-state public/private/legacy streams also require the exact recorded uncompressed stdout/stderr digests; variant streams are compared byte for byte. Independent NET and Mono jobs require the same frozen digests. Neither job pretends to have executed or directly compared the other backend. The boundary stream additionally checks its complete fixed summary and raw digest/size, then compares all three streams byte for byte.

The emitted-metadata prerequisite compiles original/f53/final and reads their PE metadata offline. It verifies original factory and prior storage normalized IL/metadata, unchanged f53 storage MethodDef tokens/within-type order, the original 40-byte public constructor initializer and BeforeFieldInit/no-static-constructor behavior. Raw reference-token differences and f7 global offsets are recorded separately. Target metadata assemblies are never loaded or executed.

## Explicit inclusion/exclusion map

- The API fixture’s frozen `test` mode disables Stopwatch timing but retains three `GC.GetTotalMemory` probe pairs and three 64-result cohorts per process. These historical side effects are preserved and disclosed; their retained-byte values are not emitted as performance evidence.
- The generic value-proxy fixture retains its fixed 1,000 operations per side and allocation counter. Its exact 32-source-tag generic alias path is not silently replaced by a smaller getter fixture. Final warm bytes must not exceed f53 on either side. This is a bounded inherited check, not cold revalidation or timing.
- The historical standalone sparse-alias allocation probe is excluded. Successful state, tails, identity, sparse compaction and continuations remain covered by selected-seed and large-boundary fixtures. Its old warmed-byte result is not claimed to have been rerun here.
- The 1,722-call stronger matrix, final-source 336-call empty/dirty matrix, frozen UnionOutputCold/UnionSelfState allocation matrices and fresh value-proxy cold calls are outside this semantic stage. Their prior raw results and pool-review outcomes remain preserved. The separate stronger CI stage owns its own declared cold protocol.
- Removed borrowed-private diagnostics and four historical helper-only throw clones are not relabelled as executed. No final public OOM/resource-failure equivalence is claimed.

## Timeouts, evidence and resources

Each build/restore has a 300-second deadline; target fixture processes have a 1,800-second deadline; the entire adapter has an 82-minute deadline, also checked during final decompression/comparison and at completion. Both stdout and stderr are drained concurrently to gzip while a real process-start watchdog runs. A timeout kills the process group, preserves partial streams and records the exit/error; it never retries. The enclosing workflow should allow 90 minutes and always upload the output directory on success or failure.

Successful completion requires every fixed process and comparison plus all immutability checks. `report.json` records portable command arguments, logical file identities, selected versions, allowlisted runtime configuration, hashes, exit/error categories, timeouts, comparisons and limits. It does not publish inherited environment values, absolute machine-local paths or full tracebacks. `completion.json` exists only after complete success. Both compressed-file and uncompressed-stream hashes are retained. All raw semantic streams are compared unchanged in the private work tree. Only fixed-protocol streams are copied byte-for-byte to public evidence after every semantic/source/binary gate succeeds. Unexpected failing streams and core diagnostics remain status/hash-only publicly; no redaction is applied to compared bytes. Compiler, Git and host-query transcripts plus intermediate binaries stay in a separate temporary working directory outside the uploaded evidence tree; only their hashes and status are reported. That directory is cleaned after execution, including failures. Unexpected diagnostic bytes are therefore not retained after exit; their hashes, exit/error categories and coverage stage remain in the public report. Failures remain failures even if an earlier run passed.

Resource estimate from prior concurrent local runs: roughly 50–75 minutes per backend is a planning estimate, not a runtime promise; slower CI may hit the preserved timeout. Expect approximately 1.5–2 GiB of compressed evidence per backend, plus build outputs and restored dependencies. Provide at least 8 GiB free disk and a normal 7-GiB-or-larger Linux CI runner. Peak RSS was not measured, so no smaller-memory guarantee is made. Output is streamed rather than held as full strings or uncompressed files.

## Scope limits

Opposite-selected aliases above 1,024 explicit IDs retain the temporary-result fallback. Ordinary stack FIFO payload reaches 4 KiB; opposite snapshot plus queue reaches 8 KiB, excluding frames. Caller stack availability is not guaranteed. Successful-state checks do not establish equal OOM, stack-exhaustion or early-reservation failure state. This package does not qualify performance, infer Unity performance, or erase prior failed/review evidence.

## Public artifact data boundary

The output directory contains reports, test results and validated unchanged compressed semantic streams. Upload only the explicit patterns in `artifact-upload-allowlist.txt`, never the entire output tree. A per-run `upload-allowlist.json` records exact artifact hashes. Build trees, dependency metadata and PDBs are excluded on success and failure. Private build products, response files, restored dependency metadata, host-query output and Git root transcripts are outside that directory and are never linked into it. Command records use `${PACKAGE}`, `${REPOSITORY}`, `${CANDIDATE}`, `${OUTPUT}`, `${WORK}`, `${DOTNET}`, `${DOTNET_ROOT}` and `${MONO}` as reproducible logical identities. These stand for caller-supplied locations; their original absolute values are not retained in public reports.

Logging minimization does not change the fixture code, execution matrix, source hash gates, compiler flags, command ordering, semantic digests, binary checks or timeouts. Validation failures still fail the stage; only diagnostic publication is minimized. Actual intermediate filesystem roots differ only to keep non-test artifacts outside the upload tree. Full private preparation evidence is not bundled.
