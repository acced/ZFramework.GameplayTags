# Tag Playground · API 日志示例

Demo 只有一个 MonoBehaviour 脚本 TagPlayground.cs，标签定义保存在独立的 Editor/GameplayTagSettings.asset 中。脚本没有 UI、模型层或 assembly 标签特性。

## 当前项目运行

打开 TagPlayground.unity，点击 Play，在 Console 查看 [Tag Demo] 输出。也可以将 TagPlayground 挂到空物体上。

Awake 初始化生成注册表；Start 演示添加和移除标签、父标签和精确匹配、施法条件，以及两个来源的燃烧计数和事件。每条查询附带预期结果；事件数量依次为 1、2、1、0。

## 编辑标签

1. 打开 Tools → Gameplay Tags → Tag Settings。
2. TreeView 显示 Demo 的五条显式标签和隐式父节点。支持搜索、添加、修改、删除分支、Undo/Redo。
3. 修改名称或说明 后点击“保存修改”，再点击“生成代码”。
4. 等 Unity 编译完成，使用 GameplayTagRegistry.Tags 中的字段。

SO 是生成器唯一的标签来源。Editor 和 Player 都使用生成注册表；编辑器选择器直接读取 SO，不初始化运行时标签。不要再添加 assembly 特性，也不要手写缓存字段。

重命名会更新配置中的整个分支，不会自动修改 C# 字段引用、场景或其他资产中的名称；这些引用需同步修改。

## 首次导入

先安装包并等待 Editor 工具编译成功，再在 Package Manager 中导入 Samples → Tag Playground。示例附带配置 SO。

执行 Tools → Gameplay Tags → Generate Player Registry，或在标签编辑器中点击“生成代码”。即使 Demo 因生成文件尚不存在而报错，已加载的 Editor 工具也能直接读取 SO 并生成完整文件，无需先编译 Demo 或执行 Player Build。

项目只允许一份 GameplayTagSettings。已有配置时，将示例的五条标签添加到原配置，再移除示例附带的配置资产，避免两份配置冲突。

## 生成文件与构建

输出位于 Assets/GameplayTags.Generated，包含注册表、只读标签字段及独立 asmdef；Demo asmdef 已引用生成程序集。删除生成目录后可从 SO 再次生成，无需旧业务 DLL。

修改配置后必须重新生成。构建前会检查配置与生成结果，不一致时会提示生成并停止构建，不会再扫描 Player 程序集或在构建中覆盖生成表。

必须在首次访问标签字段及加载带标签的序列化资源前调用 GameplayTagRegistry.Initialize()。当前纯代码 Demo 在 Awake 初始化即可；业务项目应按自己的资源加载顺序安排启动。

Packages 下的 Samples~ 是发布源文件，Unity 实际编译的是导入 Assets 后的副本。

