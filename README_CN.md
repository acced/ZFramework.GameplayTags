# ZFramework Gameplay Tags

**简体中文** | [English](README.md#en)

**用同一套标签，描述角色状态、技能条件和 Buff 叠加。**

ZFramework Gameplay Tags 是一个面向 Unity 的 C# 游戏标签库。战斗系统给角色加上 `State.Burning`，技能系统检查 `Ability.Cast`，UI 监听状态变化。它们共享一套名称和层级，不必知道一个状态具体来自装备、技能还是场景效果。

**一个 `GameplayTagContainer` 就够了：既能在 Inspector 中配置并保存，也能直接用于运行时。** 资源里保存名称，加载时恢复整数成员，后续查询使用整数索引。使用上最重要的一条是：**先准备注册表，再加载带标签的内容。**

[用途](#why-tags) · [安装](#installation) · [快速开始](#quick-start) · **[加载顺序](#loading)** · [Inspector 与保存](#unity-and-serialization) · [技能规则](#queries) · [Buff 与通知](#counts) · [集合运算](#set-algebra) · [对象池](#pooling) · [API 导航](#api-map)

<a name="why-tags"></a>
### 它能解决什么问题？

| 游戏中的问题 | 标签库的做法 |
| --- | --- |
| 受到冰冻或沉默时，角色不能施法。 | 都归入 `State.Control`，检查一个父标签即可。 |
| 火球和地面火焰都造成燃烧，结束其中一个不能清掉整个状态。 | 使用计数容器，最后一个来源移除后状态才消失。 |
| 装备和光环分别赋予能力，角色需要统一查询。 | 子容器管理自己的贡献，父容器汇总。 |
| 技能栏的多个按钮反复检查相同条件。 | 共享固定规则，用规则组与每角色缓存减少重复查询。 |

它是游戏逻辑的基础积木，不是一整套技能框架：伤害、持续时间、冷却和来源对象仍由业务管理。只有一个独立开关时，`bool` 就很好；需要跨系统组合、层级匹配和叠加贡献时，再使用标签。

<a name="installation"></a>
### 安装

包目标为 **Unity 2022.3 及以上**。在 Package Manager 选择 **+ → Add package from git URL**：

```text
https://github.com/acced/ZFramework.GameplayTags.git#main
```

团队项目可以把 `main` 换成完整提交号来固定版本。本地源码通过 **Add package from disk** 选择 `package.json`。使用 `.asmdef` 的业务程序集需要引用 **`ZFramework.GameplayTags`**；代码命名空间是 **`GameplayTags`**。不要同时安装另一份定义相同类型的包。

独立 C# 项目可以引用 `GameplayTags.Runtime.csproj`，或执行 `dotnet build GameplayTags.Runtime.csproj -c Release`。核心目标为 .NET Standard 2.1 / C# 9，独立工程不编译 Unity 组件和 Editor 代码。

<a name="quick-start"></a>
### 快速开始：声明一次，运行时使用句柄

本指南的默认示例使用当前主线内置的**程序集声明**。已有自己的配置或代码生成流程时，也可以使用[显式注册](#registration)，容器用法不变。

创建 **`GameTags.cs`**，把程序集特性放在 `using` 之后、类型声明之前：

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

父标签会自动补全。`GameTags` 只是业务自己的缓存类，不依赖源码生成器。显式静态构造器避免字段提前初始化，但首次访问仍应在注册表准备好之后。

只体验纯代码用法时，把下面的 **`CharacterTagsExample.cs`** 挂到空物体上：

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
        states.AddTag(GameTags.Frozen);

        Debug.Log(states.HasTag(GameTags.Control));      // True: 属于控制状态。
        Debug.Log(states.HasTagExact(GameTags.Control)); // False: 没有直接添加父标签。
        Debug.Log(states.HasTagExact(GameTags.Frozen));  // True。

        states.RemoveTag(GameTags.Frozen);
        Debug.Log(states.IsEmpty);                      // True。
    }
}
```

**这里在 `Awake` 初始化成立，是因为容器也在它之后才创建。** 如果容器来自场景、Prefab 或 SO 字段，使用下面的加载流程，而不是把同样的 `Awake` 调用复制过去。

<a name="loading"></a>
### 接入项目：先初始化，再加载内容

注册表回答“项目有哪些标签”，容器回答“这个对象有哪些成员”。注册表每个进程／脚本域准备一次；每个角色可以有自己的容器。

```text
启动入口（不含需要恢复的标签容器）
    ↓ 初始化选定的完整注册表
    ↓ 允许访问缓存句柄
    ↓ 发起游戏场景、Prefab、SO 或存档的加载
    ↓ 容器回调按名称恢复整数成员
    ↓ 查询、增删、计数和规则匹配
```

关键不是“比其他组件的 `Awake` 早”，而是**比带标签内容的反序列化早**。容器会在 `OnAfterDeserialize()` 立即恢复成员，不等第一次查询，也不需要再调用一次 `LoadInto()`。

#### 推荐方式：一个不携带游戏内容的启动场景

新建 **Boot** 和 **Game** 场景，都加入构建列表，把 Boot 放在第一位。Boot 只放启动逻辑；不要在它的序列化字段、预加载资产或其他启动逻辑里提前引用／加载带标签容器的游戏内容。

将 **`GameBootstrap.cs`** 挂到 Boot 场景中的一个物体：

```csharp
using GameplayTags;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class GameBootstrap : MonoBehaviour
{
    private void Awake()
    {
        // 此示例选择程序集声明。自有注册表应在这里替换这一个调用。
        GameplayTagManager.InitializeIfNeeded();

        // 从这里开始，后续内容加载才可能恢复标签容器。
        SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);
    }
}
```

**这个顺序的保证来自：初始化返回之后，才发起 Game 场景的加载。** 从 Boot 进入游戏；直接打开 Game 并点击 Play，会绕过这个入口，需要项目自己的等价启动流程。

用 Resources、Addressables、AssetBundle 或自定义存档加载器时，原则相同：在第一次可能读取相关内容的加载／反序列化操作之前初始化，而不是等对象加载完、准备 `Instantiate` 时才补做。初始化后再启动依赖标签的后台工作；不要让多个加载线程争抢首次初始化。

| 使用方式 | 注册表应当何时准备好？ |
| --- | --- |
| 代码中 `new GameplayTagContainer()` | 在解析句柄、添加或使用成员之前；空对象分配本身不是加载。 |
| 场景、Prefab、SO 中已有容器字段 | 在加载它们及其依赖、恢复字段之前。 |
| 从存档恢复容器 | 在调用反序列化器之前。 |
| Editor 自定义工具加载资产 | 在工具发起相应资产读取之前，不能依赖游戏的 Boot 场景。 |

不要把 `BeforeSceneLoad` 当成“尚未反序列化”：Unity 文档说明这个时点首场景对象已经加载到内存，只是尚未调用 `Awake`。`AfterAssembliesLoaded` 更早，但预加载资产已经初始化，也不能覆盖任意早期加载。这里采用显式启动场景，就是为了不猜这些回调之间的顺序。参见 [BeforeSceneLoad](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/RuntimeInitializeLoadType.BeforeSceneLoad.html) 和 [AfterAssembliesLoaded](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/RuntimeInitializeLoadType.AfterAssembliesLoaded.html)。

主线的名称查询虽然保留了首次程序集发现，但**不要靠序列化回调第一次查名字来替你选择初始化方式**。特别是使用显式或生成注册表时，过早的 Inspector、静态字段或回调查询可能先发布另一套声明。

<a name="unity-and-serialization"></a>
### 一个容器：Inspector 配置，加载后直接使用

`GameplayTagContainer` 同时支持资源保存和运行时使用，不需要另外定义一套数据容器：

```text
保存到资源：显式标签名称
加载恢复：名称 → 当前注册表的整数成员与祖先
运行期间：直接查询和修改整数存储
再次保存：当前显式成员 → 名称
```

上面的加载流程已就位后，可以在 **Game** 场景中的组件上直接配置容器。创建 **`CharacterState.cs`**：

```csharp
using GameplayTags;
using UnityEngine;

