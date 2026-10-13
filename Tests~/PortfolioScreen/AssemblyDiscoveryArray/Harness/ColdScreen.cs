using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
#if !PORTABLE_MONO
using System.Runtime.Loader;
#endif
using System.Security.Cryptography;
using GameplayTags;

// Persisted declarations are loaded before counters; no manager operation, manager-field
// reflection, or custom-attribute retrieval occurs before the one measured NativeAll.
internal static class DiscoveryArrayCheck
{
#if PORTABLE_MONO
    // Resolution and first invocation are outside the measured operation.
    private static readonly Func<long> Allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static));
    private const string BindingModel = "mono-current-domain";
#else
    private const string BindingModel = "net-default-alc";
#endif
    private const string MainIdentity = "Test, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";
    private const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static void Check(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
    private static string Bool(bool value) => value ? "true" : "false";
    private static string Quote(string value) => value == null ? "null" : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
    private static string Hash(byte[] value) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(value)).Replace("-", "").ToLowerInvariant(); }
    private sealed class Row
    {
        internal string Shape, MainPath, MainHash, FixturePath, FixtureHash;
        internal int N;
        internal Assembly Entry, Fixture;
        internal Assembly[] Before;
    }
    private static void CheckLoadedAssemblies(Row row, Assembly[] assemblies)
    {
        int mains = 0, fixtures = 0;
        foreach (Assembly assembly in assemblies)
        {
#if PORTABLE_MONO
            Check(!assembly.IsDynamic, "Only persisted current-AppDomain assemblies are admitted");
#else
            Check(!assembly.IsDynamic && ReferenceEquals(AssemblyLoadContext.GetLoadContext(assembly), AssemblyLoadContext.Default), "Only persisted Default-context assemblies are admitted");
#endif
            string name = assembly.GetName().Name;
            if (name == "Test")
            {
                mains++; Check(ReferenceEquals(assembly, row.Entry) && assembly.FullName == MainIdentity && Path.GetFullPath(assembly.Location) == row.MainPath, "Single loaded Test is the pinned executing variant");
            }
            if (name.StartsWith("DiscoveryDeclarations", StringComparison.Ordinal))
            {
                fixtures++; Check(row.N != 0 && ReferenceEquals(assembly, row.Fixture) && name == "DiscoveryDeclarations" + row.N && Path.GetFullPath(assembly.Location) == row.FixturePath, "Only the selected shared fixture is loaded");
            }
        }
        Check(mains == 1 && fixtures == (row.N == 0 ? 0 : 1), "Exact main and selected-fixture assembly counts");
    }
    private static Row Prepare(string[] args)
    {
        int n = args[0] == "n0" ? 0 : args[0] == "n1" ? 1 : args[0] == "n32" ? 32 : -1;
        Check(n >= 0 && IntPtr.Size == 8, "Fixed shape and x64 required");
        var row = new Row { Shape = args[0], N = n, MainPath = Path.GetFullPath(args[3]), MainHash = args[4], Entry = Assembly.GetEntryAssembly() };
        Check(row.Entry != null && row.Entry.FullName == MainIdentity && Path.GetFullPath(row.Entry.Location) == row.MainPath, "Exact entry identity and variant path");
#if !PORTABLE_MONO
        Check(ReferenceEquals(AssemblyLoadContext.GetLoadContext(row.Entry), AssemblyLoadContext.Default), "Entry is in Default context");
#endif
        Check(Hash(File.ReadAllBytes(row.MainPath)) == row.MainHash, "Entry bytes match coordinator pin");
        if (n == 0) Check(args[5] == "-" && args[6] == "-", "Empty control has no fixture");
        else
        {
            row.FixturePath = Path.GetFullPath(args[5]); row.FixtureHash = args[6];
            Check(Path.GetFileName(row.FixturePath) == "DiscoveryDeclarations" + n + ".dll", "Exact selected fixture path");
            Check(!File.Exists(Path.Combine(Path.GetDirectoryName(row.FixturePath), "Test.dll")) && !File.Exists(Path.Combine(Path.GetDirectoryName(row.FixturePath), "Test.exe")), "No Test.dll or Test.exe beside shared fixtures");
            Check(Hash(File.ReadAllBytes(row.FixturePath)) == row.FixtureHash, "Shared fixture bytes match coordinator pin");
#if PORTABLE_MONO
            row.Fixture = Assembly.LoadFrom(row.FixturePath);
#else
            row.Fixture = AssemblyLoadContext.Default.LoadFromAssemblyPath(row.FixturePath);
            Check(ReferenceEquals(AssemblyLoadContext.GetLoadContext(row.Fixture), AssemblyLoadContext.Default), "Fixture is in Default context");
#endif
            Check(row.Fixture.FullName == "DiscoveryDeclarations" + n + ", Version=0.0.0.0, Culture=neutral, PublicKeyToken=null", "Exact persisted fixture identity");
            AssemblyName reference = null;
            foreach (AssemblyName name in row.Fixture.GetReferencedAssemblies())
                if (name.Name == "Test") { Check(reference == null, "One Test AssemblyRef"); reference = name; }
            Check(reference != null && reference.FullName == MainIdentity, "Fixture references exact common Test identity");
#if PORTABLE_MONO
            Check(ReferenceEquals(Assembly.Load(reference), row.Entry), "Fixture Test reference resolves to executing variant");
#else
            Check(ReferenceEquals(AssemblyLoadContext.Default.LoadFromAssemblyName(reference), row.Entry), "Fixture Test reference resolves to executing variant");
#endif
        }
        // Prime only measurement/output infrastructure, then retain the loaded assembly set.
        // This intentionally primes framework GetAssemblies machinery equally in both variants.
        _ = Stopwatch.Frequency; _ = Stopwatch.GetTimestamp();
#if PORTABLE_MONO
        _ = Allocated();
#else
        _ = GC.GetAllocatedBytesForCurrentThread();
#endif
        _ = GC.CollectionCount(0); _ = GC.CollectionCount(1); _ = GC.CollectionCount(2); Console.Out.Flush();
        row.Before = AppDomain.CurrentDomain.GetAssemblies(); CheckLoadedAssemblies(row, row.Before);
        return row;
    }
    // Exact body, name and noinline annotation of the naturally audited caller.
    [MethodImpl(MethodImplOptions.NoInlining)] private static int NativeAll() => GameplayTagManager.GetAllTags().Length;

    // All manager reflection, typed attribute metadata and independent registry validation
    // stay behind this post-measurement noinline boundary. No Runtime annotations change.
    private static FieldInfo[] RegistryFields() => typeof(GameplayTagManager).GetFields(StaticFields).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
    private static object Field(string name) => typeof(GameplayTagManager).GetField(name, StaticFields).GetValue(null);
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
            Check(array.Rank == 1, "Only one-dimensional registry arrays"); writer.Write(array.Length);
            foreach (object item in array) Value(writer, item); return;
        }
        if (value is Dictionary<string, int> dictionary)
        {
            var fields = type.GetFields(InstanceFields).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray(); CheckDictionaryFields(fields); writer.Write(fields.Length);
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
        Check(type.IsValueType, "Unsupported registry reference: " + type.FullName);
        var members = type.GetFields(InstanceFields).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray(); writer.Write(members.Length);
        foreach (FieldInfo field in members) { writer.Write(field.Name); Value(writer, field.GetValue(value)); }
    }
    private static void WriteSnapshot(Row row, int observed, string path)
    {
        using (var writer = new BinaryWriter(new FileStream(path, FileMode.CreateNew)))
        {
            writer.Write(row.Shape); writer.Write(row.N); writer.Write(observed);
            FieldInfo[] fields = RegistryFields(); writer.Write(fields.Length);
            foreach (FieldInfo field in fields)
            {
                writer.Write(field.Name); writer.Write(field.FieldType.FullName);
                if (field.Name == "s_InitializationLock") { writer.Write(field.GetValue(null) != null); continue; }
                Value(writer, field.GetValue(null));
            }
        }
    }
    private static string Inventory(Assembly[] assemblies, Assembly entry)
    {
        var rows = new List<string>();
        foreach (Assembly assembly in assemblies.OrderBy(a => a.FullName, StringComparer.Ordinal))
            rows.Add("{\"FullName\":" + Quote(assembly.FullName) + ",\"Location\":" + Quote(assembly.Location) + ",\"Mvid\":" + Quote(assembly.ManifestModule.ModuleVersionId.ToString("D")) +
                ",\"IsDynamic\":" + Bool(assembly.IsDynamic) + ",\"IsDefaultContext\":" +
#if PORTABLE_MONO
                "null" +
#else
                Bool(ReferenceEquals(AssemblyLoadContext.GetLoadContext(assembly), AssemblyLoadContext.Default)) +
#endif
                ",\"IsCurrentDomain\":true,\"IsEntry\":" + Bool(ReferenceEquals(assembly, entry)) + "}");
        return "[" + string.Join(",", rows) + "]";
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ValidateAndSnapshot(Row row, int observed, Exception operationError, string snapshotPath, string inventoryPath,
        out string snapshotHash, out string inventoryHash, out bool bindingPass, out bool registryPass, out bool attributeTypePass, out bool inventoryPass)
    {
        snapshotHash = inventoryHash = ""; bindingPass = registryPass = attributeTypePass = inventoryPass = false;
        Assembly[] after = AppDomain.CurrentDomain.GetAssemblies();
        using (var writer = new StreamWriter(new FileStream(inventoryPath, FileMode.CreateNew)))
            writer.WriteLine("{\"BindingModel\":" + Quote(BindingModel) + ",\"Before\":" + Inventory(row.Before, row.Entry) + ",\"After\":" + Inventory(after, row.Entry) + "}");
        inventoryHash = Hash(File.ReadAllBytes(inventoryPath));
        // Preserve complete physical registry state even if the measured operation failed.
        WriteSnapshot(row, observed, snapshotPath); snapshotHash = Hash(File.ReadAllBytes(snapshotPath));
        CheckLoadedAssemblies(row, row.Before); CheckLoadedAssemblies(row, after); inventoryPass = true;
        Check(Hash(File.ReadAllBytes(row.MainPath)) == row.MainHash && ReferenceEquals(typeof(GameplayTagManager).Assembly, row.Entry), "Executing Runtime identity after operation");
        if (row.N != 0) Check(Hash(File.ReadAllBytes(row.FixturePath)) == row.FixtureHash && !File.Exists(Path.Combine(Path.GetDirectoryName(row.FixturePath), "Test.dll")) && !File.Exists(Path.Combine(Path.GetDirectoryName(row.FixturePath), "Test.exe")), "Shared fixture pin after operation");
        bindingPass = true;
        // Attribute types/constructor metadata are first resolved here, after counters/raw row.
        Type expectedAttributeType = typeof(GameplayTagAttribute); int total = 0, nonempty = 0;
        foreach (Assembly assembly in after)
        {
            int count = 0;
            foreach (CustomAttributeData attribute in assembly.GetCustomAttributesData())
            {
                if (!expectedAttributeType.IsAssignableFrom(attribute.AttributeType)) continue;
                Check(attribute.AttributeType == expectedAttributeType && ReferenceEquals(attribute.AttributeType.Assembly, row.Entry) && ReferenceEquals(assembly, row.Fixture), "Actual attribute type binds to executing Test and selected fixture");
                Check(attribute.ConstructorArguments.Count == 3 && attribute.NamedArguments.Count == 0, "Plain constructor-only declaration");
                string name = (string)attribute.ConstructorArguments[0].Value;
                Check(name == "D" + count.ToString("D2", CultureInfo.InvariantCulture) && (string)attribute.ConstructorArguments[1].Value == "description-" + count.ToString("D2", CultureInfo.InvariantCulture) && Convert.ToInt32(attribute.ConstructorArguments[2].Value, CultureInfo.InvariantCulture) == count % 4, "Exact persisted declaration metadata/order");
                count++;
            }
            if (count != 0) nonempty++; total += count;
        }
        Check(total == row.N && nonempty == (row.N == 0 ? 0 : 1), "Exact declaration and attributed-assembly counts"); attributeTypePass = true;
        Check(operationError == null && observed == row.N && GameplayTagManager.IsInitialized, "Measured first operation succeeded and published");
        var definitions = (GameplayTagDefinition[])Field("s_Definitions"); var tags = (GameplayTag[])Field("s_Tags");
        var parents = (int[])Field("s_Parents"); var ends = (int[])Field("s_SubtreeEnds");
        var hierarchyIndices = (int[])Field("s_HierarchyIndices"); var hierarchyTags = (GameplayTag[])Field("s_HierarchyTags"); var dictionary = (Dictionary<string, int>)Field("s_IndicesByName");
        Check(definitions.Length == row.N + 1 && tags.Length == row.N + 1 && parents.Length == row.N + 1 && ends.Length == row.N + 1 && hierarchyIndices.Length == row.N && hierarchyTags.Length == row.N && dictionary.Count == row.N, "Complete registry array dimensions");
        Check(definitions[0].TagName == null && definitions[0].Description == "" && definitions[0].Label == "None" && definitions[0].Flags == GameplayTagFlags.None && definitions[0].RuntimeIndex == 0 && definitions[0].ParentIndex == 0 && definitions[0].SubtreeEnd == 1 && definitions[0].HierarchyLevel == 0 && definitions[0].HierarchyOffset == 0 && tags[0].RuntimeIndex == 0 && tags[0].Name == null && parents[0] == 0 && ends[0] == 1, "Complete None entry");
        for (int i = 0; i < row.N; i++)
        {
            int id = i + 1; string name = "D" + i.ToString("D2", CultureInfo.InvariantCulture); GameplayTagDefinition definition = definitions[id];
            Check(definition.TagName == name && definition.Label == name && definition.Description == "description-" + i.ToString("D2", CultureInfo.InvariantCulture) && (int)definition.Flags == i % 4 && definition.RuntimeIndex == id && definition.ParentIndex == 0 && definition.SubtreeEnd == id + 1 && definition.HierarchyLevel == 1 && definition.HierarchyOffset == i, "Independent definition/order oracle");
            Check(tags[id].Name == name && tags[id].RuntimeIndex == id && parents[id] == 0 && ends[id] == id + 1 && hierarchyIndices[i] == id && hierarchyTags[i].Name == name && hierarchyTags[i].RuntimeIndex == id && dictionary.TryGetValue(name, out int found) && found == id, "Independent complete topology/name-map oracle");
            Check(GameplayTagManager.RequestTag(name).RuntimeIndex == id && GameplayTagManager.RequestTag(name, out GameplayTag resolved) && resolved.RuntimeIndex == id, "Published public lookup consistency");
        }
        Check(GameplayTagManager.TagCount == row.N && GameplayTagManager.GetAllTags().Length == row.N && GameplayTagManager.RequestTag(null).RuntimeIndex == 0 && GameplayTagManager.RequestTag("Missing").RuntimeIndex == 0, "Published public count/span/None consistency"); registryPass = true;
    }
    private static int Main(string[] args)
    {
        if (args.Length != 7) throw new ArgumentException("Fixed shape, fresh snapshot/inventory, pinned main path/hash and selected fixture path/hash required");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        Row row = Prepare(args); long frequency = Stopwatch.Frequency;
        Exception operationError = null; int observed = -1;
#if PORTABLE_MONO
        long before = Allocated();
#else
        long before = GC.GetAllocatedBytesForCurrentThread();
#endif
        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        long start = Stopwatch.GetTimestamp();
        try { observed = NativeAll(); } catch (Exception error) { operationError = error; }
        long ticks = Stopwatch.GetTimestamp() - start;
#if PORTABLE_MONO
        long bytes = Allocated() - before;
#else
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
#endif
        int delta0 = GC.CollectionCount(0) - gc0, delta1 = GC.CollectionCount(1) - gc1, delta2 = GC.CollectionCount(2) - gc2;
        Console.WriteLine("{\"RecordType\":\"measurement\",\"Shape\":" + Quote(row.Shape) + ",\"DeclarationCount\":" + row.N + ",\"AttributedAssemblyCount\":" + (row.N == 0 ? 0 : 1) + ",\"ObservedCount\":" + observed +
            ",\"Ticks\":" + ticks + ",\"Frequency\":" + frequency + ",\"AllocatedBytes\":" + bytes + ",\"GC0\":" + delta0 + ",\"GC1\":" + delta1 + ",\"GC2\":" + delta2 + ",\"PointerSize\":" + IntPtr.Size +
            ",\"OperationPass\":" + Bool(operationError == null) + ",\"OperationErrorType\":" + Quote(operationError == null ? "" : operationError.GetType().FullName) + ",\"FirstBridgeJitIncluded\":true,\"PreBoundaryInventoryPrimed\":true,\"WholeHeapRetainedBytesMeasured\":false}");
        Console.Out.Flush();
        bool binding = false, registry = false, attributeType = false, inventory = false, pass = operationError == null && observed == row.N;
        string snapshotHash = "", inventoryHash = "", errorType = "";
        try { ValidateAndSnapshot(row, observed, operationError, args[1], args[2], out snapshotHash, out inventoryHash, out binding, out registry, out attributeType, out inventory); }
        catch (Exception error) { pass = false; errorType = error.GetType().FullName; }
        Console.WriteLine("{\"RecordType\":\"semantics\",\"Shape\":" + Quote(row.Shape) + ",\"SemanticsPass\":" + Bool(pass) + ",\"ErrorType\":" + Quote(errorType) + ",\"BindingPass\":" + Bool(binding) + ",\"RegistryPass\":" + Bool(registry) + ",\"PostAttributeTypePass\":" + Bool(attributeType) + ",\"AssemblyInventoryPass\":" + Bool(inventory) + ",\"ObservedCount\":" + observed + ",\"SnapshotSHA256\":" + Quote(snapshotHash) + ",\"InventorySHA256\":" + Quote(inventoryHash) + ",\"AssemblyName\":\"Test\",\"DeclarationCount\":" + row.N + "}");
        Console.Out.Flush(); GC.KeepAlive(row); return pass ? 0 : 1;
    }
}
