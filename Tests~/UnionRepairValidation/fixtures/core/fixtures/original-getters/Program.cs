using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using GameplayTags;

// Focused behavioral differential. No runtime files are edited. V is passed directly
// through constrained generic in parameters; only its shared State is a reference.
static class Program
{
    static readonly string[] Tags = {"Root.A.One", "Root.A.Two", "Root.B.One", "Other.C.One", "Other.C.Two", "Deep.A.B.C.D", "Extra.E.One", "Extra.F.Two", "Tail.Z"};
    static GameplayTag T(int i) => GameplayTagManager.RequestTag(Tags[i]);
    static readonly string[] Choices = {"Nn", "Cc", "Pn", "Pc", "Pz", "Dn", "Dc", "Dz", "Vn", "Vc", "Vz"};
    static readonly string[] Behaviors = {"stable", "change-l2", "change-r2", "change-both2", "throw-l1", "throw-r1", "throw-l2", "throw-r2", "shared-throw2", "shared-throw3", "shared-change2", "shared-alternate", "l1-add-output", "r1-clear-output", "l1-add-other", "r1-remove-left", "r1-replace-output", "l1-clear-first", "l2-replace-output", "r2-set-serialized", "l1-serialize-output", "r1-add-count", "r1-return-current-output"};
    static FieldInfo Buckets = typeof(TagStorage).GetField("m_Buckets", BindingFlags.Instance | BindingFlags.NonPublic);
    static FieldInfo Mask = typeof(TagStorage).GetField("m_BucketMask", BindingFlags.Instance | BindingFlags.NonPublic);

    public sealed class State
    {
        public World W; public string Name; public GameplayTagContainerIndices First, Alternate; public int Reads;
        public GameplayTagContainerIndices Read(int local)
        {
            int n = ++Reads, global = ++W.GlobalReads;
            W.Log.Add(Name + n + "#" + global + "/local" + local);
            string b = W.Behavior;
            if ((b == "throw-" + Name.ToLowerInvariant() + n) || (b == "shared-throw2" && global == 2) || (b == "shared-throw3" && global == 3))
                throw new GetterException(Name + n + "#" + global);
            if (Name == "L" && n == 1)
            {
                if (b == "l1-add-output") { W.Output.AddTag(T(6)); W.Log.Add("add-output"); }
                if (b == "l1-add-other") { W.RNormal.AddTag(T(7)); W.RCount.AddTag(T(7), 3); W.Log.Add("add-other"); }
                if (b == "l1-clear-first") { First.Clear(); W.Log.Add("clear-first"); }
                if (b == "l1-serialize-output") { W.Output.OnBeforeSerialize(); W.Log.Add("serialize-output"); }
            }
            if (Name == "R" && n == 1)
            {
                if (b == "r1-clear-output") { W.Output.Clear(); W.Log.Add("clear-output"); }
                if (b == "r1-remove-left") { W.LNormal.RemoveTag(T(0)); W.LCount.RemoveTag(T(0)); W.Log.Add("remove-left"); }
                if (b == "r1-replace-output") { W.ReplaceOutput(); W.Log.Add("replace-output"); }
                if (b == "r1-add-count") { W.LCount.AddTag(T(4), 5); W.Log.Add("add-count"); }
                if (b == "r1-return-current-output") { W.Log.Add("current-output"); return W.Output.Indices; }
            }
            if (Name == "L" && n == 2 && b == "l2-replace-output") { W.ReplaceOutput(); W.Log.Add("replace-output"); }
            if (Name == "R" && n == 2 && b == "r2-set-serialized") { W.Output.m_SerializedExplicitTags = new List<string> { "second-getter-marker" }; W.Log.Add("serialized-marker"); }
            if ((n >= 2 && (b == "change-" + Name.ToLowerInvariant() + "2" || b == "change-both2")) || (b == "shared-change2" && global >= 2) || (b == "shared-alternate" && global % 2 == 0))
            { W.Log.Add("alternate-" + Name); return Alternate; }
            return First;
        }
    }
    sealed class GetterException : Exception { public GetterException(string m) : base(m) {} }

