using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using GameplayTags;
using UnityEngine;

internal static class DenseBlockTests
{
    private static long assertions;
    private static int checksum;
    private static readonly FieldInfo Data = typeof(RuntimeTagSet).GetField("m_Data", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo Active = typeof(RuntimeTagSet).GetField("m_ActiveWords", BindingFlags.Instance | BindingFlags.NonPublic);
    private static void Check(bool value) { assertions++; if (!value) throw new Exception("Dense block invariant failed"); }
    private static TagRegistry Registry(int count)
    {
        var settings = ScriptableObject.CreateInstance<GameplayTagSettings>();
        settings.ReplaceAll(Enumerable.Range(0, count).Select(i => new GameplayTagDefinition("T" + i.ToString("D7"), "", "Default", false, true)).ToList(),
            new List<GameplayTagRedirect>(), new List<GameplayTagSource> { new GameplayTagSource("Default", "", false) });
        return TagRegistry.Create(settings);
    }
    private static RuntimeTagSet Set(TagRegistry registry, IEnumerable<int> ids)
    {
        var result = new RuntimeTagSet(registry, 1);
        foreach (int id in ids) result.AddTag(registry.GetTagAt(id));
        return result;
    }
    private static void Same(RuntimeTagSet set, IEnumerable<int> ids)
    {
        int[] expected = ids.Distinct().OrderBy(i => i).ToArray();
        var seen = new List<int>();
        foreach (RuntimeTag tag in set) seen.Add(tag.RuntimeIndex);
        Check(expected.SequenceEqual(seen)); Check(set.Count == expected.Length);
        var iterator = set.GetEnumerator(); while (iterator.MoveNext()) { }
        Check(!iterator.MoveNext()); Check(!iterator.MoveNext());
        var words = (ulong[])Data.GetValue(set);
        int length = set.Registry.WordCount, offset = 0;
        int active = 0;
        for (int i = 0; i < length; i++) if (words[i] != 0) active++;
        Check(active == (int)Active.GetValue(set));
        while (length > 1)
        {
            int parents = (length + 63) >> 6;
            for (int p = 0; p < parents; p++)
            {
                ulong mask = 0;
                for (int bit = 0; bit < 64 && p * 64 + bit < length; bit++)
                    if (words[offset + p * 64 + bit] != 0) mask |= 1UL << bit;
                Check(words[offset + length + p] == mask);
            }
            offset += length; length = parents;
        }
        Check(offset + length == words.Length);
    }
    private static int Main(string[] args)
    {
        foreach (int universe in new[] { 1, 63, 64, 65, 4095, 4096, 4097, 262145 })
        {
            var registry = Registry(universe);
            var random = new Random(928 + universe);
            for (int pass = 0; pass < 48; pass++)
            {
                int[] x = Enumerable.Range(0, pass + 1).Select(_ => random.Next(universe))
                    .Concat(new[] { 0, universe - 1 }).Distinct().ToArray();
                int[] y = Enumerable.Range(0, pass + 1).Select(_ => random.Next(universe))
                    .Concat(x.Where((_, i) => i % 2 == 0)).Distinct().ToArray();
                var a = Set(registry, x); var b = Set(registry, y);
                var output = Set(registry, new[] { universe / 2 });
                RuntimeTagSet.UnionInto(a, b, output); Same(output, x.Concat(y));
                output.CopyFrom(a); Same(output, x);
                output.AppendTags(b); Same(output, x.Concat(y));
                output.RemoveTags(b); Same(output, x.Except(y));
                output.CopyFrom(a); RuntimeTagSet.IntersectionExactInto(output, b, output); Same(output, x.Intersect(y));
                output.CopyFrom(b); RuntimeTagSet.IntersectionExactInto(a, output, output); Same(output, x.Intersect(y));
                a.FilterExactInto(b, output); Same(output, x.Intersect(y));
                a.FilterInto(b, output); Same(output, x.Intersect(y)); // All definitions in this fixture are roots.
                output.Clear(); Same(output, Array.Empty<int>());
                Same(a, x); Same(b, y);
            }
        }
        var large = Registry(262145);
        var left = Set(large, Enumerable.Range(0, 128).Select(i => i * 2048));
        var right = Set(large, Enumerable.Range(0, 128).Select(i => i * 2048 + 1));
        var work = new RuntimeTagSet(large, 1);
        Action action = () => { RuntimeTagSet.UnionInto(left, right, work); work.RemoveTags(right); work.AppendTags(right);
            work.CopyFrom(left); foreach (RuntimeTag tag in work) checksum ^= tag.RuntimeIndex; };
        for (int i = 0; i < 1000; i++) action();
        var bytes = new long[7];
        for (int pass = 0; pass < bytes.Length; pass++)
        {
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) action();
            bytes[pass] = GC.GetAllocatedBytesForCurrentThread() - start;
            Check(bytes[pass] == 0);
        }
        File.WriteAllText(args[0], JsonSerializer.Serialize(new { assertions, checksum, allocated_bytes = bytes, passed = true,
            native_unity_executed = false }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("BLOCK TRANSACTIONS assertions=" + assertions + " prepared allocation=" + string.Join(",", bytes));
        return 0;
    }
}
