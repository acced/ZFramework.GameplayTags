# Fixed Mono-only completion

This package completes the previously unexecuted Mono measurement portion of the original BindingRecordIds portability screen. It does not repeat .NET timing, change Runtime, tune counts, or qualify the optimization. Existing adverse/mixed .NET cold/control results remain a hold regardless of this run's outcome.

## Exact scope and preserved evidence

The original screen is pinned by seal `0f2c191c3d35d8e22afdad3e3a69962d26272c748bf0d4d00a10f9fdb252ec95`. Its Mono branch stopped during original semantic capture before any cold/warm target. Subsequent original-only diagnostic and semantic-only collector stages remain preserved, including the signature-context and event-collection failures. The r2 semantic-only package (seal `bb7acc191c191c4f202cb9b5691ec0b8fd3370a5bdd0117e81ae94336fea973d`) subsequently passed both original/candidate semantic and full metadata gates on .NET and Mono, with the same snapshot and managed record size24 to16 bytes. Those successful semantic-only jobs contained no measured rows.

This package copies that exact r2 collector (SHA-256 `12b31470a0a952923d7986ad7b3e3e0ea5c4009b9d0a47ae3ea1e29af477fc1c`). It retains only the previously reviewed empty generic-context and null event-array normalizations. Every other combined Harness file and every Runtime file remain the exact original screen inputs. No new C# or fixture family is introduced. See [r2 rationale](../BindingRecordIdsMetadataRepairR2/README.md), [original screen](../BindingRecordIds/README.md), and [preserved local evidence](../BindingRecordIds/LOCAL_EVIDENCE.md).

## One fixed coordinator

Mono6.8.0.105/x64 only. At most two builds, two semantic targets,32 cold targets and56 warm targets under a fixed300-second total coordinator budget beginning at wrapper entry. Zero pilots, retries, count recalibration, replacement samples, row dropping or automatic continuation. The full original coordinator's semantic, cold and warm sequence is byte-identical. The wrapper changes only its start clock, extra input verification, private Harness path and protected exit-code capture. It does not change the original sampling/validation/projection functions.

Both builds precede original then candidate semantics. The complete semantic/runtime/layout/metadata gate must pass before any measurement. All32 cold records are attempted before assessing complete pairs and allocation regressions; original cold semantic/allocation fail-stop blocks warm. Individual cold latency outliers do not automatically stop the sequence and remain available for review. Warm failures/negative/mixed/control/short/GC observations remain in their original fixed records. Timeout ends the stage without continuation.

Shapes: absent, present, existing external listener, and five-Bind growth. Cold holder construction, presence/external setup remain outside; group construction plus Bind(s) are inside the measured boundary. Retained owned-array payload is capacity times actual managed sizeof; whole-call allocation is reported separately. Neither is a total retained-heap estimate or a forced-GC name-lifetime criterion.

Warm rows and unchanged original-fixture indexes/counts:

- fresh-single (index0): 1,710,404
- retained-absent (index1): 3,466,725
- retained-present (index2): 3,506,477
- distinct-eight (index4): 403,525
- external-multicast (index6): 1,481,094
- direct-subscription (index7): 7,639,630
- direct-dispatch (index8): 3,212,116

Every shape/row uses four paired orders AB/BA/BA/AB, A=original/B=candidate. Each selected row has its own fresh process and unchanged fixture warmup; this differs from earlier full-traversal context. Retained distinct-eight is multi-record work, not a growth timing claim. Count map SHA-256 `bc03af680b1b7dc7583054ef51bd26cdb0f61b15b430eab9b00722cb5de0f4f9` is reused without modification. Counts were frozen from preserved original .NET samples; they are not recalibrated on Mono, so short or long batches remain evidence rather than a reason to adapt.

CLI: `run.py --backend mono --dotnet <host> --nuget <packages> --git-root <repository> --out <new-public-directory> --work <new-private-directory> --expected-seal <SHA256-of-this-frozen.json> [--mono <host>] [--deadline-seconds 300]`. --mono defaults to mono. The only accepted backend is mono and the only deadline is300. Optional --package defaults to sibling BindingRecordIds with the exact original seal above.

## Integrity and public output

The existing source/tool/artifact pinning, process-group termination, deferred child cleanup, verify-before-rebaseline, runtime identity and exact semantic stdout/snapshot comparisons remain. The private Harness copy is checked against frozen byte hashes at every existing verification boundary. The collector is the exact r2 copy. Target exit codes are captured inside protected cleanup before a nonzero failure can raise.

Upload exactly the existing eight paths: status.json, provenance.json, config.json, correctness.json, count-map.json, raw/cold.json, raw/warm.json, summary.json. Raw streams, commands/arguments, private paths, environment dumps, binaries and complete metadata snapshots remain private. Numerical observation projections, hashes and finite known error/frame codes are the only additions to public reports. The unchanged diagnostics helper records both semantic targets independently, preserving absent versus zero and actual exit outcomes. Measured targets keep the original fixed numeric/hash report projection.

Status adds CompletionSeal, OriginalPackageSeal, MonoCompletionOnly, InputHashesVerifiedAfter, WithinDeadline, FixedSequenceValidated and a finite WrapperFailureCode. Full completion requires counts2/2/32/56/0 and32/56 records. The original blocked cold-gate status is preserved with counts2/2/32/0/0 and32/0 records; rejected warm-gate status is likewise preserved. Neither is converted into success. Missing sequence/integrity/deadline evidence stays incomplete. AcceptanceEvidence, LatencyPassEstablished and AutomaticContinuation remain false. Catchable failures retain available outputs; an uncatchable termination may prevent final public output.

This run only supplies missing Mono evidence under the existing same-group comparator. Caller-manual retained-delegate subscriptions may avoid group overhead; direct-subscription/dispatch controls do not implement full group semantics. Earlier release of arbitrary noncanonical/mutated private tag names remains an intentional, potentially WeakReference-observable graph change outside the approved lifetime contract. No whole-heap retention or cross-runtime latency benefit is inferred in advance.

## Preparation checks

Source-only tests prove the entire original semantic/cold/warm sequence is unchanged, the collector equals r2 byte-for-byte, the CLI rejects other backends/deadlines, and blocked/rejected/incomplete outcomes remain preserved. The same11 synthetic projection/privacy tests are reused unchanged. These Python checks invoke no compiler or runtime target. Source/disclosure review and fresh publication/execution authorization remain required.
