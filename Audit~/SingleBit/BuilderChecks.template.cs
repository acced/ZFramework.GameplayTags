using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GameplayTags;
using GameplayTags.Experiments;
using Set = GameplayTags.Experiments.__TYPE__;

internal static class SingletonChecks
{
    private static long assertions;
    private static object sink;
    private static readonly FieldInfo Used = typeof(Set).GetField("used", BindingFlags.Instance|BindingFlags.NonPublic);
    private static readonly FieldInfo Entries = typeof(Set).GetField("entries", BindingFlags.Instance|BindingFlags.NonPublic);
    private static void Check(bool value,string message) { assertions++; if(!value) throw new Exception(message); }
    private static TagRegistry Registry(int n)
    {
        var s=UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        s.ReplaceAll(Enumerable.Range(0,n).Select(i=>new GameplayTagDefinition("T"+i.ToString("D6"),"","Default",false,true)).ToList(),new List<GameplayTagRedirect>(),new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});
        return TagRegistry.Create(s);
    }
    private static Set Make(TagRegistry r,IEnumerable<int> ids,int cap=4,BitmapLayout layout=BitmapLayout.Micro)
    { var s=new Set(r,cap,layout);foreach(var id in ids)s.AddTag(r.GetTagAt(id));return s; }
    private static int[] Values(Set s) { var v=new List<int>();foreach(var t in s)v.Add(t.RuntimeIndex);return v.ToArray(); }
    private static bool IsSingle(Set s) => s.Layout==BitmapLayout.Micro && s.Count==(int)Used.GetValue(s);
    private static void Verify(Set s,IEnumerable<int> ids)
    {
        int[] expected=ids.Distinct().OrderBy(x=>x).ToArray();
        s.AssertInvariants();Check(true,"invariants");
        Check(s.Count==expected.Length,"eager Count");Check(Values(s).SequenceEqual(expected),"members and order");
        if(s.Layout==BitmapLayout.Micro)
        {
            bool independent=expected.GroupBy(x=>x>>4).All(g=>g.Count()==1);
            Check(IsSingle(s)==independent,"Count==used equivalence");
        }
    }
    private static void Masks()
    {
        var r=Registry(257);
        for(int mask=0;mask<65536;mask++)
        {
            int bit=(mask^(mask>>8))&15;
            int[] av={bit,64};
            int[] bv=Enumerable.Range(0,16).Where(i=>(mask&(1<<i))!=0).Concat(new[]{64,65}).ToArray();
            var a=Make(r,av);var b=Make(r,bv);Check(IsSingle(a),"single lhs");Check(!IsSingle(b),"multi rhs");
            var u=Set.Union(a,b,BitmapLayout.Micro);Verify(u,av.Concat(bv));
            var rev=Set.Union(b,a,BitmapLayout.Micro);Verify(rev,av.Concat(bv));
            var appended=new Set(a);appended.AppendTags(b);Verify(appended,av.Concat(bv));
            var reverseAppend=new Set(b);reverseAppend.AppendTags(a);Verify(reverseAppend,av.Concat(bv));
            var removed=new Set(a);removed.RemoveTags(b);Verify(removed,av.Except(bv));
            var generic=new Set(b);generic.RemoveTags(a);Verify(generic,bv.Except(av));
            if((mask&255)==0){Verify(a,av);Verify(b,bv);}
        }
    }
    private static void Transitions()
    {
        var r=Registry(4113);
        foreach(int cap in new[]{0,1,4,128})
        {
            var a=Make(r,new[]{1,65,4096},cap);Check(IsSingle(a),"initial single");
            a.AddTag(r.GetTagAt(2));Check(!IsSingle(a),"adding second bit exits path");
            var b=Make(r,new[]{2,66,4096},4);
            var u=Set.Union(a,b);Verify(u,new[]{1,2,65,66,4096});Check(!IsSingle(u),"union Count is not record count");
            a.RemoveTag(r.GetTagAt(2));Check(IsSingle(a),"removing second bit reenters path");
            a.AppendTags(b);Verify(a,new[]{1,2,65,66,4096});Check(!IsSingle(a),"append exits path");
            a.RemoveTags(b);Verify(a,new[]{1,65});Check(IsSingle(a),"batch removal reenters path");
            a.RemoveTag(r.GetTagAt(65));Verify(a,new[]{1});
            a.EnsureCapacity(128);Check(Entries.GetValue(a)!=null,"retained backing with one record");
            var z=Make(r,new[]{1,2,128},4);a.RemoveTags(z);Verify(a,Array.Empty<int>());
            a.AddTag(r.GetTagAt(4112));Verify(a,new[]{4112});
            var copy=new Set(a);Verify(copy,new[]{4112});copy.RemoveTags(copy);Verify(copy,Array.Empty<int>());Verify(a,new[]{4112});
        }
        // Two SINGLE records in the same interval with different bits produce TWO members.
        Set left=Make(r,new[]{0,64}),right=Make(r,new[]{1,65});
        var result=Set.Union(left,right);Verify(result,new[]{0,1,64,65});Check(result.Count==4 && (int)Used.GetValue(result)==2,"different-bit union cardinality");
        var oneMulti=Make(r,new[]{0,1,64,65});oneMulti.RemoveTags(left);Verify(oneMulti,new[]{1,65});Check(IsSingle(oneMulti),"general removal can create singleton state");
        foreach(var layout in new[]{BitmapLayout.Micro,BitmapLayout.Dense})
        {
            var x=Make(r,new[]{1,65},128,layout);var y=Make(r,new[]{1,2,66},128);
            Set.UnionInto(x,y,x);Verify(x,new[]{1,2,65,66});
            Set.UnionInto(x,y,y);Verify(y,new[]{1,2,65,66});
            x.RemoveTags(y);Verify(x,Array.Empty<int>());
        }
        var aa=Make(r,new[]{0,32,64,96});var bb=Make(r,new[]{1,33,65,97});var target=Make(r,Array.Empty<int>(),4);
        Set.UnionInto(aa,bb,target);Verify(target,new[]{0,1,32,33,64,65,96,97});
        Check(target.ReservedMemberCapacity==4,"actual record capacity retained");
    }
    private static void Churn()
    {
        var r=Registry(4113);var random=new Random(20261001);var s=new Set(r,256,BitmapLayout.Micro);var expected=new HashSet<int>();
        for(int i=0;i<5000;i++)
        {
            int[] ids=Enumerable.Range(0,random.Next(1,40)).Select(_=>random.Next(r.Count)).ToArray();
            var b=Make(r,ids,64);
            if((i&1)==0){s.AppendTags(b);expected.UnionWith(ids);}else{s.RemoveTags(b);expected.ExceptWith(ids);}
            if(i%23==0){s.Clear();expected.Clear();}
            if(i%7==0)Verify(s,expected);
        }
        Verify(s,expected);
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static object Allocate() => new byte[128];
    private static void Allocations()
    {
        var r=Registry(4113);var a=Make(r,new[]{0,32,64,96},64);var b=Make(r,new[]{0,1,32,33,128,129},64);var c=new Set(r,64,BitmapLayout.Micro);
        for(int k=0;k<4000;k++){c.CopyFrom(a);c.AppendTags(b);c.RemoveTags(b);Set.UnionInto(a,b,c);}
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int k=0;k<6000;k++){c.CopyFrom(a);c.AppendTags(b);c.RemoveTags(b);Set.UnionInto(a,b,c);}
        long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        before=GC.GetAllocatedBytesForCurrentThread();sink=Allocate();long positive=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(bytes==0,"prepared allocation");Check(positive>0,"allocation positive control");
        Verify(c,new[]{0,1,32,33,64,96,128,129});
        Console.WriteLine("SINGLE_ALLOCATION bytes="+bytes+" positive="+positive);
    }
    private static int Main(string[] args)
    {
        Masks();Transitions();Churn();Allocations();
        string json=JsonSerializer.Serialize(new{candidate=typeof(Set).Name,assertions,failures=0,masks=65536,seed=20261001,scope="single-bit record predicate; no instrumentation in timed source",native_unity="not_run"});
        File.WriteAllText(args[0],json);Console.WriteLine("SINGLE_TESTS "+json);return 0;
    }
}
