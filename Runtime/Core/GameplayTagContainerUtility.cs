using System.Collections.Generic;

namespace GameplayTags
{
    public static class GameplayTagContainerUtility
    {
        /// <summary>Every requirement is present in either holder. No temporary container is allocated.</summary>
        public static bool HasAll<T, U, V>(T containerA, in U containerB, in V other)
            where T : IGameplayTagContainer where U : IGameplayTagContainer where V : IGameplayTagContainer
        {
            if (other is null)
                return true;
            foreach (GameplayTag tag in other.GetExplicitTags())
                if (!containerA.HasTag(tag) && !containerB.HasTag(tag))
                    return false;
            return true;
        }

        public static bool HasAllExact<T, U, V>(T containerA, in U containerB, in V other)
            where T : IGameplayTagContainer where U : IGameplayTagContainer where V : IGameplayTagContainer
        {
            if (other is null)
                return true;
            foreach (GameplayTag tag in other.GetExplicitTags())
                if (!containerA.HasTagExact(tag) && !containerB.HasTagExact(tag))
                    return false;
            return true;
        }

        // Append nearest parent first. Siblings in the container cannot hide an ancestor.
        internal static void GetParentTags(TagStorage storage, GameplayTag tag,
            List<GameplayTag> output, bool explicitOnly = false)
        {
            if (storage == null)
                return;
            var hierarchy = GameplayTagManager.GetHierarchyIndices(tag.RuntimeIndex);
            for (int i = hierarchy.Length - 2; i >= 0; i--)
            {
                int id = hierarchy[i];
                if (explicitOnly ? storage.ContainsExplicit(id) : storage.Contains(id))
                    output.Add(GameplayTagManager.GetTagFromRuntimeIndex(id));
            }
        }

        // Append strict descendants. Scan the smaller of the occupied container and subtree.
        internal static void GetChildTags(TagStorage storage, GameplayTag tag,
            List<GameplayTag> output, bool explicitOnly = false)
        {
            if (storage == null)
                return;
            int first = tag.RuntimeIndex + 1;
            int end = GameplayTagManager.GetSubtreeEnd(tag.RuntimeIndex);
            int count = explicitOnly ? storage.ExplicitCount : storage.Count;
            if (end - first < count)
            {
                for (int id = first; id < end; id++)
                    if (explicitOnly ? storage.ContainsExplicit(id) : storage.Contains(id))
                        output.Add(GameplayTagManager.GetTagFromRuntimeIndex(id));
                return;
            }
            uint length = (uint)(end - first);
            for (int i = 0; i < count; i++)
            {
                TagStorage.Entry entry = storage.Entries[explicitOnly ? storage.ExplicitIndices[i] : i];
                if ((uint)(entry.Id - first) < length)
                    output.Add(GameplayTagManager.GetTagFromRuntimeIndex(entry.Id));
            }
        }
    }
}
