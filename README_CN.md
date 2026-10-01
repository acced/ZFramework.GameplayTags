# ZFramework Gameplay Tags：整数运行时

[English](README.md) · [架构与迁移](Documentation~/RUNTIME_INDEX.md) · [原生验收要求](Documentation~/RELEASE.md)

**3.0.0-pre.1 是破坏性结构升级的预发布版，不表示稳定发布已获批准。**声明支持 Unity 2021.3+、C# 9、.NET Standard 2.1。托管 CI 使用明确标注的 Unity API 替身；真实 Unity Editor 和 IL2CPP 必须另外执行。

## 安装与配置

在 Package Manager 选择 **Add package from disk**，打开根目录 `package.json`；使用 Git 时选择 `refactor/runtime-index-bitset-20260920` 分支。业务程序集引用 `GameplayTags`。

通过 **Tools → ZFramework → Gameplay Tags → Create Settings** 创建配置，保留默认路径 `Assets/Resources/GameplayTags/GameplayTagSettings.asset`。标签管理窗口、Inspector、CSV、重定向、来源限制、生成代码和校验继续用于编辑数据。原名称字段和 Unity 元数据保留。

## 编辑数据与战斗数据分开

`GameplayTag`、`GameplayTagContainer` 保存**可序列化的名称**；它们不再承担战斗集合运算。加载时转换成 `RuntimeTag`、`RuntimeTagSet`，并用同一个不可变 `TagRegistry` 冻结查询。

```csharp
using GameplayTags;
using UnityEngine;

public sealed class RuntimeTagExample : MonoBehaviour
{
    [SerializeField] private GameplayTagContainer initialTags = new GameplayTagContainer();
    [SerializeField] private GameplayTagQuery activation = new GameplayTagQuery();
    private RuntimeTagSet owned;
    private FrozenGameplayTagQuery matcher;

    private void Awake()
    {
        TagRegistry registry = GameplayTagManager.CurrentRegistry;
        owned = initialTags.ToRuntime(registry, 32);
        matcher = activation.Freeze(registry);
    }

    public bool CanActivate() => matcher.Matches(owned);
    public bool AddState(RuntimeTag tag) => owned.AddTag(tag);
    public bool RemoveState(RuntimeTag tag) => owned.RemoveTag(tag);
}
```

在 Inspector 配置两个源字段。空查询返回 false。运行方法要求对象已完成 Awake 中的准备过程，不会在查询时自动初始化。

多个单位共享的技能规则，由加载上下文冻结一次：

```csharp
using GameplayTags;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Skill rule")]
public sealed class SharedSkillRule : ScriptableObject
{
    [SerializeField] private GameplayTagQuery conditions = new GameplayTagQuery();

    public FrozenGameplayTagQuery Prepare(TagRegistry registry)
    {
        return conditions.Freeze(registry);
    }
}
```

调用方持有并共享返回的 matcher；随后修改源资产不会改变已经冻结的执行结果。

## 运行时接口与契约

在加载阶段用 `registry.Resolve("State.Debuff.Burning")` 取得句柄并保存。`HasTagExact` 检查显式成员；`HasTag(parent)` 检查父标签本身或任意后代。容器只存显式标签，不维护父标签副本，`Count` 始终为显式成员数量。

`RuntimeTagSet.UnionInto`、`IntersectionExactInto` 覆盖调用方的输出，支持任一输入作为输出。层级 `FilterInto` 支持覆盖源集合，但拒绝覆盖另一个独立条件集合。`CopyFrom`、`AppendTags`、`RemoveTags` 不改变存储表示，也不修改其他输入。`RuntimeTag.None` 不属于任何集合。跨注册表的有效句柄、集合和查询会在修改结果之前失败，不能混用；运行集合参数不能为 null。

`Union`、`IntersectionExact` 创建独立结果，会分配内存。复制构造函数进行真实复制，不使用写时复制。

## 存储选择与零分配范围

每个运行容器只有一份有效成员缓冲：有序 `int[]` 或 `ulong[]` 位图。`TagSetStorage.Auto` 在构造时根据预计容量和数据区大小选择；之后普通增删不自动切换。稀疏数组容量不足时按摊销策略增长，位图一次分配注册表对应的完整数据区。显式转换时创建所需模式的容器，再调用 `CopyFrom`。

注册表、句柄、查询和缓冲准备完成后，约定的查询与修改路径不产生托管分配。初始化、ToRuntime、Freeze、扩容和新建返回值接口不属于该承诺。Into 不会仅因为输入数量上界较大，就扩容一个已经能容纳实际结果的缓冲。位图置位计数在操作中完成，不推迟到计时之后。

