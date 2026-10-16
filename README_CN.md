# ZFramework Gameplay Tags

**简体中文** | [English](README.md)

一个面向 Unity 的轻量 C# 游戏标签库，提供缓存整数句柄、层级匹配、显式集合、引用计数、父子容器和变化通知。

**内核信任调用方。** 在加载边界解析名称，缓存标签句柄，热路径只处理整数。参数校验、生命周期管理、初始化顺序和线程同步由应用负责，运行时不叠加防御性策略层。

| 项目 | 内容 |
| --- | --- |
| 包名 | `com.acced.zframework.gameplaytags` |
| 源码版本 | `2.1.0-rc.2` |
| Unity 包目标 | Unity 2022.3 及以上；请在实际 Editor 和 Player 后端验收 |
| 独立 C# 目标 | .NET Standard 2.1、C# 9 |
| 命名空间 | `GameplayTags` |
| Unity 程序集 | `ZFramework.GameplayTags` |
| 许可证 | [MIT](LICENSE) |

当前是源码候选版，不宣称所有操作全面提速或已通过 Unity/IL2CPP 的完整验收。仓库当前目录只保留库、编辑器支持和使用文档；历史测试、基准及实验材料留在 Git 历史，不进入当前包。

## 目录

[安装](#installation) · [快速开始](#quick-start) · [注册与缓存](#registration) · [标签语义](#tag-semantics) · [普通集合](#sets) · [条件查询](#queries) · [集合运算](#set-algebra) · [枚举](#enumeration) · [引用计数](#counts) · [事件](#events) · [父子容器](#parent-containers) · [对象池](#pooling) · [Unity 与序列化](#unity-and-serialization) · [调用方契约](#caller-contract) · [API 索引](#api-map)

<a id="installation"></a>
## 安装

### Unity Package Manager

打开 Package Manager，选择 **+ → Add package from git URL**，输入：

```text
https://github.com/acced/ZFramework.GameplayTags.git#main
```

也可以在 `Packages/manifest.json` 现有的 `dependencies` 对象中加入：

```json
"com.acced.zframework.gameplaytags": "https://github.com/acced/ZFramework.GameplayTags.git#main"
```

需要固定版本时，将 `#main` 替换为 `#<commit-sha>`，使用已经审阅的完整提交号。本地源码通过 **Add package from disk** 选择仓库中的 `package.json`。安装前移除其他定义了同名 `GameplayTags` 类型的包或复制到项目内的源码，不要与名称版 `zframework.gameplaytag` 同时安装。

业务脚本使用 `.asmdef` 时，添加对 **`ZFramework.GameplayTags`** 的引用。附带的 Editor 程序集只在编辑器中启用。安装方式参考：[Unity Git 依赖文档](https://docs.unity3d.com/2022.3/Documentation/Manual/upm-ui-giturl.html)。

### 独立 C# 项目

保留的小型工程可以编译不含 GameObject 适配器和 Editor 的核心库：

```bash
dotnet build GameplayTags.Runtime.csproj -c Release
```

在兼容目标框架的业务工程中引用 `GameplayTags.Runtime.csproj` 即可。核心不依赖源码生成器工程、NUnit 或测试运行器。

<a id="quick-start"></a>
## 快速开始

新建 `GameTagDeclarations.cs`。程序集特性必须位于 `using` 之后、任何命名空间或类型声明之前：

```csharp
using GameplayTags;

[assembly: GameplayTagAttribute("State.Burning", "正在承受燃烧伤害。")]
[assembly: GameplayTagAttribute("State.Frozen", "移动被冻结。")]
[assembly: GameplayTagAttribute("Ability.Cast", "允许施法。")]

// 由业务维护的缓存句柄，不需要源码生成器。
public static class GameTags
{
    public static readonly GameplayTag State = GameplayTagManager.RequestTag("State");
    public static readonly GameplayTag Burning = GameplayTagManager.RequestTag("State.Burning");
    public static readonly GameplayTag Frozen = GameplayTagManager.RequestTag("State.Frozen");
    public static readonly GameplayTag Cast = GameplayTagManager.RequestTag("Ability.Cast");
}
```

新建 `TagExample.cs` 并挂到 GameObject。这个例子在运行时创建容器；序列化容器还需要遵守后文的加载顺序要求。

```csharp
using GameplayTags;
using UnityEngine;

public sealed class TagExample : MonoBehaviour
{
    private GameplayTagContainer tags;

    private void Awake()
    {
        // 程序集发现只发生在初始化阶段；在初始化线程执行。
        GameplayTagManager.InitializeIfNeeded();
        tags = new GameplayTagContainer(16);
        tags.AddTag(GameTags.Burning);

        Debug.Log(tags.HasTag(GameTags.State));          // True：匹配祖先。
        Debug.Log(tags.HasTagExact(GameTags.State));     // False：没有显式加入 State。
        Debug.Log(tags.HasTagExact(GameTags.Burning));   // True。
        Debug.Log(tags.ExplicitTagCount);                // 1。
        Debug.Log(tags.TagCount);                        // 2：State 与 State.Burning。

        tags.RemoveTag(GameTags.Burning);
        Debug.Log(tags.IsEmpty);                         // True。
    }
}
```

后面的示例使用上述声明和 `GameTags` 缓存类。方法体片段请放到业务方法中执行。

<a id="registration"></a>
## 注册与句柄缓存

每个进程或脚本域选择一种注册方式。

**程序集声明：** 使用 `[assembly: GameplayTagAttribute(...)]`。首次 `InitializeIfNeeded()`、非空名称查询、`TagCount` 或 `GetAllTags()` 可以扫描当时已加载程序集的声明。初始化后才加载的程序集不会被自动补进注册表。

**显式注册：** 在任何名称查询、缓存字段访问、Inspector 引发的查询或容器反序列化之前，只调用一次 `Initialize`。这条路径不会合并程序集声明，需要自己提供完整标签集：

```csharp
// 这是程序集发现的替代方案，不是第二次初始化步骤。
GameplayTagManager.Initialize(
    new GameplayTagRegistration("State.Burning", "正在承受燃烧伤害。"),
    new GameplayTagRegistration("State.Frozen", "移动被冻结。"),
    new GameplayTagRegistration("Ability.Cast", "允许施法。"));

GameplayTag burning = GameplayTagManager.RequestTag("State.Burning");
```

`State`、`Ability` 等父标签会自动补全。名称区分大小写，每一级使用字母、数字或下划线，以 `.` 分隔。`GameplayTagUtility.IsValidName(name)` 和 `ValidateName(name)` 是供导入器、编辑工具主动调用的校验工具；注册过程不会自动调用它们。

`RequestTag(name)` 对未知、null 或空名称返回 `GameplayTag.None`，带 `out` 的重载可以报告解析是否成功：

```csharp
if (GameplayTagManager.RequestTag(externalName, out GameplayTag resolved))
{
    // 在调用方边界检查一次；频繁使用前应缓存 resolved。
    destination.AddTag(resolved);
}
```

这里的 `externalName` 和 `destination` 是业务传入的名称与目标容器。不要在 `Update` 中反复解析字符串，也不要在热循环里依赖字符串到标签的隐式转换。

`Initialize` 没有重复初始化保护或锁。**先发布一次注册表，之后只读；只要标签、容器、订阅或池内对象仍然存活，就不能重建注册表。** 初始化时序、跨线程发布与同步由调用方负责。整数 ID 只属于当前注册表，不是跨进程、存档、网络或不同构建之间的稳定标识。

<a id="tag-semantics"></a>
## 标签语义

`GameplayTag` 保存名称和缓存整数。`RuntimeIndex`、`IsValid`、相等比较和哈希都使用缓存 ID；修改 `m_Name` 不会自动更新它。`IsValid` 只表示缓存 ID 非零，不代表刚刚重新校验了名称。

```csharp
GameplayTag edited = new GameplayTag { m_Name = "State.Burning" };
edited = edited.Resolve(); // 在加载或编辑边界接收并保存返回的结构体。
```

未解析或未知标签的 ID 为零，即使保留的名称不同，也会与 `None` 比较相等。不要把它们当作不同的字典键，也不要修改正在作为哈希键使用的标签。

| 成员 | 含义 |
| --- | --- |
| `Name` / `Label` | 完整名称 / 最后一级名称 |
| `Description` / `Flags` | 注册元数据；Flags 不是权限检查 |
| `ParentTag` | 直接父标签；根标签返回 `None` |
| `ParentTags` | 严格祖先，从根到直接父级，不包含自身 |
| `HierarchyTags` | 从根到自身的完整路径 |
| `ChildTags` | 注册表内全部严格后代，采用 DFS 顺序 |
| `HierarchyLevel` | 层级深度，根标签为 1 |
| `IsParentOf` / `IsChildOf` | 严格祖先关系；标签不是自己的祖先 |

这些属性查询的是**注册表中的层级关系**，不是某个容器里已经存在的成员。`GetAllTags()` 返回 DFS 顺序的只读 span，不意味着容器默认枚举承诺全局名称排序。内置 `HideInEditor` 已标记过时，新功能不要依赖它。

<a id="sets"></a>
## 普通集合

`GameplayTagContainer` 保存显式标签，并维护它们的祖先闭包。同一个显式标签重复添加不会增加贡献次数。

```csharp
var tags = new GameplayTagContainer(32); // 容量需要包含不同的祖先标签。
bool added = tags.TryAddTag(GameTags.Burning);       // True。
bool duplicate = tags.TryAddTag(GameTags.Burning);   // False。
tags.AddTag(GameTags.Frozen);

// 只要还有后代贡献，State 就继续存在。
tags.RemoveTag(GameTags.Burning);
bool stillPresent = tags.HasTag(GameTags.State);     // True。
bool removedAgain = tags.TryRemoveTag(GameTags.Burning); // False。

tags.Clear(); // 清除成员，但保留底层容量供复用。
```

`TryAddTag` 与 `TryRemoveTag` 返回的是成员是否发生变化，**不是参数是否合法**。修改操作必须传入有效、非 None、已解析的标签。删除一个仅隐式存在的父标签，不会移除它的后代或后代贡献；需要独立的父级成员身份时，显式添加父标签。

`EnsureCapacity(n)` 为**包含祖先的不同标签总数**预留空间，不是只按显式叶子数，也不是按重复次数预留。它不添加成员、不缩小已保留的缓冲区。计数、容量运算范围和实际可分配内存由调用方保证。

<a id="queries"></a>
## Any / All 与组合条件

调用者是成员持有者，参数容器提供显式查询条件：

```csharp
var holder = new GameplayTagContainer();
holder.AddTag(GameTags.Burning);
holder.AddTag(GameTags.Cast);
var query = new GameplayTagContainer();
query.AddTag(GameTags.State);

bool any = holder.HasAny(query);          // True。
bool all = holder.HasAll(query);          // True。
bool anyExact = holder.HasAnyExact(query); // False。
bool allExact = holder.HasAllExact(query); // False。
```

`HasAny(空条件)` 为 false，`HasAll(空条件)` 为 true。具有明确类型的 null **条件集合**也按空条件处理。这不代表可以对 null 持有者调用实例方法，也不允许把 null 当作修改操作数。

两个条件集合与两个持有者是不同的调用：

```csharp
bool bothQueries = holder.HasAll(queryA, queryB);
bool splitAcrossHolders = GameplayTagContainerUtility.HasAll(staticTags, dynamicTags, query);
bool splitExact = GameplayTagContainerUtility.HasAllExact(staticTags, dynamicTags, query);
```

第一种要求同一个 holder 同时满足两组条件。后两种允许每个条件分别由任一 holder 提供，不需要构建临时并集。此片段中的变量是业务已有的非 null 容器。

```csharp
var forbidden = new GameplayTagContainer();
forbidden.AddTag(GameTags.Frozen);
var required = new GameplayTagContainer();
required.AddTag(GameTags.Cast);
var rule = new GameplayTagRequirements(forbidden, required);

bool canCast = rule.Matches(holder);
// rule.Matches(staticTags, dynamicTags) 也支持两个持有者。
```

Requirements 采用层级匹配，并保留条件容器的引用，因此随后修改这些容器会改变匹配结果。空要求可以通过。

<a id="set-algebra"></a>
## 复制、并集、交集与差集

```csharp
var left = new GameplayTagContainer();
left.AddTag(GameTags.Burning);
left.AddTag(GameTags.Cast);
var right = new GameplayTagContainer();
right.AddTag(GameTags.Frozen);
right.AddTag(GameTags.Cast);

var output = new GameplayTagContainer(32);
GameplayTagContainer.Copy(output, left);              // 替换 output 内容。
output.AddTags(right);                                // 向现有 output 合并。
GameplayTagContainer union = GameplayTagContainer.Union(left, right);
GameplayTagContainer intersection = GameplayTagContainer.Intersection(left, right);
GameplayTagContainer.Intersection(output, left, right); // 替换为 {Ability.Cast}。
left.IntersectWith(right);                            // 原地显式交集。
GameplayTagContainer clone = right.Clone();           // 独立可修改的存储。
clone.RemoveTags(intersection);                       // 显式差集。
```

并集、交集先对**显式成员**运算，再推导结果的祖先。`{State.Burning}` 与 `{State.Frozen}` 的交集为空，不能因为共同祖先而留下隐式 `State`。计数容器作为输入时，每个不同显式标签只贡献一次存在性，不保留它的重复次数。

复制自己是 no-op；交集输出可以就是任一输入。普通集合 `AddTags(self)` 是 no-op，`RemoveTags(self)` 清空自身。输出与两个输入都不同时，可以通过 `Copy(output, left); output.AddTags(right);` 创建复用并集。输出本来就是 `right` 时，应直接追加 `left`；先复制会破坏原来的右输入。

这个整数 API 没有名称版 rc.1 的 `SetUnion`、`CopyFrom` 或 `SetIntersectionExact` 方法，请使用上面的实际接口。

需要 UI 差异时，`current.GetDiffExplicitTags(previous, added, removed)` 会向 `added` **追加** current-minus-previous，向 `removed` 追加 previous-minus-current。复用这两个 List 前，应由调用方先清空。

<a id="enumeration"></a>
## 枚举与层级提取

```csharp
foreach (GameplayTag tag in tags.GetExplicitTags()) { /* 只枚举显式成员。 */ }
foreach (GameplayTag tag in tags) { /* 枚举全部成员，包含隐式祖先。 */ }
```

`ExplicitTagCount` 是不同显式成员数量，`TagCount` 包含不同隐式祖先。具体类型枚举使用结构体枚举器；经过 `IEnumerable<GameplayTag>` / `IEnumerator<GameplayTag>` 接口时可能装箱。`Indices` 是共享存储的实时视图，不是快照；独立结果应使用 `Clone` 或 `Copy`。

枚举**不保证顺序**，修改后旧枚举器失效。即使在 Debug 构建中，也没有版本跟踪或 fail-fast 检查。不要依赖插入顺序，也不要因为某次批量结果刚好按 DFS 排列就把它当作契约。

只在 UI、存档格式或协议确实需要顺序的边界排序：

```csharp
var names = new System.Collections.Generic.List<string>();
foreach (GameplayTag tag in tags.GetExplicitTags()) names.Add(tag.Name);
names.Sort(System.StringComparer.Ordinal);
```

`GetParentTags(tag, list)`、`GetChildTags(tag, list)` 追加容器中存在的严格祖先或后代；`GetExplicitParentTags`、`GetExplicitChildTags` 进一步限制为显式成员。四个方法都不会先清空 List。父级输出从最近父级开始；不要依赖子级输出顺序。

<a id="counts"></a>
## 引用计数标签

多个独立系统可能贡献同一个标签时，使用 `GameplayTagCountContainer`：

```csharp
var effects = new GameplayTagCountContainer(32);
effects.AddTag(GameTags.Burning, 3);
effects.AddTag(GameTags.Frozen, 2);

int burningCount = effects.GetExplicitTagCount(GameTags.Burning); // 3。
int stateExplicit = effects.GetExplicitTagCount(GameTags.State);  // 0。
int stateTotal = effects.GetTagCount(GameTags.State);             // 5。
int distinctExplicit = effects.ExplicitTagCount;                 // 2，不是 5。
int distinctTotal = effects.TagCount;                            // 3。

effects.RemoveTag(GameTags.Burning, 2);                           // Burning 剩 1 次。
effects.Clear();
```

数量必须为正，删除量不能超过该标签的显式次数，累计次数不能溢出。内核不预检查这些条件。对没有显式次数的成员执行删除，是无变化操作。

`AddTags(source)` 对每个不同显式成员只加 **1 次**；`RemoveTags(source)` 各减 1 次，不复制或移除来源的全部重复次数。因此，计数容器 `RemoveTags(self)` 是每个显式成员各减一次，与普通集合的清空语义不同。`Clear()` 清除所有次数，但保留订阅。需要显式复制重复次数时，遍历另一个计数来源，使用 `AddTag(tag, source.GetExplicitTagCount(tag))`。

<a id="events"></a>
## 事件与绑定生命周期

```csharp
var effects = new GameplayTagCountContainer();
OnTagCountChangedDelegate onChange = (tag, count) =>
    UnityEngine.Debug.Log($"{tag.Name}: {count}");

effects.RegisterTagEventCallback(GameTags.State, GameplayTagEventType.AnyCountChange, onChange);
effects.AddTag(GameTags.Burning); // State 回调：1。
effects.AddTag(GameTags.Frozen);  // State 回调：2。
effects.Clear();                 // State 回调：0。
effects.RemoveTagEventCallback(GameTags.State, GameplayTagEventType.AnyCountChange, onChange);
```

`AnyCountChange` 监听总次数变化。`NewOrRemoved` 只监听从零到正数、从正数到零的变化。祖先也会收到贡献，因此可以订阅祖先。直接注册回调不会立即报告当前值。

全局事件为 `OnAnyTagCountChange` 和 `OnAnyTagNewOrRemove`。取消订阅时使用相同的委托。`RemoveAllTagEventCallbacks()` 只清理按标签注册的回调，**不清理**通过全局事件 `+=` 注册的订阅。

`GameplayTagContainerBinds` 提供成组的存在性订阅，并在绑定时立即报告当前状态：

```csharp
using (var binds = new GameplayTagContainerBinds(effects))
{
    binds.Bind(GameTags.State, present => UnityEngine.Debug.Log(present));
    effects.AddTag(GameTags.Burning);
} // 释放这一组订阅，不释放 effects 容器。
```

绑定会创建状态或委托，建议在生命周期边界建立，不放进热循环。绑定结构体的副本共享订阅状态。初次 Bind 回调出错时不会自动回滚订阅，调用方负责释放。常规计数事件在修改提交后派发，回调重入产生的通知进入队列。回调抛异常会传播异常、保留已提交状态并释放待派发通知；这是资源清理，不是事务回滚。

<a id="parent-containers"></a>
## 父子容器

容器的父子关系用于聚合贡献，与名称中的点分层级是两回事：

```csharp
var actor = new GameplayTagCountContainer();
var equipment = new GameplayTagHierarchicalContainer { Parent = actor };
var aura = new GameplayTagHierarchicalContainer { Parent = actor };
equipment.AddTag(GameTags.Cast, 2);
aura.AddTag(GameTags.Cast);
int total = actor.GetExplicitTagCount(GameTags.Cast); // 3。

equipment.Parent = null;                            // 撤销这 2 次父级贡献。
int remaining = actor.GetExplicitTagCount(GameTags.Cast); // 1。
equipment.Parent = actor;                           // 重新转移当前已有贡献。
equipment.Clear();                                 // 只移除 equipment 的贡献。
aura.Clear();
aura.Parent = null;
equipment.Parent = null;
```

贡献方向是**子容器到父容器**，不是父容器反向给子容器继承成员。重新挂接会转移现有显式次数。调用方必须避免循环、溢出和并发修改。不能直接从聚合父容器移除子容器拥有的贡献，也不能在子容器仍认为贡献存在时清空父容器；应先清空或解除子容器的挂接。内建父链会批量派发事件，自定义父容器自行决定行为。很长的父容器链仍有递归传播成本。

<a id="pooling"></a>
## 容器复用与对象池

```csharp
using (GameplayTagContainerPool.Get(out GameplayTagContainer temporary))
{
    temporary.EnsureCapacity(64);
    temporary.AddTag(GameTags.Burning);
    temporary.AddTag(GameTags.Cast);
    // 在此作用域中消费 temporary，不向外保留这个引用。
}
```

也可以直接配对 `GameplayTagContainerPool.Get()` 与一次 `Release(container)`。必须在借出线程归还，不能复制租约后重复 Dispose、Dispose 默认租约、重复归还或归还后继续使用。池不会检测这些错误。每个线程最多保留 64 个归还容器，但它们的内部缓冲区仍可能很大。新建、扩容、序列化以及内部数组池首次租用都可能分配；预热复用不等于所有操作无条件零分配。

<a id="unity-and-serialization"></a>
## Unity 组件、Inspector 与序列化

附带的 PropertyDrawer 编辑 `GameplayTag.m_Name` 和 `GameplayTagContainer.m_SerializedExplicitTags`。它们用于选择已注册标签，不是配置资源管理器，也不会创建标签声明。修改程序集声明后应重新编译或重载脚本域；注册表不会每次查询都自动刷新。

```csharp
using GameplayTags;
using UnityEngine;

public sealed class SerializedTagOwner : MonoBehaviour
{
    [SerializeField] private GameplayTag selectedTag;
    [SerializeField] private GameplayTagContainer configuredTags = new GameplayTagContainer();

    private void Awake()
    {
        // 序列化容器被加载之前，注册表就必须准备好。
        // 独立结构体字段由持有者显式 Resolve，并保存返回值。
        selectedTag = selectedTag.Resolve();
    }
}
```

**初始化必须早于反序列化，不只是早于第一次 Update。** 容器 `OnAfterDeserialize()` 会立即重建成员；注册表未准备好时可能触发程序集发现。Unity 序列化回调可能不在主线程执行，也不能假定结构体回调一定执行。应在加载资源、场景之前，由拥有初始化职责的线程准备注册表，Editor 路径也需要考虑。不能假定仅靠 `Awake` 或 `BeforeSceneLoad` 就早于所有编辑器与资源反序列化。参考：[Unity 序列化回调说明](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ISerializationCallbackReceiver.html)。

关闭 Domain Reload 时，静态注册表、缓存句柄、订阅和线程池可能跨 Play Mode 切换继续存活，不能直接在它们上面重新发布另一套注册表。保持同一套注册表，或真正重启脚本域并重建所有关联状态。参考：[Unity Domain Reload 文档](https://docs.unity3d.com/2022.3/Documentation/Manual/DomainReloading.html)。

`GameObjectGameplayTagContainer` 的 `GameplayTagContainer` 属性提供运行时计数容器，`m_PersistentTags` 会在该组件的 `Awake` 中加入一次。计数容器对象在 Awake 前已经存在，但持久标签贡献尚未加入；访问未激活对象或依赖其他组件 Awake 顺序时，需要业务协调。`gameObject.Bind()` 会获取或添加这个组件并返回绑定组，绑定持有者禁用或销毁时应释放该组。

普通容器序列化的是按 Ordinal 排序的显式**名称**，加载时重新推导祖先。未知名称不会恢复为成员，并可能在下一次序列化时丢弃。单标签保留基于名称的 .NET DataContract 回调；Unity 的独立标签字段由持有者显式解析。`GameplayTagCountContainer` 和 `GameplayTagHierarchicalContainer` 是运行时对象，不是存档 DTO；需要保存计数时，请在业务存档中保存名称和次数。

不要保存 `RuntimeIndex` 或内部数组。编辑 `m_Name` 后必须重新赋值 `tag = tag.Resolve()`。直接修改活容器的序列化名称列表不会同步运行时成员；请使用 Add/Remove API，或在受控加载边界显式从列表重建。调用容器 `OnAfterDeserialize()` 会替换运行态，不应随意对已经被业务修改的活容器调用。

<a id="caller-contract"></a>
## 调用方契约与性能边界

| 责任 | 要求 |
| --- | --- |
| 注册表 | 加载、绑定之前初始化一次；关联状态存活时不替换；调用方负责同步 |
| 句柄 | 修改操作传入当前注册表的有效、已解析、非 None 标签；改名后重新解析 |
| 集合 | 修改操作数非 null；`Empty` 只读是约定，不是运行时保护 |
| 次数与容量 | 数量为正、不超额删除、不溢出；容量可分配且运算不越界 |
| 所有权 | 父链无环；通过贡献所属的子容器移除贡献 |
| 并发 | 不并发修改、不修改中枚举；没有自动线程安全 |
| 对象池 | 租约单一所有者、同线程归还一次、归还后不使用 |
| 错误 | 调用方处理非法输入和回调失败；没有通用回滚保证 |

保留的重复集合判断、缺失成员 no-op、别名处理、祖先计数和内部资源清理，是算法语义，不是防御性校验。没有检查模式开关、每次查询的名称一致性兜底、池代次跟踪或计数溢出预扫描。

缓存句柄、复用容器和输出 List、按合理的闭包规模预留容量，避免在热循环中打印日志或做诊断。底层包括条目、哈希桶和显式索引，**不是每个标签只占 4 字节**。层级修改需要处理祖先路径；交集可能排序所选 ID 并重建闭包。请在自己的操作比例、层级深度、内存规模和 Unity 后端上测量后再下性能结论。这是托管实现，不是 NativeContainer，也不是可直接用于 Burst/Jobs 的线程安全 API。

### 从名称版迁移

这不是名称版 1.1 rc.1 的无缝替换。包名、程序集、API、容器序列化字段、排序和注册表生命周期都不同。将旧 `Count` 改成实际需要的显式或总成员数，使用整数版 Copy/Union/Intersection，显式迁移保存的标签名称，并在需要顺序的输出边界排序。旧的 Settings/Redirects API 不在本版中，也不会自动修改 YAML、Prefab 或配置资源。

<a id="api-map"></a>
## API 索引与仓库结构

| 类型 | 用途 |
| --- | --- |
| `GameplayTagManager` | 一次性注册、名称解析、注册元数据 |
| `GameplayTag` | 缓存句柄、严格层级关系、显式 Resolve |
| `GameplayTagContainer` | 普通显式集合、克隆、复制、交并差运算 |
| `GameplayTagCountContainer` | 重复次数、祖先累计次数与通知 |
| `GameplayTagHierarchicalContainer` | 向聚合父容器贡献计数 |
| `GameplayTagRequirements` | 必需、禁止条件；支持一个或两个持有者 |
| `GameplayTagContainerUtility` | 双持有者查询与容器层级提取 |
| `GameplayTagContainerBinds` | 成组的标签存在性订阅 |
| `GameplayTagContainerPool` | 线程局部的临时普通容器池 |
| `GameObjectGameplayTagContainer` | Unity 组件适配器 |
| `GameplayTagUtility` | 按需名称校验与路径解析 |

名称工具包括 `GetHierarchyNames`、`GetHierarchyLevelFromName`、`TryGetParentName`、`GetLabel`；历史 `GetHeirarchy...` 拼写继续作为别名保留。路径解析工具假定输入名称合法。

```text
Runtime/                      核心与 Unity 组件适配器
Editor/                       Inspector 绘制与树形选择窗口
GameplayTags.Runtime.csproj    可选的独立 C# 构建入口
package.json                  UPM 标识与最低 Unity 目标
README.md / README_CN.md      英文 / 简体中文用法
LICENSE                       MIT 许可证
```

必要的 `.meta` 文件保持不变。当前目录不包含测试、基准、实验快照、结果归档、SDK 版本锁、打包工具、可选源码生成器工程及它们的 CI 工作流。不假定存在自动生成的 `GameTags` API，快速开始中的缓存类就是普通业务代码。移除的开发材料仍可在仓库历史中查看。

[Runtime 源码](Runtime) · [Editor 源码](Editor) · [MIT 许可证](LICENSE)
