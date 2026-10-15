using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace GameplayTags
{
    /// <summary>
    /// A cached tag handle. Equality and hierarchy operations use the runtime ID only.
    /// The name remains present for source and Unity asset serialization compatibility.
    /// </summary>
    [Serializable]
    [DebuggerDisplay("{m_Name,nq}")]
    public struct GameplayTag : IEquatable<GameplayTag>
#if UNITY_5_3_OR_NEWER
        , UnityEngine.ISerializationCallbackReceiver
#endif
    {
        public static readonly GameplayTag None = default;
        public readonly int RuntimeIndex => m_RuntimeIndex;
        internal readonly ref readonly GameplayTagDefinition Definition => ref GameplayTagManager.GetDefinitionFromRuntimeIndex(m_RuntimeIndex);

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
        public readonly GameplayTagFlags Flags => Definition.Flags;
        public readonly string Name => m_Name;

        /// <summary>The immediate parent, or None for a root/None tag.</summary>
        public readonly GameplayTag ParentTag => GameplayTagManager.GetTagFromRuntimeIndex(
            GameplayTagManager.GetParentIndex(m_RuntimeIndex));

        // Retained field name and visibility keep existing Unity assets and property drawers compatible.
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
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
        public readonly bool IsParentOf(in GameplayTag tag) => tag.m_RuntimeIndex > m_RuntimeIndex &&
            tag.m_RuntimeIndex < GameplayTagManager.GetSubtreeEnd(m_RuntimeIndex);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool IsChildOf(in GameplayTag parentTag) => m_RuntimeIndex > parentTag.m_RuntimeIndex &&
            m_RuntimeIndex < GameplayTagManager.GetSubtreeEnd(parentTag.m_RuntimeIndex);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Equals(GameplayTag other) => m_RuntimeIndex == other.m_RuntimeIndex;

        public readonly override bool Equals(object obj) => obj is GameplayTag tag && m_RuntimeIndex == tag.m_RuntimeIndex;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly override int GetHashCode() => m_RuntimeIndex;

        public readonly override string ToString() => m_RuntimeIndex == 0 ? "<None>" : m_Name;

        // Runtime IDs may change when declarations change. Both serializers store names
        // and resolve IDs after deserialization, never restore a stale numeric identity.
        [OnSerializing]
        private void OnSerializing(StreamingContext context) => OnBeforeSerialize();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context) => OnAfterDeserialize();

        public void OnBeforeSerialize() => m_Name = m_RuntimeIndex == 0
            ? null
            : GameplayTagManager.GetTagFromRuntimeIndex(m_RuntimeIndex).m_Name;

        public void OnAfterDeserialize() => this = GameplayTagManager.RequestTag(m_Name);

        public static implicit operator GameplayTag(string tagName) => GameplayTagManager.RequestTag(tagName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(in GameplayTag lhs, in GameplayTag rhs) => lhs.m_RuntimeIndex == rhs.m_RuntimeIndex;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(in GameplayTag lhs, in GameplayTag rhs) => lhs.m_RuntimeIndex != rhs.m_RuntimeIndex;
    }
}
