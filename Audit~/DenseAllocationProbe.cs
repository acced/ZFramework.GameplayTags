using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GameplayTags;
using UnityEngine;

// No stopwatch calls inside allocation regions. Kept separate from timed benchmark telemetry.
internal static class DenseAllocationProbe
{
    private static object sink;
    private static int checksum;
    private static int failures;
    private static readonly List<object> rows = new List<object>();
    private static void Measure(string operation, int universe, int members, Action action)
    {
        const int iterations = 10000;
        for (int i = 0; i < 2000; i++) action();
        var bytes = new long[3];
        for (int pass = 0; pass < bytes.Length; pass++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < iterations; i++) action();
            bytes[pass] = GC.GetAllocatedBytesForCurrentThread() - before;
            if (bytes[pass] != 0) failures++;
        }
        rows.Add(new { operation, universe, members, iterations, bytes });
    }
    private static int Main(string[] args)
    {
        long start = GC.GetAllocatedBytesForCurrentThread(); sink = new byte[4096];
        long positiveControl = GC.GetAllocatedBytesForCurrentThread() - start;
        if (positiveControl <= 0) throw new Exception("Inactive allocation counter");
        foreach (int universe in new[] { 10000, 65536, 262144 })
        {
            var settings = ScriptableObject.CreateInstance<GameplayTagSettings>();
            settings.ReplaceAll(Enumerable.Range(0, universe).Select(i => new GameplayTagDefinition("T" + i.ToString("D7"), "", "Default", false, true)).ToList(),
                new List<GameplayTagRedirect>(), new List<GameplayTagSource> { new GameplayTagSource("Default", "", false) });
            TagRegistry registry = TagRegistry.Create(settings);
            foreach (int count in new[] { 0, 1, 8, 32, 128, 1024, 4096 })
            {
                var a = new RuntimeTagSet(registry, 1); var b = new RuntimeTagSet(registry, 1);
                var output = new RuntimeTagSet(registry, 1); var work = new RuntimeTagSet(registry, 1);
                int stride = Math.Max(1, universe / Math.Max(1, count));
                for (int i = 0; i < count; i++)
                {
                    RuntimeTag tag = registry.GetTagAt(i * stride);
                    a.AddTag(tag); if ((i & 1) == 0) b.AddTag(tag);
                }
                RuntimeTag query = registry.GetTagAt(universe / 2);
                Measure("exact", universe, count, () => checksum ^= a.HasTagExact(query) ? 1 : 0);
                Measure("union-into", universe, count, () => { RuntimeTagSet.UnionInto(a, b, output); checksum ^= output.Count; });
                Measure("copy-append-reuse", universe, count, () => { work.CopyFrom(a); work.AppendTags(b); checksum ^= work.Count; });
                Measure("copy-remove-reuse", universe, count, () => { work.CopyFrom(a); work.RemoveTags(b); checksum ^= work.Count; });
            }
        }
        File.WriteAllText(args[0], JsonSerializer.Serialize(new { failures, positive_control_bytes = positiveControl, checksum, rows,
            limitation = "Allocation-only prepared-loop checks, no stopwatch in the region; raw timed-loop telemetry is still preserved independently." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("ALLOCATION-ONLY cases=" + rows.Count + " failures=" + failures + " positive_control=" + positiveControl);
        return failures == 0 ? 0 : 1;
    }
}
