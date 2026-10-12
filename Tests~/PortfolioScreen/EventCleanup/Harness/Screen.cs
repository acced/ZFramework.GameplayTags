using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
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

    static readonly string[] ColdIds = { "silent/d1", "one-miss/d4", "one-hit/d1", "dense-path/d64" };
    static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object Field(object value, string name) => value.GetType().GetField(name, Fields).GetValue(value);
    static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    sealed class State
    {
        internal int TagCount, ExplicitCount, EntryCapacity, ExplicitCapacity, BucketCapacity;
        internal int CallbackCount, CallbackCapacity, EventCapacity, EventCount, NextEvent, Dispatching, PendingReferences;
        internal string Json(string prefix) =>
            ",\"" + prefix + "TagCount\":" + TagCount + ",\"" + prefix + "ExplicitCount\":" + ExplicitCount +
            ",\"" + prefix + "EntryCapacity\":" + EntryCapacity + ",\"" + prefix + "ExplicitCapacity\":" + ExplicitCapacity +
            ",\"" + prefix + "BucketCapacity\":" + BucketCapacity + ",\"" + prefix + "CallbackCount\":" + CallbackCount +
            ",\"" + prefix + "CallbackCapacity\":" + CallbackCapacity + ",\"" + prefix + "EventCapacity\":" + EventCapacity +
            ",\"" + prefix + "EventCount\":" + EventCount + ",\"" + prefix + "NextEvent\":" + NextEvent +
            ",\"" + prefix + "Dispatching\":" + Dispatching + ",\"" + prefix + "PendingReferences\":" + PendingReferences;
    }

    static State Capture(Row row)
    {
        var storage = row.Target.Indices.Storage;
        var events = (Array)Field(row.Target, "m_Events");
        var state = new State {
            TagCount = row.Target.TagCount, ExplicitCount = row.Target.ExplicitTagCount,
            EntryCapacity = storage.Entries.Length, ExplicitCapacity = storage.ExplicitIndices.Length,
            BucketCapacity = ((Array)Field(storage, "m_Buckets")).Length,
            CallbackCount = (int)Field(row.Target, "m_CallbackCount"),
            CallbackCapacity = ((Array)Field(row.Target, "m_Callbacks")).Length,
            EventCapacity = events.Length, EventCount = (int)Field(row.Target, "m_EventCount"),
            NextEvent = (int)Field(row.Target, "m_NextEvent"), Dispatching = (bool)Field(row.Target, "m_Dispatching") ? 1 : 0
        };
        foreach (object item in events) if (Field(item, "Callback") != null) state.PendingReferences++;
        return state;
    }

    static Row ColdFixture(string id)
    {
        Check(ColdIds.Contains(id), "declared cold shape");
        RegisterFixture();
        Row row;
        if (id == "silent/d1") row = Make(id, new[] { T("Solo") }, Array.Empty<GameplayTag>());
        else if (id == "one-miss/d4") row = Make(id, new[] { T("Short.A.B.C") }, new[] { T("ZLast") });
        else if (id == "one-hit/d1") row = Make(id, new[] { T("Solo") }, new[] { T("Solo") });
        else { GameplayTag deep = T(DeepName); row = Make(id, new[] { deep }, deep.HierarchyTags.ToArray()); }
        Check(row.Target.IsEmpty && row.Events == 0 && row.Checksum == 0, "cold target untouched");
        return row;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void FirstAdd(Row row) => row.Target.AddTag(row.Leaves[0]);

    static bool QueueDrained(State state) => state.EventCount == 0 && state.NextEvent == 0 &&
        state.Dispatching == 0 && state.PendingReferences == 0;

    static int First(string id, bool measure)
    {
        Row row = ColdFixture(id);
        State before = Capture(row);
        int depth = row.Leaves[0].HierarchyLevel;
        int expectedEvents = row.ExpectedEvents / 2;
        long expectedChecksum = (row.ExpectedChecksum + expectedEvents) / 2;
        bool beforeOk = before.TagCount == 0 && before.ExplicitCount == 0 && before.EntryCapacity == 0 &&
            before.ExplicitCapacity == 0 && before.BucketCapacity == 0 && before.EventCapacity == 0 &&
            before.CallbackCount == row.Callbacks.Length && before.CallbackCapacity == (row.Callbacks.Length == 0 ? 0 : Math.Max(4, row.Callbacks.Length)) && QueueDrained(before);
        Check(beforeOk, "first AddTag target is fresh with only declared subscriptions");
        long ticks = 0, bytes = 0, frequency = 0;
        int gc0 = 0, gc1 = 0, gc2 = 0;
        Exception failure = null;
        if (measure)
        {
            Allocated(); Stopwatch.GetTimestamp(); GC.CollectionCount(0); GC.CollectionCount(1); GC.CollectionCount(2);
            frequency = Stopwatch.Frequency;
            long allocated = Allocated();
            int beforeGc0 = GC.CollectionCount(0), beforeGc1 = GC.CollectionCount(1), beforeGc2 = GC.CollectionCount(2);
            long start = Stopwatch.GetTimestamp();
            try { FirstAdd(row); } catch (Exception exception) { failure = exception; }
            ticks = Stopwatch.GetTimestamp() - start;
            bytes = Allocated() - allocated;
            gc0 = GC.CollectionCount(0) - beforeGc0; gc1 = GC.CollectionCount(1) - beforeGc1; gc2 = GC.CollectionCount(2) - beforeGc2;
        }
        else
        {
            try { FirstAdd(row); } catch (Exception exception) { failure = exception; }
        }
        State after = Capture(row);
        bool countsOk = row.Target.GetExplicitTagCount(row.Leaves[0]) == 1;
        foreach (GameplayTag tag in row.Leaves[0].HierarchyTags)
            countsOk &= row.Target.GetTagCount(tag) == 1 && row.Target.GetExplicitTagCount(tag) == (tag == row.Leaves[0] ? 1 : 0);
        bool semanticOk = failure == null && countsOk && row.Events == expectedEvents && row.Checksum == expectedChecksum &&
            after.TagCount == depth && after.ExplicitCount == 1 && after.EntryCapacity == Math.Max(8, depth) &&
            after.ExplicitCapacity == 8 && after.BucketCapacity == (depth > 8 ? 128 : 0) &&
            after.CallbackCount == before.CallbackCount && after.CallbackCapacity == before.CallbackCapacity &&
            after.EventCapacity == (expectedEvents == 0 ? 0 : Math.Max(8, expectedEvents)) && QueueDrained(after);
        Console.WriteLine("{\"Mode\":" + Q(measure ? "cold" : "cold-preflight") + ",\"Id\":" + Q(id) +
            ",\"Depth\":" + depth + ",\"Callbacks\":" + row.Callbacks.Length + ",\"ExpectedEvents\":" + expectedEvents +
            ",\"ExpectedChecksum\":" + expectedChecksum + ",\"Events\":" + row.Events + ",\"Checksum\":" + row.Checksum +
            ",\"Ticks\":" + ticks + ",\"StopwatchFrequency\":" + frequency + ",\"Ns\":" + N(measure ? ticks * 1000000000.0 / frequency : 0) +
            ",\"Bytes\":" + bytes + ",\"Gc0\":" + gc0 + ",\"Gc1\":" + gc1 + ",\"Gc2\":" + gc2 +
            ",\"OperationThrew\":" + (failure == null ? 0 : 1) + ",\"HierarchyCountsCorrect\":" + (countsOk ? 1 : 0) +
            ",\"SemanticOk\":" + (semanticOk ? 1 : 0) + before.Json("Before") + after.Json("After") + RuntimeFields() + "}");
        if (!semanticOk) throw new InvalidOperationException("cold fixture invariant", failure);
        return 0;
    }

    static string RuntimeFields()
    {
        bool tiering; bool known = AppContext.TryGetSwitch("System.Runtime.TieredCompilation", out tiering);
        bool mono = Type.GetType("Mono.Runtime") != null;
        return ",\"RuntimeVersion\":" + Q(Environment.Version.ToString()) + ",\"ServerGC\":" + (GCSettings.IsServerGC ? 1 : 0) +
            ",\"TieringConfigured\":" + (known ? (tiering ? 1 : 0) : -1) +
            ",\"ReadyToRunRequested\":" + (mono ? -1 : int.Parse(Environment.GetEnvironmentVariable("DOTNET_ReadyToRun"), CultureInfo.InvariantCulture)) +
            ",\"TieringRequested\":" + (mono ? -1 : int.Parse(Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), CultureInfo.InvariantCulture));
    }

    static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "preflight") { Preflight(Fixtures()); return 0; }
        if (args.Length == 2 && (args[0] == "cold" || args[0] == "cold-preflight"))
            return First(args[1], args[0] == "cold");
        throw new ArgumentException("preflight, cold-preflight <id>, or cold <id> required; no warm mode");
    }
}
