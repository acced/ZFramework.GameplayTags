using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using GameplayTags;

internal static class ColdDrainedQueueScreen
{
    private static readonly Func<long> Allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static));
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static object Field(object value, string name) => value.GetType().GetField(name, Fields).GetValue(value);
    private static int Number(object value, string name) => (int)Field(value, name);
    private static string Bool(bool value) => value ? "true" : "false";
    private static string Hash(byte[] value) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(value)).Replace("-", "").ToLowerInvariant(); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private struct Event { internal int Id, Count; }
    private sealed class Row
    {
        internal string Shape;
        internal bool Candidate, PendingControl;
        internal GameplayTagCountContainer Container;
        internal GameplayTag Target, Extra;
        internal GameplayTag[] DeepPath;
        internal OnTagCountChangedDelegate Callback;
        internal Event[] Trace;
        internal byte[] SourceBefore;
        internal int Limit, Events, PublicAdds, Depth, MaxDepth;
        internal long Checksum;
        internal void Observe(GameplayTag tag, int count)
        {
            Depth++; if (Depth > MaxDepth) MaxDepth = Depth;
            int slot = Events++; Trace[slot] = new Event { Id = tag.RuntimeIndex, Count = count };
            Checksum += (long)Events * (tag.RuntimeIndex * 65537L + count);
            if (PendingControl)
            {
                // Eight original root-to-self notifications; the seventh callback
                // appends while the eighth remains pending in the full array.
                if (tag == DeepPath[6]) { PublicAdds++; Container.AddTag(Extra); }
            }
            else if (Events < Limit) { PublicAdds++; Container.AddTag(Target); }
            Depth--;
        }
    }
    private static Row Prepare(string shape, bool candidate)
    {
        Check(new[] { "chain-1", "chain-8", "chain-9", "chain-1025", "full-with-pending" }.Contains(shape), "Fixed shape required");
        GameplayTagManager.Initialize(new GameplayTagRegistration("A.B.C.D.E.F.G.H"), new GameplayTagRegistration("Z"));
        var leaf = GameplayTagManager.RequestTag("A.B.C.D.E.F.G.H");
        var row = new Row { Shape = shape, Candidate = candidate, PendingControl = shape == "full-with-pending", Container = new GameplayTagCountContainer(),
            DeepPath = leaf.HierarchyTags.ToArray(), Extra = GameplayTagManager.RequestTag("Z") };
        row.Limit = row.PendingControl ? 9 : int.Parse(shape.Substring(6), CultureInfo.InvariantCulture);
        row.Target = row.PendingControl ? leaf : GameplayTagManager.RequestTag("A");
        row.Trace = new Event[row.Limit]; row.Callback = row.Observe; row.Container.OnAnyTagCountChange += row.Callback;
        row.SourceBefore = SourceSnapshot(row.DeepPath, row.Extra);
        Check(row.DeepPath.Length == 8 && row.DeepPath[6].Name == "A.B.C.D.E.F.G" && row.Events == 0 && row.PublicAdds == 0, "Setup never invokes target mutation or observer");
        return row;
    }
    // Same first AddTag boundary for all five shapes; never called during setup.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FirstAdd(Row row) { row.PublicAdds++; row.Container.AddTag(row.Target); }
    // Selected immutable definitions and both full input paths, reused from CountedAddCold's encoder.
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
    private static void WriteDelegate(BinaryWriter writer, Delegate value, Row row)
    {
        Delegate[] list = value == null ? Array.Empty<Delegate>() : value.GetInvocationList(); writer.Write(list.Length);
        foreach (Delegate item in list)
        {
            writer.Write(item.Equals(row.Callback)); writer.Write(item.Method.DeclaringType.FullName); writer.Write(item.Method.Name); writer.Write(ReferenceEquals(item.Target, row));
            Check(item.Equals(row.Callback) && ReferenceEquals(item.Target, row), "Exact global callback ownership");
        }
    }
    private static int Growth(int n) { int cap = 8; while (cap < n) cap *= 2; return cap; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ValidateAndSnapshot(Row row, string path, out string snapshotHash, out int eventCapacity, out int storageCount, out int explicitCount)
    {
        snapshotHash = ""; eventCapacity = -1; storageCount = -1; explicitCount = -1;
        Check(row.Events == row.Limit && row.MaxDepth == 1 && row.Depth == 0 && row.PublicAdds == (row.PendingControl ? 2 : row.Limit), "Complete deferred callback depth/count");
        long expectedChecksum = 0;
        for (int i = 0; i < row.Limit; i++)
        {
            GameplayTag expectedTag = row.PendingControl ? (i < 8 ? row.DeepPath[i] : row.Extra) : row.Target;
            int expectedCount = row.PendingControl ? 1 : i + 1;
            Check(row.Trace[i].Id == expectedTag.RuntimeIndex && row.Trace[i].Count == expectedCount, "Exact independent root-to-self/FIFO or finite-chain trace");
            expectedChecksum += (long)(i + 1) * (expectedTag.RuntimeIndex * 65537L + expectedCount);
        }
        Check(row.Checksum == expectedChecksum, "Independent ordered checksum");
        byte[] sourceAfter = SourceSnapshot(row.DeepPath, row.Extra); Check(row.SourceBefore.SequenceEqual(sourceAfter), "Selected source definitions/paths unchanged");
        var c = row.Container; var storage = c.Indices.Storage; storageCount = c.TagCount; explicitCount = c.ExplicitTagCount;
        Check(storageCount == (row.PendingControl ? 9 : 1) && explicitCount == (row.PendingControl ? 2 : 1), "Expected public storage counts");
        foreach (GameplayTag tag in GameplayTagManager.GetAllTags())
        {
            int expected = row.PendingControl ? 1 : (tag == row.Target ? row.Limit : 0);
            int expectedExplicit = row.PendingControl ? (tag == row.Target || tag == row.Extra ? 1 : 0) : (tag == row.Target ? row.Limit : 0);
            Check(c.GetTagCount(tag) == expected && c.GetExplicitTagCount(tag) == expectedExplicit, "Exact public per-tag counts");
        }
        var callbacks = (Array)Field(c, "m_Callbacks"); var events = (DeferredTagChangedDelegate[])Field(c, "m_Events"); eventCapacity = events.Length;
        Check(eventCapacity == (row.PendingControl ? 16 : row.Candidate ? 8 : Growth(row.Limit)), "Declared physical event capacity");
        Check(callbacks.Length == 0 && Number(c, "m_CallbackCount") == 0, "No per-tag callback table created");
        Check(Field(c, "OnAnyTagNewOrRemove") == null && ReferenceEquals(Field(c, "OnAnyTagCountChange"), row.Callback), "Global callback remains exact");
        foreach (string name in new[] { "m_EventCount", "m_NextEvent", "m_BatchDepth" }) Check(Number(c, name) == 0, "Queue counters reset");
        Check(!(bool)Field(c, "m_Dispatching"), "Dispatcher ownership reset");
        foreach (DeferredTagChangedDelegate e in events) Check(e.TagId == 0 && e.Count == 0 && e.Callback == null, "All retained event slots default");
        using (var writer = new BinaryWriter(new FileStream(path, FileMode.CreateNew)))
        {
            writer.Write(row.Shape); writer.Write(row.Target.RuntimeIndex); writer.Write(row.Limit); writer.Write(row.PublicAdds); writer.Write(row.Events); writer.Write(row.MaxDepth); writer.Write(row.Checksum);
            foreach (Event e in row.Trace) { writer.Write(e.Id); writer.Write(e.Count); }
            foreach (byte[] source in new[] { row.SourceBefore, sourceAfter }) { writer.Write(source.Length); writer.Write(source); }
            writer.Write(Number(c, "m_CallbackCount")); writer.Write(callbacks.Length);
            foreach (object entry in callbacks)
            {
                writer.Write(Number(entry, "Id")); object info = Field(entry, "Callbacks"); WriteDelegate(writer, (Delegate)Field(info, "OnNewOrRemove"), row); WriteDelegate(writer, (Delegate)Field(info, "OnAnyChange"), row);
            }
            WriteDelegate(writer, (Delegate)Field(c, "OnAnyTagNewOrRemove"), row); WriteDelegate(writer, (Delegate)Field(c, "OnAnyTagCountChange"), row);
            // Logical empty suffix and ownership compare; physical capacity is only in raw JSON.
            foreach (string name in new[] { "m_EventCount", "m_NextEvent", "m_BatchDepth" }) writer.Write(Number(c, name)); writer.Write((bool)Field(c, "m_Dispatching"));
            var snapshot = (int[])Field(c, "m_Snapshot"); writer.Write(snapshot.Length); foreach (int n in snapshot) writer.Write(n);
            writer.Write(storage.Count); writer.Write(storage.ExplicitCount); writer.Write(Number(storage, "m_BucketMask")); writer.Write(storage.Entries.Length);
            foreach (var e in storage.Entries) { writer.Write(e.Id); writer.Write(e.Count); writer.Write(e.ExplicitCount); writer.Write(e.ExplicitIndex); }
            foreach (var array in new[] { storage.ExplicitIndices, (int[])Field(storage, "m_Buckets") }) { writer.Write(array.Length); foreach (int n in array) writer.Write(n); }
            var all = c.GetTags().ToArray(); writer.Write(all.Length); foreach (GameplayTag tag in all) { writer.Write(tag.Name); writer.Write(c.GetTagCount(tag)); writer.Write(c.GetExplicitTagCount(tag)); }
            var explicitTags = c.GetExplicitTags().ToArray(); writer.Write(explicitTags.Length); foreach (GameplayTag tag in explicitTags) writer.Write(tag.Name);
        }
        snapshotHash = Hash(File.ReadAllBytes(path));
    }
    private static int Main(string[] args)
    {
        if (args.Length != 3 || (args[2] != "original" && args[2] != "candidate")) throw new ArgumentException("Fixed shape, fresh snapshot path and exact variant required");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        Row row = Prepare(args[0], args[2] == "candidate");
        long frequency = Stopwatch.Frequency; Allocated(); Stopwatch.GetTimestamp(); GC.CollectionCount(0); GC.CollectionCount(1); GC.CollectionCount(2);
        Exception operationError = null;
        long before = Allocated(); int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        long start = Stopwatch.GetTimestamp();
        try { FirstAdd(row); } catch (Exception error) { operationError = error; }
        long ticks = Stopwatch.GetTimestamp() - start, bytes = Allocated() - before;
        int delta0 = GC.CollectionCount(0) - gc0, delta1 = GC.CollectionCount(1) - gc1, delta2 = GC.CollectionCount(2) - gc2;
        // Save every numeric observation before post-call reflection/state encoding, even on failure.
        Console.WriteLine("{\"RecordType\":\"measurement\",\"Shape\":\"" + row.Shape + "\",\"TargetTagId\":" + row.Target.RuntimeIndex + ",\"ExpectedEvents\":" + row.Limit +
            ",\"SetupTargetMutations\":0,\"Ticks\":" + ticks + ",\"Frequency\":" + frequency + ",\"AllocatedBytes\":" + bytes +
            ",\"GC0\":" + delta0 + ",\"GC1\":" + delta1 + ",\"GC2\":" + delta2 + ",\"Events\":" + row.Events + ",\"PublicAdds\":" + row.PublicAdds + ",\"MaxDepth\":" + row.MaxDepth + ",\"Checksum\":" + row.Checksum +
            ",\"PointerSize\":" + IntPtr.Size + ",\"OperationPass\":" + Bool(operationError == null) + ",\"OperationErrorType\":\"" + (operationError == null ? "" : operationError.GetType().Name) +
            "\",\"FirstBridgeJitIncluded\":true,\"PreBoundaryTargetReflection\":false,\"WholeHeapRetainedBytesMeasured\":false}");
        Console.Out.Flush();
        bool pass = operationError == null; string errorType = "", hash = ""; int capacity = -1, count = -1, explicitCount = -1;
        try { ValidateAndSnapshot(row, args[1], out hash, out capacity, out count, out explicitCount); } catch (Exception error) { pass = false; errorType = error.GetType().Name; }
        Console.WriteLine("{\"RecordType\":\"semantics\",\"Shape\":\"" + row.Shape + "\",\"SemanticsPass\":" + Bool(pass) + ",\"ErrorType\":\"" + errorType +
            "\",\"StorageCount\":" + count + ",\"ExplicitCount\":" + explicitCount + ",\"EventCapacity\":" + capacity + ",\"QueueCount\":" + Number(row.Container, "m_EventCount") + ",\"QueueNext\":" + Number(row.Container, "m_NextEvent") +
            ",\"SnapshotSHA256\":\"" + hash + "\"}");
        Console.Out.Flush(); GC.KeepAlive(row); return pass ? 0 : 1;
    }
}
