using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime;
using System.Security.Cryptography;
using System.Text;
using GameplayTags;

internal static class Program
{
    static readonly string[] Ids = { "fresh-single", "retained-absent", "retained-present", "same-tag-eight", "distinct-eight", "distinct-128", "external-multicast", "direct-subscription", "direct-dispatch" };
    static readonly int[] Seeds = { 8192, 16384, 16384, 4096, 4096, 256, 8192, 32768, 32768 };
    static readonly Func<long> Allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static));
    static readonly BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static GameplayTag[] Tags;
    static void Check(bool value) { if (!value) throw new InvalidOperationException("fixture invariant"); }
    static object Field(object value, string name) => value.GetType().GetField(name, Fields).GetValue(value);
    static object State(GameplayTagContainerBinds binds) => Field(binds, "m_State");
    static int Owned(GameplayTagContainerBinds binds) => (int)Field(State(binds), "Count");
    static string Q(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    static string Hash(byte[] value) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(value)).Replace("-", "").ToLowerInvariant(); }
    static void Init()
    {
        var registrations = Enumerable.Range(0, 128).Select(i => new GameplayTagRegistration("T" + i.ToString("D3", CultureInfo.InvariantCulture))).ToArray();
        GameplayTagManager.Initialize(registrations);
        Tags = registrations.Select((x, i) => GameplayTagManager.RequestTag("T" + i.ToString("D3", CultureInfo.InvariantCulture))).ToArray();
    }
    sealed class Row
    {
        internal string Id;
        internal int Count, Distinct, ExternalCount, Seed;
        internal bool Present;
        internal GameplayTagCountContainer Container;
        internal GameplayTagContainerBinds Binds;
        internal Action<bool> Callback;
        internal OnTagCountChangedDelegate External1, External2, Direct;
        internal long Events, Checksum, ExternalEvents, ExternalChecksum;
        internal void Observe(bool present) { Events++; Checksum += present ? 3 : 1; }
        internal void ObserveExternal1(GameplayTag tag, int count) { ExternalEvents++; ExternalChecksum += tag.RuntimeIndex * 31L + count; }
        internal void ObserveExternal2(GameplayTag tag, int count) { ExternalEvents++; ExternalChecksum += tag.RuntimeIndex * 31L + count; }
        internal void ObserveDirect(GameplayTag tag, int count) { Events++; Checksum += count + 1; }
        internal void ResetCounters() { Events = Checksum = ExternalEvents = ExternalChecksum = 0; }
        internal void NewContainer()
        {
            Container = new GameplayTagCountContainer(); Binds = new GameplayTagContainerBinds(Container);
            if (Present) Container.AddTag(Tags[0]);
            if (ExternalCount != 0)
            {
                Container.RegisterTagEventCallback(Tags[0], GameplayTagEventType.NewOrRemoved, External1);
                Container.RegisterTagEventCallback(Tags[0], GameplayTagEventType.NewOrRemoved, External2);
            }
        }
        internal void BindAll() { for (int j = 0; j < Count; j++) Binds.Bind(Tags[j % Distinct], Callback); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void Cycle()
        {
            if (Id == "direct-subscription")
            {
                Container.RegisterTagEventCallback(Tags[0], GameplayTagEventType.NewOrRemoved, Direct);
                Container.RemoveTagEventCallback(Tags[0], GameplayTagEventType.NewOrRemoved, Direct);
            }
            else if (Id == "direct-dispatch") { Container.AddTag(Tags[0]); Container.RemoveTag(Tags[0]); }
            else
            {
                if (Id == "fresh-single") NewContainer();
                BindAll(); Binds.UnbindAll();
            }
        }
    }
    static Row Make(int index)
    {
        var row = new Row { Id = Ids[index], Count = index == 3 || index == 4 ? 8 : index == 5 ? 128 : index >= 7 ? 0 : 1,
            Distinct = index == 4 ? 8 : index == 5 ? 128 : 1, Present = index == 2, ExternalCount = index == 6 || index == 8 ? 2 : 0, Seed = Seeds[index] };
        row.Callback = row.Observe; row.External1 = row.ObserveExternal1; row.External2 = row.ObserveExternal2; row.Direct = row.ObserveDirect;
        row.NewContainer(); return row;
    }
    static int Subscriptions(Row row)
    {
        int entries = (int)Field(row.Container, "m_CallbackCount"), subscriptions = 0;
        var array = (Array)Field(row.Container, "m_Callbacks");
        for (int i = 0; i < entries; i++)
        {
            var callback = (Delegate)Field(Field(array.GetValue(i), "Callbacks"), "OnNewOrRemove");
            if (callback != null)
            {
                Delegate[] list = callback.GetInvocationList(); subscriptions += list.Length;
                Check(row.ExternalCount == 2 && list.Length == 2 && list[0].Equals(row.External1) && list[1].Equals(row.External2));
            }
        }
        Check(entries == (row.ExternalCount == 0 ? 0 : 1));
        return subscriptions;
    }
    static void FinalState(Row row)
    {
        Check(Owned(row.Binds) == 0);
        foreach (object item in (Array)Field(State(row.Binds), "Items")) Check(Field(item, "Callback") == null);
        Check(Subscriptions(row) == row.ExternalCount);
        Check(row.Container.TagCount == (row.Present ? 1 : 0));
        for (int j = 0; j < row.Distinct; j++) Check(row.Container.GetTagCount(Tags[j]) == (row.Present && j == 0 ? 1 : 0));
    }
    static void Toggle(Row row)
    {
        for (int j = 0; j < row.Distinct; j++) { if (row.Present) row.Container.RemoveTag(Tags[j]); else row.Container.AddTag(Tags[j]); }
        for (int j = 0; j < row.Distinct; j++) { if (row.Present) row.Container.AddTag(Tags[j]); else row.Container.RemoveTag(Tags[j]); }
    }
    static long[] Probe(Row row)
    {
        FinalState(row); row.ResetCounters(); row.BindAll(); Toggle(row);
        Check(row.Events == 3L * row.Count && row.Checksum == (row.Present ? 7L : 5L) * row.Count);
        row.Binds.UnbindAll(); long events = row.Events, checksum = row.Checksum; Toggle(row);
        Check(row.Events == events && row.Checksum == checksum);
        Check(row.ExternalEvents == row.ExternalCount * 4L);
        Check(row.ExternalChecksum == row.ExternalCount * 2L * (Tags[0].RuntimeIndex * 62L + 1));
        FinalState(row);
        return new[] { row.Events, row.Checksum, row.ExternalEvents, row.ExternalChecksum };
    }
    static string ProbeFields(long[] before, long[] after) => ",\"BeforeProbeEvents\":" + before[0] + ",\"BeforeProbeChecksum\":" + before[1] + ",\"BeforeProbeExternalEvents\":" + before[2] + ",\"BeforeProbeExternalChecksum\":" + before[3] +
        ",\"AfterProbeEvents\":" + after[0] + ",\"AfterProbeChecksum\":" + after[1] + ",\"AfterProbeExternalEvents\":" + after[2] + ",\"AfterProbeExternalChecksum\":" + after[3];
    static string StateFields(Row row) => ",\"OwnedSubscriptions\":" + Owned(row.Binds) + ",\"ExternalSubscriptions\":" + Subscriptions(row) + ",\"TagCount\":" + row.Container.TagCount;
    static void Warm(Row row, int iterations, string mode)
    {
        long[] before = Probe(row);
        if (mode == "preflight")
        {
            row.Cycle(); FinalState(row); long[] afterFixture = Probe(row);
            Check(before.SequenceEqual(afterFixture));
            Console.WriteLine("{\"Mode\":\"preflight\",\"Id\":" + Q(row.Id) + ",\"Binds\":" + row.Count + ",\"Tags\":" + row.Distinct + ProbeFields(before, afterFixture) + StateFields(row) + RuntimeFields() + "}"); return;
        }
        for (int j = 0; j < 1024; j++) row.Cycle(); row.ResetCounters();
        Allocated(); Stopwatch.GetTimestamp(); GC.CollectionCount(0); GC.CollectionCount(1); GC.CollectionCount(2);
        long allocated = Allocated(); int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        long start = Stopwatch.GetTimestamp();
        for (int j = 0; j < iterations; j++) row.Cycle();
        long ticks = Stopwatch.GetTimestamp() - start, bytes = Allocated() - allocated;
        int dg0 = GC.CollectionCount(0) - gc0, dg1 = GC.CollectionCount(1) - gc1, dg2 = GC.CollectionCount(2) - gc2;
        long events = row.Events, checksum = row.Checksum, extEvents = row.ExternalEvents, extChecksum = row.ExternalChecksum;
        Check(events == (long)iterations * row.Count && checksum == (long)iterations * row.Count * (row.Present ? 3 : 1));
        Check(extEvents == (row.Id == "direct-dispatch" ? 4L * iterations : 0));
        Check(extChecksum == (row.Id == "direct-dispatch" ? 2L * iterations * (Tags[0].RuntimeIndex * 62L + 1) : 0));
        FinalState(row); long[] after = Probe(row); Check(before.SequenceEqual(after));
        Console.WriteLine("{\"Mode\":" + Q(mode) + ",\"Id\":" + Q(row.Id) + ",\"Iterations\":" + iterations + ",\"Ticks\":" + ticks + ",\"StopwatchFrequency\":" + Stopwatch.Frequency +
            ",\"BatchMs\":" + N(ticks * 1000.0 / Stopwatch.Frequency) + ",\"Ns\":" + N(ticks * 1000000000.0 / Stopwatch.Frequency / iterations) + ",\"Bytes\":" + bytes +
            ",\"Events\":" + events + ",\"Checksum\":" + checksum + ",\"ExternalEvents\":" + extEvents + ",\"ExternalChecksum\":" + extChecksum +
            ",\"Gc0\":" + dg0 + ",\"Gc1\":" + dg1 + ",\"Gc2\":" + dg2 + ProbeFields(before, after) + StateFields(row) + RuntimeFields() + "}");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void FirstBind(Row row) => row.Binds.Bind(Tags[0], row.Callback);
    static void First(string id)
    {
        Check(id == "first-empty" || id == "first-external-multicast" || id == "first-present");
        Row row = Make(id == "first-external-multicast" ? 6 : id == "first-present" ? 2 : 1);
        int bindingCapacity = ((Array)Field(State(row.Binds), "Items")).Length;
        int callbackCapacity = ((Array)Field(row.Container, "m_Callbacks")).Length;
        FinalState(row); row.ResetCounters();
        Allocated(); Stopwatch.GetTimestamp(); GC.CollectionCount(0); GC.CollectionCount(1); GC.CollectionCount(2);
        long allocated = Allocated(); int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        long start = Stopwatch.GetTimestamp();
        FirstBind(row);
        long ticks = Stopwatch.GetTimestamp() - start, bytes = Allocated() - allocated;
        int dg0 = GC.CollectionCount(0) - gc0, dg1 = GC.CollectionCount(1) - gc1, dg2 = GC.CollectionCount(2) - gc2;
        Check(row.Events == 1 && row.Checksum == (row.Present ? 3 : 1) && Owned(row.Binds) == 1);
        Toggle(row); Check(row.Events == 3 && row.Checksum == (row.Present ? 7 : 5)); row.Binds.UnbindAll(); Toggle(row);
        Check(row.Events == 3 && row.Checksum == (row.Present ? 7 : 5) && row.ExternalEvents == row.ExternalCount * 4L); FinalState(row);
        Console.WriteLine("{\"Mode\":\"cold\",\"Id\":" + Q(id) + ",\"Binds\":1,\"BindingCapacityBefore\":" + bindingCapacity + ",\"CallbackCapacityBefore\":" + callbackCapacity +
            ",\"Ticks\":" + ticks + ",\"StopwatchFrequency\":" + Stopwatch.Frequency + ",\"Ns\":" + N(ticks * 1000000000.0 / Stopwatch.Frequency) + ",\"Bytes\":" + bytes +
            ",\"Gc0\":" + dg0 + ",\"Gc1\":" + dg1 + ",\"Gc2\":" + dg2 + ",\"Events\":" + row.Events + ",\"Checksum\":" + row.Checksum + ",\"ExternalEvents\":" + row.ExternalEvents + ",\"ExternalChecksum\":" + row.ExternalChecksum + StateFields(row) + RuntimeFields() + "}");
    }
    static string RuntimeFields()
    {
        bool tiering; bool known = AppContext.TryGetSwitch("System.Runtime.TieredCompilation", out tiering);
        bool mono = Type.GetType("Mono.Runtime") != null;
        return ",\"RuntimeVersion\":" + Q(Environment.Version.ToString()) + ",\"ServerGC\":" + (GCSettings.IsServerGC ? 1 : 0) +
            ",\"TieringConfigured\":" + (known ? (tiering ? 1 : 0) : -1) + ",\"ReadyToRunRequested\":" + (mono ? -1 : int.Parse(Environment.GetEnvironmentVariable("DOTNET_ReadyToRun"), CultureInfo.InvariantCulture)) +
            ",\"TieringRequested\":" + (mono ? -1 : int.Parse(Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), CultureInfo.InvariantCulture));
    }
    static void Policy()
    {
        MethodInfo method = typeof(GameplayTagContainerBinds).GetMethod("Bind"); byte[] bytes = method.GetMethodBody().GetILAsByteArray();
        var ops = typeof(OpCodes).GetFields(BindingFlags.Static | BindingFlags.Public).Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => unchecked((ushort)o.Value));
        int constructors = 0;
        for (int p = 0; p < bytes.Length;)
        {
            ushort code = bytes[p++]; if (code == 0xfe) code = (ushort)(0xfe00 | bytes[p++]); OpCode op = ops[code];
            switch (op.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineI: case OperandType.ShortInlineVar: case OperandType.ShortInlineBrTarget: p++; break;
                case OperandType.InlineVar: p += 2; break;
                case OperandType.InlineMethod:
                    MemberInfo member = method.Module.ResolveMember(BitConverter.ToInt32(bytes, p));
                    if (op == OpCodes.Newobj && member.DeclaringType == typeof(OnTagCountChangedDelegate)) constructors++;
                    p += 4; break;
                case OperandType.InlineI: case OperandType.InlineField: case OperandType.InlineType: case OperandType.InlineTok: case OperandType.InlineBrTarget: case OperandType.InlineString: case OperandType.InlineSig: case OperandType.ShortInlineR: p += 4; break;
                case OperandType.InlineI8: case OperandType.InlineR: p += 8; break;
                case OperandType.InlineSwitch: int count = BitConverter.ToInt32(bytes, p); p += 4 + count * 4; break;
                default: throw new InvalidOperationException("unsupported IL operand");
            }
        }
        Type[] types = typeof(GameplayTagContainerBinds).Assembly.GetTypes().Where(t => t.Namespace == "GameplayTags").OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();
        var layout = new StringBuilder(); int fields = 0;
        foreach (Type type in types)
        {
            layout.Append(type.FullName).Append('|').Append((int)type.Attributes).Append('|').Append(type.BaseType);
            if (type.StructLayoutAttribute != null) layout.Append('|').Append(type.StructLayoutAttribute.Value).Append('|').Append(type.StructLayoutAttribute.Pack).Append('|').Append(type.StructLayoutAttribute.Size);
            layout.Append('\n');
            foreach (FieldInfo field in type.GetFields(Fields | BindingFlags.Static | BindingFlags.DeclaredOnly).OrderBy(f => f.MetadataToken))
            { fields++; layout.Append(field.Name).Append('|').Append(field.FieldType).Append('|').Append((int)field.Attributes).Append('\n'); }
        }
        Console.WriteLine("{\"Mode\":\"policy\",\"BindILBytes\":" + bytes.Length + ",\"DelegateConstructors\":" + constructors + ",\"RuntimeTypes\":" + types.Length + ",\"RuntimeFields\":" + fields + ",\"LayoutSHA256\":" + Q(Hash(Encoding.UTF8.GetBytes(layout.ToString()))) + ",\"BindILSHA256\":" + Q(Hash(bytes)) + RuntimeFields() + "}");
    }
    static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "policy") { Policy(); return 0; }
        Init();
        if (args.Length == 2 && args[0] == "cold") { First(args[1]); return 0; }
        bool pilot = args.Length == 1 && args[0] == "pilot", preflight = args.Length == 1 && args[0] == "preflight";
        Check(pilot || preflight || args.Length == 3 && args[0] == "timing" && args[1] == "--iterations-file");
        string[] map = pilot || preflight ? null : File.ReadAllLines(args[2]); if (map != null) Check(map.Length == Ids.Length);
        for (int i = 0; i < Ids.Length; i++)
        {
            int iterations = Seeds[i];
            if (map != null) { string[] parts = map[i].Split('\t'); Check(parts.Length == 2 && parts[0] == Ids[i]); iterations = int.Parse(parts[1], CultureInfo.InvariantCulture); Check(iterations >= Seeds[i]); }
            Warm(Make(i), iterations, preflight ? "preflight" : pilot ? "pilot" : "timing");
        }
        return 0;
    }
}
