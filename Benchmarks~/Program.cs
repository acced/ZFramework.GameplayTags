using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using GameplayTags;

internal static class Program
{
    private const int UniverseSize = 4096;
    private const int QueryCount = 4096;
    private static int sink;
    private static bool quick;
    private static readonly List<Measurement> Results = new List<Measurement>();
    private static readonly List<MemoryMeasurement> MemoryResults = new List<MemoryMeasurement>();

    private static int Main(string[] arguments)
    {
        string output = null;
        for (int i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] == "--quick") quick = true;
            else if (arguments[i] == "--out" && i + 1 < arguments.Length) output = arguments[++i];
            else throw new ArgumentException("Unknown benchmark argument: " + arguments[i]);
        }
        FixtureRegistration.Register(Fixtures());
        Console.WriteLine("size,depth,operation,unit,median_ns,min_ns,max_ns,allocated_bytes");
        foreach (int size in new[] { 4, 16, 128, 1024 }) Benchmark(size);
        foreach (int depth in new[] { 32, 128 }) BenchmarkDepth(depth);
        Report report = new Report
        {
#if BASELINE
            Variant = "baseline-da8a6d2",
#else
            Variant = "rewrite",
#endif
            Framework = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            StopwatchFrequency = Stopwatch.Frequency,
            Mode = quick ? "smoke" : "full",
            RegisteredLeafUniverse = UniverseSize,
            QuerySequenceLength = QueryCount,
            Samples = quick ? 3 : 7,
            TieredCompilation = false,
            ServerGC = System.Runtime.GCSettings.IsServerGC,
            Checksum = Volatile.Read(ref sink),
            Measurements = Results,
            RetainedMemory = MemoryResults
        };
        if (output != null)
        {
            File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Saved benchmark report: " + output);
        }
        Console.WriteLine("Observed checksum: " + report.Checksum);
        return 0;
    }

    private static IEnumerable<string> Fixtures()
    {
        for (int i = 0; i < UniverseSize; i++) yield return Name(i);
        yield return "Other.Leaf";
        yield return DeepName(32);
        yield return DeepName(128);
    }

    private static string Name(int index) { return "Bench.G" + (index % 32).ToString("D2") + ".T" + index.ToString("D4"); }
    private static string DeepName(int depth) { return "Deep" + depth + "." + string.Join(".", Enumerable.Range(1, depth - 1).Select(i => "N" + i)); }

    private static GameplayTag[] Sequence(GameplayTag[] values, int seed)
    {
        GameplayTag[] result = new GameplayTag[QueryCount];
        Random random = new Random(seed);
        for (int i = 0; i < result.Length; i++) result[i] = values[random.Next(values.Length)];
        return result;
    }

    private static void Benchmark(int size)
    {
        GameplayTagContainer container = new GameplayTagContainer();
        GameplayTagCountContainer counted = new GameplayTagCountContainer();
        GameplayTag[] hits = new GameplayTag[size];
        GameplayTag[] misses = new GameplayTag[size];
        GameplayTag[] parents = new GameplayTag[size];
        GameplayTag[] missingParents = new GameplayTag[size];
        for (int i = 0; i < size; i++)
        {
            // A permutation spreads accesses through the fixed registry. The
            // disjoint even/odd sets also occupy disjoint parent groups.
            int index = ((i * 977) & 2047) * 2;
            hits[i] = GameplayTagManager.RequestTag(Name(index));
            misses[i] = GameplayTagManager.RequestTag(Name(index + 1));
            parents[i] = hits[i].ParentTag;
            missingParents[i] = misses[i].ParentTag;
            container.AddTag(hits[i]); counted.AddTag(hits[i]);
        }
        GameplayTag[] hitQueries = Sequence(hits, 93247);
        GameplayTag[] missQueries = Sequence(misses, 74183);
        GameplayTag[] parentQueries = Sequence(parents, 21791);
        GameplayTag[] missingParentQueries = Sequence(missingParents, 81391);
        GameplayTag[] mixedQueries = new GameplayTag[QueryCount];
        Random mixer = new Random(7193);
        for (int i = 0; i < mixedQueries.Length; i++) mixedQueries[i] = mixer.Next(2) == 0 ? hitQueries[i] : missQueries[i];
        GameplayTag root = GameplayTagManager.RequestTag("Bench");

        // Semantic preflight is not timed. Benchmarking methods known to have
        // different or broken baseline semantics (HasAny/All/intersection/events)
        // is intentionally outside this comparison.
        for (int i = 0; i < size; i++)
        {
            Require(container.HasTagExact(hits[i]) && !container.HasTagExact(misses[i]), "exact hit/miss");
            Require(container.HasTag(parents[i]) && !container.HasTag(missingParents[i]), "parent hit/miss");
            Require(root.IsParentOf(hits[i]) && hits[i].IsChildOf(root), "strict hierarchy");
        }
        int expectedClosure = size + parents.Distinct().Count() + 1;
        Require(container.ExplicitTagCount == size && container.TagCount == expectedClosure, "fixture closure");

        int reads = quick ? 20000 : 250000;
        int relations = quick ? 2000 : 20000;
        int mutations = quick ? 2000 : 25000;
        Measure("HasTagExact/hit", "call", size, reads, n =>
        {
            int result = 0;
            for (int i = 0; i < n; i++) if (container.HasTagExact(hitQueries[i & (QueryCount - 1)])) result++;
            Volatile.Write(ref sink, unchecked(Volatile.Read(ref sink) * 16777619 + result));
        });
        Measure("HasTagExact/miss", "call", size, reads, n =>
        {
            int result = 0;
            for (int i = 0; i < n; i++) if (container.HasTagExact(missQueries[i & (QueryCount - 1)])) result++;
            Volatile.Write(ref sink, unchecked(Volatile.Read(ref sink) * 16777619 + result));
        });
        Measure("HasTagExact/mixed", "call", size, reads, n =>
        {
            int result = 0;
            for (int i = 0; i < n; i++) if (container.HasTagExact(mixedQueries[i & (QueryCount - 1)])) result++;
            Volatile.Write(ref sink, unchecked(Volatile.Read(ref sink) * 16777619 + result));
        });
        Measure("HasTag/parent-hit", "call", size, reads, n =>
        {
            int result = 0;
            for (int i = 0; i < n; i++) if (container.HasTag(parentQueries[i & (QueryCount - 1)])) result++;
            Volatile.Write(ref sink, unchecked(Volatile.Read(ref sink) * 16777619 + result));
        });
        Measure("HasTag/parent-miss", "call", size, reads, n =>
        {
            int result = 0;
            for (int i = 0; i < n; i++) if (container.HasTag(missingParentQueries[i & (QueryCount - 1)])) result++;
            Volatile.Write(ref sink, unchecked(Volatile.Read(ref sink) * 16777619 + result));
        });
        Measure("IsParentOf/hit", "call", size, relations, n =>
        {
            int result = 0;
            for (int i = 0; i < n; i++) if (root.IsParentOf(hitQueries[i & (QueryCount - 1)])) result++;
            Volatile.Write(ref sink, unchecked(Volatile.Read(ref sink) * 16777619 + result));
        });
        Measure("IsChildOf/hit", "call", size, reads, n =>
        {
            int result = 0;
            for (int i = 0; i < n; i++) if (hitQueries[i & (QueryCount - 1)].IsChildOf(root)) result++;
            Volatile.Write(ref sink, unchecked(Volatile.Read(ref sink) * 16777619 + result));
        });
        Measure("RemoveAdd/existing", "pair", size, mutations, n =>
        {
            for (int i = 0; i < n; i++)
            {
                GameplayTag tag = hitQueries[i & (QueryCount - 1)];
                container.RemoveTag(tag); container.AddTag(tag);
            }
            Volatile.Write(ref sink, unchecked(Volatile.Read(ref sink) * 16777619 + container.TagCount));
        });
        Measure("AddRemove/absent", "pair", size, mutations, n =>
        {
            for (int i = 0; i < n; i++)
            {
                GameplayTag tag = missQueries[i & (QueryCount - 1)];
                container.AddTag(tag); container.RemoveTag(tag);
            }
            Volatile.Write(ref sink, unchecked(Volatile.Read(ref sink) * 16777619 + container.TagCount));
        });
        Measure("Count/AddRemove-existing", "pair", size, mutations, n =>
        {
            for (int i = 0; i < n; i++)
            {
                GameplayTag tag = hitQueries[i & (QueryCount - 1)];
                counted.AddTag(tag); counted.RemoveTag(tag);
            }
            Volatile.Write(ref sink, unchecked(Volatile.Read(ref sink) * 16777619 + counted.TagCount));
        });
        Measure("Enumerate/explicit", "traversal", size, quick ? 1000 : 10000, n =>
        {
            int result = 0;
            for (int i = 0; i < n; i++)
                foreach (GameplayTag tag in container.GetExplicitTags()) result = unchecked(result + tag.RuntimeIndex);
            Volatile.Write(ref sink, unchecked(Volatile.Read(ref sink) * 16777619 + result));
        });
        int constructions = quick ? Math.Max(16, 1024 / size) : Math.Max(128, 8192 / size);
        Measure("Construct/fill", "container", size, constructions, n =>
        {
            int result = 0;
            for (int i = 0; i < n; i++)
            {
                GameplayTagContainer fresh = new GameplayTagContainer();
                for (int j = 0; j < hits.Length; j++) fresh.AddTag(hits[j]);
                result = unchecked(result + fresh.TagCount);
            }
            Volatile.Write(ref sink, unchecked(Volatile.Read(ref sink) * 16777619 + result));
        });
        Require(container.ExplicitTagCount == size && container.TagCount == expectedClosure, "mutation pair closure preserved");
        foreach (GameplayTag hit in hits)
        {
            Require(container.HasTagExact(hit), "mutation membership preserved");
            Require(counted.GetExplicitTagCount(hit) == 1, "count pair multiplicity preserved");
        }
        foreach (GameplayTag miss in misses) Require(!container.HasTagExact(miss), "temporary tag removed");
        MeasureRetained(size, hits);
    }

    private static void Require(bool condition, string operation)
    {
        if (!condition) throw new InvalidOperationException("Benchmark preflight/postflight failed: " + operation);
    }

    private static void BenchmarkDepth(int depth)
    {
        GameplayTag leaf = GameplayTagManager.RequestTag(DeepName(depth));
        GameplayTagContainer container = new GameplayTagContainer();
        GameplayTagContainer requirement = new GameplayTagContainer();
        container.AddTag(leaf); requirement.AddTag(leaf);
        Require(container.ExplicitTagCount == 1 && container.TagCount == depth, "deep fixture closure");
        // The original singleton HasAllExact path returns immediately after its
        // first successful binary search; this specific case has matching semantics.
        Require(container.HasAllExact(requirement), "singleton exact requirement");
        int reads = quick ? 20000 : 250000;
        Measure("Depth/HasTagExact", "call", 1, reads, n =>
        {
            int result = 0;
            for (int i = 0; i < n; i++) if (container.HasTagExact(leaf)) result++;
            Volatile.Write(ref sink, unchecked(Volatile.Read(ref sink) * 16777619 + result));
        }, depth);
        Measure("Depth/HasAllExact-singleton", "call", 1, reads, n =>
        {
            int result = 0;
            for (int i = 0; i < n; i++) if (container.HasAllExact(requirement)) result++;
            Volatile.Write(ref sink, unchecked(Volatile.Read(ref sink) * 16777619 + result));
        }, depth);
        Measure("Depth/Enumerate-explicit", "traversal", 1, reads, n =>
        {
            int result = 0;
            for (int i = 0; i < n; i++) foreach (GameplayTag tag in container.GetExplicitTags()) result = unchecked(result + tag.RuntimeIndex);
            Volatile.Write(ref sink, unchecked(Volatile.Read(ref sink) * 16777619 + result));
        }, depth);
        Measure("Depth/RemoveAdd", "pair", 1, quick ? 2000 : 20000, n =>
        {
            for (int i = 0; i < n; i++) { container.RemoveTag(leaf); container.AddTag(leaf); }
            Volatile.Write(ref sink, unchecked(Volatile.Read(ref sink) * 16777619 + container.TagCount));
        }, depth);
        Require(container.ExplicitTagCount == 1 && container.TagCount == depth && container.HasTagExact(leaf), "deep mutation postflight");
        MeasureRetained(1, new[] { leaf }, depth);
    }

    private static void Measure(string name, string unit, int size, int iterations, Action<int> action, int depth = 3)
    {
        // Warm every branch and pool used by this operation. Invocation overhead
        // is one Action call per timed batch, not one delegate call per tag.
        action(Math.Min(iterations, 20000));
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        int samples = quick ? 3 : 7;
        double[] times = new double[samples], allocations = new double[samples];
        for (int sample = 0; sample < samples; sample++)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            action(iterations);
            long stop = Stopwatch.GetTimestamp();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            times[sample] = (stop - start) * (1_000_000_000.0 / Stopwatch.Frequency) / iterations;
            allocations[sample] = (double)bytes / iterations;
        }
        double[] originalTimes = (double[])times.Clone(), originalAllocations = (double[])allocations.Clone();
        Array.Sort(times); Array.Sort(allocations);
        Measurement measurement = new Measurement
        {
            Operation = name, Unit = unit, ExplicitTagCount = size, HierarchyDepth = depth, IterationsPerSample = iterations,
            MedianNanoseconds = times[samples / 2], MinNanoseconds = times[0], MaxNanoseconds = times[samples - 1],
            AllocatedBytesPerOperation = allocations[samples / 2], NanosecondSamples = originalTimes,
            AllocatedByteSamples = originalAllocations
        };
        Results.Add(measurement);
        Console.WriteLine(string.Join(",", size, depth, name, unit, F(measurement.MedianNanoseconds), F(measurement.MinNanoseconds),
            F(measurement.MaxNanoseconds), F(measurement.AllocatedBytesPerOperation)));
    }

    private static void MeasureRetained(int size, GameplayTag[] tags, int depth = 3)
    {
        // Report retained live memory separately from bytes allocated during
        // construction (which include discarded growth arrays). The holder array
        // exists before the baseline GC, so it is not included in the delta.
        int cohort = quick ? 32 : 128;
        double[] retained = new double[3];
        for (int sample = 0; sample < retained.Length; sample++)
        {
            GameplayTagContainer[] holders = new GameplayTagContainer[cohort];
            long before = GC.GetTotalMemory(true);
            for (int i = 0; i < holders.Length; i++)
            {
                GameplayTagContainer container = new GameplayTagContainer();
                foreach (GameplayTag tag in tags) container.AddTag(tag);
                holders[i] = container;
            }
            long after = GC.GetTotalMemory(true);
            retained[sample] = (double)(after - before) / cohort;
            GC.KeepAlive(holders);
        }
        Array.Sort(retained);
        MemoryResults.Add(new MemoryMeasurement { ExplicitTagCount = size, HierarchyDepth = depth, Cohort = cohort,
            MedianRetainedBytesPerContainer = retained[1], MinRetainedBytesPerContainer = retained[0], MaxRetainedBytesPerContainer = retained[2] });
    }

    private static string F(double number) { return number.ToString("F3", CultureInfo.InvariantCulture); }

    private sealed class Measurement
    {
        public string Operation { get; set; }
        public string Unit { get; set; }
        public int ExplicitTagCount { get; set; }
        public int HierarchyDepth { get; set; }
        public int IterationsPerSample { get; set; }
        public double MedianNanoseconds { get; set; }
        public double MinNanoseconds { get; set; }
        public double MaxNanoseconds { get; set; }
        public double AllocatedBytesPerOperation { get; set; }
        public double[] NanosecondSamples { get; set; }
        public double[] AllocatedByteSamples { get; set; }
    }

    private sealed class MemoryMeasurement
    {
        public int ExplicitTagCount { get; set; }
        public int HierarchyDepth { get; set; }
        public int Cohort { get; set; }
        public double MedianRetainedBytesPerContainer { get; set; }
        public double MinRetainedBytesPerContainer { get; set; }
        public double MaxRetainedBytesPerContainer { get; set; }
    }

    private sealed class Report
    {
        public string Variant { get; set; }
        public string Framework { get; set; }
        public string OS { get; set; }
        public string Architecture { get; set; }
        public string Mode { get; set; }
        public int RegisteredLeafUniverse { get; set; }
        public int QuerySequenceLength { get; set; }
        public int Samples { get; set; }
        public long StopwatchFrequency { get; set; }
        public bool TieredCompilation { get; set; }
        public bool ServerGC { get; set; }
        public int Checksum { get; set; }
        public List<Measurement> Measurements { get; set; }
        public List<MemoryMeasurement> RetainedMemory { get; set; }
    }
}
