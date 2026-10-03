# Published PR26 Runtime refinement: managed validation

This is the experimental source and reproducible managed validation package for the selected PR26 refinement. The usable package is **[source/selected](source/selected)**. Repository-root `Runtime/` remains the historical packed implementation; it is neither the selected source nor this benchmark's baseline. No existing workflow or root Runtime source is replaced.

The baseline is PR26's measured experiment at commit `98311092b7e8892e5632c2863a391187f4367cf8`, generated variant `empty`: guarded capacity plus empty-source Clear/return, without NoInlining. Its exact kernel/builders are normalized into the same additive Runtime bridge and checked-guard integration scaffold. This is not a comparison against default shipped RuntimeTagSet. `source/baseline` is that matched baseline. `source/bridge-before` is the admitted selected-v3 source before splitting the bridge range helpers; it is used only for the explicitly labeled bridge comparison.

Source selection and all immutable payload bytes are pinned by [source-selection.json](source-selection.json) and [payload-sha256.json](payload-sha256.json). The driver validates the complete 14-file Runtime inventory for each source, its original integration proof, and its canonical source-set digest before copying or compiling. Source selection is always explicit. Tests, facade, Runtime and Editor compile into separate assemblies; no production source is merged into the test host. The selected publication documentation has an updated status paragraph; Runtime source bytes are unchanged.

## What Actions runs

The new workflow is `.github/workflows/pr26-runtime-refinement.yml`. Pushes only on `refactor/runtime-adaptive-20261001` touching its source, runner, fixtures, source/provenance manifests or that workflow launch it. Historical-data and result-only publications do not match the push paths. Manual dispatch is supported only for the same branch. No pull-request trigger or unrelated path filter was added.

- Six correctness jobs: Linux x64 and native Linux ARM64, each with normal net8, checked net8 and an actual checked netstandard2.1 Runtime library referenced by a separate net8 host. All seven selected correctness suites execute on default and hardware-disabled backends, and separately instrumented allocation-fault suites run against the selected library in each configuration. The matched baseline also executes all seven suites in normal net8. Checked baseline is not a gate: the frozen original kernel predates the candidate's checked-cast fixes
- Sixteen performance jobs after correctness succeeds: architecture × default/nohw × query/builder/mixed/bridge. Every job builds its own libraries on its actual architecture, binds host/dependency hashes, and checks the Runtime DLL SHA256/path reported by the loaded process
- Query, builder and mixed cover 111, 90 and 225 cells per backend, respectively, across Micro/Dense/Auto. Both backends produce 852 cells and 53,676 actual samples per architecture. Bridge adds 54 cells per backend with legacy A/A, old Direct B and split Direct C (4,536 actual samples per backend). Construction/freezing is outside the bridge timing; charged mutation rows retain their original public calls and representation differences
- Three bracketed rounds, seven samples per process, paired calibration toward 5 ms, fixed common iteration counts, 64 MiB sample-allocation ceiling, actual sample minimum 1 ms, Batch GC, CPU affinity, tiering/PGO/ReadyToRun/concurrent-GC controls, and the original row validity/classification rules are unchanged. All measured processes run sequentially within each dedicated hosted job
- Untimed feature probes report process/OS architecture, Vector support/lanes, AVX2, Popcnt/X64, AdvSimd/Arm64 and hardware-disable controls. ELF and uname checks reject wrong-architecture SDKs. The ARM64 label uses native ARM hardware, not a QEMU job; this is a hosted VM, not a claim of bare-metal isolation

The C# timed bodies, Python benchmark runners and analyzers in `inputs/` are byte-identical to the validated local inputs. The adapter provides a disposable directory and the original relative SDK mapping; it does not patch their bodies, thresholds or plans. Native hardware intrinsics and nohw remain separate cohorts. nohw is not an emulation of the netstandard library, and checked Standard correctness is not Standard timing evidence.

Jobs use 40-minute correctness and 60-minute performance timeouts. The historical x64 full groups took roughly 2–5 minutes of measurement each; the larger margins include compilation and slower/noisier hosted machines. Performance is split by complete group/backend so each paired cohort stays on one host. The workflow does not cancel earlier runs on a new push. Fail-fast is disabled and raw partial results are uploaded on failure. Each archive includes build logs, fixtures/projects, per-build DLLs, source/loaded-DLL bindings, commands, feature metadata, all samples, stdout/stderr, calibrations, iteration plans and classification summaries. SDK symlinks and CLI caches are excluded.

