using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using GameplayTags;

internal static partial class Program
{
    private static int s_Sink;

    private static void MakeBenchmarkInputs(int size, int overlapPercent, bool mutated,
        out GameplayTagContainer a, out GameplayTagContainer b)
    {
        var left = new List<string>(); var right = new List<string>();
        for (int i = 0; i < size; i++) left.Add(s_Names[i]);
        int shared = size * overlapPercent / 100;
        for (int i = 0; i < shared; i++) right.Add(s_Names[i * size / Math.Max(shared, 1)]);
        for (int i = shared; i < size; i++) right.Add(s_Names[size + i - shared]);
        if (mutated)
        {
            var random = new Random(710923);
            Shuffle(left, random); Shuffle(right, random);
        }
        a = Set(left); b = Set(right);
        if (mutated)
        {
            for (int i = 0; i < size; i += 4)
            {
                GameplayTag tag = GameplayTagManager.RequestTag(left[i]);
                a.RemoveTag(tag); a.AddTag(tag);
            }
        }
    }

    private static void Measure(string name, Action operation, int iterations)
    {
        for (int i = 0; i < 120; i++) operation();
        AllocatedBytes();
        long before = AllocatedBytes();
        for (int i = 0; i < iterations; i++) operation();
        long bytes = AllocatedBytes() - before;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++) operation();
        long elapsed = Stopwatch.GetTimestamp() - start;
        double ns = elapsed * (1e9 / Stopwatch.Frequency) / iterations;
        Console.WriteLine("RESULT\t{0}\t{1}\t{2}\t{3}", name,
            ns.ToString("F3", CultureInfo.InvariantCulture),
            ((double)bytes / iterations).ToString("F3", CultureInfo.InvariantCulture), iterations);
    }

    private static void RunBenchmarks(string[] args)
    {
        Console.WriteLine("ENV\truntime={0}\tmono={1}\t64bit={2}\ttags={3}",
            Environment.Version, Type.GetType("Mono.Runtime") != null,
            Environment.Is64BitProcess, GameplayTagManager.TagCount);
        foreach (int size in new[] { 64, 1024 })
        foreach (int overlap in new[] { 0, 50, 100 })
        {
            MakeBenchmarkInputs(size, overlap, true, out GameplayTagContainer a, out GameplayTagContainer b);
            var output = new GameplayTagContainer();
            int expectedExplicit = size * overlap / 100;
            GameplayTagContainer.Intersection(output, a, b);
            Check(output.ExplicitTagCount == expectedExplicit, "benchmark result cardinality");
            string scenario = "E" + size + ".Overlap" + overlap;
            Measure(scenario + ".Default.Reused", () =>
            {
                GameplayTagContainer.Intersection(output, a, b);
                s_Sink = unchecked(s_Sink + output.TagCount + output.ExplicitTagCount);
            }, 1000);
            Measure(scenario + ".Default.New", () =>
            {
                var result = GameplayTagContainer.Intersection(a, b);
                s_Sink = unchecked(s_Sink + result.TagCount + result.ExplicitTagCount);
            }, 200);
#if !BASELINE
            var workspace = new GameplayTagIntersectionWorkspace();
            Measure(scenario + ".Workspace.Reused", () =>
            {
                GameplayTagContainer.Intersection(output, a, b, workspace);
                s_Sink = unchecked(s_Sink + output.TagCount + output.ExplicitTagCount);
            }, 1000);
#endif
        }
        MakeBenchmarkInputs(1024, 50, false, out GameplayTagContainer orderedA, out GameplayTagContainer orderedB);
        var orderedOutput = new GameplayTagContainer();
        Measure("E1024.Ordered.Default.Reused", () =>
        {
            GameplayTagContainer.Intersection(orderedOutput, orderedA, orderedB);
            s_Sink = unchecked(s_Sink + orderedOutput.TagCount);
        }, 1000);
        MakeBenchmarkInputs(1024, 50, true, out GameplayTagContainer ma, out GameplayTagContainer mb);
        var copy = new GameplayTagContainer();
        Measure("E1024.Copy.Reused", () => { GameplayTagContainer.Copy(copy, ma); s_Sink += copy.TagCount; }, 1000);
        Measure("E1024.Union.New", () => { var u = GameplayTagContainer.Union(ma, mb); s_Sink += u.TagCount; }, 200);
        GameplayTag hit = GameplayTagManager.RequestTag(s_Names[400]);
        Measure("E1024.Query", () => { if (ma.HasTagExact(hit)) s_Sink++; }, 200000);
        Measure("E1024.RemoveAdd", () => { ma.RemoveTag(hit); ma.AddTag(hit); s_Sink += ma.TagCount; }, 10000);
        var sort = new int[1030];
        Measure("Sort.BCL.Segment", () =>
        {
            for (int i = 0; i < 1024; i++) sort[i + 3] = 1024 - i;
            Array.Sort(sort, 3, 1024);
            s_Sink += sort[3];
        }, 1000);
#if !BASELINE
        Measure("Sort.Specialized.Segment", () =>
        {
            for (int i = 0; i < 1024; i++) sort[i + 3] = 1024 - i;
            Int32Sort.Sort(sort, 3, 1024);
            s_Sink += sort[3];
        }, 1000);
#endif
        Console.WriteLine("SINK\t" + s_Sink);
    }
}
