using System;
using System.Collections.Generic;
using System.Linq;
using GameplayTags;
using UnityEngine;

internal static class PackedLayoutChecks
{
    private static long checks;
    private static void Check(bool value) { checks++; if (!value) throw new Exception("Full-word/direct-layout transition failed at check " + checks); }
    internal static void Run()
    {
        foreach (int size in new[] { 0, 1, 63, 64, 65, 127, 128, 129, 4095, 4096, 4097, 8193 })
        {
            var settings = ScriptableObject.CreateInstance<GameplayTagSettings>();
            settings.ReplaceAll(Enumerable.Range(0, size).Select(i => new GameplayTagDefinition("T" + i.ToString("D8"), "", "Default", false, true)).ToList(),
                new List<GameplayTagRedirect>(), new List<GameplayTagSource> { new GameplayTagSource("Default", "", false) });
            var registry = TagRegistry.Create(settings); var owned = new RuntimeTagSet(registry, size);
            var expected = new HashSet<int>();
            for (int id = 0; id < size; id += 64) { owned.AddTag(registry.GetTagAt(id)); expected.Add(id); }
            Verify(owned, expected);
            var copy = new RuntimeTagSet(owned); Verify(copy, expected);
            if (size > 64)
            {
                owned.RemoveTag(registry.GetTagAt(64)); expected.Remove(64); Verify(owned, expected);
                owned.AddTag(registry.GetTagAt(64)); expected.Add(64); Verify(owned, expected);
            }
            if (size > 0)
            {
                owned.RemoveTag(registry.GetTagAt(0)); expected.Remove(0); Verify(owned, expected);
                owned.AddTag(registry.GetTagAt(0)); expected.Add(0); Verify(owned, expected);
                copy.RemoveTag(registry.GetTagAt(0)); Check(owned.HasTagExact(registry.GetTagAt(0)));
            }
            owned.Clear(); Verify(owned, new HashSet<int>());
            var reversed = new RuntimeTagSet(registry);
            foreach (int id in expected.OrderByDescending(x => x)) reversed.AddTag(registry.GetTagAt(id));
            Verify(reversed, expected);
            foreach (int id in expected.OrderBy(x => x)) Check(reversed.RemoveTag(registry.GetTagAt(id)));
            Verify(reversed, new HashSet<int>());
        }
        Console.WriteLine("LAYOUT_TRANSITIONS assertions=" + checks + " failed=0");
    }
    private static void Verify(RuntimeTagSet set, HashSet<int> expected)
    {
        Check(set.Count == expected.Count);
        for (int id = 0; id < set.Registry.Count; id++) Check(set.HasTagExact(set.Registry.GetTagAt(id)) == expected.Contains(id));
        int seen = 0;
        foreach (RuntimeTag tag in set) { Check(expected.Contains(tag.RuntimeIndex)); seen++; }
        Check(seen == expected.Count);
    }
}
