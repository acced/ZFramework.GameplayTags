# Semantic-only metadata collector portability repair

This is a collector-only repair proposal. All prior packages, failed runs and performance observations remain unchanged. It does not modify Runtime or qualify the optimization.

## Evidence and exact repair

Mono diagnostic run 38284300928 observed one first failure at a generic-instantiation head: method token 100663490, local-signature token 285212746, signature length 11/hash `65a34972975a1a779d01d8866fc4156508c245b01a3c88b0cbda59cafe52ca86`, decoder offset 8, immediately before ResolveType, encoded type117 / TypeRef16777245. Caller type-argument count was zero; method context was null. The fixed exception code identified bad-image format. Core/focused semantics, exact 1,824-byte snapshot and original managed record size24 had already passed; candidate semantics did not run.

The exact Mono6.8.0.105 implementation distinguishes null context from a non-null empty argument array. Its [context construction and ResolveType path](https://github.com/mono/mono/blob/mono-6.8.0.105/mono/metadata/icall.c) create a non-null zero-arity generic context for the latter and inflate the resolved class. The [generic parameter inflation path](https://github.com/mono/mono/blob/mono-6.8.0.105/mono/metadata/class.c) leaves an open parameter unchanged when context is null, but reports bad-image format when its index exceeds a present zero-arity context. This matches the observed failure path. The exact Mono TypeRef/method names have not been independently mapped from its on-disk tables; local signature-shape corroboration is not claimed as that mapping.

Starting from the original collector (SHA-256 `a7767a51b42e42758c213cbe9fa6f24c7bf10dd70ccea0071c55f325480279f1`), only two constructor assignments change:

- `ta = typeArgs != null && typeArgs.Length == 0 ? null : typeArgs;`
- `ma = methodArgs != null && methodArgs.Length == 0 ? null : methodArgs;`

Null remains null and every nonempty context is preserved. Raw signatures, token construction, all parsing/resolution rules and full metadata comparison remain unchanged. There is no catch-and-skip or blanket removal of generic contexts. This package does not retain the earlier diagnostic collector instrumentation. `metadata-repair.patch` is the complete C# delta.

## Exact bounded validation

Two backend jobs, .NET and Mono, each have at most two builds and two semantic targets under a hard120-second coordinator cap beginning at wrapper entry. Both builds precede original then candidate semantics. Any original failure stops candidate execution. The maximum across both jobs is four builds/four semantic targets; there are zero cold, warm or pilot processes. No timing repeat, count adaptation, retry, extra reader executable or local target smoke is part of this preparation.

CLI: `run.py --backend {net,mono} --dotnet <host> --nuget <packages> --git-root <repository> --out <new-public-directory> --work <new-private-directory> --expected-seal <SHA256-of-this-frozen.json> [--mono <host>] [--deadline-seconds 120]`. The only deadline is120. `--mono` defaults to `mono` and is resolved/used only by the Mono backend. Optional `--package` defaults to sibling BindingRecordIds, which must retain seal `0f2c191c3d35d8e22afdad3e3a69962d26272c748bf0d4d00a10f9fdb252ec95`.

The existing combined build helper and complete original semantic block are reused unchanged. Both builds use a private byte-verified Harness copy with only MetadataGate.cs overlaid. All Runtime and other fixture bytes remain exact. Source/tool/artifact checks, deferred child cleanup, process-group termination and verify-before-rebaseline remain. Actual exit codes are recorded during protected cleanup before a nonzero failure can raise.

The preserved gates require actual expected runtime/x64 identity, core7,595,855 assertions/zero failed groups, focused70 checks,13 supplemental frames, exact stdout and snapshot byte equality, actual emitted managed sizeof, and the complete private-field/Bind/Unbind metadata allowlist with unrelated normalized/raw IL and definition-token checks. The comparator must pass before an explicit semantic-only stop. No gate is replaced by a marker or diagnostic. Same-backend comparisons do not assume identical metadata/layout across runtimes.

## Public output and failure preservation

Upload only the eight explicit names in upload-allowlist.txt: status.json, provenance.json, config.json, correctness.json, count-map.json, raw/cold.json, raw/warm.json, summary.json. Raw measurement arrays must stay empty. Configuration explicitly records zero measurement processes; inherited row descriptions/count map are unused provenance, not an executed timing plan.

Status adds RepairSeal, OriginalPackageSeal, SemanticOnly, InputHashesVerifiedAfter, WrapperFailureCode and WithinDeadline. WrapperFailureCode is null or a fixed TIMEOUT/VALUE_ERROR/RUNTIME_ERROR/IO_ERROR/INTERRUPTED/UNCLASSIFIED code. AcceptanceEvidence stays false. Missing/incomplete gates, integrity or deadline leave State=incomplete.

Correctness retains the original complete gate fields and adds only collector hashes and a fixed two-entry SemanticDiagnostics array (original/candidate). Each entry has fixed Variant/Backend labels, attempted/exit status, independently validated numeric runtime/layout, known marker booleans, artifact existence/length/SHA-256, finite integer exception codes, fixed harness-frame codes and an unclassified-stderr flag. No command arguments, paths, raw bytes, arbitrary type/message names, environment data or metadata content is copied. An unattempted candidate stays explicitly unattempted; null is not converted to zero. Diagnostic success cannot override a failed original gate.

Private raw stdout/stderr, command records, metadata snapshots, binaries and source-copy paths remain outside upload scope. Catchable failures preserve available hashes/status; uncatchable termination may prevent final output. A successful repair stage establishes only the specified semantic/collector/layout checks. It does not clear adverse timings, prove whole-heap retention, establish Mono allocation savings, authorize more timing or qualify a version.

## Preparation-only checks

Python-only projection tests use synthetic private-path/message canaries and unknown errors, test both runtime-header schemas, and distinguish absent/zero values. Source-only adapter tests require the original semantic block to remain byte-identical and the collector delta to be exactly the two assignments above. These tests execute no C# compiler or runtime target. Independent source/disclosure review and fresh publication/execution authorization remain required.
