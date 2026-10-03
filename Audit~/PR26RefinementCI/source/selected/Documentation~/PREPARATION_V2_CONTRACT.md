# Preparation v2：边界、所有权与接入合同

本文件描述 PR #13 的实验 API，不表示生产 Runtime 已替换，也不表示性能验收已通过。实际计时源码固定在 `aae745466fde9bec4e11d986886572be4c6bec2e`。生成的 Array/Dense 与 Micro/Dense 是两个独立实验编译目标。

## 三种问题分别处理

### 有序且唯一的输入

`FromSortedUnique` 接收同一注册表颁发、按 RuntimeIndex 严格递增的句柄。
新结果在私有存储中边校验边写入；任一输入不合法都抛异常，不返回半成品。

`ResetFromSortedUnique` 覆盖已有集合，因此先校验完整输入，再扩容、清空、写入。
无效输入不改变已有成员、计数或容量。空输入等同清空；单元素仍须验证注册表身份。

两者的校验顺序不同是所有权差异，不是热路径省略了外部校验。
调用期间不得从其他线程修改输入。没有回调、延迟执行或共享的可变准备标志。

### 乱序、可能重复的输入

Dense：直接置位，只有原来为零的位才增加 Count；不需要先排序。

Array：当原始输入长度不超过最终预留容量时，直接在最终独占数组中填入编号，对有效前缀排序，再原地去重。闲置容量不参与排序和查询。

原始重复数量超过整个注册表大小时，最终容量仍按原合同限制在注册表大小，不能容纳所有原始编号。因此保留临时排序工作区回退。不能把这条路径写成“所有乱序输入都无临时分配”。

Micro：除空/单成员以外，仍用私有编号工作区排序后编码。未加入未经测量的原地编码排序算法。

调用者的数组从不被排序、修改或保留。

### 独立复制与显式转换

`new RuntimeTagSet(source)` 是普通复制，继续保留源 Capacity 和源表示。

`source.CopyAsStorage(target, capacity)` 是新的显式准备操作，返回独立对象，预留容量为：

```text
min(registry.Count, max(source.Count, requestedCapacity))
```

因此它不是普通复制构造器的替代性能测点。基准中的对照是：

```csharp
var result = new RuntimeTagSet(source.Registry,
    Math.Max(requestedCapacity, source.Count), target);
result.CopyFrom(source);
```

同一种表示也允许显式复制到新的预约容量。Auto 不是显式转换目标，会被拒绝。
现有对象没有换表示，没有同时维护两份成员集合，也没有查询次数等运行时统计。

## 实验工程里的调用示例

以下示例需要生成的 v2 类，并非当前生产包中已存在的 API。

```csharp
// 输入必须已经解析为 registry 的有效句柄。
var tags = RuntimeTagSet.FromUnordered(
    registry, incomingHandles, storage: TagSetStorage.Dense);

// 复用已有对象；容量足够时，本入口不分配。
tags.ResetFromSortedUnique(sortedUniqueHandles);

// 显式、独立的新结果；不是在 tags 内原地换存储。
var compact = tags.CopyAsStorage(TagSetStorage.Sparse, capacity: 128);

// 普通克隆仍保留原容量，不能拿上面的小容量结果代替它的测量。
var clone = new RuntimeTagSet(tags);
```

Array/Dense 的转换不能假装已经覆盖 Array↔Micro。
Micro 编译目标提供对应的 `BitmapLayout` API，但尚未合并成一套自适应生产类型。

## 内存与错误路径

没有新增集合实例字段，没有池、写时复制、共享临时缓冲或延迟 Count。
新建集合仍分配独立结果；零分配承诺仅适用于明确的、容量充足的复用路径。

新建时后段才出现错误，v2 可能已经分配结果缓冲。这个错误路径代价必须保留在说明中，不能据成功路径的 0 B 复用成绩推导错误路径也是零分配。
覆盖已有对象则继续保持完整验证后才修改的保证。

本轮的测量环境是 Linux .NET。Unity、IL2CPP、Burst、移动设备、冷初始化及完整长期内存预算需要分别验收。原来的精确查询、独立并集、复制后追加、复制后删除不因新增准备 API 就自动算作提速。
