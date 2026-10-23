# ZFramework Gameplay Tags

**[English](README.md) | 简体中文**

[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-000000?logo=unity)](https://unity.com/)
[![Language](https://img.shields.io/badge/Language-C%23%209.0-239120?logo=csharp)](https://learn.microsoft.com/dotnet/csharp/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

> **让战斗、技能、装备和 UI 用同一套“词典”对话，彻底告别散落满地的布尔开关与枚举耦合。**

在动作、RPG 或战术游戏中，你一定遇到过这些令人头疼的场景：
- **布尔地狱**：`isStunned`、`isFrozen`、`isSilenced`、`isDisarmed`……每加一种新控制效果，就要去翻遍移动、普攻、闪避和施法脚本修改 `if` 判断。
- **Buff 互相误删**：火球术给角色挂了 3 秒“燃烧”，角色同时又踩在“火焰地面”上。3 秒后火球术到期，直接把燃烧状态设成了 `false`——结果人还站在火里，身上的火却灭了。
- **跨系统强耦合**：判断一个技能能不能放，技能系统得去问背包系统“有没有拿法杖”、问 Buff 系统“有没有被沉默”、问角色状态机“是不是在空中”。

**ZFramework Gameplay Tags** 是一个面向 Unity 的高性能层级标签库。它采用**可视化编辑器配置 + 静态代码生成 + 默认自动初始化 + 4 字节整数句柄**的设计：在编辑器里统一维护标签树，一键生成强类型 C# 字段；运行时开箱即用，只做纯粹的整数与紧凑数组运算，不扫描程序集、不在热路径里查字符串。

---

## 目录

- [核心工作流：三步跑通](#核心工作流三步跑通)
- [安装指南](#安装指南)
- [五分钟快速上手](#五分钟快速上手)
- [实战场景与代码示例](#实战场景与代码示例)
  - [场景 1：层级匹配与控制判定（`HasTag` vs `HasTagExact`）](#场景-1层级匹配与控制判定hastag-vs-hastagexact)
  - [场景 2：多来源 Buff 叠加与安全移除（`GameplayTagCountContainer`）](#场景-2多来源-buff-叠加与安全移除gameplaytagcountcontainer)
  - [场景 3：装备、光环与角色状态汇总（`GameplayTagHierarchicalContainer`）](#场景-3装备光环与角色状态汇总gameplaytaghierarchicalcontainer)
  - [场景 4：技能释放条件与批量规则缓存（`GameplayTagRequirements` & `RuleSet`）](#场景-4技能释放条件与批量规则缓存gameplaytagrequirements--ruleset)
  - [场景 5：事件驱动的 UI 与特效绑定（`RegisterTagEventCallback` & `Binds`）](#场景-5事件驱动的-ui-与特效绑定registertageventcallback--binds)
  - [场景 6：集合运算与零 GC 容器复用（`Union` / `Intersection` / `Pool`）](#场景-6集合运算与零-gc-容器复用union--intersection--pool)
- [Unity 集成：自动初始化、Inspector 与存档](#unity-集成自动初始化inspector-与存档)
  - [初始化机制：默认全自动，按需可手动](#初始化机制默认全自动按需可手动)
  - [在 Inspector 中直接配置标签](#在-inspector-中直接配置标签)
  - [存档与读档（Save / Load）](#存档与读档save--load)
- [标签编辑器与代码生成详解](#标签编辑器与代码生成详解)
- [可运行示例：Tag Playground](#可运行示例tag-playground)
- [核心 API 速查表](#核心-api-速查表)
- [进阶说明：在非 Unity 环境或自定义管线中使用](#进阶说明在非-unity-环境或自定义管线中使用)
- [旧版本升级指南](#旧版本升级指南)
- [性能边界与使用约定](#性能边界与使用约定)

---

## 核心工作流：三步跑通

本库摒弃了传统的 `[assembly: GameplayTag]` 特性扫描和 Mono.Cecil 后处理，采用更直观、编译更快的 **SO 配置 + 代码生成 + 自动初始化** 流程：

```text
┌─────────────────────────────┐    ┌─────────────────────────────┐    ┌─────────────────────────────┐
│ 1. 可视化配置               │    │ 2. 一键生成代码             │    │ 3. 运行时直接使用           │
│ Tools → Gameplay Tags       │ ──►│ 生成 GameplayTagRegistry    │ ──►│ 注册表在首次查询或反序列化  │
│ → Tag Settings 编辑标签树   │    │ 与强类型只读字段 Tags.*     │    │ 时自动就绪，直接用 Tags.*   │
└─────────────────────────────┘    └─────────────────────────────┘    └─────────────────────────────┘
```

- **它解决什么？** 跨系统的状态表达、层级归类、多来源计数叠加、技能条件前置校验，以及状态变化通知。
- **它不替你做什么？** 它不是一整套沉重的技能框架（GAS），不管伤害公式、冷却倒计时或特效播放。如果只是一个模块内部独立的开关，直接用 `bool` 依然是最好的选择。

---

## 安装指南

要求 **Unity 2022.3 或更高版本**。

1. 打开 **Window → Package Manager**。
2. 点击左上角 **+ → Add package from git URL...**，输入：
   ```text
   https://github.com/acced/ZFramework.GameplayTags.git#main
   ```
   *(团队项目若需锁定版本，可将 `#main` 替换为具体的 Commit SHA；本地调试可选择 **Add package from disk...** 并选中仓库下的 `package.json`。)*
3. **配置程序集引用（`.asmdef`）**：
   - 你的业务程序集需要引用 **`ZFramework.GameplayTags`**。
   - 在完成下一步“生成代码”后，工程会自动生成 `Assets/GameplayTags.Generated/ZFramework.GameplayTags.Generated.asmdef`（默认开启 `autoReferenced`）。如果你的业务 `.asmdef` 关闭了自动引用，请将 `ZFramework.GameplayTags.Generated` 一并加入引用列表。

---

## 五分钟快速上手

### 第一步：在编辑器中添加标签

1. 打开菜单 **Tools → Gameplay Tags → Tag Settings**。
2. 点击 **“创建标签配置”**，默认会在 `Assets/GameplayTags/Editor/GameplayTagSettings.asset` 创建项目中唯一的一份标签配置资产。
3. 在窗口中依次添加以下 5 个标签（可按需填写备注说明）：
   ```text
   State.Burning
   State.Control.Frozen
   State.Control.Silenced
   Ability.Cast
   Equipment.Weapon.Staff
   ```
   > **提示**：你只需要添加最末端的具体标签，像 `State`、`State.Control`、`Equipment`、`Equipment.Weapon` 这样的父层级会被自动推导并补齐（在树状图中显示为灰色节点），无需手动逐级创建。

### 第二步：生成 C# 访问代码

点击窗口顶部工具栏的 **“生成代码”** 按钮（或菜单 **Tools → Gameplay Tags → Generate Player Registry**），等待 Unity 编译完成。

此时工程下会生成 `Assets/GameplayTags.Generated/GameplayTags.Generated.cs`。所有的标签（包括自动推导的父标签）都会变成 `GameplayTagRegistry.Tags` 下的静态只读字段，其中 `.` 会被转换为双下划线 `__`：
- `State.Control` → `Tags.State__Control`
- `State.Control.Frozen` → `Tags.State__Control__Frozen`

### 第三步：写一段测试脚本

创建一个脚本 `QuickStartExample.cs`，挂在空物体上并点击 Play（**默认无需手动调用初始化**）：

```csharp
using GameplayTags;
using UnityEngine;
using Tags = GameplayTags.Generated.GameplayTagRegistry.Tags;

public sealed class QuickStartExample : MonoBehaviour
{
    private void Start()
    {
        // 1. 创建一个标签容器，给角色加上“冰冻”状态（注册表会自动完成初始化）
        var states = new GameplayTagContainer();
        states.AddTag(Tags.State__Control__Frozen);

        // 2. 层级查询 vs 精确查询
        Debug.Log(states.HasTag(Tags.State__Control));              // True：冰冻属于控制状态（State.Control）
        Debug.Log(states.HasTag(Tags.State));                       // True：冰冻也属于状态（State）
        Debug.Log(states.HasTagExact(Tags.State__Control));         // False：我们并没有直接添加 State.Control 本身
        Debug.Log(states.HasTagExact(Tags.State__Control__Frozen)); // True：明确添加过冰冻

        // 3. 移除冰冻状态
        states.RemoveTag(Tags.State__Control__Frozen);
        Debug.Log(states.IsEmpty);                                  // True：容器已空
    }
}
```

---

## 实战场景与代码示例

以下示例统一引入了命名空间与生成的标签别名：
```csharp
using GameplayTags;
using Tags = GameplayTags.Generated.GameplayTagRegistry.Tags;
```

### 场景 1：层级匹配与控制判定（`HasTag` vs `HasTagExact`）

当角色被施加 `State.Control.Frozen` 时，容器内部的状态结构如下：

```text
State                       ◄── 隐式祖先：HasTag 匹配为 True，HasTagExact 为 False
└── Control                 ◄── 隐式祖先：HasTag 匹配为 True，HasTagExact 为 False
    └── Frozen              ◄── 显式标签：直接添加的成员，HasTag 与 HasTagExact 均为 True
```

这让你可以在不同维度向容器提问：
- **宽泛归类查询 (`HasTag`)**：“角色现在有没有受到任何控制？” —— 调用 `states.HasTag(Tags.State__Control)`。无论是冰冻、眩晕还是沉默，只要挂在 `State.Control` 下都能命中，以后策划新增 `State.Control.Polymorph`（变形术）也不用改一行移动或施法代码。
- **精确点名查询 (`HasTagExact`)**：“角色身上是不是明确挂着冰冻（以触发碎冰双倍伤害）？” —— 调用 `states.HasTagExact(Tags.State__Control__Frozen)`。

```csharp
var states = new GameplayTagContainer();
states.AddTag(Tags.State__Control__Frozen);

int explicitCount = states.ExplicitTagCount; // 1（只直接加了 Frozen）
int totalCount    = states.TagCount;         // 3（包含 State、State.Control、State.Control.Frozen）

// 遍历实际添加的 Buff/状态列表（只输出 State.Control.Frozen）
foreach (GameplayTag tag in states.GetExplicitTags())
{
    Debug.Log($"显式状态: {tag.Name}");
}

// 默认遍历包含所有隐式父节点（输出 State、State.Control、State.Control.Frozen，顺序不固定）
foreach (GameplayTag tag in states)
{
    Debug.Log($"全集节点: {tag.Name}");
}
```

> **注意**：
> 1. **父不推子**：只添加父标签 `State.Control`，绝不代表角色拥有子标签 `State.Control.Frozen`。
> 2. **删叶不删根**：普通 `GameplayTagContainer` 是一个**集合**（重复添加同一标签不会叠层）。移除一个未曾显式添加过的父标签（如 `RemoveTag(Tags.State__Control)`）不会清除其下的 `Frozen`，请始终移除当初实际添加的那个具体标签。

---

### 场景 2：多来源 Buff 叠加与安全移除（`GameplayTagCountContainer`）

普通容器只记录“有或没有”。当**火球术**和**火焰地面**同时给角色附加 `State.Burning` 时，使用计数容器 `GameplayTagCountContainer` 可以避免在其中一个效果结束时误删另一个效果的状态：

```csharp
var effects = new GameplayTagCountContainer();

// 两个独立来源各自添加一次燃烧
effects.AddTag(Tags.State__Burning); // 火球术命中 (+1)
effects.AddTag(Tags.State__Burning); // 踩上火焰地面 (+1)

Debug.Log(effects.GetExplicitTagCount(Tags.State__Burning)); // 2

// 火球术持续时间结束，移除它自己的那 1 份贡献
effects.RemoveTag(Tags.State__Burning);
Debug.Log(effects.HasTag(Tags.State__Burning));              // True：人还在火里，燃烧继续保持！

// 角色走出火焰地面，移除最后 1 份贡献
effects.RemoveTag(Tags.State__Burning);
Debug.Log(effects.HasTag(Tags.State__Burning));              // False：所有来源都已移除，燃烧结束
```

- **多层一次性操作**：支持 `AddTag(tag, amount)` 和 `RemoveTag(tag, amount)`。
- **自身计数 vs 分类汇总计数**：`GetExplicitTagCount(tag)` 返回该标签自身被直接添加的次数；`GetTagCount(tag)` 则会加上所有子标签的贡献总和（例如 2 层 `State.Burning` + 1 层 `State.Control.Frozen`，会让 `effects.GetTagCount(Tags.State)` 等于 `3`）。

---

### 场景 3：装备、光环与角色状态汇总（`GameplayTagHierarchicalContainer`）

如果“法杖”赋予了 `Ability.Cast`，“奥术光环”也赋予了 `Ability.Cast`，把它们直接混进角色的同一个容器里，卸下装备时很容易算错账。

使用 `GameplayTagHierarchicalContainer`，每个子系统自己管自己的子容器，并将 `Parent` 指向角色的总容器：

```csharp
var actor     = new GameplayTagCountContainer();
var equipment = new GameplayTagHierarchicalContainer { Parent = actor };
var aura      = new GameplayTagHierarchicalContainer { Parent = actor };

// 装备与光环各自向角色贡献能力
equipment.AddTag(Tags.Ability__Cast);
aura.AddTag(Tags.Ability__Cast);

Debug.Log(actor.GetTagCount(Tags.Ability__Cast)); // 2：角色总容器自动汇总

// 卸下装备：直接断开父子连接，它贡献给角色的标签会自动撤回
equipment.Parent = null;
Debug.Log(actor.HasTag(Tags.Ability__Cast));       // True（计数为 1）：光环依然生效！

// 重新穿上装备：重新挂回 Parent，装备里的标签会再次同步给角色
equipment.Parent = actor;
Debug.Log(actor.GetTagCount(Tags.Ability__Cast)); // 2
```

> **提示**：这种关系是**子容器向父容器向上汇总**，而不是子容器继承父容器的标签。当子容器还挂在父容器上时，请通过子容器来增删标签或 `Clear()`，不要直接清空顶层父容器。

---

### 场景 4：技能释放条件与批量规则缓存（`GameplayTagRequirements` & `RuleSet`）

#### 单条技能规则 (`GameplayTagRequirements`)

假设释放“炎爆术”需要同时满足：**持有法杖**、**拥有施法能力**，且**没有处于任何控制状态**。你可以在技能初始化时构建一条不可变规则，在运行时反复调用：

```csharp
// 参数依次为：(禁止的标签, 必需标签1, 必需标签2) —— 零临时数组分配
var pyroblastRule = GameplayTagRequirements.Create(
    forbidden:          Tags.State__Control,
    required:           Tags.Ability__Cast,
    additionalRequired: Tags.Equipment__Weapon__Staff);

var actor = new GameplayTagContainer();
actor.AddTag(Tags.Ability__Cast);
actor.AddTag(Tags.Equipment__Weapon__Staff);

Debug.Log(pyroblastRule.Matches(actor)); // True：条件满足，允许施法

actor.AddTag(Tags.State__Control__Silenced);
Debug.Log(pyroblastRule.Matches(actor)); // False：被沉默（属于 State.Control），禁止施法
```

- **更多条件**：如果禁止或必需标签超过上面快捷方法的数量，可使用 `new GameplayTagRequirements(forbiddenSpanOrArray, requiredSpanOrArray)`。构造时会自动去重、化简包含关系并识别自相矛盾的条件；不超过 4 个有效条件时完全内联在结构体中，无堆内存分配。
- **跨两个容器联合匹配**：如果角色把“常驻装备标签”和“临时 Buff 标签”放在两个容器里，无需先合并，直接调用 `pyroblastRule.Matches(equipmentTags, buffTags)` 即可零分配完成检查。
- **简单集合查询**：如果不需要构造成规则对象，也可以直接用容器扩展方法 `HasAny(query)`、`HasAll(query)` 以及精确匹配版 `HasAnyExact(query)`、`HasAllExact(query)`。

#### 多技能共享规则组与增量缓存 (`GameplayTagRuleSet`)

当角色技能栏里有十几个技能，很多技能都在重复检查 `State.Control` 或 `Ability.Cast` 时，可以用 `GameplayTagRuleSet` 把规则打包：

```csharp
// 1. 全游戏共享一份规则组定义
var skillRules = new GameplayTagRuleSet(new[]
{
    pyroblastRule,
    new GameplayTagRequirements(
        forbidden: System.Array.Empty<GameplayTag>(),
        required:  new[] { Tags.State__Burning })
});

// 2. 每个角色各自持有一份评估缓存
GameplayTagRuleSet.Cache actorRuleCache = skillRules.CreateCache();

// 3. 评估所有技能可用性
System.ReadOnlySpan<bool> canUseSkills = actorRuleCache.Evaluate(actor);
bool canCastPyroblast = canUseSkills[0];
bool canUseFireCombo  = canUseSkills[1];
```

`Cache` 会追踪容器的“存在性版本号（PresenceVersion）”：
- 如果角色身上的标签种类没有增减（哪怕某层 Buff 从 1 层叠到了 5 层），`Evaluate` 会**直接返回上次缓存的结果数组**。
- 当有标签新增或彻底消失时，它只重新查询发生变化的条件，并仅重算受该条件影响的技能规则。

---

### 场景 5：事件驱动的 UI 与特效绑定（`RegisterTagEventCallback` & `Binds`）

不要在 `Update` 里每帧轮询标签来刷新 UI 图标或角色身上的火焰特效。计数容器提供了精确的事件回调：

#### 方式 A：监听层数变化或状态出现/消失

```csharp
var effects = new GameplayTagCountContainer();

OnTagCountChangedDelegate onBurningChanged = (tag, newCount) =>
{
    Debug.Log($"{tag.Name} 当前层数: {newCount}");
};

// AnyCountChange：每次层数变化都触发（1 → 2 → 1 → 0）
// NewOrRemoved：只在“从无到有 (0 → >0)”和“从有到无 (>0 → 0)”时触发
effects.RegisterTagEventCallback(
    Tags.State__Burning,
    GameplayTagEventType.AnyCountChange,
    onBurningChanged);

effects.AddTag(Tags.State__Burning);    // 输出: 1
effects.AddTag(Tags.State__Burning);    // 输出: 2
effects.RemoveTag(Tags.State__Burning); // 输出: 1
effects.Clear();                        // 输出: 0（Clear 会清零所有标签并触发归零通知，但保留订阅关系）

// 不再需要时取消订阅
effects.RemoveTagEventCallback(
    Tags.State__Burning,
    GameplayTagEventType.AnyCountChange,
    onBurningChanged);
```

> **技巧**：你可以直接订阅父标签（例如 `Tags.State__Control`）配合 `GameplayTagEventType.NewOrRemoved`，这样不管是冰冻、眩晕还是沉默，只要角色进入或脱离“被控制”状态，你的受控 UI 提示就能收到通知。

#### 方式 B：使用 `GameplayTagContainerBinds` 绑定 UI 视图生命周期

在写 UI 面板时，通常需要在打开面板时**立刻读取一次当前状态**，并在面板关闭时**一键注销所有监听**：

```csharp
public sealed class StatusHudView : MonoBehaviour
{
    private GameplayTagContainerBinds m_Binds;

    public void BindActor(GameplayTagCountContainer actorEffects)
    {
        m_Binds.Dispose(); // 清理旧绑定
        m_Binds = new GameplayTagContainerBinds(actorEffects);

        // Bind 会在注册时立即回调一次当前是否存在，之后在状态出现/消失时再次回调
        m_Binds.Bind(Tags.State__Burning, isBurning =>
        {
            Debug.Log($"燃烧图标显示状态: {isBurning}");
        });

        m_Binds.Bind(Tags.State__Control, isControlled =>
        {
            Debug.Log($"控制遮罩显示状态: {isControlled}");
        });
    }

    private void OnDestroy()
    {
        // 销毁面板时一键退订整组事件，不会影响 actorEffects 容器本身
        m_Binds.Dispose();
    }
}
```

---

### 场景 6：集合运算与零 GC 容器复用（`Union` / `Intersection` / `Pool`）

需要对两个容器求并集、交集或差集时：

```csharp
var left = new GameplayTagContainer();
left.AddTag(Tags.State__Burning);
left.AddTag(Tags.Ability__Cast);

var right = new GameplayTagContainer();
right.AddTag(Tags.State__Control__Frozen);
right.AddTag(Tags.Ability__Cast);

// 分配新容器返回结果
var union        = GameplayTagContainer.Union(left, right);        // Burning, Frozen, Cast
var intersection = GameplayTagContainer.Intersection(left, right); // 只有 Cast
var diff         = left.Clone();
diff.RemoveTags(right);                                            // 只有 Burning
```

> **注意**：集合运算始终基于**显式标签**进行，再由结果推导祖先。`{State.Burning}` 和 `{State.Control.Frozen}` 的交集是**空集**，不会因为它们都有共同祖先 `State` 就交出一个显式的 `State`。

#### 复用目标容器与线程本地对象池 (`GameplayTagContainerPool`)

在高频逻辑中，你可以复用同一个输出容器，或者从对象池借用临时容器以避免 GC：

```csharp
// 1. 写入已有容器（支持 output 与 left 或 right 为同一个容器）
var reusableOutput = new GameplayTagContainer(capacity: 32);
GameplayTagContainer.Union(reusableOutput, left, right);
GameplayTagContainer.Intersection(reusableOutput, left, right);
reusableOutput.Clear(); // 清空内容但保留底层数组容量

// 2. 在局部作用域中从对象池借用临时容器
using (GameplayTagContainerPool.Get(out GameplayTagContainer scratch))
{
    scratch.AddTag(Tags.State__Burning);
    // 在此作用域内使用 scratch，离开 using 块自动清空并归还池中
}
```

对象池默认采用无上限保留策略（`RetentionPolicy.Unbounded`），保证大容器不会因为内置固定阈值被悄悄丢弃。你也可以在启动阶段为当前线程配置保留上限：

```csharp
GameplayTagContainerPool.Configure(new GameplayTagContainerPool.RetentionPolicy(
    maxCount: 32,
    maxContainerBytes: 256 * 1024,
    maxTotalBytes: 4L * 1024 * 1024));
```

---

## Unity 集成：自动初始化、Inspector 与存档

### 初始化机制：默认全自动，按需可手动

在默认配置下，**你不需要在业务代码里手写 `GameplayTagRegistry.Initialize()`**：

1. **首次查询与反序列化自动初始化**：
   - 生成的 `GameplayTagRegistry` 带有 `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`，会在 Unity 启动最早期自动完成注册。
   - 如果某个场景对象、Prefab、ScriptableObject 反序列化或首次访问 `Tags.*` / `RequestTag` 发生得更早（甚至在后台反序列化线程中），`GameplayTagManager` 也会自动发现并调用 `GameplayTagRegistry.Initialize()`，无需担心初始化顺序问题。
2. **保留 `GameplayTagRegistry.Initialize()` 且幂等安全**：
   - 如果你的项目有统一的 Boot 预热流程，依然可以显式调用 `GameplayTagRegistry.Initialize()` 或 `GameplayTagManager.Initialize(...)`。
   - **重复调用不会重建注册表**：一旦注册表初始化完成，后续的 `Initialize` 调用会直接安全跳过（No-op），已缓存的所有 `GameplayTag` 句柄始终保持有效。
3. **可选的纯手动模式（`GAMEPLAY_TAGS_MANUAL_INITIALIZATION`）**：
   - 如果你希望完全关闭自动发现与 `RuntimeInitializeOnLoadMethod`（例如需要由外部网络下发配置或完全自定义启动时序），可以在 Unity 的 **Player Settings → Scripting Define Symbols** 中添加宏：
     ```text
     GAMEPLAY_TAGS_MANUAL_INITIALIZATION
     ```
   - 开启该宏后，必须在首次访问任何标签或反序列化含标签的资产之前，手动调用 `GameplayTagRegistry.Initialize()` 或 `GameplayTagManager.Initialize(registrations)`，否则将直接抛出 `InvalidOperationException`。

### 在 Inspector 中直接配置标签

在 Unity 中，`GameplayTagContainer` 既可以在 Inspector 面板上直接勾选配置，也可以在运行时直接调用 `HasTag` / `AddTag`，**不需要拆分成两套 Data 类，也不需要手动调转换函数**：

```csharp
using GameplayTags;
using UnityEngine;
using Tags = GameplayTags.Generated.GameplayTagRegistry.Tags;

public sealed class CharacterState : MonoBehaviour
{
    // 在 Inspector 中点击 "Edit Tags..." 即可弹出树状选择器勾选初始标签，反序列化时自动就绪
    [SerializeField] private GameplayTagContainer m_States = new GameplayTagContainer();

    // 单个标签字段同样提供下拉树状选择器
    [SerializeField] private GameplayTag m_CategoryTag;

    private void Awake()
    {
        // 注意：独立的 GameplayTag 是值类型 (struct)，如果需要在运行时读取 Inspector 配置的单标签，
        // 请在加载后调用一次 Resolve() 并赋回给它自己：
        m_CategoryTag = m_CategoryTag.Resolve();
    }

    public bool IsControlled => m_States.HasTag(Tags.State__Control);
    public void ApplyBurning() => m_States.AddTag(Tags.State__Burning);
}
```

- **共享 ScriptableObject 配置模板**：如果多个怪物共享同一个 `ScriptableObject` 上的 `GameplayTagContainer` 作为初始属性模板，请用 `var runtimeTags = config.InitialTags.Clone();` 拷贝一份独立容器给怪物实例使用，避免直接修改共享模板。
- **`GameObjectGameplayTagContainer` 组件**：包内自带了一个挂在 GameObject 上的便捷组件，内部持有一个序列化配置容器 `m_PersistentTags` 和一个运行时计数容器 `GameplayTagContainer`（在 `Awake` 中自动将 `m_PersistentTags` 加入计数容器），并提供 `gameObject.Bind()` 扩展方法快速创建事件绑定。

### 存档与读档（Save / Load）

- **永远保存标签名称（`tag.Name`），绝不保存 `tag.RuntimeIndex`**：整数 `RuntimeIndex` 是当前注册表按字典序生成的运行时编号。一旦以后新增或删除了标签，编号就会顺移。
- **`GameplayTagContainer` 原生支持序列化**：无论是 Unity 序列化还是 `DataContractSerializer`，它都会自动只保存显式标签名称（按字典序排序），并在反序列化时自动初始化注册表并还原祖先关系。
- **计数容器 (`GameplayTagCountContainer`) 的存档**：遍历 `container.GetExplicitTags()`，将每个 `tag.Name` 与 `container.GetExplicitTagCount(tag)` 保存下来；读档时通过 `GameplayTagManager.RequestTag(savedName, out GameplayTag tag)` 校验名称是否仍然有效，再调用 `AddTag(tag, savedCount)` 恢复即可（切勿把隐式祖先的汇总计数当成显式层数重复添加）。

---

## 标签编辑器与代码生成详解

打开 **Tools → Gameplay Tags → Tag Settings** 即可管理全项目的标签：

- **单一数据源**：全项目只保留一份 `GameplayTagSettings.asset`（放在 `Editor` 目录下即可，它只用于编辑器绘制和生成代码，不会打进 Player 包体）。编辑器下的标签选择器直接读取该 SO，不会触发运行时注册表初始化。
- **树状管理与搜索**：支持按全路径关键字搜索、新增根标签、新增子标签、编辑说明、整支重命名、整支删除，以及完整的 **Undo / Redo**。
- **隐式父节点转正**：树状图中灰色的父节点是自动推导出来的；如果你选中它并保存一段说明文字，它就会变成显式登记的标签。
- **重命名与引用同步**：在窗口中重命名某个父节点时，配置表里的所有子节点会自动同步改名；但不会自动改写场景、Prefab 或 C# 脚本里的旧引用。改名并点击 **“生成代码”** 后，编译器会直接报出旧字段名（如 `Tags.Old__Name`）的位置，方便你安全定位和替换。
- **构建前自动校验（Build Check）**：在打包 Player 之前，`IPreprocessBuildWithReport` 会自动检查 `GameplayTagSettings` 与 `GameplayTags.Generated.cs` 的指纹是否一致。如果改了配置忘了点生成，构建会立即中止并给出明确提示，杜绝“编辑器里改了标签、打出来的包还是旧表”的隐患。

---

## 可运行示例：Tag Playground

包内附带了一个开箱即用的纯日志示例，无需搭建任何 UI，也不用手动写初始化代码，即可在 Console 观察所有核心 API 的运行结果：

1. 确保包已安装且编译完成。
2. 在 **Window → Package Manager** 中选中 **ZFramework Gameplay Tags**，展开右侧 **Samples**，点击 **Import** 导入 **Tag Playground**。
   *(注：如果项目中已经有自己的 `GameplayTagSettings.asset`，请将示例中的 5 个 `Demo.*` 标签合入你的配置并删除示例附带的 SO，保持全项目只有一份配置。)*
3. 打开 **Tools → Gameplay Tags → Generate Player Registry** 生成代码并等待编译完成。
4. 打开示例场景 `TagPlayground.unity` 点击 Play，即可在 Console 看到层级匹配、施法条件校验、双来源火焰叠加以及事件回调（`1 → 2 → 1 → 0`）的完整输出。

---

## 核心 API 速查表

| 你想做的事 | 推荐类型 / 方法 |
| :--- | :--- |
| **表示单个标签句柄** | [`GameplayTag`](Runtime/GameplayTag.cs) — 4 字节整数 ID 比较，`ParentTag`、`ParentTags`、`ChildTags`、`IsParentOf`、`IsChildOf` |
| **保存状态集合（无叠层、可序列化）** | [`GameplayTagContainer`](Runtime/GameplayTagContainer.cs) — `AddTag`、`RemoveTag`、`HasTag`、`HasTagExact`、`HasAny`、`HasAll`、`Union`、`Intersection`、`Clone` |
| **管理多来源 Buff 叠层与事件** | [`GameplayTagCountContainer`](Runtime/GameplayTagCountContainer.cs) — `AddTag(tag, amount)`、`GetExplicitTagCount`、`GetTagCount`、`RegisterTagEventCallback` |
| **将装备/光环等子系统状态汇总到角色** | [`GameplayTagHierarchicalContainer`](Runtime/GameplayTagHierarchicalContainer.cs) — 设置 `Parent` 自动同步增删与挂载/卸载 |
| **定义技能释放的必需/禁止标签规则** | [`GameplayTagRequirements`](Runtime/GameplayTagRequirements.cs) — `Create(forbidden, req1, req2)`、`Matches(actor)`、`Matches(equip, buffs)` |
| **批量评估多条技能规则并缓存结果** | [`GameplayTagRuleSet`](Runtime/GameplayTagRuleSet.cs) & `GameplayTagRuleSet.Cache` — `CreateCache()`、`cache.Evaluate(actor)` |
| **成组绑定 UI 状态监听并一键注销** | [`GameplayTagContainerBinds`](Runtime/GameplayTagContainerBinds.cs) — `Bind(tag, Action<bool>)`、`Dispose()` / `gameObject.Bind()` |
| **在高频局部逻辑中借用临时容器** | [`GameplayTagContainerPool`](Runtime/GameplayTagContainerPool.cs) — `using (GameplayTagContainerPool.Get(out var temp))` |
| **按字符串查找标签或自定义注册表** | [`GameplayTagManager`](Runtime/GameplayTagManager.cs) — `RequestTag(name, out tag)`、`GetAllTags()`、`Initialize(...)` |

---

## 进阶说明：在非 Unity 环境或自定义管线中使用

核心运行时没有任何 Unity 强依赖，可直接通过仓库根目录的 `GameplayTags.Runtime.csproj` 编译为标准的 **.NET Standard 2.1 / C# 9** 类库（例如用于独立战斗校验服或纯 C# 单元测试）：

```bash
dotnet build GameplayTags.Runtime.csproj -c Release
```

- **如果一起编译了生成的 `GameplayTags.Generated.cs`**：即使在非 Unity 环境下，首次查询标签或反序列化时也会自动发现并调用 `GameplayTagRegistry.Initialize()`。
- **如果不使用代码生成器（纯动态配置表）**：只需在首次访问标签前调用一次 `GameplayTagManager.Initialize` 传入注册表即可（后续重复调用将被忽略）：

```csharp
GameplayTagManager.Initialize(
    new GameplayTagRegistration("State.Burning", "持续火焰伤害"),
    new GameplayTagRegistration("State.Control.Frozen", "无法移动和施法"),
    new GameplayTagRegistration("Ability.Cast", "允许吟唱法术"));

// 启动或配置加载阶段解析一次并缓存句柄，运行时不要每帧用字符串查找
GameplayTag burningTag = GameplayTagManager.RequestTag("State.Burning");
```

---

## 旧版本升级指南

如果你是从早期版本升级而来，请注意以下变更：

1. **升级后请重新生成一次代码**：点击 **Tools → Gameplay Tags → Generate Player Registry** 重新生成 `GameplayTags.Generated.cs`（新版生成器格式版本为 `v5`，内置自动初始化支持）。
2. **默认无需手写 `GameplayTagRegistry.Initialize()`**：现在首次查询和反序列化都会自动初始化注册表；已有代码里保留的 `GameplayTagRegistry.Initialize()` 依然兼容且不会重复重建注册表。若需严格手动控制，可添加 `GAMEPLAY_TAGS_MANUAL_INITIALIZATION` 编译宏。
3. **移除程序集扫描与相关特性**：`GameplayTagAttribute`、`GameplayTagFlags`、`InitializeIfNeeded()` 以及基于 Mono.Cecil 的 Player DLL 扫描已彻底移除。请将标签定义迁移至 **Tools → Gameplay Tags → Tag Settings** 的 SO 配置中。
4. **清理旧的生成文件**：新版的生成目录为 `Assets/GameplayTags.Generated/`（含独立 `.asmdef`）。如果工程根目录下还留有旧版单文件 `Assets/GameplayTags.Generated.cs` 及其 `.meta`，请将其删除。
5. **不可变规则构造**：`GameplayTagRequirements` 现在是不可变的 `readonly struct`，通过标签数组或 `ReadOnlySpan<GameplayTag>` 构造，不再引用两个可变容器。

---

## 性能边界与使用约定

为了保持运行时热路径足够精简，本库采用**调用方契约（Caller-Contract）**设计——把字符串解析、自动初始化和格式校验留在冷路径边界（首次加载/反序列化），不在每次 `HasTag` / `AddTag` 时做防御性兜底：

- **冷路径解析，热路径复用句柄**：不要在每帧循环里调用 `RequestTag("...")` 或反复 `new GameplayTagRuleSet(...)`，请直接使用生成的 `Tags.*` 静态字段或缓存好的 `GameplayTag` 句柄。
- **传入有效句柄与合法参数**：修改容器时请传入已注册的非 `None` 标签，叠加层数 `amount` 需为正整数，移除层数不应超过当前系统自己持有的层数；`GameplayTagContainer.Empty` 为全局共享空容器，请仅作只读使用。
- **容量预留**：`EnsureCapacity(n)` 预留的是**包含隐式祖先在内的不同标签总数**，而非仅显式叶标签个数。
- **线程与运行环境**：注册表初始化线程安全；容器、计数事件与 `RuleSet.Cache` 面向单线程游戏逻辑设计（对象池为 `[ThreadStatic]` 线程本地池，需在借用线程归还）；核心为托管 C# 实现，并非 Burst / Jobs 直接使用的 `NativeContainer`。

---

## 许可证与反馈

本项目基于 [MIT License](LICENSE) 开源。欢迎通过 [GitHub Issues](https://github.com/acced/ZFramework.GameplayTags/issues) 提交问题或建议。
