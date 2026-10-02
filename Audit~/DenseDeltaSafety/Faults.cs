// Separate diagnostic executable. These fields/hooks never enter timed builds.
using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GameplayTags;
using GameplayTags.Experiments;
using Set=GameplayTags.Experiments.DirectArraySet;
using Layout=GameplayTags.Experiments.BitmapLayout;
namespace GameplayTags.Experiments
{
    internal static class AllocationFault
    {
        internal static bool Enabled;
        internal static int Calls, FailAt;
        internal static void Arm(int failAt){Calls=0;FailAt=failAt;Enabled=true;}
        internal static void BeforeAllocation(int size)
        {
            if(Enabled && ++Calls==FailAt) throw new OutOfMemoryException("injected-growth");
        }
    }
}
internal static class FaultProbe
{
    static long checks;static int cases,exceptions,scopedFailures,minimalBad;
    static readonly List<object> observations=new List<object>();
    static void Check(bool b,string m){checks++;if(!b)throw new Exception(m);}
    static TagRegistry Registry(int n)
    {
        var s=UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        s.ReplaceAll(Enumerable.Range(0,n).Select(i=>new GameplayTagDefinition("T"+i.ToString("D7"),"","Default",false,true)).ToList(),new List<GameplayTagRedirect>(),new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});
        return TagRegistry.Create(s);
    }
    static Set Make(TagRegistry r,int[] ids,Layout mode,int capacity)
    {var s=new Set(r,capacity,mode);foreach(int i in ids)s.AddTag(r.GetTagAt(i));return s;}
    static int[] Snapshot(Set s){var list=new List<int>();foreach(var t in s)list.Add(t.RuntimeIndex);return list.ToArray();}
    static void Apply(Set a,Set b,int api)
    {if(api==0)a.AppendTags(b);else if(api==1)Set.UnionInto(a,b,a);else Set.UnionInto(b,a,a);}
    static void Exercise(string label,TagRegistry r,int[] x,int[] y,int reserve,int api,string shape)
    {
        var source=Make(r,y,Layout.Dense,0);var union=x.Concat(y).Distinct().OrderBy(i=>i).ToArray();
        var trial=Make(r,x,Layout.Micro,reserve);
        AllocationFault.Arm(int.MaxValue);Apply(trial,source,api);int sites=AllocationFault.Calls;AllocationFault.Enabled=false;
        Check(Snapshot(trial).SequenceEqual(union)&&trial.Count==union.Length,"fault-free result wrong");
        for(int fail=1;fail<=sites;fail++)
        {
            var target=Make(r,x,Layout.Micro,reserve);bool caught=false;
            AllocationFault.Arm(fail);
            try{Apply(target,source,api);}
            catch(OutOfMemoryException e) when(e.Message=="injected-growth"){caught=true;exceptions++;}
            finally{AllocationFault.Enabled=false;}
            cases++;Check(caught,"injected failure did not propagate");
            int[] actual=Snapshot(target);bool invariant=true;
            try{target.AssertInvariants();}catch(Exception){invariant=false;}
            bool countOk=target.Count==actual.Length;
            bool membersOk=actual.SequenceEqual(actual.Distinct().OrderBy(i=>i)) && !x.Except(actual).Any() && !actual.Except(union).Any();
            Check(Snapshot(source).SequenceEqual(y)&&source.Count==y.Length,"source mutated on failure");
            bool scoped=x.Length>0&&(label=="batch32"||label=="monotone"||(label=="safe32"&&y.Length<=32));
            bool valid=countOk&&membersOk&&invariant;
            if(!valid)
            {
                if(scoped)scopedFailures++;
                observations.Add(new{label,shape,reserve,api,fail,sourceCount=y.Length,targetBefore=x.Length,targetAfter=actual.Length,reported=target.Count,countOk,membersOk,invariant,scoped});
                if(shape=="minimal"&&label=="delta32")minimalBad++;
            }
            if(scoped)
            {
                Check(valid,"new helper invalid after injected failure: "+JsonSerializer.Serialize(observations.LastOrDefault()));
                // Continued use is permitted: retry without the simulated failure.
                target.AppendTags(source);Check(Snapshot(target).SequenceEqual(union)&&target.Count==union.Length,"retry invalid");
                target.RemoveTags(source);int[] remaining=x.Except(y).OrderBy(i=>i).ToArray();
                Check(Snapshot(target).SequenceEqual(remaining)&&target.Count==remaining.Length,"remove after failure invalid");
                target.AssertInvariants();
            }
        }
    }
    static void Main(string[] args)
    {
        string label=args[0];var r=Registry(8193);
        Exercise(label,r,new[]{0},new[]{1,16},0,0,"minimal");
        foreach(int nx in new[]{0,1,8,64,128})
        foreach(int ny in new[]{1,8,31,32,33,65,129})
        foreach(int reserve in new[]{0,1,4,32})
        foreach(bool high in new[]{false,true})
        foreach(int api in new[]{0,1,2})
        {
            int[] x=Enumerable.Range(0,nx).Select(i=>(high?4096:0)+i*32).ToArray();
            int[] y=Enumerable.Range(0,ny).Select(i=>i==0&&nx>0?x[0]+1:16+(i-1+(nx==0?1:0))*32).Distinct().OrderBy(i=>i).ToArray();
            Exercise(label,r,x,y,reserve,api,high?"high-target":"mixed-matches-missing");
        }
        if(label=="delta32")Check(minimalBad==1,"old minimal Count mismatch was not reproduced");
        Check(scopedFailures==0,"scoped failure");
        var result=new{label,checks,cases,exceptions,minimalBad,scopedFailures,observations,scope="nonempty batch32/monotone; nonempty safe32 with source Count<=32 only",nativeUnity="not_run",simulated=true};
        File.WriteAllText(args[1],JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("FAULT_RESULT "+JsonSerializer.Serialize(new{label,checks,cases,exceptions,minimalBad,scopedFailures,observations=observations.Count}));
    }
}
