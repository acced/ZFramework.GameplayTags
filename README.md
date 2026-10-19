# ZFramework Gameplay Tags

**[EN · English](#en) | [CN · 简体中文](#cn)**

<a name="en"></a>
## English

**Give character states, ability requirements, and stacking effects a shared language.**

ZFramework Gameplay Tags is a C# library for Unity. It lets your game describe *what is true* about an actor or item, then ask questions about those facts without knowing which system supplied them.

A character can be `State.Burning`, carry `Equipment.Weapon.Staff`, and have `Ability.Cast` at the same time. Combat adds the states, abilities check their requirements, and the UI listens for changes. Each system uses the same tags.

[Why tags?](#why-tags) · [Install](#installation) · [Quick start](#quick-start) · [Ability rules](#queries) · [Stacking effects](#counts) · [Notifications](#events) · [Unity integration](#unity-and-serialization) · [API reference](#api-map)

<a name="why-tags"></a>
### What does this help with?

Consider three everyday gameplay problems:

| In your game | With Gameplay Tags |
| --- | --- |
| A skill requires a staff and casting ability, but must be blocked by crowd control. | Build required and forbidden tag sets, then evaluate one rule. |
| Two sources apply burning. Removing one must not cancel the other. | Count each contribution; burning disappears only when its count reaches zero. |
| Equipment and temporary effects both grant the same ability. | Keep their contributions in separate child containers and query the actor's combined state. |

Tags also have a hierarchy. `State.Control.Frozen` and `State.Control.Silenced` both match `State.Control`, so a rule can block an entire category without listing every effect.

This is a building block, not an ability framework. It does not apply damage, advance buff timers, or make AI decisions. Your systems own those behaviors; the library gives them a way to describe and query state. For a single independent on/off value, a `bool` is still a good choice.

<a name="installation"></a>
### Install

The Unity package targets **Unity 2022.3+**. In **Window → Package Manager**, choose **+ → Add package from git URL** and paste:

```text
https://github.com/acced/ZFramework.GameplayTags.git#main
```

For a reproducible project, replace `main` with the full commit SHA you want to use. With a local checkout, choose **Add package from disk** and select `package.json` instead.

If your scripts use an assembly definition, add a reference to **`ZFramework.GameplayTags`**. The namespace is **`GameplayTags`**. Do not install another package defining these same types alongside it.

<details>
<summary>Using the core outside Unity</summary>

Reference the included `GameplayTags.Runtime.csproj` from a compatible C# project, or build it directly:

```bash
dotnet build GameplayTags.Runtime.csproj -c Release
```

The standalone project targets .NET Standard 2.1 / C# 9 and excludes the Unity component and Editor integration. The core does not require a source generator.

</details>

<a name="quick-start"></a>
### Quick start: put a character into a state

First, declare the names your game uses. Create **`GameTags.cs`**:

```csharp
using GameplayTags;

[assembly: GameplayTagAttribute("State.Burning")]
[assembly: GameplayTagAttribute("State.Control.Frozen")]
[assembly: GameplayTagAttribute("State.Control.Silenced")]
[assembly: GameplayTagAttribute("Ability.Cast")]
[assembly: GameplayTagAttribute("Equipment.Weapon.Staff")]

public static class GameTags
{
    static GameTags() { }
    public static readonly GameplayTag State = GameplayTagManager.RequestTag("State");
    public static readonly GameplayTag Control = GameplayTagManager.RequestTag("State.Control");
    public static readonly GameplayTag Burning = GameplayTagManager.RequestTag("State.Burning");
    public static readonly GameplayTag Frozen = GameplayTagManager.RequestTag("State.Control.Frozen");
    public static readonly GameplayTag Silenced = GameplayTagManager.RequestTag("State.Control.Silenced");
    public static readonly GameplayTag Cast = GameplayTagManager.RequestTag("Ability.Cast");
    public static readonly GameplayTag Staff = GameplayTagManager.RequestTag("Equipment.Weapon.Staff");
}
```

Parent names such as `State`, `State.Control`, and `Equipment.Weapon` are registered automatically. `GameTags` is an ordinary class you own: it resolves each name once and keeps the handle for later use.

Now create **`CharacterTagsExample.cs`**, attach it to a GameObject, and enter Play Mode:

```csharp
using GameplayTags;
using UnityEngine;

public sealed class CharacterTagsExample : MonoBehaviour
{
    private GameplayTagContainer states;

    private void Awake()
    {
        GameplayTagManager.InitializeIfNeeded();
        states = new GameplayTagContainer();
        states.AddTag(GameTags.Burning);

        Debug.Log(states.HasTag(GameTags.State));        // True: burning is a state.
        Debug.Log(states.HasTagExact(GameTags.State));   // False: State was not added itself.
        Debug.Log(states.HasTagExact(GameTags.Burning)); // True.

        states.AddTag(GameTags.Frozen);
        Debug.Log(!states.HasTag(GameTags.Control));     // False: crowd control is active.

        states.RemoveTag(GameTags.Frozen);
        Debug.Log(!states.HasTag(GameTags.Control));     // True: no crowd-control tag remains.
    }
}
```

This example creates its container at runtime. For fields configured in the Inspector, see [Unity integration](#unity-and-serialization). The following snippets belong in your methods and use the same `GameTags` class.

<a name="tag-semantics"></a>
<a name="sets"></a>
### Two questions: “is it in this category?” and “was this tag added?”

Adding `State.Control.Frozen` gives you:

```text
State                       ← matches HasTag
└── Control                 ← matches HasTag
    └── Frozen              ← explicitly added; also matches HasTagExact
```

Use `HasTag(GameTags.Control)` to ask whether *any* crowd-control state is present. Use `HasTagExact(GameTags.Frozen)` when the exact effect matters. A parent does not imply its children: adding `State.Control` alone does not make `State.Control.Frozen` present.

An ordinary `GameplayTagContainer` is a **set**. Adding the same tag twice still gives one explicit member. `TryAddTag` and `TryRemoveTag` report whether membership changed; use a [counted container](#counts) when multiple sources can own the same tag.

`ExplicitTagCount` counts what you added. `TagCount` also includes the implied ancestors. A container holding only `State.Control.Frozen` therefore has **1 explicit tag and 3 total tags**. Removing a merely implicit parent does not remove its children; remove the effect you actually added.

<a name="queries"></a>
### Recipe 1: decide whether a character can cast

A skill requires a staff and casting ability, and forbids crowd control. Build its rule when the skill loads, then reuse it:

```csharp
var castingRule = GameplayTagRequirements.Create(
    GameTags.Control, GameTags.Cast, GameTags.Staff);

var actor = new GameplayTagContainer();
actor.AddTag(GameTags.Cast);
actor.AddTag(GameTags.Staff);
UnityEngine.Debug.Log(castingRule.Matches(actor)); // True.
actor.AddTag(GameTags.Silenced);
UnityEngine.Debug.Log(castingRule.Matches(actor)); // False: silence belongs to Control.
```

`Create(forbidden, required, additionalRequired)` needs no temporary condition containers. For larger rules, use `new GameplayTagRequirements(forbiddenTags, requiredTags)` with arrays or spans of resolved tags. Construction removes duplicate/implied conditions and identifies contradictions; up to four effective conditions stay inline.

A rule is an **immutable runtime snapshot**: it does not retain your input arrays. Rebuild it when configuration changes; each match still reads the actor's current state. The old constructor taking two live containers is no longer available. Rules hold runtime IDs, so save names/configuration and reconstruct rules after loading rather than serializing the rule itself.

Simple checks remain available as `HasAny(query)` and `HasAll(query)`, or `HasAnyExact`/`HasAllExact` for exact conditions. An empty condition set satisfies `HasAll`, but not `HasAny`.

Equipment and temporary effects can jointly satisfy a rule without allocating a union:

```csharp
var equipment = new GameplayTagContainer();
equipment.AddTag(GameTags.Staff);
var effects = new GameplayTagContainer();
effects.AddTag(GameTags.Cast);
bool canCast = castingRule.Matches(equipment, effects); // True.
```

### Evaluate shared skill conditions together

Share the rule group across actors, and keep a separate cache for each actor:

```csharp
var rules = new GameplayTagRuleSet(new[]
{
    castingRule,
    new GameplayTagRequirements(System.Array.Empty<GameplayTag>(), new[] { GameTags.Burning })
});
var actorCache = rules.CreateCache();
System.ReadOnlySpan<bool> available = actorCache.Evaluate(actor);
bool canUseFirstSkill = available[0];
```

The group queries each distinct condition once. An unchanged presence version lets the cache reuse its results; after a change it refreshes distinct conditions and recomputes affected rules. A stack-count change that leaves presence unchanged need not invalidate presence-only rules. The container version is necessary cache state, not an input-validation layer.

Without a cache, use `rules.Evaluate(actor, resultSpan)` with caller-owned space of at least `rules.Count` elements. The cached result span is a borrowed view overwritten by the next evaluation; do not share a cache concurrently. Two holders also work: `actorCache.Evaluate(equipment, effects)`. Create groups and caches outside per-frame loops.

<a name="counts"></a>
### Example: two fires, one burning state

A set cannot distinguish two owners of the same tag. A `GameplayTagCountContainer` can:

```csharp
var effects = new GameplayTagCountContainer();

effects.AddTag(GameTags.Burning); // A fireball applies burning.
effects.AddTag(GameTags.Burning); // A fire zone applies burning too.
UnityEngine.Debug.Log(effects.GetExplicitTagCount(GameTags.Burning)); // 2.

effects.RemoveTag(GameTags.Burning); // The fireball effect ends.
UnityEngine.Debug.Log(effects.HasTag(GameTags.Burning)); // True: the zone still contributes.

effects.RemoveTag(GameTags.Burning); // The character leaves the zone.
UnityEngine.Debug.Log(effects.HasTag(GameTags.Burning)); // False.
```

Use `AddTag(tag, amount)` / `RemoveTag(tag, amount)` for several occurrences at once. `GetExplicitTagCount(tag)` returns that tag's own count; `GetTagCount(tag)` includes descendant contributions. For example, two burning and one frozen contribution give `State` a total count of three.

The caller owns effect duration and removal. The container counts contributions; it does not identify the contributing effect objects or expire them automatically. Remove only the occurrences your system owns.

Bulk `AddTags(source)` adds **one occurrence per distinct explicit member**, not the source's multiplicity. `RemoveTags(source)` similarly removes one each. `Clear()` removes all counts but leaves subscriptions in place.

<a name="events"></a>
### React to state changes instead of checking every frame

A buff icon or animation layer often needs to know when a state appears or disappears. Subscribe to a counted container:

```csharp
var effects = new GameplayTagCountContainer();
OnTagCountChangedDelegate onBurningChanged = (tag, count) =>
    UnityEngine.Debug.Log($"Burning stacks: {count}");

effects.RegisterTagEventCallback(
    GameTags.Burning, GameplayTagEventType.AnyCountChange, onBurningChanged);

effects.AddTag(GameTags.Burning);    // Callback: 1.
effects.AddTag(GameTags.Burning);    // Callback: 2.
effects.RemoveTag(GameTags.Burning); // Callback: 1.
effects.Clear();                    // Callback: 0.

effects.RemoveTagEventCallback(
    GameTags.Burning, GameplayTagEventType.AnyCountChange, onBurningChanged);
```

Choose `NewOrRemoved` when only presence matters: it fires on **0 → positive** and **positive → 0**, not on every stack change. Subscribe to a parent such as `State.Control` to observe the whole category. Direct event registration does not emit an initial value.

For a group of presence subscriptions, `GameplayTagContainerBinds` also reports the current state immediately:

```csharp
var effects = new GameplayTagCountContainer();
using (var bindings = new GameplayTagContainerBinds(effects))
{
    bindings.Bind(GameTags.Burning, present => UnityEngine.Debug.Log(present)); // False now.
    effects.AddTag(GameTags.Burning); // True.
} // Unsubscribes this group; the effects container is still yours.
```

In a UI component, keep the binding group for as long as the view is active and dispose it when the view closes. A `using` block is suitable for a temporary scope, not a persistent UI subscription. Global notifications are available through `OnAnyTagCountChange` and `OnAnyTagNewOrRemove`; unsubscribe from those separately.

Callbacks run after the state change has been committed. Reentrant changes queue their notifications; a thrown callback exception propagates and does not undo the state change.

<a name="parent-containers"></a>
### Let equipment and effects contribute independently

Use `GameplayTagHierarchicalContainer` when each subsystem should own its tags while the actor exposes their combined counts:

```csharp
var actor = new GameplayTagCountContainer();
var equipment = new GameplayTagHierarchicalContainer { Parent = actor };
var aura = new GameplayTagHierarchicalContainer { Parent = actor };

equipment.AddTag(GameTags.Cast);
aura.AddTag(GameTags.Cast);
UnityEngine.Debug.Log(actor.GetExplicitTagCount(GameTags.Cast)); // 2.

equipment.Parent = null; // Unequip: withdraw equipment's existing contributions.
UnityEngine.Debug.Log(actor.GetExplicitTagCount(GameTags.Cast)); // 1: the aura remains.

aura.Clear();           // Withdraw the aura's contribution as well.
aura.Parent = null;
```

This is **child-to-parent aggregation**, not inheritance from the parent. Detaching does not clear the child itself; attaching it again transfers its current contributions. Container parent links are separate from the dot-separated hierarchy of tag names.

Remove contributions through the child that owns them. Do not clear an aggregate parent while attached children still believe their contributions are present. Keep parent links acyclic.

<a name="set-algebra"></a>
### Combine, filter, and reuse tag sets

Imagine comparing two characters' states:

```csharp
var left = new GameplayTagContainer();
left.AddTag(GameTags.Burning);
left.AddTag(GameTags.Cast);
var right = new GameplayTagContainer();
right.AddTag(GameTags.Frozen);
right.AddTag(GameTags.Cast);

var combined = GameplayTagContainer.Union(left, right);       // Burning, Frozen, Cast.
var shared = GameplayTagContainer.Intersection(left, right); // Cast only.

var remaining = left.Clone(); // Independent storage.
remaining.RemoveTags(right);  // Burning only.
```

Operations work on **explicit members** and derive ancestors from the result. Intersecting `{State.Burning}` with `{State.Control.Frozen}` is empty: their common ancestor is not a shared explicit member. Counted inputs contribute membership, not repeated copies of a tag.

For repeated work, keep an output container rather than creating a new one every time:

```csharp
var output = new GameplayTagContainer(32);
GameplayTagContainer.Copy(output, left);               // Replace output with left.
output.AddTags(right);                                 // Merge right into it.
GameplayTagContainer.Union(output, left, right);      // Replaces output; either input may be output.
GameplayTagContainer.Intersection(output, left, right); // Replace output with the intersection.
output.Clear();                                       // Keep capacity for the next use.
```

Here `output` is distinct from both inputs. If output is already one of the union inputs, append the other input directly instead of copying over it. Intersection supports output aliasing either input; `IntersectWith(other)` is its in-place form. Copying a set to itself and adding it to itself are no-ops; removing an ordinary set from itself clears it.

<a name="enumeration"></a>
### Read the members you actually need

```csharp
var states = new GameplayTagContainer();
states.AddTag(GameTags.Frozen);

foreach (GameplayTag tag in states.GetExplicitTags())
    UnityEngine.Debug.Log(tag.Name); // State.Control.Frozen.

foreach (GameplayTag tag in states)
    UnityEngine.Debug.Log(tag.Name); // State, State.Control, State.Control.Frozen; order unspecified.
```

Use **`GetExplicitTags()`** for authored effects or a list of active buffs. The default `foreach` also includes ancestors. Enumeration is unordered; do not change a container while enumerating it.

Sort at the display or export boundary when you need stable names:

```csharp
var names = new System.Collections.Generic.List<string>();
foreach (GameplayTag tag in states.GetExplicitTags()) names.Add(tag.Name);
names.Sort(System.StringComparer.Ordinal);
```

For UI updates, `current.GetDiffExplicitTags(previous, added, removed)` appends the difference to two lists. Clear reusable output lists before calling it. The parent/child extraction helpers also append rather than clear their output.

<a name="registration"></a>
### Loading tags from your own data

The quick start uses assembly declarations. A game with its own content pipeline can instead provide the full registry at startup:

```csharp
// An alternative to assembly discovery. Run before any lookup or GameTags field access.
GameplayTagManager.Initialize(
    new GameplayTagRegistration("State.Burning"),
    new GameplayTagRegistration("State.Control.Frozen"),
    new GameplayTagRegistration("State.Control.Silenced"),
    new GameplayTagRegistration("Ability.Cast"),
    new GameplayTagRegistration("Equipment.Weapon.Staff"));
```

Choose one approach. Explicit registration does not merge assembly declarations. Initialize once on the owning thread, before loading tagged assets, and keep that registry unchanged while handles, containers, subscriptions, or pooled objects remain alive. There is no lock or repeated-initialization guard.

Names are case-sensitive. Use `.` between levels and letters, digits, or underscores within each level. For imported data, the `out` lookup lets your application decide how to handle a missing declaration:

```csharp
string savedName = "State.Burning";
if (GameplayTagManager.RequestTag(savedName, out GameplayTag tag))
{
    var loaded = new GameplayTagContainer();
    loaded.AddTag(tag);
}
```

A missing, null, or empty name resolves to `None`. `GameplayTagUtility.IsValidName` and `ValidateName` are available to authoring tools; the runtime does not automatically validate declarations. Cache resolved tags rather than looking up strings on every frame.

<a name="unity-and-serialization"></a>
### Inspector fields and saved data

The package includes Inspector drawers for selecting registered tags. They edit names; they do not create declarations or manage a settings asset.

For a single serialized tag, resolve and store the handle after loading:

```csharp
using GameplayTags;
using UnityEngine;

public sealed class DamageTypeExample : MonoBehaviour
{
    [SerializeField] private GameplayTag damageType;

    private void Awake()
    {
        GameplayTagManager.InitializeIfNeeded();
        damageType = damageType.Resolve();
    }
}
```

Declare the selectable names first. `Resolve()` returns a struct: **assign its result**. Do the same after changing `m_Name` in your own code. Queries use the cached ID and will not repair an edited name for you.

A serialized **container** has a different loading requirement: its deserialization callback immediately rebuilds members. Initialize the registry **before loading those assets or scenes**, including in Editor tooling. An `Awake` method is not a universal solution for serialized containers, and Unity callbacks may run away from the main thread. See [Unity's serialization callback notes](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ISerializationCallbackReceiver.html).

`GameObjectGameplayTagContainer` provides a ready-made counted container on a GameObject. Its `m_PersistentTags` are added during `Awake`; obtain the runtime container from its `GameplayTagContainer` property. `gameObject.Bind()` gets or adds this component and creates a binding group. Coordinate initialization and binding lifetime with your components.

Save **names**, not `RuntimeIndex`. IDs belong to one registry and can change between builds. Ordinary containers serialize explicit names and derive ancestors on load; unknown names do not restore members. For counted containers, store names and amounts in your game's save data. This library does not provide automatic save migration or name redirects.

With Domain Reload disabled, static registries, cached handles, subscriptions, and pools can survive Play Mode transitions. Keep their registry consistent rather than initializing a replacement over live state. The older name-based package also needs explicit API and asset migration; it is not a drop-in upgrade to this integer version.

<a name="pooling"></a>
### Temporary work: choose your reuse policy

For work repeated every frame, keep a container and call `Clear()` after use. Use the thread-local pool when separate temporary scopes benefit from borrowing:

```csharp
using (GameplayTagContainerPool.Get(out GameplayTagContainer temporary))
{
    temporary.AddTag(GameTags.Burning);
    temporary.AddTag(GameTags.Cast);
}
```

The default is **unbounded retention**, so a large container is not silently discarded by a built-in 64 KiB threshold. Choose a policy at a loading boundary on the borrowing thread:

```csharp
// Limit object count, but not an individual container's capacity.
GameplayTagContainerPool.Configure(new GameplayTagContainerPool.RetentionPolicy(32));

// Or limit count, individual array payload and total array payload.
// These numbers are application examples, not library defaults.
GameplayTagContainerPool.Configure(new GameplayTagContainerPool.RetentionPolicy(
    maxCount: 64, maxContainerBytes: 256 * 1024, maxTotalBytes: 4L * 1024 * 1024));

// Or retain nothing, or restore unbounded retention.
GameplayTagContainerPool.Configure(GameplayTagContainerPool.RetentionPolicy.Disabled);
GameplayTagContainerPool.Configure(GameplayTagContainerPool.RetentionPolicy.Unbounded);
```

`Configure` drops the current thread's cached objects. `Clear()` also releases the pool array, but keeps the policy. Observe `RetainedCount` and `RetainedDataBytes`; bytes count container array payload only, excluding object/array headers and process-shared data. Budget comparisons implement the selected policy, not argument validation: supply valid, nonnegative limits.

Configuration is per-thread and does not propagate. Unbounded retention can retain peak memory; consider an application-owned long-lived container or a suitable budget for large workloads. Borrow and return on the same thread exactly once. Do not copy/dispose leases twice, dispose a default lease or access returned references. The pool does not track misuse.

<a name="player-registry"></a>
### Player registration: read the build, not the Editor's approximation

Assembly discovery and explicit `Initialize` remain available. To avoid discovery reflection at Player startup, enable the Editor generator. Mono.Cecil reads **the actual compiled Player DLL metadata** without loading or executing Player code. Target assembly selection, `#if !UNITY_EDITOR`, platform symbols, extra scripting defines and included managed plugins are taken from that build, not from the Editor AppDomain.

Choose **Tools → Gameplay Tags → Generate Player Registry**, then build normally. On the first build or after declaration changes, the post-script-compilation callback writes `Assets/GameplayTags.Generated.cs` and stops the current build. Let Unity import/compile the updated script, then build with the same configuration. The second pass checks both source and the **fingerprint in the compiled table**. Changing target/defines may require regeneration; an old table is not silently accepted.

This is intentionally a two-pass generation process: C# written after script compilation cannot be treated as already compiled. In batch mode, start another Unity build after the table is generated; do not blindly retry unrelated build errors. A custom pipeline can call `GameplayTagRegistryGenerator.GenerateFromPlayerAssemblies(paths)` with its complete actual Player managed-assembly set before linking/stripping. False means the updated table must be compiled. Without the output file, generation validation is disabled.

The generated table installs **no automatic startup callback**. Call `GameplayTags.Generated.GameplayTagRegistry.Initialize()` once from your boot sequence, before caching tags or loading serialized containers; do not combine it with another explicit initialization. In Editor this entry uses the Editor's own declarations; in Player it uses the generated table. An explicit static constructor on application cache classes avoids `beforefieldinit` eager initialization; their first access must still follow boot.

Direct `GameplayTagAttribute` declarations support constructor arguments and named `TagName`, `Description` and `Flags` overrides. Derived attributes can execute arbitrary code and cannot be evaluated safely from metadata: the build reports them instead of silently omitting them; use direct attributes or explicit registration. Assemblies loaded dynamically later are outside this Player build. Mono.Cecil is referenced only by the Editor assembly, not the runtime API. The callback adds build-time file/metadata work; actual Unity/IL2CPP integration still needs validation in your project.

<a name="caller-contract"></a>
### Keeping hot paths small

The runtime uses integer handles, packed storage, and a hash index for larger sets. It avoids putting name resolution or general validation in every query. The practical pattern is simple: **register at startup, resolve at loading boundaries, then reuse handles and containers.**

`EnsureCapacity(n)` reserves space for distinct members **including ancestors**, not just leaves. Concrete enumeration uses a struct enumerator; passing through `IEnumerable<GameplayTag>` can box it. New containers, growth, serialization, subscriptions, and first-use pool allocations still cost memory. This is managed C# code, not a Burst/Jobs `NativeContainer`.

The application supplies valid, non-`None` handles and non-null mutation operands, positive amounts, valid capacities, and nonoverflowing counts. Removal must not exceed the occurrences the caller owns. It also owns synchronization, parent-chain ownership, and pooled lifetimes. `Empty` is shared and must be treated as read-only. Invalid use is not a supported recovery path; the library does not add a defensive layer around it. Keep error handling and validation at your application's boundaries.

**Version note:** the current source is `2.1.0-rc.2-optimized.2`. It is not a claim of completed Unity/IL2CPP qualification or a universal performance win. Measure with your tag counts, hierarchy depth, and target backend before choosing capacity or pooling policies.

<a name="api-map"></a>
### Find the next API

| You want to… | Start here |
| --- | --- |
| Hold a set of states | [`GameplayTagContainer`](Runtime/GameplayTagContainer.cs) |
| Track stacked effects or subscribe to changes | [`GameplayTagCountContainer`](Runtime/GameplayTagCountContainer.cs) |
| Combine contributions from owned subsystems | [`GameplayTagHierarchicalContainer`](Runtime/GameplayTagHierarchicalContanier.cs) |
| Express required and forbidden conditions | [`GameplayTagRequirements`](Runtime/GameplayTagRequirements.cs) |
| Register names and cache handles | [`GameplayTagManager`](Runtime/GameplayTagManager.cs), [`GameplayTag`](Runtime/GameplayTag.cs) |
| Group subscriptions or borrow a scratch container | [`GameplayTagContainerBinds`](Runtime/GameplayTagContainerBinds.cs), [`GameplayTagContainerPool`](Runtime/GameplayTagContainerPool.cs) |

`GameplayTag` also exposes `Name`, `Label`, `Description`, `Flags`, `ParentTag`, `ParentTags`, `HierarchyTags`, and `ChildTags`. These describe the registry. `IsParentOf` / `IsChildOf` are strict: a tag is not its own ancestor. For members present in a container, use `GetParentTags`, `GetChildTags`, or their explicit-only variants.

The repository keeps the [runtime](Runtime), [Editor integration](Editor), and these guides together. License: [MIT](LICENSE).

[CN · 简体中文](#cn) · [Back to English](#en)

---

<a name="cn"></a>
## 简体中文

[EN · English](#en) | **CN · 简体中文**

**用同一套标签，描述角色状态、技能条件和 Buff 叠加。**

ZFramework Gameplay Tags 是一个面向 Unity 的 C# 游戏标签库。它让不同系统都能回答一个问题：**这个角色或物品，现在具备哪些状态和能力？**

角色可以同时处于 `State.Burning`，持有 `Equipment.Weapon.Staff`，并具备 `Ability.Cast`。战斗系统添加状态，技能系统检查条件，UI 监听变化。它们使用同一套标签，不必知道状态具体来自哪个系统。

[解决什么问题](#cn-why-tags) · [安装](#cn-installation) · [快速开始](#cn-quick-start) · [技能条件](#cn-queries) · [Buff 叠加](#cn-counts) · [变化通知](#cn-events) · [Unity 集成](#cn-unity-and-serialization) · [API 导航](#cn-api-map)

<a name="cn-why-tags"></a>
### 什么时候会用到它？

假设你正在做一个有技能、装备和 Buff 的游戏：

| 遇到的问题 | 用标签怎么处理 |
| --- | --- |
| 技能要求装备法杖、拥有施法能力，而且不能处于控制状态。 | 把“必须有”和“不能有”的标签组成一条规则。 |
| 火球和地面火焰同时造成燃烧，其中一个结束后，不应该直接清掉燃烧状态。 | 为每次贡献计数，最后一个来源移除后，状态才消失。 |
| 装备和光环都能赋予同一种能力，卸下装备不能把光环的效果一起删掉。 | 各自维护子容器，角色容器汇总它们的贡献。 |

标签还可以分层。`State.Control.Frozen` 和 `State.Control.Silenced` 都属于 `State.Control`。因此，“受到任意控制时不能施法”只需要检查父标签，不必把每一种控制效果都列出来。

它是一块基础积木，不是一整套技能框架。伤害怎么算、Buff 持续多久、AI 如何决策，仍由你的游戏负责；标签库只处理状态的表达、组合和查询。如果只是一个独立开关，直接用 `bool` 就很好。

<a name="cn-installation"></a>
### 安装

Unity 包目标为 **Unity 2022.3 及以上**。打开 **Window → Package Manager**，选择 **+ → Add package from git URL**，粘贴：

```text
https://github.com/acced/ZFramework.GameplayTags.git#main
```

团队项目需要固定版本时，把 `main` 换成你要使用的完整提交号。本地源码则通过 **Add package from disk** 选择 `package.json`。

使用 `.asmdef` 的业务程序集需要引用 **`ZFramework.GameplayTags`**，代码命名空间是 **`GameplayTags`**。不要同时安装另一份定义同名类型的包。

<details>
<summary>在 Unity 之外使用核心库</summary>

在兼容的 C# 工程中引用附带的 `GameplayTags.Runtime.csproj`，或直接构建：

```bash
dotnet build GameplayTags.Runtime.csproj -c Release
```

独立工程目标为 .NET Standard 2.1 / C# 9，不包含 Unity 组件和 Editor 集成，也不需要源码生成器。

</details>

<a name="cn-quick-start"></a>
### 快速开始：给角色添加一个状态

先声明游戏会用到的标签。创建 **`GameTags.cs`**：

```csharp
using GameplayTags;

[assembly: GameplayTagAttribute("State.Burning")]
[assembly: GameplayTagAttribute("State.Control.Frozen")]
[assembly: GameplayTagAttribute("State.Control.Silenced")]
[assembly: GameplayTagAttribute("Ability.Cast")]
[assembly: GameplayTagAttribute("Equipment.Weapon.Staff")]

public static class GameTags
{
    static GameTags() { }
    public static readonly GameplayTag State = GameplayTagManager.RequestTag("State");
    public static readonly GameplayTag Control = GameplayTagManager.RequestTag("State.Control");
    public static readonly GameplayTag Burning = GameplayTagManager.RequestTag("State.Burning");
    public static readonly GameplayTag Frozen = GameplayTagManager.RequestTag("State.Control.Frozen");
    public static readonly GameplayTag Silenced = GameplayTagManager.RequestTag("State.Control.Silenced");
    public static readonly GameplayTag Cast = GameplayTagManager.RequestTag("Ability.Cast");
    public static readonly GameplayTag Staff = GameplayTagManager.RequestTag("Equipment.Weapon.Staff");
}
```

`State`、`State.Control`、`Equipment.Weapon` 等父标签会自动注册。这里的 `GameTags` 就是你自己维护的普通类：解析一次名称，把句柄留给后续代码使用。

再创建 **`CharacterTagsExample.cs`**，挂到 GameObject 上，进入 Play Mode：

```csharp
using GameplayTags;
using UnityEngine;

public sealed class CharacterTagsExample : MonoBehaviour
{
    private GameplayTagContainer states;

    private void Awake()
    {
        GameplayTagManager.InitializeIfNeeded();
        states = new GameplayTagContainer();
        states.AddTag(GameTags.Burning);

        Debug.Log(states.HasTag(GameTags.State));        // True：燃烧属于 State。
        Debug.Log(states.HasTagExact(GameTags.State));   // False：没有直接加入 State。
        Debug.Log(states.HasTagExact(GameTags.Burning)); // True。

        states.AddTag(GameTags.Frozen);
        Debug.Log(!states.HasTag(GameTags.Control));     // False：还不能行动。

        states.RemoveTag(GameTags.Frozen);
        Debug.Log(!states.HasTag(GameTags.Control));     // True：已经没有控制状态。
    }
}
```

这个例子在运行时创建容器。在 Inspector 中配置标签字段的用法见 [Unity 集成](#cn-unity-and-serialization)。后面的代码片段放进业务方法即可，沿用上面的 `GameTags`。

<a name="cn-tag-semantics"></a>
<a name="cn-sets"></a>
### 分清两个问题：“属于这一类吗？”和“真的加过它吗？”

添加 `State.Control.Frozen` 后，可以这样理解容器：

```text
State                       ← HasTag 能匹配
└── Control                 ← HasTag 能匹配
    └── Frozen              ← 直接添加的标签，HasTagExact 也能匹配
```

`HasTag(GameTags.Control)` 问的是“是否处于任意控制状态”；`HasTagExact(GameTags.Frozen)` 问的是“是否明确存在冰冻”。这个关系不是双向的：只添加 `State.Control`，并不代表角色也处于冰冻。

普通 `GameplayTagContainer` 是一个**集合**，同一个标签加两次仍然只有一份。`TryAddTag`、`TryRemoveTag` 返回成员是否发生变化；多个系统需要同时持有同一标签时，改用[计数容器](#cn-counts)。

`ExplicitTagCount` 统计直接添加的标签，`TagCount` 还包括它们的祖先。只有 `State.Control.Frozen` 的容器，因此是 **1 个显式标签、3 个总标签**。删除一个仅由后代带来的父标签不会清掉后代；应该删除实际添加的那个效果。

<a name="cn-queries"></a>
### 用法一：判断角色能不能施法

技能要求“有法杖、能施法”，同时禁止控制状态。规则在技能加载时创建一次，之后反复使用：

```csharp
var castingRule = GameplayTagRequirements.Create(
    GameTags.Control, GameTags.Cast, GameTags.Staff);

var actor = new GameplayTagContainer();
actor.AddTag(GameTags.Cast);
actor.AddTag(GameTags.Staff);
UnityEngine.Debug.Log(castingRule.Matches(actor)); // True。
actor.AddTag(GameTags.Silenced);
UnityEngine.Debug.Log(castingRule.Matches(actor)); // False：沉默属于 Control。
```

`Create(禁止标签, 必需标签, 另一必需标签)` 不需要临时条件容器。条件较多时，使用 `new GameplayTagRequirements(forbiddenTags, requiredTags)`，两个参数是已解析标签的数组或 span。构造时去除重复和被包含的条件，并识别矛盾；最多四个有效条件内联保存。

规则是**不可变的运行时快照**：输入数组不会被保留，配置改变时重新创建规则；匹配时读取的是角色此刻的状态。旧版接受两个容器、跟随容器修改的构造器已移除。规则保存运行时 ID，不直接序列化；存档保存名称，加载后重新创建规则。

简单判断仍可以用 `HasAny(query)`、`HasAll(query)`；精确条件用 `HasAnyExact`、`HasAllExact`。空条件的 `HasAll` 为 true，`HasAny` 为 false。

装备和临时效果分开放，也不需要先分配并集：

```csharp
var equipment = new GameplayTagContainer();
equipment.AddTag(GameTags.Staff);
var effects = new GameplayTagContainer();
effects.AddTag(GameTags.Cast);
bool canCast = castingRule.Matches(equipment, effects); // True。
```

### 多个技能共享条件时，集中判断

把同一组技能规则共享给角色，每个角色持有自己的缓存：

```csharp
var rules = new GameplayTagRuleSet(new[]
{
    castingRule,
    new GameplayTagRequirements(System.Array.Empty<GameplayTag>(), new[] { GameTags.Burning })
});
var actorCache = rules.CreateCache();
System.ReadOnlySpan<bool> available = actorCache.Evaluate(actor);
bool canUseFirstSkill = available[0];
```

规则组每次只查询一次相同条件。缓存发现角色存在状态未变时直接复用结果；状态改变时刷新不同条件，只重算受影响的规则。层数改变但“有／没有”没变，不必重算存在性规则。容器的存在版本是缓存正确性所需的状态，不是参数防御。

不需要缓存时，用 `rules.Evaluate(actor, resultSpan)` 写入调用方提供的结果空间，长度至少为 `rules.Count`。缓存返回的 span 是借用视图，下次评估会覆盖；不要跨线程共享一个缓存。它也支持 `actorCache.Evaluate(equipment, effects)`。不要每帧重新创建规则组或缓存。

<a name="cn-counts"></a>
### 用法二：两个燃烧来源，互不误删

普通集合只能知道“有或没有”，计数容器能保留多个来源的贡献：

```csharp
var effects = new GameplayTagCountContainer();

effects.AddTag(GameTags.Burning); // 火球附加一次燃烧。
effects.AddTag(GameTags.Burning); // 地面火焰再附加一次。
UnityEngine.Debug.Log(effects.GetExplicitTagCount(GameTags.Burning)); // 2。

effects.RemoveTag(GameTags.Burning); // 火球效果结束。
UnityEngine.Debug.Log(effects.HasTag(GameTags.Burning)); // True：地面火焰还在。

effects.RemoveTag(GameTags.Burning); // 角色离开火焰区域。
UnityEngine.Debug.Log(effects.HasTag(GameTags.Burning)); // False。
```

一次处理多层时，使用 `AddTag(tag, amount)` 和 `RemoveTag(tag, amount)`。`GetExplicitTagCount(tag)` 是标签自身的次数，`GetTagCount(tag)` 还会累计后代贡献。例如，两次燃烧加一次冰冻，会让 `State` 的总次数变成三。

效果持续时间和移除时机仍由业务管理。容器只记录次数，不替你识别每个来源对象，也不会自动让 Buff 到期。各系统只应移除自己添加的次数。

批量 `AddTags(source)` 对来源的每个不同显式标签各加 **1 次**，不会照搬来源的叠加层数；`RemoveTags(source)` 同样各减一次。`Clear()` 清掉所有次数，但保留订阅。

<a name="cn-events"></a>
### 用法三：状态变了再更新 UI

燃烧图标、控制提示和动画层通常只关心状态变化，没有必要每帧重新查询。可以直接订阅计数容器：

```csharp
var effects = new GameplayTagCountContainer();
OnTagCountChangedDelegate onBurningChanged = (tag, count) =>
    UnityEngine.Debug.Log($"燃烧层数：{count}");

effects.RegisterTagEventCallback(
    GameTags.Burning, GameplayTagEventType.AnyCountChange, onBurningChanged);

effects.AddTag(GameTags.Burning);    // 回调：1。
effects.AddTag(GameTags.Burning);    // 回调：2。
effects.RemoveTag(GameTags.Burning); // 回调：1。
effects.Clear();                    // 回调：0。

effects.RemoveTagEventCallback(
    GameTags.Burning, GameplayTagEventType.AnyCountChange, onBurningChanged);
```

只关心“出现、消失”时，用 `NewOrRemoved`：它只在 **0 → 正数**和**正数 → 0** 时触发，不会每次叠层都通知。订阅 `State.Control` 这样的父标签，就能观察整个类别。直接注册事件不会立即推送当前值。

一组只关心存在性的监听，可以用 `GameplayTagContainerBinds` 管理，它还会在绑定时立即报告当前状态：

```csharp
var effects = new GameplayTagCountContainer();
using (var bindings = new GameplayTagContainerBinds(effects))
{
    bindings.Bind(GameTags.Burning, present => UnityEngine.Debug.Log(present)); // 现在是 False。
    effects.AddTag(GameTags.Burning); // True。
} // 取消这一组订阅，不释放 effects 容器。
```

实际 UI 中，让绑定组和界面一起存活，关闭界面时再 `Dispose()`。上面的 `using` 适合临时作用域，不适合长期显示的界面。全局通知还有 `OnAnyTagCountChange` 和 `OnAnyTagNewOrRemove`，需要分别取消订阅。

回调发生时，状态已经修改完成。回调中的再次修改会排队通知；回调抛出异常会向上传播，不会撤销已经完成的状态变化。

<a name="cn-parent-containers"></a>
### 用法四：装备与光环分别管理，角色统一查询

各子系统需要管理自己的贡献时，用 `GameplayTagHierarchicalContainer`：

```csharp
var actor = new GameplayTagCountContainer();
var equipment = new GameplayTagHierarchicalContainer { Parent = actor };
var aura = new GameplayTagHierarchicalContainer { Parent = actor };

equipment.AddTag(GameTags.Cast);
aura.AddTag(GameTags.Cast);
UnityEngine.Debug.Log(actor.GetExplicitTagCount(GameTags.Cast)); // 2。

equipment.Parent = null; // 卸下装备：撤销它在角色身上的已有贡献。
UnityEngine.Debug.Log(actor.GetExplicitTagCount(GameTags.Cast)); // 1：光环仍然有效。

aura.Clear();           // 再移除光环贡献。
aura.Parent = null;
```

这是**子容器向父容器汇总**，不是从父容器继承标签。解除挂接不会清空子容器本身，重新挂接时会转移它当前的贡献。容器之间的父子关系，与标签名称中的点分层级是两回事。

通过拥有贡献的子容器来移除效果。子容器仍然挂接、认为贡献还存在时，不要直接清空聚合父容器；父链也不能形成循环。

<a name="cn-set-algebra"></a>
### 合并、筛选和复用标签集合

例如，比较两个角色的状态：

```csharp
var left = new GameplayTagContainer();
left.AddTag(GameTags.Burning);
left.AddTag(GameTags.Cast);
var right = new GameplayTagContainer();
right.AddTag(GameTags.Frozen);
right.AddTag(GameTags.Cast);

var combined = GameplayTagContainer.Union(left, right);       // Burning、Frozen、Cast。
var shared = GameplayTagContainer.Intersection(left, right); // 只有 Cast。

var remaining = left.Clone(); // 独立的存储。
remaining.RemoveTags(right);  // 只剩 Burning。
```

集合运算针对**显式成员**，再从结果推导祖先。`{State.Burning}` 和 `{State.Control.Frozen}` 的交集为空，不会因为它们都属于 `State` 就产生一个显式 `State`。计数容器作为来源时，也只贡献标签是否存在，不复制重复次数。

需要频繁计算时，可以保留输出容器：

```csharp
var output = new GameplayTagContainer(32);
GameplayTagContainer.Copy(output, left);               // 用 left 替换 output。
output.AddTags(right);                                 // 把 right 合并进来。
GameplayTagContainer.Union(output, left, right);      // Replaces output; either input may be output.
GameplayTagContainer.Intersection(output, left, right); // 替换成交集。
output.Clear();                                       // 保留容量，下次继续用。
```

这里的 `output` 与两份输入不同。如果输出本来就是某个并集输入，直接追加另一份，不要先用复制覆盖掉它。交集允许输出与任一输入相同，也可以用 `IntersectWith(other)` 原地求交集。普通集合复制自己、添加自己都不改变内容，移除自己则会清空。

<a name="cn-enumeration"></a>
### 只读取你真正关心的成员

```csharp
var states = new GameplayTagContainer();
states.AddTag(GameTags.Frozen);

foreach (GameplayTag tag in states.GetExplicitTags())
    UnityEngine.Debug.Log(tag.Name); // State.Control.Frozen。

foreach (GameplayTag tag in states)
    UnityEngine.Debug.Log(tag.Name); // State、State.Control、State.Control.Frozen，顺序不保证。
```

展示实际添加的效果或 Buff 列表时，用 **`GetExplicitTags()`**；默认 `foreach` 还包含祖先。枚举是无序的，枚举期间不要修改容器。

UI 或导出文件需要稳定的名称顺序时，在输出环节排序：

```csharp
var names = new System.Collections.Generic.List<string>();
foreach (GameplayTag tag in states.GetExplicitTags()) names.Add(tag.Name);
names.Sort(System.StringComparer.Ordinal);
```

更新 UI 列表还可以用 `current.GetDiffExplicitTags(previous, added, removed)`，它会向两个列表追加新增项和移除项。复用输出列表时先清空；父子标签提取方法同样是追加，不会自动清空结果。

<a name="cn-registration"></a>
### 接入自己的配置和存档

快速开始使用程序集声明。如果游戏有自己的内容管线，也可以在启动时传入完整注册表：

```csharp
// 代替程序集发现；放在任何名称查询或 GameTags 字段访问之前。
GameplayTagManager.Initialize(
    new GameplayTagRegistration("State.Burning"),
    new GameplayTagRegistration("State.Control.Frozen"),
    new GameplayTagRegistration("State.Control.Silenced"),
    new GameplayTagRegistration("Ability.Cast"),
    new GameplayTagRegistration("Equipment.Weapon.Staff"));
```

两种方式选一种即可。显式注册不会再合并程序集声明。由负责启动的线程初始化一次，之后再加载带标签的资源；只要句柄、容器、订阅或池内对象还在使用，就不要替换注册表。这里没有初始化锁或重复调用保护。

名称区分大小写，每一级使用字母、数字或下划线，以 `.` 分隔。读取外部名称时，可以用带 `out` 的查询，在业务边界决定如何处理缺失声明：

```csharp
string savedName = "State.Burning";
if (GameplayTagManager.RequestTag(savedName, out GameplayTag tag))
{
    var loaded = new GameplayTagContainer();
    loaded.AddTag(tag);
}
```

未知、null 或空名称返回 `None`。编辑工具需要校验名称时，可以主动调用 `GameplayTagUtility.IsValidName` 或 `ValidateName`；运行时不会自动检查声明。解析后的标签应缓存下来，不要每帧重新查询字符串。

<a name="cn-unity-and-serialization"></a>
### 在 Inspector 和存档中使用

包内提供了标签选择的 Inspector 绘制器。它们用于选择已注册的名称，不会创建声明，也不是一套配置资源管理器。

独立的序列化标签字段，加载后解析并保存句柄：

```csharp
using GameplayTags;
using UnityEngine;

public sealed class DamageTypeExample : MonoBehaviour
{
    [SerializeField] private GameplayTag damageType;

    private void Awake()
    {
        GameplayTagManager.InitializeIfNeeded();
        damageType = damageType.Resolve();
    }
}
```

先声明需要选择的名称。`Resolve()` 返回的是结构体，**要接住返回值**；自己修改 `m_Name` 后也一样。查询只使用缓存 ID，不会顺便修正被改过的名称。

序列化的**容器**需要更早的准备：它会在反序列化回调里立即重建成员。因此要在**加载这些资源或场景之前**初始化注册表，Editor 工具也需要考虑这个顺序。不能把 `Awake` 当成所有序列化容器的通用初始化位置；Unity 回调还可能不在主线程执行。相关行为见 [Unity 序列化回调说明](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ISerializationCallbackReceiver.html)。

需要直接挂在 GameObject 上时，可以使用 `GameObjectGameplayTagContainer`。它的 `m_PersistentTags` 在 `Awake` 中加入计数容器，通过 `GameplayTagContainer` 属性访问运行态。`gameObject.Bind()` 会获取或添加这个组件，并创建绑定组；初始化先后和绑定的释放时机由业务组件协调。

存档保存**名称**，不要保存 `RuntimeIndex`。ID 只属于当前注册表，不同构建之间可能变化。普通容器保存显式名称，加载后推导祖先；未知名称不会恢复为成员。计数容器则应由业务存档保存名称和次数。本库不提供自动存档迁移或名称重定向。

关闭 Domain Reload 时，静态注册表、缓存句柄、订阅和对象池可能跨 Play Mode 保留，不能在这些活状态上重新发布另一套注册表。从旧名称版升级也需要明确迁移 API 和资源，并非直接替换包就能兼容。

<a name="cn-pooling"></a>
### 临时计算：自己选择复用策略

每帧重复使用的工作容器，可以直接长期持有，用完 `Clear()`。多个临时作用域需要借还时，再用线程本地池：

```csharp
using (GameplayTagContainerPool.Get(out GameplayTagContainer temporary))
{
    temporary.AddTag(GameTags.Burning);
    temporary.AddTag(GameTags.Cast);
}
```

默认采用**不限制保留预算**的策略，大容器不会因为内置的 64 KiB 阈值被丢弃。在负责借还的线程上，加载阶段自行选择：

```csharp
// 只限制个数，不限制单个容器的容量。
GameplayTagContainerPool.Configure(new GameplayTagContainerPool.RetentionPolicy(32));

// 或者同时限制个数、单个和总数组载荷。以下数值仅为业务配置示例。
GameplayTagContainerPool.Configure(new GameplayTagContainerPool.RetentionPolicy(
    maxCount: 64, maxContainerBytes: 256 * 1024, maxTotalBytes: 4L * 1024 * 1024));

// 或者不保留归还对象，或恢复不限制预算。
GameplayTagContainerPool.Configure(GameplayTagContainerPool.RetentionPolicy.Disabled);
GameplayTagContainerPool.Configure(GameplayTagContainerPool.RetentionPolicy.Unbounded);
```

`Configure` 会释放当前线程已经缓存的对象。`Clear()` 同样释放缓存及池数组，但保留选定策略。`RetainedCount` 与 `RetainedDataBytes` 可用于观察保留量；字节数只计算容器的数组载荷，不含对象头、数组头和进程共享数据。预算判断是调用方选择的复用策略，不是非法参数校验；预算必须有效且非负。

策略只影响当前线程，不会自动传播到其他线程。默认不限制预算可能保留峰值内存；大型工作流可以选择长期持有容器，或设置适合项目的预算。借还期间由调用方保证同线程、恰好一次归还，不复制租约分别释放，不释放默认租约，不使用已经归还的引用。池不追踪这些误用。

<a name="cn-player-registry"></a>
### Player 注册表：按实际构建采集，不猜 Editor 声明

普通项目继续使用程序集声明或显式 `Initialize`。希望 Player 启动不扫描程序集时，可以启用 Editor 生成器。它通过 Mono.Cecil **读取本次 Player 编译后的 DLL 元数据**，不加载或执行这些 DLL，不再把 Editor 域当成 Player。程序集定义的平台选择、`#if !UNITY_EDITOR`、平台宏、额外 scripting defines，以及随构建进入的托管插件，都以该次编译产物为准。

在 **Tools → Gameplay Tags → Generate Player Registry** 启用后，使用平时的 Build 流程构建目标。第一次或声明变化时，脚本编译后的回调会写出正确的 `Assets/GameplayTags.Generated.cs`，并停止当前构建。让 Unity 导入并编译新脚本，再以相同配置构建。第二次会同时核对源码和**已编译表的指纹**；旧代码没编进去就不会通过。切换平台或 defines 导致声明不同，也会更新后要求重编，不静默沿用旧表。

这是有意的两阶段生成，不尝试在已经完成的脚本编译阶段塞入新 C#。批处理时，第一次输出生成文件后，由构建任务重新启动一次 Unity 构建；不要无条件循环重试所有构建错误。自定义管线也可以在链接／裁剪前，将该次完整 Player 托管 DLL 列表交给 `GameplayTagRegistryGenerator.GenerateFromPlayerAssemblies(paths)`，返回 false 表示需要编译更新后的表。不存在输出文件时不启用生成检查。

生成表**不安装自动启动回调**。由自己的启动流程调用一次 `GameplayTags.Generated.GameplayTagRegistry.Initialize()`，再创建标签缓存和加载序列化容器；不要与另一套显式初始化混用。Editor 下该入口使用 Editor 自己的程序集声明，Player 下使用生成表。业务缓存类加显式静态构造器，可以避免 `beforefieldinit` 提前初始化；首次访问仍必须排在启动之后。

只支持直接的 `GameplayTagAttribute` 声明，包含构造参数和 `TagName`、`Description`、`Flags` 命名覆盖。自定义派生特性可能执行任意代码，元数据工具不能替它求值；发现这类声明会在构建时报错，改用直接特性或显式注册，不会把它静默遗漏。运行中加载的额外程序集不属于本次 Player 构建。Mono.Cecil 只被 Editor 程序集引用，不进入运行时 API。该回调会增加构建期读取／中转工作，不是零成本构建；实际 Unity/IL2CPP 整体流程仍需在项目中验收。

<a name="cn-caller-contract"></a>
### 让热路径保持简单

运行时使用整数句柄、紧凑存储，并为较大的集合建立哈希索引，不把名称解析和通用校验放进每次查询。实际使用时，记住一条即可：**启动时注册，加载时解析，运行时复用句柄和容器。**

`EnsureCapacity(n)` 按**包含祖先的不同成员总数**预留，不是只按叶标签数量。具体类型的枚举使用结构体枚举器，通过 `IEnumerable<GameplayTag>` 使用时可能装箱。新建、扩容、序列化、订阅和池首次使用仍可能分配内存。这是托管 C# 实现，不是可直接放进 Burst/Jobs 的 `NativeContainer`。

调用方需要提供有效、非 `None` 的句柄和非 null 修改操作数，保证数量为正、容量有效、计数不溢出，删除不超过自己拥有的次数，并管理好同步、父链贡献和对象池生命周期。共享 `Empty` 按只读使用。非法使用不属于内核的恢复职责；把输入校验和错误处理放在业务边界即可。

**版本说明：** 当前源码为 `2.1.0-rc.2-optimized.2`，不代表已经完成 Unity/IL2CPP 全面验收，也不承诺所有场景普遍更快。容量与池化策略，请根据项目自己的标签数量、层级深度和目标后端来选择。

<a name="cn-api-map"></a>
### 接下来查哪个 API？

| 你想做的事 | 从这里开始 |
| --- | --- |
| 保存一组角色状态 | [`GameplayTagContainer`](Runtime/GameplayTagContainer.cs) |
| 记录 Buff 叠层、监听变化 | [`GameplayTagCountContainer`](Runtime/GameplayTagCountContainer.cs) |
| 汇总各子系统拥有的贡献 | [`GameplayTagHierarchicalContainer`](Runtime/GameplayTagHierarchicalContanier.cs) |
| 表达必需条件与禁止条件 | [`GameplayTagRequirements`](Runtime/GameplayTagRequirements.cs) |
| 注册名称、缓存标签 | [`GameplayTagManager`](Runtime/GameplayTagManager.cs)、[`GameplayTag`](Runtime/GameplayTag.cs) |
| 管理一组订阅、借用临时容器 | [`GameplayTagContainerBinds`](Runtime/GameplayTagContainerBinds.cs)、[`GameplayTagContainerPool`](Runtime/GameplayTagContainerPool.cs) |

`GameplayTag` 还提供 `Name`、`Label`、`Description`、`Flags`、`ParentTag`、`ParentTags`、`HierarchyTags` 和 `ChildTags`，描述的是注册表关系。`IsParentOf`、`IsChildOf` 是严格的祖先关系，标签不是自己的祖先。只提取容器中实际存在的父子成员时，用 `GetParentTags`、`GetChildTags` 或显式成员版本。

仓库只保留[运行时](Runtime)、[Editor 集成](Editor)和使用文档。许可证：[MIT](LICENSE)。

[返回中文开头](#cn) · [EN · English](#en)
