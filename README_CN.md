# ZFramework Gameplay Tags：按需分配的压缩位图候选版

[English](README.md) · [结构、取舍与验收](Documentation~/PACKED_BITMAP.md)

本分支从旧 Auto 的固定提交 `84729cc` 重新开始，不沿用全域 Dense 分支的成员存储。它是预发布候选，不是稳定发布，也不预先承诺每项性能第一。

## 运行时结构

`GameplayTag`、`GameplayTagContainer` 继续保存可序列化名称。加载时将其转换为注册表作用域内的 `RuntimeTag`、`RuntimeTagSet`。不可变注册表、DFS 子树区间、源查询校验和冻结查询的语义保持。

所有运行时成员都由位图表达。每个目录项覆盖 4096 个编号，用 64 位目录说明哪些成员字非零，使用位计数定位紧凑存储中的字。空白区域不分配成员存储，一个成员字可以直接放在对象内部。

没有 `TagSetStorage`，不恢复 Sparse 成员 ID 数组，不使用对象池、共享可变结果或延迟名称解析。这在物理分类上属于**分块压缩位图**，不是覆盖全域的完整 Dense 数组。目录中的地址不是成员 ID 列表。首版使用有序的有效块目录，而不是为递归树的每个节点创建对象；块定位也不能冒充完整位图的常数时间直接寻址。

## Unity 接入

将完整分支作为本地 UPM 包安装，选择 `package.json`，保留 `.meta`。声明的最低版本仍是 Unity 2021.3；真实 Unity 与 IL2CPP 要单独验收，不能用托管替身的编译结果代替。

通过 Tools → ZFramework → Gameplay Tags 创建配置。标签定义、重定向、CSV、Inspector 工作流保持。生成代码仍是按注册表创建的实例绑定，不是静态整数句柄。

## 加载时准备，运行时复用

```csharp
using GameplayTags;
using UnityEngine;

public sealed class CharacterTagOwner : MonoBehaviour
{
    [SerializeField] private GameplayTagContainer definition = new GameplayTagContainer();
    public RuntimeTagSet Tags { get; private set; }

    private void Awake()
    {
        TagRegistry registry = GameplayTagManager.CurrentRegistry;
        Tags = definition.ToRuntime(registry, 128);
    }
}
```

这里预留 128 个成员，意味着为任意分布的 128 个成员准备目录和字容量，而不是只对连续编号保证不分配。

```csharp
using GameplayTags;
using UnityEngine;

public sealed class TagCondition : MonoBehaviour
{
    [SerializeField] private GameplayTagQuery definition = new GameplayTagQuery();
    private FrozenGameplayTagQuery matcher;

    private void Awake()
    {
        matcher = definition.Freeze(GameplayTagManager.CurrentRegistry);
    }

    public bool Matches(RuntimeTagSet owned)
    {
        return matcher.Matches(owned);
    }
}
```

使用 `registry.Resolve("State.Debuff.Burning")` 取得句柄后反复用于增删和查询，不要逐帧解析字符串。默认句柄不匹配，其他注册表的句柄被拒绝，即使名称相同。

## 新建结果与缓冲复用

复制构造、`Union` 返回独立可修改结果。分配与有效目录及成员字有关，不再直接按整个注册表分配；复制的是内容，不复制源对象尚未使用的预留容量。

`CopyFrom`、`UnionInto`、`IntersectionExactInto`、`AppendTags`、`RemoveTags` 复用自己的缓冲。并集和精确交集允许覆盖任一输入。层级 `FilterInto` 允许覆盖源集合，不允许覆盖另一个独立条件集合。所有对象必须属于同一注册表，`Count` 在操作结束时立即准确。

`EnsureCapacity(n)` 为任意分布预留；`ReservedMemberCapacity` 表示与分布无关的保证。`Capacity` 至少等于 Count，也可能包含现有聚集成员所达到的容量；改变分布前不要用它代替 `EnsureCapacity`。扩容采用摊销增长，清空和删除保留缓冲。只有预留足够目录和字空间的准备路径才承诺零分配。

枚举使用 DFS 编号顺序，不承诺名称排序。存档和 UI 排序显式导出名称。并发修改、枚举过程中修改均不支持。

## 验收与限制

CI 导出完整源码和固定基线，编译实际 C#，执行跨字／跨块、别名、独立所有权、目录位计数、随机操作、分配正对照检查，并保留共同输入的所有性能样本。新建分配必须包含在新建结果计时中，预分配输出单列，失败项和未领先项不删除。

`Audit~/packed_audit.py` 运行共同基准；`Audit~/packed_compat.py` 将旧语义测试的两种表示维度显式缩减为一种位图，保留适配记录，并编译中英文示例、生成代码和程序集边界。测试程序不改写生产源码。

托管 CI 通过不代表真实 Unity、移动设备、稳定发布或全部性能目标已经通过。旧测试脚本和历史设计文档用于追溯，不是当前表示的验收入口。

许可证：[MIT](LICENSE)。
