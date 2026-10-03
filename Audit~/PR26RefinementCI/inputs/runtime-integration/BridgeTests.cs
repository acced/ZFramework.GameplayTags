using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using GameplayTags;
using GameplayTags.Experiments;

// Managed behavioral tests. UnityEngine is an explicitly separate API facade.
// Production methods are loaded from the referenced GameplayTags DLL.
internal static class BridgeTests
{
    private static long checks;
    private static int checksum;
    private static object sink;
    private static readonly BitmapLayout[] Layouts = { BitmapLayout.Micro, BitmapLayout.Dense, BitmapLayout.Auto };
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    private static void Throws<T>(Action action, string message) where T : Exception
    { bool thrown = false; try { action(); } catch (T) { thrown = true; } Check(thrown, message); }

    private static TagRegistry Registry(IEnumerable<string> names, bool initialize = false)
    {
        var settings = UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        typeof(GameplayTagSettings).GetMethod("ReplaceAll", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(settings, new object[] {
            names.Select(x => new GameplayTagDefinition(x, "", "Default", false, true)).ToList(),
            new List<GameplayTagRedirect>(), new List<GameplayTagSource> { new GameplayTagSource("Default", "", false) }
        });
        if (!initialize) return TagRegistry.Create(settings);
        GameplayTagManager.Initialize(settings, true);
        return GameplayTagManager.CurrentRegistry;
    }

    private static void Ranges()
    {
        var random = new Random(260210);
        foreach (int n in new[] { 0, 1, 15, 16, 17, 31, 32, 63, 64, 65, 129 })
        {
            TagRegistry registry = Registry(Enumerable.Range(0, n).Select(i => "T" + i.ToString("D3")));
            for (int pattern = 0; pattern < 8; pattern++)
            {
                var present = new bool[n];
                for (int i = 0; i < n; i++) present[i] = pattern == 0 ? false : pattern == 1 ? true : pattern == 2 ? i == n - 1 : random.Next(5) == 0;
                var prefix = new int[n + 1];
                for (int i = 0; i < n; i++) prefix[i + 1] = prefix[i] + (present[i] ? 1 : 0);
                foreach (BitmapLayout layout in Layouts)
                {
                    var set = new DirectArraySet(registry, 0, layout);
                    for (int i = 0; i < n; i++) if (present[i]) set.AddTag(registry.GetTagAt(i));
                    var method = typeof(DirectArraySet).GetMethod("AnyInRange", BindingFlags.Instance | BindingFlags.NonPublic);
                    var any = (Func<int, int, bool>)method.CreateDelegate(typeof(Func<int, int, bool>), set);
                    for (int start = 0; start <= n; start++)
                    for (int end = start; end <= n; end++)
                        Check(any(start, end) == (prefix[end] != prefix[start]), "half-open range " + n + ":" + start + ":" + end + ":" + layout);
                    set.AssertInvariants();
                }
            }
        }
    }

    private static bool Ancestor(string child, string parent) => child == parent || child.StartsWith(parent + ".", StringComparison.Ordinal);
    private static GameplayTagQueryExpression Expression(Random random, string[] names, int depth)
    {
        var result = new GameplayTagQueryExpression((GameplayTagQueryExpressionType)random.Next(1, depth == 0 ? 4 : 7));
        int n = random.Next(5);
        for (int i = 0; i < n; i++)
        {
            if (result.UsesTags) result.AddTag(GameplayTagManager.RequestTag(names[random.Next(names.Length)]));
            else result.AddExpression(Expression(random, names, depth - 1));
        }
        return result;
    }
    private static bool Oracle(GameplayTagQueryExpression expression, HashSet<string> members)
    {
        switch (expression.Type)
        {
            case GameplayTagQueryExpressionType.AnyTagsMatch:
                foreach (GameplayTag tag in expression.Tags) if (members.Any(name => Ancestor(name, tag.Name))) return true;
                return false;
            case GameplayTagQueryExpressionType.AllTagsMatch:
                foreach (GameplayTag tag in expression.Tags) if (!members.Any(name => Ancestor(name, tag.Name))) return false;
                return true;
            case GameplayTagQueryExpressionType.NoTagsMatch:
                foreach (GameplayTag tag in expression.Tags) if (members.Any(name => Ancestor(name, tag.Name))) return false;
                return true;
            case GameplayTagQueryExpressionType.AnyExpressionsMatch: return expression.Expressions.Any(x => Oracle(x, members));
            case GameplayTagQueryExpressionType.AllExpressionsMatch: return expression.Expressions.All(x => Oracle(x, members));
            case GameplayTagQueryExpressionType.NoExpressionsMatch: return !expression.Expressions.Any(x => Oracle(x, members));
            default: throw new Exception("Invalid independent oracle input");
        }
    }

    private static void HierarchyAndQueries()
    {
        var input = new[] { "A", "A.B", "A.B.C", "A-x", "A!x", "Z", "状态.灼烧", "Wide😀.叶" }
            .Concat(Enumerable.Range(0, 140).Select(i => "Boundary.T" + i.ToString("D3")))
            .Concat(Enumerable.Range(0, 32).Select(i => "Tree.Branch" + i.ToString("D2") + ".Leaf"));
        TagRegistry registry = Registry(input, true);
        string[] names = Enumerable.Range(0, registry.Count).Select(i => registry.GetTagAt(i).Name).ToArray();
        var random = new Random(21102026);
        foreach (BitmapLayout layout in Layouts)
        for (int sample = 0; sample < 80; sample++)
        {
            var members = new HashSet<string>(names.Where(x => random.Next(7) == 0), StringComparer.Ordinal);
            var set = new DirectArraySet(registry, 0, layout);
            var reference = new RuntimeTagSet(registry, 0, sample % 2 == 0 ? TagSetStorage.Sparse : TagSetStorage.Dense);
            foreach (string name in members) { set.AddTag(registry.Resolve(name)); reference.AddTag(registry.Resolve(name)); }
            foreach (string name in names)
            {
                RuntimeTag handle = registry.Resolve(name);
                bool expected = members.Any(x => Ancestor(x, name));
                Check(set.HasTag(handle) == expected, "hierarchy/string oracle");
                Check(set.HasTag(handle) == reference.HasTag(handle), "hierarchy/runtime comparison");
                Check(set.HasTagExact(handle) == members.Contains(name), "exact distinct from hierarchy");
            }
            for (int queryIndex = 0; queryIndex < 30; queryIndex++)
            {
                GameplayTagQueryExpression expression = Expression(random, names, 3);
                var query = new GameplayTagQuery(expression).Freeze(registry);
                Check(set.Matches(query) == Oracle(expression, members), "query/string oracle");
                Check(set.Matches(query) == query.Matches(reference), "query/runtime comparison");
            }
            for (int type = 1; type <= 6; type++)
            {
                var emptyExpression = new GameplayTagQueryExpression((GameplayTagQueryExpressionType)type);
                var query = new GameplayTagQuery(emptyExpression).Freeze(registry);
                Check(set.Matches(query) == Oracle(emptyExpression, members), "empty quantifier semantics");
            }
            Check(!set.HasTag(default), "default handle");
            Check(!set.Matches(null), "null query");
            var emptyQuery = new GameplayTagQuery().Freeze(registry);
            Check(!set.Matches(emptyQuery), "empty frozen query");
            Check(!emptyQuery.Matches(null), "old null call remains source compatible");
            set.AssertInvariants();
        }

        var owned = new DirectArraySet(registry, 0, BitmapLayout.Micro);
        owned.AddTag(registry.Resolve("A.B.C"));
        var mutable = GameplayTagQueryExpression.AllTagsMatch().AddTag(GameplayTagManager.RequestTag("A"));
        var frozen = new GameplayTagQuery(mutable).Freeze(registry);
        mutable.AddTag(GameplayTagManager.RequestTag("Z"));
        Check(owned.Matches(frozen), "frozen query independent of source mutation");
        Check(!owned.Matches(new GameplayTagQuery(mutable).Freeze(registry)), "new freeze observes mutation");
        TagRegistry foreign = Registry(input, true);
        Throws<ArgumentException>(() => owned.HasTag(foreign.Resolve("A")), "foreign hierarchy handle rejected");
        var foreignEmpty = new GameplayTagQuery().Freeze(foreign);
        Throws<ArgumentException>(() => owned.Matches(foreignEmpty), "foreign empty query rejected before evaluation");
        Check(owned.Matches(frozen), "old snapshot valid after registry replacement");
        owned.Clear();
        Check(!owned.HasTag(registry.Resolve("A")), "clear reflected in hierarchy");
        Check(!owned.Matches(frozen), "clear reflected in query");
    }

    private static object Allocations()
    {
        TagRegistry registry = Registry(new[] { "A.B.C", "Z" }, true);
        var query = new GameplayTagQuery(GameplayTagQueryExpression.AllTagsMatch().AddTag(GameplayTagManager.RequestTag("A"))).Freeze(registry);
        RuntimeTag parent = registry.Resolve("A");
        RuntimeTag child = registry.Resolve("A.B.C");
        var rows = new List<object>();
        foreach (BitmapLayout layout in Layouts)
        {
            var set = DirectArraySet.FromSortedUnique(registry, new[] { child }, storage: layout);
            for (int warm = 0; warm < 20000; warm++) checksum += set.HasTag(parent) && set.Matches(query) ? 1 : 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            sink = new byte[4096];
            long positive = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(positive > 0, "allocation counter positive control");
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20000; i++) checksum += set.HasTag(parent) && set.Matches(query) ? 1 : 0;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated == 0, "hierarchy/query bridge allocated " + layout);
            rows.Add(new { layout = layout.ToString(), bytes = allocated, positiveControlBytes = positive, iterations = 20000 });
        }
        return rows;
    }

    static int Main(string[] args)
    {
        try
        {
            Ranges();
            HierarchyAndQueries();
            object allocations = Allocations();
            Check(checksum == 120000, "allocation-loop checksum");
            var result = new { pass = true, checks, checksum, allocations, library = typeof(DirectArraySet).Assembly.Location, framework = typeof(DirectArraySet).Assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>().FrameworkName, sourceCompatibility = "query.Matches(null) compiles; original overload remains unique" };
            string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(args[0], json); Console.WriteLine(json); return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(args[0], JsonSerializer.Serialize(new { pass = false, checks, error = error.ToString() }));
            Console.WriteLine(error); return 1;
        }
    }
}
