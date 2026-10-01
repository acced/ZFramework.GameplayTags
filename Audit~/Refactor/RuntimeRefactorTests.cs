// Independent public-API regression checks for the runtime refactor.
// Compile with Runtime/**/*.cs and Audit~/UnityStubs.cs in a standalone host.
// This is managed correctness evidence, not native Unity/IL2CPP validation.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GameplayTags;
using UnityEngine;

internal static class RuntimeRefactorTests
{
    private const int Seed = 20261001;
#if GAMEPLAYTAGS_EXPECT_PORTABLE_RUNTIME
    private const string KernelFlavor = "referenced-portable-runtime";
#elif GAMEPLAYTAGS_FORCE_PORTABLE
    private const string KernelFlavor = "forced-portable";
#else
    private const string KernelFlavor = "default-host-dispatch";
#endif
    private static long assertions;
    private static int failures;
    private static int pairs;
    private static int sink;
    private static readonly List<object> results = new List<object>();
    private static readonly TagSetStorage[] Modes = { TagSetStorage.Sparse, TagSetStorage.Dense, TagSetStorage.Compressed };
    private static readonly TagSetStorage[] AllModes = { TagSetStorage.Auto, TagSetStorage.Sparse, TagSetStorage.Dense, TagSetStorage.Compressed };

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
    private static RuntimeTagSet Set(TagRegistry registry, HashSet<int> members, TagSetStorage mode, int capacity = 0, bool ordered = false)
    {
        var sorted = new List<int>(members); sorted.Sort();
        if (ordered)
        {
            var tags = new RuntimeTag[sorted.Count];
            for (int i = 0; i < tags.Length; i++) tags[i] = registry.GetTagAt(sorted[i]);
            return RuntimeTagSet.FromTags(registry, tags, capacity, mode);
        }
        var result = new RuntimeTagSet(registry, capacity, mode);
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
        int[] probes = { 0, 1, 14, 15, 16, 17, 30, 31, 32, 63, 64, 65, 127, 128, 255, 256, actual.Registry.Count - 1 };
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
    private static void Pair(TagRegistry registry, HashSet<int> a, HashSet<int> b, bool factories = false, bool ordered = false)
    {
        pairs++;
        foreach (TagSetStorage am in Modes) foreach (TagSetStorage bm in Modes)
        {
            var left = Set(registry, a, am, ordered: ordered); var right = Set(registry, b, bm, ordered: ordered);
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
        // A same-record bit and a wholly new packed record both reuse reserved storage.
        foreach (TagSetStorage mode in Modes)
        {
            var initial = SetOf(0, 64, 128);
            var mutable = Set(registry, initial, mode, registry.Count);
            RuntimeTag sameRecord = registry.GetTagAt(15), newRecord = registry.GetTagAt(1024);
            Action mutate = () => {
                Check(mutable.AddTag(sameRecord), "prepared add same-record bit");
                Check(mutable.AddTag(newRecord), "prepared add new record");
                Check(mutable.RemoveTag(sameRecord), "prepared remove same-record bit");
                Check(mutable.RemoveTag(newRecord), "prepared remove whole record");
            };
            for (int i = 0; i < 40; i++) mutate();
            long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 250; i++) mutate();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated == 0, "prepared mutation allocation " + mode + " bytes=" + allocated);
            Same(mutable, initial, "prepared mutation restores exact membership"); PackedBufferInvariant(mutable);
        }
        // Right-output aliasing is zero-allocation with dense output or sufficient
        // sparse capacity for the union. Sparse preservation must use that one buffer.
        foreach (TagSetStorage am in Modes) foreach (TagSetStorage bm in Modes)
        {
            var a = Set(registry, SetOf(0, 64, 4096), am, registry.Count);
            var original = Set(registry, SetOf(64), bm, registry.Count);
            var right = new RuntimeTagSet(original);
            Action action = () => { right.CopyFrom(original); RuntimeTagSet.DifferenceExactInto(a, right, right); sink ^= right.Count; };
            for (int i = 0; i < 100; i++) action();
            long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 50; i++) action();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated == 0, "registry-reserved right alias allocation");
            Same(right, SetOf(0, 4096), "right alias allocation case membership");
        }
    }
    private static void RightAliasPreservation()
    {
        TagRegistry registry = Registry(4097);
        HashSet<int>[] shapes = { SetOf(), SetOf(0), SetOf(4096), SetOf(0, 64, 4096),
            SetOf(1, 63, 64, 65, 127, 128, 4095, 4096), Range(4097), Range(4097, 2), Range(4097, 2, 1),
            Range(1536, 3), Range(3072, 3, 1536) };
        foreach (HashSet<int> aMembers in shapes) foreach (HashSet<int> bMembers in shapes)
        foreach (TagSetStorage am in Modes) foreach (TagSetStorage bm in Modes)
        {
            var expected = Oracle(2, aMembers, bMembers);
            var union = Oracle(0, aMembers, bMembers);
            var a = Set(registry, aMembers, am);
            var original = Set(registry, bMembers, bm, bMembers.Count);
            var right = Set(registry, bMembers, bm, union.Count);
            int reservedCapacity = right.Capacity;
            Check(bm != TagSetStorage.Sparse || reservedCapacity == union.Count, "exact-union sparse capacity fixture");
            Action action = () => { right.CopyFrom(original); RuntimeTagSet.DifferenceExactInto(a, right, right); sink ^= right.Count; };
            for (int i = 0; i < 20; i++) action();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 40; i++) action();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated == 0, "exact-union right-alias allocation " + am + "/" + bm
                + " A=" + aMembers.Count + " B=" + bMembers.Count + " union=" + union.Count + " capacity=" + right.Capacity + " bytes=" + allocated);
            Check(right.Capacity == reservedCapacity, "reserved right alias unexpectedly resized");
            Same(right, expected, "exact-union right alias");
            Same(a, aMembers, "right alias source unchanged"); Same(original, bMembers, "right alias reset source unchanged");

            // The first insufficient-capacity call may grow. Its result must still be exact,
            // must not exceed the registry bound, and the warmed repetition must allocate zero.
            var tight = Set(registry, bMembers, bm, bMembers.Count);
            int beforeCapacity = tight.Capacity;
            RuntimeTagSet.DifferenceExactInto(a, tight, tight);
            Same(tight, expected, "tight right alias");
            Check(tight.Capacity >= expected.Count && tight.Capacity <= registry.Count, "tight right alias capacity bounds");
            if (bm == TagSetStorage.Dense) Check(tight.Capacity == beforeCapacity, "dense alias must not resize");
            Action repeat = () => { tight.CopyFrom(original); RuntimeTagSet.DifferenceExactInto(a, tight, tight); sink ^= tight.Count; };
            for (int i = 0; i < 20; i++) repeat();
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 40; i++) repeat();
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated == 0, "warmed tight right alias allocation " + am + "/" + bm + " bytes=" + allocated);
            Same(tight, expected, "warmed tight right alias");
        }
        // Independent/left-alias outputs keep the actual-result capacity guarantee.
        foreach (TagSetStorage am in Modes) foreach (TagSetStorage bm in Modes)
        {
            var a = Set(registry, Range(4097), am);
            var b = Set(registry, Range(4096), bm);
            var output = new RuntimeTagSet(registry, 1, TagSetStorage.Sparse);
            RuntimeTagSet.DifferenceExactInto(a, b, output);
            Check(output.Capacity == 1, "independent difference must not reserve the union");
            Same(output, SetOf(4096), "one-member independent difference");
            var empty = new RuntimeTagSet(registry, 0, TagSetStorage.Sparse);
            RuntimeTagSet.DifferenceExactInto(a, a, empty);
            Check(empty.Capacity == 0 && empty.Count == 0, "empty independent difference capacity");
            var aliasLeft = new RuntimeTagSet(a); int capacity = aliasLeft.Capacity; long bufferBytes = aliasLeft.BufferBytes;
            RuntimeTagSet.DifferenceExactInto(aliasLeft, b, aliasLeft);
            Check(am == TagSetStorage.Compressed ? aliasLeft.BufferBytes == bufferBytes : aliasLeft.Capacity == capacity, "left-alias difference must not resize");
            Same(aliasLeft, SetOf(4096), "left-alias one-member difference");
        }
    }
    // Test-only numeric fixture: bypass authoring/registry construction to isolate packed-ID
    // boundaries without allocating one million names. It models flat disjoint subtrees.
    // This is NOT evidence that a million-name real registry was built or validated.
    private static TagRegistry SyntheticNumericRegistry(int count)
    {
        var result = (TagRegistry)RuntimeHelpers.GetUninitializedObject(typeof(TagRegistry));
        var parents = new int[count]; var ends = new int[count];
        for (int i = 0; i < count; i++) { parents[i] = -1; ends[i] = i + 1; }
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(TagRegistry).GetField("m_Names", fields).SetValue(result, new string[count]);
        typeof(TagRegistry).GetField("m_Indices", fields).SetValue(result, new Dictionary<string, int>());
        typeof(TagRegistry).GetField("m_AuthoringTags", fields).SetValue(result, Array.Empty<GameplayTag>());
        typeof(TagRegistry).GetField("Parents", fields).SetValue(result, parents);
        typeof(TagRegistry).GetField("Ends", fields).SetValue(result, ends);
        return result;
    }
    private static RuntimeTag[] Handles(TagRegistry registry, HashSet<int> ids)
    {
        var tags = new RuntimeTag[ids.Count]; int index = 0;
        foreach (int id in ids) tags[index++] = registry.GetTagAt(id);
        return tags;
    }
    private static void PackedBufferInvariant(RuntimeTagSet value)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var ids = (int[])typeof(RuntimeTagSet).GetField("m_Ids", fields).GetValue(value);
        var words = (ulong[])typeof(RuntimeTagSet).GetField("m_Words", fields).GetValue(value);
        Check((ids == null) != (words == null), "exactly one member buffer must be live");
        if (value.Storage != TagSetStorage.Compressed) return;
        Check(ids != null && words == null, "compressed must own int records only");
        Check(value.RecordCapacity == ids.Length && value.BufferBytes == 4L * ids.Length, "compressed physical capacity");
        var blocks = new HashSet<int>(); foreach (RuntimeTag tag in value) blocks.Add(tag.RuntimeIndex >> 4);
        Check(value.RecordCount == blocks.Count && value.RecordCount <= value.RecordCapacity, "compressed distinct record count");
        int previous = -1;
        for (int i = 0; i < value.RecordCount; i++)
        {
            int record = ids[i];
            int block = (int)((uint)(record ^ int.MinValue) >> 16);
            Check(block > previous && (record & 65535) != 0 && blocks.Contains(block), "biased packed key/order/nonzero mask");
            previous = block;
        }
    }
    private static void PackedBoundariesAndFormatLimit()
    {
        foreach (int universe in new[] { 15, 16, 17, 31, 32, 33, 255, 256, 257 })
        {
            var registry = Registry(universe);
            Pair(registry, Range(universe), Range(universe, 2), true);
            Pair(registry, Range(universe, 16), Range(universe, 16, 15), true);
        }
        var high = SyntheticNumericRegistry(1 << 20);
        var edge = SetOf();
        foreach (int origin in new[] { 0, 16, 64, (1 << 19) - 16, 1 << 19, (1 << 20) - 32 })
            for (int offset = 0; offset < 32 && origin + offset < high.Count; offset++) edge.Add(origin + offset);
        var even = SetOf(); var odd = SetOf(); foreach (int id in edge) { if ((id & 1) == 0) even.Add(id); else odd.Add(id); }
        Pair(high, edge, even, true); Pair(high, even, odd, true); Pair(high, odd, even, true);
        var compressed = Set(high, edge, TagSetStorage.Compressed);
        PackedBufferInvariant(compressed);
        var copied = new RuntimeTagSet(compressed); Same(copied, edge, "packed high-ID physical copy");
        Check(copied.RecordCapacity == compressed.RecordCapacity, "copy preserves packed capacity");
        PackedBufferInvariant(copied); copied.Clear(); Same(compressed, edge, "packed copy independent");
        var descending = new List<int>(edge); descending.Sort(); descending.Reverse();
        foreach (int id in descending)
        {
            Check(compressed.RemoveTag(high.GetTagAt(id)), "remove packed bit/record");
            Check(!compressed.RemoveTag(high.GetTagAt(id)), "duplicate packed removal");
            Check(!compressed.HasTagExact(high.GetTagAt(id)), "removed packed high ID absent");
            PackedBufferInvariant(compressed);
        }
        Same(compressed, SetOf(), "packed remove every bit");
        foreach (int id in descending) { Check(compressed.AddTag(high.GetTagAt(id)), "reinsert packed high ID"); Check(!compressed.AddTag(high.GetTagAt(id)), "duplicate packed insert"); }
        Same(compressed, edge, "packed reuse after complete deletion"); PackedBufferInvariant(compressed);
        Check(!compressed.HasTagExact(default) && !compressed.HasTag(default), "packed default queries");
        for (int id = (1 << 19) - 32; id < (1 << 19) + 48; id++)
            Check(compressed.HasTag(high.GetTagAt(id)) == edge.Contains(id), "flat synthetic hierarchy across biased sign boundary");

        var over = SyntheticNumericRegistry((1 << 20) + 1);
        Throws<ArgumentOutOfRangeException>(() => new RuntimeTagSet(over, 0, TagSetStorage.Compressed));
        var largeSparse = Set(over, SetOf(0, 1 << 19, 1 << 20), TagSetStorage.Sparse);
        Throws<ArgumentOutOfRangeException>(() => largeSparse.ToStorage(TagSetStorage.Compressed));
        Throws<ArgumentOutOfRangeException>(() => RuntimeTagSet.FromTags(over, Handles(over, SetOf(0)), 0, TagSetStorage.Compressed));
        Throws<ArgumentOutOfRangeException>(() => RuntimeTagSet.Union(largeSparse, largeSparse, TagSetStorage.Compressed));
        Same(largeSparse, SetOf(0, 1 << 19, 1 << 20), "format-limit failure leaves source intact");
    }
    private static void BulkPreparationPolicyAndOwnership()
    {
        var registry = Registry(4097);
        foreach (HashSet<int> members in new[] { SetOf(), Range(63), Range(64), Range(128), Range(4097, 32) })
        foreach (int reservation in new[] { 0, 1, 33, 4097, int.MaxValue })
        {
            RuntimeTag[] tags = Handles(registry, members); Array.Reverse(tags);
            var result = RuntimeTagSet.FromTagsForBulk(registry, tags, reservation);
            Same(result, members, "bulk preparation exact members"); PackedBufferInvariant(result);
            Check(result.ReservedMemberCapacity >= Math.Min(reservation, registry.Count), "bulk reservation contract");
            Array.Clear(tags, 0, tags.Length); Same(result, members, "bulk factory owns its input");
            var copy = new RuntimeTagSet(result); copy.Clear(); Same(result, members, "bulk copy owns buffer");
            if (members.Count == 64 || members.Count == 128) Check(result.Storage == TagSetStorage.Compressed, "localized bulk chooses compressed");
        }
        var aliases = new RuntimeTag[150];
        for (int i = 0; i < aliases.Length; i++) aliases[i] = i % 5 == 0 ? default : registry.GetTagAt(i % 9);
        Same(RuntimeTagSet.FromTagsForBulk(registry, aliases), Range(9), "bulk duplicate/default handles");
        var foreign = Registry(3); aliases[149] = foreign.GetTagAt(0);
        Throws<ArgumentException>(() => RuntimeTagSet.FromTagsForBulk(registry, aliases));
        Throws<ArgumentNullException>(() => RuntimeTagSet.FromTagsForBulk(null, aliases));
        Throws<ArgumentOutOfRangeException>(() => RuntimeTagSet.FromTagsForBulk(registry, Array.Empty<RuntimeTag>(), -1));
        foreach (TagSetStorage am in Modes) foreach (TagSetStorage bm in Modes)
        {
            var a = Set(registry, Range(128), am); var b = Set(registry, Range(256, 1, 64), bm);
            var result = RuntimeTagSet.UnionForBulk(a, b); Same(result, Range(256), "bulk union mixed storage");
            PackedBufferInvariant(result); result.Clear(); Same(a, Range(128), "bulk union left independent"); Same(b, Range(256, 1, 64), "bulk union right independent");
            Same(RuntimeTagSet.UnionForBulk(a, a), Range(128), "bulk union self");
        }
        var sparse = Set(registry, SetOf(0), TagSetStorage.Sparse);
        Throws<ArgumentNullException>(() => RuntimeTagSet.UnionForBulk(null, sparse));
        Throws<ArgumentNullException>(() => RuntimeTagSet.UnionForBulk(sparse, null));
        Throws<ArgumentException>(() => RuntimeTagSet.UnionForBulk(sparse, Set(foreign, SetOf(0), TagSetStorage.Sparse)));
        var over = SyntheticNumericRegistry((1 << 20) + 1);
        foreach (HashSet<int> members in new[] { Range(128, 1, 64), Range(10000), SetOf((1 << 20) - 1, 1 << 20) })
        {
            var bulk = RuntimeTagSet.FromTagsForBulk(over, Handles(over, members));
            Check(bulk.Storage != TagSetStorage.Compressed, "bulk must fall back beyond packed format limit");
            Same(bulk, members, "bulk above-format fallback preserves high IDs");
            Same(RuntimeTagSet.UnionForBulk(bulk, bulk), members, "bulk above-format union fallback");
        }
        foreach (int capacity in new[] { 0, 1, 8, 128, 4097 })
        {
            var ordinary = new RuntimeTagSet(registry, capacity, TagSetStorage.Auto);
            TagSetStorage expected = 8L * ((registry.Count + 63) / 64) <= 4L * capacity ? TagSetStorage.Dense : TagSetStorage.Sparse;
            Check(ordinary.Storage == expected, "existing Auto construction memory policy must remain explicit");
        }
    }
    private static void OrderedBulkValidationAndReservations()
    {
        var registry = Registry(4097); var foreign = Registry(2);
        foreach (int count in new[] { 0, 1, 63, 64, 65, 128 }) foreach (int step in new[] { 1, 32 })
        {
            var expected = SetOf(); var values = new List<RuntimeTag>();
            // Padding forces the ordered preparation path even with fewer than 64 unique IDs.
            for (int i = 0; i < 70; i++) values.Add(default);
            for (int i = 0; i < count; i++)
            {
                int id = i * step; expected.Add(id);
                values.Add(registry.GetTagAt(id)); values.Add(default); values.Add(registry.GetTagAt(id));
            }
            RuntimeTag[] tags = values.ToArray(); var original = (RuntimeTag[])tags.Clone();
            int records = count == 0 ? 0 : step == 1 ? (count + 15) / 16 : count;
            foreach (int capacity in new[] { 0, 1, Math.Max(0, records - 1), records, records + 1, Math.Max(0, count - 1), count, registry.Count, int.MaxValue })
            {
                var result = RuntimeTagSet.FromTagsForBulk(registry, tags, capacity);
                Same(result, expected, "ordered bulk defaults/duplicates exact membership"); PackedBufferInvariant(result);
                Check(result.ReservedMemberCapacity >= Math.Min(capacity, registry.Count), "ordered bulk arbitrary-member reservation");
                if (count >= 64 && step == 1) Check(result.Storage == TagSetStorage.Compressed, "ordered clustered bulk layout");
                if (count >= 64 && step == 32) Check(result.Storage == TagSetStorage.Dense, "ordered scattered bulk layout");
                for (int j = 0; j < tags.Length; j++) Check(tags[j] == original[j], "ordered bulk input untouched");
                result.Clear(); Same(RuntimeTagSet.FromTagsForBulk(registry, tags, capacity), expected, "ordered bulk independent reconstruction");
            }
            Same(RuntimeTagSet.FromTagsForBulk(registry, new ReadOnlySpan<RuntimeTag>(tags, 1, tags.Length - 1)), expected, "ordered sliced bulk span");
            var bad = new RuntimeTag[tags.Length + 1]; Array.Copy(tags, bad, tags.Length); bad[bad.Length - 1] = foreign.GetTagAt(1);
            Throws<ArgumentException>(() => RuntimeTagSet.FromTagsForBulk(registry, bad));
            if (count > 1)
            {
                // An early descending pair makes the loader fall back; a later foreign handle
                // still must be rejected by the complete loading pass.
                bad[0] = registry.GetTagAt(3); bad[1] = registry.GetTagAt(2);
                Throws<ArgumentException>(() => RuntimeTagSet.FromTagsForBulk(registry, bad));
            }
            Throws<ArgumentOutOfRangeException>(() => RuntimeTagSet.FromTagsForBulk(registry, tags, -1));
        }
    }
    private static void LongBulkAdmissionAndFallback()
    {
        var foreign = Registry(2);
        int backendWidth = 1;
#if NET8_0_OR_GREATER && !GAMEPLAYTAGS_FORCE_PORTABLE && !GAMEPLAYTAGS_EXPECT_PORTABLE_RUNTIME
        if (System.Runtime.Intrinsics.X86.Avx2.IsSupported) backendWidth = 4;
        else if (System.Runtime.Intrinsics.Arm.AdvSimd.Arm64.IsSupported) backendWidth = 2;
#endif
        foreach (int universe in new[] { 0, 1, 65, 4097 })
        {
            var registry = Registry(universe);
            foreach (int count in new[] { 0, 1, 63, 64, 65, 128 })
            {
                if (count > universe) continue;
                foreach (bool scattered in new[] { false, true }) foreach (bool reverse in new[] { false, true })
                {
                    var members = SetOf(); var tags = new RuntimeTag[Math.Max(8193, universe + 4096)];
                    int stride = scattered ? Math.Max(1, universe / Math.Max(1, count)) : 1;
                    for (int i = 0; i < count; i++)
                    {
                        int id = i * stride; members.Add(id);
                        // Long default padding plus repeated real handles forces actual-count
                        // admission; raw length is deliberately larger than the entire registry.
                        tags[i * 4 + 1] = registry.GetTagAt(id); tags[i * 4 + 2] = registry.GetTagAt(id);
                    }
                    if (reverse) Array.Reverse(tags);
                    int records = OccupiedRecords(members), words = (universe + 63) / 64;
                    TagSetStorage expected = count < 64 ? TagSetStorage.Sparse
                        : records * 4 <= count ? (words <= backendWidth * records ? TagSetStorage.Dense : TagSetStorage.Compressed)
                        : words <= 2 * count ? TagSetStorage.Dense : TagSetStorage.Sparse;
                    foreach (int reservation in new[] { 0, 1, count, universe, int.MaxValue })
                    {
                        var result = RuntimeTagSet.FromTagsForBulk(registry, tags, reservation);
                        Same(result, members, "long bulk default/duplicate actual membership"); PackedBufferInvariant(result);
                        Check(result.Storage == expected, "long bulk raw input length must not change exact selector");
                        Check(result.ReservedMemberCapacity >= Math.Min(reservation, universe), "long bulk fallback preserves requested reservation");
                        var copy = new RuntimeTagSet(result); result.Clear(); Same(copy, members, "long bulk independent copy");
                    }
                    // A foreign handle after all valid/default/duplicate entries must still
                    // fail, including zero-member and early-disorder fallback inputs.
                    tags[tags.Length - 1] = foreign.GetTagAt(1);
                    Throws<ArgumentException>(() => RuntimeTagSet.FromTagsForBulk(registry, tags));
                    if (universe > 1)
                    {
                        tags[0] = registry.GetTagAt(1); tags[1] = registry.GetTagAt(0);
                        Throws<ArgumentException>(() => RuntimeTagSet.FromTagsForBulk(registry, tags));
                    }
                    Throws<ArgumentOutOfRangeException>(() => RuntimeTagSet.FromTagsForBulk(registry, tags, -1));
                }
            }
        }
        // At U=2^20+1 this raw length also reaches the narrow-dense speculation
        // condition for a scalar backend. Actual members must still enforce format limits.
        var over = SyntheticNumericRegistry((1 << 20) + 1);
        foreach (int count in new[] { 64, 10000 })
        {
            var members = Range(over.Count, 1, over.Count - count);
            var tags = new RuntimeTag[262160]; int write = 0;
            foreach (int id in members) { tags[write++] = over.GetTagAt(id); tags[write++] = over.GetTagAt(id); }
            foreach (bool reverse in new[] { false, true })
            {
                if (reverse) Array.Reverse(tags);
                var result = RuntimeTagSet.FromTagsForBulk(over, tags, 65);
                Same(result, members, "long above-format bulk actual membership"); PackedBufferInvariant(result);
                Check(result.Storage == (count == 64 ? TagSetStorage.Sparse : TagSetStorage.Dense), "long above-format bulk exact fallback");
                Check(result.ReservedMemberCapacity >= 65, "long above-format fallback reservation");
            }
            tags[tags.Length - 1] = foreign.GetTagAt(1);
            Throws<ArgumentException>(() => RuntimeTagSet.FromTagsForBulk(over, tags));
        }
    }
    private static object MemberBuffer(RuntimeTagSet value)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        return typeof(RuntimeTagSet).GetField("m_Ids", fields).GetValue(value)
            ?? typeof(RuntimeTagSet).GetField("m_Words", fields).GetValue(value);
    }
    private static void CheckBulkConversion(TagRegistry registry, HashSet<int> members, TagSetStorage initial)
    {
        var source = Set(registry, members, initial, ordered: true);
        object buffer = MemberBuffer(source); int capacityBefore = source.Capacity;
        int recordsBefore = source.RecordCount; long bytesBefore = source.BufferBytes;
        TagSetStorage selected = source.SelectBulkStorage();
        if (members.Count < 64) Check(selected == source.Storage, "tiny existing bulk selector retains layout");
        if (registry.Count > (1 << 20)) Check(selected != TagSetStorage.Compressed, "bulk selector respects packed format limit");
        for (int i = 0; i < 12; i++) sink = (int)source.SelectBulkStorage();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 64; i++) sink = (int)source.SelectBulkStorage();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, "bulk selector must allocate zero bytes: " + allocated);
        Same(source, members, "selector preserves exact members");
        Check(source.Storage == initial && ReferenceEquals(buffer, MemberBuffer(source)) && source.Capacity == capacityBefore
            && source.RecordCount == recordsBefore && source.BufferBytes == bytesBefore, "selector must not mutate source representation");
        Throws<ArgumentOutOfRangeException>(() => source.ToStorageForBulk(-1));
        foreach (int reservation in new[] { 0, 1, Math.Max(0, members.Count - 1), members.Count, members.Count + 1, registry.Count, int.MaxValue })
        {
            var converted = source.ToStorageForBulk(reservation);
            Check(!ReferenceEquals(source, converted) && ReferenceEquals(converted.Registry, registry), "bulk conversion owns distinct set in same registry");
            Check(converted.Storage == selected, "bulk selector and direct conversion must agree");
            Check(converted.ReservedMemberCapacity >= Math.Min(reservation, registry.Count), "bulk conversion honors arbitrary-member reservation");
            if (converted.BufferBytes != 0) Check(!ReferenceEquals(buffer, MemberBuffer(converted)), "bulk conversion must not share writable buffer even when layout is retained");
            Same(converted, members, "bulk direct conversion membership"); PackedBufferInvariant(converted);
            source.Clear(); Same(converted, members, "bulk conversion remains intact after source clear");
            foreach (int id in members) source.AddTag(registry.GetTagAt(id));
            converted.Clear(); Same(source, members, "bulk source remains intact after result clear");
            if (registry.Count != 0)
            {
                int probe = registry.Count - 1;
                converted.AddTag(registry.GetTagAt(probe)); Same(source, members, "bulk result mutation independent");
            }
            Check(source.Storage == initial && ReferenceEquals(buffer, MemberBuffer(source)), "bulk conversion leaves original layout/buffer intact");
        }
    }
    private static void BulkSelectionAndDirectConversion()
    {
        foreach (int universe in new[] { 0, 1, 65, 1024, 4097 })
        {
            var registry = Registry(universe);
            foreach (int count in new[] { 0, 1, 2, 63, 64, 65, 128 })
            {
                if (count > universe) continue;
                var local = Range(count);
                var spread = SetOf();
                for (int i = 0; i < count; i++) spread.Add(i * Math.Max(1, universe / Math.Max(1, count)));
                foreach (TagSetStorage mode in Modes)
                { CheckBulkConversion(registry, local, mode); CheckBulkConversion(registry, spread, mode); }
            }
        }
        // Fixed four-record data straddles the documented backend policy boundary. This
        // tests the public decision at either side, without calling its private selector.
        int backendWidth = 1;
#if NET8_0_OR_GREATER && !GAMEPLAYTAGS_FORCE_PORTABLE && !GAMEPLAYTAGS_EXPECT_PORTABLE_RUNTIME
        if (System.Runtime.Intrinsics.X86.Avx2.IsSupported) backendWidth = 4;
        else if (System.Runtime.Intrinsics.Arm.AdvSimd.Arm64.IsSupported) backendWidth = 2;
#endif
        foreach (int wordCount in new[] { 4 * backendWidth - 1, 4 * backendWidth, 4 * backendWidth + 1 })
        {
            var registry = Registry(wordCount * 64);
            foreach (TagSetStorage mode in Modes)
            {
                var source = Set(registry, Range(64), mode, ordered: true);
                TagSetStorage expected = wordCount <= 4 * backendWidth ? TagSetStorage.Dense : TagSetStorage.Compressed;
                Check(source.SelectBulkStorage() == expected, "bulk dense/record policy boundary");
                Check(RuntimeTagSet.FromTagsForBulk(registry, Handles(registry, Range(64))).Storage == expected, "bulk factory and existing policy boundary");
                CheckBulkConversion(registry, Range(64), mode);
            }
        }
        foreach (int universe in new[] { 1 << 20, (1 << 20) + 1 })
        {
            var registry = SyntheticNumericRegistry(universe);
            var high = Range(universe, 1, universe - 128);
            var sparseHigh = SetOf(0, 1 << 19, universe - 1);
            var scattered = SetOf(); for (int i = 0; i < 128; i++) scattered.Add(i * 4097);
            foreach (TagSetStorage mode in Modes)
            {
                if (universe > (1 << 20) && mode == TagSetStorage.Compressed) continue;
                CheckBulkConversion(registry, high, mode);
                CheckBulkConversion(registry, sparseHigh, mode);
                CheckBulkConversion(registry, scattered, mode);
            }
        }
    }
    private static void CompactQueriesAndEmptyAdmission()
    {
        var registry = Registry(129); var foreign = Registry(129);
        var counts = new HashSet<int> { 0, 1, 2, 3, 4, 7, 8, 9, 15, 16, 17, 31, 32, 33,
            System.Numerics.Vector<int>.Count - 1, System.Numerics.Vector<int>.Count, System.Numerics.Vector<int>.Count + 1 };
        foreach (int count in counts)
        {
            var members = Range(count * 2, 2);
            var source = Set(registry, Range(registry.Count), TagSetStorage.Sparse);
            source.Clear(); foreach (int id in members) source.AddTag(registry.GetTagAt(id));
            for (int id = 0; id < registry.Count; id++)
                Check(source.HasTagExact(registry.GetTagAt(id)) == members.Contains(id), "compact sparse hit/miss incl stale reserved tail");
            Check(!source.HasTagExact(default), "compact sparse default handle");
            Throws<ArgumentException>(() => source.HasTagExact(foreign.GetTagAt(0)));
            for (int warm = 0; warm < 12; warm++) sink = source.HasTagExact(registry.GetTagAt(128)) ? 1 : 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int repeat = 0; repeat < 64; repeat++) for (int id = 0; id < registry.Count; id++)
                sink = source.HasTagExact(registry.GetTagAt(id)) ? 1 : 0;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated == 0, "compact exact queries must allocate zero bytes");
        }
        foreach (TagSetStorage am in Modes) foreach (TagSetStorage bm in Modes)
        {
            var empty = new RuntimeTagSet(registry, 0, am);
            var foreignEmpty = new RuntimeTagSet(foreign, 0, bm);
            Throws<ArgumentException>(() => empty.HasAnyExact(foreignEmpty));
            Throws<ArgumentException>(() => empty.HasAllExact(foreignEmpty));
            Throws<ArgumentException>(() => empty.SetEquals(foreignEmpty));
            for (int op = 0; op < 3; op++)
            {
                int operation = op;
                Throws<ArgumentException>(() => Factory(operation, empty, foreignEmpty, am));
                foreach (TagSetStorage rm in Modes)
                {
                    var output = Set(registry, SetOf(128), rm);
                    Throws<ArgumentException>(() => Operate(operation, empty, foreignEmpty, output));
                    Same(output, SetOf(128), "empty fast-path registry error leaves output intact");
                    Throws<ArgumentException>(() => Operate(operation, empty, empty, foreignEmpty));
                    Throws<ArgumentNullException>(() => Operate(operation, empty, empty, null));
                }
                Throws<ArgumentOutOfRangeException>(() => Factory(operation, empty, empty, (TagSetStorage)99));
            }
        }
    }
    private static void PackedHierarchyQueriesAndAllocation()
    {
        var names = new List<string> { "A", "A.B", "A.B.C", "A.B.C.D", "A.Z", "Z" };
        for (int i = 0; i < 160; i++) names.Add("A.W.T" + i.ToString("D3"));
        var settings = Settings(names); GameplayTagManager.Initialize(settings, true);
        var registry = GameplayTagManager.CurrentRegistry;
        var any = GameplayTagQueryExpression.AnyTagsMatch().AddTag(new GameplayTag("A.B")).AddTag(new GameplayTag("A.W"));
        var excluded = GameplayTagQueryExpression.NoTagsMatch().AddTag(new GameplayTag("A.Z"));
        var frozen = new GameplayTagQuery(GameplayTagQueryExpression.AllExpressionsMatch().AddExpression(any).AddExpression(excluded)).Freeze(registry);
        var random = new Random(Seed ^ 0x6161);
        for (int iteration = 0; iteration < 64; iteration++)
        {
            var members = SetOf(); var conditions = SetOf();
            for (int id = 0; id < registry.Count; id++) { if (random.Next(5) == 0) members.Add(id); if (random.Next(13) == 0) conditions.Add(id); }
            var a = Set(registry, members, TagSetStorage.Compressed, registry.Count);
            bool expectedAny = false, expectedAll = true, frozenAny = false, forbidden = false;
            for (int parent = 0; parent < registry.Count; parent++)
            {
                bool match = false;
                foreach (int child in members) if (IsChild(registry.GetTagAt(child).Name, registry.GetTagAt(parent).Name)) { match = true; break; }
                Check(a.HasTag(registry.GetTagAt(parent)) == match, "packed hierarchy public HasTag string oracle");
                if (conditions.Contains(parent)) { expectedAny |= match; expectedAll &= match; }
            }
            foreach (int id in members)
            {
                string name = registry.GetTagAt(id).Name;
                frozenAny |= IsChild(name, "A.B") || IsChild(name, "A.W"); forbidden |= IsChild(name, "A.Z");
            }
            Check(frozen.Matches(a) == (frozenAny && !forbidden), "packed frozen nested query string oracle");
            foreach (TagSetStorage mode in Modes)
            {
                var c = Set(registry, conditions, mode, registry.Count);
                Check(a.HasAny(c) == expectedAny && a.HasAll(c) == expectedAll, "mixed hierarchy any/all");
                var filtered = new RuntimeTagSet(registry, registry.Count, mode);
                var alias = new RuntimeTagSet(registry, registry.Count, TagSetStorage.Compressed);
                var expectedFilter = SetOf();
                foreach (int child in members) foreach (int parent in conditions)
                    if (IsChild(registry.GetTagAt(child).Name, registry.GetTagAt(parent).Name)) { expectedFilter.Add(child); break; }
                Action action = () => {
                    sink ^= (a.HasAny(c) ? 1 : 0) + (a.HasAll(c) ? 2 : 0) + (frozen.Matches(a) ? 4 : 0);
                    a.FilterInto(c, filtered); alias.CopyFrom(a); alias.FilterInto(c, alias);
                    sink ^= filtered.Count + alias.Count;
                };
                for (int j = 0; j < 20; j++) action();
                long before = GC.GetAllocatedBytesForCurrentThread(); for (int j = 0; j < 40; j++) action();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Check(allocated == 0, "prepared packed public queries/filtering allocate zero: " + mode + " bytes=" + allocated);
                Same(filtered, expectedFilter, "prepared packed hierarchy output"); Same(alias, expectedFilter, "prepared packed hierarchy alias");
            }
            PackedBufferInvariant(a);
        }
    }
    private static void ReservedMixedAliases(TagRegistry registry, HashSet<int> aMembers, HashSet<int> bMembers)
    {
        var expected = Oracle(2, aMembers, bMembers);
        foreach (TagSetStorage am in Modes) foreach (TagSetStorage bm in Modes) foreach (TagSetStorage rm in Modes)
        {
            var a = Set(registry, aMembers, am, registry.Count, true);
            var b = Set(registry, bMembers, bm, registry.Count, true);
            var output = new RuntimeTagSet(registry, registry.Count, rm);
            var leftAlias = new RuntimeTagSet(registry, registry.Count, am);
            var rightAlias = new RuntimeTagSet(registry, registry.Count, bm);
            Action operation = () => {
                for (int op = 0; op < 3; op++)
                {
                    Operate(op, a, b, output);
                    leftAlias.CopyFrom(a); Operate(op, leftAlias, b, leftAlias);
                    rightAlias.CopyFrom(b); Operate(op, a, rightAlias, rightAlias);
                    sink ^= output.Count + leftAlias.Count + rightAlias.Count;
                }
            };
            for (int i = 0; i < 3; i++) operation();
            long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 5; i++) operation();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated == 0, "large mixed/alias reserved allocation " + am + "/" + bm + "/" + rm + " bytes=" + allocated);
            Same(output, expected, "large mixed reserved independent difference");
            Same(leftAlias, expected, "large mixed reserved left alias difference");
            Same(rightAlias, expected, "large mixed reserved right alias difference");
            Same(a, aMembers, "large mixed left untouched"); Same(b, bMembers, "large mixed right untouched");
            PackedBufferInvariant(output); PackedBufferInvariant(leftAlias); PackedBufferInvariant(rightAlias);
        }
    }
    private static void LargeMixedAndTinySkew()
    {
        var registry = Registry(65537);
        // Ordered input construction keeps this kernel regression group from spending its
        // time deliberately shifting 16K sparse insertions; descending insertion is tested above.
        var scattered = Range(65536, 4); scattered.Remove(0); scattered.Add(65536);
        var localized = Range(32768, 1, 16384);
        Pair(registry, scattered, localized, true, true);
        Pair(registry, localized, scattered, true, true);
        ReservedMixedAliases(registry, scattered, localized);
        var large = Range(32768, 8);
        var tiny = SetOf(0, 8, 8192, 16384, 32760, 1, 16385, 65536);
        Pair(registry, large, tiny, true, true); Pair(registry, tiny, large, true, true);
        ReservedMixedAliases(registry, large, tiny); ReservedMixedAliases(registry, tiny, large);
        foreach (int count in new[] { 63, 64, 65 })
        {
            var boundary = SetOf();
            for (int i = 0; i < count; i++) boundary.Add(i * 512 + (i % 2 == 0 || i == 63 ? 0 : 1));
            // At size64, the last match sets bit63 in the on-stack mask. Size65 must
            // take the non-tiny path, preserving both matching and missing members.
            Pair(registry, large, boundary, true, true); Pair(registry, boundary, large, true, true);
            if (count == 64) { ReservedMixedAliases(registry, large, boundary); ReservedMixedAliases(registry, boundary, large); }
        }
    }
    private static HashSet<int> BlockRun(int start, int length, int pattern)
    {
        var result = SetOf();
        for (int block = start; block < start + length; block++)
        {
            int mask = pattern == 0 ? 65535 : pattern == 1 ? 0x5555 : pattern == 2 ? 0xAAAA
                : pattern == 3 ? (block % 3 == 0 ? 65535 : block % 3 == 1 ? 0x5555 : 0xAAAA)
                : pattern == 4 ? (block % 3 == 0 ? 0x5555 : block % 3 == 1 ? 0xAAAA : 65535)
                : 1 << (block & 15);
            for (int bit = 0; bit < 16; bit++) if ((mask & (1 << bit)) != 0) result.Add(block * 16 + bit);
        }
        return result;
    }
    private static int OccupiedRecords(HashSet<int> members)
    { var blocks = new HashSet<int>(); foreach (int id in members) blocks.Add(id >> 4); return blocks.Count; }
    private static void ExactOutputCapacities(TagRegistry registry, HashSet<int> x, HashSet<int> y)
    {
        foreach (TagSetStorage am in Modes) foreach (TagSetStorage bm in Modes)
        {
            var a = Set(registry, x, am, ordered: true); var b = Set(registry, y, bm, ordered: true);
            for (int op = 0; op < 3; op++) foreach (TagSetStorage rm in Modes)
            {
                var expected = Oracle(op, x, y);
                int actual = rm == TagSetStorage.Compressed ? OccupiedRecords(expected) : expected.Count;
                int upper = rm == TagSetStorage.Compressed ? OccupiedRecords(Oracle(0, x, y)) : Oracle(0, x, y).Count;
                foreach (int capacity in new HashSet<int> { 0, Math.Max(0, actual - 1), actual, upper })
                {
                    var output = new RuntimeTagSet(registry, capacity, rm);
                    if (capacity != 0) output.AddTag(registry.GetTagAt(registry.Count - 1));
                    object buffer = MemberBuffer(output); long originalBytes = output.BufferBytes;
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    Operate(op, a, b, output);
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    Same(output, expected, "range exact/insufficient/upper destination capacity"); PackedBufferInvariant(output);
                    if (capacity >= actual || rm == TagSetStorage.Dense)
                    {
                        Check(ReferenceEquals(buffer, MemberBuffer(output)) && originalBytes == output.BufferBytes,
                            "actual-result-sized independent output must not grow: " + op + "/" + am + "/" + bm + "/" + rm);
                        Check(allocated == 0, "actual-result-sized range operation allocates zero: " + allocated);
                    }
                }
            }
            Same(a, x, "capacity variants keep left intact"); Same(b, y, "capacity variants keep right intact");
        }
    }
    private static void ShiftedPackedRangesAndCapacity()
    {
        var registry = Registry(2049);
        // Both offset directions, exact touching/disjoint intervals, containment, and
        // SIMD thresholds. Alternating masks create zero and nonzero output records.
        int[,] shapes = { { 32, 7, 33, 7 }, { 32, 8, 31, 8 }, { 32, 15, 33, 15 },
            { 32, 16, 31, 16 }, { 32, 17, 33, 17 }, { 32, 25, 40, 8 },
            { 32, 8, 24, 25 }, { 32, 17, 49, 17 }, { 32, 17, 50, 17 },
            { 32, 17, 15, 17 }, { 32, 17, 14, 17 }, { 32, 17, 32, 17 } };
        for (int row = 0; row < shapes.GetLength(0); row++)
        {
            var a = BlockRun(shapes[row, 0], shapes[row, 1], 3);
            var b = BlockRun(shapes[row, 2], shapes[row, 3], 4);
            Pair(registry, a, b, true, true); Pair(registry, b, a, true, true);
            ExactOutputCapacities(registry, a, b); ExactOutputCapacities(registry, b, a);
            if (row == 4 || row == 8) { ReservedMixedAliases(registry, a, b); ReservedMixedAliases(registry, b, a); }
        }
        // Equal block keys with wholly disjoint member masks must compact every record.
        var even = BlockRun(32, 33, 1); var odd = BlockRun(32, 33, 2);
        Pair(registry, even, odd, true, true); ExactOutputCapacities(registry, even, odd);
        var full = BlockRun(32, 33, 0);
        Pair(registry, even, full, true, true); ExactOutputCapacities(registry, even, full);
        // Sparse inputs group several IDs per record, with both earlier and later keys.
        var sparse = BlockRun(20, 49, 5); var packed = BlockRun(32, 17, 3);
        Pair(registry, packed, sparse, true, true); Pair(registry, sparse, packed, true, true);
        ExactOutputCapacities(registry, packed, sparse); ExactOutputCapacities(registry, sparse, packed);
    }
    private static void PackedLookupGapsSignBoundaryAndQuarters()
    {
        var registry = SyntheticNumericRegistry(1 << 20);
        foreach (int start in new[] { 0, (1 << 15) - 12, (1 << 16) - 24 })
        {
            var expected = BlockRun(start, 24, 5);
            var packed = Set(registry, expected, TagSetStorage.Compressed, registry.Count, true);
            Action verify = () =>
            {
                Same(packed, expected, "contiguous/gapped direct packed lookup"); PackedBufferInvariant(packed);
                for (int id = Math.Max(0, start * 16 - 17); id < Math.Min(registry.Count, (start + 24) * 16 + 17); id++)
                {
                    var tag = registry.GetTagAt(id);
                    Check(packed.HasTagExact(tag) == expected.Contains(id), "packed arithmetic index across signed key/gap boundary");
                    Check(packed.HasTag(tag) == expected.Contains(id), "flat synthetic packed range across gap");
                }
            };
            verify();
            // Remove/reinsert complete interior, first and last blocks. The representation
            // alternates between contiguous indexing and binary lower-bound lookup.
            foreach (int block in new[] { start + 12, start, start + 23, start + 11, start + 13 })
            {
                int id = block * 16 + (block & 15);
                Check(packed.RemoveTag(registry.GetTagAt(id)) == expected.Remove(id), "remove whole packed block"); verify();
                int differentBit = block * 16 + ((block + 7) & 15);
                Check(packed.AddTag(registry.GetTagAt(differentBit)) == expected.Add(differentBit), "reinsert gap with different bit"); verify();
                Check(packed.AddTag(registry.GetTagAt(id)) == expected.Add(id), "add bit into restored block"); verify();
                Check(packed.RemoveTag(registry.GetTagAt(differentBit)) == expected.Remove(differentBit), "remove partial bit while retaining block"); verify();
            }
            var shifted = BlockRun(start == 0 ? 3 : start - 3, 24, 3);
            Pair(registry, expected, shifted, true, true); Pair(registry, shifted, expected, true, true);
        }
        for (int quarterMask = 0; quarterMask < 16; quarterMask++)
        {
            var members = SetOf();
            // Five populated words separated by zeros exercise every nonempty-quarter
            // pattern. Full quarters make bulk conversion select compressed for N>=64.
            for (int word = 0; word < 5; word++) for (int q = 0; q < 4; q++)
                if ((quarterMask & (1 << q)) != 0)
                    for (int bit = 0; bit < 16; bit++) members.Add((word * 3 + 8190) * 64 + q * 16 + bit);
            foreach (TagSetStorage mode in Modes)
            {
                var source = Set(registry, members, mode, ordered: true);
                TagSetStorage selected = source.SelectBulkStorage();
                Check(selected == (members.Count == 0 ? mode : TagSetStorage.Compressed), "quarter occupancy public bulk selector");
                var output = source.ToStorageForBulk();
                Same(output, members, "quarter occupancy direct conversion"); PackedBufferInvariant(output);
                if (members.Count != 0) Check(output.RecordCount == OccupiedRecords(members), "quarter occupancy exact record count");
            }
        }
    }
    private static void CheckedRegistryIndexAdmission()
    {
        foreach (int count in new[] { 0, 1, 129 })
        {
            var registry = Registry(count);
            foreach (int index in new[] { -1, int.MinValue, count, int.MaxValue })
                Throws<ArgumentOutOfRangeException>(() => registry.GetTagAt(index));
            if (count != 0) Check(registry.GetTagAt(count - 1).RuntimeIndex == count - 1, "last valid registry index");
        }
    }
    private static void CheckedPackedLowerMiss()
    {
        var registry = Registry(257);
        foreach (var members in new[] { SetOf(64), SetOf(64, 80, 96), SetOf(64, 96, 256) })
        {
            var value = Set(registry, members, TagSetStorage.Compressed);
            for (int id = 0; id < 64; id++)
            {
                Check(!value.HasTagExact(registry.GetTagAt(id)), "compressed earlier block is a miss under checked arithmetic");
                Check(!value.HasTag(registry.GetTagAt(id)), "compressed earlier flat subtree is a miss");
                Check(!value.RemoveTag(registry.GetTagAt(id)), "compressed earlier removal is a miss");
            }
            Same(value, members, "checked lower misses preserve members");
        }
    }
    private static void CheckedIteratorTermination(TagSetStorage mode, bool records)
    {
        var registry = Registry(257);
        Type iterator = typeof(RuntimeTagSet).GetNestedType(records ? "RecordEnumerator" : "IdEnumerator", BindingFlags.NonPublic);
        var ctor = iterator.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(RuntimeTagSet), typeof(bool) }, null);
        var move = iterator.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var current = iterator.GetProperty("Current", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        foreach (var members in new[] { SetOf(), SetOf(0), SetOf(0, 15, 16, 63, 64, 127, 128, 256) })
        foreach (bool reverse in new[] { false, true })
        {
            var source = Set(registry, members, mode);
            var expected = new List<int>();
            if (records)
            {
                var masks = new SortedDictionary<int,int>();
                foreach (int id in members) { int block = id / 16; masks.TryGetValue(block, out int mask); masks[block] = mask | (1 << (id % 16)); }
                foreach (var pair in masks) expected.Add(unchecked((pair.Key << 16) ^ int.MinValue) | pair.Value);
            }
            else { expected.AddRange(members); expected.Sort(); }
            if (reverse) expected.Reverse();
            object cursor = ctor.Invoke(new object[] { source, reverse });
            foreach (int item in expected)
            {
                Check((bool)move.Invoke(cursor, null), "checked iterator has next value");
                Check((int)current.GetValue(cursor) == item, "checked iterator mathematical sequence");
            }
            Check(!(bool)move.Invoke(cursor, null), "checked iterator terminates normally");
            Check(!(bool)move.Invoke(cursor, null), "checked iterator remains terminated");
            Same(source, members, "checked iterator source remains unchanged");
        }
    }
    private static void CheckedReversePublicAliases()
    {
        var registry = Registry(257);
        Pair(registry, SetOf(0, 32, 64, 128, 256), SetOf(0, 1, 63, 65, 127), true);
        Pair(registry, Range(129, 2), Range(130, 2, 1), true);
        RightAliasPreservation();
    }
    private static int Main(string[] args)
    {
        bool aliasOnly = args.Length > 1 && args[1] == "right-alias-only";
        bool packedOnly = args.Length > 1 && args[1] == "compressed-only";
        bool mixedOnly = args.Length > 1 && args[1] == "mixed-large-only";
        bool policyOnly = args.Length > 1 && args[1] == "bulk-policy-only";
        bool rangesOnly = args.Length > 1 && args[1] == "packed-ranges-only";
        bool checkedOnly = args.Length > 1 && args[1] == "checked-runtime-only";
        if (!aliasOnly && !packedOnly && !mixedOnly && !policyOnly && !rangesOnly && !checkedOnly)
        {
        Test("independent-exhaustive-random-skew-word-boundary-operators", DifferenceAndSkew);
        Test("explicit-conversion-resolved-loading-and-authoring-aliases", ConversionAndLoading);
        Test("hierarchy-sweep-nested-intervals-and-aliasing", HierarchySweep);
        Test("registry-null-invalid-mode-and-failure-isolation", AdmissionAndExceptions);
        Test("shuffled-definitions-prefix-reuse-and-canonical-dfs", RegistryPrefixReuse);
        Test("reserved-zero-allocation-and-right-alias-boundary", ReservedAllocationContract);
        }
        if (!packedOnly && !mixedOnly && !policyOnly && !rangesOnly && !checkedOnly) Test("right-alias-union-capacity-preservation-and-growth", RightAliasPreservation);
        if (!aliasOnly && !mixedOnly && !policyOnly && !rangesOnly && !checkedOnly)
        {
            Test("compressed-16-id-biased-boundaries-and-synthetic-format-limit", PackedBoundariesAndFormatLimit);
            Test("explicit-bulk-preparation-policy-ownership-and-format-fallback", BulkPreparationPolicyAndOwnership);
            Test("compressed-hierarchy-and-frozen-query-oracles-zero-allocation", PackedHierarchyQueriesAndAllocation);
            Test("ordered-bulk-defaults-duplicates-late-foreign-and-capacity-boundaries", OrderedBulkValidationAndReservations);
            Test("long-bulk-defaults-duplicates-admission-and-exact-selector-fallback", LongBulkAdmissionAndFallback);
        }
        if (!aliasOnly && !packedOnly && !policyOnly && !rangesOnly && !checkedOnly) Test("large-dense-packed-and-tiny-skew-alias-oracles-zero-allocation", LargeMixedAndTinySkew);
        if (!aliasOnly && !packedOnly && !mixedOnly && !rangesOnly && !checkedOnly)
        {
            Test("bulk-selector-purity-independent-direct-conversion-capacity-and-format", BulkSelectionAndDirectConversion);
            Test("compact-query-boundaries-stale-tails-and-empty-registry-admission", CompactQueriesAndEmptyAdmission);
        }
        if (!aliasOnly && !packedOnly && !mixedOnly && !policyOnly && !checkedOnly)
        {
            Test("shifted-packed-range-and-direct-sparse-aliases-exact-capacity", ShiftedPackedRangesAndCapacity);
            Test("packed-arithmetic-lookup-gaps-sign-boundaries-and-quarter-selector", PackedLookupGapsSignBoundaryAndQuarters);
        }
        if (!aliasOnly && !packedOnly && !mixedOnly && !policyOnly && !rangesOnly)
        {
            Test("checked-runtime-negative-registry-index-admission", CheckedRegistryIndexAdmission);
            Test("checked-runtime-compressed-lower-bound-misses", CheckedPackedLowerMiss);
            foreach (TagSetStorage mode in Modes)
            {
                Test("checked-runtime-id-iterator-termination-" + mode, () => CheckedIteratorTermination(mode, false));
                Test("checked-runtime-record-iterator-termination-" + mode, () => CheckedIteratorTermination(mode, true));
            }
            Test("checked-runtime-public-reverse-alias-termination", CheckedReversePublicAliases);
        }
        var report = new { seed = Seed, assertions, pairs, failures, kernelFlavor = KernelFlavor, runtime = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(), sink, results,
            gcMode = Environment.GetEnvironmentVariable("DOTNET_gcConcurrent") == "0" ? "BatchGC" : "Unspecified",
            gcLatencyMode = GCSettings.LatencyMode.ToString(), syntheticNumericRegistryBoundaryFixture = true,
            hardwareIntrinsicsDisabled = Environment.GetEnvironmentVariable("DOTNET_EnableHWIntrinsic") == "0",
            vectorIntCount = System.Numerics.Vector<int>.Count, vectorHardwareAccelerated = System.Numerics.Vector.IsHardwareAccelerated,
            denseWordsPerPackedRecord = RuntimeBitOperations.DenseWordsPerPackedRecord,
#if GAMEPLAYTAGS_EXPECT_CHECKED_RUNTIME
            expectedRuntimeCheckedArithmetic = true,
#else
            expectedRuntimeCheckedArithmetic = false,
#endif
            native_unity_executed = false, note = "Independent HashSet and string-prefix oracles with explicit Unity API facades. Does not validate native Unity, IL2CPP, or ARM64." };
        if (args.Length != 0)
        { Directory.CreateDirectory(args[0]); File.WriteAllText(Path.Combine(args[0], "runtime-refactor-tests.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); }
        Console.WriteLine("RUNTIME REFACTOR TESTS assertions=" + assertions + " pairs=" + pairs + " failures=" + failures);
        return failures == 0 ? 0 : 1;
    }
}
