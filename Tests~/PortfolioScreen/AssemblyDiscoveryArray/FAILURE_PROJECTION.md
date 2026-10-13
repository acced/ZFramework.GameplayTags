# Finite output and failure projection

This contract is implemented by the pure, hash-verified `diagnostics.py` and fixed coordinator schemas. It is not a generic log sanitizer. Unknown content is suppressed rather than echoed. Only these six result files are eligible for upload:

1. status.json
2. provenance.json
3. config.json
4. correctness.json
5. raw/cold.json
6. summary.json

All keys and nonnumeric labels are fixed in source. Allowed variable values are booleans, bounded/validated versions, numeric measurements and exit codes, SHA256 digests and lengths. No raw exception message, frame text, absolute path, command, assembly inventory, metadata report or environment dump is an allowed output value. Synthetic private-text tests contain no real environment identifiers.

## Planned slots

Build roles are original, candidate, declarations1 and declarations32. Semantic slots are empty-original, empty-candidate, retry-original and retry-candidate. Cold slots are exactly the configured 24 shape/pair/order/variant combinations. Every slot is represented, even after an early stop. Attempted and Completed are separate; missing exit status is null rather than success.

Each target has six fixed private artifact roles: Stdout, Stderr, Snapshot, Inventory, Metadata and Trace. Each projected artifact has only Present, Length and SHA256. Absent and present-empty remain distinct. Text-artifact UTF-8 validity is separate; binary snapshots are never decoded. Raw artifacts are retained privately regardless of parse or exit status.

The first stdout line alone can supply a PORTABLE_RUNTIME header. Exact unique keys and types are required. .NET requires EnvironmentVersion 8.0.31 and empty MonoVersion; Mono requires MonoVersion 6.8.0.105 and a numeric three/four-component EnvironmentVersion with bounded components. PointerSize must be 8. Malformed headers yield null validated runtime and fail runtime gates; their text is never exported.

## Semantic and metadata status

Only exact empty/retry completion lines and the fixed public-caller-context completion line produce pass-marker booleans. MetadataJsonShapeValid checks the fixed R2 root schema, expected root field shapes and nonempty type objects. It does not mean metadata equivalence.

SnapshotEqual, TraceEqual, full metadata equivalence and exact Init-mechanism success are independent coordinator gates. Private metadata reports may explain failures in detail but no actual report content is copied into public output. Public counts/booleans remain finite.

## Independent cold records

Each cold stdout is scanned for one measurement and one semantics record independently. Duplicate/excess-key/type-invalid records fail that record's parse gate; an identifiable malformed duplicate cannot silently disappear. A valid measurement survives a later bad validation row, invalid UTF-8 elsewhere, or a nonzero target exit. Missing/malformed measurement is null, never invented zero counters.

Measurement retains shape/declaration/attributed-assembly/observed counts, elapsed ticks/frequency, allocated bytes, all three GC deltas, pointer size, operation success and the three fixed boundary flags. Arbitrary OperationErrorType becomes only ErrorObserved, KnownErrorCode and UnclassifiedError. Signed counter anomalies remain visible; CounterRangeValid is a separate gate. FirstBridgeJitIncluded and PreBoundaryInventoryPrimed must be true; WholeHeapRetainedBytesMeasured must be false.

Validation retains only its fixed pass booleans, observed/declaration counts, validated snapshot/inventory SHA256 values and mapped error flags. Constant AssemblyName is checked but not exported. Empty hashes become null. Arbitrary ErrorType never leaves the projector. Pair comparability is separate from parse success; valid numerical pairs remain visible even if a state gate fails. Ratios require valid positive timing operands. No adverse pair or unexplained outlier is replaced or filtered.

## Closed error vocabulary

Exception codes preserve R2 numbering:

| Code | Known type |
|---:|---|
| 100 | System.Exception |
| 101 | System.NullReferenceException |
| 102 | System.InvalidOperationException |
| 103 | System.NotImplementedException |
| 104 | System.NotSupportedException |
| 105 | System.Reflection.TargetInvocationException |
| 106 | System.TypeInitializationException |
| 107 | System.TypeLoadException |
| 108 | System.MissingMethodException |
| 109 | System.MissingFieldException |
| 110 | System.ArgumentException |
| 111 | System.ArgumentNullException |
| 112 | System.OutOfMemoryException |
| 113 | System.IO.InvalidDataException |
| 114 | System.IO.IOException |
| 115 | System.IO.FileNotFoundException |
| 116 | System.IO.FileLoadException |
| 117 | System.ArgumentOutOfRangeException |
| 118 | System.IndexOutOfRangeException |
| 119 | System.BadImageFormatException |
| 120 | DiscoveryConstructorException |

Only fixed frame codes are allowed: ENTRY; semantic MAIN/EMPTY/RETRY/SNAPSHOT/TYPED_ARRAY/FAST_PATHS/REFERENCES/VALUE; cold MAIN/PREPARE/ASSEMBLIES/NATIVE_ALL/VALIDATE/SNAPSHOT/VALUE; and the R2 METADATA_MAIN/TYPE/FIELD/PROPERTY/EVENT/METHOD/PARAMETER/BODY/IL/INTERFACE/ATTRIBUTES/REFERENCE/TYPE_NAME/VALUE/SIGNATURE/JSON codes. Signature-decoder matches are restricted to a fixed method-name set. Unknown types or lines produce UnclassifiedError, UnclassifiedStdoutPresent or UnclassifiedStderrPresent booleans only. A recognized marker never authorizes exporting its surrounding text.

Coordinator stages are setup, source, toolchain, main-build, fixture-build, semantic, metadata, cold, analysis, finalization and complete. Reasons are none, source-integrity, toolchain, build-exit, artifact-integrity, target-exit, runtime, record-schema, semantics, metadata, snapshot, inventory, deadline, interrupted and internal. Multiple per-gate diagnostics and counts are retained even when one primary reason is selected. An unrecognized coordinator exception maps to internal, never its text.

## Integrity and completion

The projector is loaded only from a file whose bytes match the sealed manifest, and the verified module stays available if a later integrity check fails. Source/tool/build/raw artifact pins are checked before every permitted new group is admitted. Shared fixture/dependency bytes and directory inventories are protected before samples and afterward. Captured child exit status is finalized within protected signal cleanup. Deadline, interruption, integrity and finalization failures cannot be overwritten by a later complete status.

`test_diagnostics.py` covers positive records, both runtime headers, malformed/duplicate/extra keys, absent versus empty artifacts, incomplete calls, failed exit with valid counters, broken validation with preserved counters, negative counter anomalies, invalid UTF-8, known/unknown exception frames, bool-versus-int mistakes and private-text nonleak. `projection-checks.json` reports only fixed test names/counts/booleans and source hashes. These preparation tests do not execute a compiler or C# target.
