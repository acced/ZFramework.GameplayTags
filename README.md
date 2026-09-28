# ZFramework Gameplay Tags — Dense-only 4.0 prerelease

[简体中文](README_CN.md) · [Design, costs and migration](Documentation~/DENSE_ONLY.md) · [MIT license](LICENSE)

This branch removes the Sparse/Auto runtime backend. Every prepared set is one dense member bitmap with 64-way occupancy summaries in the same array. The registry remains an immutable DFS snapshot; editable and serialized data remain stable names.

**Not a stable release.** Managed checks and hosted benchmarks do not replace native Unity 2021.3 / Unity 6 or Android/iOS IL2CPP validation. Full results, including losing cases, are retained by the Dense audit. Flat Dense allocation is proportional to the entire registry, even when a character owns few tags.

## Install and define tags

Install this branch as a local UPM package using its `package.json`. Reference `GameplayTags` from a gameplay asmdef. Use Tools → ZFramework → Gameplay Tags to create the default settings at `Assets/Resources/GameplayTags/GameplayTagSettings.asset` and define `State.Alive`, `State.Debuff.Burning`, and `State.Debuff.Stunned`.

## Load once; reuse at runtime

```csharp
using GameplayTags;
using UnityEngine;

public sealed class DenseTagsExample : MonoBehaviour
{
    [SerializeField] private GameplayTagContainer initialTags = new GameplayTagContainer();
    [SerializeField] private GameplayTagQuery activation = new GameplayTagQuery();
    private RuntimeTagSet owned;
    private FrozenGameplayTagQuery matcher;

    private void Awake()
    {
        TagRegistry registry = GameplayTagManager.CurrentRegistry;
        owned = initialTags.ToRuntime(registry, 1);
        matcher = activation.Freeze(registry);
    }

    public bool CanActivate() => matcher.Matches(owned);
}
```

Any positive capacity reserves the entire Dense buffer once. `EnsureCapacity(1)` does not reserve one tag-sized array. After preparation, normal operations do not allocate. A zero-capacity empty object allocates its bitmap on first addition; queries never allocate or repair state.

## Caller-owned results

```csharp
using GameplayTags;
using UnityEngine;

public sealed class DenseUnionExample : MonoBehaviour
{
    private RuntimeTagSet owned;
    private RuntimeTagSet buffs;
    private RuntimeTagSet output;

    private void Awake()
    {
        TagRegistry registry = GameplayTagManager.CurrentRegistry;
        owned = new RuntimeTagSet(registry, 1);
        buffs = new RuntimeTagSet(registry, 1);
        output = new RuntimeTagSet(registry, 1);
        owned.AddTag(registry.Resolve("State.Alive"));
        buffs.AddTag(registry.Resolve("State.Debuff.Burning"));
    }

    public int CombinedCount()
    {
        RuntimeTagSet.UnionInto(owned, buffs, output);
        return output.Count;
    }
}
```

Union and exact intersection may overwrite either input. Hierarchical filtering may overwrite its source, but not a separate condition set. All runtime operands must be non-null and belong to the same registry snapshot. Unknown names fail at loading; None is not a member. An empty All condition is true and an empty Any condition is false. An empty source query is false.

`new RuntimeTagSet(source)` and `Union(left,right)` create independent objects and include allocation costs. `CopyFrom` overwrites existing members and keeps prepared capacity; `Clear` keeps capacity. A new empty copy does not copy unused reservation.

## Migration and performance

Remove `TagSetStorage` arguments and `.Storage` calls: the type no longer exists. Names, redirects, generated registry-bound binding instances and `Freeze(registry)` remain. Never persist RuntimeIndex. Old snapshots remain valid while referenced; different scopes cannot be mixed. Enumeration is DFS ID order, not ordinal name order. Export/sort names explicitly for UI or persistence.

The algorithms are occupancy-summary successor search, set-bit iteration, portable SWAR population count, and direct word operations. A linear-versus-indexed *kernel* choice never changes member storage. It is not a claim that Dense dominates sparse arrays for every universe or allocation pattern.

Run `Audit~/dense_audit.py` with the exported pinned references. The workflow compares unchanged Z main, Z optimize, Alex-Rachel, the preceding Auto implementation, its forced Dense path, and this Dense-only candidate. It retains the original 100-case matrix and adds a larger registry and prepared workloads. Native tests are compiled by the managed facade, not executed by it.
