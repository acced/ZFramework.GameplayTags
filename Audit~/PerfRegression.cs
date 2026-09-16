using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using GameplayTags;
using UnityEngine;

// Managed measurements, not Unity or device acceptance.
internal static class PerfRegression
{
    private static long assertions;
    private static int checksum;
    private static object sink;
    private const int Seed = 9160713;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Setup(IEnumerable<string> names)
    {
        var settings = ScriptableObject.CreateInstance<GameplayTagSettings>();
        settings.ReplaceAll(names.Select(n => new GameplayTagDefinition(n, "", "Default", false, true)).ToList(),
            new List<GameplayTagRedirect>(), new List<GameplayTagSource> { new GameplayTagSource("Default", "", false) });
        GameplayTagManager.Initialize(settings, true);
    }
    private static GameplayTagContainer Container(IEnumerable<string> names)
    {
        var result = new GameplayTagContainer();
        foreach (string name in names) result.AddTag(name);
        return result;
    }
    private static void Same(GameplayTagContainer actual, string[] expected)
    {
        Check(actual.Count == expected.Length, "Intersection count differs from reference");
        for (int i = 0; i < expected.Length; i++) Check(actual[i].Name == expected[i], "Intersection content differs from reference");
    }
    private static void IntersectionCase(string[] leftNames, string[] rightNames)
    {
        var left = Container(leftNames);
        var right = Container(rightNames);
        string[] expected = leftNames.Intersect(rightNames, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Same(left.FilterExact(right), expected);
        Same(GameplayTagContainer.IntersectionExact(left, right), expected);
        var output = new GameplayTagContainer(expected.Length);
        GameplayTagContainer.IntersectionExactInto(left, right, output);
        Same(output, expected);
        Check(output.Capacity == expected.Length, "Into reserved more than the actual result");
        var alias = new GameplayTagContainer(left);
        GameplayTagContainer.IntersectionExactInto(alias, right, alias);
        Same(alias, expected);
        alias.CopyFrom(right);
        GameplayTagContainer.IntersectionExactInto(left, alias, alias);
        Same(alias, expected);
        Same(left, leftNames.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Same(right, rightNames.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }
    private static void Tests()
    {
        string[] names = Enumerable.Range(0, 4096).Select(i => "Set.T" + i.ToString("D5")).ToArray();
        Setup(names);
        var empty = new GameplayTagContainer();
        var tag = GameplayTagManager.RequestTag(names[0]);
        Check(!empty.HasTagExact(tag) && !empty.HasTagExact(GameplayTag.None), "Empty set matched");
        empty.AddTag(tag);
        Check(empty.HasTagExact(tag), "Add did not leave empty path");
        empty.RemoveTag(tag);
        Check(!empty.HasTagExact(tag), "Remove retained an empty-set result");
        foreach (int count in new[] { 0, 1, 2, 4, 8, 9, 16, 32, 128, 512, 1024 })
        {
            string[] left = names.Take(count).ToArray();
            string[][] cases = {
                names.Skip(count / 2).Take(count).ToArray(), left,
                names.Skip(count).Take(count).ToArray(),
                names.Take(count).Where((_, i) => (i & 1) == 0).ToArray(),
                names.Skip(Math.Max(0, count - 1)).Take(1).ToArray()
            };
            foreach (string[] right in cases) { IntersectionCase(left, right); IntersectionCase(right, left); }
        }
        var random = new Random(Seed);
        for (int i = 0; i < 400; i++)
            IntersectionCase(names.Take(257).Where(_ => random.Next(4) == 0).ToArray(),
                names.Skip(random.Next(257)).Take(257).Where(_ => random.Next(3) == 0).ToArray());
        var evens = Container(names.Take(2048).Where((_, i) => (i & 1) == 0));
        var odds = Container(names.Take(2048).Where((_, i) => (i & 1) != 0));
        var result = evens.FilterExact(odds);
        Check(result.Count == 0 && result.Capacity == 0, "Empty intersection allocated an input-sized buffer");
        odds.AddTag(names[0]);
        result = evens.FilterExact(odds);
        Check(result.Count == 1 && result.Capacity <= 4, "Sparse intersection retained an input-sized buffer");
        var reserved = new GameplayTagContainer(1);
        for (int i = 0; i < 1000; i++) evens.FilterExactInto(odds, reserved);
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) evens.FilterExactInto(odds, reserved);
        Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "Actual-result-sized Into allocated");
        Check(reserved.Capacity == 1, "Into lost caller capacity");
        long control = GC.GetAllocatedBytesForCurrentThread();
        sink = new byte[1024];
        Check(GC.GetAllocatedBytesForCurrentThread() > control, "Allocation positive control failed");
    }
    private static object Measure(string name, int size, Action action, int iterations, Func<int> capacity = null)
    {
        for (int i = 0; i < 10000; i++) action();
        var ns = new double[7];
        var bytes = new double[7];
        for (int sample = 0; sample < ns.Length; sample++)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) action();
            long end = Stopwatch.GetTimestamp();
            bytes[sample] = (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)iterations;
            ns[sample] = (end - start) * 1e9 / Stopwatch.Frequency / iterations;
        }
        return new { name, size, iterations, ns, allocated_bytes = bytes, retained_capacity = capacity?.Invoke() };
    }
    private static string Name(int i, int depth) => string.Concat(Enumerable.Repeat("Root.", depth - 1)) + "T" + i.ToString("D5");
    private static List<object> Bench()
    {
        var rows = new List<object>();
        foreach (int depth in new[] { 1, 4, 8 })
        {
            string[] names = Enumerable.Range(0, 10000).Select(i => Name(i, depth)).ToArray();
            Setup(names);
            var empty = new GameplayTagContainer();
            var hit = GameplayTagManager.RequestTag(names[0]);
            var miss = GameplayTagManager.RequestTag(names[9999]);
            rows.Add(Measure("exact.empty.d" + depth, 0, () => { checksum ^= empty.HasTagExact(hit) ? 1 : 0; }, 3000000));
            rows.Add(Measure("exact.miss.d" + depth, 0, () => { checksum ^= empty.HasTagExact(miss) ? 1 : 0; }, 3000000));
            if (depth == 4)
            {
                var small = Container(names.Take(8));
                rows.Add(Measure("hierarchy.miss.d4", 8, () => { checksum ^= small.HasTag(miss) ? 1 : 0; }, 500000));
            }
        }
        string[] batch = Enumerable.Range(0, 4096).Select(i => "Batch.T" + i.ToString("D5")).ToArray();
        Setup(batch);
        foreach (int size in new[] { 1, 2, 4, 8, 9, 16, 32, 128, 512 })
        {
            var a = Container(batch.Take(size));
            var b = Container(batch.Skip(size / 2).Take(size));
            rows.Add(Measure("filter.exact", size, () => { sink = a.FilterExact(b); }, 30000,
                () => ((GameplayTagContainer)sink).Capacity));
        }
        var even = Container(batch.Take(2048).Where((_, i) => (i & 1) == 0));
        var odd = Container(batch.Take(2048).Where((_, i) => (i & 1) != 0));
        rows.Add(Measure("filter.exact.interleaved-empty", 1024, () => { sink = even.FilterExact(odd); }, 2000,
            () => ((GameplayTagContainer)sink).Capacity));
        odd.AddTag(batch[0]);
        rows.Add(Measure("filter.exact.interleaved-one", 1024, () => { sink = even.FilterExact(odd); }, 2000,
            () => ((GameplayTagContainer)sink).Capacity));
        var tail = Container(batch.Skip(1024).Take(1024));
        var prefix = Container(batch.Take(1024));
        rows.Add(Measure("filter.exact.disjoint-ranges", 1024, () => { sink = prefix.FilterExact(tail); }, 2000,
            () => ((GameplayTagContainer)sink).Capacity));
        return rows;
    }
    private static int Main(string[] args)
    {
        try
        {
            Tests();
            var rows = args.Length > 1 && args[1] == "tests-only" ? new List<object>() : Bench();
            File.WriteAllText(args[0], JsonSerializer.Serialize(new {
                runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(), seed = Seed,
                stopwatch_frequency = Stopwatch.Frequency, assertions, checksum, passed = true, rows,
                limitation = "Managed host only. Longer fixed batches supplement, not replace, the original full matrix. No device or statistical-significance claim."
            }, Json));
            Console.WriteLine("FOCUSED passed; assertions=" + assertions + "; rows=" + rows.Count);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
