# ID-only binding records: bounded portability/rejection screen

This is an unaccepted optimization candidate against exact base revision `f7c99546dbf029d70896c0f05e5119b19ffd6d26`. The candidate changes only private `GameplayTagContainerBinds.BindData.Tag` to `TagId`, stores `tag.RuntimeIndex`, and reconstructs `new GameplayTag(null, binding.TagId)` for the existing removal call. Both original delegate constructions, callback registration/immediate invocation order, shared group ownership, growth, and clearing remain. `candidate.patch` is the complete Runtime delta; `source-pins.json` identifies all 44 files of both Runtime trees.

## Contract and comparator

The public removal method is nonvirtual and uses only the tag ID. The internal value constructor performs no manager lookup. The existing callback still receives the container's actual event tag; the reconstructed null name is used only for removal. Derived hidden methods cannot intercept this statically typed base call.

The private graph intentionally changes: an arbitrary mutable/noncanonical name string need no longer remain rooted by a binding record. A public `WeakReference` can observe earlier collection. This is outside the subscription lifetime contract. Canonical registered names remain registry-rooted. No new forced-GC name-lifetime criterion or whole-heap retained-byte claim is made; the inherited callback-target-release check remains unchanged.

The strongest same-group comparator is the unchanged base API with the same workload. A caller with a fixed known set can instead retain its own callback, register it, issue the immediate notification, and remove it later, potentially avoiding group costs. The direct subscription/dispatch controls omit group ownership/immediate `Action<bool>` notification and are unchanged-path controls, not equivalent group replacements. This screen does not establish superiority over a manually owned subscription group.

## Fixed execution contract

One invocation selects `--backend net` or `--backend mono`; the workflow runs them separately. Each backend has exactly two builds, two untimed semantic targets, 32 fresh-process first-use targets, and 56 fresh-process warm targets. There is no pilot, adaptation, retry, row dropping, replacement sample, continuation, or automatic acceptance. The coordinator's hard deadline is 300 seconds including materialization/builds/targets/validation. A shorter requested deadline is allowed; extending it is not.

CLI: `run.py --backend {net,mono} --dotnet <host> --mono <host> --nuget <packages> --git-root <repository> --out <new-public-directory> --work <new-private-directory> --expected-seal <SHA256-of-frozen.json> [--deadline-seconds 300]`. Output and work directories must be new, separate, nonnested and outside this package. Publication and execution require their own reviewed authorization.

Builds reuse the existing portable direct-C#-compiler helper: SDK 8.0.425, .NET runtime/reference pack 8.0.31, and the existing .NET Framework 4.7.2 reference/package set for Mono. Actual target runtime identities must be .NET 8.0.31 or Mono 6.8.0.105, with 8-byte pointers. The Mono version is read from the executing `Mono.Runtime`, not inferred from installation. Tiering is disabled on .NET and requested runtime switches are retained in warm records. Compiler/runtime/reference inputs are hash-checked; the inherited helper hashes the Mono executable but does not enumerate every loaded Mono framework/GAC library. This is not a complete machine/environment attestation.

### Semantic and mechanism gate

The combined assembly runs the existing core suite (7,595,855 assertions), the 70-check binding lifecycle fixture, and the same 13 focused frames: None/named-None before registry initialization, mutated/null/noncanonical names, hidden derived methods, shared copies, reentrancy/throw behavior, growth and cleared tails. Exact stdout bytes and snapshot bytes must match within each backend.

`MetadataGate.cs` records the complete included GameplayTags metadata/IL. The comparator allows only the private record field substitution and the two scoped method bodies, with the precise added ID getter/value constructor, two original delegates, and no manager lookup or boxing. Unrelated normalized/raw IL and definition tokens remain checked. Managed `sizeof` is emitted into a `DynamicMethod`; `Marshal.SizeOf` is not used. Both backends measure their own positive record-size reduction with equal tag sizes; Mono is not presumed to share .NET's layout. Native code is not captured in this portable screen. Unity-only wrapper/conditional builds, compiler-global implementation details, PE RVAs/MVID and absent conditional configurations remain outside the reused metadata scope.

Any semantic/metadata/layout failure stops before cold targets. All 32 cold slots finish before group completeness, semantic equality and paired whole-call allocation deltas are assessed. Failure or any allocation increase blocks warm targets. Individual cold latency outliers never trigger that automatic gate. All 56 warm slots then remain fixed; invalid semantics or allocation increase reject the completed warm stage. Latency is retained for review and never establishes acceptance by itself.

### First-use boundary

