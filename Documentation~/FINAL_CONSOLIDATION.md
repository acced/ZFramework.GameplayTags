# Final consolidation candidate against 7037e76

## Status and baseline

This is a **review candidate, not an accepted universal-performance release**. The frozen task-start branch is `7037e76a43127939ab9545b2e1056441202961e6`. All comparisons use that exact Runtime, including its reusable Union overload and adaptive queue implementation, not f7, Alex, or a weaker factory-plus-Copy substitute.

The local final candidate completed 56 identical-public-API workloads per backend with 14 alternating independent process pairs. These are .NET 8.0.31 and standalone Mono 6.8.0.105 on the same x64 machine. Unity 6000.3.14f1 Editor, Player, IL2CPP and 32-bit execution were **not** performed.

**Warm improvements do not remove the first-use tradeoff.** Four-pair first-use checks show repeatable cold latency losses in .NET first AddTag and in the general intersection on both backends. They remain explicitly recorded below. No successful-version counter is incremented by this candidate. Green correctness/evidence CI is not an assertion that every workload is faster.

## Implemented changes

* Clear invalidates the active bucket mask in constant work rather than hashing and clearing every occupied slot. Its previous live count becomes a rebuild hint. Refill clears/rebuilds one appropriately sized index instead of repeatedly growing from the small-table threshold. Sparse histories retain no registry-sized scratch. Deferred work is included in the `clear-refill` benchmark; the earlier naive invalidation prototype was slower there and was replaced.
* Normal AddTag fuses the explicit and self contribution into one lookup. Only strict ancestors remain in its update loop. The existing counted mutation, event transaction, exception cleanup and Union algorithms are retained.
* The first explicit tag completes its ancestor path in one bulk pass, preserving leaf-first dense order. Deep first construction skips repeated entry-array growth and repeated index construction. The result remains independent and fully mutable.
* ContainsExplicit consumes the matching entry directly inside its probe loop. Generic Find is unchanged.
* The 16-byte x64 concrete enumerator stores an iteration bound and explicit-mode bit in one integer. MoveNext no longer fetches the storage count and mode on every step. Reset and interface enumeration remain supported; as before, mutating the source invalidates an enumerator.
* Identical-storage HasAll/HasAny operations use their set identity rather than probing themselves repeatedly. Independent but equal containers do not use this shortcut.
* Intersection separates complete-hit proof from general result collection. Complete ordinary-set hits copy before allocating or writing selection scratch. Small selections use bounded stack buffers; partial paths preserve counted normalization, both output aliases, custom integer sorting and optional sort-free workspace behavior.

There are no new public APIs to opt into and no unsafe pointer dependencies. Existing calls receive these changes. Runtime IDs, name serialization, unordered enumeration, valid-tag/positive-count/single-writer contracts are unchanged. No null/argument checks were removed as a substitute for algorithmic improvement.

## Fixed local results

Times below are microseconds for the **entire named operation**, including output replacement, creation or refill where indicated. Ratios are paired geometric speedups, not a ratio of the displayed medians. They do not imply statistically established gains for every row. Complete 56-row medians, approximate 99% intervals, bytes and slower-pair counts are in `results/final-consolidation/local-*-comparison.json`.

| Operation | .NET before / after (us) | .NET paired ratio | Mono before / after (us) | Mono paired ratio |
|---|---:|---:|---:|---:|
| `n1024/query-exact-hit` | 0.0053 / 0.0046 | 1.183x | 0.0103 / 0.0091 | 1.163x |
| `n1024/enumerate-explicit` | 1.3694 / 1.3027 | 1.095x | 6.9515 / 4.2266 | 1.650x |
| `n1024/remove-add` | 0.0441 / 0.0385 | 1.163x | 0.1434 / 0.1372 | 1.033x |
| `n1024/fresh-build` | 48.0130 / 39.6321 | 1.184x | 130.5980 / 124.2698 | 1.072x |
| `n1024/copy-reused` | 0.8209 / 0.8471 | 0.989x | 1.5714 / 1.5358 | 1.032x |
| `n1024/union-new` | 15.5547 / 16.1351 | 0.996x | 48.6719 / 49.2336 | 1.001x |
| `n1024/union-reused` | 9.7611 / 9.8130 | 0.991x | 24.4863 / 23.3531 | 1.027x |
| `n1024/copy-addtags` | 21.9084 / 18.4685 | 1.172x | 52.8627 / 47.4510 | 1.100x |
| `n1024/intersection-reused` | 12.2321 / 12.0494 | 1.060x | 37.7207 / 35.1866 | 1.078x |
| `n1024/intersection-workspace` | 9.4118 / 9.6458 | 0.975x | 27.5529 / 26.4649 | 1.120x |
| `n1024/intersection-full` | 5.9982 / 4.2178 | 1.409x | 16.0391 / 11.5761 | 1.384x |
| `n1024/copy-clear` | 2.2348 / 0.8296 | 2.668x | 4.4094 / 1.5292 | 2.808x |
| `n1024/clear-refill` | 26.3759 / 19.9440 | 1.333x | 71.7437 / 61.7678 | 1.177x |
| `depth128/fresh-single` | 2.3643 / 1.2560 | 1.907x | 3.2620 / 1.4584 | 2.238x |
| `depth3000/fresh-single` | 47.2328 / 17.0454 | 2.722x | 146.7945 / 59.6711 | 2.551x |
| `buff/dense-depth128` | 7.0542 / 7.2017 | 0.992x | 13.2108 / 13.9913 | 0.991x |

