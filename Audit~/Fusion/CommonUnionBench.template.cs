// Same generic Action caller, allocation protocol and input objects for all variants.
// This is a diagnostic fixture; do not blend its timings with the historical fixture.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using GameplayTags;
using GameplayTags.Experiments;
internal static class CommonUnionBench
{
    static object sink;
    sealed class Subject { public string Name, Operation; public Action Action, Verify; }
    static IEnumerable<Subject> Make<T>(string name, TagRegistry registry, RuntimeTag[] x, RuntimeTag[] y,
        Func<TagRegistry,T> empty, Action<T,RuntimeTag> add, Func<T,T,T> union,
        Func<T,T> copy, Func<T,int> count, Func<T,int[]> values) where T:class
    {
        T a=empty(registry),b=empty(registry);
        foreach(var tag in x) add(a,tag); foreach(var tag in y) add(b,tag);
        int[] expectedX=x.Select(t=>t.Id).OrderBy(i=>i).ToArray();
        int[] expectedU=x.Concat(y).Select(t=>t.Id).Distinct().OrderBy(i=>i).ToArray();
        void Verify(int[] expected)
        {
            var result=(T)sink;
            if(ReferenceEquals(result,a)||ReferenceEquals(result,b)||count(result)!=expected.Length||!values(result).SequenceEqual(expected)) throw new Exception("Incorrect or borrowed result");
            if(!values(a).SequenceEqual(expectedX)||!values(b).SequenceEqual(y.Select(t=>t.Id).OrderBy(i=>i))) throw new Exception("Input changed");
        }
        yield return new Subject {Name=name,Operation="union_fresh",Action=()=>sink=union(a,b),Verify=()=>Verify(expectedU)};
        yield return new Subject {Name=name,Operation="copy_only",Action=()=>sink=copy(a),Verify=()=>Verify(expectedX)};
        yield return new Subject {Name=name,Operation="empty_dense",Action=()=>sink=empty(registry),Verify=()=>Verify(Array.Empty<int>())};
    }
    // Generated typed adapters below make all representations explicit.
@@ADAPTERS@@
    static void Main(string[] args)
    {
        int round=int.Parse(args[1]); var rows=new List<object>();
        string[] names=Enumerable.Range(0,10000).Select(i=>"Bench.G"+(i/64).ToString("D4")+".T"+i.ToString("D5")).ToArray();
        var settings=UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        settings.ReplaceAll(names.Select(n=>new GameplayTagDefinition(n,"","Default",false,true)).ToList(),new List<GameplayTagRedirect>(),new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});
        GameplayTagManager.Initialize(settings,true); var registry=GameplayTagManager.CurrentRegistry;
        foreach(string distribution in (round%2==0?new[]{"contiguous","scattered"}:new[]{"scattered","contiguous"}))
        {
            string[] sequence=(string[])names.Clone(); if(distribution=="scattered") {var r=new Random(20260920);for(int i=sequence.Length-1;i>0;i--){int j=r.Next(i+1);string t=sequence[i];sequence[i]=sequence[j];sequence[j]=t;}}
            RuntimeTag[] x=sequence.Take(1024).Select(registry.Resolve).ToArray(),y=sequence.Skip(512).Take(1024).Select(registry.Resolve).ToArray();
            string digest=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(",",x.Select(t=>t.Id))+"/"+string.Join(",",y.Select(t=>t.Id)))));
            var subjects=new List<Subject>();
@@SUBJECTS@@
            foreach(string protocol in new[]{"settle_per_case","settle_per_sample"})
            {
                var order=subjects.Skip(round%subjects.Count).Concat(subjects.Take(round%subjects.Count)).ToArray();
                if(round%2!=0) Array.Reverse(order);
                foreach(var s in order)
                {
                    for(int i=0;i<100;i++) s.Action(); s.Verify();
                    double[] ns=new double[7],alloc=new double[7];int[] g0=new int[7],g1=new int[7],g2=new int[7];
                    GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                    const int iterations=8192;
                    for(int sample=0;sample<7;sample++)
                    {
                        if(protocol=="settle_per_sample"){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
                        long before=GC.GetAllocatedBytesForCurrentThread();int c0=GC.CollectionCount(0),c1=GC.CollectionCount(1),c2=GC.CollectionCount(2);
                        long start=Stopwatch.GetTimestamp(); for(int i=0;i<iterations;i++) s.Action(); long elapsed=Stopwatch.GetTimestamp()-start;
                        alloc[sample]=(GC.GetAllocatedBytesForCurrentThread()-before)/(double)iterations;
                        ns[sample]=elapsed*(1e9/Stopwatch.Frequency)/iterations;
                        g0[sample]=GC.CollectionCount(0)-c0;g1[sample]=GC.CollectionCount(1)-c1;g2[sample]=GC.CollectionCount(2)-c2;
                    }
                    s.Verify();GC.KeepAlive(sink);
                    rows.Add(new{variant=s.Name,operation=s.Operation,distribution,protocol,iterations,ns,allocated_bytes=alloc,gen0=g0,gen1=g1,gen2=g2,inputDigest=digest});
                }
            }
        }
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{round,runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,backend=DenseFusion.Backend,rows},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("COMMON_UNION rows="+rows.Count+" round="+round+" backend="+DenseFusion.Backend);
    }
}
