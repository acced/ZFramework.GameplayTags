using System;
using System.Collections;
using System.Collections.Generic;

namespace GameplayTags
{
    /// <summary>
    /// A counted container whose explicit occurrences also contribute to its parent.
    /// Parent links must be acyclic. Mutations and reparenting preserve exact multiplicities.
    /// </summary>
    public class GameplayTagHierarchicalContainer : IGameplayTagCountContainer
    {
        private readonly GameplayTagCountContainer m_UnderlyingContainer = new GameplayTagCountContainer();
        private IGameplayTagCountContainer m_ParentContainer;
        private int[] m_Snapshot = Array.Empty<int>();

        public event OnTagCountChangedDelegate OnAnyTagCountChange
        {
            add => m_UnderlyingContainer.OnAnyTagCountChange += value;
            remove => m_UnderlyingContainer.OnAnyTagCountChange -= value;
        }

        public event OnTagCountChangedDelegate OnAnyTagNewOrRemove
        {
            add => m_UnderlyingContainer.OnAnyTagNewOrRemove += value;
            remove => m_UnderlyingContainer.OnAnyTagNewOrRemove -= value;
        }

        public bool IsEmpty => m_UnderlyingContainer.IsEmpty;
        public int ExplicitTagCount => m_UnderlyingContainer.ExplicitTagCount;
        public int TagCount => m_UnderlyingContainer.TagCount;
        public GameplayTagContainerIndices Indices => m_UnderlyingContainer.Indices;

        /// <summary>
        /// Changes where this container contributes its counts. Known built-in parent chains commit
        /// the full transfer before delivering callbacks. Custom parent implementations control their
        /// own callback timing.
        /// </summary>
        public IGameplayTagCountContainer Parent
        {
            get => m_ParentContainer;
            set
            {
                IGameplayTagCountContainer previous = m_ParentContainer;
                if (ReferenceEquals(previous, value))
                    return;

                if (!m_UnderlyingContainer.HasListeners && !ParentHasListeners(previous) && !ParentHasListeners(value))
                {
                    m_ParentContainer = value;
                    TransferTo(previous, false);
                    TransferTo(value, true);
                    return;
                }

                m_UnderlyingContainer.BeginBatch();
                BeginParentBatch(previous);
                BeginParentBatch(value);
                bool committed = false;
                try
                {
                    m_ParentContainer = value;
                    TransferTo(previous, false);
                    TransferTo(value, true);
                    committed = true;
                }
                finally
                {
                    m_UnderlyingContainer.EndBatchWithoutDispatch();
                    EndParentBatch(previous);
                    EndParentBatch(value);
                    if (!committed)
                    {
                        m_UnderlyingContainer.DiscardPendingEvents();
                        DiscardParentEvents(previous);
                        DiscardParentEvents(value);
                    }
                }

                bool dispatched = false;
                try
                {
                    m_UnderlyingContainer.FlushEvents();
                    FlushParentEvents(previous);
                    FlushParentEvents(value);
                    dispatched = true;
                }
                finally
                {
                    if (!dispatched)
                    {
                        m_UnderlyingContainer.DiscardPendingEvents();
                        DiscardParentEvents(previous);
                        DiscardParentEvents(value);
                    }
                }
            }
        }

        public void AddTag(GameplayTag tag) => AddTag(tag, 1);

        /// <summary>Adds a positive number of explicit occurrences to this container and its parent.</summary>
        public void AddTag(GameplayTag tag, int amount)
        {
            if (!HasListenersInChain())
            {
                AddTagInternal(tag.RuntimeIndex, amount);
                return;
            }

            BeginBatch();
            bool committed = false;
            try
            {
                AddTagInternal(tag.RuntimeIndex, amount);
                committed = true;
            }
            finally
            {
                EndBatchWithoutDispatch();
                if (!committed)
                    DiscardPendingEvents();
            }

            FlushEvents();
        }

        /// <summary>Adds one occurrence of every distinct explicit tag in the source.</summary>
        public void AddTags<T>(in T other) where T : IGameplayTagContainer
        {
            int count = SnapshotExplicitTags(other.Indices.Storage);
            if (!HasListenersInChain())
            {
                for (int i = 0; i < count; i++)
                    AddTagInternal(m_Snapshot[i], 1);
                return;
            }

            BeginBatch();
            bool committed = false;
            try
            {
                for (int i = 0; i < count; i++)
                    AddTagInternal(m_Snapshot[i], 1);
                committed = true;
            }
            finally
            {
                EndBatchWithoutDispatch();
                if (!committed)
                    DiscardPendingEvents();
            }

            FlushEvents();
        }

