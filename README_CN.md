# ZFramework Gameplay Tags

**[English](README.md) | 简体中文**

[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-000000?logo=unity)](https://unity.com/)
[![Language](https://img.shields.io/badge/Language-C%23%209.0-239120?logo=csharp)](https://learn.microsoft.com/dotnet/csharp/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

> **让战斗、技能、装备和 UI 用同一套“语言”对话，把你的代码从满屏的 `bool` 和耦合的 `enum` 里解放出来。**

---

## 为什么要做这个库？

只要你写过动作、RPG、肉鸽或策略游戏，一定经历过下面这三个时刻：

1. **永远改不完的 `if` 判断**
   一开始角色只有眩晕，你在移动和施法代码里写了 `if (isStunned) return;`。后来策划加了冰冻、石化、睡眠、羊刀变形……于是你的代码变成了：
   ```csharp
   if (isStunned || isFrozen || isPetrified || isSleeping || isPolymorphed) return;
   ```
   每加一种控制，你就要把移动、普攻、闪避、技能脚本全翻出来改一遍，漏掉一处就是 Bug。

2. **两个火球把火浇灭了**
   火球术给怪物挂了 3 秒“燃烧”，同时怪物又踩在“火焰地面”上。3 秒后火球术时间到了，顺手执行了一句 `isBurning = false`——结果怪物明明还站在火海里，身上的燃烧效果却莫名其妙消失了。

3. **技能系统认识了所有人**
   为了判断一发“炎爆术”能不能放出来，技能脚本不得不引用背包系统（查有没有拿法杖）、引用 Buff 系统（查有没有被沉默）、引用角色控制器（查是不是在地面上）。系统之间互相牵扯，谁也拆不开谁。

**Gameplay Tags（层级游戏标签）** 就是为了解决这些问题而生的。

与其用零散的布尔变量去描述状态，不如用一颗有层级的标签树——比如 `State.Control.Frozen`（状态 → 控制 → 冰冻）。当角色身上挂着 `State.Control.Frozen` 时：
- 移动系统只需要问一句：**“身上有 `State.Control` 吗？”** —— 只要是挂在 `Control` 分支下的任何控制效果，哪怕是下个月新加的“时间停止”，这里都会自动返回 `true`。
- 碎冰被动技能只需要问一句：**“身上明确挂着 `State.Control.Frozen` 吗？”** —— 只有真正的冰冻才会触发双倍伤害。

---

## 它好在哪？

市面上不缺标签系统的概念，但 **ZFramework Gameplay Tags** 把重点放在了**开发体验**和**运行时性能**的平衡上：

- **可视化树状管理，代码自动生成**：在 Unity 编辑器窗口里直接增删改查标签树，一键生成强类型的 `Tags.State__Control__Frozen` 静态字段。写代码时全程享受 IDE 自动补全，彻底告别手敲字符串拼写错误。
- **开箱即用，默认全自动初始化**：无论是进入 Play Mode、加载场景、反序列化 Prefab/ScriptableObject，还是第一次查询标签，注册表都会自动就绪，不需要你在业务脚本里到处操心初始化顺序。
- **Inspector 与运行时合一**：同一个 `GameplayTagContainer` 字段，在 Inspector 里可以直接点开树状弹窗勾选初始标签，到了运行时直接调 `HasTag` / `AddTag`，不用写任何数据转换胶水代码。
- **热路径只有 4 字节整数运算**：字符串只存在于编辑器和加载边界。到了运行时，每个 `GameplayTag` 都只是一个 4 字节的整数索引，判等、父子区间判断、集合二分与线性扫描全部基于紧凑的连续数组完成，高频战斗帧里零 GC 分配。
- **克制而专注**：它只把“状态表达、计数叠加、条件判断、事件通知”做到极致，不强行绑定任何技能框架。你可以花十分钟把它接进现有的状态机、行为树或战斗系统里。

---

## 目录

- [安装](#安装)
- [三分钟快速上手](#三分钟快速上手)
- [六大核心用法（附实战代码）](#六大核心用法附实战代码)
  - [1. 层级归类与精确匹配：一套逻辑搞定所有控制效果](#1-层级归类与精确匹配一套逻辑搞定所有控制效果)
  - [2. 多来源 Buff 计数：火球术和火海再也不会互相误删](#2-多来源-buff-计数火球术和火海再也不会互相误删)
  - [3. 装备与光环状态汇总：穿脱装备自动同步，绝不记错账](#3-装备与光环状态汇总穿脱装备自动同步绝不记错账)
  - [4. 技能释放条件与规则缓存：一句话校验，十几个技能批量提速](#4-技能释放条件与规则缓存一句话校验十几个技能批量提速)
  - [5. 事件驱动 UI 与特效：告别 Update 轮询](#5-事件驱动-ui-与特效告别-update-轮询)
  - [6. 集合运算与对象池：交集、并集与零 GC 临时容器](#6-集合运算与对象池交集并集与零-gc-临时容器)
- [在 Unity 中使用：Inspector 配置、存档与初始化控制](#在-unity-中使用inspector-配置存档与初始化控制)
- [标签编辑器实用技巧](#标签编辑器实用技巧)
- [动手试试：Tag Playground 示例](#动手试试tag-playground-示例)
- [API 速查表](#api-速查表)
- [独立 .NET 环境与性能贴士](#独立-net-环境与性能贴士)

---

## 安装

支持 **Unity 2022.3 及以上版本**。

1. 打开 **Window → Package Manager**。
2. 点击左上角 **+ → Add package from git URL...**，填入：
   ```text
   https://github.com/acced/ZFramework.GameplayTags.git#main
   ```
   *(如果团队需要固定版本，可以把 `#main` 换成具体的 Commit SHA；本地调试可选择 **Add package from disk...** 并选中本仓库的 `package.json`。)*
3. 如果你的业务代码使用了自定义的 `.asmdef`（Assembly Definition），请确保引用了 **`ZFramework.GameplayTags`**（生成代码创建的 `ZFramework.GameplayTags.Generated` 默认已开启自动引用；若你的 `.asmdef` 关闭了 `autoReferenced`，也请一并勾选上）。

---

## 三分钟快速上手

```text
┌─────────────────────────────┐    ┌─────────────────────────────┐    ┌─────────────────────────────┐
│ 第一步：在窗口里建标签      │    │ 第二步：点一下“生成代码”    │    │ 第三步：直接写逻辑          │
│ Tools → Gameplay Tags       │ ──►│ 自动生成强类型 Tags.* 字段  │ ──►│ 自动初始化，IDE 全程补全    │
│ → Tag Settings              │    │                             │    │                             │
└─────────────────────────────┘    └─────────────────────────────┘    └─────────────────────────────┘
```

### 1. 打开标签窗口添加几个标签

1. 点击顶部菜单 **Tools → Gameplay Tags → Tag Settings**。
2. 点击 **“创建标签配置”**（默认保存在 `Assets/GameplayTags/Editor/GameplayTagSettings.asset`）。
3. 添加下面这 5 个标签：
   ```text
   State.Burning
   State.Control.Frozen
   State.Control.Silenced
   Ability.Cast
   Equipment.Weapon.Staff
   ```
   > **省心细节**：你只需要添加最末端的具体标签。像 `State`、`State.Control`、`Equipment`、`Equipment.Weapon` 这些中间层级会自动推导出来（在树里显示为灰色节点），不用你挨个手动建。

### 2. 生成 C# 代码

点击窗口上方的 **“生成代码”** 按钮（或菜单 **Tools → Gameplay Tags → Generate Player Registry**）。

编译完成后，所有标签（包含自动推导的父节点）都会出现在 `GameplayTagRegistry.Tags` 下，路径里的点号 `.` 会变成双下划线 `__`：
- `State.Control` 对应 `Tags.State__Control`
- `State.Control.Frozen` 对应 `Tags.State__Control__Frozen`

### 3. 挂个脚本跑起来

新建 `QuickStartExample.cs` 挂在场景任意物体上，点下 Play：

```csharp
using GameplayTags;
using UnityEngine;
using Tags = GameplayTags.Generated.GameplayTagRegistry.Tags;

public sealed class QuickStartExample : MonoBehaviour
{
    private void Start()
    {
        // 创建一个标签容器，给角色挂上“冰冻”
        var states = new GameplayTagContainer();
        states.AddTag(Tags.State__Control__Frozen);

        // 问大类：角色现在被控制了吗？（HasTag 会自动匹配父层级）
        Debug.Log(states.HasTag(Tags.State__Control));              // True
        Debug.Log(states.HasTag(Tags.State));                       // True

        // 问具体：角色身上直接挂的是 State.Control 本身，还是 Frozen？
        Debug.Log(states.HasTagExact(Tags.State__Control));         // False：没有直接添加父标签本身
        Debug.Log(states.HasTagExact(Tags.State__Control__Frozen)); // True：明确添加了冰冻

        // 解除冰冻
        states.RemoveTag(Tags.State__Control__Frozen);
        Debug.Log(states.IsEmpty);                                  // True
    }
}
```

---

## 六大核心用法（附实战代码）

下面的所有代码片段都默认在文件头加了这两行引用：
```csharp
using GameplayTags;
using Tags = GameplayTags.Generated.GameplayTagRegistry.Tags;
```

### 1. 层级归类与精确匹配：一套逻辑搞定所有控制效果

当你往容器里放入 `State.Control.Frozen` 时，容器里的层级关系是这样的：

```text
State                       ◄── 隐式祖先：HasTag 为 True，HasTagExact 为 False
└── Control                 ◄── 隐式祖先：HasTag 为 True，HasTagExact 为 False
    └── Frozen              ◄── 显式成员：HasTag 与 HasTagExact 均为 True
```

- **用 `HasTag` 查“是不是这一类”**：比如移动系统查 `states.HasTag(Tags.State__Control)`。不管身上是冰冻、眩晕还是沉默，全部命中。
- **用 `HasTagExact` 查“是不是精准的这一个”**：比如破冰技能查 `states.HasTagExact(Tags.State__Control__Frozen)`。

```csharp
var states = new GameplayTagContainer();
states.AddTag(Tags.State__Control__Frozen);

int explicitCount = states.ExplicitTagCount; // 1（只直接添加了 Frozen）
int totalCount    = states.TagCount;         // 3（包含 State、State.Control、State.Control.Frozen）

// 只遍历当初真正添加进去的标签（输出 State.Control.Frozen）
foreach (GameplayTag tag in states.GetExplicitTags())
{
    Debug.Log($"实际挂上的标签: {tag.Name}");
}

// 默认遍历会包含所有推导出的父节点（输出 State、State.Control、State.Control.Frozen）
foreach (GameplayTag tag in states)
{
    Debug.Log($"当前生效的全部节点: {tag.Name}");
}
```

> **两个符合直觉的规则**：
> - **父不推子**：只加一个宽泛的 `State.Control`，绝不会让角色自动变成 `Frozen`。
> - **谁加的移除谁**：`GameplayTagContainer` 是一个无叠层的集合。要解除冰冻，请调用 `RemoveTag(Tags.State__Control__Frozen)`；直接移除未曾显式添加过的父节点 `State.Control` 不会清空它下面的具体状态。

---

### 2. 多来源 Buff 计数：火球术和火海再也不会互相误删

普通容器只管“有或没有”。而战斗里的状态往往来自多个地方——**火球术**点着了角色，同时角色又站在**火海**里。这时换用 **`GameplayTagCountContainer`**，它会为每个来源记账：

```csharp
var effects = new GameplayTagCountContainer();

// 两个来源分别给角色加上燃烧
effects.AddTag(Tags.State__Burning); // 火球术命中 (+1)
effects.AddTag(Tags.State__Burning); // 踩进火海 (+1)

Debug.Log(effects.GetExplicitTagCount(Tags.State__Burning)); // 当前有 2 个燃烧来源

// 3 秒后火球术到期，只撤回它自己的那 1 份
effects.RemoveTag(Tags.State__Burning);
Debug.Log(effects.HasTag(Tags.State__Burning));              // True！人还在火海里，继续烧

// 角色走出火海，撤回最后 1 份
effects.RemoveTag(Tags.State__Burning);
Debug.Log(effects.HasTag(Tags.State__Burning));              // False：所有来源都结束了，火焰熄灭
```

- **一次加减多层**：支持 `AddTag(tag, amount)` 和 `RemoveTag(tag, amount)`。
- **查单点 vs 查整支**：`GetExplicitTagCount(tag)` 返回这个标签自身被加了多少次；`GetTagCount(tag)` 则会把底下所有子标签的层数也汇总上来（比如 2 层 `State.Burning` + 1 层 `State.Control.Frozen`，查 `effects.GetTagCount(Tags.State)` 会得到 `3`）。

---

### 3. 装备与光环状态汇总：穿脱装备自动同步，绝不记错账

假设“巫师法杖”提供 `Ability.Cast`，“奥术光环”也提供 `Ability.Cast`。如果装备系统和光环系统都往角色同一个容器里直接塞标签，卸装备或者重置光环时稍不留神就会把别人的贡献扣掉。

用 **`GameplayTagHierarchicalContainer`**，让每个子系统管好自己的一亩三分地，再把 `Parent` 指向角色的总容器：

```csharp
var actor     = new GameplayTagCountContainer();
var equipment = new GameplayTagHierarchicalContainer { Parent = actor };
var aura      = new GameplayTagHierarchicalContainer { Parent = actor };

// 装备和光环各自管自己的标签，自动汇总给角色
equipment.AddTag(Tags.Ability__Cast);
aura.AddTag(Tags.Ability__Cast);

Debug.Log(actor.GetTagCount(Tags.Ability__Cast)); // 2：角色总容器自动拿到两边之和

// 卸下装备：直接断开父子连接，法杖贡献的所有标签会自动从角色身上撤回
equipment.Parent = null;
Debug.Log(actor.HasTag(Tags.Ability__Cast));       // True（剩余 1）：光环还在，依然能施法！

// 重新穿上装备：挂回 Parent，法杖的标签又自动同步回角色身上
equipment.Parent = actor;
Debug.Log(actor.GetTagCount(Tags.Ability__Cast)); // 2
```

---

### 4. 技能释放条件与规则缓存：一句话校验，十几个技能批量提速

#### 单条技能规则 (`GameplayTagRequirements`)

“炎爆术”要求角色**必须有施法能力 (`Ability.Cast`)**、**必须拿着法杖 (`Equipment.Weapon.Staff`)**，并且**不能处于任何控制状态 (`State.Control`)**。

用 `GameplayTagRequirements` 把这条规则定义好，随时拿来校验角色容器：

```csharp
// 参数依次为：(禁止的标签, 必需标签1, 必需标签2) —— 纯结构体，零堆内存分配
var pyroblastRule = GameplayTagRequirements.Create(
    forbidden:          Tags.State__Control,
    required:           Tags.Ability__Cast,
    additionalRequired: Tags.Equipment__Weapon__Staff);

var actor = new GameplayTagContainer();
actor.AddTag(Tags.Ability__Cast);
actor.AddTag(Tags.Equipment__Weapon__Staff);

Debug.Log(pyroblastRule.Matches(actor)); // True：条件齐全，可以吟唱

actor.AddTag(Tags.State__Control__Silenced);
Debug.Log(pyroblastRule.Matches(actor)); // False：被沉默了（命中禁止项 State.Control），无法释放
```

- **支持任意数量的条件**：条件更多时，用 `new GameplayTagRequirements(forbiddenArrayOrSpan, requiredArrayOrSpan)`。它会自动去重并化简父子包含关系，4 个以内有效条件完全内联在结构体里。
- **跨两个容器直接判**：如果角色的“装备常驻标签”和“战斗临时 Buff”分成了两个容器，直接调 `pyroblastRule.Matches(equipmentTags, buffTags)` 就能联合判定，根本不需要先把两个容器合并。
- **轻量快捷查询**：平时随手查几个标签，也可以直接调容器的 `HasAny(query)`、`HasAll(query)` 或 `HasAnyExact(query)`、`HasAllExact(query)`。

#### 技能栏批量评估与增量缓存 (`GameplayTagRuleSet`)

如果角色技能栏里有 12 个技能，每个技能都在检查“有没有被控制”、“有没有施法能力”，每帧挨个跑一遍未免浪费。把它们放进 **`GameplayTagRuleSet`**：

```csharp
// 1. 多个同职业角色可以共享同一份 RuleSet
var skillRules = new GameplayTagRuleSet(new[]
{
    pyroblastRule,
    new GameplayTagRequirements(
        forbidden: System.Array.Empty<GameplayTag>(),
        required:  new[] { Tags.State__Burning })
});

// 2. 每个角色自己持有一份轻量缓存
GameplayTagRuleSet.Cache actorRuleCache = skillRules.CreateCache();

// 3. 一次性拿到所有技能当前能不能放
System.ReadOnlySpan<bool> canUseSkills = actorRuleCache.Evaluate(actor);
bool canCastPyroblast = canUseSkills[0];
bool canUseFireCombo  = canUseSkills[1];
```

这份 `Cache` 非常聪明：
- 只要角色身上的标签**种类**没有出现或消失（哪怕燃烧从 1 层叠到了 5 层），调用 `Evaluate` 会**直接返回缓存好的结果**，不做任何重复查询。
- 只有当某个标签真正新增或归零消失时，它才精准重算受那个标签影响的技能。

---

### 5. 事件驱动 UI 与特效：告别 Update 轮询

别在 `Update()` 里每帧查标签来控制头顶的眩晕星星或身上的火焰特效了，让容器在状态变化时主动通知你：

#### 方式一：监听层数变化或状态开关

```csharp
var effects = new GameplayTagCountContainer();

OnTagCountChangedDelegate onBurningChanged = (tag, newCount) =>
{
    Debug.Log($"{tag.Name} 当前层数: {newCount}");
};

// AnyCountChange：层数每次变化都触发（1 → 2 → 1 → 0）
// NewOrRemoved：  只在“从 0 变有”和“从有变 0”时触发
effects.RegisterTagEventCallback(
    Tags.State__Burning,
    GameplayTagEventType.AnyCountChange,
    onBurningChanged);

effects.AddTag(Tags.State__Burning);    // 打印: 1
effects.AddTag(Tags.State__Burning);    // 打印: 2
effects.RemoveTag(Tags.State__Burning); // 打印: 1
effects.Clear();                        // 打印: 0（清空计数并触发归零回调，订阅本身会保留）

// 不再需要时退订
effects.RemoveTagEventCallback(
    Tags.State__Burning,
    GameplayTagEventType.AnyCountChange,
    onBurningChanged);
```

> **实战窍门**：直接对父标签 `Tags.State__Control` 注册 `GameplayTagEventType.NewOrRemoved`。这样不管是哪种控制技能打中角色，只要角色进入或脱离受控状态，你的 UI 就能立刻弹起或收起“受控中”的提示条。

#### 方式二：UI 面板专用的成组绑定 (`GameplayTagContainerBinds`)

写 UI 面板时，我们往往希望**绑定瞬间就拿到一次当前状态来刷新界面**，并且在面板关闭时**一行代码取消所有监听**：

```csharp
public sealed class StatusHudView : MonoBehaviour
{
    private GameplayTagContainerBinds m_Binds;

    public void BindActor(GameplayTagCountContainer actorEffects)
    {
        m_Binds.Dispose(); // 切换观察目标时，先清理旧绑定
        m_Binds = new GameplayTagContainerBinds(actorEffects);

        // Bind 时会立刻回调一次当前状态，之后在标签出现/消失时再次回调
        m_Binds.Bind(Tags.State__Burning, isBurning =>
        {
            Debug.Log($"燃烧图标显隐: {isBurning}");
        });

        m_Binds.Bind(Tags.State__Control, isControlled =>
        {
            Debug.Log($"受控边框显隐: {isControlled}");
        });
    }

    private void OnDestroy()
    {
        // 面板销毁时一键解绑所有事件，不会影响角色自身的容器
        m_Binds.Dispose();
    }
}
```

---

### 6. 集合运算与对象池：交集、并集与零 GC 临时容器

需要对比或合并两个容器时，可以直接做集合运算：

```csharp
var left = new GameplayTagContainer();
left.AddTag(Tags.State__Burning);
left.AddTag(Tags.Ability__Cast);

var right = new GameplayTagContainer();
right.AddTag(Tags.State__Control__Frozen);
right.AddTag(Tags.Ability__Cast);

var union        = GameplayTagContainer.Union(left, right);        // 并集：Burning, Frozen, Cast
var intersection = GameplayTagContainer.Intersection(left, right); // 交集：只有 Cast
var diff         = left.Clone();
diff.RemoveTags(right);                                            // 差集：只有 Burning
```

如果在高频调用的函数里不想产生任何临时对象分配，可以**复用目标容器**或者向**线程本地对象池 (`GameplayTagContainerPool`)** 借一个临时容器：

```csharp
// 1. 直接把结果写进已有的容器里（零分配）
var reusableOutput = new GameplayTagContainer(capacity: 32);
GameplayTagContainer.Union(reusableOutput, left, right);
reusableOutput.Clear(); // 清空元素，保留底层数组容量供下次使用

// 2. 用 using 从对象池借用临时容器，离开作用域自动清空并归还
using (GameplayTagContainerPool.Get(out GameplayTagContainer scratch))
{
    scratch.AddTag(Tags.State__Burning);
    // 在这里使用 scratch...
}
```

---

## 在 Unity 中使用：Inspector 配置、存档与初始化控制

### 在 Inspector 里直接勾选标签

把 `GameplayTagContainer` 写成序列化字段，Inspector 上就会出现带搜索框的树状选择器：

```csharp
using GameplayTags;
using UnityEngine;
using Tags = GameplayTags.Generated.GameplayTagRegistry.Tags;

public sealed class CharacterState : MonoBehaviour
{
    // 在 Inspector 中点击 "Edit Tags..." 就能勾选初始标签，场景加载时自动就绪
    [SerializeField] private GameplayTagContainer m_States = new GameplayTagContainer();

    // 单个标签字段同样有下拉树状选择器
    [SerializeField] private GameplayTag m_CategoryTag;

    private void Awake()
    {
        // 小提示：单个 GameplayTag 是值类型 (struct)，如果在 Inspector 里配了单标签，
        // 在加载后调用一次 Resolve() 赋回给自己即可：
        m_CategoryTag = m_CategoryTag.Resolve();
    }

    public bool IsControlled => m_States.HasTag(Tags.State__Control);
    public void ApplyBurning() => m_States.AddTag(Tags.State__Burning);
}
```

- **ScriptableObject 共享配置**：如果十几个怪物共用同一个 `ScriptableObject` 资产里的 `GameplayTagContainer` 作为初始模板，请在怪物出生时调 `var runtimeTags = config.InitialTags.Clone();` 拷贝一份，别让单个怪物的战斗状态改脏了共享模板。
- **现成的 `GameObjectGameplayTagContainer` 组件**：如果不想自己写包装脚本，可以直接给物体挂上包里自带的 `GameObjectGameplayTagContainer` 组件（内置序列化初始标签 + 运行时计数容器），还能直接用 `gameObject.Bind(tag, callback)` 绑定事件。

### 存档与读档（Save / Load）

- **存档只存 `tag.Name`，不要存 `tag.RuntimeIndex`**：整数 ID 是根据当前标签表按字母序排出来的，以后加新标签时序号会变；而标签名是稳定的。
- **`GameplayTagContainer` 直接支持序列化**：无论用 Unity 序列化还是 `DataContractSerializer`，它都会自动保存显式标签名，并在读档时自动还原整棵父子层级。
- **计数容器 (`GameplayTagCountContainer`) 怎么存**：遍历 `container.GetExplicitTags()`，把每个标签的 `tag.Name` 和 `container.GetExplicitTagCount(tag)` 存下来；读档时用 `GameplayTagManager.RequestTag(savedName, out GameplayTag tag)` 查出标签，再调 `AddTag(tag, savedCount)` 恢复即可。

### 初始化机制：默认全自动，也支持手动接管

- **默认全自动（推荐）**：你不需要写任何初始化代码。不管是 Unity 启动、场景/资源反序列化，还是第一次访问 `Tags.*` / `RequestTag`，注册表都会在首次接触时自动完成初始化。
- **显式预热 (`GameplayTagRegistry.Initialize()`)**：如果你喜欢在游戏启动 Loading 界面统一预热所有系统，可以随时调用 `GameplayTagRegistry.Initialize()`。**重复调用是完全安全的（不会重建注册表，也不会让已有的标签句柄失效）**。
- **纯手动模式 (`GAMEPLAY_TAGS_MANUAL_INITIALIZATION`)**：如果你希望彻底关闭自动初始化（例如需要先从外部配置表动态构建标签），只需在 **Player Settings → Scripting Define Symbols** 中添加 `GAMEPLAY_TAGS_MANUAL_INITIALIZATION` 宏，然后在首次使用标签前手动调用 `GameplayTagRegistry.Initialize()` 或 `GameplayTagManager.Initialize(registrations)`。

---

## 标签编辑器实用技巧

打开 **Tools → Gameplay Tags → Tag Settings**：

- **全工程唯一配置表**：整个项目只需要维护一份 `GameplayTagSettings.asset`（放在 `Editor` 目录下，只参与编辑器配置与代码生成，不占包体）。
- **搜索与整支操作**：支持按路径关键字实时过滤、新增子标签、编辑备注说明、整支重命名、整支删除，并完整支持 `Ctrl+Z` / `Ctrl+Y` 撤销重做。
- **灰色父节点转正**：自动推导出来的父节点显示为灰色；选中它并填一段备注保存，它就会变成显式登记的节点。
- **编译器帮你找改名引用**：重命名某个分支后，点一下 **“生成代码”**，C# 编译器会立刻帮你标出所有还在用旧字段名（如 `Tags.Old__Name`）的脚本位置，改起来既快又放心。
- **打包前防呆检查**：在 Build Player 时，系统会自动核对 `GameplayTagSettings.asset` 和生成代码是否一致。如果策划改了标签忘了点“生成代码”，打包会立刻停下并给出明确提示，绝不让你打出代码与配置对不上的包。

---

## 动手试试：Tag Playground 示例

想直接看运行效果？包里自带了一个零 UI 的纯日志示例场景：

1. 在 **Window → Package Manager** 中找到 **ZFramework Gameplay Tags**，展开右侧 **Samples**，点击 **Import** 导入 **Tag Playground**。
   *(如果项目里已经有自己的 `GameplayTagSettings.asset`，把示例里的 5 个 `Demo.*` 标签加进你的配置并删掉示例附带的 SO 即可。)*
2. 点击菜单 **Tools → Gameplay Tags → Generate Player Registry** 生成代码。
3. 打开 `TagPlayground.unity` 点击 Play，就能在 Console 窗口看到层级匹配、施法条件判断、双来源燃烧叠加和事件回调（`1 → 2 → 1 → 0`）的完整运行日志。

---

## API 速查表

| 你的需求 | 用哪个类型 / 方法 |
| :--- | :--- |
| **拿一个轻量标签句柄** | [`GameplayTag`](Runtime/GameplayTag.cs) — 4 字节整数判等，`ParentTag`、`ParentTags`、`ChildTags`、`IsParentOf`、`IsChildOf` |
| **存角色或物体的状态集合（无叠层）** | [`GameplayTagContainer`](Runtime/GameplayTagContainer.cs) — `AddTag`、`RemoveTag`、`HasTag`、`HasTagExact`、`HasAny`、`HasAll`、`Union`、`Intersection`、`Clone` |
| **管理可叠层的 Buff 与状态事件** | [`GameplayTagCountContainer`](Runtime/GameplayTagCountContainer.cs) — `AddTag(tag, amount)`、`GetExplicitTagCount`、`GetTagCount`、`RegisterTagEventCallback` |
| **把装备、光环等子系统的状态汇总给角色** | [`GameplayTagHierarchicalContainer`](Runtime/GameplayTagHierarchicalContainer.cs) — 设置 `Parent` 自动同步增删与穿脱 |
| **检查技能或行为的必需/禁止标签** | [`GameplayTagRequirements`](Runtime/GameplayTagRequirements.cs) — `Create(forbidden, req1, req2)`、`Matches(actor)`、`Matches(equip, buffs)` |
| **批量评估一排技能并缓存结果** | [`GameplayTagRuleSet`](Runtime/GameplayTagRuleSet.cs) & `GameplayTagRuleSet.Cache` — `CreateCache()`、`cache.Evaluate(actor)` |
| **给 UI 面板绑定状态并在销毁时一键解绑** | [`GameplayTagContainerBinds`](Runtime/GameplayTagContainerBinds.cs) — `Bind(tag, Action<bool>)`、`Dispose()`、`gameObject.Bind()` |
| **在高频函数里借用零 GC 临时容器** | [`GameplayTagContainerPool`](Runtime/GameplayTagContainerPool.cs) — `using (GameplayTagContainerPool.Get(out var temp))` |
| **按字符串查标签或动态注册** | [`GameplayTagManager`](Runtime/GameplayTagManager.cs) — `RequestTag(name, out tag)`、`GetAllTags()`、`Initialize(...)` |

---

## 独立 .NET 环境与性能贴士

### 在纯 .NET 服务端或测试工程中运行

核心运行时不依赖 Unity，可以直接通过根目录的 `GameplayTags.Runtime.csproj` 编译成标准的 **.NET Standard 2.1 / C# 9** 类库，跑在独立战斗校验服或单元测试里：

```bash
dotnet build GameplayTags.Runtime.csproj -c Release
```

如果不使用生成的 `GameplayTagRegistry`，只需要在首次查询前把标签表传给 `GameplayTagManager.Initialize` 即可：

```csharp
GameplayTagManager.Initialize(
    new GameplayTagRegistration("State.Burning", "持续火焰伤害"),
    new GameplayTagRegistration("State.Control.Frozen", "无法移动和施法"),
    new GameplayTagRegistration("Ability.Cast", "允许吟唱法术"));

GameplayTag burningTag = GameplayTagManager.RequestTag("State.Burning");
```

### 让性能保持最佳状态的几个习惯

- **平时直接用 `Tags.*` 字段**：`RequestTag("...")` 字符串查找是为读档、配表加载准备的冷路径接口，战斗循环里请直接使用生成的 `Tags.*` 静态字段。
- **只传有效参数**：往容器里增删标签时请传入已注册的有效标签，层数 `amount` 传正整数，只移除当前系统自己加进去的层数；`GameplayTagContainer.Empty` 是全局只读空容器，不要往里面塞东西。
- **容量预留**：调用 `EnsureCapacity(n)` 时，预留的是**包含隐式父节点在内的不同标签总数**。
- **线程说明**：注册表的首次初始化是线程安全的；各个容器、事件回调和 `RuleSet.Cache` 则面向单线程游戏逻辑设计（`GameplayTagContainerPool` 为 `[ThreadStatic]` 线程本地池，在哪个线程借就在哪个线程还）。

---

## 许可证与反馈

本项目基于 [MIT License](LICENSE) 开源。用得顺手或遇到任何问题，欢迎来 [GitHub Issues](https://github.com/acced/ZFramework.GameplayTags/issues) 交流！
