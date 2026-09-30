using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using GameplayTags;
using UnityEngine;

internal static class PackedTests
{
    private static long assertions;
    private static int failed, checksum;
    private static object sink;
    private static readonly List<object> results = new List<object>();
    private static void Check(bool value, string message = "assertion") { assertions++; if (!value) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    { assertions++; try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static void Test(string name, Action body)
    {
        try { body(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; results.Add(new { name, passed = false, error = e.ToString() }); Console.WriteLine("FAIL " + name + " " + e); }
    }
    private static TagRegistry Registry(int size)
    {
        var settings = ScriptableObject.CreateInstance<GameplayTagSettings>();
        settings.ReplaceAll(Enumerable.Range(0, size).Select(i => new GameplayTagDefinition("T" + i.ToString("D8"), "", "Default", false, true)).ToList(),
            new List<GameplayTagRedirect>(), new List<GameplayTagSource> { new GameplayTagSource("Default", "", false) });
        return TagRegistry.Create(settings);
    }
    private static RuntimeTagSet Set(TagRegistry registry, IEnumerable<int> input, int capacity = 0)
    {
        var set = new RuntimeTagSet(registry, capacity);
        foreach (int id in input) set.AddTag(registry.GetTagAt(id));
        return set;
    }
    private static void Same(RuntimeTagSet set, IEnumerable<int> values)
    {
        int[] expected = values.Distinct().OrderBy(i => i).ToArray();
        var actual = new List<int>();
        foreach (var tag in set) { Check(ReferenceEquals(tag.Registry, set.Registry)); actual.Add(tag.RuntimeIndex); }
        Check(actual.SequenceEqual(expected), "wrong ordered members: actual=" + string.Join(",", actual.Take(20)) + " expected=" + string.Join(",", expected.Take(20)));
        Check(set.Count == expected.Length, "wrong Count");
        foreach (int id in expected) Check(set.HasTagExact(set.Registry.GetTagAt(id)), "lookup disagrees with enumeration");
    }
    private static void Layout(RuntimeTagSet set)
    {
        Type t = typeof(RuntimeTagSet);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        int ng = (int)t.GetField("m_GroupCount", flags).GetValue(set), nw = (int)t.GetField("m_WordCount", flags).GetValue(set);
        var getGroup = t.GetMethod("ReadGroup", flags); var getWord = t.GetMethod("ReadWord", flags);
        int offset = 0, previous = -1, count = 0;
        for (int i = 0; i < ng; i++)
        {
            object group = getGroup.Invoke(set, new object[] { i }); Type g = group.GetType();
            int key = (int)g.GetField("Key", flags).GetValue(group), start = (int)g.GetField("Offset", flags).GetValue(group);
            ulong mask = (ulong)g.GetField("Mask", flags).GetValue(group);
            Check(mask != 0 && key > previous && start == offset, "directory invariant"); previous = key;
            for (int bit = 0; bit < 64; bit++) if ((mask & (1UL << bit)) != 0)
            {
                ulong word = (ulong)getWord.Invoke(set, new object[] { offset++ });
                Check(word != 0, "stored zero member word");
                for (int b = 0; b < 64; b++) if ((word & (1UL << b)) != 0)
                { Check(((long)key << 12) + (bit << 6) + b < set.Registry.Count, "tail bit"); count++; }
            }
        }
        Check(offset == nw && count == set.Count, "directory rank/Count mismatch");
    }
    private static void Settled()
    { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    private static void Tests()
    {
        Test("empty-singleton-high-id-inline-and-scope", () =>
        {
            TagRegistry registry = Registry(8193), foreign = Registry(8193);
            var a = new RuntimeTagSet(registry);
            Check(!a.HasTagExact(RuntimeTag.None)); Check(!a.RemoveTag(RuntimeTag.None));
            foreach (int id in new[] { 0, 63, 64, 4095, 4096, 8192 })
            {
                a.Clear(); Check(a.AddTag(registry.GetTagAt(id))); Check(!a.AddTag(registry.GetTagAt(id)));
                Same(a, new[] { id }); Layout(a);
                Check(a.BufferBytes == 24, "single word should fit inline");
                var copy = new RuntimeTagSet(a); Same(copy, new[] { id });
                copy.Clear(); Same(a, new[] { id });
            }
            Throws<ArgumentException>(() => a.AddTag(foreign.GetTagAt(0)));
            Throws<ArgumentException>(() => a.CopyFrom(new RuntimeTagSet(foreign)));
            Throws<ArgumentOutOfRangeException>(() => a.EnsureCapacity(-1));
            Check(typeof(RuntimeTagSet).Assembly.GetType("GameplayTags.TagSetStorage") == null, "legacy selector still shipped");
        });
        Test("growing-independent-subset-and-filter-output", () =>
        {
            var r = Registry(16385);
            int[] ids = new[] { 0, 64, 128, 192, 256, 4096, 8192, 12288, 16384 };
            var a = Set(r, ids); var b = Set(r, ids);
            var output = new RuntimeTagSet(r);
            RuntimeTagSet.IntersectionExactInto(a, b, output); Same(output, ids); Layout(output);
            output = new RuntimeTagSet(r); a.FilterInto(b, output); Same(output, ids); Layout(output);
        });
        Test("exhaustive-cross-word-cross-page-sets-and-aliases", () =>
        {
            var r = Registry(8193); int[] domain = { 0, 1, 63, 64, 4095, 4096 };
            for (int x = 0; x < 64; x++) for (int y = 0; y < 64; y++)
            {
                int[] a = domain.Where((_, i) => (x & (1 << i)) != 0).ToArray(), b = domain.Where((_, i) => (y & (1 << i)) != 0).ToArray();
                var left = Set(r, a); var right = Set(r, b);
                Same(RuntimeTagSet.Union(left, right), a.Concat(b));
                Same(RuntimeTagSet.IntersectionExact(left, right), a.Intersect(b));
                var output = Set(r, new[] { 128, 512, 8192 });
                RuntimeTagSet.UnionInto(left, right, output); Same(output, a.Concat(b));
                RuntimeTagSet.IntersectionExactInto(left, right, output); Same(output, a.Intersect(b));
                output.CopyFrom(left); output.AppendTags(right); Same(output, a.Concat(b));
                output.CopyFrom(left); Check(output.RemoveTags(right) == a.Intersect(b).Any()); Same(output, a.Except(b));
                var alias = new RuntimeTagSet(left); RuntimeTagSet.UnionInto(alias, right, alias); Same(alias, a.Concat(b));
                alias = new RuntimeTagSet(right); RuntimeTagSet.UnionInto(left, alias, alias); Same(alias, a.Concat(b));
                alias = new RuntimeTagSet(left); RuntimeTagSet.IntersectionExactInto(alias, right, alias); Same(alias, a.Intersect(b));
                alias = new RuntimeTagSet(right); RuntimeTagSet.IntersectionExactInto(left, alias, alias); Same(alias, a.Intersect(b));
                alias = new RuntimeTagSet(left); alias.FilterInto(right, alias); Same(alias, a.Intersect(b));
                Check(left.HasAnyExact(right) == a.Intersect(b).Any()); Check(left.HasAllExact(right) == b.All(a.Contains));
                Check(left.SetEquals(right) == a.SequenceEqual(b));
                Same(left, a); Same(right, b); Layout(output); Layout(alias);
            }
        });
        Test("random-repositioning-and-directory-reuse", () =>
        {
            var r = Registry(32769); var random = new Random(20260930);
            var set = new RuntimeTagSet(r); var expected = new HashSet<int>();
            for (int round = 0; round < 4000; round++)
            {
                int id = random.Next(r.Count);
                switch (random.Next(5))
                {
                    case 0: Check(set.AddTag(r.GetTagAt(id)) == expected.Add(id)); break;
                    case 1: Check(set.RemoveTag(r.GetTagAt(id)) == expected.Remove(id)); break;
                    case 2:
                        int[] incoming = Enumerable.Range(0, 40).Select(_ => random.Next(r.Count)).Distinct().ToArray();
                        var other = Set(r, incoming); set.AppendTags(other); expected.UnionWith(incoming); break;
                    case 3:
                        int[] removal = expected.Where(_ => random.Next(4) == 0).ToArray();
                        Check(set.RemoveTags(Set(r, removal)) == removal.Any()); expected.ExceptWith(removal); break;
                    default:
                        if (round % 23 == 0) { set.Clear(); expected.Clear(); }
                        else Check(set.HasTagExact(r.GetTagAt(id)) == expected.Contains(id)); break;
                }
                Same(set, expected);
                if (round % 19 == 0) Layout(set);
            }
        });
        Test("hierarchy-source-isolation-and-invalid-negation", () =>
        {
            var settings = ScriptableObject.CreateInstance<GameplayTagSettings>();
            string[] names = { "A.B.C", "A!x", "A.Z", "Z", "状态.燃烧" };
            settings.ReplaceAll(names.Select(n => new GameplayTagDefinition(n, "", "Default", false, true)).ToList(),
                new List<GameplayTagRedirect>(), new List<GameplayTagSource> { new GameplayTagSource("Default", "", false) });
            GameplayTagManager.Initialize(settings, true); var r = GameplayTagManager.CurrentRegistry;
            var set = new RuntimeTagSet(r); set.AddTag(r.Resolve("A.B.C"));
            Check(set.HasTag(r.Resolve("A"))); Check(!set.HasTagExact(r.Resolve("A")));
            for (int i = 0; i < r.Count; i++) for (int j = i; j <= r.Count; j++)
                Check(set.AnyInRange(i, j) == (r.Resolve("A.B.C").RuntimeIndex >= i && r.Resolve("A.B.C").RuntimeIndex < j));
            var source = new GameplayTagQuery(GameplayTagQueryExpression.AllTagsMatch().AddTag(GameplayTagManager.RequestTag("A")));
            var frozen = source.Freeze(r); source.RootExpression.AddTag(GameplayTagManager.RequestTag("Z"));
            Check(frozen.Matches(set)); Check(!source.Freeze(r).Matches(set));
            var invalid = new GameplayTagQuery(GameplayTagQueryExpression.NoExpressionsMatch().AddExpression(new GameplayTagQueryExpression(GameplayTagQueryExpressionType.Undefined)));
            Throws<InvalidOperationException>(() => invalid.Freeze(r));
        });
        Test("prepared-arbitrary-placement-zero-allocation-positive-control", () =>
        {
            var r = Registry(262145); int[] ids = Enumerable.Range(0, 128).Select(i => (i * 2039) % r.Count).ToArray();
            var a = Set(r, ids); var b = Set(r, ids.Where((_, i) => (i & 1) == 0));
            var work = new RuntimeTagSet(r, 256); var output = new RuntimeTagSet(r, 256);
            RuntimeTag low = r.GetTagAt(0), high = r.GetTagAt(r.Count - 1);
            Action action = () =>
            {
                work.CopyFrom(a); RuntimeTagSet.UnionInto(work, b, output);
                RuntimeTagSet.IntersectionExactInto(work, b, output); work.RemoveTags(b); work.AppendTags(b);
                work.Clear(); work.AddTag(low); work.AddTag(high); work.RemoveTag(low); work.RemoveTag(high);
                checksum ^= output.Count;
            };
            for (int i = 0; i < 100; i++) action(); Settled();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 3000; i++) action();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine("PREPARED_ALLOC=" + bytes); Check(bytes == 0, "prepared mutation allocated");
            before = GC.GetAllocatedBytesForCurrentThread(); sink = new byte[4096];
            Check(GC.GetAllocatedBytesForCurrentThread() - before >= 4096, "allocation positive control failed");
            var small = Set(r, ids.Take(8)); var copy = new RuntimeTagSet(small);
            Check(copy.BufferBytes < 2048, "tiny copy allocated a universe-sized bitmap");
            Same(copy, ids.Take(8)); copy.Clear(); Same(small, ids.Take(8)); Layout(output);
        });
    }
    private static int Main(string[] args)
    {
        Tests();
        File.WriteAllText(args[0], JsonSerializer.Serialize(new { assertions, failed, results, native_unity = "not_run" }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PACKED_TESTS groups={results.Count} assertions={assertions} failed={failed}");
        return failed == 0 ? 0 : 1;
    }
}