    // Deliberately do not inherit GameplayTagContainer: its runtime type flag matters.
    public class P : IGameplayTagContainer
    {
        public State S;
        public GameplayTagContainerIndices Indices => S.Read(0);
        public bool IsEmpty => S.First.IsEmpty;
        public int ExplicitTagCount => S.First.ExplicitTagCount;
        public int TagCount => S.First.TagCount;
        public void AddTag(GameplayTag t) { throw new NotSupportedException(); }
        public void RemoveTag(GameplayTag t) { throw new NotSupportedException(); }
        public void AddTags<TX>(in TX t) where TX : IGameplayTagContainer { throw new NotSupportedException(); }
        public void RemoveTags<TX>(in TX t) where TX : IGameplayTagContainer { throw new NotSupportedException(); }
        public GameplayTagEnumerator GetTags() => new GameplayTagEnumerator(S.First.Storage);
        public GameplayTagEnumerator GetExplicitTags() => new GameplayTagEnumerator(S.First.Storage, true);
        public void GetParentTags(GameplayTag t, List<GameplayTag> o) { throw new NotSupportedException(); }
        public void GetChildTags(GameplayTag t, List<GameplayTag> o) { throw new NotSupportedException(); }
        public void GetExplicitParentTags(GameplayTag t, List<GameplayTag> o) { throw new NotSupportedException(); }
        public void GetExplicitChildTags(GameplayTag t, List<GameplayTag> o) { throw new NotSupportedException(); }
        public void Clear() { throw new NotSupportedException(); }
        public IEnumerator<GameplayTag> GetEnumerator() => GetTags();
        IEnumerator IEnumerable.GetEnumerator() => GetTags();
    }
    public sealed class D : GameplayTagContainer, IGameplayTagContainer
    {
        public State S;
        GameplayTagContainerIndices IGameplayTagContainer.Indices => S.Read(0);
    }
    public struct V : IGameplayTagContainer
    {
        public State S; public int LocalReads;
        public GameplayTagContainerIndices Indices => S.Read(++LocalReads);
        public bool IsEmpty => S.First.IsEmpty;
        public int ExplicitTagCount => S.First.ExplicitTagCount;
        public int TagCount => S.First.TagCount;
        public void AddTag(GameplayTag t) { throw new NotSupportedException(); }
        public void RemoveTag(GameplayTag t) { throw new NotSupportedException(); }
        public void AddTags<TX>(in TX t) where TX : IGameplayTagContainer { throw new NotSupportedException(); }
        public void RemoveTags<TX>(in TX t) where TX : IGameplayTagContainer { throw new NotSupportedException(); }
        public GameplayTagEnumerator GetTags() => new GameplayTagEnumerator(S.First.Storage);
        public GameplayTagEnumerator GetExplicitTags() => new GameplayTagEnumerator(S.First.Storage, true);
        public void GetParentTags(GameplayTag t, List<GameplayTag> o) { throw new NotSupportedException(); }
        public void GetChildTags(GameplayTag t, List<GameplayTag> o) { throw new NotSupportedException(); }
        public void GetExplicitParentTags(GameplayTag t, List<GameplayTag> o) { throw new NotSupportedException(); }
        public void GetExplicitChildTags(GameplayTag t, List<GameplayTag> o) { throw new NotSupportedException(); }
        public void Clear() { throw new NotSupportedException(); }
        public IEnumerator<GameplayTag> GetEnumerator() => GetTags();
        IEnumerator IEnumerable.GetEnumerator() => GetTags();
    }
    public sealed class World
    {
        public string LChoice, RChoice, Behavior, Alias; public int Variant, GlobalReads;
        public GameplayTagContainer LNormal, RNormal, AltL, AltR, Output;
        public GameplayTagCountContainer LCount, RCount;
        public State L, R; public P LP, RP; public D LD, RD; public V LV, RV;
        public List<string> Log = new List<string>(); public string Exception = "none";
        public World(string lc, string rc, string alias, string behavior, int variant)
        {
            LChoice = lc; RChoice = rc; Alias = alias; Behavior = behavior; Variant = variant;
            L = new State { W = this, Name = "L" }; R = new State { W = this, Name = "R" };
            LD = new D { S = L }; RD = new D { S = R };
            LNormal = lc[0] == 'D' ? LD : new GameplayTagContainer();
            RNormal = rc[0] == 'D' ? RD : new GameplayTagContainer();
            LCount = new GameplayTagCountContainer(); RCount = new GameplayTagCountContainer();
            Seed(LNormal, new[] { 0, 1, 3 }); Seed(RNormal, new[] { 1, 2, 5 });
            LCount.AddTag(T(0), 4); LCount.AddTag(T(1), 2); LCount.AddTag(T(3), 7);
            RCount.AddTag(T(1), 3); RCount.AddTag(T(2), 6); RCount.AddTag(T(5), 2);
            if (variant == 1) { LNormal.Clear(); LCount.Clear(); }
            if (variant == 2) { LNormal.EnsureCapacity(96); RNormal.EnsureCapacity(192); }
            if (variant == 3) { LNormal.RemoveTag(T(1)); LNormal.AddTag(T(1)); RNormal.AddTag(T(8)); }
            if (alias == "both" || alias == "self") RNormal = LNormal;
            Output = alias == "left" || alias == "both" || alias == "self" ? LNormal : alias == "right" ? RNormal : new GameplayTagContainer(variant == 2 ? 256 : 0);
            if (alias == "distinct") Seed(Output, new[] { 4, 8 });
            AltL = new GameplayTagContainer(); AltR = new GameplayTagContainer();
            Seed(AltL, new[] { 6, 8 }); Seed(AltR, new[] { 4, 7 });
            L.First = View(lc, LNormal, LCount); R.First = View(rc, RNormal, RCount);
            L.Alternate = AltL.Indices; R.Alternate = AltR.Indices;
            LP = new P { S = L }; RP = new P { S = R }; LV = new V { S = L }; RV = new V { S = R };
            if (alias == "self") { R = L; RP = LP; RD = LD; RV = LV; }
            Output.m_SerializedExplicitTags = variant == 1 ? null : new List<string> { "stale", "marker" };
        }
        public void ReplaceOutput() { Output.m_SerializedExplicitTags = new List<string> { Tags[4], Tags[8] }; Output.OnAfterDeserialize(); }
        static GameplayTagContainerIndices View(string choice, GameplayTagContainer normal, GameplayTagCountContainer count) => choice[1] == 'z' ? default : choice[1] == 'c' ? count.Indices : normal.Indices;
        static void Seed(GameplayTagContainer c, int[] tags) { foreach (int i in tags) c.AddTag(T(i)); }
    }
    static void Invoke<TL, TR>(World w, in TL l, in TR r, bool oracle) where TL : IGameplayTagContainer where TR : IGameplayTagContainer
    {
        if (oracle) { var result = GameplayTagContainer.Union(in l, in r); GameplayTagContainer.Copy(w.Output, result); }
        else
        {
#if REUSABLE_UNION
            GameplayTagContainer.Union(w.Output, in l, in r);
#else
            var result = GameplayTagContainer.Union(in l, in r); GameplayTagContainer.Copy(w.Output, result);
#endif
        }
    }
    static void PickRight<TL>(World w, in TL l, bool oracle) where TL : IGameplayTagContainer
    {
        switch (w.RChoice[0])
        {
            case 'N': Invoke(w, in l, in w.RNormal, oracle); break;
            case 'C': Invoke(w, in l, in w.RCount, oracle); break;
            case 'P': Invoke(w, in l, in w.RP, oracle); break;
            case 'D': Invoke(w, in l, in w.RD, oracle); break;
            case 'V': Invoke(w, in l, in w.RV, oracle); break;
        }
    }
    static void Run(World w, bool oracle)
    {
        try
        {
            switch (w.LChoice[0])
            {
                case 'N': PickRight(w, in w.LNormal, oracle); break;
                case 'C': PickRight(w, in w.LCount, oracle); break;
                case 'P': PickRight(w, in w.LP, oracle); break;
                case 'D': PickRight(w, in w.LD, oracle); break;
                case 'V': PickRight(w, in w.LV, oracle); break;
            }
        }
        catch (Exception ex) { w.Exception = ex.GetType().FullName + ":" + ex.Message; }
    }
    static string Names(GameplayTagEnumerator e) { var a = new List<string>(); foreach (var tag in e) a.Add(tag.Name); return string.Join(",", a); }
    static string Serialized(GameplayTagContainer c) => c.m_SerializedExplicitTags == null ? "null" : string.Join(",", c.m_SerializedExplicitTags);
    static string Storage(TagStorage s)
    {
        if (s == null) return "null";
        var a = new List<string>();
        for (int i = 0; i < s.Count; i++) { var e = s.Entries[i]; a.Add(GameplayTagManager.GetTagFromRuntimeIndex(e.Id).Name + ":" + e.Count + ":" + e.ExplicitCount); }
        return s.Count + "/" + s.ExplicitCount + "/" + string.Join(",", a);
    }
    static string Normal(GameplayTagContainer c)
    {
        var q = new List<string>(); foreach (string t in Tags) { var tag = GameplayTagManager.RequestTag(t); q.Add((c.HasTag(tag) ? "1" : "0") + (c.HasTagExact(tag) ? "1" : "0")); }
        return c.IsEmpty + ";" + c.TagCount + ";" + c.ExplicitTagCount + ";" + Names(c.GetTags()) + ";" + Names(c.GetExplicitTags()) + ";" + string.Join(",", q) + ";" + Serialized(c) + ";" + Storage(c.Indices.Storage);
    }
    static string Count(GameplayTagCountContainer c)
    {
        var q = new List<string>(); foreach (string t in Tags) { var tag = GameplayTagManager.RequestTag(t); q.Add(c.GetTagCount(tag) + "/" + c.GetExplicitTagCount(tag)); }
        return c.IsEmpty + ";" + c.TagCount + ";" + c.ExplicitTagCount + ";" + Names(c.GetTags()) + ";" + Names(c.GetExplicitTags()) + ";" + string.Join(",", q) + ";" + Storage(c.Indices.Storage);
    }
    static string Snapshot(World w)
    {
        string before = "exception=" + w.Exception + "\nlog=" + string.Join(",", w.Log) + "\nreads=" + w.L.Reads + "," + w.R.Reads + "," + w.GlobalReads + ";local=" + w.LV.LocalReads + "," + w.RV.LocalReads +
            "\noutput=" + Normal(w.Output) + "\nleft=" + Normal(w.LNormal) + "\nright=" + Normal(w.RNormal) + "\ncountLeft=" + Count(w.LCount) + "\ncountRight=" + Count(w.RCount) + "\nfirstLeft=" + Storage(w.L.First.Storage) + "\nfirstRight=" + Storage(w.R.First.Storage);
        w.Output.OnBeforeSerialize();
        return before + "\nserializedOutput=" + Serialized(w.Output);
    }
    static string Capacity(GameplayTagContainer c)
    {
        var s = c.Indices.Storage; return s.Entries.Length + "/" + s.ExplicitIndices.Length + "/" + ((int[])Buckets.GetValue(s)).Length + "/" + Mask.GetValue(s);
    }
    static string Encode(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
    public static void Main()
    {
        var regs = new List<GameplayTagRegistration>(); foreach (var t in Tags) regs.Add(new GameplayTagRegistration(t)); GameplayTagManager.Initialize(regs.ToArray());
        int total = 0, mismatch = 0, capMismatch = 0;
        foreach (string l in Choices) foreach (string r in Choices)
        foreach (string alias in new[] { "distinct", "left", "right", "both", "self" })
        {
            if ((alias == "left" || alias == "both" || alias == "self") && l[1] != 'n') continue;
            if ((alias == "right" || alias == "both" || alias == "self") && r[1] != 'n') continue;
            if (alias == "self" && l != r) continue;
            foreach (string behavior in Behaviors) for (int variant = 0; variant < 4; variant++)
            {
                var expected = new World(l, r, alias, behavior, variant); var actual = new World(l, r, alias, behavior, variant);
                Run(expected, true); Run(actual, false);
                string ec = Capacity(expected.Output), ac = Capacity(actual.Output), es = Snapshot(expected), a = Snapshot(actual);
                bool same = es == a, caps = ec == ac;
                total++; if (!same) mismatch++; if (!caps) capMismatch++;
                Console.WriteLine(string.Join("\t", new[] { "CASE", l + "-" + r + "/" + alias + "/" + behavior + "/v" + variant, same ? "PASS" : "FAIL", caps ? "SAME" : "DIFFERENT", ec, ac, Encode(es), Encode(a) }));
            }
        }
        Console.Error.WriteLine("SUMMARY cases=" + total + " semanticMismatches=" + mismatch + " capacityDifferences=" + capMismatch);
    }
}