运行枚举按确定性的 DFS ID 顺序，不等于名称序数顺序；位图没有伪装成 O(1) 的“第几个成员”索引器。使用 foreach，UI 或存档需要名称顺序时显式导出再排序。`BufferBytes` 只统计成员数组数据区，不是完整对象、注册表或原生内存。

## 生命周期与生成代码迁移

Manager 重建时发布新快照，不会原地改变旧 ID。旧句柄、容器和查询继续绑定旧快照；旧上下文可以自行结束，但不能自动混入新上下文。持久化保存名称，不保存 RuntimeIndex。加载与编辑在主线程，集合由单一调用方管理，不支持并发修改。

重新生成 C# API。生成类改为接收 `TagRegistry` 的实例类，字段是 readonly RuntimeTag，例如 `var tags = new Game.GameplayTags(registry);`。每个运行上下文创建一次绑定，避免静态句柄跨注册表重建失效。构建前仍比较完整生成文本，而不只比较哈希。

## 复现与放行

使用完整 Git checkout、.NET 8 SDK 与 Python 3.12+。工作流会导出三份固定版本；本地导出后运行：

```text
python3 Audit~/integer_audit.py --references artifacts --output artifacts/results --rounds 5
```

新 CI 编译生产代码、Editor、示例、README 完整代码及测试程序集边界；执行差分、快照、区间、容量和分配测试，再用同一输入比较 main、旧 optimize、Alex-Rachel 与当前版本。Auto 为主比较，强制 Sparse/Dense 为补充。保留原始样本、错误行、内存数据区、源码哈希及源码 ZIP；托管排名不等于 Unity/IL2CPP 放行。

原 `run.py` 和旧性能脚本针对 2.x 名称容器，仅供历史复现；本分支主入口为 `integer_audit.py`。原生入口 `unity_acceptance.py` 保留，必须用新源码在隔离项目运行测试，并获取同一版本的设备结果；旧版本的原生报告不能替代。

[MIT 许可证](LICENSE)。完整设计、成本与算法出处见 [RUNTIME_INDEX.md](Documentation~/RUNTIME_INDEX.md)。

## 本地运行时重构（未发布）

新增 `RuntimeTagSet.FromTags(registry, handles)` 批量加载已解析句柄、`ToStorage(storage, capacity)` 显式转换，以及 `DifferenceExact` / `DifferenceExactInto` 直接差集 API。差集不是复制后删除的替代计时；右输入兼作输出时，Sparse 输出会使用自身缓冲区尾部保存排除成员；预留并集成员数容量即可零分配，否则可能扩容。Dense 右别名路径无需临时分配。Sparse/Dense 的原始计时合同分别保留。

.NET 8 的 AVX2/ARM64 优化与 Unity/.NET Standard 2.1 标量实现分开报告。`GAMEPLAYTAGS_FORCE_PORTABLE` 可在托管测试中强制后者；这不等于运行 Unity/IL2CPP。详见 `Documentation~/RUNTIME_INDEX.md` 与 `Audit~/Refactor/`。

### 显式压缩与批量准备

`TagSetStorage.Compressed` 把每个已占用的 16-ID 块压成一个 32 位键/掩码记录，支持最多 2^20 个注册表 ID。集合仍只拥有一份成员数组。`FromTagsForBulk` 在显式分配的加载阶段，按局部性选择压缩记录或稠密位图；小集合保留原有路径。`UnionForBulk` 为新结果选择存储。已有集合不会在查询或修改时隐式转换，准备与转换成本必须纳入端到端测量。

压缩模式的 `RecordCount` / `RecordCapacity` 是物理记录数。`ReservedMemberCapacity` 是任意成员分布的保守预留；同一记录可容纳多个成员。`Capacity` 取当前成员数与保守预留的较大值，因此 Clear 后可能降低，但缓冲区没有缩小。`EnsureCapacity(n)` 为任意 n 个成员的分布预留足够记录。右侧输入同时作为差集输出时，预留左侧的已占用记录数可避免扩容。`BufferBytes` 计算已分配数组容量，不包含对象和数组头。

已有集合可先调用不修改输入的 `SelectBulkStorage()`；存储已合适时，由调用方明确复用输入，否则 `ToStorageForBulk()` 直接生成独立转换结果，不再导出临时句柄数组，也绝不返回原输入。选择过程在不能根据成员数界限直接判断时仍需扫描，成本计入测量。

批量策略会比较已占用记录数、全局字数和稠密后端宽度，属于显式可选策略，并不保证每种工作负载都快。一次操作的转换、部分混合分布仍可能更慢。小型稀疏查询采用标量与 `System.Numerics.Vector<int>`；`GAMEPLAYTAGS_FORCE_PORTABLE` 关闭显式 .NET 8 稠密指令后端，不代表这个可移植 Vector API 也关闭。真实 Unity/IL2CPP 验证仍需单独执行。
