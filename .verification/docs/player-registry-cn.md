### Player 注册表：按实际构建采集，不猜 Editor 声明

普通项目继续使用程序集声明或显式 `Initialize`。希望 Player 启动不扫描程序集时，可以启用 Editor 生成器。它通过 Mono.Cecil **读取本次 Player 编译后的 DLL 元数据**，不加载或执行这些 DLL，不再把 Editor 域当成 Player。程序集定义的平台选择、`#if !UNITY_EDITOR`、平台宏、额外 scripting defines，以及随构建进入的托管插件，都以该次编译产物为准。

在 **Tools → Gameplay Tags → Generate Player Registry** 启用后，使用平时的 Build 流程构建目标。第一次或声明变化时，脚本编译后的回调会写出正确的 `Assets/GameplayTags.Generated.cs`，并停止当前构建。让 Unity 导入并编译新脚本，再以相同配置构建。第二次会同时核对源码和**已编译表的指纹**；旧代码没编进去就不会通过。切换平台或 defines 导致声明不同，也会更新后要求重编，不静默沿用旧表。

这是有意的两阶段生成，不尝试在已经完成的脚本编译阶段塞入新 C#。批处理时，第一次输出生成文件后，由构建任务重新启动一次 Unity 构建；不要无条件循环重试所有构建错误。自定义管线也可以在链接／裁剪前，将该次完整 Player 托管 DLL 列表交给 `GameplayTagRegistryGenerator.GenerateFromPlayerAssemblies(paths)`，返回 false 表示需要编译更新后的表。不存在输出文件时不启用生成检查。

生成表**不安装自动启动回调**。由自己的启动流程调用一次 `GameplayTags.Generated.GameplayTagRegistry.Initialize()`，再创建标签缓存和加载序列化容器；不要与另一套显式初始化混用。Editor 下该入口使用 Editor 自己的程序集声明，Player 下使用生成表。业务缓存类加显式静态构造器，可以避免 `beforefieldinit` 提前初始化；首次访问仍必须排在启动之后。

只支持直接的 `GameplayTagAttribute` 声明，包含构造参数和 `TagName`、`Description`、`Flags` 命名覆盖。自定义派生特性可能执行任意代码，元数据工具不能替它求值；发现这类声明会在构建时报错，改用直接特性或显式注册，不会把它静默遗漏。运行中加载的额外程序集不属于本次 Player 构建。Mono.Cecil 只被 Editor 程序集引用，不进入运行时 API。该回调会增加构建期读取／中转工作，不是零成本构建；实际 Unity/IL2CPP 整体流程仍需在项目中验收。

