using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace GameplayTags
{
    /// <summary>A live view of a container. Copies share storage; this is not a snapshot.</summary>
    public struct GameplayTagContainerIndices
    {
        internal TagStorage Storage { get; private set; }
        public readonly bool IsCreated => Storage != null;
        public readonly bool IsEmpty => ExplicitTagCount == 0;
        public readonly int TagCount => Storage == null ? 0 : Storage.Count;
        public readonly int ExplicitTagCount => Storage == null ? 0 : Storage.ExplicitCount;

        public static GameplayTagContainerIndices Create() =>
            new GameplayTagContainerIndices { Storage = new TagStorage() };

        public static void Create(ref GameplayTagContainerIndices indices)
        {
            if (indices.Storage == null)
                indices = Create();
        }

        internal readonly void Clear() => Storage?.Clear();
        internal readonly void CopyTo(in GameplayTagContainerIndices other) => other.Storage.CopyFrom(Storage);
    }

    public interface IGameplayTagContainer : IEnumerable<GameplayTag>
    {
        bool IsEmpty { get; }
        int ExplicitTagCount { get; }
        int TagCount { get; }
        GameplayTagContainerIndices Indices { get; }
        void AddTag(GameplayTag tag);
        void RemoveTag(GameplayTag tag);
        GameplayTagEnumerator GetTags();
        GameplayTagEnumerator GetExplicitTags();
        void AddTags<T>(in T other) where T : IGameplayTagContainer;
        void RemoveTags<T>(in T other) where T : IGameplayTagContainer;
        void GetParentTags(GameplayTag tag, List<GameplayTag> output);
        void GetChildTags(GameplayTag tag, List<GameplayTag> output);
        void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> output);
        void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> output);
        void Clear();
    }

    /// <summary>
    /// A set of explicit tags with reference-counted ancestor closure. Mutations require
    /// registered, non-None tags. Enumeration is unordered and invalidated by mutation.
    /// </summary>
    [Serializable]
    [DataContract]
    [DebuggerTypeProxy(typeof(GameplayTagContainerDebugView))]
    [DebuggerDisplay("Explicit = {ExplicitTagCount}, Total = {TagCount}")]
    public class GameplayTagContainer : IGameplayTagContainer
#if UNITY_5_3_OR_NEWER
        , UnityEngine.ISerializationCallbackReceiver
