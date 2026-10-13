# Assembly discovery array: Mono serializer-only repair

Preparation only. Independent review of this package's exact seal is required before a separately authorized execution. This is a bounded collector compatibility validation, not another performance screen. The original [.NET allocation CLOSE and four failed Mono semantic attempts](../ASSEMBLY_DISCOVERY_ARRAY_OUTCOME.md) remain preserved; qualification remains 0/10.

## Immutable source and exact correction

The source package `Tests~/PortfolioScreen/AssemblyDiscoveryArray` remains byte-for-byte and inventory-identical to frozen seal `470305cba602b6378c495959aaa24faf6d98986cd4a9c1c778dcdef4405f1840`. No existing package or root Runtime file is edited.

Both `Harness/SemanticChecks.cs` and `Harness/ColdScreen.cs` have exactly two changed lines, inside their Mono-only branches:

- Sorted dictionary field allowlist becomes `_buckets,_comparer,_count,_entries,_freeCount,_freeList,_keys,_syncRoot,_values,_version`.
- Null/type reference encoding applies to `_comparer`, `_keys`, `_values` and `_syncRoot`.

No field is skipped. All buckets, entries, spare array tails and scalar fields remain serialized. Unknown fields still fail. Entry.cs and the exact R2 MetadataGate.cs are unchanged copies. Runtime variants, persisted fixture sources, runtime header, reflection binding, counters, semantic scenarios and strict metadata/Init-body gate are unchanged. ColdScreen is corrected for consistency, but no cold target is dispatched by this repair.

The original allowlist was derived from an unselected Reference Source file. The pinned [Mono source manifest](https://github.com/mono/mono/blob/82fffe0749720025ec5f7a72eae7329295ea15a4/mcs/class/corlib/corlib.dll.sources#L1760) selects CoreFX Dictionary at gitlink `c30d255aec6b7de633d1c67b1a7f2ec121250359`. Its [instance fields](https://github.com/mono/corefx/blob/c30d255aec6b7de633d1c67b1a7f2ec121250359/src/Common/src/CoreLib/System/Collections/Generic/Dictionary.cs#L49) are all underscored. This verifies a source-level collector defect. The exact assertion behind the saved code102 failure is still inferred because the finite report did not expose its message or complete reflected field inventory.

## Fixed Mono-only semantic validation

One invocation permits exactly the inherited four compiler attempts, four fresh semantic calls and zero cold/warm/pilot calls, with a 120-second clock beginning at wrapper entry. The four builds are original Test, candidate Test, and the unchanged one/32-declaration DLLs, preserving the exact build graph. The four semantic calls are empty/retry on both variants; the full snapshot/trace/metadata gate must pass. Builds are not measurement samples, and declaration DLLs are not loaded into a cold target.

The thin wrapper verifies both complete sealed packages and reuses the original coordinator in memory. Only fixed unique anchors alter the entry clock, deadline/backend restriction, repaired harness location, additional identity guards, explicit repair provenance, pre-cold stop and final exact receipt. The compiler helper still builds the same four-file harness shape. No alternate source fallback, broad metadata normalization, retry, replacement or new fixture is allowed. Any new metadata/runtime incompatibility stops without widening the gate.

The explicit repair receipt requires 4 builds/4 semantics/0 cold and zero C# measurement rows. The inherited finalizer preserves all 24 planned cold diagnostic slots as unattempted placeholders, not measurements or a cold pass. All four semantic completions, successful full gate and zero cold attempts/completions/rows are required for success. A failed target remains failed even when partial snapshots match.

## Integrity, diagnostics and output

The exact original inputs, original rejected allowlists/predicates, repaired files, adapter anchors and source-chain commits are pinned in source-pins.json and frozen.json. The wrapper adds its own full inventory/hash verification before and after inherited processes, rejects symlinks and unknown package members, and never modifies the source package. The original protected signal cleanup, exit capture, source/tool/build/raw guards, shared Mono dependency pins, verified finite projector and sticky deadline/finalization handling are retained.

There are exactly six sanitized output files: status.json, provenance.json, config.json, correctness.json, raw/cold.json and summary.json. RepairFrozenSHA256, SourcePackageFrozenSHA256 and the fixed RepairBudget distinguish this derivative from the original 24-call plan. Raw logs, commands, absolute paths, metadata and physical snapshots remain private. No unrestricted environment dump is made. The original bounded code102 diagnostic vocabulary remains unchanged; its general CheckDictionaryFields frame is not newly exported.

CLI uses the established backend/tool/output/work/seal conventions, but accepts only Mono and 120 seconds. The source package defaults to sibling `AssemblyDiscoveryArray`; an explicit source-package argument must still match the same immutable seal. Out/work directories must be fresh and separate from both input packages. Parent workflow/common README and publication manifest are managed separately.

No compiler or C# target ran during preparation. Python-only syntax, exact two-line diff, unchanged-source, unique-anchor and missing/duplicate-anchor checks are preparation evidence, not a Mono semantic pass. No new timing continuation or qualification follows automatically from a successful repair.
