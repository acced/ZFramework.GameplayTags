using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GameplayTags;

internal static class Program
{
    private static readonly List<string> Universe = new List<string>();
    private static string[] Chain;
    private static string Seed;
    private static int Checks;
    private static GameplayTag T(string name) => GameplayTagManager.RequestTag(name);
    private static void Check(bool condition, string context) { Checks++; if (!condition) throw new Exception(context); }
    private static IEnumerable<string> Path(string name)
    {
        for (int i = 0; i < name.Length; i++) if (name[i] == '.') yield return name.Substring(0, i);
        yield return name;
    }
    private static Dictionary<string, int> Counts(IEnumerable<string> names)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string name in names) foreach (string p in Path(name)) { counts.TryGetValue(p, out int n); counts[p] = n + 1; }
        return counts;
    }
    private static HashSet<string> Names(IGameplayTagContainer value)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (GameplayTag tag in value.GetExplicitTags()) Check(names.Add(tag.Name), "duplicate explicit enumeration");
        return names;
    }
    private static GameplayTagContainer Set(IEnumerable<string> names)
    {
        var result = new GameplayTagContainer(); foreach (string name in names) result.AddTag(T(name)); return result;
    }
    private static void Assert(IGameplayTagContainer result, IEnumerable<string> expected)
    {
        var names = new HashSet<string>(expected, StringComparer.Ordinal); var counts = Counts(names);
        Check(result.ExplicitTagCount == names.Count && result.TagCount == counts.Count, "cardinalities");
        Check(Names(result).SetEquals(names), "explicit names");
        var closure = new HashSet<string>(StringComparer.Ordinal);
        foreach (GameplayTag tag in result.GetTags()) Check(closure.Add(tag.Name), "duplicate closure enumeration");
        Check(closure.SetEquals(counts.Keys), "closure names");
        foreach (string name in counts.Keys) Check(result.HasTag(T(name)) && result.HasTagExact(T(name)) == names.Contains(name), "public membership");
    }
    private static void Drain(GameplayTagContainer result, IEnumerable<string> expected, bool reverse)
    {
        var order = expected.Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        if (reverse) Array.Reverse(order);
        Assert(result, order); var counts = Counts(order); int remaining = order.Length;
        foreach (string name in order)
        {
            result.AddTag(T(name)); result.RemoveTag(T(name)); result.RemoveTag(T(name)); remaining--;
            foreach (string p in Path(name)) if (--counts[p] == 0) counts.Remove(p);
            Check(result.ExplicitTagCount == remaining && result.TagCount == counts.Count, "drained ancestor contribution counts");
            Check(!result.HasTagExact(T(name)), "drained explicit absence");
        }
        Check(result.IsEmpty && result.TagCount == 0, "fully drained");
        result.AddTag(T(Chain[159])); result.AddTag(T(Chain[0])); result.RemoveTag(T(Chain[159]));
        Assert(result, new[] { Chain[0] }); result.RemoveTag(T(Chain[0])); Check(result.TagCount == 0, "reuse after drain");
    }
    private static void Shuffle<T>(T[] values, Random random)
    {
        for (int i = values.Length - 1; i > 0; i--) { int j = random.Next(i + 1); T t = values[i]; values[i] = values[j]; values[j] = t; }
    }
    private static void Exercise(string[] left, string[] right)
    {
        var a = Set(left); var b = Set(right); var expected = new HashSet<string>(left, StringComparer.Ordinal); expected.UnionWith(right);
        var ca = new GameplayTagCountContainer(); var cb = new GameplayTagCountContainer();
        foreach (string name in left) ca.AddTag(T(name), 3);
        foreach (string name in right) cb.AddTag(T(name), 5);
        foreach (bool reverse in new[] { false, true })
        {
            Drain(GameplayTagContainer.Union(a, b), expected, reverse);
            Drain(GameplayTagContainer.Union(b, a), expected, reverse);
            Drain(GameplayTagContainer.Union(ca, b), expected, reverse);
            Drain(GameplayTagContainer.Union(a, cb), expected, reverse);
            Drain(GameplayTagContainer.Union(ca, cb), expected, reverse);
            Drain(GameplayTagContainer.Union(new Proxy(a), new Proxy(b)), expected, reverse);
            var added = a.Clone(); added.AddTags(b); Drain(added, expected, reverse);
            added = a.Clone(); added.AddTags(cb); Drain(added, expected, reverse);
            added = a.Clone(); added.AddTags(new Proxy(b)); Drain(added, expected, reverse);
#if REUSABLE_UNION
            var output = Set(new[] { Seed }); output.EnsureCapacity(8192); output.OnBeforeSerialize();
            GameplayTagContainer.Union(output, a, b); Check(output.m_SerializedExplicitTags.Count == 0, "reused serialized cache cleared"); Drain(output, expected, reverse);
            GameplayTagContainer.Union(output, ca, cb); Drain(output, expected, reverse);
            foreach (bool aliasLeft in new[] { false, true })
            {
                var alias = aliasLeft ? a.Clone() : b.Clone(); alias.OnBeforeSerialize();
                GameplayTagContainer.Union(alias, aliasLeft ? alias : a, aliasLeft ? b : alias);
                Check(alias.m_SerializedExplicitTags.Count == 0, "aliased serialized cache cleared"); Drain(alias, expected, reverse);
                alias = aliasLeft ? a.Clone() : b.Clone();
                GameplayTagContainer.Union(alias, new Proxy(aliasLeft ? alias : a), new Proxy(aliasLeft ? b : alias));
                Drain(alias, expected, reverse);
            }
            var self = a.Clone(); GameplayTagContainer.Union(self, self, new Proxy(self)); Drain(self, left, reverse);
            GameplayTagContainer.Union(output, new GameplayTagContainer(), new GameplayTagContainer()); Check(output.IsEmpty && output.TagCount == 0, "empty replaces reused output");
#endif
        }
        Assert(a, left); Assert(b, right); Assert(ca, left); Assert(cb, right);
        var leftCounts = Counts(left); var rightCounts = Counts(right);
        foreach (string name in leftCounts.Keys) Check(ca.GetTagCount(T(name)) == leftCounts[name] * 3, "counted left source preserved");
        foreach (string name in rightCounts.Keys) Check(cb.GetTagCount(T(name)) == rightCounts[name] * 5, "counted right source preserved");
    }
    private static void Main()
    {
        Chain = new string[160]; Chain[0] = "Deep";
        for (int i = 1; i < Chain.Length; i++) Chain[i] = Chain[i - 1] + ".N";
        Seed = "Seed" + string.Concat(Enumerable.Repeat(".N", 320));
        var declarations = new List<string> { Chain[159], Seed };
        Universe.AddRange(Chain);
        for (int i = 0; i < 96; i++) { string n = Chain[(i * 37) % 160] + ".Branch" + i + ".Leaf"; declarations.Add(n); Universe.Add(n); }
        for (int i = 0; i < 128; i++) { string n = "Forest.R" + i % 8 + ".B" + i % 17 + ".L" + i; declarations.Add(n); Universe.Add(n); }
        GameplayTagManager.Initialize(declarations.Select(n => new GameplayTagRegistration(n)).ToArray());
        foreach (int n in new[] { 9, 16, 17, 32, 33, 64, 127, 128, 160 })
        {
            string[] forward = Chain.Take(n).ToArray(); string[] backward = forward.Reverse().ToArray(); string[] shuffled = (string[])forward.Clone(); Shuffle(shuffled, new Random(19011 + n));
            Exercise(new[] { Seed }, forward); Exercise(new[] { Seed }, backward); Exercise(new[] { Seed, Chain[0], Chain[n / 2] }, shuffled);
        }
        var random = new Random(71531);
        for (int i = 0; i < 16; i++)
        {
            string[] order = Universe.ToArray(); Shuffle(order, random);
            string[] left = order.Where((n, k) => k % 5 == i % 5).Concat(new[] { Seed }).ToArray();
            string[] right = order.Where((n, k) => k % 3 != i % 3).ToArray();
            Exercise(left, right);
        }
        AddTagsViews();
        Console.WriteLine("PASS Union candidates: " + Checks + " name/count assertions; both removal orders, counted normalization, random deep forest, remainder propagation, source preservation"
#if REUSABLE_UNION
            + ", reusable output, both aliases, proxy aliases, serialized cache"
#endif
            + ".");
    }
    private static void AddTagsViews()
    {
        var indices = Set(new[] { Seed });
        var view = Set(Chain.Where((n, i) => i % 7 == 0));
        foreach (int mode in new[] { 0, 1, 2, 3 })
        {
            var proxy = new Proxy(indices, view, mode);
            var expected = Names(proxy); expected.Add(Chain[159]);
            var output = Set(new[] { Chain[159] });
            output.AddTags(proxy); Drain(output, expected, false);
            Assert(indices, new[] { Seed }); Assert(view, Chain.Where((n, i) => i % 7 == 0));
        }
        // The original Indices-based alias short circuit has precedence over
        // custom enumeration, and must remain so after bulk optimization.
        var alias = Set(new[] { Seed });
        alias.AddTags(new Proxy(alias, view)); Assert(alias, new[] { Seed });
        // A different Indices view can nevertheless enumerate the destination.
        // The original enumeration only attempts duplicate insertions.
        alias.AddTags(new Proxy(indices, alias)); Assert(alias, new[] { Seed });
        var emptyView = new Proxy(new GameplayTagContainer(), view, 3);
        alias.AddTags(emptyView); Assert(alias, new[] { Seed });
    }
    private sealed class Proxy : IGameplayTagContainer
    {
        private readonly GameplayTagContainer Inner;
        private readonly GameplayTagContainer View;
        private readonly int Mode;
        public Proxy(GameplayTagContainer inner, GameplayTagContainer view = null, int mode = 0) { Inner = inner; View = view ?? inner; Mode = mode; }
        public bool IsEmpty => Inner.IsEmpty;
        public int ExplicitTagCount => Inner.ExplicitTagCount;
        public int TagCount => Inner.TagCount;
        public GameplayTagContainerIndices Indices => Inner.Indices;
        public void AddTag(GameplayTag tag) => Inner.AddTag(tag);
        public void RemoveTag(GameplayTag tag) => Inner.RemoveTag(tag);
        public GameplayTagEnumerator GetTags() => Inner.GetTags();
        public GameplayTagEnumerator GetExplicitTags()
        {
            if (Mode == 3) return default;
            GameplayTagEnumerator result = Mode == 2 ? View.GetTags() : View.GetExplicitTags();
            if (Mode == 1) result.MoveNext();
            return result;
        }
        public void AddTags<T>(in T other) where T : IGameplayTagContainer => Inner.AddTags(other);
        public void RemoveTags<T>(in T other) where T : IGameplayTagContainer => Inner.RemoveTags(other);
        public void GetParentTags(GameplayTag tag, List<GameplayTag> output) => Inner.GetParentTags(tag, output);
        public void GetChildTags(GameplayTag tag, List<GameplayTag> output) => Inner.GetChildTags(tag, output);
        public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> output) => Inner.GetExplicitParentTags(tag, output);
        public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> output) => Inner.GetExplicitChildTags(tag, output);
        public void Clear() => Inner.Clear();
        public IEnumerator<GameplayTag> GetEnumerator() => ((IEnumerable<GameplayTag>)Inner).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