## Run locally or on a native runner

Requirements: Linux, Python 3, `taskset`, and the native .NET SDK 8.0.425. The workflow installs SDK 8.0.425. The driver requires that exact installed SDK, writes `global.json` with roll-forward disabled, and records `dotnet --info`. Newer SDKs already installed on a hosted runner cannot silently change the compiler. The observed .NET 8 runtime patch is recorded per process. Checkout, SDK setup and artifact upload Actions are pinned to verified official commit IDs in the workflow. No NuGet packages are required; package feeds are cleared. Use a fresh work directory for every invocation. Reserve the selected CPU from other measurement/build activity.

```sh
python3 'Audit~/PR26RefinementCI/run_ci.py' verify

# Small relocation, metadata, checked Standard and separate-host preflight; no timings
python3 'Audit~/PR26RefinementCI/run_ci.py' preflight \
  --expect-arch x64 --smoke netstandard-checked --work /tmp/pr26-standard-preflight

python3 'Audit~/PR26RefinementCI/run_ci.py' correctness \
  --expect-arch x64 --configuration net8-normal --work /tmp/pr26-correctness-normal
python3 'Audit~/PR26RefinementCI/run_ci.py' correctness \
  --expect-arch x64 --configuration net8-checked --work /tmp/pr26-correctness-checked
python3 'Audit~/PR26RefinementCI/run_ci.py' correctness \
  --expect-arch x64 --configuration netstandard-checked --work /tmp/pr26-correctness-standard

# Complete x64 broad and bridge matrices; use arm64 on native ARM64 hardware
for backend in default nohw; do
  for group in query builder mixed bridge; do
    python3 'Audit~/PR26RefinementCI/run_ci.py' benchmark \
      --expect-arch x64 --group "$group" --backend "$backend" \
      --work "/tmp/pr26-x64-$group-$backend"
  done
done

python3 'Audit~/PR26RefinementCI/run_ci.py' archive \
  --work /tmp/pr26-x64-query-default --output /tmp/pr26-query-evidence.zip
```

Use `--dotnet /absolute/path/to/dotnet` to select a particular complete installation containing SDK 8.0.425. x64 and ARM64 each rebuild from identical pinned source bytes. Historical local DLL hashes (`1c032a39…` selected, `1dd0309a…` baseline) are provenance only; the driver never requires rebuilt or ARM64 DLLs to equal those x64 bytes. Each new build has its own verified identity. Do not move a work directory mid-run: completed result manifests retain absolute within-run paths, while their original payload and launch commands are relocatable.

After publishing, a push that adds this path to the exact branch triggers the workflow. If it is already available for dispatch:

```sh
gh workflow run pr26-runtime-refinement.yml --ref refactor/runtime-adaptive-20261001
```

Inspect checks on the exact published commit and both architectures before making any claim about the new Actions results.

## Honest interpretation

A green performance job means the workload executed, source/binary binding passed, and the original analyzer completed. It does **not** mean there were no regressions or invalid samples. New regressions, uncertain cells and invalid controls remain in every summary; invalid cells are unranked. No new architecture's outcomes are inferred from x64. Hosted runner noise and A/A drift remain visible.

The original local result is preserved under [historical](historical). Its full 852-cell matrix contains **four median regressions and six invalid controls**. `verify` recalculates those counts from all six historical summaries and refuses deletion/relabeling. The four regressions are Dense balanced-8 query/default, Auto missing-bit-8 query/default, Dense sorted clustered-4096 builder/nohw, and Micro random-4096 restore-remove/nohw. The six invalid cells are the Dense empty sorted, empty shuffled and singleton sorted controls on both backends, with actual samples below 1 ms at the original budget cap. P95/max batch-tail ratios remain in the summaries. These results are not overwritten or converted to passes when Actions is added.

These are managed .NET8 benchmarks with a separate Unity API test facade. Unity Editor tests are only compile-checked in this runner; there is no native Unity Editor, Mono player, IL2CPP, Burst or device performance claim. Native Editor/device gates remain outstanding.

Native runner documentation: [GitHub-hosted runner labels and architectures](https://docs.github.com/en/actions/reference/runners/github-hosted-runners). Separate historical-data archives, if present, have their own manifest and are not inputs to new measurements.
