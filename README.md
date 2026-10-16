# ZFramework Gameplay Tags

[简体中文](README_CN.md) | **English**

A small C# gameplay-tag library for Unity: cached integer handles, hierarchical matching, explicit sets, reference-counted tags, parent containers and change notifications.

**The core trusts the caller.** Resolve names at loading boundaries, cache handles and keep hot paths on integers. Input validation, lifetime ownership, initialization order and synchronization belong to the application; the runtime does not add a defensive policy layer.

| Item | Value |
| --- | --- |
| Package | `com.acced.zframework.gameplaytags` |
| Source version | `2.1.0-rc.2` |
| Unity package target | Unity 2022.3 or later; validate on your actual Editor and player backend |
| Standalone target | .NET Standard 2.1, C# 9 |
| Namespace | `GameplayTags` |
| Unity assembly | `ZFramework.GameplayTags` |
| License | [MIT](LICENSE) |

This is a source candidate, not a claim of universal speedups or completed Unity/IL2CPP certification. The repository contains the library, editor integration and usage documentation only. Historical tests, benchmarks and experiments remain in Git history, not in the current package.

## Contents

[Installation](#installation) · [Quick start](#quick-start) · [Registration](#registration) · [Tag semantics](#tag-semantics) · [Sets](#sets) · [Queries](#queries) · [Set algebra](#set-algebra) · [Enumeration](#enumeration) · [Counts](#counts) · [Events](#events) · [Parent containers](#parent-containers) · [Pooling](#pooling) · [Unity and serialization](#unity-and-serialization) · [Caller contract](#caller-contract) · [API map](#api-map)

<a id="installation"></a>
## Installation

### Unity Package Manager

Open Package Manager, select **+ → Add package from git URL**, then enter:

```text
https://github.com/acced/ZFramework.GameplayTags.git#main
```

Or add this entry to the existing `dependencies` object in `Packages/manifest.json`:

```json
"com.acced.zframework.gameplaytags": "https://github.com/acced/ZFramework.GameplayTags.git#main"
```

Use `#<commit-sha>` instead of `#main` to pin a reviewed revision. For a local checkout, use **Add package from disk** and select its `package.json`. Remove other packages or copied sources defining the same `GameplayTags` types first. Do not install this package alongside the name-based `zframework.gameplaytag` package.

If your scripts use assembly definitions, reference **`ZFramework.GameplayTags`**. The included Editor assembly is Editor-only. Installation reference: [Unity Git dependencies](https://docs.unity3d.com/2022.3/Documentation/Manual/upm-ui-giturl.html).

### Standalone C#

The optional project compiles the core without the GameObject adapter or Editor code:

```bash
dotnet build GameplayTags.Runtime.csproj -c Release
```

Reference `GameplayTags.Runtime.csproj` from an application targeting a compatible framework. No source-generator project, NUnit dependency or test runner is required by the core.

<a id="quick-start"></a>
## Quick start

Create `GameTagDeclarations.cs`. Assembly attributes must appear after `using` directives and before any namespace or type declaration:

```csharp
using GameplayTags;

[assembly: GameplayTagAttribute("State.Burning", "Fire damage is active.")]
[assembly: GameplayTagAttribute("State.Frozen", "Movement is frozen.")]
[assembly: GameplayTagAttribute("Ability.Cast", "Spell casting is available.")]

// Application-owned cached handles. No generated code is required.
public static class GameTags
{
    public static readonly GameplayTag State = GameplayTagManager.RequestTag("State");
    public static readonly GameplayTag Burning = GameplayTagManager.RequestTag("State.Burning");
    public static readonly GameplayTag Frozen = GameplayTagManager.RequestTag("State.Frozen");
    public static readonly GameplayTag Cast = GameplayTagManager.RequestTag("Ability.Cast");
}
```

Create `TagExample.cs` and attach it to a GameObject. This example creates its container at runtime; serialized containers have the additional loading requirements described below.

```csharp
using GameplayTags;
using UnityEngine;

public sealed class TagExample : MonoBehaviour
{
    private GameplayTagContainer tags;

    private void Awake()
    {
        // Cold assembly-discovery path. Do this on the initialization thread.
        GameplayTagManager.InitializeIfNeeded();
        tags = new GameplayTagContainer(16);
        tags.AddTag(GameTags.Burning);

        Debug.Log(tags.HasTag(GameTags.State));          // True: ancestor match.
        Debug.Log(tags.HasTagExact(GameTags.State));     // False: not explicit.
        Debug.Log(tags.HasTagExact(GameTags.Burning));   // True.
        Debug.Log(tags.ExplicitTagCount);                // 1.
        Debug.Log(tags.TagCount);                        // 2: State + State.Burning.

        tags.RemoveTag(GameTags.Burning);
        Debug.Log(tags.IsEmpty);                         // True.
    }
}
```

The remaining examples use the declarations and `GameTags` class above. Method-body snippets belong inside your application's methods.

<a id="registration"></a>
## Registration and cached handles

Choose one registration strategy for each process/domain.

**Assembly declarations:** use `[assembly: GameplayTagAttribute(...)]`. The first `InitializeIfNeeded()`, nonempty name lookup, `TagCount` or `GetAllTags()` can discover declarations in currently loaded assemblies. Later-loaded assemblies are not automatically added to an already initialized registry.

**Explicit registration:** call `Initialize` once, before any lookup, cached-field access, Inspector-driven lookup or container deserialization. This path does not scan assembly declarations; supply the complete set yourself:

```csharp
// Alternative to assembly discovery, not a second initialization step.
GameplayTagManager.Initialize(
    new GameplayTagRegistration("State.Burning", "Fire damage is active."),
    new GameplayTagRegistration("State.Frozen", "Movement is frozen."),
    new GameplayTagRegistration("Ability.Cast", "Spell casting is available."));

GameplayTag burning = GameplayTagManager.RequestTag("State.Burning");
```

Parents such as `State` and `Ability` are added automatically. Names are case-sensitive. Labels should contain letters, digits or underscores and use `.` as a separator. `GameplayTagUtility.IsValidName(name)` and `ValidateName(name)` are optional authoring/import tools; registration does not call them automatically.

`RequestTag(name)` returns `GameplayTag.None` for a missing, null or empty name. Its `out` overload reports resolution success:

```csharp
if (GameplayTagManager.RequestTag(externalName, out GameplayTag resolved))
{
    // Caller-side validation at the boundary; cache resolved before repeated use.
    destination.AddTag(resolved);
}
```

Here `externalName` and `destination` are application inputs. Do not repeatedly resolve strings in `Update`, and do not rely on the implicit string-to-tag conversion inside a hot loop.

`Initialize` has no repeated-initialization guard or lock. **Publish once and never rebuild the registry while handles, containers, subscriptions or pooled containers remain live.** Initialization ordering and cross-thread publication are the caller's responsibility. Runtime IDs belong to that registry; they are not stable save-file, network or cross-build identifiers.

<a id="tag-semantics"></a>
## Tag semantics

`GameplayTag` stores a name and a cached integer. `RuntimeIndex`, `IsValid`, equality and hashing use the cached ID; changing `m_Name` does not update it. `IsValid` means the cached ID is nonzero, not that a name has just been checked against the registry.

```csharp
GameplayTag edited = new GameplayTag { m_Name = "State.Burning" };
edited = edited.Resolve(); // Assign the returned struct at the loading/editing boundary.
```

Unresolved and unknown handles have ID zero and compare equal to `None`, even when their stored names differ. Never use them as distinct dictionary keys. Do not change a tag used as a hash key.

| Member | Meaning |
| --- | --- |
| `Name` / `Label` | Full name / final path segment |
| `Description` / `Flags` | Registration metadata; flags are not permission checks |
| `ParentTag` | Immediate parent; `None` for a root |
| `ParentTags` | Strict ancestors, root to immediate parent |
| `HierarchyTags` | Root-to-self path, including the tag |
| `ChildTags` | All strict registered descendants in DFS order |
| `HierarchyLevel` | Root is level 1 |
| `IsParentOf` / `IsChildOf` | Strict relationship: a tag is not its own ancestor |

These relationship properties describe the **registry**, not membership in a particular container. `GetAllTags()` returns a read-only span in DFS order, not a promise of globally name-sorted container enumeration. Built-in `HideInEditor` is obsolete; do not base new behavior on it.

<a id="sets"></a>
## Ordinary sets

`GameplayTagContainer` owns a set of explicit members and maintains their ancestor closure. Adding the same explicit tag twice does not increase its contribution.

```csharp
var tags = new GameplayTagContainer(32); // Capacity includes distinct ancestors.
bool added = tags.TryAddTag(GameTags.Burning);       // True.
bool duplicate = tags.TryAddTag(GameTags.Burning);   // False.
tags.AddTag(GameTags.Frozen);

// State remains present while either descendant contributes it.
tags.RemoveTag(GameTags.Burning);
bool stillPresent = tags.HasTag(GameTags.State);     // True.
bool removedAgain = tags.TryRemoveTag(GameTags.Burning); // False.

tags.Clear(); // Removes members; retains reusable backing capacity.
```

`TryAddTag` and `TryRemoveTag` report membership changes; they are **not** validation APIs. Supply valid, non-None resolved tags. Removing an implicit parent does not remove its descendants or their contribution. Add the parent explicitly first when you need an independent parent membership.

`EnsureCapacity(n)` reserves for **distinct tags including ancestors**, not just explicit leaves and not multiplicity. It does not add members or shrink retained storage. All count/capacity arithmetic and available-memory limits belong to the caller.

<a id="queries"></a>
## Any/all queries and requirements

The holder supplies membership; the other container supplies explicit conditions:

```csharp
var holder = new GameplayTagContainer();
holder.AddTag(GameTags.Burning);
holder.AddTag(GameTags.Cast);
var query = new GameplayTagContainer();
query.AddTag(GameTags.State);

bool any = holder.HasAny(query);          // True.
bool all = holder.HasAll(query);          // True.
bool anyExact = holder.HasAnyExact(query); // False.
bool allExact = holder.HasAllExact(query); // False.
```

`HasAny(emptyQuery)` is false; `HasAll(emptyQuery)` is true. A typed null **query** has the same semantics. This does not permit calling instance methods on a null holder or passing null mutation operands.

Two query sets versus two holders are different operations:

```csharp
bool bothQueries = holder.HasAll(queryA, queryB);
bool splitAcrossHolders = GameplayTagContainerUtility.HasAll(staticTags, dynamicTags, query);
bool splitExact = GameplayTagContainerUtility.HasAllExact(staticTags, dynamicTags, query);
```

The first requires the holder to satisfy both condition sets. The latter two allow each condition to come from either holder without constructing a union. Variables in this snippet are your existing non-null containers.

```csharp
var forbidden = new GameplayTagContainer();
forbidden.AddTag(GameTags.Frozen);
var required = new GameplayTagContainer();
required.AddTag(GameTags.Cast);
var rule = new GameplayTagRequirements(forbidden, required);

bool canCast = rule.Matches(holder);
// rule.Matches(staticTags, dynamicTags) also supports two holders.
```

Requirements use hierarchical matching. They retain references to the condition containers, so later changes to those containers affect matching. An empty requirement succeeds.

<a id="set-algebra"></a>
## Copy, union, intersection and difference

```csharp
var left = new GameplayTagContainer();
left.AddTag(GameTags.Burning);
left.AddTag(GameTags.Cast);
var right = new GameplayTagContainer();
right.AddTag(GameTags.Frozen);
right.AddTag(GameTags.Cast);

var output = new GameplayTagContainer(32);
GameplayTagContainer.Copy(output, left);              // Replace output.
output.AddTags(right);                                // Union into existing output.
GameplayTagContainer union = GameplayTagContainer.Union(left, right);
GameplayTagContainer intersection = GameplayTagContainer.Intersection(left, right);
GameplayTagContainer.Intersection(output, left, right); // Replace with {Ability.Cast}.
left.IntersectWith(right);                            // In-place explicit intersection.
GameplayTagContainer clone = right.Clone();           // Independently mutable storage.
clone.RemoveTags(intersection);                       // Explicit set difference.
```

Union/intersection act on **explicit members**, then derive the result's ancestors. `{State.Burning}` intersected with `{State.Frozen}` is empty, not an implicit `State` result. Counted inputs contribute existence once per distinct explicit tag, not their multiplicity.

Copy to self is a no-op. Intersection output may be either input. `AddTags(self)` on an ordinary set is a no-op; `RemoveTags(self)` clears it. Reuse `Copy(output, left); output.AddTags(right);` when output is distinct from both inputs. If output is already `right`, append `left` directly instead: copying over `right` first would destroy that input.

There is no rc.1-style `SetUnion`, `CopyFrom` or `SetIntersectionExact` method on this integer API. Use the methods above.

For a UI delta, `current.GetDiffExplicitTags(previous, added, removed)` **appends** current-minus-previous to `added` and previous-minus-current to `removed`. Clear reused output lists first.

<a id="enumeration"></a>
## Enumeration and hierarchy extraction

```csharp
foreach (GameplayTag tag in tags.GetExplicitTags()) { /* Explicit members only. */ }
foreach (GameplayTag tag in tags) { /* All members, including implicit ancestors. */ }
```

`ExplicitTagCount` is the number of distinct explicit members. `TagCount` includes distinct implicit ancestors. Concrete enumeration uses a struct enumerator; using it through `IEnumerable<GameplayTag>`/`IEnumerator<GameplayTag>` can box. `Indices` is a shared live storage view, not a snapshot; use `Clone` or `Copy` for an independent result.

Enumeration is unordered and becomes invalid after mutation. No fail-fast version tracking is provided, including Debug builds. Do not infer insertion order or rely on an observed DFS order after a bulk operation.

Sort only where your UI, save format or protocol actually needs it:

```csharp
var names = new System.Collections.Generic.List<string>();
foreach (GameplayTag tag in tags.GetExplicitTags()) names.Add(tag.Name);
names.Sort(System.StringComparer.Ordinal);
```

`GetParentTags(tag, list)` and `GetChildTags(tag, list)` append matching strict ancestors/descendants that are present in the container. `GetExplicitParentTags`/`GetExplicitChildTags` restrict membership to explicit tags. All four append without clearing. Parent results are nearest-parent first; do not rely on child result ordering.

<a id="counts"></a>
## Reference-counted tags

Use `GameplayTagCountContainer` when independent systems contribute the same tag:

```csharp
var effects = new GameplayTagCountContainer(32);
effects.AddTag(GameTags.Burning, 3);
effects.AddTag(GameTags.Frozen, 2);

int burningCount = effects.GetExplicitTagCount(GameTags.Burning); // 3.
int stateExplicit = effects.GetExplicitTagCount(GameTags.State);  // 0.
int stateTotal = effects.GetTagCount(GameTags.State);             // 5.
int distinctExplicit = effects.ExplicitTagCount;                 // 2, not 5.
int distinctTotal = effects.TagCount;                            // 3.

effects.RemoveTag(GameTags.Burning, 2);                           // Burning is now 1.
effects.Clear();
```

Amounts must be positive; removal must not exceed the tag's explicit count; accumulated counts must not overflow. No preflight checks are performed. A member with no explicit occurrences is ignored by removal.

`AddTags(source)` adds **one** occurrence per distinct explicit source member. `RemoveTags(source)` removes one each; it does not copy/remove the source's multiplicity. `RemoveTags(self)` therefore subtracts one occurrence per explicit member, unlike the ordinary set's clear behavior. `Clear()` removes all counts but keeps subscriptions. To preserve source multiplicity explicitly, iterate a distinct counted source and use `AddTag(tag, source.GetExplicitTagCount(tag))`.

<a id="events"></a>
## Events and binding lifetime

```csharp
var effects = new GameplayTagCountContainer();
OnTagCountChangedDelegate onChange = (tag, count) =>
    UnityEngine.Debug.Log($"{tag.Name}: {count}");

effects.RegisterTagEventCallback(GameTags.State, GameplayTagEventType.AnyCountChange, onChange);
effects.AddTag(GameTags.Burning); // State callback: 1.
effects.AddTag(GameTags.Frozen);  // State callback: 2.
effects.Clear();                 // State callback: 0.
effects.RemoveTagEventCallback(GameTags.State, GameplayTagEventType.AnyCountChange, onChange);
```

`AnyCountChange` observes changes to the total count. `NewOrRemoved` observes only zero-to-positive and positive-to-zero transitions. Ancestors receive contributions and can have their own notifications. Registering a raw callback does not immediately report the current value.

Global events are `OnAnyTagCountChange` and `OnAnyTagNewOrRemove`. Unsubscribe with the same delegate. `RemoveAllTagEventCallbacks()` clears tag-specific registrations, **not** subscribers attached with `+=` to the global events.

`GameplayTagContainerBinds` provides grouped presence subscriptions and immediately reports current presence:

```csharp
using (var binds = new GameplayTagContainerBinds(effects))
{
    binds.Bind(GameTags.State, present => UnityEngine.Debug.Log(present));
    effects.AddTag(GameTags.Burning);
} // Disposes these subscriptions, not the effects container.
```

Bindings can allocate state/delegates; set them up outside hot loops. Copies share subscription state. The immediate Bind callback is not rolled back automatically on error; the caller owns disposal. Established count-event dispatch observes committed changes; reentrant events are queued. A thrown callback propagates, retains committed state and releases pending notifications. This is cleanup, not transactional rollback.

<a id="parent-containers"></a>
## Parent containers

Container parents aggregate contributions; they are different from the dotted-name hierarchy:

```csharp
var actor = new GameplayTagCountContainer();
var equipment = new GameplayTagHierarchicalContainer { Parent = actor };
var aura = new GameplayTagHierarchicalContainer { Parent = actor };
equipment.AddTag(GameTags.Cast, 2);
aura.AddTag(GameTags.Cast);
int total = actor.GetExplicitTagCount(GameTags.Cast); // 3.

equipment.Parent = null;                            // Detach its two contributions.
int remaining = actor.GetExplicitTagCount(GameTags.Cast); // 1.
equipment.Parent = actor;                           // Transfer existing contributions back.
equipment.Clear();                                 // Remove only equipment's contributions.
aura.Clear();
aura.Parent = null;
equipment.Parent = null;
```

Contributions flow **child to parent**, not parent to child. Reparenting transfers existing explicit counts. Prevent cycles, overflow and concurrent changes yourself. Never directly remove child-owned contributions from an aggregate parent, or clear the parent while children still assume their contributions are present. Clear/detach children first. Built-in chains batch notifications; custom parent implementations control their own behavior. Long parent-container chains still have recursive propagation cost.

<a id="pooling"></a>
## Reuse and pooling

```csharp
using (GameplayTagContainerPool.Get(out GameplayTagContainer temporary))
{
    temporary.EnsureCapacity(64);
    temporary.AddTag(GameTags.Burning);
    temporary.AddTag(GameTags.Cast);
    // Consume temporary here. Do not let the reference escape the scope.
}
```

Or pair `GameplayTagContainerPool.Get()` with exactly one `Release(container)`. Return on the borrowing thread. Do not copy a lease and dispose both copies, dispose a default lease, return twice, or access a returned reference. The pool does not check these mistakes. Each thread keeps up to 64 returned containers; their retained buffers can remain large. New containers, growth, serialization and cold array-pool rentals can allocate. Warm reuse is not an unconditional zero-allocation guarantee.

<a id="unity-and-serialization"></a>
## Unity components, Inspector and serialization

The included property drawers edit `GameplayTag.m_Name` and `GameplayTagContainer.m_SerializedExplicitTags`. They select registered tags; they are not a settings-asset manager and do not create declarations. Recompile/reload the domain after changing assembly declarations; the registry does not refresh itself on every lookup.

```csharp
using GameplayTags;
using UnityEngine;

public sealed class SerializedTagOwner : MonoBehaviour
{
    [SerializeField] private GameplayTag selectedTag;
    [SerializeField] private GameplayTagContainer configuredTags = new GameplayTagContainer();

    private void Awake()
    {
        // Registry must already be ready before serialized containers are loaded.
        // Resolve standalone struct fields explicitly; assign the returned value.
        selectedTag = selectedTag.Resolve();
    }
}
```

**Initialization comes before deserialization, not merely before the first Update.** Container `OnAfterDeserialize()` rebuilds membership immediately and may cause assembly discovery when the registry is not ready. Unity serialization callbacks can run away from the main thread, and struct callbacks must not be assumed to run. Prepare registration on the owning thread before loading assets/scenes, including the Editor path. An `Awake`/`BeforeSceneLoad` hook alone must not be assumed to precede every Editor or asset deserialization. See [Unity serialization callbacks](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ISerializationCallbackReceiver.html).

With domain reload disabled, static registry fields, cached handles, subscriptions and thread-local pools can survive a play transition. Do not republish a different registry over them. Keep the same registry, or arrange a genuine domain restart and rebuild all dependent state. See [Unity domain reloading](https://docs.unity3d.com/2022.3/Documentation/Manual/DomainReloading.html).

`GameObjectGameplayTagContainer` exposes a runtime `GameplayTagCountContainer` through its `GameplayTagContainer` property. `m_PersistentTags` are added once in the component's `Awake`. The count container exists before Awake, but persistent contributions do not; accessing an inactive object or relying on another component's Awake order requires application coordination. `gameObject.Bind()` gets/adds this component and returns a binding group; dispose that group when its owner is disabled/destroyed.

Normal containers serialize explicit **names**, sorted Ordinal, and derive ancestors again when loaded. Unknown names are not restored as members and may be discarded on subsequent serialization. Single tags have name-based .NET DataContract callbacks; standalone Unity tag fields should be resolved explicitly by their owner. `GameplayTagCountContainer` and `GameplayTagHierarchicalContainer` are runtime objects, not persistence DTOs: store names/counts in your own save model.

Never persist `RuntimeIndex` or private arrays. Editing `m_Name` requires `tag = tag.Resolve()` afterward. Directly changing a live container's serialized name list does not update membership; use Add/Remove APIs, or explicitly rebuild from that list at a controlled loading boundary. Calling container `OnAfterDeserialize()` replaces runtime state, so do not call it casually on a modified live container.

<a id="caller-contract"></a>
## Caller contract and performance boundaries

| Responsibility | Requirement |
| --- | --- |
| Registry | Initialize once before loading/binding; no replacement while state is live; caller-owned synchronization |
| Handles | Valid, resolved, non-None mutation operands from that registry; resolve again after name editing |
| Collections | Non-null mutation operands; `Empty` is read-only by convention, not enforcement |
| Counts/capacity | Positive amounts, no excess removal or overflow; valid allocatable capacities and arithmetic |
| Ownership | No parent cycles; remove contributions through the child that owns them |
| Concurrency | No concurrent mutation; no mutation during enumeration; no automatic thread safety |
| Pooling | Single-owner lease; exactly one same-thread return; no use after return |
| Errors | Caller handles invalid inputs and callback failures; no general rollback guarantee |

The implementation keeps duplicate-set semantics, missing-member no-ops, alias handling, ancestor accounting and internal resource cleanup. Those are algorithm behavior, not defensive validation. There is no checked-mode switch, name-consistency fallback on every query, pool-generation tracker or count-overflow preflight.

Cache handles, reuse containers/output lists, reserve realistic closure capacity and keep diagnostics/logging out of hot loops. Storage includes entries, hash buckets and an explicit index, so it is **not four bytes per tag**. Hierarchical updates cost work along ancestor paths; intersections can sort selected IDs and rebuild closure. Measure your own mixes, hierarchy depths, memory usage and Unity backend before making performance claims. This managed implementation is not a NativeContainer or a Burst/Jobs-safe API.

### Upgrading from the name-based package

This is not a drop-in replacement for name-based 1.1 rc.1. Package/assembly identities, APIs, container serialized fields, ordering and registry lifetime differ. Replace `Count` with the appropriate explicit/total count, use integer-version Copy/Union/Intersection APIs, migrate stored tag names deliberately and sort at output boundaries when required. The old Settings/Redirects APIs are not provided. No automatic YAML, prefab or settings migration runs.

<a id="api-map"></a>
## API map and repository layout

| Type | Use |
| --- | --- |
| `GameplayTagManager` | One-time registration, name lookup, registered metadata |
| `GameplayTag` | Cached handle, strict hierarchy relationships, explicit Resolve |
| `GameplayTagContainer` | Ordinary explicit set, clone/copy, union, intersection, difference |
| `GameplayTagCountContainer` | Multiplicity, aggregate ancestor counts, notifications |
| `GameplayTagHierarchicalContainer` | Count contributions to an aggregate parent |
| `GameplayTagRequirements` | Required/forbidden conditions, one or two holders |
| `GameplayTagContainerUtility` | Two-holder queries and container hierarchy extraction |
| `GameplayTagContainerBinds` | Grouped tag-presence subscriptions |
| `GameplayTagContainerPool` | Thread-local temporary ordinary containers |
| `GameObjectGameplayTagContainer` | Unity component adapter |
| `GameplayTagUtility` | Explicit name validation and path parsing |

Name helpers include `GetHierarchyNames`, `GetHierarchyLevelFromName`, `TryGetParentName` and `GetLabel`; the historical `GetHeirarchy...` spellings remain as aliases. Parsing helpers assume valid input.

```text
Runtime/                      Core and Unity component adapter
Editor/                       Inspector drawers and popup tree views
GameplayTags.Runtime.csproj    Optional standalone core build
package.json                  UPM identity and minimum Unity target
README.md / README_CN.md      English / Simplified Chinese usage
LICENSE                       MIT license
```

Necessary `.meta` files are retained. Tests, benchmarks, experimental snapshots, result archives, SDK pins, packaging tools, optional source-generator projects and their CI workflows are not part of this clean tree. No generated `GameTags` API is assumed: the cached class in the quick start is ordinary application code. Removed development material remains available in repository history.

[Runtime source](Runtime) · [Editor source](Editor) · [MIT license](LICENSE)
