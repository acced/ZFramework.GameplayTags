# Supplemental adaptive closed-union correctness

Source-only portable CI package. The production workflow archives the exact original and accepted Runtime trees, passes the actual candidate commit, and verifies Git provenance with `--git-repository "$PWD"`. See [semantic and storage argument](EQUIVALENCE.md). The five C# harnesses are copied byte-for-byte from the sealed `adaptive-closed-union-validation` evidence. `provenance.json` pins their origins and SHA-256 hashes. This package changes no production source, frozen main matrix, Union cold fixture, or timing workload. Keep generated evidence outside this directory.

## Coverage

Every variant runs three public regressions in independent processes:

1. Pending-contribution barrier counterexample.
2. Stale explicit-index counterexample using public count overflow, removal, union, and enumeration.
3. Healthy-union followed by counted-view Copy continuation, including the retained-tail observation.

Every variant also runs the unchanged full-state program at depths 257 and 769 in **separate processes**, since the registry cannot be initialized twice. The total is 92 fixtures and 1,932 observations per source/runtime: 84/1,764 at depth 257 and 8/168 at depth 769. Each run retains every owned storage field, all entries/index/bucket elements including unused tails, array capacities, mask, public state, serialized contents, exception observations, and within-process reference-identity results. It performs 230 input/Copy-source immutability assertions and 6,716 reference comparisons per source/runtime. Continuations include duplicate Add, removals/re-additions, Copy, self-Copy, Clear, regrow, and copying empty storage.

Only `--api-variant` labels define `REUSABLE_UNION`, and only for the full-state harness. Other variants use original factory Union followed by Copy as the oracle. The first variant must be the original oracle. All three public programs use the factory API unchanged for every variant.

Only `--gate-variant` labels build/run `GateCases.cs` and execute full-state `admission` mode. Normally this is the candidate only. These diagnostics assume the candidate private-method ABI (`CanOrderUnionContributions`, `Insert`, `AddExplicitIndex`, and `QueueContribution`) and the pinned storage/entry field schemas. A changed private ABI fails explicitly; it must be reviewed rather than silently skipped. The 208 gate cases include two-way mappings, complete ancestor closure, rejected null-queue pre-marker cases, pending barriers, public bulk negative links, full-state/queue restoration and retained array identity. Admission reconstructs the real depth-769 pending state using unchanged production helpers and verifies gate acceptance/restoration after the original FIFO threshold. This is an admission diagnostic, not the separate Mono trace proving entry into the actual ordered sweep; retain the existing sealed trace evidence for that claim.

## Requirements and interpretation

- Python 3.8+, Git, and an installed .NET 8 SDK.
- .NET runs are always included. Supplying `--mono PATH` adds a Mono/net472 run of the identical matrix and cross-runtime comparisons.
- Existing cached NuGet packages must include `Microsoft.NETFramework.ReferenceAssemblies.net472` 1.0.3, `System.Memory` 4.5.5, `System.Buffers` 4.5.1 and their transitive dependencies. The SDK may also resolve the cached `Microsoft.NETFramework.ReferenceAssemblies` package. The driver uses an empty package feed and disabled NuGet auditing; it downloads/installs nothing. Provision any CI cache outside this invocation.
- Mono's normal runtime/assembly/native-library environment must already be configured. Prefer the native Mono engine path; if a launcher is supplied its bytes and reported engine version are preserved, and Linux process evidence additionally identifies the executed engine and observed mapped runtime files.
- Every declared revision is verified against Git, not trusted as a label. A detached source snapshot can use `--git-repository REPO` to identify the repository containing its revisions. Otherwise each `SOURCE` must itself be a Git checkout.
- All files under each input `Runtime/` must match the declared revision exactly. Explicit preparation-only `--source-patch LABEL=PATH` instead verifies the exact revision plus the specified patch. This is reported as a base-plus-patch snapshot, never as the base commit itself. Patches must only affect `Runtime/`. Patch verification uses a private bare Git directory and an explicit disposable work tree, so an output folder inside another checkout cannot redirect application into that checkout.
- No timing, allocation, or broad acceptance claim is made. This bounded supplemental semantic suite does not replace existing semantic/cold/main/trace evidence. Actual Mono 6.8 coverage still requires running it on the Mono 6.8 CI host; the local preparation smoke uses Mono 6.12.

## Published CI: exact revisions

Assume three separately prepared checkouts and this source package. Set `CANDIDATE_SHA` to the **actual full 40-character published candidate commit**, rather than the candidate's base. No candidate commit is fabricated here. Run from the directory containing the checkouts/package:

```sh
ORIGINAL_SHA=f7c99546dbf029d70896c0f05e5119b19ffd6d26
ACCEPTED_SHA=f53c5fb94c22c2750f54c33dcfdee443a72bf595
: "${CANDIDATE_SHA:?Set the actual full published candidate commit}"
python3 Tests~/AdaptiveClosedUnion/run.py \
  --dotnet "$(command -v dotnet)" \
  --variant original=original --revision original="$ORIGINAL_SHA" \
  --variant accepted=accepted --revision accepted="$ACCEPTED_SHA" \
  --variant candidate=candidate --revision candidate="$CANDIDATE_SHA" \
  --api-variant accepted --api-variant candidate --gate-variant candidate \
  --out evidence/adaptive-correctness --gzip
```

