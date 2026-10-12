# Dense Clear: compact rejection screen

This package prepares the sole bounded repair after the first dense Clear screen
was rejected. See [FIRST_SCREEN.md](FIRST_SCREEN.md) for both preserved backend
results and artifact identities. It is not an accepted optimization, qualified
version, or Unity performance evidence. No shared Runtime change is included.

## Candidate and strongest existing comparator

The comparator remains exact original public Clear and Runtime from
`f7c99546dbf029d70896c0f05e5119b19ffd6d26`, with the same AddTag restore sequence and
measurement boundary. The complete source maps differ only in TagStorage.cs.

This one repair makes Clear a small wrapper: call bucket work only when mask and
Count are nonzero, then reset both Count and ExplicitCount unconditionally. Thus
a zero total Count with a nonzero ExplicitCount still resets both, as before.
Private `ClearBuckets`, appended after the old methods, is marked NoInlining and
retains the wide density rule plus original occupied-slot fallback. An explicit
empty public constructor occupies the old synthesized constructor's position
before the appended helper. Fields and old method order are unchanged; emitted
constructor initialization, flags and position must be checked after compilation.

Native inspection of the first candidate did not demonstrate lost inlining: both
original and candidate had the same public Clear callsite expansion and called
TagStorage.Clear out of line. The candidate's body and saved-register frame were
larger. This motivates isolating bucket work, but does not prove that difference
caused all observed timing cost. The helper adds a method and a call for nonempty
indexed storage; generated footprint and inlining remain backend-dependent. No
performance improvement is claimed before the unchanged screen evaluates it.

## Twelve fixed rows

All tags are independent roots, making total and explicit live counts identical.
`config.json` fixes every row, reserve/grow/keep count, actual expected active mask,
array capacity and pilot count before any target execution:

- Empty linear storage: mask 0
- Empty retained index: 8,192 reserved, 1,024 grown/drained, mask 16,383
- Tiny 8: mask 0
- Fresh 9: mask 31
- Fresh 63/64/65: masks 127/127/255
- Fresh 1,024: mask 2,047
- Density boundary 127/128/129: reserve/grow 512, mask 1,023
- Retained sparse 16: reserve 8,192, grow 1,024, remove down, mask 16,383

The ten nonempty rows measure complete AddTag refill, cardinality observation,
and public Clear cycles. They are not isolated Clear timings. The two empty rows
measure public Clear plus cardinality observation, with no AddTag calls. Refill
and observation cost can dilute a Clear regression; no cycle win establishes
pure Clear performance. Pure Clear and the complete final semantic/backend gates
remain necessary for qualification.

No timing fixture uses Copy. Each is prepared by reserve/grow/remove; actual
Count, membership, active mask, array identities and capacities are checked
outside timing. Warmup and final checks prove the intended index remains active.
Full regrowth and reverse drain verify continuation. Complete entries/explicit
arrays, zero active buckets and unchanged inactive suffix are checked around an
untimed Clear. A separate untimed Copy fixture produces a genuinely nonzero
retained inactive suffix and checks Clear preservation and later safe growth; it
is not used to restore any timing row or as a sparse timing result.

## Small correctness gate and arithmetic limits

`Harness/Correctness.cs` is the unchanged 25-case threshold/retention suite. It
checks fresh and truly sparse live counts 8/9/15/16/17/31/32/33/63/64/65 and density
127/128/129 against active 1,024, with three Clear/full-regrowth/reverse-drain
passes each. Before and after must produce exactly equal structural rows.
The original min64-cached structure and the wide-product first screen passed
standalone semantic checks. This wrapper repair retains the same small correctness
gate; it does not copy the full main suite or substitute for its final rerun.

Untimed scalar checks cover the minimum/density edges and positive 32-bit Counts
through `int.MaxValue`, including the old 32-bit product overflow boundaries.
The candidate casts Count to long before multiplying by eight, making the density
policy mathematically consistent and avoiding an added multiplication overflow
under checked compilation. Even `int.MaxValue * 8L` fits in a signed 64-bit value.
Mask plus one is representable for a coherent active array prefix. The scalar
checks do not allocate fabricated huge storage, establish reachability of giant
states, or claim full checked-build testing. The prior narrow and wide candidate snapshots are preserved separately. The
wrapper and appended helper are the sole bounded repair, with no new timing rows.

## Frozen process protocol

Reuse `Tests~/PortfolioScreen/Hierarchical/build_support.py` for direct-csc builds.
The SDK is 8.0.425 with CLR/reference pack 8.0.31; CI Mono timing requires
6.8.0.105 and the existing PortfolioScreen reference packages. Preflight-only mode
permits other local standalone Mono versions and records the actual version.

One baseline-only pilot process measures each row once after 1,024 warmup cycles.
For each backend the shared count is:

    max(seed, ceil(seed * 200 / baseline_pilot_batch_ms))

Raw pilot rows and the immutable count map are saved before candidate timing.
Both variants use this same map in four process pairs, ordered AB, BA, AB, BA.
Each process executes all twelve rows in fixed order. There is no candidate
calibration, second pilot, row removal, retry, resampling or extension. Short
batches, raw zero/nonzero GC counts and every observed ratio remain disclosed.
Nonzero warm allocation, a wrong checksum, changed geometry or changed source,
compiler/reference/binary/map identity invalidates the run.

The fixed driver deadline begins at entry, including source materialization,
toolchain setup, four builds, correctness, fixture preflight, pilot and pairs.
Every child has only the remaining budget. Failure or timeout kills/reaps its
process group and retains partial results. This small screen does not qualify
anything, even if every row improves.

## Invocation

Use new, separate, non-nested public and private result directories. After explicit
preflight authorization, compile and run threshold correctness plus untimed row
checks, without pilot or timing:

    python3 PATH_TO_THIS_PACKAGE/run.py --git-root . --backend net \
      --dotnet "$DOTNET" --out "$PUBLIC" --work "$PRIVATE" \
      --preflight-only --deadline-seconds 300

For Mono add `--backend mono --mono "$MONO" --nuget "$PACKAGES"` instead of the net
backend option. After separate CI timing authorization, omit `--preflight-only`
and use the default 600-second cap. There is no local timing authorization in the
preparation task.

For a project build against a separately materialized pinned Runtime, use
`Screen.csproj -p:RuntimeRoot=...` and `-p:Harness=Screen` or `Correctness`.
The bounded driver remains the reproducible evidence entry point.

## Public evidence boundary

Upload only the eight JSON paths in `upload-allowlist.txt`, relative to `--out`.
These contain sanitized status, fixed configuration, numeric results and
repository-relative source hashes. The complete output folder is checked against
this allowlist. `AcceptanceEvidence` remains false.

Never upload `--work`, command lines, absolute tool/workspace paths, environment
dumps, compiler response files, compiled binaries, generated Runtime copies, raw
stdout/stderr, private prior evidence or failure traces. Failure status exposes
only a fixed stage. Do not recursively upload logs/build/work directories.
