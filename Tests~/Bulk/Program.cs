using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GameplayTags;

internal static class Program
{
    private static readonly Dictionary<string, GameplayTag> Tags = new Dictionary<string, GameplayTag>(StringComparer.Ordinal);
    private static readonly string[] Leaves = Enumerable.Range(0, 512)
        .Select(i => "R" + (i / 64) + ".B" + ((i / 8) % 8) + ".L" + (i % 8)).ToArray();
    private static readonly string DeepLeaf = "Deep." + string.Join(".", Enumerable.Range(1, 63).Select(i => "N" + i));
    private static string[] Names;
    private static long assertions;
    private static int failures;

    private static int Main()
    {
        string[] declarations = Leaves.Concat(new[]
        {
            "A.B.C", "A.B.D", "A.Z.Leaf", "A0.B.Leaf", "Case.Child", "case.Child", DeepLeaf
        }).ToArray();
        GameplayTagManager.Initialize(declarations.Select(name => new GameplayTagRegistration(name)).ToArray());
        // The universe and every expected hierarchy come from input strings, not
        // runtime IDs, definition arrays, container storage or registry traversal.
        Names = Closure(declarations).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        foreach (string name in Names)
        {
            GameplayTag tag = GameplayTagManager.RequestTag(name);
            Check(tag.Name == name && tag.RuntimeIndex != 0, "fixture", "registered name", name);
            Tags.Add(name, tag);
        }

        Run("copy across reserved capacities and retained growth histories", CopyCapacityMatrix);
        Run("union/intersection overlap, asymmetric sizes and explicit ancestors", AlgebraMatrix);
        Run("copy and intersection aliases, interface proxies and independent results", Aliases);
        Run("counted inputs normalize to one ordinary-set occurrence", CountNormalization);
        Run("randomized bulk construction followed by mutations and full removal", RandomizedConstruction);
        Console.WriteLine($"Bulk regression: {assertions:N0} assertions; {failures} failed groups; seeds 19011 / 630127 / 918731 / 271828.");
        return failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action action)
    {
        try
        {
            action();
            Console.WriteLine("PASS " + name);
        }
        catch (Exception exception)
        {
            failures++;
            Console.Error.WriteLine("FAIL " + name + ": " + exception);
        }
    }

