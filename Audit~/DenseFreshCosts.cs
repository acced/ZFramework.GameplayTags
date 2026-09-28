using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using GameplayTags;
using UnityEngine;

// Supplement, never a replacement for FourWay: isolate fresh-result allocation from set work.
internal static class DenseFreshCosts
{
    private static object sink;
    private static readonly List<object> Rows = new List<object>();
    private static void Settle()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
    }
    private static void Measure(string name, int universe, int size, string distribution, long payload, Action action, Action verify)
    {
        verify();
        for (int i = 0; i < 100; i++) action();
        Settle();
        var ns = new double[7]; var bytes = new double[7];
        const int iterations = 1000;
        for (int sample = 0; sample < 7; sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) action();
            long elapsed = Stopwatch.GetTimestamp() - start;
            bytes[sample] = (double)(GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
            ns[sample] = elapsed * (1e9 / Stopwatch.Frequency) / iterations;
            GC.KeepAlive(sink);
        }
        verify();
        Rows.Add(new { operation = name, universe, size, distribution, payload_bytes = payload,
            measurement_protocol = "settled-setup-v2", iterations, ns, allocated_bytes = bytes });
    }
    private static int Main(string[] args)
    {
        foreach (int universe in new[] { 10000, 65536, 262144 })
        {
            string[] names = Enumerable.Range(0, universe).Select(i => "Bench.G" + (i / 64).ToString("D4", CultureInfo.InvariantCulture)
                + ".T" + i.ToString("D5", CultureInfo.InvariantCulture)).ToArray();
            var settings = ScriptableObject.CreateInstance<GameplayTagSettings>();
            settings.ReplaceAll(names.Select(n => new GameplayTagDefinition(n, "", "Default", false, true)).ToList(),
                new List<GameplayTagRedirect>(), new List<GameplayTagSource> { new GameplayTagSource("Default", "", false) });
            var registry = TagRegistry.Create(settings);
            foreach (bool scattered in new[] { false, true })
            {
                string[] ordered = (string[])names.Clone();
                if (scattered)
                {
                    var random = new Random(20260920);
                    for (int i = ordered.Length - 1; i > 0; i--) { int j = random.Next(i + 1); string t = ordered[i]; ordered[i] = ordered[j]; ordered[j] = t; }
                }
                foreach (int n in universe == 262144 ? new[] { 1, 8, 32, 128, 1024 } : new[] { 8, 1024 })
                {
                    var a = new RuntimeTagSet(registry, 1); var b = new RuntimeTagSet(registry, 1); var subset = new RuntimeTagSet(registry, 1);
                    for (int i = 0; i < n; i++) { a.AddTag(registry.Resolve(ordered[i])); b.AddTag(registry.Resolve(ordered[i + n / 2])); if ((i & 1) == 0) subset.AddTag(registry.Resolve(ordered[i])); }
                    long payload = a.BufferBytes; string distribution = scattered ? "scattered" : "contiguous";
                    Action inputs = () => { if (a.Count != n || b.Count != n) throw new Exception("Input changed"); };
                    Measure("payload.zero_array", universe, n, distribution, payload, () => sink = new ulong[payload / 8], inputs);
                    Measure("set.reserve_empty", universe, n, distribution, payload, () => sink = new RuntimeTagSet(registry, 1), inputs);
                    Measure("set.copy", universe, n, distribution, payload, () => sink = new RuntimeTagSet(a),
                        () => { inputs(); if (!new RuntimeTagSet(a).SetEquals(a)) throw new Exception("Copy mismatch"); });
                    Measure("set.union", universe, n, distribution, payload, () => sink = RuntimeTagSet.Union(a, b),
                        () => { inputs(); var s = RuntimeTagSet.Union(a, b); if (!s.HasAllExact(a) || !s.HasAllExact(b) || s.Count != n + n / 2) throw new Exception("Union mismatch"); });
                    Measure("set.copy_append", universe, n, distribution, payload, () => { var s = new RuntimeTagSet(a); s.AppendTags(b); sink = s; },
                        () => { inputs(); var s = new RuntimeTagSet(a); s.AppendTags(b); if (s.Count != n + n / 2) throw new Exception("Append mismatch"); });
                    Measure("set.copy_remove", universe, n, distribution, payload, () => { var s = new RuntimeTagSet(a); s.RemoveTags(subset); sink = s; },
                        () => { inputs(); var s = new RuntimeTagSet(a); s.RemoveTags(subset); if (s.Count != n / 2 || s.HasAnyExact(subset)) throw new Exception("Remove mismatch"); });
                }
            }
        }
        Settle(); long before = GC.GetAllocatedBytesForCurrentThread(); sink = new byte[4096];
        long positive = GC.GetAllocatedBytesForCurrentThread() - before;
        if (positive <= 0) throw new Exception("Inactive allocation counter");
        File.WriteAllText(args[0], JsonSerializer.Serialize(new { rows = Rows, positive_control_bytes = positive,
            runtime = RuntimeInformation.FrameworkDescription, native_unity_executed = false,
            note = "Allocation controls are diagnostic, not an algorithm ranking or a subtractable timing floor. No pooling, lazy results, fusion or timing subtraction." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("FRESH COST rows=" + Rows.Count + " positive_control=" + positive);
        return 0;
    }
}
