using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using GameplayTags;

internal static class WarmDrainedQueueScreen
{
    private static readonly Func<long> Allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static));
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly string[] Shapes = { "single", "chain9", "chain1025", "pending" };
    private static readonly int[] Seeds = { 32768, 4096, 128, 4096 };
    private static readonly int[] Caps = { 4194304, 1048576, 65536, 1048576 };
    private const int Warmups = 128;
    private static bool Candidate;
    private static GameplayTag[] DeepPath;
    private static GameplayTag Extra;
    private static object Field(object value, string name) => value.GetType().GetField(name, Fields).GetValue(value);
    private static int Number(object value, string name) => (int)Field(value, name);
    private static string Bool(bool value) => value ? "true" : "false";
    private static string Hash(byte[] value) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(value)).Replace("-", "").ToLowerInvariant(); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private struct Event { internal int Id, Count; }
    private sealed class Row
    {
        internal string Id;
        internal int N, EventsPerCycle, CycleEvents, Depth, MaxDepth;
        internal bool Control, Restoring, EveryEventOrderPass = true;
        internal GameplayTagCountContainer Container;
        internal GameplayTag Target;
        internal OnTagCountChangedDelegate Callback;
        internal Event[] Trace, ExpectedTrace;
        internal long CompletedCycles, EventsTotal, ChecksumTotal, CycleChecksum, ExpectedCycleChecksum;
        internal byte[] SourceBefore, StateBefore;
        internal object[] RetainedArrays;
        internal void Observe(GameplayTag tag, int count)
        {
            Depth++; if (Depth > MaxDepth) MaxDepth = Depth;
            int slot = CycleEvents++; Trace[slot] = new Event { Id = tag.RuntimeIndex, Count = count };
            if (tag.RuntimeIndex != ExpectedTrace[slot].Id || count != ExpectedTrace[slot].Count) EveryEventOrderPass = false;
            long weight = (long)CycleEvents * (tag.RuntimeIndex * 65537L + count);
            CycleChecksum += weight; ChecksumTotal += weight; EventsTotal++;
            if (!Restoring)
            {
                if (Control) { if (tag == DeepPath[6]) Container.AddTag(Extra); }
                else if (CycleEvents < N) Container.AddTag(Target);
            }
            Depth--;
        }
    }
    private static void Initialize()
    {
        GameplayTagManager.Initialize(new GameplayTagRegistration("A.B.C.D.E.F.G.H"), new GameplayTagRegistration("Z"));
        DeepPath = GameplayTagManager.RequestTag("A.B.C.D.E.F.G.H").HierarchyTags.ToArray(); Extra = GameplayTagManager.RequestTag("Z");
        Check(DeepPath.Length == 8 && DeepPath[0].RuntimeIndex == 1 && Extra.RuntimeIndex == 9, "Fixed immutable registry IDs");
    }
    private static Event Expected(Row row, int i)
    {
        if (!row.Control) return new Event { Id = row.Target.RuntimeIndex, Count = i < row.N ? i + 1 : 0 };
        if (i < 9) return new Event { Id = i < 8 ? DeepPath[i].RuntimeIndex : Extra.RuntimeIndex, Count = 1 };
        return new Event { Id = i < 17 ? DeepPath[i - 9].RuntimeIndex : Extra.RuntimeIndex, Count = 0 };
    }
    private static Row Prepare(int index)
    {
        var row = new Row { Id = Shapes[index], N = index == 0 ? 1 : index == 1 ? 9 : index == 2 ? 1025 : 8, Control = index == 3, Container = new GameplayTagCountContainer() };
        row.Target = row.Control ? DeepPath[7] : DeepPath[0]; row.EventsPerCycle = row.Control ? 18 : row.N + 1;
        row.Trace = new Event[row.EventsPerCycle]; row.ExpectedTrace = new Event[row.EventsPerCycle]; row.Callback = row.Observe; row.Container.OnAnyTagCountChange += row.Callback;
        row.SourceBefore = SourceSnapshot(DeepPath, Extra);
        for (int i = 0; i < row.EventsPerCycle; i++) { Event e = Expected(row, i); row.ExpectedTrace[i] = e; row.ExpectedCycleChecksum += (long)(i + 1) * (e.Id * 65537L + e.Count); }
        Check((long)(Caps[index] + Warmups) * row.EventsPerCycle < int.MaxValue, "Fixed callback-count bound");
        Check(row.ExpectedCycleChecksum <= long.MaxValue / (Caps[index] + Warmups), "Fixed checksum bound");
        return row;
    }
    // Complete public mutation-and-restoration cycle, including every restoration callback.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long Cycle(Row row)
    {
        row.CycleEvents = 0; row.CycleChecksum = 0; row.Restoring = false;
        row.Container.AddTag(row.Target);
        row.Restoring = true;
        row.Container.RemoveTag(row.Target, row.Control ? 1 : row.N);
        if (row.Control) row.Container.RemoveTag(Extra);
        row.CompletedCycles++;
        return row.CycleChecksum;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long RunBatch(Row row, int iterations)
    {
        long checksum = 0; for (int i = 0; i < iterations; i++) checksum += Cycle(row); return checksum;
    }
    // Same selected immutable-definition/path encoder as the fixed first-use package.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] SourceSnapshot(GameplayTag[] path, GameplayTag extra)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(GameplayTagManager.TagCount);
            foreach (GameplayTag[] selected in new[] { path, new[] { extra } })
            {
                writer.Write(selected.Length);
                foreach (GameplayTag tag in selected)
                {
                    ref readonly GameplayTagDefinition d = ref tag.Definition;
                    writer.Write(d.TagName); writer.Write(d.Description); writer.Write(d.Label); writer.Write((int)d.Flags);
                    writer.Write(d.RuntimeIndex); writer.Write(d.ParentIndex); writer.Write(d.SubtreeEnd);
                    writer.Write(d.HierarchyLevel); writer.Write(d.HierarchyOffset);
                }
                ReadOnlySpan<GameplayTag> current = selected[selected.Length - 1].HierarchyTags;
                writer.Write(current.Length); foreach (GameplayTag tag in current) { writer.Write(tag.RuntimeIndex); writer.Write(tag.Name); }
            }
            writer.Flush(); return stream.ToArray();
        }
    }
    private static object[] Arrays(Row row)
    {
        var c = row.Container; var s = c.Indices.Storage;
        return new object[] { s.Entries, s.ExplicitIndices, Field(s, "m_Buckets"), Field(c, "m_Events"), Field(c, "m_Callbacks"), Field(c, "m_Snapshot") };
    }
    private static int EventCapacity(Row row) => ((Array)Field(row.Container, "m_Events")).Length;
    private static int ExpectedCapacity(Row row) => row.Control ? 16 : Candidate ? 8 : row.N == 1025 ? 2048 : row.N == 9 ? 16 : 8;
    // Logical snapshot: full storage/callback representation and final-cycle trace;
    // event capacity stays in raw JSON, while every retained event slot is checked default.
    private static byte[] State(Row row)
    {
        var c = row.Container; var s = c.Indices.Storage;
        Check(c.IsEmpty && c.TagCount == 0 && c.ExplicitTagCount == 0, "Full public restoration leaves holder empty");
        foreach (GameplayTag tag in GameplayTagManager.GetAllTags()) Check(c.GetTagCount(tag) == 0 && c.GetExplicitTagCount(tag) == 0, "All restored public counts zero");
        Check(c.GetTags().ToArray().Length == 0 && c.GetExplicitTags().ToArray().Length == 0, "Restored enumerations empty");
        Check(EventCapacity(row) == ExpectedCapacity(row), "Naturally retained original/candidate capacity");
        foreach (DeferredTagChangedDelegate e in (DeferredTagChangedDelegate[])Field(c, "m_Events")) Check(e.TagId == 0 && e.Count == 0 && e.Callback == null, "Retained queue slots default");
        Check(Number(c, "m_EventCount") == 0 && Number(c, "m_NextEvent") == 0 && Number(c, "m_BatchDepth") == 0 && !(bool)Field(c, "m_Dispatching"), "Drained queue ownership");
        Check(((Array)Field(c, "m_Callbacks")).Length == 0 && Number(c, "m_CallbackCount") == 0 && Field(c, "OnAnyTagNewOrRemove") == null && ReferenceEquals(Field(c, "OnAnyTagCountChange"), row.Callback), "Exact sole global callback ownership");
        Check(row.EveryEventOrderPass && row.CycleEvents == row.EventsPerCycle && row.MaxDepth == 1 && row.Depth == 0 && row.Restoring && row.CycleChecksum == row.ExpectedCycleChecksum, "Complete last cycle/depth/checksum");
        byte[] source = SourceSnapshot(DeepPath, Extra); Check(row.SourceBefore.SequenceEqual(source), "Immutable source unchanged");
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write(row.Id); w.Write(row.N); w.Write(row.Control); w.Write(row.EventsPerCycle);
        for (int i = 0; i < row.Trace.Length; i++) { Event e = Expected(row, i); Check(row.Trace[i].Id == e.Id && row.Trace[i].Count == e.Count, "Independent complete last-cycle order"); w.Write(row.Trace[i].Id); w.Write(row.Trace[i].Count); }
        w.Write(source.Length); w.Write(source);
        w.Write(Number(c, "m_CallbackCount")); w.Write(((Array)Field(c, "m_Callbacks")).Length); w.Write(true); // exact global owner checked above
        foreach (string name in new[] { "m_EventCount", "m_NextEvent", "m_BatchDepth" }) w.Write(Number(c, name)); w.Write((bool)Field(c, "m_Dispatching"));
        var snap = (int[])Field(c, "m_Snapshot"); w.Write(snap.Length); foreach (int n in snap) w.Write(n);
        w.Write(s.Count); w.Write(s.ExplicitCount); w.Write(Number(s, "m_BucketMask")); w.Write(s.Entries.Length);
        foreach (var e in s.Entries) { w.Write(e.Id); w.Write(e.Count); w.Write(e.ExplicitCount); w.Write(e.ExplicitIndex); }
        foreach (var a in new[] { s.ExplicitIndices, (int[])Field(s, "m_Buckets") }) { w.Write(a.Length); foreach (int n in a) w.Write(n); }
        w.Flush(); return stream.ToArray();
    }
    private static void Validate(Row row, long cycles, string path, out string hash)
    {
        hash = ""; Check(row.CompletedCycles == cycles && row.EventsTotal == cycles * row.EventsPerCycle && row.ChecksumTotal == cycles * row.ExpectedCycleChecksum, "Cumulative cycle/event/checksum bounds");
        byte[] state = State(row);
        if (row.StateBefore == null) { row.StateBefore = state; row.RetainedArrays = Arrays(row); }
        else
        {
            Check(row.StateBefore.SequenceEqual(state), "Full pre/post restored logical state equality");
            object[] now = Arrays(row); for (int i = 0; i < now.Length; i++) Check(ReferenceEquals(now[i], row.RetainedArrays[i]), "Every retained array identity stable");
        }
        if (path == null) return;
        using (var w = new BinaryWriter(new FileStream(path, FileMode.CreateNew)))
        {
            w.Write(row.Id); w.Write(cycles); w.Write(row.EventsTotal); w.Write(row.ChecksumTotal); w.Write(true);
            w.Write(row.StateBefore.Length); w.Write(row.StateBefore); w.Write(state.Length); w.Write(state);
        }
        hash = Hash(File.ReadAllBytes(path));
    }
    private static bool Measure(string mode, int index, int iterations, string path)
    {
        Check(iterations >= Seeds[index] && iterations <= Caps[index], "Frozen count within fixed bounds");
        Row row = Prepare(index); long warm = RunBatch(row, Warmups); Check(warm == (long)Warmups * row.ExpectedCycleChecksum, "Warmup checksum");
        Validate(row, Warmups, null, out _);
        long frequency = Stopwatch.Frequency; Allocated(); Stopwatch.GetTimestamp(); GC.CollectionCount(0); GC.CollectionCount(1); GC.CollectionCount(2);
        long before = Allocated(); int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        Exception error = null; long checksum = -1; long start = Stopwatch.GetTimestamp();
        try { checksum = RunBatch(row, iterations); } catch (Exception caught) { error = caught; }
        long ticks = Stopwatch.GetTimestamp() - start, bytes = Allocated() - before;
        int delta0 = GC.CollectionCount(0) - g0, delta1 = GC.CollectionCount(1) - g1, delta2 = GC.CollectionCount(2) - g2;
        Console.WriteLine("{\"RecordType\":\"measurement\",\"Mode\":\"" + mode + "\",\"Id\":\"" + row.Id + "\",\"Iterations\":" + iterations + ",\"Ticks\":" + ticks + ",\"Frequency\":" + frequency + ",\"Bytes\":" + bytes +
            ",\"Gc0\":" + delta0 + ",\"Gc1\":" + delta1 + ",\"Gc2\":" + delta2 + ",\"Checksum\":" + checksum + ",\"N\":" + row.N + ",\"Control\":" + Bool(row.Control) + ",\"Warmups\":" + Warmups +
            ",\"BeforeCycles\":" + Warmups + ",\"EventsPerCycle\":" + row.EventsPerCycle + ",\"CycleChecksum\":" + row.ExpectedCycleChecksum + ",\"EventsTotalBefore\":" + ((long)Warmups * row.EventsPerCycle) +
            ",\"PointerSize\":" + IntPtr.Size + ",\"OperationPass\":" + Bool(error == null) + ",\"OperationErrorType\":\"" + (error == null ? "" : error.GetType().FullName) + "\"}");
        Console.Out.Flush(); bool pass = error == null && checksum == iterations * row.ExpectedCycleChecksum; string failure = "", hash = "";
        try { Validate(row, (long)Warmups + iterations, path, out hash); } catch (Exception caught) { pass = false; failure = caught.GetType().FullName; }
        Console.WriteLine("{\"RecordType\":\"semantics\",\"Mode\":\"" + mode + "\",\"Id\":\"" + row.Id + "\",\"SemanticsPass\":" + Bool(pass) + ",\"ErrorType\":\"" + failure +
            "\",\"Empty\":" + Bool(row.Container.IsEmpty) + ",\"IdentityPass\":" + Bool(pass) + ",\"EventCapacity\":" + EventCapacity(row) + ",\"QueueCount\":" + Number(row.Container, "m_EventCount") + ",\"QueueNext\":" + Number(row.Container, "m_NextEvent") +
            ",\"CompletedCycles\":" + row.CompletedCycles + ",\"EventsTotal\":" + row.EventsTotal + ",\"ChecksumTotal\":" + row.ChecksumTotal + ",\"AssemblyName\":\"Test\",\"SnapshotSHA256\":\"" + hash + "\"}");
        Console.Out.Flush(); GC.KeepAlive(row); return pass;
    }
    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        Check(args.Length >= 3 && (args[args.Length - 1] == "original" || args[args.Length - 1] == "candidate"), "Mode/arguments/exact variant required");
        Candidate = args[args.Length - 1] == "candidate"; Initialize();
        if (args.Length == 3 && args[0] == "pilot") { bool pass = true; for (int i = 0; i < Shapes.Length; i++) pass &= Measure("pilot", i, Seeds[i], Path.Combine(args[1], "pilot-" + Shapes[i] + ".snapshot.bin")); return pass ? 0 : 1; }
        Check(args.Length == 7 && args[0] == "timing", "Four frozen counts, snapshot prefix and exact variant required");
        bool all = true; for (int i = 0; i < Shapes.Length; i++) all &= Measure("timing", i, int.Parse(args[i + 1], CultureInfo.InvariantCulture), args[5] + "-" + Shapes[i] + ".snapshot.bin"); return all ? 0 : 1;
    }
}
