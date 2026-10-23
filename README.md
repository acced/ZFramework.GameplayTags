# ZFramework Gameplay Tags

**English | [简体中文](README_CN.md)**

[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-000000?logo=unity)](https://unity.com/)
[![Language](https://img.shields.io/badge/Language-C%23%209.0-239120?logo=csharp)](https://learn.microsoft.com/dotnet/csharp/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

> **Give combat, abilities, equipment, and UI a shared vocabulary—without a tangle of booleans and tightly coupled enums.**

If you have built an action, RPG, or tactical game, you have probably run into these headaches:
- **Boolean sprawl**: `isStunned`, `isFrozen`, `isSilenced`, `isDisarmed`… Every time design adds a new crowd-control effect, you have to hunt down `if` statements across movement, attack, dodge, and casting scripts.
- **Clobbered buff stacks**: A fireball applies `Burning` for 3 seconds while the character is also standing in a fire zone. When the fireball expires, it sets `isBurning = false`—and suddenly the character stops burning while still standing in the flames.
- **Cross-system coupling**: To check whether a spell can be cast, the ability system has to ask the inventory if a staff is equipped, ask the buff manager if silence is active, and ask the locomotion controller if the actor is grounded.

**ZFramework Gameplay Tags** is a lightweight, high-performance hierarchical gameplay tag library for Unity. It combines **visual ScriptableObject authoring + static C# code generation + automatic initialization + 4-byte integer handles**: you organize your tag tree in an Editor window, generate strongly typed C# fields with one click, and use them immediately at runtime with pure integer and compact array operations—zero assembly scanning and zero string lookups on hot paths.

---

## Table of Contents

- [How It Works in Three Steps](#how-it-works-in-three-steps)
- [Installation](#installation)
- [5-Minute Quick Start](#5-minute-quick-start)
- [Practical Recipes & Code Examples](#practical-recipes--code-examples)
  - [Recipe 1: Hierarchical Matching & Crowd Control (`HasTag` vs `HasTagExact`)](#recipe-1-hierarchical-matching--crowd-control-hastag-vs-hastagexact)
  - [Recipe 2: Multi-Source Buff Stacking (`GameplayTagCountContainer`)](#recipe-2-multi-source-buff-stacking-gameplaytagcountcontainer)
  - [Recipe 3: Aggregating Equipment & Auras (`GameplayTagHierarchicalContainer`)](#recipe-3-aggregating-equipment--auras-gameplaytaghierarchicalcontainer)
  - [Recipe 4: Ability Requirements & Cached Rule Sets (`GameplayTagRequirements` & `RuleSet`)](#recipe-4-ability-requirements--cached-rule-sets-gameplaytagrequirements--ruleset)
  - [Recipe 5: Event-Driven UI & VFX Bindings (`RegisterTagEventCallback` & `Binds`)](#recipe-5-event-driven-ui--vfx-bindings-registertageventcallback--binds)
  - [Recipe 6: Set Algebra & Zero-GC Container Reuse (`Union` / `Intersection` / `Pool`)](#recipe-6-set-algebra--zero-gc-container-reuse-union--intersection--pool)
- [Unity Integration: Auto-Initialization, Inspector & Saves](#unity-integration-auto-initialization-inspector--saves)
  - [Initialization: Automatic by Default, Manual When Wanted](#initialization-automatic-by-default-manual-when-wanted)
  - [Authoring Tags Directly in the Inspector](#authoring-tags-directly-in-the-inspector)
  - [Saving and Loading State](#saving-and-loading-state)
- [Tag Settings Editor & Code Generation](#tag-settings-editor--code-generation)
- [Interactive Sample: Tag Playground](#interactive-sample-tag-playground)
- [API Cheat Sheet](#api-cheat-sheet)
- [Standalone .NET & Custom Content Pipelines](#standalone-net--custom-content-pipelines)
- [Migrating from Earlier Versions](#migrating-from-earlier-versions)
- [Performance & Caller Contract](#performance--caller-contract)

---

## How It Works in Three Steps

Instead of scanning assemblies for `[assembly: GameplayTag]` attributes or post-processing DLLs with Mono.Cecil, ZFramework Gameplay Tags uses a clean, deterministic **ScriptableObject + Code Generation + Auto-Initialization** workflow:

```text
┌─────────────────────────────┐    ┌─────────────────────────────┐    ┌─────────────────────────────┐
│ 1. Author in Editor         │    │ 2. Generate C# Code         │    │ 3. Query at Runtime         │
│ Tools → Gameplay Tags       │ ──►│ Emits GameplayTagRegistry   │ ──►│ Auto-initializes on first   │
│ → Tag Settings (TreeView)   │    │ and static Tags.* fields    │    │ query or deserialization    │
└─────────────────────────────┘    └─────────────────────────────┘    └─────────────────────────────┘
```

- **What it solves**: Cross-system state representation, hierarchical categorization, reference-counted stacking from multiple sources, ability prerequisite checks, and reactive change notifications.
- **What it leaves to you**: It is a focused building block, not a monolithic ability framework. Your game still owns damage formulas, buff timers, and cooldowns. And if you just need a private on/off switch inside a single script, a plain `bool` is still great.

---

## Installation

Requires **Unity 2022.3 or later**.

1. Open **Window → Package Manager**.
2. Click **+ → Add package from git URL...** and enter:
   ```text
   https://github.com/acced/ZFramework.GameplayTags.git#main
   ```
   *(To pin a specific version for your team, replace `#main` with a full commit SHA. For local development, choose **Add package from disk...** and select `package.json`.)*
3. **Assembly Definition (`.asmdef`) references**:
   - Reference **`ZFramework.GameplayTags`** in your gameplay `.asmdef`.
   - Generating code creates `Assets/GameplayTags.Generated/ZFramework.GameplayTags.Generated.asmdef` (with `autoReferenced: true` by default). If your gameplay `.asmdef` disables auto-referencing, add `ZFramework.GameplayTags.Generated` to its references list as well.

---

## 5-Minute Quick Start

### 1. Define Your Tags

1. Open **Tools → Gameplay Tags → Tag Settings**.
2. Click **创建标签配置** (*Create Tag Configuration*). By default, this creates your project's single tag settings asset at `Assets/GameplayTags/Editor/GameplayTagSettings.asset`.
3. Add these five tags (with optional descriptions):
   ```text
   State.Burning
   State.Control.Frozen
   State.Control.Silenced
   Ability.Cast
   Equipment.Weapon.Staff
   ```
   > **Tip**: You only need to add leaf tags. Parent nodes like `State`, `State.Control`, `Equipment`, and `Equipment.Weapon` are derived automatically and shown as dimmed nodes in the tree.

### 2. Generate the C# Registry

Click **生成代码** (*Generate Code*) in the window toolbar—or run **Tools → Gameplay Tags → Generate Player Registry**—and wait for Unity to finish compiling.

This generates `Assets/GameplayTags.Generated/GameplayTags.Generated.cs`. Every explicit tag and implicit parent becomes a `readonly` field under `GameplayTagRegistry.Tags`, with dots encoded as double underscores (`__`):
- `State.Control` → `Tags.State__Control`
- `State.Control.Frozen` → `Tags.State__Control__Frozen`

### 3. Try It in Play Mode

Create `QuickStartExample.cs`, attach it to an empty GameObject, and press Play (**no manual initialization call needed**):

```csharp
using GameplayTags;
using UnityEngine;
using Tags = GameplayTags.Generated.GameplayTagRegistry.Tags;

public sealed class QuickStartExample : MonoBehaviour
{
    private void Start()
    {
        // 1. Create a container and apply Frozen (the registry auto-initializes on first use)
        var states = new GameplayTagContainer();
        states.AddTag(Tags.State__Control__Frozen);

        // 2. Hierarchical match vs. exact match
        Debug.Log(states.HasTag(Tags.State__Control));              // True: Frozen is a kind of State.Control
        Debug.Log(states.HasTag(Tags.State));                       // True: Frozen is also a kind of State
        Debug.Log(states.HasTagExact(Tags.State__Control));         // False: State.Control was not added directly
        Debug.Log(states.HasTagExact(Tags.State__Control__Frozen)); // True: Frozen was explicitly added

        // 3. Remove the state
        states.RemoveTag(Tags.State__Control__Frozen);
        Debug.Log(states.IsEmpty);                                  // True
    }
}
```

---

## Practical Recipes & Code Examples

All snippets below use the generated `Tags` alias:
```csharp
using GameplayTags;
using Tags = GameplayTags.Generated.GameplayTagRegistry.Tags;
```

### Recipe 1: Hierarchical Matching & Crowd Control (`HasTag` vs `HasTagExact`)

When you add `State.Control.Frozen` to a container, the hierarchy looks like this:

```text
State                       ◄── Implicit ancestor: HasTag is True, HasTagExact is False
└── Control                 ◄── Implicit ancestor: HasTag is True, HasTagExact is False
    └── Frozen              ◄── Explicit member:   HasTag is True, HasTagExact is True
```

This lets you ask two different kinds of questions:
- **Category check (`HasTag`)**: *"Is the character under any crowd control?"* — Call `states.HasTag(Tags.State__Control)`. Frozen, stunned, or silenced all match. When a designer adds `State.Control.Polymorph` next month, your movement and casting code works without a single change.
- **Exact check (`HasTagExact`)**: *"Is the character specifically Frozen (to trigger a shatter bonus)?"* — Call `states.HasTagExact(Tags.State__Control__Frozen)`.

```csharp
var states = new GameplayTagContainer();
states.AddTag(Tags.State__Control__Frozen);

int explicitCount = states.ExplicitTagCount; // 1 (only Frozen was added directly)
int totalCount    = states.TagCount;         // 3 (State, State.Control, and State.Control.Frozen)

// Iterate only the tags that were explicitly added (prints State.Control.Frozen)
foreach (GameplayTag tag in states.GetExplicitTags())
{
    Debug.Log($"Explicit: {tag.Name}");
}

// Default iteration includes implied ancestors (prints State, State.Control, State.Control.Frozen; unordered)
foreach (GameplayTag tag in states)
{
    Debug.Log($"All present: {tag.Name}");
}
```

> **Keep in mind**:
> 1. **Parents do not imply children**: Adding `State.Control` by itself never makes `State.Control.Frozen` true.
> 2. **Remove what you added**: An ordinary `GameplayTagContainer` is a **set** (adding the same tag twice does not stack). Removing an unadded parent (`RemoveTag(Tags.State__Control)`) does not strip `Frozen`—always remove the specific tag that was added.

---

### Recipe 2: Multi-Source Buff Stacking (`GameplayTagCountContainer`)

An ordinary set only knows whether a tag is present. When both a **fireball** and a **flame floor** apply `State.Burning`, `GameplayTagCountContainer` tracks each contribution so one expiring effect never cancels the other prematurely:

```csharp
var effects = new GameplayTagCountContainer();

// Two independent sources apply Burning
effects.AddTag(Tags.State__Burning); // Fireball hits (+1)
effects.AddTag(Tags.State__Burning); // Steps onto flame floor (+1)

Debug.Log(effects.GetExplicitTagCount(Tags.State__Burning)); // 2

// The fireball expires and removes its single contribution
effects.RemoveTag(Tags.State__Burning);
Debug.Log(effects.HasTag(Tags.State__Burning));              // True: still standing in the flame floor!

// The character leaves the flame floor
effects.RemoveTag(Tags.State__Burning);
Debug.Log(effects.HasTag(Tags.State__Burning));              // False: all sources are gone
```

- **Multi-stack operations**: Use `AddTag(tag, amount)` and `RemoveTag(tag, amount)` to add or remove multiple stacks in one call.
- **Explicit count vs. hierarchy count**: `GetExplicitTagCount(tag)` returns the stacks added directly to that tag. `GetTagCount(tag)` sums the tag and all of its descendants (e.g., 2 stacks of `State.Burning` + 1 stack of `State.Control.Frozen` gives `effects.GetTagCount(Tags.State) == 3`).

---

### Recipe 3: Aggregating Equipment & Auras (`GameplayTagHierarchicalContainer`)

Suppose both a **Staff** and an **Arcane Aura** grant `Ability.Cast`. If both write directly into the character's main container, unequipping the staff or refreshing equipment can easily desynchronize the counts.

With `GameplayTagHierarchicalContainer`, each subsystem owns its own child container and points its `Parent` at the character's aggregate container:

```csharp
var actor     = new GameplayTagCountContainer();
var equipment = new GameplayTagHierarchicalContainer { Parent = actor };
var aura      = new GameplayTagHierarchicalContainer { Parent = actor };

// Equipment and aura each contribute Ability.Cast
equipment.AddTag(Tags.Ability__Cast);
aura.AddTag(Tags.Ability__Cast);

Debug.Log(actor.GetTagCount(Tags.Ability__Cast)); // 2

// Unequip: detaching the child automatically withdraws all of its contributions from actor
equipment.Parent = null;
Debug.Log(actor.HasTag(Tags.Ability__Cast));       // True (count is 1): the aura is still active!

// Re-equip: reattaching transfers equipment's tags back into actor
equipment.Parent = actor;
Debug.Log(actor.GetTagCount(Tags.Ability__Cast)); // 2
```

> **Note**: This is **bottom-up aggregation** (children contribute counts to their parent), not top-down inheritance. Always remove or `Clear()` tags through the child container that owns them rather than clearing the aggregate parent while children are still attached.

---

### Recipe 4: Ability Requirements & Cached Rule Sets (`GameplayTagRequirements` & `RuleSet`)

#### Single Ability Rule (`GameplayTagRequirements`)

Suppose casting *Pyroblast* requires **Ability.Cast** and **Equipment.Weapon.Staff**, and is blocked by any **State.Control**. Build an immutable rule once when the ability loads and evaluate it whenever needed:

```csharp
// (forbidden, required1, required2) — zero temporary array or container allocations
var pyroblastRule = GameplayTagRequirements.Create(
    forbidden:          Tags.State__Control,
    required:           Tags.Ability__Cast,
    additionalRequired: Tags.Equipment__Weapon__Staff);

var actor = new GameplayTagContainer();
actor.AddTag(Tags.Ability__Cast);
actor.AddTag(Tags.Equipment__Weapon__Staff);

Debug.Log(pyroblastRule.Matches(actor)); // True: ready to cast

actor.AddTag(Tags.State__Control__Silenced);
Debug.Log(pyroblastRule.Matches(actor)); // False: Silenced matches forbidden State.Control
```

- **Larger rules**: Pass arrays or `ReadOnlySpan<GameplayTag>` to `new GameplayTagRequirements(forbidden, required)`. Construction automatically deduplicates conditions, prunes redundant ancestors/children, and detects contradictions. Up to 4 effective conditions are stored inline in the struct with zero heap allocation.
- **Match across two containers**: If you keep permanent equipment tags and temporary buff tags in separate containers, call `pyroblastRule.Matches(equipmentTags, buffTags)` directly—no temporary union needed.
- **Ad-hoc container queries**: For simple checks, use `HasAny(query)`, `HasAll(query)`, and their exact counterparts `HasAnyExact(query)` and `HasAllExact(query)`.

#### Batch Evaluation & Incremental Caching (`GameplayTagRuleSet`)

When an action bar has a dozen abilities that repeatedly test `State.Control` or `Ability.Cast`, pack them into a shared `GameplayTagRuleSet` and give each actor its own `Cache`:

```csharp
// 1. Share one immutable rule set across all actors using these abilities
var skillRules = new GameplayTagRuleSet(new[]
{
    pyroblastRule,
    new GameplayTagRequirements(
        forbidden: System.Array.Empty<GameplayTag>(),
        required:  new[] { Tags.State__Burning })
});

// 2. Keep one lightweight cache per actor
GameplayTagRuleSet.Cache actorRuleCache = skillRules.CreateCache();

// 3. Evaluate all rules at once
System.ReadOnlySpan<bool> canUseSkills = actorRuleCache.Evaluate(actor);
bool canCastPyroblast = canUseSkills[0];
bool canUseFireCombo  = canUseSkills[1];
```

`Cache` tracks the container's `PresenceVersion`:
- If no tag has appeared or disappeared since the last check (even if a stack count changed from 1 to 5), `Evaluate` **returns the cached results immediately**.
- When tag presence changes, it queries each distinct condition once and recomputes only the rules that depend on the changed conditions.

---

### Recipe 5: Event-Driven UI & VFX Bindings (`RegisterTagEventCallback` & `Binds`)

Instead of polling tags in `Update()` to toggle status icons or particle effects, subscribe to changes on a `GameplayTagCountContainer`:

#### Option A: Direct Stack & Presence Callbacks

```csharp
var effects = new GameplayTagCountContainer();

OnTagCountChangedDelegate onBurningChanged = (tag, newCount) =>
{
    Debug.Log($"{tag.Name} stacks: {newCount}");
};

// AnyCountChange: fires on every stack change (1 → 2 → 1 → 0)
// NewOrRemoved:   fires only on presence transitions (0 → >0 and >0 → 0)
effects.RegisterTagEventCallback(
    Tags.State__Burning,
    GameplayTagEventType.AnyCountChange,
    onBurningChanged);

effects.AddTag(Tags.State__Burning);    // Logs: 1
effects.AddTag(Tags.State__Burning);    // Logs: 2
effects.RemoveTag(Tags.State__Burning); // Logs: 1
effects.Clear();                        // Logs: 0 (Clear zeroes all counts and fires 0 callbacks, keeping subscriptions)

effects.RemoveTagEventCallback(
    Tags.State__Burning,
    GameplayTagEventType.AnyCountChange,
    onBurningChanged);
```

> **Pro tip**: Subscribe to a parent tag like `Tags.State__Control` with `GameplayTagEventType.NewOrRemoved` to drive a "Crowd Controlled!" HUD banner whenever *any* control effect begins or ends.

#### Option B: Lifecycle-Bound UI Subscriptions (`GameplayTagContainerBinds`)

UI views usually want the **current state immediately upon binding** and a **single cleanup call when the view closes**:

```csharp
public sealed class StatusHudView : MonoBehaviour
{
    private GameplayTagContainerBinds m_Binds;

    public void BindActor(GameplayTagCountContainer actorEffects)
    {
        m_Binds.Dispose(); // Clear any previous bindings
        m_Binds = new GameplayTagContainerBinds(actorEffects);

        // Bind immediately invokes the callback with the current presence, then listens for changes
        m_Binds.Bind(Tags.State__Burning, isBurning =>
        {
            Debug.Log($"Show burning icon: {isBurning}");
        });

        m_Binds.Bind(Tags.State__Control, isControlled =>
        {
            Debug.Log($"Show crowd-control overlay: {isControlled}");
        });
    }

    private void OnDestroy()
    {
        // Unsubscribes all callbacks in this group without touching actorEffects itself
        m_Binds.Dispose();
    }
}
```

---

### Recipe 6: Set Algebra & Zero-GC Container Reuse (`Union` / `Intersection` / `Pool`)

Combine or compare containers using explicit set operations:

```csharp
var left = new GameplayTagContainer();
left.AddTag(Tags.State__Burning);
left.AddTag(Tags.Ability__Cast);

var right = new GameplayTagContainer();
right.AddTag(Tags.State__Control__Frozen);
right.AddTag(Tags.Ability__Cast);

// Allocate new result containers
var union        = GameplayTagContainer.Union(left, right);        // Burning, Frozen, Cast
var intersection = GameplayTagContainer.Intersection(left, right); // Cast only
var diff         = left.Clone();
diff.RemoveTags(right);                                            // Burning only
```

> **Note**: Set algebra operates on **explicit members** and derives ancestors from the result. Intersecting `{State.Burning}` with `{State.Control.Frozen}` yields an **empty container**—sharing the implicit ancestor `State` does not manufacture an explicit `State` tag.

#### Reusable Output Containers & Thread-Local Pooling (`GameplayTagContainerPool`)

To avoid allocations in frequent calculations, reuse a destination container or borrow a temporary one from the thread-local pool:

```csharp
// 1. Write into an existing container (output may safely alias either input)
var reusableOutput = new GameplayTagContainer(capacity: 32);
GameplayTagContainer.Union(reusableOutput, left, right);
GameplayTagContainer.Intersection(reusableOutput, left, right);
reusableOutput.Clear(); // Clears members while keeping internal array capacity

// 2. Borrow a scoped scratch container from the thread-local pool
using (GameplayTagContainerPool.Get(out GameplayTagContainer scratch))
{
    scratch.AddTag(Tags.State__Burning);
    // Use scratch inside this scope; automatically cleared and returned on Dispose
}
```

By default, the pool uses `RetentionPolicy.Unbounded` so large containers are never silently dropped by a hidden size cap. You can configure a retention budget for the current thread during startup:

```csharp
GameplayTagContainerPool.Configure(new GameplayTagContainerPool.RetentionPolicy(
    maxCount: 32,
    maxContainerBytes: 256 * 1024,
    maxTotalBytes: 4L * 1024 * 1024));
```

---

## Unity Integration: Auto-Initialization, Inspector & Saves

### Initialization: Automatic by Default, Manual When Wanted

By default, **you do not need to call `GameplayTagRegistry.Initialize()` in your gameplay code**:

1. **Auto-initializes on first query and during deserialization**:
   - The generated `GameplayTagRegistry` registers a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` hook for early Unity startup.
   - Even if a scene object, Prefab, or ScriptableObject deserializes earlier than that (including on background deserialization threads) or you query `Tags.*` / `RequestTag` in the Editor, `GameplayTagManager` automatically locates and invokes `GameplayTagRegistry.Initialize()` on demand.
2. **`GameplayTagRegistry.Initialize()` remains available and idempotent**:
   - If your project has an explicit boot prewarm stage, you can still call `GameplayTagRegistry.Initialize()` (or `GameplayTagManager.Initialize(...)`) directly.
   - **Repeated calls never rebuild the registry**: Once initialized, subsequent calls are safe no-ops, preserving all cached `GameplayTag` handles.
3. **Opt-in Manual Mode (`GAMEPLAY_TAGS_MANUAL_INITIALIZATION`)**:
   - If you want strict manual control over when the registry is initialized (for example, to supply custom runtime registrations before any generated table can run), add the following scripting define symbol in **Player Settings → Scripting Define Symbols**:
     ```text
     GAMEPLAY_TAGS_MANUAL_INITIALIZATION
     ```
   - When `GAMEPLAY_TAGS_MANUAL_INITIALIZATION` is defined, automatic initialization is disabled, and you must call `GameplayTagRegistry.Initialize()` or `GameplayTagManager.Initialize(registrations)` before the first tag query or asset deserialization.

### Authoring Tags Directly in the Inspector

In Unity, a single `GameplayTagContainer` works both as an **Inspector-serialized field** and as your **live runtime container**—there is no separate data wrapper class and no manual post-load conversion step:

```csharp
using GameplayTags;
using UnityEngine;
using Tags = GameplayTags.Generated.GameplayTagRegistry.Tags;

public sealed class CharacterState : MonoBehaviour
{
    // Click "Edit Tags..." in the Inspector to pick initial tags; ready automatically on load
    [SerializeField] private GameplayTagContainer m_States = new GameplayTagContainer();

    // Standalone single-tag fields also get a searchable dropdown picker
    [SerializeField] private GameplayTag m_CategoryTag;

    private void Awake()
    {
        // Note: For standalone GameplayTag struct fields edited or loaded in Unity,
        // call Resolve() and assign it back after loading:
        m_CategoryTag = m_CategoryTag.Resolve();
    }

    public bool IsControlled => m_States.HasTag(Tags.State__Control);
    public void ApplyBurning() => m_States.AddTag(Tags.State__Burning);
}
```

- **Shared ScriptableObject templates**: When multiple actors spawn from a shared `ScriptableObject` template containing a `GameplayTagContainer`, call `var runtimeTags = config.InitialTags.Clone();` so each actor mutates its own copy rather than the shared asset.
- **`GameObjectGameplayTagContainer` component**: The package includes a ready-made `MonoBehaviour` holding serialized `m_PersistentTags` and a live `GameplayTagCountContainer` (populated in `Awake()`), plus a `gameObject.Bind()` extension method for quick UI/effect binding.

### Saving and Loading State

- **Persist `tag.Name`, never `tag.RuntimeIndex`**: Integer runtime IDs are assigned in ordinal order from the current registry and will shift when new tags are added.
- **`GameplayTagContainer` serializes names automatically**: Both Unity's serializer and `DataContractSerializer` store sorted explicit tag names, auto-initialize the registry if needed, and reconstruct the ancestor closure on load.
- **Saving a `GameplayTagCountContainer`**: Save each explicit tag's `Name` (`foreach (var tag in container.GetExplicitTags())`) along with `container.GetExplicitTagCount(tag)`. When loading, resolve the name via `GameplayTagManager.RequestTag(savedName, out GameplayTag tag)` and restore stacks with `AddTag(tag, savedCount)`. Never save or replay aggregated parent counts as explicit stacks.

---

## Tag Settings Editor & Code Generation

Open **Tools → Gameplay Tags → Tag Settings** to manage your project's tag hierarchy:

- **Single source of truth**: Keep one `GameplayTagSettings.asset` in your project (typically under an `Editor` folder; it is Editor-only authoring data and is not included in Player builds). Inspector pickers read directly from this asset without initializing the runtime registry.
- **TreeView editing & search**: Search by full path, add root or child tags, edit descriptions, rename entire branches, delete branches, and use full **Undo / Redo**.
- **Promoting implicit parents**: Auto-derived parent nodes appear dimmed in the tree. Selecting an implicit parent and saving a description promotes it to an explicit tag entry.
- **Branch renaming & compiler-assisted refactoring**: Renaming a parent updates all of its descendants inside `GameplayTagSettings.asset`. It does not silently rewrite scenes, Prefabs, or C# scripts—once you click **Generate Code**, the C# compiler will immediately highlight any outdated `Tags.Old__Name` references so you can update them safely.
- **Pre-build verification**: Before a Player build starts, `IPreprocessBuildWithReport` verifies that `GameplayTags.Generated.cs` matches `GameplayTagSettings.asset`. If someone edited tags and forgot to regenerate code, the build stops immediately with a clear error message.

---

## Interactive Sample: Tag Playground

The package includes a single-script, UI-free sample (`TagPlayground.cs`) that prints live results and expected values to the Unity Console—with zero manual initialization boilerplate:

1. Install the package and let Unity finish compiling.
2. In **Window → Package Manager**, select **ZFramework Gameplay Tags**, expand **Samples**, and click **Import** next to **Tag Playground**.
   *(Note: If your project already has a `GameplayTagSettings.asset`, add the sample's five `Demo.*` tags to your existing settings and delete the imported sample SO so only one settings asset exists.)*
3. Run **Tools → Gameplay Tags → Generate Player Registry** and wait for compilation.
4. Open `TagPlayground.unity` and press Play to see hierarchical matching, casting requirements, dual-source burning stacks, and `1 → 2 → 1 → 0` event callbacks logged in the Console.

---

## API Cheat Sheet

| Goal | Primary Type / API |
| :--- | :--- |
| **Represent a single tag handle** | [`GameplayTag`](Runtime/GameplayTag.cs) — 4-byte ID equality, `ParentTag`, `ParentTags`, `ChildTags`, `IsParentOf`, `IsChildOf` |
| **Store a tag set (unstacked, serializable)** | [`GameplayTagContainer`](Runtime/GameplayTagContainer.cs) — `AddTag`, `RemoveTag`, `HasTag`, `HasTagExact`, `HasAny`, `HasAll`, `Union`, `Intersection`, `Clone` |
| **Track multi-source stacks & listen to events** | [`GameplayTagCountContainer`](Runtime/GameplayTagCountContainer.cs) — `AddTag(tag, amount)`, `GetExplicitTagCount`, `GetTagCount`, `RegisterTagEventCallback` |
| **Roll up subsystem tags into an actor** | [`GameplayTagHierarchicalContainer`](Runtime/GameplayTagHierarchicalContainer.cs) — set `Parent` to automatically contribute & withdraw counts |
| **Check required & forbidden ability tags** | [`GameplayTagRequirements`](Runtime/GameplayTagRequirements.cs) — `Create(forbidden, req1, req2)`, `Matches(actor)`, `Matches(equip, buffs)` |
| **Batch-evaluate & cache multiple rules** | [`GameplayTagRuleSet`](Runtime/GameplayTagRuleSet.cs) & `GameplayTagRuleSet.Cache` — `CreateCache()`, `cache.Evaluate(actor)` |
| **Bind & dispose grouped UI presence listeners** | [`GameplayTagContainerBinds`](Runtime/GameplayTagContainerBinds.cs) — `Bind(tag, Action<bool>)`, `Dispose()`, `gameObject.Bind()` |
| **Borrow temporary containers without GC** | [`GameplayTagContainerPool`](Runtime/GameplayTagContainerPool.cs) — `using (GameplayTagContainerPool.Get(out var temp))` |
| **Resolve external names or custom registries** | [`GameplayTagManager`](Runtime/GameplayTagManager.cs) — `RequestTag(name, out tag)`, `GetAllTags()`, `Initialize(...)` |

---

## Standalone .NET & Custom Content Pipelines

The core runtime has no hard dependency on Unity and can be built as a standalone **.NET Standard 2.1 / C# 9** library (for dedicated servers, combat simulators, or unit tests) using `GameplayTags.Runtime.csproj`:

```bash
dotnet build GameplayTags.Runtime.csproj -c Release
```

- **With generated code**: If `ZFramework.GameplayTags.Generated` is loaded in the process, the first tag lookup or deserialization automatically initializes `GameplayTagRegistry` even outside Unity.
- **Without generated code (custom data tables)**: Call `GameplayTagManager.Initialize` once before the first tag lookup (subsequent calls are no-ops):

```csharp
GameplayTagManager.Initialize(
    new GameplayTagRegistration("State.Burning", "Damage over time"),
    new GameplayTagRegistration("State.Control.Frozen", "Cannot move or cast"),
    new GameplayTagRegistration("Ability.Cast", "Can cast spells"));

// Resolve once at startup/load time and cache the handle; do not look up strings every frame
GameplayTag burningTag = GameplayTagManager.RequestTag("State.Burning");
```

---

## Migrating from Earlier Versions

If you are upgrading from an earlier version:

1. **Regenerate the registry once after upgrading**: Run **Tools → Gameplay Tags → Generate Player Registry** so `GameplayTags.Generated.cs` is updated to the new auto-initializing format (`GeneratedFormatVersion = 5`).
2. **Manual `GameplayTagRegistry.Initialize()` calls are now optional**: First tag queries and asset deserialization now initialize the registry automatically. Existing `GameplayTagRegistry.Initialize()` calls remain safe and idempotent (they will not rebuild an initialized registry). Define `GAMEPLAY_TAGS_MANUAL_INITIALIZATION` only if you want to require manual initialization.
3. **Assembly attributes & DLL scanning removed**: `GameplayTagAttribute`, `GameplayTagFlags`, `InitializeIfNeeded()`, and Mono.Cecil Player DLL scanning have been removed. Author tags in **Tools → Gameplay Tags → Tag Settings** instead.
4. **Clean up legacy generated files**: Generated output lives in `Assets/GameplayTags.Generated/` with its own `ZFramework.GameplayTags.Generated.asmdef`. Delete any leftover root `Assets/GameplayTags.Generated.cs` (and its `.meta`) from older versions.
5. **Immutable `GameplayTagRequirements`**: Rules are now immutable `readonly struct` snapshots built from tag arrays or `ReadOnlySpan<GameplayTag>` rather than holding references to two mutable containers.

---

## Performance & Caller Contract

To keep hot paths fast and predictable, the runtime follows a **caller-contract** design—keeping string parsing, on-demand initialization, and validation at cold boundaries (first lookup / deserialization) rather than inside every query:

- **Resolve at cold boundaries, reuse handles on hot paths**: Avoid calling `RequestTag("...")` or reconstructing `GameplayTagRuleSet` inside per-frame loops; use the generated `Tags.*` fields or cached `GameplayTag` handles.
- **Supply valid handles and arguments**: Mutate containers only with registered, non-`None` tags; pass positive stack amounts; remove only the occurrences your system added; treat `GameplayTagContainer.Empty` as read-only.
- **Capacity reservation**: `EnsureCapacity(n)` reserves space for **distinct tags including implied ancestors**, not just leaf tags.
- **Threading & runtime model**: Registry initialization is thread-safe. Containers, events, and `RuleSet.Cache` are designed for single-threaded gameplay logic (`GameplayTagContainerPool` is `[ThreadStatic]` and must be rented and returned on the same thread). The library is managed C#, not a Burst/Jobs `NativeContainer`.

---

## License & Feedback

Licensed under the [MIT License](LICENSE). Bug reports and feature suggestions are welcome via [GitHub Issues](https://github.com/acced/ZFramework.GameplayTags/issues).
