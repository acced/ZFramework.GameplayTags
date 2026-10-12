using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using GameplayTags;

// Untimed supplemental checks. Existing core and binding bodies are reused separately.
internal static class BindingRecordCheck
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static BinaryWriter Writer;
    private static int Frames;
    private static object Field(object value, string name)
    {
        for (Type type = value.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo field = type.GetField(name, Fields | BindingFlags.DeclaredOnly);
            if (field != null) return field.GetValue(value);
        }
        throw new MissingFieldException(value.GetType().FullName, name);
    }
    private static object State(GameplayTagContainerBinds binds) => Field(binds, "m_State");
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static int Id(object item)
    {
        FieldInfo field = item.GetType().GetField("TagId", Fields);
        return field == null ? ((GameplayTag)Field(item, "Tag")).RuntimeIndex : (int)field.GetValue(item);
    }
    private static void Snapshot(string label, GameplayTagContainerBinds binds, GameplayTagCountContainer container, List<string> log)
    {
        object state = State(binds); var items = (Array)Field(state, "Items"); int count = (int)Field(state, "Count");
        Writer.Write(label); Writer.Write(count); Writer.Write(items.Length);
        for (int i = 0; i < items.Length; i++)
        {
            object item = items.GetValue(i); var callback = (Delegate)Field(item, "Callback");
            Writer.Write(Id(item)); Writer.Write(callback != null);
            if (i >= count)
            {
                Check(Id(item) == 0 && callback == null, "Cleared or unused record tail");
                FieldInfo tagField = item.GetType().GetField("Tag", Fields);
                if (tagField != null) Check(((GameplayTag)tagField.GetValue(item)).m_Name == null, "Cleared old tag-name tail");
            }
        }
        int registered = (int)Field(container, "m_CallbackCount"); var table = (Array)Field(container, "m_Callbacks");
        Writer.Write(registered); Writer.Write(table.Length);
        for (int i = 0; i < table.Length; i++)
        {
            object entry = table.GetValue(i); Writer.Write((int)Field(entry, "Id"));
            object callbacks = Field(entry, "Callbacks");
            foreach (string field in new[] { "OnNewOrRemove", "OnAnyChange" })
            {
                var callback = (Delegate)Field(callbacks, field); Writer.Write(callback == null ? 0 : callback.GetInvocationList().Length);
                if (i >= registered) Check(callback == null, "Cleared callback-table tail");
            }
            if (i >= registered) Check((int)Field(entry, "Id") == 0, "Cleared callback-table ID tail");
        }
        Writer.Write(log.Count); foreach (string value in log) Writer.Write(value);
        Frames++;
    }
    private static void NoneBeforeRegistry()
    {
        Check(!GameplayTagManager.IsInitialized, "Registry initially untouched");
        var container = new GameplayTagCountContainer(); var binds = new GameplayTagContainerBinds(container); var copy = binds;
        var log = new List<string>(); Action<bool> callback = value => log.Add("none:" + value);
        Snapshot("none-empty", binds, container, log);
        GameplayTag namedNone = default; namedNone.m_Name = new string('x', 47);
        binds.Bind(GameplayTag.None, callback); copy.Bind(namedNone, callback);
        Check(log.SequenceEqual(new[] { "none:False", "none:False" }), "None immediate order");
        Snapshot("none-bound", binds, container, log); copy.UnbindAll();
        Snapshot("none-cleared", binds, container, log); binds.Dispose();
        Check(!GameplayTagManager.IsInitialized, "None Bind/Unbind did not initialize registry");
        Console.WriteLine("PASS None and named-None before registry initialization");
    }
    private sealed class HiddenContainer : GameplayTagCountContainer, IGameplayTagCountContainer
    {
        public new void RegisterTagEventCallback(GameplayTag tag, GameplayTagEventType type, OnTagCountChangedDelegate callback) => throw new Exception("hidden registration");
        public new void RemoveTagEventCallback(GameplayTag tag, GameplayTagEventType type, OnTagCountChangedDelegate callback) => throw new Exception("hidden removal");
        public new int GetTagCount(GameplayTag tag) => throw new Exception("hidden count");
    }
    private static void NamesGrowthAndHiddenMethods()
    {
        GameplayTag a = GameplayTagManager.RequestTag("A.B.C"), b = GameplayTagManager.RequestTag("A.B.D");
        GameplayTag nullName = a, otherName = a, arbitraryName = b;
        nullName.m_Name = null; otherName.m_Name = b.Name; arbitraryName.m_Name = new string('z', 53);
        GameplayTagCountContainer container = new HiddenContainer(); var binds = new GameplayTagContainerBinds(container); var copy = binds;
        var log = new List<string>(); OnTagCountChangedDelegate external = (tag, value) => log.Add("external:" + tag.Name + ":" + value);
        container.RegisterTagEventCallback(a, GameplayTagEventType.NewOrRemoved, external);
        Snapshot("names-empty", binds, container, log);
        GameplayTag[] tags = { nullName, otherName, arbitraryName, a, b };
        for (int i = 0; i < tags.Length; i++)
        {
            int position = i; copy.Bind(tags[i], value => log.Add(position + ":" + value));
            tags[i].m_Name = "changed-after-bind";
            Check(((Array)Field(State(binds), "Items")).Length == (i < 4 ? 4 : 8), "Original capacity growth");
            Snapshot("names-bind-" + i, binds, container, log);
        }
        container.AddTag(a); container.AddTag(b);
        Check(log.SequenceEqual(new[] { "0:False", "1:False", "2:False", "3:False", "4:False", "external:A.B.C:1", "0:True", "1:True", "3:True", "2:True", "4:True" }), "Mutated-name ID behavior and order");
        Snapshot("names-events", binds, container, log); copy.Dispose(); Snapshot("names-cleared", binds, container, log);
        container.RemoveTag(a); container.RemoveTag(b); Check(log.Last() == "external:A.B.C:0" && log.Count == 12, "Only external listener survives");
        binds.Bind(nullName, value => log.Add("reuse:" + value)); binds.UnbindAll();
        container.RemoveTagEventCallback(a, GameplayTagEventType.NewOrRemoved, external);
        Snapshot("names-reused-cleared", binds, container, log);
        Check((int)Field(container, "m_CallbackCount") == 0, "All selected listeners removed");
        Console.WriteLine("PASS mutated/null/noncanonical names, growth boundaries, cleared tails, copies and hidden derived methods");
    }
    private static void QueuedUnbind()
    {
        GameplayTag a = GameplayTagManager.RequestTag("A.B.C"); var container = new GameplayTagCountContainer();
        var binds = new GameplayTagContainerBinds(container); var copy = binds; var log = new List<string>();
        container.RegisterTagEventCallback(a, GameplayTagEventType.NewOrRemoved, (tag, count) => { log.Add("external:" + count); copy.UnbindAll(); });
        binds.Bind(a, value => log.Add("bound:" + value)); container.AddTag(a); container.RemoveTag(a);
        Check(log.SequenceEqual(new[] { "bound:False", "external:1", "bound:True", "external:0" }), "Already queued multicast retains bound callback");
        Snapshot("queued-unbind", binds, container, log);
        Console.WriteLine("PASS queued callback unbind and original multicast delivery");
    }
    private static int ManagedSize(Type type)
    {
        var method = new DynamicMethod("ManagedRecordSize", typeof(int), Type.EmptyTypes, typeof(BindingRecordCheck).Module, true);
        ILGenerator il = method.GetILGenerator(); il.Emit(OpCodes.Sizeof, type); il.Emit(OpCodes.Ret);
        return ((Func<int>)method.CreateDelegate(typeof(Func<int>)))();
    }
    private static int Main(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("snapshot, managed-layout and metadata paths required");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        Check(typeof(GameplayTagContainerBinds).Assembly.GetName().Name == "Test", "Native capture assembly Test");
        using (Writer = new BinaryWriter(new FileStream(args[0], FileMode.CreateNew)))
        {
            NoneBeforeRegistry();
            int result = (int)typeof(CoreProgram).GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            if (result != 0) return result;
            Probe.Run(); NamesGrowthAndHiddenMethods(); QueuedUnbind();
        }
        Type record = typeof(GameplayTagContainerBinds).GetNestedType("BindData", BindingFlags.NonPublic);
        using (var writer = new StreamWriter(new FileStream(args[1], FileMode.CreateNew)))
            writer.WriteLine("{\"PointerSize\":" + IntPtr.Size + ",\"GameplayTagSize\":" + ManagedSize(typeof(GameplayTag)) + ",\"BindDataSize\":" + ManagedSize(record) + "}");
        MetadataGate.Write(args[2]);
        Console.WriteLine("PASS binding-record supplemental frames: " + Frames);
        Console.WriteLine("PASS core and binding suites, record layout, complete portable metadata capture; no performance measurement");
        return 0;
    }
}
