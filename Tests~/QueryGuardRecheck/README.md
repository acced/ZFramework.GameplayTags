# Bounded query guard recheck

Supplemental .NET diagnostic for exact original f7c99546dbf029d70896c0f05e5119b19ffd6d26 and combined candidate 3af34cba7c0e70c2fc8e3f3a5f1f930ebf079991. It cannot replace the full matrix or excuse an unfavorable sample.

`isolated.py` compiles the frozen Evolution Program.cs unchanged using its original separate Runtime assembly build helper. Each measurement process has one registry and original fixture traversal, preflight/postflight, 64-call warmup, and retention cohorts. There are no producer threads or native-inspection runs.

Four original rows have longer fixed counts: n1024 any-exact-nonself, n16 all-hierarchical-self, n1024 any-hierarchical-nonself control, and n1024 addtags-self control. Counts are ceil(500,000,000 / fastest observed baseline ns) using every baseline sample from the verified 3af .NET artifact; provenance and hashes are included. Both slots use identical counts. The other 127 timed rows use 65 calls, so total preceding workload volume differs from the full matrix. All 155 rows remain in every raw report. Only these four preselected rows are decision summaries; all other rows are diagnostic context.

AA invokes the exact same baseline binary in both slots. AB invokes original and candidate binaries. Fourteen independent paired process rounds alternate AB/BA; every raw sample, CPU/wall/GC/allocation row and matching-host result must be retained. No-match AMD host slots provide no performance conclusion. The workflow predeclares two AMD EPYC 7763 slots, preserving both matching outcomes without choosing a favorable one.

CLI: `python3 Tests~/QueryGuardRecheck/isolated.py --dotnet DOTNET --before ORIGINAL --after CANDIDATE --before-revision FULL_SHA --after-revision FULL_SHA --iterations-file Tests~/QueryGuardRecheck/iterations.tsv --mode aa --rounds 14 --out EMPTY_OUTPUT`. Repeat in a distinct empty output with mode ab. Optional `--compile-only` records the exact source/assembly/environment provenance without measurement.

Full lowercase 40-character revision labels and nonempty Runtime source trees are required. SHA-256 source and assembly digests, rather than labels alone, identify tested code. Local one-pair smokes validate orchestration and correctness only; they are not acceptance evidence.
