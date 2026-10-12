# Callback cursor: compact portable rejection screen

The current source is one bounded repair following the rejected first CI screen;
see [FIRST_SCREEN.md](FIRST_SCREEN.md) for every retained adverse row and artifact
identity. The repair has passed standalone correctness but has not been timed.
It is not an accepted optimization or Unity result. No shared Runtime change is
part of this package.

## Exact sources

Baseline is `f7c99546dbf029d70896c0f05e5119b19ffd6d26`. The driver materializes its
Runtime directory from Git, then creates the candidate by replacing only
`Runtime/GameplayTagCountContainer.cs`. `source-pins.json` contains both complete
repository-relative source hash maps. `candidate.patch` is the reviewed delta.
The original QueueChange signature/body and original method order are retained;
two helpers are appended and cursor locals follow no-listener early returns.

The sole repair selects the original full notification loop when
`hierarchy.Length <= 1 || m_CallbackCount <= 1`, after the existing no-listener
return. Otherwise it uses the same cursor loop. There is no per-node gate.
Explicit-count mutation, total-count mutation, QueueChange order and returns
remain unchanged. This intentionally gives up possible exhausted-table gains;
the added admission branches and larger methods may still cost time. All twelve
rows, including every prior adverse control, retain the original protocol.

Independent static review cleared this exact fallback. Baseline and repaired
candidate each passed 7,599,843 assertions on .NET 8.0.31 and standalone Mono
6.12.0.199 in one bounded four-check correctness run. No timeout or failure
occurred. Those private logs/manifests are not copied into this package, and the
local Mono result does not replace the CI Mono 6.8.0.105 screen.

`Harness/Correctness.cs` combines the existing main correctness suite and its
fixture registration with one focused cursor group. It checks sorted lookup,
explicit reflection signatures, global-only/sparse listeners, exact event order,
bulk resets, listener edits, reentrancy, exception recovery and cleared pending
references. It uses the ordinary lookup on baseline and the cursor overload on
candidate. Normalized output must match with 7,599,843 successful assertions.
No private local result or command/environment manifest is included.

## Twelve fixed rows

Every process prepares one registry and shares immutable tag fixtures outside
timing. Callback arrays contain 8,192 entries for the three large-table guards.
The fixture explicitly checks actual table sizes, increasing hierarchy IDs,
first-search midpoint index 4,095, a missing tag beyond all callback IDs, and
four actual groups of 2,048 unrelated callback IDs between successive ancestors
and after the leaf. Thus the sparse row cannot silently become early exhaustion.
The bulk source has eight disjoint depth-four leaves in descending runtime-ID
order. Setup asserts all rows' expected event counts, checksums and empty final
state. The Clear row measures an AddTag/Clear cycle, not isolated Clear.

Rows and pilot seed counts are frozen in `config.json`. One baseline-only pilot
process measures all twelve rows once, following 1,024 warmup operation pairs per
row. For each backend the shared count is:

    max(seed, ceil(seed * 200 / pilot_batch_ms))

Raw pilot and resulting map are written before any candidate timing. The shared
map hash is checked before/after target calls, after the final timing child, and
again during and after public-result finalization. Published map identity remains
the original frozen hash; any mismatch marks the run incomplete. Both
variants use that map in four process pairs, ordered AB, BA, AB, BA. Each process
emits all twelve rows in fixed order. There is no candidate calibration, pilot
repeat, row removal or rerun. Short comparison batches remain disclosed; they
do not change counts. Every measured pair must have the expected checksum/event
count and zero warm allocation. GC counts are retained in each raw row.

## Invocation

Use separate new, non-nested public/private output directories. The existing
PortfolioScreen direct-csc helper supplies pinned SDK 8.0.425 and CLR/reference
pack 8.0.31. CI Mono timings require 6.8.0.105 and the packages in the existing
`Tests~/PortfolioScreen/References.csproj`. The shared workflow owns installation,
backend job selection and upload. This package does not modify that workflow.

Compile and run the full correctness suite and twelve untimed fixture checks:

    python3 Tests~/PortfolioScreen/Callbacks/run.py --git-root . \
      --backend net --dotnet "$DOTNET" --out "$PUBLIC" --work "$PRIVATE" \
      --preflight-only --deadline-seconds 300

After separate timing authorization, omit `--preflight-only` and use the default
600-second cap. For Mono add `--backend mono --mono "$MONO" --nuget "$PACKAGES"`.
Preflight-only mode reports the actual Mono version and permits local portable
validation; CI pilot/timing mode requires the pinned CI Mono version.

The deadline starts at driver main entry, including archive materialization and
toolchain setup. Prior CI installation occurs before the driver is launched. The
cap covers all setup, four builds, correctness, preflight, pilot and comparison
processes. Timeout/failure kills and reaps the owned process group. Partial raw
results remain; no timeout extension or automatic retry is performed.

For an ordinary .NET build against an already materialized pinned Runtime, the
project command is:

    dotnet build Tests~/PortfolioScreen/Callbacks/Screen.csproj -c Release \
      -p:RuntimeRoot="$PINNED_RUNTIME" -p:Harness=Screen -o "$BUILD"

Use `-p:Harness=Correctness` for that executable. The bounded driver is the
reproducible evidence entry point and records the exact private compiler inputs.
It directly reuses `Hierarchical/build_support.py`; no new build framework.

## Public evidence boundary

Upload only the eight JSON paths in `upload-allowlist.txt`, relative to `--out`.
They contain fixed configuration, sanitized status, numeric results and
repository-relative source hashes. The output folder is verified against that
allowlist. `AcceptanceEvidence` is always false. Public provenance includes
numeric compiler/runtime versions but no actual tool locations, machine metadata,
raw commands, environment dump or private work path.

Never upload `--work`, compiler response files, compiled binaries, generated
Runtime copies, raw stdout/stderr, failure traces or execution-input manifests.
Do not recursively upload work/build/log directories. Public failure status
identifies only a fixed stage; details remain in private logs. A complete screen
can still show regressions or inconclusive short/noisy batches. No successful
optimization version follows from this rejection screen alone.
