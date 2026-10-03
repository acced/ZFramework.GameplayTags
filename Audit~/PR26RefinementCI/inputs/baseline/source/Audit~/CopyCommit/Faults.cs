// Separate test-only executable. Allocation hooks are absent from timed builds.
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
    internal static class CopyFault
    {
        internal static bool Enabled;
        internal static int Calls,FailAt;
        internal static void Arm(int fail){Calls=0;FailAt=fail;Enabled=true;}
        internal static void Before(){if(Enabled&&++Calls==FailAt)throw new OutOfMemoryException("injected-copy-growth");}
    }
}
internal static class CopyFaultProbe
{
    static long checks;static int cases,injected,normal,minimalBad;
    static readonly List<object> invalid=new List<object>();
    static void Check(bool ok,string reason){checks++;if(!ok)throw new Exception(reason);}
    static TagRegistry Registry(int n)
    {
        var s=UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        s.ReplaceAll(Enumerable.Range(0,n).Select(i=>new GameplayTagDefinition("T"+i.ToString("D7"),"","Default",false,true)).ToList(),new List<GameplayTagRedirect>(),new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});
        return TagRegistry.Create(s);
    }
    static Set Make(TagRegistry r,int[] ids,Layout layout,int capacity)
    {var s=new Set(r,capacity,layout);foreach(int id in ids)s.AddTag(r.GetTagAt(id));return s;}
    static int[] Snapshot(Set s){var ids=new List<int>();foreach(var t in s)ids.Add(t.RuntimeIndex);return ids.ToArray();}
    static void Apply(Set s,Set source,Set empty,int api)
    {
        if(api==0)s.CopyFrom(source);
        else if(api==1)s.AppendTags(source);
        else if(api==2)Set.UnionInto(s,source,s);
        else if(api==3)Set.UnionInto(source,s,s);
        else if(api==4)Set.UnionInto(source,source,s);
        else if(api==5)Set.UnionInto(empty,source,s);
        else Set.UnionInto(source,empty,s);
    }
    static void Case(string label,TagRegistry r,int[] old,int[] ids,int reserve,int api,Layout mode,bool minimal=false)
    {
        var source=Make(r,ids,mode,0);var empty=new Set(r,0,Layout.Micro);
        var clean=Make(r,old,Layout.Micro,reserve);
        CopyFault.Arm(int.MaxValue);Apply(clean,source,empty,api);int sites=CopyFault.Calls;CopyFault.Enabled=false;
        normal++;clean.AssertInvariants();Check(clean.Count==ids.Length&&Snapshot(clean).SequenceEqual(ids),"normal copy");
        for(int site=1;site<=sites;site++)
        {
            var target=Make(r,old,Layout.Micro,reserve);bool caught=false;
            CopyFault.Arm(site);
            try{Apply(target,source,empty,api);}
            catch(OutOfMemoryException e)when(e.Message=="injected-copy-growth"){caught=true;injected++;}
            finally{CopyFault.Enabled=false;}
            cases++;Check(caught,"allocation failure swallowed");
            int[] actual=Snapshot(target);bool invariant=true;
            try{target.AssertInvariants();}catch(Exception){invariant=false;}
            // Dense replacement clears first; same-Micro allocation happens before replacing.
            bool membership=mode==Layout.Dense?actual.SequenceEqual(ids.Take(actual.Length)):actual.SequenceEqual(old);
            bool valid=invariant&&membership&&target.Count==actual.Length;
            Check(source.Count==ids.Length&&Snapshot(source).SequenceEqual(ids),"source mutated");
            if(!valid)
            {
                invalid.Add(new{label,api,mode=mode.ToString(),reserve,site,old=old.Length,source=ids.Length,actual=actual.Length,reported=target.Count,invariant,membership,minimal});
                if(minimal)minimalBad++;
            }
            if(label!="control")
            {
                Check(valid,"copy partial state: "+JsonSerializer.Serialize(invalid.LastOrDefault()));
                // Retry uses CopyFrom even when the tested entry was an alias shortcut.
                target.CopyFrom(source);target.AssertInvariants();Check(Snapshot(target).SequenceEqual(ids),"retry");
                if(ids.Length>0){target.RemoveTag(r.GetTagAt(ids[0]));Check(target.Count==ids.Length-1,"remove after failure");}
                target.Clear();target.AppendTags(source);target.AssertInvariants();Check(target.Count==ids.Length,"reuse after failure");
            }
        }
    }
    static void Main(string[] args)
    {
        string label=args[0];var r=Registry(32769);
        Case(label,r,Array.Empty<int>(),new[]{0,1,16},0,1,Layout.Dense,true);
        foreach(int records in new[]{0,1,2,3,4,5,8,17,33,129,513})
        foreach(int reserve in new[]{0,1,2,4,8,31,64,1024})
        foreach(bool high in new[]{false,true})
        foreach(bool dirty in new[]{false,true})
        foreach(var mode in new[]{Layout.Dense,Layout.Micro})
        for(int api=0;api<7;api++)
        {
            if(dirty&&api>=1&&api<=3)continue;
            int offset=high?16384:0;
            int[] ids=Enumerable.Range(0,records).SelectMany(i=>new[]{offset+i*16,offset+i*16+1}).ToArray();
            Case(label,r,dirty?new[]{7,39,32768}:Array.Empty<int>(),ids,reserve,api,mode);
        }
        if(label=="control")Check(minimalBad==1&&invalid.Count>0,"old empty-copy defect not reproduced");
        else Check(invalid.Count==0&&minimalBad==0,"new copy failure-state error");
        File.WriteAllText(args[1],JsonSerializer.Serialize(new{label,checks,cases,injected,normal,minimalBad,invalid,failures=0,simulated=true,scope="copy and empty/same-input union shortcuts; not arbitrary mixed union OOM"}));
        Console.WriteLine("COPY_FAULT "+JsonSerializer.Serialize(new{label,checks,cases,injected,normal,minimalBad,invalid=invalid.Count,failures=0}));
    }
}