    private static void Check(bool condition, string context, string detail, string name = null)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException(context + ": " + detail + (name == null ? string.Empty : " [" + name + "]"));
    }

    private static GameplayTag T(string name) => Tags[name];
    private static HashSet<string> Set(IEnumerable<string> names) => new HashSet<string>(names, StringComparer.Ordinal);
    private static HashSet<string> Set(params string[] names) => Set((IEnumerable<string>)names);

    private static IEnumerable<string> Prefixes(string name)
    {
        for (int i = 0; i < name.Length; i++)
            if (name[i] == '.')
                yield return name.Substring(0, i);
        yield return name;
    }

    private static HashSet<string> Closure(IEnumerable<string> names)
    {
        HashSet<string> result = Set();
        foreach (string name in names)
            foreach (string prefix in Prefixes(name))
                result.Add(prefix);
        return result;
    }

    private static void Shuffle<T>(T[] values, Random random)
    {
        for (int i = values.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            T temporary = values[i];
            values[i] = values[j];
            values[j] = temporary;
        }
    }

    private static HashSet<string> MixedNames(int count, int salt)
    {
        HashSet<string> result = Set();
        for (int i = 0; i < count; i++)
            result.Add(i == 0 && count > 1 ? "A.B" : i == 1 ? "A.B.C" : Leaves[(i * 73 + salt) & 511]);
        return result;
    }

    private static GameplayTagContainer Build(HashSet<string> desired, int capacity = 0, bool growThenShrink = false)
    {
        var result = new GameplayTagContainer();
        if (capacity != 0)
            result.EnsureCapacity(capacity);
        if (growThenShrink)
            for (int i = 0; i < 320; i++)
                result.AddTag(T(Leaves[i]));
        foreach (string name in desired)
            result.AddTag(T(name));
        if (growThenShrink)
        {
            string[] extras = Leaves.Take(320).Where(name => !desired.Contains(name)).ToArray();
            Shuffle(extras, new Random(19011 + desired.Count + capacity));
            foreach (string name in extras)
                result.RemoveTag(T(name));
        }
        return result;
    }

    private static void AssertModel(IGameplayTagContainer actual, HashSet<string> explicitNames, string context)
    {
        HashSet<string> expectedClosure = Closure(explicitNames);
        Check(actual.ExplicitTagCount == explicitNames.Count, context, "explicit cardinality");
        Check(actual.TagCount == expectedClosure.Count, context, "ancestor closure cardinality");
        Check(actual.IsEmpty == (explicitNames.Count == 0), context, "empty state");
        HashSet<string> seen = Set();
        foreach (GameplayTag tag in actual.GetExplicitTags())
            Check(seen.Add(tag.Name), context, "duplicate explicit enumeration", tag.Name);
        Check(seen.SetEquals(explicitNames), context, "explicit enumeration differs from reference set");
        seen.Clear();
        foreach (GameplayTag tag in actual.GetTags())
            Check(seen.Add(tag.Name), context, "duplicate closure enumeration", tag.Name);
        Check(seen.SetEquals(expectedClosure), context, "closure enumeration differs from string prefixes");
        foreach (string name in Names)
        {
            Check(actual.HasTag(T(name)) == expectedClosure.Contains(name), context, "closure lookup", name);
            Check(actual.HasTagExact(T(name)) == explicitNames.Contains(name), context, "explicit lookup", name);
        }
    }

    // Removing each explicit tag exactly once observes whether a bulk constructor
    // gave ancestors the right contribution counts. Membership alone cannot reveal
    // an extra reference that would leave a phantom ancestor after later deletion.
    private static void Drain(GameplayTagContainer actual, HashSet<string> original, int seed, string context)
    {
        HashSet<string> expected = Set(original);
        string[] order = expected.ToArray();
        Shuffle(order, new Random(seed));
        if (order.Length != 0)
        {
            actual.AddTag(T(order[0]));
            AssertModel(actual, expected, context + " duplicate insertion");
        }
        for (int i = 0; i < order.Length; i++)
        {
            actual.RemoveTag(T(order[i]));
            expected.Remove(order[i]);
            if ((i & 3) == 0)
                actual.RemoveTag(T(order[i]));
            AssertModel(actual, expected, context + " remove " + i);
        }
        AssertModel(actual, Set(), context + " fully drained");
        actual.AddTag(T("A.B"));
        actual.AddTag(T("A.B.C"));
        actual.RemoveTag(T("A.B"));
        AssertModel(actual, Set("A.B.C"), context + " reuse retains implied parent");
        actual.RemoveTag(T("A.B.C"));
        AssertModel(actual, Set(), context + " reuse clears last contribution");
    }

    private static void CopyCapacityMatrix()
    {
        var sourcePlans = new[]
        {
            new Plan("fresh empty", 0, 0), new Plan("small", 0, 1), new Plan("mixed", 0, 5),
            new Plan("reserved64", 64, 24), new Plan("reserved512", 512, 128),
            new Plan("oversized", 4096, 5), new Plan("grew then shrank", 0, 5, true),
            new Plan("grew then emptied", 0, 0, true), new Plan("reserved empty", 4096, 0),
            new Plan("deep singleton", 4096, 1, false, true)
        };
        foreach (Plan sourcePlan in sourcePlans)
        {
            var targetPlans = new[]
            {
                new Plan("fresh", 0, 0), new Plan("same requested capacity", sourcePlan.Capacity, 11),
                new Plan("reserved64", 64, 17), new Plan("reserved512", 512, 37),
                new Plan("oversized", 4096, 3), new Plan("grew then shrank", 0, 7, true),
                new Plan("grew then emptied", 4096, 0, true)
            };
            foreach (Plan targetPlan in targetPlans)
            {
                string context = "copy " + sourcePlan.Name + " -> " + targetPlan.Name;
                HashSet<string> sourceNames = sourcePlan.Deep ? Set(DeepLeaf) : MixedNames(sourcePlan.Size, 13);
                HashSet<string> targetNames = MixedNames(targetPlan.Size, 257);
                GameplayTagContainer source = Build(sourceNames, sourcePlan.Capacity, sourcePlan.Grow);
                GameplayTagContainer target = Build(targetNames, targetPlan.Capacity, targetPlan.Grow);
                GameplayTagContainer.Copy(target, source);
                targetNames = Set(sourceNames);
                AssertModel(target, targetNames, context);
                AssertModel(source, sourceNames, context + " source unchanged");

                string addition = Names.First(name => !targetNames.Contains(name));
                target.AddTag(T(addition));
                targetNames.Add(addition);
                if (sourceNames.Count != 0)
                {
                    string removed = sourceNames.First();
                    target.RemoveTag(T(removed));
                    targetNames.Remove(removed);
                }
                AssertModel(target, targetNames, context + " target independently modified");
                AssertModel(source, sourceNames, context + " source survives target modification");
                source.Clear();
                AssertModel(target, targetNames, context + " target survives source clear");
                source.AddTag(T("A0.B.Leaf"));
                AssertModel(target, targetNames, context + " target survives source reuse");

                GameplayTagContainer.Copy(target, source);
                AssertModel(target, Set("A0.B.Leaf"), context + " repeated copy replaces old state");
                target.RemoveTag(T("A0.B.Leaf"));
                AssertModel(target, Set(), context + " copied state removes once");
                AssertModel(source, Set("A0.B.Leaf"), context + " source remains independent");
                source.Clear();
                target.AddTag(T("A.B.C"));
                GameplayTagContainer.Copy(target, source);
                AssertModel(target, Set(), context + " empty source clears reused target");
            }
        }
        CopyAfterChurn();
        RetainedBucketTransitions();
    }

    private static void ChurnForCopy(GameplayTagContainer actual, HashSet<string> expected, int seed)
    {
        var random = new Random(seed);
        HashSet<string> original = Set(expected);
        string[] removed = original.ToArray();
        Shuffle(removed, random);
        foreach (string name in removed.Take(120))
        {
            actual.RemoveTag(T(name));
            expected.Remove(name);
        }
        string[] inserted = Leaves.Where(name => !original.Contains(name)).ToArray();
        Shuffle(inserted, random);
        foreach (string name in inserted.Take(120))
        {
            actual.AddTag(T(name));
            expected.Add(name);
        }
        foreach (string name in new[] { "A.B", "A.B.C" })
        {
            actual.AddTag(T(name));
            expected.Add(name);
        }
        AssertModel(actual, expected, "copy churn preparation " + seed);
    }

    private static void CopyAfterChurn()
    {
        // Both reservations stay at 512 distinct IDs: 242 explicit names plus
        // at most 75 implicit ancestors fit without growth. Both sources are
        // dense enough for the same-mask block-copy branch, and both have had
        // dense-index swaps and hash-cluster repairs before further insertion.
        HashSet<string> sourceNames = Set(Leaves.Take(240));
        HashSet<string> targetNames = Set(Leaves.Skip(256).Take(240));
        GameplayTagContainer source = Build(sourceNames, 512);
        GameplayTagContainer target = Build(targetNames, 512);
        ChurnForCopy(source, sourceNames, 42421);
        ChurnForCopy(target, targetNames, 42422);
        GameplayTagContainer.Copy(target, source);
        AssertModel(target, sourceNames, "same-capacity copy after both sides deleted and inserted");
        AssertModel(source, sourceNames, "churned source preserved after copy");
        Drain(target, sourceNames, 42423, "drain same-capacity churn copy");
        AssertModel(source, sourceNames, "churned source independent of drained target");
        Drain(source, sourceNames, 42424, "drain original churned source");
    }

    private static void GrowAndPruneReserved(GameplayTagContainer target, HashSet<string> expected,
        int seed, string context)
    {
        string[] inserted = Leaves.ToArray();
        var random = new Random(seed);
        Shuffle(inserted, random);
        // Every insertion is observed, including transitions across the small
        // linear representation and each hash-table size within the reservation.
        foreach (string name in inserted)
        {
            target.AddTag(T(name));
            expected.Add(name);
            AssertModel(target, expected, context + " growing " + name);
        }
        string[] removed = expected.ToArray();
        Shuffle(removed, random);
        foreach (string name in removed.Take(removed.Length / 2))
        {
            target.RemoveTag(T(name));
            expected.Remove(name);
            AssertModel(target, expected, context + " shrinking " + name);
        }
    }

    private static void RetainedBucketTransitions()
    {
        var plans = new[]
        {
            new Plan("small hash source", 64, 24),
            new Plan("medium hash source", 256, 83),
            new Plan("sparse oversized source", 4096, 5)
        };
        int seed = 517031;
        foreach (Plan plan in plans)
        {
            string context = "retained buckets " + plan.Name;
            HashSet<string> sourceNames = MixedNames(plan.Size, seed & 511);
            GameplayTagContainer source = Build(sourceNames, plan.Capacity);
            GameplayTagContainer target = Build(Set(Leaves), 1024);
            // Copying a much smaller active index leaves occupied old slots
            // beyond it in the reserved array. The public queries must ignore
            // those slots now, and future growth must clear them before reuse.
            GameplayTagContainer.Copy(target, source);
            HashSet<string> expected = Set(sourceNames);
            AssertModel(target, expected, context + " copied smaller active index");
            GrowAndPruneReserved(target, expected, seed++, context + " first growth");
            AssertModel(source, sourceNames, context + " source independent after growth");

            HashSet<string> linearNames = Set("A.B", "A.B.C");
            GameplayTagContainer linearSource = Build(linearNames);
            GameplayTagContainer.Copy(target, linearSource);
            expected = Set(linearNames);
            AssertModel(target, expected, context + " copied linear source over populated hash");
            GrowAndPruneReserved(target, expected, seed++, context + " linear to hash growth");
            AssertModel(linearSource, linearNames, context + " linear source independent");

            GameplayTagContainer.Copy(target, linearSource);
            target.Clear();
            expected.Clear();
            AssertModel(target, expected, context + " cleared linear state with retained slots");
            // This is still below the original reservation. It must activate a
            // clean prefix, even though no backing-array allocation is needed.
            target.EnsureCapacity(128);
            GrowAndPruneReserved(target, expected, seed++, context + " reserved prefix expansion after clear");
            Drain(target, expected, seed++, context + " independent target drain");
            AssertModel(source, sourceNames, context + " source survived repeated target transitions");
            AssertModel(linearSource, linearNames, context + " linear source survived target drain");
            Drain(source, sourceNames, seed++, context + " independent hash source drain");
            Drain(linearSource, linearNames, seed++, context + " independent linear source drain");
        }
    }

    private static void AlgebraMatrix()
    {
        int[,] sizes = { { 0, 0 }, { 0, 17 }, { 17, 0 }, { 1, 1 }, { 1, 97 }, { 97, 1 },
            { 4, 16 }, { 16, 4 }, { 16, 16 }, { 65, 129 }, { 129, 65 }, { 128, 128 } };
        int seed = 630127;
        for (int row = 0; row < sizes.GetLength(0); row++)
            foreach (int percent in new[] { 0, 25, 50, 100 })
            {
                int leftCount = sizes[row, 0], rightCount = sizes[row, 1];
                int common = Math.Min(leftCount, rightCount) * percent / 100;
                HashSet<string> left = Set(Leaves.Take(leftCount));
                HashSet<string> right = Set(Leaves.Take(common).Concat(Leaves.Skip(leftCount).Take(rightCount - common)));
                AlgebraCase(left, right, seed++, "sizes " + leftCount + "/" + rightCount + " overlap " + percent + "%");
            }
        AlgebraCase(Set("A.B.C"), Set("A.B.D"), seed++, "siblings share only implicit ancestors");
        AlgebraCase(Set("A", "A.B.C", "A.B.D"), Set("A.B", "A.B.C"), seed++, "parent explicit in only one input");
        AlgebraCase(Set("A", "A.B", "A.B.C"), Set("A", "A.B.C", "A0.B.Leaf", "Case.Child"), seed++, "common parent and child");
        AlgebraCase(Set("Case.Child", "A.B.C"), Set("case.Child", "A0.B.Leaf"), seed++, "ordinal names and prefix boundaries");
        string[] chain = Prefixes(DeepLeaf).ToArray();
        AlgebraCase(Set(chain), Set(chain.Where((_, i) => (i & 1) == 0)), seed++, "many explicit tags on one deep path");
        AlgebraCase(Set(DeepLeaf), Set(chain.Take(chain.Length - 1)), seed, "explicit leaf versus implicit leaf ancestors");
    }

    private static void AlgebraCase(HashSet<string> leftNames, HashSet<string> rightNames, int seed, string context)
    {
        GameplayTagContainer left = Build(leftNames, 64, true);
        GameplayTagContainer right = Build(rightNames, 4096);
        HashSet<string> unionNames = Set(leftNames);
        unionNames.UnionWith(rightNames);
        HashSet<string> intersectionNames = Set(leftNames);
        intersectionNames.IntersectWith(rightNames);

        GameplayTagContainer union = GameplayTagContainer.Union(left, right);
        GameplayTagContainer intersection = GameplayTagContainer.Intersection(left, right);
        GameplayTagContainer reverse = GameplayTagContainer.Intersection(right, left);
        AssertModel(union, unionNames, context + " union");
        AssertModel(intersection, intersectionNames, context + " intersection");
        AssertModel(reverse, intersectionNames, context + " reversed intersection");
        GameplayTagContainer output = Build(MixedNames(41, seed & 511), 4096, true);
        GameplayTagContainer.Intersection(output, left, right);
        AssertModel(output, intersectionNames, context + " reused output");
        Drain(union, unionNames, seed, context + " drain union");
        Drain(intersection, intersectionNames, seed + 1, context + " drain intersection");
        Drain(output, intersectionNames, seed + 2, context + " drain reused output");
        AssertModel(left, leftNames, context + " lhs remains independent");
        AssertModel(right, rightNames, context + " rhs remains independent");
    }

    private static void Aliases()
    {
        HashSet<string> leftNames = MixedNames(71, 17);
        HashSet<string> rightNames = MixedNames(43, 90);
        rightNames.UnionWith(leftNames.Take(15));
        HashSet<string> common = Set(leftNames);
        common.IntersectWith(rightNames);
        GameplayTagContainer left = Build(leftNames, 4096, true);
        GameplayTagContainer right = Build(rightNames);
        IGameplayTagContainer asInterface = left;
        var proxy = new ProxyContainer(left);
        GameplayTagContainer.Copy(left, left);
        GameplayTagContainer.Copy(left, asInterface);
        GameplayTagContainer.Copy(left, proxy);
        AssertModel(left, leftNames, "copy aliases through concrete/interface/proxy");

        GameplayTagContainer unionSelf = GameplayTagContainer.Union(left, left);
        GameplayTagContainer unionProxy = GameplayTagContainer.Union(proxy, left);
        GameplayTagContainer intersectionSelf = GameplayTagContainer.Intersection(proxy, left);
        Drain(unionSelf, leftNames, 918731, "union identical inputs");
        Drain(unionProxy, leftNames, 918732, "union aliased proxy input");
        Drain(intersectionSelf, leftNames, 918733, "intersection aliased inputs returns independent result");
        AssertModel(left, leftNames, "same-input results did not alias their source");
        GameplayTagContainer.Intersection(left, proxy, left);
        AssertModel(left, leftNames, "intersection output aliases both inputs");
        left.IntersectWith(proxy);
        AssertModel(left, leftNames, "self intersect through proxy");

        GameplayTagContainer.Intersection(left, proxy, right);
        AssertModel(left, common, "intersection output aliases lhs proxy");
        Drain(left, common, 918734, "drain lhs alias");
        AssertModel(right, rightNames, "rhs unaffected by lhs alias mutation");
        left = Build(leftNames);
        proxy = new ProxyContainer(right);
        GameplayTagContainer.Intersection(right, left, proxy);
        AssertModel(right, common, "intersection output aliases rhs proxy");
        Drain(right, common, 918735, "drain rhs alias");
        AssertModel(left, leftNames, "lhs unaffected by rhs alias mutation");
        GameplayTagContainer copiedProxy = new GameplayTagContainer();
        GameplayTagContainer.Copy(copiedProxy, new ProxyContainer(left));
        Drain(copiedProxy, leftNames, 918736, "copy through independent proxy normalizes contributions");
        AssertModel(left, leftNames, "proxy copy did not share source storage");
    }

    private static GameplayTagCountContainer Counted(Dictionary<string, int> model)
    {
        var result = new GameplayTagCountContainer(4096);
        foreach (KeyValuePair<string, int> pair in model)
            result.AddTag(T(pair.Key), pair.Value);
        return result;
    }

    private static void AssertCounts(GameplayTagCountContainer actual, Dictionary<string, int> explicitCounts, string context)
    {
        AssertModel(actual, Set(explicitCounts.Keys), context);
        var total = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, int> pair in explicitCounts)
            foreach (string prefix in Prefixes(pair.Key))
            {
                total.TryGetValue(prefix, out int previous);
                total[prefix] = previous + pair.Value;
            }
        foreach (string name in Names)
        {
            explicitCounts.TryGetValue(name, out int expectedExact);
            total.TryGetValue(name, out int expectedTotal);
            Check(actual.GetExplicitTagCount(T(name)) == expectedExact, context, "source explicit multiplicity changed", name);
            Check(actual.GetTagCount(T(name)) == expectedTotal, context, "source ancestor multiplicity changed", name);
        }
    }

    private static void CountNormalization()
    {
        HashSet<string> aNames = MixedNames(65, 31);
        aNames.UnionWith(new[] { "A", "A.B", "A.B.C", DeepLeaf, "Deep.N1" });
        HashSet<string> bNames = MixedNames(37, 177);
        bNames.UnionWith(aNames.Take(18));
        bNames.UnionWith(new[] { "A", "A.B.D", "case.Child" });
        Dictionary<string, int> aModel = aNames.Select((name, i) => new { name, count = 2 + i % 7 })
            .ToDictionary(pair => pair.name, pair => pair.count, StringComparer.Ordinal);
        Dictionary<string, int> bModel = bNames.Select((name, i) => new { name, count = 3 + i % 11 })
            .ToDictionary(pair => pair.name, pair => pair.count, StringComparer.Ordinal);
        GameplayTagCountContainer a = Counted(aModel), b = Counted(bModel);
        GameplayTagContainer plain = Build(bNames, 4096, true);
        HashSet<string> union = Set(aNames);
        union.UnionWith(bNames);
        HashSet<string> common = Set(aNames);
        common.IntersectWith(bNames);
        int seed = 271828;
        foreach (int capacity in new[] { 0, 64, 4096 })
        {
            GameplayTagContainer destination = Build(MixedNames(29, capacity & 511), capacity, true);
            GameplayTagContainer.Copy(destination, a);
            AssertModel(destination, aNames, "count-to-set copy " + capacity);
            Drain(destination, aNames, seed++, "normalized copy " + capacity);
        }
        Drain(GameplayTagContainer.Union(a, b), union, seed++, "union two counted inputs");
        Drain(GameplayTagContainer.Union(a, plain), union, seed++, "union counted lhs");
        Drain(GameplayTagContainer.Union(plain, a), union, seed++, "union counted rhs");
        Drain(GameplayTagContainer.Union(a, a), aNames, seed++, "union counted self");
        Drain(GameplayTagContainer.Intersection(a, b), common, seed++, "intersection two counted inputs");
        Drain(GameplayTagContainer.Intersection(a, plain), common, seed++, "intersection counted lhs");
        Drain(GameplayTagContainer.Intersection(plain, a), common, seed++, "intersection counted rhs");
        Drain(GameplayTagContainer.Intersection(a, a), aNames, seed++, "intersection counted self");
        GameplayTagContainer.Intersection(plain, a, plain);
        Drain(plain, common, seed++, "count intersection aliases ordinary output");
        var emptyCounted = new GameplayTagCountContainer(4096);
        plain.AddTag(T("A.B.C"));
        GameplayTagContainer.Copy(plain, emptyCounted);
        AssertModel(plain, Set(), "empty counted source");
        Drain(GameplayTagContainer.Union(a, emptyCounted), aNames, seed++, "count union empty rhs");
        Drain(GameplayTagContainer.Union(emptyCounted, a), aNames, seed++, "count union empty lhs");
        Drain(GameplayTagContainer.Intersection(a, emptyCounted), Set(), seed, "count intersection empty");
        AssertCounts(a, aModel, "count lhs input preserved");
        AssertCounts(b, bModel, "count rhs input preserved");
    }

    private static HashSet<string> Sample(Random random, int count)
    {
        HashSet<string> result = Set();
        while (result.Count < count)
            result.Add(Names[random.Next(Names.Length)]);
        return result;
    }

    private static void RandomizedConstruction()
    {
        foreach (int seed in new[] { 630127, 918731, 271828 })
        {
            var random = new Random(seed);
            for (int iteration = 0; iteration < 24; iteration++)
            {
                string context = "random seed " + seed + " case " + iteration;
                HashSet<string> aNames = Sample(random, random.Next(49));
                HashSet<string> bNames = Sample(random, random.Next(49));
                foreach (string name in aNames)
                    if (random.Next(3) == 0)
                        bNames.Add(name);
                GameplayTagContainer a = Build(aNames, iteration % 2 == 0 ? 4096 : 0, true);
                GameplayTagContainer b = Build(bNames, iteration % 3 == 0 ? 4096 : 64);
                GameplayTagContainer result;
                HashSet<string> expected = Set(aNames);
                if (iteration % 3 == 0)
                {
                    result = Build(MixedNames(17, iteration), 4096, true);
                    GameplayTagContainer.Copy(result, a);
                }
                else if (iteration % 3 == 1)
                {
                    result = GameplayTagContainer.Union(a, b);
                    expected.UnionWith(bNames);
                }
                else
                {
                    result = Build(MixedNames(17, iteration), 4096, true);
                    GameplayTagContainer.Intersection(result, a, b);
                    expected.IntersectWith(bNames);
                }
                AssertModel(result, expected, context + " bulk result");
                for (int mutation = 0; mutation < 20; mutation++)
                {
                    if (expected.Count != 0 && random.Next(3) != 0)
                    {
                        string name = expected.ElementAt(random.Next(expected.Count));
                        result.RemoveTag(T(name));
                        expected.Remove(name);
                    }
                    else
                    {
                        string name = Names[random.Next(Names.Length)];
                        result.AddTag(T(name));
                        expected.Add(name);
                    }
                    AssertModel(result, expected, context + " mutation " + mutation);
                }
                Drain(result, expected, seed + iteration, context + " drain");
                AssertModel(a, aNames, context + " lhs independent");
                AssertModel(b, bNames, context + " rhs independent");
            }
        }
    }

    private readonly struct Plan
    {
        internal Plan(string name, int capacity, int size, bool grow = false, bool deep = false)
        {
            Name = name;
            Capacity = capacity;
            Size = size;
            Grow = grow;
            Deep = deep;
        }
        internal string Name { get; }
        internal int Capacity { get; }
        internal int Size { get; }
        internal bool Grow { get; }
        internal bool Deep { get; }
    }

    // A distinct value-type input can still expose the same logical container.
    // Alias tests must not depend on object identity or a concrete generic type.
    private readonly struct ProxyContainer : IGameplayTagContainer
    {
        private readonly GameplayTagContainer inner;
        internal ProxyContainer(GameplayTagContainer value) => inner = value;
        public bool IsEmpty => inner.IsEmpty;
        public int ExplicitTagCount => inner.ExplicitTagCount;
        public int TagCount => inner.TagCount;
        public GameplayTagContainerIndices Indices => inner.Indices;
        public void AddTag(GameplayTag tag) => inner.AddTag(tag);
        public void RemoveTag(GameplayTag tag) => inner.RemoveTag(tag);
        public GameplayTagEnumerator GetTags() => inner.GetTags();
        public GameplayTagEnumerator GetExplicitTags() => inner.GetExplicitTags();
        public void AddTags<T>(in T other) where T : IGameplayTagContainer => inner.AddTags(other);
        public void RemoveTags<T>(in T other) where T : IGameplayTagContainer => inner.RemoveTags(other);
        public void GetParentTags(GameplayTag tag, List<GameplayTag> output) => inner.GetParentTags(tag, output);
        public void GetChildTags(GameplayTag tag, List<GameplayTag> output) => inner.GetChildTags(tag, output);
        public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> output) => inner.GetExplicitParentTags(tag, output);
        public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> output) => inner.GetExplicitChildTags(tag, output);
        public void Clear() => inner.Clear();
        public IEnumerator<GameplayTag> GetEnumerator() => inner.GetTags();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
