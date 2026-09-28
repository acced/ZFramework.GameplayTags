using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using GameplayTags;
using UnityEngine;

internal static class DenseIndexTests
{
    private static long assertions;
    private static int failures, checksum;
    private static object sink;
    private static readonly List<object> results = new List<object>();
    private static readonly FieldInfo Data = typeof(RuntimeTagSet).GetField("m_Data", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo Active = typeof(RuntimeTagSet).GetField("m_ActiveWords", BindingFlags.NonPublic | BindingFlags.Instance);
    private static void Check(bool value, string message = "assertion") { assertions++; if (!value) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    { assertions++; try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static void Test(string name, Action action)
    {
        try { action(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
        catch (Exception e) { failures++; results.Add(new { name, passed = false, error = e.ToString() }); Console.WriteLine("FAIL " + name + " " + e); }
    }
    private static TagRegistry Registry(int count)
    {
        var settings = ScriptableObject.CreateInstance<GameplayTagSettings>();
        settings.ReplaceAll(Enumerable.Range(0, count).Select(i => new GameplayTagDefinition("T" + i.ToString("D7"), "", "Default", false, true)).ToList(),
            new List<GameplayTagRedirect>(), new List<GameplayTagSource> { new GameplayTagSource("Default", "", false) });
        return TagRegistry.Create(settings);
    }
    private static RuntimeTagSet Set(TagRegistry registry, IEnumerable<int> ids, bool prepare = true)
    {
        var set = new RuntimeTagSet(registry, prepare ? 1 : 0);
        foreach (int id in ids) set.AddTag(registry.GetTagAt(id));
        return set;
    }
    private static int CountBits(ulong word)
    {
        int count = 0;
        for (int bit = 0; bit < 64; bit++) if ((word & (1UL << bit)) != 0) count++;
        return count;
    }
    private static void Invariant(RuntimeTagSet set)
    {
        var data = (ulong[])Data.GetValue(set);
        Check(set.BufferBytes == data.LongLength * 8);
        if (data.Length == 0) { Check(set.Count == 0); Check(set.Capacity == 0); return; }
        int words = set.Registry.WordCount, cardinality = 0, active = 0;
        for (int i = 0; i < words; i++) { cardinality += CountBits(data[i]); if (data[i] != 0) active++; }
        Check(cardinality == set.Count, "wrong eager cardinality");
        Check(active == (int)Active.GetValue(set), "wrong occupied-word count");
        if ((set.Registry.Count & 63) != 0)
            Check((data[words - 1] >> (set.Registry.Count & 63)) == 0, "phantom tail bits");
        int offset = 0, length = words;
        while (length > 1)
        {
            int parentOffset = offset + length, parents = (length + 63) >> 6;
            for (int p = 0; p < parents; p++)
            {
                ulong expected = 0;
                for (int b = 0; b < 64 && p * 64 + b < length; b++)
                    if (data[offset + p * 64 + b] != 0) expected |= 1UL << b;
                Check(data[parentOffset + p] == expected, "stale occupancy summary");
            }
            offset = parentOffset; length = parents;
        }
        Check(offset + length == data.Length, "wrong hierarchy allocation");
    }
    private static void Same(RuntimeTagSet set, IEnumerable<int> expected)
    {
        var wanted = new HashSet<int>(expected);
        int previous = -1, seen = 0;
        foreach (RuntimeTag tag in set)
        {
            Check(tag.RuntimeIndex > previous); previous = tag.RuntimeIndex;
            Check(ReferenceEquals(tag.Registry, set.Registry));
            Check(wanted.Remove(tag.RuntimeIndex), "unexpected/duplicate member"); seen++;
        }
        Check(wanted.Count == 0 && seen == set.Count);
        Invariant(set);
    }
    private static void Tests()
    {
        Test("no-sparse-api-or-member-array", () => {
            Check(typeof(RuntimeTagSet).Assembly.GetType("GameplayTags.TagSetStorage") == null);
            Check(!typeof(RuntimeTagSet).GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Any(f => f.FieldType == typeof(int[])));
            var registry = Registry(0); var set = new RuntimeTagSet(registry, 1);
            Same(set, Array.Empty<int>()); Check(!set.HasTagExact(default));
            Throws<ArgumentOutOfRangeException>(() => new RuntimeTagSet(registry, -1));
        });
        Test("all-64-bit-positions-and-enumerator-exhaustion", () => {
            var registry = Registry(129); var set = new RuntimeTagSet(registry, 1);
            for (int i = 0; i < registry.Count; i++)
            {
                set.Clear(); set.AddTag(registry.GetTagAt(i)); Same(set, new[] { i });
                var iterator = set.GetEnumerator(); Check(iterator.MoveNext()); Check(iterator.Current.RuntimeIndex == i);
                Check(!iterator.MoveNext()); Check(!iterator.MoveNext());
                for (int j = 0; j < registry.Count; j++) Check(set.HasTagExact(registry.GetTagAt(j)) == (i == j));
            }
        });
        Test("four-summary-levels-and-far-apart-members", () => {
            var registry = Registry(262209);
            int[] ids = { 0, 63, 64, 4095, 4096, 262143, 262144, 262208 };
            var set = Set(registry, ids); Same(set, ids);
            foreach (int id in ids)
            {
                Check(set.AnyInRange(id, id + 1));
                Check(set.RemoveTag(registry.GetTagAt(id)));
                Check(!set.AnyInRange(id, id + 1));
                Same(set, ids.Where(x => x > id));
            }
            set.AppendTags(Set(registry, ids));
            var random = new Random(9282026);
            for (int i = 0; i < 10000; i++)
            {
                int a = random.Next(registry.Count + 1), b = random.Next(registry.Count + 1);
                int start = Math.Min(a, b), end = Math.Max(a, b);
                Check(set.AnyInRange(start, end) == ids.Any(x => x >= start && x < end));
            }
            set.Clear(); Same(set, Array.Empty<int>()); Check(set.Capacity == registry.Count);
            var emptyCopy = new RuntimeTagSet(set); Check(emptyCopy.Capacity == 0);
        });
        Test("density-kernel-boundaries-dirty-output-and-aliases", () => {
            var registry = Registry(8192);
            foreach (int a in new[] { 0, 1, 31, 32, 33, 127, 128 })
            foreach (int b in new[] { 0, 1, 31, 32, 33, 127, 128 })
            {
                int[] x = Enumerable.Range(0, a).Select(i => i * 64).ToArray();
                int[] y = Enumerable.Range(0, b).Select(i => (127 - i) * 64 + (i % 2)).ToArray();
                RuntimeTagSet left = Set(registry, x), right = Set(registry, y), output = Set(registry, new[] { 8191 });
                RuntimeTagSet.UnionInto(left, right, output); Same(output, x.Concat(y));
                RuntimeTagSet.IntersectionExactInto(left, right, output); Same(output, x.Intersect(y));
                output.CopyFrom(left); output.AppendTags(right); Same(output, x.Concat(y));
                output.CopyFrom(left); output.RemoveTags(right); Same(output, x.Except(y));
                output.CopyFrom(left); RuntimeTagSet.UnionInto(output, right, output); Same(output, x.Concat(y));
                output.CopyFrom(right); RuntimeTagSet.IntersectionExactInto(left, output, output); Same(output, x.Intersect(y));
                Check(left.HasAnyExact(right) == x.Intersect(y).Any()); Check(left.HasAllExact(right) == y.All(x.Contains));
                output.CopyFrom(left); Check(output.SetEquals(left)); output.Clear(); Same(output, Array.Empty<int>());
            }
        });
        Test("random-word-transitions-and-reference-operations", () => {
            var registry = Registry(4097); var random = new Random(928);
            var set = new RuntimeTagSet(registry, 1); var reference = new HashSet<int>();
            for (int i = 0; i < 12000; i++)
            {
                int id = random.Next(registry.Count);
                switch (random.Next(7))
                {
                    case 0: Check(set.AddTag(registry.GetTagAt(id)) == reference.Add(id)); break;
                    case 1: Check(set.RemoveTag(registry.GetTagAt(id)) == reference.Remove(id)); break;
                    case 2: Check(set.HasTagExact(registry.GetTagAt(id)) == reference.Contains(id)); break;
                    case 3:
                    case 4:
                        var ids = Enumerable.Range(0, random.Next(64)).Select(_ => random.Next(registry.Count)).ToArray();
                        var other = Set(registry, ids);
                        if ((i & 1) == 0) { set.AppendTags(other); reference.UnionWith(ids); }
                        else { set.RemoveTags(other); reference.ExceptWith(ids); }
                        break;
                    case 5:
                        var copy = new RuntimeTagSet(set); Same(copy, reference); Check(copy.SetEquals(set)); break;
                    default:
                        if ((i & 127) == 0) { set.Clear(); reference.Clear(); }
                        break;
                }
                Invariant(set);
                if ((i & 31) == 0) Same(set, reference);
            }
        });
        Test("prepared-large-low-occupancy-zero-allocation", () => {
            var registry = Registry(262209);
            var a = Set(registry, new[] { 1, 65, 4096, 65535, 65536, 131071, 262143, 262208 });
            var b = Set(registry, new[] { 65, 4096, 65536, 262208 });
            var work = new RuntimeTagSet(registry, 1); var output = new RuntimeTagSet(registry, 1);
            Action action = () => {
                work.CopyFrom(a); work.AppendTags(b); work.RemoveTags(b);
                RuntimeTagSet.UnionInto(a, b, output); RuntimeTagSet.IntersectionExactInto(a, b, output);
                a.FilterInto(b, output);
                foreach (RuntimeTag tag in work) checksum ^= tag.RuntimeIndex;
                checksum += work.AnyInRange(0, 4096) ? 1 : 0;
            };
            for (int i = 0; i < 200; i++) action();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 3000; i++) action();
            Check(GC.GetAllocatedBytesForCurrentThread() == before, "prepared path allocated");
            before = GC.GetAllocatedBytesForCurrentThread(); sink = new byte[4096];
            Check(GC.GetAllocatedBytesForCurrentThread() > before, "inactive allocation counter");
            Invariant(work); Invariant(output);
        });
    }
    private static int Main(string[] args)
    {
        Tests();
        File.WriteAllText(args[0], JsonSerializer.Serialize(new { assertions, failures, results, checksum, native_unity_executed = false }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("DENSE INDEX tests=" + results.Count + " assertions=" + assertions + " failures=" + failures);
        return failures == 0 ? 0 : 1;
    }
}
