using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using GameplayTags;
using UnityEngine;
internal static class AdaptiveChecks
{
    private static long checks;
    private static void Check(bool yes) { checks++; if (!yes) throw new Exception("Adaptive boundary regression"); }
    private static TagRegistry Registry()
    {
        var s=ScriptableObject.CreateInstance<GameplayTagSettings>();
        s.ReplaceAll(Enumerable.Range(0,4097).Select(i=>new GameplayTagDefinition("T"+i.ToString("D5"),"","Default",false,true)).ToList(),
            new List<GameplayTagRedirect>(),new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});
        return TagRegistry.Create(s);
    }
    private static void Same(RuntimeTagSet s,HashSet<int> expected)
    {
        Check(s.Count==expected.Count); int seen=0,previous=-1;
        foreach(var t in s) { Check(t.RuntimeIndex>previous && expected.Contains(t.RuntimeIndex)); previous=t.RuntimeIndex; seen++; }
        Check(seen==expected.Count);
        for(int i=0;i<s.Registry.Count;i++) Check(s.HasTagExact(s.Registry.GetTagAt(i))==expected.Contains(i));
        Check(!s.HasTagExact(default));
    }
    public static int Main(string[] args)
    {
        var r=Registry(); var foreign=Registry();
        foreach(var mode in new[]{TagSetStorage.Sparse,TagSetStorage.Dense})
        {
            for(int n=0;n<=65;n++)
            {
                var s=new RuntimeTagSet(r,257,mode);var expected=new HashSet<int>();
                for(int i=0;i<257;i++)s.AddTag(r.GetTagAt(i*7));
                s.Clear();
                for(int i=0;i<n;i++){int id=i*17+3;s.AddTag(r.GetTagAt(id));expected.Add(id);}
                Same(s,expected);var c=new RuntimeTagSet(s);
                Check(c.Capacity==s.Capacity && c.Storage==s.Storage); Same(c,expected);
                c.Clear(); Same(s,expected);s.CopyFrom(c);Same(s,new HashSet<int>());
                for(int i=0;i<33;i++)s.AddTag(r.GetTagAt(i+100));
                for(int i=32;i>=0;i--) { s.RemoveTag(r.GetTagAt(i+100)); var e=new HashSet<int>(Enumerable.Range(100,i));Same(s,e); }
                bool rejected=false; try{s.HasTagExact(foreign.GetTagAt(0));}catch(ArgumentException){rejected=true;}catch(InvalidOperationException){rejected=true;}
                Check(rejected);
            }
        }
        var random=new Random(20261001);var a=new RuntimeTagSet(r,512,TagSetStorage.Sparse);var b=new RuntimeTagSet(r,512,TagSetStorage.Dense);
        var expectedA=new HashSet<int>();var expectedB=new HashSet<int>();
        for(int step=0;step<3000;step++)
        {
            int id=random.Next(r.Count); bool add=random.Next(2)==0;
            if(add){Check(a.AddTag(r.GetTagAt(id))==expectedA.Add(id));}else{Check(a.RemoveTag(r.GetTagAt(id))==expectedA.Remove(id));}
            int idB=random.Next(r.Count); if((step&1)==0){b.AddTag(r.GetTagAt(idB));expectedB.Add(idB);}else{b.RemoveTag(r.GetTagAt(idB));expectedB.Remove(idB);}
            if(step%100==0){Same(a,expectedA);Same(b,expectedB);var union=RuntimeTagSet.Union(a,b);var expected=new HashSet<int>(expectedA);expected.UnionWith(expectedB);Same(union,expected);}
        }
        var p=new RuntimeTagSet(r,256,TagSetStorage.Sparse);var q=new RuntimeTagSet(r,256,TagSetStorage.Sparse);var output=new RuntimeTagSet(r,512,TagSetStorage.Sparse);
        for(int i=0;i<128;i++){p.AddTag(r.GetTagAt(i*7));q.AddTag(r.GetTagAt(i*7+1));}
        for(int k=0;k<200;k++){RuntimeTagSet.UnionInto(p,q,output);output.CopyFrom(p);output.AppendTags(q);output.RemoveTags(q);}
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect(); long before=GC.GetAllocatedBytesForCurrentThread();
        for(int k=0;k<3000;k++){RuntimeTagSet.UnionInto(p,q,output);output.CopyFrom(p);output.AppendTags(q);output.RemoveTags(q);Check(output.Count==p.Count);}
        long bytes=GC.GetAllocatedBytesForCurrentThread()-before;Check(bytes==0);
        before=GC.GetAllocatedBytesForCurrentThread();object control=new byte[128];GC.KeepAlive(control);long positive=GC.GetAllocatedBytesForCurrentThread()-before;Check(positive>=128);
        var result=new{checks,failures=0,prepared_bytes=bytes,positive_bytes=positive,vector_width=Vector<int>.Count,vector_accelerated=Vector.IsHardwareAccelerated,native_unity="not_run"};
        File.WriteAllText(args[0],JsonSerializer.Serialize(result));Console.WriteLine("ADAPTIVE_CHECKS "+JsonSerializer.Serialize(result));return 0;
    }
}
