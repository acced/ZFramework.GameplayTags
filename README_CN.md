# ZFramework Gameplay Tags：Dense-only 4.0 预发布

[English](README.md) · [结构、代价与迁移说明](Documentation~/DENSE_ONLY.md) · [MIT 许可证](LICENSE)

本分支移除 Sparse 和 Auto 后端。每个已经准备好的运行时集合只持有一个 `ulong[]`，前面是完整成员位图，后面是每层缩小 64 倍的非零字索引。注册表仍是不可变 DFS 快照，编辑和序列化仍保存稳定名称。

**这不是已通过 Unity 真机验收的正式版。** 托管编译和 CI 性能数据不能替代 Unity 2021.3 / Unity 6、Android/iOS IL2CPP 验收。所有未领先测点必须保留。完整 Dense 位图的分配成本仍随整个注册表增长，少量成员不会消除这个成本。

## 安装和创建标签

在 Package Manager 中选择本地包的 `package.json`。业务程序集引用 `GameplayTags`。通过 Tools → ZFramework → Gameplay Tags 创建默认配置 `Assets/Resources/GameplayTags/GameplayTagSettings.asset`，添加 `State.Alive`、`State.Debuff.Burning`、`State.Debuff.Stunned`。

## 加载阶段准备，运行阶段复用

```csharp
using GameplayTags;
using UnityEngine;

public sealed class DenseTagsExample : MonoBehaviour
{
    [SerializeField] private GameplayTagContainer initialTags = new GameplayTagContainer();
    [SerializeField] private GameplayTagQuery activation = new GameplayTagQuery();
    private RuntimeTagSet owned;
    private FrozenGameplayTagQuery matcher;

    private void Awake()
    {
        TagRegistry registry = GameplayTagManager.CurrentRegistry;
        owned = initialTags.ToRuntime(registry, 1);
        matcher = activation.Freeze(registry);
    }

    public bool CanActivate() => matcher.Matches(owned);
}
```

任意正容量都会一次准备完整位图。`EnsureCapacity(1)` 不是只为一个标签分配空间。容量为零的空对象可以不持有位图；第一次加入成员会分配。查询不分配、不自动修复。零 GC 承诺以注册表、查询和结果缓冲均已准备为前提。

## 调用方持有结果，不创建临时集合

```csharp
using GameplayTags;
using UnityEngine;

public sealed class DenseUnionExample : MonoBehaviour
{
    private RuntimeTagSet owned;
    private RuntimeTagSet buffs;
    private RuntimeTagSet output;

    private void Awake()
    {
        TagRegistry registry = GameplayTagManager.CurrentRegistry;
        owned = new RuntimeTagSet(registry, 1);
        buffs = new RuntimeTagSet(registry, 1);
        output = new RuntimeTagSet(registry, 1);
        owned.AddTag(registry.Resolve("State.Alive"));
        buffs.AddTag(registry.Resolve("State.Debuff.Burning"));
    }

    public int CombinedCount()
    {
        RuntimeTagSet.UnionInto(owned, buffs, output);
        return output.Count;
    }
}
```

并集、精确交集允许覆盖任一输入。层级过滤允许覆盖源集合，但不允许覆盖另一个独立的条件集合。所有运行时操作数必须非空，且属于同一注册表快照；即使集合为空也不能混用快照。

`new RuntimeTagSet(source)` 和 `Union(left,right)` 创建独立对象，分配包含在对应基准中。`CopyFrom` 覆盖原成员、保留预分配；`Clear` 不释放容量。新建空副本不复制原对象未使用的预留空间。空 All 为真、空 Any 为假；没有根表达式的查询为假；None 不是成员。

## 迁移

删除所有 `TagSetStorage` 参数和 `.Storage` 调用，本分支不提供 Sparse 兼容包装。名称、重定向、按注册表创建的生成绑定和 `Freeze(registry)` 保持原有职责。不要保存 RuntimeIndex；旧快照仍可使用，不同快照不能混合。枚举顺序为 DFS 编号，需要 UI 名称排序时显式导出排序。

## 原理与验收

采用分层非零索引跳过空区域、低位清除逐位置位扫描、便携 SWAR 计数，以及按字的 OR / AND / AND NOT。按占用量选择线性或索引遍历只是算法路径选择，成员始终为 Dense 位图。

不能因为更换数据结构就保证任意输入都第一。尤其是大注册表、少成员、每次新建结果时，Dense 的初始化成本必须计入。`Audit~/dense_audit.py` 同步比较固定的 main、optimize、Alex-Rachel、上一版 Auto、上一版强制 Dense 和本候选。旧的完整 100 场景保留，增加更大注册表与复用缓冲场景；所有失败、未领先、分配与内存成本一并输出。

CI 中的原生测试只有接口替身编译，没有原生执行。真实平台放行仍须提供对应源码的 Unity 与 IL2CPP 证据。
