// Same workload source is compiled against each Runtime revision. No Runtime internals
// are read: the independent oracle uses ordinal names and string-prefix ancestry.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using GameplayTags;

internal static class Program
{
    private const int FixtureLeafCount = 2048;
    private const int FixtureGroups = 32;
    private const int DeepDepth = 128;
    private static GameplayTagContainer s_LastResult;
    private static long s_Observed;
    private static GameplayTag s_Sentinel;

    private enum Operation
    {
        Copy, CopyAlternating, CopyCount, CopySelf, Union, UnionCount,
        Intersection, IntersectionCount, IntersectionReuse, HasTagExactMixed, RemoveAddAfterCopy
    }

    private sealed class Workload
    {
        public string Id;
        public Operation Operation;
        public GameplayTagContainer Left;
        public GameplayTagContainer Right;
        public GameplayTagCountContainer CountLeft;
        public GameplayTagContainer Destination;
        public HashSet<string> Expected;
        public HashSet<string> AlternateExpected;
        public int Size;
        public int Depth;
        public int OverlapPercent;
        public int CapacityMultiplier;
        public int RequestedCapacity;
        public int Iterations;
        public bool SameInstance;
        public GameplayTag[] Probes;
        public bool[] ProbeExpected;
        public string ProbeChecksum;
        public string LeftKind => CountLeft == null ? "set" : "count (multiplicity 2..4)";
    }

    private sealed class Measurement
    {
        public string Id { get; set; }
        public string Operation { get; set; }
        public string Unit { get; set; }
        public int ExplicitTagCount { get; set; }
        public int LeftExplicitTagCount { get; set; }
        public int RightExplicitTagCount { get; set; }
        public int LeftTotalTagCount { get; set; }
        public int RightTotalTagCount { get; set; }
        public int ResultExplicitTagCount { get; set; }
        public int ResultTotalTagCount { get; set; }
        public int HierarchyDepth { get; set; }
        public int OverlapPercent { get; set; }
        public int CapacityMultiplier { get; set; }
        public int RequestedCapacity { get; set; }
        public string LeftKind { get; set; }
        public bool SameInstance { get; set; }
        public int Iterations { get; set; }
        public double NanosecondsPerOperation { get; set; }
        public double AllocatedBytesPerOperation { get; set; }
        public long ElapsedTicks { get; set; }
        public long AllocatedBytes { get; set; }
        public int Gen0Collections { get; set; }
        public int Gen1Collections { get; set; }
        public int Gen2Collections { get; set; }
        public string ObservedChecksum { get; set; }
        public string ExplicitSetChecksum { get; set; }
        public string ClosureSetChecksum { get; set; }
        public string AlternateExplicitSetChecksum { get; set; }
        public string LeftInputChecksum { get; set; }
        public string RightInputChecksum { get; set; }
        public string ProbeChecksum { get; set; }
    }

    private sealed class Report
    {
        public string Schema { get; set; } = "gameplaytags-bulk-v1";
        public string Variant { get; set; }
        public int Round { get; set; }
        public string Framework { get; set; }
        public string Architecture { get; set; }
        public string OperatingSystem { get; set; }
        public bool ServerGC { get; set; }
        public object TieredCompilation { get; set; }
        public bool Quick { get; set; }
        public int RegistryLeafCount { get; set; }
        public int RegistryTagCount { get; set; }
        public int RegistryShallowGroupCount { get; set; }
        public long StopwatchFrequency { get; set; }
        public string ObservedChecksum { get; set; }
        public List<Measurement> Measurements { get; set; }
    }

    private static int Main(string[] args)
    {
        try
        {
            string output = "bulk-sample.json", variant = "standalone", filter = null;
            int round = 0;
            bool quick = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--out": output = args[++i]; break;
                    case "--variant": variant = args[++i]; break;
                    case "--round": round = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--filter": filter = args[++i]; break;
                    case "--quick": quick = true; break;
                    default: throw new ArgumentException("Unknown argument: " + args[i]);
                }
            }

