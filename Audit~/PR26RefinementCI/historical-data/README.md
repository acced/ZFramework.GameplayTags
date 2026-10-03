# Historical PR26 evidence

This directory preserves the selected historical **Linux x64 .NET 8.0.31** performance and validation record. It contains no new timing measurements. The final cohort has **852 cells and 53,676 timed samples: 68 median improvements, 4 median regressions, 774 uncertain/small cells, and 6 invalid controls**. The losses and invalid controls remain in the raw data and [readable results](RESULTS.md).

The selected Runtime DLL was `1c032a39cf571ba4ef80ff22c12dd99049b01e1ce0a2cd34040868d899188fa3`; the matched baseline was `1dd0309a59eb38834eed5da8edfd4e959d9b584457976ed5c69d61fcc2a4d834`. The adjacent [selected source](../source/selected/), [baseline source](../source/baseline/), [bridge-before source](../source/bridge-before/), and CI fixtures are published separately. Source and DLL hash receipts in these archives are historical records. **Historical compiled DLLs, PDBs and executable hosts are omitted.** This directory cannot independently replay those exact binaries or prove their omitted bytes from hash receipts alone.

## Read and verify

- [RESULTS.md](RESULTS.md): six-cohort totals, all four final losses, all six invalid controls, separate factors, and constructor-diagnostic outcome
- [FAILURES.md](FAILURES.md): retained failures and their current interpretation
- [inventory.json](inventory.json): every retained original relative path, archive/member, byte length and SHA256; each archive's digest and size
- [PAYLOAD_REVIEW.md](PAYLOAD_REVIEW.md): publication selection, exclusions and limits
- [verify.py](verify.py): archive/member integrity and safe-path checks, without extraction
- [reanalyze.py](reanalyze.py): read-only statistical recomputation from archived raw samples
- [statistical-verification.txt](statistical-verification.txt): recorded portable reanalysis output
- [SHA256SUMS](SHA256SUMS): hashes of every published file in this directory except the checksum file itself

From this directory, with Python 3.9 or later:

```sh
python3 verify.py
python3 reanalyze.py
```

These commands do not run .NET, build code, rerun benchmarks, modify recorded data, or follow historical absolute paths. `reanalyze.py` reports raw-data/statistical integrity only; it does not turn omitted historical binary or compiler checks into passes. The original analyzers and runner sources remain in the archives as reference methods. Their original entry points assume the historical filesystem and binaries and are not the portable command above.

## Archive layout

| Archive | Included original paths |
|---|---|
| `final-query.tar.gz` | Complete `benchmark/results/final-query-{default,nohw}/` |
| `final-builder.tar.gz` | Complete `benchmark/results/final-builder-{default,nohw}/` |
| `final-mixed-default.tar.gz` | Complete `benchmark/results/final-mixed-default/` |
| `final-mixed-nohw.tar.gz` | Complete `benchmark/results/final-mixed-nohw/` |
| `bridge-factor.tar.gz` | Complete `benchmark/results/bridge-split-factor-{default,nohw}/` |
| `count-factor.tar.gz` | Complete `benchmark/results/factor-v3-{default,nohw}/` |
| `constructor-diagnostic.tar.gz` | Sorted-history raw results, scientific protocol, source/count plan, integrity checks, native text/captures, host receipts, and independent review; no binaries/cache directories |
| `validation-and-methods.tar.gz` | Six independent final reviews; selected source/bridge proofs; managed semantic, fault, compiler/source/DLL-binding records; methods and factor/native evidence |
| `historical-failures.tar.gz` | Checked arithmetic failures, earlier invalid union-failure states, checked original-suite failures, and an incomplete early query run |

Archive members retain their original relative paths. `inventory.json` records the historical root needed to interpret unmodified absolute paths embedded in receipts; those paths are data, not usable dependencies or instructions. Each file is byte-for-byte unchanged. Only tar metadata is normalized (owner names, mode and timestamp). The inventory maps the complete selected public evidence set, not every file or dependency in the historical workspace.

## Historical source locations

Original source roots referenced by retained compiler receipts map to the adjacent published source as follows. The original receipt bytes were not rewritten to use these new locations.

| Original path below the historical root | Published location |
|---|---|
| `runtime-integration/bridge-split-v1/source/` | [`../source/selected/`](../source/selected/) |
| `runtime-integration/checked-guards/baseline/source/` | [`../source/baseline/`](../source/baseline/) |
| `runtime-integration/selected-v3/source/` | [`../source/bridge-before/`](../source/bridge-before/) |

Generated selected-v3 and baseline kernel inputs are retained in `validation-and-methods.tar.gz`. An archived receipt pointing to any other old source tree or binary does not imply that dependency is published. Current source/fixture verification belongs to the adjacent CI's own manifest and results.

## Scope

All performance observations here are single-host managed x64 measurements. `nohw` means the .NET 8 binary ran with hardware intrinsics disabled. Checked `netstandard2.1` records are correctness evidence from separately compiled libraries running in managed hosts, not Standard performance measurements. Native text in these archives is .NET JIT diagnostic output, not native Unity acceptance. Unity player, Mono, IL2CPP, Burst, ARM64 and device performance were not measured in this historical set.

The adjacent CI can produce new platform results. Those new artifacts must identify their own source, architecture, SDK/runtime and binaries; they do not retroactively expand the scope of these historical observations. Earlier readouts can reference evidence outside this deliberately selected publication set. Missing old binaries or older source snapshots are not implied to be included.
