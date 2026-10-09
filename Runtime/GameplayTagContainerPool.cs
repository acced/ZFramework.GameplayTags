using System;

namespace GameplayTags
{
    /// <summary>A small thread-local pool. Release each lease exactly once, on the borrowing thread.</summary>
    public class GameplayTagContainerPool
    {
        private const int RetainedLimit = 64;
        [ThreadStatic] private static GameplayTagContainer[] s_Items;
        [ThreadStatic] private static int s_Count;

        public readonly struct PooledContainer : IDisposable
        {
            public GameplayTagContainer Container { get; }
            public PooledContainer(GameplayTagContainer container) => Container = container;
            public void Dispose() => Release(Container);
        }

        public static GameplayTagContainer Get()
        {
            if (s_Count == 0)
                return new GameplayTagContainer();
            GameplayTagContainer container = s_Items[--s_Count];
            s_Items[s_Count] = null;
            return container;
        }

        public static void Release(GameplayTagContainer container)
        {
            container.Clear();
            if (s_Count == RetainedLimit)
                return;
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
