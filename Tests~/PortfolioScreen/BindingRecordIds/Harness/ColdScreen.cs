using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using GameplayTags;

internal static class ColdBindingRecordScreen
{
    private static readonly Func<long> Allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static));
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private sealed class Row
    {
        internal string Shape;
        internal int Count;
        internal bool Present, External;
        internal GameplayTag[] Tags;
        internal GameplayTagCountContainer Container;
        internal GameplayTagContainerBinds Binds;
        internal Action<bool> Callback;
        internal OnTagCountChangedDelegate ExternalCallback;
        internal long Events, Checksum, ExternalEvents, ExternalChecksum;
        internal void Observe(bool value) { Events++; Checksum += value ? 3 : 1; }
        internal void ObserveExternal(GameplayTag tag, int count) { ExternalEvents++; ExternalChecksum += tag.RuntimeIndex * 31L + count; }
    }
    private static object Field(object value, string name) => value.GetType().GetField(name, Fields).GetValue(value);
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static Row Prepare(string shape)
    {
        Check(new[] { "absent", "present", "external", "growth-five" }.Contains(shape), "Fixed shape");
        GameplayTagManager.Initialize(Enumerable.Range(0, 5).Select(i => new GameplayTagRegistration("Tag" + i)).ToArray());
        var row = new Row { Shape = shape, Count = shape == "growth-five" ? 5 : 1, Present = shape == "present", External = shape == "external",
            Container = new GameplayTagCountContainer(), Tags = Enumerable.Range(0, 5).Select(i => GameplayTagManager.RequestTag("Tag" + i)).ToArray() };
        row.Callback = row.Observe; row.ExternalCallback = row.ObserveExternal;
        if (row.Present) row.Container.AddTag(row.Tags[0]);
        if (row.External) row.Container.RegisterTagEventCallback(row.Tags[0], GameplayTagEventType.NewOrRemoved, row.ExternalCallback);
        return row;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ConstructAndBind(Row row)
    {
        row.Binds = new GameplayTagContainerBinds(row.Container);
        for (int i = 0; i < row.Count; i++) row.Binds.Bind(row.Tags[i], row.Callback);
    }
    private static int ManagedSize(Type type)
    {
        var method = new DynamicMethod("ManagedRecordSize", typeof(int), Type.EmptyTypes, typeof(ColdBindingRecordScreen).Module, true);
        ILGenerator il = method.GetILGenerator(); il.Emit(OpCodes.Sizeof, type); il.Emit(OpCodes.Ret);
        return ((Func<int>)method.CreateDelegate(typeof(Func<int>)))();
    }
    private static int RecordId(object item)
    {
        FieldInfo field = item.GetType().GetField("TagId", Fields);
        return field == null ? ((GameplayTag)Field(item, "Tag")).RuntimeIndex : (int)field.GetValue(item);
    }
    private static void Toggle(Row row)
    {
        for (int i = 0; i < row.Count; i++) { if (row.Present) row.Container.RemoveTag(row.Tags[i]); else row.Container.AddTag(row.Tags[i]); }
        for (int i = 0; i < row.Count; i++) { if (row.Present) row.Container.AddTag(row.Tags[i]); else row.Container.RemoveTag(row.Tags[i]); }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Validate(Row row, out int capacity, out int size, out int callbackCapacity)
    {
        object state = Field(row.Binds, "m_State"); var items = (Array)Field(state, "Items"); capacity = items.Length;
        size = ManagedSize(items.GetType().GetElementType()); callbackCapacity = ((Array)Field(row.Container, "m_Callbacks")).Length;
        Check((int)Field(state, "Count") == row.Count && capacity == (row.Count == 1 ? 4 : 8), "Group count and capacity");
        Check(row.Events == row.Count && row.Checksum == row.Count * (row.Present ? 3L : 1L) && row.ExternalEvents == 0, "Immediate callbacks");
        for (int i = 0; i < items.Length; i++)
        {
            object item = items.GetValue(i);
            Check(RecordId(item) == (i < row.Count ? row.Tags[i].RuntimeIndex : 0), "Record ID/tail");
            Check((Field(item, "Callback") != null) == (i < row.Count), "Record callback/tail");
        }
        Toggle(row);
        Check(row.Events == row.Count * 3L && row.Checksum == row.Count * (row.Present ? 7L : 5L), "Bound event probe");
        row.Binds.UnbindAll(); long events = row.Events; Toggle(row); Check(row.Events == events, "No callback after unbind");
        Check(ReferenceEquals(items, Field(state, "Items")) && (int)Field(state, "Count") == 0, "Retained owned capacity");
        foreach (object item in items)
        {
            Check(RecordId(item) == 0 && Field(item, "Callback") == null, "Complete cleared record tails");
            FieldInfo tag = item.GetType().GetField("Tag", Fields);
            if (tag != null) Check(((GameplayTag)tag.GetValue(item)).m_Name == null, "Original cleared name tail");
        }
        int count = (int)Field(row.Container, "m_CallbackCount"); Check(count == (row.External ? 1 : 0), "External listener count");
        var table = (Array)Field(row.Container, "m_Callbacks");
        for (int i = 0; i < table.Length; i++)
        {
            object entry = table.GetValue(i), callbacks = Field(entry, "Callbacks");
            var edge = (Delegate)Field(callbacks, "OnNewOrRemove");
            Check(Field(callbacks, "OnAnyChange") == null, "No any-change listener");
            if (i < count) Check((int)Field(entry, "Id") == row.Tags[0].RuntimeIndex && edge.GetInvocationList().Length == 1 && ReferenceEquals(edge.GetInvocationList()[0], row.ExternalCallback), "Exact surviving external listener");
            else Check((int)Field(entry, "Id") == 0 && edge == null, "Cleared callback table tails");
        }
        Check(row.ExternalEvents == (row.External ? 4 : 0), "External probe events");
        Check(row.ExternalChecksum == (row.External ? 2L * (row.Tags[0].RuntimeIndex * 62L + 1) : 0), "External probe checksum");
        Check(row.Container.GetTagCount(row.Tags[0]) == (row.Present ? 1 : 0), "Restored presence");
    }
    private static int Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("One fixed cold shape required");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        Row row = Prepare(args[0]);
        int callbackBefore = ((Array)Field(row.Container, "m_Callbacks")).Length;
        long frequency = Stopwatch.Frequency; Allocated(); Stopwatch.GetTimestamp(); GC.CollectionCount(0); GC.CollectionCount(1); GC.CollectionCount(2);
        long before = Allocated(); int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        long start = Stopwatch.GetTimestamp();
        ConstructAndBind(row);
        long ticks = Stopwatch.GetTimestamp() - start, bytes = Allocated() - before;
        int delta0 = GC.CollectionCount(0) - gc0, delta1 = GC.CollectionCount(1) - gc1, delta2 = GC.CollectionCount(2) - gc2;
        long immediateEvents = row.Events, immediateChecksum = row.Checksum;
        bool pass = true; string error = ""; int capacity = -1, size = -1, callbackCapacity = -1;
        try { Validate(row, out capacity, out size, out callbackCapacity); }
        catch (Exception ex) { pass = false; error = ex.GetType().Name; }
        Console.WriteLine("{\"Shape\":\"" + row.Shape + "\",\"Count\":" + row.Count + ",\"Ticks\":" + ticks + ",\"Frequency\":" + frequency + ",\"AllocatedBytes\":" + bytes +
            ",\"GC0\":" + delta0 + ",\"GC1\":" + delta1 + ",\"GC2\":" + delta2 + ",\"ImmediateEvents\":" + immediateEvents + ",\"ImmediateChecksum\":" + immediateChecksum +
            ",\"ProbeEvents\":" + row.Events + ",\"ProbeChecksum\":" + row.Checksum + ",\"ExternalEvents\":" + row.ExternalEvents + ",\"ExternalChecksum\":" + row.ExternalChecksum +
            ",\"BindingCapacity\":" + capacity + ",\"ManagedRecordSize\":" + size + ",\"OwnedRecordArrayPayloadBytes\":" + (capacity * size) +
            ",\"CallbackCapacityBefore\":" + callbackBefore + ",\"CallbackCapacityAfterBind\":" + callbackCapacity + ",\"PointerSize\":" + IntPtr.Size +
            ",\"SemanticsPass\":" + (pass ? "true" : "false") + ",\"ErrorType\":\"" + error + "\",\"FirstBridgeJitIncluded\":true,\"WholeHeapRetainedBytesMeasured\":false}");
        GC.KeepAlive(row); return pass ? 0 : 1;
    }
}
