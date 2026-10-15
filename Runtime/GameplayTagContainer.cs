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
        public static GameplayTagContainer Empty { get; } = new GameplayTagContainer(true);
        [NonSerialized] private readonly bool m_ReadOnly;
        [NonSerialized] internal long PoolGeneration;
        [NonSerialized] internal bool PoolReturned;
        [NonSerialized] internal int PoolThread;

        private GameplayTagContainer(bool readOnly) { m_ReadOnly = readOnly; }
        internal void RequireWritable()
        {
            if (m_ReadOnly) throw new InvalidOperationException("The shared Empty container is read-only.");
        }
        private TagStorage Storage
        {
            get
            {
                if (!m_Indices.IsCreated) RestoreSerializedNames();
                return m_Indices.Storage;
            }
        }
        private void RestoreSerializedNames()
        {
            var restored = new GameplayTagContainer();
            if (m_SerializedExplicitTags != null)
                for (int i = 0; i < m_SerializedExplicitTags.Count; i++)
                    if (GameplayTagManager.RequestTag(m_SerializedExplicitTags[i], out var tag)) restored.AddTag(tag);
            m_Indices = restored.m_Indices;
        }
        internal static TagStorage GetStorage<T>(in T value) where T : IGameplayTagContainer =>
            value is null ? null : value.Indices.Storage;

        [NonSerialized] private GameplayTagContainerIndices m_Indices = GameplayTagContainerIndices.Create();
        // Force a name-based object contract. Otherwise DataContractSerializer
        // recognizes IEnumerable + Add as a collection and restores implicit
        // ancestors as explicit tags, changing the meaning of the container.
        [DataMember(Order = 0)] public List<string> m_SerializedExplicitTags;

        public bool IsEmpty => Storage.ExplicitCount == 0;
        public int ExplicitTagCount => Storage.ExplicitCount;
        public int TagCount => Storage.Count;
        public GameplayTagContainerIndices Indices { get { _ = Storage; return m_Indices; } }

        public GameplayTagContainer() { }
        /// <param name="capacity">Distinct tag capacity, including ancestors.</param>
        public GameplayTagContainer(int capacity) => Storage.EnsureCapacity(capacity);
        public GameplayTagContainer(IGameplayTagContainer other) => Copy(this, other);

        /// <summary>Reserve distinct tag capacity, including ancestors.</summary>
        public void EnsureCapacity(int capacity) { RequireWritable(); Storage.EnsureCapacity(capacity); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasTag(GameplayTag tag) => Storage.Contains(tag.RuntimeIndex);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasTagExact(GameplayTag tag) => Storage.ContainsExplicit(tag.RuntimeIndex);

        public GameplayTagContainer Clone()
        {
            var clone = new GameplayTagContainer();
            clone.Storage.CopyFrom(Storage);
            return clone;
        }

        public static void Copy<T>(GameplayTagContainer dest, in T src) where T : IGameplayTagContainer
        {
            if (dest == null) throw new ArgumentNullException(nameof(dest));
            dest.RequireWritable();
            TagStorage source = GetStorage(src);
            if (source == null) { dest.Clear(); return; }
            if (ReferenceEquals(dest.Storage, source))
                return;
            if (src is GameplayTagContainer)
            {
                dest.Storage.CopyFrom(source);
                dest.m_SerializedExplicitTags?.Clear();
                return;
            }
            dest.CopyExplicitStorage(source);
        }

        public void AddTag(GameplayTag tag) => TryAddTag(tag);

        public bool TryAddTag(GameplayTag tag)
        {
            RequireWritable();
            int id = tag.RuntimeIndex;
            if (id == 0) return false;
            TagStorage storage = Storage;
            if (!storage.TryAddExplicit(id))
                return false;
            ReadOnlySpan<int> hierarchy = GameplayTagManager.GetHierarchyIndices(id);
            for (int i = 0; i < hierarchy.Length - 1; i++)
                storage.AddTotal(hierarchy[i]);
            return true;
        }

        public void RemoveTag(GameplayTag tag) => TryRemoveTag(tag);

        public bool TryRemoveTag(GameplayTag tag)
        {
            RequireWritable();
            TagStorage storage = Storage;
            if (storage.RemoveExplicit(tag.RuntimeIndex) < 0)
                return false;
            ReadOnlySpan<int> hierarchy = GameplayTagManager.GetHierarchyIndices(tag.RuntimeIndex);
            for (int i = 0; i < hierarchy.Length; i++)
                storage.RemoveTotal(hierarchy[i]);
            return true;
        }

        public void AddTags<T>(in T other) where T : IGameplayTagContainer
        {
            RequireWritable();
            TagStorage source = GetStorage(other);
            if (source == null) return;
            if (ReferenceEquals(source, Storage))
                return;
            foreach (GameplayTag tag in other.GetExplicitTags())
                AddTag(tag);
        }

        public void RemoveTags<T>(in T other) where T : IGameplayTagContainer
        {
            RequireWritable();
            if (other is null) return;
            if (ReferenceEquals(other.Indices.Storage, Storage))
            {
                Clear();
                return;
            }
            foreach (GameplayTag tag in other.GetExplicitTags())
                RemoveTag(tag);
        }

        public void Clear()
        {
            RequireWritable();
            // Clearing a pending deserialize must not initialize the registry.
            if (!m_Indices.IsCreated) m_Indices = GameplayTagContainerIndices.Create();
            m_Indices.Storage.Clear();
            m_SerializedExplicitTags?.Clear();
        }

        /// <summary>Intersection of explicit sets; ancestors are derived from the result.</summary>
        public static GameplayTagContainer Intersection<T, U>(in T lhs, in U rhs)
            where T : IGameplayTagContainer where U : IGameplayTagContainer
        {
            var result = new GameplayTagContainer();
            result.SetIntersection(GetStorage(lhs), GetStorage(rhs),
                lhs is GameplayTagContainer, rhs is GameplayTagContainer);
            return result;
        }

        /// <summary>Replace output with the explicit intersection. Output may alias either input.</summary>
        public static void Intersection<T, U>(GameplayTagContainer output, in T lhs, in U rhs)
            where T : IGameplayTagContainer where U : IGameplayTagContainer
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.SetIntersection(GetStorage(lhs), GetStorage(rhs),
                lhs is GameplayTagContainer, rhs is GameplayTagContainer);
        }

        private void SetIntersection(TagStorage a, TagStorage b, bool aIsSet, bool bIsSet)
        {
            RequireWritable();
            if (a == null || b == null || a.ExplicitCount == 0 || b.ExplicitCount == 0)
            {
                Clear();
                return;
            }
            if (a.ExplicitCount > b.ExplicitCount ||
                (a.ExplicitCount == b.ExplicitCount && a.Count > b.Count))
            {
                TagStorage temporary = a;
                a = b;
                b = temporary;
                aIsSet = bIsSet;
            }
            TagStorage output = Storage;
            if (ReferenceEquals(a, b))
            {
                if (aIsSet)
                    output.CopyFrom(a);
                else
                    CopyExplicitStorage(a);
                m_SerializedExplicitTags?.Clear();
                return;
            }

            int matches = 0;
            // A few missing tags are cheaper to remove from a block copy than
            // to rebuild the common closure. Capture them before touching an
            // output that may also be the lookup operand.
            Span<int> missingIds = stackalloc int[8];
            int missing = 0;
            long missingPathWork = 0;
            for (int i = 0; i < a.ExplicitCount; i++)
            {
                int id = a.Entries[a.ExplicitIndices[i]].Id;
                if (b.ContainsExplicit(id))
                    matches++;
                else if (missing < missingIds.Length)
                {
                    missingIds[missing++] = id;
                    missingPathWork += GameplayTagManager.GetHierarchyIndices(id).Length;
                }
            }
            if (matches == 0)
            {
                Clear();
                return;
            }
            if (aIsSet && a.ExplicitCount - matches <= missingIds.Length &&
                (missing == 0 || missingPathWork <= a.Count / 2))
            {
                output.CopyFrom(a);
                for (int i = 0; i < missing; i++)
                    RemoveTag(GameplayTagManager.GetTagFromRuntimeIndex(missingIds[i]));
                m_SerializedExplicitTags?.Clear();
                return;
            }

            output.EnsureExplicitCapacity(matches);
            int selected = 0;
            for (int i = 0; i < a.ExplicitCount; i++)
            {
                int id = a.Entries[a.ExplicitIndices[i]].Id;
                if (b.ContainsExplicit(id))
                    output.ExplicitIndices[selected++] = id;
            }
            output.BuildFromExplicitIds(selected);
            m_SerializedExplicitTags?.Clear();
        }

        public void IntersectWith<T>(in T other) where T : IGameplayTagContainer
        {
            SetIntersection(Storage, GetStorage(other), true, other is GameplayTagContainer);
        }

        public static GameplayTagContainer Union<T, U>(in T lhs, in U rhs)
            where T : IGameplayTagContainer where U : IGameplayTagContainer
        {
            var result = new GameplayTagContainer();
            TagStorage a = GetStorage(lhs), b = GetStorage(rhs);
            bool aIsSet = lhs is GameplayTagContainer, bIsSet = rhs is GameplayTagContainer;
            // Prefer a normal seed: a counted input contributes existence only.
            // Otherwise copying the larger closure leaves less work to append.
            if (a == null || (b != null && ((!aIsSet && bIsSet) ||
                (aIsSet == bIsSet && a.Count < b.Count))))
            {
                TagStorage temporary = a;
                a = b;
                b = temporary;
                aIsSet = bIsSet;
            }
            if (a == null || a.ExplicitCount == 0)
            {
                result.CopyExplicitStorage(b);
                return result;
            }
            if (aIsSet)
                result.Storage.CopyFrom(a);
            else
                result.CopyExplicitStorage(a);
            if (b != null && b.ExplicitCount != 0 && !ReferenceEquals(a, b))
                result.Storage.AddUnion(b);
            return result;
        }

        private void CopyExplicitStorage(TagStorage source)
        {
            if (source == null || source.ExplicitCount == 0)
            {
                Clear();
                return;
            }
            TagStorage output = Storage;
            int count = source.ExplicitCount;
            output.EnsureExplicitCapacity(count);
            for (int i = 0; i < count; i++)
                output.ExplicitIndices[i] = source.Entries[source.ExplicitIndices[i]].Id;
            output.BuildFromExplicitIds(count);
            m_SerializedExplicitTags?.Clear();
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

        public GameplayTagEnumerator GetTags() => new GameplayTagEnumerator(Storage);
        public GameplayTagEnumerator GetExplicitTags() => new GameplayTagEnumerator(Storage, true);
        public GameplayTagEnumerator GetEnumerator() => GetTags();
        IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator() => GetTags();
        IEnumerator IEnumerable.GetEnumerator() => GetTags();

        public void GetParentTags(GameplayTag tag, List<GameplayTag> output) =>
            GameplayTagContainerUtility.GetParentTags(Storage, tag, output);
        public void GetChildTags(GameplayTag tag, List<GameplayTag> output) =>
            GameplayTagContainerUtility.GetChildTags(Storage, tag, output);
        public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> output) =>
            GameplayTagContainerUtility.GetParentTags(Storage, tag, output, true);
        public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> output) =>
            GameplayTagContainerUtility.GetChildTags(Storage, tag, output, true);

        // Names are a cold serialization boundary. Runtime state never stores strings.
        public void OnBeforeSerialize()
        {
            if (!m_Indices.IsCreated) return; // Preserve pending names, no registry access.
            m_SerializedExplicitTags ??= new List<string>(ExplicitTagCount);
            m_SerializedExplicitTags.Clear();
            foreach (GameplayTag tag in GetExplicitTags())
                m_SerializedExplicitTags.Add(tag.Name);
            m_SerializedExplicitTags.Sort(StringComparer.Ordinal);
        }

        public void OnAfterDeserialize()
        {
            RequireWritable();
            // Managed callbacks only mark pending state. Bind on first normal access,
            // after the application's explicit registry initialization opportunity.
            if (m_Indices.IsCreated) m_Indices.Storage.Changed();
            m_Indices = default;
        }

        [OnSerializing]
        private void OnSerializing(StreamingContext context) => OnBeforeSerialize();
        [OnDeserialized]
        private void OnDeserialized(StreamingContext context) => OnAfterDeserialize();

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void Add(GameplayTag tag) => AddTag(tag);
    }
}
