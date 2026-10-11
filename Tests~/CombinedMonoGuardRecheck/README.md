# Combined Mono residual replication

Bounded unchanged-program Mono supplement for exact original f7c99546dbf029d70896c0f05e5119b19ffd6d26 versus 579aaa1a56779694a4d16ba25ff38b53bafe93f2. Six rows are selected before measurement: n16/n1024 AddTags-self, n16 hierarchical HasAll-self, n1024 dense Clear+restore, depth3 duplicate-add and depth3000 duplicate-add control.

Original Evolution Program.cs and its Mono merged Runtime+Program build are unchanged. Each process retains all fixture traversal, 64-call warmup, preflight/postflight and retained cohorts. The six rows use fixed longer counts; other125 timed rows use65 calls. All155 raw rows remain. Reduced nonselected batch volume is a context limitation compared with the full matrix.

The map uses ceil(500,000,000 / fastest of all28 baseline observations across verified ci-3e26bfd-mono and ci-579aaa1-mono). Both variants and both host classes use the same map. Provenance includes both artifact digests and28 baseline raw hashes. No candidate observation chooses counts.

A/A uses the exact same original binary in both slots. A/B uses original and candidate. Fourteen independent process pairs alternate AB/BA. Predeclared matching AMD7763 slots investigate prior residuals; matching AMD9V74 slots investigate the new duplicate-add bimodality. Results must stay hardware-specific. Every matching result and both modes of the raw distribution are retained; unmatched metadata provides no conclusion. No native/counter instrumentation or operating-system setting change is added.

CLI: `python3 Tests~/CombinedMonoGuardRecheck/run.py --dotnet DOTNET --mono MONO --before ORIGINAL --after CANDIDATE --before-revision FULL_SHA --after-revision FULL_SHA --iterations-file Tests~/CombinedMonoGuardRecheck/iterations.tsv --mode aa --rounds 14 --out EMPTY_OUTPUT`. Repeat with ab in a distinct empty directory. Optional compile-only performs no measurements. Full lowercase revision labels, nonempty sources, source/binary hashes, runtime/CPU metadata, iteration counts and paired checksums are checked or recorded.

One local single-pair smoke validates orchestration only. Full-matrix unfavorable evidence remains in the record; a broad diagnostic interval is not proof of non-regression.
