# Measured explicit bulk-policy lifecycles

`BulkWorkload.cs` compares real public APIs with immutable source snapshots. `bulk_workload.py` compiles and executes them. It does not simulate timings or select a per-row winner.

Run after the Compressed correctness gate:

```sh
python3 Audit~/Refactor/bulk_workload.py \
  --original /path/to/original/repository \
  --current . --previous /path/to/pre-compressed/snapshot \
  --output /path/to/new/evidence \
  --dotnet /path/to/dotnet --rounds 3 --samples 7 --target-ms .5 \
  --suite smoke --portable --original-forced
```

`--suite full` adds N=8/64 and overlap 0/50/100%, four-cluster membership and both mixed-locality operand orders. Smoke uses U=65,536 and262,144 definitions, N=128/1,024/4,096 or128/4,096/16,384, contiguous/scattered/mixed requested patterns,50% overlap. Actual occupied blocks and physical storage are disclosed; overlap may make an operand's actual locality differ from its requested pattern.

Compared paths:

- Original Auto uses its existing constructor+AddTag API
- Current/previous ordinary Auto uses its existing FromTags factory, then ordinary Union
- Current Bulk uses production FromTagsForBulk and UnionForBulk, including their scans, temporary buffers, conversion and allocation
- Current forced Sparse/Dense/Compressed uses the corresponding public factory and union APIs
- `--original-forced` also checks forced Sparse/Dense on original and previous sources

Fresh lifecycles start with resolved handle arrays sorted and deduplicated outside timing. This specifically exercises ordered loading and cannot support unordered-loading claims. Existing lifecycles start with ordinary Auto inputs. Existing Auto retains those already-owned inputs; a forced matching layout also retains them. The current Bulk migration workflow calls SelectBulkStorage inside timing. When the selected physical layout equals the source layout it explicitly retains that already-owned input; otherwise it calls ToStorageForBulk, which always creates independent storage. All selector work and direct-conversion cost are charged, including any repeated selection inside the copy API. Frozen gates1/2 used the earlier handle-export + factory workflow and remain separately labeled handle-export-v1; the new workflow is direct-existing-conversion-v2. No hypothetical free conversion is inserted.

Each lifecycle includes both operand preparation/conversion and an allocating initial union to choose and reserve the reusable destination through the real public API. It then executes h=0/1/32 rounds of union, intersection, copy/remove, copy/append,32 exact probes, one hierarchy probe, one frozen-query evaluation and one add/remove pair. Registry and shared frozen-query construction are outside these per-actor lifecycles; query freezing is measured separately. Mutation growth is inside the lifecycle measurement. Standalone prepared mutation is explicitly capacity-primed first, with its actual post-prime storage/capacity reported.

Both suites also include 4,096:8 and8:4,096 skew, contiguous/scattered membership, and prefix/spread/suffix shared members.

Standalone rows cover preparation, existing conversion, exact Any/All and hierarchy Any/All boolean predicates without materialized output, exact/hierarchy/frozen queries, query freezing, prepared mutation, allocating union and reused bulk operations. Every measured batch's actual result/checksum and input immutability are checked before reset or re-execution. Every operand/output records storage, member capacity, arbitrary-member reservation, physical record count/capacity and payload bytes. All allocations, GC counts, raw samples and failed rows remain visible.

Every process sets DOTNET_gcConcurrent=0 and asserts GCSettings.LatencyMode==Batch. A10,000-copy no-allocation control must report0B; a new4,096-byte array must report positive allocation. This uniform BatchGC is an explicitly labeled measurement mitigation for the observed .NET8 background-GC allocation-accounting defect, not a production GC recommendation. Tiering/ReadyToRun are disabled, Linux CPU affinity and actual SDK/runtime are recorded. Payloads/manifests record System.Numerics.Vector hardware availability and int width, plus the current bulk policy’s dense-words-per-packed-record ratio where available. The portable flag disables explicit System.Runtime.Intrinsics kernels; System.Numerics.Vector and BCL acceleration may remain. Hardware availability alone does not prove a specific path executed.

Summaries compare Bulk against current ordinary Auto and original/previous Auto separately. A preparation win over the old AddTag loop must not be misrepresented as a bulk-policy win over the current FromTags baseline. Inspect h1 and h32, conversion costs, forced-layout alternatives, mixed layouts, memory and worst losses before recommending the policy. Median/p95/max refer to batch means and finite fixture ratios, not real gameplay frequencies, statistical significance or individual-operation latency. No Unity/IL2CPP/mobile approval is implied.

## Predeclared boundary holdout

`--suite boundary` is separate from development gates. `BulkBoundaryPlan.json` fixes the plan before gate3 results: new seed20261029; N just below/above2W/4W/8W/16W using actual registry WordCount including implicit parents; equal pairs; localized/scattered/mixed operands;50% overlap with spread/suffix shared members. Counts are clamped to valid pairs. These cardinality bounds are not exact layout transitions because ceilings and parent gaps change occupied records. There are96 fixtures and768 preparation/conversion/fresh+existing lifecycle rows per process. Run it against gate3's exact source snapshots, never merge it into gate3 or tune the policy from these results.

## Focused regression and A/A controls

`--suite regression --current-aa --rounds 3 --portable` runs16 declared fixtures and352 rows per process: mixed128:128 in both operand orders with spread/late overlap atU65,536; localized4,096 at both registry sizes; localized4,096:8 and8:4,096 with early/spread/late overlap; and mixed16,384 atU262,144 in both operand orders with spread/late overlap. All use50% overlap and the development seed. This is a regression suite, not a new holdout or a policy-tuning oracle.

The additional current-hardware-Auto-AA and current-portable-Auto-AA labels execute the exact same built DLL, source snapshot, API strategy and fixtures as their respective current-Auto label. They get independent fresh processes and are interleaved with the other labels in the rotating/reversing round order. The manifest records the original source roots, immutable snapshot paths, source inventory digests, actual DLL paths/hashes and harness/runner hashes; each raw process payload records matching attribution and independently hashes its loaded assembly. Summarization rejects mismatches and nonidentical A/A builds.

A/A ratios are reported directly as AA/Auto. Comparison JSON retains every process median, per-process and pooled p95/max batch-mean tail, and paired-round A/A ratios. Summary A/A groups include all expected rows and explicitly list any failed/unranked keys. No noise subtraction, corrected policy ratios or failure filtering is performed. These controls expose process/placement variability but do not identify its cause or prove statistical equivalence.
