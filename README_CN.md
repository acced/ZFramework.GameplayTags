# ZFramework Gameplay Tags

**简体中文** | [English](README.md#en)

**用同一套标签，描述角色状态、技能条件和 Buff 叠加。**

ZFramework Gameplay Tags 是一个面向 Unity 的 C# 游戏标签库。它让不同系统都能回答一个问题：**这个角色或物品，现在具备哪些状态和能力？**

角色可以同时处于 `State.Burning`，持有 `Equipment.Weapon.Staff`，并具备 `Ability.Cast`。战斗系统添加状态，技能系统检查条件，UI 监听变化。它们使用同一套标签，不必知道状态具体来自哪个系统。

[解决什么问题](#why-tags) · [安装](#installation) · [快速开始](#quick-start) · [技能条件](#queries) · [Buff 叠加](#counts) · [变化通知](#events) · [Unity 集成](#unity-and-serialization) · [API 导航](#api-map)

<a name="why-tags"></a>
### 什么时候会用到它？

假设你正在做一个有技能、装备和 Buff 的游戏：

| 遇到的问题 | 用标签怎么处理 |
| --- | --- |
| 技能要求装备法杖、拥有施法能力，而且不能处于控制状态。 | 把“必须有”和“不能有”的标签组成一条规则。 |
| 火球和地面火焰同时造成燃烧，其中一个结束后，不应该直接清掉燃烧状态。 | 为每次贡献计数，最后一个来源移除后，状态才消失。 |
| 装备和光环都能赋予同一种能力，卸下装备不能把光环的效果一起删掉。 | 各自维护子容器，角色容器汇总它们的贡献。 |

标签还可以分层。`State.Control.Frozen` 和 `State.Control.Silenced` 都属于 `State.Control`。因此，“受到任意控制时不能施法”只需要检查父标签，不必把每一种控制效果都列出来。

它是一块基础积木，不是一整套技能框架。伤害怎么算、Buff 持续多久、AI 如何决策，仍由你的游戏负责；标签库只处理状态的表达、组合和查询。如果只是一个独立开关，直接用 `bool` 就很好。

<a name="installation"></a>
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

<a name="quick-start"></a>
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

这个例子在运行时创建容器。在 Inspector 中配置标签字段的用法见 [Unity 集成](#unity-and-serialization)。后面的代码片段放进业务方法即可，沿用上面的 `GameTags`。

<a name="tag-semantics"></a>
<a name="sets"></a>
### 分清两个问题：“属于这一类吗？”和“真的加过它吗？”

添加 `State.Control.Frozen` 后，可以这样理解容器：

```text
State                       ← HasTag 能匹配
└── Control                 ← HasTag 能匹配
    └── Frozen              ← 直接添加的标签，HasTagExact 也能匹配
```

`HasTag(GameTags.Control)` 问的是“是否处于任意控制状态”；`HasTagExact(GameTags.Frozen)` 问的是“是否明确存在冰冻”。这个关系不是双向的：只添加 `State.Control`，并不代表角色也处于冰冻。

普通 `GameplayTagContainer` 是一个**集合**，同一个标签加两次仍然只有一份。`TryAddTag`、`TryRemoveTag` 返回成员是否发生变化；多个系统需要同时持有同一标签时，改用[计数容器](#counts)。

`ExplicitTagCount` 统计直接添加的标签，`TagCount` 还包括它们的祖先。只有 `State.Control.Frozen` 的容器，因此是 **1 个显式标签、3 个总标签**。删除一个仅由后代带来的父标签不会清掉后代；应该删除实际添加的那个效果。

<a name="queries"></a>
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

<a name="counts"></a>
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

<a name="events"></a>
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

<a name="parent-containers"></a>
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

<a name="set-algebra"></a>
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

<a name="enumeration"></a>
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

<a name="registration"></a>
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

<a name="unity-and-serialization"></a>
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

<a name="pooling"></a>
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

<a name="player-registry"></a>
### Player 注册表：按实际构建采集，不猜 Editor 声明

普通项目继续使用程序集声明或显式 `Initialize`。希望 Player 启动不扫描程序集时，可以启用 Editor 生成器。它通过 Mono.Cecil **读取本次 Player 编译后的 DLL 元数据**，不加载或执行这些 DLL，不再把 Editor 域当成 Player。程序集定义的平台选择、`#if !UNITY_EDITOR`、平台宏、额外 scripting defines，以及随构建进入的托管插件，都以该次编译产物为准。

在 **Tools → Gameplay Tags → Generate Player Registry** 启用后，使用平时的 Build 流程构建目标。第一次或声明变化时，脚本编译后的回调会写出正确的 `Assets/GameplayTags.Generated.cs`，并停止当前构建。让 Unity 导入并编译新脚本，再以相同配置构建。第二次会同时核对源码和**已编译表的指纹**；旧代码没编进去就不会通过。切换平台或 defines 导致声明不同，也会更新后要求重编，不静默沿用旧表。

这是有意的两阶段生成，不尝试在已经完成的脚本编译阶段塞入新 C#。批处理时，第一次输出生成文件后，由构建任务重新启动一次 Unity 构建；不要无条件循环重试所有构建错误。自定义管线也可以在链接／裁剪前，将该次完整 Player 托管 DLL 列表交给 `GameplayTagRegistryGenerator.GenerateFromPlayerAssemblies(paths)`，返回 false 表示需要编译更新后的表。不存在输出文件时不启用生成检查。

生成表**不安装自动启动回调**。由自己的启动流程调用一次 `GameplayTags.Generated.GameplayTagRegistry.Initialize()`，再创建标签缓存和加载序列化容器；不要与另一套显式初始化混用。Editor 下该入口使用 Editor 自己的程序集声明，Player 下使用生成表。业务缓存类加显式静态构造器，可以避免 `beforefieldinit` 提前初始化；首次访问仍必须排在启动之后。

只支持直接的 `GameplayTagAttribute` 声明，包含构造参数和 `TagName`、`Description`、`Flags` 命名覆盖。自定义派生特性可能执行任意代码，元数据工具不能替它求值；发现这类声明会在构建时报错，改用直接特性或显式注册，不会把它静默遗漏。运行中加载的额外程序集不属于本次 Player 构建。Mono.Cecil 只被 Editor 程序集引用，不进入运行时 API。该回调会增加构建期读取／中转工作，不是零成本构建；实际 Unity/IL2CPP 整体流程仍需在项目中验收。

<a name="caller-contract"></a>
### 让热路径保持简单

运行时使用整数句柄、紧凑存储，并为较大的集合建立哈希索引，不把名称解析和通用校验放进每次查询。实际使用时，记住一条即可：**启动时注册，加载时解析，运行时复用句柄和容器。**

`EnsureCapacity(n)` 按**包含祖先的不同成员总数**预留，不是只按叶标签数量。具体类型的枚举使用结构体枚举器，通过 `IEnumerable<GameplayTag>` 使用时可能装箱。新建、扩容、序列化、订阅和池首次使用仍可能分配内存。这是托管 C# 实现，不是可直接放进 Burst/Jobs 的 `NativeContainer`。

调用方需要提供有效、非 `None` 的句柄和非 null 修改操作数，保证数量为正、容量有效、计数不溢出，删除不超过自己拥有的次数，并管理好同步、父链贡献和对象池生命周期。共享 `Empty` 按只读使用。非法使用不属于内核的恢复职责；把输入校验和错误处理放在业务边界即可。

**版本说明：** 当前源码为 `2.1.0-rc.2-optimized.2`，不代表已经完成 Unity/IL2CPP 全面验收，也不承诺所有场景普遍更快。容量与池化策略，请根据项目自己的标签数量、层级深度和目标后端来选择。

<a name="api-map"></a>
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
