using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using GameplayTags;

internal static class FinalConsolidation
{
    private static readonly Func<long> Allocated = Counter();
    private static GameplayTag[] Tags;
    private static GameplayTag Root, Deep128, Deep3000;
    private static long Checksum;
    private static object Sink;
    private static int Assertions;
    private static Func<long> Counter()
    {
        var method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
        if (method == null) throw new NotSupportedException("Per-thread allocation counter required.");
        return (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
    }
    private static void Check(bool condition, string context)
    { Assertions++; if (!condition) throw new Exception(context); }
    private static void Initialize()
    {
        var names = new string[2048]; var registrations = new List<GameplayTagRegistration>();
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = "World.G" + (i % 32).ToString("D2") + ".T" + i.ToString("D4");
            registrations.Add(new GameplayTagRegistration(names[i]));
        }
        string chain = "Deep";
        for (int i = 1; i < 128; i++) chain += ".N";
        string d128 = chain;
        for (int i = 128; i < 3000; i++) chain += ".N";
        registrations.Add(new GameplayTagRegistration(chain));
        GameplayTagManager.Initialize(registrations.ToArray());
        Tags = new GameplayTag[names.Length];
        for (int i = 0; i < Tags.Length; i++) Tags[i] = GameplayTagManager.RequestTag(names[i]);
        Root = GameplayTagManager.RequestTag("World");
        Deep128 = GameplayTagManager.RequestTag(d128); Deep3000 = GameplayTagManager.RequestTag(chain);
    }
    private static GameplayTagContainer Set(int start, int count)
    {
        var result = new GameplayTagContainer();
        for (int i = 0; i < count; i++) result.AddTag(Tags[start + (i * 719) % count]);
        return result;
    }
    private static void Observe(GameplayTagContainer value)
    { Checksum = unchecked(Checksum + value.TagCount + value.ExplicitTagCount); Sink = value; }
    private static void Validate(GameplayTagContainer value, HashSet<string> explicitNames)
    {
        var counts = new Dictionary<string,int>(StringComparer.Ordinal);
        foreach (string name in explicitNames)
        {
            string part = name;
            while (true)
            {
                counts.TryGetValue(part, out int count); counts[part] = count + 1;
                int dot = part.LastIndexOf('.'); if (dot < 0) break; part = part.Substring(0, dot);
            }
        }
        Check(value.ExplicitTagCount == explicitNames.Count && value.TagCount == counts.Count, "cardinality");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (GameplayTag tag in value.GetTags())
        {
            Check(seen.Add(tag.Name), "duplicate dense entry");
            Check(counts.TryGetValue(tag.Name, out int n), "unexpected closure member");
            Check(value.Indices.Storage.GetCount(tag.RuntimeIndex) == n, "ancestor contribution");
            Check(value.HasTagExact(tag) == explicitNames.Contains(tag.Name), "explicit membership");
        }
        seen.Clear(); foreach (GameplayTag tag in value.GetExplicitTags()) Check(seen.Add(tag.Name), "duplicate explicit");
        Check(seen.SetEquals(explicitNames), "explicit enumeration");
    }
    private static void Regression()
    {
        // Alternating histories deliberately leave dirty inactive bucket tails.
        var output = new GameplayTagContainer(8192); var copy = new GameplayTagContainer(16384);
        var workspace = new GameplayTagIntersectionWorkspace(); var random = new Random(710912);
        for (int round = 0; round < 320; round++)
        {
            output.Clear(); Check(output.TagCount == 0 && !output.HasTag(Root), "clear query");
            output.Clear(); Check(!output.HasTagExact(Tags[0]), "repeated clear query");
            int count = new[] { 1, 2, 4, 8, 9, 16, 128, 1024 }[round % 8];
            var expected = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++) { var tag = Tags[random.Next(Tags.Length)]; output.AddTag(tag); expected.Add(tag.Name); }
            Validate(output, expected); GameplayTagContainer.Copy(copy, output); Validate(copy, expected);
            // Copies into a larger retained index must never expose old buckets.
            var right = Set(round % 2 == 0 ? 0 : 1024, 1024);
            var intersection = new HashSet<string>(expected); intersection.RemoveWhere(n => !right.HasTagExact(GameplayTagManager.RequestTag(n)));
            GameplayTagContainer.Intersection(copy, output, right, workspace); Validate(copy, intersection);
            GameplayTagContainer.Intersection(copy, output, right); Validate(copy, intersection);
            foreach (string name in expected) output.RemoveTag(GameplayTagManager.RequestTag(name));
            Check(output.TagCount == 0, "drain after dirty-index reuse");
        }
        foreach (var tag in new[] { Deep128, Deep3000 })
        {
            output.Clear(); output.AddTag(tag); Check(output.TagCount == tag.HierarchyLevel, "single deep path");
            output.AddTag(tag); Check(output.ExplicitTagCount == 1, "duplicate deep");
            output.RemoveTag(tag); Check(output.TagCount == 0, "single deep drain");
        }
        var empty = default(GameplayTagEnumerator); Check(!empty.MoveNext(), "default enumerator"); empty.Reset(); Check(!empty.MoveNext(), "default reset");
        output.AddTag(Tags[0]); var iterator = output.GetExplicitTags(); Check(iterator.MoveNext() && iterator.Current == Tags[0] && !iterator.MoveNext(), "cached bound"); iterator.Reset(); Check(iterator.MoveNext(), "reset cached bound");
        Console.Error.WriteLine("PASS final consolidation regression: " + Assertions + " assertions; seed 710912.");
    }
    private sealed class Work
    { internal string Id; internal Action Action; internal int Pilot; internal Work(string id, Action action, int pilot = 1000) { Id = id; Action = action; Pilot = pilot; } }
    private static List<Work> Cases()
    {
        var cases = new List<Work>();
        foreach (int n in new[] { 4, 128, 1024 })
        {
            var a = Set(0, n); var b = Set(n / 2, n); var equal = Set(0,n); var output = new GameplayTagContainer(4096);
            var workspace = new GameplayTagIntersectionWorkspace(n); var tag = Tags[n / 2]; var missing = Tags[2047]; string p = "n"+n+"/";
            cases.Add(new Work(p+"query-exact-hit", () => { Checksum += a.HasTagExact(tag) ? 1 : 0; }, 100000));
            cases.Add(new Work(p+"query-exact-miss", () => { Checksum += a.HasTagExact(missing) ? 1 : 0; }, 100000));
            cases.Add(new Work(p+"query-parent", () => { Checksum += a.HasTag(Root) ? 1 : 0; }, 100000));
            cases.Add(new Work(p+"enumerate-explicit", () => { long sum=0;foreach(var t in a.GetExplicitTags())sum+=t.RuntimeIndex;Checksum+=sum; }));
            cases.Add(new Work(p+"remove-add", () => { a.RemoveTag(tag); a.AddTag(tag); Observe(a); }));
            cases.Add(new Work(p+"fresh-build", () => { var r=new GameplayTagContainer();for(int i=0;i<n;i++)r.AddTag(Tags[i]);Observe(r); }, 50));
            cases.Add(new Work(p+"copy-reused", () => { GameplayTagContainer.Copy(output,a);Observe(output); }));
            cases.Add(new Work(p+"union-new", () => Observe(GameplayTagContainer.Union(a,b)), 100));
            cases.Add(new Work(p+"union-reused", () => { GameplayTagContainer.Union(output,a,b);Observe(output); }, 100));
            cases.Add(new Work(p+"copy-addtags", () => { GameplayTagContainer.Copy(output,a);output.AddTags(b);Observe(output); }, 100));
            cases.Add(new Work(p+"intersection-reused", () => { GameplayTagContainer.Intersection(output,a,b);Observe(output); }, 100));
            cases.Add(new Work(p+"intersection-workspace", () => { GameplayTagContainer.Intersection(output,a,b,workspace);Observe(output); }, 100));
            cases.Add(new Work(p+"intersection-new", () => Observe(GameplayTagContainer.Intersection(a,b)), 100));
            cases.Add(new Work(p+"intersection-full", () => { GameplayTagContainer.Intersection(output,a,equal);Observe(output); }, 100));
            cases.Add(new Work(p+"copy-clear", () => { GameplayTagContainer.Copy(output,a);output.Clear();Observe(output); }));
            cases.Add(new Work(p+"clear-refill", () => { output.Clear();for(int i=0;i<n;i++)output.AddTag(Tags[i]);Observe(output); }, 100));
        }
        var counted = new GameplayTagCountContainer(4096); counted.AddTag(Tags[0],2);
        cases.Add(new Work("counted/existing-add-remove", () => {counted.AddTag(Tags[0]);counted.RemoveTag(Tags[0]);Checksum+=counted.GetTagCount(Root);},10000));
        var global = new GameplayTagCountContainer(); global.OnAnyTagCountChange += (t,c) => { Checksum=unchecked(Checksum+t.RuntimeIndex+c); };
        cases.Add(new Work("buff/global-transition", () => {global.AddTag(Tags[0]);global.RemoveTag(Tags[0]);},10000));
        var specific = new GameplayTagCountContainer(); specific.RegisterTagEventCallback(Root,GameplayTagEventType.AnyCountChange,(t,c)=>{Checksum+=c;});
        cases.Add(new Work("buff/specific-transition", () => {specific.AddTag(Tags[0]);specific.RemoveTag(Tags[0]);},10000));
        var dense = new GameplayTagCountContainer(); foreach(var t in Deep128.HierarchyTags) dense.RegisterTagEventCallback(t,GameplayTagEventType.AnyCountChange,(x,c)=>{Checksum+=c;});
        cases.Add(new Work("buff/dense-depth128", () => {dense.AddTag(Deep128);dense.RemoveTag(Deep128);},100));
        foreach (var tag in new[] {Deep128,Deep3000})
        {
            var reuse = new GameplayTagContainer(); string p="depth"+tag.HierarchyLevel+"/";
            cases.Add(new Work(p+"fresh-single", () => {var c=new GameplayTagContainer();c.AddTag(tag);Observe(c);},50));
            cases.Add(new Work(p+"add-remove-empty", () => {reuse.AddTag(tag);reuse.RemoveTag(tag);Observe(reuse);},100));
        }
        return cases;
    }
    private static string Quote(string text) => "\""+text.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    private static void Main(string[] args)
    {
        Initialize();
        if(args.Length==0 || args[0]=="test"){Regression();return;}
        var iterations = new Dictionary<string,int>();
        if(args.Length>1)foreach(string line in File.ReadAllLines(args[1])){var bits=line.Split('\t');iterations.Add(bits[0],int.Parse(bits[1],CultureInfo.InvariantCulture));}
        var rows=new List<string>();
        foreach(var w in Cases())
        {
            for(int i=0;i<32;i++)w.Action();
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            int n=iterations.Count==0?w.Pilot:iterations[w.Id];
            int gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2);
            long allocated=Allocated(),start=Stopwatch.GetTimestamp();
            for(int i=0;i<n;i++)w.Action();
            long elapsed=Stopwatch.GetTimestamp()-start,bytes=Allocated()-allocated;
            rows.Add("{\"Id\":"+Quote(w.Id)+",\"Ns\":"+(elapsed*(1e9/Stopwatch.Frequency)/n).ToString("R",CultureInfo.InvariantCulture)+",\"Bytes\":"+((double)bytes/n).ToString("R",CultureInfo.InvariantCulture)+",\"Iterations\":"+n+",\"WallMilliseconds\":"+(elapsed*1000.0/Stopwatch.Frequency).ToString("R",CultureInfo.InvariantCulture)+",\"Gen0\":"+(GC.CollectionCount(0)-gc0)+",\"Gen1\":"+(GC.CollectionCount(1)-gc1)+",\"Gen2\":"+(GC.CollectionCount(2)-gc2)+"}");
        }
        Console.WriteLine("{\"Runtime\":"+Quote(Environment.Version.ToString())+",\"Framework\":"+Quote(Type.GetType("Mono.Runtime")==null?".NET":"Standalone Mono")+",\"Checksum\":"+Checksum+",\"Rows\":["+string.Join(",",rows)+"]}");GC.KeepAlive(Sink);
    }
}