Four shapes are unchanged: absent tag, present tag, one existing external listener, and five distinct binds crossing capacity four. Holder construction and its presence/external-listener setup are outside the boundary. Group construction plus Bind(s) are inside, including the first no-inline bridge JIT. Cleanup/event probes/reflection/managed-size measurement occur after counters. The public holder, tags and `Action<bool>` are prepared before counters.

Each shape has four original/candidate pairs in AB/BA/BA/AB order (32 calls). Whole-call allocated bytes are reported separately from `OwnedRecordArrayPayloadBytes = BindingCapacity × actual ManagedRecordSize`. This payload excludes array headers/alignment, group/state/delegates, callback tables, and the rest of the heap; it is not a whole-array or total retained-heap estimate. Growth allocation includes both transient and retained record arrays.

### Warm boundary and count map

`Harness/WarmScreen.cs` is byte-identical to `Bindings/Harness/Screen.cs`, SHA-256 `b1cc16c0f2ce5264f36b9c001236cdbc1c6cfb41d24f44433a25b3b7d5f70cee`. Selected rows are original indices 0, 1, 2, 4, 6, 7 and 8: fresh single, retained absent, retained present, retained distinct eight, external multicast, direct subscription and direct dispatch. Fixture bodies, validation and 1,024-cycle warmups are unchanged. Retained distinct-eight is multi-record work, not a growth timing claim; cold growth is separate.

One fresh process runs each row/pair slot, unlike prior full-traversal CI. Controls therefore do not follow earlier allocating binding rows. Local and portable observations are not pooled across process/assembly contexts. Warm fresh-single includes holder and group construction; retained rows reuse warmed capacity.

The immutable shared map uses only four preserved original observations per row: `max(original_iterations, ceil(original_iterations × 250 × frequency / (1000 × minimum_original_tick_count)))`. Counts are 1,710,404; 3,466,725; 3,506,477; 403,525; 1,481,094; 7,639,630; 3,212,116 in the above row order. Exact ticks/formula/source hashes are in `count-map.json`. No candidate observation enters count selection. The same map is used on both backends without a pilot; 250 ms is a target, not a duration guarantee. Short/GC/adverse records remain evidence.

## Entry-only and build adapters

`entry-adapters.patch` records only renaming the core harness's `Program` class to `CoreProgram`, redirecting its semantic call, omitting the local native `PrepareMethod` loop, and correcting its final output description. `CoreFixtureRegistration`, `BindingCorrectness`, `MetadataGate`, `ColdScreen`, `WarmScreen`, and `WarmLauncher` are unchanged from their preserved local fixtures. `binding-fixture-adapter.patch` also preserves the earlier local binding-fixture entry/tag-registration adaptation used to coexist with the core registry; it is not a new portable test-body change. `Entry.cs` adds invariant culture, a numeric runtime header, and dispatches only semantic, cold-shape, or selected warm-timing arguments. Dormant modes in reused fixture files are unreachable through this selected assembly entrypoint. Reflection/dispatch occurs outside measured fixture boundaries. Combining these files changes assembly context; the local measurements are not asserted to transfer unchanged.

`build-adapter.patch` changes only the inherited helper's source list to include all Harness C# files and selects `PortableBindingEntry` explicitly. `mechanism_compare.py` retains the saved metadata/IL gate while removing native-listing requirements and fixed .NET layout assumptions. A separately authorized untimed two-build/two-semantic .NET smoke can check adapters without adding a public CLI mode.

## Disclosure and failure limits

Only the eight explicit files in `upload-allowlist.txt` may be uploaded: status, provenance, configuration, correctness, count map, cold numeric observations, warm numeric observations and summary. No directory glob is authorized. Reports contain fixed labels, numeric/boolean observations, hashes, and validated numeric runtime versions. Raw commands, stdout/stderr, environment/tool dumps, binaries, metadata snapshots and private failure text stay in the separate work directory. Missing/invalid samples carry failure flags and output hashes rather than raw error text.

Numerical fixture output occurs after validation. A throw or uncatchable process/host termination can prevent the numeric record from being emitted; hashes/status cannot recover missing measurements. Catchable cancellation uses a deferred child-process scope and kills its process group, including the spawn/handle-assignment interval. Prior artifacts are verified before later build inventories are added. Deadline/termination/integrity failures remain incomplete; never fill missing slots or interpret an incomplete report as a passing screen.

See [LOCAL_EVIDENCE.md](LOCAL_EVIDENCE.md) for the preserved .NET-only mechanism/allocation result and all mixed/adverse local timing directions. No version is qualified by this package.
