using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using GameplayTags;

// All support types remain outside GameplayTags; both builds use this exact file.
public sealed class DiscoveryConstructorException : Exception
{
    public DiscoveryConstructorException() : base("deliberate discovery constructor failure") { }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = true)]
public sealed class DiscoveryTraceAttribute : GameplayTagAttribute
{
    public DiscoveryTraceAttribute(string key, string name, string description, int flags)
        : base(name, description, (GameplayTagFlags)flags)
    {
        DiscoverySemanticCheck.Trace.Add(key);
        if (key == "throw" && DiscoverySemanticCheck.Phase == 0)
            throw DiscoverySemanticCheck.ConstructorFailure;
        if (key == "malformed" && DiscoverySemanticCheck.Phase == 1)
            TagName = null;
    }
}

internal static class DiscoverySemanticCheck
{
    internal static int Phase;
    internal static readonly List<string> Trace = new List<string>();
    internal static readonly DiscoveryConstructorException ConstructorFailure = new DiscoveryConstructorException();
    private const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static BinaryWriter Writer;
    private static StreamWriter Raw;
    private static void Check(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
    private static FieldInfo[] RegistryFields() => typeof(GameplayTagManager).GetFields(StaticFields).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
    private static object[] RegistryReferences() => RegistryFields().Select(f => f.GetValue(null)).ToArray();
    private static object Field(string name) => typeof(GameplayTagManager).GetField(name, StaticFields).GetValue(null);
    private static void CheckReferences(object[] before, string label)
    {
        var fields = RegistryFields();
        Check(fields.Length == before.Length, "Registry field inventory stable");
        for (int i = 0; i < fields.Length; i++)
            Check(fields[i].FieldType.IsValueType ? Equals(before[i], fields[i].GetValue(null)) : ReferenceEquals(before[i], fields[i].GetValue(null)), label + " field/reference unchanged: " + fields[i].Name);
    }
    // Backend field names are explicit; unknown fields stop rather than disappearing.
    private static void CheckDictionaryFields(FieldInfo[] fields)
    {
#if PORTABLE_MONO
        string expected = "_syncRoot,buckets,comparer,count,entries,freeCount,freeList,keys,values,version";
#else
        string expected = "_buckets,_comparer,_count,_entries,_fastModMultiplier,_freeCount,_freeList,_keys,_values,_version";
#endif
        Check(string.Join(",", fields.Select(f => f.Name)) == expected, "Exact backend Dictionary physical fields");
    }
    private static bool DictionaryReferenceField(string name)
    {
#if PORTABLE_MONO
        return name == "comparer" || name == "keys" || name == "values" || name == "_syncRoot";
#else
        return name == "_comparer" || name == "_keys" || name == "_values";
#endif
    }
    private static void Value(BinaryWriter writer, object value)
    {
        writer.Write(value != null); if (value == null) return;
        Type type = value.GetType(); writer.Write(type.FullName);
        if (value is string text) { writer.Write(text); return; }
        if (value is int number) { writer.Write(number); return; }
        if (value is uint unsigned) { writer.Write(unsigned); return; }
        if (value is ulong wideUnsigned) { writer.Write(wideUnsigned); return; }
        if (value is bool boolean) { writer.Write(boolean); return; }
        if (type.IsEnum) { writer.Write(Convert.ToInt64(value, CultureInfo.InvariantCulture)); return; }
        if (value is Array array)
        {
            Check(array.Rank == 1, "Only one-dimensional registry arrays");
            writer.Write(array.Length); foreach (object item in array) Value(writer, item); return;
        }
        if (value is Dictionary<string, int> dictionary)
        {
            // Full physical table and all scalar fields are recorded, including spare entries.
            // Comparer and lazy Keys/Values are represented by null/type identity, not global object graphs.
            var fields = type.GetFields(InstanceFields).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray(); CheckDictionaryFields(fields);
            writer.Write(fields.Length);
            foreach (FieldInfo field in fields)
            {
                writer.Write(field.Name); object item = field.GetValue(value);
                if (DictionaryReferenceField(field.Name))
                { writer.Write(item != null); if (item != null) writer.Write(item.GetType().FullName); }
                else Value(writer, item);
            }
            writer.Write(dictionary.Count);
            foreach (var pair in dictionary.OrderBy(p => p.Key, StringComparer.Ordinal)) { writer.Write(pair.Key); writer.Write(pair.Value); }
            return;
        }
        Check(type.IsValueType, "Unsupported registry reference type: " + type.FullName);
        var members = type.GetFields(InstanceFields).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
        writer.Write(members.Length);
        foreach (FieldInfo field in members) { writer.Write(field.Name); Value(writer, field.GetValue(value)); }
    }
    private static void Snapshot(string label)
    {
        Writer.Write(label);
        var fields = RegistryFields(); Writer.Write(fields.Length);
        foreach (FieldInfo field in fields)
        {
            Writer.Write(field.Name); Writer.Write(field.FieldType.FullName);
            if (field.Name == "s_InitializationLock") { Writer.Write(Field(field.Name) != null); continue; }
            Value(Writer, field.GetValue(null));
        }
    }
    private static void ExpectTrace(string label, params string[] expected)
    {
        Check(Trace.SequenceEqual(expected), label + " constructor order: " + string.Join(",", Trace));
        Writer.Write(label); Writer.Write(Trace.Count); foreach (string item in Trace) Writer.Write(item);
        Raw.WriteLine(label + "|" + string.Join(",", Trace)); Raw.Flush();
        Trace.Clear();
    }
    private static AssemblyBuilder Assembly(string suffix)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("DiscoveryFixture." + suffix), AssemblyBuilderAccess.Run);
        assembly.DefineDynamicModule("fixture"); return assembly;
    }
    private static void Plain(AssemblyBuilder assembly, string name, string description, int flags)
    {
        var constructor = typeof(GameplayTagAttribute).GetConstructor(new[] { typeof(string), typeof(string), typeof(GameplayTagFlags) });
        assembly.SetCustomAttribute(new CustomAttributeBuilder(constructor, new object[] { name, description, (GameplayTagFlags)flags }));
    }
    private static void Traced(AssemblyBuilder assembly, string key, string name, string description, int flags)
    {
        var constructor = typeof(DiscoveryTraceAttribute).GetConstructor(new[] { typeof(string), typeof(string), typeof(string), typeof(int) });
        assembly.SetCustomAttribute(new CustomAttributeBuilder(constructor, new object[] { key, name, description, flags }));
    }
    // Runtime methods and their inlining annotations are untouched. These fixed ordinary
    // callers provide complete natural contexts, including post-publication fast paths.
    [MethodImpl(MethodImplOptions.NoInlining)] private static bool NativeInitialize() { GameplayTagManager.InitializeIfNeeded(); return GameplayTagManager.IsInitialized; }
    [MethodImpl(MethodImplOptions.NoInlining)] private static int NativeCount() => GameplayTagManager.TagCount;
    [MethodImpl(MethodImplOptions.NoInlining)] private static int NativeAll() => GameplayTagManager.GetAllTags().Length;
    [MethodImpl(MethodImplOptions.NoInlining)] private static int NativeRequest(string name) => GameplayTagManager.RequestTag(name).RuntimeIndex;
    [MethodImpl(MethodImplOptions.NoInlining)] private static int NativeTryRequest(string name) => GameplayTagManager.RequestTag(name, out GameplayTag tag) ? tag.RuntimeIndex : 0;
    private static void FastPaths(int count, string known, int index)
    {
        object[] before = RegistryReferences();
        Check(NativeInitialize() && NativeCount() == count && NativeAll() == count, "Initialized direct/count/all contexts");
        Check(NativeRequest(known) == index && NativeTryRequest(known) == index, "Initialized request contexts");
        Check(NativeRequest(null) == 0 && NativeRequest("") == 0 && NativeRequest("Missing") == 0, "None request cases");
        Check(NativeTryRequest(null) == 0 && NativeTryRequest("") == 0 && NativeTryRequest("Missing") == 0, "None try-request cases");
        Check(Trace.Count == 0, "Fast paths never reconstruct attributes"); CheckReferences(before, "Initialized fast path"); Snapshot("initialized-fast-path");
        Exception failure = null; try { GameplayTagManager.Initialize(Array.Empty<GameplayTagRegistration>()); } catch (Exception error) { failure = error; }
        Check(failure is InvalidOperationException, "Explicit reinitialization remains rejected"); CheckReferences(before, "Rejected explicit reinitialization");
    }
    private static void TypedArray(Assembly assembly, int count, int derived)
    {
        Attribute[] direct = Attribute.GetCustomAttributes(assembly, typeof(GameplayTagAttribute));
        var generic = assembly.GetCustomAttributes<GameplayTagAttribute>();
        Check(direct.GetType() == typeof(GameplayTagAttribute[]) && generic.GetType() == typeof(GameplayTagAttribute[]), "Both API results have exact GameplayTagAttribute[] type");
        var a = (GameplayTagAttribute[])direct; var b = (GameplayTagAttribute[])generic;
        Check(a.Length == count && b.Length == count && a.Count(x => x is DiscoveryTraceAttribute) == derived, "Typed array includes derived declarations");
        for (int i = 0; i < count; i++) Check(a[i].GetType() == b[i].GetType() && a[i].TagName == b[i].TagName && a[i].Description == b[i].Description && a[i].Flags == b[i].Flags, "API order and values agree");
        Writer.Write("array-api"); Writer.Write(assembly.GetName().Name); Writer.Write(direct.GetType().FullName); Writer.Write(generic.GetType().FullName); Writer.Write(count); Writer.Write(derived);
        Raw.WriteLine("array-api|" + assembly.GetName().Name + "|" + direct.GetType().FullName + "|" + generic.GetType().FullName + "|" + count + "|" + derived); Raw.Flush();
    }
    private static void Empty()
    {
        var empty = Assembly("00.Empty");
        Check(!GameplayTagManager.IsInitialized && NativeRequest(null) == 0 && NativeTryRequest("") == 0 && !GameplayTagManager.IsInitialized, "Empty name requests do not initialize");
        Snapshot("uninitialized");
        Check(NativeCount() == 0 && GameplayTagManager.IsInitialized, "Cold public Count publishes empty automatic discovery");
        Check(GameplayTagManager.GetAllTags().Length == 0 && ((Dictionary<string, int>)Field("s_IndicesByName")).Count == 0, "No accidental declarations");
        Snapshot("empty-published"); FastPaths(0, "Missing", 0);
        TypedArray(empty, 0, 0); TypedArray(typeof(DiscoverySemanticCheck).Assembly, 0, 0);
        ExpectTrace("empty-no-constructors");
    }
    private static AssemblyBuilder[] Fixtures()
    {
        // Deliberately create in reverse FullName order; automatic discovery must sort them.
        var later = Assembly("40.Later"); Traced(later, "later", "Z", "last", 16);
        var malformed = Assembly("30.Malformed"); Traced(malformed, "malformed", "B", "bee", 8); Traced(malformed, "same-assembly-after-malformed", "B.Child", "child", 0);
        var throwing = Assembly("20.Throw"); Traced(throwing, "throw", "A0", "adjacent-root", 4); Traced(throwing, "same-assembly-after-throw", "A.Branch", "Beta", 2);
        var declarations = Assembly("10.Declarations");
        Plain(declarations, "A.Branch.Leaf", "leaf", 1); Traced(declarations, "early-one", "A.Branch", "zeta", 1);
        Plain(declarations, "A.Branch", "Alpha", 2); Traced(declarations, "early-two", "A.Other", "other", 4);
        var empty = Assembly("00.Empty");
        var fixtures = new[] { empty, declarations, throwing, malformed, later };
        string[] actual = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name.StartsWith("DiscoveryFixture.", StringComparison.Ordinal)).Select(a => a.GetName().Name).ToArray();
        Check(actual.Length == fixtures.Length, "All dynamic fixture assemblies are in automatic discovery domain");
        Check(actual.Distinct().Count() == fixtures.Length, "Unique fixture names");
        Raw.WriteLine("loaded-fixtures|" + string.Join(",", actual)); Raw.Flush();
        return fixtures;
    }
    private static void ConstructorFailureThenMalformedThenSuccess()
    {
        var fixtures = Fixtures(); object[] unpublished = RegistryReferences(); Snapshot("uninitialized");
        Exception caught = null;
        try { NativeRequest("A.Branch"); } catch (Exception error) { caught = error; }
        Check(caught != null && (ReferenceEquals(caught, ConstructorFailure) || (caught is TargetInvocationException && ReferenceEquals(caught.InnerException, ConstructorFailure))), "Exact constructor failure propagates, with only runtime reflection wrapper allowed");
        Writer.Write("constructor-exception"); Writer.Write(caught.GetType().FullName); Writer.Write(caught.Message); Writer.Write(ReferenceEquals(caught, ConstructorFailure));
        if (caught.InnerException != null) { Writer.Write(caught.InnerException.GetType().FullName); Writer.Write(caught.InnerException.Message); }
        Check(!GameplayTagManager.IsInitialized, "Constructor exception does not publish"); CheckReferences(unpublished, "Constructor exception"); Snapshot("constructor-no-publication");
        ExpectTrace("constructor-failure", "early-one", "early-two", "throw");
        Phase = 1; caught = null;
        try { NativeTryRequest("B"); } catch (Exception error) { caught = error; }
        Check(caught is NullReferenceException, "Null TagName fails during registration");
        Writer.Write("malformed-exception"); Writer.Write(caught.GetType().FullName); Writer.Write(caught.Message);
        Check(!GameplayTagManager.IsInitialized, "Malformed declaration does not publish"); CheckReferences(unpublished, "Malformed declaration"); Snapshot("malformed-no-publication");
        ExpectTrace("malformed-before-later-assembly", "early-one", "early-two", "throw", "same-assembly-after-throw", "malformed", "same-assembly-after-malformed");
        Phase = 2;
        Check(NativeAll() == 8 && GameplayTagManager.IsInitialized, "Successful retry publishes once");
        ExpectTrace("successful-retry", "early-one", "early-two", "throw", "same-assembly-after-throw", "malformed", "same-assembly-after-malformed", "later");
        string[] names = { "A", "A.Branch", "A.Branch.Leaf", "A.Other", "A0", "B", "B.Child", "Z" };
        int[] flags = { 7, 3, 1, 4, 4, 8, 0, 16 }; string[] descriptions = { "", "Alpha", "leaf", "other", "adjacent-root", "bee", "child", "last" };
        int[] parents = { 0, 1, 2, 1, 0, 0, 6, 0 }; int[] ends = { 5, 4, 4, 5, 6, 8, 8, 9 };
        GameplayTag[] tags = GameplayTagManager.GetAllTags().ToArray();
        Check(tags.Select(t => t.Name).SequenceEqual(names), "Ordinal sibling/depth-first order preserves A subtree before A0");
        for (int i = 0; i < names.Length; i++)
        {
            ref readonly GameplayTagDefinition definition = ref GameplayTagManager.GetDefinitionFromRuntimeIndex(i + 1);
            Check(tags[i].RuntimeIndex == i + 1 && definition.TagName == names[i] && (int)definition.Flags == flags[i] && definition.Description == descriptions[i] && definition.ParentIndex == parents[i] && definition.SubtreeEnd == ends[i], "Independent registry merge/order oracle at " + names[i]);
        }
        Snapshot("merged-registry"); FastPaths(names.Length, "A.Branch", 2);
        int[] counts = { 0, 4, 2, 2, 1 }; int[] derived = { 0, 2, 2, 2, 1 };
        for (int i = 0; i < fixtures.Length; i++) TypedArray(fixtures[i], counts[i], derived[i]);
        ExpectTrace("post-success-api-verification", "early-one", "early-two", "early-one", "early-two", "throw", "same-assembly-after-throw", "throw", "same-assembly-after-throw", "malformed", "same-assembly-after-malformed", "malformed", "same-assembly-after-malformed", "later", "later");
        Snapshot("after-api-verification");
    }
    private static int Main(string[] args)
    {
        if (args.Length != 4 || (args[0] != "empty" && args[0] != "retry")) throw new ArgumentException("Fixed mode, fresh snapshot, metadata and raw paths required");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        Check(IntPtr.Size == 8 && typeof(GameplayTagManager).Assembly.GetName().Name == "Test", "Pinned x64 Test assembly");
        using (Writer = new BinaryWriter(new FileStream(args[1], FileMode.CreateNew)))
        using (Raw = new StreamWriter(new FileStream(args[3], FileMode.CreateNew)))
        {
            Writer.Write(args[0]); Raw.WriteLine("mode|" + args[0]);
            if (args[0] == "empty") Empty(); else ConstructorFailureThenMalformedThenSuccess();
        }
        MetadataGate.Write(args[2]);
        Console.WriteLine("PASS " + args[0] + ": automatic discovery, exact registry snapshot, API typed arrays and public initialized fast paths");
        Console.WriteLine("PASS public caller contexts; no native capture, forced preparation, reset, timing or allocation sample");
        return 0;
    }
}
