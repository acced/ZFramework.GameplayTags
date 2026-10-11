# Reusable Union API supplement

These independent files leave the existing Evolution matrix unchanged. Freeze
all four package files before comparing f7c99546, the previous accepted version,
and the reusable-output candidate.

Every build has identical fixtures, public-output assertions, row IDs, iteration
counts and checksums. Baseline builds implement replacement through the already
available Union(lhs,rhs) factory followed by Copy(output,fresh). Only the
candidate is compiled with REUSABLE_UNION, selecting the new overload. Selection
is compile-time; no reflection or API dispatch is added inside the timed loop.
This is a comparison of two ways to perform observable output replacement, not
a claim that the factory-plus-Copy baseline is an isolated factory measurement.

The 28 rows contain 25 timed cases and three retained-result cohorts:
- n4/128/1024, overlap0/50/100: reused independent output
- n4/128/1024 overlap50: new output on every call, plus 64-object retained cohort
- n128 overlap50: restore + left/right aliased replacement pairs
- n128 overlap50: counted left, right and both inputs
- depth128/1024/3000, ascending/descending insertion: all-explicit chain added to
  a disjoint 3001-node seed, using reused output
- empty replacement and aliased self replacement

Shallow names use the same fixed coprime ordering as Evolution. Deep counts are
checked by names, ordinal string-prefix closure, and full normalized drain.
Setup, registration, reference models, correctness checks and delegate creation
are outside the timer. Results escape and contribute to a matched checksum.
Cold-output means cold storage, with warmed code and pool, not process startup.
Retained cohorts hold only the final output; obsolete factory results are not
retained. Measurements compile the runtime and harness together consistently on
all variants, separately from Evolution's standalone .NET runtime assembly.

Use the parent-granted timing window only. Example:

    source /tmp/gameplaytags-env.sh
    python3 run.py --dotnet /tmp/gameplaytags-dotnet/dotnet \
      --variant f7=/absolute/f7-snapshot \
      --variant previous=/absolute/previous-snapshot \
      --variant candidate=/absolute/reusable-union-snapshot \
      --revision f7=f7c99546dbf029d70896c0f05e5119b19ffd6d26 \
      --revision previous="$PREVIOUS_REVISION" \
      --revision candidate="$CANDIDATE_REVISION" \
      --api-variant candidate --out /absolute/new-output --samples 14

For standalone Mono append --mono /tmp/gameplaytags-mono/bin/mono. For correctness
only append --test-only. The existing net472 reference assemblies and dependency
cache are used; the runner installs no SDK. Always run each backend separately.

A baseline-only pilot creates the shared iteration map. Every measured process
uses that same immutable map, alternating forward/reverse variant order. Very
large deep speedups can make candidate batches short: review flags below50ms
require a new paired run with the complete reviewed --iterations-file override.
The map must contain all25 timed rows exactly once. Cold allocation, retained
memory, wall/CPU time and GC counts are reported separately. Conservative 99%
paired intervals and >3% flags support review, never blanket acceptance. This
supplement does not replace the full existing guard matrix or prove Unity or
IL2CPP behavior.

## CI provenance and failure retention

Every --variant requires exactly one --revision LABEL=40hex. Abbreviations,
missing labels, duplicates and unknown labels are rejected. These revisions are
caller-declared commit identities; the runner does not resolve remote commits or
prove that an archive belongs to a commit. Full source SHA-256s are authoritative
byte evidence, so CI must supply honest exact-revision snapshots and labels.

Before the first child process, preliminary-environment.json and environment.json
record the declared revisions, exact source/package hashes, CPU, affinity and
settings. SDK --info, host version and installed .NET runtime inventory are then
recorded. The .NET host --version is an SDK version, not an observed CLR version.
Unchanged correctness-only mode does not emit the loaded CLR version; binary
runtimeconfig/dependency files and installed runtimes remain recorded. Actual
Runtime/Framework fields from measured reports are recorded when timing is run.

Every command gets separate, untruncated raw byte stdout/stderr files under
command-logs, with its full argument list, timestamps and exit status in the
manifest. These files are streamed during execution and retained on failures;
failure.txt records caught runner exceptions. Existing successful build/test,
pilot.json and per-round raw report files retain their original formats.

After every successful build, all files in its bin tree are hashed, including
DLL/EXE dependencies, apphost, PDB, deps.json and runtimeconfig.json. At exit the
runner re-enumerates source and completed-build trees, detects added/deleted as
well as modified files, and rejects evidence on any hash mismatch. The final
manifest is also written on caught failures. Abrupt process/host termination
can prevent final validation; preliminary manifests and bytes already streamed
remain available, but an incomplete run is not validated evidence.

This hardening does not alter fixtures, rows, comparison formulas, API selection,
baseline-only calibration, shared iteration counts, or default fourteen-round
forward/reverse ordering. No SDK installation, new timing sweep or shared
workflow change is performed by the package.
