# Original-program Mono alias replication

Bounded supplement comparing exact f7c99546dbf029d70896c0f05e5119b19ffd6d26 with 856eee86c1d2c5288bb6f9e5efa33992b81d90ce. The main matrix remains authoritative; this diagnostic resolves a specific alias signal and does not replace it.

The original Evolution Program.cs is compiled unchanged through its existing Mono helper and project. Mono uses a merged Runtime+Program assembly, one process/registry, no producer threads or native instrumentation. All original fixture traversal, 64-call warmup, preflight/postflight and retained cohorts remain. Six preselected rows use longer counts: n128 half/full overlap with left/right output alias, and n16/n1024 AddTags-self controls. The other 125 timed rows use 65 calls, so preceding workload volume differs from the full matrix. All 155 raw rows are retained.

Counts are ceil(500,000,000 / fastest of all 14 original baseline ns samples) from verified artifact11686083464. The provenance file records all raw hashes and the formula. Both measured slots use identical counts; no candidate samples set them.

A/A invokes the exact same baseline binary twice per round; A/B invokes baseline/candidate. Fourteen independent paired rounds alternate AB/BA. Two predeclared CI slots require AMD EPYC 7763 before measuring. Every matching result must be retained and reviewed; a no-match slot is metadata only, never a pass.

CLI: `python3 Tests~/AliasGuardRecheck/run.py --dotnet DOTNET --mono MONO --before ORIGINAL --after CANDIDATE --before-revision FULL_SHA --after-revision FULL_SHA --iterations-file Tests~/AliasGuardRecheck/iterations.tsv --mode aa --rounds 14 --out EMPTY_OUTPUT`. Repeat with ab and a distinct empty output. `--compile-only` is available. One-pair local smokes establish orchestration/correctness only.

Full lowercase SHA labels, nonempty Runtime trees, exact source and binary hashes, host/runtime/CPU metadata, raw CPU/wall/GC/allocation values, paired checksums, registry counts and fixed iteration counts are recorded/checked. Original source and output binaries are rehashed at completion. Timing classifications are review flags, not automatic acceptance.
