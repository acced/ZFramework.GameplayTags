using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using GameplayTags;
using GameplayTags.Editor;
using UnityEngine;
using UnityEditor;

internal static class Extended
{
    static long assertions;
    static int failed;
    static object sink;
    static int checksum;
    static readonly List<object> checks = new List<object>();
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };
    static void Check(bool value,string message="assertion") { assertions++; if(!value) throw new Exception(message); }
    static void Throws<T>(Action action) where T:Exception { assertions++; try { action(); } catch(T) { return; } throw new Exception("Expected "+typeof(T).Name); }
    static void Test(string name,Action action)
    {
        try { action(); checks.Add(new{name,passed=true}); Console.WriteLine("PASS "+name); }
        catch(Exception e) { failed++; checks.Add(new{name,passed=false,error=e.ToString()}); Console.WriteLine("FAIL "+name+" "+e); }
    }
    static void Field(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
    static GameplayTagSettings Settings(params string[] names)
    {
        var s=ScriptableObject.CreateInstance<GameplayTagSettings>();
        s.ReplaceAll(names.Select(n=>new GameplayTagDefinition(n,"","Default",false,true)).ToList(),new List<GameplayTagRedirect>(),new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});
        return s;
    }
    static void Setup(params string[] names) { GameplayTagManager.ResetForTests(); GameplayTagManager.Initialize(Settings(names)); }
    static GameplayTagContainer Container(params string[] names) { var c=new GameplayTagContainer(names.Length); foreach(string name in names) c.AddTag(name); return c; }
    static void Same(GameplayTagContainer c,IEnumerable<string> expected)
    { var actual=new List<string>(); foreach(var tag in c) actual.Add(tag.Name); Check(actual.SequenceEqual(expected.Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal)),"set mismatch"); }
    static int Main(string[] args)
    {
        try
        {
            RunTests();
            var cold=failed==0?ColdCosts():new List<object>();
            var memory=failed==0?MemoryCosts():new List<object>();
            File.WriteAllText(args[0],JsonSerializer.Serialize(new{assertions,failed,checks,cold,memory,limitation="Managed host only; facade does not emulate Unity assets, Undo, callbacks or native memory. Allocation differs from resident retention."},Json));
            Console.WriteLine($"EXTENDED tests={checks.Count} assertions={assertions} failed={failed}");
            return failed==0?0:1;
        }
        catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    static void RunTests()
    {
        Test("well-formed-utf16-name-boundary",()=>{
            foreach (string name in new[]{"A\uD800","A\uDC00","A\uD800B","A\uDC00\uD800"})
                Check(!GameplayTagName.TryNormalize(name,out _,out _),"unpaired surrogate accepted");
            Check(GameplayTagName.TryNormalize("A.\uD83D\uDE00",out _,out _),"valid surrogate pair rejected");
            Check(!Settings("A\uD800").Validate(new List<string>()));
        });
        Test("freeze-punctuation-hierarchy-reference-equivalence",()=>{
            string[] names={"A","A!x","A!x.B","A-x","A+x","A.B","A.B!x","A.B.C","A.B.D","A.Z","A_Z","A.\u4e2d","A.\u4e2d.C","A.\uD83D\uDE00","Z"};
            Setup(names); var random=new Random(409671);
            for(int sample=0;sample<800;sample++)
            {
                var conditions=names.Where(_=>random.Next(3)==0).ToArray();
                foreach(var type in new[]{GameplayTagQueryExpressionType.AllTagsMatch,GameplayTagQueryExpressionType.AnyTagsMatch,GameplayTagQueryExpressionType.NoTagsMatch})
                {
                    var expression=new GameplayTagQueryExpression(type).AddTags(Container(conditions));
                    string before=expression.ToString(); var frozen=new GameplayTagQuery(expression).Freeze();
                    Check(before==expression.ToString(),"Freeze modified source");
                    for(int choice=0;choice<12;choice++)
                    {
                        var owned=names.Where(_=>random.Next(3)==0).ToArray();
                        Func<string,bool> present=p=>owned.Any(c=>c==p || c.StartsWith(p+".",StringComparison.Ordinal));
                        bool expected=type==GameplayTagQueryExpressionType.AllTagsMatch?conditions.All(present):
                            type==GameplayTagQueryExpressionType.AnyTagsMatch?conditions.Any(present):!conditions.Any(present);
                        Check(frozen.Matches(Container(owned))==expected,"hierarchy reduction changed semantics");
                    }
                }
            }
        });
        Test("large-freeze-and-canonical-alias-dedup",()=>{
            var names=Enumerable.Range(0,2048).Select(i=>"Root.T"+i.ToString("D4")).ToArray(); Setup(names);
            var expression=GameplayTagQueryExpression.AllTagsMatch().AddTags(Container(names));
            var frozen=new GameplayTagQuery(expression).Freeze();
            Check(frozen.Matches(Container(names))); Check(!frozen.Matches(Container(names.Take(2047).ToArray())));
            var s=Settings("A.B"); s.RedirectsInternal.Add(new GameplayTagRedirect("Old","A.B")); GameplayTagManager.Initialize(s,true);
            var aliases=GameplayTagQueryExpression.AllTagsMatch();
            Field(aliases.Tags,"m_GameplayTags",new List<GameplayTag>{new GameplayTag("Old"),new GameplayTag("A"),new GameplayTag("A.B")});
            Check(new GameplayTagQuery(aliases).Freeze().Matches(Container("A.B")));
        });
        Test("resolve-preserves-reserved-capacity-and-empty-noop",()=>{
            Setup("A.B"); var s=Settings("A.B"); s.RedirectsInternal.Add(new GameplayTagRedirect("Old","A.B"));
            GameplayTagManager.Initialize(s,true);
            var c=new GameplayTagContainer(64); c.AddTag("A.B"); c.ResolveRegisteredTags(); Check(c.Capacity>=64);
            c.Clear(); long before=GC.GetAllocatedBytesForCurrentThread(); c.ResolveRegisteredTags();
            Check(GC.GetAllocatedBytesForCurrentThread()==before); Check(c.Capacity>=64);
        });
        Test("append-amortized-growth",()=>{
            var names=Enumerable.Range(0,257).Select(i=>"Grow.T"+i.ToString("D4")).ToArray(); Setup(names);
            foreach(bool reverse in new[]{false,true})
            {
                var output=new GameplayTagContainer(); int resizes=0,last=0; var one=new GameplayTagContainer(1);
                for(int i=0;i<names.Length;i++)
                {
                    one.Clear(); one.AddTag(names[reverse?names.Length-1-i:i]); output.AppendTags(one);
                    if(output.Capacity!=last) {resizes++; last=output.Capacity;}
                }
                Same(output,names); Check(resizes<=12,"one backing allocation per batch");
            }
        });
        Test("canonical-input-restrictions",()=>{
            foreach(string name in new[]{" A ","A..B","A\0B",".A","A."}) { var s=Settings(name); Check(!s.Validate(new List<string>())); }
            var restricted=Settings("A","A.B"); restricted.TagsInternal[0]=new GameplayTagDefinition("A","","Default",true,false); Check(!restricted.Validate(new List<string>()));
            Check(!Settings("A.X","a.Y").Validate(new List<string>())); Check(Settings("A.X","A.Y").Validate(new List<string>()));
        });
        Test("redirect-chain-cycle-missing-and-canonical-reuse",()=>{
            var s=Settings("A.B"); s.RedirectsInternal.Add(new GameplayTagRedirect("Old","Middle")); s.RedirectsInternal.Add(new GameplayTagRedirect("Middle","A.B"));
            GameplayTagManager.Initialize(s,true); var a=GameplayTagManager.RequestTag("Old"); var b=GameplayTagManager.RequestTag(new string("A.B".ToCharArray()));
            Check(a==b); Check(ReferenceEquals(a.Name,b.Name)); Check(!(GameplayTagManager.GetAllTags() is GameplayTag[]));
            s.RedirectsInternal.Add(new GameplayTagRedirect("Loop","Loop")); Throws<GameplayTagRegistryException>(()=>GameplayTagManager.Initialize(s,true)); Check(GameplayTagManager.RequestTag("Old")==a);
            s.RedirectsInternal.RemoveAt(s.RedirectsInternal.Count-1); s.RedirectsInternal.Add(new GameplayTagRedirect("Missing","Absent")); Check(!s.Validate(new List<string>()));
        });
        Test("registry-reset-event-and-editor-cache",()=>{
            var s=Settings("A"); GameplayTagManager.Initialize(s,true); int calls=0; GameplayTagManager.RegistryChanged+=()=>calls++;
            GameplayTagManager.Initialize(s,true); Check(calls==1); GameplayTagManager.ResetForTests(); Check(!GameplayTagManager.IsInitialized);
            GameplayTagManager.Initialize(s); Check(calls==1);
            GameplayTagEditorUtility.InitializeManager(s,true); GameplayTagManager.ResetForTests(); GameplayTagEditorUtility.InitializeManager(s,false); Check(GameplayTagManager.IsInitialized);
            EditorApplication.isPlayingOrWillChangePlaymode=true;
            try { GameplayTagEditorUtility.InitializeManager(Settings("B"),true); Check(GameplayTagManager.IsRegistered("A")); }
            finally { EditorApplication.isPlayingOrWillChangePlaymode=false; }
        });
        Test("null-active-query-payloads-and-late-invalid-branches",()=>{
            Setup("A"); var tags=GameplayTagQueryExpression.AllTagsMatch(); Field(tags,"m_Tags",null); Throws<InvalidOperationException>(()=>new GameplayTagQuery(tags).Freeze());
            var children=GameplayTagQueryExpression.AnyExpressionsMatch(); Field(children,"m_Expressions",null); Throws<InvalidOperationException>(()=>new GameplayTagQuery(children).Freeze());
            var withNull=GameplayTagQueryExpression.AllExpressionsMatch(); Field(withNull,"m_Expressions",new List<GameplayTagQueryExpression>{null}); Throws<InvalidOperationException>(()=>new GameplayTagQuery(withNull).Freeze());
            var unknown=GameplayTagQueryExpression.AllTagsMatch(); Field(unknown.Tags,"m_GameplayTags",new List<GameplayTag>{new GameplayTag("Unregistered")});
            var root=GameplayTagQueryExpression.AnyExpressionsMatch().AddExpression(GameplayTagQueryExpression.AllTagsMatch()).AddExpression(unknown);
            Throws<KeyNotFoundException>(()=>new GameplayTagQuery(root).Freeze());
        });
        Test("shared-dag-work-budget-and-deep-reuse",()=>{
            Setup("A"); var a=GameplayTagQueryExpression.AllTagsMatch().AddTag(GameplayTagManager.RequestTag("A")); var b=GameplayTagQueryExpression.NoTagsMatch();
            for(int i=0;i<28;i++) { var next=GameplayTagQueryExpression.AllExpressionsMatch().AddExpression(a).AddExpression(b); b=a; a=next; }
            Throws<InvalidOperationException>(()=>new GameplayTagQuery(a).Freeze());
            var leaf=GameplayTagQueryExpression.AllTagsMatch(); var subtree=leaf;
            for(int i=0;i<16;i++) subtree=GameplayTagQueryExpression.AllExpressionsMatch().AddExpression(subtree);
            var chain=subtree; for(int i=0;i<48;i++) chain=GameplayTagQueryExpression.AnyExpressionsMatch().AddExpression(chain);
            var shared=GameplayTagQueryExpression.AllExpressionsMatch().AddExpression(subtree).AddExpression(chain);
            Throws<InvalidOperationException>(()=>new GameplayTagQuery(shared).Freeze());
        });
        Test("simplification-reduces-retained-work",()=>{
            Setup("A.B.C","Z");
            foreach(var type in new[]{GameplayTagQueryExpressionType.AllTagsMatch,GameplayTagQueryExpressionType.AnyTagsMatch,GameplayTagQueryExpressionType.NoTagsMatch})
            {
                var e=new GameplayTagQueryExpression(type).AddTag(GameplayTagManager.RequestTag("A")).AddTag(GameplayTagManager.RequestTag("A.B")).AddTag(GameplayTagManager.RequestTag("A.B.C"));
                var frozen=new GameplayTagQuery(e).Freeze(); object node=typeof(FrozenGameplayTagQuery).GetField("m_Root",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(frozen);
                var payload=(GameplayTag[])node.GetType().GetField("Tags",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(node); Check(payload.Length==1);
            }
            var inner=GameplayTagQueryExpression.AllTagsMatch().AddTag(GameplayTagManager.RequestTag("Z"));
            var wrap=GameplayTagQueryExpression.AllExpressionsMatch().AddExpression(GameplayTagQueryExpression.AllExpressionsMatch().AddExpression(inner));
            Check(new GameplayTagQuery(wrap).Freeze().Matches(Container("Z")));
        });
        Test("asymmetric-large-sets-and-aliases",()=>{
            var names=Enumerable.Range(0,1024).Select(i=>"A.T"+i.ToString("D4")).ToArray(); Setup(names); var big=Container(names); var small=Container(names[0],names[1023]);
            var result=new GameplayTagContainer(2048);
            GameplayTagContainer.IntersectionExactInto(big,small,result); Same(result,new[]{names[0],names[1023]});
            var alias=new GameplayTagContainer(big); GameplayTagContainer.IntersectionExactInto(alias,small,alias); Same(alias,new[]{names[0],names[1023]});
            alias.CopyFrom(big); alias.RemoveTags(small); Same(alias,names.Skip(1).Take(1022));
            var alternating=Container(names.Where((_,i)=>i%2==0).ToArray()); alias.CopyFrom(big); alias.RemoveTags(alternating); Same(alias,names.Where((_,i)=>i%2!=0));
            alias.AppendTags(alternating); Same(alias,names);
        });
        Test("actual-result-capacity-zero-and-one",()=>{
            Setup("A","B","C"); var a=Container("A","B"); var b=Container("C"); var empty=new GameplayTagContainer(0); var one=new GameplayTagContainer(1); var subset=Container("B");
            Action action=()=>{ a.FilterExactInto(b,empty); a.FilterInto(b,empty); GameplayTagContainer.IntersectionExactInto(a,b,empty); a.FilterExactInto(subset,one); };
            for(int i=0;i<1000;i++) action(); long start=GC.GetAllocatedBytesForCurrentThread(); for(int i=0;i<10000;i++) action(); Check(GC.GetAllocatedBytesForCurrentThread()==start); Check(empty.Capacity==0); Check(one.Capacity==1);
        });
        Test("editor-rename-preserves-implicit-and-incoming-aliases",()=>{
            var s=Settings("A","A.B.C","Z"); s.RedirectsInternal.Add(new GameplayTagRedirect("Legacy","A.B"));
            Check(GameplayTagEditorUtility.TryRenameTag(s,"A","X",true,true,out _)); GameplayTagManager.Initialize(s,true);
            Check(GameplayTagManager.RequestTag("A.B").Name=="X.B"); Check(GameplayTagManager.RequestTag("Legacy").Name=="X.B");
            var noAliases=Settings("A","A.B.C"); noAliases.RedirectsInternal.Add(new GameplayTagRedirect("Legacy","A.B"));
            Check(GameplayTagEditorUtility.TryRenameTag(noAliases,"A","Y",true,false,out _)); GameplayTagManager.Initialize(noAliases,true); Check(GameplayTagManager.RequestTag("Legacy").Name=="Y.B");
        });
        Test("editor-transaction-readonly-and-delete",()=>{
            var s=Settings("A","A.B","Z"); uint hash=s.ComputeContentHash(); int revision=s.Revision;
            Check(!GameplayTagEditorUtility.TryRenameTag(s,"A","Z",true,true,out _)); Check(s.ComputeContentHash()==hash); Check(s.Revision==revision);
            s.SourcesInternal[0]=new GameplayTagSource("Default","owner",true); Check(!GameplayTagEditorUtility.TryRemoveTag(s,"A",true,out _)); Check(s.Revision==revision);
            s.SourcesInternal[0]=new GameplayTagSource("Default","owner",false); s.RedirectsInternal.Add(new GameplayTagRedirect("OldZ","Z")); s.RedirectsInternal.Add(new GameplayTagRedirect("OldA","A"));
            Check(GameplayTagEditorUtility.TryRemoveTag(s,"A",true,out _)); GameplayTagManager.Initialize(s,true); Check(GameplayTagManager.RequestTag("OldZ").Name=="Z"); Check(!GameplayTagManager.TryRequestTag("OldA",out _));
        });
        Test("csv-invalid-input-transaction",()=>{
            var s=Settings("Existing"); string path=Path.Combine(Environment.CurrentDirectory,"invalid.csv");
            string[] cases={"Wrong,Comment\nA,test\n","Tag,Comment,Source,Restricted\nA,test,Default,maybe\n","Tag\nA\nA\n","Tag,Comment\nA,\"unfinished\n","Tag,Comment\nA,\"ok\"tail\n","Tag\nA,unexpected\n","Tag\nState.Fire\nstate.Ice\n"};
            foreach(string csv in cases) { uint before=s.ComputeContentHash(); int revision=s.Revision; File.WriteAllText(path,csv); Check(!GameplayTagCsvUtility.Import(s,path,out _)); Check(s.ComputeContentHash()==before); Check(s.Revision==revision); }
        });
        Test("csv-quoted-multiline-and-export-roundtrip",()=>{
            var s=Settings("Existing"); string path=Path.Combine(Environment.CurrentDirectory,"quoted.csv");
            File.WriteAllText(path,"Tag,Comment,Source,Restricted,AllowNonRestrictedChildren\nA,\"first, \"\"quoted\"\"\nsecond\",Default,false,true\n");
            Check(GameplayTagCsvUtility.Import(s,path,out string error),error); Check(s.TryGetTagDefinition("A",out var definition)); Check(definition.DevComment=="first, \"quoted\"\nsecond");
            GameplayTagCsvUtility.Export(s,path); var other=Settings(); Check(GameplayTagCsvUtility.Import(other,path,out error),error); Check(other.TryGetTagDefinition("A",out var copy)); Check(copy.DevComment==definition.DevComment);
        });
        Test("generated-path-options-and-content-check",()=>{
            var s=Settings("A"); var originalPath=s.GeneratedCodePath;
            s.SetEditorOptions(true,true,false,"Game","Tags","Assets/../Outside.cs"); Throws<ArgumentException>(()=>GameplayTagCodeGenerator.Generate(s));
            s.SetEditorOptions(true,true,false,"Game","Tags",originalPath); uint before=s.ComputeContentHash();
            s.SetEditorOptions(true,false,false,"Game","Tags",originalPath); Check(before!=s.ComputeContentHash());
            AssetDatabase.Values[GameplayTagSettings.DefaultAssetPath]=s;
            Check(GameplayTagBuildValidator.Validate(out _,true)); string generated=File.ReadAllText(s.GeneratedCodePath); File.WriteAllText(s.GeneratedCodePath,generated+"// stale\n");
            Check(!GameplayTagBuildValidator.Validate(out _,false)); Check(GameplayTagBuildValidator.Validate(out _,true));
        });
    }
    static object Cost(string name,Action action,int iterations)
    {
        for(int i=0;i<8;i++) action(); var ns=new double[7]; var bytes=new double[7];
        for(int s=0;s<7;s++) { long b=GC.GetAllocatedBytesForCurrentThread(); long t=Stopwatch.GetTimestamp(); for(int i=0;i<iterations;i++) action(); long end=Stopwatch.GetTimestamp(); bytes[s]=(GC.GetAllocatedBytesForCurrentThread()-b)/(double)iterations; ns[s]=(end-t)*1e9/Stopwatch.Frequency/iterations; }
        return new{name,iterations,ns,allocated_bytes=bytes};
    }
    static List<object> ColdCosts()
    {
        Setup("A.B.C","Z"); var tag=GameplayTagManager.RequestTag("A.B.C"); var e=GameplayTagQueryExpression.AllTagsMatch().AddTag(tag); var q=new GameplayTagQuery(e);
        var result=new List<object>{
            Cost("name.parent",()=>sink=tag.GetDirectParent(),1000),
            Cost("name.leaf",()=>sink=tag.LeafName,1000),
            Cost("request.trimmed",()=>sink=GameplayTagManager.RequestTag(" A.B.C "),1000),
            Cost("query.freeze",()=>sink=q.Freeze(),1000),
            Cost("request.unknown-exception",()=>{try {GameplayTagManager.RequestTag("Unknown");}catch(KeyNotFoundException){checksum++;}},100),
            Cost("container.create-and-first-growth",()=>{var c=new GameplayTagContainer();c.AddTag(tag);sink=c;},1000)
        };
        foreach(int count in new[]{100,1000,10000})
        {
            var s=Settings(Enumerable.Range(0,count).Select(i=>"Registry.G"+(i/100)+".T"+i).ToArray());
            result.Add(Cost("registry.initialize."+count,()=>GameplayTagManager.Initialize(s,true),10));
        }
        return result;
    }
    static object Retained(string name,Func<object> create,int count=2000)
    {
        for(int i=0;i<16;i++) sink=create(); sink=null;
        var held=new object[count]; GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before=GC.GetTotalMemory(true);
        for(int i=0;i<count;i++) held[i]=create();
        long after=GC.GetTotalMemory(true); GC.KeepAlive(held);
        return new{name,samples=count,estimated_retained_bytes_per_instance=(after-before)/(double)count,note="Noisy retained managed heap delta, not exact object size or peak/native memory; shared registry strings and root holder array excluded."};
    }
    static List<object> MemoryCosts()
    {
        Setup("A.B.C"); var q=new GameplayTagQuery(GameplayTagQueryExpression.AllTagsMatch().AddTag(GameplayTagManager.RequestTag("A.B.C")));
        var result=new List<object>{Retained("container.capacity8",()=>new GameplayTagContainer(8)),Retained("container.capacity128",()=>new GameplayTagContainer(128)),Retained("frozen.single",()=>q.Freeze())};
        for(int i=0;i<7;i++) q=new GameplayTagQuery(GameplayTagQueryExpression.AllExpressionsMatch().AddExpression(q.RootExpression));
        result.Add(Retained("frozen.eight-levels-after-simplification",()=>q.Freeze()));
        return result;
    }
}
