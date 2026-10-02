# Prefix-aware mixed append: avoid work from proven ordering, not a fitted size threshold

Base: PR19 cffe3c54ec881352ea727a30371122c378bf4839. Generated controls are the exact PR18 fixed cursor/insertion and PR19 bounded two-pass merge. New independent alternatives: monotone and stream. Production files remain untouched.

Monotone replaces each binary search with a forward-only destination index. It still moves a suffix for each missing record; worst-case quadratic movement remains. It is a competing simple implementation, not a universal winner.

Stream merges equal-key source prefix records in place and counts their duplicate bits. If the destination ends first, the rest of the source is a sorted, unique tail, written through PutRecord in the existing growth schedule. If all source records match existing keys, it returns after one source traversal. Neither case uses reverse traversal. At the first new interior key only, continue the existing forward cursor to count the exact remaining union records, then reverse-merge ONLY that unprocessed source suffix with PR19's last-word start bound and record-count termination. Prefix records are not rescanned or double-counted. No numeric threshold, domain restriction or persistent usage statistics.

Count = original destination Count + source Count - original overlap bits. Prefix writes cannot affect overlap of the remaining source because source keys are strictly increasing. During the reverse merge write>=read. Previously merged prefix is before the first missing source key, so remains untouched. Empty-source, owner, aliases and layout validation stay at original callers; source cannot concurrently change. Success contracts identical. As in prior experiments, an out-of-memory exception need not leave the old mutation state intact.

Records16B/Enumerator32B and the existing private cursor fields are unchanged; no extra member arrays, set fields, cached facts, COW, pools or deferred Count. Interior growth uses the same ReserveEntries capacity staircase; pure tail uses the original PutRecord increments. Scalar-insertion total allocation and final capacity must match in inherited1920growth tests, not just steady-state0B. Capacity remains available for arbitrary subsequent tags; no contextual tag-domain restriction.

Testing: all inherited65536masks×4slices and mixed/Local/Micro tests; new1175prefix/tail/gap/duplicate-bit/continued-mutation cases. Four independent builds, original2520 public-operation +192paid-both-input lifecycle +48append-shape cases, same-binary A/A,7samples×3shuffledprocessrounds. No substituted Union for Copy+Append, no prepared substitution for fresh. Compare stream/bounded, stream/fixed and stream/monotone, retain every regression. SDK8.0.425/net8.0.31, hardware-disabled and separately compiled Standard2.1 checks. NativeUnity/IL2CPP/Burst/phones not run.

The official .NET List implementation illustrates that inserts inside a range require suffix movement while tail inserts do not; this project additionally must merge same-key bitmap bits and preserve exact Count. This is not a port or a claimed .NET/Unity benchmark: https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/List.cs

Full-history checkout required:
```
python3 'Audit~/DenseStream/run.py' --output artifacts/results --rounds 3
python3 'Audit~/DenseStream/review.py' --root artifacts/results
```
The runner/verifier reuse the previous measured harness, changing only generated alternatives and additional correctness checks. Each candidate keeps one membership representation. Review checks exact generated sources, binaries, round medians, allocation/count/capacity identity and A/A. Collect downloads both originals and repeats their archived reviewer. Counts include repeated test configurations. Five-percent screening is not a confidence interval, proof of cause or universal win.
