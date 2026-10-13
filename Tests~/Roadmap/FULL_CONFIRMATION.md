# Final narrowed-source full confirmation

This is a separate, predeclared one-time confirmation after the 44-cell factory repair experiment. It does not overwrite or replace the adverse first-screen results.

- Harness: unchanged original Benchmark.cs and Benchmark.csproj.
- Versions: exact B0, exact B1, and the tested current B11Narrow Runtime.
- Workload: original 151 full-public-operation cells, including lifecycle, legal B0 compositions, allocation, GC, raw duration, short/capped flags, post-batch state verification, and retained payload.
- Counts: byte-identical to results/full-final/iterations.csv, originally calibrated on B0 to a 50 ms target. SHA-256: `85ef89dff332434c9d2822405e60212a88f313853380b3428a63ba543e4c9d99`. No recalibration or pilot.
- Fixed orders: B0/B1/Narrow; Narrow/B1/B0; B1/B0/Narrow. Absolute positions remain not fully balanced.
- Dedicated launcher: confirm-b11.sh. It validates original B0/B1/harness source hashes and the current Runtime against the completed narrow-final source inventory before building. It copies the original map into the separate output and verifies that map again after measurement.
- Separate output: results/full-confirmation. The finite wrapper refuses an existing output directory and enforces `timeout 600s`. No favorable resampling or performance threshold.

The 50 ms short flag remains the original target, including for faster variants. No forced GC. Retained payload is incomplete; the full-lifecycle retained container is a representative-input proxy. Unity/Mono/IL2CPP remain unmeasured. A later result does not prove a cause for the original +51.2% lifecycle median.
