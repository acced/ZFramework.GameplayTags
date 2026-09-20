using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using GameplayTags;
using GameplayTags.Editor;
using UnityEngine;

internal static class IntegerTests
{
    private const int Seed = 20260920;
    private static long assertions;
    private static int failures;
    private static int checksum;
    private static object sink;
    private static readonly List<object> results = new List<object>();
    private static readonly string[] Domain = { "A", "A.B", "A.B.C", "A!x", "A-x", "Z" };
    private static readonly TagSetStorage[] Modes = { TagSetStorage.Sparse, TagSetStorage.Dense };
    private static void Check(bool value, string message = "assertion") { assertions++; if (!value) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    { assertions++; try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static void Test(string name, Action action)
    {
        try { action(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
        catch (Exception e) { failures++; results.Add(new { name, passed = false, error = e.ToString() }); Console.WriteLine("FAIL " + name + " " + e); }
    }
    private static GameplayTagSettings Settings(IEnumerable<string> names)
    {
        var s = ScriptableObject.CreateInstance<GameplayTagSettings>();
        s.ReplaceAll(names.Select(n => new GameplayTagDefinition(n, "", "Default", false, true)).ToList(),
            new List<GameplayTagRedirect>(), new List<GameplayTagSource> { new GameplayTagSource("Default", "", false) });
        return s;
    }
    private static TagRegistry Setup(IEnumerable<string> names)
    { GameplayTagManager.Initialize(Settings(names), true); return GameplayTagManager.CurrentRegistry; }
    private static RuntimeTagSet Set(TagRegistry registry, IEnumerable<string> names, TagSetStorage mode, int capacity = 0)
    {
        string[] input = names.ToArray();
        var set = new RuntimeTagSet(registry, Math.Max(capacity, input.Length), mode);
        foreach (string name in input) set.AddTag(registry.Resolve(name));
        return set;
    }
    private static void Same(RuntimeTagSet actual, IEnumerable<string> names)
    {
        var expected = new HashSet<string>(names, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int previous = -1;
        foreach (RuntimeTag tag in actual)
        {
            Check(tag.RuntimeIndex > previous, "enumeration must be strictly increasing DFS IDs");
            previous = tag.RuntimeIndex;
            Check(ReferenceEquals(tag.Registry, actual.Registry), "foreign enumeration handle");
            Check(seen.Add(tag.Name), "duplicate member");
        }
        Check(seen.Count == actual.Count && seen.SetEquals(expected), "membership/count mismatch");
    }
    private static bool Matches(string child, string parent) => child == parent || child.StartsWith(parent + ".", StringComparison.Ordinal);
    private static void Field(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static GameplayTagQueryExpression Expr(Random random, int depth)
    {
        var result = new GameplayTagQueryExpression((GameplayTagQueryExpressionType)(depth == 0 ? random.Next(1, 4) : random.Next(1, 7)));
        int count = random.Next(5);
        for (int i = 0; i < count; i++)
            if (result.UsesTags) result.AddTag(GameplayTagManager.RequestTag(Domain[random.Next(Domain.Length)]));
            else result.AddExpression(Expr(random, depth - 1));
        return result;
    }
    private static bool Eval(GameplayTagQueryExpression e, HashSet<string> owned)
    {
        switch (e.Type)
        {
            case GameplayTagQueryExpressionType.AnyTagsMatch: return e.Tags.Any(t => owned.Any(n => Matches(n, t.Name)));
            case GameplayTagQueryExpressionType.AllTagsMatch: return e.Tags.All(t => owned.Any(n => Matches(n, t.Name)));
            case GameplayTagQueryExpressionType.NoTagsMatch: return !e.Tags.Any(t => owned.Any(n => Matches(n, t.Name)));
            case GameplayTagQueryExpressionType.AnyExpressionsMatch: return e.Expressions.Any(x => Eval(x, owned));
            case GameplayTagQueryExpressionType.AllExpressionsMatch: return e.Expressions.All(x => Eval(x, owned));
            case GameplayTagQueryExpressionType.NoExpressionsMatch: return !e.Expressions.Any(x => Eval(x, owned));
            default: throw new InvalidOperationException();
        }
    }
    private static void Tests()
    {
        Test("dfs-hierarchy-unicode-punctuation-and-word-boundaries", () =>
        {
            var names = Domain.Concat(new[] { "状态.灼烧", "Wide😀.叶", "A0", "A.B1", "A.B.C.D" })
                .Concat(Enumerable.Range(0, 137).Select(i => "Boundary.T" + i.ToString("D3"))).ToArray();
            TagRegistry registry = Setup(names);
            for (int i = 0; i < registry.Count; i++) for (int j = 0; j < registry.Count; j++)
            {
                RuntimeTag child = registry.GetTagAt(i), parent = registry.GetTagAt(j);
                Check(child.MatchesTag(parent) == Matches(child.Name, parent.Name), "DFS ancestry differs from independent string oracle");
            }
            foreach (TagSetStorage mode in Modes)
            {
                var set = new RuntimeTagSet(registry, registry.Count, mode);
                for (int i = 0; i < registry.Count; i++)
                {
                    set.Clear(); set.AddTag(registry.GetTagAt(i));
                    for (int j = 0; j < registry.Count; j++) Check(set.HasTag(registry.GetTagAt(j)) == Matches(registry.GetTagAt(i).Name, registry.GetTagAt(j).Name));
                    Same(set, new[] { registry.GetTagAt(i).Name });
                }
                for (int i = 0; i < registry.Count; i++) set.AddTag(registry.GetTagAt(i));
                Check(set.Count == registry.Count); set.RemoveTags(set); Check(set.Count == 0);
            }
            Check(!RuntimeTag.None.IsValid); Check(!RuntimeTag.None.MatchesTag(RuntimeTag.None));
            Check(registry.Resolve(" A.B ") == registry.Resolve("A.B"));
            Check(!registry.TryResolve("Missing", out _));
            Throws<ArgumentOutOfRangeException>(() => registry.GetTagAt(-1));
        });
        Test("eight-storage-combinations-exhaustive-six-element-sets", () =>
        {
            TagRegistry registry = Setup(Domain);
            for (int a = 0; a < 64; a++) for (int b = 0; b < 64; b++)
            {
                string[] x = Domain.Where((_, i) => (a & (1 << i)) != 0).ToArray();
                string[] y = Domain.Where((_, i) => (b & (1 << i)) != 0).ToArray();
                foreach (TagSetStorage am in Modes) foreach (TagSetStorage bm in Modes)
                {
                    var left = Set(registry, x, am); var right = Set(registry, y, bm);
                    Check(left.HasAnyExact(right) == x.Intersect(y).Any());
                    Check(left.HasAllExact(right) == y.All(n => x.Contains(n)));
                    Check(left.HasAny(right) == y.Any(p => x.Any(n => Matches(n, p))));
                    Check(left.HasAll(right) == y.All(p => x.Any(n => Matches(n, p))));
                    foreach (TagSetStorage outputMode in Modes)
                    {
                        var output = new RuntimeTagSet(registry, registry.Count, outputMode);
                        RuntimeTagSet.UnionInto(left, right, output); Same(output, x.Concat(y));
                        RuntimeTagSet.IntersectionExactInto(left, right, output); Same(output, x.Intersect(y));
                        left.FilterInto(right, output); Same(output, x.Where(n => y.Any(p => Matches(n, p))));
                        output.CopyFrom(left); Same(output, x);
                        output.AppendTags(right); Same(output, x.Concat(y));
                        output.CopyFrom(left); Check(output.RemoveTags(right) == x.Intersect(y).Any()); Same(output, x.Except(y));
                    }
                    var alias = new RuntimeTagSet(left);
                    RuntimeTagSet.UnionInto(alias, right, alias); Same(alias, x.Concat(y));
                    alias = new RuntimeTagSet(right); RuntimeTagSet.UnionInto(left, alias, alias); Same(alias, x.Concat(y));
                    alias = new RuntimeTagSet(left); RuntimeTagSet.IntersectionExactInto(alias, right, alias); Same(alias, x.Intersect(y));
                    alias = new RuntimeTagSet(right); RuntimeTagSet.IntersectionExactInto(left, alias, alias); Same(alias, x.Intersect(y));
                    alias = new RuntimeTagSet(left); alias.FilterInto(right, alias); Same(alias, x.Where(n => y.Any(p => Matches(n, p))));
                    Same(left, x); Same(right, y);
                }
            }
        });
        Test("continuous-random-mutations-and-explicit-storage", () =>
        {
            string[] names = Enumerable.Range(0, 257).Select(i => "Random.G" + (i % 13) + ".T" + i).ToArray();
            TagRegistry registry = Setup(names);
            foreach (TagSetStorage mode in Modes)
            {
                var set = new RuntimeTagSet(registry, 0, mode);
                var reference = new HashSet<string>(StringComparer.Ordinal);
                var random = new Random(Seed);
                for (int i = 0; i < 10000; i++)
                {
                    string name = names[random.Next(names.Length)];
                    switch (random.Next(5))
                    {
                        case 0: Check(set.AddTag(registry.Resolve(name)) == reference.Add(name)); break;
                        case 1: Check(set.RemoveTag(registry.Resolve(name)) == reference.Remove(name)); break;
                        case 2:
                        case 3:
                            string[] subset = names.Where(_ => random.Next(12) == 0).ToArray();
                            var other = Set(registry, subset, Modes[random.Next(2)]);
                            if ((i & 1) == 0) { set.AppendTags(other); reference.UnionWith(subset); }
                            else { set.RemoveTags(other); reference.ExceptWith(subset); }
                            break;
                        default: Check(set.HasTagExact(registry.Resolve(name)) == reference.Contains(name)); break;
                    }
                    Check(set.Storage == mode, "storage changed implicitly");
                    Same(set, reference);
                }
            }
        });
        Test("snapshot-identity-lifetime-and-transactional-rebuild", () =>
        {
            TagRegistry first = Setup(Domain), second = TagRegistry.Create(Settings(Domain));
            var old = Set(first, Domain, TagSetStorage.Dense); var foreign = Set(second, Domain, TagSetStorage.Sparse);
            RuntimeTag tag = first.Resolve("A.B"), other = second.Resolve("A.B");
            Check(tag != other); Check(tag.Name == other.Name);
            Throws<ArgumentException>(() => old.AddTag(other)); Throws<ArgumentException>(() => old.HasTagExact(other));
            Throws<ArgumentException>(() => old.RemoveTag(other)); Throws<ArgumentException>(() => old.HasTag(other));
            Throws<ArgumentException>(() => old.CopyFrom(foreign)); Throws<ArgumentException>(() => old.AppendTags(foreign));
            Throws<ArgumentException>(() => old.RemoveTags(foreign));
            Throws<ArgumentException>(() => RuntimeTagSet.UnionInto(old, old, foreign));
            Same(old, Domain); Same(foreign, Domain);
            Check(!old.AddTag(default)); Check(!old.HasTagExact(default)); Check(!old.RemoveTag(default));
            var settings = Settings(Domain); settings.TagsInternal.Add(new GameplayTagDefinition("A", "", "Default", false, true));
            Throws<GameplayTagRegistryException>(() => GameplayTagManager.Initialize(settings, true));
            Check(ReferenceEquals(first, GameplayTagManager.CurrentRegistry));
            GameplayTagManager.Initialize(Settings(new[] { "New" }), true);
            Check(old.HasTagExact(tag)); Check(tag.Name == "A.B"); Same(old, Domain);
            Check(!GameplayTagManager.CurrentRegistry.TryResolve("A.B", out _));
        });
        Test("source-serialization-loading-and-capacity-contracts", () =>
        {
            TagRegistry registry = Setup(Domain);
            var source = new GameplayTagContainer(64);
            Field(source, "m_GameplayTags", new List<GameplayTag>(64) { new GameplayTag("Z"), default, new GameplayTag("A"), new GameplayTag("A") });
            ((ISerializationCallbackReceiver)source).OnAfterDeserialize();
            Check(source.Count == 2 && source[0].Name == "A");
            RuntimeTagSet runtime = source.ToRuntime(registry, registry.Count, TagSetStorage.Dense);
            source.Clear(); Same(runtime, new[] { "A", "Z" });
            source.AddTag("A"); source.ResolveRegisteredTags(); Check(source.Capacity == 64);
            var s = Settings(Domain); s.RedirectsInternal.Add(new GameplayTagRedirect("Old", "A.B")); s.RedirectsInternal.Add(new GameplayTagRedirect("Older", "Old"));
            GameplayTagManager.Initialize(s, true); registry = GameplayTagManager.CurrentRegistry;
            Check(registry.Resolve("Older").Name == "A.B"); Check(!registry.IsRegistered("Older"));
            Field(source, "m_GameplayTags", new List<GameplayTag> { new GameplayTag("Older"), new GameplayTag("Missing") });
            Throws<KeyNotFoundException>(() => source.ToRuntime(registry));
            Throws<KeyNotFoundException>(() => source.ResolveRegisteredTags()); Check(source[0].Name == "Older");
            var large = Setup(Enumerable.Range(0, 10000).Select(i => "T" + i));
            Check(new RuntimeTagSet(large, 8).Storage == TagSetStorage.Sparse);
            Check(new RuntimeTagSet(large, 1024).Storage == TagSetStorage.Dense);
            Check(new RuntimeTagSet(large, 1024).BufferBytes == 1256);
            Throws<ArgumentOutOfRangeException>(() => new RuntimeTagSet(large, -1));
            Throws<ArgumentOutOfRangeException>(() => new RuntimeTagSet(large, 0, (TagSetStorage)99));
        });
        Test("frozen-query-random-reference-and-both-storages", () =>
        {
            TagRegistry registry = Setup(Domain);
            var random = new Random(Seed);
            for (int i = 0; i < 2000; i++)
            {
                GameplayTagQueryExpression expression = Expr(random, 3);
                FrozenGameplayTagQuery frozen = new GameplayTagQuery(expression).Freeze(registry);
                for (int j = 0; j < 8; j++)
                {
                    var owned = new HashSet<string>(Domain.Where(_ => random.Next(2) == 0), StringComparer.Ordinal);
                    bool expected = Eval(expression, owned);
                    foreach (TagSetStorage mode in Modes) Check(frozen.Matches(Set(registry, owned, mode)) == expected, "frozen expression differs from source oracle");
                }
            }
        });
        Test("frozen-validation-sharing-negation-and-source-isolation", () =>
        {
            TagRegistry registry = Setup(Domain);
            var expression = GameplayTagQueryExpression.AllTagsMatch().AddTag(GameplayTagManager.RequestTag("A"));
            var frozen = new GameplayTagQuery(expression).Freeze(registry);
            expression.AddTag(GameplayTagManager.RequestTag("Z"));
            var owned = Set(registry, new[] { "A.B.C" }, TagSetStorage.Dense);
            Check(frozen.Matches(owned)); Check(!new GameplayTagQuery(expression).Freeze(registry).Matches(owned));
            Throws<ArgumentException>(() => frozen.Matches(Set(TagRegistry.Create(Settings(Domain)), new[] { "A" }, TagSetStorage.Dense)));
            var invalid = new GameplayTagQueryExpression(GameplayTagQueryExpressionType.Undefined);
            Throws<InvalidOperationException>(() => new GameplayTagQuery(GameplayTagQueryExpression.NoExpressionsMatch().AddExpression(invalid)).Freeze(registry));
            Throws<InvalidOperationException>(() => new GameplayTagQuery(GameplayTagQueryExpression.AnyExpressionsMatch().AddExpression(GameplayTagQueryExpression.AllTagsMatch()).AddExpression(invalid)).Freeze(registry));
            var a = GameplayTagQueryExpression.AllExpressionsMatch(); var b = GameplayTagQueryExpression.AnyExpressionsMatch(); a.AddExpression(b); b.AddExpression(a);
            Throws<InvalidOperationException>(() => new GameplayTagQuery(a).Freeze(registry));
            var nullTags = GameplayTagQueryExpression.AllTagsMatch(); Field(nullTags, "m_Tags", null);
            Throws<InvalidOperationException>(() => new GameplayTagQuery(nullTags).Freeze(registry));
            var nullChildren = GameplayTagQueryExpression.AllExpressionsMatch(); Field(nullChildren, "m_Expressions", null);
            Throws<InvalidOperationException>(() => new GameplayTagQuery(nullChildren).Freeze(registry));
            var deep = GameplayTagQueryExpression.AllTagsMatch();
            for (int i = 0; i < 63; i++) deep = GameplayTagQueryExpression.AllExpressionsMatch().AddExpression(deep);
            Check(new GameplayTagQuery(deep).Freeze(registry).Matches(owned));
            deep = GameplayTagQueryExpression.AllExpressionsMatch().AddExpression(deep);
            Throws<InvalidOperationException>(() => new GameplayTagQuery(deep).Freeze(registry));
            var x = GameplayTagQueryExpression.AllTagsMatch(); var y = GameplayTagQueryExpression.NoTagsMatch();
            for (int i = 0; i < 28; i++) { var n = GameplayTagQueryExpression.AllExpressionsMatch().AddExpression(x).AddExpression(y); y = x; x = n; }
            Throws<InvalidOperationException>(() => new GameplayTagQuery(x).Freeze(registry));
            Check(new GameplayTagQuery().Freeze(registry).IsEmpty); Check(!new GameplayTagQuery().Freeze(registry).Matches(owned));
            var reduced = new GameplayTagQuery(GameplayTagQueryExpression.AllTagsMatch()
                .AddTag(GameplayTagManager.RequestTag("A")).AddTag(GameplayTagManager.RequestTag("A.B")).AddTag(GameplayTagManager.RequestTag("A.B.C"))).Freeze(registry);
            Check(reduced.NodeCount == 1 && reduced.RangeCount == 1);
        });
        Test("settings-editor-csv-and-generated-source-regressions", () =>
        {
            foreach (string invalid in new[] { " A ", "A..B", "A\0B", "A\uD800", "A\uDC00" }) Check(!Settings(new[] { invalid }).Validate(new List<string>()));
            Check(!Settings(new[] { "State.A", "state.B" }).Validate(new List<string>()));
            var restricted = Settings(new[] { "A", "A.B" }); restricted.TagsInternal[0] = new GameplayTagDefinition("A", "", "Default", true, false);
            Check(!restricted.Validate(new List<string>()));
            var s = Settings(new[] { "A", "A.B.C", "Z" }); s.RedirectsInternal.Add(new GameplayTagRedirect("Legacy", "A.B"));
            Check(GameplayTagEditorUtility.TryRenameTag(s, "A", "X", true, true, out _));
            TagRegistry registry = TagRegistry.Create(s); Check(registry.Resolve("Legacy").Name == "X.B"); Check(registry.Resolve("A.B").Name == "X.B");
            uint before = s.ComputeContentHash(); int revision = s.Revision;
            Check(!GameplayTagEditorUtility.TryRenameTag(s, "X", "Z", true, true, out _)); Check(before == s.ComputeContentHash() && revision == s.Revision);
            Directory.CreateDirectory("Assets");
            string path = Path.Combine("Assets", "invalid.csv");
            foreach (string csv in new[] { "Wrong\nA\n", "Tag\nA\nA\n", "Tag,Comment\nA,\"unfinished\n", "Tag,Comment,Source,Restricted\nA,x,Default,maybe\n" })
            { File.WriteAllText(path, csv); Check(!GameplayTagCsvUtility.Import(s, path, out _)); Check(before == s.ComputeContentHash()); }
            s.SetEditorOptions(true, true, false, "Generated", "Tags", "Assets/../Outside.cs");
            Throws<ArgumentException>(() => GameplayTagCodeGenerator.GetOutputPath(s));
            s.SetEditorOptions(true, true, false, "Generated", "Tags", "Assets/Tags.cs");
            string code = GameplayTagCodeGenerator.BuildSource(s);
            Check(code.Contains("public sealed class Tags")); Check(code.Contains("global::GameplayTags.RuntimeTag"));
            Check(!code.Contains("static readonly")); Check(code.Contains("registry.Resolve"));
        });
        Test("prepared-hot-path-allocation-and-actual-result-capacity", () =>
        {
            TagRegistry registry = Setup(Domain.Concat(Enumerable.Range(0, 140).Select(i => "W.T" + i)));
            foreach (TagSetStorage am in Modes) foreach (TagSetStorage bm in Modes) foreach (TagSetStorage rm in Modes)
            {
                var a = Set(registry, new[] { "A", "A.B.C", "Z" }, am);
                var b = Set(registry, new[] { "A.B", "Z" }, bm);
                var work = new RuntimeTagSet(registry, registry.Count, rm);
                var output = new RuntimeTagSet(registry, registry.Count, rm);
                RuntimeTag tag = registry.Resolve("A.B.C");
                var q = new GameplayTagQuery(GameplayTagQueryExpression.AllTagsMatch().AddTag(GameplayTagManager.RequestTag("A"))).Freeze(registry);
                Action action = () =>
                {
                    work.CopyFrom(a); work.AppendTags(b); work.RemoveTags(b); work.AddTag(tag); work.RemoveTag(tag);
                    RuntimeTagSet.UnionInto(a, b, output); RuntimeTagSet.IntersectionExactInto(a, b, output); a.FilterInto(b, output);
                    checksum += a.HasTagExact(tag) && a.HasTag(registry.GetTagAt(0)) ? 1 : 0;
                    checksum += q.Matches(a) ? 1 : 0;
                    foreach (RuntimeTag value in a) checksum ^= value.RuntimeIndex;
                };
                for (int i = 0; i < 200; i++) action();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 3000; i++) action();
                Check(GC.GetAllocatedBytesForCurrentThread() == before, "hot-path allocation in " + am + "/" + bm + "/" + rm);
            }
            var x = Set(registry, new[] { "A" }, TagSetStorage.Sparse);
            var y = Set(registry, new[] { "Z" }, TagSetStorage.Dense);
            var empty = new RuntimeTagSet(registry, 0, TagSetStorage.Sparse);
            var one = new RuntimeTagSet(registry, 1, TagSetStorage.Sparse);
            Action tight = () => { RuntimeTagSet.IntersectionExactInto(x, y, empty); RuntimeTagSet.UnionInto(x, x, one); };
            tight(); long start = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 3000; i++) tight();
            Check(GC.GetAllocatedBytesForCurrentThread() == start); Check(empty.Capacity == 0 && one.Capacity == 1);
            start = GC.GetAllocatedBytesForCurrentThread(); sink = new byte[1024]; Check(GC.GetAllocatedBytesForCurrentThread() > start, "allocation positive control");
            Throws<ArgumentException>(() => x.FilterInto(y, y)); Same(y, new[] { "Z" });
        });
    }
    private static int Main(string[] args)
    {
        string directory = args[0]; Directory.CreateDirectory(directory);
        Tests();
        var payload = new { seed = Seed, runtime = RuntimeInformation.FrameworkDescription, assertions, failures, results,
            native_unity_executed = false, note = "Actual C# runs against explicit Unity API facades, not native Unity execution." };
        File.WriteAllText(Path.Combine(directory, "integer-tests.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        var fixture = Settings(new[] { "GameplayTagManager", "RuntimeTag", "__arglist", "registry", "Tags", "ContentHash", "A.B", "A_B" });
        fixture.SetEditorOptions(true, true, false, "Generated", "Tags", "Assets/Bindings.cs");
        File.WriteAllText(Path.Combine(directory, "GeneratedBindings.cs"), GameplayTagCodeGenerator.BuildSource(fixture));
        Console.WriteLine("INTEGER TESTS groups=" + results.Count + " assertions=" + assertions + " failures=" + failures);
        return failures == 0 ? 0 : 1;
    }
}
