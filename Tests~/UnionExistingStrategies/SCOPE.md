# Semantic domain and competing strategies

## What this package can establish

This supplement compares accepted reusable-output Union V1 with stronger caller compositions already available at the original revision. It never changes the original `Tests~/UnionOutput`, its factory+Copy comparisons, any runtime source, CI workflow or acceptance ledger. No result from this package establishes universal noninferiority. The existing accepted count remains 1/10 unless the owner changes it using the full acceptance protocol.

Exact snapshots:

- Original: f7c99546dbf029d70896c0f05e5119b19ffd6d26
- Accepted V1: f53c5fb94c22c2750f54c33dcfdee443a72bf595

The unaccepted 90913a3 algorithm is deliberately not included. Default variants include every old strategy on both snapshots, preserving both factory+Copy controls and separating strategy choice from runtime-revision changes. Old compositions use only public preexisting API calls. Internal access is confined to post-operation observations.

## Healthy independent domain

The caller certifies all of the following before choosing a chain:

- Output owns storage distinct from both source views. It may initially be empty, dirty, populated with an unrelated deep tag, reserved, or grown and drained.
- All inputs describe finite, registered, healthy explicit sets and a coherent ancestor closure; built-in counted sources may have positive multiplicities, with no overflow, invalid removals, or inconsistent retained slots.
- Concrete ordinary containers expose their own ordinary storage. A proxy must be stable and transparent: its Indices, counts and GetExplicitTags describe the same view, with no relevant getter/enumeration side effects. Arbitrary derived normal classes hiding counted/other storage are excluded.
- No concurrent mutation occurs. A source remains unchanged during the whole multi-call composition.
- Required equivalence is unordered explicit membership, ancestor membership, correct ordinary reference counts demonstrated by duplicate-add/remove/full-drain continuations, source immutability, and serialized-cache invalidation. Enumeration order is explicitly documented as unordered by the runtime.

Within that domain, `Copy(output,left); output.AddTags(right)` replaces old output content, normalizes a counted left through Copy, and adds each distinct explicit right tag once. It is correct with empty sources, shared left/right source storage, overlap, hierarchy overlap, either operand size ordering and stable counted/proxy views. Reversing the chain is also a legal competing strategy. In sufficiently warmed retained output, the literal chain can allocate zero managed bytes: AddTags uses the struct enumerator and AddTag paths rather than AddUnion's rented queue. This is a source-derived possibility; this package's validation run does not measure it.

A literal chain is not guaranteed to preserve factory seeding, explicit/dense enumeration order, raw array contents, reserve capacities, active mask, retained bytes, first-use allocations or time. Those are separately reported. Post-hoc selection of the faster measured operand order must not be described as a free universal algorithm.

## Frozen strategies and labels

1. `factory-copy`: unchanged `Copy(output, Union(left,right))` everywhere.
2. `literal-left`: exact original Copy-left/AddTags-right composition only for the healthy independent domain; every other domain explicitly reports `factory-copy-fallback`.
3. `literal-right`: exact opposite-order Copy-right/AddTags-left under the same independent domain; otherwise explicitly labelled fallback.
4. `clear-add-both`: exact `output.Clear(); output.AddTags(left); output.AddTags(right)` in the independent domain; otherwise explicitly labelled fallback. This includes a straightforward old alternative that avoids the normalization builder when sources are counted/proxies.
5. `seed-alias-aware`: a fixed public-API heuristic. In independent healthy cases, prefer a concrete ordinary set over a counted/proxy source, then prefer larger closure by public TagCount when classification ties. Copy that seed and AddTags the other. Selection and getter costs are timed. This is factory-inspired, not a claim that the heuristic is optimal. For caller-certified exact ordinary aliases, append only the other source and clear an existing serialized cache. For unsupported domains use and label the original factory+Copy fallback.
6. `reusable`: accepted `Union(output,left,right)` everywhere.

`Domain` is an explicit fixture/caller precondition, not an alleged runtime detector of healthy arbitrary containers. Protocol 4 selects that domain route once outside measurement; every route uses the same delegate signature/invocation and literal chains pay no repeated health/fallback guard. There is no silent health scan and no oracle choosing a fastest chain after timing. We compare multiple legal strategies because no source-only argument establishes a single fastest preexisting implementation across shallow, deep, counted, overlap and alias workloads.

## Aliases

The literal left-first chain loses the original right input when output aliases right. Clearing output and adding both loses either aliased source. Neither is called equivalent on aliases. Left alias can preserve healthy membership, but self-Copy returns before clearing m_SerializedExplicitTags and AddTags does not clear it; raw left alias therefore fails full replacement-cache semantics. The same problem applies when all three objects are the same.

For exact healthy ordinary alias objects, the in-place union plus `output.m_SerializedExplicitTags?.Clear()` is a stronger legal preexisting strategy. It preserves the existing cache list identity and null-versus-empty behavior. The benchmark includes that explicit cache maintenance in its timed operation, with left, right and both-alias cases. Warm alias rows include a labelled restore+operation pair so each iteration does the same real work. First-call and warm-operation-only cold reports exclude restoration from the allocation marker and report it honestly.

A proxy may hide an alias even when object identities differ. Proxy aliases and arbitrary derived aliases therefore use factory fallback in every chain variant. The reusable strategy executes its actual API. The package includes a proxy-right-alias fallback fixture.

## Getters and proxy contract

