using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameplayTags;
using GameplayTags.Experiments;

// Fixed-actor, hot-frozen-query comparison. Query freezing and actor construction
// happen before timing; every public match (and mutation in its named row) is charged.
class BridgeProbe
{
    const double TargetMs = 5, MinimumMs = 1;
    static TagRegistry registry;
    static RuntimeTagSet legacy;
    static DirectArraySet direct;
    static FrozenGameplayTagQuery query;
    static RuntimeTag target;
    static RuntimeTag[] members;
    static BitmapLayout layout;
    static bool directCaller, mutation;
    static long expected, sink;
    static string caseName, id;
    static readonly string[] Cases = { "exact-hit", "exact-miss", "parent-hit", "parent-miss", "nested-pass", "nested-fail", "empty-query", "constant-true", "mutate-between-matches" };

    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(id + ": " + message);
    }
    static string Name(int index) => "Owned.T" + index.ToString("D5");
    static GameplayTag Tag(string name) => GameplayTagManager.RequestTag(name);
    static GameplayTagQueryExpression Any(params string[] names)
    {
        var expression = GameplayTagQueryExpression.AnyTagsMatch();
        foreach (var name in names) expression.AddTag(Tag(name));
        return expression;
    }
    static GameplayTagQueryExpression All(params string[] names)
    {
        var expression = GameplayTagQueryExpression.AllTagsMatch();
        foreach (var name in names) expression.AddTag(Tag(name));
        return expression;
    }
    static GameplayTagQueryExpression No(params string[] names)
    {
        var expression = GameplayTagQueryExpression.NoTagsMatch();
        foreach (var name in names) expression.AddTag(Tag(name));
        return expression;
    }
    static FrozenGameplayTagQuery Freeze(string name, int count)
    {
        string hit = Name((count / 2) * 4), miss = Name(1);
        GameplayTagQueryExpression expression = name switch
        {
            "exact-hit" or "mutate-between-matches" => All(hit),
            "exact-miss" => All(miss),
            "parent-hit" => All("Owned"),
            "parent-miss" => All("Absent"),
            "nested-pass" => GameplayTagQueryExpression.AllExpressionsMatch()
                .AddExpression(Any(miss, hit)).AddExpression(No(miss))
                .AddExpression(All(hit, "Owned")),
            "nested-fail" => GameplayTagQueryExpression.AllExpressionsMatch()
                .AddExpression(Any(miss, hit)).AddExpression(All(hit, miss))
                .AddExpression(No(hit)),
            "constant-true" => All(),
            _ => null
        };
        return new GameplayTagQuery(expression).Freeze(registry);
    }
    static void ValidateSets()
    {
        var want = members.Select(t => t.RuntimeIndex).ToArray();
        var old = new List<int>(); foreach (var tag in legacy) old.Add(tag.RuntimeIndex);
        var current = new List<int>(); foreach (var tag in direct) current.Add(tag.RuntimeIndex);
        Check(old.SequenceEqual(want) && current.SequenceEqual(want), "actor membership changed");
        Check(legacy.Count == members.Length && direct.Count == members.Length, "actor Count changed");
        direct.AssertInvariants();
    }
    static void Setup(int count, string name)
    {
        caseName = name; id = $"bridge/{count}/{name}";
        mutation = name == "mutate-between-matches";
        members = Enumerable.Range(0, count).Select(i => registry.Resolve(Name(i * 4))).ToArray();
        target = members[count / 2];
        var oldLayout = layout == BitmapLayout.Micro ? TagSetStorage.Sparse : layout == BitmapLayout.Dense ? TagSetStorage.Dense : TagSetStorage.Auto;
        legacy = new RuntimeTagSet(registry, count, oldLayout);
        foreach (var tag in members) legacy.AddTag(tag);
        direct = DirectArraySet.FromSortedUnique(registry, members, 0, layout);
        query = Freeze(name, count);
        bool want = name is "exact-hit" or "parent-hit" or "nested-pass" or "constant-true" or "mutate-between-matches";
        Check(query.Matches(legacy) == want && direct.Matches(query) == want, "explicit query oracle");
        expected = mutation ? 1 : want ? 64 : 0;
        if (mutation)
        {
            Check(legacy.RemoveTag(target) && direct.RemoveTag(target), "prepared removal failed");
            Check(!query.Matches(legacy) && !direct.Matches(query), "query did not observe removal");
            Check(legacy.AddTag(target) && direct.AddTag(target), "prepared re-add failed");
        }
        Check(Work(1) == expected, "work result");
        ValidateSets();
        Work(16);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Work(32);
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "warmed public matching allocated");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static long Work(int iterations)
    {
        long total = 0;
        if (directCaller)
        {
            if (mutation)
                for (int j = 0; j < iterations; j++)
                {
                    direct.RemoveTag(target);
                    total += direct.Matches(query) ? 1 : 0;
                    direct.AddTag(target);
                    total += direct.Matches(query) ? 1 : 0;
                }
            else
                for (int j = 0; j < iterations; j++)
                    for (int k = 0; k < 64; k++) total += direct.Matches(query) ? 1 : 0;
        }
        else
        {
            if (mutation)
                for (int j = 0; j < iterations; j++)
                {
                    legacy.RemoveTag(target);
                    total += query.Matches(legacy) ? 1 : 0;
                    legacy.AddTag(target);
                    total += query.Matches(legacy) ? 1 : 0;
                }
            else
                for (int j = 0; j < iterations; j++)
                    for (int k = 0; k < 64; k++) total += query.Matches(legacy) ? 1 : 0;
        }
        sink ^= total;
        return total;
    }
    static (double ms, long bytes, int[] gcs) Timed(int iterations)
    {
        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
        long result = Work(iterations);
        long end = Stopwatch.GetTimestamp(), after = GC.GetAllocatedBytesForCurrentThread();
        Check(result == expected * iterations, "sample checksum");
        return ((end - start) * 1000.0 / Stopwatch.Frequency, after - before,
            new[] { GC.CollectionCount(0) - gc0, GC.CollectionCount(1) - gc1, GC.CollectionCount(2) - gc2 });
    }
    static object Measure(int count, string name, bool calibration, int fixedIterations)
    {
        Setup(count, name);
        int iterations = fixedIterations > 0 ? fixedIterations : 16;
        if (calibration)
        {
            var probes = new List<object>();
            while (true)
            {
                var t = Timed(iterations);
                Check(t.bytes == 0, "calibration allocated");
                probes.Add(new { n = iterations, elapsed_ms = t.ms, total_bytes = t.bytes });
                ValidateSets();
                if (t.ms >= TargetMs || iterations >= 1 << 25) break;
                iterations = (int)Math.Min(1 << 25, Math.Max(iterations + 1, Math.Ceiling(iterations * Math.Min(16, TargetMs / Math.Max(t.ms, .001) * 1.2))));
            }
            return new { id, iterations, allocation_bytes_per_op = 0, samples = probes };
        }
        Work(Math.Min(iterations, 1024));
        var samples = new List<object>();
        bool valid = true;
        for (int s = 0; s < 7; s++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var t = Timed(iterations);
            Check(t.bytes == 0, "public match/mutation allocated");
            valid &= t.ms >= MinimumMs;
            samples.Add(new { sample = s, elapsed_ms = t.ms, ns_per_op = t.ms * 1e6 / iterations,
                bytes_per_op = t.bytes / (double)iterations, total_bytes = t.bytes,
                gcs = t.gcs, valid = t.ms >= MinimumMs });
            ValidateSets();
        }
        return new { id, group = "bridge", op = mutation ? "remove-match-add-match" : "matches64",
            count, case_name = name, iterations, valid, samples,
            unit = mutation ? "ns/remove+match+add+match" : "ns/64 public matches",
            input_hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(",", members.Select(t => t.RuntimeIndex))))),
            expected_checksum_per_iteration = expected, query_nodes = query.NodeCount, query_ranges = query.RangeCount,
            legacy_storage = legacy.Storage.ToString(), direct_storage = direct.Layout.ToString(),
            direct_reserved_capacity = direct.ReservedMemberCapacity, direct_payload_bytes = direct.BufferBytes };
    }
    static void Main(string[] args)
    {
        layout = Enum.Parse<BitmapLayout>(args[0]);
        directCaller = args[1] == "direct";
        bool calibration = args[2] == "calibrate";
        var fixedCounts = calibration ? new Dictionary<string, int>() : JsonSerializer.Deserialize<Dictionary<string, int>>(System.IO.File.ReadAllText(args[2]));
        var definitions = Enumerable.Range(0, 16384).SelectMany(i => new[] {
            new GameplayTagDefinition(Name(i), "", "Default", false, true),
            new GameplayTagDefinition("Absent.T" + i.ToString("D5"), "", "Default", false, true) }).ToList();
        var settings = UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        settings.ReplaceAll(definitions, new List<GameplayTagRedirect>(), new List<GameplayTagSource> { new GameplayTagSource("Default", "", false) });
        GameplayTagManager.Initialize(settings, true); registry = GameplayTagManager.CurrentRegistry;
        var rows = new List<object>();
        foreach (int count in new[] { 8, 4096 }) foreach (string name in Cases)
            rows.Add(Measure(count, name, calibration, calibration ? 0 : fixedCounts[$"bridge/{count}/{name}"]));
        string runtimePath = typeof(DirectArraySet).Assembly.Location;
        Console.WriteLine(JsonSerializer.Serialize(new { schema = 1, group = "bridge", caller = directCaller ? "direct" : "legacy", layout = layout.ToString(), calibration,
            runtime_assembly_path = runtimePath, runtime_assembly_sha256 = Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(runtimePath))).ToLowerInvariant(),
            runtime_assembly_mvid = typeof(DirectArraySet).Assembly.ManifestModule.ModuleVersionId.ToString(),
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            vector_accelerated = System.Numerics.Vector.IsHardwareAccelerated, avx2 = System.Runtime.Intrinsics.X86.Avx2.IsSupported,
            gc_latency = System.Runtime.GCSettings.LatencyMode.ToString(),
            registry_count = registry.Count, rows, sink }));
    }
}
