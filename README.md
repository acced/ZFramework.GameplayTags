# ZFramework Gameplay Tags — integer runtime

[简体中文](README_CN.md) · [Architecture and migration](Documentation~/RUNTIME_INDEX.md) · [Native release requirements](Documentation~/RELEASE.md)

Version **3.0.0-pre.1** is a breaking structural preview, not an approved stable release. Unity 2021.3+ / C# 9 / .NET Standard 2.1 are the declared targets. Native Editor and IL2CPP results must be obtained separately; managed CI uses explicitly labelled API facades.

## Install and configure

Use Unity Package Manager **Add package from disk** and select `package.json`, or select the `refactor/runtime-index-bitset-20260920` Git branch. Reference the `GameplayTags` assembly from your game assembly.

Create settings with **Tools → ZFramework → Gameplay Tags → Create Settings**. Keep the default asset at `Assets/Resources/GameplayTags/GameplayTagSettings.asset`. Define tags in the Manager, or use the CSV editor. Existing serialized names, Inspector drawers, redirects, restricted sources, validation, Undo and code-generation controls remain authoring features.

## Separate authoring from execution

`GameplayTag` and `GameplayTagContainer` contain **serialized names**. They are no longer the combat set. Resolve once into `RuntimeTag` and `RuntimeTagSet`, and freeze query definitions against the same immutable `TagRegistry`.

```csharp
using GameplayTags;
using UnityEngine;

public sealed class RuntimeTagExample : MonoBehaviour
{
    [SerializeField] private GameplayTagContainer initialTags = new GameplayTagContainer();
    [SerializeField] private GameplayTagQuery activation = new GameplayTagQuery();
    private RuntimeTagSet owned;
    private FrozenGameplayTagQuery matcher;

    private void Awake()
    {
        TagRegistry registry = GameplayTagManager.CurrentRegistry;
        owned = initialTags.ToRuntime(registry, 32);
        matcher = activation.Freeze(registry);
    }

    public bool CanActivate() => matcher.Matches(owned);
    public bool AddState(RuntimeTag tag) => owned.AddTag(tag);
    public bool RemoveState(RuntimeTag tag) => owned.RemoveTag(tag);
}
```

Populate the two authoring fields in the Inspector. An empty source query evaluates to false. Prepare the object before invoking its runtime methods.

A rule shared by many units should be frozen once by the context that loads the definition:

```csharp
using GameplayTags;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Skill rule")]
public sealed class SharedSkillRule : ScriptableObject
{
    [SerializeField] private GameplayTagQuery conditions = new GameplayTagQuery();

    public FrozenGameplayTagQuery Prepare(TagRegistry registry)
    {
        return conditions.Freeze(registry);
    }
}
```

The caller owns and shares the returned matcher. Changing the definition does not change already prepared matchers.

## Runtime operations

Resolve `registry.Resolve("State.Debuff.Burning")` at loading time and retain that scoped handle. `set.HasTagExact(handle)` tests an explicit member; `set.HasTag(parent)` tests the parent or any descendant. Only explicit members are stored. `Count` always counts explicit members.

`RuntimeTagSet.UnionInto(left, right, output)` and `IntersectionExactInto` overwrite caller-owned output and allow either input as output. `FilterInto` is hierarchical: it allows the source as output but rejects a different condition set as output. `CopyFrom`, `AppendTags` and `RemoveTags` preserve storage mode and never modify another input. Default `RuntimeTag.None` is not a member; foreign non-default handles/sets/queries throw before mutating output. Runtime set arguments must be non-null.

`Union` and `IntersectionExact` allocate independent results. A copy constructor performs a real independent copy, not copy-on-write.

## Storage and zero allocation

A set owns **one** sorted `int[]` or one `ulong[]`, never both live representations. `TagSetStorage.Auto` selects at construction using expected capacity and data-buffer size. Sparse storage grows amortized when necessary; dense storage allocates the bounded registry bitmap once. `EnsureCapacity` never silently switches representation. To convert, explicitly create another set in the desired mode and `CopyFrom` it.

Prepared handles, frozen queries and sufficiently sized output/member buffers support allocation-free runtime queries and mutations. Initialization, definition conversion, `Freeze`, growth and allocating convenience APIs are outside that promise. Into never resizes solely because an input-count upper bound is larger than an already sufficient actual-result capacity. Popcount/cardinality work is included in mutations, not postponed until after measurement.

Runtime enumeration is deterministic DFS-index order, not ordinal name order. There is no hidden O(1) k-th-member indexer for bitmaps. Use `foreach`; export and sort names explicitly for UI/persistence. `BufferBytes` describes member-array payload only, not complete managed/native memory.

## Snapshot lifetime and generated bindings

Rebuilding the manager publishes a new immutable snapshot. Old handles, sets and matchers retain their old snapshot and remain internally valid, but cannot be mixed with the new one. They never auto-rebind. Save stable names, not RuntimeIndex. Build/resolve on the main thread; mutate sets through one owner. Concurrent mutation is not supported.

Regenerate the C# API. Generated classes now contain **instance readonly RuntimeTag fields** and take a `TagRegistry` constructor argument, for example `var tags = new Game.GameplayTags(registry);`. Create one binding per context. No static runtime handles survive registry rebuilding unnoticed. Generated text is checked in full before builds.

## Reproduce the structural audit

Run from a full Git checkout with .NET 8 SDK and Python 3.12+, after exporting the three pinned references (the workflow does this):

```text
python3 Audit~/integer_audit.py --references artifacts --output artifacts/results --rounds 5
```

The integer-runtime workflow compiles production, editor, samples, exact README examples and test assembly boundaries; runs differential, lifetime, interval and allocation checks; then measures the unchanged original main, previous optimize, pinned Alex-Rachel runtime and this candidate using identical inputs. Raw samples, failed rows, member-buffer costs, source hashes and the exact source ZIP remain in the artifact. Forced sparse/dense candidate runs supplement, not replace, Auto results. A managed winner is not Unity/IL2CPP release approval.

The older `run.py` and performance fixtures are historical tools for the 2.x name-container API; this branch uses `integer_audit.py`. The native runner remains `unity_acceptance.py`; run the new source tests in a marked disposable Unity project and collect matching device evidence. Do not reuse 2.x native acceptance evidence.

[MIT license](LICENSE). Algorithm references and explicit tradeoffs are recorded in [RUNTIME_INDEX.md](Documentation~/RUNTIME_INDEX.md).
