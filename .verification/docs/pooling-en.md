### Temporary work: choose your reuse policy

For work repeated every frame, keep a container and call `Clear()` after use. Use the thread-local pool when separate temporary scopes benefit from borrowing:

```csharp
using (GameplayTagContainerPool.Get(out GameplayTagContainer temporary))
{
    temporary.AddTag(GameTags.Burning);
    temporary.AddTag(GameTags.Cast);
}
```

The default is **unbounded retention**, so a large container is not silently discarded by a built-in 64 KiB threshold. Choose a policy at a loading boundary on the borrowing thread:

```csharp
// Limit object count, but not an individual container's capacity.
GameplayTagContainerPool.Configure(new GameplayTagContainerPool.RetentionPolicy(32));

// Or limit count, individual array payload and total array payload.
// These numbers are application examples, not library defaults.
GameplayTagContainerPool.Configure(new GameplayTagContainerPool.RetentionPolicy(
    maxCount: 64, maxContainerBytes: 256 * 1024, maxTotalBytes: 4L * 1024 * 1024));

// Or retain nothing, or restore unbounded retention.
GameplayTagContainerPool.Configure(GameplayTagContainerPool.RetentionPolicy.Disabled);
GameplayTagContainerPool.Configure(GameplayTagContainerPool.RetentionPolicy.Unbounded);
```

`Configure` drops the current thread's cached objects. `Clear()` also releases the pool array, but keeps the policy. Observe `RetainedCount` and `RetainedDataBytes`; bytes count container array payload only, excluding object/array headers and process-shared data. Budget comparisons implement the selected policy, not argument validation: supply valid, nonnegative limits.

Configuration is per-thread and does not propagate. Unbounded retention can retain peak memory; consider an application-owned long-lived container or a suitable budget for large workloads. Borrow and return on the same thread exactly once. Do not copy/dispose leases twice, dispose a default lease or access returned references. The pool does not track misuse.