public sealed class CharacterState : MonoBehaviour
{
    [SerializeField] private GameplayTagContainer states = new GameplayTagContainer();

    public GameplayTagContainer Tags => states;

    private void Start()
    {
        // 注册表已由 Boot 准备好，序列化的成员也已恢复。
        Debug.Log(states.HasTag(GameTags.Control));
    }

    public void ApplyBurning() => states.AddTag(GameTags.Burning);
    public void RemoveBurning() => states.RemoveTag(GameTags.Burning);
}
```

Inspector 绘制器用于选择已注册名称。在运行中的角色实例上增删标签，就修改这个实例；不需要额外的转换模式。正常保存会导出当时的显式成员，但**不代表修改了内存对象，就自动写入磁盘或 Prefab 资产**，保存时机仍由 Unity 或业务存档流程决定。

**共享模板需要独立状态时，复制实例即可：**

```csharp
// templateTags 是已加载并恢复的模板容器。
GameplayTagContainer actorTags = templateTags.Clone();
actorTags.AddTag(GameTags.Burning); // 不修改模板。

// 已有复用目标时：
GameplayTagContainer.Copy(actorTags, templateTags);
```

存档保存显式名称，不保存 `RuntimeIndex`；加载时再推导祖先。计数容器的存档由业务保存“显式名称＋次数”，不能把祖先累计值再次当成独立贡献。未知名称不会恢复为容器成员，改名或删除声明后的资源迁移属于编辑／内容流程。

独立的 `GameplayTag` 字段与容器的恢复是两回事。注册表准备好后，在拥有者的加载入口执行 `tag = tag.Resolve()`；自己修改 `m_Name` 后也要接住返回值。容器已经由回调恢复时，不要再手工重复调用 `OnAfterDeserialize()`。

#### Editor 与重载时记住同一条顺序

Inspector、场景编辑、对象复制和脚本重载也会触发序列化；回调可能不在主线程。自有显式／生成注册表需要相应的 Editor 初始化入口，不能只安排 Player 启动。`InitializeOnLoad` 可用于项目的代码级准备，但不是所有资产回调顺序的万能保证；也不要在其中依赖尚未完成导入的资产。参见 [序列化回调](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ISerializationCallbackReceiver.html) 与 [Editor 初始化](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/InitializeOnLoadAttribute.html)。

这份指南约定的是**先准备好注册表的加载路径**，不是对任意 Editor 加载顺序的自动兜底。恢复未完成的容器不能继续当作有效空容器保存；请修正加载顺序并从原资源重新加载，而不是把空运行态写回覆盖配置。

关闭 Domain Reload 时，静态注册表、缓存句柄和订阅可能跨 Play Mode 保留。保持同一注册表，清理业务会话状态；声明改变时，在干净的脚本域重新进入。不要只重建注册表，却继续使用旧容器和旧句柄。参见 [Domain Reloading](https://docs.unity3d.com/2022.3/Documentation/Manual/DomainReloading.html)。

<a name="tag-semantics"></a>
<a name="sets"></a>
### “属于这一类”与“直接加过它”

加入 `State.Control.Frozen` 后，`HasTag(Control)` 为 true，`HasTagExact(Control)` 为 false。只有 Frozen 是显式成员；State 和 Control 由它贡献。反过来，只加入 Control 并不代表角色被冻结。

`ExplicitTagCount` 统计显式成员，`TagCount` 还包括不同的祖先；上例分别为 1 和 3。普通容器是集合，重复添加不会叠层。`TryAddTag`、`TryRemoveTag` 返回成员是否变化，不是参数校验。删除仅隐式存在的父标签不会清掉其后代。

<a name="queries"></a>
### 技能条件：有法杖、能施法，而且没有受到控制

下面的代码片段放入业务方法，沿用 `GameTags`，并在注册表初始化之后执行。规则在技能加载时创建一次：

```csharp
var castingRule = GameplayTagRequirements.Create(
    GameTags.Control, GameTags.Cast, GameTags.Staff);

