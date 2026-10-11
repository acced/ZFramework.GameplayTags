// Frozen, bounded, untimed HASH-ONLY capacity semantics. No production instrumentation.
// Public mode compares f7 factory+Copy with f53/prior/candidate reusable output.
// Diagnostic mode is candidate-only and compares Reserve+CopyFrom+unchanged AddUnion
// with unchanged CopyFrom+AddUnion. Private unusual capacities are not public claims.
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
    static readonly FieldInfo ListItems = typeof(List<string>).GetField("_items", Fields);
    static readonly FieldInfo ListSize = typeof(List<string>).GetField("_size", Fields);
    static readonly FieldInfo ListVersion = typeof(List<string>).GetField("_version", Fields);
    static readonly MethodInfo Reserve = typeof(TagStorage).GetMethod("ReserveUnionExplicitCapacity", Fields);
    static readonly MethodInfo Gate = typeof(TagStorage).GetMethod("HasIntrusiveUnionState", Fields);
    static readonly int[,] Pairs = { {1,8},{1,9},{1,15},{1,16},{7,9},{8,9},{9,9},{15,17},
        {16,17},{17,17},{31,33},{32,33},{33,33},{63,65},{64,65},{65,65} };
    static readonly int[] Capacities = { 8,16,32,64,128,8192 };
    static readonly int[] UnusualCapacities = { 1,3,7,9,15,17,31,33 };
    static readonly string[] Histories = { "reserved-empty", "dirty-one", "grown-cleared", "grown-drained" };
    static readonly string[] CountArrays = { "m_Callbacks", "m_Events", "m_Snapshot" };
    static readonly string[] CountScalars = { "m_CallbackCount", "m_EventCount", "m_NextEvent", "m_BatchDepth" };
    static readonly Dictionary<string, GameplayTagContainer> Prototypes = new Dictionary<string, GameplayTagContainer>();
    static GameplayTag[] SeedTags, SourceTags, Fillers;
    static GameplayTag SeedRoot, SourceRoot, Old, Spare;
    static int Cases, Observations, ImmutabilityChecks, HelperCases, SkipCases, ReserveCases;
    static int ZeroPlans, OnePlans, MultiPlans, OverlapReducedPlans, CopyZeroPlans, CopyOnePlans;

    sealed class Snapshot { internal string Storage, Public, Auxiliary, Full; }
    sealed class Plan { internal int A,E,M,F,L0,L1,LF,Allocations,UpperAllocations,CopyAllocations; }
    sealed class Saved
    {
        internal IGameplayTagContainer Container;
        internal Snapshot Snapshot;
        internal object[] References;
    }
    static void Check(bool condition, string why) { if (!condition) throw new InvalidOperationException(why); }
    static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string Digest(Action<BinaryWriter> action)
    {
        using (var stream = new MemoryStream()) {
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) action(writer);
            stream.Position = 0;
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
    static void Ints(BinaryWriter writer, int[] array)
    {
        writer.Write(array != null); if (array == null) return;
        writer.Write(array.Length); foreach (int value in array) writer.Write(value);
    }
    static string Raw(TagStorage storage)
    {
        return Digest(w => {
            w.Write("complete-owned-storage-v1"); w.Write(storage != null); if (storage == null) return;
            w.Write(storage.Count); w.Write(storage.ExplicitCount); w.Write((int)Mask.GetValue(storage));
            w.Write(storage.Entries != null);
            if (storage.Entries != null) {
                w.Write(storage.Entries.Length);
                foreach (var entry in storage.Entries) {
                    w.Write(entry.Id); w.Write(entry.Count); w.Write(entry.ExplicitCount); w.Write(entry.ExplicitIndex);
                }
            }
            Ints(w, storage.ExplicitIndices); Ints(w, (int[])Buckets.GetValue(storage));
        });
    }
    static void Enumeration(BinaryWriter writer, IGameplayTagContainer container, bool exact)
    {
        var enumerator = exact ? container.GetExplicitTags() : container.GetTags(); int count = 0;
        while (enumerator.MoveNext()) {
            Check(++count <= container.TagCount, "enumeration exceeds closure"); writer.Write(enumerator.Current.RuntimeIndex);
        }
        Check(count == (exact ? container.ExplicitTagCount : container.TagCount), "enumeration count"); writer.Write(-1);
    }
    static Snapshot Capture(IGameplayTagContainer container)
    {
        var result = new Snapshot { Storage = Raw(container.Indices.Storage) };
        result.Public = Digest(w => {
            w.Write("complete-public-v1"); w.Write(container.IsEmpty); w.Write(container.TagCount); w.Write(container.ExplicitTagCount);
            w.Write(container.Indices.IsCreated); w.Write(container.Indices.IsEmpty);
            w.Write(container.Indices.TagCount); w.Write(container.Indices.ExplicitTagCount);
            Enumeration(w, container, false); Enumeration(w, container, true);
            int enumerated = 0;
            foreach (GameplayTag tag in container) { Check(++enumerated <= container.TagCount, "enumerable limit"); w.Write(tag.RuntimeIndex); }
            Check(enumerated == container.TagCount, "enumerable count"); w.Write(-1);
            // Every registered ID, including all absent filler IDs, is queried.
            for (int id = 0; id <= GameplayTagManager.TagCount; id++) {
                GameplayTag tag = GameplayTagManager.GetTagFromRuntimeIndex(id);
                w.Write(id); w.Write(container.HasTag(tag)); w.Write(container.HasTagExact(tag));
                w.Write(container.Indices.Storage.GetCount(id)); w.Write(container.Indices.Storage.GetExplicitCount(id));
                if (container is GameplayTagCountContainer counted) { w.Write(counted.GetTagCount(tag)); w.Write(counted.GetExplicitTagCount(tag)); }
            }
            foreach (GameplayTag tag in new[] { SeedRoot, SourceRoot, SeedTags[0], SourceTags[0], SourceTags[64], Old, Spare }) {
                var selected = new List<GameplayTag>();
                container.GetParentTags(tag, selected); w.Write(selected.Count); foreach (var item in selected) w.Write(item.RuntimeIndex); selected.Clear();
                container.GetExplicitParentTags(tag, selected); w.Write(selected.Count); foreach (var item in selected) w.Write(item.RuntimeIndex); selected.Clear();
                container.GetChildTags(tag, selected); w.Write(selected.Count); foreach (var item in selected) w.Write(item.RuntimeIndex); selected.Clear();
                container.GetExplicitChildTags(tag, selected); w.Write(selected.Count); foreach (var item in selected) w.Write(item.RuntimeIndex);
            }
        });
        result.Auxiliary = Digest(w => {
            w.Write("complete-auxiliary-v1");
            if (container is GameplayTagContainer normal) {
                w.Write("normal"); var list = normal.m_SerializedExplicitTags; w.Write(list != null);
                if (list != null) {
                    w.Write(list.Count); w.Write(list.Capacity); w.Write((int)ListVersion.GetValue(list));
                    var items = (string[])ListItems.GetValue(list); w.Write(items.Length);
                    foreach (string item in items) { w.Write(item != null); if (item != null) w.Write(item); }
                }
            } else {
                w.Write("counted");
                foreach (string name in CountArrays) {
                    Array array = (Array)typeof(GameplayTagCountContainer).GetField(name, Fields).GetValue(container);
                    Check(array.Length == 0, "fixture unexpectedly installed counted callbacks or batch state"); w.Write(name); w.Write(array.Length);
                }
                foreach (string name in CountScalars) { w.Write(name); w.Write((int)typeof(GameplayTagCountContainer).GetField(name, Fields).GetValue(container)); }
                w.Write((bool)typeof(GameplayTagCountContainer).GetField("m_Dispatching", Fields).GetValue(container));
                foreach (string name in new[] { "OnAnyTagCountChange", "OnAnyTagNewOrRemove" }) {
                    bool present = typeof(GameplayTagCountContainer).GetField(name, Fields).GetValue(container) != null;
                    Check(!present, "fixture unexpectedly installed listener"); w.Write(present);
                }
            }
        });
        result.Full = Digest(w => { w.Write(result.Storage); w.Write(result.Public); w.Write(result.Auxiliary); }); return result;
    }
    static object[] RawRefs(TagStorage s) => s == null ? new object[] { null } : new object[] { s, s.Entries, s.ExplicitIndices, Buckets.GetValue(s) };
    static object[] Refs(IGameplayTagContainer c)
    {
        var refs = new List<object>(RawRefs(c.Indices.Storage));
        if (c is GameplayTagContainer normal) { refs.Add(normal.m_SerializedExplicitTags); refs.Add(normal.m_SerializedExplicitTags == null ? null : ListItems.GetValue(normal.m_SerializedExplicitTags)); }
        else foreach (string name in CountArrays) refs.Add(typeof(GameplayTagCountContainer).GetField(name, Fields).GetValue(c));
        return refs.ToArray();
    }
    static bool SameRefs(object[] a, object[] b) => a.Length == b.Length && !a.Where((value, i) => !ReferenceEquals(value, b[i])).Any();
    static Saved Save(IGameplayTagContainer c) => new Saved { Container = c, Snapshot = Capture(c), References = Refs(c) };
    static void Unchanged(Saved saved, string label)
    {
        Check(Capture(saved.Container).Full == saved.Snapshot.Full && SameRefs(saved.References, Refs(saved.Container)), "source mutation: " + label); ImmutabilityChecks++;
    }
    static void Observe(string id, string stage, IGameplayTagContainer c)
    {
        var snap = Capture(c); var storage = c.Indices.Storage; Observations++;
        Console.WriteLine("{\"Kind\":\"State\",\"Id\":" + Q(id) + ",\"Stage\":" + Q(stage) + ",\"FullSHA256\":" + Q(snap.Full) +
            ",\"StorageSHA256\":" + Q(snap.Storage) + ",\"PublicSHA256\":" + Q(snap.Public) + ",\"AuxiliarySHA256\":" + Q(snap.Auxiliary) +
            ",\"Count\":" + storage.Count + ",\"ExplicitCount\":" + storage.ExplicitCount + ",\"EntriesCapacity\":" + storage.Entries.Length +
            ",\"ExplicitCapacity\":" + storage.ExplicitIndices.Length + ",\"BucketCapacity\":" + ((int[])Buckets.GetValue(storage)).Length +
            ",\"BucketMask\":" + (int)Mask.GetValue(storage) + "}");
    }
    static T[] CloneArray<T>(T[] source) => source == null || source.Length == 0 ? source : (T[])source.Clone();
    static void CloneInto(TagStorage output, TagStorage source)
    {
        output.Count = source.Count; output.ExplicitCount = source.ExplicitCount;
        output.Entries = CloneArray(source.Entries); output.ExplicitIndices = CloneArray(source.ExplicitIndices);
        Buckets.SetValue(output, CloneArray((int[])Buckets.GetValue(source))); Mask.SetValue(output, Mask.GetValue(source));
    }
    static TagStorage CloneStorage(TagStorage source) { var clone = new TagStorage(); CloneInto(clone, source); return clone; }
    static GameplayTagContainer CloneExact(GameplayTagContainer source)
    {
        var clone = new GameplayTagContainer(); CloneInto(clone.Indices.Storage, source.Indices.Storage);
        if (source.m_SerializedExplicitTags != null) {
            var list = source.m_SerializedExplicitTags; var copy = new List<string>();
            ListItems.SetValue(copy, CloneArray((string[])ListItems.GetValue(list))); ListSize.SetValue(copy, list.Count); ListVersion.SetValue(copy, ListVersion.GetValue(list));
            clone.m_SerializedExplicitTags = copy;
        }
        Check(Capture(clone).Full == Capture(source).Full, "exact prototype clone differs"); return clone;
    }
    static void Register()
    {
        var registrations = new List<GameplayTagRegistration>();
        for (int i = 0; i < 65; i++) { registrations.Add(new GameplayTagRegistration("Seed.Leaf" + i.ToString("D2"))); registrations.Add(new GameplayTagRegistration("Source.Leaf" + i.ToString("D2"))); }
        for (int i = 0; i < 8192; i++) registrations.Add(new GameplayTagRegistration("Filler" + i.ToString("D4")));
        registrations.Add(new GameplayTagRegistration("Old")); registrations.Add(new GameplayTagRegistration("Spare"));
        GameplayTagManager.Initialize(registrations.ToArray());
        SeedTags = new GameplayTag[65]; SourceTags = new GameplayTag[65]; Fillers = new GameplayTag[8192];
        for (int i = 0; i < 65; i++) { SeedTags[i] = GameplayTagManager.RequestTag("Seed.Leaf" + i.ToString("D2")); SourceTags[i] = GameplayTagManager.RequestTag("Source.Leaf" + i.ToString("D2")); }
        for (int i = 0; i < 8192; i++) Fillers[i] = GameplayTagManager.RequestTag("Filler" + i.ToString("D4"));
        SeedRoot = GameplayTagManager.RequestTag("Seed"); SourceRoot = GameplayTagManager.RequestTag("Source");
        Old = GameplayTagManager.RequestTag("Old"); Spare = GameplayTagManager.RequestTag("Spare");
    }
    static string Key(int capacity, string history) => capacity + "/" + history;
    static void PreparePrototypes()
    {
        Prototypes.Add("0/true-empty", new GameplayTagContainer());
        foreach (int capacity in Capacities) foreach (string history in Histories) {
            var output = new GameplayTagContainer();
            if (history == "reserved-empty" || history == "dirty-one") output.EnsureCapacity(capacity);
            if (history == "dirty-one") { output.AddTag(Old); output.OnBeforeSerialize(); }
            if (history == "grown-cleared" || history == "grown-drained") {
                for (int i = 0; i < capacity; i++) output.AddTag(Fillers[i]);
                output.OnBeforeSerialize();
                if (history == "grown-cleared") output.Clear();
                else for (int i = 0; i < capacity; i++) output.RemoveTag(Fillers[i]);
            }
            Check(output.Indices.Storage.ExplicitIndices.Length == capacity, "prototype capacity");
            Prototypes.Add(Key(capacity, history), output);
        }
        foreach (var pair in Prototypes) {
            var output = pair.Value;
            Console.WriteLine("{\"Kind\":\"Prototype\",\"Id\":" + Q(pair.Key) + ",\"StateSHA256\":" + Q(Capture(output).Full) +
                ",\"Setup\":" + Q(pair.Key == "0/true-empty" ? "new" : pair.Key.EndsWith("reserved-empty") ? "EnsureCapacity" :
                pair.Key.EndsWith("dirty-one") ? "EnsureCapacity;Add Old;serialize" : pair.Key.EndsWith("grown-cleared") ? "Add capacity independent roots;serialize;Clear" :
                "Add capacity independent roots;serialize;Remove ascending") + "}");
        }
    }
    static int Overlap(int a, int e, int mode) { int possible = Math.Min(a,e); return mode == 0 ? 0 : mode == 1 ? possible / 2 : possible; }
    static GameplayTagContainer Seed(int a)
    {
        var seed = new GameplayTagContainer(); seed.AddTag(Old); seed.RemoveTag(Old);
        for (int i = 0; i < a; i++) seed.AddTag(SeedTags[i]); seed.OnBeforeSerialize(); return seed;
    }
    static IGameplayTagContainer Source(int a, int e, int overlapMode, bool counted)
    {
        int overlap = Overlap(a,e,overlapMode);
        if (counted) {
            var source = new GameplayTagCountContainer(); source.AddTag(Old,3); source.RemoveTag(Old,3);
            for (int i = 0; i < e; i++) source.AddTag(i < overlap ? SeedTags[i] : SourceTags[i],3); return source;
        }
        var normal = new GameplayTagContainer(); normal.AddTag(Old); normal.RemoveTag(Old);
        for (int i = 0; i < e; i++) normal.AddTag(i < overlap ? SeedTags[i] : SourceTags[i]); normal.OnBeforeSerialize(); return normal;
    }
    static int Round(int count) { int cap = 8; while (cap < count) cap = checked(cap * 2); return cap; }
    static int Growth(int a, int f, int initial, out int copyCapacity, out int finalCapacity)
    {
        int allocations = 0, capacity = initial;
        if (capacity < a) { capacity = Round(a); allocations++; }
        copyCapacity = capacity;
        while (capacity < f) { capacity = checked(Math.Max(8,capacity * 2)); allocations++; }
        finalCapacity = capacity; return allocations;
    }
    static Plan PlanFor(TagStorage seed, TagStorage source, TagStorage output)
    {
        int missing = 0;
        for (int i = 0; i < source.ExplicitCount; i++) {
            int id = source.Entries[source.ExplicitIndices[i]].Id;
            int index = seed.Find(id); if (index < 0 || seed.Entries[index].ExplicitCount == 0) missing++;
        }
        var plan = new Plan { A = seed.ExplicitCount, E = source.ExplicitCount, M = missing,
            F = checked(seed.ExplicitCount + missing), L0 = output.ExplicitIndices.Length };
        plan.Allocations = Growth(plan.A,plan.F,plan.L0,out plan.L1,out plan.LF);
        plan.CopyAllocations = plan.L0 < plan.A ? 1 : 0;
        int unused1,unused2; plan.UpperAllocations = Growth(plan.A,checked(plan.A+plan.E),plan.L0,out unused1,out unused2); return plan;
    }
    static void EmitPlan(string id, Plan plan, string route)
    {
        Console.WriteLine("{\"Kind\":\"Plan\",\"Id\":" + Q(id) + ",\"Route\":" + Q(route) + ",\"A\":" + plan.A + ",\"E\":" + plan.E +
            ",\"M\":" + plan.M + ",\"F\":" + plan.F + ",\"L0\":" + plan.L0 + ",\"L1\":" + plan.L1 + ",\"LF\":" + plan.LF +
            ",\"CopyAllocations\":" + plan.CopyAllocations + ",\"OriginalExplicitAllocations\":" + plan.Allocations + ",\"UpperBoundAllocations\":" + plan.UpperAllocations + "}");
    }
    static void Select(IGameplayTagContainer lhs, IGameplayTagContainer rhs, out TagStorage a, out TagStorage b, out bool aIsSet)
    {
        a = lhs.Indices.Storage; b = rhs.Indices.Storage; aIsSet = lhs is GameplayTagContainer; bool bIsSet = rhs is GameplayTagContainer;
        if (a == null || (b != null && ((!aIsSet && bIsSet) || (aIsSet == bIsSet && a.Count < b.Count)))) {
            var swap = a; a = b; b = swap; aIsSet = bIsSet;
        }
    }
    static void Apply(GameplayTagContainer output, IGameplayTagContainer lhs, IGameplayTagContainer rhs)
    {
#if REUSABLE_UNION
        GameplayTagContainer.Union(output,lhs,rhs);
#else
        GameplayTagContainer.Copy(output,GameplayTagContainer.Union(lhs,rhs));
#endif
    }
    static void Continue(string id, GameplayTagContainer output)
    {
        output.OnBeforeSerialize(); Observe(id,"serialized",output);
        GameplayTag first = default(GameplayTag); var explicitTags = output.GetExplicitTags();
        if (explicitTags.MoveNext()) first = explicitTags.Current;
        Check(first.RuntimeIndex != 0,"continuation requires nonempty result");
        output.RemoveTag(first); Observe(id,"remove-first",output);
        output.AddTag(first); output.AddTag(first); output.AddTag(Spare); Observe(id,"readd-duplicate-spare",output);
        var copy = CloneExact(Prototypes[Key(128,"grown-drained")]); var before = Save(output);
        GameplayTagContainer.Copy(copy,output); Observe(id,"copy-retained",copy); Unchanged(before,id+"/copy-source");
        output.Clear(); Observe(id,"clear",output);
        output.AddTag(Spare); output.AddTag(SourceTags[64]); Observe(id,"regrow",output);
        output.OnBeforeSerialize(); Observe(id,"regrow-serialized",output);
    }
    static void PublicCase(string id, IGameplayTagContainer lhs, IGameplayTagContainer rhs, GameplayTagContainer output, TagStorage expectedSeed = null)
    {
        Cases++; TagStorage selected,other; bool selectedIsSet; Select(lhs,rhs,out selected,out other,out selectedIsSet);
        Check(selectedIsSet,"public fixture did not select normal seed");
        if (expectedSeed != null) Check(ReferenceEquals(selected,expectedSeed),"counted fixture lost designated normal seed");
        bool alias = ReferenceEquals(output.Indices.Storage,lhs.Indices.Storage) || ReferenceEquals(output.Indices.Storage,rhs.Indices.Storage);
        var planOutput = alias ? new TagStorage() : output.Indices.Storage;
        if (ReferenceEquals(selected,other))
            Console.WriteLine("{\"Kind\":\"SameStorageRoute\",\"Id\":"+Q(id)+",\"OutputAliases\":"+(alias ? "true" : "false")+",\"ReservationCallExpected\":false}");
        else EmitPlan(id,PlanFor(selected,other,planOutput),alias ? "public-alias-fresh-temporary" : "public-distinct");
        var saved = new List<Saved>();
        if (!ReferenceEquals(output.Indices.Storage,lhs.Indices.Storage)) saved.Add(Save(lhs));
        if (!ReferenceEquals(output.Indices.Storage,rhs.Indices.Storage) && !ReferenceEquals(lhs,rhs)) saved.Add(Save(rhs));
        Observe(id,"lhs-before",lhs); Observe(id,"rhs-before",rhs); Observe(id,"output-before",output);
        Apply(output,lhs,rhs); Observe(id,"union",output);
        foreach (var input in saved) Unchanged(input,id+"/after-union");
        Continue(id,output);
        foreach (var input in saved) Unchanged(input,id+"/after-continuation");
    }
    static void PublicMatrix()
    {
        for (int p = 0; p < Pairs.GetLength(0); p++) for (int overlap = 0; overlap < 3; overlap++) {
            int a = Pairs[p,0], e = Pairs[p,1]; var seed = Seed(a); var source = Source(a,e,overlap,true);
            foreach (var prototype in Prototypes) {
                string id = "counted-forward/"+a+"/"+e+"/"+overlap+"/"+prototype.Key;
                PublicCase(id,seed,source,CloneExact(prototype.Value),seed.Indices.Storage);
            }
            foreach (string key in new[] { "0/true-empty", Key(8,"dirty-one") })
                PublicCase("counted-reverse/"+a+"/"+e+"/"+overlap+"/"+key,source,seed,CloneExact(Prototypes[key]),seed.Indices.Storage);
        }
        foreach (int[] pair in new[] { new[] {9,17}, new[] {17,9}, new[] {17,17} })
        for (int overlap = 0; overlap < 3; overlap++) for (int reversed = 0; reversed < 2; reversed++)
        foreach (string mode in new[] { "distinct-empty", "distinct-retained", "alias-left", "alias-right" }) {
            var seed = Seed(pair[0]); var source = Source(pair[0],pair[1],overlap,false);
            IGameplayTagContainer lhs = reversed == 0 ? seed : source, rhs = reversed == 0 ? source : seed;
            var output = mode == "alias-left" ? (GameplayTagContainer)lhs : mode == "alias-right" ? (GameplayTagContainer)rhs :
                CloneExact(Prototypes[mode == "distinct-empty" ? "0/true-empty" : Key(8192,"grown-drained")]);
            PublicCase("normal/"+pair[0]+"/"+pair[1]+"/"+overlap+"/"+reversed+"/"+mode,lhs,rhs,output);
        }
        foreach (int a in new[] { 1,9,17 }) foreach (bool alias in new[] { false,true }) {
            var seed = Seed(a); PublicCase("same-storage/"+a+"/"+alias,seed,seed,alias ? seed : CloneExact(Prototypes[Key(128,"grown-drained")]));
        }
        Check(Cases == 1374,"public case count"); Check(Observations == Cases * 11,"public observation count");
    }
    static void InvokeReserve(TagStorage output, TagStorage seed, TagStorage source)
    {
        Check(Reserve != null,"candidate reservation helper unavailable"); Reserve.Invoke(output,new object[] { seed,source });
    }
    static void VerifyReservationOnly(TagStorage output, TagStorage before, object[] references, bool expected, Plan plan)
    {
        Check(output.Count == before.Count && output.ExplicitCount == before.ExplicitCount,"reservation changed counts");
        Check(ReferenceEquals(output.Entries,references[1]) && ReferenceEquals(Buckets.GetValue(output),references[3]) &&
            Equals(Mask.GetValue(output),Mask.GetValue(before)),"reservation changed non-explicit state");
        Check(output.ExplicitIndices.Length == (expected ? plan.LF : plan.L0),"reservation capacity");
        Check(ReferenceEquals(output.ExplicitIndices,references[2]) == !expected,"reservation identity");
        for (int i = 0; i < before.ExplicitIndices.Length; i++) Check(output.ExplicitIndices[i] == before.ExplicitIndices[i],"reservation discarded old tail");
        for (int i = before.ExplicitIndices.Length; i < output.ExplicitIndices.Length; i++) Check(output.ExplicitIndices[i] == 0,"reservation new tail not zero");
        var normalized = CloneStorage(output); normalized.ExplicitIndices = before.ExplicitIndices;
        Check(Raw(normalized) == Raw(before),"reservation changed an unlisted owned field");
    }
    static void DiagnosticCase(string id, GameplayTagContainer seed, IGameplayTagContainer source, GameplayTagContainer output, bool seedAlias)
    {
        HelperCases++; var reference = CloneExact(output); var before = CloneStorage(output.Indices.Storage); var refs = RawRefs(output.Indices.Storage);
        var sourceSaved = Save(source); var seedSaved = seedAlias ? null : Save(seed);
        var plan = PlanFor(seed.Indices.Storage,source.Indices.Storage,output.Indices.Storage);
        bool expected = plan.E > 8 && plan.Allocations > 1;
        if (plan.Allocations == 0) ZeroPlans++; else if (plan.Allocations == 1) OnePlans++; else MultiPlans++;
        if (plan.CopyAllocations == 0) CopyZeroPlans++; else CopyOnePlans++;
        if (plan.UpperAllocations > 1 && plan.Allocations <= 1) OverlapReducedPlans++;
        Check((bool)Gate.Invoke(seed.Indices.Storage,new object[] { GameplayTagManager.TagCount,true }),"healthy diagnostic seed gate");
        Check((bool)Gate.Invoke(source.Indices.Storage,new object[] { GameplayTagManager.TagCount,false }),"healthy diagnostic source gate");
        EmitPlan(id,plan,seedAlias ? "private-selected-seed-alias-direct-AddUnion" : "private-distinct-direct-CopyFrom-AddUnion");
        InvokeReserve(output.Indices.Storage,seed.Indices.Storage,source.Indices.Storage);
        VerifyReservationOnly(output.Indices.Storage,before,refs,expected,plan);
        if (expected) ReserveCases++; else { SkipCases++; Check(Raw(output.Indices.Storage) == Raw(before),"skip changed output bytes"); }
        Unchanged(sourceSaved,id+"/reservation-source"); if (seedSaved != null) Unchanged(seedSaved,id+"/reservation-seed");
        if (!seedAlias) { output.Indices.Storage.CopyFrom(seed.Indices.Storage); reference.Indices.Storage.CopyFrom(seed.Indices.Storage); }
        output.Indices.Storage.AddUnion(source.Indices.Storage); reference.Indices.Storage.AddUnion(source.Indices.Storage);
        Check(Capture(output).Full == Capture(reference).Full,"reservation changed unchanged AddUnion result: "+id);
        Check(output.Indices.Storage.ExplicitIndices.Length == plan.LF,"final capacity differs from exact original simulation");
        Observe(id,"diagnostic-final",output); Observe(id,"diagnostic-reference",reference);
        // Identical later mutations expose stale index/tail mistakes without using public Union.
        output.OnBeforeSerialize(); reference.OnBeforeSerialize(); output.RemoveTag(SeedTags[0]); reference.RemoveTag(SeedTags[0]);
        output.AddTag(Spare); reference.AddTag(Spare); output.Clear(); reference.Clear();
        output.AddTag(SourceTags[64]); reference.AddTag(SourceTags[64]);
        Check(Capture(output).Full == Capture(reference).Full,"reservation continuation differs: "+id); Observe(id,"diagnostic-continuation",output);
        Unchanged(sourceSaved,id+"/completion-source"); if (seedSaved != null) Unchanged(seedSaved,id+"/completion-seed");
        Console.WriteLine("{\"Kind\":\"Helper\",\"Id\":"+Q(id)+",\"Reserved\":"+(expected ? "true" : "false")+",\"UnchangedLegacyMatch\":true}");
    }
    static void Rejection(string id, TagStorage output, TagStorage seed, TagStorage source)
    {
        var all = new[] { output,seed,source }; var hashes = all.Select(Raw).ToArray(); var refs = all.Select(RawRefs).ToArray();
        InvokeReserve(output,seed,source);
        for (int i = 0; i < all.Length; i++) Check(hashes[i] == Raw(all[i]) && SameRefs(refs[i],RawRefs(all[i])),"helper rejection wrote: "+id);
        HelperCases++; SkipCases++;
        Console.WriteLine("{\"Kind\":\"Rejected\",\"Id\":"+Q(id)+",\"OutputSHA256\":"+Q(hashes[0])+",\"SeedSHA256\":"+Q(hashes[1])+",\"SourceSHA256\":"+Q(hashes[2])+",\"AllBytesAndReferencesUnchanged\":true}");
    }
    static void RejectionMatrix()
    {
        var normal = Seed(9).Indices.Storage; var counted = Source(9,17,0,true).Indices.Storage;
        var corruptions = new Dictionary<string,Action<TagStorage>> {
            {"entries-null",s=>s.Entries=null}, {"explicit-null",s=>s.ExplicitIndices=null}, {"buckets-null",s=>Buckets.SetValue(s,null)},
            {"count-zero",s=>s.Count=0}, {"count-over-array",s=>s.Count=s.Entries.Length+1},
            {"explicit-negative",s=>s.ExplicitCount=-1}, {"explicit-over-count",s=>s.ExplicitCount=s.Count+1},
            {"explicit-short-array",s=>s.ExplicitIndices=new int[1]}, {"mask-negative",s=>Mask.SetValue(s,-1)},
            {"mask-non-power",s=>Mask.SetValue(s,30)}, {"mask-zero-large",s=>Mask.SetValue(s,0)},
            {"bucket-encoded-over-count",s=>{var b=(int[])Buckets.GetValue(s); b[0]=s.Count+1;}},
            {"full-buckets",s=>{var b=(int[])Buckets.GetValue(s); for(int i=0;i<b.Length;i++)b[i]=1;}},
            {"id-zero",s=>s.Entries[0].Id=0}, {"id-outside-registry",s=>s.Entries[0].Id=GameplayTagManager.TagCount+1},
            {"explicit-mapping-negative",s=>s.ExplicitIndices[0]=-1}, {"explicit-mapping-duplicate",s=>s.ExplicitIndices[1]=s.ExplicitIndices[0]},
            {"explicit-mapped-zero",s=>s.Entries[s.ExplicitIndices[0]].ExplicitCount=0},
            {"explicit-reciprocal-invalid",s=>s.Entries[s.ExplicitIndices[0]].ExplicitIndex=-1},
            {"missing-ancestor",s=>{int id=s.Entries[s.ExplicitIndices[0]].Id; while(GameplayTagManager.GetParentIndex(id)!=0)id=GameplayTagManager.GetParentIndex(id); s.RemoveTotal(id,s.GetCount(id));}}
        };
        foreach (var item in corruptions) foreach (bool seedSide in new[] {true,false}) {
            var seed = CloneStorage(normal); var source = CloneStorage(counted); item.Value(seedSide ? seed : source);
            Rejection("malformed/"+(seedSide ? "seed/" : "source/")+item.Key,new TagStorage(),seed,source);
        }
        var badSeed = CloneStorage(normal); badSeed.Entries[badSeed.ExplicitIndices[0]].ExplicitCount=2;
        Rejection("seed-raw-explicit-two",new TagStorage(),badSeed,CloneStorage(counted));
        foreach (string part in new[] {"entries","explicit","buckets"}) {
            var output = new TagStorage(); if(part=="entries")output.Entries=null; else if(part=="explicit")output.ExplicitIndices=null; else Buckets.SetValue(output,null);
            Rejection("output-null-"+part,output,CloneStorage(normal),CloneStorage(counted));
        }
        Rejection("null-seed",new TagStorage(),null,CloneStorage(counted)); Rejection("null-source",new TagStorage(),CloneStorage(normal),null);
        var same = CloneStorage(normal); Rejection("same-input-storage",new TagStorage(),same,same);
        var opposite = CloneStorage(counted); Rejection("opposite-source-alias",opposite,CloneStorage(normal),opposite);
        // Small real arrays plus impossible scalar headers: every call must skip without allocation.
        // This is early-rejection coverage, NOT execution with billion-element valid containers.
        foreach (int boundary in new[] {(1<<30)-1,1<<30,(1<<30)+1,int.MaxValue}) foreach (bool seedSide in new[] {true,false}) {
            var seed=CloneStorage(normal); var source=CloneStorage(counted);
            if(seedSide)seed.ExplicitCount=boundary; else source.ExplicitCount=boundary;
            Rejection("extreme-header/"+(seedSide ? "seed/" : "source/")+boundary,new TagStorage(),seed,source);
        }
    }
    static void DiagnosticMatrix()
    {
        Check(Reserve != null && Gate != null,"candidate diagnostics unavailable");
        for (int p=0;p<Pairs.GetLength(0);p++) for (int overlap=0;overlap<3;overlap++) {
            int a=Pairs[p,0],e=Pairs[p,1]; var seed=Seed(a); var source=Source(a,e,overlap,true);
            foreach(var prototype in Prototypes)
                DiagnosticCase("canonical/"+a+"/"+e+"/"+overlap+"/"+prototype.Key,seed,source,CloneExact(prototype.Value),false);
            foreach(int capacity in UnusualCapacities) {
                var output=CloneExact(Prototypes[Key(8,"dirty-one")]);
                // Initial live prefix is exactly one, so every private length is sufficient.
                var indices=new int[capacity]; for(int i=0;i<capacity;i++)indices[i]=unchecked(0x13570000+i);
                indices[0]=output.Indices.Storage.ExplicitIndices[0]; output.Indices.Storage.ExplicitIndices=indices;
                DiagnosticCase("private-unusual/"+a+"/"+e+"/"+overlap+"/"+capacity,seed,source,output,false);
            }
        }
        // Alias diagnostics have canonical capacities >= A and do not claim a public alias fast path.
        foreach(int a in new[] {1,9,17}) foreach(int e in new[] {9,17,65}) foreach(int overlap in new[] {0,2})
        foreach(int capacity in new[] {128,8192}) {
            var seed=Seed(a); seed.EnsureCapacity(capacity); var source=Source(a,e,overlap,true);
            DiagnosticCase("private-selected-alias/"+a+"/"+e+"/"+overlap+"/"+capacity,seed,source,seed,true);
        }
        // Small canonical alias buffers include actual multiple-growth reservations.
        foreach(int a in new[] {1,9,17}) foreach(int overlap in new[] {0,2}) {
            var seed=Seed(a); var source=Source(a,65,overlap,true);
            DiagnosticCase("private-selected-alias-growing/"+a+"/"+overlap,seed,source,seed,true);
        }
        RejectionMatrix();
        Check(HelperCases==1682,"diagnostic case count"); Check(Observations==1626*3,"diagnostic observation count");
        Check(ZeroPlans>0&&OnePlans>0&&MultiPlans>0&&OverlapReducedPlans>0&&CopyZeroPlans>0&&CopyOnePlans>0&&ReserveCases>0&&SkipCases>0,"missing allocation class");
    }
    static int Main(string[] args)
    {
        try {
            Check(args.Length==1&&(args[0]=="public"||args[0]=="diagnostic"),"mode must be public or diagnostic");
            Register(); PreparePrototypes();
            if(args[0]=="public")PublicMatrix(); else DiagnosticMatrix();
            Console.Error.WriteLine("SUMMARY mode="+args[0]+" cases="+Cases+" helperCases="+HelperCases+" observations="+Observations+
                " immutabilityChecks="+ImmutabilityChecks+" reserves="+ReserveCases+" skips="+SkipCases+" zeroPlans="+ZeroPlans+
                " onePlans="+OnePlans+" multiPlans="+MultiPlans+" overlapReducedPlans="+OverlapReducedPlans+
                " copyZeroPlans="+CopyZeroPlans+" copyOnePlans="+CopyOnePlans);
            return 0;
        } catch(Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
    }
}
