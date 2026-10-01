using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using GameplayTags;
using GameplayTags.Experiments;
using S = GameplayTags.Experiments.__TYPE__;

internal static class BuilderChecks
{
    private static long checks;
    private static object sink;
    private static readonly FieldInfo Entries = typeof(S).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic);
    private static void Check(bool value, string text) { checks++; if (!value) throw new Exception(text); }
    private static TagRegistry Registry()
    {
        var settings = UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        settings.ReplaceAll(Enumerable.Range(0, 20000).Select(i => new GameplayTagDefinition("T" + i.ToString("D6"), "", "Default", false, true)).ToList(),
            new List<GameplayTagRedirect>(), new List<GameplayTagSource> { new GameplayTagSource("Default", "", false) });
        return TagRegistry.Create(settings);
    }
    private static S Make(TagRegistry r, int[] ids, int capacity)
    {
        var s = new S(r, capacity, BitmapLayout.Micro);
        foreach (int id in ids) s.AddTag(r.GetTagAt(id));
        return s;
    }
    private static void Verify(S value, IEnumerable<int> ids)
    {
        int[] expected = ids.Distinct().OrderBy(x => x).ToArray();
        value.AssertInvariants(); Check(value.Count == expected.Length, "Count");
        var actual = new List<int>(); foreach (var tag in value) actual.Add(tag.RuntimeIndex);
        Check(actual.SequenceEqual(expected), "numeric membership and order");
        foreach (int id in expected) Check(value.HasTagExact(value.Registry.GetTagAt(id)), "lookup");
    }
    private static int Capacity(S s) { var array = (Array)Entries.GetValue(s); return array == null ? 1 : array.Length; }
    private static void Relations(TagRegistry r)
    {
        foreach (int n in new[] { 1, 2, 7, 8, 9, 31, 32, 33, 63, 64, 65, 127, 128, 129 })
        foreach (int relation in new[] { 0, 1, 2, 3 })
        foreach (bool retainedArray in new[] { false, true })
        {
            int[] aIds = Enumerable.Range(0, n).Select(i => i * 32 + 1).ToArray();
            int[] bIds = Enumerable.Range(0, n).Select(i => relation == 0 ? i * 32 + 1 : relation == 1 ? i * 32 + 2 : relation == 2 ? i * 32 + 17 : (i + n / 2) * 32 + 1).ToArray();
            var a = Make(r, aIds, retainedArray ? n * 3 : n);
            var b = Make(r, bIds, retainedArray ? n * 3 : n);
            int[] union = aIds.Concat(bIds).Distinct().ToArray();
            int records = union.Select(x => x >> 4).Distinct().Count();
            var output = Make(r, new[] { 19999 }, records);
            int capacity = Capacity(output);
            S.UnionInto(a, b, output); Verify(output, union);
            Check(Capacity(output) == capacity, "Into actual-result record capacity must not grow");
            var left = new S(a); S.UnionInto(left, b, left); Verify(left, union);
            var right = new S(b); S.UnionInto(a, right, right); Verify(right, union);
            var fresh = S.Union(a, b, BitmapLayout.Micro); Verify(fresh, union);
            fresh.AddTag(r.GetTagAt(19998)); Verify(a, aIds); Verify(b, bIds);
            var growth = new S(a); int beforeCapacity = Capacity(growth);
            growth.AppendTags(b); Verify(growth, union);
            int expectedCapacity = records <= beforeCapacity ? beforeCapacity : Math.Min((r.Count + 15) >> 4, Math.Max(records, Math.Max(4, beforeCapacity * 2)));
            Check(Capacity(growth) == expectedCapacity, "growth policy unchanged");
            growth.CopyFrom(a); growth.RemoveTags(b); Verify(growth, aIds.Except(bIds));
            // Move between two physical allocation states without changing the logical one-record input.
            a.Clear(); a.AddTag(r.GetTagAt(33));
            b.Clear(); b.AddTag(r.GetTagAt(34));
            output.Clear(); S.UnionInto(a, b, output); Verify(output, new[] {33,34});
            a.AppendTags(b); Verify(a, new[] {33,34}); a.RemoveTags(b); Verify(a, new[] {33});
            a.RemoveTags(a); Verify(a, Array.Empty<int>());
        }
    }
    private static void Churn(TagRegistry r)
    {
        var random = new Random(20261001);
        var a = new S(r, 129, BitmapLayout.Micro);
        var b = new S(r, 129, BitmapLayout.Micro);
        var expected = new HashSet<int>();
        for (int step = 0; step < 5000; step++)
        {
            b.Clear(); var change = new HashSet<int>();
            for (int i = 0, n = random.Next(130); i < n; i++) { int id = random.Next(r.Count); change.Add(id); b.AddTag(r.GetTagAt(id)); }
            if ((step & 1) == 0) { a.AppendTags(b); expected.UnionWith(change); }
            else { a.RemoveTags(b); expected.ExceptWith(change); }
            if (step % 17 == 0) Verify(a, expected);
            if (step % 31 == 0) { a.Clear(); expected.Clear(); }
        }
        Verify(a, expected);
    }
    private static void Allocation(TagRegistry r)
    {
        int[] av = Enumerable.Range(0, 129).Select(i => i * 32 + 1).ToArray();
        int[] bv = Enumerable.Range(0, 129).Select(i => i * 32 + 17).ToArray();
        var a = Make(r, av, av.Length); var b = Make(r, bv, bv.Length);
        var output = new S(r, 258, BitmapLayout.Micro);
        for (int i = 0; i < 200; i++) { output.CopyFrom(a); output.AppendTags(b); output.RemoveTags(b); S.UnionInto(a,b,output); }
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 3000; i++) { output.CopyFrom(a); output.AppendTags(b); output.RemoveTags(b); S.UnionInto(a,b,output); }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Check(bytes == 0, "prepared growth/merge must not allocate");
        start = GC.GetAllocatedBytesForCurrentThread(); sink = new byte[128];
        long positive = GC.GetAllocatedBytesForCurrentThread() - start;
        Check(positive > 0, "allocation counter positive control");
        Verify(output, av.Concat(bv));
        Console.WriteLine("BUILDER_ALLOCATION bytes="+bytes+" positive="+positive);
    }
    public static void Main(string[] args)
    {
        var r = Registry(); Relations(r); Churn(r); Allocation(r);
        string json = JsonSerializer.Serialize(new { candidate = typeof(S).Name, assertions = checks, failures = 0, native_unity = "not_run" });
        if (args.Length != 0) System.IO.File.WriteAllText(args[0], json);
        Console.WriteLine("BUILDER_TESTS " + json);
    }
}