var actor = new GameplayTagContainer();
actor.AddTag(GameTags.Cast);
actor.AddTag(GameTags.Staff);
UnityEngine.Debug.Log(castingRule.Matches(actor)); // True。
actor.AddTag(GameTags.Silenced);
UnityEngine.Debug.Log(castingRule.Matches(actor)); // False。
```

三个参数依次为“禁止标签、必需标签、另一个必需标签”。条件更多时，构造器接受两份已解析标签的数组或 span：`new GameplayTagRequirements(forbidden, required)`。规则是不可变快照，配置改变就重新创建；最多四个化简后的有效条件内联保存。它不再接受旧版的两个可变条件容器，也不直接保存到资源。

`castingRule.Matches(equipment, effects)` 可以让条件分别来自装备和临时效果，无需创建并集。简单条件集合仍可使用 `HasAny`、`HasAll`、`HasAnyExact`、`HasAllExact`；空条件下 Any 为 false，All 为 true。

多个技能共享条件时，规则组可以共享给所有角色，每个角色保留自己的缓存：

```csharp
var rules = new GameplayTagRuleSet(new[]
{
    castingRule,
    new GameplayTagRequirements(System.Array.Empty<GameplayTag>(), new[] { GameTags.Burning })
});
var actorCache = rules.CreateCache();
System.ReadOnlySpan<bool> available = actorCache.Evaluate(actor);
bool canCast = available[0];
```

缓存未失效时复用结果；状态改变时刷新不同条件，只重算结果受影响的规则。只改层数而不改变存在性，不必重算存在性条件。返回的 span 是借用视图，下次评估会覆盖；缓存不要跨线程共享。无缓存的 `rules.Evaluate(actor, resultSpan)` 要求调用方提供至少 `rules.Count` 个结果位置。

<a name="counts"></a>
### Buff 叠加：一个来源结束，不误删另一个

```csharp
var effects = new GameplayTagCountContainer();
effects.AddTag(GameTags.Burning); // 火球。
effects.AddTag(GameTags.Burning); // 地面火焰。
UnityEngine.Debug.Log(effects.GetExplicitTagCount(GameTags.Burning)); // 2。

