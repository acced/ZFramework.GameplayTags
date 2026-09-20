using System;
using GameplayTags;

// Authoring containers intentionally provide pattern-based iteration, not a boxed IEnumerable hot path.
internal static class IntegerTestSupport
{
    internal static bool Any(this GameplayTagContainer tags, Func<GameplayTag, bool> predicate)
    { foreach (GameplayTag tag in tags) if (predicate(tag)) return true; return false; }
    internal static bool All(this GameplayTagContainer tags, Func<GameplayTag, bool> predicate)
    { foreach (GameplayTag tag in tags) if (!predicate(tag)) return false; return true; }
}
