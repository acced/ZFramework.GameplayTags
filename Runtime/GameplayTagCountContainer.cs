using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace GameplayTags
{
    public delegate void OnTagCountChangedDelegate(GameplayTag gameplayTag, int newCount);

    public enum GameplayTagEventType
    {
        NewOrRemoved,
        AnyCountChange
    }

    internal readonly struct DeferredTagChangedDelegate
    {
        internal readonly int TagId;
        internal readonly int Count;
        internal readonly OnTagCountChangedDelegate Callback;

        internal DeferredTagChangedDelegate(int tagId, int count, OnTagCountChangedDelegate callback)
        {
            TagId = tagId;
            Count = count;
            Callback = callback;
        }

        internal void Execute() => Callback(GameplayTagManager.GetTagFromRuntimeIndex(TagId), Count);
    }

    internal struct GameplayTagDelegateInfo
    {
        internal OnTagCountChangedDelegate OnAnyChange;
        internal OnTagCountChangedDelegate OnNewOrRemove;
    }

    public interface IGameplayTagCountContainer : IGameplayTagContainer
    {
        event OnTagCountChangedDelegate OnAnyTagCountChange;
        event OnTagCountChangedDelegate OnAnyTagNewOrRemove;
        int GetExplicitTagCount(GameplayTag tag);
        int GetTagCount(GameplayTag tag);
        void RegisterTagEventCallback(GameplayTag tag, GameplayTagEventType eventType,
            OnTagCountChangedDelegate callback);
        void RemoveTagEventCallback(GameplayTag tag, GameplayTagEventType eventType,
            OnTagCountChangedDelegate callback);
        void RemoveAllTagEventCallbacks();
    }

    /// <summary>
    /// A counted tag set. Each explicit occurrence contributes one count to the tag and its ancestors.
    /// Mutations require registered, non-None tags. Instances are intended for single-threaded use.
    /// </summary>
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    [DebuggerTypeProxy(typeof(GameplayTagContainerDebugView))]
    public class GameplayTagCountContainer : IGameplayTagCountContainer
    {
        private struct CallbackEntry
        {
            internal int Id;
            internal GameplayTagDelegateInfo Callbacks;
        }

        private readonly GameplayTagContainerIndices m_Indices = GameplayTagContainerIndices.Create();
        private CallbackEntry[] m_Callbacks = Array.Empty<CallbackEntry>();
        private int m_CallbackCount;
        private const int IndexedCallbackThreshold = 16;
        private int[] m_CallbackSlots;
        private Dictionary<int, int> m_CallbackLookup;
        private DeferredTagChangedDelegate[] m_Events = Array.Empty<DeferredTagChangedDelegate>();
        private int m_EventCount;
        private int m_NextEvent;
        private int m_BatchDepth;
        private bool m_Dispatching;
        private int[] m_Snapshot = Array.Empty<int>();

        private TagStorage Storage => m_Indices.Storage;
        internal bool HasListeners => m_CallbackCount != 0 || OnAnyTagNewOrRemove != null ||
                                     OnAnyTagCountChange != null;

        public bool IsEmpty => Storage.ExplicitCount == 0;
        public int ExplicitTagCount => Storage.ExplicitCount;
        public int TagCount => Storage.Count;
        public GameplayTagContainerIndices Indices => m_Indices;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private string DebuggerDisplay => $"Count (Explicit, Total) = ({ExplicitTagCount}, {TagCount})";

        public event OnTagCountChangedDelegate OnAnyTagNewOrRemove;
        public event OnTagCountChangedDelegate OnAnyTagCountChange;

        public GameplayTagCountContainer()
        {
        }

        /// <param name="capacity">Initial capacity for distinct tags including ancestors.</param>
        public GameplayTagCountContainer(int capacity)
        {
            Storage.EnsureCapacity(capacity);
        }

        public void EnsureCapacity(int capacity) => Storage.EnsureCapacity(capacity);
        public int GetTagCount(GameplayTag tag) => Storage.GetCount(tag.RuntimeIndex);
        public int GetExplicitTagCount(GameplayTag tag) => Storage.GetExplicitCount(tag.RuntimeIndex);
        public GameplayTagEnumerator GetExplicitTags() => new GameplayTagEnumerator(Storage, true);
        public GameplayTagEnumerator GetTags() => new GameplayTagEnumerator(Storage);

        public void GetParentTags(GameplayTag tag, List<GameplayTag> parentTags) =>
            GameplayTagContainerUtility.GetParentTags(Storage, tag, parentTags);

        public void GetChildTags(GameplayTag tag, List<GameplayTag> childTags) =>
            GameplayTagContainerUtility.GetChildTags(Storage, tag, childTags);

        public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> parentTags) =>
            GameplayTagContainerUtility.GetParentTags(Storage, tag, parentTags, true);

        public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> childTags) =>
            GameplayTagContainerUtility.GetChildTags(Storage, tag, childTags, true);

        public void RegisterTagEventCallback(GameplayTag tag, GameplayTagEventType eventType,
            OnTagCountChangedDelegate callback)
        {
            int index = FindCallback(tag.RuntimeIndex);
            if (index < 0)
            {
                index = ~index;
                if (m_CallbackCount == m_Callbacks.Length)
                    Array.Resize(ref m_Callbacks, m_CallbackCount == 0 ? 4 : m_CallbackCount * 2);

                Array.Copy(m_Callbacks, index, m_Callbacks, index + 1, m_CallbackCount - index);
                m_Callbacks[index] = new CallbackEntry { Id = tag.RuntimeIndex };
                m_CallbackCount++;
                UpdateCallbackLookup(index);
            }

            ref GameplayTagDelegateInfo callbacks = ref m_Callbacks[index].Callbacks;
            if (eventType == GameplayTagEventType.NewOrRemoved)
                callbacks.OnNewOrRemove += callback;
            else
                callbacks.OnAnyChange += callback;
        }

        public void RemoveTagEventCallback(GameplayTag tag, GameplayTagEventType eventType,
            OnTagCountChangedDelegate callback)
        {
            int index = FindCallback(tag.RuntimeIndex);
            if (index < 0)
                return;

            ref GameplayTagDelegateInfo callbacks = ref m_Callbacks[index].Callbacks;
            if (eventType == GameplayTagEventType.NewOrRemoved)
                callbacks.OnNewOrRemove -= callback;
            else
                callbacks.OnAnyChange -= callback;

            if (callbacks.OnNewOrRemove == null && callbacks.OnAnyChange == null)
            {
                m_CallbackCount--;
                Array.Copy(m_Callbacks, index + 1, m_Callbacks, index, m_CallbackCount - index);
                m_Callbacks[m_CallbackCount] = default;
                UpdateCallbackLookup(index, tag.RuntimeIndex);
            }
        }

        public void RemoveAllTagEventCallbacks()
        {
            Array.Clear(m_Callbacks, 0, m_CallbackCount);
            m_CallbackCount = 0;
            m_CallbackLookup?.Clear();
            if (m_CallbackSlots != null) Array.Clear(m_CallbackSlots, 0, m_CallbackSlots.Length);
        }

        private void UpdateCallbackLookup(int firstChanged, int removedId = 0)
        {
            if (m_CallbackCount < IndexedCallbackThreshold) return;
            int tags = GameplayTagManager.TagCount;
            if (tags <= 512)
            {
                if (m_CallbackSlots == null || m_CallbackCount == IndexedCallbackThreshold)
                {
                    if (m_CallbackSlots == null) m_CallbackSlots = new int[tags + 1];
                    else Array.Clear(m_CallbackSlots, 0, m_CallbackSlots.Length);
                    firstChanged = 0;
                }
                else if (removedId != 0) m_CallbackSlots[removedId] = 0;
                for (int i = firstChanged; i < m_CallbackCount; i++) m_CallbackSlots[m_Callbacks[i].Id] = i + 1;
            }
            else
            {
                if (m_CallbackLookup == null || m_CallbackCount == IndexedCallbackThreshold)
                {
                    if (m_CallbackLookup == null) m_CallbackLookup = new Dictionary<int, int>(m_CallbackCount);
                    else m_CallbackLookup.Clear();
                    firstChanged = 0;
                }
                else if (removedId != 0) m_CallbackLookup.Remove(removedId);
                for (int i = firstChanged; i < m_CallbackCount; i++) m_CallbackLookup[m_Callbacks[i].Id] = i;
            }
        }

        private int LookupCallback(int id)
        {
            if (m_CallbackCount == 0) return -1;
            if (m_CallbackCount < IndexedCallbackThreshold) return FindCallback(id);
            if (m_CallbackSlots != null) return m_CallbackSlots[id] - 1;
            return m_CallbackLookup.TryGetValue(id, out int index) ? index : -1;
        }

        private int FindCallback(int id)
        {
            int low = 0;
            int high = m_CallbackCount - 1;
            while (low <= high)
            {
                int middle = (low + high) >> 1;
                int candidate = m_Callbacks[middle].Id;
                if (candidate == id)
                    return middle;
                if (candidate < id)
                    low = middle + 1;
                else
                    high = middle - 1;
            }

            return ~low;
        }

        public void AddTag(GameplayTag tag) => AddTag(tag, 1);

        /// <summary>Adds a positive number of explicit occurrences in one update.</summary>
        public void AddTag(GameplayTag tag, int amount)
        {
            bool notify = HasListeners;
            AddTagInternal(tag.RuntimeIndex, amount, notify);
            if (notify)
                FlushEvents();
        }

        /// <summary>Adds one occurrence of each distinct explicit tag in the source.</summary>
        public void AddTags<T>(in T other) where T : IGameplayTagContainer
        {
            TagStorage source = other.Indices.Storage;
            if (source == null)
                return;

            bool notify = HasListeners;
            int count = source.ExplicitCount;
            for (int i = 0; i < count; i++)
            {
                int id = source.Entries[source.ExplicitIndices[i]].Id;
                AddTagInternal(id, 1, notify);
            }

            if (notify)
                FlushEvents();
        }

        internal void AddTagDeferred(int id, int amount) => AddTagInternal(id, amount, HasListeners);

        private void AddTagInternal(int id, int amount, bool notify)
        {
            int ownCount = Storage.AddExplicitAndTotal(id, amount);
            ReadOnlySpan<int> hierarchy = GameplayTagManager.GetHierarchyIndices(id);
            if (!notify)
            {
                for (int i = 0; i < hierarchy.Length - 1; i++)
                    Storage.AddTotal(hierarchy[i], amount);
                return;
            }

            for (int i = 0; i < hierarchy.Length - 1; i++)
            {
                int current = hierarchy[i];
                int newCount = Storage.AddTotal(current, amount);
                QueueChange(current, newCount, newCount == amount);
            }
            QueueChange(id, ownCount, ownCount == amount);
        }

        public void RemoveTag(GameplayTag tag) => RemoveTag(tag, 1);

        /// <summary>
        /// Removes a positive number of explicit occurrences. The amount must not exceed the current
        /// explicit count. A tag with no explicit occurrences is ignored.
        /// </summary>
        public void RemoveTag(GameplayTag tag, int amount)
        {
            bool notify = HasListeners;
            RemoveTagInternal(tag.RuntimeIndex, amount, notify);
            if (notify)
                FlushEvents();
        }

        /// <summary>Removes one occurrence of each distinct explicit tag in the source.</summary>
        public void RemoveTags<T>(in T other) where T : IGameplayTagContainer
        {
            TagStorage source = other.Indices.Storage;
            if (source == null)
                return;

            bool notify = HasListeners;
            if (!notify && source.ExplicitCount > 8)
            {
                Storage.RemoveSet(source);
                return;
            }
            if (ReferenceEquals(source, Storage))
            {
                int count = source.ExplicitCount;
                if (m_Snapshot.Length < count)
                    Array.Resize(ref m_Snapshot, Math.Max(8, count));
                for (int i = 0; i < count; i++)
                    m_Snapshot[i] = source.Entries[source.ExplicitIndices[i]].Id;

                for (int i = 0; i < count; i++)
                    RemoveTagInternal(m_Snapshot[i], 1, notify);
            }
            else
            {
                for (int i = 0; i < source.ExplicitCount; i++)
                {
                    int id = source.Entries[source.ExplicitIndices[i]].Id;
                    RemoveTagInternal(id, 1, notify);
                }
            }

            if (notify)
                FlushEvents();
        }

        internal bool RemoveTagDeferred(int id, int amount) => RemoveTagInternal(id, amount, HasListeners);

        private bool RemoveTagInternal(int id, int amount, bool notify)
        {
            int ownCount = Storage.RemoveExplicitAndTotal(id, amount);
            if (ownCount < 0)
                return false;

            ReadOnlySpan<int> hierarchy = GameplayTagManager.GetHierarchyIndices(id);
            if (!notify)
            {
                for (int i = 0; i < hierarchy.Length - 1; i++)
                    Storage.RemoveTotal(hierarchy[i], amount);
                return true;
            }

            for (int i = 0; i < hierarchy.Length - 1; i++)
            {
                int current = hierarchy[i];
                int newCount = Storage.RemoveTotal(current, amount);
                QueueChange(current, newCount, newCount == 0);
            }
            QueueChange(id, ownCount, ownCount == 0);

            return true;
        }

        public void Clear()
        {
            bool notify = HasListeners;
            if (notify)
            {
                for (int i = 0; i < Storage.Count; i++)
                    QueueChange(Storage.Entries[i].Id, 0, true);
            }

            Storage.Clear();
            if (notify)
                FlushEvents();
        }

        private void QueueChange(int id, int count, bool transition)
        {
            int index = LookupCallback(id);
            GameplayTagDelegateInfo callbacks = index < 0 ? default : m_Callbacks[index].Callbacks;
            if (transition)
            {
                if (callbacks.OnNewOrRemove != null)
                    Enqueue(id, count, callbacks.OnNewOrRemove);
                if (OnAnyTagNewOrRemove != null)
                    Enqueue(id, count, OnAnyTagNewOrRemove);
            }

            if (callbacks.OnAnyChange != null)
                Enqueue(id, count, callbacks.OnAnyChange);
            if (OnAnyTagCountChange != null)
                Enqueue(id, count, OnAnyTagCountChange);
        }

        private void Enqueue(int id, int count, OnTagCountChangedDelegate callback)
        {
            if (m_EventCount == m_Events.Length)
                Array.Resize(ref m_Events, m_EventCount == 0 ? 8 : m_EventCount * 2);
            m_Events[m_EventCount++] = new DeferredTagChangedDelegate(id, count, callback);
        }

        internal void BeginBatch() => m_BatchDepth++;
        internal void EndBatchWithoutDispatch() => m_BatchDepth--;

        /// <summary>
        /// Reentrant changes append to this queue. A thrown callback leaves committed state intact,
        /// releases pending callback references, and permits the next mutation to dispatch normally.
        /// </summary>
        internal void FlushEvents()
        {
            if (m_BatchDepth != 0 || m_Dispatching || m_EventCount == 0)
                return;

            m_Dispatching = true;
            try
            {
                while (m_NextEvent < m_EventCount)
                {
                    DeferredTagChangedDelegate notification = m_Events[m_NextEvent];
                    m_Events[m_NextEvent++] = default;
                    notification.Execute();
                }
            }
            finally
            {
                Array.Clear(m_Events, m_NextEvent, m_EventCount - m_NextEvent);
                m_EventCount = 0;
                m_NextEvent = 0;
                m_Dispatching = false;
            }
        }

        internal void DiscardPendingEvents()
        {
            // An active dispatcher owns its own exception cleanup and may have unrelated queued work.
            if (m_Dispatching)
                return;

            Array.Clear(m_Events, 0, m_EventCount);
            m_EventCount = 0;
            m_NextEvent = 0;
        }

        public GameplayTagEnumerator GetEnumerator() => GetTags();
        IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