effects.RemoveTag(GameTags.Burning); // 火球结束。
UnityEngine.Debug.Log(effects.HasTag(GameTags.Burning)); // True。
effects.RemoveTag(GameTags.Burning); // 离开火焰区域。
UnityEngine.Debug.Log(effects.HasTag(GameTags.Burning)); // False。
```

批量层数用 `AddTag(tag, amount)`、`RemoveTag(tag, amount)`。`GetExplicitTagCount` 是自身次数，`GetTagCount` 包含后代贡献；`ExplicitTagCount` 仍统计不同的显式标签，而不是层数总和。

各系统只移除自己添加的贡献。批量 `AddTags(source)`／`RemoveTags(source)` 对每个不同显式来源标签各加／减一次，不照搬来源的层数；`Clear()` 移除所有计数但保留订阅。

<a name="events"></a>
### 状态变化时再更新 UI

```csharp
var effects = new GameplayTagCountContainer();
OnTagCountChangedDelegate onBurning = (tag, count) =>
    UnityEngine.Debug.Log($"燃烧层数：{count}");

effects.RegisterTagEventCallback(
    GameTags.Burning, GameplayTagEventType.AnyCountChange, onBurning);
effects.AddTag(GameTags.Burning);    // 1。
effects.AddTag(GameTags.Burning);    // 2。
effects.RemoveTag(GameTags.Burning); // 1。
effects.Clear();                    // 0。
effects.RemoveTagEventCallback(
    GameTags.Burning, GameplayTagEventType.AnyCountChange, onBurning);
```

只关心出现／消失，用 `NewOrRemoved`；监听父标签能观察整个类别。直接注册不会立即报告当前值。管理一组存在性监听时，用 `GameplayTagContainerBinds`：

```csharp
using (var bindings = new GameplayTagContainerBinds(effects))
{
    bindings.Bind(GameTags.Burning, present => UnityEngine.Debug.Log(present));
    effects.AddTag(GameTags.Burning);
}
```

绑定会立即报告当前存在性，释放绑定组会取消这些订阅，不释放容器。长期 UI 应让绑定组随界面存活，关闭时再释放，而不是在每帧建立一个短暂的 `using`。回调看到已提交状态；回调内的修改排队通知，异常不会撤销已完成的状态变化。

<a name="parent-containers"></a>
### 装备和光环各管各的，角色统一查询

```csharp
var actor = new GameplayTagCountContainer();
var equipment = new GameplayTagHierarchicalContainer { Parent = actor };
var aura = new GameplayTagHierarchicalContainer { Parent = actor };
equipment.AddTag(GameTags.Cast);
aura.AddTag(GameTags.Cast);
UnityEngine.Debug.Log(actor.GetExplicitTagCount(GameTags.Cast)); // 2。

