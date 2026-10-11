// Bounded, untimed packing-limit differential. Production sources are not instrumented.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using GameplayTags;

internal static class Program
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly FieldInfo Buckets = typeof(TagStorage).GetField("m_Buckets", Fields);
    static readonly FieldInfo Mask = typeof(TagStorage).GetField("m_BucketMask", Fields);
    static readonly FieldInfo StringItems = typeof(List<string>).GetField("_items", Fields);
    static readonly string[] CountArrays = { "m_Callbacks", "m_Events", "m_Snapshot" };
    static readonly string[] CountScalars = { "m_CallbackCount", "m_EventCount", "m_NextEvent", "m_BatchDepth" };
    static GameplayTag[] Leaves;
    static GameplayTag Common, SeedA, SeedB, Old, Spare;
    static int Observations, ImmutabilityChecks;
    sealed class Snapshot
    {
        internal string Storage, Public, Auxiliary, Full;
        internal int Count, ExplicitCount, EntriesCapacity, ExplicitCapacity, BucketCapacity, BucketMask, ParentTotal, ParentExplicit;
    }
    static void Check(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
    static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
    static string Digest(Action<BinaryWriter> write)
    {
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) write(writer);
            stream.Position = 0;
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
    static void IntArray(BinaryWriter writer, int[] values)
    {
        writer.Write(values.Length);
        for (int i = 0; i < values.Length; i++) writer.Write(values[i]);
    }
    static void WriteEnumeration(BinaryWriter writer, IGameplayTagContainer c, bool exact)
    {
        var enumerator = exact ? c.GetExplicitTags() : c.GetTags();
        int count = 0;
        while (enumerator.MoveNext())
        {
            Check(++count <= c.TagCount, "enumeration exceeds closure");
            writer.Write(enumerator.Current.RuntimeIndex);
        }
        Check(count == (exact ? c.ExplicitTagCount : c.TagCount), "enumeration count mismatch");
        writer.Write(-1);
    }
    static Snapshot Capture(IGameplayTagContainer c)
    {
        var s = c.Indices.Storage;
        int[] buckets = (int[])Buckets.GetValue(s);
        var result = new Snapshot {
            Count = s.Count, ExplicitCount = s.ExplicitCount, EntriesCapacity = s.Entries.Length,
            ExplicitCapacity = s.ExplicitIndices.Length, BucketCapacity = buckets.Length,
            BucketMask = (int)Mask.GetValue(s), ParentTotal = s.GetCount(Common.RuntimeIndex), ParentExplicit = s.GetExplicitCount(Common.RuntimeIndex)
        };
        result.Storage = Digest(w => {
            w.Write("TagStorage-v1-complete"); w.Write(s.Count); w.Write(s.ExplicitCount); w.Write(result.BucketMask);
            w.Write(s.Entries.Length);
            foreach (var entry in s.Entries) { w.Write(entry.Id); w.Write(entry.Count); w.Write(entry.ExplicitCount); w.Write(entry.ExplicitIndex); }
            IntArray(w, s.ExplicitIndices); IntArray(w, buckets);
        });
        result.Public = Digest(w => {
            w.Write("Public-v1"); w.Write(c.IsEmpty); w.Write(c.TagCount); w.Write(c.ExplicitTagCount);
            w.Write(c.Indices.IsCreated); w.Write(c.Indices.IsEmpty); w.Write(c.Indices.TagCount); w.Write(c.Indices.ExplicitTagCount);
            WriteEnumeration(w, c, false); WriteEnumeration(w, c, true);
            int enumerated = 0;
            foreach (GameplayTag tag in c) { Check(++enumerated <= c.TagCount, "IEnumerable limit"); w.Write(tag.RuntimeIndex); }
            Check(enumerated == c.TagCount, "IEnumerable count"); w.Write(-1);
            for (int id = 0; id <= GameplayTagManager.TagCount; id++) {
                GameplayTag tag = GameplayTagManager.GetTagFromRuntimeIndex(id);
                w.Write(id); w.Write(c.HasTag(tag)); w.Write(c.HasTagExact(tag));
                if (c is GameplayTagCountContainer counted) { w.Write(counted.GetTagCount(tag)); w.Write(counted.GetExplicitTagCount(tag)); }
            }
            foreach (GameplayTag tag in new[] { Common, Leaves[0], Leaves[Leaves.Length - 1], SeedA, Spare }) {
                var selected = new List<GameplayTag>();
                c.GetParentTags(tag, selected); w.Write(selected.Count); foreach (var item in selected) w.Write(item.RuntimeIndex); selected.Clear();
                c.GetExplicitParentTags(tag, selected); w.Write(selected.Count); foreach (var item in selected) w.Write(item.RuntimeIndex); selected.Clear();
                c.GetChildTags(tag, selected); w.Write(selected.Count); foreach (var item in selected) w.Write(item.RuntimeIndex); selected.Clear();
                c.GetExplicitChildTags(tag, selected); w.Write(selected.Count); foreach (var item in selected) w.Write(item.RuntimeIndex);
            }
        });
        result.Auxiliary = Digest(w => {
            w.Write("Auxiliary-v1");
            if (c is GameplayTagContainer normal) {
                w.Write("normal"); var list = normal.m_SerializedExplicitTags; w.Write(list != null);
                if (list != null) {
                    w.Write(list.Count); w.Write(list.Capacity); var items = (string[])StringItems.GetValue(list); w.Write(items.Length);
                    foreach (string value in items) { w.Write(value != null); if (value != null) w.Write(value); }
                }
            } else {
                w.Write("counted");
                foreach (string name in CountArrays) {
                    Array array = (Array)typeof(GameplayTagCountContainer).GetField(name, Fields).GetValue(c);
                    // This fixture installs no callbacks and performs no counted batch operations.
                    // Require complete auxiliary arrays to be empty, rather than omit a nonempty tail.
                    Check(array.Length == 0, "unexpected counted auxiliary allocation: " + name); w.Write(name); w.Write(array.Length);
                }
                foreach (string name in CountScalars) { w.Write(name); w.Write((int)typeof(GameplayTagCountContainer).GetField(name, Fields).GetValue(c)); }
                w.Write((bool)typeof(GameplayTagCountContainer).GetField("m_Dispatching", Fields).GetValue(c));
                foreach (string name in new[] { "OnAnyTagCountChange", "OnAnyTagNewOrRemove" }) {
                    bool present = typeof(GameplayTagCountContainer).GetField(name, Fields).GetValue(c) != null;
                    Check(!present, "unexpected listener"); w.Write(present);
                }
            }
        });
        result.Full = Digest(w => { w.Write("Snapshot-v1"); w.Write(result.Storage); w.Write(result.Public); w.Write(result.Auxiliary); });
        return result;
    }
    static object[] Refs(IGameplayTagContainer c)
    {
        var s = c.Indices.Storage; var refs = new List<object> { s, s.Entries, s.ExplicitIndices, Buckets.GetValue(s) };
        if (c is GameplayTagContainer normal) { refs.Add(normal.m_SerializedExplicitTags); refs.Add(normal.m_SerializedExplicitTags == null ? null : StringItems.GetValue(normal.m_SerializedExplicitTags)); }
        else foreach (string name in CountArrays) refs.Add(typeof(GameplayTagCountContainer).GetField(name, Fields).GetValue(c));
        return refs.ToArray();
    }
    static bool SameRefs(object[] before, IGameplayTagContainer c)
    {
        object[] after = Refs(c); return before.Length == after.Length && !before.Where((value, i) => !ReferenceEquals(value, after[i])).Any();
    }
    static void Unchanged(string label, Snapshot before, object[] refs, IGameplayTagContainer c)
    {
        Snapshot after = Capture(c); Check(before.Full == after.Full && SameRefs(refs, c), label + " mutated state or identities"); ImmutabilityChecks++;
    }
    static void Emit(int e, string stage, IGameplayTagContainer c)
    {
        var s = Capture(c); Observations++;
        Console.WriteLine("{\"Kind\":\"State\",\"E\":" + e + ",\"Stage\":" + Q(stage) + ",\"FullSHA256\":" + Q(s.Full) +
            ",\"StorageSHA256\":" + Q(s.Storage) + ",\"PublicSHA256\":" + Q(s.Public) + ",\"AuxiliarySHA256\":" + Q(s.Auxiliary) +
            ",\"Count\":" + s.Count + ",\"ExplicitCount\":" + s.ExplicitCount + ",\"EntriesCapacity\":" + s.EntriesCapacity +
            ",\"ExplicitCapacity\":" + s.ExplicitCapacity + ",\"BucketCapacity\":" + s.BucketCapacity + ",\"BucketMask\":" + s.BucketMask +
            ",\"ParentTotal\":" + s.ParentTotal + ",\"ParentExplicit\":" + s.ParentExplicit + "}");
    }
    static void Register()
    {
        var registrations = new List<GameplayTagRegistration>();
        for (int i = 0; i < 32768; i++) registrations.Add(new GameplayTagRegistration("Common.Leaf" + i.ToString("D5")));
        foreach (string name in new[] { "SeedA", "SeedB", "Old", "Spare" }) registrations.Add(new GameplayTagRegistration(name));
        GameplayTagManager.Initialize(registrations.ToArray());
        Leaves = new GameplayTag[32768];
        for (int i = 0; i < Leaves.Length; i++) Leaves[i] = GameplayTagManager.RequestTag("Common.Leaf" + i.ToString("D5"));
        Common = GameplayTagManager.RequestTag("Common"); SeedA = GameplayTagManager.RequestTag("SeedA"); SeedB = GameplayTagManager.RequestTag("SeedB");
        Old = GameplayTagManager.RequestTag("Old"); Spare = GameplayTagManager.RequestTag("Spare");
    }
    static GameplayTagContainer MakeSeed()
    {
        var seed = new GameplayTagContainer(); seed.AddTag(Old); seed.RemoveTag(Old); seed.AddTag(SeedA); seed.AddTag(SeedB); seed.OnBeforeSerialize(); return seed;
    }
    static GameplayTagCountContainer MakeSource(int e)
    {
        var source = new GameplayTagCountContainer(); source.AddTag(Old, 3); source.RemoveTag(Old, 3);
        for (int i = 0; i < e; i++) source.AddTag(Leaves[i], 3);
        return source;
    }
    static GameplayTagContainer MakeOutput()
    {
        var output = new GameplayTagContainer(); output.AddTag(Old); output.OnBeforeSerialize(); output.Clear(); return output;
    }
    static int Bits(int value) { int bits = 0; while (value != 0) { bits++; value >>= 1; } return bits; }
    static void ValidateInputs(int e, GameplayTagContainer seed, GameplayTagCountContainer source)
    {
        Check(source.ExplicitTagCount == e && source.TagCount == e + 1, "source closure count");
        Check(source.GetTagCount(Common) == 3 * e && source.GetExplicitTagCount(Common) == 0, "source root count");
        Check(seed.TagCount == 2 && seed.ExplicitTagCount == 2 && !seed.HasTag(Common), "small disjoint seed");
        for (int i = 0; i < e; i++) {
            Check(source.GetTagCount(Leaves[i]) == 3 && source.GetExplicitTagCount(Leaves[i]) == 3, "leaf amount");
            Check(GameplayTagManager.GetParentIndex(Leaves[i].RuntimeIndex) == Common.RuntimeIndex, "leaf common parent");
            Check(!seed.HasTag(Leaves[i]), "seed overlap");
        }
        Check(GameplayTagManager.GetParentIndex(Common.RuntimeIndex) == 0, "common must be root");
        foreach (var entry in seed.Indices.Storage.Entries) Check(entry.ExplicitCount == 0 || entry.ExplicitCount == 1, "seed raw explicit field");
        int p = Bits(e), links = Bits(seed.TagCount + source.TagCount);
        Check(p == (e == 32767 ? 15 : 16) && links == 16, "packing arithmetic");
        Console.WriteLine("{\"Kind\":\"Fixture\",\"E\":" + e + ",\"SourceCount\":" + source.TagCount + ",\"SourceAmount\":3,\"SeedCount\":2,\"MaximumCount\":" + (source.TagCount + seed.TagCount) +
            ",\"P\":" + p + ",\"I\":" + links + ",\"ExpectedAdmission\":" + (e == 32767 ? "true" : "false") + ",\"ExpectedCommonPendingMass\":" + e + "}");
    }
    static void CheckUnion(int e, GameplayTagContainer output)
    {
        Check(output.TagCount == e + 3 && output.ExplicitTagCount == e + 2, "union counts");
        Check(output.HasTag(Common) && !output.HasTagExact(Common) && output.Indices.Storage.GetCount(Common.RuntimeIndex) == e, "parent contribution E");
        Check(output.HasTagExact(SeedA) && output.HasTagExact(SeedB), "seed lost");
        for (int i = 0; i < e; i++) Check(output.HasTagExact(Leaves[i]) && output.Indices.Storage.GetCount(Leaves[i].RuntimeIndex) == 1, "leaf union amount");
        if (e < Leaves.Length) Check(!output.HasTag(Leaves[e]), "extra leaf");
        Check(!output.HasTag(Old) && !output.HasTag(Spare), "extra old/spare");
        var s = output.Indices.Storage;
        for (int i = 0; i < s.Count; i++) Check(s.Entries[i].ExplicitCount == 0 || s.Entries[i].ExplicitCount == 1, "undrained high bits");
    }
    static void Continue(int e, GameplayTagContainer output)
    {
        output.Clear(); Check(output.TagCount == 0 && output.ExplicitTagCount == 0, "clear failed"); Emit(e, "clear", output);
        output.AddTag(Spare); output.AddTag(Leaves[0]); output.AddTag(Leaves[e - 1]);
        Check(output.TagCount == 4 && output.ExplicitTagCount == 3 && output.Indices.Storage.GetCount(Common.RuntimeIndex) == 2, "regrow failed");
        Emit(e, "regrow", output); output.OnBeforeSerialize(); Emit(e, "regrow-serialized", output);
    }
    static void PublicCase(int e)
    {
        var seed = MakeSeed(); var source = MakeSource(e); var output = MakeOutput(); ValidateInputs(e, seed, source);
        var seedBefore = Capture(seed); var sourceBefore = Capture(source); var seedRefs = Refs(seed); var sourceRefs = Refs(source);
        Emit(e, "seed-before", seed); Emit(e, "source-before", source); Emit(e, "output-before", output);
#if REUSABLE_UNION
        GameplayTagContainer.Union(output, seed, source);
#else
        GameplayTagContainer.Copy(output, GameplayTagContainer.Union(seed, source));
#endif
        CheckUnion(e, output); Emit(e, "union", output);
        Unchanged("public seed", seedBefore, seedRefs, seed); Unchanged("public counted source", sourceBefore, sourceRefs, source);
        Emit(e, "seed-after", seed); Emit(e, "source-after", source); Continue(e, output);
        Unchanged("post-continuation seed", seedBefore, seedRefs, seed); Unchanged("post-continuation source", sourceBefore, sourceRefs, source);
    }
    static void DiagnosticCase(int e)
    {
        var seed = MakeSeed(); var source = MakeSource(e); var output = MakeOutput(); var reference = MakeOutput(); ValidateInputs(e, seed, source);
        GameplayTagContainer.Copy(output, seed); GameplayTagContainer.Copy(reference, seed);
        var before = Capture(output); var refs = Refs(output); var sourceBefore = Capture(source); var sourceRefs = Refs(source); var seedBefore = Capture(seed); var seedRefs = Refs(seed);
        Emit(e, "diagnostic-before", output); Emit(e, "diagnostic-source-before", source);
        var gate = typeof(TagStorage).GetMethod("HasIntrusiveUnionState", Fields);
        var method = typeof(TagStorage).GetMethod("TryAddUnionIntrusive", Fields);
        Check(gate != null && method != null, "intrusive diagnostics unavailable");
        Check((bool)gate.Invoke(output.Indices.Storage, new object[] { GameplayTagManager.TagCount, true }), "destination fails healthy gate");
        Check((bool)gate.Invoke(source.Indices.Storage, new object[] { GameplayTagManager.TagCount, false }), "source fails healthy gate");
        Unchanged("readonly healthy gate destination", before, refs, output); Unchanged("readonly healthy gate source", sourceBefore, sourceRefs, source);
        bool admitted = (bool)method.Invoke(output.Indices.Storage, new object[] { source.Indices.Storage });
        Check(admitted == (e == 32767), "admission differs from frozen expectation");
        Emit(e, "diagnostic-after-admission", output);
        if (!admitted) {
            Unchanged("packing rejection destination", before, refs, output);
            output.Indices.Storage.AddUnion(source.Indices.Storage);
        }
        reference.Indices.Storage.AddUnion(source.Indices.Storage);
        CheckUnion(e, output); CheckUnion(e, reference);
        Check(Capture(output).Full == Capture(reference).Full, "intrusive/fallback differs from unchanged FIFO");
        Emit(e, "diagnostic-result", output); Emit(e, "diagnostic-reference-fifo", reference);
        Unchanged("diagnostic source", sourceBefore, sourceRefs, source); Unchanged("diagnostic seed", seedBefore, seedRefs, seed);
        Console.WriteLine("{\"Kind\":\"Admission\",\"E\":" + e + ",\"Admitted\":" + (admitted ? "true" : "false") +
            ",\"HealthyDestination\":true,\"HealthySource\":true,\"RejectedWithoutMutation\":" + (admitted ? "null" : "true") +
            ",\"UnchangedFIFOMatch\":true,\"SourceUnchanged\":true,\"ParentTotal\":" + output.Indices.Storage.GetCount(Common.RuntimeIndex) + "}");
        Continue(e, output); Continue(e, reference);
        Check(Capture(output).Full == Capture(reference).Full, "continuation differs from unchanged FIFO");
        Unchanged("diagnostic continuation source", sourceBefore, sourceRefs, source); Unchanged("diagnostic continuation seed", seedBefore, seedRefs, seed);
    }
    static int Main(string[] args)
    {
        try {
            Check(args.Length == 1 && (args[0] == "public" || args[0] == "diagnostic"), "mode"); Register();
            foreach (int e in new[] { 32767, 32768 }) { if (args[0] == "public") PublicCase(e); else DiagnosticCase(e); }
            Console.Error.WriteLine("SUMMARY mode=" + args[0] + " cases=2 observations=" + Observations + " immutabilityChecks=" + ImmutabilityChecks); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
    }
}
