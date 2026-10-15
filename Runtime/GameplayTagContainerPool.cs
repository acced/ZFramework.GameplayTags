using System;
using System.Threading;

namespace GameplayTags
{
    /// <summary>Thread-local pool. Copied leases are idempotent and cannot return a later borrow.</summary>
    public class GameplayTagContainerPool
    {
        private const int RetainedLimit = 64;
        [ThreadStatic] private static GameplayTagContainer[] s_Items;
        [ThreadStatic] private static int s_Count;

        public readonly struct PooledContainer : IDisposable
        {
            private readonly GameplayTagContainer m_Container;
            private readonly long m_Generation;
            public GameplayTagContainer Container
            {
                get
                {
                    if (m_Container == null || m_Container.PoolReturned || m_Container.PoolGeneration != m_Generation)
                        throw new ObjectDisposedException(nameof(PooledContainer));
                    return m_Container;
                }
            }
            public PooledContainer(GameplayTagContainer container)
            {
                if (container == null) throw new ArgumentNullException(nameof(container));
                container.RequireWritable();
                if (container.PoolReturned) throw new ObjectDisposedException(nameof(container));
                if (container.PoolThread == 0) container.PoolThread = Thread.CurrentThread.ManagedThreadId;
                m_Container = container;
                m_Generation = container.PoolGeneration;
            }
            public void Dispose()
            {
                if (m_Container == null || m_Container.PoolReturned || m_Container.PoolGeneration != m_Generation) return;
                Release(m_Container);
            }
        }

        public static GameplayTagContainer Get()
        {
            GameplayTagContainer container;
            if (s_Count == 0) container = new GameplayTagContainer();
            else
            {
                container = s_Items[--s_Count];
                s_Items[s_Count] = null;
            }
            container.PoolGeneration = checked(container.PoolGeneration + 1);
            container.PoolReturned = false;
            container.PoolThread = Thread.CurrentThread.ManagedThreadId;
            return container;
        }

        public static void Release(GameplayTagContainer container)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            container.RequireWritable();
            if (container.PoolReturned) throw new InvalidOperationException("Container already returned to pool.");
            int thread = Thread.CurrentThread.ManagedThreadId;
            if (container.PoolThread != 0 && container.PoolThread != thread)
                throw new InvalidOperationException("Return the container on its borrowing thread.");
            container.Clear();
            container.PoolThread = thread;
            container.PoolReturned = true;
            if (s_Count == RetainedLimit) return;
            s_Items ??= new GameplayTagContainer[RetainedLimit];
            s_Items[s_Count++] = container;
        }

        public static PooledContainer Get(out GameplayTagContainer container)
        {
            container = Get();
            return new PooledContainer(container);
        }
    }
}
