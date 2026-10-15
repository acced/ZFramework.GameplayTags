# ZFramework.GameplayTags

面向性能的 C# 层级标签库。运行时使用整数 ID、紧凑数组与开放寻址索引；标签祖先关系在注册时编译为连续区间。支持普通集合、计数集合、父容器传播、条件查询、事件、对象池、Unity 序列化和源码生成。

这一版重写了运行时存储和算法。**普通集合删除只更新受影响的祖先，不再重建全部隐式标签。** 不为每个实体分配覆盖整个全局注册表的数组。首次重写的历史测量见 [性能说明](Documentation~/PERFORMANCE.md)，Copy、Union 与显式 Intersection 的后续优化和交叉测量见 [批量操作性能记录](Documentation~/BULK_PERFORMANCE.md)，升级行为见 [迁移说明](Documentation~/MIGRATION.md)。

## 安装与构建

Unity Package Manager 可从本仓库的 Git URL 安装；选择包含本版本 `package.json` 的分支、tag 或提交，并在 URL 后追加 `#引用` 固定版本。包以 Unity 2022.3、C# 9、.NET Standard 2.1 为兼容目标，包含独立的 Runtime / Editor assembly definition 与固定 GUID 的 `.meta` 文件。普通 .NET 核心不依赖 Unity；以下测试需要 .NET 8 SDK：

```bash
dotnet build GameplayTags.Runtime.csproj -c Release
dotnet run --project Tests~/GameplayTags.Tests.csproj -c Release
```

`Tests~`、`Benchmarks~`、`SourceGenerator~` 和 `Documentation~` 不参与 Unity 脚本导入。Unity 编辑器、Mono 与 IL2CPP 的实际性能应在目标项目中单独测量。

## 注册一次，缓存句柄

```csharp
using GameplayTags;

// 在读取任何标签、访问生成的缓存句柄之前执行。
GameplayTagManager.Initialize(
    new GameplayTagRegistration("State.Stunned", "无法行动"),
    new GameplayTagRegistration("State.Burning", "持续燃烧"),
    new GameplayTagRegistration("Ability.Fire.Cast"));

GameplayTag stunned = GameplayTagManager.RequestTag("State.Stunned");
GameplayTag burning = GameplayTagManager.RequestTag("State.Burning");
GameplayTag state = GameplayTagManager.RequestTag("State"); // 父标签自动生成。
```

也可沿用 assembly attributes。此时第一次有效名称查询调用 `InitializeIfNeeded()`，扫描已加载程序集，然后冻结注册表：

```csharp
[assembly: GameplayTags.GameplayTag("State.Stunned", "无法行动")]
[assembly: GameplayTags.GameplayTag("State.Burning")]
```

两种入口选择一种。显式 `Initialize(...)` 只注册传入的声明，不再扫描程序集；冻结后不支持重新注册或替换 ID。名称按 `StringComparer.Ordinal` 区分大小写。运行时 ID 只在当前注册表中有效，资产、存档和网络协议应保存名称或使用自己的稳定映射。

Unity 的 Editor 标签窗口和资产反序列化也会触发名称查询，因此显式初始化必须早于这些路径，不能默认等到 `Awake`。使用序列化标签资产时，assembly attributes 是直接可用的注册方式。关闭 Domain Reload 时，已冻结的静态注册表会跨 Play 会话保留，不要在每次进入 Play 时重复初始化。

`RequestTag(name)` 对未知、空或 null 名称返回 `GameplayTag.None`，没有日志。外部输入可以在加载边界使用 `RequestTag(name, out tag)` 和 `GameplayTagUtility.IsValidName(name)`；不要在每帧重复进行字符串查找。

## 普通集合与层级查询

```csharp
// capacity 包含祖先：这是总 distinct tag 容量，不只是显式标签数量。
var tags = new GameplayTagContainer(capacity: 8);
tags.AddTag(stunned);
tags.AddTag(stunned);                     // 集合语义：重复添加不增加数量。

bool a = tags.HasTag(stunned);            // true
bool b = tags.HasTag(state);              // true，隐式祖先。
bool c = tags.HasTagExact(state);         // false，未显式加入。

tags.AddTag(burning);
tags.RemoveTag(stunned);
bool d = tags.HasTag(state);              // true，Burning 仍贡献 State。

foreach (GameplayTag tag in tags) { }      // 具体类型 foreach 不装箱。
foreach (GameplayTag tag in tags.GetExplicitTags()) { }
```

`TagCount` 是包含祖先的不同标签数量；`ExplicitTagCount` 是显式不同标签数量。`IsParentOf` / `IsChildOf` 表示严格祖先关系，自己与自己不匹配，`None` 不充当根节点。

查询另一集合时，查询集合的**显式标签**是条件：

| 调用 | 含义 |
| --- | --- |
| `holder.HasTag(tag)` | holder 显式或隐式包含 tag |
| `holder.HasTagExact(tag)` | holder 显式包含 tag |
| `holder.HasAny(query)` | 至少满足 query 的一个显式条件 |
| `holder.HasAll(query)` | 满足 query 的全部显式条件 |
| `HasAnyExact` / `HasAllExact` | 只使用 holder 的显式标签匹配 |
| `holder.HasAll(queryA, queryB)` | 同时满足两组条件 |
| `GameplayTagContainerUtility.HasAll(a, b, query)` | 每个条件由 a 或 b 任意一方满足 |

空条件的 `HasAny` 为 false，`HasAll` 为 true。null 查询集合沿用空条件语义。`GetParentTags` / `GetChildTags` 及其 Explicit 版本向调用者的列表追加结果，不会隐式清空列表。