The default fixture has 50% explicit overlap. `intersection-full` uses two independent equal inputs. `copy-addtags` times the pre-existing allocation-free alternative, `Copy(output, a); output.AddTags(b)`, on both versions. `union-reused` invokes the same already-existing reusable overload on both versions. No implementation is compared with a deliberately allocating fallback.

* net: 20 intervals establish more than 3% improvement; 0 establish more than 3% regression. Only 28 of 56 bound potential regression below 3%. The remaining intervals are uncertain, not passes for global noninferiority.
* mono: 18 intervals establish more than 3% improvement; 0 establish more than 3% regression. Only 28 of 56 bound potential regression below 3%. The remaining intervals are uncertain, not passes for global noninferiority.

Timing uses one baseline-only pilot targeting 50 ms and a shared immutable iteration count for both versions. The candidate can finish a batch sooner; the actual minimum is retained in every row. Tiering is disabled, workstation GC is used, and both variants run sequentially pinned to the same allowed CPU. There is no sample removal, A/A drift subtraction or rerun-until-passing rule. Intervals assume independent paired log-ratios and are approximate, per-row rather than family-wise guarantees. Measurements have visible host/process variation.

## Allocation and retained memory

All final benchmark warm paths that were zero bytes before remain zero bytes after. Existing fresh-output operations still allocate. Deep single-tag construction allocation decreases:

| Backend | depth 128 before / after | depth 3000 before / after |
|---|---:|---:|
| net | 6,248 / 3,416 B | 196,952 / 98,648 B |
| mono | 6,328 / 3,448 B | 197,112 / 98,680 B |

Entry remains four integers, and the ordinary container still owns its entry, explicit-index and bucket arrays. This is **not** a general reduction of ordinary-set retained array capacity. The new four-byte rebuild-hint field fills existing object alignment padding on the tested x64 layouts; the measured normal creation allocations do not grow. A 32-bit layout may grow by four bytes and was not executed. Workspace retention is unchanged. GC counters measure calling-thread managed allocation bytes, not Unity GC.Alloc event counts, native/JIT code size or process-wide pool memory.

## First-use evidence and blockers

Eight fixture kinds, four alternating pairs each, give 64 fresh processes per backend (128 total). No operation being measured is warmed beforehand; inputs are prepared as required by that fixture. All paired observable states agree and allocated bytes never increase in the sampled pairs. Local raw results are in `results/final-consolidation/local-cold.json`.

* net `fresh` n=1: slower in 4/4 pairs; after/before ratios 1.591, 1.705, 1.365, 1.459.
* net `fresh-deep` n=128: slower in 4/4 pairs; after/before ratios 1.186, 1.320, 1.105, 1.258.
* net `intersection` n=1024: slower in 4/4 pairs; after/before ratios 1.096, 1.108, 1.102, 1.603.
* mono `intersection` n=1024: slower in 4/4 pairs; after/before ratios 1.167, 1.156, 1.157, 1.090.

These are substantial first-use costs, not cleared by equal allocation or by warm improvements. Four pairs do not establish a universal cold-latency bound. Startup/JIT preparation was not moved outside the operation to conceal it. Runtime initialization can allocate on first use even for a warm-zero operation; cold and warm bytes are separately recorded.

## Correctness and reproduction

The final source passes the existing 7,595,855-assertion core suite on both runtimes; the 31,738,094-assertion Bulk suite on .NET; and the 16,101,616-assertion intersection suite on Mono, plus the original Evolution semantic matrix on both runtimes. Additional 646,194-assertion independent-name-prefix tests pass on **both baseline and candidate, both backends**. They alternate large/small/empty clears and refill, reserved capacities, inactive stale buckets, default/reset enumerators, aliases, exact counts and full drain. The exact final Runtime also passes the standalone registry and 3000-level hierarchy suites, source-generator checks, .NET Standard 2.1 runtime compilation, and Unity conditional compilation against stubs. This is not actual Unity execution. Published CI must still be read independently for its exact commit.

```sh
mkdir baseline-7037
# Run from a clone containing the pinned commit.
git archive 7037e76a43127939ab9545b2e1056441202961e6 Runtime | tar -x -C baseline-7037
python3 Tests~/FinalConsolidation/run.py --baseline baseline-7037 --backend net --samples 14 --target-ms 50 --output evidence-net
python3 Tests~/FinalConsolidation/run.py --baseline baseline-7037 --backend mono --samples 14 --target-ms 50 --output evidence-mono
```

The runner requires .NET SDK 8; Mono mode additionally needs a standalone Mono installation. It saves all raw samples, both source and binary hashes, shared iteration counts, execution order, runtime/CPU, allocation/GC data and first-use observations. It never disables correctness checks to make a measurement pass. CI artifacts contain the same evidence types. Unity users should still run their original 6000.3.14f1 harness; desktop .NET and Mono are not substitutes for that test.

## Retained unsuccessful experiments

The first local 131-row exploratory run stopped at its 200-second tool limit after three of four intended pairs; it was not completed or used as final acceptance evidence. Subsequent 56-row exploratory revisions exposed refill costs and small-path signals. One Mono exploratory run stopped with seven of eight intended process samples. All such partial/negative local samples are retained in the conversation evidence archive, never substituted for the final fixed 14-pair runs. The final bundle also retains the previous 14-pair revision rather than overwriting its results. This final PR does not claim that the complete legacy 131-row **timing** matrix was requalified; its semantic matrix was rerun, and 56 hot-path timing fixtures were measured.
