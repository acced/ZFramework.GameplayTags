// Complete-operation bulk-construction benchmark. No production Runtime changes.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameplayTags;
#if BULK_MICRO
using Set = GameplayTags.Experiments.DirectArraySet;
using Layout = GameplayTags.Experiments.BitmapLayout;
#else
using Set = GameplayTags.RuntimeTagSet;
using Layout = GameplayTags.TagSetStorage;
#endif
internal static partial class BulkProbe
{
    static Set sink;
    static int checksum;
    static long assertions;
    static Layout layout;
    static string label;
    static int round;
    static readonly List<object> rows = new List<object>();
    static void Check(bool value, string message = "check")
    { assertions++; if (!value) throw new Exception(message); }
    static void Throws<T>(Action body) where T : Exception
    { assertions++; try { body(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static TagRegistry Registry(int leaves)
    {
        var settings = UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        settings.ReplaceAll(Enumerable.Range(0,leaves).Select(i => new GameplayTagDefinition("B.T"+i.ToString("D7"),"","Default",false,true)).ToList(),
            new List<GameplayTagRedirect>(),new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});
        return TagRegistry.Create(settings);
    }
    static RuntimeTag[] Tags(TagRegistry registry, IEnumerable<int> ids)
        => ids.Select(registry.GetTagAt).ToArray();
    static Set Legacy(TagRegistry registry, RuntimeTag[] tags, int capacity = 0)
    {
        var result = new Set(registry,Math.Max(capacity,tags.Length),layout);
        for (int i = 0; i < tags.Length; i++) result.AddTag(tags[i]);
        return result;
    }
    static void Verify(Set set, IEnumerable<int> expected)
    {
        int[] ids = expected.Distinct().OrderBy(x => x).ToArray();
        Check(set.Count == ids.Length,"Count"); int at = 0;
        foreach (RuntimeTag tag in set)
        {
            Check(ReferenceEquals(tag.Registry,set.Registry),"owner");
            Check(at < ids.Length && ids[at++] == tag.RuntimeIndex,"ordered members");
        }
        Check(at == ids.Length,"enumerated length");
    }
    static string Digest(IEnumerable<int> ids)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join(",",ids)))).Replace("-","");
    }
    static int[] Sequence(int universe,int count,string distribution,int seed)
    {
        if (distribution == "contiguous") return Enumerable.Range(1+(universe-count)/3,count).ToArray();
        if (distribution == "clusters4") return Enumerable.Range(0,count).Select(i=>1+(i%4)*(universe/4)+i/4).ToArray();
        int[] values = Enumerable.Range(1,universe).ToArray(); var random = new Random(seed);
        for (int i=0;i<count;i++) { int j=random.Next(i,values.Length); int t=values[i];values[i]=values[j];values[j]=t; }
        return values.Take(count).ToArray();
    }
    static RuntimeTag[] Shuffle(RuntimeTag[] input,int seed)
    {
        var values=(RuntimeTag[])input.Clone();var random=new Random(seed);
        for(int i=values.Length-1;i>0;i--){int j=random.Next(i+1);RuntimeTag t=values[i];values[i]=values[j];values[j]=t;}
        return values;
    }

    // Exact measured caller. Branches choose a whole operation, not an inner per-ID delegate.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int ConstructionLoop(TagRegistry registry,RuntimeTag[] input,Set peer,Set work,int operation,bool bulk,int repeats)
    {
        switch(operation)
        {
            case 0:
                if(bulk)for(int i=0;i<repeats;i++)sink=Set.FromSortedUnique(registry,input,0,layout);
                else for(int i=0;i<repeats;i++)sink=Legacy(registry,input);
                break;
            case 1:
                if(bulk)for(int i=0;i<repeats;i++)sink=Set.FromUnordered(registry,input,0,layout);
                else for(int i=0;i<repeats;i++)sink=Legacy(registry,input);
                break;
            case 2:
                if(bulk)for(int i=0;i<repeats;i++)work.ResetFromSortedUnique(input);
                else for(int i=0;i<repeats;i++){work.Clear();for(int j=0;j<input.Length;j++)work.AddTag(input[j]);}
                sink=work;break;
            case 3:
            case 4:
                int uses=operation==3?1:16;
                for(int i=0;i<repeats;i++)
                {
                    var a=bulk?Set.FromSortedUnique(registry,input,0,layout):Legacy(registry,input);
                    for(int j=0;j<uses;j++)sink=Set.Union(a,peer,layout);
                }
                break;
            default:throw new ArgumentOutOfRangeException(nameof(operation));
        }
        return sink.Count;
    }
    static void Measure(int universe,int members,string distribution,int seed,string order,string operation,
        string strategy,string digest,Func<int,int> body,bool prepared,object shape)
    {
        checksum^=body(1);long startBytes=GC.GetAllocatedBytesForCurrentThread();checksum^=body(1);
        long bytes=GC.GetAllocatedBytesForCurrentThread()-startBytes;
        int limit=bytes>0?(int)Math.Max(1,Math.Min(1<<20,(16L<<20)/bytes)):(1<<20);
        int iterations=1;
        for(;;)
        {
            long t=Stopwatch.GetTimestamp();checksum^=body(iterations);long elapsed=Stopwatch.GetTimestamp()-t;
            if(elapsed>=Stopwatch.Frequency/1000||iterations>=limit)break;
            iterations=Math.Min(limit,iterations*2);
        }
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();checksum^=body(Math.Min(iterations,32));
        var ns=new double[7];var allocated=new double[7];var gc=new int[7][];var ms=new double[7];
        for(int s=0;s<7;s++)
        {
            int g0=GC.CollectionCount(0),g1=GC.CollectionCount(1),g2=GC.CollectionCount(2);
            long a=GC.GetAllocatedBytesForCurrentThread(),t=Stopwatch.GetTimestamp();checksum^=body(iterations);
            long elapsed=Stopwatch.GetTimestamp()-t;
            allocated[s]=(GC.GetAllocatedBytesForCurrentThread()-a)/(double)iterations;
            ns[s]=elapsed*(1e9/Stopwatch.Frequency)/iterations;ms[s]=elapsed*1000.0/Stopwatch.Frequency;
            gc[s]=new[]{GC.CollectionCount(0)-g0,GC.CollectionCount(1)-g1,GC.CollectionCount(2)-g2};
            if(prepared)Check(allocated[s]==0,"prepared allocation");
        }
        GC.KeepAlive(sink);
        rows.Add(new{label,round,universe,members,distribution,seed,order,operation,strategy,digest,iterations,
            ns,allocated,gc,ms,shape,resultCount=sink.Count,resultBuffer=sink.BufferBytes});
    }
    static void Matrix(bool smoke)
    {
        foreach(int u in smoke?new[]{10000}:new[]{10000,65536,262144})
        {
            var reg=Registry(u);
            foreach(int seed in smoke?new[]{20261002}:new[]{20261002,20261019})
            foreach(string distribution in new[]{"contiguous","clusters4","scattered"})
            foreach(int n in smoke?new[]{0,8,128}:new[]{0,1,8,32,128,1024,4096})
            {
                int[] sequence=Sequence(u,2*n,distribution,seed);
                int[] x=sequence.Take(n).OrderBy(i=>i).ToArray();
                int[] y=x.Take(n/2).Concat(sequence.Skip(n).Take(n-n/2)).OrderBy(i=>i).ToArray();
                RuntimeTag[] sorted=Tags(reg,x);Set peer=Legacy(reg,Tags(reg,y));
                var variants=new[]{sorted,sorted.Reverse().ToArray(),Shuffle(sorted,seed+97),Shuffle(sorted.Concat(sorted).ToArray(),seed+311)};
                string[] orders={"sorted_unique","reverse_unique","shuffled_unique","shuffled_duplicates"};
                int runs=0;for(int i=0;i<x.Length;i++)if(i==0||x[i]!=x[i-1]+1)runs++;
                int blocks=x.Select(i=>i>>4).Distinct().Count();
                for(int inputKind=0;inputKind<variants.Length;inputKind++)
                {
                    RuntimeTag[] input=variants[inputKind];string order=orders[inputKind];
                    int[] operations=inputKind==0?new[]{0,2,3,4}:new[]{1};
                    Set work=Legacy(reg,Tags(reg,y),input.Length);
                    string digest=Digest(input.Select(t=>t.RuntimeIndex));
                    foreach(int op in operations)
                    {
                        string operation=op<=1?"fresh_build":op==2?"prepared_reset":op==3?"build_plus_1_union":"build_plus_16_unions";
                        var shape=new{registryCount=reg.Count,words=reg.WordCount,blocks16=blocks,runs,
                            inputCount=input.Length,capacity=Math.Min(reg.Count,input.Length),peerDigest=Digest(y),
                            uses=op==3?1:op==4?16:0,rule="forced layout; no selector"};
                        // Rotate strategy order by seed/size/round, not by observed timing.
                        foreach(bool bulk in ((seed+n+round)&1)==0?new[]{false,true}:new[]{true,false})
                        {
                            ConstructionLoop(reg,input,peer,work,op,bulk,1);
                            Verify(sink,op>=3?x.Concat(y):x);Verify(peer,y);
                            int selected=op;bool selectedBulk=bulk;
                            Measure(u,n,distribution,seed,order,operation,bulk?"bulk":"loop",digest,
                                r=>ConstructionLoop(reg,input,peer,work,selected,selectedBulk,r),op==2,shape);
                            Verify(sink,op>=3?x.Concat(y):x);
                            Check(Digest(input.Select(t=>t.RuntimeIndex))==digest,"caller input mutated");
                        }
                    }
                }
            }
        }
    }
    static int Main(string[] args)
    {
        label=args[0];string stage=args[1];round=args.Length>3?int.Parse(args[3]):0;
#if BULK_MICRO
        layout=Layout.Micro;
#else
        layout=label.StartsWith("dense",StringComparison.Ordinal)?Layout.Dense:Layout.Sparse;
#endif
        if(stage=="tests")
        {
            var result=Tests();File.WriteAllText(args[2],JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("BULK_TESTS "+JsonSerializer.Serialize(result));return 0;
        }
        Matrix(args.Length>4&&args[4]=="smoke");
        File.WriteAllText(args[2],JsonSerializer.Serialize(new{label,round,runtime=RuntimeInformation.FrameworkDescription,
            arch=RuntimeInformation.ProcessArchitecture.ToString(),os=RuntimeInformation.OSDescription,checksum,rows,
            contract="Strict sorted handles versus valid repeated Add; unordered includes private scratch, sorting and deduplication. Same capacity and ownership. Build-plus-unions includes source preparation and unchanged public unions, not the standalone four-operation benchmark.",
            limits="Linux .NET, not Unity/IL2CPP. Calibration and all samples retained; no adaptive rule or universal performance approval."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("BULK_MEASURED "+label+" rows="+rows.Count+" round="+round);return 0;
    }
}
