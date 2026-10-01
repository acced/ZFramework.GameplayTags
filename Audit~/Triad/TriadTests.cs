using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text.Json;
using GameplayTags;
using GameplayTags.Experiments;

internal static class TriadTests
{
    private static long assertions;
    private static object sink;
    private static readonly List<object> groups = new List<object>();
    private static void Equal(bool value, string label) { assertions++; if (!value) throw new Exception(label); }
    private static TagRegistry Registry(int n)
    {
        var settings = UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        var definitions = new List<GameplayTagDefinition>(n);
        for (int i=0;i<n;i++) definitions.Add(new GameplayTagDefinition("T"+i.ToString("D7"),"","Default",false,true));
        settings.ReplaceAll(definitions,new List<GameplayTagRedirect>(),new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});
        return TagRegistry.Create(settings);
    }
    private static int Pop(ulong x) { int n=0;while(x!=0){x&=x-1;n++;}return n; }
    private static void Run(string label, Action action)
    { long before=assertions; action(); groups.Add(new{name=label,assertions=assertions-before,failed=0}); Console.WriteLine("PASS "+label+" assertions="+(assertions-before)); }
    private static RobinMicroBitmapSet Make(TagRegistry r,IEnumerable<int> values,int capacity=0)
    { var s=new RobinMicroBitmapSet(r,capacity);foreach(int id in values)s.AddTag(r.GetTagAt(id));return s; }
    private static void Check(RobinMicroBitmapSet set,IEnumerable<int> expected)
    {
        set.AssertInvariants(); var goal=new HashSet<int>(expected); var actual=new HashSet<int>();
        foreach(var t in set){Equal(actual.Add(t.RuntimeIndex),"duplicate enumeration");Equal(ReferenceEquals(t.Registry,set.Registry),"owner");}
        Equal(set.Count==goal.Count,"count");Equal(actual.SetEquals(goal),"membership");
        foreach(int id in goal)Equal(set.HasTagExact(set.Registry.GetTagAt(id)),"probe path");
    }
    private static void Scope(TagRegistry r)
    {
        var s=new RobinMicroBitmapSet(r); Equal(!s.HasTagExact(default),"default query");
        Equal(!s.AddTag(default)&&!s.RemoveTag(default),"default mutation");
        var other=Registry(16);s.AddTag(r.GetTagAt(15));bool threw=false;
        try{s.AddTag(other.GetTagAt(15));}catch(ArgumentException){threw=true;}Equal(threw,"foreign handle");
        threw=false;try{s.CopyFrom(new RobinMicroBitmapSet(other));}catch(ArgumentException){threw=true;}Equal(threw,"foreign copy");
        Check(s,new[]{15});s.CopyFrom(s);s.AppendTags(s);Check(s,new[]{15});s.RemoveTags(s);Check(s,Array.Empty<int>());
        threw=false;try{new RobinMicroBitmapSet(r,-1);}catch(ArgumentOutOfRangeException){threw=true;}Equal(threw,"negative capacity");
    }
    private static void Exhaustive(TagRegistry r)
    {
        int[] keys={0,15,16,63,1024,65535};
        for(int x=0;x<64;x++)for(int y=0;y<64;y++)
        {
            int[] a=keys.Where((_,i)=>(x&(1<<i))!=0).ToArray(),b=keys.Where((_,i)=>(y&(1<<i))!=0).ToArray();
            var left=Make(r,a);var right=Make(r,b);var fresh=RobinMicroBitmapSet.Union(left,right);
            Check(fresh,a.Union(b)); Check(left,a);Check(right,b);
            fresh.AddTag(r.GetTagAt(4000)); Check(left,a);Check(right,b);
            var copy=new RobinMicroBitmapSet(left);copy.AppendTags(right);Check(copy,a.Union(b));
            copy.CopyFrom(left);copy.RemoveTags(right);Check(copy,a.Except(b));
            var output=Make(r,new[]{6,700,65500},128);RobinMicroBitmapSet.UnionInto(left,right,output);Check(output,a.Union(b));
            var la=new RobinMicroBitmapSet(left);RobinMicroBitmapSet.UnionInto(la,right,la);Check(la,a.Union(b));
            var ra=new RobinMicroBitmapSet(right);RobinMicroBitmapSet.UnionInto(left,ra,ra);Check(ra,a.Union(b));
            left.Clear();Check(left,Array.Empty<int>());Check(right,b);
        }
    }
    private static void Collisions(TagRegistry r)
    {
        // 16 slots, a wraparound cluster starting at slot 14; no instrumented timed methods.
        var keys=new List<int>();
        for(uint k=0;k<4096&&keys.Count<10;k++)if((unchecked(k*2654435769U)>>28)==14)keys.Add((int)k*16);
        Equal(keys.Count==10,"collision construction");
        for(int erase=0;erase<keys.Count;erase++)
        {
            var set=Make(r,keys,10);var expected=new HashSet<int>(keys);
            Equal(set.SlotCount==16,"known capacity");
            set.RemoveTag(r.GetTagAt(keys[erase]));expected.Remove(keys[erase]);Check(set,expected);
            set.AddTag(r.GetTagAt(keys[erase]+3));expected.Add(keys[erase]+3);Check(set,expected);
            foreach(int id in keys){set.RemoveTag(r.GetTagAt(id));expected.Remove(id);Check(set,expected);}
        }
        var report=Make(r,keys,10);
        Console.WriteLine("COLLISION_PROBES "+JsonSerializer.Serialize(keys.Select(id=>report.ProbeCount(r.GetTagAt(id))).ToArray()));
    }
    private static void RandomChanges(TagRegistry r)
    {
        var random=new Random(20261001);var set=new RobinMicroBitmapSet(r,128);var expected=new HashSet<int>();
        for(int i=0;i<20000;i++)
        {
            int id=random.Next(r.Count);
            if((i&3)!=0)Equal(set.AddTag(r.GetTagAt(id))==expected.Add(id),"random add");
            else Equal(set.RemoveTag(r.GetTagAt(id))==expected.Remove(id),"random delete");
            if(i%71==0){Check(set,expected);var copy=new RobinMicroBitmapSet(set);Check(copy,expected);}
            if(i%997==0){set.Clear();expected.Clear();Check(set,expected);}
        }
        Check(set,expected);
        for(int n=0;n<140;n++)
        {var values=Enumerable.Range(0,n).Select(i=>i*31).ToArray();var a=Make(r,values,n);var b=new RobinMicroBitmapSet(a);b.AppendTags(a);Check(b,values);}
    }
    private static void Masks(TagRegistry r)
    {
        var s=new RobinMicroBitmapSet(r);var simd=new OrderedSimdBitmapSet(r,32,BitmapLayout.Micro);
        for(int mask=0;mask<65536;mask++)
        {
            s.Clear();simd.Clear();
            for(int bit=0;bit<16;bit++)if((mask&(1<<bit))!=0){s.AddTag(r.GetTagAt(32+bit));simd.AddTag(r.GetTagAt(32+bit));}
            Equal(s.Count==Pop((uint)mask),"all-mask count");
            for(int bit=0;bit<16;bit++){bool wanted=(mask&(1<<bit))!=0;Equal(s.HasTagExact(r.GetTagAt(32+bit))==wanted,"all-mask membership");Equal(simd.HasTagExact(r.GetTagAt(32+bit))==wanted,"simd all-mask");}
            s.AssertInvariants();
        }
        // Valid lanes only: stale reserved entries and vector tails must never become members.
        for(int n=0;n<65;n++)
        {
            simd.Clear();var goal=new HashSet<int>();for(int i=0;i<n;i++){simd.AddTag(r.GetTagAt(i*19));goal.Add(i*19);}
            for(int i=0;i<1300;i++)Equal(simd.HasTagExact(r.GetTagAt(i))==goal.Contains(i),"simd lane/tail/stale");
            for(int i=0;i<n;i+=2){simd.RemoveTag(r.GetTagAt(i*19));goal.Remove(i*19);}
            for(int i=0;i<1300;i++)Equal(simd.HasTagExact(r.GetTagAt(i))==goal.Contains(i),"simd deletion tail");
        }
    }
    private static void Prepared(TagRegistry r)
    {
        var a=new RobinMicroBitmapSet(r,128);var b=new RobinMicroBitmapSet(r,128);var o=new RobinMicroBitmapSet(r,256);
        var tags=Enumerable.Range(0,512).Select(i=>r.GetTagAt((i*127)%r.Count)).ToArray();
        Action work=()=>{for(int t=0;t<1000;t++){a.Clear();b.Clear();for(int i=0;i<128;i++){a.AddTag(tags[(i+t)%256]);b.AddTag(tags[256+(i+t)%256]);}RobinMicroBitmapSet.UnionInto(a,b,o);o.CopyFrom(a);o.AppendTags(b);o.RemoveTags(b);}};
        work();GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        long before=GC.GetAllocatedBytesForCurrentThread();work();long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Equal(bytes==0,"prepared Robin allocation");before=GC.GetAllocatedBytesForCurrentThread();sink=new byte[128];long positive=GC.GetAllocatedBytesForCurrentThread()-before;
        Equal(positive>0,"allocation positive control");Console.WriteLine("ROBIN_ALLOCATION bytes="+bytes+" positive="+positive);
    }
    private static void Dense()
    {
        var rng=new Random(3102026);
        foreach(int n in Enumerable.Range(0,70).Concat(new[]{127,128,129,158,159,160,1040,1041,4160,4161}))
        for(int pattern=0;pattern<4;pattern++)
        {
            var a=new ulong[n];var b=new ulong[n];
            for(int i=0;i<n;i++){a[i]=pattern==0?0:pattern==1?ulong.MaxValue:pattern==2?0xAAAAAAAAAAAAAAAAUL:((ulong)(uint)rng.Next()<<32)|(uint)rng.Next();b[i]=pattern==1?0x5555555555555555UL:((ulong)(uint)rng.Next()<<32)|(uint)rng.Next();}
            for(int op=0;op<3;op++)
            {
                ulong[] goal=new ulong[n];int count=0;
                for(int i=0;i<n;i++){goal[i]=op==0?a[i]|b[i]:op==1?a[i]&b[i]:a[i]&~b[i];count+=Pop(goal[i]);}
                for(int kernel=0;kernel<4;kernel++)for(int alias=0;alias<3;alias++)
                {
                    var aa=(ulong[])a.Clone();var bb=(ulong[])b.Clone();var o=alias==0?new ulong[n]:alias==1?aa:bb;
                    if(alias==0)Array.Fill(o,ulong.MaxValue);
                    int got=DenseVectorKernels.Run(aa,bb,o,op,kernel);Equal(got==count,"dense count");
                    for(int i=0;i<n;i++)Equal(o[i]==goal[i],"dense output/alias/tail");
                }
            }
        }
        var x=new ulong[159];var y=new ulong[159];var z=new ulong[159];Array.Fill(x,0x0123456789ABCDEFUL);Array.Fill(y,0xAAAAAAAAAAAAAAAAUL);
        for(int k=0;k<4;k++)
        {
            for(int t=0;t<1000;t++)DenseVectorKernels.Run(x,y,z,0,k);
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();long start=GC.GetAllocatedBytesForCurrentThread();
            for(int t=0;t<10000;t++)DenseVectorKernels.Run(x,y,z,0,k);
            long bytes=GC.GetAllocatedBytesForCurrentThread()-start;Equal(bytes==0,"dense allocation");
            Console.WriteLine("DENSE_ALLOCATION kernel="+k+" bytes="+bytes);
        }
    }
    public static int Main(string[] args)
    {
        Console.WriteLine("FEATURES "+JsonSerializer.Serialize(new{runtime=RuntimeInformation.FrameworkDescription,arch=RuntimeInformation.ProcessArchitecture.ToString(),vector=Vector.IsHardwareAccelerated,lanes=Vector<uint>.Count,avx2=Avx2.IsSupported,popcnt=Popcnt.X64.IsSupported}));
        var r=Registry(65536);
        Run("scope-and-empty",()=>Scope(r));Run("4096-set-pairs",()=>Exhaustive(r));Run("wraparound-backshift-clusters",()=>Collisions(r));
        Run("random-churn-and-capacity-boundaries",()=>RandomChanges(r));Run("all-16bit-masks-and-SIMD-tail",()=>Masks(r));
        Run("arbitrary-placement-prepared-allocation",()=>Prepared(r));Run("dense-fused-output-count-alias-tails",Dense);
        var result=new{groups,assertions,failed=0,native_unity="not_run",vector=Vector.IsHardwareAccelerated,avx2=Avx2.IsSupported};
        File.WriteAllText(args[0],JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("TRIAD_TESTS "+JsonSerializer.Serialize(result));return 0;
    }
}
