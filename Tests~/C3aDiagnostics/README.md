# Historical lifecycle diagnostics accompanying C3a

This package records diagnostic evidence for **B11, C1 and current792**, not the experimental C3a candidate. All three original validation-only JIT targets passed. No profiler workload ran: Unix-socket permission failure stopped the first trace attempt before target launch; one approved elevated retry failed during environment bootstrap. There is no CPU/GC trace result. See [REPORT.md](REPORT.md).

The unchanged lifecycle contains the actual **1024-target ← 512-source** CopyFrom, preserving target/source representatives and natural caller code. Every JIT target executed 64 warmups plus one requested lifecycle. The optional profiling proposal uses 64 warmups plus exactly 4096 requested lifecycles. Neither mode emits formal timing samples.

## Included sources and evidence

`harness/Program.cs`, `UnityStubs.cs` and `NuGet.Config` are byte-identical to `Tests~/C12Measurements` at `792ce83f1c2a9aeaadc045c192d70081d866d903`. The project sets the diagnostic assembly name; it adds no forced inlining/no-inlining or production instrumentation. `HARNESS_ORIGIN.json` records the hashes.

Source identities are frozen in `SOURCE_MANIFEST.json`:

- B11: `352d304299ecbb7486e2c6a56ba71874c90fe21b`, existing vendored `../C12Correctness/Baselines/PR4/Runtime`.
- C1: `6830b912ad5c3eb00f78059e7c1f374e4d959028`, existing vendored `../C12Correctness/Baselines/C1/Runtime`.
- current792: `792ce83f1c2a9aeaadc045c192d70081d866d903`, shared frozen `../C3aMeasurements/Sources/current792/Runtime`.

All seven C# files in each snapshot were verified against the indicated commit. `build.py` verifies every hash before and after compilation. It never selects working-tree `Runtime` implicitly. If the shared current792 snapshot is absent, the script can obtain it with read-only `git show` from that exact commit. No network fetch is automatic. In a shallow source checkout, obtain that commit yourself from the verified repository origin, or supply an existing exact seven-file snapshot using `--current792-runtime`. That input is still hash-checked. A full source archive with the shared snapshot needs no git history.

`Evidence/` contains normalized native listings preserving small numeric constants and offsets, pairwise differences, validation results, source/binary fingerprints, and sanitized trace failure status. Only the observed canonical 12-digit native-address forms are replaced; packed UTF-16 comparison constants and other immediates remain. The sanitizer is specific to these Linux x64 captures and requires review for a different address layout. Full raw dumps and environment-specific receipts remain privately retained; this publication contains no host paths or environment-variable values.

## Reproduction prerequisites

The original captures used official Microsoft .NET SDK **8.0.425**, runtime **8.0.31**, Linux x64. Select the SDK executable through `DOTNET`, `--dotnet`, or PATH. `TOOL_PROVENANCE.json` records the official archive URL, Microsoft release-metadata URL, and verified SHA512.

Optional trace reproduction needs the official Microsoft-owned NuGet `dotnet-trace` **8.0.547301**. Select its command through `DOTNET_TRACE`, `--dotnet-trace`, or PATH. The supplied scripts never install or download software. Consult the [official NuGet package](https://www.nuget.org/packages/dotnet-trace/8.0.547301) and [Microsoft documentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace). The package is [MIT licensed](https://licenses.nuget.org/MIT). Its original package passed Microsoft-author and NuGet-repository signature verification.

No tests, builds, timing or other profiling should run concurrently with a diagnostic capture. The portable scripts are adaptations of the original reviewed local runners. The build/capture/decoder Python syntax and source paths were checked for publication, without replaying those scripts. Saved-text normalization was reapplied to the existing raw dumps. The optional C# decoder has not been compiled.

## Build and natural JIT capture

Run from this directory, using new output directories. `artifacts/` is ignored. These commands perform work only when explicitly invoked:

```sh
python3 build.py --out artifacts/build
python3 run_diagnostics.py jit --manifest artifacts/build/manifest.json --out artifacts/jit --reviewed
python3 analyze_jit.py --input artifacts/jit --out artifacts/jit-analysis
```

`build.py` performs three sequential Release builds, maximum 60 seconds each. It never invokes a workload target. `run_diagnostics.py jit` performs exactly three fresh-process validation-only targets, 65 lifecycle iterations each, maximum 60 seconds each. It captures all natural caller/Runtime listings with a diagnostic assembly filter. The actual callsite is inspected rather than guessing a compiler-generated method name. No EventPipe profiler is attached to these runs.

`--reviewed` is an operator review assertion, not permission to take additional actions. The runner verifies source/harness/binary hashes, removes inherited runtime switches, uses the original tiering-disabled configuration, and retains every stdout/stderr/launch/terminal receipt. It protects subprocess ownership during signal handling and kills/reaps an interrupted child process group. Do not force-kill its parent before allowing cleanup.

## Optional trace capture, in a suitable environment

The original environment could not create the required Unix-domain diagnostic socket. Do not claim this reproduction succeeded without fresh evidence, and do not weaken security controls or change environments to evade a denial.

```sh
python3 run_diagnostics.py trace --manifest artifacts/build/manifest.json --out artifacts/trace --reviewed
```

The optional stage is exactly three sequential fresh-process targets with 4096 requested plus 64 warmup lifecycles each. It uses official `cpu-sampling` plus explicit `Microsoft-Windows-DotNETRuntime:0x1:4`, a 64MB buffer, default rundown, a 60-second capture cap, and a 90-second process-group limit. Reaching the cap is conservatively incomplete. Failure ends the stage; no resampling, count changes, retries or replacement target is automatic.

Whole-process traces include registry/setup, warmup, the requested lifecycles and postvalidation. No phase markers exist, so loop-only pause attribution is unavailable. The legacy profile samples managed threads, not precise on-CPU time. Trace elapsed times are not formal comparisons.

The six workload targets have 450 seconds of subprocess-wait ceilings in total, plus 180 seconds for three builds. Optional offline build/analysis adds at most 240 seconds. When executing the whole proposal, use an overall bounded wrapper and permit signal cleanup. These are diagnostic bounds, not predicted durations or benchmark results.

## Optional offline decoder

Only if all three traces actually exist and the trace stage is complete:

```sh
python3 decode_traces.py --traces artifacts/trace --out artifacts/decoded --trace-tool-directory <official-package-tools/net6.0/any-directory>
```

The directory is the existing official package folder containing `dotnet-trace.dll`, `Microsoft.Diagnostics.Tracing.TraceEvent.dll` and its companion DLLs. The decoder uses those installed assemblies, checks the TraceEvent hash recorded in `TOOL_PROVENANCE.json`, and performs no additional package installation. `DOTNET` or `--dotnet` selects the SDK.

The decoder records actual SuspendEE/RestartEE event names, timestamps, payloads and counts. It pairs by process/CLR instance, validates temporal order, and preserves missing, duplicate, unknown, unmatched and lost-event gaps. Suspend-start to restart-end includes suspension coordination. An inner fully-suspended duration requires all inner boundaries. GC-related classification uses the observed reason; collection counts do not measure pause duration or establish causation.

The separate official `topN` report is saved as sampled-stack evidence. Missing samples/events mean unavailable or inconclusive evidence. No code-size, sample percentage, GC-count or elapsed-trace observation alone proves causality or a speedup.