#endif
    {
        /// <summary>Shared empty query operand. Treat as read-only.</summary>
        public static GameplayTagContainer Empty { get; } = new GameplayTagContainer();

        [NonSerialized] private GameplayTagContainerIndices m_Indices = GameplayTagContainerIndices.Create();
        // Force a name-based object contract. Otherwise DataContractSerializer
        // recognizes IEnumerable + Add as a collection and restores implicit
        // ancestors as explicit tags, changing the meaning of the container.
        [DataMember(Order = 0)] public List<string> m_SerializedExplicitTags;

        public bool IsEmpty => m_Indices.Storage.ExplicitCount == 0;
        public int ExplicitTagCount => m_Indices.Storage.ExplicitCount;
        public int TagCount => m_Indices.Storage.Count;
        public GameplayTagContainerIndices Indices => m_Indices;

        public GameplayTagContainer() { }
        /// <param name="capacity">Distinct tag capacity, including ancestors.</param>
        public GameplayTagContainer(int capacity) => m_Indices.Storage.EnsureCapacity(capacity);
        public GameplayTagContainer(IGameplayTagContainer other) => Copy(this, other);

        /// <summary>Reserve distinct tag capacity, including ancestors.</summary>
        public void EnsureCapacity(int capacity) => m_Indices.Storage.EnsureCapacity(capacity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasTag(GameplayTag tag) => m_Indices.Storage.Contains(tag.RuntimeIndex);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasTagExact(GameplayTag tag) => m_Indices.Storage.ContainsExplicit(tag.RuntimeIndex);

        public GameplayTagContainer Clone()
        {
            var clone = new GameplayTagContainer();
            clone.m_Indices.Storage.CopyFrom(m_Indices.Storage);
            return clone;
        }

        public static void Copy<T>(GameplayTagContainer dest, in T src) where T : IGameplayTagContainer
        {
            TagStorage source = src.Indices.Storage;
            if (ReferenceEquals(dest.m_Indices.Storage, source))
                return;
            if (src is GameplayTagContainer)
            {
                dest.m_Indices.Storage.CopyFrom(source);
                dest.m_SerializedExplicitTags?.Clear();
                return;
            }
            dest.Clear();
            dest.AddTags(src);
        }

        public void AddTag(GameplayTag tag) => TryAddTag(tag);

        public bool TryAddTag(GameplayTag tag)
        {
            TagStorage storage = m_Indices.Storage;
            if (!storage.TryAddExplicit(tag.RuntimeIndex))
                return false;
            ReadOnlySpan<int> hierarchy = GameplayTagManager.GetHierarchyIndices(tag.RuntimeIndex);
            for (int i = 0; i < hierarchy.Length; i++)
                storage.AddTotal(hierarchy[i]);
            return true;
        }

        public void RemoveTag(GameplayTag tag) => TryRemoveTag(tag);

        public bool TryRemoveTag(GameplayTag tag)
        {
            TagStorage storage = m_Indices.Storage;
            if (storage.RemoveExplicit(tag.RuntimeIndex) < 0)
                return false;
            ReadOnlySpan<int> hierarchy = GameplayTagManager.GetHierarchyIndices(tag.RuntimeIndex);
            for (int i = 0; i < hierarchy.Length; i++)
                storage.RemoveTotal(hierarchy[i]);
            return true;
        }

        public void AddTags<T>(in T other) where T : IGameplayTagContainer
        {
            TagStorage source = other.Indices.Storage;
            if (ReferenceEquals(source, m_Indices.Storage))
                return;
            foreach (GameplayTag tag in other.GetExplicitTags())
                AddTag(tag);
        }

        public void RemoveTags<T>(in T other) where T : IGameplayTagContainer
        {
            if (ReferenceEquals(other.Indices.Storage, m_Indices.Storage))
            {
                Clear();
                return;
            }
            foreach (GameplayTag tag in other.GetExplicitTags())
                RemoveTag(tag);
        }

        public void Clear()
        {
            m_Indices.Storage.Clear();
            m_SerializedExplicitTags?.Clear();
        }

        /// <summary>Intersection of explicit sets; ancestors are derived from the result.</summary>
        public static GameplayTagContainer Intersection<T, U>(in T lhs, in U rhs)
            where T : IGameplayTagContainer where U : IGameplayTagContainer
        {
            var result = new GameplayTagContainer();
            result.AddIntersection(lhs, rhs);
            return result;
        }

        /// <summary>Replace output with the explicit intersection. Output may alias either input.</summary>
        public static void Intersection<T, U>(GameplayTagContainer output, in T lhs, in U rhs)
            where T : IGameplayTagContainer where U : IGameplayTagContainer
        {
            if (ReferenceEquals(output.m_Indices.Storage, lhs.Indices.Storage))
            {
                output.IntersectWith(rhs);
                return;
            }
            if (ReferenceEquals(output.m_Indices.Storage, rhs.Indices.Storage))
            {
                output.IntersectWith(lhs);
                return;
            }
            output.Clear();
            output.AddIntersection(lhs, rhs);
        }

        internal void AddIntersection<T, U>(in T lhs, in U rhs)
            where T : IGameplayTagContainer where U : IGameplayTagContainer
        {
            TagStorage a = lhs.Indices.Storage, b = rhs.Indices.Storage;
            if (a == null || b == null)
                return;
            if (a.ExplicitCount > b.ExplicitCount)
            {
                TagStorage temporary = a;
                a = b;
                b = temporary;
            }
            for (int i = 0; i < a.ExplicitCount; i++)
            {
                TagStorage.Entry entry = a.Entries[a.ExplicitIndices[i]];
                if (b.ContainsExplicit(entry.Id))
                    AddTag(GameplayTagManager.GetTagFromRuntimeIndex(entry.Id));
            }
        }

        public void IntersectWith<T>(in T other) where T : IGameplayTagContainer
        {
            TagStorage storage = m_Indices.Storage, source = other.Indices.Storage;
            if (ReferenceEquals(storage, source))
                return;
            if (source == null || source.ExplicitCount == 0)
            {
                Clear();
                return;
            }
            // The explicit index is maintained independently from ancestor closure.
            for (int i = storage.ExplicitCount - 1; i >= 0; i--)
            {
                TagStorage.Entry entry = storage.Entries[storage.ExplicitIndices[i]];
                if (!source.ContainsExplicit(entry.Id))
                    RemoveTag(GameplayTagManager.GetTagFromRuntimeIndex(entry.Id));
            }
        }

        public static GameplayTagContainer Union<T, U>(in T lhs, in U rhs)
            where T : IGameplayTagContainer where U : IGameplayTagContainer
        {
            var result = new GameplayTagContainer(lhs);
            result.AddTags(rhs);
            return result;
        }

        /// <summary>Append this-minus-other to added, and other-minus-this to removed.</summary>
        public void GetDiffExplicitTags<T>(T other, List<GameplayTag> added, List<GameplayTag> removed)
            where T : IGameplayTagContainer
        {
            foreach (GameplayTag tag in GetExplicitTags())
                if (!other.HasTagExact(tag))
                    added.Add(tag);
            foreach (GameplayTag tag in other.GetExplicitTags())
                if (!HasTagExact(tag))
                    removed.Add(tag);
        }

        public GameplayTagEnumerator GetTags() => new GameplayTagEnumerator(m_Indices.Storage);
        public GameplayTagEnumerator GetExplicitTags() => new GameplayTagEnumerator(m_Indices.Storage, true);
        public GameplayTagEnumerator GetEnumerator() => GetTags();
        IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator() => GetTags();
        IEnumerator IEnumerable.GetEnumerator() => GetTags();

        public void GetParentTags(GameplayTag tag, List<GameplayTag> output) =>
            GameplayTagContainerUtility.GetParentTags(m_Indices.Storage, tag, output);
        public void GetChildTags(GameplayTag tag, List<GameplayTag> output) =>
            GameplayTagContainerUtility.GetChildTags(m_Indices.Storage, tag, output);
        public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> output) =>
            GameplayTagContainerUtility.GetParentTags(m_Indices.Storage, tag, output, true);
        public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> output) =>
            GameplayTagContainerUtility.GetChildTags(m_Indices.Storage, tag, output, true);

        // Names are a cold serialization boundary. Runtime state never stores strings.
        public void OnBeforeSerialize()
        {
            m_SerializedExplicitTags ??= new List<string>(ExplicitTagCount);
            m_SerializedExplicitTags.Clear();
            foreach (GameplayTag tag in GetExplicitTags())
                m_SerializedExplicitTags.Add(tag.Name);
            m_SerializedExplicitTags.Sort(StringComparer.Ordinal);
        }

        public void OnAfterDeserialize()
        {
            m_Indices = GameplayTagContainerIndices.Create();
            if (m_SerializedExplicitTags == null)
                return;
            for (int i = 0; i < m_SerializedExplicitTags.Count; i++)
                if (GameplayTagManager.RequestTag(m_SerializedExplicitTags[i], out GameplayTag tag))
                    AddTag(tag);
        }

        [OnSerializing]
        private void OnSerializing(StreamingContext context) => OnBeforeSerialize();
        [OnDeserialized]
        private void OnDeserialized(StreamingContext context) => OnAfterDeserialize();

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void Add(GameplayTag tag) => AddTag(tag);
    }
}
