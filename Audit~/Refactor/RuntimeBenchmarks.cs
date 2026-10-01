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
    static bool onlyNewApis, validationSelfTest;
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
    static void Measure(string stage, string operation, Fixture f, Func<int, int> body, Action verify, Action<int, int> validateActual,
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
            var ns = new double[samples]; var allocated = new double[samples]; var durations = new double[samples]; var gc = new int[samples][]; var returnedValues = new int[samples];
            for (int sample = 0; sample < samples; sample++)
            {
                int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
                long bytes = GC.GetAllocatedBytesForCurrentThread(), before = Stopwatch.GetTimestamp();
                int actual = body(iterations); checksum ^= actual;
                long elapsed = Stopwatch.GetTimestamp() - before;
                allocated[sample] = (GC.GetAllocatedBytesForCurrentThread() - bytes) / (iterations * (double)units);
                durations[sample] = elapsed * 1000.0 / Stopwatch.Frequency;
                ns[sample] = elapsed * (1e9 / Stopwatch.Frequency) / (iterations * (double)units);
                gc[sample] = new[] { GC.CollectionCount(0) - g0, GC.CollectionCount(1) - g1, GC.CollectionCount(2) - g2 };
                returnedValues[sample] = actual;
                if (expectZero) Check(allocated[sample] == 0, "Prepared operation allocated " + allocated[sample] + " bytes/unit");
                // Validate the final value/object produced by THIS measured batch before any reset,
                // warmup, new operation, or preflight callback can overwrite it. Not timed/allocated.
                validateActual(actual, iterations);
            }
            verify(); GC.KeepAlive(sink);
            Rows.Add(new { stage, operation, f.universe, f.leftCount, f.rightCount, f.distribution, f.overlap, f.relation, f.digest,
                status = "measured", iterations, units, returned_values = returnedValues, timed_output_validated = true, ns, allocated_bytes = allocated, batch_ms = durations, gc_collections = gc,
                median_ns = Percentile(ns, .5), p95_batch_mean_ns = Percentile(ns, .95), max_batch_mean_ns = ns.Max(),
                median_allocated_bytes = Percentile(allocated, .5), details = detail ?? f.Detail });
        }
        catch (Exception e)
        {
            failures++;
            Rows.Add(new { stage, operation, f.universe, f.leftCount, f.rightCount, f.distribution, f.overlap, f.relation, f.digest,
                status = "failed", error = e.ToString() });
            Console.WriteLine((validationSelfTest ? "EXPECTED VALIDATION FAILURE " : "FAILED ") + operation + " " + f.leftCount + "/" + f.rightCount + ": " + e.Message);
        }
    }
    static void VerifyOrdered(Set set, int[] expected)
    {
        Check(set.Count == expected.Length, "Actual timed Count mismatch"); int cursor = 0;
        foreach (var tag in set)
            Check(cursor < expected.Length && tag.RuntimeIndex == expected[cursor++], "Actual timed membership/order mismatch");
        Check(cursor == expected.Length, "Actual timed enumeration length mismatch");
    }
    static void VerifyTimedSet(Fixture f, int[] expected, int returned)
    {
        var actual = sink as Set;
        Check(actual != null && ReferenceEquals(actual.Registry, f.registry), "Actual timed result/registry mismatch");
        Check(!ReferenceEquals(actual, f.a) && !ReferenceEquals(actual, f.b), "Timed output aliases immutable input");
        Check(returned == expected.Length, "Actual timed returned Count mismatch");
        VerifyOrdered(actual, expected); VerifyOrdered(f.a, f.x); VerifyOrdered(f.b, f.y);
    }
    static void VerifyMutationPreflight(Set set, RuntimeTag tag, int[] expected)
    {
        VerifyOrdered(set, expected); Check(!set.HasTagExact(tag), "Mutation precondition");
        Check(set.AddTag(tag), "Add must return true");
        Check(set.Count == expected.Length + 1 && set.HasTagExact(tag), "Mutation add intermediate membership");
        Check(set.RemoveTag(tag), "Remove must return true");
        Check(set.Count == expected.Length && !set.HasTagExact(tag), "Mutation remove intermediate membership");
        VerifyOrdered(set, expected);
    }
    static void VerifyTimedMutation(Set set, RuntimeTag tag, int[] expected, int returned, int repeats)
    {
        Check(returned == 2 * repeats, "Actual timed mutation checksum: add/remove must each succeed");
        VerifyOrdered(set, expected); Check(!set.HasTagExact(tag), "Actual timed mutation did not restore input");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int EnumerateLoop(Set set, int repeats)
    {
        int total = 0;
        for (int r = 0; r < repeats; r++)
        {
            int hash = 0, count = 0;
            foreach (var tag in set) { hash ^= tag.RuntimeIndex; count++; }
            // Odd per-pass checksum cannot cancel to zero for the <=2^20 calibrated repeats.
            total = unchecked(total + ((hash * 31 + count) | 1));
        }
        return total;
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
                (value, repeats) => VerifyTimedSet(f, expected, value),
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
            () => { sink = Input(f.registry, f.x, mode); verifySink(); }, (value, repeats) => VerifyTimedSet(f, f.x, value));
        Measure("prepare", "build.resolved_reverse", f, r => { for (int i = 0; i < r; i++) { var s = new Set(f.registry, tags.Length, mode); foreach (var t in reversed) s.AddTag(t); sink = s; } return ((Set)sink).Count; },
            () => { sink = Input(f.registry, f.x.Reverse().ToArray(), mode); verifySink(); }, (value, repeats) => VerifyTimedSet(f, f.x, value));
        Measure("prepare", "convert.authoring", f, r => { for (int i = 0; i < r; i++) sink = authoring.ToRuntime(f.registry, tags.Length, mode); return ((Set)sink).Count; },
            () => { sink = authoring.ToRuntime(f.registry, tags.Length, mode); verifySink(); }, (value, repeats) => VerifyTimedSet(f, f.x, value));
        foreach (TagSetStorage sourceMode in new[] { TagSetStorage.Sparse, TagSetStorage.Dense })
        {
            Set source = Input(f.registry, f.x, sourceMode);
            Measure("conversion", "convert." + sourceMode + "_to_requested", f,
                r => { for (int i = 0; i < r; i++) { var s = new Set(f.registry, f.leftCount, mode); s.CopyFrom(source); sink = s; } return ((Set)sink).Count; },
                () => { var s = new Set(f.registry, f.leftCount, mode); s.CopyFrom(source); sink = s; verifySink(); },
                (value, repeats) => { VerifyTimedSet(f, f.x, value); VerifyOrdered(source, f.x); },
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
            bool[] expectedProbes = item.Item2.Select(t => item.Item3 ? f.x.Any(id => f.registry.GetTagAt(id).MatchesTag(t)) : f.x.Contains(t.RuntimeIndex)).ToArray();
            int expected = expectedProbes.Count(value => value);
            Measure("lookup", item.Item1, f, r => ProbeLoop(f.a, item.Item2, item.Item3, r),
                () => { for (int i = 0; i < item.Item2.Length; i++) Check((item.Item3 ? f.a.HasTag(item.Item2[i]) : f.a.HasTagExact(item.Item2[i])) == expectedProbes[i], "Individual probe mismatch"); },
                (value, repeats) => { Check(value == expected * repeats, "Actual timed probe checksum"); VerifyOrdered(f.a, f.x); }, 256, true);
        }
        var query = Query(tags.Select(t => t.Name).ToArray()); var frozen = query.Freeze(f.registry);
        bool queryExpected = tags.Length != 0;
        Measure("query", "query.freeze", f, r => { for (int i = 0; i < r; i++) sink = query.Freeze(f.registry); return ((FrozenGameplayTagQuery)sink).NodeCount; },
            () => Check(query.Freeze(f.registry).Matches(f.a) == queryExpected, "Frozen query mismatch"),
            (value, repeats) => { var actual = (FrozenGameplayTagQuery)sink; Check(ReferenceEquals(actual.Registry, f.registry) && actual.Matches(f.a) == queryExpected && actual.NodeCount == value, "Actual frozen query result"); });
        Measure("query", "query.matches", f, r => { int sum = 0; for (int i = 0; i < r; i++) sum += frozen.Matches(f.a) ? 1 : 0; return sum; },
            () => Check(frozen.Matches(f.a) == queryExpected, "Frozen query mismatch"),
            (value, repeats) => { Check(value == (queryExpected ? repeats : 0), "Actual timed frozen query checksum"); VerifyOrdered(f.a, f.x); }, expectZero: true);
        var absentExpression = GameplayTagQueryExpression.AnyTagsMatch();
        foreach (int id in missing.Take(8)) absentExpression.AddTag(GameplayTagManager.RequestTag(f.registry.GetTagAt(id).Name));
        var absent = new GameplayTagQuery(absentExpression).Freeze(f.registry);
        Measure("query", "query.matches_miss", f, r => { int sum = 0; for (int i = 0; i < r; i++) sum += absent.Matches(f.a) ? 1 : 0; return sum; },
            () => Check(!absent.Matches(f.a), "Absent frozen query mismatch"),
            (value, repeats) => { Check(value == 0, "Actual timed absent query checksum"); VerifyOrdered(f.a, f.x); }, expectZero: true);
        var presentExpression = GameplayTagQueryExpression.AnyTagsMatch();
        foreach (var t in tags.Take(4)) presentExpression.AddTag(GameplayTagManager.RequestTag(t.Name));
        var excludedExpression = GameplayTagQueryExpression.NoTagsMatch();
        foreach (int id in missing.Take(4)) excludedExpression.AddTag(GameplayTagManager.RequestTag(f.registry.GetTagAt(id).Name));
        var nested = new GameplayTagQuery(GameplayTagQueryExpression.AllExpressionsMatch().AddExpression(presentExpression).AddExpression(excludedExpression)).Freeze(f.registry);
        Measure("query", "query.matches_nested", f, r => { int sum = 0; for (int i = 0; i < r; i++) sum += nested.Matches(f.a) ? 1 : 0; return sum; },
            () => Check(nested.Matches(f.a) == queryExpected, "Nested frozen query mismatch"),
            (value, repeats) => { Check(value == (queryExpected ? repeats : 0), "Actual timed nested query checksum"); VerifyOrdered(f.a, f.x); }, expectZero: true,
            detail: new { nodeCount = nested.NodeCount, rangeCount = nested.RangeCount });
        int[] parentIds = f.y.Select(id => f.registry.GetTagAt(id).GetDirectParent().RuntimeIndex).Distinct().OrderBy(id => id).ToArray();
        var parentMembership = new HashSet<int>(parentIds);
        int[] filteredExpected = f.x.Where(id => parentMembership.Contains(f.registry.GetTagAt(id).GetDirectParent().RuntimeIndex)).ToArray();
        Set conditions = Input(f.registry, parentIds, mode);
        Set filtered = new Set(f.registry, f.leftCount, mode);
        Measure("hierarchy", "hierarchy.filter_into", f,
            r => { for (int i = 0; i < r; i++) f.a.FilterInto(conditions, filtered); sink = filtered; return filtered.Count; },
            () => { f.a.FilterInto(conditions, filtered); Verify(filtered, filteredExpected); Verify(f.a, f.x); Verify(conditions, parentIds); },
            (value, repeats) => { VerifyTimedSet(f, filteredExpected, value); VerifyOrdered(conditions, parentIds); }, expectZero: true,
            detail: new { conditionCount = conditions.Count, conditionStorage = conditions.Storage.ToString(), outputStorage = filtered.Storage.ToString(),
                outputBufferBytes = filtered.BufferBytes, expectedCount = filteredExpected.Length, conditions = "direct parents of right operand leaves" });
        Measure("hierarchy", "hierarchy.filter_alias", f,
            r => { for (int i = 0; i < r; i++) { filtered.CopyFrom(f.a); filtered.FilterInto(conditions, filtered); } sink = filtered; return filtered.Count; },
            () => { filtered.CopyFrom(f.a); filtered.FilterInto(conditions, filtered); Verify(filtered, filteredExpected); Verify(f.a, f.x); Verify(conditions, parentIds); },
            (value, repeats) => { VerifyTimedSet(f, filteredExpected, value); VerifyOrdered(conditions, parentIds); }, expectZero: true,
            detail: new { conditionCount = conditions.Count, conditionStorage = conditions.Storage.ToString(), outputStorage = filtered.Storage.ToString(),
                outputBufferBytes = filtered.BufferBytes, expectedCount = filteredExpected.Length, resetIncluded = "CopyFrom left" });
        RuntimeTag mutate = f.registry.GetTagAt(missing[0]);
        Set mutable = Input(f.registry, f.x, mode, f.x.Length + 1);
        Measure("mutation", "mutation.add_remove", f, r => { int sum = 0; for (int i = 0; i < r; i++) { if (mutable.AddTag(mutate)) sum++; if (mutable.RemoveTag(mutate)) sum++; } return sum; },
            () => VerifyMutationPreflight(mutable, mutate, f.x),
            (value, repeats) => VerifyTimedMutation(mutable, mutate, f.x, value, repeats), expectZero: true);
        int enumerationHash = 0; foreach (int id in f.x) enumerationHash ^= id;
        enumerationHash = (enumerationHash * 31 + f.x.Length) | 1;
        Measure("enumeration", "enumerate", f, r => EnumerateLoop(f.a, r),
            () => Verify(f.a, f.x),
            (value, repeats) => { Check(value == unchecked(enumerationHash * repeats), "Actual timed enumeration checksum"); VerifyOrdered(f.a, f.x); }, expectZero: true);
        int[] lifecycleExpected = f.x.Except(f.y).ToArray();
        int lifecycleStepValue = f.x.Union(f.y).Count() + 3 * f.x.Intersect(f.y).Count() + 5 * lifecycleExpected.Length
            + mixed.Take(32).Count(t => f.x.Contains(t.RuntimeIndex)) + (queryExpected ? 1 : 0);
        foreach (int horizon in new[] { 1, 32 })
        {
            int h = horizon;
            Measure("lifecycle", "lifecycle.build_bulk_query.h" + h, f, r => Lifecycle(f, tags, rightTags, frozen, mixed, h, r),
                () => { Lifecycle(f, tags, rightTags, frozen, mixed, h, 1); Verify((Set)sink, lifecycleExpected); },
                (value, repeats) => { VerifyTimedSet(f, lifecycleExpected, ((Set)sink).Count); Check(value == unchecked(lifecycleStepValue * h * repeats), "Actual timed lifecycle checksum"); },
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
                Set.UnionInto(a, b, work); sum += work.Count;
                Set.IntersectionExactInto(a, b, work); sum += 3 * work.Count;
                work.CopyFrom(a); work.RemoveTags(b); sum += 5 * work.Count;
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
                () => Verify(Set.FromTags(f.registry, item.Item2, item.Item2.Length, mode), f.x),
                (value, repeats) => VerifyTimedSet(f, f.x, value));
        }
        Measure("candidate_only", "difference.direct_new", f,
            r => { for (int i = 0; i < r; i++) sink = Set.DifferenceExact(f.a, f.b, mode); return ((Set)sink).Count; },
            () => Verify(Set.DifferenceExact(f.a, f.b, mode), expected),
            (value, repeats) => VerifyTimedSet(f, expected, value));
        Measure("candidate_only", "difference.direct_into", f,
            r => { for (int i = 0; i < r; i++) Set.DifferenceExactInto(f.a, f.b, f.work); sink = f.work; return f.work.Count; },
            () => { Set.DifferenceExactInto(f.a, f.b, f.work); Verify(f.work, expected); Verify(f.a, f.x); Verify(f.b, f.y); },
            (value, repeats) => VerifyTimedSet(f, expected, value), expectZero: true);
        int exactUnionCapacity = f.x.Union(f.y).Count();
        Set rightAlias = Input(f.registry, f.y, mode, exactUnionCapacity);
        Measure("candidate_only", "difference.right_alias_prepared", f,
            r => { for (int i = 0; i < r; i++) { rightAlias.CopyFrom(f.b); Set.DifferenceExactInto(f.a, rightAlias, rightAlias); } sink = rightAlias; return rightAlias.Count; },
            () => { rightAlias.CopyFrom(f.b); Set.DifferenceExactInto(f.a, rightAlias, rightAlias); Verify(rightAlias, expected); Verify(f.a, f.x); Verify(f.b, f.y); },
            (value, repeats) => VerifyTimedSet(f, expected, value), expectZero: true,
            detail: new { exactUnionCapacity, actualCapacity = rightAlias.Capacity, rightStorage = rightAlias.Storage.ToString(),
                rightBufferBytes = rightAlias.BufferBytes, resetIncluded = "CopyFrom original right", preparationExcluded = "initial independent target plus exact union reservation" });
        foreach (TagSetStorage sourceMode in new[] { TagSetStorage.Sparse, TagSetStorage.Dense })
        {
            Set source = Input(f.registry, f.x, sourceMode);
            Measure("candidate_only", "conversion.ToStorage.from_" + sourceMode, f,
                r => { for (int i = 0; i < r; i++) sink = source.ToStorage(mode); return ((Set)sink).Count; },
                () => { Set result = source.ToStorage(mode); Verify(result, f.x); result.Clear(); Verify(source, f.x); },
                (value, repeats) => { VerifyTimedSet(f, f.x, value); VerifyOrdered(source, f.x); },
                detail: new { sourceStorage = sourceMode.ToString(), targetRequested = mode.ToString() });
        }
        CandidateOnlyRows.AddRange(Rows.Skip(start)); Rows.RemoveRange(start, Rows.Count - start);
#endif
    }
    static void RegistryCost(GameplayTagSettings settings, Fixture f, string order = "grouped")
    {
        Measure("registry", "registry.create." + order, f,
            r => { for (int i = 0; i < r; i++) sink = TagRegistry.Create(settings); return ((TagRegistry)sink).Count; },
            () => Check(TagRegistry.Create(settings).Count == f.registry.Count, "Registry count mismatch"),
            (value, repeats) => { var actual = (TagRegistry)sink; Check(actual.Count == f.registry.Count && value == actual.Count, "Actual timed registry Count mismatch");
                foreach (int index in new[] { 0, actual.Count / 2, actual.Count - 1 }) if (index >= 0 && index < actual.Count) Check(actual.GetTagAt(index).Name == f.registry.GetTagAt(index).Name, "Actual timed registry name mismatch"); }, iterationCap: 1,
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
            () => Verify(authoring.ToRuntime(registry, count, mode), ids),
            (value, repeats) => VerifyTimedSet(f, ids, value), detail: new { ordinalDescendingTransitions = inversions, note = "Ordinal authoring order inserts a late low-ID half after a high-ID half" });
    }
    static int ValidationSelfTests()
    {
        validationSelfTest = true; variant = "validator-control"; mode = TagSetStorage.Sparse; samples = 3; targetMs = .01;
        string[] names = Names(16); var settings = Settings(names); GameplayTagManager.Initialize(settings, true);
        var registry = GameplayTagManager.CurrentRegistry; int[] leaves = names.Select(n => registry.Resolve(n).RuntimeIndex).ToArray();
        var f = CreateFixture(registry, leaves, 2, 2, "contiguous", 50);
        int before = failures;
        // A fresh preflight produces the right answer, but the actual timed body returns a wrong empty set.
        // Calling preflight first during post-verification would incorrectly hide this corruption.
        Measure("validation", "reject_wrong_timed_output", f,
            r => { sink = new Set(registry); return 0; },
            () => { sink = Input(registry, f.x, mode); Verify((Set)sink, f.x); },
            (value, repeats) => VerifyTimedSet(f, f.x, value));
        Check(failures == before + 1, "Validator accepted wrong actual timed output");
        RuntimeTag extra = registry.GetTagAt(leaves[leaves.Length - 1]); var mutable = Input(registry, f.x, mode, f.x.Length + 1);
        Measure("validation", "reject_noop_timed_mutation", f, r => 0,
            () => VerifyMutationPreflight(mutable, extra, f.x),
            (value, repeats) => VerifyTimedMutation(mutable, extra, f.x, value, repeats), expectZero: true);
        Check(failures == before + 2, "Validator accepted no-op timed mutation");
        Measure("validation", "reject_wrong_timed_probe_checksum", f, r => 0,
            () => Check(f.a.HasTagExact(registry.GetTagAt(f.x[0])), "Probe preflight"),
            (value, repeats) => Check(value == repeats, "Actual timed probe checksum"), expectZero: true);
        Check(failures == before + 3, "Validator accepted wrong timed checksum");
        Measure("validation", "accept_correct_timed_output", f,
            r => { for (int i = 0; i < r; i++) f.work.CopyFrom(f.a); sink = f.work; return f.work.Count; },
            () => { f.work.CopyFrom(f.a); Verify(f.work, f.x); },
            (value, repeats) => VerifyTimedSet(f, f.x, value), expectZero: true);
        Check(failures == before + 3, "Validator rejected the correct positive control");
        Console.WriteLine("VALIDATION SELF-TEST PASS: three injected faults rejected and positive control accepted");
        return 0;
    }
    static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--validation-self-test") return ValidationSelfTests();
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
            gcLatencyMode = System.Runtime.GCSettings.LatencyMode.ToString(), gcConcurrent = Environment.GetEnvironmentVariable("DOTNET_gcConcurrent"), serverGC = System.Runtime.GCSettings.IsServerGC,
            os = RuntimeInformation.OSDescription, stopwatchFrequency = Stopwatch.Frequency, processorCount = Environment.ProcessorCount, portableKernelsForced = PortableKernelsForced, vectorHardwareAccelerated = System.Numerics.Vector.IsHardwareAccelerated, vectorIntWidth = System.Numerics.Vector<int>.Count,
            portable_backend_scope = "Forced portable disables explicit dense intrinsics only; System.Numerics.Vector query paths and CoreCLR/BCL SIMD can remain active. Availability is host capability, not proof a particular path executed.", avx2Available = System.Runtime.Intrinsics.X86.Avx2.IsSupported, popcntAvailable = System.Runtime.Intrinsics.X86.Popcnt.X64.IsSupported, rows = Rows, candidate_only_rows = CandidateOnlyRows,
            validation_protocol = "actual-timed-output-before-reset-v2", protocol = "Independent fresh processes per variant/mode/round. Calibration targets milliseconds with 8 MiB allocation budget per batch. All raw samples and GC counts retained. Timing units are operations except 256-probe loops reported per probe. No GC-forcing inside timed bodies.",
            tails = "p95/max describe calibrated batch-mean latency across samples, NOT individual-operation latency and NOT independent-process confidence intervals.",
            limitations = "CoreCLR managed host only; measured architecture is recorded above. No Unity/Mono, IL2CPP, Burst, physical-mobile execution, native peak memory, user telemetry, cross-architecture inference, or universal fastest claim. Member buffers exclude object headers/shared registry; cumulative allocation is not retained memory. Dense/sparse layout conversion uses public new+CopyFrom, including destination allocation." };
        File.WriteAllText(args[0], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("BENCH " + variant + " " + mode + " rows=" + Rows.Count + " newApiRows=" + CandidateOnlyRows.Count + " assertions=" + assertions + " failures=" + failures);
        return failures == 0 ? 0 : 1;
    }
}
