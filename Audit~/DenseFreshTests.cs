using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using GameplayTags;
using UnityEngine;

// Test fresh public results as mutable independent sets, not just equal initial Count values.
internal static class DenseFreshTests
{
    private static long assertions;
    private static readonly FieldInfo Data = typeof(RuntimeTagSet).GetField("m_Data", BindingFlags.Instance | BindingFlags.NonPublic);
    private static void Check(bool value) { assertions++; if (!value) throw new Exception("Fresh Dense result regression"); }
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
    private static void Verify(RuntimeTagSet set, IEnumerable<int> ids)
    {
        var expected = new HashSet<int>(ids);
        var seen = new List<int>();
        foreach (RuntimeTag tag in set) seen.Add(tag.RuntimeIndex);
        Check(seen.Count == set.Count && seen.Count == expected.Count);
        Check(seen.SequenceEqual(expected.OrderBy(i => i)));
        ulong[] data = (ulong[])Data.GetValue(set);
        if (data.Length == 0) { Check(set.Count == 0); return; }
        int length = set.Registry.WordCount, offset = 0;
        for (int word = 0; word < length; word++)
        {
            ulong mask = 0;
            for (int bit = 0; bit < 64; bit++) if (expected.Contains(word * 64 + bit)) mask |= 1UL << bit;
            Check(data[word] == mask);
        }
        while (length > 1)
        {
            int parents = (length + 63) >> 6;
            for (int p = 0; p < parents; p++)
            {
                ulong mask = 0;
                for (int bit = 0; bit < 64 && p * 64 + bit < length; bit++)
                    if (data[offset + p * 64 + bit] != 0) mask |= 1UL << bit;
                Check(data[offset + length + p] == mask);
            }
            offset += length; length = parents;
        }
        Check(offset + length == data.Length);
    }
    private static int Main(string[] args)
    {
        foreach (int universe in new[] { 0, 1, 63, 64, 65, 4095, 4096, 4097, 262145 })
        {
            var registry = Registry(universe);
            var empty = new RuntimeTagSet(registry, 1);
            var emptyCopy = new RuntimeTagSet(empty);
            Check(!ReferenceEquals(emptyCopy, empty));
            Check(emptyCopy.Capacity == 0);
            Verify(emptyCopy, Array.Empty<int>());
            if (universe == 0) continue;
            int[] sizes = { 1, 8, 32, 128, 1024, 4096 };
            foreach (int requested in sizes)
            foreach (bool scattered in new[] { false, true })
            {
                int n = Math.Min(requested, universe);
                var random = new Random(928 + universe + requested);
                int[] x = Enumerable.Range(0, n).Select(i => scattered ? random.Next(universe) : i).Distinct().ToArray();
                int[] y = Enumerable.Range(0, n).Select(i => scattered ? random.Next(universe) : (i + n / 2) % universe).Distinct().ToArray();
                var a = Set(registry, x); var b = Set(registry, y);
                var copy = new RuntimeTagSet(a);
                Check(!ReferenceEquals(Data.GetValue(copy), Data.GetValue(a)));
                Verify(copy, x);
                copy.RemoveTags(b); Verify(copy, x.Except(y)); Verify(a, x); Verify(b, y);
                copy.Clear(); copy.AddTag(registry.GetTagAt(universe - 1)); Verify(a, x);
                var union = RuntimeTagSet.Union(a, b);
                Check(!ReferenceEquals(Data.GetValue(union), Data.GetValue(a)) && !ReferenceEquals(Data.GetValue(union), Data.GetValue(b)));
                Verify(union, x.Concat(y)); union.Clear(); Verify(a, x); Verify(b, y);
                var self = RuntimeTagSet.Union(a, a); Verify(self, x);
                self.Clear(); Verify(a, x);
                var oneEmpty = RuntimeTagSet.Union(empty, a); Verify(oneEmpty, x);
                oneEmpty.Clear(); Verify(a, x);
                Verify(RuntimeTagSet.Union(a, empty), x);
            }
            var foreign = new RuntimeTagSet(Registry(universe));
            bool rejected = false;
            try { RuntimeTagSet.Union(empty, foreign); } catch (ArgumentException) { rejected = true; }
            Check(rejected);
        }
        bool nullRejected = false;
        try { new RuntimeTagSet((RuntimeTagSet)null); } catch (ArgumentNullException) { nullRejected = true; }
        Check(nullRejected);
        File.WriteAllText(args[0], JsonSerializer.Serialize(new { assertions, failures = 0, passed = true, native_unity_executed = false }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("FRESH RESULT assertions=" + assertions + " failures=0");
        return 0;
    }
}
