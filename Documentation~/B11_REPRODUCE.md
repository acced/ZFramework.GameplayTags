# Reproducing B1.1 checks and measurements

## Correctness

Use an installed .NET 8 SDK (the recorded SDK is 8.0.425):

```sh
bash Tests~/Roadmap/run.sh
```

The default B0/B1/current variants compare a deterministic public-behavior transcript and compile for .NET Standard 2.1/C#9. This is portable validation with Unity stubs, not an actual Unity Editor/Player test.

## Final core comparison

The committed `Evidence` directories preserve raw results, shared count maps, source inventories and runtime context. The executed launchers expect historical inputs in local ignored `results` directories. In a clean checkout, prepare those inputs without replacing an existing local result:

```sh
mkdir -p Tests~/Roadmap/results/full-final Tests~/Roadmap/results/focused-final
cp -n Tests~/Roadmap/Evidence/full-final/iterations.csv Tests~/Roadmap/results/full-final/
cp -n Tests~/Roadmap/Evidence/full-final/source.before.sha256 Tests~/Roadmap/results/full-final/
cp -n Tests~/Roadmap/Evidence/focused-final/iterations.csv Tests~/Roadmap/results/focused-final/
timeout 600s bash Tests~/Roadmap/core-b11.sh
```

Set `DOTNET` if the SDK executable is not on PATH. Use a fresh `BENCHMARK_OUT` directory when repeating a measurement. The core launcher checks the exact historical count-map hashes and final production source hashes, refuses calibration, and measures 77 cells in four balanced B1/core pairs. It uses historical counts calibrated for 50 or 100 ms, not adaptive counts for the new run. Short/capped intervals, allocation and GC remain observations. Run without competing builds or benchmarks.

## Historical experiments

- `Baselines/B0`: original string-list API, with explicitly labelled adapters for later APIs.
- `Baselines/B1`: exact PR #4 initial B1 source.
- `Baselines/B11First`: first B1.1 candidate with sparse/skew intersection/difference policy.
- `Baselines/B11Narrow`: that experimental policy after narrowing fresh factory allocation.
- `Runtime`: final conservative core. Its intersection/difference policies are restored to B1.

Historical source inventories use `Runtime/` for the candidate tested at that time. Match that path to the appropriate preserved baseline above; do not compare it to final core and infer an identity failure. To replay an experimental launcher, use a separate disposable checkout and replace only its Runtime `.cs` files from the relevant baseline. Never overwrite a working checkout. The first focused/full launchers calibrate as originally recorded; `confirm-b11.sh` reuses historical full counts and additionally needs the narrow source inventory in `results/narrow-final`.

Production results are in `B11_FINAL_RESULTS.md`. The first, narrow and narrow-confirmation reports retain the rejected policy tradeoffs. They are separate datasets and are not pooled or relabelled as final-core timing.

Private build logs, workspace paths and command transcripts are excluded. Recorded CSV, source inventories and sanitized environment outputs are copied verbatim. Packaging changes to documentation do not rewrite the historical measured source inventories.
