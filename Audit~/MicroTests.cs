using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GameplayTags;
using GameplayTags.Experiments;
internal static class MicroTests
{
    private static long assertions;
    private static int groups;
    private static object sink;
    private static readonly BitmapLayout[] Layouts = { BitmapLayout.Micro, BitmapLayout.Dense };
    private static void Check(bool ok, string label) { assertions++; if (!ok) throw new Exception(label); }
    private static TagRegistry Registry(int size)
    {
        var s = UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        s.ReplaceAll(Enumerable.Range(0,size).Select(i=>new GameplayTagDefinition("T"+i.ToString("D6"),"","Default",false,true)).ToList(),
            new List<GameplayTagRedirect>(),new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});
        return TagRegistry.Create(s);
    }
    private static MicroBitmapSet Set(TagRegistry r, IEnumerable<int> ids, BitmapLayout layout, int capacity=0)
    { var s = new MicroBitmapSet(r,capacity,layout); foreach(int id in ids) s.AddTag(r.GetTagAt(id)); return s; }
    private static int[] Values(MicroBitmapSet s)
    { var v=new List<int>(); foreach(var t in s) { Check(ReferenceEquals(t.Registry,s.Registry),"enumerator owner");v.Add(t.RuntimeIndex); } return v.ToArray(); }
    private static void Verify(MicroBitmapSet s, IEnumerable<int> expected)
    {
        s.AssertInvariants(); assertions++;
        int[] v=Values(s), wanted=expected.Distinct().OrderBy(x=>x).ToArray();
        Check(v.SequenceEqual(wanted),"membership/order"); Check(s.Count==wanted.Length,"Count");
        foreach(int x in wanted) Check(s.HasTagExact(s.Registry.GetTagAt(x)),"contains enumerated member");
    }
    private static void Throws(Action a)
    { bool thrown=false;try { a(); } catch(ArgumentException){ thrown=true; }Check(thrown,"Expected input exception"); }
    private static void Group(string name,Action a) { a();groups++;Console.WriteLine("PASS "+name); }
    private static void Boundaries()
    {
        var r=Registry(259); var other=Registry(259);
        foreach(var layout in Layouts)
        {
            var s=new MicroBitmapSet(r,0,layout);
            Check(!s.HasTagExact(default)&&!s.AddTag(default)&&!s.RemoveTag(default),"default handle");
            int[] ids={0,1,15,16,31,32,63,64,127,128,255,258};
            foreach(int id in ids){Check(s.AddTag(r.GetTagAt(id)),"add");Check(!s.AddTag(r.GetTagAt(id)),"duplicate");}
            Verify(s,ids);Check(!s.RemoveTag(r.GetTagAt(2)),"missing remove");
            Throws(()=>s.AddTag(other.GetTagAt(1)));Throws(()=>s.HasTagExact(other.GetTagAt(1)));
            Throws(()=>s.AppendTags(new MicroBitmapSet(other)));Throws(()=>s.CopyFrom(new MicroBitmapSet(other)));Verify(s,ids);
            var copy=new MicroBitmapSet(s);copy.RemoveTag(r.GetTagAt(0));Verify(s,ids);Check(s.Count!=copy.Count,"independent copy");
            s.CopyFrom(s);Verify(s,ids);s.RemoveTags(s);Verify(s,Array.Empty<int>());
            s.EnsureCapacity(128);int cap=s.ReservedMemberCapacity;Check(cap>=128,"reserve");s.Clear();Check(s.ReservedMemberCapacity==cap,"clear reserve");
            Throws(()=>s.EnsureCapacity(-1));Throws(()=>new MicroBitmapSet(r,-1));Throws(()=>new MicroBitmapSet(r,0,(BitmapLayout)42));
        }
        var empty=Registry(0);foreach(var l in Layouts){var e=new MicroBitmapSet(empty,99,l);Verify(e,Array.Empty<int>());}
    }
    private static void Exhaustive()
    {
        var r=Registry(259);int[] keys={0,15,16,31,64,258};
        for(int a=0;a<64;a++)for(int b=0;b<64;b++)foreach(var la in Layouts)foreach(var lb in Layouts)
        {
            int[] av=keys.Where((x,i)=>(a&(1<<i))!=0).ToArray(),bv=keys.Where((x,i)=>(b&(1<<i))!=0).ToArray();
            var x=Set(r,av,la);var y=Set(r,bv,lb);
            foreach(var output in Layouts)
            {
                var z=Set(r,new[]{3,100,200},output,12);MicroBitmapSet.UnionInto(x,y,z);Verify(z,av.Concat(bv));
                var u=MicroBitmapSet.Union(x,y,output);Verify(u,av.Concat(bv));u.AddTag(r.GetTagAt(257));Verify(x,av);Verify(y,bv);
                var target=Set(r,Array.Empty<int>(),output);target.CopyFrom(x);Verify(target,av);target.AppendTags(y);Verify(target,av.Concat(bv));
                target.CopyFrom(x);target.RemoveTags(y);Verify(target,av.Except(bv));
            }
            var left=new MicroBitmapSet(x);MicroBitmapSet.UnionInto(left,y,left);Verify(left,av.Concat(bv));
            var right=new MicroBitmapSet(y);MicroBitmapSet.UnionInto(x,right,right);Verify(right,av.Concat(bv));
            var same=new MicroBitmapSet(x);MicroBitmapSet.UnionInto(same,same,same);Verify(same,av);
        }
    }
    private static void Randomized()
    {
        var r=Registry(4113);var random=new Random(20260930);
        foreach(var layout in new[]{BitmapLayout.Micro,BitmapLayout.Dense,BitmapLayout.Auto})
        {
            var actual=new MicroBitmapSet(r,0,layout);var expected=new HashSet<int>();
            for(int step=0;step<6000;step++)
            {
                int id=random.Next(r.Count),op=random.Next(10);
                if(op<5)Check(actual.AddTag(r.GetTagAt(id))==expected.Add(id),"random add");
                else if(op<8)Check(actual.RemoveTag(r.GetTagAt(id))==expected.Remove(id),"random remove");
                else if(op==8)
                {
                    int[] ids=Enumerable.Range(0,random.Next(45)).Select(_=>random.Next(r.Count)).ToArray();
                    var b=Set(r,ids,Layouts[step&1]);if((step&2)==0){actual.AppendTags(b);expected.UnionWith(ids);}else{actual.RemoveTags(b);expected.ExceptWith(ids);}
                }
                else if(step%111==0){actual.Clear();expected.Clear();}
                if(step%13==0)Verify(actual,expected);
                if(step%101==0)
                {
                    var copy=new MicroBitmapSet(actual);Verify(copy,expected);
                    var converted=new MicroBitmapSet(r,0,Layouts[step&1]);converted.CopyFrom(actual);Verify(converted,expected);
                }
            }
            Verify(actual,expected);
        }
    }
    private static void EncodingLimit()
    {
        // Minimal owner for high-address arithmetic only; NOT registry construction acceptance.
        Func<int,TagRegistry> fake = n => {
            var r=(TagRegistry)RuntimeHelpers.GetUninitializedObject(typeof(TagRegistry));
            typeof(TagRegistry).GetField("m_Names",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(r,new string[n]);return r;
        };
        var maximum=fake(1<<20);var m=new MicroBitmapSet(maximum);
        int[] ids={0,15,16,65535,65536,(1<<20)-2,(1<<20)-1};
        foreach(var x in ids)m.AddTag(maximum.GetTagAt(x));Verify(m,ids);
        var larger=fake((1<<20)+97);
#if MICRO_WIDE
        var w=new MicroBitmapSet(larger);int[] high={0,31,32,(1<<20)-1,1<<20,larger.Count-1};
        foreach(int id in high)w.AddTag(larger.GetTagAt(id));Verify(w,high);
        var copy=new MicroBitmapSet(w);copy.RemoveTags(w);Check(copy.Count==0,"wide high removal");
#else
        Throws(()=>new MicroBitmapSet(larger));
#endif
    }
    private static void AllocationChecks()
    {
        var r=Registry(4099);long bytes=0;
        foreach(var la in Layouts)foreach(var lb in Layouts)foreach(var lc in Layouts)
        {
            var a=new MicroBitmapSet(r,64,la);var b=new MicroBitmapSet(r,64,lb);var c=new MicroBitmapSet(r,128,lc);
            var ids=new RuntimeTag[64];for(int i=0;i<ids.Length;i++)ids[i]=r.GetTagAt((i*61+3)%r.Count);
            Action body=()=>{
                a.Clear();b.Clear();for(int i=0;i<32;i++){a.AddTag(ids[i]);b.AddTag(ids[i+16]);}
                MicroBitmapSet.UnionInto(a,b,c); c.CopyFrom(a);c.AppendTags(b);c.RemoveTags(b);
                a.CopyFrom(b);a.RemoveTag(ids[17]);a.AddTag(ids[1]);sink=c;
            };
            for(int i=0;i<100;i++)body();GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)body();
            long delta=GC.GetAllocatedBytesForCurrentThread()-before;bytes+=delta;Check(delta==0,"prepared allocated "+delta);a.AssertInvariants();c.AssertInvariants();
        }
        long positiveBefore=GC.GetAllocatedBytesForCurrentThread();sink=new byte[128];
        long positive=GC.GetAllocatedBytesForCurrentThread()-positiveBefore;Check(positive>=128,"broken counter");
        Console.WriteLine("PREPARED_BYTES="+bytes+" POSITIVE_BYTES="+positive);
        var x=Set(r,new[]{0,16,32,64,128,512,1024,4096},BitmapLayout.Micro,8);
        var y=new MicroBitmapSet(x);var result=new MicroBitmapSet(r,8,BitmapLayout.Micro);
        MicroBitmapSet.UnionInto(x,y,result);long start=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++)MicroBitmapSet.UnionInto(x,y,result);
        Check(GC.GetAllocatedBytesForCurrentThread()==start,"actual result capacity grew");Verify(result,Values(x));
    }
    private static int Main(string[] args)
    {
        try
        {
            Group("scope-default-empty-tail-inline-growth-and-independent-copy",Boundaries);
            Group("4096-pairs-both-encodings-dirty-outputs-and-aliases",Exhaustive);
            Group("fixed-seed-continuous-mutating-and-explicit-conversion",Randomized);
            Group("encoding-limit-rejection-or-wide-addresses",EncodingLimit);
            Group("eight-layout-combinations-prepared-and-actual-result-capacity",AllocationChecks);
            var result=new{groups,assertions,failures=0,native_unity="not_run",production_promoted=false};
            if(args.Length>0)File.WriteAllText(args[0],JsonSerializer.Serialize(result));
            Console.WriteLine("MICRO_TESTS "+JsonSerializer.Serialize(result));return 0;
        }
        catch(Exception e){Console.WriteLine(e);return 1;}
    }
}
