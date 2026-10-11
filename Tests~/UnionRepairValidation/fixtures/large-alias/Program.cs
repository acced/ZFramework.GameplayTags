using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using GameplayTags;

// Frozen untimed supplement. Construction uses public APIs only. Reflection is
// read-only observation of the existing owned-state schema, never test setup.
internal static class Program
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly FieldInfo Buckets = typeof(TagStorage).GetField("m_Buckets", Fields);
    static readonly FieldInfo Mask = typeof(TagStorage).GetField("m_BucketMask", Fields);
    static GameplayTag[] Leaves, Ancestors, SeedTips, Dirty;
    static GameplayTag Keep, Wrapped, Spare;
    static IGameplayTagContainer Legacy;
    static BinaryWriter Evidence;
    static int Cases, Observations, PairChecks, IdentityChecks, ImmutabilityChecks, GrowthCases, RetainedCases, SparseCases;
    static readonly Dictionary<string, int> Routes = new Dictionary<string, int>();

    sealed class NormalView : GameplayTagContainer, IGameplayTagContainer
    {
        readonly IGameplayTagContainer inner;
        internal NormalView(IGameplayTagContainer value) { inner = value; }
        GameplayTagContainerIndices IGameplayTagContainer.Indices => inner.Indices;
    }
    static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    static void Register()
    {
        var registrations = new List<GameplayTagRegistration>();
        var chainNames = new List<string>(); var tips = new List<string>();
        for (int branch = 0; branch < 4; branch++)
        {
            string name = "A" + branch;
            for (int depth = 0; depth < 1024; depth++)
            {
                if (depth != 0) name += ".N";
                if (depth < 1023) chainNames.Add(name);
            }
            tips.Add(name); registrations.Add(new GameplayTagRegistration(name));
        }
        for (int i = 0; i < 4096; i++) registrations.Add(new GameplayTagRegistration("F.G" + (i % 16).ToString("D2") + ".L" + i.ToString("D4")));
        for (int i = 0; i < 8192; i++) registrations.Add(new GameplayTagRegistration("D.L" + i.ToString("D4")));
        foreach (string name in new[] { "Keep", "Wrapped", "Spare.Leaf" }) registrations.Add(new GameplayTagRegistration(name));
        GameplayTagManager.Initialize(registrations.ToArray());
        Leaves = Enumerable.Range(0, 4096).Select(i => GameplayTagManager.RequestTag("F.G" + (i % 16).ToString("D2") + ".L" + i.ToString("D4"))).OrderBy(t => t.RuntimeIndex).ToArray();
        Ancestors = chainNames.Select(GameplayTagManager.RequestTag).OrderBy(t => t.RuntimeIndex).ToArray();
        SeedTips = tips.Select(GameplayTagManager.RequestTag).OrderBy(t => t.RuntimeIndex).ToArray();
        Dirty = Enumerable.Range(0, 8192).Select(i => GameplayTagManager.RequestTag("D.L" + i.ToString("D4"))).ToArray();
        Keep = GameplayTagManager.RequestTag("Keep"); Wrapped = GameplayTagManager.RequestTag("Wrapped"); Spare = GameplayTagManager.RequestTag("Spare.Leaf");
        // Existing public stale-Copy continuation recipe, with independent tag names.
        var counted = new GameplayTagCountContainer(); counted.AddTag(Keep);
        for (int i = 0; i < 1023; i++) counted.AddTag(Dirty[i]);
        counted.AddTag(Wrapped, int.MaxValue); counted.AddTag(Wrapped, int.MaxValue); counted.AddTag(Wrapped, 2); counted.AddTag(Wrapped, 1); counted.RemoveTag(Wrapped, 1);
        for (int i = 1022; i >= 0; i--) counted.RemoveTag(Dirty[i]);
        Legacy = new NormalView(counted);
    }
    static GameplayTagContainer MakeSeed(int history, bool descending, bool cached)
    {
        var result = new GameplayTagContainer();
        if (history != 0)
        {
            foreach (var tag in Dirty) result.AddTag(tag);
            result.Clear();
            if (history == 2) result.EnsureCapacity(32768);
        }
        foreach (var tag in descending ? SeedTips.Reverse() : SeedTips) result.AddTag(tag);
        if (cached) result.OnBeforeSerialize();
        return result;
    }
    static GameplayTagContainer MakeSource(int e, int shape, bool descending, bool cached)
    {
        var result = new GameplayTagContainer();
        // A public removed history leaves real inactive source tails to protect.
        for (int i = 0; i < 37; i++) result.AddTag(Dirty[i]);
        if (cached) result.OnBeforeSerialize();
        result.Clear();
        GameplayTag[] pool = shape == 0 ? Leaves : Ancestors;
        for (int i = 0; i < e; i++) result.AddTag(pool[descending ? e - 1 - i : i]);
        return result;
    }
    static object[] Refs(IGameplayTagContainer container)
    {
        var storage = container.Indices.Storage;
        return new object[] { storage, storage.Entries, storage.ExplicitIndices, Buckets.GetValue(storage), (container as GameplayTagContainer)?.m_SerializedExplicitTags };
    }
    static string Identity(object[] before, IGameplayTagContainer after)
    {
        var current = Refs(after); var text = new StringBuilder();
        for (int i = 0; i < before.Length; i++) { text.Append(ReferenceEquals(before[i], current[i]) ? 'S' : 'C'); IdentityChecks++; }
        return text.ToString();
    }
    static string Capture(Func<string> observation)
    {
        try { return observation(); } catch (Exception error) { return "THROW:" + error.GetType().FullName; }
    }
    static string Enumeration(IGameplayTagContainer container, bool explicitOnly)
    {
        var enumerator = explicitOnly ? container.GetExplicitTags() : container.GetTags();
        var ids = new List<int>(); var storage = container.Indices.Storage;
        while (enumerator.MoveNext())
        {
            if (ids.Count > storage.Entries.Length + storage.ExplicitIndices.Length + 2) return "LIMIT";
            ids.Add(enumerator.Current.RuntimeIndex);
        }
        return string.Join(",", ids);
    }
    static byte[] State(IGameplayTagContainer container)
    {
        using (var bytes = new MemoryStream())
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true))
        {
            var s = container.Indices.Storage;
            writer.Write(s.Count); writer.Write(s.ExplicitCount); writer.Write((int)Mask.GetValue(s));
            writer.Write(s.Entries.Length);
            foreach (var entry in s.Entries) { writer.Write(entry.Id); writer.Write(entry.Count); writer.Write(entry.ExplicitCount); writer.Write(entry.ExplicitIndex); }
            writer.Write(s.ExplicitIndices.Length); foreach (int index in s.ExplicitIndices) writer.Write(index);
            var buckets = (int[])Buckets.GetValue(s); writer.Write(buckets.Length); foreach (int bucket in buckets) writer.Write(bucket);
            var cache = (container as GameplayTagContainer)?.m_SerializedExplicitTags;
            writer.Write(cache != null);
            if (cache != null)
            {
                writer.Write(cache.Capacity); writer.Write(cache.Count);
                foreach (string text in cache) { writer.Write(text != null); if (text != null) writer.Write(text); }
            }
            writer.Write(Capture(() => container.TagCount + "/" + container.ExplicitTagCount + "/" + container.IsEmpty));
            writer.Write(Capture(() => Enumeration(container, false))); writer.Write(Capture(() => Enumeration(container, true)));
            foreach (var tag in new[] { SeedTips[0], SeedTips[3], Leaves[0], Leaves[2999], Ancestors[0], Ancestors[2999], Dirty[0], Dirty[8191], Keep, Wrapped, Spare })
                writer.Write(Capture(() => tag.RuntimeIndex + ":" + s.Contains(tag.RuntimeIndex) + "/" + s.ContainsExplicit(tag.RuntimeIndex) + "/" + s.GetCount(tag.RuntimeIndex) + "/" + s.GetExplicitCount(tag.RuntimeIndex)));
            writer.Flush(); return bytes.ToArray();
        }
    }
    static void Equal(byte[] expected, byte[] actual, string context)
    {
        Check(expected.Length == actual.Length, context + " state byte length " + expected.Length + " != " + actual.Length);
        for (int i = 0; i < expected.Length; i++) if (expected[i] != actual[i]) throw new Exception(context + " first state byte mismatch at " + i);
        PairChecks++;
    }
    static void Emit(string id, string stage, IGameplayTagContainer value, string exception, string identity)
    {
        byte[] state = State(value);
        Evidence.Write(id); Evidence.Write(stage); Evidence.Write(exception ?? ""); Evidence.Write(identity ?? ""); Evidence.Write(state.Length); Evidence.Write(state); Observations++;
    }
    static void Pair(string id, string stage, GameplayTagContainer expected, GameplayTagContainer actual, Action reference, Action operation, bool allowException = false)
    {
        var er = Refs(expected); var ar = Refs(actual); string ee = null, ae = null;
        try { reference(); } catch (Exception error) { ee = error.GetType().FullName; }
        try { operation(); } catch (Exception error) { ae = error.GetType().FullName; }
        Check(ee == ae && (allowException || ee == null), id + "/" + stage + " exception mismatch or unexpected exception: " + ee + "/" + ae);
        Equal(State(expected), State(actual), id + "/" + stage);
        string ei = Identity(er, expected), ai = Identity(ar, actual);
        Check(ei == ai, id + "/" + stage + " identity changes " + ei + " != " + ai);
        Emit(id, stage, actual, ae, ai);
    }
    static void Apply(GameplayTagContainer output, GameplayTagContainer other, bool aliasRight)
    {
#if REUSABLE_UNION
        if (aliasRight) GameplayTagContainer.Union(output, other, output); else GameplayTagContainer.Union(output, output, other);
#else
        FactoryCopy(output, other, aliasRight);
#endif
    }
    static void FactoryCopy(GameplayTagContainer output, GameplayTagContainer other, bool aliasRight)
    {
        if (aliasRight) GameplayTagContainer.Copy(output, GameplayTagContainer.Union(other, output));
        else GameplayTagContainer.Copy(output, GameplayTagContainer.Union(output, other));
    }
    static void Run(int e, bool aliasRight, bool descending, int shape, int history, bool cached)
    {
        string id = "E" + e + "/" + (aliasRight ? "selected-right" : "selected-left") + "/" + (descending ? "descending" : "ascending") + "/" + (shape == 0 ? "disjoint" : "promotion") + "/" + new[] { "growth", "retained", "sparse" }[history] + "/" + (cached ? "cached" : "null-cache");
        var expected = MakeSeed(history, descending, cached); var actual = MakeSeed(history, descending, cached);
        var es = MakeSource(e, shape, descending, cached); var source = MakeSource(e, shape, descending, cached);
        Equal(State(expected), State(actual), id + "/setup-output"); Equal(State(es), State(source), id + "/setup-source");
        var storage = actual.Indices.Storage; int bucketCount = (int)Mask.GetValue(storage) + 1;
        Check(actual.TagCount == 4096 && actual.TagCount > source.TagCount, id + " not strictly selected seed");
        Check(actual.ExplicitTagCount == 4 && source.ExplicitTagCount == e, id + " unexpected explicit cardinality");
        if (history == 0) { Check(storage.ExplicitIndices.Length == 8 && e + 4 > 8, id + " no genuine explicit growth"); GrowthCases++; }
        else
        {
            Check(storage.ExplicitIndices.Length >= e + 4 && storage.Entries.Length > storage.Count, id + " not retained");
            Check(storage.Entries.Skip(storage.Count).Any(x => x.Id != 0) && storage.ExplicitIndices.Skip(storage.ExplicitCount).Any(x => x != 0), id + " missing public inactive tails");
            if (history == 1) { Check(bucketCount <= Math.Max(32, storage.Count * 8), id + " retained history unexpectedly sparse"); RetainedCases++; }
            else { Check(bucketCount > Math.Max(32, storage.Count * 8), id + " sparse history not sparse"); SparseCases++; }
        }
        // The exact API route is established by the strict seed/count/type state
        // and frozen dispatch source. No private gate or helper is invoked here.
        string route = e <= 8 ? "small" : e <= 1024 ? "stack" : "intrusive-or-original";
        if (!Routes.ContainsKey(route)) Routes[route] = 0; Routes[route]++;
        byte[] sourceBefore = State(source), esBefore = State(es); var sourceRefs = Refs(source); var esRefs = Refs(es);
        Emit(id, "before-output", actual, null, "SSSSS"); Emit(id, "before-source", source, null, "SSSSS");
        Pair(id, "union", expected, actual, () => FactoryCopy(expected, es, aliasRight), () => Apply(actual, source, aliasRight));
        Equal(sourceBefore, State(source), id + "/source-immutable"); Equal(esBefore, State(es), id + "/oracle-source-immutable");
        Check(Identity(sourceRefs, source) == "SSSSS" && Identity(esRefs, es) == "SSSSS", id + " source references changed"); ImmutabilityChecks += 2;
        Emit(id, "after-source", source, null, "SSSSS");
        Pair(id, "duplicate-add", expected, actual, () => expected.AddTag(SeedTips[0]), () => actual.AddTag(SeedTips[0]));
        GameplayTag middle = (shape == 0 ? Leaves : Ancestors)[e / 2];
        Pair(id, "remove-middle", expected, actual, () => expected.RemoveTag(middle), () => actual.RemoveTag(middle));
        Pair(id, "readd-middle", expected, actual, () => expected.AddTag(middle), () => actual.AddTag(middle));
        Pair(id, "add-spare", expected, actual, () => expected.AddTag(Spare), () => actual.AddTag(Spare));
        var ec = new GameplayTagContainer(8192); var ac = new GameplayTagContainer(8192);
        ec.AddTag(Dirty[3]); ac.AddTag(Dirty[3]); if (cached) { ec.OnBeforeSerialize(); ac.OnBeforeSerialize(); }
        byte[] copySource = State(actual); var copySourceRefs = Refs(actual);
        Pair(id, "copy-retained", ec, ac, () => GameplayTagContainer.Copy(ec, expected), () => GameplayTagContainer.Copy(ac, actual));
        Equal(copySource, State(actual), id + "/copy-source-immutable"); Check(Identity(copySourceRefs, actual) == "SSSSS", id + " Copy source references changed"); ImmutabilityChecks++;
        Pair(id, "copy-self", ec, ac, () => GameplayTagContainer.Copy(ec, ec), () => GameplayTagContainer.Copy(ac, ac));
        Pair(id, "copy-remove-spare", ec, ac, () => ec.RemoveTag(Spare), () => ac.RemoveTag(Spare));
        Pair(id, "copy-back", expected, actual, () => GameplayTagContainer.Copy(expected, ec), () => GameplayTagContainer.Copy(actual, ac));
        byte[] legacyBefore = State(Legacy); var legacyRefs = Refs(Legacy);
        Pair(id, "legacy-public-copy", expected, actual, () => GameplayTagContainer.Copy(expected, Legacy), () => GameplayTagContainer.Copy(actual, Legacy), true);
        Equal(legacyBefore, State(Legacy), id + "/legacy-source-immutable"); Check(Identity(legacyRefs, Legacy) == "SSSSS", id + " legacy source references changed"); ImmutabilityChecks++;
        Pair(id, "clear-after-legacy", expected, actual, () => expected.Clear(), () => actual.Clear());
        Pair(id, "regrow-after-legacy", expected, actual, () => { expected.AddTag(Spare); expected.AddTag(SeedTips[3]); }, () => { actual.AddTag(Spare); actual.AddTag(SeedTips[3]); });
        Pair(id, "serialize-regrown", expected, actual, () => expected.OnBeforeSerialize(), () => actual.OnBeforeSerialize());
        Pair(id, "copy-empty", expected, actual, () => GameplayTagContainer.Copy(expected, new GameplayTagContainer()), () => GameplayTagContainer.Copy(actual, new GameplayTagContainer()));
        Cases++; Console.Error.WriteLine("PASS " + id);
    }
    static void Main(string[] args)
    {
        Check(args.Length == 1, "supply fresh evidence stream path");
        string schema = string.Join(",", typeof(TagStorage).GetFields(Fields).Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));
        string entrySchema = string.Join(",", typeof(TagStorage.Entry).GetFields(Fields).Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));
        Check(schema == "Count,Entries,ExplicitCount,ExplicitIndices,m_BucketMask,m_Buckets", "unexpected storage schema: " + schema);
        Check(entrySchema == "Count,ExplicitCount,ExplicitIndex,Id", "unexpected entry schema: " + entrySchema);
        Register();
        using (var file = new FileStream(args[0], FileMode.CreateNew, FileAccess.Write))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        using (Evidence = new BinaryWriter(gzip, Encoding.UTF8))
        {
            Evidence.Write("final-selected-alias-boundaries-v1");
            foreach (int e in new[] { 8, 9, 1024, 1025, 2048, 3000 })
            foreach (bool right in new[] { false, true })
            foreach (bool descending in new[] { false, true })
            foreach (int shape in new[] { 0, 1 })
            foreach (int history in new[] { 0, 1, 2 })
            foreach (bool cached in new[] { false, true }) Run(e, right, descending, shape, history, cached);
        }
        Check(Cases == 288 && Observations == 4896 && GrowthCases == 96 && RetainedCases == 96 && SparseCases == 96, "coverage count drift");
        Console.WriteLine("{\"Cases\":" + Cases + ",\"Observations\":" + Observations + ",\"ExactStateComparisons\":" + PairChecks + ",\"IdentityFieldComparisons\":" + IdentityChecks + ",\"SourceImmutabilityChecks\":" + ImmutabilityChecks + ",\"GrowthCases\":" + GrowthCases + ",\"RetainedCases\":" + RetainedCases + ",\"SparseCases\":" + SparseCases + ",\"SmallCases\":" + Routes["small"] + ",\"StackCases\":" + Routes["stack"] + ",\"LargeCases\":" + Routes["intrusive-or-original"] + ",\"Timing\":false}");
    }
}
