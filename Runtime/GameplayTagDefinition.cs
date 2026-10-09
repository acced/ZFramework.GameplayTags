using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace GameplayTags
{
    /// <summary>
    /// Immutable cold metadata. Parent links and subtree ends also live in compact
    /// integer arrays so membership operations do not load strings or this struct.
    /// </summary>
    [DebuggerDisplay("{TagName,nq}")]
    internal readonly struct GameplayTagDefinition
    {
        internal GameplayTagDefinition(string name, string description, string label,
            GameplayTagFlags flags, int index, int parentIndex, int subtreeEnd,
            int depth, int hierarchyOffset)
        {
            TagName = name;
            Description = description;
            Label = label;
            Flags = flags;
            RuntimeIndex = index;
            ParentIndex = parentIndex;
            SubtreeEnd = subtreeEnd;
            HierarchyLevel = depth;
            HierarchyOffset = hierarchyOffset;
        }

        public GameplayTag Tag => new GameplayTag(TagName, RuntimeIndex);
        public string TagName { get; }
        public string Description { get; }
        public string Label { get; }
        public GameplayTagFlags Flags { get; }
        public int RuntimeIndex { get; }
        public int ParentIndex { get; }
        public int SubtreeEnd { get; }
        public int HierarchyLevel { get; }
        internal int HierarchyOffset { get; }

        /// <summary>Strict ancestors in root-to-parent order; excludes this tag.</summary>
        public ReadOnlySpan<GameplayTag> ParentTags => GameplayTagManager.GetHierarchyTags(
            HierarchyOffset, HierarchyLevel == 0 ? 0 : HierarchyLevel - 1);

        /// <summary>All strict descendants, in deterministic depth-first order.</summary>
        public ReadOnlySpan<GameplayTag> ChildTags => GameplayTagManager.GetTagRange(
            RuntimeIndex + 1, SubtreeEnd - RuntimeIndex - 1);

        /// <summary>Root-to-self path, including this tag.</summary>
        public ReadOnlySpan<GameplayTag> HierarchyTags => GameplayTagManager.GetHierarchyTags(
            HierarchyOffset, HierarchyLevel);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsChildOf(GameplayTag tag) => RuntimeIndex > tag.RuntimeIndex &&
            RuntimeIndex < GameplayTagManager.GetSubtreeEnd(tag.RuntimeIndex);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsParentOf(GameplayTag tag) => tag.RuntimeIndex > RuntimeIndex &&
            tag.RuntimeIndex < SubtreeEnd;

        internal static GameplayTagDefinition None => new GameplayTagDefinition(
            null, string.Empty, "None", GameplayTagFlags.None, 0, 0, 1, 0, 0);
    }
}
