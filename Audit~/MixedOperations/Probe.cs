// Whole public operations, same resolved inputs and exact result contracts.
using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameplayTags;
using GameplayTags.Experiments;
using Set = GameplayTags.Experiments.DirectArraySet;
using Layout = GameplayTags.Experiments.BitmapLayout;

internal static class MixedProbe
{
    static Set sink;
    static int checksum;
    static long assertions;
    static string label;
    static int round;
    static readonly List<object> rows = new List<object>();
    static readonly Layout[] layouts = { Layout.Micro, Layout.Dense };
    static void Check(bool ok, string message)
    { assertions++; if (!ok) throw new Exception(message); }
    static TagRegistry Registry(int size)
    {
        var s = UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        var definitions = Enumerable.Range(0, size).Select(i =>
            new GameplayTagDefinition("T"+i.ToString("D7"),"","Default",false,true)).ToList();
        s.ReplaceAll(definitions,new List<GameplayTagRedirect>(),
            new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});
        return TagRegistry.Create(s);
    }
    static RuntimeTag[] Tags(TagRegistry r, IEnumerable<int> ids) => ids.Select(r.GetTagAt).ToArray();
    static Set Make(TagRegistry r, int[] ids, Layout mode, int capacity=0) =>
        Set.FromSortedUnique(r, Tags(r,ids),capacity,mode);
    static int[] Snapshot(Set s)
    { var list=new List<int>(); foreach(var t in s) list.Add(t.RuntimeIndex); return list.ToArray(); }
    static void Verify(Set s, IEnumerable<int> expected)
    {
        int[] ids=expected.Distinct().OrderBy(x=>x).ToArray();
        s.AssertInvariants(); Check(s.Count==ids.Length,"count");
        int at=0;
        foreach(var tag in s)
        {
            Check(ReferenceEquals(tag.Registry,s.Registry),"owner");
            Check(at<ids.Length && tag.RuntimeIndex==ids[at++],"ordered enumeration");
            Check(s.HasTagExact(tag),"enumerated member not found");
        }
        Check(at==ids.Length,"enumeration length");
        var iterator=s.GetEnumerator(); int sum=0;
        while(iterator.MoveNext()) sum++;
        Check(sum==s.Count && !iterator.MoveNext() && !iterator.MoveNext(),"exhausted cursor");
    }
    static int[] Sequence(int u,int count,string dist,int seed)
    {
        if(dist=="contiguous") return Enumerable.Range((u-count)/3,count).ToArray();
        if(dist=="clusters4") return Enumerable.Range(0,count).Select(i=>(i%4)*(u/4)+i/4).ToArray();
        var random=new Random(seed); var values=new HashSet<int>();
        while(values.Count<count) values.Add(random.Next(u));
        return values.ToArray();
    }
    static string Digest(IEnumerable<int> ids)
    {
        using(var sha=SHA256.Create())
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join(",",ids))));
    }
    static object Tests()
    {
        var random=new Random(2026100207);
        foreach(int u in new[]{0,1,15,16,17,63,64,65,513})
        {
            var r=Registry(u);
            for(int trial=0;trial<48;trial++)
            {
                int[] x=Enumerable.Range(0,u).Where(_=>random.Next(4)==0).ToArray();
                int[] y=Enumerable.Range(0,u).Where(_=>random.Next(3)==0).ToArray();
                if(trial==0) { x=Array.Empty<int>(); y=Array.Empty<int>(); }
                if(trial==1) { x=Enumerable.Range(0,u).ToArray(); y=x.Where(i=>(i&1)==0).ToArray(); }
                if(trial==2 && u>0) { x=new[]{u-1}; y=new[]{0}; }
                foreach(var lm in layouts) foreach(var rm in layouts)
                {
                    var a=Make(r,x,lm,u); var b=Make(r,y,rm,u);
                    foreach(var dest in layouts)
                    {
                        var result=Make(r,Enumerable.Range(0,u).Where(i=>(i&1)==0).ToArray(),dest,u);
                        long capacity=result.BufferBytes;
                        Set.UnionInto(a,b,result); Verify(result,x.Concat(y));
                        Check(result.BufferBytes==capacity,"sufficient output grew");
                        Verify(Set.Union(a,b,dest),x.Concat(y));
                        Verify(Set.Union(b,a,dest),x.Concat(y));
                        Set.UnionInto(a,a,result); Verify(result,x);
                        result.CopyFrom(b); Verify(result,y);
                    }
                    var left=new Set(a); Set.UnionInto(left,b,left); Verify(left,x.Concat(y));
                    var right=new Set(b); Set.UnionInto(a,right,right); Verify(right,x.Concat(y));
                    var copy=new Set(a); copy.AppendTags(b); Verify(copy,x.Concat(y));
                    copy=new Set(a); copy.RemoveTags(b); Verify(copy,x.Except(y));
                    var independent=Set.Union(a,b,lm); independent.Clear(); Verify(a,x); Verify(b,y);
                    var it=a.GetEnumerator();
                    if(it.MoveNext())
                    {
                        var fork=it;
                        while(it.MoveNext()) { Check(fork.MoveNext(),"copied iterator ended"); Check(it.Current.Equals(fork.Current),"copied iterator differs"); }
                        Check(!fork.MoveNext(),"copied iterator extra");
                    }
                }
            }
        }
        // Every 16-bit mask at a high word, with empty words on either side and a tail.
        var large=Registry(262145);
        foreach(var mode in layouts)
        {
            var s=new Set(large,32,mode);
            for(int mask=0;mask<65536;mask++)
            {
                s.Clear(); var expected=new List<int>{262144}; s.AddTag(large.GetTagAt(262144));
                for(int b=0;b<16;b++) if((mask&(1<<b))!=0)
                { expected.Add(65520+b); s.AddTag(large.GetTagAt(65520+b)); }
                Verify(s,expected);
            }
        }
        var r2=Registry(10000); var allocation=new List<object>();
        foreach(var lm in layouts) foreach(var rm in layouts) foreach(var dest in layouts)
        {
            int[] x={0,15,16,63,64,9999}, y={1,15,31,64,511,9998};
            var a=Make(r2,x,lm,64); var b=Make(r2,y,rm,64); var output=new Set(r2,64,dest);
            for(int i=0;i<100;i++) Set.UnionInto(a,b,output);
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<1000;i++) { Set.UnionInto(a,b,output); checksum^=EnumerateLoop(output,1); }
            long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
            Check(bytes==0,"prepared operation allocated"); Verify(output,x.Concat(y));
            allocation.Add(new{left=lm.ToString(),right=rm.ToString(),output=dest.ToString(),bytes});
        }
        long start=GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(new byte[128]);
        long positive=GC.GetAllocatedBytesForCurrentThread()-start;
        Check(positive>0,"allocation control invalid");
        var sizeMethod=typeof(Unsafe).GetMethod("SizeOf").MakeGenericMethod(
            typeof(Set).GetNestedType("Records",BindingFlags.NonPublic));
        int cursorBytes=(int)sizeMethod.Invoke(null,null);
        return new{assertions,failures=0,positiveAllocationBytes=positive,allocation,
            cursorBytes,enumeratorBytes=Unsafe.SizeOf<Set.Enumerator>(),nativeUnity="not_run"};
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int UnionLoop(Set a,Set b,Set output,Layout mode,bool fresh,int repeats)
    {
        for(int i=0;i<repeats;i++)
        { if(fresh) sink=Set.Union(a,b,mode); else { Set.UnionInto(a,b,output); sink=output; } }
        return sink.Count;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int ModifyLoop(Set a,Set b,Set output,bool remove,bool fresh,int repeats)
    {
        for(int i=0;i<repeats;i++)
        {
            Set result;
            if(fresh) result=new Set(a); else { output.CopyFrom(a); result=output; }
            if(remove) result.RemoveTags(b); else result.AppendTags(b);
            sink=result;
        }
        return sink.Count;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int EnumerateLoop(Set a,int repeats)
    {
        int total=0;
        for(int i=0;i<repeats;i++) foreach(var t in a) total=unchecked(total+t.RuntimeIndex+1);
        return total;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int QueryLoop(Set a,RuntimeTag tag,int repeats)
    { int total=0; for(int i=0;i<repeats;i++) total+=a.HasTagExact(tag)?1:0; return total; }

    // Input origin. Both operands' preparation is paid; output mode is explicit, not Auto.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int PairLoop(TagRegistry r,RuntimeTag[] x,RuntimeTag[] y,Layout from,Layout to,
        int strategy,int uses,int repeats)
    {
        for(int i=0;i<repeats;i++)
        {
            Set a,b;
            if(strategy==3) { a=Set.FromSortedUnique(r,x,0,to); b=Set.FromSortedUnique(r,y,0,to); }
            else
            {
                a=Set.FromSortedUnique(r,x,0,from); b=Set.FromSortedUnique(r,y,0,from);
                if(strategy>=1) a=a.CopyAsStorage(to);
                if(strategy==2) b=b.CopyAsStorage(to);
            }
            sink=a;
            for(int j=0;j<uses;j++) sink=Set.Union(a,b,strategy==0?from:to);
            checksum^=b.Count; // Both prepared operands remain observable even when uses==0.
        }
        return sink.Count;
    }
    static void Measure(object shape,string operation,string output,Func<int,int> action,bool zero)
    {
        checksum^=action(16);
        long startBytes=GC.GetAllocatedBytesForCurrentThread(); checksum^=action(1);
        long bpo=GC.GetAllocatedBytesForCurrentThread()-startBytes;
        int cap=bpo==0 ? 1<<22 : (int)Math.Max(1,Math.Min(1<<22,(8L<<20)/bpo));
        int iterations=1;
        while(true)
        {
            long start=Stopwatch.GetTimestamp(); checksum^=action(iterations);
            if(Stopwatch.GetTimestamp()-start>=Stopwatch.Frequency/2000 || iterations>=cap) break;
            iterations=Math.Min(cap,iterations*2);
        }
        var ns=new double[7]; var bytes=new long[7]; var gc=new int[7][];
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        for(int i=0;i<7;i++)
        {
            gc[i]=new int[3]; int a0=GC.CollectionCount(0),a1=GC.CollectionCount(1),a2=GC.CollectionCount(2);
            long before=GC.GetAllocatedBytesForCurrentThread(), t=Stopwatch.GetTimestamp();
            checksum^=action(iterations);
            long elapsed=Stopwatch.GetTimestamp()-t;
            bytes[i]=GC.GetAllocatedBytesForCurrentThread()-before;
            ns[i]=elapsed*(1e9/Stopwatch.Frequency)/iterations;
            gc[i][0]=GC.CollectionCount(0)-a0;gc[i][1]=GC.CollectionCount(1)-a1;gc[i][2]=GC.CollectionCount(2)-a2;
            Check(bytes[i]==bpo*iterations,"allocation amount changed");
            if(zero) Check(bytes[i]==0,"zero contract violated");
        }
        rows.Add(new{label,round,shape,operation,output,iterations,ns,allocated=bytes,
            bytesPerOp=bpo,gc,resultCount=sink==null?0:sink.Count,
            resultBuffer=sink==null?0:sink.BufferBytes});
    }
    static void Matrix(bool smoke)
    {
        foreach(int u in smoke?new[]{10000}:new[]{10000,262144})
        {
            var r=Registry(u);
            foreach(int seed in smoke?new[]{20261031}:new[]{20261031,20261113})
            foreach(string dist in smoke?new[]{"scattered"}:new[]{"contiguous","clusters4","scattered"})
            foreach(int n in smoke?new[]{0,8,128}:new[]{0,1,8,128,4096})
            {
                int[] seq=Sequence(u,2*n,dist,seed);
                int[] x=seq.Take(n).OrderBy(i=>i).ToArray();
                int[] y=x.Take(n/2).Concat(seq.Skip(n).Take(n-n/2)).OrderBy(i=>i).ToArray();
                foreach(var lm in layouts)
                {
                    var a=Make(r,x,lm,Math.Min(u,2*n+17));
                    int miss=Array.FindIndex(Enumerable.Range(0,u).ToArray(),v=>Array.BinarySearch(x,v)<0);
                    var shape0=new{u,n,seed,dist,left=lm.ToString(),right="-",digest=Digest(x),peerDigest="-",
                        words=(u+63)/64,blocks=x.Select(v=>v>>4).Distinct().Count()};
                    sink=a;
                    Measure(shape0,"enumerate",lm.ToString(),rep=>EnumerateLoop(a,rep),true);
                    Measure(shape0,"exact_hit",lm.ToString(),rep=>QueryLoop(a,n==0?default:r.GetTagAt(x[n/2]),rep),true);
                    Measure(shape0,"exact_miss",lm.ToString(),rep=>QueryLoop(a,r.GetTagAt(miss),rep),true);
                    foreach(var rm in layouts)
                    {
                        var b=Make(r,y,rm,Math.Min(u,2*n+17));
                        var shape=new{u,n,seed,dist,left=lm.ToString(),right=rm.ToString(),
                            digest=Digest(x),peerDigest=Digest(y),words=(u+63)/64,
                            blocks=x.Select(v=>v>>4).Distinct().Count()};
                        foreach(var dest in layouts)
                        {
                            var output=new Set(r,Math.Min(u,2*n+17),dest);
                            foreach(bool fresh in new[]{true,false})
                            {
                                UnionLoop(a,b,output,dest,fresh,1); Verify(sink,x.Concat(y));
                                Measure(shape,fresh?"fresh_union":"union_into",dest.ToString(),
                                    rep=>UnionLoop(a,b,output,dest,fresh,rep),!fresh);
                                Verify(sink,x.Concat(y));
                            }
                        }
                        var workset=new Set(r,Math.Min(u,2*n+17),lm);
                        int[] deleted=x.Where((_,i)=>(i&1)==0).ToArray();
                        var removal=Make(r,deleted,rm,Math.Min(u,2*n+17));
                        foreach(bool remove in new[]{false,true}) foreach(bool fresh in new[]{true,false})
                        {
                            var peer=remove?removal:b;
                            var opShape=new{u,n,seed,dist,left=lm.ToString(),right=rm.ToString(),
                                digest=Digest(x),peerDigest=Digest(remove?deleted:y),rightMembers=peer.Count,
                                words=(u+63)/64,blocks=x.Select(v=>v>>4).Distinct().Count()};
                            ModifyLoop(a,peer,workset,remove,fresh,1);
                            Verify(sink,remove?x.Except(deleted):x.Concat(y));
                            Measure(opShape,(fresh?"copy_":"reuse_copy_")+(remove?"remove":"append"),lm.ToString(),
                                rep=>ModifyLoop(a,peer,workset,remove,fresh,rep),!fresh);
                            Verify(sink,remove?x.Except(deleted):x.Concat(y));
                        }
                        Verify(a,x); Verify(b,y);
                    }
                }
            }
        }
    }
    static void Pairs(bool smoke)
    {
        foreach(int u in smoke?new[]{10000}:new[]{10000,262144})
        {
            var r=Registry(u);
            foreach(int n in smoke?new[]{8}:new[]{8,4096})
            foreach(string dist in new[]{"contiguous","scattered"})
            {
                var seq=Sequence(u,2*n,dist,20261123);
                int[] x=seq.Take(n).OrderBy(v=>v).ToArray(),y=x.Take(n/2).Concat(seq.Skip(n).Take(n/2)).OrderBy(v=>v).ToArray();
                var xt=Tags(r,x);var yt=Tags(r,y);
                foreach(var from in layouts)
                {
                    var to=from==Layout.Dense?Layout.Micro:Layout.Dense;
                    foreach(int uses in smoke?new[]{1}:new[]{0,1,16})
                    foreach(int strategy in new[]{0,1,2,3})
                    {
                        string name=new[]{"keep_pair","convert_one","convert_both","build_pair_target"}[strategy];
                        var shape=new{u,n,dist,uses,left=from.ToString(),right=to.ToString(),seed=20261123,
                            digest=Digest(x),peerDigest=Digest(y),protocol="input_pair_all_preparation_paid"};
                        PairLoop(r,xt,yt,from,to,strategy,uses,1);Verify(sink,uses==0?x:x.Concat(y));
                        Measure(shape,name,to.ToString(),rep=>PairLoop(r,xt,yt,from,to,strategy,uses,rep),false);
                        Verify(sink,uses==0?x:x.Concat(y));
                    }
                }
            }
        }
    }
    static int Main(string[] args)
    {
        label=args[0]; string stage=args[1]; round=int.Parse(args[3]);
        if(stage=="tests")
        {
            var result=Tests();File.WriteAllText(args[2],JsonSerializer.Serialize(result));
            Console.WriteLine("MIXED_TESTS "+JsonSerializer.Serialize(result));return 0;
        }
        bool smoke=args.Length>4 && args[4]=="smoke";
        if(stage=="pairs") Pairs(smoke); else Matrix(smoke);
        File.WriteAllText(args[2],JsonSerializer.Serialize(new{label,round,stage,
            runtime=RuntimeInformation.FrameworkDescription,arch=RuntimeInformation.ProcessArchitecture.ToString(),
            rows,checks=assertions,checksum}));
        Console.WriteLine("MIXED_MEASURED "+label+" stage="+stage+" rows="+rows.Count+" round="+round);
        return 0;
    }
}