        public void RemoveTag(GameplayTag tag) => RemoveTag(tag, 1);

        /// <summary>
        /// Removes a positive number of explicit occurrences. The amount must not exceed the local
        /// explicit count. A tag absent from this container does not change the parent.
        /// </summary>
        public void RemoveTag(GameplayTag tag, int amount)
        {
            if (!HasListenersInChain())
            {
                RemoveTagInternal(tag.RuntimeIndex, amount);
                return;
            }

            BeginBatch();
            bool committed = false;
            try
            {
                RemoveTagInternal(tag.RuntimeIndex, amount);
                committed = true;
            }
            finally
            {
                EndBatchWithoutDispatch();
                if (!committed)
                    DiscardPendingEvents();
            }

            FlushEvents();
        }

        /// <summary>Removes one occurrence of every distinct explicit tag in the source.</summary>
        public void RemoveTags<T>(in T other) where T : IGameplayTagContainer
        {
            int count = SnapshotExplicitTags(other.Indices.Storage);
            if (!HasListenersInChain())
            {
                for (int i = 0; i < count; i++)
                    RemoveTagInternal(m_Snapshot[i], 1);
                return;
            }

            BeginBatch();
            bool committed = false;
            try
            {
                for (int i = 0; i < count; i++)
                    RemoveTagInternal(m_Snapshot[i], 1);
                committed = true;
            }
            finally
            {
                EndBatchWithoutDispatch();
                if (!committed)
                    DiscardPendingEvents();
            }

            FlushEvents();
        }

        public void Clear()
        {
            if (!HasListenersInChain())
            {
                TransferTo(m_ParentContainer, false);
                m_UnderlyingContainer.Clear();
                return;
            }

            BeginBatch();
            bool committed = false;
            try
            {
                TransferTo(m_ParentContainer, false);
                m_UnderlyingContainer.Clear();
                committed = true;
            }
            finally
            {
                EndBatchWithoutDispatch();
                if (!committed)
                    DiscardPendingEvents();
            }

            FlushEvents();
        }

        private int SnapshotExplicitTags(TagStorage source)
        {
            if (source == null)
                return 0;

            int count = source.ExplicitCount;
            if (m_Snapshot.Length < count)
                Array.Resize(ref m_Snapshot, Math.Max(8, count));
            for (int i = 0; i < count; i++)
                m_Snapshot[i] = source.Entries[source.ExplicitIndices[i]].Id;
            return count;
        }

        private void AddTagInternal(int id, int amount)
        {
            m_UnderlyingContainer.AddTagDeferred(id, amount);
            AddToParent(m_ParentContainer, id, amount);
        }

        private void RemoveTagInternal(int id, int amount)
        {
            if (m_UnderlyingContainer.RemoveTagDeferred(id, amount))
                RemoveFromParent(m_ParentContainer, id, amount);
        }

        private void TransferTo(IGameplayTagCountContainer parent, bool add)
        {
            if (parent == null)
                return;

            TagStorage storage = Indices.Storage;
            for (int i = 0; i < storage.ExplicitCount; i++)
            {
                TagStorage.Entry entry = storage.Entries[storage.ExplicitIndices[i]];
                if (add)
                    AddToParent(parent, entry.Id, entry.ExplicitCount);
                else
                    RemoveFromParent(parent, entry.Id, entry.ExplicitCount);
            }
        }

        private static void AddToParent(IGameplayTagCountContainer parent, int id, int amount)
        {
            if (parent is GameplayTagCountContainer counted)
                counted.AddTagDeferred(id, amount);
            else if (parent is GameplayTagHierarchicalContainer hierarchical)
                hierarchical.AddTagInternal(id, amount);
            else if (parent != null)
            {
                GameplayTag tag = GameplayTagManager.GetTagFromRuntimeIndex(id);
                for (int i = 0; i < amount; i++)
                    parent.AddTag(tag);
            }
        }

        private static void RemoveFromParent(IGameplayTagCountContainer parent, int id, int amount)
        {
            if (parent is GameplayTagCountContainer counted)
                counted.RemoveTagDeferred(id, amount);
            else if (parent is GameplayTagHierarchicalContainer hierarchical)
                hierarchical.RemoveTagInternal(id, amount);
            else if (parent != null)
            {
                GameplayTag tag = GameplayTagManager.GetTagFromRuntimeIndex(id);
                for (int i = 0; i < amount; i++)
                    parent.RemoveTag(tag);
            }
        }

