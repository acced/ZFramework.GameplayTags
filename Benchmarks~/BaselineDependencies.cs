#if BASELINE
// The original checkout references these unprovided dependencies. These minimal
// compile shims do not alter its algorithms. Pools use a warmed Stack and return
// value-type leases; no fresh collection is allocated on a warmed Get/Release.
// Logging is disabled for benchmarks and valid mutation inputs avoid warnings.
using System;
using System.Collections.Generic;

namespace GameplayTags
{
    internal static class Log
    {
        internal static void Warn(string message) { }
        internal static void Warn(string format, object arg) { }
    }

    internal sealed class ObjectPool<T> where T : class
    {
        private readonly Stack<T> items = new Stack<T>();
        private readonly Func<T> create;
        private readonly Action<T> release;
        internal ObjectPool(Func<T> createFunc, Action<T> actionOnRelease = null)
        {
            create = createFunc;
            release = actionOnRelease;
        }
        internal T Get() { return items.Count == 0 ? create() : items.Pop(); }
        internal void Release(T item) { release?.Invoke(item); items.Push(item); }
    }

    internal static class GenericPool<T> where T : class, new()
    {
        private static readonly Stack<T> items = new Stack<T>();
        internal static Lease Get(out T item)
        {
            item = items.Count == 0 ? new T() : items.Pop();
            return new Lease(item);
        }
        internal readonly struct Lease : IDisposable
        {
            private readonly T item;
            internal Lease(T value) { item = value; }
            public void Dispose() { items.Push(item); }
        }
    }

    internal static class ListPool<T>
    {
        private static readonly Stack<List<T>> items = new Stack<List<T>>();
        internal static Lease Get(out List<T> item)
        {
            item = items.Count == 0 ? new List<T>() : items.Pop();
            return new Lease(item);
        }
        internal readonly struct Lease : IDisposable
        {
            private readonly List<T> item;
            internal Lease(List<T> value) { item = value; }
            public void Dispose() { item.Clear(); items.Push(item); }
        }
    }
}
#endif
