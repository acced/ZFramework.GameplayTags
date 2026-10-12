using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using GameplayTags;

internal static class Program
{
    const int LargeCount = 8192;
    const int GapCount = LargeCount / 4;
    static readonly string[] Ids = {
        "dense-path/d64", "exhausted-path/d64", "silent/d1", "global-only/d1",
        "global-only/d4", "one-hit/d1", "one-miss/d4", "huge-gaps/d4",
        "huge-midpoint/d1", "huge-late-miss/d1", "clear-cycle/d4", "bulk-reset/8xd4"
    };
    static readonly int[] PilotCounts = { 4096, 4096, 65536, 65536, 16384, 65536, 16384, 16384, 65536, 16384, 16384, 4096 };
    static readonly Func<long> Allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>),
        typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static));
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int Checks;
    static string DeepName => "Deep." + string.Join(".", Enumerable.Range(1, 63).Select(i => "N" + i));
    static string Flat(int i) => "Flat" + i.ToString("D5", CultureInfo.InvariantCulture);
    static string[] GapParents = { "Gap", "Gap.Z", "Gap.Z.Z", "Gap.Z.Z.Z" };
    static GameplayTag T(string name) => GameplayTagManager.RequestTag(name);
    static string Q(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static void Check(bool value, string message) { Checks++; if (!value) throw new InvalidOperationException(message); }

    sealed class Row
    {
        internal string Id;
        internal GameplayTagCountContainer Target = new GameplayTagCountContainer();
        internal GameplayTag[] Leaves;
        internal GameplayTag[] Callbacks;
        internal bool Global;
        internal Action Pair;
        internal long Checksum;
        internal long ExpectedChecksum;
        internal int ExpectedEvents;
        internal long Events;
        internal int PilotCount;
        internal string Geometry = "[]";
        internal void Observe(GameplayTag tag, int count)
        {
            Checksum += tag.RuntimeIndex * 31L + count;
            Events++;
        }
    }

    static void RegisterFixture()
    {
        var names = new List<GameplayTagRegistration> {
            new GameplayTagRegistration(DeepName), new GameplayTagRegistration("Short.A.B.C"),
            new GameplayTagRegistration("Solo"), new GameplayTagRegistration("ZLast")
        };
        for (int i = 0; i < LargeCount; i++) names.Add(new GameplayTagRegistration(Flat(i)));
        for (int group = 0; group < 4; group++)
            for (int i = 0; i < GapCount; i++)
                names.Add(new GameplayTagRegistration(GapParents[group] + ".A" + i.ToString("D5", CultureInfo.InvariantCulture)));
        for (int i = 0; i < 8; i++) names.Add(new GameplayTagRegistration("Bulk" + i + ".A.B.C"));
        GameplayTagManager.Initialize(names.ToArray());
    }

    static Row Make(string id, GameplayTag[] leaves, GameplayTag[] callbacks, bool global = false, bool clear = false, bool bulk = false)
    {
        var row = new Row { Id = id, Leaves = leaves, Callbacks = callbacks, Global = global };
        foreach (GameplayTag callback in callbacks.OrderBy(tag => tag.RuntimeIndex))
            row.Target.RegisterTagEventCallback(callback, GameplayTagEventType.AnyCountChange, row.Observe);
        if (global) row.Target.OnAnyTagCountChange += row.Observe;
        if (bulk)
        {
            var source = new GameplayTagContainer();
            foreach (GameplayTag leaf in leaves) source.AddTag(leaf);
            row.Pair = () => { row.Target.AddTags(source); row.Target.RemoveTags(source); };
        }
        else if (clear) row.Pair = () => { row.Target.AddTag(leaves[0]); row.Target.Clear(); };
        else row.Pair = () => { row.Target.AddTag(leaves[0]); row.Target.RemoveTag(leaves[0]); };
        var selected = new HashSet<int>(callbacks.Select(tag => tag.RuntimeIndex));
        foreach (GameplayTag leaf in leaves)
            foreach (GameplayTag tag in leaf.HierarchyTags)
            {
                int receivers = (selected.Contains(tag.RuntimeIndex) ? 1 : 0) + (global ? 1 : 0);
                row.ExpectedEvents += receivers * 2;
                row.ExpectedChecksum += receivers * (tag.RuntimeIndex * 62L + 1);
            }
        return row;
    }

    static Row[] Fixtures()
    {
        RegisterFixture();
        GameplayTag deep = T(DeepName), shortLeaf = T("Short.A.B.C"), solo = T("Solo"), late = T("ZLast");
        GameplayTag[] dense = deep.HierarchyTags.ToArray();
        GameplayTag[] flat = Enumerable.Range(0, LargeCount).Select(i => T(Flat(i))).ToArray();
        GameplayTag[] gap = GapParents.SelectMany(parent => Enumerable.Range(0, GapCount)
            .Select(i => T(parent + ".A" + i.ToString("D5", CultureInfo.InvariantCulture)))).ToArray();
        GameplayTag gapLeaf = T("Gap.Z.Z.Z");
        GameplayTag[] bulk = Enumerable.Range(0, 8).Select(i => T("Bulk" + i + ".A.B.C"))
            .OrderByDescending(tag => tag.RuntimeIndex).ToArray();
        GameplayTag[] bulkCallbacks = bulk.SelectMany(tag => tag.HierarchyTags.ToArray()).ToArray();
        var rows = new[] {
            Make(Ids[0], new[] { deep }, dense),
            Make(Ids[1], new[] { deep }, new[] { dense[0] }),
            Make(Ids[2], new[] { solo }, Array.Empty<GameplayTag>()),
            Make(Ids[3], new[] { solo }, Array.Empty<GameplayTag>(), global: true),
            Make(Ids[4], new[] { shortLeaf }, Array.Empty<GameplayTag>(), global: true),
            Make(Ids[5], new[] { solo }, new[] { solo }),
            Make(Ids[6], new[] { shortLeaf }, new[] { late }),
            Make(Ids[7], new[] { gapLeaf }, gap),
            Make(Ids[8], new[] { flat[(LargeCount - 1) / 2] }, flat),
            Make(Ids[9], new[] { late }, flat),
            Make(Ids[10], new[] { shortLeaf }, shortLeaf.HierarchyTags.ToArray(), clear: true),
            Make(Ids[11], bulk, bulkCallbacks, bulk: true)
        };
        for (int i = 0; i < rows.Length; i++)
        {
            Row row = rows[i]; row.PilotCount = PilotCounts[i];
            int actualCallbacks = (int)typeof(GameplayTagCountContainer).GetField("m_CallbackCount", Private).GetValue(row.Target);
            Check(actualCallbacks == row.Callbacks.Length, "actual callback table size");
            Check(row.Leaves.All(tag => tag != GameplayTag.None), "registered leaves");
            Check(row.Callbacks.All(tag => tag != GameplayTag.None), "registered callback keys");
            row.Pair();
            Check(row.Target.IsEmpty && row.Target.TagCount == 0, "fixture pair drains state");
            Check(row.Events == row.ExpectedEvents && row.Checksum == row.ExpectedChecksum, "fixture exact events/checksum");
            row.Events = 0; row.Checksum = 0;
        }
        Check(dense.Length == 64 && dense.Zip(dense.Skip(1), (a,b) => a.RuntimeIndex < b.RuntimeIndex).All(x => x), "dense strict hierarchy");
        Check(shortLeaf.HierarchyLevel == 4 && gapLeaf.HierarchyLevel == 4, "shallow depths");
        Check(flat.All(tag => tag.HierarchyLevel == 1), "large shallow roots");
        Check(flat.Zip(flat.Skip(1), (a,b) => a.RuntimeIndex < b.RuntimeIndex).All(x => x), "large table sorted IDs");
        Check(rows[8].Leaves[0] == flat[4095], "old first binary midpoint");
        Check(late.RuntimeIndex > flat[LargeCount - 1].RuntimeIndex && late.HierarchyLevel == 1, "late miss beyond table");
        Check(late.RuntimeIndex > shortLeaf.RuntimeIndex, "tiny missing callback remains ahead");
        GameplayTag[] path = gapLeaf.HierarchyTags.ToArray();
        var gaps = new int[4];
        for (int i = 0; i < 4; i++)
        {
            int lower = path[i].RuntimeIndex, upper = i == 3 ? int.MaxValue : path[i + 1].RuntimeIndex;
            gaps[i] = gap.Count(tag => tag.RuntimeIndex > lower && tag.RuntimeIndex < upper);
            Check(gaps[i] == GapCount, "actual unrelated callback gap " + i);
            Check(!gap.Any(tag => tag.RuntimeIndex == lower), "no accidental ancestor callback hit");
        }
        rows[7].Geometry = "[" + string.Join(",", gaps) + "]";
        Check(bulk.Length == 8 && bulk.All(tag => tag.HierarchyLevel == 4), "bulk leaf depths");
        Check(bulk.Zip(bulk.Skip(1), (a,b) => a.RuntimeIndex > b.RuntimeIndex).All(x => x), "descending bulk leaf order");
        Check(bulkCallbacks.Select(tag => tag.RuntimeIndex).Distinct().Count() == 32, "disjoint bulk closures");
        return rows;
    }

    static void Preflight(Row[] rows)
    {
        foreach (Row row in rows)
            Console.WriteLine("{\"Mode\":\"preflight\",\"Id\":" + Q(row.Id) + ",\"Checks\":" + Checks +
                ",\"Depth\":" + row.Leaves[0].HierarchyLevel + ",\"Callbacks\":" + row.Callbacks.Length +
                ",\"Leaves\":" + row.Leaves.Length + ",\"EventsPerPair\":" + row.ExpectedEvents +
                ",\"ChecksumPerPair\":" + row.ExpectedChecksum + ",\"Gaps\":" + row.Geometry + "}");
    }

    static void Measure(Row row, int iterations, string mode)
    {
        for (int i = 0; i < 1024; i++) row.Pair();
        row.Events = 0; row.Checksum = 0;
        Stopwatch.GetTimestamp(); Allocated();
        GC.CollectionCount(0); GC.CollectionCount(1); GC.CollectionCount(2);
        long allocated = Allocated();
        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++) row.Pair();
        long ticks = Stopwatch.GetTimestamp() - start;
        long bytes = Allocated() - allocated;
        int dg0 = GC.CollectionCount(0) - gc0, dg1 = GC.CollectionCount(1) - gc1, dg2 = GC.CollectionCount(2) - gc2;
        Check(row.Target.IsEmpty && row.Target.TagCount == 0, "measured pair drains state");
        Check(row.Checksum == row.ExpectedChecksum * iterations, "measured checksum");
        Check(row.Events == (long)row.ExpectedEvents * iterations, "measured events");
        double ms = ticks * 1000.0 / Stopwatch.Frequency;
        Console.WriteLine("{\"Mode\":" + Q(mode) + ",\"Id\":" + Q(row.Id) + ",\"Iterations\":" + iterations +
            ",\"BatchMs\":" + ms.ToString("R", CultureInfo.InvariantCulture) +
            ",\"Ns\":" + (ticks * 1000000000.0 / Stopwatch.Frequency / iterations).ToString("R", CultureInfo.InvariantCulture) +
            ",\"Bytes\":" + bytes + ",\"Checksum\":" + row.Checksum + ",\"Events\":" + row.Events +
            ",\"Gc0\":" + dg0 + ",\"Gc1\":" + dg1 + ",\"Gc2\":" + dg2 + "}");
    }

    static int Main(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException("preflight, pilot, or timing is required");
        Row[] rows = Fixtures();
        if (args[0] == "preflight") { Preflight(rows); return 0; }
        if (args[0] == "pilot")
        {
            if (args.Length != 1) throw new ArgumentException("pilot takes no arguments");
            foreach (Row row in rows) Measure(row, row.PilotCount, "pilot");
            return 0;
        }
        if (args[0] != "timing" || args.Length != 3 || args[1] != "--iterations-file")
            throw new ArgumentException("timing --iterations-file <file> is required");
        string[] lines = File.ReadAllLines(args[2]);
        Check(lines.Length == rows.Length, "exact iteration row count");
        for (int i = 0; i < rows.Length; i++)
        {
            string[] fields = lines[i].Split('\t');
            Check(fields.Length == 2 && fields[0] == rows[i].Id, "fixed iteration row order");
            int count = int.Parse(fields[1], CultureInfo.InvariantCulture);
            Check(count >= rows[i].PilotCount, "iteration count not below seed");
            Measure(rows[i], count, "timing");
        }
        return 0;
    }
}
