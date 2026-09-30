# ZFramework Gameplay Tags — packed bitmap candidate

[简体中文](README_CN.md) · [Architecture and costs](Documentation~/PACKED_BITMAP.md)

This branch starts again from the immutable-registry/Auto implementation at `84729cc`. It does **not** build on the full-universe Dense-only branch. It is a prerelease, not a stable release or a claim of universal performance leadership.

## Runtime model

`GameplayTag` and `GameplayTagContainer` remain serialized authoring names. Resolve them into registry-scoped `RuntimeTag` and `RuntimeTagSet` objects at loading. `TagRegistry`, DFS subtree intervals, editable query validation and frozen-query execution retain the previous contracts.

All runtime membership is now bitmap data. Nonzero 64-bit words are packed behind 4096-ID block directories. Each directory has a bitmap and a rank-based offset; absent regions allocate no member storage. A single member word fits inline. There is no `TagSetStorage`, Sparse member-ID array, selector, pool, shared mutable result, or lazy name resolution.

This is a **compressed/block bitmap**, not a physically full-universe Dense array. The directory holds block addresses, not member IDs. The implementation uses a sorted directory of occupied blocks rather than an object per recursive tree node. Its lookup cost is not the O(1) direct addressing of a full bitmap.

## Unity installation

Install this branch as a local UPM package by selecting its `package.json`. Minimum declared version remains Unity 2021.3; native Unity and IL2CPP must be validated independently of the .NET audit. Keep all `.meta` files.

Create settings through Tools → ZFramework → Gameplay Tags. Existing configuration, redirect, CSV and Inspector workflows remain. Generated tags are instance bindings constructed with a registry, not static integer handles.

## Load once, then operate

```csharp
using GameplayTags;
using UnityEngine;

public sealed class CharacterTagOwner : MonoBehaviour
{
    [SerializeField] private GameplayTagContainer definition = new GameplayTagContainer();
    public RuntimeTagSet Tags { get; private set; }

    private void Awake()
    {
        TagRegistry registry = GameplayTagManager.CurrentRegistry;
        Tags = definition.ToRuntime(registry, 128);
    }
}
```

A capacity of 128 reserves enough directory and word slots for **any distribution** of up to 128 members. It is not a promise limited to adjacent IDs.

```csharp
using GameplayTags;
using UnityEngine;

public sealed class TagCondition : MonoBehaviour
{
    [SerializeField] private GameplayTagQuery definition = new GameplayTagQuery();
    private FrozenGameplayTagQuery matcher;

    private void Awake()
    {
        matcher = definition.Freeze(GameplayTagManager.CurrentRegistry);
    }

    public bool Matches(RuntimeTagSet owned)
    {
        return matcher.Matches(owned);
    }
}
```

Resolve a runtime handle once with `registry.Resolve("State.Debuff.Burning")`; reuse it with `AddTag`, `RemoveTag`, `HasTagExact` and `HasTag`. A default handle does not match. A handle from another registry is rejected even if its name is identical.

## Independent results and prepared outputs

`new RuntimeTagSet(source)` and `RuntimeTagSet.Union(left, right)` create independent mutable results. Their allocation is proportional to occupied bitmap words/directories, not registry size. Copies retain the contents, not unused source reserve.

`CopyFrom`, `UnionInto`, `IntersectionExactInto`, `AppendTags` and `RemoveTags` reuse owned buffers. `UnionInto` and exact intersection support either input as output. Hierarchical `FilterInto` may overwrite its source but not a separate condition set. All inputs/results must belong to the same registry. Count is accurate immediately.

`EnsureCapacity(n)` reserves arbitrary placement; `ReservedMemberCapacity` exposes the guaranteed placement-independent reserve. `Capacity` is at least Count but can additionally reflect already clustered contents. Do not use `Capacity` instead of `EnsureCapacity` when changing the planned member distribution. Resizing is amortized; clear and deletion keep owned buffers. Prepared operations allocate zero only when enough word and directory capacity was reserved.

Enumeration is ascending DFS ID order. Name sorting/persistence remains an explicit boundary operation. Mutation during enumeration and concurrent mutation are not supported.

## Audit

The workflow exports the complete checked source and fixed references, compiles real C# against labelled Unity facades, executes correctness/ownership/rank/positive-allocation checks and compares identical inputs. Fresh allocation remains inside fresh-result timing; prepared results are separate. All non-winning cases and raw samples are retained.

Run `python3 Audit~/packed_audit.py --references artifacts/references --output artifacts/results --rounds 3` after exporting the pinned references. `Audit~/packed_compat.py` ports the previous semantic suite to one representation and compiles exact documentation, generated bindings and assembly boundaries. Its adaptation log is test-only; production source is never rewritten by the audit.

A green managed audit does not approve native Unity, mobile devices, release, or the goal of winning every workload. Older audit scripts and historical design documents remain for reference, not acceptance of the new representation.

License: [MIT](LICENSE).
