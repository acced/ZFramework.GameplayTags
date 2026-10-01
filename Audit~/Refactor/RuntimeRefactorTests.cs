// Independent public-API regression checks for the runtime refactor.
// Compile with Runtime/**/*.cs and Audit~/UnityStubs.cs in a standalone host.
// This is managed correctness evidence, not native Unity/IL2CPP validation.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using GameplayTags;
using UnityEngine;

internal static class RuntimeRefactorTests
{
    private const int Seed = 20261001;
#if GAMEPLAYTAGS_FORCE_PORTABLE
    private const string KernelFlavor = "forced-portable";
#else
    private const string KernelFlavor = "default-host-dispatch";
#endif
    private static long assertions;
    private static int failures;
    private static int pairs;
    private static int sink;
    private static readonly List<object> results = new List<object>();
    private static readonly TagSetStorage[] Modes = { TagSetStorage.Sparse, TagSetStorage.Dense };
    private static readonly TagSetStorage[] AllModes = { TagSetStorage.Auto, TagSetStorage.Sparse, TagSetStorage.Dense };

    private static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        assertions++;
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Test(string name, Action action)
    {
        try { action(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failures++; results.Add(new { name, passed = false, error = error.ToString() }); Console.WriteLine("FAIL " + name + " " + error); }
    }
    private static GameplayTagSettings Settings(IEnumerable<string> names, List<GameplayTagRedirect> redirects = null)
    {
        var definitions = new List<GameplayTagDefinition>();
        foreach (string name in names) definitions.Add(new GameplayTagDefinition(name, "", "Default", false, true));
        var settings = ScriptableObject.CreateInstance<GameplayTagSettings>();
        settings.ReplaceAll(definitions, redirects ?? new List<GameplayTagRedirect>(),
            new List<GameplayTagSource> { new GameplayTagSource("Default", "", false) });
        return settings;
    }
    private static TagRegistry Registry(int count)
    {
        var names = new string[count];
        for (int i = 0; i < count; i++) names[i] = "T" + i.ToString("D6");
        return TagRegistry.Create(Settings(names));
    }
    private static HashSet<int> SetOf(params int[] values) => new HashSet<int>(values);
    private static HashSet<int> Range(int count, int step = 1, int start = 0)
    {
        var result = new HashSet<int>();
        for (int i = start; i < count; i += step) result.Add(i);
        return result;
    }
    private static RuntimeTagSet Set(TagRegistry registry, HashSet<int> members, TagSetStorage mode, int capacity = 0)
    {
        var result = new RuntimeTagSet(registry, capacity, mode);
        var sorted = new List<int>(members); sorted.Sort();
        // Deliberately exercise middle/descending inserts, not only sorted loading.
        for (int i = sorted.Count - 1; i >= 0; i--) Check(result.AddTag(registry.GetTagAt(sorted[i])), "first AddTag false");
        return result;
    }
    private static void Same(RuntimeTagSet actual, HashSet<int> expected, string context)
    {
        Check(actual.Count == expected.Count, context + ": Count");
        Check(actual.IsEmpty == (expected.Count == 0), context + ": IsEmpty");
        int previous = -1, count = 0;
        foreach (RuntimeTag tag in actual)
        {
            Check(ReferenceEquals(tag.Registry, actual.Registry), context + ": handle registry");
            Check(tag.RuntimeIndex > previous && tag.RuntimeIndex < actual.Registry.Count, context + ": ordered/bounded enumeration");
            Check(expected.Contains(tag.RuntimeIndex), context + ": unexpected member " + tag.RuntimeIndex);
            Check(actual.HasTagExact(tag), context + ": enumerated member not found");
            previous = tag.RuntimeIndex; count++;
        }
        Check(count == expected.Count, context + ": missing enumeration member");
        int[] probes = { 0, 1, 31, 32, 63, 64, 65, 127, 128, 255, 256, actual.Registry.Count - 1 };
        foreach (int id in probes) if ((uint)id < (uint)actual.Registry.Count)
            Check(actual.HasTagExact(actual.Registry.GetTagAt(id)) == expected.Contains(id), context + ": membership probe");
    }
    private static void Operate(int operation, RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet output)
    {
        if (operation == 0) RuntimeTagSet.UnionInto(left, right, output);
        else if (operation == 1) RuntimeTagSet.IntersectionExactInto(left, right, output);
        else RuntimeTagSet.DifferenceExactInto(left, right, output);
    }
    private static RuntimeTagSet Factory(int operation, RuntimeTagSet left, RuntimeTagSet right, TagSetStorage storage)
        => operation == 0 ? RuntimeTagSet.Union(left, right, storage)
            : operation == 1 ? RuntimeTagSet.IntersectionExact(left, right, storage)
            : RuntimeTagSet.DifferenceExact(left, right, storage);
    private static HashSet<int> Oracle(int operation, HashSet<int> left, HashSet<int> right)
    {
        var expected = new HashSet<int>(left);
        if (operation == 0) expected.UnionWith(right);
        else if (operation == 1) expected.IntersectWith(right);
        else expected.ExceptWith(right);
        return expected;
    }
    private static void Pair(TagRegistry registry, HashSet<int> a, HashSet<int> b, bool factories = false)
    {
        pairs++;
        foreach (TagSetStorage am in Modes) foreach (TagSetStorage bm in Modes)
        {
            var left = Set(registry, a, am); var right = Set(registry, b, bm);
            Check(left.HasAnyExact(right) == a.Overlaps(b), "HasAnyExact oracle");
            Check(left.HasAllExact(right) == a.IsSupersetOf(b), "HasAllExact oracle");
            Check(left.SetEquals(right) == a.SetEquals(b), "SetEquals oracle");
            for (int operation = 0; operation < 3; operation++)
            {
                HashSet<int> expected = Oracle(operation, a, b);
                foreach (TagSetStorage rm in Modes)
                {
                    // Zero capacity checks growth; stale members check complete replacement.
                    var output = new RuntimeTagSet(registry, 0, rm);
                    Operate(operation, left, right, output); Same(output, expected, "fresh destination " + operation);
                    if (registry.Count != 0) output.AddTag(registry.GetTagAt(registry.Count - 1));
                    Operate(operation, left, right, output); Same(output, expected, "reused destination " + operation);
                    if (factories) Same(Factory(operation, left, right, rm), expected, "factory " + operation);
                }
                var aliasLeft = new RuntimeTagSet(left);
                Operate(operation, aliasLeft, right, aliasLeft); Same(aliasLeft, expected, "left alias " + operation);
                var aliasRight = new RuntimeTagSet(right);
                Operate(operation, left, aliasRight, aliasRight); Same(aliasRight, expected, "right alias " + operation);
                var self = new RuntimeTagSet(left);
                Operate(operation, self, self, self);
                Same(self, operation == 2 ? SetOf() : a, "all alias " + operation);
                var separate = new RuntimeTagSet(registry, 0, am);
                if (registry.Count != 0) separate.AddTag(registry.GetTagAt(registry.Count - 1));
                Operate(operation, left, left, separate);
                Same(separate, operation == 2 ? SetOf() : a, "same inputs " + operation);
                Same(left, a, "left unchanged"); Same(right, b, "right unchanged");
            }
            var removal = new RuntimeTagSet(left);
            Check(removal.RemoveTags(right) == a.Overlaps(b), "RemoveTags return");
            Same(removal, Oracle(2, a, b), "RemoveTags oracle");
            var append = new RuntimeTagSet(left); append.AppendTags(right);
            Same(append, Oracle(0, a, b), "AppendTags oracle");
        }
    }
    private static void DifferenceAndSkew()
    {
        TagRegistry tiny = Registry(5);
        for (int am = 0; am < 32; am++) for (int bm = 0; bm < 32; bm++)
        {
            var a = SetOf(); var b = SetOf();
            for (int i = 0; i < 5; i++) { if ((am & (1 << i)) != 0) a.Add(i); if ((bm & (1 << i)) != 0) b.Add(i); }
            Pair(tiny, a, b);
        }
        foreach (int universe in new[] { 0, 1, 63, 64, 65, 127, 128, 129, 257, 4097 })
        {
            TagRegistry registry = Registry(universe);
            Pair(registry, SetOf(), SetOf(), true);
            if (universe == 0) continue;
            HashSet<int>[] shapes = { Range(universe), Range(universe, 2), Range(universe, 2, 1),
                SetOf(0), SetOf(universe - 1), Range(Math.Min(2, universe)), SetOf() };
            foreach (HashSet<int> a in shapes) foreach (HashSet<int> b in shapes) Pair(registry, a, b, true);
        }
        // Exercise the strict 32:1 dispatch boundary, both skew directions and late matches.
        TagRegistry wide = Registry(4097);
        foreach (int count in new[] { 31, 32, 33, 63, 64, 65, 1024 })
        {
            var large = Range(count * 2, 2);
            foreach (var small in new[] { SetOf(0), SetOf(count * 2 - 2), SetOf(1), SetOf(0, count * 2 - 2), SetOf(1, count * 2 - 1) })
            { Pair(wide, large, small); Pair(wide, small, large); }
        }
        var random = new Random(Seed);
        for (int iteration = 0; iteration < 140; iteration++)
        {
            int universe = new[] { 65, 129, 257, 4097 }[iteration % 4];
            TagRegistry registry = Registry(universe);
            var a = SetOf(); var b = SetOf();
            int divisorA = random.Next(1, 40), divisorB = random.Next(1, 40);
            for (int id = 0; id < universe; id++)
            { if (random.Next(divisorA) == 0) a.Add(id); if (random.Next(divisorB) == 0) b.Add(id); }
            Pair(registry, a, b, iteration % 10 == 0);
        }
    }
    private static void ConversionAndLoading()
    {
        foreach (int universe in new[] { 0, 1, 65, 129, 257 })
        {
            TagRegistry registry = Registry(universe);
            foreach (TagSetStorage mode in AllModes)
            {
                var expected = Range(universe, 3);
                var source = Set(registry, expected, mode, universe);
                foreach (TagSetStorage target in AllModes) foreach (int capacity in new[] { 0, 1, universe, int.MaxValue })
                {
                    var converted = source.ToStorage(target, capacity);
                    Same(converted, expected, "ToStorage");
                    Check(!ReferenceEquals(source, converted), "conversion must be independent object");
                    Check(ReferenceEquals(source.Registry, converted.Registry), "conversion registry");
                    if (target != TagSetStorage.Auto) Check(converted.Storage == target, "explicit target storage");
                    Check(converted.Capacity >= Math.Min(registry.Count, Math.Max(expected.Count, capacity)), "conversion reserve");
                    converted.Clear(); Same(source, expected, "conversion independent buffer");
                }
                var tags = new RuntimeTag[universe * 3 + 5];
                var originals = new RuntimeTag[tags.Length];
                for (int i = 0; i < tags.Length; i++)
                    tags[i] = universe == 0 || i % 4 == 0 ? default : registry.GetTagAt((tags.Length - i) % universe);
                Array.Copy(tags, originals, tags.Length);
                var wanted = SetOf(); foreach (RuntimeTag tag in tags) if (tag.IsValid) wanted.Add(tag.RuntimeIndex);
                var loaded = RuntimeTagSet.FromTags(registry, tags, 0, mode);
                Same(loaded, wanted, "FromTags duplicate/default/length>U");
                for (int i = 0; i < tags.Length; i++) Check(tags[i] == originals[i], "FromTags mutated input span");
                Array.Clear(tags, 0, tags.Length); Same(loaded, wanted, "FromTags owns buffer");
                var reversed = new RuntimeTag[universe];
                for (int i = 0; i < universe; i++) reversed[i] = registry.GetTagAt(universe - i - 1);
                Same(RuntimeTagSet.FromTags(registry, reversed, 0, mode), Range(universe), "FromTags reversed sorting");
                if (universe >= 5)
                {
                    var withDuplicates = new[] { registry.GetTagAt(4), default, registry.GetTagAt(1), registry.GetTagAt(4) };
                    Same(RuntimeTagSet.FromTags(registry, withDuplicates, 0, mode), SetOf(1, 4), "FromTags compact duplicate path");
                    Same(RuntimeTagSet.FromTags(registry, new ReadOnlySpan<RuntimeTag>(reversed, 1, 3), 0, mode),
                        SetOf(universe - 2, universe - 3, universe - 4), "FromTags sliced span");
                }
                Same(RuntimeTagSet.FromTags(registry, ReadOnlySpan<RuntimeTag>.Empty, 0, mode), SetOf(), "FromTags empty span");
            }
        }
        var redirects = new List<GameplayTagRedirect>();
        for (int i = 0; i < 33; i++) redirects.Add(new GameplayTagRedirect("Legacy" + i.ToString("D2"), i % 2 == 0 ? "A.Leaf" : "Z"));
        var aliasRegistry = TagRegistry.Create(Settings(new[] { "A.Leaf", "Z" }, redirects));
        var authoring = new GameplayTagContainer();
        var names = new List<GameplayTag>();
        for (int i = 32; i >= 0; i--) names.Add(new GameplayTag("Legacy" + i.ToString("D2")));
        names.Add(new GameplayTag("A.Leaf")); names.Add(new GameplayTag("Z"));
        typeof(GameplayTagContainer).GetField("m_GameplayTags", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(authoring, names);
        ((ISerializationCallbackReceiver)authoring).OnAfterDeserialize();
        Check(authoring.Count > aliasRegistry.Count, "alias fixture must exceed registry");
        var aliasExpected = SetOf(aliasRegistry.Resolve("A.Leaf").RuntimeIndex, aliasRegistry.Resolve("Z").RuntimeIndex);
        foreach (TagSetStorage mode in AllModes) Same(authoring.ToRuntime(aliasRegistry, 0, mode), aliasExpected, "authoring aliases > registry");
        Check(authoring.Count == 35, "loading mutated authoring container");

        // Lexical authoring order differs from DFS order around punctuation; redirects
        // can also produce duplicate IDs in the fast sort/deduplicate path (length <= U).
        var compactRegistry = TagRegistry.Create(Settings(new[] { "A.B", "A!", "A.B.C", "A.B1", "Z" },
            new List<GameplayTagRedirect> { new GameplayTagRedirect("Old", "Z"), new GameplayTagRedirect("Older", "Z") }));
        var compactAuthoring = new GameplayTagContainer();
        typeof(GameplayTagContainer).GetField("m_GameplayTags", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(compactAuthoring,
            new List<GameplayTag> { new GameplayTag("Older"), new GameplayTag("A.B"), new GameplayTag("Old"), new GameplayTag("A!") });
        ((ISerializationCallbackReceiver)compactAuthoring).OnAfterDeserialize();
        Check(compactAuthoring.Count <= compactRegistry.Count, "compact authoring fixture must hit fast path");
        var compactExpected = SetOf(compactRegistry.Resolve("A.B").RuntimeIndex, compactRegistry.Resolve("A!").RuntimeIndex, compactRegistry.Resolve("Z").RuntimeIndex);
        foreach (TagSetStorage mode in AllModes) Same(compactAuthoring.ToRuntime(compactRegistry, 0, mode), compactExpected, "authoring lexical/DFS mismatch and aliases");
    }
    private static bool IsChild(string child, string parent) => child == parent || child.StartsWith(parent + ".", StringComparison.Ordinal);
    private static void HierarchySweep()
    {
        var names = new List<string> { "A", "A!", "A.B", "A.B.C", "A.B.C.D", "A.B1", "A.Z", "AA", "Z", "状态.叶" };
        for (int i = 0; i < 140; i++) names.Add("A.W.T" + i.ToString("D3"));
        TagRegistry registry = TagRegistry.Create(Settings(names));
        var random = new Random(Seed ^ 0x5757);
        for (int iteration = 0; iteration < 160; iteration++)
        {
            var members = SetOf(); var conditions = SetOf();
            for (int i = 0; i < registry.Count; i++)
            {
                if (random.Next(iteration % 3 == 0 ? 2 : 9) == 0) members.Add(i);
                if (random.Next(iteration % 3 == 0 ? 19 : 3) == 0) conditions.Add(i);
            }
            if (iteration % 4 == 0) { conditions.Add(registry.Resolve("A").RuntimeIndex); conditions.Add(registry.Resolve("A.B").RuntimeIndex); }
            var expected = SetOf();
            foreach (int id in members) foreach (int condition in conditions)
                if (IsChild(registry.GetTagAt(id).Name, registry.GetTagAt(condition).Name)) { expected.Add(id); break; }
            foreach (TagSetStorage am in Modes) foreach (TagSetStorage bm in Modes)
            {
                var source = Set(registry, members, am); var filter = Set(registry, conditions, bm);
                foreach (TagSetStorage rm in Modes)
                {
                    var output = Set(registry, Range(registry.Count), rm);
                    source.FilterInto(filter, output); Same(output, expected, "hierarchy separate string oracle");
                }
                var alias = new RuntimeTagSet(source); alias.FilterInto(filter, alias);
                Same(alias, expected, "hierarchy alias string oracle");
                Same(source, members, "hierarchy source untouched"); Same(filter, conditions, "hierarchy conditions untouched");
                Throws<ArgumentException>(() => source.FilterInto(filter, filter));
            }
        }
    }
    private static void AdmissionAndExceptions()
    {
        var registry = Registry(65); var foreignRegistry = Registry(65);
        var a = Set(registry, SetOf(0, 64), TagSetStorage.Sparse);
        var b = Set(registry, SetOf(1, 64), TagSetStorage.Dense);
        var foreign = Set(foreignRegistry, SetOf(0), TagSetStorage.Sparse);
        var output = Set(registry, SetOf(32), TagSetStorage.Sparse);
        Throws<ArgumentNullException>(() => RuntimeTagSet.DifferenceExact(null, b));
        Throws<ArgumentNullException>(() => RuntimeTagSet.DifferenceExact(a, null));
        Throws<ArgumentNullException>(() => RuntimeTagSet.DifferenceExactInto(a, b, null));
        Throws<ArgumentException>(() => RuntimeTagSet.DifferenceExact(a, foreign));
        Throws<ArgumentException>(() => RuntimeTagSet.DifferenceExactInto(a, foreign, output));
        Throws<ArgumentException>(() => RuntimeTagSet.DifferenceExactInto(a, b, foreign));
        Throws<ArgumentOutOfRangeException>(() => RuntimeTagSet.DifferenceExact(a, b, (TagSetStorage)99));
        Throws<ArgumentOutOfRangeException>(() => a.ToStorage(TagSetStorage.Sparse, -1));
        Throws<ArgumentOutOfRangeException>(() => a.ToStorage((TagSetStorage)99));
        foreach (TagSetStorage mode in AllModes)
        {
            Throws<ArgumentException>(() => RuntimeTagSet.FromTags(registry, new[] { registry.GetTagAt(0), foreignRegistry.GetTagAt(1) }, 0, mode));
            var longInput = new RuntimeTag[99]; longInput[98] = foreignRegistry.GetTagAt(1);
            Throws<ArgumentException>(() => RuntimeTagSet.FromTags(registry, longInput, 0, mode));
        }
        Throws<ArgumentNullException>(() => RuntimeTagSet.FromTags(null, Array.Empty<RuntimeTag>()));
        Throws<ArgumentOutOfRangeException>(() => RuntimeTagSet.FromTags(registry, Array.Empty<RuntimeTag>(), -1));
        Throws<ArgumentOutOfRangeException>(() => RuntimeTagSet.FromTags(registry, Array.Empty<RuntimeTag>(), 0, (TagSetStorage)99));
        Same(a, SetOf(0, 64), "exception left unchanged"); Same(b, SetOf(1, 64), "exception right unchanged");
        Same(output, SetOf(32), "exception destination unchanged"); Same(foreign, SetOf(0), "exception foreign unchanged");
    }
    private static void RegistryPrefixReuse()
    {
        var definitions = new List<string> { "A", "A!", "A!B.Leaf", "A.B", "A.B.C", "A.B.C.D", "A.B1", "A.B1.Leaf", "AA.B.C", "Z", "状态.叶" };
        for (int i = 0; i < 90; i++) definitions.Add("Group" + (i % 7) + ".Branch" + (i % 13) + ".Leaf" + i);
        var expectedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (string definition in definitions)
        {
            string prefix = definition;
            while (true)
            {
                expectedNames.Add(prefix);
                int dot = prefix.LastIndexOf('.');
                if (dot < 0) break;
                prefix = prefix.Substring(0, dot);
            }
        }
        var baseline = TagRegistry.Create(Settings(definitions));
        var random = new Random(Seed ^ 0x12345);
        for (int iteration = 0; iteration < 24; iteration++)
        {
            for (int i = definitions.Count - 1; i > 0; i--)
            { int j = random.Next(i + 1); string value = definitions[i]; definitions[i] = definitions[j]; definitions[j] = value; }
            var registry = TagRegistry.Create(Settings(definitions));
            Check(registry.Count == expectedNames.Count, "shuffled registry canonical count");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < registry.Count; i++)
            {
                RuntimeTag child = registry.GetTagAt(i);
                Check(seen.Add(child.Name), "shuffled registry duplicate name");
                Check(child.Name == baseline.GetTagAt(i).Name, "shuffled definitions changed canonical DFS index");
                int dot = child.Name.LastIndexOf('.');
                string parent = dot < 0 ? string.Empty : child.Name.Substring(0, dot);
                Check(child.GetDirectParent().Name == parent, "shuffled registry direct parent");
                for (int j = 0; j < registry.Count; j++)
                {
                    RuntimeTag ancestor = registry.GetTagAt(j);
                    Check(child.MatchesTag(ancestor) == IsChild(child.Name, ancestor.Name), "shuffled registry interval ancestry");
                }
            }
            Check(seen.SetEquals(expectedNames), "shuffled registry names");
        }
    }
    private static void ReservedAllocationContract()
    {
        TagRegistry registry = Registry(4097);
        foreach (TagSetStorage am in Modes) foreach (TagSetStorage bm in Modes) foreach (TagSetStorage rm in Modes)
        {
            var a = Set(registry, Range(4097, 2), am, registry.Count);
            var b = Set(registry, SetOf(1, 64, 2048, 4096), bm, registry.Count);
            var output = new RuntimeTagSet(registry, registry.Count, rm);
            Action action = () =>
            {
                RuntimeTagSet.DifferenceExactInto(a, b, output);
                RuntimeTagSet.IntersectionExactInto(a, b, output);
                RuntimeTagSet.UnionInto(a, b, output);
                output.CopyFrom(a); RuntimeTagSet.DifferenceExactInto(output, b, output);
                sink ^= output.Count;
            };
            for (int i = 0; i < 100; i++) action();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 250; i++) action();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated == 0, "reserved operation allocation " + am + "/" + bm + "/" + rm + " = " + allocated);
        }
        // Right-operand aliasing currently preserves a temporary except for dense/dense.
        // This tests the disclosed boundary instead of silently asserting every Into is zero-GC.
        foreach (TagSetStorage am in Modes) foreach (TagSetStorage bm in Modes)
        {
            var a = Set(registry, SetOf(0, 64, 4096), am, registry.Count);
            var original = Set(registry, SetOf(64), bm, registry.Count);
            var right = new RuntimeTagSet(original);
            Action action = () => { right.CopyFrom(original); RuntimeTagSet.DifferenceExactInto(a, right, right); sink ^= right.Count; };
            for (int i = 0; i < 100; i++) action();
            long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 50; i++) action();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(am == TagSetStorage.Dense && bm == TagSetStorage.Dense ? allocated == 0 : allocated > 0, "right alias allocation boundary");
            Same(right, SetOf(0, 4096), "right alias allocation case membership");
        }
    }
    private static int Main(string[] args)
    {
        Test("independent-exhaustive-random-skew-word-boundary-operators", DifferenceAndSkew);
        Test("explicit-conversion-resolved-loading-and-authoring-aliases", ConversionAndLoading);
        Test("hierarchy-sweep-nested-intervals-and-aliasing", HierarchySweep);
        Test("registry-null-invalid-mode-and-failure-isolation", AdmissionAndExceptions);
        Test("shuffled-definitions-prefix-reuse-and-canonical-dfs", RegistryPrefixReuse);
        Test("reserved-zero-allocation-and-right-alias-boundary", ReservedAllocationContract);
        var report = new { seed = Seed, assertions, pairs, failures, kernelFlavor = KernelFlavor, runtime = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(), sink, results,
            native_unity_executed = false, note = "Independent HashSet and string-prefix oracles with explicit Unity API facades. Does not validate native Unity, IL2CPP, or ARM64." };
        if (args.Length != 0)
        { Directory.CreateDirectory(args[0]); File.WriteAllText(Path.Combine(args[0], "runtime-refactor-tests.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); }
        Console.WriteLine("RUNTIME REFACTOR TESTS assertions=" + assertions + " pairs=" + pairs + " failures=" + failures);
        return failures == 0 ? 0 : 1;
    }
}
