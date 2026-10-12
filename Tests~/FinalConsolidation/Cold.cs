using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using GameplayTags;

// One requested operation per fresh process. Inputs/setup are outside the timer.
// First-call elapsed time includes JIT effects; it is not a warmed latency sample.
internal static class FinalConsolidationCold
{
    private static readonly Func<long> Allocated = Counter();
    private static Func<long> Counter()
    {
        var m = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
        if (m == null) throw new NotSupportedException("Real per-thread allocation counter required.");
        return (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), m);
    }
    private static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    public static void Main(string[] args)
    {
        string mode = args[0];
        string[] names = Enumerable.Range(0, 2048).Select(i => "Cold.G" + (i % 32) + ".L" + i).ToArray();
        string deepName = "Depth" + string.Concat(Enumerable.Repeat(".N", 255));
        GameplayTagManager.Initialize(names.Concat(new[] { deepName }).Select(n => new GameplayTagRegistration(n)).ToArray());
        var tags = names.Select(n => GameplayTagManager.RequestTag(n)).ToArray();
        var deep = GameplayTagManager.RequestTag(deepName);
        GameplayTagContainer result = new GameplayTagContainer(4096);
        GameplayTagCountContainer countResult = null;
        var left = new GameplayTagContainer(); var right = new GameplayTagContainer();
        var workspace = new GameplayTagIntersectionWorkspace();
        if (mode == "copy" || mode == "intersection" || mode == "workspace" || mode == "union")
        {
            for (int i = 0; i < 1024; i++) { left.AddTag(tags[(i * 719) & 1023]); right.AddTag(tags[512 + ((i * 719) & 1023)]); }
            if (mode == "workspace") workspace.EnsureCapacity(1024);
        }
        Action action;
        int expected;
        switch (mode)
        {
            case "set-shallow": action = () => { result = new GameplayTagContainer(); result.AddTag(tags[0]); }; expected = 1; break;
            case "set-deep": action = () => { result = new GameplayTagContainer(); result.AddTag(deep); }; expected = 1; break;
            case "count": action = () => { countResult = new GameplayTagCountContainer(); countResult.AddTag(tags[0], 3); }; expected = 1; break;
            case "copy": action = () => GameplayTagContainer.Copy(result, left); expected = 1024; break;
            case "intersection": action = () => GameplayTagContainer.Intersection(result, left, right); expected = 512; break;
            case "workspace": action = () => GameplayTagContainer.Intersection(result, left, right, workspace); expected = 512; break;
            case "union": action = () => GameplayTagContainer.Union(result, left, right); expected = 1536; break;
            case "empty-clear": action = () => result.Clear(); expected = 0; break;
            default: throw new ArgumentException(mode);
        }
        Allocated(); Stopwatch.GetTimestamp(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long bytes = Allocated(), start = Stopwatch.GetTimestamp();
        action();
        long ticks = Stopwatch.GetTimestamp() - start, firstBytes = Allocated() - bytes;
        long warmBytes = Allocated();
        for (int i = 0; i < 32; i++) action();
        warmBytes = Allocated() - warmBytes;
        int actual = countResult == null ? result.ExplicitTagCount : countResult.ExplicitTagCount;
        if (actual != expected) throw new Exception("Unexpected cold operation result");
        Console.WriteLine("{\"Mode\":\"" + mode + "\",\"FirstNs\":" + N(ticks * (1e9 / Stopwatch.Frequency)) +
            ",\"FirstBytes\":" + firstBytes + ",\"Following32BytesPerCall\":" + N(warmBytes / 32.0) + ",\"ExplicitCount\":" + actual + "}");
        GC.KeepAlive(result); GC.KeepAlive(countResult);
    }
}