Factory Union and accepted output Union read lhs.Indices then rhs.Indices once each before replacing output, and do not call source GetExplicitTags. Copy+AddTags instead reads the left view, mutates output, reads the right view and invokes right.GetExplicitTags. Right-first changes getter order. The seed heuristic adds public TagCount reads. These sequences are observably different for side-effectful getters, alternating views, enumerators describing different storage, default/truncated/preadvanced enumerators or getters that inspect output.

The package verifies the order and provides a concrete incoherent-enumerator mismatch, raw alias-right mismatch, raw alias-left stale-cache mismatch, and a stateful-indices fallback requiring exactly one input getter and zero enumeration calls. A source getter observing output is also checked against factory's read-before-replace order. These are exclusions, not a suggestion to suppress real source callbacks in the comparator. Actual public methods are called in every chain.

## Counted sources, public overflow and legacy continuations

Healthy concrete counted containers are normalized to ordinary set semantics and their source multiplicities remain unchanged. That does not extend to arbitrary positive-count views disguised as ordinary classes, or a counted source that has overflowed signed integers. The existing implementation exposes legacy states with stale explicit slots and can propagate them through a derived ordinary interface view and Copy. Even an exact ordinary container can acquire such state through public calls; a type check alone cannot certify health.

All such inputs use factory fallback for every chain strategy. Untimed tests construct overflow with MaxValue, MaxValue, 2, then cover a derived ordinary view in both input positions and the Count=0/ExplicitCount=1 state reached by public Copy/add/remove. They compare exception type, public raw observation and every owned array element via SHA-256 against the original composition within each revision. No reflection mutation creates the inputs. This is a focused guard, not a replacement for the repository's complete legacy/overflow suite.

## Serialization and state

All normal replacement fixtures begin with a non-null stale serialized list, except the separately labelled fresh-result timing/cohort operations, which use a true new output. Healthy assertions require cache clearing, and alias correctness checks start with stale data before the operation. Callback and actual DataContractSerializer round trips are checked outside timing for representative ordinary and alias cases. Subsequent normalized full drains verify counts rather than membership alone.

Owned-state observations include entry/explicit/bucket capacities, active mask, count fields, hashes of every slot including retained tails, and serialized-cache count/capacity. ArrayPayloadBytes is exactly 16 * entry-capacity + 4 * explicit-capacity + 4 * bucket-capacity. It is not total managed heap or object-header accounting. GC retained cohorts are separately labelled noisy measurements. Healthy chains may legitimately produce different owned layouts; those differences are data to review, not asserted equivalent. They also prevent generalizing healthy equivalence to later arbitrary malformed/overflow continuations. The original full-state factory/reusable evidence remains authoritative for that broader contract.

## Measurement boundaries

- Warm repeated operation: output already contains the prior result; 64 core/four deep prewarm calls precede the fixed-count measurement (protocol 3). Alias rows include Copy restoration. Time, per-thread managed allocation, CPU time and GC counts are separate fields.
- Fresh output in a process-warm loop: constructor plus operation per iteration; shared input setup and registry are outside the timer. This is not process-cold behavior.
- Process-cold: one fixture per fresh executable; no Union or Apply prewarm. The output's specified initial state is prepared outside the allocation marker. After uniform route selection/delegate construction, the first selected-operation invocation and the immediately following capacity-warmed invocation are measured separately, with alias restoration outside both markers; there are no intervening selected-operation calls. Delegate creation can affect JIT/cache state; the report does not claim untouched code-resolution state and excludes delegate construction for every route. Setup can call Copy for aliases, and that fact is not hidden. No process-cold timing claim is made.
- Retention: independently held cohorts of fresh output results with shared sources, plus exact owned-capacity reports outside the timed marker. Pool/global retention is not mislabelled as per-output ownership.
- Existing tests, runtime startup, fixture generation, semantic assertions, hashing, serialization and logging are outside measured loops.

All routes use the same delegate signature with interface-typed public operands to keep per-fixture dispatch fixed. A generic/concrete-only microbenchmark could have different inlining/dispatch costs and would require another separately frozen workload. The original UnionOutput control remains unchanged for that reason too.

The runner saves exact raw command output on success/failure, full package/runtime-source hashes, all built-binary hashes, full declared revisions, loaded CLR version, host inventory, CPU/affinity, command lines, execution order and fixed iteration map. It checks immutable inputs and binary trees at exit. Declarations are not cryptographic proof of a commit's identity; the included snapshot provenance records git object IDs and content hashes from exact git-show extraction.

Five original-f7-only pilots cover all old strategies, using two measured/four warmup calls for deep rows and 128 measured/64 warmup calls for core rows. Per row the slowest observed original strategy sets the extrapolated shared count. Fixed predeclared deep caps are 4096/64/8 for depths 128/1024/3000. This does not guarantee an actual batch reaches the target; fast deep rows may be short and every deep row remains diagnostic-only. Every variant gets the same map, whose SHA-256 is recorded and checked before/after each child command and at exit. A three-hour whole-run budget and 300-second child cap cover builds, cold calls, warmups, cohorts, setup, pilots, timing and final validation; timeout fails rather than dropping rows. Fourteen default rounds alternate forward/reverse variant order. Reported 99% paired log-ratio intervals are per row and approximate, not a simultaneous familywise noninferiority bound. Significant >3% regression flags, allocation changes, short batches and retained/owned differences require review. An inconclusive interval is not proof of within-3% performance. If any core batch is below 50 ms, do not rescue just that row after looking at accepted/candidate results: review/freeze a complete shared map using original-only evidence and rerun the whole paired comparison. Every map override must still obey the predeclared deep caps; deep ratios are descriptive and not acceptance evidence.
