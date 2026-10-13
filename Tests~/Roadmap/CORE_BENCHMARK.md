# Final conservative-core comparison

The production scope restores B1 intersection/difference policies and retains the reviewed batching, append, alias-union, copy, and constructor changes. Earlier First/Narrow experiments remain preserved; their timing and sparse-capacity results are not relabeled as core results.

## One fixed comparison

77 cells × exact B1 / final B11Core × four fixed balanced pair orders = 616 samples. Orders: B1/Core; Core/B1; Core/B1; B1/Core. One finite run, external 600-second timeout, no pilot, recalibration, favorable resampling, or further policy variants.

25 cells use the original full-final iteration map and 50 ms short target:

- n=0,1,8,64,1024: new union, new exact intersection, target copy, target exact intersection, and complete lifecycle.

52 cells use the original focused-final iteration map and 100 ms short target:

- All original focused rows except the three focused-copy rows. This includes capacity construction, five append shapes, three alias-union shapes, and sparse reused intersections at n=0,8,1024, plus 16 skew intersection/difference controls at n=1024.

Every row records count_source and count_target_ms. The launcher verifies both original map hashes, writes count-provenance.csv, pins exact B1 and all seven approved core source hashes, and rechecks the maps and source after the run. BenchmarkCore.cs rejects calibration mode. Historical harnesses and raw datasets remain unchanged.

## Coverage gaps

There are no original direct append/alias/capacity-only counts for n=1 or n=64. Those sizes use the existing full copy/union/intersection/lifecycle workloads; a nonalias union does not cover an alias. Difference-specific controls are present only at n=1024. No missing-cell counts are invented. This is not a repeat of the entire 151-cell full matrix and does not newly measure lookup, enumeration, serialization, or registry rebuild in isolation.

The same complete public operations, restoration, and observers stay in measured batches. Source/result validation and preparation remain outside. Allocation/GC share timing intervals. No forced GC. Retained array payload excludes headers, strings, whole heap, and peak transient memory; lifecycle retention is an input-container proxy. No Unity/Mono/IL2CPP performance claim or noisy performance gate.

## Reproduction

Use core-b11.sh with an installed .NET 8 SDK. It expects the preserved original count maps and source inventory at results/full-final and results/focused-final; published evidence may be copied back to those paths before replay. The execution wrapper additionally refuses an existing output directory and invokes timeout 600s.
