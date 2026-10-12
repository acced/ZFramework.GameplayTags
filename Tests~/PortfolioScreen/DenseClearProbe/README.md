# Bounded isolated Clear and first-use probe

This is a cheap rejection probe for the unchanged dense Clear wrapper candidate.
Original `f7c99546dbf029d70896c0f05e5119b19ffd6d26` remains the comparator. The exact
candidate patch/source hashes match the wrapper screen; only harness and driver
preparation is new. It cannot qualify an optimization or establish Unity results.
The existing refill/Clear screen and all prior evidence remain separate.

## Nine warm cohort rows

The fixed root-only fixtures are retained empty, tiny8/mask0, hashed9, fresh63,
fresh64, fresh1024, retained sparse16, and density127/128 with active1024. Sparse
fixtures reserve8192, grow1024 and remove down; no Copy compacts their active
mask. Config fixes all reserve/grow/keep counts, actual masks and array lengths.

Each row owns a fixed128-container cohort, used in the same order throughout.
After four warmup passes, each interval refills the entire cohort through public
AddTag and verifies its live counts outside timing. The timed interval contains
only the sequential loop of public Clear calls and the surrounding timestamps.
Empty/count observations, allocation counters, GC counters, reflection, array
identity checks and full-state hashes are outside the Clear interval. The full
block wall clock also includes refill and per-interval checks/counters, allowing
a bounded work estimate. No measurement subtracts loop or timestamp overhead.

This is an immediately refilled, warmed-method cohort. It is neither universal
cache-cold execution nor repeatedly clearing one hot instance. Sparse cohort
backing arrays occupy28MiB before headers; active working data may be much
smaller. Actual cache residency is not asserted. All objects and interval-result
arrays are prepared outside measured intervals.

One original-only pilot contains eight intervals per row and records both
summed Clear milliseconds and full-block wall milliseconds. The shared count is:

    max(1, min(4096, ceil(8 * 200 / ClearMs), floor(8 * 1000 / WallMs)))

All three limiting counts and the chosen count are persisted before candidate
warm timing. The resulting TSV hash is checked before/after processes and at
finalization. There is no candidate calibration, second pilot, count adjustment,
row removal or adaptive rerun. The one-second estimate is a forecast, not a
runtime guarantee. Four process pairs use AB, BA, AB, BA; their measured blocks
are forecast to take at most72seconds per backend, excluding fixed setup, builds,
state validation, pilot and cold process startup. The complete driver has one
600-second kill/reap deadline covering every stage, without extension.

A pilot is insufficient when either duration is nonpositive, any interval fails
to exceed the timestamp-pair median, or its total ticks are below twice eight
times max(1,timestamp-pair median). Such a pilot is retained, the probe stays
incomplete, and no candidate warm timing occurs. No replacement pilot is run.
All interval ticks, minimum/total ticks, timer overhead and wall duration remain
visible. Short (<200ms), capped and timer-dominated rows remain explicitly
flagged. Timer domination means the median interval is at most ten times the
observed timestamp-pair median (floored at one tick). Those rows are inconclusive
for small changes; neither an aggregate win nor the absence of a coarse flag is
acceptance evidence. Repeatable material adversity still matters.

## Eight first-use routes

Cold routes use the same fixtures except fresh63. Each route receives three
fresh processes per source/backend,48processes per backend total. The fixed
three-pair order is AB, BA, AB, which necessarily gives the original first turn
in two pairs. This order imbalance is disclosed and not repaired by resampling.

Each process performs identical normal registry/container setup, verifies the
prepared state, warms only the observation/timestamp machinery, then measures
its first public Clear call. No prior Clear, Copy, target-method preparation or
target warmup occurs. Observer/reflection/hash work is outside the allocation
and timing brackets. Caller JIT may occur before the first timestamp. Process
launch, all prior runtime initialization, native JIT allocation and universal
runtime-cold behavior are not measured. Three noisy first-call samples do not
establish reliable cold-latency wins, but are retained for repeatable large
adverse patterns. The appended helper's native/JIT footprint is not measured by
owned-array equality.

Before-state, expected-after-state, actual-after-state and capacity hashes are
recorded. Expected Clear state resets both counts and only active buckets;
entries, explicit-index tails, inactive bucket suffix, array identities and
capacity are preserved. Before and after hashes are not expected to match;
actual poststate must match the expected transformation and both sources.

Zero managed allocation is expected. Every observed nonnegative allocation and
GC count is preserved. Candidate-positive allocation differences are adverse;
equal nonzero runtime overhead is reported separately, not erased or treated as
a parser failure. GC-confounded observations remain flagged. Native allocations
are outside this metric. After retaining all full-mode cold samples and their
summary, a missing/incomplete fixture group or any candidate-positive allocation
difference stops the probe before its warm pilot and comparisons. Explicit stages
identify cold incompleteness or allocation adversity. The one-route preflight
remains exempt from the full-group completeness requirement and retains its byte
observations.

## Minimal preflight and tool reuse

The driver is a direct derivative of the existing compact DenseClear runner and
reuses `Tests~/PortfolioScreen/Hierarchical/build_support.py`. No benchmark
adapter or runtime change is introduced. SDK8.0.425, CLR/reference8.0.31 and CI
Mono6.8.0.105 remain pinned. Preflight mode permits and reports other local Mono
versions.

After separate authorization, the minimal local route is:

    python3 PATH_TO_PACKAGE/run.py --git-root . --backend net \
      --dotnet "$DOTNET" --out "$PUBLIC" --work "$PRIVATE" \
      --preflight-only --deadline-seconds 300

It builds the two probe executables, checks all nine cohort fixtures without
calling measurement routes, then runs exactly one fresh9-hashed first-Clear
sample per source to verify the cold route and result format. It performs no
pilot, warm timing or full cold matrix. For local Mono select the mono backend
and supply its executable and the existing NuGet reference package directory.
Full probing is reserved for separately authorized CI execution.

## Output boundary

The nine JSON paths in `upload-allowlist.txt` are the complete public output.
They contain fixed configuration, numeric observations, state/source hashes,
sanitary stage status and explicit limitations. `AcceptanceEvidence` stays false.
Only upload those exact paths from the public directory. Keep command lines,
absolute paths, raw logs, compiler responses, environment dumps, generated Runtime
copies, assemblies, execution manifests and prior private evidence in the
separate private work directory. Never upload that directory recursively.
