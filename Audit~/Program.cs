using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using GameplayTags;
using GameplayTags.Editor;
using UnityEngine;

internal static class Program
{
    static long assertions;
    static int failed;
    static object sink;
    static int checksum;
    static readonly List<object> results = new List<object>();
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };
    const int Seed = 9341416;
    static readonly string[] Names = { "A", "A.B", "A.B.C", "A!x", "A-x", "A0", "Z", "Z.Q", "Ability.Attack", "Ability.AttackSpeed", "State.Fire", "状态.灼烧" };

    static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "bench") { Bench(args[1]); return 0; }
            if (args.Length > 0 && args[0] == "generate") { GenerateCases(args[1]); return 0; }
            Tests();
            string output = args.Length > 1 ? args[1] : "tests.json";
            File.WriteAllText(output, JsonSerializer.Serialize(new { runtime=RuntimeInformation.FrameworkDescription, seed=Seed, assertions, failed, results }, Json));
            Console.WriteLine($"AUDIT tests={results.Count} assertions={assertions} failed={failed}");
            return failed == 0 ? 0 : 1;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    static void Check(bool condition, string message = "assertion") { assertions++; if (!condition) throw new Exception(message); }
    static void Throws<T>(Action action) where T:Exception
    { assertions++; try { action(); } catch(T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static void Test(string name, Action body)
    {
        try { body(); results.Add(new { name, passed=true }); Console.WriteLine("PASS " + name); }
        catch(Exception e) { failed++; results.Add(new { name, passed=false, error=e.ToString() }); Console.WriteLine("FAIL " + name + " " + e); }
    }
    static GameplayTagSettings Settings(IEnumerable<string> names)
    {
        var s=ScriptableObject.CreateInstance<GameplayTagSettings>();
        s.ReplaceAll(names.Select(n=>new GameplayTagDefinition(n,"","Default",false,true)).ToList(), new List<GameplayTagRedirect>(), new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});
        return s;
    }
    static void Setup() { GameplayTagManager.ResetForTests(); GameplayTagManager.Initialize(Settings(Names)); }
    static GameplayTag Tag(string name)=>GameplayTagManager.RequestTag(name);
    static GameplayTagContainer Container(IEnumerable<string> names)
    { var c=new GameplayTagContainer(); foreach(string n in names) c.AddTag(n); return c; }
    static string[] Values(GameplayTagContainer c) { var a=new string[c.Count]; for(int i=0;i<a.Length;i++) a[i]=c[i].Name; return a; }
    static bool Matches(string child,string parent)=>parent.Length>0 && (child==parent || child.StartsWith(parent+".",StringComparison.Ordinal));
    static void Same(GameplayTagContainer actual,IEnumerable<string> expected)
    { Check(Values(actual).SequenceEqual(expected.Distinct(StringComparer.Ordinal).OrderBy(n=>n,StringComparer.Ordinal)),"set mismatch"); }
    static void SetField(object obj,string name,object value)=>obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(obj,value);
    static bool Eval(GameplayTagQueryExpression e,HashSet<string> owned)
    {
        switch(e.Type)
        {
            case GameplayTagQueryExpressionType.AnyTagsMatch: for(int i=0;i<e.Tags.Count;i++) if(owned.Any(n=>Matches(n,e.Tags[i].Name))) return true; return false;
            case GameplayTagQueryExpressionType.AllTagsMatch: for(int i=0;i<e.Tags.Count;i++) if(!owned.Any(n=>Matches(n,e.Tags[i].Name))) return false; return true;
            case GameplayTagQueryExpressionType.NoTagsMatch: for(int i=0;i<e.Tags.Count;i++) if(owned.Any(n=>Matches(n,e.Tags[i].Name))) return false; return true;
            case GameplayTagQueryExpressionType.AnyExpressionsMatch: return e.Expressions.Any(x=>Eval(x,owned));
            case GameplayTagQueryExpressionType.AllExpressionsMatch: return e.Expressions.All(x=>Eval(x,owned));
            case GameplayTagQueryExpressionType.NoExpressionsMatch: return !e.Expressions.Any(x=>Eval(x,owned));
            default: throw new InvalidOperationException();
        }
    }
    static GameplayTagQueryExpression RandomExpr(Random r,int depth)
    {
        var e=new GameplayTagQueryExpression((GameplayTagQueryExpressionType)(depth==0?r.Next(1,4):r.Next(1,7)));
        int count=r.Next(0,5);
        for(int i=0;i<count;i++) { if(e.UsesTags) e.AddTag(Tag(Names[r.Next(Names.Length)])); else e.AddExpression(RandomExpr(r,depth-1)); }
        return e;
    }
    static Func<GameplayTagContainer,bool> Matcher(GameplayTagQuery q)
    {
#if OPTIMIZED
        return q.Freeze().Matches;
#else
        return q.Matches;
#endif
    }
    static void Tests()
    {
        Test("tag-boundaries-and-default",()=>{
            Setup(); Check(Tag("A.B").MatchesTag(Tag("A"))); Check(!Tag("A").MatchesTag(Tag("A.B")));
            Check(!Tag("Ability.AttackSpeed").MatchesTag(Tag("Ability.Attack"))); Check(!GameplayTag.None.MatchesTag(GameplayTag.None));
            Check(Tag("A.B.C").MatchesTagDepth(Tag("A.B"))==2); Check(Tag("A.B").GetDirectParent()==Tag("A"));
#if OPTIMIZED
            object empty=GameplayTag.None; SetField(empty,"m_Name",string.Empty); var e=(GameplayTag)empty;
            Check(e==GameplayTag.None); Check(e.GetHashCode()==GameplayTag.None.GetHashCode());
            Check(!GameplayTagName.TryNormalize("A\0B",out _,out _));
#endif
        });
        Test("registry-redirect-and-transaction",()=>{
            Setup(); var s=Settings(Names); s.RedirectsInternal.Add(new GameplayTagRedirect("Old.A","A.B"));
            GameplayTagManager.Initialize(s,true); Check(Tag(" Old.A ")==Tag("A.B"));
            s.TagsInternal.Add(new GameplayTagDefinition("A","","Default",false,true));
            Throws<GameplayTagRegistryException>(()=>GameplayTagManager.Initialize(s,true)); Check(Tag("Old.A")==Tag("A.B"));
        });
        Test("exhaustive-six-element-sets",()=>{
            Setup(); var domain=Names.Take(6).ToArray();
            for(int a=0;a<64;a++) for(int b=0;b<64;b++)
            {
                var x=domain.Where((_,i)=>(a&(1<<i))!=0).ToArray(); var y=domain.Where((_,i)=>(b&(1<<i))!=0).ToArray();
                var left=Container(x); var right=Container(y);
                Same(GameplayTagContainer.Union(left,right),x.Concat(y)); Same(GameplayTagContainer.IntersectionExact(left,right),x.Intersect(y));
                Same(left.FilterExact(right),x.Intersect(y)); Same(left.Filter(right),x.Where(n=>y.Any(p=>Matches(n,p))));
                Check(left.HasAny(right)==y.Any(p=>x.Any(n=>Matches(n,p)))); Check(left.HasAll(right)==y.All(p=>x.Any(n=>Matches(n,p))));
                Check(left.HasAnyExact(right)==y.Any(x.Contains)); Check(left.HasAllExact(right)==y.All(x.Contains));
                var copy=new GameplayTagContainer(left); copy.AppendTags(right); Same(copy,x.Concat(y));
                copy.CopyFrom(left); copy.RemoveTags(right); Same(copy,x.Except(y));
#if OPTIMIZED
                var output=new GameplayTagContainer(12); GameplayTagContainer.UnionInto(left,right,output); Same(output,x.Concat(y));
                GameplayTagContainer.IntersectionExactInto(left,right,output); Same(output,x.Intersect(y));
                left.FilterInto(right,output); Same(output,x.Where(n=>y.Any(p=>Matches(n,p))));
                var alias=new GameplayTagContainer(left); GameplayTagContainer.UnionInto(alias,right,alias); Same(alias,x.Concat(y));
                alias.CopyFrom(right); GameplayTagContainer.UnionInto(left,alias,alias); Same(alias,x.Concat(y));
                alias.CopyFrom(left); GameplayTagContainer.IntersectionExactInto(alias,right,alias); Same(alias,x.Intersect(y));
                alias.CopyFrom(right); GameplayTagContainer.IntersectionExactInto(left,alias,alias); Same(alias,x.Intersect(y));
                alias.CopyFrom(left); alias.FilterInto(right,alias); Same(alias,x.Where(n=>y.Any(p=>Matches(n,p))));
#endif
            }
        });
        Test("continuous-random-mutations",()=>{
            Setup(); var r=new Random(Seed); var c=new GameplayTagContainer(Names.Length); var reference=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<20000;i++)
            {
                string n=Names[r.Next(Names.Length)]; int operation=r.Next(5);
                if(operation==0) Check(c.AddTag(Tag(n))==reference.Add(n));
                else if(operation==1) Check(c.RemoveTag(Tag(n))==reference.Remove(n));
                else if(operation==2) { var items=Names.Where(_=>r.Next(3)==0).ToArray(); c.AppendTags(Container(items)); reference.UnionWith(items); }
                else if(operation==3) { var items=Names.Where(_=>r.Next(3)==0).ToArray(); c.RemoveTags(Container(items)); reference.ExceptWith(items); }
                else { Check(c.HasTag(Tag(n))==reference.Any(x=>Matches(x,n))); }
                Same(c,reference);
            }
        });
        Test("self-empty-and-null-contracts",()=>{
            Setup(); var c=Container(Names); c.CopyFrom(c); Same(c,Names); c.AppendTags(c); Same(c,Names); Check(c.RemoveTags(c)); Check(c.IsEmpty); Check(!c.RemoveTags(c));
            Check(!c.HasAny(null)); Check(c.HasAll(null)); Check(!Matcher(new GameplayTagQuery())(c));
            Check(Matcher(new GameplayTagQuery(GameplayTagQueryExpression.AllTagsMatch()))(c));
            Check(Matcher(new GameplayTagQuery(GameplayTagQueryExpression.NoTagsMatch()))(c));
        });
        Test("random-query-reference-equivalence",()=>{
            Setup(); var r=new Random(Seed);
            for(int i=0;i<3000;i++) { var e=RandomExpr(r,3); var match=Matcher(new GameplayTagQuery(e));
                for(int j=0;j<8;j++) { var owned=new HashSet<string>(Names.Where(_=>r.Next(2)==0),StringComparer.Ordinal); Check(match(Container(owned))==Eval(e,owned),"query mismatch"); }
            }
        });
        Test("serialization-normalization",()=>{
            Setup(); var c=new GameplayTagContainer(); SetField(c,"m_GameplayTags",new List<GameplayTag>{Tag("Z"),Tag("A"),Tag("Z"),default});
            ((ISerializationCallbackReceiver)c).OnAfterDeserialize(); Same(c,new[]{"A","Z"});
        });
        Test("known-invalid-input-regressions",()=>{
            Setup(); var invalid=new GameplayTagQuery(GameplayTagQueryExpression.NoExpressionsMatch().AddExpression(new GameplayTagQueryExpression(GameplayTagQueryExpressionType.Undefined)));
            var s=Settings(new[]{"State.Fire","state.Ice"}); var errors=new List<string>();
#if OPTIMIZED
            Throws<InvalidOperationException>(()=>invalid.Freeze()); Check(!s.Validate(errors));
            var padded=Settings(new[]{" A ","A.B"}); padded.TagsInternal[0]=new GameplayTagDefinition(" A ","","Default",true,false); Check(!padded.Validate(errors));
#else
            Check(invalid.Matches(new GameplayTagContainer()),"baseline No(Undefined) defect no longer reproduces");
            Check(s.Validate(errors),"baseline implicit-parent case defect no longer reproduces");
            Console.WriteLine("BASELINE DEFECTS REPRODUCED: No(Undefined) passes; implicit parent casing accepted.");
#endif
        });
#if OPTIMIZED
        Test("frozen-isolation-cycles-and-depth",()=>{
            Setup(); var e=GameplayTagQueryExpression.AllTagsMatch().AddTag(Tag("A")); var f=new GameplayTagQuery(e).Freeze(); e.AddTag(Tag("Z")); Check(f.Matches(Container(new[]{"A.B"})));
            var a=GameplayTagQueryExpression.AllExpressionsMatch(); var b=GameplayTagQueryExpression.AnyExpressionsMatch(); a.AddExpression(b); b.AddExpression(a);
            Throws<InvalidOperationException>(()=>new GameplayTagQuery(a).Freeze());
            var deep=GameplayTagQueryExpression.AllTagsMatch(); for(int i=0;i<64;i++) deep=GameplayTagQueryExpression.AllExpressionsMatch().AddExpression(deep);
            Throws<InvalidOperationException>(()=>new GameplayTagQuery(deep).Freeze());
            var shorted=GameplayTagQueryExpression.AnyExpressionsMatch().AddExpression(GameplayTagQueryExpression.AllTagsMatch()).AddExpression(new GameplayTagQueryExpression(GameplayTagQueryExpressionType.Undefined));
            Throws<InvalidOperationException>(()=>new GameplayTagQuery(shorted).Freeze());
        });
        Test("query-simplification-and-alias-rejection",()=>{
            Setup(); foreach(var type in new[]{GameplayTagQueryExpressionType.AnyTagsMatch,GameplayTagQueryExpressionType.AllTagsMatch,GameplayTagQueryExpressionType.NoTagsMatch})
            {
                var e=new GameplayTagQueryExpression(type).AddTag(Tag("A")).AddTag(Tag("A.B")).AddTag(Tag("A.B.C")).AddTag(Tag("A!x")); var f=new GameplayTagQuery(e).Freeze();
                for(int mask=0;mask<64;mask++) { var owned=new HashSet<string>(Names.Take(6).Where((_,i)=>(mask&(1<<i))!=0)); Check(f.Matches(Container(owned))==Eval(e,owned)); }
            }
            var x=Container(new[]{"A.B"}); var y=Container(new[]{"A"}); Throws<ArgumentException>(()=>x.FilterInto(y,y)); Same(y,new[]{"A"});
        });
        Test("resolve-snapshot-transaction",()=>{
            Setup(); var c=new GameplayTagContainer(); SetField(c,"m_GameplayTags",new List<GameplayTag>{new GameplayTag("Old"),new GameplayTag("Unknown")}); ((ISerializationCallbackReceiver)c).OnAfterDeserialize();
            var s=Settings(Names); s.RedirectsInternal.Add(new GameplayTagRedirect("Old","A")); GameplayTagManager.Initialize(s,true);
            Throws<KeyNotFoundException>(()=>c.ResolveRegisteredTags()); Same(c,new[]{"Old","Unknown"});
            c.RemoveTag(new GameplayTag("Unknown")); c.ResolveRegisteredTags(); Same(c,new[]{"A"});
        });
        Test("zero-allocation-hot-paths",()=>{
            Setup(); var a=Container(Names); var b=Container(Names.Take(6)); var dst=new GameplayTagContainer(64); var work=new GameplayTagContainer(64);
            var tag=Tag("A.B"); var frozen=new GameplayTagQuery(GameplayTagQueryExpression.AllTagsMatch().AddTags(b)).Freeze();
            var actions=new Dictionary<string,Action>{
                ["queries"]=()=>{ checksum^=a.HasTag(tag)?1:0; checksum^=a.HasTagExact(tag)?1:0; checksum^=a.HasAny(b)?1:0; checksum^=a.HasAll(b)?1:0; },
                ["frozen"]=()=>{ checksum^=frozen.Matches(a)?1:0; },
                ["mutations"]=()=>{work.CopyFrom(a); work.RemoveTags(b); work.AppendTags(b); work.RemoveTag(tag); work.AddTag(tag);},
                ["into"]=()=>{GameplayTagContainer.UnionInto(a,b,dst); GameplayTagContainer.IntersectionExactInto(a,b,dst); a.FilterInto(b,dst); a.FilterExactInto(b,dst);},
                ["registry"]=()=>{checksum^=GameplayTagManager.RequestTag("A.B").GetHashCode();}
            };
            foreach(var p in actions) { for(int i=0;i<1000;i++) p.Value(); long start=GC.GetAllocatedBytesForCurrentThread(); for(int i=0;i<10000;i++) p.Value(); long bytes=GC.GetAllocatedBytesForCurrentThread()-start; Console.WriteLine("ALLOC "+p.Key+" "+bytes); Check(bytes==0,"allocation: "+p.Key); }
            long before=GC.GetAllocatedBytesForCurrentThread(); sink=new byte[1024]; Check(GC.GetAllocatedBytesForCurrentThread()>before,"allocation counter positive control");
        });
#endif
    }

    static object Measure(string name,int size,Action action,int iterations=30000)
    {
        for(int i=0;i<2000;i++) action();
        var ns=new double[7]; var allocations=new double[7];
        for(int sample=0;sample<ns.Length;sample++)
        {
            long bytes=GC.GetAllocatedBytesForCurrentThread(); long start=Stopwatch.GetTimestamp();
            for(int i=0;i<iterations;i++) action();
            long stop=Stopwatch.GetTimestamp(); allocations[sample]=(GC.GetAllocatedBytesForCurrentThread()-bytes)/(double)iterations;
            ns[sample]=(stop-start)*1e9/Stopwatch.Frequency/iterations;
        }
        return new { name,size,iterations,ns,allocated_bytes=allocations };
    }
    static object Mutation(string name,int size,GameplayTagContainer initial,Action<GameplayTagContainer> action)
    {
        const int batch=512; var targets=new GameplayTagContainer[batch];
        for(int i=0;i<batch;i++) { targets[i]=new GameplayTagContainer(size*2+16); targets[i].CopyFrom(initial); action(targets[i]); }
        var ns=new double[7]; var allocations=new double[7];
        for(int s=0;s<7;s++)
        {
            for(int i=0;i<batch;i++) targets[i].CopyFrom(initial);
            long bytes=GC.GetAllocatedBytesForCurrentThread(); long start=Stopwatch.GetTimestamp();
            for(int i=0;i<batch;i++) action(targets[i]);
            long stop=Stopwatch.GetTimestamp(); allocations[s]=(GC.GetAllocatedBytesForCurrentThread()-bytes)/(double)batch;
            ns[s]=(stop-start)*1e9/Stopwatch.Frequency/batch;
        }
        checksum^=targets[batch-1].Count;
        return new { name,size,iterations=batch,ns,allocated_bytes=allocations };
    }
    static string BenchName(int i,int depth)=>string.Concat(Enumerable.Repeat("Root.",depth-1))+"T"+i.ToString("D5");
    static void Bench(string path)
    {
        var rows=new List<object>();
        foreach(int depth in new[]{1,4,8})
        {
            var names=Enumerable.Range(0,10000).Select(i=>BenchName(i,depth)).ToArray();
            GameplayTagManager.Initialize(Settings(names),true);
            foreach(int size in new[]{0,8,32,128,512})
            {
                var c=Container(names.Take(size)); var hit=Tag(names[Math.Max(0,size/2)]); var miss=Tag(names[9999]); var parent=depth>1?hit.GetDirectParent():hit;
                rows.Add(Measure("exact."+(size==0?"empty":"hit")+".d"+depth,size,()=>{checksum^=c.HasTagExact(hit)?1:0;}));
                rows.Add(Measure("exact.miss.d"+depth,size,()=>{checksum^=c.HasTagExact(miss)?1:0;}));
                rows.Add(Measure("hierarchy.parent.d"+depth,size,()=>{checksum^=c.HasTag(parent)?1:0;}));
                rows.Add(Measure("hierarchy.miss.d"+depth,size,()=>{checksum^=c.HasTag(miss)?1:0;}));
            }
            rows.Add(Measure("registry.request.d"+depth,10000,()=>{checksum^=Tag(names[5555]).GetHashCode();}));
        }
        Setup(); var owned=Container(Names);
        foreach(int depth in new[]{1,4,8})
        {
            var e=GameplayTagQueryExpression.AllTagsMatch().AddTag(Tag("A")).AddTag(Tag("A.B")).AddTag(Tag("A.B.C"));
            for(int i=1;i<depth;i++) e=GameplayTagQueryExpression.AllExpressionsMatch().AddExpression(e);
            var q=new GameplayTagQuery(e); var matcher=Matcher(q);
            rows.Add(Measure("query.evaluate.d"+depth,depth,()=>{checksum^=matcher(owned)?1:0;}));
            rows.Add(Measure("query.construct.d"+depth,depth,()=>{sink=Matcher(new GameplayTagQuery(e));},2000));
        }
        var batchNames=Enumerable.Range(0,2048).Select(i=>"Batch.T"+i.ToString("D5")).ToArray(); GameplayTagManager.Initialize(Settings(batchNames),true);
        foreach(int size in new[]{8,32,128,512})
        {
            var a=Container(batchNames.Take(size)); var b=Container(batchNames.Skip(size/2).Take(size)); var few=Container(new[]{batchNames[0],batchNames[size-1]});
            rows.Add(Mutation("append.overlap",size,a,c=>c.AppendTags(b))); rows.Add(Mutation("remove.half",size,a,c=>c.RemoveTags(b))); rows.Add(Mutation("remove.two",size,a,c=>c.RemoveTags(few)));
            rows.Add(Measure("filter.exact",size,()=>{sink=a.FilterExact(b);},2000));
            rows.Add(Measure("intersection.asymmetric",size,()=>{sink=GameplayTagContainer.IntersectionExact(a,few);},2000));
#if OPTIMIZED
            var dst=new GameplayTagContainer(size*2); rows.Add(Measure("union.into",size,()=>GameplayTagContainer.UnionInto(a,b,dst),2000));
            rows.Add(Measure("filter.into",size,()=>a.FilterExactInto(b,dst),2000));
#endif
        }
        var prefixNames=Enumerable.Range(0,512).Select(i=>"A!"+i.ToString("D5")).Concat(new[]{"A.B"}).ToArray(); GameplayTagManager.Initialize(Settings(prefixNames),true);
        var siblings=Container(prefixNames.Take(512)); var root=Tag("A"); rows.Add(Measure("hierarchy.similar-prefix-miss",512,()=>{checksum^=siblings.HasTag(root)?1:0;}));
        File.WriteAllText(path,JsonSerializer.Serialize(new { runtime=RuntimeInformation.FrameworkDescription,os=RuntimeInformation.OSDescription,architecture=RuntimeInformation.ProcessArchitecture.ToString(),stopwatchFrequency=Stopwatch.Frequency,seed=Seed,checksum,rows },Json));
        Console.WriteLine("BENCH rows="+rows.Count+" "+path);
    }
    static void GenerateCases(string output)
    {
        Directory.CreateDirectory(output); Directory.CreateDirectory("Assets");
        foreach(string name in new[]{"GameplayTagManager","__arglist"})
        {
            var s=Settings(new[]{name}); s.SetEditorOptions(true,true,false,"Generated","Tags","Assets/"+name+".gen.cs");
            GameplayTagCodeGenerator.Generate(s);
            File.Copy("Assets/"+name+".gen.cs",Path.Combine(output,name+".cs"),true);
        }
    }
}
