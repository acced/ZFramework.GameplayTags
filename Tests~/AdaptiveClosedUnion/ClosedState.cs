using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using GameplayTags;

internal static class Program
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly FieldInfo Buckets = typeof(TagStorage).GetField("m_Buckets", Fields);
    static readonly FieldInfo Mask = typeof(TagStorage).GetField("m_BucketMask", Fields);
    static string[] Names;
    static GameplayTag[] Tags;
    static GameplayTag Seed, Spare, Old;
    static int Observations, Cases, ImmutabilityChecks, IdentityChecks;
    static string Q(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
    static void Check(bool v, string why) { if (!v) throw new Exception(why); }
    static object[] Refs(IGameplayTagContainer c) { var s = c.Indices.Storage; return new object[] { s, s.Entries, s.ExplicitIndices, Buckets.GetValue(s) }; }
    static string Identities(object[] before, IGameplayTagContainer after)
    {
        object[] a = Refs(after); IdentityChecks += a.Length;
        return string.Join(",", before.Select((x, i) => ReferenceEquals(x, a[i]) ? "same" : "changed"));
    }
    static string Storage(TagStorage s)
    {
        var b = new StringBuilder();
        b.Append("Count=").Append(s.Count).Append(";ExplicitCount=").Append(s.ExplicitCount).Append(";m_BucketMask=").Append((int)Mask.GetValue(s));
        b.Append(";Entries[").Append(s.Entries.Length).Append("]=");
        foreach (var e in s.Entries) b.Append(e.Id).Append(',').Append(e.Count).Append(',').Append(e.ExplicitCount).Append(',').Append(e.ExplicitIndex).Append('|');
        b.Append(";ExplicitIndices[").Append(s.ExplicitIndices.Length).Append("]=").Append(string.Join(",", s.ExplicitIndices));
        int[] buckets = (int[])Buckets.GetValue(s);
        b.Append(";m_Buckets[").Append(buckets.Length).Append("]=").Append(string.Join(",", buckets));
        return b.ToString();
    }
    static string Capture(Func<string> f) { try { return f(); } catch (Exception e) { return "THROW:" + e.GetType().FullName; } }
    static string Enumeration(IGameplayTagContainer c, bool exact)
    {
        var e = exact ? c.GetExplicitTags() : c.GetTags(); var ids = new List<int>();
        while (e.MoveNext()) { if (ids.Count > c.Indices.Storage.ExplicitIndices.Length + c.Indices.Storage.Entries.Length + 2) return "LIMIT"; ids.Add(e.Current.RuntimeIndex); }
        return string.Join(",", ids);
    }
    static string Snapshot(IGameplayTagContainer c)
    {
        var b = new StringBuilder(Storage(c.Indices.Storage));
        b.Append(";Public=").Append(Capture(() => c.TagCount + "/" + c.ExplicitTagCount + "/" + c.IsEmpty));
        b.Append(";Enumeration=").Append(Capture(() => Enumeration(c, false))).Append(";ExplicitEnumeration=").Append(Capture(() => Enumeration(c, true)));
        foreach (var t in new[] { Tags[0], Tags[Tags.Length / 2], Tags[Tags.Length - 1], Seed, Spare, Old })
            b.Append(";Probe").Append(t.RuntimeIndex).Append('=').Append(Capture(() => c.HasTag(t) + "/" + c.HasTagExact(t) + "/" + c.Indices.Storage.GetCount(t.RuntimeIndex) + "/" + c.Indices.Storage.GetExplicitCount(t.RuntimeIndex)));
        if (c is GameplayTagContainer normal)
            b.Append(";Serialized=").Append(normal.m_SerializedExplicitTags == null ? "null" : normal.m_SerializedExplicitTags.Capacity + ":" + string.Join("|", normal.m_SerializedExplicitTags));
        return b.ToString();
    }
    static void Emit(string id, string stage, IGameplayTagContainer c, string exception = null, string identity = null)
    {
        Observations++;
        Console.WriteLine("{\"Id\":" + Q(id + "/" + stage) + ",\"Exception\":" + Q(exception) + ",\"Identity\":" + Q(identity) + ",\"State\":" + Q(Snapshot(c)) + "}");
    }
    static string Do(string id, string stage, GameplayTagContainer c, Action operation, bool retained = false)
    {
        object[] before = Refs(c); string ex = null;
        try { operation(); } catch (Exception e) { ex = e.GetType().FullName; }
        string identity = Identities(before, c);
        if (retained) Check(identity == "same,same,same,same", id + "/" + stage + " unexpectedly replaced owned arrays");
        Emit(id, stage, c, ex, identity); return ex;
    }
    static void Register(int depth)
    {
        Names = new string[depth]; Names[0] = "Chain";
        for (int i = 1; i < depth; i++) Names[i] = Names[i - 1] + ".N";
        string seed = "Seed" + string.Concat(Enumerable.Repeat(".N", depth));
        GameplayTagManager.Initialize(new[] { new GameplayTagRegistration(Names[depth - 1]), new GameplayTagRegistration(seed), new GameplayTagRegistration("Spare.Leaf"), new GameplayTagRegistration("Old.Leaf") });
        Tags = Names.Select(GameplayTagManager.RequestTag).ToArray();
        Seed = GameplayTagManager.RequestTag(seed); Spare = GameplayTagManager.RequestTag("Spare.Leaf"); Old = GameplayTagManager.RequestTag("Old.Leaf");
    }
    static void Add(IGameplayTagContainer c, GameplayTag t, string kind)
    {
        if (c is GameplayTagCountContainer counted)
        {
            if (kind == "max" || kind == "zero" || kind == "readd")
            {
                counted.AddTag(t, int.MaxValue);
                if (kind != "max") { counted.AddTag(t, int.MaxValue); counted.AddTag(t, 2); }
                if (kind == "readd") counted.AddTag(t, 1);
            }
            else counted.AddTag(t, kind == "five" ? 5 : 3);
        }
        else c.AddTag(t);
    }
    static IGameplayTagContainer Make(bool seed, string kind, int order)
    {
        IGameplayTagContainer c = kind == "set" ? (IGameplayTagContainer)new GameplayTagContainer() : new GameplayTagCountContainer();
        // Keep a stale retained tail using only public operations before building.
        c.AddTag(Old); c.RemoveTag(Old);
        if (seed) { Add(c, Seed, kind); Add(c, Tags[0], kind); }
        else
        {
            int[] indices = Enumerable.Range(0, Tags.Length).ToArray();
            if (order == 1) Array.Reverse(indices);
            if (order == 2) { var rng = new Random(83177); for (int i = indices.Length - 1; i > 0; i--) { int j = rng.Next(i + 1), t = indices[i]; indices[i] = indices[j]; indices[j] = t; } }
            foreach (int i in indices) Add(c, Tags[i], kind);
        }
        return c;
    }
    static void Apply(GameplayTagContainer output, IGameplayTagContainer a, IGameplayTagContainer b)
    {
#if REUSABLE_UNION
        GameplayTagContainer.Union(output, a, b);
#else
        GameplayTagContainer.Copy(output, GameplayTagContainer.Union(a, b));
#endif
    }
    static void Run(int depth, int order, string seedKind, string sourceKind, string mode)
    {
        string id = depth + "/" + order + "/" + seedKind + "-" + sourceKind + "/" + mode;
        Cases++;
        IGameplayTagContainer a = Make(true, seedKind, order), b = Make(false, sourceKind, order);
        var result = new GameplayTagContainer(depth * 4 + 32); result.AddTag(Old); result.OnBeforeSerialize();
        if (mode == "left-alias") { result = new GameplayTagContainer(a); a = result; }
        if (mode == "right-alias") { result = new GameplayTagContainer(b); b = result; }
        string beforeA = Snapshot(a), beforeB = Snapshot(b); object[] refsA = Refs(a), refsB = Refs(b), refsOutput = Refs(result);
        Emit(id, "source-a-before", a); Emit(id, "source-b-before", b); Emit(id, "output-before", result);
        string exception = null;
        try { if (mode == "factory") result = GameplayTagContainer.Union(a, b); else Apply(result, a, b); }
        catch (Exception e) { exception = e.GetType().FullName; }
        Emit(id, "union", result, exception, mode == "factory" ? null : Identities(refsOutput, result));
        if (mode != "left-alias") { Check(beforeA == Snapshot(a) && Identities(refsA, a) == "same,same,same,same", id + " source A changed"); ImmutabilityChecks++; }
        if (mode != "right-alias") { Check(beforeB == Snapshot(b) && Identities(refsB, b) == "same,same,same,same", id + " source B changed"); ImmutabilityChecks++; }
        Emit(id, "source-a-after", a); Emit(id, "source-b-after", b);
        // Preserve legacy exceptions and resulting state; continue safe public operations.
        Do(id, "duplicate-add", result, () => result.AddTag(Tags[0]));
        Do(id, "remove-mid", result, () => result.RemoveTag(Tags[depth / 2]));
        Do(id, "readd-mid", result, () => result.AddTag(Tags[depth / 2]));
        Do(id, "remove-leaf", result, () => result.RemoveTag(Tags[depth - 1]));
        Do(id, "remove-root", result, () => result.RemoveTag(Tags[0]));
        Do(id, "add-spare", result, () => result.AddTag(Spare));
        var copy = new GameplayTagContainer(depth * 4 + 32); copy.AddTag(Old); copy.OnBeforeSerialize();
        string preCopy = Snapshot(result); object[] sourceRefs = Refs(result);
        Do(id, "copy-retained", copy, () => GameplayTagContainer.Copy(copy, result));
        Check(preCopy == Snapshot(result) && Identities(sourceRefs, result) == "same,same,same,same", id + " Copy mutated source"); ImmutabilityChecks++;
        Do(id, "copy-self", copy, () => GameplayTagContainer.Copy(copy, copy), true);
        Do(id, "copy-remove", copy, () => copy.RemoveTag(Spare));
        Do(id, "clear", result, () => result.Clear(), true);
        Do(id, "regrow", result, () => { result.AddTag(Spare); result.AddTag(Tags[depth - 1]); }, true);
        Do(id, "copy-back", result, () => GameplayTagContainer.Copy(result, copy));
        Do(id, "clear-copy", copy, () => copy.Clear(), true);
        Do(id, "regrow-copy", copy, () => { copy.AddTag(Seed); copy.AddTag(Tags[0]); }, true);
        Do(id, "copy-empty", result, () => GameplayTagContainer.Copy(result, new GameplayTagContainer()), true);
    }
    static void Admission()
    {
        Register(769);
        var source = (GameplayTagContainer)Make(false, "set", 0);
        var seed = (GameplayTagContainer)Make(true, "set", 0);
        var pending = new TagStorage(); pending.CopyFrom(seed.Indices.Storage);
        var insert = typeof(TagStorage).GetMethod("Insert", Fields);
        var explicitIndex = typeof(TagStorage).GetMethod("AddExplicitIndex", Fields);
        var contribution = typeof(TagStorage).GetMethod("QueueContribution", Fields);
        var gate = typeof(TagStorage).GetMethod("CanOrderUnionContributions", Fields);
        Check(gate != null, "candidate gate missing");
        int[] queue = ArrayPool<int>.Shared.Rent(source.ExplicitTagCount);
        int tail = 0, queued = 0;
        Action<int, int> enqueue = (id, amount) => { object[] args = { id, amount, queue, tail, queued }; contribution.Invoke(pending, args); tail = (int)args[3]; queued = (int)args[4]; };
        try
        {
            var s = source.Indices.Storage;
            for (int i = 0; i < s.ExplicitCount; i++)
            {
                int id = s.Entries[s.ExplicitIndices[i]].Id, index = pending.Find(id);
                if (index < 0) index = (int)insert.Invoke(pending, new object[] { id });
                else if ((pending.Entries[index].ExplicitCount & 1) != 0) continue;
                pending.Entries[index].ExplicitCount |= 1;
                explicitIndex.Invoke(pending, new object[] { index });
                pending.Entries[index].Count++;
                int parent = GameplayTagManager.GetParentIndex(id); if (parent != 0) enqueue(parent, 1);
            }
            int admissionAfter = queue.Length * Math.Max(2, s.Count / queue.Length), processed = 0, head = 0;
            while (queued != 0 && processed < admissionAfter)
            {
                int index = queue[head++]; queued--; processed++;
                int amount = (int)((uint)pending.Entries[index].ExplicitCount >> 1);
                pending.Entries[index].ExplicitCount &= 1; pending.Entries[index].Count += amount;
                int parent = GameplayTagManager.GetParentIndex(pending.Entries[index].Id); if (parent != 0) enqueue(parent, amount);
                if (head == queue.Length) head = 0;
            }
            Check(processed == admissionAfter && queued > 0 && head == 0, "ancestor-first workload did not reach adaptive gate");
            string before = Storage(pending); object e = pending.Entries, x = pending.ExplicitIndices, b = Buckets.GetValue(pending); int[] q = (int[])queue.Clone();
            bool admitted = (bool)gate.Invoke(pending, new object[] { queue, queued });
            Check(admitted, "closed ancestor-first gate rejected");
            Check(before == Storage(pending) && ReferenceEquals(e, pending.Entries) && ReferenceEquals(x, pending.ExplicitIndices) && ReferenceEquals(b, Buckets.GetValue(pending)) && q.SequenceEqual(queue), "gate changed state or owned references");
            Console.WriteLine("{\"Id\":\"admission/ancestor-first-all-explicit\",\"Depth\":769,\"SourceExplicitCount\":" + s.ExplicitCount + ",\"SourceCount\":" + s.Count + ",\"QueueLength\":" + queue.Length + ",\"ProcessedBeforeGate\":" + processed + ",\"PendingQueueEntries\":" + queued + ",\"Admitted\":true,\"GateRestoredEveryFieldAndArray\":true}");
        }
        finally { ArrayPool<int>.Shared.Return(queue, false); }
    }
    static void Main(string[] args)
    {
        string schema = string.Join(",", typeof(TagStorage).GetFields(Fields).Select(f => f.Name).OrderBy(x => x, StringComparer.Ordinal));
        Check(schema == "Count,Entries,ExplicitCount,ExplicitIndices,m_BucketMask,m_Buckets", "unexpected unobserved storage field: " + schema);
        string entrySchema = string.Join(",", typeof(TagStorage.Entry).GetFields(Fields).Select(f => f.Name).OrderBy(x => x, StringComparer.Ordinal));
        Check(entrySchema == "Count,ExplicitCount,ExplicitIndex,Id", "unexpected unobserved entry field: " + entrySchema);
        if (args.Length != 0 && args[0] == "admission") { Admission(); return; }
        foreach (int depth in new[] { args.Length == 0 ? 257 : int.Parse(args[0]) })
        {
            Register(depth);
            foreach (int order in depth == 257 ? new[] { 0, 1, 2 } : new[] { 0 })
            foreach (string pair in depth == 257 ? new[] { "set/set", "set/three", "five/set", "five/three", "set/max", "set/zero", "set/readd" } : new[] { "set/set", "set/three" })
            foreach (string mode in new[] { "factory", "distinct", "left-alias", "right-alias" })
            {
                string[] kinds = pair.Split('/'); Run(depth, order, kinds[0], kinds[1], mode);
            }
        }
        Console.Error.WriteLine("Cases=" + Cases + ";Observations=" + Observations + ";InputAndCopyImmutabilityChecks=" + ImmutabilityChecks + ";WithinProcessReferenceComparisons=" + IdentityChecks + ";StorageFields=" + schema + ";EntryFields=" + entrySchema);
    }
}
