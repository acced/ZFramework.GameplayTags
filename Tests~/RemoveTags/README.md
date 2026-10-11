# Frozen RemoveTags API supplement

This independent package measures only the changed RemoveTags API and its stated restore pairs. It does not alter the existing Evolution matrix. The current CI compares original f7, accepted version 1 f53, and the candidate selected by the exact branch head. Native shrink alone is not a verified performance contribution.

## Exactly twenty predeclared rows

- Empty self and distinct-empty repeated RemoveTags calls (2).
- n1/4/16/1024: Copy(seed→work) + RemoveTags for self, equal-content distinct ordinary source, and disjoint ordinary source (12).
- n4/n1024: the same restore pair with an equal-content counted source, value-type proxy, or reference-type proxy (6).

The eighteen nonempty rows measure the entire Copy-restore + RemoveTags + observation pair. They are not isolated RemoveTags costs. Empty rows contain no Copy. Counted sources hold contribution 3 per explicit leaf; proxies wrap a distinct ordinary equal-content source. Proxy dispatch is statically typed at the call site, with no timed type switch or boxing of the value proxy.

The fixed registration contains 2,048 three-level leaves named Flat.G(group).L(index). Left insertion uses the established coprime719 ordering; equal and disjoint sources reverse their insertion order. Disjoint explicit leaves share some implicit ancestors, so correct explicit removal is required. Setup, name-based reference models, delegates and validation stay outside timers.

Every row has semantic preflight and postflight: exact explicit names, ordinal-prefix ancestor closure, counts, unchanged seed/source, counted contributions and normalized draining of a cloned result. Work escapes through a static sink and a checksum that includes an increment even for empty outputs. Measured checksums are checked against the independently calculated expected result weight and fixed iteration count. The existing main/overflow/derived-getter qualification suites remain necessary; this supplement does not replace them.

## Warm, cold and retained structure

Timed batches get 64 warmup calls. Per-thread allocation, wall/CPU duration, generation0/1/2 counts, iterations, expected/observed checksums and retained capacities are recorded. Warm allocation must be zero. Work storage and Entries/ExplicitIndices/bucket-array identities must remain unchanged through the warmed batch. Array capacities and retained field descriptions are compared across revisions.

Before any timing, the coordinator runs every cold row in its own fresh process. Registry, source, seed, work object, delegate and reflection setup occur before the counter; the first workload invocation is measured without a semantic preflight that would warm it. For nonempty cases this includes cold Copy growth plus RemoveTags and observation; it is not total process startup or an isolated API call. Before/after capacities and cold bytes are recorded. Subsequent 64 warmups and 256 allocation-probe calls must allocate zero and retain the post-cold backing arrays. Cold-allocation increases or structural differences stop the coordinator before calibration. Cold samples are not timed samples. The cold report's generic GC fields are default zeros, not measured collection counts; do not interpret them as no GC during cold invocation.

Correctness-only mode uses one process per supplied source to validate all twenty rows, including a 256-call warmed allocation probe and retained-array checks. It does not run fresh-process cold checks and does not calibrate or measure durations. Its report states the loaded runtime. For a single-process smoke, supply exactly one variant and --test-only.

## Paired measurement protocol

A full run requires two or three sources, in first-baseline, optional prior-accepted, candidate order. All build the identical harness and runtime together. All labels require a full caller-declared 40-hex revision; exact file hashes are authoritative for snapshots. The runner does not prove that an archive belongs to its declared commit, and never decides whether a revision was accepted.

The first baseline alone calibrates each row. Calibration increases counts until an actual baseline batch reaches at least --batch-ms (default/minimum200ms); all attempts are retained. The resulting complete iteration map is hashed and then immutable. There is no candidate calibration, count retuning, or override midway through a run.

Exactly fourteen rounds use independent processes: AB/BA alternating for two variants, ABC/CBA for three. Every variant uses the same counts in every round. Raw stdout/stderr, source/binary hashes, CPU/affinity, toolchain, loaded runtime, process order, timestamps and exit codes are retained. Each measured process independently validates its pre/post state. Same-count reports must match registry size, whole-process checksum, expected per-row checksum, row order and capacities.

Comparisons retain every sample and use the established paired geometric ratio and conservative approximate99% interval (t=3.012). Timing flags use3%; batches below150ms, CPU/wall below0.9 or any collection get separate review flags. Counts of samples below200ms are also shown. These flags support review and never automatically accept a revision. Calibration guarantees the baseline calibration samples reach200ms; later host variance or a faster candidate can yield shorter measured batches, which remain visible and must not be silently retuned.

## Commands and provenance

Use only an authorized timing window. No timing sweep is part of package preparation.

    source /tmp/gameplaytags-env.sh
    python3 run.py --dotnet /tmp/gameplaytags-dotnet/dotnet \
      --variant original=/absolute/original-snapshot \
      --variant previous=/absolute/prior-accepted-snapshot \
      --variant candidate=/absolute/candidate-snapshot \
      --revision original=f7c99546dbf029d70896c0f05e5119b19ffd6d26 \
      --revision previous="$PREVIOUS_ACCEPTED_REVISION" \
      --revision candidate="$CANDIDATE_REVISION" \
      --out /absolute/new-output

Omit the optional previous pair when absent. For standalone Mono add --mono /tmp/gameplaytags-mono/bin/mono. Run each backend separately. For untimed correctness add --test-only; one variant is allowed in that mode.

The coordinator reuses the hardened Union supplement's preliminary manifest, raw byte command logs, failure retention and final source/binary tree re-enumeration. Source/package hashes are frozen before any child command. Every completed build's full bin tree is hashed, including dependencies and runtime configuration. Added, removed or modified source/binary files invalidate the run; failed commands retain raw output and a journal. Abrupt host termination can prevent final validation, so an unfinished journal is not validated evidence.

This package neither installs software nor edits/publishes the shared repository. It does not establish Unity/IL2CPP behavior or make a performance claim before paired timing is actually run.
