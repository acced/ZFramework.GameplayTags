# Preparation v3：独立调用点补充复核

主实现仍为 `db0ffc25448a5df399651550acf627d210482aeb`，没有修改运行时代码。补充夹具实际成功测量提交为 `847a014a51914023bdc54517b2760605645971b7`。

- 成功诊断：https://github.com/acced/ZFramework.GameplayTags/actions/runs/36971172133
- 下载后独立复核：https://github.com/acced/ZFramework.GameplayTags/actions/runs/36971434893
- 原始双平台诊断与复核包：https://github.com/acced/ZFramework.GameplayTags/actions/runs/36971434893/artifacts/11211289000
- 验收包 SHA256：`1e7e7e66a8afea805e0ec123ef89a689e19e5e018dc8288d4e161b5cd3ad2272`。

每个平台核对12个生成源码文件与主实验完全一致，同时校验6个实际二进制的哈希。逐条复算864原始测量行、6048个耗时样本，重算各轮中位数、最终中位数、A/A与5%筛选值；所有分配样本为0B。

## 结果

v3与v1在独立调用点每个家族的24个场景中，x64和ARM64都没有持续5%退化。A/A全部在5%内的数量：x64 Runtime19/24、Micro15/24；ARM64两家族均24/24。不能将x64噪声忽略。

x64八个分散成员：Runtime v1 14.484ns、v2 17.620ns、v3 14.521ns。ARM64同场景：v1 14.743ns、v2 16.947ns、v3 14.870ns。诊断主机和校准不同，这些数字不能替换主矩阵中的16/19/15ns等结果。

空/单成员的v2收益仍然可能丢失。ARM64单成员Runtime为v2 3.955ns、v3 5.600ns；Micro为v2 3.833ns、v3 9.034ns。没有以这些代价换取全项领先的声明。

## 机器码证据的准确范围

x64实际被计时的ResetLoop中，v2出现带名称的ResetFromSortedUnique调用；v1/v3对应Reset路径已内联。Runtime的调用方机器码为v1/v3各155B，v2调用方80B加独立Reset345B；Micro为133B与80B加355B。不能只比较调用方字节数，忽略被调用方法。

ARM64记录了实际调用方与独立方法反汇编及相同的性能趋势：Runtime v1/v3调用方244B，v2调用方124B并生成独立Reset504B；Micro220B与124B/496B。自动摘录没有解析出带方法名的ARM64call行，因此不把空的命名匹配列表解释成“没有调用”，也不以此单独证明ARM64的具体调用目标。

这项诊断排除了“只有巨大ConstructionLoop才会出现该性能趋势”的简单解释，但不证明某个具体JIT预算是全部原因。方法分派、内联、生成代码和宿主布局仍需要分别考虑。主要结果应使用原始完整矩阵。

首次诊断run36970766344在未计时校验中错误使用Set.Select导致编译失败，因为Set只提供foreach模式。修正为foreach后重新执行成功；失败记录未删除，也不计作通过。未为了修正夹具增加库接口。

Linux .NET x64/ARM64，非Unity/IL2CPP/Burst或手机验证。此附录不增加生产API、存储状态或自动选择规则。
