using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using GameplayTags;

internal static class Program
{
    private static object Sink;
    private static long Checksum;
    private static bool TestOnly;
    private static string[] Chain;
    private static string Seed;
    private static Dictionary<string,int> FixedIterations;
    private static readonly Process Process = Process.GetCurrentProcess();
    private static readonly Func<long> Allocated = AllocationCounter();
    private static Func<long> AllocationCounter()
    {
        var method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
        if (method == null) throw new NotSupportedException("A real per-thread allocation counter is required.");
        return (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
    }
    private static void Check(bool value, string context) { if (!value) throw new Exception(context); }
    private static GameplayTag T(string name) => GameplayTagManager.RequestTag(name);
    private static string Leaf(int index) => "Flat.G" + index % 32 + ".L" + index;
    private static GameplayTagContainer Set(IEnumerable<string> names) { var result = new GameplayTagContainer(); foreach (string name in names) result.AddTag(T(name)); return result; }
    private static HashSet<string> Names(IGameplayTagContainer value) { var names = new HashSet<string>(StringComparer.Ordinal); foreach (GameplayTag tag in value.GetExplicitTags()) Check(names.Add(tag.Name), "duplicate explicit"); return names; }
    private static HashSet<string> Closure(IEnumerable<string> names)
    {
        var closure = new HashSet<string>(StringComparer.Ordinal);
        // Longest names first avoid recreating an already-known chain's
        // prefixes. This oracle still depends only on ordinal name strings.
        foreach (string name in names.OrderByDescending(n => n.Length))
        {
            if (!closure.Add(name)) continue;
            for (int i = 0; i < name.Length; i++) if (name[i] == '.') closure.Add(name.Substring(0, i));
        }
        return closure;
    }
    private static void Assert(GameplayTagContainer value, HashSet<string> expected)
    {
        HashSet<string> closure = Closure(expected), actual = new HashSet<string>(StringComparer.Ordinal);
        Check(value.ExplicitTagCount == expected.Count && value.TagCount == closure.Count, "cardinalities"); Check(Names(value).SetEquals(expected), "explicit names");
        foreach (GameplayTag tag in value.GetTags()) Check(actual.Add(tag.Name), "duplicate closure");
        Check(actual.SetEquals(closure), "closure names");
    }
    private static void Drain(GameplayTagContainer value, HashSet<string> expected)
    {
        Assert(value, expected);
        foreach (string name in expected.OrderBy(n => n, StringComparer.Ordinal)) { value.AddTag(T(name)); value.RemoveTag(T(name)); }
        Check(value.IsEmpty && value.TagCount == 0, "full normalized drain");
    }
    private static void Register()
    {
        Chain = new string[3000]; Chain[0] = "Deep"; for (int i = 1; i < Chain.Length; i++) Chain[i] = Chain[i - 1] + ".N";
        Seed = "Seed" + string.Concat(Enumerable.Repeat(".N", 3000));
        var registrations = Enumerable.Range(0, 2048).Select(i => new GameplayTagRegistration(Leaf(i))).ToList();
        registrations.Add(new GameplayTagRegistration(Chain[2999])); registrations.Add(new GameplayTagRegistration(Seed));
        GameplayTagManager.Initialize(registrations.ToArray());
    }
    // Both builds perform the same observable replacement. The baseline uses the
    // existing factory + Copy API; compile-time selection is outside the timer.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Replace<T,U>(GameplayTagContainer output, in T lhs, in U rhs) where T : IGameplayTagContainer where U : IGameplayTagContainer
    {
#if REUSABLE_UNION
        GameplayTagContainer.Union(output, lhs, rhs);
#else
        GameplayTagContainer.Copy(output, GameplayTagContainer.Union(lhs, rhs));
#endif
    }
    private sealed class Row
    {
        public string Id { get; set; }
        public double Ns { get; set; }
        public double Bytes { get; set; }
        public int Iterations { get; set; }
        public double WallMilliseconds { get; set; }
        public double CpuMilliseconds { get; set; }
        public int Gen0 { get; set; }
        public int Gen1 { get; set; }
        public int Gen2 { get; set; }
        public long RetainedBytes { get; set; }
        public int Cohort { get; set; }
    }
    private static void Observe(GameplayTagContainer value) { Checksum = unchecked(Checksum + value.TagCount + value.ExplicitTagCount); Sink = value; }
    private static Row Measure(string id, Action action, int iterations)
    {
        if (TestOnly) { action(); return new Row { Id = id }; }
        if (FixedIterations != null) iterations = FixedIterations[id];
        for (int i = 0; i < 64; i++) action(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long cpu = Process.TotalProcessorTime.Ticks, bytes = Allocated(); int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        long start = Stopwatch.GetTimestamp(); for (int i = 0; i < iterations; i++) action(); long elapsed = Stopwatch.GetTimestamp() - start;
        bytes = Allocated() - bytes; cpu = Process.TotalProcessorTime.Ticks - cpu;
        return new Row { Id = id, Ns = elapsed * (1e9 / Stopwatch.Frequency) / iterations, Bytes = (double)bytes / iterations, Iterations = iterations,
            WallMilliseconds = elapsed * 1000.0 / Stopwatch.Frequency, CpuMilliseconds = cpu / 10000.0, Gen0 = GC.CollectionCount(0) - g0, Gen1 = GC.CollectionCount(1) - g1, Gen2 = GC.CollectionCount(2) - g2 };
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Row Retained(string id, Func<object> factory)
    {
        const int cohortSize = 64; var cohort = new object[cohortSize]; Sink = null;
        long before = GC.GetTotalMemory(true); for (int i = 0; i < cohortSize; i++) cohort[i] = factory(); long after = GC.GetTotalMemory(true); GC.KeepAlive(cohort);
        return new Row { Id = id, RetainedBytes = after - before, Cohort = cohortSize };
    }
    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    private static string Json(Row row) => "{" + string.Join(",", typeof(Row).GetProperties().Select(p => Quote(p.Name) + ":" + (p.PropertyType == typeof(string) ? Quote((string)p.GetValue(row)) : Convert.ToString(p.GetValue(row), CultureInfo.InvariantCulture)))) + "}";
    private static void Main(string[] args)
    {
        TestOnly = args.Length == 0 || args[0] == "test"; Register();
        if (args.Length > 1) { FixedIterations = new Dictionary<string,int>(); foreach (string line in System.IO.File.ReadAllLines(args[1])) { string[] parts = line.Split('\t'); FixedIterations.Add(parts[0], int.Parse(parts[1], CultureInfo.InvariantCulture)); } }
        var rows = new List<Row>();
        foreach (int n in new[] { 4, 128, 1024 }) foreach (int overlap in new[] { 0, 50, 100 })
        {
            string[] left = Enumerable.Range(0, n).Select(i => Leaf((i * 719) % n)).ToArray();
            string[] right = Enumerable.Range(0, n).Select(i => i < n * overlap / 100 ? left[i] : Leaf(1024 + i)).Reverse().ToArray();
            var a = Set(left); var b = Set(right); var expected = Names(a); expected.UnionWith(Names(b));
            var output = Set(new[] { Chain[2999] }); Replace(output, a, b); Drain(output, expected); Replace(output, a, b);
            string prefix = "union-output/n" + n + "/overlap" + overlap;
            rows.Add(Measure(prefix + "/reused", () => { Replace(output, a, b); Observe(output); }, 128)); Assert(output, expected);
            if (overlap == 50)
            {
                rows.Add(Measure(prefix + "/cold-output", () => { var cold = new GameplayTagContainer(); Replace(cold, a, b); Observe(cold); }, 128));
                rows.Add(Retained(prefix + "/retained-result", () => { var cold = new GameplayTagContainer(); Replace(cold, a, b); return cold; }));
            }
            if (n == 128 && overlap == 50)
            {
                foreach (bool leftAlias in new[] { true, false })
                {
                    var alias = new GameplayTagContainer(); GameplayTagContainer.Copy(alias, leftAlias ? a : b);
                    Replace(alias, leftAlias ? alias : a, leftAlias ? b : alias); Drain(alias, expected);
                    rows.Add(Measure(prefix + "/alias-" + (leftAlias ? "left" : "right") + "-restore-pair", () => { GameplayTagContainer.Copy(alias, leftAlias ? a : b); Replace(alias, leftAlias ? alias : a, leftAlias ? b : alias); Observe(alias); }, 128)); Assert(alias, expected);
                }
                var countedA = new GameplayTagCountContainer(); var countedB = new GameplayTagCountContainer();
                foreach (string name in left) countedA.AddTag(T(name), 3); foreach (string name in right) countedB.AddTag(T(name), 5);
                Replace(output, countedA, b); Drain(output, expected);
                rows.Add(Measure(prefix + "/counted-left-reused", () => { Replace(output, countedA, b); Observe(output); }, 128)); Assert(output, expected);
                Replace(output, a, countedB); Drain(output, expected);
                rows.Add(Measure(prefix + "/counted-right-reused", () => { Replace(output, a, countedB); Observe(output); }, 128)); Assert(output, expected);
                Replace(output, countedA, countedB); Drain(output, expected);
                rows.Add(Measure(prefix + "/counted-both-reused", () => { Replace(output, countedA, countedB); Observe(output); }, 128)); Assert(output, expected);
                foreach (string name in left) Check(countedA.GetExplicitTagCount(T(name)) == 3, "counted left immutable");
                foreach (string name in right) Check(countedB.GetExplicitTagCount(T(name)) == 5, "counted right immutable");
            }
            Check(Names(a).SetEquals(left) && Names(b).SetEquals(right), "sources immutable");
        }
        foreach (int depth in new[] { 128, 1024, 3000 }) foreach (bool descending in new[] { false, true })
        {
            string[] explicitNames = Chain.Take(depth).ToArray(); if (descending) Array.Reverse(explicitNames);
            var source = Set(explicitNames); var seed = Set(new[] { Seed }); var output = new GameplayTagContainer();
            var expected = Names(source); expected.Add(Seed); Replace(output, seed, source); Drain(output, expected); Replace(output, seed, source);
            rows.Add(Measure("union-output/depth" + depth + "/" + (descending ? "descending" : "ascending") + "-reused", () => { Replace(output, seed, source); Observe(output); }, 16)); Assert(output, expected);
            Check(Names(source).SetEquals(explicitNames) && seed.ExplicitTagCount == 1, "deep sources immutable");
        }
        var empty = new GameplayTagContainer(); var tiny = Set(new[] { Leaf(0) }); var reused = Set(new[] { Seed });
        Replace(reused, empty, empty); Check(reused.IsEmpty && reused.TagCount == 0, "empty replacement");
        rows.Add(Measure("union-output/empty-reused", () => { Replace(reused, empty, empty); Observe(reused); }, 512));
        rows.Add(Measure("union-output/self-reused", () => { Replace(tiny, tiny, tiny); Observe(tiny); }, 512)); Check(tiny.ExplicitTagCount == 1, "self replacement");
        if (TestOnly) Console.WriteLine("PASS reusable-union supplemental public names, counts, normalization, aliases, source immutability and full drain; " + rows.Count + " fixed rows.");
        else Console.WriteLine("{\"Schema\":1,\"Runtime\":" + Quote(Environment.Version.ToString()) + ",\"Framework\":" + Quote(Type.GetType("Mono.Runtime") != null ? "Standalone Mono" : ".NET") + ",\"RegistryCount\":" + GameplayTagManager.TagCount + ",\"Checksum\":" + Checksum + ",\"Rows\":[" + string.Join(",", rows.Select(Json)) + "]}");
    }
}
