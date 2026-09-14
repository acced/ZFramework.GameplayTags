<h1 align="center">ZFramework Gameplay Tags</h1>

<p align="center">Hierarchical gameplay tags for Unity.<br>Define tags once. Use them in code, components, and gameplay queries.</p>

<p align="center">
  <a href="./README_CN.md">CN · 简体中文</a> · <a href="./README.md"><strong>EN · English</strong></a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Unity-2021.3%2B-222222?logo=unity" alt="Unity 2021.3 or later">
  <img src="https://img.shields.io/badge/Package-UPM-3178C6" alt="Unity Package Manager">
  <a href="./LICENSE.md"><img src="https://img.shields.io/badge/License-MIT-22A06B" alt="MIT License"></a>
</p>

**[ZFramework Gameplay Tags](https://github.com/acced/ZFramework.GameplayTags)** is an Unreal-style tag system for Unity. Use names such as `Ability.Attack.Melee` or `State.Debuff.Stunned` to describe abilities, states, damage types, and other gameplay concepts.

- **Hierarchy matching** — child tags match their parents; parent tags are registered automatically.
- **Tag containers** — store unique tags, check any/all conditions, and filter tag sets.
- **Composable queries** — combine tag and expression conditions with any, all, and none semantics.
- **Editor tools** — manage definitions, choose tags in the Inspector, and import/export CSV.
- **Generated C# API** — access tags through named fields with IDE completion.
- **Definition validation** — check names, redirects, sources, and restricted hierarchies before a build.

[Installation](#installation) · [Quick start](#quick-start) · [Usage](#usage) · [Editor guide](#editor-guide) · [Configuration](#configuration) · [Samples](#samples) · [FAQ](#faq)

<a id="installation"></a>
## Installation

Requires **Unity 2021.3 or later**, as declared in `package.json`. The runtime assembly has no references to other ZFramework packages.

### Local package

1. Download or clone [the repository](https://github.com/acced/ZFramework.GameplayTags) and locate the package folder containing `package.json`.
2. Open **Window → Package Manager** in Unity.
3. Select **+ → Add package from disk…** and choose that `package.json`.
4. Wait for Unity to import and compile the package.

Alternatively, copy the whole package folder into your Unity project:

```text
YourUnityProject/
└── Packages/
    └── zframework.gameplaytag/
        ├── package.json
        ├── Runtime/
        ├── Editor/
        └── Samples~/
```

If it already exists at `Packages/zframework.gameplaytag`, it is an embedded package and needs no additional installation.

### Git URL

In Unity Package Manager, choose **+ → Add package from git URL…**. When the package's `package.json` is at the repository root, use:

```text
https://github.com/acced/ZFramework.GameplayTags.git
```

If the repository contains the full Unity project and the package is under `Packages/zframework.gameplaytag`, use this URL instead:

```text
https://github.com/acced/ZFramework.GameplayTags.git?path=/Packages/zframework.gameplaytag
```

Git must be installed for this method. The package files must be committed to the repository at the location used by the URL.

### Assembly definitions

For scripts under your own `.asmdef`, add **GameplayTags** to its **Assembly Definition References**. Runtime code uses:

```csharp
using GameplayTags;
```

<a id="quick-start"></a>
## Quick start

### 1. Create the settings asset

Open **Tools → ZFramework → Gameplay Tags → Create Settings**. The default asset is created at:

```text
Assets/Resources/GameplayTags/GameplayTagSettings.asset
```

Keep it at this location when using default runtime loading. Commit this asset and its `.meta` file with your project.

### 2. Define some tags

Open **Tools → ZFramework → Gameplay Tags → Manager**. On the **Tags** tab, enter each name in **Add Tag**, then click **Add**:

```text
Sample.State.Alive
Sample.State.Debuff.DamageOverTime.Poisoned
Sample.State.Debuff.Control.Stunned
```

Parents such as `Sample.State.Debuff` are registered implicitly; you do not need to add separate definitions for them. Names are case-sensitive. Use dot-separated segments without spaces or empty segments.

### 3. Run a component

Create `GameplayTagsQuickStart.cs` with the following code, attach it to any GameObject, and enter Play Mode:

```csharp
using GameplayTags;
using UnityEngine;

public sealed class GameplayTagsQuickStart : MonoBehaviour
{
    private void Awake()
    {
        GameplayTag alive = GameplayTagManager.RequestTag("Sample.State.Alive");
        GameplayTag poisoned = GameplayTagManager.RequestTag(
            "Sample.State.Debuff.DamageOverTime.Poisoned");
        GameplayTag stunned = GameplayTagManager.RequestTag(
            "Sample.State.Debuff.Control.Stunned");
        GameplayTag debuff = GameplayTagManager.RequestTag("Sample.State.Debuff");

        var owned = new GameplayTagContainer();
        owned.AddTag(alive);
        owned.AddTag(poisoned);

        Debug.Log(owned.HasTag(debuff));       // True: poisoned is a descendant.
        Debug.Log(owned.HasTagExact(debuff));  // False: debuff itself was not added.

        var canAct = new GameplayTagQuery(
            GameplayTagQueryExpression.AllExpressionsMatch()
                .AddExpression(GameplayTagQueryExpression.AllTagsMatch().AddTag(alive))
                .AddExpression(GameplayTagQueryExpression.NoTagsMatch().AddTag(stunned)),
            "Alive and not stunned");

        Debug.Log(canAct.Matches(owned));      // True
        owned.AddTag(stunned);
        Debug.Log(canAct.Matches(owned));      // False
    }
}
```

With the default settings, the registry initializes before the first scene loads. Requesting a tag looks it up in the registry; it does not create a new definition.

<a id="usage"></a>
## Usage

### Match tags and containers

Hierarchy matching goes from **child to parent**. Using the variables from the quick start:

```csharp
poisoned.MatchesTag(debuff);       // true
debuff.MatchesTag(poisoned);       // false
poisoned.MatchesTagExact(debuff);  // false
```

| API | Behavior |
| --- | --- |
| `owned.AddTag(tag)` | Adds a registered tag; returns `false` for duplicates or unregistered tags. |
| `owned.RemoveTag(tag)` | Removes that exact tag. |
| `owned.HasTag(tag)` | Matches the tag itself or a stored descendant. |
| `owned.HasTagExact(tag)` | Matches only the exact stored name. |
| `owned.HasAny(other)` / `owned.HasAll(other)` | Checks any/all tags in another container, including hierarchy matches. |
| `owned.HasAnyExact(other)` / `owned.HasAllExact(other)` | Checks any/all using exact names. |
| `owned.Filter(other)` | Returns stored tags that match any tag in `other`. |
| `query.Matches(owned)` | Evaluates a query against the container. |

For optional lookups, use `TryRequestTag` to handle missing names without an exception:

```csharp
if (GameplayTagManager.TryRequestTag("Sample.State.Alive", out GameplayTag tag))
{
    Debug.Log(tag.Name);
}
```

`RequestTag(name)` throws when the name cannot be resolved. `RequestTag(name, false)` returns `GameplayTag.None` instead. `IsValid` checks whether a tag has a nonempty name; use `GameplayTagManager.IsRegistered(tag)` to check current registry membership.

### Build queries

| Factory | Meaning |
| --- | --- |
| `AnyTagsMatch()` | At least one listed tag matches. |
| `AllTagsMatch()` | Every listed tag matches. |
| `NoTagsMatch()` | None of the listed tags match. |
| `AnyExpressionsMatch()` | At least one child expression succeeds. |
| `AllExpressionsMatch()` | Every child expression succeeds. |
| `NoExpressionsMatch()` | No child expression succeeds. |

Use `.AddTag(tag)` with tag expressions and `.AddExpression(expression)` with expression groups, as in the quick start. Tag conditions use hierarchy matching. A query with no root expression returns `false`.

### Select tags in the Inspector

Create `Skill.cs` and attach it to a GameObject:

```csharp
using GameplayTags;
using UnityEngine;

public sealed class Skill : MonoBehaviour
{
    [SerializeField] private GameplayTag damageTag;
    [SerializeField] private GameplayTagContainer ownedTags = new GameplayTagContainer();
    [SerializeField] private GameplayTagQuery activationQuery = new GameplayTagQuery();
}
```

Select the GameObject in **Hierarchy**, then edit these fields under **Inspector → Skill**. Custom property drawers provide a single-tag picker, a container editor, and a query editor. The same field types work in `ScriptableObject` assets.

These selections belong to the component or asset being inspected. Manage the project's available tag definitions in **Gameplay Tag Manager**.

### Generate a C# API

1. Open **Edit → Project Settings… → ZFramework → Gameplay Tags**.
2. Set **Generated Namespace**, **Generated Class Name**, and **Generated Code Path**.
3. Use an output file under `Assets/`, such as `Assets/Scripts/Generated/GameplayTags.gen.cs`.
4. Save the project, then click **Generate C# API** and wait for compilation.

With the default namespace `Game` and class name `GameplayTags`, a gameplay method can use:

```csharp
GameplayTag alive = Game.GameplayTags.Sample_State_Alive;
```

Dots become underscores in generated field names. Conflicting names receive suffixes such as `_2`; the original tag names stay unchanged. Generated fields resolve registered tags during static initialization.

Regenerate after editing definitions or generation options. Generation overwrites the configured file. If you move the output path, remove the obsolete generated `.cs` asset through Unity to avoid duplicate class definitions.

If your gameplay scripts use an `.asmdef`, generate into that assembly's folder, or into a separate assembly they reference. A custom assembly cannot reference `Assembly-CSharp`, which is where scripts outside any `.asmdef` normally compile. The assembly containing generated code must reference **GameplayTags**.

<a id="editor-guide"></a>
## Editor guide

| Task | Where to go |
| --- | --- |
| Create the default settings asset | **Tools → ZFramework → Gameplay Tags → Create Settings** |
| Add, rename, or delete definitions | **Tools → ZFramework → Gameplay Tags → Manager → Tags** |
| Configure old-name mappings | **Manager → Redirects** |
| Add source groups and view ownership | **Manager → Sources** |
| Set initialization and code output options | **Edit → Project Settings… → ZFramework → Gameplay Tags** |
| Choose tags for a component or asset | Its **Inspector** |
| Generate code | **Tools → ZFramework → Gameplay Tags → Generate C# API**, or **Generate** in Manager |
| Validate definitions | **Tools → ZFramework → Gameplay Tags → Validate**, or **Validate** in Manager |

Manager is also available through **Window → ZFramework → Gameplay Tag Manager**. Its **Generate** button uses the options configured in Project Settings.

### Rename tags and use redirects

Select a definition, edit **Name**, and click **Rename Hierarchy**. This renames the tag and its defined descendants, and creates redirects for renamed definitions. **Apply Metadata** applies the comment, source, and restriction options.

Redirects allow `RequestTag` / `TryRequestTag` to resolve an old name to its current name. They do not rewrite every existing component or asset. An old name must no longer be an active tag, and its redirect chain must end at a registered tag without forming a cycle.

### Sources and restricted tags

Use **Sources** to add groups such as `Game` or `Combat` with an owner and a read-only flag. Assign a tag's source in **Tags**, then click **Apply Metadata**. Editor operations reject changes to tags in read-only sources. A restricted parent can prohibit non-restricted descendants when **Allow Non-restricted Children** is disabled.

### Import and export CSV

Use **Import CSV** / **Export CSV** in Manager. The column order is:

```csv
Tag,Comment,Source,Restricted,AllowNonRestrictedChildren
Sample.State.Alive,Character is alive,Default,false,true
Sample.State.Debuff.Control.Stunned,Prevents actions,Default,false,true
```

Import adds new names and replaces definitions with matching names; tags absent from the CSV remain unchanged. New source names are added as editable sources. The imported result is validated before applying changes. Export includes tag definitions and their source names, but not redirects or source ownership/read-only metadata.

<a id="configuration"></a>
## Configuration

Open **Edit → Project Settings… → ZFramework → Gameplay Tags**. All six options are stored in the `GameplayTagSettings` asset.

| Field | Default | Effect |
| --- | --- | --- |
| Auto Initialize | `true` | Initializes before the first scene loads. Disabling it skips startup initialization; registry APIs can still initialize on first use. |
| Include Implicit Parent Tags In Generated Code | `true` | Generates fields for automatically registered parents as well as explicit definitions. Runtime parent matching is unchanged. |
| Warn On Invalid Serialized Tags | `true` | Warns when `AddTag(GameplayTag)` rejects an unregistered nonempty name, once per name until registry initialization resets the warning history. Does not scan all assets. |
| Generated Namespace | `Game` | Namespace of the generated class. Empty means no namespace. |
| Generated Class Name | `GameplayTags` | Name of the generated static class. |
| Generated Code Path | `Assets/GameScripts/Main/Generated/GameplayTags.gen.cs` | Destination of the generated C# file. |

The readouts show **Defined Tags**, **Registered Tags** including parents, **Redirects**, and **Content Hash**. The hash covers tag definitions and redirects; generation options are excluded, so it is not a complete check for whether generated code needs updating.

After changing options, use **File → Save Project**. This settings page marks the asset dirty without explicitly saving it to disk. Generate again when you change generation options.

<a id="samples"></a>
## Samples

In **Window → Package Manager**, select **ZFramework Gameplay Tags**, expand **Samples**, and import **Basic Usage**. Define the three `Sample.*` tags from the quick start, then attach `BasicGameplayTagsExample` to a GameObject and enter Play Mode.

Browse the [sample instructions](./Samples~/BasicUsage/README.md) or [sample source](./Samples~/BasicUsage/BasicGameplayTagsExample.cs) directly.

<a id="faq"></a>
## FAQ

**Where is Generated Code Path?**  
In **Project Settings → ZFramework → Gameplay Tags**, below **Generated Class Name**. The tag list window is Manager; use Project Settings for output options.

**Why does a tag appear in the Inspector?**  
`GameplayTag` is serializable and has a custom property drawer. Put a public field or a private `[SerializeField]` field on a component or `ScriptableObject`. A script must be attached to a GameObject to appear as a scene component.

**Why does RequestTag fail?**  
Check the name and casing, define the tag in Manager, and keep the default settings asset under `Resources/GameplayTags/GameplayTagSettings.asset`. The editor can find a moved asset that default runtime loading cannot find. Use `TryRequestTag` when absence is expected.

**Why does a build fail validation?**  
Run **Tools → ZFramework → Gameplay Tags → Validate** and fix the reported configuration errors. Build validation requires a settings asset and checks definitions, sources, restrictions, and redirects. It does not automatically generate the C# API.

<a id="license"></a>
## License

[MIT](./LICENSE.md).
