# Preserved local evidence

These are separate completed local stages, not public-package execution. Private raw artifacts are not included in this package. `source-pins.json` binds the source/result manifests by digest.

## Semantic and natural native stage

- Exactly two compiler attempts and four fresh targets completed within the 120-second limit: empty/retry modes on original/candidate.
- Every semantic trace and complete physical registry snapshot matched. Throwing constructor retry, failed publication, malformed declaration sequencing, ordinary/derived typed arrays, merge/order and public initialized contexts passed.
- Native files were collected in full after process exit, without PrepareMethod, copied prefixes, forced Runtime inline/noinline annotations or recompilation.
- InitializeIfNeeded shrank 70 bytes in each mode: 951 to 881 bytes for empty, 898 to 828 for retry.
- Actual old code retained interface GetEnumerator, Current, MoveNext and normal/exceptional Dispose work. Candidate removed those calls and the enumeration EH funclet. The main saved registers and 40-byte stack allocation stayed the same.
- Candidate adds/retains a typed-array check with cast fallback, array/index/length work and stack traffic around RegisterTag; no separate element bounds-check branch was seen. The Monitor EH remains. All 16 other paired captured native contexts were equal, including public-trigger and initialized contexts.
- Framework enumeration callees were not independently captured; no isolated enumerator object-size or deployed-framework IL claim follows.

Semantic/native result-manifest SHA256: `17883620a94ecd4fc5eff59b12cf921f3b4167e660110e5180d318ae53d9d597`.

## Persisted first-discovery stage

Exactly four compiler attempts and all 24 fresh targets completed within 120 seconds. n0/n1/n32 each used AB, BA, BA, AB pairs. All 12 full snapshot/inventory pairs and all semantic/oracle gates matched. GC0/GC1/GC2 were zero in every sample. Neither sample replacements nor attribution-driven exclusions were made.

Allocation bytes were constant within each shape/variant:

| Shape | Original | Candidate | Candidate minus original |
|---|---:|---:|---:|
| n0 | 9608 | 9576 | -32 |
| n1 | 10624 | 10560 | -64 |
| n32 | 30072 | 30008 | -64 |

Both nonempty shapes have one attributed assembly. The extra 32-byte difference versus empty is consistent with removal of nonempty enumeration work, but this does not independently identify every allocated object or explain empty's 32 bytes.

All latency ratios (candidate/original), in fixed pair order:

| Shape | AB | BA | BA | AB |
|---|---:|---:|---:|---:|
| n0 | 0.954785 | 1.257063 | 0.582886 | 0.951093 |
| n1 | 1.075382 | 0.991539 | 0.882343 | 0.953313 |
| n32 | 0.711035 | 0.639162 | 1.073414 | 0.464732 |

Each shape has one adverse pair. Large original-side latency outliers remain unexplained and retained. These few first-use measurements do not establish a latency pass. After-inventory-only framework loads cannot be attributed exclusively to discovery because capture occurs at post-counter validation entry.

Persisted cold result-manifest SHA256: `889d166d013cf2967fbd7aeea99cf423853f3b2c4fd0bc9ed533b4893929cb4f`.

Qualification remains 0/10. This package requests only a bounded dual-runtime semantic/persisted-first-discovery portability screen, followed by evidence review. It does not automatically accept the optimization.
