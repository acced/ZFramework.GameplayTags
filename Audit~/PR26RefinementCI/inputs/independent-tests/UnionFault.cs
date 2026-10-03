using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GameplayTags;
using Set=GameplayTags.Experiments.DirectArraySet;
using Layout=GameplayTags.Experiments.BitmapLayout;
namespace GameplayTags.Experiments
{
    internal static class CopyFault
    {
        internal static bool Enabled;
        internal static int Calls,FailAt;
        internal static void Arm(int at) { Calls=0;FailAt=at;Enabled=true; }
        internal static void Before() { if(Enabled&&++Calls==FailAt) throw new OutOfMemoryException("union-growth-injected"); }
    }
}
internal static class UnionFault
{
    static long checks;
    static void Check(bool ok,string why) { checks++;if(!ok)throw new Exception(why); }
    static TagRegistry Registry(int n)
    {
        var s=UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        s.ReplaceAll(Enumerable.Range(0,n).Select(i=>new GameplayTagDefinition("T"+i.ToString("D7"),"","Default",false,true)).ToList(),new List<GameplayTagRedirect>(),new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});
        return TagRegistry.Create(s);
    }
    static Set Make(TagRegistry r,int[] ids,Layout layout,int capacity=0)
    {var s=new Set(r,capacity,layout);foreach(int id in ids)s.AddTag(r.GetTagAt(id));return s;}
    static int[] Snapshot(Set s) {var a=new List<int>();foreach(var t in s)a.Add(t.RuntimeIndex);return a.ToArray();}
    static int Main(string[] args)
    {
        var r=Registry(4099);var rows=new List<object>();int normal=0,injected=0,invalid=0;
        foreach(bool bothDense in new[]{false,true}) foreach(int records in new[]{2,3,8,33,129}) foreach(int capacity in new[]{0,1,2,4,8,64,256}) foreach(bool dirty in new[]{false,true})
        {
            int[] av=Enumerable.Range(0,records).SelectMany(i=>new[]{i*16,i*16+1}).ToArray(), bv=new[]{records*16,records*16+3,4098};
            int[] expected=av.Concat(bv).OrderBy(x=>x).ToArray(), old=dirty?new[]{7,4097}:Array.Empty<int>();
            var a=Make(r,av,Layout.Dense);var b=Make(r,bv,bothDense?Layout.Dense:Layout.Micro);
            var clean=Make(r,old,Layout.Micro,capacity);GameplayTags.Experiments.CopyFault.Arm(int.MaxValue);Set.UnionInto(a,b,clean);int growth=GameplayTags.Experiments.CopyFault.Calls;GameplayTags.Experiments.CopyFault.Enabled=false;
            clean.AssertInvariants();Check(Snapshot(clean).SequenceEqual(expected),"normal membership");normal++;
            for(int site=1;site<=growth;site++)
            {
                var result=Make(r,old,Layout.Micro,capacity);bool caught=false;
                GameplayTags.Experiments.CopyFault.Arm(site);
                try {Set.UnionInto(a,b,result);} catch(OutOfMemoryException e) when(e.Message=="union-growth-injected") {caught=true;} finally {GameplayTags.Experiments.CopyFault.Enabled=false;}
                Check(caught,"growth fault must escape");injected++;
                int[] actual=Snapshot(result);bool invariant=true;string error=null;
                try {result.AssertInvariants();} catch(Exception e) {invariant=false;error=e.Message;}
                bool prefix=actual.SequenceEqual(expected.Take(actual.Length)), exact=result.Count==actual.Length;
                Check(prefix,"invalid prefix");Check(Snapshot(a).SequenceEqual(av)&&a.Count==av.Length,"left source mutated");Check(Snapshot(b).SequenceEqual(bv)&&b.Count==bv.Length,"right source mutated");a.AssertInvariants();b.AssertInvariants();
                if(!invariant||!exact)invalid++;
                rows.Add(new { path=bothDense?"dense+dense-to-micro":"dense+micro-to-micro",records,capacity,dirty,site,actual=actual.Length,reported=result.Count,invariant,prefix,error });
                result.CopyFrom(a);result.AssertInvariants();Check(Snapshot(result).SequenceEqual(av),"retry after failed union");
                result.Clear();result.AddTag(r.GetTagAt(4098));result.AssertInvariants();Check(result.Count==1,"reuse after failed union");
            }
        }
        var report=new { checks,normal,injected,invalid,simulated=true,rows };File.WriteAllText(args[0],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new {checks,normal,injected,invalid}));
        return Environment.GetEnvironmentVariable("EXPECT_VALID_UNION_FAULTS")=="1"&&invalid!=0?1:0;
    }
}
