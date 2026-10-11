# Full-context Union alias replication

Bounded standalone Mono diagnostic comparing exact f7 with f53 on predeclared AMD EPYC 9V74 slots. The original Tests~/UnionOutput/Program.cs and project are compiled unchanged with each Runtime. All28 rows (25 timed,3 retained), fixture construction/order,64 warmup calls, checksum/oracle checks and cohorts remain intact. No workload is shortened or added, and no new pilot runs.

The exact25-row iteration map from the verified f53 full Mono artifact11697920732 is reused byte-for-byte. Every AA/AB slot uses that map. Its SHA and expected28 row/count/cohort tuples are pinned in iterations.provenance.json. Calibration originally used only f7; candidate data does not select new counts.

AA executes the exact same physical original binary path twice per alternating round. AB executes original and f53 binaries. The original is compiled for factory+Copy and candidate for the reusable API; both complete correctness checks before measurement. Fourteen independent AB/BA pairs per mode; every matching host result retained/reviewed independently, unmatched slots metadata only. No native instrumentation or host security changes.

CLI: `python3 Tests~/UnionAliasRecheck/run.py --dotnet DOTNET --mono MONO --before ORIGINAL --after CANDIDATE --before-revision FULL_SHA --after-revision FULL_SHA --iterations-file Tests~/UnionAliasRecheck/iterations.tsv --mode aa --rounds 14 --out EMPTY_OUTPUT`. Repeat ab with a distinct output. One round is accepted only for orchestration smoke and produces unbounded timing intervals. Test-only skips measurements. Exact source/binary/map hashes, command stdout/stderr including failures, CPU/runtime/environment, execution order and all raw rows are preserved. Immutability checks run even on failure.

Prior7763 raw loops total roughly17.3 seconds/original process and11seconds/candidate. AA+AB14 pairs require about18.5 minutes measured work plus unchanged initialization, warmup, retained cohorts and builds; a60-minute job bound leaves headroom. This timing estimate is not a performance claim for9V74.

The prior9V74 subpercent alias-right cost and other historical observations remain in the evidence record. Broad intervals or an unmatched host do not establish non-regression. No extra rounds or outlier exclusions are authorized by this driver.
