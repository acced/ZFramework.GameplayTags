using System;

namespace GameplayTags
{
    /// <summary>
    /// Optional, caller-owned scratch for sort-free explicit intersection.
    /// Share sequentially between containers, never between concurrent operations.
    /// No container retains this workspace. It holds integers, not object references.
    /// Retained memory is approximately 4 * (registered tag count + selected capacity)
    /// bytes plus array headers. Repeated calls do not scan or clear the registry.
    /// </summary>
    public sealed class GameplayTagIntersectionWorkspace
    {
        internal int[] Positions = Array.Empty<int>();
        internal int[] SelectedIds = Array.Empty<int>();

        /// <summary>Create an empty workspace without initializing the registry.</summary>
        public GameplayTagIntersectionWorkspace() { }

        /// <summary>Reserve after registry initialization; capacity counts explicit tags.</summary>
        public GameplayTagIntersectionWorkspace(int explicitCapacity) => EnsureCapacity(explicitCapacity);

        public int ExplicitCapacity => SelectedIds.Length;
        public int RegisteredTagCapacity => Positions.Length == 0 ? 0 : Positions.Length - 1;

        /// <summary>
        /// Reserve scratch for the current frozen registry and expected selected tag count.
        /// Also prewarm the output container for its ancestor closure to avoid growth there.
        /// Capacity is nonnegative. This cold operation can initialize the registry.
        /// </summary>
        public void EnsureCapacity(int explicitCapacity)
        {
            EnsureSelectedCapacity(explicitCapacity);
            PrepareLookup();
        }

        internal void EnsureSelectedCapacity(int capacity)
        {
            if (SelectedIds.Length >= capacity)
                return;
            int size = Math.Max(8, SelectedIds.Length);
            while (size < capacity)
                size *= 2;
            Array.Resize(ref SelectedIds, size);
        }

        internal void PrepareLookup()
        {
            int needed = GameplayTagManager.TagCount + 1;
            if (Positions.Length < needed)
                Positions = new int[needed];
        }
    }
}
