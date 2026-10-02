# PR26：空源复制边界与调用隔离——已执行结果

日期2026-10-02。PR：https://github.com/acced/ZFramework.GameplayTags/pull/26

## 本轮决定

保留 **guarded容量证明＋CopyCore空源直接Clear/return** 的`empty`实现作为较简单的候选；没有证据支持为它额外加NoInlining。指定复制故障一致性修复继续保留。**仍有非空小输入、脏目标和其他公开路径回退，不整包替换生产Runtime，不合并、不发布，不声称原四项全面领先。**

本次接续没有重新创建此前已实现的6个候选。新增独立验收脚本和工作流，重新下载冻结原始ZIP，重跑原归档review，核对补充反汇编与两平台计时二进制/源码身份，补算`empty/eager`和`empty/control`同次样本对照，交付并关闭旧pending状态。新增验收生成**0个新的排名用性能样本**。不得把本报告提交SHA写成原性能计时提交。

## 固定版本与交付

- 主计时提交：`98311092b7e8892e5632c2863a391187f4367cf8`；tree `f6238fab87e3eb9e1e306f42ee22e517e6532aee`。
- 主运行：https://github.com/acced/ZFramework.GameplayTags/actions/runs/37024028909 ，x64/ARM64/collect成功。
- 额外反汇编：https://github.com/acced/ZFramework.GameplayTags/actions/runs/37026586337 。使用各架构原始已计时DLL，未重新编译；不同同架构宿主，不保证同一机器或地址布局。失败的首次诊断37025304103不进入性能结果。
- 本次验收提交：`2a493cb2f3e64f7ec023d83412b4a23cccfdaeb2`；成功运行：https://github.com/acced/ZFramework.GameplayTags/actions/runs/37029414923 。
- 源码/验收包：https://github.com/acced/ZFramework.GameplayTags/actions/runs/37029414923/artifacts/11236074772 ，5,285,527B，SHA256 `14947c76e75f89ce334312605d6a523d36fa06044dde816d2be94ade91ad1bf4`。
- 全部证据包：https://github.com/acced/ZFramework.GameplayTags/actions/runs/37029414923/artifacts/11236985223 ，57,244,959B，SHA256 `afe41a8fb71e1dce6e8e7c082248582d8c26af48b39513cccc08d5d7bf6f32e1`。
- 主原收集包11235249996，SHA256 `7c6b8bf86c65b764c450c4c3a5153f7ec5ab3e79286cba3cb0e7a8db8f01a967`；两个trace原ZIP及其hash在Acceptance.json。

已下载两个最终包并成功保存用户Library。小包含确切Source_Tested.zip、六份生成C#、全部CSV/回退、验收JSON及两平台原DLL的独立JIT输出；大包另外保留未改原始主收集ZIP、trace原ZIP和再次review日志。后加的本解释文档不冒充已在冻结计时快照内。相同类名的候选分别编译，不可一起导入Unity。源码ZIP不包含Git历史，再生成需完整历史检出固定SHA。

## 六个独立构建及两个变量

|版本|复制实现|新因素|
|---|---|---|
|control|固定PR24 batch32；复制的旧故障缺陷保留为反例|无|
|eager|每条成功写入后提交Count|固定PR25正确性对照|
|guarded|一次容量证明；不能证明时走eager增长助手|固定PR25性能对照|
|isolated|guarded|仅给CopyDenseToMicro加NoInlining|
|empty|guarded|仅在CopyCore开头判断空源，Clear后返回|
|boundary|guarded|上述两个因素同时存在|

三个PR25控制核对固定SHA256。公开CopyFrom的Require和自身复制判断不变；私有CopyCore/Union捷径内部条件不取代公开注册表检查。不同注册表的空源、null输入仍拒绝，已有输出不因错误输入被修改。

空源复制不必读源位图：目标已空时，原Clear不执行数组清零；目标Dense非空时仍实际清零全部目标字。没有把全域清零成本假装O(1)，没有共享源数组、缩容量或保留旧位。目标表示与容量不变。

