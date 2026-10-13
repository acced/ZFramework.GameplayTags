# Assembly discovery array: bounded portability screen

Preparation only. Neither this package nor its public workflow has been executed. A source seal and independent review are required before either backend may run. This is not an accepted optimization; the qualification count remains 0/10.

## Candidate and comparator

The exact f7 baseline automatically discovers declarations from sorted loaded assemblies. The candidate changes only the foreach input in `GameplayTagManager.InitializeIfNeeded`:

```csharp
(GameplayTagAttribute[])Attribute.GetCustomAttributes(assemblies[i], typeof(GameplayTagAttribute))
```

The exact two-argument static API has `inherit=true`. Registration, assembly sort, lock, double volatile initialization check and publication remain unchanged. The baseline's generic extension exposes this same requested-type array as `IEnumerable<GameplayTagAttribute>`. This is not a ToArray copy. Explicit `Initialize` bypasses required automatic discovery and is a narrower alternative, not this screen's comparator.

`source-pins.json` pins all 44 authenticated f7 Runtime files for both variants. Only the one overlaid manager file differs. No current working-tree Runtime file is read as the baseline. GameObject-only Unity integration is excluded from these non-Unity compiler inputs; no Unity/IL2CPP claim follows.

## Fixed scope, per backend

- .NET SDK 8.0.425 compiler, .NET runtime/reference pack 8.0.31; or the same compiler with net472 references and Mono 6.8.0.105. x64 only.
- Four compiler attempts: original Test, candidate Test, declarations1 and declarations32. No compiler retry.
- Four fresh semantic calls: empty and retry, each on original and candidate. All planned calls finish before semantic review unless integrity/deadline prevents them.
- Only a complete semantic/full-state/trace/metadata pass admits cold calls.
- Twenty-four fresh cold calls: n0, n1, n32; four pairs each in AB, BA, BA, AB order. Once admitted, all call groups finish before analysis even when a target or oracle fails. Integrity/deadline failure stops, retaining every planned slot as attempted or unattempted.
- One fixed 300-second whole-run ceiling, including preparation, children and finalization. No warm measurement, pilot, replacement, retry, native capture or automatic continuation.

Across both separately selected backends this is at most eight compiler attempts, eight semantic targets and 48 cold targets. Python preparation checks exercise only the pure sanitizer and source invariants, never a C# target.

CLI conventions remain `--backend net|mono --dotnet --mono --nuget --git-root --out --work --expected-seal --deadline-seconds 300`. Public output and private work must be fresh, distinct directories. The expected seal is SHA256 of `frozen.json`; the coordinator authenticates its complete package file inventory before importing its helpers. The workflow and common README are coordinated separately.

## Semantic coverage

The empty mode exercises actual automatic discovery with no matching declarations. The retry mode creates the same five named Reflection.Emit assemblies in reverse FullName order, with ordinary and derived declarations. It checks deterministic construction/merge/order, a throwing constructor with no publication, a subsequent malformed null TagName failure after same-assembly eager construction but before later-assembly construction, and a final successful retry. There is no private registry reset.

Both modes check actual typed arrays for empty/nonempty/derived retrieval, public initialized fast paths, None/unknown lookups, full registry topology and reference preservation. The successful ordinary declarations here are dynamic assembly metadata; they do not establish persisted-nonempty performance. The separate cold fixtures below do.

The complete private physical-state snapshots include registry arrays, dictionary buckets/entries/spare slots and scalar fields. Comparer and lazy collection/sync-root references are encoded by null/type identity rather than recursively serializing framework graphs. Exact backend dictionary field allowlists reject unknown fields. Reference identity is separately checked within each semantic process. Snapshot/trace equality is required within backend, never across runtime implementations.

## Persisted cold fixtures and boundary

The two declaration-only DLL source files are unchanged from the admitted local cold screen. They contain one or 32 ordinary assembly attributes and no defined type or initializer. Each backend builds its two DLLs against its original Test identity, then uses identical immutable selected DLLs under both Test variants. Neither Test.dll nor Test.exe may be beside them. n0 loads no fixture. Both nonempty shapes contain one attributed assembly: n32 tests traversal, not 32 separate enumerators.

