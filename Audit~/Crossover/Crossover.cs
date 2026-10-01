// Experimental measurement host. Production Runtime is never edited by this suite.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameplayTags;
#if MICRO
using Set = GameplayTags.Experiments.DirectArraySet;
using Layout = GameplayTags.Experiments.BitmapLayout;
#else
using Set = GameplayTags.RuntimeTagSet;
using Layout = GameplayTags.TagSetStorage;
#endif
internal static partial class Crossover
{
    static Set sink;
    static int[] arraySink;
    static int checksum;
    static long assertions;
    static readonly List<object> rows = new List<object>();
    static string variant;
    static Layout layout;
    static int round;
    static void Check(bool v, string message="check") { assertions++; if(!v)throw new Exception(message); }
    static TagRegistry Registry(int leaves)
    {
        var settings = UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        settings.ReplaceAll(Enumerable.Range(0,leaves).Select(i=>new GameplayTagDefinition("B.T"+i.ToString("D7"),"","Default",false,true)).ToList(),
            new List<GameplayTagRedirect>(),new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});
        return TagRegistry.Create(settings);
    }
    static Set Input(TagRegistry reg, int[] ids, int capacity=-1)
    {
        var s=new Set(reg,capacity<0?ids.Length:capacity,layout);
        foreach(int id in ids)s.AddTag(reg.GetTagAt(id));
        return s;
    }
    static void Verify(Set set,IEnumerable<int> expected)
    {
        var x=expected.Distinct().OrderBy(i=>i).ToArray();
        Check(set.Count==x.Length,"count");int at=0;
        foreach(var t in set) { Check(at<x.Length&&t.RuntimeIndex==x[at++],"members/order"); }
        Check(at==x.Length,"enum length");
    }
    static string Digest(int[] a,int[] b)
    {
        using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join(",",a)+"|"+string.Join(",",b)))).Replace("-","");
    }
    // A real public API caller; diagnostics disassemble this exact measured method.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int QueryLoop(Set set,RuntimeTag[] probes,int repeats)
    {
        int sum=0;
        for(int r=0;r<repeats;r++)for(int i=0;i<probes.Length;i++)sum+=set.HasTagExact(probes[i])?1:0;
        return sum;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int OperationLoop(Set a,Set b,Set sub,Set work,RuntimeTag[] tags,int op,int repeats)
    {
        switch(op)
        {
            case 0: for(int i=0;i<repeats;i++)sink=Set.Union(a,b,layout); break;
            case 1: for(int i=0;i<repeats;i++){var t=new Set(a);t.AppendTags(b);sink=t;} break;
            case 2: for(int i=0;i<repeats;i++){var t=new Set(a);t.RemoveTags(sub);sink=t;} break;
            case 3: for(int i=0;i<repeats;i++)Set.UnionInto(a,b,work); sink=work;break;
            case 4: for(int i=0;i<repeats;i++){work.CopyFrom(a);work.AppendTags(b);}sink=work;break;
            case 5: for(int i=0;i<repeats;i++){work.CopyFrom(a);work.RemoveTags(sub);}sink=work;break;
            case 6: for(int i=0;i<repeats;i++){var t=new Set(a.Registry,tags.Length,layout);foreach(var tag in tags)t.AddTag(tag);sink=t;}break;
            default: throw new ArgumentOutOfRangeException(nameof(op));
        }
        return sink.Count;
    }
    static void Measure(string stage,string op,int universe,int members,string distribution,int seed,string relation,string digest,
        Func<int,int> body,int units=1,bool zero=false,object details=null)
    {
        // Same calibration/settling procedure for every implementation; never delete slow samples.
        checksum^=body(1);long bytes0=GC.GetAllocatedBytesForCurrentThread();checksum^=body(1);
        long estimatedBytes=GC.GetAllocatedBytesForCurrentThread()-bytes0;
        int limit=estimatedBytes>0?(int)Math.Max(1,Math.Min(1<<20,(16L<<20)/estimatedBytes)):(1<<20);
        int iterations=1;long warm;
        do {long t=Stopwatch.GetTimestamp();checksum^=body(iterations);warm=Stopwatch.GetTimestamp()-t;
            if(warm>=Stopwatch.Frequency/1000||iterations>=limit)break;iterations=Math.Min(limit,iterations*2);
        }while(true);
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        checksum^=body(Math.Min(iterations,32));
        var ns=new double[7];var allocated=new double[7];var gc=new int[7][];var ms=new double[7];
        for(int s=0;s<7;s++)
        {
            int g0=GC.CollectionCount(0),g1=GC.CollectionCount(1),g2=GC.CollectionCount(2);
            long al=GC.GetAllocatedBytesForCurrentThread(),t=Stopwatch.GetTimestamp();
            checksum^=body(iterations);long elapsed=Stopwatch.GetTimestamp()-t;
            allocated[s]=(GC.GetAllocatedBytesForCurrentThread()-al)/(double)(iterations*units);
            ms[s]=elapsed*1000.0/Stopwatch.Frequency;ns[s]=elapsed*(1e9/Stopwatch.Frequency)/(iterations*(double)units);
            gc[s]=new[]{GC.CollectionCount(0)-g0,GC.CollectionCount(1)-g1,GC.CollectionCount(2)-g2};
            if(zero)Check(allocated[s]==0,"prepared allocation");
        }
        GC.KeepAlive(sink);GC.KeepAlive(arraySink);
        rows.Add(new{variant,round,stage,op,universe,members,distribution,seed,relation,digest,iterations,units,ns,allocated,gc,ms,details});
    }
    static int[] Sequence(int universe,int count,string distribution,int seed)
    {
        if(distribution=="contiguous")return Enumerable.Range(1+(universe-count)/3,count).ToArray();
        if(distribution=="clusters4")return Enumerable.Range(0,count).Select(i=>1+(i%4)*(universe/4)+i/4).ToArray();
        var values=Enumerable.Range(1,universe).ToArray();var rng=new Random(seed);
        for(int i=0;i<count;i++){int j=rng.Next(i,values.Length);int t=values[i];values[i]=values[j];values[j]=t;}
        return values.Take(count).ToArray();
    }
    static void QueryMatrix(bool smoke)
    {
        var reg=Registry(65536);int[] sizes=smoke?new[]{0,1,7,8,9,32,33,128}:new[]{0,1,2,3,4,5,7,8,9,15,16,17,31,32,33,63,64,65,128,256,512};
        foreach(int seed in smoke?new[]{20261001}:new[]{20261001,20261017})foreach(int n in sizes)
        {
            int[] ids=Sequence(65534,n,"scattered",seed).Select(x=>x+1).OrderBy(x=>x).ToArray();
            var a=Input(reg,ids,n+65);
            // Leave a removed key in unused capacity. Public probes must ignore it.
            a.AddTag(reg.GetTagAt(65536));a.RemoveTag(reg.GetTagAt(65536));
            foreach(string kind in new[]{"fixed_middle","random_hits","mixed","miss_high"})
            {
                var rng=new Random(seed+11);var probes=new RuntimeTag[256];
                for(int i=0;i<probes.Length;i++)
                {
                    int id=kind=="miss_high"?65536:kind=="fixed_middle"?(n==0?1:ids[n/2]):
                        kind=="random_hits"?(n==0?1:ids[rng.Next(n)]):(i%2==0&&n>0?ids[rng.Next(n)]:rng.Next(0,65537));
                    probes[i]=reg.GetTagAt(id);Check(a.HasTagExact(probes[i])==ids.Contains(id),"probe correctness");
                }
                Measure("query",kind,65536,n,"scattered",seed,"probes256",Digest(ids,probes.Select(t=>t.RuntimeIndex).ToArray()),r=>QueryLoop(a,probes,r),256,true,
                    new{width=Vector<int>.Count,vector=Vector.IsHardwareAccelerated,active=n,capacity=n+65});
            }
        }
    }
    static void SetMatrix(bool smoke)
    {
        string[] operations={"new_union","copy_append","copy_remove","union_into","copy_append_reuse","copy_remove_reuse","build"};
        foreach(int u in smoke?new[]{10000,262144}:new[]{10000,65536,262144})
        {
            var reg=Registry(u);
            foreach(int seed in smoke?new[]{20261001}:new[]{20261001,20261017})
            foreach(string distribution in new[]{"contiguous","clusters4","scattered"})
            foreach(int n in smoke?new[]{8,1024}:new[]{0,1,8,32,128,1024,4096})
            foreach(int overlap in smoke?new[]{50}:new[]{0,50,100})
            {
                var seq=Sequence(u,2*n,distribution,seed);var x=seq.Take(n).OrderBy(i=>i).ToArray();
                int k=n*overlap/100;var y=x.Take(k).Concat(seq.Skip(n).Take(n-k)).OrderBy(i=>i).ToArray();
                var sub=x.Where((_,i)=>i%2==0).ToArray();var a=Input(reg,x);var b=Input(reg,y);var c=Input(reg,sub);
                var work=new Set(reg,Math.Min(reg.Count,2*n),layout);var tags=x.Select(i=>reg.GetTagAt(i)).ToArray();
                string digest=Digest(x,y);int words16=x.Select(i=>i>>4).Distinct().Count();int spanWords=n==0?0:(x[n-1]>>6)-(x[0]>>6)+1;
                for(int op=0;op<operations.Length;op++)
                {
                    // Existing-half deletion is independent of union overlap; don't duplicate it three times.
                    if(overlap!=50&&(op==2||op==5||op==6))continue;
                    int selected=op;OperationLoop(a,b,c,work,tags,selected,1);
                    Verify(sink,op==2||op==5?x.Except(sub):op==6?x:x.Concat(y));Verify(a,x);Verify(b,y);
                    Measure("sets",operations[op],u,n,distribution,seed,overlap.ToString(),digest,
                        r=>OperationLoop(a,b,c,work,tags,selected,r),1,op>=3&&op<=5,
                        new{actualRegistry=reg.Count,words16,spanWords,inputBuffer=a.BufferBytes,outputBuffer=sink.BufferBytes,
                            copyPolicy=variant=="micro"?"preserve-record-capacity":"preserve-source-capacity"});
                    Verify(sink,op==2||op==5?x.Except(sub):op==6?x:x.Concat(y));
                }
            }
        }
    }
    static void Tests()
    {
        var reg=Registry(513);var rng=new Random(20261001);
        foreach(int n in Enumerable.Range(0,130))
        {
            var ids=Enumerable.Range(1,n).Select(x=>x*3).ToArray();var a=Input(reg,ids,200);
            a.AddTag(reg.GetTagAt(510));a.RemoveTag(reg.GetTagAt(510));
            for(int id=0;id<reg.Count;id++)Check(a.HasTagExact(reg.GetTagAt(id))==ids.Contains(id),"active prefix / SIMD tail");
            Check(!a.HasTagExact(default(RuntimeTag)),"default");
            var copy=new Set(a);copy.Clear();Verify(a,ids);
        }
        for(int trial=0;trial<1000;trial++)
        {
            var x=Enumerable.Range(1,128).Where(_=>rng.Next(3)==0).ToArray();var y=Enumerable.Range(1,128).Where(_=>rng.Next(3)==0).ToArray();
            var a=Input(reg,x);var b=Input(reg,y);var w=new Set(reg,256,layout);
            Set.UnionInto(a,b,w);Verify(w,x.Concat(y));Set.UnionInto(a,b,a);Verify(a,x.Concat(y));
            a=Input(reg,x);a.RemoveTags(b);Verify(a,x.Except(y));a=Input(reg,x);a.AppendTags(b);Verify(a,x.Concat(y));
            a=Input(reg,x);var independent=Set.Union(a,b,layout);independent.Clear();Verify(a,x);Verify(b,y);
        }
        var foreign=Registry(3);bool thrown=false;try{Input(reg,new[]{1}).HasTagExact(foreign.GetTagAt(1));}catch(ArgumentException){thrown=true;}Check(thrown,"foreign scope");
        long start=GC.GetAllocatedBytesForCurrentThread();var positive=new byte[128];long positiveBytes=GC.GetAllocatedBytesForCurrentThread()-start;GC.KeepAlive(positive);Check(positiveBytes>0,"positive control");
        Console.WriteLine(JsonSerializer.Serialize(new{test="Crossover",variant,assertions,failed=0,positiveBytes,vector=Vector.IsHardwareAccelerated,width=Vector<int>.Count}));
    }
    static int Main(string[] args)
    {
        variant=args[0];string stage=args[1],output=args[2];round=args.Length>3?int.Parse(args[3]):0;bool smoke=args.Length>4&&args[4]=="smoke";
#if MICRO
        layout=Layout.Micro;
#else
        layout=variant=="dense"?Layout.Dense:variant=="auto"?Layout.Auto:Layout.Sparse;
#endif
        if(stage=="tests"){Tests();return 0;}
        if(stage=="query")QueryMatrix(smoke);else if(stage=="sets")SetMatrix(smoke);else if(stage=="ratio")RatioMatrix(smoke);else if(stage=="inline")AllocationMatrix();else throw new ArgumentException(stage);
        File.WriteAllText(output,JsonSerializer.Serialize(new{variant,stage,round,rows,checksum,runtime=RuntimeInformation.FrameworkDescription,
            architecture=RuntimeInformation.ProcessArchitecture.ToString(),os=RuntimeInformation.OSDescription,
            note="Calibration targets 1 ms; 16 MiB per-sample allocation budget. All seven samples retained. Not historical protocol; no native Unity run."}));
        Console.WriteLine(variant+" "+stage+" rows="+rows.Count);
        return 0;
    }
}
