// Real public-API lifecycle comparison. Selection is production FromTagsForBulk/UnionForBulk only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameplayTags;
using Set = GameplayTags.RuntimeTagSet;

internal static class BulkWorkload
{
    const int StandardSeed = 20261001, BoundarySeed = 20261029;
    static int seed;
    static string implementation, strategy, suite, label;
    static int round;
    static TagSetStorage requested;
    static bool bulkPolicy, keepExistingAuto;
    static int samples, failures, assertions, checksum;
    static double targetMs;
    static Set sink, lastA, lastB;
    static object allocationSink;
    static readonly List<object> rows = new List<object>();
#if GAMEPLAYTAGS_FORCE_PORTABLE
    const bool portable = true;
#else
    const bool portable = false;
#endif
    static void Check(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
    static string Digest(int[] a, int[] b) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(",", a) + "|" + string.Join(",", b))));
    static TagSetStorage Requested => requested;
    static Set Build(TagRegistry registry, RuntimeTag[] tags, bool ordinary = false)
    {
        TagSetStorage mode = ordinary ? TagSetStorage.Auto : Requested;
#if BULK_API
        if (!ordinary && bulkPolicy) return Set.FromTagsForBulk(registry, tags);
#endif
#if PREPARED_API
        return Set.FromTags(registry, tags, 0, mode);
#else
        var result = new Set(registry, tags.Length, mode); foreach (var tag in tags) result.AddTag(tag); return result;
#endif
    }
    static Set Union(Set a, Set b)
    {
#if BULK_API
        if (bulkPolicy) return Set.UnionForBulk(a, b);
#endif
        return Set.Union(a, b, Requested);
    }
    static Set FromExisting(Set source)
    {
#if BULK_API
        if (bulkPolicy)
        {
            // Selection and direct conversion are both real public calls and both remain timed.
            // A retained input is explicitly already owned by this lifecycle, not a copy API result.
            TagSetStorage selected = source.SelectBulkStorage();
            return source.Storage == selected ? source : source.ToStorageForBulk();
        }
#endif
        if (keepExistingAuto || source.Storage == Requested) return source;
        var result = new Set(source.Registry, source.Count, Requested); result.CopyFrom(source); return result;
    }
    static int? DenseWordsPerPackedRecord
    {
        get
        {
#if BULK_API
            return RuntimeBitOperations.DenseWordsPerPackedRecord;
#else
            return null;
#endif
        }
    }
    static object Shape(Set value)
    {
        if (value == null) return null;
#if BULK_API
        int records = value.RecordCount, recordCapacity = value.RecordCapacity, reserved = value.ReservedMemberCapacity;
#else
        int records = 0, recordCapacity = 0, reserved = value.Capacity;
#endif
        return new { storage = value.Storage.ToString(), members = value.Count, memberCapacity = value.Capacity,
            reservedMemberCapacity = reserved, physicalRecordCount = records, physicalRecordCapacity = recordCapacity,
            bufferBytes = value.BufferBytes };
    }
    sealed class Fixture
    {
        public int universe, members, rightMembers, overlap;
        public string leftPattern, rightPattern, sharingPattern, digest;
        public TagRegistry registry;
        public int[] x, y, union, intersection, difference, conditionIds;
        public RuntimeTag[] left, right, probes;
        public RuntimeTag missing, parent;
        public Set a, b, existingA, existingB, work, mutable, conditions, emptyQueryInput;
        public bool anyExact, allExact, anyHierarchy, allHierarchy;
        public FrozenGameplayTagQuery query;
        public GameplayTagQuery authoringQuery;
        public int exactHits;
        public object Details => new { a = Shape(lastA), b = Shape(lastB), result = Shape(sink),
            originalExistingA = Shape(existingA), originalExistingB = Shape(existingB),
            inputBlocks16A = x.Select(id => id >> 4).Distinct().Count(), inputBlocks16B = y.Select(id => id >> 4).Distinct().Count(),
            inputSpanWordsA = x.Length == 0 ? 0 : (x[x.Length - 1] >> 6) - (x[0] >> 6) + 1,
            actualRegistryCount = registry.Count, retainsExistingA = ReferenceEquals(lastA, existingA), retainsExistingB = ReferenceEquals(lastB, existingB) };
    }
    static void Verify(Set set, int[] expected, TagRegistry registry)
    {
        Check(set != null && ReferenceEquals(set.Registry, registry), "Wrong actual registry");
        Check(set.Count == expected.Length, "Wrong actual Count"); int at = 0;
        foreach (var tag in set) Check(at < expected.Length && tag.RuntimeIndex == expected[at++], "Wrong actual membership/order");
        Check(at == expected.Length, "Wrong actual enumeration length");
    }
    static void VerifyInputs(Fixture f)
    { Verify(f.a, f.x, f.registry); Verify(f.b, f.y, f.registry); Verify(f.existingA, f.x, f.registry); Verify(f.existingB, f.y, f.registry); Verify(f.conditions, f.conditionIds, f.registry); }
    static int[] Candidates(int[] leaves, string pattern, int seed, int count)
    {
        if (pattern == "contiguous")
        {
            int start = (leaves.Length - 2 * count) / 3;
            return leaves.Skip(start).Concat(leaves.Take(start)).ToArray();
        }
        if (pattern == "clusters4")
            return Enumerable.Range(0, leaves.Length).Select(i => leaves[(i % 4) * (leaves.Length / 4) + i / 4]).ToArray();
        var shuffled = (int[])leaves.Clone(); var random = new Random(seed);
        for (int i = shuffled.Length - 1; i > 0; i--) { int j = random.Next(i + 1); int t = shuffled[i]; shuffled[i] = shuffled[j]; shuffled[j] = t; }
        return shuffled;
    }
    static Fixture Create(TagRegistry registry, int[] leaves, int count, int rightCount, int overlap, string leftPattern, string rightPattern, string sharingPattern = "spread")
    {
        int[] x = Candidates(leaves, leftPattern, seed + count, count).Take(count).OrderBy(id => id).ToArray();
        var owned = new HashSet<int>(x); int shared = Math.Min(count, rightCount) * overlap / 100;
        int[] y = Enumerable.Range(0, shared).Select(i => x[sharingPattern == "prefix" ? i : sharingPattern == "suffix" ? count - shared + i : (int)((long)i * count / Math.Max(1, shared))])
            .Concat(Candidates(leaves, rightPattern, seed + count + rightCount + 29, rightCount).Where(id => !owned.Contains(id)).Take(rightCount - shared)).OrderBy(id => id).ToArray();
        var f = new Fixture { registry = registry, universe = leaves.Length, members = count, rightMembers = rightCount, overlap = overlap, sharingPattern = sharingPattern,
            leftPattern = leftPattern, rightPattern = rightPattern, x = x, y = y, digest = Digest(x, y),
            union = x.Union(y).OrderBy(id => id).ToArray(), intersection = x.Intersect(y).ToArray(), difference = x.Except(y).ToArray(),
            left = x.Select(registry.GetTagAt).ToArray(), right = y.Select(registry.GetTagAt).ToArray() };
        f.a = Build(registry, f.left); f.b = Build(registry, f.right);
        f.existingA = Build(registry, f.left, true); f.existingB = Build(registry, f.right, true);
        f.work = Union(f.a, f.b);
        int[] parentIds = y.Select(id => registry.GetTagAt(id).GetDirectParent().RuntimeIndex).Distinct().OrderBy(id => id).ToArray();
        f.conditionIds = parentIds;
        f.conditions = Build(registry, parentIds.Select(registry.GetTagAt).ToArray());
        var ownedParents = new HashSet<int>(x.Select(id => registry.GetTagAt(id).GetDirectParent().RuntimeIndex));
        f.anyExact = f.intersection.Length > 0; f.allExact = f.intersection.Length == y.Length;
        f.anyHierarchy = parentIds.Any(ownedParents.Contains); f.allHierarchy = parentIds.All(ownedParents.Contains);
        int missing = leaves.Last(id => Array.BinarySearch(f.union, id) < 0); f.missing = registry.GetTagAt(missing);
        f.parent = f.left[0].GetDirectParent();
        f.probes = Enumerable.Range(0, 32).Select(i => i % 2 == 0 ? f.left[(i * 37) % count] : f.missing).ToArray();
        f.exactHits = 16;
        var expression = GameplayTagQueryExpression.AllTagsMatch();
        foreach (var tag in f.left.Take(4)) expression.AddTag(GameplayTagManager.RequestTag(tag.Name));
        f.authoringQuery = new GameplayTagQuery(expression); f.query = f.authoringQuery.Freeze(registry);
        f.emptyQueryInput = new Set(registry);
        Check(f.query.Matches(f.a) && !f.query.Matches(f.emptyQueryInput), "Frozen query positive/negative preflight");
        f.mutable = Build(registry, f.left);
        Check(f.mutable.AddTag(f.missing), "Mutation priming add return");
        Check(f.mutable.HasTagExact(f.missing) && f.mutable.Count == count + 1, "Mutation priming intermediate add membership");
        Check(f.mutable.RemoveTag(f.missing), "Mutation priming remove return");
        Check(!f.mutable.HasTagExact(f.missing) && f.mutable.Count == count, "Mutation priming intermediate remove membership");
        VerifyInputs(f); Verify(f.work, f.union, registry); Verify(f.mutable, f.x, registry);
        return f;
    }
    static void Measure(Fixture f, string operation, Func<int, int> body, Action<int, int> actual, bool zero = false, int units = 1, string scope = null)
    {
        int iterations = 0, completed = 0;
        double[] ns = Array.Empty<double>(), bytes = Array.Empty<double>(), batchMs = Array.Empty<double>();
        long[] ticks = Array.Empty<long>(); int[] values = Array.Empty<int>(); int[][] gc = Array.Empty<int[]>();
        try
        {
            lastA = f.a; lastB = f.b; sink = f.work;
            int first = body(1); actual(first, 1);
            long allocated = GC.GetAllocatedBytesForCurrentThread(); checksum ^= body(1);
            long estimate = GC.GetAllocatedBytesForCurrentThread() - allocated;
            int limit = estimate > 0 ? (int)Math.Max(1, Math.Min(1 << 20, (8L << 20) / estimate)) : 1 << 20;
            iterations = 1;
            while (true)
            {
                long before = Stopwatch.GetTimestamp(); checksum ^= body(iterations);
                if ((Stopwatch.GetTimestamp() - before) * 1000.0 / Stopwatch.Frequency >= targetMs || iterations >= limit) break;
                iterations = Math.Min(limit, iterations * 2);
            }
            ns = new double[samples]; bytes = new double[samples]; values = new int[samples]; gc = new int[samples][]; batchMs = new double[samples]; ticks = new long[samples];
            for (int sample = 0; sample < samples; sample++)
            {
                int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
                long alloc = GC.GetAllocatedBytesForCurrentThread(), before = Stopwatch.GetTimestamp();
                int value = body(iterations); checksum ^= value;
                long elapsed = Stopwatch.GetTimestamp() - before;
                bytes[sample] = (GC.GetAllocatedBytesForCurrentThread() - alloc) / (iterations * (double)units);
                ns[sample] = elapsed * (1e9 / Stopwatch.Frequency) / (iterations * (double)units);
                gc[sample] = new[] { GC.CollectionCount(0) - g0, GC.CollectionCount(1) - g1, GC.CollectionCount(2) - g2 };
                values[sample] = value; ticks[sample] = elapsed; batchMs[sample] = elapsed * 1000.0 / Stopwatch.Frequency; completed = sample + 1;
                if (zero) Check(bytes[sample] == 0, "Prepared operation allocated under BatchGC: " + bytes[sample]);
                actual(value, iterations); VerifyInputs(f);
            }
            rows.Add(new { operation, f.universe, f.members, f.rightMembers, f.overlap, f.leftPattern, f.rightPattern, f.sharingPattern, f.digest,
                status = "measured", iterations, units, ns, allocated_bytes = bytes, returned_values = values, gc_collections = gc, elapsed_ticks = ticks, batch_ms = batchMs, completed_samples = completed,
                timed_output_validated = true, zero_allocation_required = zero, scope, details = f.Details });
        }
        catch (Exception error)
        {
            failures++; rows.Add(new { operation, f.universe, f.members, f.rightMembers, f.overlap, f.leftPattern, f.rightPattern, f.sharingPattern, f.digest, status = "failed", error = error.ToString(), iterations, units, completed_samples = completed,
                ns = ns.Take(completed).ToArray(), allocated_bytes = bytes.Take(completed).ToArray(), returned_values = values.Take(completed).ToArray(),
                elapsed_ticks = ticks.Take(completed).ToArray(), batch_ms = batchMs.Take(completed).ToArray(), gc_collections = gc.Take(completed).ToArray(), scope });
            Console.WriteLine("FAILED " + operation + " " + f.members + " " + f.leftPattern + "/" + f.rightPattern + ": " + error.Message);
        }
    }
    static int BulkLoop(Fixture f, int op, int repeats)
    {
        for (int i = 0; i < repeats; i++)
        {
            if (op == 0) sink = Union(f.a, f.b);
            else if (op == 1) { Set.UnionInto(f.a, f.b, f.work); sink = f.work; }
            else if (op == 2) { Set.IntersectionExactInto(f.a, f.b, f.work); sink = f.work; }
            else if (op == 3) { f.work.CopyFrom(f.a); f.work.RemoveTags(f.b); sink = f.work; }
            else { f.work.CopyFrom(f.a); f.work.AppendTags(f.b); sink = f.work; }
        }
        return sink.Count;
    }
    static int Lifecycle(Fixture f, bool existing, int horizon, int repeats)
    {
        int total = 0;
        for (int r = 0; r < repeats; r++)
        {
            Set a = existing ? FromExisting(f.existingA) : Build(f.registry, f.left);
            Set b = existing ? FromExisting(f.existingB) : Build(f.registry, f.right);
            Set work = Union(a, b); // The actual public API chooses/reserves the reusable destination. Cost included.
            for (int h = 0; h < horizon; h++)
            {
                Set.UnionInto(a, b, work); total += work.Count;
                Set.IntersectionExactInto(a, b, work); total += 3 * work.Count;
                work.CopyFrom(a); work.RemoveTags(b); total += 5 * work.Count;
                work.CopyFrom(a); work.AppendTags(b); total += 7 * work.Count;
                foreach (var probe in f.probes) total += a.HasTagExact(probe) ? 1 : 0;
                total += a.HasTag(f.parent) ? 1 : 0; total += f.query.Matches(a) ? 1 : 0;
                total += work.AddTag(f.missing) ? 1 : 0; total += work.RemoveTag(f.missing) ? 1 : 0;
            }
            lastA = a; lastB = b; sink = work;
        }
        return total;
    }
    static void RunFixture(Fixture f)
    {
        Measure(f, "prepare.from_resolved", r => { for (int i = 0; i < r; i++) sink = Build(f.registry, f.left); lastA = sink; return sink.Count; },
            (value, repeats) => { Check(value == f.members, "Build return"); Verify(sink, f.x, f.registry); }, scope: "One new owned set; resolved input handles pre-exist; policy scan and temporary buffers included");
        Measure(f, "convert.from_existing", r => { for (int i = 0; i < r; i++) sink = FromExisting(f.existingA); lastA = sink; return sink.Count; },
            (value, repeats) => { Check(value == f.members, "Conversion return"); Verify(sink, f.x, f.registry); }, scope: "Auto keeps existing owned input; forced same-layout keeps it; Bulk calls SelectBulkStorage and retains matching layout, otherwise calls independent ToStorageForBulk; all decision/conversion cost included");
        if (suite != "boundary")
        {
        Measure(f, "query.exact_mixed", r => { int total = 0; for (int i = 0; i < r; i++) foreach (var tag in f.probes) total += f.a.HasTagExact(tag) ? 1 : 0; sink = null; return total; },
            (value, repeats) => Check(value == f.exactHits * repeats, "Exact checksum"), true, 32);
        Measure(f, "query.hierarchy_hit", r => { int total = 0; for (int i = 0; i < r; i++) total += f.a.HasTag(f.parent) ? 1 : 0; sink = null; return total; },
            (value, repeats) => Check(value == repeats, "Hierarchy checksum"), true);
        for (int predicate = 0; predicate < 4; predicate++)
        {
            int op = predicate;
            string[] names = { "predicate.any_exact", "predicate.all_exact", "predicate.any_hierarchy", "predicate.all_hierarchy" };
            bool expected = op == 0 ? f.anyExact : op == 1 ? f.allExact : op == 2 ? f.anyHierarchy : f.allHierarchy;
            Measure(f, names[op], r => {
                int total = 0;
                for (int i = 0; i < r; i++) total += (op == 0 ? f.a.HasAnyExact(f.b) : op == 1 ? f.a.HasAllExact(f.b) : op == 2 ? f.a.HasAny(f.conditions) : f.a.HasAll(f.conditions)) ? 1 : 0;
                lastB = op < 2 ? f.b : f.conditions; sink = null; return total;
            }, (value, repeats) => Check(value == (expected ? repeats : 0), "Set predicate checksum"), true,
                scope: "Boolean predicate only, no materialized membership result; hierarchy conditions are direct parents of right operand members");
        }
        FrozenGameplayTagQuery frozenSink = null;
        Measure(f, "query.freeze", r => { for (int i = 0; i < r; i++) frozenSink = f.authoringQuery.Freeze(f.registry); sink = null; return frozenSink.NodeCount; },
            (value, repeats) => Check(frozenSink.NodeCount == value && frozenSink.Matches(f.a) && !frozenSink.Matches(f.emptyQueryInput), "Frozen query actual output"));
        Measure(f, "query.frozen_match", r => { int total = 0; for (int i = 0; i < r; i++) total += f.query.Matches(f.a) ? 1 : 0; sink = null; return total; },
            (value, repeats) => Check(value == repeats, "Frozen match checksum"), true);
        Measure(f, "mutation.prepared_pair", r => { int total = 0; for (int i = 0; i < r; i++) { total += f.mutable.AddTag(f.missing) ? 1 : 0; total += f.mutable.RemoveTag(f.missing) ? 1 : 0; } lastA = f.mutable; sink = null; return total; },
            (value, repeats) => { Check(value == 2 * repeats, "Mutation checksum"); Verify(f.mutable, f.x, f.registry); }, true,
            scope: "One add/remove pair; separate priming add/remove reserves natural growth before timing; actual post-prime capacity disclosed");
        string[] operations = { "bulk.union_new", "bulk.union_into", "bulk.intersection_into", "bulk.difference_reuse", "bulk.append_reuse" };
        for (int op = 0; op < operations.Length; op++)
        {
            int selected = op; int[] expected = op == 2 ? f.intersection : op == 3 ? f.difference : f.union;
            Measure(f, operations[op], r => BulkLoop(f, selected, r),
                (value, repeats) => { Check(value == expected.Length, "Bulk return"); Verify(sink, expected, f.registry); }, op != 0);
        }
        }
        int stepValue = 8 * f.union.Length + 3 * f.intersection.Length + 5 * f.difference.Length + f.exactHits + 4;
        foreach (bool existing in new[] { false, true })
        foreach (int horizon in new[] { 0, 1, 32 })
        {
            bool fromExisting = existing; int h = horizon;
            Measure(f, (existing ? "existing" : "fresh") + ".lifecycle.h" + h, r => Lifecycle(f, fromExisting, h, r),
                (value, repeats) => { Check(value == unchecked(stepValue * h * repeats), "Lifecycle checksum"); Verify(sink, f.union, f.registry); Verify(lastA, f.x, f.registry); Verify(lastB, f.y, f.registry); },
                scope: "Includes both operand preparation/conversion and initial allocating union destination, then h rounds union/intersection/difference/append,32 exact probes,one hierarchy probe,one frozen query,one add/remove pair. Shared registry/resolved source/query pre-exist. Mutation growth is INCLUDED.");
        }
    }
    static int[] BoundarySizes(int registryCount, int definitions)
    {
        int words = (registryCount + 63) / 64;
        return new[] { 2, 4, 8, 16 }.SelectMany(multiplier => new[] { multiplier * words - 1, multiplier * words + 1 })
            .Select(n => Math.Max(8, Math.Min(definitions / 2, n))).Distinct().OrderBy(n => n).ToArray();
    }
    static object AllocationControls()
    {
        Check(GCSettings.LatencyMode == GCLatencyMode.Batch, "BatchGC is mandatory for every variant because background-GC allocation accounting is unreliable on this host");
        var a = new int[2048]; var b = new int[2048]; a[13] = 73;
        for (int i = 0; i < 100; i++) Array.Copy(a, b, a.Length);
        long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 10000; i++) Array.Copy(a, b, a.Length);
        long negative = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread(); allocationSink = new byte[4096]; long positive = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(negative == 0 && b[13] == 73, "Allocation negative/copy control failed"); Check(positive >= 4096, "Allocation positive control failed");
        return new { negative_copy_bytes = negative, positive_new_array_bytes = positive, copies = 10000, bytes_per_copy = a.Length * 4 };
    }
    static int Main(string[] args)
    {
        string output = args[0]; implementation = args[1]; strategy = args[2]; suite = args[3]; samples = int.Parse(args[4]); targetMs = double.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture); label = args[6]; round = int.Parse(args[7]);
        seed = suite == "boundary" ? BoundarySeed : StandardSeed;
        bulkPolicy = strategy == "Bulk"; keepExistingAuto = strategy == "Auto";
        requested = bulkPolicy ? TagSetStorage.Auto : (TagSetStorage)Enum.Parse(typeof(TagSetStorage), strategy);
        Check(samples >= 3 && targetMs > 0, "Invalid sampling"); object controls = AllocationControls();
        foreach (int universe in new[] { 65536, 262144 })
        {
            string[] names = Enumerable.Range(0, universe).Select(i => "Bulk.G" + (i / 64).ToString("D5") + ".T" + i.ToString("D7")).ToArray();
            var settings = UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
            settings.ReplaceAll(names.Select(n => new GameplayTagDefinition(n, "", "Default", false, true)).ToList(), new List<GameplayTagRedirect>(), new List<GameplayTagSource> { new GameplayTagSource("Default", "", false) });
            GameplayTagManager.Initialize(settings, true); var registry = GameplayTagManager.CurrentRegistry;
            int[] leaves = names.Select(n => registry.Resolve(n).RuntimeIndex).OrderBy(id => id).ToArray();
            if (suite == "regression")
            {
                // Fixed regression coordinates, not a held-out policy selection set.
                if (universe == 65536)
                {
                    foreach (string pair in new[] { "contiguous:scattered", "scattered:contiguous" })
                    foreach (string sharing in new[] { "spread", "suffix" })
                    { var split = pair.Split(':'); RunFixture(Create(registry, leaves, 128, 128, 50, split[0], split[1], sharing)); }
                    foreach (bool reverse in new[] { false, true })
                    foreach (string sharing in new[] { "prefix", "spread", "suffix" })
                        RunFixture(Create(registry, leaves, reverse ? 8 : 4096, reverse ? 4096 : 8, 50, "contiguous", "contiguous", sharing));
                }
                RunFixture(Create(registry, leaves, 4096, 4096, 50, "contiguous", "contiguous", "spread"));
                if (universe == 262144)
                    foreach (string pair in new[] { "contiguous:scattered", "scattered:contiguous" })
                    foreach (string sharing in new[] { "spread", "suffix" })
                    { var split = pair.Split(':'); RunFixture(Create(registry, leaves, 16384, 16384, 50, split[0], split[1], sharing)); }
                Console.WriteLine(implementation + "/" + strategy + " U=" + universe + " rows=" + rows.Count + " failures=" + failures);
                continue;
            }
            int[] sizes = suite == "boundary" ? BoundarySizes(registry.Count, universe) : suite == "smoke" ? (universe == 65536 ? new[] { 128, 1024, 4096 } : new[] { 128, 4096, 16384 }) : new[] { 8, 64, 128, 1024, 4096, 16384 };
            string[] patterns = suite != "full" ? new[] { "contiguous:contiguous", "scattered:scattered", "contiguous:scattered" } : new[] { "contiguous:contiguous", "clusters4:clusters4", "scattered:scattered", "contiguous:scattered", "scattered:contiguous" };
            foreach (int count in sizes) foreach (string pair in patterns) foreach (int overlap in suite != "full" ? new[] { 50 } : new[] { 0, 50, 100 })
            foreach (string sharing in suite == "boundary" ? new[] { "spread", "suffix" } : new[] { "spread" })
            { var split = pair.Split(':'); RunFixture(Create(registry, leaves, count, count, overlap, split[0], split[1], sharing)); }
            if (universe == 65536 && suite != "boundary")
                foreach (bool reverse in new[] { false, true })
                foreach (string pattern in new[] { "contiguous", "scattered" })
                foreach (string sharing in new[] { "prefix", "spread", "suffix" })
                    RunFixture(Create(registry, leaves, reverse ? 8 : 4096, reverse ? 4096 : 8, 50, pattern, pattern, sharing));
            Console.WriteLine(implementation + "/" + strategy + " U=" + universe + " rows=" + rows.Count + " failures=" + failures);
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new { label, round, implementation, strategy, suite, samples, targetMs, stopwatchFrequency = Stopwatch.Frequency, seed, portableKernelsForced = portable,
            attribution_protocol = "source-and-executable-v1", executablePath = typeof(BulkWorkload).Assembly.Location,
            executableSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(BulkWorkload).Assembly.Location))).ToLowerInvariant(),
            runtimeSourceRoot = args[8], sourceInventorySha256 = args[9], harnessSha256 = args[10], runnerSha256 = args[11],
            runtime = RuntimeInformation.FrameworkDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(), os = RuntimeInformation.OSDescription,
            gcMode = GCSettings.LatencyMode.ToString(), serverGC = GCSettings.IsServerGC,
            vectorHardwareAccelerated = System.Numerics.Vector.IsHardwareAccelerated, vectorIntWidth = System.Numerics.Vector<int>.Count,
            denseWordsPerPackedRecord = DenseWordsPerPackedRecord,
            portable_backend_scope = "Forced portable disables explicit System.Runtime.Intrinsics kernels; System.Numerics.Vector and BCL acceleration may remain. Availability is host capability, not proof a particular path executed.", controls, failures, assertions, checksum, rows,
            validation_protocol = "actual-timed-output-before-reset-BatchGC-v1", workload_protocol = "direct-existing-conversion-v2",
            study_role = suite == "boundary" ? "predeclared boundary holdout; do not retune policy from these rows or merge with development gates" : suite == "regression" ? "fixed regression suite with independent same-executable Auto A/A controls" : "development matrix",
            boundary_definition = suite == "boundary" ? "N at2W,4W,8W,16W minus/plus1, clamped8..U/2; W includes implicit parents. These are cardinality bounds; parent gaps change actual occupied records. New fixed seed20261029; localized/scattered/mixed,50% overlap,spread/suffix; preparation and h0/h1/h32 fresh/existing only." : null,
            input_setup = "Resolved runtime handle arrays are sorted and deduplicated outside timing. This measures ordered loading, not unordered loading.",
            timing = "All raw calibrated batch means retained. Report median of process medians; p95/max are batch-mean tails, not individual operation latency. No per-row oracle or timing-trained selector.",
            limitations = "Managed host only, not Unity/IL2CPP/mobile. BatchGC is a measurement control for an observed .NET8 background-GC allocation-counter defect and is not a production GC recommendation. Existing Bulk explicitly pays SelectBulkStorage; matching physical layout retains the already-owned input, otherwise independent ToStorageForBulk conversion is charged. Buffer payload excludes headers/shared registry; allocation is cumulative, not peak/retained." }, new JsonSerializerOptions { WriteIndented = true }));
        GC.KeepAlive(allocationSink); return failures == 0 ? 0 : 1;
    }
}