guarded的证明仍为`EntryCapacity >= min(source.Count, MaxEntries)`，并非增加预约或经验Count阈值；非空路径、准确最终Count和可能扩容时的有效部分结果保证保留。所有版本无新增实例字段/公开迭代器字段/对象池/成员临时数组。

## 一、空源的主要收益

同一次主实验，262144实际编号。以下来自新增空源夹具；单位**ns/完整公开操作**，所有行0B。dirty-target在计时循环中先AddTag，再CopyFrom，准备脏状态不是免费操作。报告主轮中位数，不代表每项A/A均稳定；逐项筛选在All_Comparisons.csv。

|平台/空源→目标/目标状态|guarded|empty|
|---|---:|---:|
|x64 Dense→Dense，已空|761.853|6.344|
|ARM64 Dense→Dense，已空|465.138|5.347|
|x64 Dense→Dense，先置脏|764.394|172.747|
|ARM64 Dense→Dense，先置脏|463.763|190.822|
|x64 Dense→Micro，已空|11.846|6.339|
|ARM64 Dense→Micro，已空|10.802|5.320|
|x64 Dense→Micro，先置脏|16.121|11.020|
|ARM64 Dense→Micro，先置脏|15.515|10.163|

大的差距主要来自取消不必要的全Dense零缓冲复制；不能将这个空输入成绩外推到非空、任意注册表和真实手机。

empty/guarded完整分组（快/慢要求三轮主/A/A最不利组合跨过5%，不是统计置信区间）：

|组|数量|x64快/慢|ARM64快/慢|
|---|---:|---:|---:|
|主矩阵空源相关公开路径|504|261/0|278/0|
|补充复制空源|24|15/0|16/0|
|新空源夹具，目标原本已空|48|31/0|32/0|
|新空源夹具，目标先置脏|48|24/0|20/2|
|主矩阵直接非空Dense→Micro CopyFrom|48|0/1|0/0|
|补充非空新建|56|0/0|0/0|
|补充非空预分配|112|0/1|0/0|
|其余主矩阵，包括间接复制路径|1968|20/7|47/9|

因此不是“多了一个空判断，所有场景免费”。主矩阵的空源组包含多种操作/重复配置，不是504种独立使用场景。A/A稳定交集单列在Groups.csv。

## 二、非空容量证明收益仍保留，不归功于本轮空分支

同次运行，U262144，Dense→Micro预分配CopyFrom，单位微秒，0B。

|平台/输入|正确eager|guarded|empty|
|---|---:|---:|---:|
|x64 4096连续|5.342|4.447|4.440|
|ARM64 4096连续|5.760|4.679|4.667|
|x64 4096分散|48.649|29.780|26.898|
|ARM64 4096分散|51.132|34.699|34.852|

empty与guarded大部分是近似成本，分散x64有A/A与具体输入差别，不能把中位数差全部记作新算法收益。新补算empty/eager对照不是重新选阈值：48个主直接非空复制，x6422快/0慢、ARM6423快/0慢；112个补充非空prepared，x6437快/1慢、ARM6440快/0慢。56个fresh相对同样正确eager无持续快/慢，不是所有新建已经领先。

新建Dense→Micro需要成长容量时仍计入全部分配：4096分散新建x64 eager54.541us、empty54.120us；ARM64 eager54.852us、empty54.329us；都是33072B，不是普通克隆后追加成绩。旧缺陷control可作正常成本参考，不能因为更快就恢复其Count错误。

## 三、不保留无收益的NoInlining因素

isolated/guarded：直接非空复制、全部补充copy/empty在两平台没有持续加速。主矩阵x64只有2快/2慢，ARM64无持续快/慢；这些不构成普遍优化证据。

boundary/empty：ARM64完整2808项无持续快/慢；x64有11项持续回退，全部A/A稳定，且没有持续加速。它们位于其他主矩阵路径，不能仅因源码没有直接改到就删除；因果尚未隔离，不能自动说是JIT布局或GC。

