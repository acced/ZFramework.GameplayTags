# B1.1 dispatch attribution screen

This isolated benchmark compiles the preserved B1 Runtime under `B1Source/`, copied exactly from `../Roadmap/Baselines/B1/Runtime/`. `source-sha256.txt` records inputs. Production Runtime is not edited by this experiment.

## Frozen plan (2026-10-13)

- Actual `GameplayTag`, `GameplayTagContainer`, and B1 ordinal merge implementations.
- Intersection: actual merge, smaller-side ordinal binary search preserving lhs values, and the same binary search with a separated-range check.
- Difference: actual merge, per-source ordinal binary membership, and copy-then-descending-find/remove (no copy when in place).
- Include reusable target writes. In-place difference includes equal clear/AddRange input-reset work for all algorithms, so results describe reset+operation rather than isolated mutation.
- Size pairs:1024/1024,4/1024,4/100000. Each has separated disjoint, interleaved disjoint,25% overlap, and100% smaller-set overlap.
- Each unbalanced pair is tested in both directions. Total12 size/pattern pairs,20 directed cells,60 operation groups,180 algorithm cells,720 samples.
- Four alternating sample pairs. The baseline is calibrated to approximately25ms per sample,100ms total. Every contender has its own >=2ms doubling pilot. Contenders use the baseline call count or a smaller count capped to an estimated30ms per sample. Baseline calibration uses an >=8ms doubling pilot. All counts and durations retained. Maximum4,194,304 calls/sample; internal180-second entry check and external `timeout --kill-after=5s 180s` wall-time limit (the internal check alone is not a hard cap). This is a short, finite attribution screen, not a precise crossover estimator.
- Ordered full result contents, uniqueness and lhs Name-object representative identity checked before timing and after every sample; per-operation count/last-name sink prevents elimination. Validation occurs outside timers.
- Every raw numeric row is flushed before post-sample validation; failures print FAIL and retain all completed samples.
- Bytes allocated on the current thread and all GC generation counts measured with each sample. Inputs, expected results and preallocated outputs outside timers. Dataset setup/allocation outside timers.
- Tiered compilation disabled; no concurrent project benchmarks/tests during the approved timing window.
- .NET8 Linux measurements are evidence for this host, not Unity Mono/IL2CPP certification.

## Run

Build with an installed .NET 8 SDK: `dotnet build Tests~/B11Dispatch/B11Dispatch.csproj -c Release`. Then run `timeout --kill-after=5s 180s env DOTNET_TieredCompilation=0 dotnet Tests~/B11Dispatch/bin/Release/net8.0/B11Dispatch.dll Tests~/B11Dispatch/results > Tests~/B11Dispatch/results.log`. Use the installed SDK executable in place of dotnet when needed.

Raw and aggregate CSV include absolute timing, ratios, calls, allocation and GC. Do not infer a universal integer imbalance threshold from this screen alone.

This is kernel attribution with candidate-specific pilot caps, not an equal-count public-API comparison. Separate `--validate` mode validates the complete frozen grid without timing. Each result gets a deterministic names SHA256 in validation.csv; the ledger SHA256 is printed. No production threshold is justified without integrated public-API evidence.

Packaging note: the historical harness-sha256.txt records the README used for the run. This README subsequently replaces a machine-local SDK command with portable instructions; Program/project/source measurement bytes are unchanged.