equipment.Parent = null;
UnityEngine.Debug.Log(actor.GetExplicitTagCount(GameTags.Cast)); // 1。
aura.Clear();
aura.Parent = null;
```

这是子容器向父容器汇总，不是继承父容器的成员。解除挂接撤销它在父级的贡献，但不清空子容器；重新挂接会转移现有贡献。通过拥有者移除贡献，不要在子容器仍挂接时直接清空聚合父容器，父链不能形成循环。

<a name="set-algebra"></a>
### 复制、并集、交集与差集

```csharp
var left = new GameplayTagContainer();
left.AddTag(GameTags.Burning);
left.AddTag(GameTags.Cast);
var right = new GameplayTagContainer();
right.AddTag(GameTags.Frozen);
right.AddTag(GameTags.Cast);

var combined = GameplayTagContainer.Union(left, right);
var shared = GameplayTagContainer.Intersection(left, right); // 只有 Cast。
var remaining = left.Clone();
remaining.RemoveTags(right); // 只剩 Burning。

var output = new GameplayTagContainer(32);
GameplayTagContainer.Copy(output, left);
GameplayTagContainer.Union(output, left, right);
GameplayTagContainer.Intersection(output, left, right);
output.Clear(); // 保留容量，下次继续用。
```

运算针对显式成员，再推导结果祖先。燃烧与冰冻的交集为空，不会因为共同属于 State 就产生显式 State。计数输入也只贡献成员存在性。

三参数并集／交集允许输出与任一输入相同；复制自己、普通集合添加自己是无操作，普通集合移除自己会清空。`AddTags(other)` 原地合并，`IntersectWith(other)` 原地求交。不要先用 `Copy(output, left)` 覆盖仍需作为右输入的同一对象，再试图求并集。

<a name="enumeration"></a>
### 枚举实际效果，而不是所有祖先

```csharp
foreach (GameplayTag tag in actorTags.GetExplicitTags())
    UnityEngine.Debug.Log(tag.Name); // 直接添加的成员。

foreach (GameplayTag tag in actorTags)
    UnityEngine.Debug.Log(tag.Name); // 包含祖先。
```

容器枚举无序，期间不要修改。UI／导出需要稳定顺序时，再提取名称并用 `StringComparer.Ordinal` 排序。`GetDiffExplicitTags(previous, added, removed)` 以及父子成员提取方法向结果列表追加，不会自动清空它们。

<a name="registration"></a>
### 已有自己的配置或生成表？使用显式注册

在同一个启动位置，把程序集发现**替换为**完整注册表的初始化：

```csharp
GameplayTagManager.Initialize(
    new GameplayTagRegistration("State.Burning"),
    new GameplayTagRegistration("State.Control.Frozen"),
    new GameplayTagRegistration("State.Control.Silenced"),
    new GameplayTagRegistration("Ability.Cast"),
    new GameplayTagRegistration("Equipment.Weapon.Staff"));
```

它不会合并程序集特性。自有 SO 或配置生成器可以输出这些注册项，加载顺序仍是“初始化→缓存句柄→加载内容”。当前主线自带的可选生成器见[下一节](#player-registry)，不是 SO 标签设置窗口。

名称区分大小写。外部存档或输入可在边界使用 `RequestTag(name, out tag)` 处理缺失标签，再缓存结果；不要在每帧依赖字符串隐式转换。注册表发布后保持不变，`Initialize` 本身没有锁或防重复保护，不能为了切换场景反复调用它。

<a name="player-registry"></a>
### 可选：生成 Player 注册表，减少启动发现工作

**Tools → Gameplay Tags → Generate Player Registry** 启用当前主线的生成器。它读取实际 Player 编译后的 DLL 声明，输出 `Assets/GameplayTags.Generated.cs`；Mono.Cecil 只用于 Editor。

首次构建或声明改变时，生成器更新文件并停止构建；让 Unity 编译它，再以同一平台和宏配置构建。第二次核对源码及已编译表指纹。自定义管线可在裁剪前调用 `GenerateFromPlayerAssemblies(paths)`；返回 false 表示生成代码需要重新编译。

在已有启动入口中，用下面的调用**替换**程序集发现：

```csharp
GameplayTags.Generated.GameplayTagRegistry.Initialize();
```

随后才加载内容。该入口在 Player 使用生成表，在 Editor 使用程序集发现；它不生成 `GameTags` 字段，也不安装自动启动回调。不要混用另一套显式初始化。只支持直接 `GameplayTagAttribute` 声明及其命名参数，不执行自定义派生特性；不能求值的派生声明会在构建时报告，而不是静默遗漏。

<a name="pooling"></a>
### 临时计算：按项目选择复用策略

每帧使用的工作容器可以长期持有并 `Clear()`。需要多个短作用域时，再借用线程本地池：

```csharp
using (GameplayTagContainerPool.Get(out GameplayTagContainer temporary))
{
    temporary.AddTag(GameTags.Burning);
    // 查询、组合或筛选；不把 temporary 留到作用域之外。
}
```

默认不限保留预算。在加载阶段、负责借还的线程上选择一种策略：

```csharp
GameplayTagContainerPool.Configure(new GameplayTagContainerPool.RetentionPolicy(32));

