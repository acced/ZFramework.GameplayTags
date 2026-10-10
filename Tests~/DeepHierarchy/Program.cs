using System;
using System.Collections.Generic;
using System.Linq;
using GameplayTags;

internal static class Program
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Main()
    {
        const int depth = 3000;
        string name = "Deep." + string.Join(".", Enumerable.Repeat("A", depth - 1));
        GameplayTagManager.Initialize(new GameplayTagRegistration(name), new GameplayTagRegistration("Other.Leaf"),
            new GameplayTagRegistration("Other.Peer"), new GameplayTagRegistration("Other.Third"));
        GameplayTag leaf = GameplayTagManager.RequestTag(name);
        GameplayTag root = GameplayTagManager.RequestTag("Deep");
        GameplayTag other = GameplayTagManager.RequestTag("Other.Leaf");
        GameplayTagContainer container = new GameplayTagContainer();
        container.AddTag(leaf);
        Require(container.ExplicitTagCount == 1 && container.TagCount == depth, "deep closure count");
        int explicitCount = 0;
        foreach (GameplayTag tag in container.GetExplicitTags()) { Require(tag == leaf, "only explicit leaf"); explicitCount++; }
        Require(explicitCount == 1, "single explicit enumeration");
        Require(container.HasTag(root) && container.HasTagExact(leaf) && !container.HasTagExact(root), "deep membership");
        Require(leaf.IsChildOf(root) && root.IsParentOf(leaf) && !root.IsParentOf(other), "deep intervals");
        GameplayTagContainer required = new GameplayTagContainer { leaf };
        Require(container.HasAllExact(required) && container.HasAnyExact(required), "deep single requirement");
        required.AddTag(other);
        Require(!container.HasAllExact(required) && container.HasAnyExact(required), "deep missing requirement");
        List<GameplayTag> parents = new List<GameplayTag>();
        container.GetParentTags(leaf, parents);
        Require(parents.Count == depth - 1, "all strict deep parents");
        GameplayTagContainer copy = container.Clone();
        copy.RemoveTag(leaf);
        Require(copy.IsEmpty && copy.TagCount == 0 && container.TagCount == depth, "deep copy and removal");
        container.RemoveTags(container);
        Require(container.IsEmpty && container.TagCount == 0, "deep alias removal");

        GameplayTagCountContainer counted = new GameplayTagCountContainer();
        counted.AddTag(leaf); counted.AddTag(leaf);
        Require(counted.GetTagCount(root) == 2 && counted.GetExplicitTagCount(leaf) == 2, "deep count propagation");
        counted.RemoveTag(leaf);
        Require(counted.GetTagCount(root) == 1 && counted.TagCount == depth, "deep shared contribution retained");
        counted.Clear();
        Require(counted.IsEmpty && counted.GetTagCount(root) == 0, "deep count clear");
        ValidateBulkOperations(name, leaf, other);
        Console.WriteLine("PASS 3000-level hierarchy: one explicit tag, relationships, queries, copy, aliases, counts, " +
            "count-to-set bulk normalization, tiny intersections and queued ancestor unions.");
    }

    private static HashSet<string> PrefixNames(string name)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < name.Length; i++)
            if (name[i] == '.')
                result.Add(name.Substring(0, i));
        result.Add(name);
        return result;
    }

    private static HashSet<string> Names(params string[] names) =>
        new HashSet<string>(names, StringComparer.Ordinal);

    private static void AssertSet(GameplayTagContainer actual, HashSet<string> explicitNames,
        HashSet<string> closure, GameplayTag[] queries, string context)
    {
        Require(actual.ExplicitTagCount == explicitNames.Count, context + " explicit cardinality");
        Require(actual.TagCount == closure.Count, context + " closure cardinality");
        Require(actual.IsEmpty == (explicitNames.Count == 0), context + " empty state");
        var seen = Names();
        foreach (GameplayTag tag in actual.GetExplicitTags())
            Require(seen.Add(tag.Name), context + " duplicate explicit enumeration");
        Require(seen.SetEquals(explicitNames), context + " explicit names");
        seen.Clear();
        foreach (GameplayTag tag in actual.GetTags())
            Require(seen.Add(tag.Name), context + " duplicate closure enumeration");
        Require(seen.SetEquals(closure), context + " closure names");
        foreach (GameplayTag tag in queries)
        {
            Require(actual.HasTagExact(tag) == explicitNames.Contains(tag.Name), context + " explicit lookup");
            Require(actual.HasTag(tag) == closure.Contains(tag.Name), context + " closure lookup");
        }
    }

    private static void CheckSingletonResult(GameplayTagContainer result, GameplayTag explicitTag,
        HashSet<string> closure, GameplayTag[] queries, string context)
    {
        AssertSet(result, Names(explicitTag.Name), closure, queries, context);
        result.AddTag(explicitTag);
        result.RemoveTag(explicitTag);
        // One removal must release every ancestor, even if the input carried
        // many occurrences and the output was built in a single bulk pass.
        AssertSet(result, Names(), Names(), queries, context + " one removal drains closure");
        result.AddTag(explicitTag);
        AssertSet(result, Names(explicitTag.Name), closure, queries, context + " reuse");
        result.RemoveTag(explicitTag);
        AssertSet(result, Names(), Names(), queries, context + " reused closure drains");
    }

    private static void ValidateBulkOperations(string deepName, GameplayTag leaf, GameplayTag other)
    {
        // Derive expected names directly from fixture strings, not from the
        // runtime's hierarchy arrays. Keep this large universe out of Bulk's
        // per-mutation random tests so their cost stays proportional to that fixture.
        HashSet<string> deepClosure = PrefixNames(deepName);
        HashSet<string> otherClosure = PrefixNames("Other.Leaf");
        HashSet<string> universe = new HashSet<string>(deepClosure, StringComparer.Ordinal);
        universe.UnionWith(otherClosure);
        universe.UnionWith(PrefixNames("Other.Peer"));
        universe.UnionWith(PrefixNames("Other.Third"));
        GameplayTag[] queries = universe.Select(GameplayTagManager.RequestTag).ToArray();

        var counted = new GameplayTagCountContainer();
        counted.AddTag(leaf, 7);
        var plain = new GameplayTagContainer { leaf };
        var empty = new GameplayTagContainer();
        var copied = new GameplayTagContainer();
        GameplayTagContainer.Copy(copied, counted);
        CheckSingletonResult(copied, leaf, deepClosure, queries, "deep counted copy to fresh set");
        copied.AddTag(other);
        GameplayTagContainer.Copy(copied, counted);
        CheckSingletonResult(copied, leaf, deepClosure, queries, "deep counted copy to reused set");
        CheckSingletonResult(GameplayTagContainer.Union(counted, empty), leaf, deepClosure, queries, "deep counted union empty");
        CheckSingletonResult(GameplayTagContainer.Union(empty, counted), leaf, deepClosure, queries, "deep empty union counted");
        CheckSingletonResult(GameplayTagContainer.Union(counted, plain), leaf, deepClosure, queries, "deep counted union set");
        CheckSingletonResult(GameplayTagContainer.Intersection(counted, counted), leaf, deepClosure, queries, "deep counted self intersection");
        CheckSingletonResult(GameplayTagContainer.Intersection(counted, plain), leaf, deepClosure, queries, "deep counted intersection set");
        CheckSingletonResult(GameplayTagContainer.Intersection(plain, counted), leaf, deepClosure, queries, "deep set intersection counted");
        GameplayTagContainer.Intersection(plain, counted, plain);
        CheckSingletonResult(plain, leaf, deepClosure, queries, "deep counted intersection aliases set output");
        foreach (GameplayTag tag in queries)
        {
            Require(counted.GetExplicitTagCount(tag) == (tag == leaf ? 7 : 0), "deep counted source explicit multiplicity preserved");
            Require(counted.GetTagCount(tag) == (deepClosure.Contains(tag.Name) ? 7 : 0), "deep counted source closure multiplicity preserved");
        }

        GameplayTag peer = GameplayTagManager.RequestTag("Other.Peer");
        GameplayTag third = GameplayTagManager.RequestTag("Other.Third");
        var deepAndShallow = new GameplayTagContainer { leaf, other };
        var shallowPair = new GameplayTagContainer { other, peer };
        var shallowTriple = new GameplayTagContainer { other, peer, third };
        CheckSingletonResult(GameplayTagContainer.Intersection(deepAndShallow, shallowPair), other,
            otherClosure, queries, "equal explicit sizes deep-shallow intersection");
        CheckSingletonResult(GameplayTagContainer.Intersection(shallowPair, deepAndShallow), other,
            otherClosure, queries, "reversed equal sizes deep-shallow intersection");
        CheckSingletonResult(GameplayTagContainer.Intersection(deepAndShallow, shallowTriple), other,
            otherClosure, queries, "smaller explicit input has deep closure");
        CheckSingletonResult(GameplayTagContainer.Intersection(shallowTriple, deepAndShallow), other,
            otherClosure, queries, "reversed smaller input has deep closure");
        GameplayTagContainer.Intersection(deepAndShallow, deepAndShallow, shallowTriple);
        CheckSingletonResult(deepAndShallow, other, otherClosure, queries, "deep-shallow intersection aliases large closure output");
        ValidateQueuedAncestorUnions(deepClosure, leaf);
    }

    private static void AssertChainSource(IGameplayTagContainer source, GameplayTag[] chain,
        int[] explicitCounts, string context)
    {
        var expectedNames = Names();
        int closureDepth = 0;
        for (int depth = 1; depth < explicitCounts.Length; depth++)
            if (explicitCounts[depth] != 0)
            {
                expectedNames.Add(chain[depth - 1].Name);
                closureDepth = depth;
            }
        Require(source.ExplicitTagCount == expectedNames.Count, context + " explicit cardinality");
        Require(source.TagCount == closureDepth, context + " closure cardinality");
        var seen = Names();
        foreach (GameplayTag tag in source.GetExplicitTags())
            Require(seen.Add(tag.Name), context + " duplicate explicit enumeration");
        Require(seen.SetEquals(expectedNames), context + " explicit names preserved");

        // A suffix sum is the reference count on a single fixture chain.
        // This is one O(depth) source check, independent of runtime metadata.
        IGameplayTagCountContainer counted = source as IGameplayTagCountContainer;
        int total = 0;
        for (int depth = chain.Length; depth >= 1; depth--)
        {
            total += explicitCounts[depth];
            GameplayTag tag = chain[depth - 1];
            Require(source.HasTag(tag) == (total != 0), context + " ancestor membership at " + depth);
            Require(source.HasTagExact(tag) == (explicitCounts[depth] != 0), context + " explicit membership at " + depth);
            if (counted != null)
            {
                Require(counted.GetExplicitTagCount(tag) == explicitCounts[depth], context + " explicit multiplicity at " + depth);
                Require(counted.GetTagCount(tag) == total, context + " total multiplicity at " + depth);
            }
        }
    }

    private static void AssertSparseChainResult(GameplayTagContainer result, GameplayTag[] chain,
        Dictionary<string, int> remaining, int[] probeDepths, string context)
    {
        int closureDepth = remaining.Count == 0 ? 0 : remaining.Values.Max();
        Require(result.ExplicitTagCount == remaining.Count, context + " explicit cardinality");
        Require(result.TagCount == closureDepth, context + " closure cardinality");
        Require(result.IsEmpty == (remaining.Count == 0), context + " empty state");
        var seen = Names();
        foreach (GameplayTag tag in result.GetExplicitTags())
        {
            Require(seen.Add(tag.Name), context + " duplicate explicit enumeration");
            Require(remaining.ContainsKey(tag.Name), context + " unexpected explicit tag");
        }
        Require(seen.Count == remaining.Count, context + " missing explicit tag");
        foreach (int depth in probeDepths)
        {
            GameplayTag tag = chain[depth - 1];
            Require(result.HasTag(tag) == (depth <= closureDepth), context + " ancestor membership at " + depth);
            Require(result.HasTagExact(tag) == remaining.ContainsKey(tag.Name), context + " explicit membership at " + depth);
        }
    }

    private static void DrainQueuedUnion(GameplayTagContainer result, GameplayTag[] chain,
        int[] ancestorOrder, string context)
    {
        int[] removeDepths = new[] { chain.Length }.Concat(ancestorOrder).ToArray();
        var remaining = removeDepths.ToDictionary(depth => chain[depth - 1].Name, depth => depth, StringComparer.Ordinal);
        AssertSparseChainResult(result, chain, remaining, removeDepths, context + " initial");
        foreach (int depth in removeDepths)
        {
            GameplayTag tag = chain[depth - 1];
            result.AddTag(tag); // A normalized ordinary set ignores duplicates.
            result.RemoveTag(tag);
            remaining.Remove(tag.Name);
            // Probe only the 15 explicit candidates after each deletion. The
            // fixture's deepest remaining name determines its complete closure.
            AssertSparseChainResult(result, chain, remaining, removeDepths, context + " removed " + depth);
        }
        foreach (GameplayTag tag in chain)
            Require(!result.HasTag(tag) && !result.HasTagExact(tag), context + " no contribution remains after full drain");
    }

    private static void ValidateQueuedAncestorUnions(HashSet<string> deepClosure, GameplayTag leaf)
    {
        // All fixture segments after Deep have equal length, so ordering these
        // existing prefix strings by length gives depth without registry metadata.
        GameplayTag[] chain = deepClosure.OrderBy(name => name.Length).Select(GameplayTagManager.RequestTag).ToArray();
        int[] ascending = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 1499, 1500, 2998, 2999 };
        int[][] orders =
        {
            ascending,
            ascending.Reverse().ToArray(),
            new[] { 2999, 1500, 10, 2998, 7, 1499, 9, 3, 1, 8, 2, 6, 4, 5 }
        };
        string[] orderNames = { "ascending", "descending", "mixed" };
        var plainSeedModel = new int[chain.Length + 1];
        var countedSeedModel = new int[chain.Length + 1];
        var plainAncestorModel = new int[chain.Length + 1];
        var countedAncestorModel = new int[chain.Length + 1];
        plainSeedModel[chain.Length] = 1;
        countedSeedModel[chain.Length] = 11;
        for (int i = 0; i < ascending.Length; i++)
        {
            plainAncestorModel[ascending[i]] = 1;
            countedAncestorModel[ascending[i]] = 2 + i % 7;
        }

        for (int orderIndex = 0; orderIndex < orders.Length; orderIndex++)
        {
            int[] order = orders[orderIndex];
            string context = "queued deep union " + orderNames[orderIndex];
            var seed = new GameplayTagContainer { leaf };
            var countedSeed = new GameplayTagCountContainer();
            countedSeed.AddTag(leaf, countedSeedModel[chain.Length]);
            var ancestors = new GameplayTagContainer();
            var countedAncestors = new GameplayTagCountContainer();
            foreach (int depth in order)
            {
                GameplayTag tag = chain[depth - 1];
                ancestors.AddTag(tag);
                countedAncestors.AddTag(tag, countedAncestorModel[depth]);
            }

            // The leaf seed has the larger closure. All 14 strict ancestors
            // must be appended, exceeding the small-input path's eight tags.
            // Descending/mixed input queues a direct parent (2998, 1499, 9)
            // before its later explicit insertion promotes that pending entry.
            DrainQueuedUnion(GameplayTagContainer.Union(seed, ancestors), chain, order, context + " set/set");
            DrainQueuedUnion(GameplayTagContainer.Union(ancestors, seed), chain, order, context + " reversed set/set");
            DrainQueuedUnion(GameplayTagContainer.Union(seed, countedAncestors), chain, order, context + " set/count");
            DrainQueuedUnion(GameplayTagContainer.Union(countedAncestors, seed), chain, order, context + " reversed count/set");
            DrainQueuedUnion(GameplayTagContainer.Union(countedSeed, countedAncestors), chain, order, context + " count/count");
            DrainQueuedUnion(GameplayTagContainer.Union(countedAncestors, countedSeed), chain, order, context + " reversed count/count");
            AssertChainSource(seed, chain, plainSeedModel, context + " set seed preserved");
            AssertChainSource(ancestors, chain, plainAncestorModel, context + " set ancestors preserved");
            AssertChainSource(countedSeed, chain, countedSeedModel, context + " counted seed preserved");
            AssertChainSource(countedAncestors, chain, countedAncestorModel, context + " counted ancestors preserved");
        }
    }
}
