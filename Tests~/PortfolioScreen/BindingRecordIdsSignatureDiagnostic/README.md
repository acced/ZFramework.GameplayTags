# Mono local-signature failure localization

This is a separate diagnostic derivative. Preserve the first portable run and first Mono diagnostic unchanged. Run 38281761432 completed two builds and one original semantic target with exit 1 on validated Mono 6.8.0.105/x64. None/core/70-check markers passed; the 1,824-byte snapshot matched `730eaa6d941f0fd91f88ec5478741cfd4284754cf1c48c4fd2a8cfe296b87ea0` and original managed record size was 24 bytes. Metadata was absent. Known frames included CaptureBody and SignatureDecoder; the exact failure remains unknown. The final supplemental banner occurs after metadata capture, so its absence does not invalidate the complete snapshot.

## Fixed scope and CLI

This derivative performs the same two combined builds, then exactly one original Mono semantic target, under a 120-second maximum beginning at wrapper entry. The candidate assembly is never executed. There are no performance samples, additional reader executable, dependency installations, general PE parser, retries or automatic fixes.

`run.py --backend mono --dotnet <host> --mono <host> --nuget <packages> --git-root <repository> --out <new-public-directory> --work <new-private-directory> --expected-seal <SHA256-of-this-frozen.json> [--deadline-seconds 120]`

The only backend/deadline choices are Mono/120. Optional `--package` defaults to sibling `BindingRecordIds` and must match its original seal `0f2c191c3d35d8e22afdad3e3a69962d26272c748bf0d4d00a10f9fdb252ec95`. The sole upload path is `diagnostic.json`; the reused coordinator's eight reports and all raw streams, commands, compiled assemblies, metadata and private fixture copy remain private.

## Exact source changes

All Runtime, entry, core, registration, focused binding, supplemental semantic, cold and warm fixture sources remain byte-identical to the sealed package. Both builds use a private byte-verified copy of the same Harness directory, replacing only MetadataGate.cs with this package's overlay. Its exact delta is `metadata-diagnostic.patch`.

Only metadata observation is instrumented:

1. Wrap the existing nonzero local-signature resolve/decode expression with a catch. Capture the first failure, then rethrow with `throw;`. Diagnostic emission has its own guarded catch so it cannot replace the original exception. No fallback, signature skipping, successful-decode substitution, comparison weakening or continuation is added.
2. Add nullable numeric breadcrumbs inside the existing decoder, setting the current stage before the relevant decode or ResolveType call. Preserve its actual token calculation, context arguments, parsing and type resolution.
3. Emit one fixed numeric/hash record before rethrow. SHA-256 and length are recorded for the exact returned signature buffer; its raw bytes/hex are never emitted. No exception message/type name, arbitrary string or path is included in that record.

The Python wrapper still reuses the original guarded coordinator. `coordinator-adapter.patch` records its in-memory changes: earlier common deadline, actual exit-code capture during protected cleanup, one original semantic attempt followed by stop, and the verified private Harness path. Additional verification checks the sealed overlay and every copied fixture hash at each existing boundary. Runtime source verification and compiler/tool/output checks remain intact.

## Public projection

The existing diagnostic outcome, command counts, actual exit, validated runtime/layout, fixed pass markers and artifact lengths/hashes are retained. Outcomes remain INCOMPLETE_DIAGNOSTIC, TARGET_NONZERO_EXIT, HEADER_VALIDATION_FAILURE, ZERO_EXIT_UNVERIFIED and NON_REPRODUCTION. AcceptanceEvidence and RootCauseEstablished remain false. A successful repeat explains no prior failure.

This derivative replaces the earlier textual KnownExceptionTypes list with integer KnownExceptionCodes (100 plus the frozen TYPES-tuple index). It never publishes arbitrary exception names. The first failure's dedicated ExceptionCode below is independent and more precise. The finite harness-frame and coordinator-failure codes remain unchanged; unknown text remains unclassified.

Additional fixed fields:

- `SignatureDiagnosticCount`: integer count of prefixed records. Exactly one is required for `SignatureDiagnosticValid=true`; zero, multiple, malformed or out-of-schema records yield null `SignatureDiagnostic`.
- `SignatureDiagnostic`: null or exactly MethodToken, LocalSignatureToken, SignatureLength, SignatureSHA256, DecoderOffset, DecoderStage, LastElementKind, LastEncodedType, LastResolvedToken, TypeArgumentCount, MethodArgumentCount and ExceptionCode. All values are bounded integers/null except the SHA-256/null. Null represents an absent buffer, decoder state or context; it is never substituted with numeric zero.
- `OriginalAssemblySHA256`, `MetadataCollectorSHA256`, `OriginalMetadataCollectorSHA256`: identities tying the observation to the built original and exact helper overlay. No assembly contents are uploaded.

DecoderStage is fixed: 1 resolving signature before a decoder exists; 2 decoder constructed; 3 Read entry; 4 signature header; 5 compressed integer; 6 type element; 7 TypeDefOrRef decoding/token construction; 8 immediately before ResolveType; 9 resolved-type formatting; 10 trailing-byte check; 11 completed read. Stage 8 alone does not establish a token's correctness or a runtime defect.

ExceptionCode is fixed: 0 unknown; 1 argument out of range; 2 index out of range; 3 bad image format; 4 invalid data; 5 type load; 6 unsupported operation; 7 unimplemented operation; 8 other argument error; 9 invalid operation; 10 missing method; 11 missing field; 12 out of memory. Unknown exceptions publish code 0, without their type/message. The original exception still terminates capture.

No signature bytes are public because they have not been independently matched to the same assembly's on-disk StandAloneSig/#Blob payload. A length/hash observation localizes a failure but does not guarantee a representation diagnosis. The existing independent PEReader would require another executable and is deliberately outside this budget. Any later static extraction or repair needs separate review.

## Preparation checks and limits

Python-only tests cover the earlier private-path/message canaries, known/unknown errors, exit/header/marker distinctions, the fixed adapter stop, numeric signature schema, forbidden raw-byte/extra fields, offset limits, and separate null/zero representation. Python AST checks and source-byte/delta checks do not establish C# compilation or Mono correctness. No compiler or runtime target is executed during preparation.

The first failure is the only localization target. Missing diagnostics remain missing; uncatchable termination can prevent delivery. The source contract, private-name lifetime caveat, manual-subscription comparator scope and all prior adverse timing observations remain as documented in the unchanged original package. No timing campaign or qualification follows automatically from this diagnostic.
