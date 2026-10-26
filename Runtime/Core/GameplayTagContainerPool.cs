using System;

namespace GameplayTags
{
    /// <summary>
    /// Thread-local container reuse. Configure retention at a loading boundary.
    /// The caller owns same-thread, exactly-once return and exclusive ownership.
    /// </summary>
    public class GameplayTagContainerPool
    {
        /// <summary>Array-payload budgets; object/array headers are not counted.</summary>
        public readonly struct RetentionPolicy
        {
            public readonly int MaxCount;
            public readonly long MaxContainerBytes;
            public readonly long MaxTotalBytes;

            public RetentionPolicy(int maxCount, long maxContainerBytes = long.MaxValue,
                long maxTotalBytes = long.MaxValue)
            {
                MaxCount = maxCount;
                MaxContainerBytes = maxContainerBytes;
                MaxTotalBytes = maxTotalBytes;
            }

            public static RetentionPolicy Unbounded => new RetentionPolicy(int.MaxValue);
            public static RetentionPolicy Disabled => default;
        }

        [ThreadStatic] private static GameplayTagContainer[] s_Items;
        [ThreadStatic] private static int s_Count;
        [ThreadStatic] private static long s_RetainedBytes;
        [ThreadStatic] private static RetentionPolicy? s_Policy;

        /// <summary>Unset threads use unbounded retention, not a hidden fixed byte budget.</summary>
        public static RetentionPolicy Policy => s_Policy ?? RetentionPolicy.Unbounded;
        public static long RetainedDataBytes => s_RetainedBytes;
        public static int RetainedCount => s_Count;

        /// <summary>
        /// Drop current cached objects and select this thread's policy. Limits must be
        /// nonnegative. Configure before borrowing; outstanding leases are caller-owned.
        /// </summary>
        public static void Configure(RetentionPolicy policy)
        {
            Clear();
            s_Policy = policy;
        }

        /// <summary>Release all retained references and the pool array; keep the policy.</summary>
        public static void Clear()
        {
            s_Items = null;
            s_Count = 0;
            s_RetainedBytes = 0;
        }

        public readonly struct PooledContainer : IDisposable
        {
            public GameplayTagContainer Container { get; }
            public PooledContainer(GameplayTagContainer container) => Container = container;
            public void Dispose() => Release(Container);
        }

        public static GameplayTagContainer Get()
        {
            if (s_Count == 0) return new GameplayTagContainer();
            GameplayTagContainer container = s_Items[--s_Count];
            s_Items[s_Count] = null;
            s_RetainedBytes -= container.RetainedDataBytes;
            return container;
        }

        public static void Release(GameplayTagContainer container)
        {
            RetentionPolicy policy = Policy;
            long bytes = container.RetainedDataBytes;
            // Retention decisions are the selected strategy, not input validation.
            // Ownership has ended even if the object is not retained.
            if (s_Count >= policy.MaxCount || bytes > policy.MaxContainerBytes ||
                bytes > policy.MaxTotalBytes - s_RetainedBytes) return;
            container.Clear();
            if (s_Items == null) s_Items = new GameplayTagContainer[Math.Min(16, policy.MaxCount)];
            else if (s_Count == s_Items.Length)
                Array.Resize(ref s_Items, (int)Math.Min((long)policy.MaxCount, (long)s_Count * 2));
            s_Items[s_Count++] = container;
            s_RetainedBytes += bytes;
        }

        public static PooledContainer Get(out GameplayTagContainer container)
        {
            container = Get();
            return new PooledContainer(container);
        }
    }
}
