using System;
using System.Collections.Generic;
using System.Linq;
using GameplayTags;
using PageExperiment;
using Set = PageExperiment.PagedBitmapSet;

internal static class PageTests
{
    private static long assertions;
    private static int groups;
    private static TagRegistry registry;
    private static void Check(bool condition,string message){assertions++;if(!condition)throw new Exception(message);}
    private static RuntimeTag Tag(int id)=>new RuntimeTag(registry,id);
    private static TagRegistry MakeRegistry(int n)
    {
        var settings=UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        var definitions=new List<GameplayTagDefinition>(n);
        for(int i=0;i<n;i++)definitions.Add(new GameplayTagDefinition("T"+i.ToString("D6"),"","Default",false,true));
        settings.ReplaceAll(definitions,new List<GameplayTagRedirect>(),new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});
        return TagRegistry.Create(settings);
    }
    private static Set Make(IEnumerable<int> ids,int capacity=0)
    {var s=new Set(registry,capacity);foreach(int id in ids)s.AddTag(Tag(id));return s;}
    private static void Verify(Set actual,IEnumerable<int> expected)
    {
        var reference=new HashSet<int>(expected);var found=new HashSet<int>();
        foreach(var tag in actual)
        {Check(ReferenceEquals(tag.Registry,registry),"scope in enumeration");Check(found.Add(tag.RuntimeIndex),"duplicate");Check(actual.HasTagExact(tag),"enumerated member absent");}
        Check(reference.SetEquals(found),"wrong explicit members");Check(actual.Count==reference.Count,"wrong Count");
        Check(actual.IsEmpty==(reference.Count==0),"empty flag");actual.CheckInvariants();assertions++;
    }
    private static void Throws<T>(Action action) where T:Exception
    {try{action();}catch(T){assertions++;return;}throw new Exception("Expected "+typeof(T).Name);}
    private static void Test(string name,Action body){body();groups++;Console.WriteLine("PASS "+name);}
    private static void Exhaustive()
    {
        int[] edge={0,63,64,4095,4096,registry.Count-1};
        for(int a=0;a<64;a++)for(int b=0;b<64;b++)
        {
            int[] x=edge.Where((_,i)=>(a&(1<<i))!=0).ToArray(),y=edge.Where((_,i)=>(b&(1<<i))!=0).ToArray();
            Set left=Make(x),right=Make(y),result=Make(new[]{999,4000,17000},12);
            int[] union=x.Union(y).ToArray(),intersection=x.Intersect(y).ToArray(),difference=x.Except(y).ToArray();
            Verify(Set.Union(left,right),union);
            Set.UnionInto(left,right,result);Verify(result,union);Verify(left,x);Verify(right,y);
            var l=new Set(left);Set.UnionInto(l,right,l);Verify(l,union);
            var r=new Set(right);Set.UnionInto(left,r,r);Verify(r,union);
            l=new Set(left);l.AppendTags(right);Verify(l,union);l.RemoveTags(right);Verify(l,difference);
            l=new Set(left);l.RemoveTags(right);Verify(l,difference);
            Set.IntersectionExactInto(left,right,result);Verify(result,intersection);
            l=new Set(left);Set.IntersectionExactInto(l,right,l);Verify(l,intersection);
            r=new Set(right);Set.IntersectionExactInto(left,r,r);Verify(r,intersection);
            var copy=new Set(left);copy.AddTag(Tag(12345));Verify(left,x);copy.Clear();Verify(left,x);
            result.CopyFrom(left);result.CopyFrom(result);Verify(result,x);
            result.AppendTags(result);Verify(result,x);result.RemoveTags(result);Verify(result,Array.Empty<int>());
        }
    }
    private static void Transitions()
    {
        var actual=new Set(registry);var expected=new HashSet<int>();
        int[] ids={0,1,62,63,64,65,127,128,255,256,511,512,4095,4096,32767,32768,65535,65536,131071,131072,registry.Count-1};
        for(int round=0;round<5;round++)
        {
            foreach(int id in ids){Check(actual.AddTag(Tag(id))==expected.Add(id),"add");Verify(actual,expected);}
            foreach(int id in ids.Reverse()){Check(actual.RemoveTag(Tag(id))==expected.Remove(id),"remove");Verify(actual,expected);}
        }
        actual.EnsureCapacity(128);long buffer=actual.BufferBytes;
        for(int round=0;round<40;round++)
        {
            actual.Clear();expected.Clear();
            for(int i=0;i<128;i++){int id=(round*8191+i*2039)%registry.Count;actual.AddTag(Tag(id));expected.Add(id);}
            Verify(actual,expected);Check(actual.BufferBytes==buffer,"reserved arbitrary placement grew");
            foreach(int id in expected.ToArray()){actual.RemoveTag(Tag(id));expected.Remove(id);}
            Verify(actual,expected);Check(actual.BufferBytes==buffer,"delete changed reserve");
        }
        actual=Make(ids);var reverse=Make(ids.Reverse());Verify(Set.Union(actual,reverse),ids);
        Set.IntersectionExactInto(actual,reverse,reverse);Verify(reverse,ids);
        var empty=new Set(registry);actual.CopyFrom(empty);Verify(actual,Array.Empty<int>());
    }
    private static void Randomized()
    {
        var random=new Random(20260930);var actual=new Set(registry,512);var expected=new HashSet<int>();
        for(int step=0;step<16000;step++)
        {
            int id=random.Next(registry.Count),op=random.Next(9);
            if(op<4){Check(actual.AddTag(Tag(id))==expected.Add(id),"random add");}
            else if(op<7){Check(actual.RemoveTag(Tag(id))==expected.Remove(id),"random remove");}
            else if(op==7&&expected.Count>0){int old=expected.ElementAt(random.Next(expected.Count));Check(actual.RemoveTag(Tag(old)),"existing random delete");expected.Remove(old);}
            else if(expected.Count>300){actual.Clear();expected.Clear();}
            if(step%37==0){Verify(actual,expected);var copy=new Set(actual);Verify(copy,expected);}
        }
        Verify(actual,expected);
        for(int round=0;round<128;round++)
        {
            int[] x=Enumerable.Range(0,random.Next(1,300)).Select(_=>random.Next(registry.Count)).Distinct().ToArray();
            int[] y=Enumerable.Range(0,random.Next(1,300)).Select(_=>random.Next(registry.Count)).Concat(x.Take(x.Length/3)).Distinct().ToArray();
            var a=Make(x);var b=Make(y);var c=Set.Union(a,b);Verify(c,x.Union(y));
            c.RemoveTags(a);Verify(c,y.Except(x));Verify(a,x);Verify(b,y);
        }
    }
    private static void Boundaries()
    {
        Throws<ArgumentNullException>(()=>new Set((TagRegistry)null));
        Throws<ArgumentNullException>(()=>new Set((Set)null));
        Throws<ArgumentOutOfRangeException>(()=>new Set(registry,-1));
        var a=Make(new[]{0,64,registry.Count-1});
        Check(!a.HasTagExact(default),"default query");Check(!a.AddTag(default),"default add");Check(!a.RemoveTag(default),"default remove");
        Throws<ArgumentNullException>(()=>a.CopyFrom(null));Throws<ArgumentNullException>(()=>a.AppendTags(null));
        var other=MakeRegistry(70);var b=new Set(other);b.AddTag(new RuntimeTag(other,0));
        Throws<ArgumentException>(()=>a.HasTagExact(new RuntimeTag(other,0)));
        Throws<ArgumentException>(()=>a.CopyFrom(b));Throws<ArgumentException>(()=>a.AppendTags(b));
        Throws<ArgumentException>(()=>a.RemoveTags(b));Throws<ArgumentException>(()=>Set.Union(a,b));
        Verify(a,new[]{0,64,registry.Count-1});
        var empty=new Set(TagRegistry.Create(null));empty.EnsureCapacity(1000);Check(empty.Count==0,"empty registry");empty.Clear();empty.CheckInvariants();
    }
    private static void Allocation()
    {
        var a=new Set(registry,256);var b=new Set(registry,256);var target=new Set(registry,512);
        void Cycle(int step)
        {
            a.Clear();b.Clear();
            for(int i=0;i<128;i++) {a.AddTag(Tag((step*8191+i*2039)%registry.Count));b.AddTag(Tag((step*8191+i*2017)%registry.Count));}
            Set.UnionInto(a,b,target);target.CopyFrom(a);target.AppendTags(b);target.RemoveTags(a);
            Set.IntersectionExactInto(a,b,target);
        }
        for(int i=0;i<500;i++)Cycle(i);
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)Cycle(i);long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(bytes==0,"prepared allocation "+bytes);target.CheckInvariants();
        before=GC.GetAllocatedBytesForCurrentThread();var positive=new byte[128];long positiveBytes=GC.GetAllocatedBytesForCurrentThread()-before;GC.KeepAlive(positive);
        Check(positiveBytes>=128,"allocation counter positive control");Console.WriteLine("PREPARED_BYTES="+bytes+" POSITIVE_BYTES="+positiveBytes);
    }
    public static int Main()
    {
        registry=MakeRegistry(270001);
        Test("scope-default-empty-and-input-boundaries",Boundaries);
        Test("inline-page-branch-and-free-slot-transitions",Transitions);
        Test("exhaustive-4096-set-pairs-ownership-and-aliases",Exhaustive);
        Test("fixed-seed-random-mutation-and-batches",Randomized);
        Test("arbitrary-placement-prepared-allocation-positive-control",Allocation);
        Console.WriteLine("PAGE_TESTS format="+Set.Label+" groups="+groups+" assertions="+assertions+" failed=0");return 0;
    }
}
