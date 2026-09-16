<h1 align="center">ZFramework Gameplay Tags</h1>

<p align="center">适用于 Unity 的层级化游戏标签系统。<br>统一定义标签，在代码、组件和游戏条件查询中使用。</p>

<p align="center">
  <a href="./README_CN.md"><strong>CN · 简体中文</strong></a> · <a href="./README.md">EN · English</a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Unity-2021.3%2B-222222?logo=unity" alt="Unity 2021.3 及以上">
  <img src="https://img.shields.io/badge/Package-UPM-3178C6" alt="Unity Package Manager">
  <a href="./LICENSE"><img src="https://img.shields.io/badge/License-MIT-22A06B" alt="MIT 许可证"></a>
</p>

**[ZFramework Gameplay Tags](https://github.com/acced/ZFramework.GameplayTags)** 为 Unity 提供类似 Unreal Gameplay Tags 的标签能力。使用 `Ability.Attack.Melee`、`State.Debuff.Stunned` 这样的名称描述技能、状态、伤害类型等游戏概念。

- **层级匹配**：子标签可以匹配父标签，父级自动注册。
- **标签容器**：保存不重复的标签，支持任意匹配、全部匹配和集合筛选。
- **组合查询**：通过任意、全部、均不满足等条件组合标签与表达式。
- **编辑器工具**：管理标签定义，在 Inspector 中选择标签，导入和导出 CSV。
- **生成 C# API**：通过具名字段访问标签，获得 IDE 自动补全。
- **配置校验**：构建前检查标签名称、重定向、来源分组和受限层级。

[安装](#installation) · [快速开始](#quick-start) · [使用方式](#usage) · [编辑器操作](#editor-guide) · [项目设置](#configuration) · [示例](#samples) · [常见问题](#faq)

## 2.0 版本状态与迁移

本分支为 **2.0.0-pre.2**，不是已经通过 Unity/设备验收的稳定版。托管 CI 的成功只证明其覆盖的 C# 检查完成；正式发布条件见 [发布验收](Documentation~/RELEASE.md)。安装本 PR 使用下面带分支名的 Git URL，生产项目应固定到已审核的 commit/tag。

**Query 源数据与执行对象已分离。** 在加载阶段调用 `source.Freeze()`，复用返回的 `FrozenGameplayTagQuery`；修改源图不会改变已有 matcher。先完整验证活动源图，再做层级冗余删除、常量折叠与同类 All/Any 展平。不要逐帧或逐单位重复冻结相同规则。

**线程与生命周期：**注册表和 Editor 操作在主线程；容器由单一调用方持有，不支持并发修改或边枚举边修改。注册表重建不会自动更新旧名称快照。关闭 Domain Reload 时库会复位静态注册表；调用方必须在新的运行会话重新建立订阅和加载状态。

**序列化：**字段名保留；反序列化只对本地列表排序去重，不加载 Resources。主线程加载边界对序列化容器调用 `ResolveRegisteredTags()`，未知名称抛异常且不改原容器；解析保留预留容量。单标签用 `RequestTag(serializedTag.Name)`；查询标签由 Freeze 解析。快照复制/集合操作不再通过注册表偷偷过滤名称。

**容量与结果：**`Capacity` 可预留；`FilterInto`、`FilterExactInto`、`UnionInto`、`IntersectionExactInto` 覆盖调用方提供的结果。在已初始化、输入已解析且实际结果容量足够时，这些核心操作不分配托管内存。容量不足按增长策略扩容，不丢结果。便利返回值 API、Freeze、加载解析、父/叶名称截取与异常路径不在零分配承诺内。

| 别名/空条件 | 契约 |
|---|---|
| Copy/Append 自身 | 不操作；Remove 自身清空 |
| UnionInto / 精确交集与过滤 | 可覆盖任一输入 |
| 层级 FilterInto | 可覆盖源；拒绝覆盖另一独立条件容器，且修改前报错 |
| 空/ null 条件 | Any=false，All=true；空 Query=false；空 No=true |
| None | 非有效标签；`IsValid` 仅表示非空，不代表当前已注册 |

<a id="installation"></a>
## 安装

最低版本为 **Unity 2021.3**，以 `package.json` 的声明为准。运行时程序集不依赖其他 ZFramework 包。

### 本地包安装

1. 下载或克隆[项目仓库](https://github.com/acced/ZFramework.GameplayTags)，找到包含 `package.json` 的包文件夹。
2. 在 Unity 中打开 **Window → Package Manager**。
3. 点击 **+ → Add package from disk…**，选择该文件夹里的 `package.json`。
4. 等待 Unity 导入并完成编译。

也可以将整个包文件夹复制到 Unity 项目的以下位置：

```text
YourUnityProject/
└── Packages/
    └── zframework.gameplaytag/
        ├── package.json
        ├── Runtime/
        ├── Editor/
        └── Samples~/
```

如果项目中已经有 `Packages/zframework.gameplaytag`，它就是嵌入式包，无需再次安装。

### Git URL 安装

在 Unity Package Manager 中点击 **+ → Add package from git URL…**。包的 `package.json` 位于仓库根目录时，使用：

```text
https://github.com/acced/ZFramework.GameplayTags.git#optimize/whole-repo-20260916
```

如果仓库包含完整 Unity 项目，包位于 `Packages/zframework.gameplaytag`，则使用以下地址：

```text
https://github.com/acced/ZFramework.GameplayTags.git?path=/Packages/zframework.gameplaytag
```

使用此方式前需安装 Git，并确保包文件已提交到安装地址对应的仓库位置。

### 程序集引用

如果业务脚本位于自定义 `.asmdef` 下，在其 **Assembly Definition References** 中添加 **GameplayTags**。运行时代码使用以下命名空间：

```csharp
using GameplayTags;
```

<a id="quick-start"></a>
## 快速开始

### 1. 创建配置资源

打开 **Tools → ZFramework → Gameplay Tags → Create Settings**。默认配置会创建在：

```text
Assets/Resources/GameplayTags/GameplayTagSettings.asset
```

使用默认运行时加载方式时，请保留这个位置。将该资源及其 `.meta` 文件一起纳入项目版本管理。

### 2. 定义标签

打开 **Tools → ZFramework → Gameplay Tags → Manager**。在 **Tags** 页下方的 **Add Tag** 输入框中，逐个输入以下名称并点击 **Add**：

```text
Sample.State.Alive
Sample.State.Debuff.DamageOverTime.Poisoned
Sample.State.Debuff.Control.Stunned
```

`Sample.State.Debuff` 等父级会自动注册，无需单独添加定义。标签名称区分大小写，使用点号分隔层级，不要包含空格或空层级。

### 3. 运行一个组件

创建 `GameplayTagsQuickStart.cs`，写入以下代码，将组件挂到任意 GameObject 上，然后进入 Play Mode：

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

        Debug.Log(owned.HasTag(debuff));       // True：中毒属于 Debuff 的后代。
        Debug.Log(owned.HasTagExact(debuff));  // False：没有直接加入 Debuff 本身。

        var canAct = new GameplayTagQuery(
            GameplayTagQueryExpression.AllExpressionsMatch()
                .AddExpression(GameplayTagQueryExpression.AllTagsMatch().AddTag(alive))
                .AddExpression(GameplayTagQueryExpression.NoTagsMatch().AddTag(stunned)),
            "Alive and not stunned");

        FrozenGameplayTagQuery matcher = canAct.Freeze();
        Debug.Log(matcher.Matches(owned));     // True
        owned.AddTag(stunned);
        Debug.Log(matcher.Matches(owned));      // False
    }
}
```

默认设置下，注册表会在第一个场景加载前初始化。请求标签只是从注册表查找，不会创建新的标签定义。

<a id="usage"></a>
## 使用方式

### 标签匹配与容器

层级匹配的方向是 **子标签匹配父标签**。沿用快速开始中的变量：

```csharp
poisoned.MatchesTag(debuff);       // true
debuff.MatchesTag(poisoned);       // false
poisoned.MatchesTagExact(debuff);  // false
```

| API | 作用 |
| --- | --- |
| `owned.AddTag(tag)` | 加入已注册标签；重复或未注册时返回 `false`。 |
| `owned.RemoveTag(tag)` | 精确移除这个标签。 |
| `owned.HasTag(tag)` | 检查容器是否包含该标签或它的后代。 |
| `owned.HasTagExact(tag)` | 只检查是否保存了完全相同的名称。 |
| `owned.HasAny(other)` / `owned.HasAll(other)` | 对另一个容器中的标签做任意或全部匹配，包含层级匹配。 |
| `owned.HasAnyExact(other)` / `owned.HasAllExact(other)` | 使用精确名称做任意或全部匹配。 |
| `owned.Filter(other)` | 返回当前容器中能匹配 `other` 任意标签的那些标签。 |
| `matcher.Matches(owned)` | 对容器执行查询。 |

对于可能不存在的标签，使用 `TryRequestTag`，以返回值处理查找失败：

```csharp
if (GameplayTagManager.TryRequestTag("Sample.State.Alive", out GameplayTag tag))
{
    Debug.Log(tag.Name);
}
```

`RequestTag(name)` 在无法解析名称时抛出异常；`RequestTag(name, false)` 则返回 `GameplayTag.None`。`IsValid` 只判断名称是否非空，检查当前注册状态应使用 `GameplayTagManager.IsRegistered(tag)`。

### 组合查询

| 工厂方法 | 含义 |
| --- | --- |
| `AnyTagsMatch()` | 列出的标签至少有一个匹配。 |
| `AllTagsMatch()` | 列出的标签全部匹配。 |
| `NoTagsMatch()` | 列出的标签全部不匹配。 |
| `AnyExpressionsMatch()` | 子表达式至少有一个成立。 |
| `AllExpressionsMatch()` | 子表达式全部成立。 |
| `NoExpressionsMatch()` | 子表达式全部不成立。 |

标签条件用 `.AddTag(tag)` 添加标签，表达式组用 `.AddExpression(expression)` 添加子条件，具体组合方式见快速开始。标签条件使用层级匹配；没有根表达式的空查询返回 `false`。

### 在 Inspector 中选择标签

创建 `Skill.cs`，并将组件挂到 GameObject 上：

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

在 **Hierarchy** 中选中该 GameObject，就能在 **Inspector → Skill** 下编辑这些字段。自定义 PropertyDrawer 分别提供单标签选择器、容器编辑器和查询编辑器。相同的字段类型也能用于 `ScriptableObject` 资源。

选择结果保存在当前组件或资源中。项目里有哪些可用标签，统一在 **Gameplay Tag Manager** 中管理。

### 生成 C# API

1. 打开 **Edit → Project Settings… → ZFramework → Gameplay Tags**。
2. 设置 **Generated Namespace**、**Generated Class Name** 和 **Generated Code Path**。
3. 输出路径使用 `Assets/` 下的文件，例如 `Assets/Scripts/Generated/GameplayTags.gen.cs`。
4. 保存项目，点击 **Generate C# API**，等待编译完成。

使用默认命名空间 `Game` 和类名 `GameplayTags` 时，可以在业务方法中直接访问：

```csharp
GameplayTag alive = Game.GameplayTags.Sample_State_Alive;
```

生成字段名中的点号会变成下划线，重名时追加 `_2` 等后缀，原始标签名称保持不变。生成字段在静态初始化时解析已注册的标签。

修改标签定义或生成选项后，需要重新生成。生成器会覆盖指定文件。修改输出位置后，请在 Unity 中删除旧的生成 `.cs` 资源，避免重复类定义。

如果业务脚本使用 `.asmdef`，请将生成文件放在该程序集目录下，或者放到业务程序集引用的另一个独立程序集中。自定义程序集不能引用 `Assembly-CSharp`，而没有归入任何 `.asmdef` 的普通脚本通常会编译到那里。生成文件所在的程序集也需要引用 **GameplayTags**。

<a id="editor-guide"></a>
## 编辑器操作

| 要做什么 | 操作入口 |
| --- | --- |
| 创建默认配置资源 | **Tools → ZFramework → Gameplay Tags → Create Settings** |
| 添加、改名、删除标签定义 | **Tools → ZFramework → Gameplay Tags → Manager → Tags** |
| 配置旧名称映射 | **Manager → Redirects** |
| 添加来源分组、查看归属 | **Manager → Sources** |
| 设置初始化方式和代码输出选项 | **Edit → Project Settings… → ZFramework → Gameplay Tags** |
| 给组件或资源选择标签 | 对应对象的 **Inspector** |
| 生成代码 | **Tools → ZFramework → Gameplay Tags → Generate C# API**，或 Manager 的 **Generate** |
| 校验标签定义 | **Tools → ZFramework → Gameplay Tags → Validate**，或 Manager 的 **Validate** |

也可以通过 **Window → ZFramework → Gameplay Tag Manager** 打开管理窗口。Manager 的 **Generate** 按钮使用 Project Settings 中配置的生成选项。

### 改名与重定向

选中一个标签定义，修改 **Name**，点击 **Rename Hierarchy**，即可一起修改该标签及其已定义的后代，并为改名的定义创建重定向。**Apply Metadata** 用于应用注释、来源分组和限制选项。

重定向让 `RequestTag` / `TryRequestTag` 能将旧名称解析为当前名称，不会批量改写所有已有组件或资源。旧名称不能仍是有效的注册标签，重定向链必须最终指向已注册标签，且不能成环。

### 来源分组与受限标签

在 **Sources** 中添加 `Game`、`Combat` 等分组，填写归属者并设置只读标志。在 **Tags** 中选择标签的 Source，再点击 **Apply Metadata**。编辑器操作会拒绝修改只读来源中的标签。父标签开启 **Restricted** 且关闭 **Allow Non-restricted Children** 后，会禁止其下面出现非受限标签。

### CSV 导入与导出

使用 Manager 顶部的 **Import CSV** / **Export CSV**。列顺序如下：

```csv
Tag,Comment,Source,Restricted,AllowNonRestrictedChildren
Sample.State.Alive,Character is alive,Default,false,true
Sample.State.Debuff.Control.Stunned,Prevents actions,Default,false,true
```

导入会新增不存在的名称、覆盖同名定义；CSV 中没有列出的现有标签保持不变。不存在的来源分组会创建为可编辑分组，整份结果通过校验后才应用。导出包含标签定义及其来源名称，不包含重定向、来源归属者或只读标志。

<a id="configuration"></a>
## 项目设置

打开 **Edit → Project Settings… → ZFramework → Gameplay Tags**。六个选项都保存在 `GameplayTagSettings` 资源中。

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| Auto Initialize | `true` | 在首个场景加载前初始化。关闭后跳过启动初始化，注册表 API 仍可能在首次使用时初始化。 |
| Include Implicit Parent Tags In Generated Code | `true` | 为自动注册的父级和显式定义都生成字段，不影响运行时父子匹配。 |
| Warn On Invalid Serialized Tags | `true` | 当 `AddTag(GameplayTag)` 拒绝未注册且非空的名称时提醒；仅 Editor/Development Build 按名称去重提示，Release 不保留该提示集合。不会扫描全部资源。 |
| Generated Namespace | `Game` | 生成类的命名空间，留空则不生成命名空间。 |
| Generated Class Name | `GameplayTags` | 生成的静态类名。 |
| Generated Code Path | `Assets/GameScripts/Main/Generated/GameplayTags.gen.cs` | 生成 C# 文件的目标路径。 |

配置哈希覆盖定义、重定向和生成选项，仅用于诊断；构建检查比较完整的预期生成文本，不以哈希相等代替源码一致性验证。

修改选项后，使用 **File → Save Project** 保存。这个设置页会将资源标记为已修改，但不会主动立即写入磁盘。修改生成选项后还需重新生成代码。

<a id="samples"></a>
## 示例

在 **Window → Package Manager** 中选择 **ZFramework Gameplay Tags**，展开 **Samples**，导入 **Basic Usage**。先创建快速开始中的三个 `Sample.*` 标签，再将 `BasicGameplayTagsExample` 挂到 GameObject 上，进入 Play Mode。

也可以直接阅读[示例说明（英文）](./Samples~/BasicUsage/README.md)和[示例源码](./Samples~/BasicUsage/BasicGameplayTagsExample.cs)。

<a id="faq"></a>
## 常见问题

**Generated Code Path 在哪里？**  
在 **Project Settings → ZFramework → Gameplay Tags** 中，位于 **Generated Class Name** 下方。显示标签列表的窗口是 Manager，输出选项在 Project Settings 中设置。

**为什么 GameplayTag 能显示在 Inspector 中？**  
它是可序列化类型，并且有自定义 PropertyDrawer。在组件或 `ScriptableObject` 中声明 public 字段，或使用 `[SerializeField]` 标记 private 字段即可。脚本需要挂到 GameObject 上，才会作为场景组件显示。

**RequestTag 为什么找不到标签？**  
检查名称和大小写，确认已经在 Manager 中定义，并将默认配置保留在 `Resources/GameplayTags/GameplayTagSettings.asset`。默认编辑器工作流与默认运行时加载器使用相同的固定位置。如果允许标签不存在，使用 `TryRequestTag`。

**构建为什么被校验拦住？**  
先运行 **Tools → ZFramework → Gameplay Tags → Validate**，按提示修复配置。构建校验要求配置资源存在，检查定义、来源、限制和重定向，并比较完整的预期生成源码；生成文件缺失或过期都会阻止构建。构建阶段不会自动生成 C# API。

<a id="license"></a>
## 许可证

[MIT](./LICENSE)。