        private bool HasListenersInChain() => m_UnderlyingContainer.HasListeners || ParentHasListeners(m_ParentContainer);

        private static bool ParentHasListeners(IGameplayTagCountContainer parent)
        {
            if (parent is GameplayTagCountContainer counted)
                return counted.HasListeners;
            if (parent is GameplayTagHierarchicalContainer hierarchical)
                return hierarchical.HasListenersInChain();
            // A custom parent owns its subscription state and notification policy.
            return parent != null;
        }

        private void BeginBatch()
        {
            m_UnderlyingContainer.BeginBatch();
            BeginParentBatch(m_ParentContainer);
        }

        private static void BeginParentBatch(IGameplayTagCountContainer parent)
        {
            if (parent is GameplayTagCountContainer counted)
                counted.BeginBatch();
            else if (parent is GameplayTagHierarchicalContainer hierarchical)
                hierarchical.BeginBatch();
        }

        private void EndBatchWithoutDispatch()
        {
            m_UnderlyingContainer.EndBatchWithoutDispatch();
            EndParentBatch(m_ParentContainer);
        }

        private static void EndParentBatch(IGameplayTagCountContainer parent)
        {
            if (parent is GameplayTagCountContainer counted)
                counted.EndBatchWithoutDispatch();
            else if (parent is GameplayTagHierarchicalContainer hierarchical)
                hierarchical.EndBatchWithoutDispatch();
        }

        private void FlushEvents()
        {
            bool dispatched = false;
            try
            {
                FlushEventsUnchecked();
                dispatched = true;
            }
            finally
            {
                if (!dispatched)
                    DiscardPendingEvents();
            }
        }

        private void FlushEventsUnchecked()
        {
            IGameplayTagCountContainer parent = m_ParentContainer;
            m_UnderlyingContainer.FlushEvents();
            FlushParentEvents(parent);
        }

        private static void FlushParentEvents(IGameplayTagCountContainer parent)
        {
            if (parent is GameplayTagCountContainer counted)
                counted.FlushEvents();
            else if (parent is GameplayTagHierarchicalContainer hierarchical)
                hierarchical.FlushEventsUnchecked();
        }

        private void DiscardPendingEvents()
        {
            m_UnderlyingContainer.DiscardPendingEvents();
            DiscardParentEvents(m_ParentContainer);
        }

        private static void DiscardParentEvents(IGameplayTagCountContainer parent)
        {
            if (parent is GameplayTagCountContainer counted)
                counted.DiscardPendingEvents();
            else if (parent is GameplayTagHierarchicalContainer hierarchical)
                hierarchical.DiscardPendingEvents();
        }

        public int GetTagCount(GameplayTag tag) => m_UnderlyingContainer.GetTagCount(tag);
        public int GetExplicitTagCount(GameplayTag tag) => m_UnderlyingContainer.GetExplicitTagCount(tag);
        public GameplayTagEnumerator GetTags() => m_UnderlyingContainer.GetTags();
        public GameplayTagEnumerator GetExplicitTags() => m_UnderlyingContainer.GetExplicitTags();

        public void GetParentTags(GameplayTag tag, List<GameplayTag> tags) =>
            m_UnderlyingContainer.GetParentTags(tag, tags);

        public void GetChildTags(GameplayTag tag, List<GameplayTag> tags) =>
            m_UnderlyingContainer.GetChildTags(tag, tags);

        public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> tags) =>
            m_UnderlyingContainer.GetExplicitParentTags(tag, tags);

        public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> tags) =>
            m_UnderlyingContainer.GetExplicitChildTags(tag, tags);

        public void RegisterTagEventCallback(GameplayTag tag, GameplayTagEventType eventType,
            OnTagCountChangedDelegate callback) =>
            m_UnderlyingContainer.RegisterTagEventCallback(tag, eventType, callback);

        public void RemoveTagEventCallback(GameplayTag tag, GameplayTagEventType eventType,
            OnTagCountChangedDelegate callback) =>
            m_UnderlyingContainer.RemoveTagEventCallback(tag, eventType, callback);

        public void RemoveAllTagEventCallbacks() => m_UnderlyingContainer.RemoveAllTagEventCallbacks();
        public GameplayTagEnumerator GetEnumerator() => GetTags();
        IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