// 或限制单个与总数组载荷；数值仅为项目配置示例。
GameplayTagContainerPool.Configure(new GameplayTagContainerPool.RetentionPolicy(
    maxCount: 64, maxContainerBytes: 256 * 1024, maxTotalBytes: 4L * 1024 * 1024));

// 或禁用保留，或恢复不限制预算。实际使用时选择一种。
GameplayTagContainerPool.Configure(GameplayTagContainerPool.RetentionPolicy.Disabled);
GameplayTagContainerPool.Configure(GameplayTagContainerPool.RetentionPolicy.Unbounded);
```

`Configure` 释放当前缓存并切换策略；池的 `Clear()` 释放缓存但保留策略，和容器自身保留容量的 `Clear()` 不同。`RetainedCount`、`RetainedDataBytes` 用于观察保留情况；字节数只统计容器数组载荷。

默认不限预算可能保留峰值内存。调用方负责同线程、恰好一次归还，不复制租约后重复释放、不释放默认租约、不使用已归还的引用。

<a name="caller-contract"></a>
### 让运行时保持简单

**启动时注册，加载时恢复，运行时直接使用。** 不增加单独的数据容器，不在 `HasTag` 中偷偷恢复，也不要求每次查询检查初始化。

使用有效的非 None 句柄与非 null 修改操作数；正数计数、合法容量、不溢出的运算、正确的贡献所有权和线程同步由调用方保证。共享 `Empty` 按只读使用。`EnsureCapacity(n)` 的容量包含不同的祖先，不是只算显式叶子。

具体类型枚举使用结构体枚举器，经接口使用可能装箱；新建、扩容、订阅和序列化仍可能分配。该库是托管 C#，不是 Burst/Jobs 的 NativeContainer。当前版本为 `2.1.0-rc.2-optimized.2`，完整 Unity／IL2CPP 与目标设备验证仍需由项目完成；这份文档不宣称全面提速或新的性能验收。

<a name="api-map"></a>
### 接下来查哪个 API？

| 你想做的事 | 入口 |
| --- | --- |
| Inspector 成员、运行时状态、复制与集合运算 | [`GameplayTagContainer`](Runtime/GameplayTagContainer.cs) |
| Buff 层数和变化事件 | [`GameplayTagCountContainer`](Runtime/GameplayTagCountContainer.cs) |
| 各系统的贡献汇总 | [`GameplayTagHierarchicalContainer`](Runtime/GameplayTagHierarchicalContanier.cs) |
| 固定技能条件与共享规则缓存 | [`GameplayTagRequirements`](Runtime/GameplayTagRequirements.cs)、[`GameplayTagRuleSet`](Runtime/GameplayTagRuleSet.cs) |
| 注册表、句柄、名称与层级关系 | [`GameplayTagManager`](Runtime/GameplayTagManager.cs)、[`GameplayTag`](Runtime/GameplayTag.cs) |
| 一组 UI 订阅与临时容器 | [`GameplayTagContainerBinds`](Runtime/GameplayTagContainerBinds.cs)、[`GameplayTagContainerPool`](Runtime/GameplayTagContainerPool.cs) |

`GameplayTag` 提供 `Name`、`Label`、`Description`、`Flags`、`ParentTag`、`ParentTags`、`HierarchyTags` 和 `ChildTags`。它们描述注册表关系；`IsParentOf`、`IsChildOf` 不把自身算作祖先。`GameObjectGameplayTagContainer` 可把计数容器挂到对象上，持久标签在 `Awake` 加入，注册表仍需提前准备。

许可证：[MIT](LICENSE)。仓库主线只保留运行时、Editor 集成、包配置和使用文档。