For the Mono CI host, add `--mono "$(command -v mono)"` and use a different fresh `--out`. This adds Mono execution and exact cross-runtime comparisons. Do not label Mono 6.12 evidence as Mono 6.8.

## Local unpublished preparation: exact base plus patch

From the workspace root containing `gameplaytags/` and `union-candidates/`, with the existing SDK/Mono environment configured:

```sh
EVIDENCE=union-candidates/adaptive-closed-union-validation
PACKAGE=union-candidates/adaptive-closed-union-ci-package
python3 "$PACKAGE/run.py" \
  --dotnet "$(command -v dotnet)" --mono "$(command -v mono)" \
  --git-repository gameplaytags \
  --variant original="$EVIDENCE/base" \
  --revision original=f7c99546dbf029d70896c0f05e5119b19ffd6d26 \
  --variant accepted="$EVIDENCE/current" \
  --revision accepted=f53c5fb94c22c2750f54c33dcfdee443a72bf595 \
  --variant candidate="$EVIDENCE/candidate" \
  --revision candidate=f53c5fb94c22c2750f54c33dcfdee443a72bf595 \
  --source-patch candidate="$EVIDENCE/adaptive-closed-union.patch" \
  --api-variant accepted --api-variant candidate --gate-variant candidate \
  --out union-candidates/adaptive-closed-union-ci-smoke --gzip
```

The exact local candidate `Runtime/TagStorage.cs` SHA-256 is `ae1b90d08dfebbde3bc106f06121a24796c7445fc1f6c49e3550a8c207df3a7b`. A fresh output directory is required on every invocation.

## Evidence and failure handling

Three variants with one candidate gate execute 17 fixture processes per runtime, or 34 with Mono. Builds and provenance commands are separate from this count. For each runtime, the original is compared with every later variant and every adjacent predecessor is compared with its successor. Complete stdout **and** stderr are compared byte-for-byte for all three public probes and both full-state depth processes. With Mono, all corresponding source/fixture streams, including candidate gate/admission, are also compared across runtimes. No output normalization, truncation, or sampled checksum is used.

`environment.json` is updated before/after child commands and finalized on success or failure. It contains the exact command order, source/harness/project/coordinator hashes, verified Git tree/blob and patch provenance, compiled binary hashes, resolved compiler/reference files, SDK/runtime inventory, actual CoreCLR path and runtime-directory hashes, supplied Mono host/version, effective relevant environment and runtime evidence, all run counts and full-stream comparison results. Source/harness, all completed build outputs, host binaries, compiler/references and runtime files are re-enumerated/rehashed before and after. A difference invalidates evidence.

Every command streams raw stdout and stderr directly to disk, preserving partial output on exceptions/timeouts. Failures leave `failure.txt`, raw streams and the final manifest. Native .NET host traces identify the selected CLR without changing a C# harness. An abrupt external kill can prevent finalization; preliminary metadata and already-written streams remain.

On success, optional `--gzip` losslessly compresses each raw stream/trace, verifies its decompressed SHA-256 against the original, and durably records both hashes and original size before removing each uncompressed duplicate. A compression exception marks the evidence failed; completed mappings and remaining raw files are retained. Use `gzip -dc FILE.gz` to inspect the exact original bytes. Failed runs keep uncompressed raw evidence. Archive evidence separately; publish only this small source/project/driver/README/provenance package, not local SDKs, builds or generated state output.

## Local packaging verification boundary

The bounded packaging smoke completed 34 counted fixture processes on .NET 8.0.31 and Mono 6.12.0.199, with 94/94 complete raw-stream comparisons equal. It began under the initial coordinator: all 17 .NET processes passed, then optional Mono native-provenance capture attempted to hash an ephemeral `/dev/shm/mono.<pid>` mapping after exit. The first Mono attempt's exit code was not persisted, so that attempt was not counted as a pass. Its original manifest and raw bytes remain preserved separately.

The final coordinator restricts Mono mapping capture to persistent engine/assembly/native-library files. Only the 17 Mono fixtures were then executed on the original unchanged build artifacts; all existing .NET observations were reused. Final subprocess/provenance and comparison routines were exercised against those retained observations. The other bounded coordinator changes isolate Git patch verification from enclosing checkouts and persist each verified gzip mapping before deleting a duplicate. Separate nested-checkout patch verification and successful/injected-failure compression checks passed. Fixture sources and project did not change.

This is a completed bounded runtime matrix plus targeted final-coordinator checks, **not a fresh end-to-end invocation of the final CLI**. Published CI should execute the complete final CLI above, including the actual Mono 6.8 host leg. No resume option or recovery script is part of this source package.
