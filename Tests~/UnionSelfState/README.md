# Same-storage reusable Union: frozen CI probes

This standalone package reuses five validated C# files byte-for-byte. It adds no runtime fixtures, timing, warmup, or measurement-body instrumentation. `fixtures.json` pins the C# SHA-256 hashes and exact case matrix.

## Coverage

- `Common.cs` + `Structural.cs`: 1,026 cases, five stages each (before, Union, clear, regrow, drain). Normal, proxy and derived views; flat boundaries, hierarchy, public overflow/re-add histories; fresh, reserved and grown/drained storage; serialization cache absent/present.
- `CountZeroGuardCommon.cs` + `CountZeroGuard.cs`: 54 cases, five stages each. Public operations reach Count=0 with global ExplicitCount=1. Normal-seed Union must clear to 0/0; the both-proxy control intentionally reconstructs 2/1.
- `Common.cs` + `Cold.cs`: 12 cases: flat 1/9/65 and one hierarchy leaf, each with fresh/reserved8192/grown1024-drained history. Every allocation and audit case gets a separate fresh process. No preliminary Apply, Union or Copy invocation or warmed repeat occurs.

Each unchanged structural/guard program compares every allocated entry field, explicit-index element and bucket against its factory+Copy oracle, including inactive retained tails. Raw JSONL retains public outputs, capacities, masks and SHA-256 array checksums for cross-source comparison. Cold raw JSON contains full before/after arrays. The large supplemental structural binary-witness replay is deliberately excluded.

## Run

Requires Python 3.9+, a .NET 8 SDK and, for the Mono leg, an installed standalone Mono runtime. Mono builds use net472 and the pinned package references in `UnionSelfStateProbe.csproj`; .NET builds target net8.0. CI may prepopulate its normal NuGet cache. Executable and source paths are supplied by the caller; none are baked into the package.

```sh
python3 Tests~/UnionSelfState/run.py \
  --dotnet "$(command -v dotnet)" \
  --variant original=/path/to/original-checkout \
  --revision original=FULL_40_HEX_ORIGINAL_COMMIT \
  --variant candidate=/path/to/candidate-checkout \
  --revision candidate=FULL_40_HEX_CANDIDATE_COMMIT \
  --api-variant candidate \
  --out /path/to/new/self-dotnet
```

Add `--mono "$(command -v mono)"` and choose a different new output directory for the standalone Mono leg. This option switches the execution backend; run the command once per backend. There are 26 managed fixture processes per variant per backend: one structural, one guard, and 24 cold. Two variants therefore use 52 fixture processes per backend.

The first variant always uses factory Union followed by Copy. Only `--api-variant` labels compile `REUSABLE_UNION`. Supply as many ordered `--variant`/`--revision` pairs as needed. Comparisons cover original-to-every-later-variant and each adjacent selected pair without rerunning workloads.

For an explicitly uncommitted smoke, add `--source-patch candidate=/path/to/patch` and supply its exact base commit in `--revision candidate=...`. This labels a base-plus-patch snapshot, never an exact commit. Declarations do not perform Git verification or prove that a patch produced the snapshot; source and patch hashes are the authoritative preserved evidence. Published CI should use exact committed checkouts.

The portable project also supports direct builds using `RuntimeRoot`, `ProgramSource`, `CommonSource` and `MonoTarget`. The coordinator chooses the three pinned source pairs and distinct build directories. Release optimization is enabled. Tiering and server GC are disabled consistently with the existing frozen probe setup; no Mono optimization-disabling switches are added. Effective known runtime settings are recorded rather than silently assuming a clean environment.

## Evidence and interpretation

`preliminary-environment.json` is saved before any child command. `environment.json` is atomically updated throughout and finalized on success/failure. All child stdout/stderr, including build errors and partial timeout output, are streamed byte-for-byte into `raw/`. `observations.json` indexes each successful fixture's raw file, checksum and actual runtime evidence; it does not duplicate large raw arrays. `comparison.json` records each original/adjacent comparison and review flag. A failed run keeps these files plus `failure.txt`.

The manifest records declared revisions/patch hashes, every Runtime/package input hash, every completed build-output hash, supplied host hashes, command order, platform/CPU count/affinity, effective known runtime settings and SDK/runtime inventory. Sources, package, binaries and hosts are re-enumerated/rehashed at completion; added, removed or changed files invalidate evidence. On Linux, observed native process executables are additionally hashed, including a Mono engine behind a launcher when observed. Unobserved launcher targets are not guessed. An external kill may prevent finalization; the preliminary record and streamed bytes remain.

For .NET, per-fixture native host traces preserve the actual loaded CoreCLR path/hash. `dotnet --version` is explicitly an SDK CLI version, not the runtime version. For Mono, the engine's reported version and supplied/observed executable hashes are retained. A local Mono 6.12 smoke does not establish Mono 6.8 CI results.

Gates flag structural rows/public outputs/capacities/masks/checksums, cold full retained snapshots, any cold managed-allocation increase, and changed .NET observed pool payloads. Allocation mode has no EventListener. Audit mode is a separate process and excluded from allocation comparisons. Every comparison is saved before review flags cause a nonzero exit. No fixed amount of improvement is required.

`ObservedApplyPoolEvents` contains only events captured during Apply. `PoolSourceObserved` is a listener-lifetime flag evaluated when output is constructed; post-Apply formatting may affect it. It is not evidence that the source existed at measurement start. Raw event payload fields are compared exactly, including any process-specific values, so differences are review flags rather than an automatic diagnosis. Equal empty/unobserved payloads never establish no rent or pool equivalence. Mono payloads are preserved but never used to infer pool-event equivalence.

Whole-GC retained-heap deltas are preserved verbatim and intentionally never gated as stable retained-memory measurements. No timing, stable heap, or Mono/.NET event-equivalence claim is made. Compare results only within the recorded backend; separate backend runs are not allocation-equivalent environments.
