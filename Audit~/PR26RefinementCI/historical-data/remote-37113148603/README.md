# PR26 remote measurements: run 37113148603

This additive record preserves the 16 performance shards from [Actions run 37113148603](https://github.com/acced/ZFramework.GameplayTags/actions/runs/37113148603), exact commit `fc555ca1ce637c5d04274945ce90734afc89f2f1`, attempt 1. Each shard passed its original execution/integrity gate, independent source/build/deployed-DLL audit, and independent statistical recomputation. The publication preparation reran no benchmarks.

**Hardware-enabled/default main results** cover query, builder and mixed workloads, 426 cells per architecture:

| Architecture | Improvements | Regressions | Uncertain/small | Invalid |
|---|---:|---:|---:|---:|
| x64 default | 126 | 31 | 265 | 4 |
| ARM64 default | 68 | 3 | 353 | 2 |

These are classification counts across separately measured shards, not pooled timing ratios. The deliberately hardware-disabled `nohw` controls and the bridge comparisons are reported separately. Every regression, invalid sample and adverse batch-tail observation remains visible in [RESULTS.md](RESULTS.md) and the retained evidence. A successful CI job is not a claim of universal performance superiority.

## Readable and structured results

- [RESULTS.md](RESULTS.md): default results first, separately labeled nohw controls, all shard counts, allocations, A/A drift, batch tails, named selected/baseline and split/old-Direct losses, every invalid control, and all eight architecture-specific mappings of the four historical local losses
- [results-summary.json](results-summary.json): condensed per-shard results and every regression/invalid row, official artifact IDs/API URLs/digests/expiration, recorded Runtime hashes, and independent audit receipts
- [SOURCE_PINS.json](SOURCE_PINS.json): common source-selection/payload digests and selected/baseline/bridge-before Runtime source-set pins, identical in all 22 recorded timing/correctness receipts
- `derived-results.tar.gz` and [derived-inventory.json](derived-inventory.json): all 1,920 recomputed rows in 16 complete JSON summaries and 16 CSV tables; JSON retains every round and crossing
- `raw-*.tar.gz` and [raw-inventory.json](raw-inventory.json): all 1,908 files from the original complete performance result directories plus 64 original receipt/feature files
- [RAW_PAYLOAD_REVIEW.json](RAW_PAYLOAD_REVIEW.json): byte-for-byte comparison of the retained raw subset with the original nested GitHub artifact ZIP members
- `correctness-records.tar.gz`, [correctness-inventory.json](correctness-inventory.json), and [CORRECTNESS_PAYLOAD_REVIEW.json](CORRECTNESS_PAYLOAD_REVIEW.json): original nonbinary records from all six separate correctness jobs, including 136 process summaries/logs, source/DLL-binding receipts, features and SDK/job metadata

The complete measured set across all groups/backends/architectures contains 1,920 cells and 125,496 timed samples. This count includes the nohw controls and separate bridge matrix; it is not an aggregate of default-only performance.

## Permanent correctness records

| Architecture | Configuration | Recorded passing semantic/fault processes |
|---|---|---:|
| x64 | net8 normal | 32 |
| x64 | net8 checked | 18 |
| x64 | actual netstandard2.1 checked library with net8 host | 18 |
| ARM64 | net8 normal | 32 |
| ARM64 | net8 checked | 18 |
| ARM64 | actual netstandard2.1 checked library with net8 host | 18 |

All 136 original process outcomes and their nonbinary records are preserved. Normal jobs include the matched baseline suites; every configuration includes selected-source semantics and separate instrumented copy/union fault checks. Default and nohw executions remain identified separately within each job. Compiler source before/after hashes, build/library bindings, run logs and receipts are retained; compiled binaries and duplicate source trees are omitted. This is preserved managed evidence and does not claim native Unity acceptance.

## Verify and recompute without running benchmarks

With Python 3.9 or later, from this directory:

```sh
sha256sum -c SHA256SUMS
python3 reanalyze.py
```

The Python verifier reads archives in memory and never extracts them, invokes .NET, accesses the network, follows historical absolute paths, changes recorded evidence, or writes output files. It checks archive/member digests, complete raw cohort coverage, manifest-bound JSON/stdout hashes and equality, original workload/output/allocation contracts, backend controls, every raw/derived JSON and CSV summary, classification counts, allocation/tail tables, and the remote side of each historical-coordinate mapping. It also checks the correctness-record archive hashes and 136 recorded zero-exit process summaries, source-pin agreement across all 22 receipts, and raw process metadata against matching feature receipts. These are record checks; no correctness test is rerun.

The calculation helpers are explicitly restated in `statistics_methods.py`. Their lineage is the adjacent historical reanalysis, with only two dataset-name generalizations documented in `methods-adaptation.patch` and `methods-provenance.json`; numerical formulas are unchanged. Six baseline crossings and max(3%, per-cell A/A drift) govern broad/legacy-relative classifications. Split/old-Direct uses three within-round median ratios with the same drift threshold. Invalid cells are unranked. P95 and maximum ratios describe batches, not individual-operation tail latency.

## Retained bytes and omitted provenance

Eight raw archives group the two backends by architecture/workload. Each is below 2 MiB. Raw member bytes are unchanged; tar ownership, modes and timestamps are normalized. `raw-inventory.json` maps every published member to its original inner ZIP member and official artifact identity. Timing-result members keep their original `pr26-refinement/benchmark/results/ci-.../` paths. Common receipt/feature files gain a shard-name prefix to avoid collisions; the original member path remains in the inventory.

This subtree publishes complete raw timing result folders, limited timing run/feature receipts, and the nonbinary correctness-record subset described above. It **omits DLLs, executable hosts, PDBs, duplicate source copies and full compiler/dependency artifact trees**. The original independent audit verified those full artifacts before this selection. The portable script validates retained raw/statistical evidence; it does not revalidate omitted binaries or turn their historical hash receipts into new provenance proof. The original complete GitHub artifact ZIPs have the expiration dates recorded in the inventory; preserve those full ZIPs for later binary or complete compiler-provenance reanalysis.

These are managed Linux hosted-VM .NET 8.0.31 observations, SDK 8.0.425, native x64/ARM64 ISA. They do not establish Unity player, Mono, IL2CPP, Burst or device performance. Shards ran on separate VMs; timings are not pooled across hosts or architectures. The older local x64 measurements and their four regressions/six invalid controls are separate, unchanged observations, not replaced by this run.