集合代数以显式标签为基础，再生成结果的祖先闭包：

```csharp
GameplayTagContainer union = GameplayTagContainer.Union(left, right);
GameplayTagContainer common = GameplayTagContainer.Intersection(left, right);

// 支持覆盖输出及输入/输出别名。
GameplayTagContainer.Intersection(left, left, right);
GameplayTagContainer.Copy(destination, source);
```

## 计数与事件

```csharp
var counts = new GameplayTagCountContainer(capacity: 16);
counts.RegisterTagEventCallback(state, GameplayTagEventType.NewOrRemoved,
    (tag, count) => UpdateStatePresence(count != 0));
counts.OnAnyTagCountChange += (tag, count) => ObserveCount(tag, count);

counts.AddTag(stunned, 3);
int exact = counts.GetExplicitTagCount(stunned); // 3
int total = counts.GetTagCount(state);           // 3
counts.RemoveTag(stunned, 2);                    // 两者都变为 1。
counts.Clear();                                 // 两类事件都收到归零通知。
```

`NewOrRemoved` 仅用于 0 与非 0 之间的转换；`AnyCountChange` 用于每次计数变化。`AddTags(other)` / `RemoveTags(other)` 对来源中的每个**不同显式标签**加/减一次，不复制来源的完整次数。使用 amount 重载可以批量更新一个标签；amount 必须为正，删除量不得大于现有显式次数。

普通计数容器在完整修改完成后派发回调。批量操作先完成全部修改，再派发通知；回调可重入修改同一容器，新的通知排到当前队列后面。通知携带该次修改后的计数，回调读取容器时可能看到更晚的重入状态。回调抛出的异常直接传播，已经提交的数据不回滚，尚未派发的通知被释放，下次修改可正常派发。

没有监听器时跳过事件队列。队列和快照缓冲可复用；第一次增长容量、订阅委托和创建集合仍会分配内存。`RemoveAllTagEventCallbacks()` 清除逐标签订阅；全局 C# events 仍由订阅方解除。

## 父容器、条件与对象池

`GameplayTagHierarchicalContainer.Parent` 接收子容器的全部显式次数。换父容器、清空、移除部分标签都会按实际次数转移贡献。父关系必须无环；传播到父容器的次数由子容器拥有，应通过子容器修改，不能在父容器直接删除这些贡献。清空一个仍承载子容器贡献的父容器前，应先解除相应子容器的 Parent。

```csharp
var parent = new GameplayTagCountContainer();
var child = new GameplayTagHierarchicalContainer { Parent = parent };
child.AddTag(stunned, 3);
child.Parent = anotherParent; // 原父容器减少 3，新父容器增加 3。

var requirements = new GameplayTagRequirements(forbidden, required);
bool allowed = requirements.Matches(staticTags, dynamicTags);

using (GameplayTagContainerPool.Get(out GameplayTagContainer temporary))
{
    temporary.AddTags(tags);
    // 借用期间使用 temporary。
}
```

内置父容器链在提交整条链后派发事件；自定义 `IGameplayTagCountContainer` 父容器控制自己的通知时机。对象池按线程存储，最多保留 64 个容器；借出的容器必须在借用线程上释放一次，释放后不可继续使用。

`GameplayTagContainerBinds` 实现 `IDisposable`，绑定后立即报告存在状态，结构体副本共享订阅所有权。Unity 的 `GameObjectGameplayTagContainer` 在 `Awake` 前就提供可用的运行时容器，在 `Awake` 加入持久标签。

## 使用契约与兼容范围

- 热路径信任调用者：修改操作只接收当前注册表中有效的非 None 标签；不做重复名称验证、警告日志或异常包装。
- 容器不是并发集合。注册表冻结后可并发读取；同一个可变容器需要由调用方保证单线程访问或外部同步。
- 枚举顺序不固定，删除采用末项交换。修改集合使已有枚举器失效；不执行版本检测。
- `GameplayTagContainer.Empty` 是只读约定下的共享空查询对象，请勿修改。
- `m_Name` 与 `m_SerializedExplicitTags` 保留给旧资产和序列化；运行时修改应使用库 API，不能直接篡改这些字段。
- CLR 自身的数组边界检查仍然存在。没有使用不受管理的指针来绕开内存边界。

生成的类型可避免手写名称：参见 [源码生成器](SourceGenerator~/README.md)。该生成器需要单独构建；Unity 安装步骤也在该文档中。

## 验证与复现

行为验证包含确定性回归、随机操作与独立 HashSet / Dictionary / 名称前缀模型对照。基准用固定提交的原始源码和同一套场景比较，并记录分配量；不以计时阈值作为 CI 正确性断言。

详细命令、环境限制和结果见 [性能说明](Documentation~/PERFORMANCE.md)。

## 算法参考

以下是结构选型的原始资料，不能视为本库在某种硬件上的性能保证：

- [LLVM 容器指南](https://llvm.org/docs/ProgrammersManual.html#set-like-containers-std-set-smallset-setvector-etc)：小集合线性扫描、DenseSet 与 SparseSet 的成本取舍。
- [EnTT 作者：稀疏集合](https://skypjack.github.io/2020-08-02-ecs-baf-part-9/)：连续存储与末项交换删除。
- [Jeff Erickson：Depth-First Search](https://courses.grainger.illinois.edu/cs374al1/fa2025/notes/06-dfs.pdf)：DFS 区间与祖先关系。
- [Abseil Swiss Tables](https://abseil.io/about/design/swisstables)：分组探测、控制字节和 SIMD 设计。本库使用适合可移植 C# 的标量索引，并未把它命名为 SwissTable。

MIT License。
