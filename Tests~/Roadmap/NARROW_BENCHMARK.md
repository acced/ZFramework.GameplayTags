# Single factory-narrowing repair experiment

This fixed follow-on experiment preserves the adverse first-screen data. It does not replace that screen, subtract component timings, or assume allocation caused its lifecycle latency result.

## Source identities

- B1: exact PR #4 `dac77e7e7d5bad1ba7e77ed9fca7824d50187a72`, in Baselines/B1.
- B11First: complete first-screen Runtime in Baselines/B11First. All seven C# files match the original full-final/source.before.sha256 Runtime entries. The original changed pair is also preserved in the sibling first-screen archive.
- B11Narrow: current Runtime. Only the static fresh IntersectionExact factory differs from First: overlapping nonempty input ranges reserve min(lhs.Count, rhs.Count), while null/empty/separated ranges start without a backing buffer. The constructor, append, aliases, reused-target growth, comparison-work dispatch, public exact lookup, and enumerator are unchanged from First.

The original Benchmark.cs, Benchmark.csproj, scripts, focused-final and full-final raw datasets are preserved unchanged. BenchmarkNarrow.cs is a separate derived harness, and BenchmarkNarrow.csproj compiles only it.

## Fixed matrix and protocol

Exactly 44 cells: n=0, 8, 64, 1024, each with:

1. Existing whole lifecycle, new union, new half-overlap exact intersection, and reused half-overlap target intersection (4 cells).
2. Fresh exact-intersection factories with 0, 1, 2, and full matches (4 cells). At n=0 all four geometries are empty. Nonempty 0-match inputs have strictly separated ranges; sparse 1/2-match inputs have overlapping ranges and late disjoint members.
3. Existing reused sparse intersections with 0, 1, and 2 matches (3 cells).

Calibration runs once against B1 only, targeting 200 ms, with the unchanged finite iteration cap. The resulting count for each cell is shared across all three sources and all four rounds. Four fixed orders:

- B1, First, Narrow
- Narrow, First, B1
- First, B1, Narrow
- Narrow, B1, First

Every pair appears in each precedence twice. Absolute source positions are not balanced: Narrow never runs in the middle position. There are no adaptive repeats or favorable resampling. Build-only precedes the one finite run, which is subject to a 600-second outer timeout.

Whole public operations and their observers remain inside timing and allocation measurement. Warmup, preparation, result/source verification after every batch, and array-payload census remain outside. Timing/allocation/GC share each measured batch. The short flag uses the actual 200 ms target; capped counts, raw durations, failed assertions, and source changes remain visible. No forced GC.

## Interpretation boundaries

Restoring fresh-factory reservation can lower transient allocation for dense outputs while increasing retained capacity for overlapping sparse outputs. It does not alter growth for reused targets. Preserve both outcomes. Full lifecycle has the same retained-container proxy caveat as the first screen: its census samples a newly built representative input, not the measured output tuple.

This experiment does not remeasure all 151 original public cells and does not turn earlier First results into current Narrow measurements. It makes no Unity/Mono/IL2CPP performance claim. The first-screen 51.2% adverse lifecycle median remains in its dataset; byte accounting alone does not establish its cause.

Run with a .NET 8 SDK:

- `bash Tests~/Roadmap/narrow-b11.sh --build-only`
- `bash Tests~/Roadmap/narrow-b11.sh`

The execution wrapper additionally refuses an existing output directory, records wall time, and enforces `timeout 600s`.
