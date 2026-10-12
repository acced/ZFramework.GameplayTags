using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using GameplayTags;

// Complements the unchanged Evolution/Bulk/Intersection suites. All operands are
// prepared outside timing; new-result rows deliberately include their allocation.
internal static class FinalConsolidationProbe
{
    private static object Sink;
    private static long Checksum;
    private static readonly Func<long> Allocated = MakeCounter();
    private static GameplayTag[] Tags;
    private static GameplayTag Deep;
    private static readonly List<Case> Cases = new List<Case>();
    private sealed class Case
    {
        internal string Name;
        internal Action Run;
        internal Action Check;
    }
    private static Func<long> MakeCounter()
    {
        MethodInfo method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
        if (method == null) throw new NotSupportedException("A real allocation counter is required.");
        return (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
    private static string Name(int i) => "Final.G" + (i % 32).ToString("D2") + ".L" + i.ToString("D4");
    private static void Register()
    {
        var declarations = new List<GameplayTagRegistration>();
        for (int i = 0; i < 4096; i++) declarations.Add(new GameplayTagRegistration(Name(i)));
        string deep = "Deep" + string.Concat(Enumerable.Repeat(".N", 511));
        declarations.Add(new GameplayTagRegistration(deep));
        GameplayTagManager.Initialize(declarations.ToArray());
        Tags = Enumerable.Range(0, 4096).Select(i => GameplayTagManager.RequestTag(Name(i))).ToArray();
        Deep = GameplayTagManager.RequestTag(deep);
    }
    private static GameplayTagContainer Set(int start, int count)
    {
        var result = new GameplayTagContainer();
        // Odd stride is coprime to the power-of-two sizes used below.
        for (int i = 0; i < count; i++) result.AddTag(Tags[start + ((i * 719) & (count - 1))]);
        return result;
    }
    private static void Observe(GameplayTagContainer result)
    {
        Checksum = unchecked(Checksum + result.TagCount + result.ExplicitTagCount);
        Sink = result;
    }
    private static void Add(string name, Action action, Action check = null)
    {
        Cases.Add(new Case { Name = name, Run = action, Check = check });
    }
    private static void Fixtures()
    {
        foreach (int n in new[] { 4, 1024 })
        {
            string key = "n" + n + "/";
            var a = Set(0, n); var b = Set(n / 2, n);
            var output = new GameplayTagContainer();
            var oversized = new GameplayTagContainer(n * 16);
            var counted = new GameplayTagCountContainer();
            foreach (GameplayTag tag in a.GetExplicitTags()) counted.AddTag(tag, 3);
            GameplayTag hit = Tags[n / 2], absent = Tags[4095];
            GameplayTag parent = hit.ParentTag;
            Add(key + "query-exact-hit", () => { if (a.HasTagExact(hit)) Checksum++; });
            Add(key + "query-exact-miss", () => { if (a.HasTagExact(absent)) Checksum++; });
            Add(key + "query-parent", () => { if (a.HasTag(parent)) Checksum++; });
            Add(key + "enumerate-explicit", () => { foreach (var t in a.GetExplicitTags()) Checksum += t.RuntimeIndex; });
            Add(key + "enumerate-closure", () => { foreach (var t in a.GetTags()) Checksum += t.RuntimeIndex; });
            Add(key + "duplicate-add", () => { a.AddTag(hit); Observe(a); });
            Add(key + "remove-add", () => { a.RemoveTag(hit); a.AddTag(hit); Observe(a); }, () => Require(a.HasTagExact(hit), "restore pair"));
            Add(key + "new-add-remove", () => { a.AddTag(absent); a.RemoveTag(absent); Observe(a); });
            Add(key + "build-new", () => { var c = Set(0, n); Observe(c); });
            Add(key + "copy-reused", () => { GameplayTagContainer.Copy(output, a); Observe(output); });
            Add(key + "copy-oversized", () => { GameplayTagContainer.Copy(oversized, a); Observe(oversized); });
            Add(key + "union-factory", () => Observe(GameplayTagContainer.Union(a, b)));
            Add(key + "union-reused", () => { GameplayTagContainer.Union(output, a, b); Observe(output); });
            // Strong old-API control, not only factory-plus-Copy.
            Add(key + "union-copy-addtags", () => { GameplayTagContainer.Copy(output, a); output.AddTags(b); Observe(output); });
            Add(key + "count-existing-add-remove", () => { counted.AddTag(hit); counted.RemoveTag(hit); Checksum += counted.GetTagCount(hit); });
            Add(key + "count-lookup", () => { Checksum += counted.GetTagCount(hit); });
            foreach (int overlap in new[] { 0, 50, 100 })
            {
                var right = Set(n * (100 - overlap) / 100, n);
                var result = new GameplayTagContainer();
                var workspace = new GameplayTagIntersectionWorkspace();
                int expected = n * overlap / 100;
                Add(key + "intersection-" + overlap + "-reused", () => { GameplayTagContainer.Intersection(result, a, right); Observe(result); },
                    () => Require(result.ExplicitTagCount == expected, "intersection count"));
                Add(key + "intersection-" + overlap + "-workspace", () => { GameplayTagContainer.Intersection(result, a, right, workspace); Observe(result); },
                    () => Require(result.ExplicitTagCount == expected, "workspace count"));
            }
            Add(key + "copy-clear", () => { GameplayTagContainer.Copy(output, a); output.Clear(); Observe(output); });
        }
        var buff = new GameplayTagCountContainer(); var tag0 = Tags[0];
        buff.RegisterTagEventCallback(tag0, GameplayTagEventType.AnyCountChange, (t, count) => { Checksum += count + t.RuntimeIndex; });
        buff.OnAnyTagCountChange += (t, count) => { Checksum += count + t.RuntimeIndex; };
        Add("buff/lifetime-global-and-leaf", () => { buff.AddTag(tag0); buff.RemoveTag(tag0); }, () => Require(buff.IsEmpty, "buff drain"));
        var deepBuff = new GameplayTagCountContainer(); deepBuff.AddTag(Deep, 2);
        deepBuff.OnAnyTagCountChange += (t, count) => { Checksum += count; };
        Add("buff/deep512-existing", () => { deepBuff.AddTag(Deep); deepBuff.RemoveTag(Deep); }, () => Require(deepBuff.GetExplicitTagCount(Deep) == 2, "deep buff"));
        Add("build/deep512-single", () => { var c = new GameplayTagContainer(); c.AddTag(Deep); Observe(c); });
        var pooled = GameplayTagContainerPool.Get(); GameplayTagContainerPool.Release(pooled);
        Add("pool/get-release", () => { var c = GameplayTagContainerPool.Get(); Checksum += c.TagCount; GameplayTagContainerPool.Release(c); });
    }
    private static void ValidateStorage(GameplayTagContainer value)
    {
        var explicitNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in value.GetExplicitTags()) Require(explicitNames.Add(tag.Name), "duplicate explicit");
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string name in explicitNames)
        {
            string path = name;
            for (;;)
            {
                totals.TryGetValue(path, out int count); totals[path] = count + 1;
                int dot = path.LastIndexOf('.'); if (dot < 0) break;
                path = path.Substring(0, dot);
            }
        }
        Require(value.TagCount == totals.Count, "closure count");
        TagStorage storage = value.Indices.Storage;
        foreach (var pair in totals)
        {
            var tag = GameplayTagManager.RequestTag(pair.Key);
            Require(value.HasTag(tag) && value.HasTagExact(tag) == explicitNames.Contains(pair.Key), "membership");
            Require(storage.GetCount(tag.RuntimeIndex) == pair.Value, "contribution count");
        }
    }
    private static void Tests()
    {
        foreach (int capacity in new[] { 0, 8, 9, 32, 4096 })
        {
            var set = new GameplayTagContainer(capacity);
            for (int cycle = 0; cycle < 8; cycle++)
            {
                set.AddTag(Deep); ValidateStorage(set); set.RemoveTag(Deep);
                Require(set.IsEmpty && set.TagCount == 0, "singleton removal");
                for (int i = 0; i < 300; i++) set.AddTag(Tags[(i * 17 + cycle) % Tags.Length]);
                ValidateStorage(set);
                for (int i = 0; i < 300; i += 2) set.RemoveTag(Tags[(i * 17 + cycle) % Tags.Length]);
                ValidateStorage(set); set.Clear();
                for (int i = 0; i < 17; i++) set.AddTag(Tags[4095 - i]);
                ValidateStorage(set); set.Clear();
            }
        }
        // Force probe clusters across slot zero and multiple growth boundaries.
        var collided = new GameplayTagContainer();
        var chosen = Tags.Where(t => { uint h = unchecked((uint)t.RuntimeIndex * 0x9e3779b9u); return ((h ^ (h >> 16)) & 31) == 31; }).Take(96).ToArray();
        foreach (var tag in chosen) { collided.AddTag(tag); ValidateStorage(collided); }
        foreach (var tag in chosen.Reverse()) { collided.RemoveTag(tag); ValidateStorage(collided); }
        Require(collided.IsEmpty, "cluster deletion");
        var workspace = new GameplayTagIntersectionWorkspace(); var output = new GameplayTagContainer();
        var one = new GameplayTagCountContainer(); one.AddTag(Deep, 7);
        GameplayTagContainer.Intersection(output, one, one, workspace); ValidateStorage(output);
        output.RemoveTag(Deep); Require(output.IsEmpty && output.TagCount == 0, "singleton normalized");
        foreach (Case c in Cases) { for (int i = 0; i < 100; i++) c.Run(); c.Check?.Invoke(); }
        Console.WriteLine("PASS final consolidation: singleton reuse, collisions/growth, closure counts, normalization and " + Cases.Count + " public workloads.");
    }
    private static string Q(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    private static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string Measure(Case c, int iterations)
    {
        for (int i = 0; i < 64; i++) c.Run();
        c.Check?.Invoke(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        long bytes = Allocated(), start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++) c.Run();
        long ticks = Stopwatch.GetTimestamp() - start; bytes = Allocated() - bytes;
        c.Check?.Invoke();
        return "{\"Id\":" + Q(c.Name) + ",\"Iterations\":" + iterations + ",\"Ns\":" + N(ticks * (1e9 / Stopwatch.Frequency) / iterations) +
            ",\"Bytes\":" + N((double)bytes / iterations) + ",\"WallMilliseconds\":" + N(ticks * 1000.0 / Stopwatch.Frequency) +
            ",\"Gen0\":" + (GC.CollectionCount(0) - gen0) + ",\"Gen1\":" + (GC.CollectionCount(1) - gen1) + ",\"Gen2\":" + (GC.CollectionCount(2) - gen2) + "}";
    }
    public static void Main(string[] args)
    {
        Register(); Fixtures();
        if (args.Length == 0 || args[0] == "test") { Tests(); return; }
        var counts = new Dictionary<string, int>();
        if (args.Length > 1) foreach (string line in System.IO.File.ReadAllLines(args[1])) { string[] p = line.Split('\t'); counts.Add(p[0], int.Parse(p[1])); }
        var rows = new List<string>();
        foreach (Case c in Cases) rows.Add(Measure(c, counts.TryGetValue(c.Name, out int count) ? count : 128));
        Console.WriteLine("{\"Runtime\":" + Q(Environment.Version.ToString()) + ",\"Mono\":" + (Type.GetType("Mono.Runtime") == null ? "false" : "true") +
            ",\"Checksum\":" + Checksum + ",\"Rows\":[" + string.Join(",", rows) + "]}");
        GC.KeepAlive(Sink);
    }
}
