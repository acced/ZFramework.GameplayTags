# Combined candidate matching-host guard recheck

Bounded original-program .NET diagnostic for original f7 versus combined candidate 579aaa1. This addresses prior AMD EPYC 7763 residuals that a newer 9V45 full run cannot resolve. No claim about unmeasured hosts or the full matrix follows from this supplement alone.

`isolated.py` compiles frozen Evolution Program.cs unchanged with its original separate Runtime DLL context. One process/registry, original fixture traversal, 64-call warmup, preflight/postflight and retained cohorts are preserved. Six rows have predeclared long counts: n4 half-overlap workspace reused and cold growth; n16 AnyHier nonself; depth1024 descending Union; n16 and n1024 AddTags-self. Other125 timed rows use65 calls, preserving execution but reducing preceding volume. All155 raw rows remain; only six selected rows receive decision summaries.

Counts use ceil(500,000,000 / fastest of all14 baseline ns samples) from verified ci-3e26bfd-net artifact11689963295. The provenance file records source hashes/formula. Every measured source uses the same counts. Candidate data did not choose them.

A/A invokes the exact same original binary in both slots; A/B invokes original/candidate. Fourteen independent process pairs alternate AB/BA. Same-model CI slots must be predeclared, every matching result retained/reviewed, and unmatched metadata never treated as a pass. No native/performance-counter instrumentation is added.

CLI: `python3 Tests~/CombinedGuardRecheck/isolated.py --dotnet DOTNET --before ORIGINAL --after CANDIDATE --before-revision FULL_SHA --after-revision FULL_SHA --iterations-file Tests~/CombinedGuardRecheck/iterations.tsv --mode aa --rounds 14 --out EMPTY_OUTPUT`. Repeat with mode ab and distinct output. Optional compile-only performs no measurements. Full lowercase revision labels, nonempty sources, exact source/binary hashes, CPU/runtime environment, counts/checksums and all raw CPU/wall/GC/allocation rows are retained or checked.

One local single-pair smoke is orchestration evidence only. Preparation may remain unexecuted in CI if the full Mono result rejects the combined candidate. Existing adverse full-run observations remain in the record.
