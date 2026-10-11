using System;
using System.Collections.Generic;
using System.Linq;
using GameplayTags;

internal static class DeepProgram
{
    private const int Depth = 3000;
    private static string[] Names;
    private static GameplayTag[] Tags;
    private static GameplayTag Seed;
    private static long Checks;
    private static void Check(bool condition, string context) { Checks++; if (!condition) throw new Exception(context); }
    private static void Drain(GameplayTagContainer result, bool descending)
    {
        Check(result.ExplicitTagCount == Depth + 1 && result.TagCount == Depth * 2 + 1, "initial normalized cardinalities");
        var names = new HashSet<string>(); foreach (GameplayTag tag in result.GetExplicitTags()) Check(names.Add(tag.Name), "duplicate explicit name");
        Check(names.SetEquals(Names.Concat(new[] { Seed.Name })), "independent explicit name set");
        for (int i = 0; i < Depth; i++)
        {
            int removed = descending ? Depth - i - 1 : i;
            result.AddTag(Tags[removed]); result.RemoveTag(Tags[removed]); result.RemoveTag(Tags[removed]);
            int remaining = Depth - i - 1;
            int closure = remaining == 0 ? 0 : descending ? remaining : Depth;
            Check(result.ExplicitTagCount == remaining + 1 && result.TagCount == closure + Depth + 1, "chain suffix contribution counts");
            foreach (int probe in new[] { 0, Depth / 2, Depth - 1 })
            {
                bool exact = descending ? probe < remaining : probe > i;
                Check(result.HasTagExact(Tags[probe]) == exact && result.HasTag(Tags[probe]) == (probe < closure), "chain positions after removal");
            }
        }
        result.RemoveTag(Seed); Check(result.IsEmpty && result.TagCount == 0, "fully drained disjoint roots");
    }
    private static void Main()
    {
        Names = new string[Depth]; Names[0] = "Deep"; for (int i = 1; i < Depth; i++) Names[i] = Names[i - 1] + ".N";
        string seedName = "Seed" + string.Concat(Enumerable.Repeat(".N", Depth));
        GameplayTagManager.Initialize(new[] { new GameplayTagRegistration(Names[Depth - 1]), new GameplayTagRegistration(seedName) });
        Tags = Names.Select(GameplayTagManager.RequestTag).ToArray(); Seed = GameplayTagManager.RequestTag(seedName);
        foreach (int order in new[] { 0, 1, 2 })
        {
            int[] ids = Enumerable.Range(0, Depth).ToArray();
            if (order == 1) Array.Reverse(ids);
            if (order == 2) { var random = new Random(83177); for (int i = ids.Length - 1; i > 0; i--) { int j = random.Next(i + 1); int t = ids[i]; ids[i] = ids[j]; ids[j] = t; } }
            var source = new GameplayTagContainer(); var counted = new GameplayTagCountContainer();
            foreach (int i in ids) { source.AddTag(Tags[i]); counted.AddTag(Tags[i], 3); }
            var seed = new GameplayTagContainer(); seed.AddTag(Seed); seed.AddTag(Tags[0]);
            var countedSeed = new GameplayTagCountContainer(); countedSeed.AddTag(Seed, 5); countedSeed.AddTag(Tags[0], 7);
            foreach (bool descending in new[] { false, true })
            {
                Drain(GameplayTagContainer.Union(seed, source), descending);
                Drain(GameplayTagContainer.Union(seed, counted), descending);
                Drain(GameplayTagContainer.Union(counted, seed), descending);
                Drain(GameplayTagContainer.Union(countedSeed, counted), descending);
            }
            Check(source.ExplicitTagCount == Depth && source.TagCount == Depth, "ordinary source immutable");
            Check(seed.ExplicitTagCount == 2 && seed.TagCount == Depth + 2, "seed immutable");
            for (int i = 0; i < Depth; i++) Check(counted.GetExplicitTagCount(Tags[i]) == 3 && counted.GetTagCount(Tags[i]) == (Depth - i) * 3, "counted suffix model immutable");
            Check(countedSeed.GetExplicitTagCount(Seed) == 5 && countedSeed.GetExplicitTagCount(Tags[0]) == 7, "counted seed immutable");
        }
        Console.WriteLine("PASS deep Union: " + Checks + " independent depth/name assertions, 3000 explicit nodes, ascending/descending/shuffled source order, both drain orders, counted source/seed normalization and overlapping explicit root.");
    }
}
