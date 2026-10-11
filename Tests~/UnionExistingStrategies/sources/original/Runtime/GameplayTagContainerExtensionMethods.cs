using System.Runtime.CompilerServices;

namespace GameplayTags
{
    public static class GameplayTagContainerExtensionMethods
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasTag<T>(this T container, GameplayTag tag) where T : IGameplayTagContainer
        {
            TagStorage storage = container.Indices.Storage;
            return storage != null && storage.Contains(tag.RuntimeIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasTagExact<T>(this T container, GameplayTag tag) where T : IGameplayTagContainer
        {
            TagStorage storage = container.Indices.Storage;
            return storage != null && storage.ContainsExplicit(tag.RuntimeIndex);
        }

        /// <summary>Any explicit query tag is present in the holder's ancestor closure. Null query means empty.</summary>
        public static bool HasAny<T, U>(this T container, in U other)
            where T : IGameplayTagContainer where U : IGameplayTagContainer =>
            other is null ? false : HasAnyInternal(container.Indices.Storage, other.Indices.Storage, false);

        public static bool HasAnyExact<T, U>(this T container, in U other)
            where T : IGameplayTagContainer where U : IGameplayTagContainer =>
            other is null ? false : HasAnyInternal(container.Indices.Storage, other.Indices.Storage, true);

        private static bool HasAnyInternal(TagStorage holder, TagStorage query, bool exact)
        {
            if (holder == null || query == null)
                return false;
            // Probe with the smaller relevant set; one explicit condition must not
            // require scanning its entire (possibly very deep) ancestor closure.
            int holderCount = exact ? holder.ExplicitCount : holder.Count;
            if (holderCount < query.ExplicitCount)
            {
                for (int i = 0; i < holderCount; i++)
                {
                    TagStorage.Entry entry = holder.Entries[exact ? holder.ExplicitIndices[i] : i];
                    if (query.ContainsExplicit(entry.Id))
                        return true;
                }
            }
            else
            {
                for (int i = 0; i < query.ExplicitCount; i++)
                {
                    TagStorage.Entry entry = query.Entries[query.ExplicitIndices[i]];
                    if (exact ? holder.ContainsExplicit(entry.Id) : holder.Contains(entry.Id))
                        return true;
                }
            }
            return false;
        }

        /// <summary>Every explicit query tag is present. An empty or null query is satisfied.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasAll<T, U>(this T container, in U other)
            where T : IGameplayTagContainer where U : IGameplayTagContainer =>
            other is null || HasAllInternal(container.Indices.Storage, other.Indices.Storage, false);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasAllExact<T, U>(this T container, in U other)
            where T : IGameplayTagContainer where U : IGameplayTagContainer =>
            other is null || HasAllInternal(container.Indices.Storage, other.Indices.Storage, true);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasAllInternal(TagStorage holder, TagStorage query, bool exact)
        {
            if (query == null || query.ExplicitCount == 0)
                return true;
            if (holder == null || query.ExplicitCount > (exact ? holder.ExplicitCount : holder.Count))
                return false;
            if (query.ExplicitCount == 1)
            {
                int id = query.Entries[query.ExplicitIndices[0]].Id;
                if (exact && holder.ExplicitCount == 1)
                    return holder.Entries[holder.ExplicitIndices[0]].Id == id;
                return exact ? holder.ContainsExplicit(id) : holder.Contains(id);
            }
            for (int i = 0; i < query.ExplicitCount; i++)
            {
                TagStorage.Entry entry = query.Entries[query.ExplicitIndices[i]];
                if (!(exact ? holder.ContainsExplicit(entry.Id) : holder.Contains(entry.Id)))
                    return false;
            }
            return true;
        }

        /// <summary>The holder satisfies both query sets, without constructing a temporary union.</summary>
        public static bool HasAll<T, U, V>(this T container, in U otherA, in V otherB)
            where T : IGameplayTagContainer where U : IGameplayTagContainer where V : IGameplayTagContainer =>
            container.HasAll(otherA) && container.HasAll(otherB);
    }
}
