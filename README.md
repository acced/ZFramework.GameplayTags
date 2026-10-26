# ZFramework Gameplay Tags

**English | [简体中文](README_CN.md)**

[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-000000?logo=unity)](https://unity.com/)
[![Language](https://img.shields.io/badge/Language-C%23%209.0-239120?logo=csharp)](https://learn.microsoft.com/dotnet/csharp/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

> **Give combat, abilities, equipment, and UI a shared language—and retire the wall of `bool` flags and tightly coupled `enum` switches.**

---

## Why Use Gameplay Tags?

If you have built an action, RPG, roguelike, or strategy game, you have almost certainly lived through these three moments:

1. **The `if` statement that never stops growing**
   At first, your character only had a stun effect, so you wrote `if (isStunned) return;` in your movement and casting scripts. Then design added freeze, petrify, sleep, and polymorph. Soon every script in your project looked like this:
   ```csharp
   if (isStunned || isFrozen || isPetrified || isSleeping || isPolymorphed) return;
   ```
   Every time a new crowd-control effect is added, you have to hunt down every movement, attack, dodge, and ability script—and missing just one creates a bug.

2. **Two fire sources putting each other out**
   A fireball sets `isBurning = true` on a monster for 3 seconds while the monster is also standing in a pool of lava. Three seconds later, the fireball expires and sets `isBurning = false`—and suddenly the monster stops burning while still standing knee-deep in lava.

3. **Every system knowing too much about every other system**
   Just to check whether *Pyroblast* can be cast, the ability script has to ask the inventory if a staff is equipped, ask the buff manager if silence is active, and ask the character controller if the player is grounded.

**Gameplay Tags** solve all three problems with a single idea: represent states and capabilities as a **hierarchy of tags** like `State.Control.Frozen`.

When a character has `State.Control.Frozen`:
- Your movement script only needs to ask: **"Do they have `State.Control`?"** — Any crowd-control tag under `State.Control` matches automatically, including new ones designers add next month.
- Your *Ice Shatter* passive only needs to ask: **"Do they specifically have `State.Control.Frozen`?"** — Only actual freeze triggers the bonus damage.

---

## What Makes This Library Stand Out?

**ZFramework Gameplay Tags** is built around two goals: **effortless daily workflow** in the Unity Editor and **zero-nonsense speed** at runtime.

- **Visual Tree Editor + One-Click C# Generation**: Manage your project's tag tree in a dedicated Editor window and generate strongly typed `Tags.State__Control__Frozen` fields with one click. You get full IDE autocomplete and compile-time safety—no raw strings scattered across gameplay scripts.
- **Zero-Boilerplate Auto-Initialization**: Enter Play Mode, load a scene, deserialize a Prefab or ScriptableObject, or query a tag—the registry initializes itself automatically on first contact.
- **One Container for Both Inspector & Runtime**: Drop a `[SerializeField] GameplayTagContainer` onto any `MonoBehaviour` or `ScriptableObject`, pick tags from a searchable tree in the Inspector, and call `HasTag` / `AddTag` on that exact same field at runtime. No wrapper classes, no conversion step.
- **4-Byte Integer Hot Paths**: Strings only exist at authoring and loading boundaries. At runtime, every `GameplayTag` is a 4-byte integer handle. Equality checks, parent/child subtree checks, and set queries run over compact contiguous arrays with zero GC allocations in combat loops.
- **Focused & Modular**: It does tag sets, reference-counted stacks, prerequisite rules, and reactive events exceptionally well—without forcing you into a heavyweight ability framework. You can plug it into your existing state machine or combat code in ten minutes.

---

## Table of Contents

- [Installation](#installation)
- [3-Minute Quick Start](#3-minute-quick-start)
- [Six Core Workflows (With Code)](#six-core-workflows-with-code)
  - [1. Category vs. Exact Matching: Handle All Crowd Control with One Check](#1-category-vs-exact-matching-handle-all-crowd-control-with-one-check)
  - [2. Multi-Source Buff Stacking: Fireballs and Lava Pools Play Nice](#2-multi-source-buff-stacking-fireballs-and-lava-pools-play-nice)
  - [3. Equipment & Aura Rollups: Equip and Unequip Without Bookkeeping Bugs](#3-equipment--aura-rollups-equip-and-unequip-without-bookkeeping-bugs)
  - [4. Ability Requirements & Cached Rule Sets: Fast Checks for Entire Action Bars](#4-ability-requirements--cached-rule-sets-fast-checks-for-entire-action-bars)
  - [5. Event-Driven UI & VFX: Stop Polling in Update()](#5-event-driven-ui--vfx-stop-polling-in-update)
  - [6. Set Algebra & Object Pooling: Unions, Intersections, and Zero-GC Scratch Containers](#6-set-algebra--object-pooling-unions-intersections-and-zero-gc-scratch-containers)
- [Using It in Unity: Inspector Fields, Saves & Initialization Control](#using-it-in-unity-inspector-fields-saves--initialization-control)
- [Tag Settings Editor Tips](#tag-settings-editor-tips)
- [Try It Live: Tag Playground Sample](#try-it-live-tag-playground-sample)
- [API Cheat Sheet](#api-cheat-sheet)
- [Standalone .NET & Performance Tips](#standalone-net--performance-tips)

---

## Installation

Requires **Unity 2022.3 or later**.

1. Open **Window → Package Manager**.
2. Click **+ → Add package from git URL...** and enter:
   ```text
   https://github.com/acced/ZFramework.GameplayTags.git#main
   ```
   *(To lock a specific version for your team, replace `#main` with a commit SHA. For local development, choose **Add package from disk...** and select `package.json`.)*
3. If your scripts use custom Assembly Definitions (`.asmdef`), reference **`ZFramework.GameplayTags`** (the generated `ZFramework.GameplayTags.Generated` assembly has `autoReferenced: true` enabled by default; add it explicitly only if your `.asmdef` disables auto-referencing).

---

## 3-Minute Quick Start

```text
┌─────────────────────────────┐    ┌─────────────────────────────┐    ┌─────────────────────────────┐
│ 1. Add Tags in Window       │    │ 2. Click "Generate Code"    │    │ 3. Use in Gameplay Scripts  │
│ Tools → Gameplay Tags       │ ──►│ Creates strongly typed      │ ──►│ Auto-initializes on first   │
│ → Tag Settings              │    │ Tags.* static fields        │    │ use with full autocomplete  │
└─────────────────────────────┘    └─────────────────────────────┘    └─────────────────────────────┘
```

### 1. Add a Few Tags

1. Open **Tools → Gameplay Tags → Tag Settings**.
2. Click **创建标签配置** (*Create Tag Configuration*), which creates `Assets/GameplayTags/Editor/GameplayTagSettings.asset`.
3. Add these five tags:
   ```text
   State.Burning
   State.Control.Frozen
   State.Control.Silenced
   Ability.Cast
   Equipment.Weapon.Staff
   ```
   > **Nice touch**: You only need to enter the leaf tags. Parent nodes like `State`, `State.Control`, `Equipment`, and `Equipment.Weapon` are created automatically and shown as dimmed nodes in the tree.

### 2. Generate C# Fields

Click **生成代码** (*Generate Code*) in the window toolbar (or **Tools → Gameplay Tags → Generate Player Registry**).

Once Unity finishes compiling, every tag (including implied parent nodes) is available under `GameplayTagRegistry.Tags` with dots replaced by double underscores (`__`):
- `State.Control` → `Tags.State__Control`
- `State.Control.Frozen` → `Tags.State__Control__Frozen`

### 3. Run a Quick Test

Attach `QuickStartExample.cs` to any GameObject in your scene and press Play:

```csharp
using GameplayTags;
using UnityEngine;
using Tags = GameplayTags.Generated.GameplayTagRegistry.Tags;

public sealed class QuickStartExample : MonoBehaviour
{
    private void Start()
    {
        // Create a container and apply Frozen
        var states = new GameplayTagContainer();
        states.AddTag(Tags.State__Control__Frozen);

        // Ask broadly: Is the character under any crowd control?
        Debug.Log(states.HasTag(Tags.State__Control));              // True
        Debug.Log(states.HasTag(Tags.State));                       // True

        // Ask specifically: Was State.Control added directly, or Frozen?
        Debug.Log(states.HasTagExact(Tags.State__Control));         // False: parent was not added directly
        Debug.Log(states.HasTagExact(Tags.State__Control__Frozen)); // True: Frozen was explicitly added

        // Clean up
        states.RemoveTag(Tags.State__Control__Frozen);
        Debug.Log(states.IsEmpty);                                  // True
    }
}
```

---

## Six Core Workflows (With Code)

All snippets below assume these two `using` lines at the top of your file:
```csharp
using GameplayTags;
using Tags = GameplayTags.Generated.GameplayTagRegistry.Tags;
```

### 1. Category vs. Exact Matching: Handle All Crowd Control with One Check

When you add `State.Control.Frozen` to a container, the container tracks both the explicit tag and its ancestor chain:

```text
State                       ◄── Implicit ancestor: HasTag is True, HasTagExact is False
└── Control                 ◄── Implicit ancestor: HasTag is True, HasTagExact is False
    └── Frozen              ◄── Explicit member:   HasTag is True, HasTagExact is True
```

- **Use `HasTag` for category questions**: *"Is the character crowd-controlled?"* (`states.HasTag(Tags.State__Control)`). Frozen, stunned, or silenced all match.
- **Use `HasTagExact` for specific questions**: *"Is the character specifically Frozen?"* (`states.HasTagExact(Tags.State__Control__Frozen)`).

```csharp
var states = new GameplayTagContainer();
states.AddTag(Tags.State__Control__Frozen);

int explicitCount = states.ExplicitTagCount; // 1 (only Frozen was added directly)
int totalCount    = states.TagCount;         // 3 (State, State.Control, and State.Control.Frozen)

// Iterate only the tags you explicitly added (logs State.Control.Frozen)
foreach (GameplayTag tag in states.GetExplicitTags())
{
    Debug.Log($"Explicit tag: {tag.Name}");
}

// Default iteration includes implied ancestors (logs State, State.Control, State.Control.Frozen)
foreach (GameplayTag tag in states)
{
    Debug.Log($"All active nodes: {tag.Name}");
}
```

> **Two intuitive rules**:
> - **Parents never imply children**: Adding `State.Control` does not mean the character has `State.Control.Frozen`.
> - **Remove what you added**: `GameplayTagContainer` is an unstacked set. To clear freeze, call `RemoveTag(Tags.State__Control__Frozen)`. Removing an unadded parent (`RemoveTag(Tags.State__Control)`) will not strip `Frozen`.

---

### 2. Multi-Source Buff Stacking: Fireballs and Lava Pools Play Nice

An ordinary set only knows whether a tag is present. When multiple independent sources—say, a **fireball** and a **lava pool**—both apply `State.Burning`, use **`GameplayTagCountContainer`** so each source's contribution is counted separately:

```csharp
var effects = new GameplayTagCountContainer();

// Two independent sources apply Burning
effects.AddTag(Tags.State__Burning); // Fireball hits (+1)
effects.AddTag(Tags.State__Burning); // Steps into lava (+1)

Debug.Log(effects.GetExplicitTagCount(Tags.State__Burning)); // 2 active sources

// The fireball expires after 3 seconds and removes its single stack
effects.RemoveTag(Tags.State__Burning);
Debug.Log(effects.HasTag(Tags.State__Burning));              // True! Still standing in lava

// The character walks out of the lava pool
effects.RemoveTag(Tags.State__Burning);
Debug.Log(effects.HasTag(Tags.State__Burning));              // False: all burning sources are gone
```

- **Add or remove multiple stacks at once**: `AddTag(tag, amount)` and `RemoveTag(tag, amount)`.
- **Single tag count vs. branch total**: `GetExplicitTagCount(tag)` returns how many times that exact tag was added; `GetTagCount(tag)` sums that tag plus all of its descendants (e.g., 2 stacks of `State.Burning` + 1 stack of `State.Control.Frozen` gives `effects.GetTagCount(Tags.State) == 3`).

---

### 3. Equipment & Aura Rollups: Equip and Unequip Without Bookkeeping Bugs

Suppose a **Wizard Staff** grants `Ability.Cast` and an **Arcane Aura** also grants `Ability.Cast`. If your equipment system and aura system both write directly into the character's main container, unequipping items or refreshing buffs can easily corrupt the counts.

With **`GameplayTagHierarchicalContainer`**, each subsystem owns its own child container and points its `Parent` at the character's main container:

```csharp
var actor     = new GameplayTagCountContainer();
var equipment = new GameplayTagHierarchicalContainer { Parent = actor };
var aura      = new GameplayTagHierarchicalContainer { Parent = actor };

// Both subsystems grant Ability.Cast
equipment.AddTag(Tags.Ability__Cast);
aura.AddTag(Tags.Ability__Cast);

Debug.Log(actor.GetTagCount(Tags.Ability__Cast)); // 2: automatically rolled up into actor

// Unequip the staff: detaching Parent withdraws all of equipment's tags in one step
equipment.Parent = null;
Debug.Log(actor.HasTag(Tags.Ability__Cast));       // True (count is 1): the aura is still active!

// Re-equip: reattaching Parent restores all of equipment's tags to actor
equipment.Parent = actor;
Debug.Log(actor.GetTagCount(Tags.Ability__Cast)); // 2
```

---

### 4. Ability Requirements & Cached Rule Sets: Fast Checks for Entire Action Bars

#### Single Ability Check (`GameplayTagRequirements`)

Suppose casting *Pyroblast* requires **Ability.Cast** and **Equipment.Weapon.Staff**, and is blocked by any **State.Control**. Build an immutable rule once and test it anytime:

```csharp
// (forbidden, required1, required2) — inline struct, zero heap allocation
var pyroblastRule = GameplayTagRequirements.Create(
    forbidden:          Tags.State__Control,
    required:           Tags.Ability__Cast,
    additionalRequired: Tags.Equipment__Weapon__Staff);

var actor = new GameplayTagContainer();
actor.AddTag(Tags.Ability__Cast);
actor.AddTag(Tags.Equipment__Weapon__Staff);

Debug.Log(pyroblastRule.Matches(actor)); // True: good to cast

actor.AddTag(Tags.State__Control__Silenced);
Debug.Log(pyroblastRule.Matches(actor)); // False: Silenced matches forbidden State.Control
```

- **Need more conditions?** Use `new GameplayTagRequirements(forbiddenArrayOrSpan, requiredArrayOrSpan)`. It automatically deduplicates tags and simplifies parent/child overlaps, storing up to 4 effective conditions inline in the struct.
- **Check across two containers without merging**: If permanent equipment tags and temporary buff tags live in two separate containers, call `pyroblastRule.Matches(equipmentTags, buffTags)` directly.
- **Quick ad-hoc checks**: You can also call `HasAny(query)`, `HasAll(query)`, `HasAnyExact(query)`, or `HasAllExact(query)` directly on any container.

#### Action-Bar Batch Evaluation & Caching (`GameplayTagRuleSet`)

When an action bar has a dozen skills that all check `State.Control` or `Ability.Cast`, pack them into a shared **`GameplayTagRuleSet`** and give each character a lightweight `Cache`:

```csharp
// 1. Share one RuleSet across all characters with this loadout
var skillRules = new GameplayTagRuleSet(new[]
{
    pyroblastRule,
    new GameplayTagRequirements(
        forbidden: System.Array.Empty<GameplayTag>(),
        required:  new[] { Tags.State__Burning })
});

// 2. Each character keeps its own cache instance
GameplayTagRuleSet.Cache actorRuleCache = skillRules.CreateCache();

// 3. Evaluate every skill on the bar in one call
System.ReadOnlySpan<bool> canUseSkills = actorRuleCache.Evaluate(actor);
bool canCastPyroblast = canUseSkills[0];
bool canUseFireCombo  = canUseSkills[1];
```

Why this is fast:
- As long as no tag has appeared or disappeared on `actor` (even if `Burning` stacks climb from 1 to 5), `Evaluate` **returns the cached boolean array immediately**.
- When a tag is added or removed, it re-checks only the affected conditions and recomputes only the rules that depend on them.

---

### 5. Event-Driven UI & VFX: Stop Polling in Update()

Instead of checking tags every frame in `Update()` to toggle status icons or particle effects, let `GameplayTagCountContainer` notify you when states change:

#### Option 1: Direct Stack or Presence Callbacks

```csharp
var effects = new GameplayTagCountContainer();

OnTagCountChangedDelegate onBurningChanged = (tag, newCount) =>
{
    Debug.Log($"{tag.Name} stacks: {newCount}");
};

// AnyCountChange: fires on every stack change (1 → 2 → 1 → 0)
// NewOrRemoved:   fires only when a tag appears (0 → >0) or disappears (>0 → 0)
effects.RegisterTagEventCallback(
    Tags.State__Burning,
    GameplayTagEventType.AnyCountChange,
    onBurningChanged);

effects.AddTag(Tags.State__Burning);    // Logs: 1
effects.AddTag(Tags.State__Burning);    // Logs: 2
effects.RemoveTag(Tags.State__Burning); // Logs: 1
effects.Clear();                        // Logs: 0 (zeroes all counts and fires 0 callbacks; subscriptions stay intact)

effects.RemoveTagEventCallback(
    Tags.State__Burning,
    GameplayTagEventType.AnyCountChange,
    onBurningChanged);
```

> **Pro tip**: Subscribe to a parent tag like `Tags.State__Control` with `GameplayTagEventType.NewOrRemoved`. Your HUD's "Crowd Controlled!" banner will automatically show when *any* stun/freeze/silence lands and hide only when the last one wears off.

#### Option 2: Grouped UI Bindings (`GameplayTagContainerBinds`)

When binding a UI panel to a character, you usually want to **sync the initial UI state immediately** and **unsubscribe everything in one line** when the panel closes:

```csharp
public sealed class StatusHudView : MonoBehaviour
{
    private GameplayTagContainerBinds m_Binds;

    public void BindActor(GameplayTagCountContainer actorEffects)
    {
        m_Binds.Dispose(); // Clean up previous target bindings if switching targets
        m_Binds = new GameplayTagContainerBinds(actorEffects);

        // Fires immediately with the current state, then again whenever presence changes
        m_Binds.Bind(Tags.State__Burning, isBurning =>
        {
            Debug.Log($"Burning icon visible: {isBurning}");
        });

        m_Binds.Bind(Tags.State__Control, isControlled =>
        {
            Debug.Log($"Crowd-control overlay visible: {isControlled}");
        });
    }

    private void OnDestroy()
    {
        // Unsubscribes all listeners in this group without touching actorEffects
        m_Binds.Dispose();
    }
}
```

---

### 6. Set Algebra & Object Pooling: Unions, Intersections, and Zero-GC Scratch Containers

Combine or compare containers with standard set operations:

```csharp
var left = new GameplayTagContainer();
left.AddTag(Tags.State__Burning);
left.AddTag(Tags.Ability__Cast);

var right = new GameplayTagContainer();
right.AddTag(Tags.State__Control__Frozen);
right.AddTag(Tags.Ability__Cast);

var union        = GameplayTagContainer.Union(left, right);        // Burning, Frozen, Cast
var intersection = GameplayTagContainer.Intersection(left, right); // Cast only
var diff         = left.Clone();
diff.RemoveTags(right);                                            // Burning only
```

For hot paths where you want zero heap allocations, **reuse an output container** or rent a temporary one from **`GameplayTagContainerPool`**:

```csharp
// 1. Write directly into a reusable container (zero allocation)
var reusableOutput = new GameplayTagContainer(capacity: 32);
GameplayTagContainer.Union(reusableOutput, left, right);
reusableOutput.Clear(); // Clears tags while keeping the underlying array capacity

// 2. Rent a temporary container with a using scope; automatically cleared and returned
using (GameplayTagContainerPool.Get(out GameplayTagContainer scratch))
{
    scratch.AddTag(Tags.State__Burning);
    // Use scratch here...
}
```

---

## Using It in Unity: Inspector Fields, Saves & Initialization Control

### Authoring Tags in the Inspector

Serialize `GameplayTagContainer` on any `MonoBehaviour` or `ScriptableObject` to get a searchable TreeView picker right in the Inspector:

```csharp
using GameplayTags;
using UnityEngine;
using Tags = GameplayTags.Generated.GameplayTagRegistry.Tags;

public sealed class CharacterState : MonoBehaviour
{
    // Click "Edit Tags..." in the Inspector to pick initial tags; ready to query on load
    [SerializeField] private GameplayTagContainer m_States = new GameplayTagContainer();

    // Single-tag fields also get a searchable dropdown picker
    [SerializeField] private GameplayTag m_CategoryTag;

    private void Awake()
    {
        // Note: Because a standalone GameplayTag is a value-type struct,
        // call Resolve() and assign it back after loading if you configure a single tag in the Inspector:
        m_CategoryTag = m_CategoryTag.Resolve();
    }

    public bool IsControlled => m_States.HasTag(Tags.State__Control);
    public void ApplyBurning() => m_States.AddTag(Tags.State__Burning);
}
```

- **Shared `ScriptableObject` templates**: If multiple enemies spawn from a shared `ScriptableObject` config containing a `GameplayTagContainer`, call `var runtimeTags = config.InitialTags.Clone();` on spawn so each enemy mutates its own copy.
- **Ready-made `GameObjectGameplayTagContainer` component**: Attach `GameObjectGameplayTagContainer` to any GameObject if you want a built-in serialized tag list backed by a live `GameplayTagCountContainer` at runtime, plus the `gameObject.Bind(tag, callback)` shorthand.

### Saving and Loading State

- **Always save `tag.Name`, never `tag.RuntimeIndex`**: Integer runtime indices are assigned in alphabetical order from the current tag table and will shift when new tags are added to the project. Tag names are stable.
- **`GameplayTagContainer` is directly serializable**: Both Unity's serializer and `DataContractSerializer` save explicit tag names and reconstruct the full hierarchy on load.
- **Saving a `GameplayTagCountContainer`**: Loop over `container.GetExplicitTags()` and save each `tag.Name` paired with `container.GetExplicitTagCount(tag)`. On load, resolve each name via `GameplayTagManager.RequestTag(savedName, out GameplayTag tag)` and restore stacks with `AddTag(tag, savedCount)`.

### Initialization: Automatic by Default, Manual When Wanted

- **Automatic by default (recommended)**: You do not need to call `GameplayTagRegistry.Initialize()` in your scripts. Whether Unity is starting up, deserializing a scene/Prefab/ScriptableObject, or executing your first `Tags.*` / `RequestTag` query, the registry initializes itself automatically on first contact.
- **Explicit prewarm (`GameplayTagRegistry.Initialize()`)**: If you prefer to prewarm systems during a loading screen, you can still call `GameplayTagRegistry.Initialize()` explicitly. **Repeated calls are safe no-ops**—they never rebuild the registry or invalidate existing `GameplayTag` handles.
- **Strict manual mode (`GAMEPLAY_TAGS_MANUAL_INITIALIZATION`)**: If you want to disable auto-initialization completely (for instance, to feed in custom runtime registrations before any tag is touched), add `GAMEPLAY_TAGS_MANUAL_INITIALIZATION` under **Player Settings → Scripting Define Symbols** and call `GameplayTagRegistry.Initialize()` or `GameplayTagManager.Initialize(registrations)` yourself at startup.

---

## Tag Settings Editor Tips

Open **Tools → Gameplay Tags → Tag Settings**:

- **Single project-wide asset**: All tag definitions live in one `GameplayTagSettings.asset` (kept in an `Editor` folder so it never bloats Player builds).
- **Fast tree editing**: Search by full path, add root or child tags, edit descriptions, rename or delete entire branches, and use full `Ctrl+Z` / `Ctrl+Y` Undo/Redo.
- **Promote implicit parents**: Dimmed parent nodes are auto-derived; select one and save a description to promote it to an explicit entry.
- **Compiler-guided renames**: After renaming a branch and clicking **Generate Code**, the C# compiler immediately points out any script still referencing the old `Tags.Old__Name` field so you can update references with confidence.
- **Pre-build safety check**: Before building a Player, the package automatically checks that `GameplayTags.Generated.cs` is in sync with `GameplayTagSettings.asset`. If someone edited tags and forgot to click Generate, the build stops immediately with a clear reminder.

---

## Try It Live: Tag Playground Sample

Want to see everything working in thirty seconds? Import the included console sample:

1. In **Window → Package Manager**, select **ZFramework Gameplay Tags**, expand **Samples**, and click **Import** next to **Tag Playground**.
   *(If your project already has a `GameplayTagSettings.asset`, add the sample's five `Demo.*` tags to your existing settings and delete the sample's SO asset.)*
2. Run **Tools → Gameplay Tags → Generate Player Registry**.
3. Open `TagPlayground.unity` and press Play to watch hierarchical matching, casting requirements, dual-source burning stacks, and `1 → 2 → 1 → 0` event callbacks log to the Console.

---

## API Cheat Sheet

| What You Want to Do | Type / Method |
| :--- | :--- |
| **Hold a lightweight tag handle** | [`GameplayTag`](Runtime/GameplayTag.cs) — 4-byte ID equality, `ParentTag`, `ParentTags`, `ChildTags`, `IsParentOf`, `IsChildOf` |
| **Store a set of tags (unstacked, serializable)** | [`GameplayTagContainer`](Runtime/GameplayTagContainer.cs) — `AddTag`, `RemoveTag`, `HasTag`, `HasTagExact`, `HasAny`, `HasAll`, `Union`, `Intersection`, `Clone` |
| **Track multi-source stacks & events** | [`GameplayTagCountContainer`](Runtime/GameplayTagCountContainer.cs) — `AddTag(tag, amount)`, `GetExplicitTagCount`, `GetTagCount`, `RegisterTagEventCallback` |
| **Roll up equipment/aura tags into an actor** | [`GameplayTagHierarchicalContainer`](Runtime/GameplayTagHierarchicalContainer.cs) — set `Parent` to automatically contribute & withdraw counts |
| **Check required & forbidden ability tags** | [`GameplayTagRequirements`](Runtime/GameplayTagRequirements.cs) — `Create(forbidden, req1, req2)`, `Matches(actor)`, `Matches(equip, buffs)` |
| **Batch-evaluate & cache an action bar of rules** | [`GameplayTagRuleSet`](Runtime/GameplayTagRuleSet.cs) & `GameplayTagRuleSet.Cache` — `CreateCache()`, `cache.Evaluate(actor)` |
| **Bind & dispose grouped UI listeners** | [`GameplayTagContainerBinds`](Runtime/GameplayTagContainerBinds.cs) — `Bind(tag, Action<bool>)`, `Dispose()`, `gameObject.Bind()` |
| **Borrow temporary containers with zero GC** | [`GameplayTagContainerPool`](Runtime/GameplayTagContainerPool.cs) — `using (GameplayTagContainerPool.Get(out var temp))` |
| **Look up external tag names or custom registries** | [`GameplayTagManager`](Runtime/GameplayTagManager.cs) — `RequestTag(name, out tag)`, `GetAllTags()`, `Initialize(...)` |

---

## Standalone .NET & Performance Tips

### Running on Dedicated .NET Servers or Unit Tests

The core runtime has zero hard dependencies on Unity and builds cleanly as a standard **.NET Standard 2.1 / C# 9** library via `GameplayTags.Runtime.csproj`:

```bash
dotnet build GameplayTags.Runtime.csproj -c Release
```

If you are not compiling the generated `GameplayTagRegistry` into your server/test project, pass your registrations to `GameplayTagManager.Initialize` once at startup:

```csharp
GameplayTagManager.Initialize(
    new GameplayTagRegistration("State.Burning", "Damage over time"),
    new GameplayTagRegistration("State.Control.Frozen", "Cannot move or cast"),
    new GameplayTagRegistration("Ability.Cast", "Can cast spells"));

GameplayTag burningTag = GameplayTagManager.RequestTag("State.Burning");
```

### Habits for Peak Performance

- **Use `Tags.*` in gameplay loops**: `RequestTag("...")` is meant for cold boundaries like save/load or data-table parsing. In regular gameplay code, use the generated `Tags.*` static fields.
- **Pass valid handles & counts**: Add/remove registered non-`None` tags, pass positive stack amounts, and remove only the stacks your system added. Treat `GameplayTagContainer.Empty` as read-only.
- **Capacity reservation**: `EnsureCapacity(n)` reserves space for **distinct tags including implied ancestors**, not just leaf tags.
- **Threading**: Registry initialization is thread-safe. Containers, events, and `RuleSet.Cache` are designed for single-threaded gameplay logic (`GameplayTagContainerPool` is `[ThreadStatic]`—return rented containers on the same thread).

---

## License & Feedback

Released under the [MIT License](LICENSE). Questions, ideas, and bug reports are always welcome in [GitHub Issues](https://github.com/acced/ZFramework.GameplayTags/issues)!
