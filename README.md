# ZFramework Gameplay Tags

**[简体中文](#cn) | [English](#en)**

Unity 2022.3+ · C# · [MIT](LICENSE)

<a name="cn"></a>
## 简体中文

**让角色状态、技能条件和 Buff 使用同一套标签。**

ZFramework Gameplay Tags 是一个面向 Unity 的轻量级 C# 游戏标签库，提供标签编辑、代码生成、层级匹配、引用计数和变化通知。

你可以在编辑器里维护 `State.Burning`、`State.Control.Frozen`、`Ability.Cast`，生成对应的 C# 字段。战斗系统负责添加状态，技能系统检查条件，UI 订阅变化；它们只需要认识标签，不必互相了解实现。

从配置到运行，使用流程只有四步：

```text
在 Tag Settings 编辑标签 → 生成注册表和字段 → 启动时初始化 → 使用容器查询和修改
```

<a name="cn-features"></a>
### 核心特性

- **可视化编辑。** TreeView 支持搜索、增删改、分支重命名和 Undo/Redo，标签集中保存在独立 SO 配置中。
- **生成即可使用。** 从配置生成注册表与只读标签字段，包含自动补齐的父标签。Editor 与 Player 的运行态使用同一份生成数据，不再扫描程序集特性。
- **层级匹配。** 冰冻和沉默都可以归入 `State.Control`，一条条件就能表达“受到任意控制”。
- **叠加与通知。** 用计数容器管理多个 Buff 来源，最后一份贡献移除时才取消状态；通过事件更新图标、动画或技能栏。
- **集合与复用。** 支持复制、并集、显式交集、批量删除和目标容器复用，提供由调用方配置策略的线程本地池。
- **固定规则与缓存。** 把技能要求预先构造成规则，共享条件查询，并为不同角色分别缓存判断结果。

它适合角色状态、装备能力、技能前置条件和效果叠加，不负责伤害计算、持续时间或冷却。一个独立开关直接用 `bool` 即可；需要跨系统组合状态时，再使用标签。

### 文档导航

[安装](#cn-installation) · [快速上手](#cn-quick-start) · **[初始化与加载](#cn-loading)** · [常用操作](#cn-usage) · [Inspector 与保存](#cn-serialization) · [对象池](#cn-pooling) · [示例](#cn-sample) · [升级说明](#cn-migration) · [API](#cn-api)

<a name="cn-installation"></a>
### 安装

Unity 包目标为 **Unity 2022.3 及以上**。在 Package Manager 中选择 **+ → Add package from git URL**，输入：

```text
https://github.com/acced/ZFramework.GameplayTags.git#main
```

本地安装则选择 **Add package from disk**，打开包内的 `package.json`。团队项目可以将 `#main` 替换成完整提交号，固定使用的版本。

代码命名空间为 `GameplayTags`。业务使用 `.asmdef` 时，引用 `ZFramework.GameplayTags`；完成下面的代码生成后，再引用 `ZFramework.GameplayTags.Generated` 以使用生成字段。不要同时安装其他定义同名类型的旧包。

<a name="cn-quick-start"></a>
### 快速上手

#### 1. 创建标签配置

打开 **Tools → Gameplay Tags → Tag Settings**，点击“创建标签配置”。默认位置为：

```text
Assets/GameplayTags/Editor/GameplayTagSettings.asset
```

添加以下五个标签，可以给每个标签填写说明：

```text
State.Burning
State.Control.Frozen
State.Control.Silenced
Ability.Cast
Equipment.Weapon.Staff
```

父节点会自动补齐，不必另外声明 `State`、`State.Control` 等名称。项目只使用一份 `GameplayTagSettings`；它是标签定义的来源，不是某个角色的当前状态。

#### 2. 生成代码

在窗口点击 **“生成代码”**，等待 Unity 编译完成。也可以通过 **Tools → Gameplay Tags → Generate Player Registry** 执行同一个生成操作。

生成目录包含：

```text
Assets/GameplayTags.Generated/
├── GameplayTags.Generated.cs
└── ZFramework.GameplayTags.Generated.asmdef
```

注册入口是 `GameplayTags.Generated.GameplayTagRegistry.Initialize()`；字段位于 `GameplayTagRegistry.Tags`。例如 `State.Control.Frozen` 对应 `Tags.State__Control__Frozen`。点号变成双下划线，原有下划线和其他字符会转义，以避免字段重名。

不需要手写程序集特性、缓存类或生成文件。修改标签后重新生成；代码中的字段引用也会帮助你发现改名后的使用位置。

#### 3. 给角色添加一个状态

创建 `TagExample.cs`，挂到一个空物体上。这个独立示例在初始化后创建容器：

```csharp
using GameplayTags;
using GameplayTags.Generated;
using UnityEngine;
using Tags = GameplayTags.Generated.GameplayTagRegistry.Tags;

public sealed class TagExample : MonoBehaviour
{
    private void Awake()
    {
        GameplayTagRegistry.Initialize();

        var states = new GameplayTagContainer();
        states.AddTag(Tags.State__Control__Frozen);

        Debug.Log(states.HasTag(Tags.State__Control));      // True
        Debug.Log(states.HasTagExact(Tags.State__Control)); // False
        Debug.Log(states.HasTagExact(Tags.State__Control__Frozen)); // True

        states.RemoveTag(Tags.State__Control__Frozen);
        Debug.Log(states.IsEmpty); // True
    }
}
```

这里只放一个示例组件。正式项目应把初始化集中到启动入口，**不要在每个角色的 `Awake` 重复初始化**。场景或资源中已经保存了容器成员时，按下一节安排加载。

<a name="cn-loading"></a>
### 初始化与加载：先注册，再加载内容

**`GameplayTagContainer` 既能在 Inspector 中保存成员，也能直接作为运行时容器。无需另一套数据类型，也无需加载后再手动转换。**

它保存的是显式标签名称，反序列化时会根据注册表恢复整数成员。因此，使用顺序应当是：

```text
启动入口 → 初始化生成注册表 → 访问缓存字段 → 加载场景／Prefab／SO／存档 → 使用容器
```

一个直接的做法是准备 **Boot** 和 **Game** 两个场景。都加入构建列表，Boot 放在第一位；Boot 只放启动逻辑，不提前引用或加载携带标签容器的游戏内容。

```csharp
using GameplayTags.Generated;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class GameBootstrap : MonoBehaviour
{
    private void Awake()
    {
        GameplayTagRegistry.Initialize();
        SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);
    }
}
```

顺序的保证来自：**初始化完成之后，才开始加载 Game。** 使用 Resources、Addressables、AssetBundle 或自定义存档加载器时，也把初始化放在第一次相关加载／反序列化操作之前，而不是等资源已经加载完成再补做。

直接打开 Game 点击 Play 会绕过 Boot，需要项目提供等价的启动流程。生成字段不能在初始化前被静态字段提前访问。注册表在一个进程／脚本域中准备一次；切换场景不需要重建，存活的容器和句柄也不能跨注册表替换继续使用。

<details>
<summary>Editor、预加载资产与 Domain Reload</summary>

Tag Settings 和标签选择器读取 SO 中的名称，不会替你初始化运行时注册表。Editor 工具要读取会恢复容器的资产时，应先准备同一份生成注册表；Player 的 Boot 不覆盖 Editor 资产导入、对象复制和脚本重载。预加载资产也不能绕过上述顺序。

不要把 Script Execution Order 或 `BeforeSceneLoad` 当成“反序列化之前”：Unity 文档说明 `BeforeSceneLoad` 时首场景对象已加载，只是尚未执行 `Awake`；序列化回调还可能在非主线程运行。这份库约定注册表就绪的加载路径，不自动修复任意 Editor 顺序。恢复未完成的容器不要继续按有效空容器保存，应修正时序并重新加载原资源。

关闭 Domain Reload 时，静态注册表、生成字段和池可能跨 Play Mode 保留。保持同一注册表并清理业务会话；配置变化后，在干净的脚本域重新初始化，不只替换注册表而保留旧句柄。

参考 Unity 文档：[BeforeSceneLoad](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/RuntimeInitializeLoadType.BeforeSceneLoad.html)、[序列化回调](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ISerializationCallbackReceiver.html)、[Domain Reloading](https://docs.unity3d.com/2022.3/Documentation/Manual/DomainReloading.html)。

</details>

<a name="cn-usage"></a>
### 常用操作

下列片段放在业务方法中，使用 `using GameplayTags;` 和 `using Tags = GameplayTags.Generated.GameplayTagRegistry.Tags;`，并在注册表初始化后执行。

#### 层级匹配与显式成员

添加 `State.Control.Frozen` 后，容器同时能匹配 `State.Control` 和 `State`。但只有 Frozen 是直接加入的成员：

```csharp
var states = new GameplayTagContainer();
states.AddTag(Tags.State__Control__Frozen);

bool controlled = states.HasTag(Tags.State__Control);      // True
bool explicitControl = states.HasTagExact(Tags.State__Control); // False
int explicitCount = states.ExplicitTagCount; // 1
int totalCount = states.TagCount;            // 3

foreach (GameplayTag tag in states.GetExplicitTags()) { /* Frozen */ }
foreach (GameplayTag tag in states) { /* State, Control, Frozen */ }
```

这个关系不是双向的：只添加 Control 不代表存在 Frozen。普通容器是集合，重复添加不叠层；删除隐式父标签也不会删除后代。枚举无序，期间不要修改；UI 或导出需要稳定顺序时，再按名称排序。

#### 技能条件：有法杖、能施法、没受控制

```csharp
var canCast = GameplayTagRequirements.Create(
    Tags.State__Control, Tags.Ability__Cast, Tags.Equipment__Weapon__Staff);

var actor = new GameplayTagContainer();
actor.AddTag(Tags.Ability__Cast);
actor.AddTag(Tags.Equipment__Weapon__Staff);
bool available = canCast.Matches(actor); // True

actor.AddTag(Tags.State__Control__Silenced);
bool silenced = canCast.Matches(actor);  // False
```

三参数依次是一个禁止标签、两个必需标签。更多条件使用 `new GameplayTagRequirements(forbidden, required)`，传入标签数组或 span。规则是不可变快照，在技能加载时创建，配置变化后重新创建。

装备和临时状态分开存放时，`canCast.Matches(equipment, effects)` 可以跨两个持有者满足要求，不必先分配并集。简单查询也可用 `HasAny`、`HasAll` 及其 `Exact` 版本；空查询的 Any 为 false，All 为 true。

多条规则共享条件时，可以共享规则组，为每个角色单独创建缓存：

```csharp
var rules = new GameplayTagRuleSet(new[]
{
    canCast,
    new GameplayTagRequirements(
        System.Array.Empty<GameplayTag>(), new[] { Tags.State__Burning })
});
var cache = rules.CreateCache();
System.ReadOnlySpan<bool> results = cache.Evaluate(actor);
bool castEnabled = results[0];
```

未失效时复用结果；存在性变化后刷新不同条件，只重算受影响的规则。返回的是借用视图，下次评估会覆盖；缓存不跨线程共享，也不应每帧重新创建。

#### Buff 叠加与变化通知

两个来源都造成燃烧时，用 `GameplayTagCountContainer`，移除一份贡献不会误删另一份：

```csharp
var effects = new GameplayTagCountContainer();
OnTagCountChangedDelegate onBurning = (tag, count) =>
    UnityEngine.Debug.Log($"{tag.Name}: {count}");

effects.RegisterTagEventCallback(
    Tags.State__Burning, GameplayTagEventType.AnyCountChange, onBurning);
effects.AddTag(Tags.State__Burning);    // 1
effects.AddTag(Tags.State__Burning);    // 2
effects.RemoveTag(Tags.State__Burning); // 1
effects.RemoveTag(Tags.State__Burning); // 0
effects.RemoveTagEventCallback(
    Tags.State__Burning, GameplayTagEventType.AnyCountChange, onBurning);
```

`GetExplicitTagCount(tag)` 读取自身次数，`GetTagCount(tag)` 还包含后代贡献。`AddTag(tag, amount)` 和 `RemoveTag(tag, amount)` 支持一次处理多层；数量和贡献所有权由业务管理。批量 `AddTags(source)`／`RemoveTags(source)` 每个不同显式来源标签各处理一次，不照搬来源层数。

只关心出现和消失时订阅 `NewOrRemoved`；订阅父标签可观察整个类别。直接注册不立即报告当前值；`GameplayTagContainerBinds.Bind` 会报告当前存在性，并能成组取消订阅。绑定组随 UI 存活，关闭 UI 时释放；`Clear()` 清成员但保留容器订阅。

#### 装备和光环各自贡献，角色统一查询

```csharp
var actor = new GameplayTagCountContainer();
var equipment = new GameplayTagHierarchicalContainer { Parent = actor };
var aura = new GameplayTagHierarchicalContainer { Parent = actor };

equipment.AddTag(Tags.Ability__Cast);
aura.AddTag(Tags.Ability__Cast);  // actor 中有两份贡献

equipment.Parent = null;       // 只撤销装备的贡献，光环仍然有效
aura.Clear();
aura.Parent = null;
```

这里是子容器向父容器汇总，不是继承父容器的标签。子容器仍挂接时，不要直接清空聚合父容器；由贡献的拥有者执行移除，父链也不能成环。

#### 复制、集合运算与目标复用

```csharp
var left = new GameplayTagContainer();
left.AddTag(Tags.State__Burning);
left.AddTag(Tags.Ability__Cast);
var right = new GameplayTagContainer();
right.AddTag(Tags.State__Control__Frozen);
right.AddTag(Tags.Ability__Cast);

var union = GameplayTagContainer.Union(left, right);
var intersection = GameplayTagContainer.Intersection(left, right); // Cast
var difference = left.Clone();
difference.RemoveTags(right); // Burning

var output = new GameplayTagContainer(32);
GameplayTagContainer.Copy(output, left);
GameplayTagContainer.Union(output, left, right);
GameplayTagContainer.Intersection(output, left, right);
output.Clear(); // 保留容量供下次使用
```

集合运算针对显式成员，再推导祖先。Burning 和 Frozen 不会因为同属 State，就交出一个显式 State。三参数并集／交集允许目标与任一输入相同；`AddTags`、`IntersectWith` 可原地操作。`EnsureCapacity` 按包含祖先的不同成员数预留，不按叠层次数预留。

<a name="cn-serialization"></a>
### Inspector 配置与存档

**继续使用同一个 `GameplayTagContainer`。** 注册表准备好后加载内容，容器回调会恢复成员，运行时直接使用：

```csharp
using GameplayTags;
using UnityEngine;
using Tags = GameplayTags.Generated.GameplayTagRegistry.Tags;

public sealed class CharacterState : MonoBehaviour
{
    [SerializeField] private GameplayTagContainer states = new GameplayTagContainer();
    public GameplayTagContainer TagsInUse => states;

    public void ApplyBurning() => states.AddTag(Tags.State__Burning);
    public void RemoveBurning() => states.RemoveTag(Tags.State__Burning);
}
```

把它放在 Boot 之后加载的 Game 内容中，而不是依赖这个组件自己的 `Awake` 才初始化。名称是保存形式，整数存储是计算形式，不需要额外数据类或 `LoadInto()`。

共享 SO 模板需要每个角色独立修改时，用 `templateTags.Clone()`，或者 `GameplayTagContainer.Copy(runtimeTags, templateTags)` 写入已有目标。修改运行时实例不等于自动保存磁盘资产，写入时机由业务和 Unity 决定。

独立的 `GameplayTag` 字段加载或编辑后，在注册表就绪的边界执行 **`tag = tag.Resolve()`**，接住返回的结构体。存档保存显式名称；计数存档另外保存显式次数，不保存 `RuntimeIndex`，也不把祖先累计量作为独立贡献重放。未知名称不会恢复为成员。

<a name="cn-pooling"></a>
### 临时计算与对象池

长期重复使用的工作容器直接持有并 `Clear()` 即可。需要短作用域借还时：

```csharp
using (GameplayTagContainerPool.Get(out GameplayTagContainer temporary))
{
    temporary.AddTag(Tags.State__Burning);
    // 在这个作用域内使用 temporary。
}
```

默认不限制保留预算。可以在负责借还的线程、加载阶段选择策略：

```csharp
GameplayTagContainerPool.Configure(new GameplayTagContainerPool.RetentionPolicy(
    maxCount: 32, maxContainerBytes: 256 * 1024, maxTotalBytes: 4L * 1024 * 1024));
```

这些数值只是示例。只限数量可用 `new RetentionPolicy(32)`；不保留用 `RetentionPolicy.Disabled`，不限预算用 `RetentionPolicy.Unbounded`。`Configure` 会清理当前线程缓存，池的 `Clear()` 释放缓存而保留策略。`RetainedDataBytes` 只统计数组载荷，不是完整堆占用。

同线程借还一次，不复制租约重复释放，不使用已归还的引用。默认不限预算可能保留峰值内存，策略由项目选择。

<a name="cn-sample"></a>
### 示例：Tag Playground

包内附带[一个脚本、一个场景的日志示例](Samples~/TagPlayground/README.md)，演示匹配、施法条件、两个燃烧来源和 `1 → 2 → 1 → 0` 的通知。

先安装包并等 Editor 工具编译完成，再从 Package Manager 的 **Samples → Tag Playground** 导入。示例附带自己的 SO，使用 `Demo.*` 标签，而不是上面快速开始的无前缀名称。

项目已有 SO 时，把示例的五条 `Demo.*` 定义合入现有配置，再移除导入的示例 SO，保持只有一份配置。执行“生成代码”，等待编译，打开 `TagPlayground.unity` 点击 Play，在 Console 查看 `[Tag Demo]` 输出。首次导入时，Demo 可能因尚未生成的程序集／字段报错；使用此前已加载的 Tag Settings 工具生成后，再等待编译。

Demo 在 `Awake` 初始化，在 `Start` 创建运行时容器；它不是序列化内容的通用启动入口。正式配置建议放在项目固定目录，不长期依赖带版本号的 Samples 路径。

<a name="cn-generation"></a>
### 配置修改与生成

**SO 是唯一的编辑来源，生成文件不要手改。** 重命名会更新 SO 的整个分支，但不会自动迁移 C# 引用、场景、其他资产或存档中的旧名称。完成改名后同步这些引用，再生成代码。

Undo/Redo 撤销的是 SO 编辑，不会同步回滚已生成的 C#。撤销后也要重新生成。标准 Player 构建前会检查配置与生成文件是否一致；不一致会停止并提示生成，但不会替普通进入 Play Mode 自动同步。

Editor 选择器读取 SO 当前内容，Editor 与 Player 的运行态读取生成表。两者只有在重新生成并编译后才同步。生成代码不会自动初始化，仍使用自己的启动入口。

<details>
<summary>非 Unity 环境，或已有自己的注册数据</summary>

核心可通过 `GameplayTags.Runtime.csproj` 编译为 .NET Standard 2.1 / C# 9：

```bash
dotnet build GameplayTags.Runtime.csproj -c Release
```

不使用 SO 流程时，在相同的加载边界提供完整注册表：

```csharp
GameplayTagManager.Initialize(
    new GameplayTagRegistration("State.Burning"),
    new GameplayTagRegistration("Ability.Cast"));
GameplayTag burning = GameplayTagManager.RequestTag("State.Burning");
```

这是生成初始化的替代方案，不是第二次初始化。外部名称可以用 `RequestTag(name, out tag)` 在业务边界检查；缓存解析结果，运行期间不反复查字符串。

</details>

<a name="cn-migration"></a>
### 从旧版本升级

本版以 SO 配置替代程序集声明，不再提供 `GameplayTagAttribute`、`GameplayTagFlags`、`InitializeIfNeeded()` 或 Player DLL 扫描流程。把已有声明迁入 Tag Settings，重新生成，并改用生成字段或显式注册。

旧的 `Assets/GameplayTags.Generated.cs` 与新的生成目录不同。确认项目中只保留一份生成注册表，移除旧生成文件及其 `.meta`，更新业务 `.asmdef` 引用，避免同名类型冲突。已有场景／配置先备份，名称更改需主动迁移。

`GameplayTagRequirements` 使用标签数组／span 构建不可变规则，不再持有两个可变条件容器。规则条件改变时重新构建。

<a name="cn-api"></a>
### API 与使用边界

| 需求 | 类型 |
| --- | --- |
| 保存成员、层级匹配、复制和集合运算 | [`GameplayTagContainer`](Runtime/GameplayTagContainer.cs) |
| 叠加次数和变化通知 | [`GameplayTagCountContainer`](Runtime/GameplayTagCountContainer.cs) |
| 汇总独立来源 | [`GameplayTagHierarchicalContainer`](Runtime/GameplayTagHierarchicalContainer.cs) |
| 技能条件与规则缓存 | [`GameplayTagRequirements`](Runtime/GameplayTagRequirements.cs)、[`GameplayTagRuleSet`](Runtime/GameplayTagRuleSet.cs) |
| 标签定义、名称查询和层级关系 | [`GameplayTagManager`](Runtime/GameplayTagManager.cs)、[`GameplayTag`](Runtime/GameplayTag.cs) |
| 管理监听和临时容器 | [`GameplayTagContainerBinds`](Runtime/GameplayTagContainerBinds.cs)、[`GameplayTagContainerPool`](Runtime/GameplayTagContainerPool.cs) |

核心使用整数句柄和紧凑索引，不在每次查询中恢复序列化状态。调用方负责有效句柄、数量、容量、贡献所有权、初始化和线程同步；共享 `Empty` 按只读使用。名字解析与编辑器检查在加载／编辑边界处理。新建、扩容、序列化、订阅和池首次使用仍可能分配内存，该库也不是可直接用于 Burst/Jobs 的 NativeContainer。

包版本保留为 `2.1.0-rc.2-optimized.2`。这是 SO 编辑工作流的源码版本，具体 Unity／Player 后端和设备性能应在项目中验证，不以文档或版本名代替测试结果。

### 许可证与反馈

采用 [MIT](LICENSE) 许可证。问题和建议可以提交到 [GitHub Issues](https://github.com/acced/ZFramework.GameplayTags/issues)。

---

**[简体中文](#cn) | [English](#en)**

<a name="en"></a>
## English

**A shared language for character states, ability requirements, and stacking effects.**

ZFramework Gameplay Tags is a lightweight C# gameplay-tag library for Unity. It combines a tag editor and code generation with hierarchical matching, reference counts, and change notifications.

Define names such as `State.Burning`, `State.Control.Frozen`, and `Ability.Cast` in the editor, then generate their C# fields. Combat adds states, abilities check requirements, and UI listens for changes. Each system understands the tags without depending on the system that produced them.

The workflow is straightforward:

```text
Edit tags in Tag Settings → Generate the registry and fields → Initialize at startup → Use containers
```

<a name="en-features"></a>
### Features

- **Visual authoring.** Search, add, edit, remove, rename branches, and Undo/Redo in a TreeView. A dedicated ScriptableObject holds the project's tag definitions.
- **Generated access.** Generate the registry and cached read-only fields, including implicit parents. Editor and Player runtime use the same generated registration data, without assembly-attribute discovery.
- **Hierarchical matching.** Group frozen and silenced states under `State.Control` and express “any control effect” with one condition.
- **Stacking and notifications.** Count contributions from independent effects. The state disappears only when the last contribution is removed; events can drive icons, animations, and ability buttons.
- **Sets and reuse.** Copy, union, explicit intersection, batch removal, and reusable destinations, with a caller-configurable thread-local container pool.
- **Fixed rules and caching.** Build ability requirements once, share condition queries, and keep a separate evaluation cache for each character.

Use it for character states, equipment capabilities, ability prerequisites, and overlapping effects. It does not implement damage, duration, or cooldowns. A `bool` is enough for an isolated switch; tags help when states need to compose across systems.

### Documentation

[Install](#en-installation) · [Quick start](#en-quick-start) · **[Initialize and load](#en-loading)** · [Usage](#en-usage) · [Inspector and saves](#en-serialization) · [Pooling](#en-pooling) · [Sample](#en-sample) · [Migration](#en-migration) · [API](#en-api)

<a name="en-installation"></a>
### Install

The Unity package targets **Unity 2022.3 or later**. In Package Manager, choose **+ → Add package from git URL**:

```text
https://github.com/acced/ZFramework.GameplayTags.git#main
```

For a local checkout, choose **Add package from disk** and select `package.json`. Replace `#main` with a full commit SHA to pin a revision for your team.

The namespace is `GameplayTags`. If your application uses `.asmdef` files, reference `ZFramework.GameplayTags`. After generating code below, also reference `ZFramework.GameplayTags.Generated` to use its fields. Remove older packages that define the same types.

<a name="en-quick-start"></a>
### Quick start

#### 1. Create the tag configuration

Open **Tools → Gameplay Tags → Tag Settings** and click **创建标签配置** (Create Tag Configuration). The default asset path is:

```text
Assets/GameplayTags/Editor/GameplayTagSettings.asset
```

Add these five tags, with optional descriptions:

```text
State.Burning
State.Control.Frozen
State.Control.Silenced
Ability.Cast
Equipment.Weapon.Staff
```

Parent nodes are added automatically; there is no need to declare `State` or `State.Control` separately. Keep one `GameplayTagSettings` in the project. It defines the available tags, not a character's current state.

#### 2. Generate code

Click **生成代码** (Generate Code) and allow Unity to compile. **Tools → Gameplay Tags → Generate Player Registry** runs the same generation command.

The output is:

```text
Assets/GameplayTags.Generated/
├── GameplayTags.Generated.cs
└── ZFramework.GameplayTags.Generated.asmdef
```

Initialize through `GameplayTags.Generated.GameplayTagRegistry.Initialize()` and access fields under `GameplayTagRegistry.Tags`. For example, `State.Control.Frozen` becomes `Tags.State__Control__Frozen`. Dots become double underscores; literal underscores and other characters are escaped to avoid identifier collisions.

No handwritten assembly attributes, cache classes, or generated files are needed. Regenerate after editing definitions; compiler errors on renamed fields help locate affected code.

#### 3. Give a character a state

Create `TagExample.cs` and attach it to an empty GameObject. This standalone example creates its container after initialization:

```csharp
using GameplayTags;
using GameplayTags.Generated;
using UnityEngine;
using Tags = GameplayTags.Generated.GameplayTagRegistry.Tags;

public sealed class TagExample : MonoBehaviour
{
    private void Awake()
    {
        GameplayTagRegistry.Initialize();

        var states = new GameplayTagContainer();
        states.AddTag(Tags.State__Control__Frozen);

        Debug.Log(states.HasTag(Tags.State__Control));      // True
        Debug.Log(states.HasTagExact(Tags.State__Control)); // False
        Debug.Log(states.HasTagExact(Tags.State__Control__Frozen)); // True

        states.RemoveTag(Tags.State__Control__Frozen);
        Debug.Log(states.IsEmpty); // True
    }
}
```

Use one instance of this example. In a real project, centralize initialization in your bootstrap; **do not initialize again in every character's `Awake`**. Containers already stored in scenes or assets need the loading order below.

<a name="en-loading"></a>
### Initialize first, then load content

**The same `GameplayTagContainer` works as an Inspector-configured field and a runtime container. There is no separate data type or manual post-load conversion step.**

It stores explicit names for serialization and restores integer members from the registry during deserialization. Arrange startup like this:

```text
Bootstrap → Initialize generated registry → Access cached fields → Load scenes / Prefabs / SOs / saves → Use containers
```

One straightforward setup is a **Boot** scene followed by **Game**. Add both to the build list, with Boot first. Boot contains only startup logic and must not reference or load tag-bearing gameplay content early.

```csharp
using GameplayTags.Generated;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class GameBootstrap : MonoBehaviour
{
    private void Awake()
    {
        GameplayTagRegistry.Initialize();
        SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);
    }
}
```

The guarantee comes from **starting the Game load only after initialization returns**. For Resources, Addressables, AssetBundles, or save loaders, initialize before the first relevant loading/deserialization operation, not after the asset has already loaded.

Opening Game directly and pressing Play bypasses Boot; provide an equivalent startup path for that workflow. Do not access generated fields early from other static initializers. Prepare the registry once per process/script domain, not on every scene change. Live handles and containers must not outlive a replacement registry.

<details>
<summary>Editor loading, preloaded assets, and Domain Reload</summary>

Tag Settings and the pickers read names from the SO; they do not initialize the runtime registry. An Editor tool loading assets that restore containers must prepare the same generated registry first. Player's Boot scene does not cover Editor imports, cloning, script reloads, or earlier preloaded assets.

Script Execution Order and `BeforeSceneLoad` are not pre-deserialization guarantees: Unity documents that the first scene's objects are already loaded at `BeforeSceneLoad`, and serialization callbacks may run off the main thread. This library expects registry-ready loading, not automatic repair of arbitrary Editor ordering. Do not save an incompletely restored container as a valid empty one; fix the sequence and reload the original resource.

With Domain Reload disabled, the registry, generated fields, and pool may survive Play Mode sessions. Keep the same registry and reset application session state. After changing definitions, initialize in a fresh script domain instead of replacing the registry underneath old handles.

Unity references: [BeforeSceneLoad](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/RuntimeInitializeLoadType.BeforeSceneLoad.html), [serialization callbacks](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ISerializationCallbackReceiver.html), [Domain Reloading](https://docs.unity3d.com/2022.3/Documentation/Manual/DomainReloading.html).

</details>

<a name="en-usage"></a>
### Usage

Place these snippets in application methods, with `using GameplayTags;` and `using Tags = GameplayTags.Generated.GameplayTagRegistry.Tags;`, after initialization.

#### Hierarchical matching and explicit membership

Adding `State.Control.Frozen` also makes the container match `State.Control` and `State`, but only Frozen was added explicitly:

```csharp
var states = new GameplayTagContainer();
states.AddTag(Tags.State__Control__Frozen);

bool controlled = states.HasTag(Tags.State__Control);      // True
bool explicitControl = states.HasTagExact(Tags.State__Control); // False
int explicitCount = states.ExplicitTagCount; // 1
int totalCount = states.TagCount;            // 3

foreach (GameplayTag tag in states.GetExplicitTags()) { /* Frozen */ }
foreach (GameplayTag tag in states) { /* State, Control, Frozen */ }
```

The relationship is directional: adding Control alone does not imply Frozen. Ordinary containers are sets, so duplicates do not stack. Removing an implicit parent does not remove descendants. Enumeration is unordered and must not overlap mutation; sort names only when a UI or export requires a stable order.

#### Ability requirements: a staff, casting ability, and no control effect

```csharp
var canCast = GameplayTagRequirements.Create(
    Tags.State__Control, Tags.Ability__Cast, Tags.Equipment__Weapon__Staff);

var actor = new GameplayTagContainer();
actor.AddTag(Tags.Ability__Cast);
actor.AddTag(Tags.Equipment__Weapon__Staff);
bool available = canCast.Matches(actor); // True

actor.AddTag(Tags.State__Control__Silenced);
bool silenced = canCast.Matches(actor);  // False
```

The three arguments are one forbidden tag and two required tags. For larger rules, pass tag arrays or spans to `new GameplayTagRequirements(forbidden, required)`. Rules are immutable snapshots: create them when loading abilities and recreate them when conditions change.

`canCast.Matches(equipment, effects)` lets either holder supply required tags, without allocating a union. Simple queries can use `HasAny`, `HasAll`, and their `Exact` variants. An empty query makes Any false and All true.

When many rules share conditions, share a rule set and create a separate cache for each character:

```csharp
var rules = new GameplayTagRuleSet(new[]
{
    canCast,
    new GameplayTagRequirements(
        System.Array.Empty<GameplayTag>(), new[] { Tags.State__Burning })
});
var cache = rules.CreateCache();
System.ReadOnlySpan<bool> results = cache.Evaluate(actor);
bool castEnabled = results[0];
```

An unchanged cache reuses its results. Presence changes refresh distinct conditions and recompute affected rules. The returned span is a borrowed view overwritten by the next evaluation. Do not share a cache concurrently or recreate it every frame.

#### Stacking effects and notifications

Use `GameplayTagCountContainer` when two sources both apply burning, so removing one contribution does not remove the other:

```csharp
var effects = new GameplayTagCountContainer();
OnTagCountChangedDelegate onBurning = (tag, count) =>
    UnityEngine.Debug.Log($"{tag.Name}: {count}");

effects.RegisterTagEventCallback(
    Tags.State__Burning, GameplayTagEventType.AnyCountChange, onBurning);
effects.AddTag(Tags.State__Burning);    // 1
effects.AddTag(Tags.State__Burning);    // 2
effects.RemoveTag(Tags.State__Burning); // 1
effects.RemoveTag(Tags.State__Burning); // 0
effects.RemoveTagEventCallback(
    Tags.State__Burning, GameplayTagEventType.AnyCountChange, onBurning);
```

`GetExplicitTagCount(tag)` reads the tag's own count; `GetTagCount(tag)` includes descendant contributions. Use `AddTag(tag, amount)` and `RemoveTag(tag, amount)` for several stacks. Your application owns amounts and sources. Batch `AddTags(source)` / `RemoveTags(source)` adds/removes one occurrence per distinct explicit source member, not the source's multiplicity.

Subscribe to `NewOrRemoved` for appearance/disappearance only, or to a parent tag for a whole category. Direct registration does not report the current value immediately; `GameplayTagContainerBinds.Bind` does and supports grouped unsubscription. Keep bindings for the UI's lifetime and dispose them when it closes. `Clear()` removes members but keeps container subscriptions.

#### Independent equipment and aura contributions

```csharp
var actor = new GameplayTagCountContainer();
var equipment = new GameplayTagHierarchicalContainer { Parent = actor };
var aura = new GameplayTagHierarchicalContainer { Parent = actor };

equipment.AddTag(Tags.Ability__Cast);
aura.AddTag(Tags.Ability__Cast);  // Two contributions in actor.

equipment.Parent = null;       // Removes equipment's contribution only.
aura.Clear();
aura.Parent = null;
```

Children contribute upward; they do not inherit the parent's members. Do not clear the aggregate parent while children remain attached. Remove contributions through their owner and keep parent chains acyclic.

#### Copy, set operations, and reusable outputs

```csharp
var left = new GameplayTagContainer();
left.AddTag(Tags.State__Burning);
left.AddTag(Tags.Ability__Cast);
var right = new GameplayTagContainer();
right.AddTag(Tags.State__Control__Frozen);
right.AddTag(Tags.Ability__Cast);

var union = GameplayTagContainer.Union(left, right);
var intersection = GameplayTagContainer.Intersection(left, right); // Cast
var difference = left.Clone();
difference.RemoveTags(right); // Burning

var output = new GameplayTagContainer(32);
GameplayTagContainer.Copy(output, left);
GameplayTagContainer.Union(output, left, right);
GameplayTagContainer.Intersection(output, left, right);
output.Clear(); // Retain capacity for reuse.
```

Set operations work on explicit members, then derive ancestors. Burning and Frozen do not produce an explicit State intersection merely because both belong to it. Three-argument union/intersection support output aliasing either input. `AddTags` and `IntersectWith` operate in place. `EnsureCapacity` reserves distinct members including ancestors, not stack counts.

<a name="en-serialization"></a>
### Inspector configuration and saves

**Keep using `GameplayTagContainer`.** Once the registry is ready before content loads, its callback restores members and runtime code uses the container directly:

```csharp
using GameplayTags;
using UnityEngine;
using Tags = GameplayTags.Generated.GameplayTagRegistry.Tags;

public sealed class CharacterState : MonoBehaviour
{
    [SerializeField] private GameplayTagContainer states = new GameplayTagContainer();
    public GameplayTagContainer TagsInUse => states;

    public void ApplyBurning() => states.AddTag(Tags.State__Burning);
    public void RemoveBurning() => states.RemoveTag(Tags.State__Burning);
}
```

Place this in Game content loaded after Boot; do not wait for this component's own `Awake` to initialize. Names are the saved representation, integers are the working representation. No extra data class or `LoadInto()` is needed.

For independent character state from a shared SO template, use `templateTags.Clone()` or `GameplayTagContainer.Copy(runtimeTags, templateTags)` with an existing destination. Changing an instance does not automatically save a disk asset; your application and Unity control persistence.

For standalone `GameplayTag` fields after loading or editing, assign **`tag = tag.Resolve()`** at a registry-ready boundary. Save explicit names and, for counted state, explicit amounts. Do not persist `RuntimeIndex` or replay aggregate ancestor counts as independent contributions. Unknown names do not restore as container members.

<a name="en-pooling"></a>
### Temporary work and pooling

A frequently reused working container can simply be retained and cleared. For short scopes:

```csharp
using (GameplayTagContainerPool.Get(out GameplayTagContainer temporary))
{
    temporary.AddTag(Tags.State__Burning);
    // Use temporary inside this scope.
}
```

Retention is unbounded by default. Select a policy at a loading boundary on the thread that rents and returns containers:

```csharp
GameplayTagContainerPool.Configure(new GameplayTagContainerPool.RetentionPolicy(
    maxCount: 32, maxContainerBytes: 256 * 1024, maxTotalBytes: 4L * 1024 * 1024));
```

These numbers are examples. Use `new RetentionPolicy(32)` for a count-only limit, `RetentionPolicy.Disabled` for no retention, or `RetentionPolicy.Unbounded` for no budget limit. `Configure` clears the current thread's cache; the pool's `Clear()` releases cached storage while retaining the policy. `RetainedDataBytes` measures array payload, not total heap usage.

Return once on the same thread, do not dispose copied leases twice, and do not use returned references. Unbounded retention can keep peak memory; the application chooses the tradeoff.

<a name="en-sample"></a>
### Sample: Tag Playground

The package includes [a one-script, one-scene logging sample](Samples~/TagPlayground/README.md) covering matching, ability requirements, two burning sources, and `1 → 2 → 1 → 0` notifications.

Install the package and let its Editor tools compile before importing **Samples → Tag Playground** in Package Manager. The sample brings its own SO and uses `Demo.*` tags, not the unprefixed names from the quick start.

If you already have a configuration, copy the sample's five `Demo.*` definitions into it and remove the imported sample SO so only one configuration remains. Generate code, wait for compilation, then open `TagPlayground.unity` and press Play. Look for `[Tag Demo]` in the Console. On first import, the sample may report a missing generated assembly or fields; generate using the already-loaded Tag Settings tools, then allow compilation to complete.

The sample initializes in `Awake` and creates runtime containers in `Start`. It is not a general bootstrap for serialized content. Keep production settings in a stable project directory instead of relying on a versioned Samples path.

<a name="en-generation"></a>
### Editing and regeneration

**The SO is the authoring source; do not hand-edit generated files.** Renaming updates the branch inside the SO, not C# references, scenes, other assets, or saved games. Update those references and regenerate after renaming.

Undo/Redo restores SO edits, not previously generated C#. Regenerate after undoing a change as well. Standard Player builds check that configuration and generated output agree and stop when they differ. Entering Play Mode does not automatically regenerate them.

Pickers display current SO data; Editor and Player runtime use the generated table. They agree after generation and compilation. Generated code does not initialize itself; keep initialization in your bootstrap.

<details>
<summary>Standalone C# or application-owned registration data</summary>

Build the core as .NET Standard 2.1 / C# 9 through `GameplayTags.Runtime.csproj`:

```bash
dotnet build GameplayTags.Runtime.csproj -c Release
```

Without the SO workflow, provide the complete registry at the same loading boundary:

```csharp
GameplayTagManager.Initialize(
    new GameplayTagRegistration("State.Burning"),
    new GameplayTagRegistration("Ability.Cast"));
GameplayTag burning = GameplayTagManager.RequestTag("State.Burning");
```

This replaces generated initialization; it is not a second initialization step. Use `RequestTag(name, out tag)` to handle external names at your application boundary, then cache resolved handles instead of repeatedly looking up strings.

</details>

<a name="en-migration"></a>
### Upgrading from earlier versions

This version replaces assembly declarations with SO authoring. It no longer provides `GameplayTagAttribute`, `GameplayTagFlags`, `InitializeIfNeeded()`, or Player DLL scanning. Move declarations into Tag Settings, regenerate, and use generated fields or explicit registration.

The old `Assets/GameplayTags.Generated.cs` is outside the new output directory. Keep only one generated registry: remove the old generated file and its `.meta`, and update application `.asmdef` references to avoid duplicate types. Back up scenes/configuration before migrating and explicitly update renamed names.

`GameplayTagRequirements` now builds immutable rules from tag arrays/spans, not references to two mutable condition containers. Rebuild a rule when its conditions change.

<a name="en-api"></a>
### API and operating assumptions

| Task | Type |
| --- | --- |
| Serialized members, hierarchical queries, copying, and sets | [`GameplayTagContainer`](Runtime/GameplayTagContainer.cs) |
| Stack counts and change events | [`GameplayTagCountContainer`](Runtime/GameplayTagCountContainer.cs) |
| Aggregate independent contributions | [`GameplayTagHierarchicalContainer`](Runtime/GameplayTagHierarchicalContainer.cs) |
| Ability conditions and cached rule groups | [`GameplayTagRequirements`](Runtime/GameplayTagRequirements.cs), [`GameplayTagRuleSet`](Runtime/GameplayTagRuleSet.cs) |
| Definitions, name resolution, and hierarchy | [`GameplayTagManager`](Runtime/GameplayTagManager.cs), [`GameplayTag`](Runtime/GameplayTag.cs) |
| Grouped subscriptions and temporary containers | [`GameplayTagContainerBinds`](Runtime/GameplayTagContainerBinds.cs), [`GameplayTagContainerPool`](Runtime/GameplayTagContainerPool.cs) |

The core uses integer handles and compact indices; queries do not restore serialized state. Callers own valid handles, amounts, capacities, contribution ownership, initialization, and synchronization. Treat shared `Empty` as read-only. Name resolution and authoring checks belong at loading/editing boundaries. Creation, growth, serialization, subscriptions, and first pool use can allocate. This is managed C#, not a Burst/Jobs NativeContainer.

The package retains version `2.1.0-rc.2-optimized.2`. It is a source version of the SO authoring workflow. Validate it on your project's Unity/Player backends and devices; documentation and version labels are not test results.

### License and feedback

Licensed under [MIT](LICENSE). Report problems and suggestions through [GitHub Issues](https://github.com/acced/ZFramework.GameplayTags/issues).