            InitializeRegistry();
            var workloads = CreateWorkloads();
            if (filter != null)
                workloads.RemoveAll(w => w.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0);
            if (workloads.Count == 0)
                throw new ArgumentException("No cases matched the filter.");
            var rows = new List<Measurement>(workloads.Count);
            // Rotation is identical for both variants in each round, changing which
            // operation sees the earliest cache/GC history across rounds.
            for (int i = 0; i < workloads.Count; i++)
            {
                Workload workload = workloads[(i + round) % workloads.Count];
                if (quick) workload.Iterations = Math.Max(32, workload.Iterations / 8);
                rows.Add(Measure(workload));
            }
            rows.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            var report = new Report
            {
                Variant = variant, Round = round, Framework = RuntimeInformation.FrameworkDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                OperatingSystem = RuntimeInformation.OSDescription, ServerGC = System.Runtime.GCSettings.IsServerGC,
                TieredCompilation = AppContext.GetData("System.Runtime.TieredCompilation"), Quick = quick,
                RegistryLeafCount = FixtureLeafCount + 4, RegistryTagCount = GameplayTagManager.TagCount,
                RegistryShallowGroupCount = FixtureGroups, StopwatchFrequency = Stopwatch.Frequency,
                ObservedChecksum = s_Observed.ToString(CultureInfo.InvariantCulture), Measurements = rows
            };
            string fullOutput = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutput));
            File.WriteAllText(fullOutput, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(variant + " round " + round + ": " + rows.Count + " cases, public-API oracles passed; " + fullOutput);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static string LeafName(int index) => "Bulk.G" + (index % FixtureGroups).ToString("D2", CultureInfo.InvariantCulture) +
        ".T" + index.ToString("D4", CultureInfo.InvariantCulture);

    private static string DeepParent()
    {
        string name = "Deep";
        for (int i = 1; i < DeepDepth - 1; i++) name += ".N" + i.ToString("D3", CultureInfo.InvariantCulture);
        return name;
    }

    private static void InitializeRegistry()
    {
        var declarations = new List<GameplayTagRegistration>(FixtureLeafCount + 4);
        for (int i = 0; i < FixtureLeafCount; i++) declarations.Add(new GameplayTagRegistration(LeafName(i)));
        string deep = DeepParent();
        declarations.Add(new GameplayTagRegistration(deep + ".A"));
        declarations.Add(new GameplayTagRegistration(deep + ".B"));
        declarations.Add(new GameplayTagRegistration(deep + ".C"));
        declarations.Add(new GameplayTagRegistration("Outside.Probe"));
        GameplayTagManager.Initialize(declarations.ToArray());
        s_Sentinel = GameplayTagManager.RequestTag("Outside.Probe");
    }

    private static List<string> Names(int size, int overlapPercent, bool right)
    {
        int overlap = size * overlapPercent / 100;
        var names = new List<string>(size);
        // Odd multiplier permutes each tested power-of-two size; neither operand is
        // inserted in runtime-ID order. Right-hand insertion uses a different order.
        for (int i = 0; i < size; i++)
        {
            int index = (i * (right ? 619 : 997)) % size;
            names.Add(LeafName(right && index >= overlap ? 1024 + index - overlap : index));
        }
        return names;
    }

    private static GameplayTagContainer Set(IEnumerable<string> names)
    {
        var set = new GameplayTagContainer();
        foreach (string name in names) set.AddTag(GameplayTagManager.RequestTag(name));
        return set;
    }

    private static GameplayTagCountContainer Counted(IEnumerable<string> names)
    {
        var set = new GameplayTagCountContainer();
        int index = 0;
        foreach (string name in names) set.AddTag(GameplayTagManager.RequestTag(name), 2 + index++ % 3);
        return set;
    }

    private static HashSet<string> NameSet(IEnumerable<string> names) => new HashSet<string>(names, StringComparer.Ordinal);

    private static HashSet<string> Combine(IEnumerable<string> left, IEnumerable<string> right, bool union)
    {
        var expected = NameSet(left);
        if (union) expected.UnionWith(right); else expected.IntersectWith(right);
        return expected;
    }

    private static List<Workload> CreateWorkloads()
    {
        var rows = new List<Workload>();
        foreach (int size in new[] { 16, 128, 1024 })
        {
            List<string> leftNames = Names(size, 100, false);
            GameplayTagContainer left = Set(leftNames);
            int[] overlaps = size == 1024 ? new[] { 0, 50, 100 } : new[] { 50 };
            AddCopy(rows, "copy_reuse/n" + size, Operation.Copy, left, null, NameSet(leftNames), null, size, 3, 1);
            if (size == 1024)
            {
                AddCopy(rows, "copy_reuse_8x_capacity/n1024", Operation.Copy, left, null, NameSet(leftNames), null, size, 3, 8);
                List<string> otherNames = Names(size, 0, true);
                AddCopy(rows, "copy_alternating_disjoint/n1024", Operation.CopyAlternating, left, Set(otherNames),
                    NameSet(leftNames), NameSet(otherNames), size, 3, 1);
                var count = Counted(leftNames);
                Workload copyCount = AddCopy(rows, "copy_count_normalize/n1024", Operation.CopyCount, null, null,
                    NameSet(leftNames), null, size, 3, 1, count);
                copyCount.Iterations = 1024;
                foreach (int capacityMultiplier in new[] { 1, 8 })
                {
                    Workload query = AddCopy(rows, "find_exact_after_copy/" + capacityMultiplier + "x_capacity/n1024",
                        Operation.HasTagExactMixed, left, null, NameSet(leftNames), null, size, 3, capacityMultiplier);
                    query.Probes = new GameplayTag[64];
                    query.ProbeExpected = new bool[64];
                    var probeNames = new List<string>();
                    for (int i = 0; i < query.Probes.Length; i++)
                    {
                        int index = (i * 17) % 1024 + ((i & 1) == 0 ? 0 : 1024);
                        string name = LeafName(index);
                        query.Probes[i] = GameplayTagManager.RequestTag(name);
                        query.ProbeExpected[i] = (i & 1) == 0;
                        probeNames.Add(name);
                    }
                    query.ProbeChecksum = SetChecksum(probeNames); query.Iterations = 1048576;
                    Workload mutation = AddCopy(rows, "remove_add_after_copy/" + capacityMultiplier + "x_capacity/n1024",
                        Operation.RemoveAddAfterCopy, left, null, NameSet(leftNames), null, size, 3, capacityMultiplier);
                    mutation.Probes = new GameplayTag[64];
                    probeNames.Clear();
                    for (int i = 0; i < mutation.Probes.Length; i++)
                    {
                        string name = LeafName((i * 17) % 1024);
                        mutation.Probes[i] = GameplayTagManager.RequestTag(name); probeNames.Add(name);
                    }
                    mutation.ProbeChecksum = SetChecksum(probeNames); mutation.Iterations = 65536;
                }
            }
            foreach (int overlap in overlaps)
            {
                List<string> rightNames = Names(size, overlap, true);
                GameplayTagContainer right = Set(rightNames);
                AddBinary(rows, "union_new/n" + size + "/overlap" + overlap, Operation.Union, left, right,
                    Combine(leftNames, rightNames, true), size, 3, overlap);
                AddBinary(rows, "intersection_new/n" + size + "/overlap" + overlap, Operation.Intersection, left, right,
                    Combine(leftNames, rightNames, false), size, 3, overlap);
                AddBinary(rows, "intersection_reuse/n" + size + "/overlap" + overlap, Operation.IntersectionReuse, left, right,
                    Combine(leftNames, rightNames, false), size, 3, overlap);
                if (size == 1024 && overlap == 50)
                {
                    GameplayTagCountContainer count = Counted(leftNames);
                    AddBinary(rows, "union_count_left/n1024/overlap50", Operation.UnionCount, null, right,
                        Combine(leftNames, rightNames, true), size, 3, overlap, count);
                    AddBinary(rows, "intersection_count_left/n1024/overlap50", Operation.IntersectionCount, null, right,
                        Combine(leftNames, rightNames, false), size, 3, overlap, count);
                }
            }
            if (size == 1024)
            {
                Workload selfCopy = AddCopy(rows, "copy_self/n1024", Operation.CopySelf, left, null, NameSet(leftNames), null, size, 3, 0);
                selfCopy.Destination = left; selfCopy.SameInstance = true; selfCopy.Iterations = 1048576;
                Workload sameUnion = AddBinary(rows, "union_new_same_instance/n1024", Operation.Union, left, left,
                    NameSet(leftNames), size, 3, 100); sameUnion.SameInstance = true;
                Workload sameIntersection = AddBinary(rows, "intersection_new_same_instance/n1024", Operation.Intersection, left, left,
                    NameSet(leftNames), size, 3, 100); sameIntersection.SameInstance = true;
                var empty = new GameplayTagContainer();
                AddBinary(rows, "union_new_empty_left/n1024", Operation.Union, empty, left, NameSet(leftNames), size, 3, -1);
                AddBinary(rows, "union_new_empty_right/n1024", Operation.Union, left, empty, NameSet(leftNames), size, 3, -1);
                AddBinary(rows, "intersection_new_empty_right/n1024", Operation.Intersection, left, empty,
                    NameSet(Array.Empty<string>()), size, 3, -1).Iterations = 32768;
            }
        }
        var noNames = NameSet(Array.Empty<string>());
        var emptyLeft = new GameplayTagContainer();
        var emptyRight = new GameplayTagContainer();
        AddCopy(rows, "copy_both_empty/n0", Operation.Copy, emptyLeft, null, noNames, null, 0, 0, 0).Iterations = 262144;
        AddBinary(rows, "union_new_both_empty/n0", Operation.Union, emptyLeft, emptyRight, noNames, 0, 0, -1).Iterations = 32768;
        AddBinary(rows, "intersection_new_both_empty/n0", Operation.Intersection, emptyLeft, emptyRight, noNames, 0, 0, -1).Iterations = 32768;

        string parent = DeepParent();
        var deepLeftNames = new[] { parent + ".A", parent + ".B" };
        var deepRightNames = new[] { parent + ".B", parent + ".C" };
        GameplayTagContainer deepLeft = Set(deepLeftNames), deepRight = Set(deepRightNames);
        AddCopy(rows, "copy_reuse/depth128/n2", Operation.Copy, deepLeft, null, NameSet(deepLeftNames), null, 2, 128, 1);
        AddBinary(rows, "union_new/depth128/n2/overlap50", Operation.Union, deepLeft, deepRight,
            Combine(deepLeftNames, deepRightNames, true), 2, 128, 50);
        AddBinary(rows, "intersection_new/depth128/n2/overlap50", Operation.Intersection, deepLeft, deepRight,
            Combine(deepLeftNames, deepRightNames, false), 2, 128, 50);
        AddBinary(rows, "intersection_reuse/depth128/n2/overlap50", Operation.IntersectionReuse, deepLeft, deepRight,
            Combine(deepLeftNames, deepRightNames, false), 2, 128, 50);
        var oneDeepName = new[] { parent + ".A" };
        AddCopy(rows, "copy_count_normalize/depth128/n1", Operation.CopyCount, null, null,
            NameSet(oneDeepName), null, 1, 128, 1, Counted(oneDeepName));
        // A deep path which is excluded from the result must not force a large
        // retained result or make operand order dominate a two-element query.
        var mixedDeepNames = new[] { parent + ".A", LeafName(0) };
        var mixedShallowNames = new[] { LeafName(0), LeafName(1) };
        GameplayTagContainer mixedDeep = Set(mixedDeepNames), mixedShallow = Set(mixedShallowNames);
        HashSet<string> mixedExpected = NameSet(new[] { LeafName(0) });
        AddBinary(rows, "intersection_new/deep_miss_left/n2", Operation.Intersection,
            mixedDeep, mixedShallow, mixedExpected, 2, 128, 50);
        AddBinary(rows, "intersection_new/deep_miss_right/n2", Operation.Intersection,
            mixedShallow, mixedDeep, mixedExpected, 2, 128, 50);
        return rows;
    }

    private static Workload AddCopy(List<Workload> rows, string id, Operation operation,
        GameplayTagContainer left, GameplayTagContainer right, HashSet<string> expected,
        HashSet<string> alternateExpected, int size, int depth, int capacityMultiplier,
        GameplayTagCountContainer count = null)
    {
        int total = count == null ? left.TagCount : count.TagCount;
        int capacity = total * capacityMultiplier;
        var workload = new Workload
        {
            Id = id, Operation = operation, Left = left, Right = right, CountLeft = count,
            Expected = expected, AlternateExpected = alternateExpected, Size = size, Depth = depth,
            OverlapPercent = operation == Operation.CopyAlternating ? 0 : -1,
            CapacityMultiplier = capacityMultiplier, RequestedCapacity = capacity,
            Destination = new GameplayTagContainer(capacity), Iterations = size >= 1024 ? 2048 : 32768
        };
        rows.Add(workload);
        return workload;
    }

    private static Workload AddBinary(List<Workload> rows, string id, Operation operation,
        GameplayTagContainer left, GameplayTagContainer right, HashSet<string> expected,
        int size, int depth, int overlap, GameplayTagCountContainer count = null)
    {
        int capacity = Math.Max(count == null ? left.TagCount : count.TagCount, right.TagCount);
        bool reuse = operation == Operation.IntersectionReuse;
        var workload = new Workload
        {
            Id = id, Operation = operation, Left = left, Right = right, CountLeft = count,
            Expected = expected, Size = size, Depth = depth, OverlapPercent = overlap,
            CapacityMultiplier = reuse ? 1 : 0, RequestedCapacity = reuse ? capacity : 0,
            Destination = reuse ? new GameplayTagContainer(capacity) : null,
            Iterations = size >= 1024 ? 512 : (size <= 16 && depth <= 3 ? 8192 : 2048)
        };
        rows.Add(workload);
        return workload;
    }

    private static Measurement Measure(Workload workload)
    {
        string beforeLeft = InputChecksum(workload.CountLeft ?? (IGameplayTagContainer)workload.Left);
        string beforeRight = InputChecksum(workload.Right);
        PrepareCopyDiagnostics(workload);
        // Preflight every alternating source and check independence/normalization.
        RunIterations(workload, 1);
        VerifyAndDrain(workload, 1, beforeLeft, beforeRight);
        if (workload.Operation == Operation.Union || workload.Operation == Operation.UnionCount ||
            workload.Operation == Operation.Intersection || workload.Operation == Operation.IntersectionCount)
        {
            GameplayTagContainer previousResult = Volatile.Read(ref s_LastResult);
            RunIterations(workload, 1);
            GameplayTagContainer nextResult = Volatile.Read(ref s_LastResult);
            if (ReferenceEquals(previousResult, nextResult))
                throw new InvalidOperationException(workload.Id + ": consecutive new results are the same object.");
            previousResult.AddTag(s_Sentinel);
            VerifySet(workload.Id, nextResult, workload.Expected);
            previousResult.RemoveTag(s_Sentinel);
            VerifyAndDrain(workload, 1, beforeLeft, beforeRight);
        }
        if (workload.Operation == Operation.CopyAlternating)
        {
            RunIterations(workload, 2);
            VerifyAndDrain(workload, 2, beforeLeft, beforeRight);
        }
        PrepareCopyDiagnostics(workload);
        RunIterations(workload, Math.Min(workload.Iterations, 64));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        long observed = RunIterations(workload, workload.Iterations);
        long elapsed = Stopwatch.GetTimestamp() - start;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        int gen0Delta = GC.CollectionCount(0) - gen0, gen1Delta = GC.CollectionCount(1) - gen1,
            gen2Delta = GC.CollectionCount(2) - gen2;
        HashSet<string> expected = Expected(workload, workload.Iterations);
        HashSet<string> closure = Closure(expected);
        var row = new Measurement
        {
            Id = workload.Id, Operation = workload.Operation.ToString(),
            Unit = workload.Operation == Operation.RemoveAddAfterCopy ? "one RemoveTag + AddTag pair" : "one public API call",
            ExplicitTagCount = workload.Size,
            LeftExplicitTagCount = workload.CountLeft == null ? workload.Left.ExplicitTagCount : workload.CountLeft.ExplicitTagCount,
            RightExplicitTagCount = workload.Right == null ? 0 : workload.Right.ExplicitTagCount,
            LeftTotalTagCount = workload.CountLeft == null ? workload.Left.TagCount : workload.CountLeft.TagCount,
            RightTotalTagCount = workload.Right == null ? 0 : workload.Right.TagCount,
            ResultExplicitTagCount = expected.Count, ResultTotalTagCount = closure.Count,
            HierarchyDepth = workload.Depth, OverlapPercent = workload.OverlapPercent,
            CapacityMultiplier = workload.CapacityMultiplier, RequestedCapacity = workload.RequestedCapacity,
            LeftKind = workload.LeftKind, SameInstance = workload.SameInstance, Iterations = workload.Iterations,
            NanosecondsPerOperation = elapsed * (1e9 / Stopwatch.Frequency) / workload.Iterations,
            AllocatedBytesPerOperation = (double)allocated / workload.Iterations,
            ElapsedTicks = elapsed, AllocatedBytes = allocated,
            Gen0Collections = gen0Delta, Gen1Collections = gen1Delta, Gen2Collections = gen2Delta,
            ObservedChecksum = observed.ToString(CultureInfo.InvariantCulture),
            ExplicitSetChecksum = SetChecksum(expected), ClosureSetChecksum = SetChecksum(closure),
            AlternateExplicitSetChecksum = workload.AlternateExpected == null ? null : SetChecksum(workload.AlternateExpected),
            LeftInputChecksum = beforeLeft, RightInputChecksum = beforeRight, ProbeChecksum = workload.ProbeChecksum
        };
        if (workload.Operation == Operation.HasTagExactMixed)
        {
            long expectedObserved = 1469598103934665603L;
            long value = ((long)expected.Count << 32) | (uint)closure.Count;
            for (int i = 0; i < workload.Iterations; i++)
                expectedObserved = unchecked(((expectedObserved ^ value) * 1099511628211L + i) ^
                    (workload.ProbeExpected[i & 63] ? 0x2468ace13579L : 0));
            if (observed != expectedObserved)
                throw new InvalidOperationException(workload.Id + ": wrong observed query results.");
        }
        VerifyAndDrain(workload, workload.Iterations, beforeLeft, beforeRight);
        // Prevent whole-batch elimination; the reference also escapes on every call.
        s_Observed = unchecked(s_Observed * 31 + observed);
        return row;
    }

    private static void PrepareCopyDiagnostics(Workload workload)
    {
        if (workload.Operation != Operation.HasTagExactMixed && workload.Operation != Operation.RemoveAddAfterCopy) return;
        GameplayTagContainer.Copy(workload.Destination, workload.Left);
        if (workload.Operation == Operation.HasTagExactMixed)
        {
            for (int i = 0; i < workload.Probes.Length; i++)
                if (workload.Destination.HasTagExact(workload.Probes[i]) != workload.ProbeExpected[i])
                    throw new InvalidOperationException(workload.Id + ": wrong preflight query result.");
        }
        else
        {
            GameplayTag first = workload.Probes[0];
            workload.Destination.RemoveTag(first);
            var removed = NameSet(workload.Expected); removed.Remove(first.Name);
            VerifySet(workload.Id, workload.Destination, removed);
            workload.Destination.AddTag(first);
            VerifySet(workload.Id, workload.Destination, workload.Expected);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Consume(GameplayTagContainer result, long previous, int iteration)
    {
        long value = ((long)result.ExplicitTagCount << 32) | (uint)result.TagCount;
        Volatile.Write(ref s_LastResult, result);
        return unchecked((previous ^ value) * 1099511628211L + iteration);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long RunIterations(Workload workload, int iterations)
    {
        long observed = 1469598103934665603L;
        // Switch once per batch; direct public generic calls retain normal concrete
        // specialization. No delegate, reflection or oracle runs in the timed loop.
        switch (workload.Operation)
        {
            case Operation.Copy:
            case Operation.CopySelf:
                for (int i = 0; i < iterations; i++)
                {
                    GameplayTagContainer.Copy(workload.Destination, workload.Left);
                    observed = Consume(workload.Destination, observed, i);
                }
                break;
            case Operation.CopyAlternating:
                for (int i = 0; i < iterations; i++)
                {
                    GameplayTagContainer source = (i & 1) == 0 ? workload.Left : workload.Right;
                    GameplayTagContainer.Copy(workload.Destination, source);
                    observed = Consume(workload.Destination, observed, i);
                }
                break;
            case Operation.CopyCount:
                for (int i = 0; i < iterations; i++)
                {
                    GameplayTagContainer.Copy(workload.Destination, workload.CountLeft);
                    observed = Consume(workload.Destination, observed, i);
                }
                break;
            case Operation.Union:
                for (int i = 0; i < iterations; i++)
                    observed = Consume(GameplayTagContainer.Union(workload.Left, workload.Right), observed, i);
                break;
            case Operation.UnionCount:
                for (int i = 0; i < iterations; i++)
                    observed = Consume(GameplayTagContainer.Union(workload.CountLeft, workload.Right), observed, i);
                break;
            case Operation.Intersection:
                for (int i = 0; i < iterations; i++)
                    observed = Consume(GameplayTagContainer.Intersection(workload.Left, workload.Right), observed, i);
                break;
            case Operation.IntersectionCount:
                for (int i = 0; i < iterations; i++)
                    observed = Consume(GameplayTagContainer.Intersection(workload.CountLeft, workload.Right), observed, i);
                break;
            case Operation.IntersectionReuse:
                for (int i = 0; i < iterations; i++)
                {
                    GameplayTagContainer.Intersection(workload.Destination, workload.Left, workload.Right);
                    observed = Consume(workload.Destination, observed, i);
                }
                break;
            case Operation.HasTagExactMixed:
                for (int i = 0; i < iterations; i++)
                {
                    bool found = workload.Destination.HasTagExact(workload.Probes[i & 63]);
                    observed = unchecked(Consume(workload.Destination, observed, i) ^ (found ? 0x2468ace13579L : 0));
                }
                break;
            case Operation.RemoveAddAfterCopy:
                for (int i = 0; i < iterations; i++)
                {
                    GameplayTag tag = workload.Probes[i & 63];
                    workload.Destination.RemoveTag(tag);
                    workload.Destination.AddTag(tag);
                    observed = Consume(workload.Destination, observed, i);
                }
                break;
            default: throw new InvalidOperationException("Unhandled operation.");
        }
        return observed;
    }

    private static HashSet<string> Expected(Workload workload, int iterations) =>
        workload.AlternateExpected != null && (iterations & 1) == 0 ? workload.AlternateExpected : workload.Expected;

    private static void VerifyAndDrain(Workload workload, int iterations, string leftInput, string rightInput)
    {
        GameplayTagContainer result = Volatile.Read(ref s_LastResult);
        if (result == null) throw new InvalidOperationException(workload.Id + ": no result.");
        HashSet<string> expected = Expected(workload, iterations);
        VerifySet(workload.Id, result, expected);
        VerifyInputs(workload, leftInput, rightInput);
        if (workload.Operation == Operation.CopySelf) return;
        if (ReferenceEquals(result, workload.Left) || ReferenceEquals(result, workload.Right) || ReferenceEquals(result, workload.CountLeft))
            throw new InvalidOperationException(workload.Id + ": result aliases an input.");

        // This also detects a shared backing store/COW omission. The sentinel is
        // unrelated to both operands, so equal cardinalities cannot hide a mutation.
        result.AddTag(s_Sentinel);
        VerifyInputs(workload, leftInput, rightInput);
        result.RemoveTag(s_Sentinel);
        VerifySet(workload.Id, result, expected);
        // A plain-set result must contribute exactly once per explicit tag even
        // when an operand has multiplicity. Remove each name once, in hash-set order.
        foreach (string name in expected) result.RemoveTag(GameplayTagManager.RequestTag(name));
        if (!result.IsEmpty || result.ExplicitTagCount != 0 || result.TagCount != 0)
            throw new InvalidOperationException(workload.Id + ": normalization or ancestor closure removal failed.");
        VerifyInputs(workload, leftInput, rightInput);
    }

    private static void VerifyInputs(Workload workload, string left, string right)
    {
        if (InputChecksum(workload.CountLeft ?? (IGameplayTagContainer)workload.Left) != left ||
            InputChecksum(workload.Right) != right)
            throw new InvalidOperationException(workload.Id + ": an input changed.");
    }

    private static void VerifySet(string id, GameplayTagContainer result, HashSet<string> expected)
    {
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (GameplayTag tag in result.GetExplicitTags())
            if (!actual.Add(tag.Name)) throw new InvalidOperationException(id + ": duplicate explicit enumeration.");
        if (actual.Count != result.ExplicitTagCount || !actual.SetEquals(expected))
            throw new InvalidOperationException(id + ": wrong explicit set.");
        var closure = new HashSet<string>(StringComparer.Ordinal);
        foreach (GameplayTag tag in result.GetTags())
            if (!closure.Add(tag.Name)) throw new InvalidOperationException(id + ": duplicate closure enumeration.");
        if (closure.Count != result.TagCount || !closure.SetEquals(Closure(expected)))
            throw new InvalidOperationException(id + ": wrong ancestor closure.");
    }

    private static HashSet<string> Closure(IEnumerable<string> names)
    {
        var closure = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            closure.Add(name);
            int dot = name.IndexOf('.');
            while (dot >= 0)
            {
                closure.Add(name.Substring(0, dot));
                dot = name.IndexOf('.', dot + 1);
            }
        }
        return closure;
    }

    private static ulong NameHash(string name)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char character in name) hash = unchecked((hash ^ character) * 1099511628211UL);
        return hash;
    }

    private static string SetChecksum(IEnumerable<string> names)
    {
        ulong sum = 0, xor = 0;
        int count = 0;
        foreach (string name in names)
        {
            ulong hash = NameHash(name);
            sum = unchecked(sum + hash); xor ^= hash; count++;
        }
        return count.ToString(CultureInfo.InvariantCulture) + ":" + sum.ToString("x16", CultureInfo.InvariantCulture) +
            ":" + xor.ToString("x16", CultureInfo.InvariantCulture);
    }

    private static string InputChecksum(IGameplayTagContainer input)
    {
        if (input == null) return null;
        var explicitNames = new List<string>();
        var closureNames = new List<string>();
        IGameplayTagCountContainer counted = input as IGameplayTagCountContainer;
        foreach (GameplayTag tag in input.GetExplicitTags())
            explicitNames.Add(counted == null ? tag.Name : tag.Name + "=" + counted.GetExplicitTagCount(tag).ToString(CultureInfo.InvariantCulture));
        foreach (GameplayTag tag in input.GetTags())
            closureNames.Add(counted == null ? tag.Name : tag.Name + "=" + counted.GetTagCount(tag).ToString(CultureInfo.InvariantCulture));
        return input.ExplicitTagCount.ToString(CultureInfo.InvariantCulture) + "/" + input.TagCount.ToString(CultureInfo.InvariantCulture) +
            "/" + SetChecksum(explicitNames) + "/" + SetChecksum(closureNames);
    }
}
