// Actual public runtime API measurements. No modeled/simulated performance data.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameplayTags;
using Set = GameplayTags.RuntimeTagSet;

internal static class RuntimeBenchmarks
{
    const int Seed = 20261001;
#if GAMEPLAYTAGS_FORCE_PORTABLE
    const bool PortableKernelsForced = true;
#else
    const bool PortableKernelsForced = false;
#endif
    static readonly List<object> Rows = new List<object>();
    static readonly List<object> CandidateOnlyRows = new List<object>();
    static object sink;
    static int checksum, assertions, failures, samples = 11;
    static string variant, suite = "full";
    static bool onlyNewApis;
    static TagSetStorage mode;
    static double targetMs = 1.0;
    static void Check(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
    static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static double Percentile(double[] a, double p)
    {
        var sorted = (double[])a.Clone(); Array.Sort(sorted);
        return sorted[Math.Max(0, Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1))];
    }
    static string[] Names(int count, bool punctuation = false)
    {
        return Enumerable.Range(0, count).Select(i => punctuation
            ? (i < count / 2 ? "A.T" : "A-T") + i.ToString("D7", CultureInfo.InvariantCulture)
            : "Bench.G" + (i / 64).ToString("D5", CultureInfo.InvariantCulture) + ".T" + i.ToString("D7", CultureInfo.InvariantCulture)).ToArray();
    }
    static GameplayTagSettings Settings(string[] names)
    {
        var settings = UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        settings.ReplaceAll(names.Select(n => new GameplayTagDefinition(n, "", "Default", false, true)).ToList(),
            new List<GameplayTagRedirect>(), new List<GameplayTagSource> { new GameplayTagSource("Default", "", false) });
        return settings;
    }
    static Set Input(TagRegistry registry, int[] ids, TagSetStorage storage, int reserve = -1)
    {
        var result = new Set(registry, reserve < 0 ? ids.Length : reserve, storage);
        foreach (int id in ids) result.AddTag(registry.GetTagAt(id));
        return result;
    }
    static void Verify(Set set, IEnumerable<int> expected)
    {
        int[] values = expected.Distinct().OrderBy(x => x).ToArray();
        Check(set.Count == values.Length, "Count mismatch"); int cursor = 0;
        foreach (var tag in set)
            Check(cursor < values.Length && tag.RuntimeIndex == values[cursor++], "Membership/enumeration mismatch");
        Check(cursor == values.Length, "Enumeration length mismatch");
    }
    sealed class Fixture
    {
        public int universe, leftCount, rightCount, overlap;
        public string distribution, relation, digest;
        public TagRegistry registry;
        public int[] x, y;
        public Set a, b, work;
        public object Detail => new { actualRegistryCount = registry.Count, leftStorage = a.Storage.ToString(),
            rightStorage = b.Storage.ToString(), reservedDestinationStorage = work.Storage.ToString(),
            leftBufferBytes = a.BufferBytes, rightBufferBytes = b.BufferBytes, reservedDestinationBufferBytes = work.BufferBytes,
            leftOccupiedWords = x.Select(i => i >> 6).Distinct().Count(),
            leftSpanWords = x.Length == 0 ? 0 : (x[x.Length - 1] >> 6) - (x[0] >> 6) + 1 };
    }
    static int[] Sequence(int[] leaves, int count, string distribution, int seed)
    {
        if (distribution == "contiguous") return leaves.Skip((leaves.Length - count) / 3).Take(count).ToArray();
        if (distribution == "clusters4")
            return Enumerable.Range(0, count).Select(i => leaves[(i % 4) * (leaves.Length / 4) + i / 4]).ToArray();
        int[] shuffled = (int[])leaves.Clone(); var random = new Random(seed);
        for (int i = 0; i < count; i++) { int j = random.Next(i, shuffled.Length); int temp = shuffled[i]; shuffled[i] = shuffled[j]; shuffled[j] = temp; }
        return shuffled.Take(count).ToArray();
    }
    static Fixture CreateFixture(TagRegistry registry, int[] leaves, int n, int m, string distribution, int overlap, string relation = "equal")
    {
        int[] sequence = Sequence(leaves, n + m, distribution, Seed + n + m);
        int[] x = sequence.Take(n).OrderBy(i => i).ToArray();
        int shared = Math.Min(n, m) * overlap / 100;
        // Shared members are spread over the large operand, not only its first few words.
        int[] sharedValues = Enumerable.Range(0, shared).Select(i => x[(int)((long)i * x.Length / Math.Max(1, shared))]).ToArray();
        int[] y = sharedValues.Concat(sequence.Skip(n).Take(m - shared)).OrderBy(i => i).ToArray();
        var f = new Fixture { universe = leaves.Length, registry = registry, leftCount = n, rightCount = m,
            distribution = distribution, overlap = overlap, relation = relation, x = x, y = y,
            digest = Hash(string.Join(",", x) + "|" + string.Join(",", y)) };
        f.a = Input(registry, x, mode); f.b = Input(registry, y, mode);
        f.work = new Set(registry, Math.Min(registry.Count, n + m), mode);
        Verify(f.a, x); Verify(f.b, y); return f;
    }
    static void Measure(string stage, string operation, Fixture f, Func<int, int> body, Action verify,
        int units = 1, bool expectZero = false, object detail = null, int iterationCap = 1 << 20)
    {
        try
        {
            verify(); checksum ^= body(1);
            long a0 = GC.GetAllocatedBytesForCurrentThread(); checksum ^= body(1);
            long estimatedBytes = GC.GetAllocatedBytesForCurrentThread() - a0;
            int limit = Math.Min(iterationCap, estimatedBytes > 0 ? (int)Math.Max(1, Math.Min(1 << 20, (8L << 20) / estimatedBytes)) : 1 << 20);
            int iterations = 1;
            while (true)
            {
                long before = Stopwatch.GetTimestamp(); checksum ^= body(iterations);
                double elapsedMs = (Stopwatch.GetTimestamp() - before) * 1000.0 / Stopwatch.Frequency;
                if (elapsedMs >= targetMs || iterations >= limit) break;
                iterations = Math.Min(limit, iterations * 2);
            }
            var ns = new double[samples]; var allocated = new double[samples]; var durations = new double[samples]; var gc = new int[samples][];
            for (int sample = 0; sample < samples; sample++)
            {
                int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
                long bytes = GC.GetAllocatedBytesForCurrentThread(), before = Stopwatch.GetTimestamp();
                checksum ^= body(iterations);
                long elapsed = Stopwatch.GetTimestamp() - before;
                allocated[sample] = (GC.GetAllocatedBytesForCurrentThread() - bytes) / (iterations * (double)units);
                durations[sample] = elapsed * 1000.0 / Stopwatch.Frequency;
                ns[sample] = elapsed * (1e9 / Stopwatch.Frequency) / (iterations * (double)units);
                gc[sample] = new[] { GC.CollectionCount(0) - g0, GC.CollectionCount(1) - g1, GC.CollectionCount(2) - g2 };
                if (expectZero) Check(allocated[sample] == 0, "Prepared operation allocated " + allocated[sample] + " bytes/unit");
            }
            verify(); GC.KeepAlive(sink);
            Rows.Add(new { stage, operation, f.universe, f.leftCount, f.rightCount, f.distribution, f.overlap, f.relation, f.digest,
                status = "measured", iterations, units, ns, allocated_bytes = allocated, batch_ms = durations, gc_collections = gc,
                median_ns = Percentile(ns, .5), p95_batch_mean_ns = Percentile(ns, .95), max_batch_mean_ns = ns.Max(),
                median_allocated_bytes = Percentile(allocated, .5), details = detail ?? f.Detail });
        }
        catch (Exception e)
        {
            failures++;
            Rows.Add(new { stage, operation, f.universe, f.leftCount, f.rightCount, f.distribution, f.overlap, f.relation, f.digest,
                status = "failed", error = e.ToString() });
            Console.WriteLine("FAILED " + operation + " " + f.leftCount + "/" + f.rightCount + ": " + e.Message);
        }
    }
    static void Bulk(Fixture f)
    {
        int[] union = f.x.Union(f.y).OrderBy(i => i).ToArray(), intersection = f.x.Intersect(f.y).ToArray(), difference = f.x.Except(f.y).ToArray();
        for (int selected = 0; selected < 9; selected++)
        {
            int op = selected;
            string[] names = { "union.new", "union.into", "intersection.new", "intersection.into", "difference.copy_remove", "difference.reuse", "append.copy", "append.reuse", "intersection.alias" };
            int[] expected = op == 2 || op == 3 || op == 8 ? intersection : op == 4 || op == 5 ? difference : union;
            Measure("bulk", names[op], f,
                r => BulkLoop(f, op, r),
                () => { BulkLoop(f, op, 1); Verify((Set)sink, expected); Verify(f.a, f.x); Verify(f.b, f.y); },
                expectZero: op == 1 || op == 3 || op == 5 || op == 7 || op == 8);
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int BulkLoop(Fixture f, int op, int repeats)
    {
        Set a = f.a, b = f.b, work = f.work;
        switch (op)
        {
            case 0: for (int i = 0; i < repeats; i++) sink = Set.Union(a, b, mode); break;
            case 1: for (int i = 0; i < repeats; i++) Set.UnionInto(a, b, work); sink = work; break;
            case 2: for (int i = 0; i < repeats; i++) sink = Set.IntersectionExact(a, b, mode); break;
            case 3: for (int i = 0; i < repeats; i++) Set.IntersectionExactInto(a, b, work); sink = work; break;
            case 4: for (int i = 0; i < repeats; i++) { var c = new Set(a); c.RemoveTags(b); sink = c; } break;
            case 5: for (int i = 0; i < repeats; i++) { work.CopyFrom(a); work.RemoveTags(b); } sink = work; break;
            case 6: for (int i = 0; i < repeats; i++) { var c = new Set(a); c.AppendTags(b); sink = c; } break;
            case 7: for (int i = 0; i < repeats; i++) { work.CopyFrom(a); work.AppendTags(b); } sink = work; break;
            case 8: for (int i = 0; i < repeats; i++) { work.CopyFrom(a); Set.IntersectionExactInto(work, b, work); } sink = work; break;
        }
        return ((Set)sink).Count;
    }
    static GameplayTagQuery Query(string[] names)
    {
        var yes = GameplayTagQueryExpression.AnyTagsMatch();
        foreach (string name in names.Take(8)) yes.AddTag(GameplayTagManager.RequestTag(name));
        var no = GameplayTagQueryExpression.NoTagsMatch().AddTag(GameplayTagManager.RequestTag("Bench.G00000"));
        var all = GameplayTagQueryExpression.AllExpressionsMatch().AddExpression(yes).AddExpression(no);
        return new GameplayTagQuery(GameplayTagQueryExpression.AnyExpressionsMatch().AddExpression(all).AddExpression(yes));
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int ProbeLoop(Set set, RuntimeTag[] probes, bool hierarchy, int repeats)
    {
        int total = 0;
        if (hierarchy) for (int r = 0; r < repeats; r++) for (int i = 0; i < probes.Length; i++) total += set.HasTag(probes[i]) ? 1 : 0;
        else for (int r = 0; r < repeats; r++) for (int i = 0; i < probes.Length; i++) total += set.HasTagExact(probes[i]) ? 1 : 0;
        return total;
    }
    static void Common(Fixture f, string[] names, int[] leaves)
    {
        RuntimeTag[] tags = f.x.Select(f.registry.GetTagAt).ToArray();
        RuntimeTag[] reversed = tags.Reverse().ToArray();
        RuntimeTag[] rightTags = f.y.Select(f.registry.GetTagAt).ToArray();
        var authoring = new GameplayTagContainer(tags.Length);
        foreach (var tag in tags) authoring.AddTag(tag.Name);
        Action verifySink = () => Verify((Set)sink, f.x);
        Measure("prepare", "build.resolved_sorted", f, r => { for (int i = 0; i < r; i++) { var s = new Set(f.registry, tags.Length, mode); foreach (var t in tags) s.AddTag(t); sink = s; } return ((Set)sink).Count; },
            () => { sink = Input(f.registry, f.x, mode); verifySink(); });
        Measure("prepare", "build.resolved_reverse", f, r => { for (int i = 0; i < r; i++) { var s = new Set(f.registry, tags.Length, mode); foreach (var t in reversed) s.AddTag(t); sink = s; } return ((Set)sink).Count; },
            () => { sink = Input(f.registry, f.x.Reverse().ToArray(), mode); verifySink(); });
        Measure("prepare", "convert.authoring", f, r => { for (int i = 0; i < r; i++) sink = authoring.ToRuntime(f.registry, tags.Length, mode); return ((Set)sink).Count; },
            () => { sink = authoring.ToRuntime(f.registry, tags.Length, mode); verifySink(); });
        foreach (TagSetStorage sourceMode in new[] { TagSetStorage.Sparse, TagSetStorage.Dense })
        {
            Set source = Input(f.registry, f.x, sourceMode);
            Measure("conversion", "convert." + sourceMode + "_to_requested", f,
                r => { for (int i = 0; i < r; i++) { var s = new Set(f.registry, f.leftCount, mode); s.CopyFrom(source); sink = s; } return ((Set)sink).Count; },
                () => { var s = new Set(f.registry, f.leftCount, mode); s.CopyFrom(source); sink = s; verifySink(); },
                detail: new { sourceStorage = source.Storage.ToString(), targetRequested = mode.ToString(), sourceBufferBytes = source.BufferBytes });
        }
        int[] missing = leaves.Except(f.x).Take(128).ToArray();
        var mixed = Enumerable.Range(0, 256).Select(i => f.registry.GetTagAt(i % 2 == 0 && tags.Length > 0 ? f.x[(i / 2) % f.x.Length] : missing[(i / 2) % missing.Length])).ToArray();
        var hits = Enumerable.Range(0, 256).Select(i => f.registry.GetTagAt(tags.Length > 0 ? f.x[(i * 131) % f.x.Length] : missing[i % missing.Length])).ToArray();
        var misses = Enumerable.Range(0, 256).Select(i => f.registry.GetTagAt(missing[i % missing.Length])).ToArray();
        var parentMixed = mixed.Select(t => t.GetDirectParent()).ToArray();
        foreach (var entry in new[] { ("exact.mixed", mixed, false), ("exact.hits", hits, false), ("exact.misses", misses, false), ("hierarchy.mixed_parent", parentMixed, true) })
        {
            var item = entry;
            int expected = item.Item2.Count(t => item.Item3 ? f.x.Any(id => f.registry.GetTagAt(id).MatchesTag(t)) : f.x.Contains(t.RuntimeIndex));
            Measure("lookup", item.Item1, f, r => ProbeLoop(f.a, item.Item2, item.Item3, r),
                () => Check(ProbeLoop(f.a, item.Item2, item.Item3, 1) == expected, "Probe mismatch"), 256, true);
        }
        var query = Query(tags.Select(t => t.Name).ToArray()); var frozen = query.Freeze(f.registry);
        bool queryExpected = tags.Length != 0;
        Measure("query", "query.freeze", f, r => { for (int i = 0; i < r; i++) sink = query.Freeze(f.registry); return ((FrozenGameplayTagQuery)sink).NodeCount; },
            () => Check(query.Freeze(f.registry).Matches(f.a) == queryExpected, "Frozen query mismatch"));
        Measure("query", "query.matches", f, r => { int sum = 0; for (int i = 0; i < r; i++) sum += frozen.Matches(f.a) ? 1 : 0; return sum; },
            () => Check(frozen.Matches(f.a) == queryExpected, "Frozen query mismatch"), expectZero: true);
        var absentExpression = GameplayTagQueryExpression.AnyTagsMatch();
        foreach (int id in missing.Take(8)) absentExpression.AddTag(GameplayTagManager.RequestTag(f.registry.GetTagAt(id).Name));
        var absent = new GameplayTagQuery(absentExpression).Freeze(f.registry);
        Measure("query", "query.matches_miss", f, r => { int sum = 0; for (int i = 0; i < r; i++) sum += absent.Matches(f.a) ? 1 : 0; return sum; },
            () => Check(!absent.Matches(f.a), "Absent frozen query mismatch"), expectZero: true);
        var presentExpression = GameplayTagQueryExpression.AnyTagsMatch();
        foreach (var t in tags.Take(4)) presentExpression.AddTag(GameplayTagManager.RequestTag(t.Name));
        var excludedExpression = GameplayTagQueryExpression.NoTagsMatch();
        foreach (int id in missing.Take(4)) excludedExpression.AddTag(GameplayTagManager.RequestTag(f.registry.GetTagAt(id).Name));
        var nested = new GameplayTagQuery(GameplayTagQueryExpression.AllExpressionsMatch().AddExpression(presentExpression).AddExpression(excludedExpression)).Freeze(f.registry);
        Measure("query", "query.matches_nested", f, r => { int sum = 0; for (int i = 0; i < r; i++) sum += nested.Matches(f.a) ? 1 : 0; return sum; },
            () => Check(nested.Matches(f.a) == queryExpected, "Nested frozen query mismatch"), expectZero: true,
            detail: new { nodeCount = nested.NodeCount, rangeCount = nested.RangeCount });
        RuntimeTag mutate = f.registry.GetTagAt(missing[0]);
        Set mutable = Input(f.registry, f.x, mode, f.x.Length + 1);
        Measure("mutation", "mutation.add_remove", f, r => { int sum = 0; for (int i = 0; i < r; i++) { if (mutable.AddTag(mutate)) sum++; if (mutable.RemoveTag(mutate)) sum++; } return sum; },
            () => { Verify(mutable, f.x); Check(!mutable.HasTagExact(mutate), "Mutation reset"); }, expectZero: true);
        Measure("enumeration", "enumerate", f, r => { int sum = 0; for (int i = 0; i < r; i++) foreach (var t in f.a) sum ^= t.RuntimeIndex; return sum; },
            () => Verify(f.a, f.x), expectZero: true);
        foreach (int horizon in new[] { 1, 32 })
        {
            int h = horizon;
            Measure("lifecycle", "lifecycle.build_bulk_query.h" + h, f, r => Lifecycle(f, tags, rightTags, frozen, mixed, h, r),
                () => { Lifecycle(f, tags, rightTags, frozen, mixed, h, 1); Verify((Set)sink, f.x.Except(f.y)); },
                detail: new { buildOperands = 2, reserveDestination = true, horizon = h, perStep = "union_into + intersection_into + copy_remove + 32 exact probes + frozen query", registryBuildExcluded = true });
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Lifecycle(Fixture f, RuntimeTag[] left, RuntimeTag[] right, FrozenGameplayTagQuery query, RuntimeTag[] probes, int horizon, int repeats)
    {
        int sum = 0;
        for (int r = 0; r < repeats; r++)
        {
            var a = new Set(f.registry, left.Length, mode); foreach (var t in left) a.AddTag(t);
            var b = new Set(f.registry, right.Length, mode); foreach (var t in right) b.AddTag(t);
            var work = new Set(f.registry, left.Length + right.Length, mode);
            for (int h = 0; h < horizon; h++)
            {
                Set.UnionInto(a, b, work); sum ^= work.Count;
                Set.IntersectionExactInto(a, b, work); sum ^= work.Count;
                work.CopyFrom(a); work.RemoveTags(b); sum ^= work.Count;
                for (int p = 0; p < 32; p++) sum += a.HasTagExact(probes[p]) ? 1 : 0;
                sum += query.Matches(a) ? 1 : 0;
            }
            sink = work;
        }
        return sum;
    }
    static void NewApis(Fixture f)
    {
#if REFACTOR_API
        int start = Rows.Count;
        int[] expected = f.x.Except(f.y).ToArray();
        RuntimeTag[] ordered = f.x.Select(f.registry.GetTagAt).ToArray();
        RuntimeTag[] reversed = ordered.Reverse().ToArray();
        foreach (var input in new[] { ("sorted", ordered), ("reverse", reversed) })
        {
            var item = input;
            Measure("candidate_only", "build.FromTags." + item.Item1, f,
                r => { for (int i = 0; i < r; i++) sink = Set.FromTags(f.registry, item.Item2, item.Item2.Length, mode); return ((Set)sink).Count; },
                () => Verify(Set.FromTags(f.registry, item.Item2, item.Item2.Length, mode), f.x));
        }
        Measure("candidate_only", "difference.direct_new", f,
            r => { for (int i = 0; i < r; i++) sink = Set.DifferenceExact(f.a, f.b, mode); return ((Set)sink).Count; },
            () => Verify(Set.DifferenceExact(f.a, f.b, mode), expected));
        Measure("candidate_only", "difference.direct_into", f,
            r => { for (int i = 0; i < r; i++) Set.DifferenceExactInto(f.a, f.b, f.work); sink = f.work; return f.work.Count; },
            () => { Set.DifferenceExactInto(f.a, f.b, f.work); Verify(f.work, expected); Verify(f.a, f.x); Verify(f.b, f.y); }, expectZero: true);
        foreach (TagSetStorage sourceMode in new[] { TagSetStorage.Sparse, TagSetStorage.Dense })
        {
            Set source = Input(f.registry, f.x, sourceMode);
            Measure("candidate_only", "conversion.ToStorage.from_" + sourceMode, f,
                r => { for (int i = 0; i < r; i++) sink = source.ToStorage(mode); return ((Set)sink).Count; },
                () => { Set result = source.ToStorage(mode); Verify(result, f.x); result.Clear(); Verify(source, f.x); },
                detail: new { sourceStorage = sourceMode.ToString(), targetRequested = mode.ToString() });
        }
        CandidateOnlyRows.AddRange(Rows.Skip(start)); Rows.RemoveRange(start, Rows.Count - start);
#endif
    }
    static void RegistryCost(GameplayTagSettings settings, Fixture f, string order = "grouped")
    {
        Measure("registry", "registry.create." + order, f,
            r => { for (int i = 0; i < r; i++) sink = TagRegistry.Create(settings); return ((TagRegistry)sink).Count; },
            () => Check(TagRegistry.Create(settings).Count == f.registry.Count, "Registry count mismatch"), iterationCap: 1,
            detail: new { includes = "validation, hierarchy construction, registry tables, authoring-tag cache", excludes = "pre-existing settings and strings", actualRegistryCount = f.registry.Count });
    }
    static void PunctuationConversion()
    {
        int count = suite == "smoke" ? 4096 : 32768;
        string[] names = Names(count, true); var settings = Settings(names);
        GameplayTagManager.Initialize(settings, true); var registry = GameplayTagManager.CurrentRegistry;
        int[] ids = names.Select(n => registry.Resolve(n).RuntimeIndex).OrderBy(i => i).ToArray();
        var authoring = new GameplayTagContainer(count); foreach (string name in names) authoring.AddTag(name);
        var f = new Fixture { universe = count, leftCount = count, rightCount = 0, overlap = 0, relation = "conversion", distribution = "punctuation_ordinal_vs_DFS", registry = registry,
            x = ids, y = Array.Empty<int>(), digest = Hash(string.Join("|", names)) };
        f.a = Input(registry, ids, mode); f.b = new Set(registry, 0, mode); f.work = new Set(registry, count, mode);
        int inversions = 0, previous = -1; foreach (var tag in authoring) { int next = registry.Resolve(tag.Name).RuntimeIndex; if (next < previous) inversions++; previous = next; }
        Check(inversions > 0, "Adversarial authoring fixture must differ from DFS order");
        Measure("prepare", "convert.authoring.punctuation", f, r => { for (int i = 0; i < r; i++) sink = authoring.ToRuntime(registry, count, mode); return ((Set)sink).Count; },
            () => Verify(authoring.ToRuntime(registry, count, mode), ids), detail: new { ordinalDescendingTransitions = inversions, note = "Ordinal authoring order inserts a late low-ID half after a high-ID half" });
    }
    static int Main(string[] args)
    {
        if (args.Length < 3) throw new ArgumentException("output.json variant Auto|Sparse|Dense [smoke|full] [samples] [target-ms]");
        variant = args[1]; mode = (TagSetStorage)Enum.Parse(typeof(TagSetStorage), args[2]);
        if (args.Length > 3) suite = args[3]; if (args.Length > 4) samples = int.Parse(args[4]); if (args.Length > 5) targetMs = double.Parse(args[5], CultureInfo.InvariantCulture);
        onlyNewApis = args.Length > 6 && args[6] == "newapis";
        Check(samples >= 3 && targetMs > 0, "At least three samples and positive duration required");
        long positiveBefore = GC.GetAllocatedBytesForCurrentThread(); sink = new byte[1024];
        Check(GC.GetAllocatedBytesForCurrentThread() > positiveBefore, "Allocation positive control");
        int[] universes = suite == "smoke" ? new[] { 65536 } : new[] { 65536, 262144 };
        foreach (int u in universes)
        {
            string[] names = Names(u); var settings = Settings(names); GameplayTagManager.Initialize(settings, true);
            TagRegistry registry = GameplayTagManager.CurrentRegistry;
            int[] leaves = names.Select(n => registry.Resolve(n).RuntimeIndex).OrderBy(i => i).ToArray();
            if (!onlyNewApis)
            {
                var rf = CreateFixture(registry, leaves, 0, 0, "contiguous", 0, "registry");
                RegistryCost(settings, rf);
                var random = new Random(Seed); string[] shuffledNames = (string[])names.Clone();
                for (int i = shuffledNames.Length - 1; i > 0; i--) { int j = random.Next(i + 1); string t = shuffledNames[i]; shuffledNames[i] = shuffledNames[j]; shuffledNames[j] = t; }
                RegistryCost(Settings(shuffledNames), rf, "shuffled");
            }
            int[] sizes = suite == "smoke" ? new[] { 8, 1024, 4096 } : u == 65536 ? new[] { 0, 1, 7, 8, 9, 32, 128, 1024, 4096, 16384 } : new[] { 8, 32, 128, 4096, 32768 };
            foreach (string distribution in suite == "smoke" ? new[] { "scattered" } : new[] { "contiguous", "clusters4", "scattered" })
            foreach (int n in sizes)
            {
                var shared = CreateFixture(registry, leaves, n, n, distribution, 50);
                if (onlyNewApis) NewApis(shared);
                else
                {
                    Common(shared, names, leaves);
                    foreach (int overlap in suite == "smoke" ? new[] { 50 } : new[] { 0, 50, 100 }) Bulk(CreateFixture(registry, leaves, n, n, distribution, overlap));
                }
            }
            // Unequal operands exercise sparse-large intersections, hit positions, and subtraction with few removals.
            foreach (int m in suite == "smoke" ? new[] { 8 } : new[] { 1, 8, 128 })
            foreach (int overlap in new[] { 0, 50, 100 })
            foreach (string distribution in suite == "smoke" ? new[] { "scattered" } : new[] { "contiguous", "scattered" })
            {
                int n = suite == "smoke" ? 4096 : u == 65536 ? 16384 : 32768;
                var unequal = CreateFixture(registry, leaves, n, m, distribution, overlap, "large_small");
                if (onlyNewApis) NewApis(unequal);
                else
                {
                    Bulk(unequal);
                    Bulk(CreateFixture(registry, leaves, m, n, distribution, overlap, "small_large"));
                }
            }
            Console.WriteLine(variant + " " + mode + " U=" + u + " rows=" + Rows.Count + " failures=" + failures);
        }
        if (!onlyNewApis) PunctuationConversion();
        var result = new { variant, requestedStorage = mode.ToString(), suite, onlyNewApis, samples, targetMs, seed = Seed, assertions, failures, checksum,
            utc = DateTime.UtcNow.ToString("O"), runtime = RuntimeInformation.FrameworkDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            os = RuntimeInformation.OSDescription, stopwatchFrequency = Stopwatch.Frequency, processorCount = Environment.ProcessorCount, portableKernelsForced = PortableKernelsForced, avx2Available = System.Runtime.Intrinsics.X86.Avx2.IsSupported, popcntAvailable = System.Runtime.Intrinsics.X86.Popcnt.X64.IsSupported, rows = Rows, candidate_only_rows = CandidateOnlyRows,
            protocol = "Independent fresh processes per variant/mode/round. Calibration targets milliseconds with 8 MiB allocation budget per batch. All raw samples and GC counts retained. Timing units are operations except 256-probe loops reported per probe. No GC-forcing inside timed bodies.",
            tails = "p95/max describe calibrated batch-mean latency across samples, NOT individual-operation latency and NOT independent-process confidence intervals.",
            limitations = "CoreCLR managed Linux host only. No Unity/Mono, IL2CPP, Burst, ARM64, native peak memory, user telemetry, or universal fastest claim. Member buffers exclude object headers/shared registry; cumulative allocation is not retained memory. Dense/sparse layout conversion uses public new+CopyFrom, including destination allocation." };
        File.WriteAllText(args[0], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("BENCH " + variant + " " + mode + " rows=" + Rows.Count + " newApiRows=" + CandidateOnlyRows.Count + " assertions=" + assertions + " failures=" + failures);
        return failures == 0 ? 0 : 1;
    }
}
