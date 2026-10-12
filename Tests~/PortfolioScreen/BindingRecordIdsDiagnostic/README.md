# Original-only Mono semantic diagnostic

Preparation only until separately reviewed publication and execution authorization. The first portable run (38279828100) completed two Mono builds but stopped during the first original semantic target or its immediate validation. It recorded no validated runtime identity, candidate semantic target, cold sample or warm sample. Private child output was deliberately not uploaded, so its cause is unknown. The installed Mono version alone does not validate the executing target's runtime. The .NET screen and all mixed/adverse measurements remain separate preserved evidence.

This diagnostic references the unchanged sibling `BindingRecordIds` package, seal `0f2c191c3d35d8e22afdad3e3a69962d26272c748bf0d4d00a10f9fdb252ec95`. Every Runtime and Harness C# byte, combined assembly source list, compiler options and dependency set remain unchanged. It performs exactly the same two builds, followed by exactly one original Mono semantic attempt. The candidate binary is built but never executed. There are no cold/warm/pilot calls, source variants, retries, timing samples, or automatic fixes. Runtime semantics and the inherited callback-release assertion are not weakened.

## CLI and bounds

`run.py --backend mono --dotnet <host> --mono <host> --nuget <packages> --git-root <repository> --out <new-public-directory> --work <new-private-directory> --expected-seal <SHA256-of-this-frozen.json> [--deadline-seconds 120]`

The only backend is Mono and the only deadline is 120 seconds. `--package` may name the exact sealed original package; its default is sibling `BindingRecordIds`. Public/work directories must be new, separate, nonnested and outside both packages. Backend execution remains CI-only; Python projection tests establish no Mono result.

The wrapper reuses the sealed coordinator in memory. `coordinator-adapter.patch` shows all changes: the common deadline begins at wrapper entry; child cleanup records the actual exit code even when nonzero; the semantic block is replaced with one original invocation using failure capture and an immediate stop. The existing deferred child-scope cancellation, process-group kill/reap, input/tool/artifact integrity, and verify-before-rebaseline remain. Both build outputs and all raw child streams/commands stay private. Unknown or uncatchable termination can prevent final report delivery; no missing details are fabricated.

The reused eight coordinator report files are written beneath the private work directory. Only one new public artifact is authorized: `diagnostic.json`, exactly as listed in `upload-allowlist.txt`. No directory glob or private report/log upload is allowed.

## Fixed output schema

`diagnostic.json` contains only the following keys. Unrecognized strings are never copied.

- `Outcome`: one of `INCOMPLETE_DIAGNOSTIC`, `TARGET_NONZERO_EXIT`, `HEADER_VALIDATION_FAILURE`, `ZERO_EXIT_UNVERIFIED`, `NON_REPRODUCTION`.
- `AcceptanceEvidence=false`, `RootCauseEstablished=false`, `CoordinatorComplete`, `ExpectedBudgetObserved`, `InputHashesVerifiedAfter`, `WithinDeadline`: booleans. `CoordinatorDeadlineSeconds=120`.
- `Counts`: six nonnegative integer/null counters: BuildAttempts, Builds, SemanticTargets, ColdTargets, WarmTargets, PilotTargets. The expected tuple is 2/2/1/0/0/0.
- `Commands`: fixed names archive-baseline, compile-original, compile-candidate, semantic-original, each with actual integer/null ExitCode. Arguments are excluded. `UnknownCommandObserved` is a boolean. `SemanticExitCode` is integer/null, retained before semantic/header validation.
- `RuntimeHeaderPresent`: boolean. `ValidatedRuntime`: null or exactly numeric-version strings EnvironmentVersion/MonoVersion plus integer PointerSize. It is emitted only after exact header shape, numeric EnvironmentVersion, Mono6.8.0.105 and pointer-size8 checks, independently of target exit. Header presence does not mean validity.
- `KnownPassMarkers`: fixed booleans NoneBeforeRegistry, CoreSummary (7,595,855 assertions/zero failed groups), Binding70, Supplemental13 and MetadataComplete. These are observations of the existing fixture output, not replacements for its assertions.
- `ValidatedManagedLayout`: null or exactly positive integer PointerSize/GameplayTagSize/BindDataSize, with pointer size8. No .NET record size is assumed. `MetadataJsonShapeValid`: boolean only; full metadata is private.
- `Artifacts`: exactly Stdout, Stderr, Snapshot, Layout and Metadata, each with Present boolean and Length integer/null plus SHA256/null. Content, paths and messages are excluded.
- `KnownExceptionTypes`: only members of the finite TYPES tuple in projection.py. `KnownHarnessFrames`: only fixed codes from its FRAMES map, matched against known harness stack prefixes. `UnclassifiedStderrPresent`: boolean when stderr has content but no allowed exception type was recognized. Arbitrary exception names, stack text, paths and messages are not emitted.
- `CoordinatorFailureCodes`: only the fixed COORDINATOR-map values or UNCLASSIFIED. `WrapperFailureCode`: null or TIMEOUT, VALUE_ERROR, RUNTIME_ERROR, IO_ERROR, INTERRUPTED, UNCLASSIFIED. Unknown failure text cannot pass through.
- `DiagnosticSeal` and `OriginalPackageSeal`: SHA-256 identities.

A target nonzero exit is distinct from a zero exit with invalid header or incomplete markers. It does not establish that a recurring failure has the same cause as the first run. A successful target/gate sequence is labeled NON_REPRODUCTION, not a root-cause explanation, performance pass or qualification. Incomplete builds, target budget, integrity, cancellation or deadline remain incomplete. A non-reproduction is the only zero exit from this diagnostic wrapper; the actual semantic exit is retained independently.

## Source-only preparation checks

`test_projection.py` uses synthetic private-path/message canaries and unknown exception/command names. It checks they cannot appear in the serialized report, while fixed known types/frames, exit codes and hashes remain. Tests cover nonzero target, valid success, invalid header/layout, incomplete markers, deadline and unknown command paths. It also parses the in-memory coordinator adapter with Python AST and checks its fixed stop before the cold stage. These tests do not import/execute any compiled C# assembly or call a subprocess/compiler.

No source-only risk is declared the cause. Plausible diagnostic locations include entry version reflection, core/focused assertions in a changed combined-assembly context, emitted managed size and full reflection metadata capture. Existing evidence cannot choose among them. Any subsequent repair or semantic rerun needs a separate evidence-based decision and exact review; no timing rerun follows automatically.
