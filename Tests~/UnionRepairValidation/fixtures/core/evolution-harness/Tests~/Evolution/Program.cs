using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Text;
using GameplayTags;

internal static class Program
{
    private static object Sink;
    private static long Checksum;
    private static string[] Chain;
    private static string DeepSeed;
    private static Dictionary<string,int> FixedIterations;
    private static bool TestOnly;
    private static readonly Process Process = Process.GetCurrentProcess();
    private static readonly Func<long> AllocatedBytes = CreateAllocationCounter();
    private static Func<long> CreateAllocationCounter()
    {
        var method=typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",Type.EmptyTypes);
        if(method==null) throw new NotSupportedException("A real per-thread allocation counter is required.");
        return (Func<long>)Delegate.CreateDelegate(typeof(Func<long>),method);
    }
    private static void Check(bool value, string context) { if (!value) throw new Exception(context); }
    private static GameplayTag Tag(string name) => GameplayTagManager.RequestTag(name);
    private static string Leaf(int n) => "Flat.G" + n % 32 + ".L" + n;
    private static GameplayTagContainer Set(IEnumerable<string> names) { var c = new GameplayTagContainer(); foreach (var n in names) c.AddTag(Tag(n)); return c; }
    private static HashSet<string> Names(IGameplayTagContainer c) => new HashSet<string>(c.GetExplicitTags().SelectNames(), StringComparer.Ordinal);
    private static HashSet<string> Closure(IEnumerable<string> names)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (string n in names) { result.Add(n); for (int i = 0; i < n.Length; i++) if (n[i] == '.') result.Add(n.Substring(0, i)); }
        return result;
    }
    private static void Validate(GameplayTagContainer c, HashSet<string> names)
    {
        var closure = Closure(names);
        Check(c.ExplicitTagCount == names.Count && c.TagCount == closure.Count, "cardinality");
        var actual = new HashSet<string>(); foreach (var t in c.GetExplicitTags()) Check(actual.Add(t.Name), "duplicate explicit");
        Check(actual.SetEquals(names), "explicit names"); actual.Clear();
        foreach (var t in c.GetTags()) Check(actual.Add(t.Name), "duplicate closure");
        Check(actual.SetEquals(closure), "closure names");
        foreach (var n in closure) Check(c.HasTag(Tag(n)) && c.HasTagExact(Tag(n)) == names.Contains(n), "membership");
    }
    private static void Drain(GameplayTagContainer c, HashSet<string> expected)
    {
        Validate(c, expected); foreach (var n in expected) { c.AddTag(Tag(n)); c.RemoveTag(Tag(n)); }
        Check(c.TagCount == 0 && c.ExplicitTagCount == 0, "normalized drain");
    }
    private static void Register(int padding)
    {
        Chain = new string[3000]; Chain[0] = "Deep";
        for (int i = 1; i < Chain.Length; i++) Chain[i] = Chain[i - 1] + ".N";
        DeepSeed="Seed"+string.Concat(Enumerable.Repeat(".N",3000));
        var registrations = new List<GameplayTagRegistration> { new GameplayTagRegistration(Chain[2999]),new GameplayTagRegistration(DeepSeed) };
        for (int i = 0; i < 2048; i++) registrations.Add(new GameplayTagRegistration(Leaf(i)));
        for (int i = 0; i < padding; i++) registrations.Add(new GameplayTagRegistration("Unused.N" + i));
        GameplayTagManager.Initialize(registrations.ToArray());
    }
    private static void Inputs(int n, int overlap, out GameplayTagContainer a, out GameplayTagContainer b)
    {
        // Coprime stride, deliberately unsorted; same exact names on every revision.
        var left = Enumerable.Range(0, n).Select(i => Leaf((i * 719) % n)).ToArray();
        int shared = n * overlap / 100;
        var right = Enumerable.Range(0, n).Select(i => i < shared ? left[i] : Leaf(1024 + i)).Reverse().ToArray();
        a = Set(left); b = Set(right);
    }
    private static void ShallowTests()
    {
        foreach (int n in new[] { 1, 4, 8, 9, 16, 128, 1024 }) foreach (int overlap in new[] { 0, 50, 100 })
        {
            Inputs(n, overlap, out var a, out var b); var an = Names(a); var bn = Names(b);
            var expected = new HashSet<string>(an); expected.IntersectWith(bn);
            var union = new HashSet<string>(an); union.UnionWith(bn);
            Drain(GameplayTagContainer.Union(a,b), union); Drain(GameplayTagContainer.Intersection(a,b), expected);
            var output = Set(new[] { Chain[2999] }); var workspace = new GameplayTagIntersectionWorkspace();
            GameplayTagContainer.Intersection(output,a,b,workspace); Drain(output, expected);
            GameplayTagContainer.Intersection(output,b,a,workspace); Drain(output, expected);
            foreach (bool left in new[] { true, false }) foreach (bool scratch in new[] { true, false })
            {
                var alias = left ? a.Clone() : b.Clone();
                if (scratch) GameplayTagContainer.Intersection(alias, left ? alias : a, left ? b : alias, workspace);
                else GameplayTagContainer.Intersection(alias, left ? alias : a, left ? b : alias);
                Drain(alias, expected);
            }
            var count = new GameplayTagCountContainer(); foreach (var name in an) count.AddTag(Tag(name), 3);
            GameplayTagContainer.Copy(output,count); Drain(output,an);
            Drain(GameplayTagContainer.Intersection(count,b),expected);
            foreach (var name in an) Check(count.GetExplicitTagCount(Tag(name)) == 3, "counted input changed");
            Validate(a,an); Validate(b,bn);
        }
    }
    private static void DeepTests()
    {
        foreach (int depth in new[] { 128, 1024, 3000 }) foreach (int order in new[] { 0, 1, 2 })
        {
            int[] indices = Enumerable.Range(0,depth).ToArray();
            if (order == 1) Array.Reverse(indices);
            if (order == 2) { var random = new Random(41041); for (int i = indices.Length - 1; i > 0; i--) { int j = random.Next(i+1); int tmp=indices[i]; indices[i]=indices[j]; indices[j]=tmp; } }
            var a = Set(indices.Select(i=>Chain[i])); var all = new HashSet<string>(Chain.Take(depth));
            var counted = new GameplayTagCountContainer(); foreach (int i in indices) counted.AddTag(Tag(Chain[i]),3);
            // Chain oracle is position/depth and suffix count, never registry IDs or runtime ancestry.
            Check(a.TagCount == depth && a.ExplicitTagCount == depth, "all-explicit chain source");
            for (int operation = 0; operation < 6; operation++) foreach (bool reverse in new[] { false, true })
            {
                GameplayTagContainer result;
                if (operation == 0) { result = new GameplayTagContainer(); GameplayTagContainer.Copy(result,a); }
                else if (operation == 1) result = GameplayTagContainer.Union(a,Set(new[] {Chain[depth-1]}));
                else if (operation == 2) result = GameplayTagContainer.Intersection(a,a.Clone());
                else if (operation == 3) { result=a.Clone(); GameplayTagContainer.Intersection(result,result,a,new GameplayTagIntersectionWorkspace()); }
                else if (operation == 4) { result=new GameplayTagContainer(); GameplayTagContainer.Copy(result,counted); }
                else result=GameplayTagContainer.Intersection(counted,a);
                Check(Names(result).SetEquals(all), "all-explicit result names");
                for (int k = 0; k < depth; k++)
                {
                    int removed = reverse ? depth-1-k : k; result.RemoveTag(Tag(Chain[removed]));
                    int remaining = depth-k-1; int closure = remaining == 0 ? 0 : reverse ? remaining : depth;
                    Check(result.ExplicitTagCount == remaining && result.TagCount == closure, "all-explicit chain drain");
                    foreach (int probe in new[] {0,depth/2,depth-1})
                    {
                        bool explicitPresent = reverse ? probe < remaining : probe > k;
                        Check(result.HasTagExact(Tag(Chain[probe])) == explicitPresent, "chain exact after removal");
                        Check(result.HasTag(Tag(Chain[probe])) == (probe < closure), "chain closure after removal");
                    }
                }
            }
            Check(a.TagCount == depth && a.ExplicitTagCount == depth && Names(a).SetEquals(all), "deep input changed");
            for (int i=0;i<depth;i++) Check(counted.GetExplicitTagCount(Tag(Chain[i]))==3 && counted.GetTagCount(Tag(Chain[i]))==3*(depth-i), "deep counted source suffix sums");
        }
    }
    private sealed class Row { public string Id {get;set;} public double Ns {get;set;} public double Bytes {get;set;} public int Iterations {get;set;} public long RetainedBytes {get;set;} public int Cohort {get;set;} public double WallMilliseconds {get;set;} public double CpuMilliseconds {get;set;} public int Gen0 {get;set;} public int Gen1 {get;set;} public int Gen2 {get;set;} }
    private static Row Measure(string id, Action action, int iterations)
    {
        if(TestOnly) { action(); return new Row {Id=id}; }
        if(FixedIterations != null) iterations=FixedIterations[id];
        for(int i=0;i<64;i++) action();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long cpu=Process.TotalProcessorTime.Ticks; int g0=GC.CollectionCount(0),g1=GC.CollectionCount(1),g2=GC.CollectionCount(2);
        long bytes=AllocatedBytes(), start=Stopwatch.GetTimestamp();
        for(int i=0;i<iterations;i++) action();
        long ticks=Stopwatch.GetTimestamp()-start; bytes=AllocatedBytes()-bytes;
        long cpuElapsed=Process.TotalProcessorTime.Ticks-cpu;
        return new Row {Id=id,Ns=ticks*(1e9/Stopwatch.Frequency)/iterations,Bytes=(double)bytes/iterations,Iterations=iterations,WallMilliseconds=ticks*1000.0/Stopwatch.Frequency,CpuMilliseconds=cpuElapsed/10000.0,Gen0=GC.CollectionCount(0)-g0,Gen1=GC.CollectionCount(1)-g1,Gen2=GC.CollectionCount(2)-g2};
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static Row Retained(string id, Func<object> factory)
    {
        const int count=64; var cohort=new object[count]; Sink=null;
        long before=GC.GetTotalMemory(true); for(int i=0;i<count;i++) cohort[i]=factory();
        long after=GC.GetTotalMemory(true); GC.KeepAlive(cohort);
        return new Row {Id=id,RetainedBytes=after-before,Cohort=count};
    }
    private static void Observe(GameplayTagContainer result) { Checksum=unchecked(Checksum+result.TagCount+result.ExplicitTagCount); Sink=result; }
    private static void Benchmark()
    {
        var rows=new List<Row>();
        foreach(int n in new[]{4,16,128,1024}) foreach(int overlap in new[]{0,50,100})
        {
            Inputs(n,overlap,out var a,out var b); var output=new GameplayTagContainer(); var workspace=new GameplayTagIntersectionWorkspace();
            var leftNames=Names(a); var rightNames=Names(b);
            var expected=new HashSet<string>(leftNames); expected.IntersectWith(rightNames); GameplayTagContainer.Intersection(output,a,b,workspace); Validate(output,expected);
            Drain(GameplayTagContainer.Intersection(a,b),expected);
            var unionNames=new HashSet<string>(leftNames);unionNames.UnionWith(rightNames);Drain(GameplayTagContainer.Union(a,b),unionNames);
            foreach(bool aliasLeft in new[]{true,false}) {var alias=aliasLeft?a.Clone():b.Clone();GameplayTagContainer.Intersection(alias,aliasLeft?alias:a,aliasLeft?b:alias);Drain(alias,expected);}
            string id="n"+n+"/overlap"+overlap; int iterations=n>=1024?128:512;
            rows.Add(Measure(id+"/intersection-reused",()=>{GameplayTagContainer.Intersection(output,a,b);Observe(output);},iterations));
            rows.Add(Measure(id+"/workspace-reused",()=>{GameplayTagContainer.Intersection(output,a,b,workspace);Observe(output);},iterations));
            rows.Add(Measure(id+"/intersection-new",()=>Observe(GameplayTagContainer.Intersection(a,b)),iterations));
            rows.Add(Measure(id+"/workspace-cold-growth",()=>{var w=new GameplayTagIntersectionWorkspace();var r=new GameplayTagContainer();GameplayTagContainer.Intersection(r,a,b,w);Observe(r);GC.KeepAlive(w);},iterations));
            rows.Add(Measure(id+"/union-new",()=>Observe(GameplayTagContainer.Union(a,b)),iterations));
            foreach(bool left in new[]{true,false})
            {
                var alias=new GameplayTagContainer();
                rows.Add(Measure(id+"/alias-"+(left?"left":"right")+"-restore-pair",()=>{GameplayTagContainer.Copy(alias,left?a:b);GameplayTagContainer.Intersection(alias,left?alias:a,left?b:alias);Observe(alias);},iterations));
            }
            rows.Add(Retained(id+"/retained-result",()=>GameplayTagContainer.Intersection(a,b)));
            rows.Add(Retained(id+"/retained-result-workspace",()=>{var w=new GameplayTagIntersectionWorkspace();var r=new GameplayTagContainer();GameplayTagContainer.Intersection(r,a,b,w);return new object[]{r,w};}));
            Validate(output,expected); Validate(a,leftNames); Validate(b,rightNames);
        }
        GuardBenchmarks(rows);
        Console.WriteLine("{\"Schema\":1,\"Runtime\":"+Quote(Environment.Version.ToString())+",\"Framework\":"+Quote(Type.GetType("Mono.Runtime") != null ? "Standalone Mono" : ".NET")+",\"RegistryCount\":"+GameplayTagManager.TagCount+",\"Checksum\":"+Checksum+",\"Rows\":["+string.Join(",",rows.Select(RowJson))+"]}");
    }
    private static string Quote(string value) => "\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    private static string RowJson(Row row) => "{"+string.Join(",",typeof(Row).GetProperties().Select(p=>Quote(p.Name)+":"+(p.PropertyType==typeof(string)?Quote((string)p.GetValue(row)):Convert.ToString(p.GetValue(row),CultureInfo.InvariantCulture))))+"}";
    private static void GuardBenchmarks(List<Row> rows)
    {
        foreach(int n in new[]{16,1024})
        {
            Inputs(n,50,out var a,out var b); var empty=new GameplayTagContainer();var names=Names(a);var closure=Closure(names);
            foreach(string relation in new[]{"self","nonself","empty"})
            {
                var other=relation=="self"?a:relation=="empty"?empty:b;var required=Names(other);
                foreach(bool exact in new[]{true,false}) foreach(bool all in new[]{true,false})
                {
                    var available=exact?names:closure; bool expected=all?required.All(available.Contains):required.Any(available.Contains);
                    Func<bool> query=()=>all?(exact?a.HasAllExact(other):a.HasAll(other)):(exact?a.HasAnyExact(other):a.HasAny(other));
                    Check(query()==expected,"predicate independent oracle");
                    rows.Add(Measure("guard/n"+n+"/"+(all?"all":"any")+"-"+(exact?"exact":"hierarchical")+"-"+relation,()=>{Checksum=unchecked(Checksum+(query()?1:0));},10000));
                }
            }
            var work=new GameplayTagContainer();var union=new HashSet<string>(names);union.UnionWith(Names(b));
            GameplayTagContainer.Copy(work,a);work.AddTags(b);Drain(work,union);
            rows.Add(Measure("guard/n"+n+"/addtags-restore-pair",()=>{GameplayTagContainer.Copy(work,a);work.AddTags(b);Observe(work);},512));
            rows.Add(Measure("guard/n"+n+"/addtags-self",()=>{a.AddTags(a);Observe(a);},10000));
            foreach(bool sparse in new[]{false,true})
            {
                var seed=a.Clone();if(sparse) {foreach(var name in names.Skip(1))seed.RemoveTag(Tag(name));}
                work.EnsureCapacity(8192);
                rows.Add(Measure("guard/n"+n+"/clear-"+(sparse?"sparse":"dense")+"-restore-pair",()=>{GameplayTagContainer.Copy(work,seed);work.Clear();Observe(work);},512));
                Check(work.TagCount==0 && work.ExplicitTagCount==0,"clear guard");
            }
            Validate(a,names);
        }
        foreach(int depth in new[]{3,128,3000})
        {
            GameplayTag tag;GameplayTagContainer source;
            if(depth==3) { Inputs(1024,50,out source,out var ignored);tag=Tag(Leaf(400)); }
            else {tag=Tag(Chain[depth-1]);source=Set(new[]{Chain[depth-1]});}
            int explicitBefore=source.ExplicitTagCount,totalBefore=source.TagCount;
            rows.Add(Measure("guard/depth"+depth+"/remove-add-pair",()=>{source.RemoveTag(tag);source.AddTag(tag);Observe(source);},1024));
            Check(source.ExplicitTagCount==explicitBefore && source.TagCount==totalBefore && source.HasTagExact(tag),"remove/add restores source");
            rows.Add(Measure("guard/depth"+depth+"/duplicate-add",()=>{source.AddTag(tag);Observe(source);},10000));
            Check(source.ExplicitTagCount==explicitBefore && source.TagCount==totalBefore,"duplicate add preserves counts");
            rows.Add(Measure("guard/depth"+depth+"/fresh-single-add",()=>{var fresh=new GameplayTagContainer();fresh.AddTag(tag);Observe(fresh);},256));
            var proof=new GameplayTagContainer();proof.AddTag(tag);Check(proof.TagCount==depth && proof.ExplicitTagCount==1,"fresh single tag closure");proof.RemoveTag(tag);Check(proof.TagCount==0,"fresh single drains");
        }
        foreach(int depth in new[]{128,1024,3000}) foreach(bool descending in new[]{false,true})
        {
            var indices=Enumerable.Range(0,depth);if(descending)indices=indices.Reverse();
            var source=Set(indices.Select(i=>Chain[i]));var seed=Set(new[]{DeepSeed});
            var result=GameplayTagContainer.Union(seed,source);
            Check(result.TagCount==3001+depth && result.ExplicitTagCount==depth+1,"disjoint larger seed deep union");
            foreach(int i in Enumerable.Range(0,depth))result.RemoveTag(Tag(Chain[i]));result.RemoveTag(Tag(DeepSeed));Check(result.TagCount==0,"deep disjoint full drain");
            rows.Add(Measure("guard/depth"+depth+"/union-larger-disjoint-seed-"+(descending?"descending":"ascending"),()=>Observe(GameplayTagContainer.Union(seed,source)),16));
            Check(source.TagCount==depth && source.ExplicitTagCount==depth && seed.TagCount==3001,"deep union inputs preserved");
        }
    }
    private static IEnumerable<string> SelectNames(this GameplayTagEnumerator enumerator) { foreach(var t in enumerator) yield return t.Name; }
    private static void Main(string[] args)
    {
        Register(args.Length>1?int.Parse(args[1]):0);
        if(args.Length>2) {FixedIterations=new Dictionary<string,int>();foreach(string line in System.IO.File.ReadAllLines(args[2])) {var parts=line.Split('\t');FixedIterations.Add(parts[0],int.Parse(parts[1],CultureInfo.InvariantCulture));}}
        if(args.Length>0 && args[0]=="bench") Benchmark();
        else { TestOnly=true; ShallowTests(); DeepTests(); GuardBenchmarks(new List<Row>()); Console.WriteLine("PASS Evolution: fixed shallow matrix, fresh/reused/both aliases, counted normalization, all-explicit deep chain insertion and deletion orders."); }
    }
}
