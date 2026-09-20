using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using GameplayTags;
using UnityEngine;

internal static class IntegerCosts
{
    private static object sink;
    private static int checksum;
    private static GameplayTagSettings Settings(int count)
    {
        var s = ScriptableObject.CreateInstance<GameplayTagSettings>();
        s.ReplaceAll(Enumerable.Range(0, count).Select(i => new GameplayTagDefinition(
            "Cost.G" + (i / 64).ToString("D4") + ".T" + i.ToString("D5"), "", "Default", false, true)).ToList(),
            new List<GameplayTagRedirect>(), new List<GameplayTagSource> { new GameplayTagSource("Default", "", false) });
        return s;
    }
    private static object Measure(string name, Action action, int iterations)
    {
        action();
        var ns = new double[7];
        var bytes = new double[7];
        for (int pass = 0; pass < 7; pass++)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long before = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) action();
            long elapsed = Stopwatch.GetTimestamp() - before;
            bytes[pass] = (double)(GC.GetAllocatedBytesForCurrentThread() - allocated) / iterations;
            ns[pass] = elapsed * (1e9 / Stopwatch.Frequency) / iterations;
            GC.KeepAlive(sink);
        }
        return new { name, iterations, ns, allocated_bytes = bytes };
    }
    private static object Retained(string name, Func<object> create, int instances)
    {
        var hold = new object[instances];
        long before = GC.GetTotalMemory(true);
        for (int i = 0; i < hold.Length; i++) hold[i] = create();
        long after = GC.GetTotalMemory(true);
        GC.KeepAlive(hold);
        return new { name, instances, estimated_retained_bytes_per_instance = (double)(after - before) / instances };
    }
    private static int Main(string[] args)
    {
        var rows = new List<object>();
        var memory = new List<object>();
        var settings = Settings(10000);
        rows.Add(Measure("registry.create.10000-definitions", () => sink = TagRegistry.Create(settings), 3));
        memory.Add(Retained("registry.snapshot-shared-source-names", () => TagRegistry.Create(settings), 5));
        GameplayTagManager.Initialize(settings, true);
        TagRegistry registry = GameplayTagManager.CurrentRegistry;
        string name = settings.Tags[0].Name;
        RuntimeTag tag = registry.Resolve(name);
        var definition = new GameplayTagContainer(1024);
        for (int i = 0; i < 1024; i++) definition.AddTag(settings.Tags[i].Name);
        foreach (TagSetStorage mode in new[] { TagSetStorage.Sparse, TagSetStorage.Dense })
        {
            rows.Add(Measure("prepare.1024." + mode, () => sink = definition.ToRuntime(registry, 1024, mode), 100));
            rows.Add(Measure("construct.empty.reserve1024." + mode, () => sink = new RuntimeTagSet(registry, 1024, mode), 1000));
            RuntimeTagSet set = definition.ToRuntime(registry, 1024, mode);
            rows.Add(Measure("enumerate.1024." + mode, () => { foreach (RuntimeTag t in set) checksum ^= t.RuntimeIndex; }, 1000));
            memory.Add(Retained("set.reserve1024." + mode, () => new RuntimeTagSet(registry, 1024, mode), 2000));
        }
        rows.Add(Measure("name.request.trimmed", () => sink = registry.Resolve(" " + name + " "), 1000));
        rows.Add(Measure("name.request.unknown-exception", () => { try { registry.Resolve("Missing"); } catch (KeyNotFoundException error) { sink = error; } }, 100));
        var source = GameplayTagQueryExpression.AllTagsMatch().AddTag(GameplayTagManager.RequestTag(name));
        foreach (int depth in new[] { 1, 4, 8, 16 })
        {
            var expression = source;
            for (int i = 1; i < depth; i++) expression = GameplayTagQueryExpression.AllExpressionsMatch().AddExpression(expression);
            var query = new GameplayTagQuery(expression);
            rows.Add(Measure("query.freeze.depth" + depth, () => sink = query.Freeze(registry), 300));
            memory.Add(Retained("query.snapshot.depth" + depth, () => query.Freeze(registry), 2000));
        }
        var result = new { runtime = RuntimeInformation.FrameworkDescription, registry_tags_including_parents = registry.Count,
            cold_and_enumeration = rows, approximate_retention = memory, checksum,
            limitations = "CoreCLR host only. Cumulative allocation is not retained memory. Retention uses forced-GC estimates, excludes pre-existing settings/name strings and shared registry, and is not native/peak memory. Trimmed request includes construction of the padded input and boxing of its return. Freeze excludes already-built authoring graph. No native Unity/IL2CPP data." };
        File.WriteAllText(args[0], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("COLD/MEMORY evidence written: " + args[0]);
        return 0;
    }
}
