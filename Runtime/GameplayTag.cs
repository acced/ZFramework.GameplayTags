using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace GameplayTags
{
    /// <summary>
    /// A cached tag handle. Equality and hierarchy operations use the runtime ID only.
    /// Names are serialized; runtime IDs are resolved against the initialized registry.
    /// </summary>
    [Serializable]
    [DataContract]
    [DebuggerDisplay("{m_Name,nq}")]
    public struct GameplayTag : IEquatable<GameplayTag>
#if UNITY_5_3_OR_NEWER
        , UnityEngine.ISerializationCallbackReceiver
#endif
    {
        public static readonly GameplayTag None = default;
        // Preconditions: a resolved handle from the current registry; no name edits
        // without assigning Resolve() at the caller's loading/editing boundary.
        public readonly int RuntimeIndex => m_RuntimeIndex;
        public readonly bool IsValid => m_RuntimeIndex != 0;
        public readonly GameplayTag Resolve()
        {
            int id = GameplayTagManager.ResolveIndex(m_Name);
            return id == 0 ? new GameplayTag(m_Name, 0) : GameplayTagManager.GetTagFromRuntimeIndex(id);
        }
        internal readonly ref readonly GameplayTagDefinition Definition => ref GameplayTagManager.GetDefinitionFromRuntimeIndex(RuntimeIndex);

        /// <summary>Strict ancestors in root-to-parent order, excluding this tag.</summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public readonly ReadOnlySpan<GameplayTag> ParentTags => Definition.ParentTags;

        /// <summary>All strict descendants, in depth-first order; excludes this tag.</summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public readonly ReadOnlySpan<GameplayTag> ChildTags => Definition.ChildTags;

        /// <summary>The complete root-to-self path, including this tag.</summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public readonly ReadOnlySpan<GameplayTag> HierarchyTags => Definition.HierarchyTags;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public readonly string Label => Definition.Label;
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public readonly int HierarchyLevel => Definition.HierarchyLevel;
        public readonly string Description => Definition.Description;
        public readonly string Name => m_Name;

        /// <summary>The immediate parent, or None for a root/None tag.</summary>
        public readonly GameplayTag ParentTag => GameplayTagManager.GetTagFromRuntimeIndex(
            GameplayTagManager.GetParentIndex(RuntimeIndex));

        [DataMember(Name = "m_Name", Order = 0)]
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        public string m_Name;

        [NonSerialized]
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private int m_RuntimeIndex;

        internal GameplayTag(string name, int runtimeTagIndex)
        {
            m_Name = name;
            m_RuntimeIndex = runtimeTagIndex;
        }

        /// <summary>True only for a strict descendant. A tag and None are never their own ancestors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool IsParentOf(in GameplayTag tag)
        {
            int parent = RuntimeIndex, child = tag.RuntimeIndex;
            return child > parent && child < GameplayTagManager.GetSubtreeEnd(parent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool IsChildOf(in GameplayTag parentTag) => parentTag.IsParentOf(this);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Equals(GameplayTag other) => RuntimeIndex == other.RuntimeIndex;

        public readonly override bool Equals(object obj) => obj is GameplayTag tag && RuntimeIndex == tag.RuntimeIndex;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly override int GetHashCode() => RuntimeIndex;

        public readonly override string ToString() => RuntimeIndex == 0 ? "<None>" : m_Name;

        // Runtime IDs may change when declarations change. Both serializers store names
        // and resolve IDs after deserialization, never restore a stale numeric identity.
        [OnSerializing]
        private void OnSerializing(StreamingContext context) => OnBeforeSerialize();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context) => OnAfterDeserialize();

        // Initialize the registry before deserialization. Binding belongs here, never in lookup.
        // Unity owners must assign Resolve() for standalone struct fields after loading.
        // Unknown names remain available to an explicit caller-side validation pass.
        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() => m_RuntimeIndex = GameplayTagManager.ResolveIndex(m_Name);

        public static implicit operator GameplayTag(string tagName) => GameplayTagManager.RequestTag(tagName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(in GameplayTag lhs, in GameplayTag rhs) => lhs.RuntimeIndex == rhs.RuntimeIndex;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(in GameplayTag lhs, in GameplayTag rhs) => lhs.RuntimeIndex != rhs.RuntimeIndex;
    }
}
