### 临时计算：自己选择复用策略

每帧重复使用的工作容器，可以直接长期持有，用完 `Clear()`。多个临时作用域需要借还时，再用线程本地池：

```csharp
using (GameplayTagContainerPool.Get(out GameplayTagContainer temporary))
{
    temporary.AddTag(GameTags.Burning);
    temporary.AddTag(GameTags.Cast);
}
```

默认采用**不限制保留预算**的策略，大容器不会因为内置的 64 KiB 阈值被丢弃。在负责借还的线程上，加载阶段自行选择：

```csharp
// 只限制个数，不限制单个容器的容量。
GameplayTagContainerPool.Configure(new GameplayTagContainerPool.RetentionPolicy(32));

// 或者同时限制个数、单个和总数组载荷。以下数值仅为业务配置示例。
GameplayTagContainerPool.Configure(new GameplayTagContainerPool.RetentionPolicy(
    maxCount: 64, maxContainerBytes: 256 * 1024, maxTotalBytes: 4L * 1024 * 1024));

// 或者不保留归还对象，或恢复不限制预算。
GameplayTagContainerPool.Configure(GameplayTagContainerPool.RetentionPolicy.Disabled);
GameplayTagContainerPool.Configure(GameplayTagContainerPool.RetentionPolicy.Unbounded);
```

`Configure` 会释放当前线程已经缓存的对象。`Clear()` 同样释放缓存及池数组，但保留选定策略。`RetainedCount` 与 `RetainedDataBytes` 可用于观察保留量；字节数只计算容器的数组载荷，不含对象头、数组头和进程共享数据。预算判断是调用方选择的复用策略，不是非法参数校验；预算必须有效且非负。

策略只影响当前线程，不会自动传播到其他线程。默认不限制预算可能保留峰值内存；大型工作流可以选择长期持有容器，或设置适合项目的预算。借还期间由调用方保证同线程、恰好一次归还，不复制租约分别释放，不释放默认租约，不使用已经归还的引用。池不追踪这些误用。