Before counters, setup verifies main and fixture identities, exact hashes, expected Test AssemblyRef and its reference-identical resolution to the executing variant, and exactly one loaded Test. .NET retains Default AssemblyLoadContext loading. Mono uses LoadFrom and Assembly.Load within the current AppDomain, without custom resolver events. Mono's four dependency DLLs occupy one guarded shared directory and MONO_PATH is overwritten to that directory, never appended. No duplicate Test is placed there. Dependency locations therefore match across variants without additional inventory normalization. Inherited DOTNET_, COMPlus_, CORECLR_ and MONO_ switches are removed before fixed runtime/diagnostic overrides are applied. A complete-environment digest and fixed controlled-overrides record are retained privately; inherited values are not dumped.

No manager operation, manager-field reflection or custom-attribute retrieval/type resolution occurs in setup or the runtime header. Framework counters, output and GetAssemblies infrastructure are primed equally. Mono resolves/primes a cached allocation-counter delegate before counters; .NET retains direct allocation-counter calls. The timed call remains the exact noinline `DiscoveryArrayCheck.NativeAll` body calling GetAllTags().Length. Its first JIT remains inside elapsed time. Counter reads sit outside the timed interval; the allocation interval also includes the common counter scaffolding.

The numerical row is flushed before noinline post-counter validation. That validator first records the After assembly inventory, then full physical state, then verifies actual attribute base/assembly identity and all declaration metadata/order, counts, ids, flags, descriptions, None and topology. After therefore means **post-counter validation-entry inventory**, after raw output and validator's first JIT; it is not an immediate operation-exit inventory or proof every newly loaded assembly came from discovery.

Paired inventories compare every field after normalizing only executing Test location/MVID. IsDefaultContext is factual and nullable: true for admitted .NET assemblies, not applicable on Mono. BindingModel and current-AppDomain membership are explicit. Full names/locations are private. The combined semantic/cold harness and runtime header change loaded-assembly/JIT context from local measurements; this is a portability replication, not a replacement sample.

## Metadata and provenance gates

The exact R2 MetadataGate writer is unchanged. Full Runtime metadata and unrelated IL are compared within backend; only InitializeIfNeeded's body may differ. Existing generated names remain exact. The changed-body gate checks the old template, exact two-argument static API, position of reflection calls, array/index locals, two volatile checks, comparison cache, and retained Monitor finally. Only the preserved expected template's known core-library qualifier changes from System.Private.CoreLib 8.0 to mscorlib 4.0 for Mono. Observed metadata is never broadly normalized. Any additional framework identity difference stops the gate instead of silently widening it.

Metadata raw token relocation is permitted only when resolved operands/signatures remain equal and is separately reported privately. The collector excludes global compiler PrivateImplementationDetails, PE RVAs/MVID and absent conditional source. The expected Init-body JSON is a fixed test fixture, not an uploaded target metadata report.

The coordinator records actual SDK/compiler/host/runtime/reference/package hashes, x64 target runtime headers, source and fixture pins, and build/target attempts. Mono's host plus selected references and package binaries are pinned; this does not attest every GAC framework assembly or native dependency. Runtime header strings alone do not establish deployed library/native equivalence.

## Failure retention and output contract

See `FAILURE_PROJECTION.md`. Only six sanitized result files may be uploaded: status.json, provenance.json, config.json, correctness.json, raw/cold.json and summary.json. Every planned call has a slot, including failures and unattempted calls. A valid numerical row survives a later malformed/failed semantic record or nonzero target exit. No replacement/filtering of adverse pairs is permitted.

Raw stdout/stderr, commands, full snapshots, complete inventories and metadata stay private. Signals/deadlines kill and reap the child process group; exit capture is inside protected cleanup. Source/tool/artifact guards verify old pins before admitting any newly created group. The finite projector is hash-verified before preload and can still render bounded diagnostics if later integrity fails. Finalization/deadline errors remain sticky.

## Evidence and limits

`LOCAL_EVIDENCE.md` preserves the admitted local native/semantic and first-discovery observations, including mixed cold ratios and outliers. No blanket speedup or automatic acceptance is claimed.

Upstream sources establish eager construction, assignable filtering, typed-array materialization and empty/nonempty enumerator behavior. Removing allocation/enumerator lifetime can change out-of-memory/resource-failure timing, object retention and GC observations; no whole-heap or resource-failure equivalence is asserted. Dispose is empty in the pinned implementations, but its removed lifetime/EH remains disclosed. The empty 32-byte observation is not attributed to a particular cache/object from source alone. No Mono native lowering, Unity or IL2CPP result is established by the local .NET native capture.
