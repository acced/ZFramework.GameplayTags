using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using GameplayTags;

internal static class Program
{
    private static string[] Registry;
    private static string Deep;
    private static GameplayTag T(string name) => GameplayTagManager.RequestTag(name);
    private static string Q(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
    private static string Capture(Func<string> read) { try { return read(); } catch (Exception ex) { return "THROW:" + ex.GetType().FullName; } }
    private static string Enum(IGameplayTagContainer value, bool exact)
    {
        var names = new List<string>(); var e = exact ? value.GetExplicitTags() : value.GetTags();
        while (e.MoveNext()) { if (names.Count == 512) return "ENUMERATION-LIMIT"; names.Add(e.Current.Name ?? "<None>"); }
        names.Sort(StringComparer.Ordinal); return "[" + string.Join(",", names.Select(Q)) + "]";
    }
    private static string Snapshot(IGameplayTagContainer value)
    {
        var items = new List<string>();
        items.Add("empty=" + Capture(() => value.IsEmpty.ToString()));
        items.Add("tags=" + Capture(() => value.TagCount.ToString()));
        items.Add("explicit=" + Capture(() => value.ExplicitTagCount.ToString()));
        items.Add("allNames=" + Capture(() => Enum(value, false))); items.Add("explicitNames=" + Capture(() => Enum(value, true)));
        foreach (string name in Registry.Concat(new string[] { null }))
        {
            GameplayTag tag = name == null ? GameplayTag.None : T(name);
            items.Add((name ?? "<None>") + ":" + Capture(() => value.HasTag(tag) + "/" + value.HasTagExact(tag)));
            if (value is IGameplayTagCountContainer counted)
                items.Add((name ?? "<None>") + "#" + Capture(() => counted.GetTagCount(tag) + "/" + counted.GetExplicitTagCount(tag)));
        }
        if (value is GameplayTagContainer set) items.Add("serialized=" + (set.m_SerializedExplicitTags == null ? "null" : "[" + string.Join(",", set.m_SerializedExplicitTags.Select(Q)) + "]"));
        return string.Join(";", items);
    }
    private static GameplayTagCountContainer Source(string state)
    {
        var value = new GameplayTagCountContainer(); GameplayTag a = T("A.Child");
        if (state.StartsWith("healthy-before", StringComparison.Ordinal)) value.AddTag(T("B.C"), 3);
        if (state.StartsWith("many-healthy", StringComparison.Ordinal)) for (int i = 0; i < 9; i++) value.AddTag(T("R" + i), 3);
        value.AddTag(a, int.MaxValue);
        if (state != "max") { value.AddTag(a, int.MaxValue); value.AddTag(a, 2); }
        if (state.Contains("readd")) value.AddTag(a, 1);
        if (state.Contains("remove")) value.RemoveTag(a, 1);
        if (state == "zero-healthy-after") value.AddTag(T("B.C"), 3);
        return value;
    }
    private static GameplayTagContainer Ordinary(string shape)
    {
        var value = new GameplayTagContainer();
        if (shape == "overlap") value.AddTag(T("A.Child"));
        else if (shape == "disjoint") value.AddTag(T("Z"));
        else if (shape == "deep") value.AddTag(T(Deep));
        else if (shape == "wide") for (int i = 0; i < 10; i++) value.AddTag(T("R" + i));
        else if (shape != "empty") throw new Exception("bad shape");
        return value;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Apply(GameplayTagContainer output, IGameplayTagContainer lhs, IGameplayTagContainer rhs)
    {
#if REUSABLE_UNION
        GameplayTagContainer.Union(output, lhs, rhs);
#else
        GameplayTagContainer.Copy(output, GameplayTagContainer.Union(lhs, rhs));
#endif
    }
    private static void Run(string state, string shape, string placement, string wrapping, bool secondCounted)
    {
        var counted = Source(state); IGameplayTagContainer ordinary = secondCounted ? (IGameplayTagContainer)Source(shape) : Ordinary(shape);
        bool alias = placement.StartsWith("alias", StringComparison.Ordinal);
        bool ordinaryLeft = placement.EndsWith("left", StringComparison.Ordinal);
        var output = alias ? (GameplayTagContainer)ordinary : Ordinary("disjoint");
        if (!alias) { output.Clear(); output.AddTag(T("Old")); }
        output.OnBeforeSerialize();
        IGameplayTagContainer ordinaryView = wrapping == "ordinary-proxy" || wrapping == "both-proxy" ? new Proxy(ordinary) : ordinary;
        IGameplayTagContainer countedView = wrapping == "counted-proxy" || wrapping == "both-proxy" ? new Proxy(counted) : (IGameplayTagContainer)counted;
        IGameplayTagContainer lhs = ordinaryLeft ? ordinaryView : countedView, rhs = ordinaryLeft ? countedView : ordinaryView;
        string beforeOutput = Snapshot(output), beforeCounted = Snapshot(counted), beforeOrdinary = Snapshot(ordinary);
        string exception = null;
        try { Apply(output, lhs, rhs); } catch (Exception ex) { exception = ex.GetType().FullName; }
        string afterOutput = Snapshot(output), afterCounted = Snapshot(counted), afterOrdinary = Snapshot(ordinary);
        string drain = "skipped after operation exception";
        if (exception == null)
        {
            string removeException = null;
            try { foreach (string name in Registry) output.RemoveTag(T(name)); } catch (Exception ex) { removeException = ex.GetType().FullName; }
            drain = "throw=" + (removeException ?? "none") + ";" + Snapshot(output);
        }
        string id = (secondCounted ? "both-counted/" : "set-counted/") + state + "/" + shape + "/" + placement + "/" + wrapping;
        Console.WriteLine("{\"Id\":" + Q(id) + ",\"BeforeOutput\":" + Q(beforeOutput) + ",\"BeforeCounted\":" + Q(beforeCounted) + ",\"BeforeOther\":" + Q(beforeOrdinary) +
            ",\"Exception\":" + Q(exception) + ",\"AfterOutput\":" + Q(afterOutput) + ",\"AfterCounted\":" + Q(afterCounted) + ",\"AfterOther\":" + Q(afterOrdinary) + ",\"Drain\":" + Q(drain) + "}");
    }
    private static void Main()
    {
        Deep = "Seed" + string.Concat(Enumerable.Repeat(".N", 16));
        Registry = new[] { "A", "A.Child", "B", "B.C", "Z", "Old", "Seed", Deep }.Concat(Enumerable.Range(0, 12).Select(i => "R" + i)).ToArray();
        GameplayTagManager.Initialize(Registry.Select(n => new GameplayTagRegistration(n)).ToArray());
        string[] states = { "max", "zero", "healthy-before-zero", "zero-healthy-after", "readd", "readd-remove", "healthy-before-readd-remove", "many-healthy-readd-remove" };
        foreach (string state in states)
        foreach (string shape in new[] { "empty", "overlap", "disjoint", "deep", "wide" })
        foreach (string placement in new[] { "nonalias-left", "nonalias-right", "alias-left", "alias-right" })
        foreach (string wrapping in new[] { "plain", "ordinary-proxy", "counted-proxy", "both-proxy" })
            Run(state,shape,placement,wrapping,false);
        foreach (string state in states)
        foreach (string second in new[] { "max", "readd-remove" })
        foreach (string placement in new[] { "nonalias-left", "nonalias-right" })
        foreach (string wrapping in new[] { "plain", "both-proxy" })
            Run(state,second,placement,wrapping,true);
    }
    private sealed class Proxy : IGameplayTagContainer
    {
        private readonly IGameplayTagContainer Inner;
        internal Proxy(IGameplayTagContainer inner) { Inner = inner; }
        public bool IsEmpty => Inner.IsEmpty; public int ExplicitTagCount => Inner.ExplicitTagCount; public int TagCount => Inner.TagCount; public GameplayTagContainerIndices Indices => Inner.Indices;
        public void AddTag(GameplayTag tag) => Inner.AddTag(tag); public void RemoveTag(GameplayTag tag) => Inner.RemoveTag(tag);
        public GameplayTagEnumerator GetTags() => Inner.GetTags(); public GameplayTagEnumerator GetExplicitTags() => Inner.GetExplicitTags();
        public void AddTags<T>(in T other) where T : IGameplayTagContainer => Inner.AddTags(other); public void RemoveTags<T>(in T other) where T : IGameplayTagContainer => Inner.RemoveTags(other);
        public void GetParentTags(GameplayTag tag, List<GameplayTag> output) => Inner.GetParentTags(tag,output); public void GetChildTags(GameplayTag tag, List<GameplayTag> output) => Inner.GetChildTags(tag,output);
        public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> output) => Inner.GetExplicitParentTags(tag,output); public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> output) => Inner.GetExplicitChildTags(tag,output);
        public void Clear() => Inner.Clear(); public IEnumerator<GameplayTag> GetEnumerator() => Inner.GetEnumerator(); IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
