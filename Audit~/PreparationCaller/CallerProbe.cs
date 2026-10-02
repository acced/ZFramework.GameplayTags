// Diagnostic only: isolate the REAL public Reset call from other factory call sites.
// No runtime implementation changes; primary matrix remains authoritative and retained.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GameplayTags;
#if BULK_MICRO
using Set = GameplayTags.Experiments.DirectArraySet;
using Layout = GameplayTags.Experiments.BitmapLayout;
#else
using Set = GameplayTags.RuntimeTagSet;
using Layout = GameplayTags.TagSetStorage;
#endif
internal static class CallerProbe
{
    static int sink;
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int ResetLoop(Set target, RuntimeTag[] input, int repetitions)
    {
        for (int i = 0; i < repetitions; i++) target.ResetFromSortedUnique(input);
        return target.Count;
    }
    // Both candidates expose the allocation-free foreach pattern, not IEnumerable<T>.
    // This untimed assertion helper must not require changing their public interfaces.
    static bool Matches(Set target, int[] expected)
    {
        if (target.Count != expected.Length) return false;
        int at = 0;
        foreach (RuntimeTag tag in target)
        {
            if (at >= expected.Length || tag.RuntimeIndex != expected[at++]) return false;
        }
        return at == expected.Length;
    }
    static TagRegistry Registry(int leaves)
    {
        var settings = UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        settings.ReplaceAll(Enumerable.Range(0,leaves).Select(i=>new GameplayTagDefinition("C.T"+i.ToString("D7"),"","Default",false,true)).ToList(),
            new List<GameplayTagRedirect>(),new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});
        return TagRegistry.Create(settings);
    }
    static int Main(string[] args)
    {
        string label=args[0];int round=int.Parse(args[2]);var rows=new List<object>();long checks=0;
        foreach(int u in new[]{10000,262144})
        {
            var registry=Registry(u);
            foreach(int n in new[]{0,1,2,8,128,4096})
            foreach(bool scattered in new[]{false,true})
            {
                int[] ids=Enumerable.Range(0,n).Select(i=>scattered?1+(i*9973%u):1+i).OrderBy(i=>i).ToArray();
                RuntimeTag[] input=ids.Select(registry.GetTagAt).ToArray();
                var target=new Set(registry,n,(Layout)1);
                target.ResetFromSortedUnique(input);
                if(!Matches(target,ids))throw new Exception("Incorrect reset");
                checks++;
                // Rejected input must leave all old members intact.
                if(n>0)
                {
                    var invalid=(RuntimeTag[])input.Clone();invalid[n-1]=default(RuntimeTag);
                    bool threw=false;try{target.ResetFromSortedUnique(invalid);}catch(ArgumentException){threw=true;}
                    if(!threw||!Matches(target,ids))throw new Exception("Admission failed");
                    checks++;
                }
                sink^=ResetLoop(target,input,8192);
                int iterations=1;
                while(iterations<1<<22)
                {
                    long start=Stopwatch.GetTimestamp();sink^=ResetLoop(target,input,iterations);
                    if(Stopwatch.GetTimestamp()-start>=Stopwatch.Frequency/500)break;
                    iterations*=2;
                }
                GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                var samples=new double[7];var bytes=new long[7];
                for(int s=0;s<7;s++)
                {
                    long a=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
                    sink^=ResetLoop(target,input,iterations);
                    samples[s]=(Stopwatch.GetTimestamp()-start)*(1e9/Stopwatch.Frequency)/iterations;
                    bytes[s]=GC.GetAllocatedBytesForCurrentThread()-a;
                    if(bytes[s]!=0)throw new Exception("Prepared reset allocated");
                }
                if(!Matches(target,ids))throw new Exception("Posttiming membership");
                checks++;
                rows.Add(new{label,round,universe=u,members=n,scattered,iterations,ns=samples,allocated=bytes});
            }
        }
        File.WriteAllText(args[1],JsonSerializer.Serialize(new{label,round,checks,rows,sink,contract="isolated public Reset caller; diagnostic, not replacement for primary matrix"}));
        Console.WriteLine("ISOLATED_RESET "+label+" round="+round+" cases="+rows.Count+" checks="+checks);
        return 0;
    }
}