独立trace核对原始DLL及源码SHA，与两平台主测量一致。x64的guarded CopyCore原本就调用独立CopyDenseToMicro；加NoInlining没有改变该边界。记录的机器码大小：

|方法|x64 guarded/isolated|x64 empty/boundary|ARM64 guarded/isolated|ARM64 empty/boundary|
|---|---:|---:|---:|---:|
|CopyCore|289|315|420|464|
|CopyDenseToMicro|251|251|348|348|
|CopyDenseGrowing|193|193|236|236|

本轮补齐了PR25未捕获的增长助手大小。相同方法尺寸不等同于全部机器码地址/字节一致，更不证明全部成本一样。trace来自另一个同架构宿主的原DLL，不给主计时做未经证实的逐周期归因。

## 四、明确保留的反例

- x64 U10000/1分散成员/非空prepared Dense→Micro：guarded148.913ns→empty165.385ns，0B，持续回退且A/A稳定。
- x64 U262144/4096分散/主矩阵CopyFrom：guarded27.120us→empty29.858us，持续回退且A/A稳定。这与补充copy的另一个seed/夹具不同，不能只展示更有利的补充行。
- ARM64 U1/空Dense源/先置脏Dense目标：10.668ns→11.491ns，持续回退且A/A稳定。完全跳过源读取仍可能在极小域付出分派代价。
- ARM64 U262144/4096分散/Micro两侧预分配并集：guarded11.281us→empty12.244us，持续回退且A/A稳定；这条核心没有被修改，原因未隔离。
- x64 U262144/8连续/Micro两侧预分配复制后删除：guarded15.264ns→empty16.463ns，持续回退且A/A稳定。
- x64 boundary/empty中，全Dense预分配Union可见约13%持续偏移，多项A/A稳定；不能把增加NoInlining解释为已解决所有间接调用回退。

empty/eager还保留主其他路径x646/ARM647项持续回退，补充dirty组x641/ARM642项；这些对照同时更改容量证明和空分支，应按整体候选验收，不能全部归为单一因素。

## 五、正确性与证据范围

六个构建均完成继承混合/掩码/容量/别名测试、原完整Micro和保护Runtime套件；新增96组边界配置（域0/1/17/257、表示对、容量、dirty、自身操作、不同空注册表、null、清空再用），原异常修复继续通过。精确断言数保留Acceptance.json.tests，不把循环断言夸大为独立案例。

每个架构/候选3931增长点注入和3873正常故障夹具配置：旧control再现3093无效部分状态；其余eager/guarded/isolated/empty/boundary均0。故障hook不进入性能DLL；只承诺指定复制/空输入/同输入Union捷径的部分结果一致性，不是任意mixedUnion OOM、真实进程内存枯竭或异步故障的回滚保证。

每平台33696汇总、101088原始行、707616耗时样本、449568零分配样本。SDK8.0.425/.NET8.0.31 Release、单CPU、关闭tiering/ReadyToRun，三轮打乱独立进程×7样本；所有慢样本、GC计数、A/A保留。

本次额外review重构Git tree、生成变换、DLL哈希、中位数、Count/容量/Bop和原比较标志；与归档结构化结果完全相同。两个额外empty/eager、empty/control比较仅从已复核同次轮次推导，原始结果不变。完整源码和trace跨平台身份匹配；生产Runtime/Editor/Samples/Tests/package未迁移。

真实Unity/IL2CPP/Burst/手机、多角色冷缓存/实际调用分布、长期峰值内存、全部历史main/Alex比较和实验Micro/FrozenQuery正式集成仍未验收。此次不能据此宣称注册表大少成员与多成员批量操作四项全部第一。

最终：保留空源语义快速路径与正确容量证明作为候选，拒绝把无证据的NoInlining混入默认实现；继续保留并定位非空/小域反例，而非再扩数据结构或把局部胜利写成发布批准。
