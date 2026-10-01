using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text.Json;
using GameplayTags.Experiments;

internal static class DenseBench
{
    private static object sink;
    private static int checksum;
    public static int Main(string[] args)
    {
        string[] labels={"swar","hardware_popcnt","avx2_fused","avx2_two_pass"};
        var rows=new List<object>(); var random=new Random(20261001);
        foreach(int length in new[]{0,1,3,4,7,8,16,32,159,1041,4161,15625})
        foreach(string distribution in new[]{"clustered","random"})
        {
            var a=new ulong[length];var b=new ulong[length];
            for(int i=0;i<length;i++)
            {
                a[i]=distribution=="clustered"?(i<length/4?ulong.MaxValue:0):((ulong)(uint)random.Next()<<33)|((ulong)(uint)random.Next()<<2)|(uint)random.Next(4);
                b[i]=distribution=="clustered"?(i>=length/8&&i<3*length/8?ulong.MaxValue:0):((ulong)(uint)random.Next()<<33)|((ulong)(uint)random.Next()<<2)|(uint)random.Next(4);
            }
            for(int op=0;op<3;op++)foreach(string contract in new[]{"prepared","fresh","copy_mutate"})
            for(int kernel=0;kernel<4;kernel++)
            {
                var output=new ulong[length]; int expected=0;
                for(int i=0;i<length;i++)expected+=BitOperations.PopCount(op==0?a[i]|b[i]:op==1?a[i]&b[i]:a[i]&~b[i]);
                int engine=kernel,operation=op;
                Action action=()=>{
                    ulong[] result=contract=="prepared"?output:contract=="fresh"?new ulong[length]:(ulong[])a.Clone();
                    checksum=DenseVectorKernels.Run(contract=="copy_mutate"?result:a,b,result,operation,engine);sink=result;
                };
                action();if(checksum!=expected)throw new Exception("benchmark output count");
                ulong[] initial=(ulong[])sink;
                for(int i=0;i<length;i++)if(initial[i]!=(op==0?a[i]|b[i]:op==1?a[i]&b[i]:a[i]&~b[i]))throw new Exception("benchmark members");
                int iterations=32;
                for(int warm=0;warm<4;warm++)
                {
                    long t=Stopwatch.GetTimestamp();for(int i=0;i<iterations;i++)action();
                    double seconds=(Stopwatch.GetTimestamp()-t)/(double)Stopwatch.Frequency;
                    if(seconds>=.001||iterations>=131072)break;
                    iterations=Math.Min(131072,Math.Max(iterations*2,(int)(iterations*.001/Math.Max(.000001,seconds))));
                }
                var ns=new double[7];var allocated=new double[7];
                GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                for(int sample=0;sample<7;sample++)
                {
                    long bytes=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
                    for(int i=0;i<iterations;i++)action();
                    long elapsed=Stopwatch.GetTimestamp()-start;
                    allocated[sample]=(GC.GetAllocatedBytesForCurrentThread()-bytes)/(double)iterations;
                    ns[sample]=elapsed*(1e9/Stopwatch.Frequency)/iterations;
                    if(contract=="prepared"&&allocated[sample]!=0)throw new Exception("prepared allocation");
                    if(checksum!=expected)throw new Exception("post-timing count");GC.KeepAlive(sink);
                }
                rows.Add(new{kernel=labels[kernel],op,contract,words=length,distribution,iterations,ns,allocated_bytes=allocated,expected_count=expected});
            }
        }
        var data=new{runtime=RuntimeInformation.FrameworkDescription,architecture=RuntimeInformation.ProcessArchitecture.ToString(),avx2=Avx2.IsSupported,popcnt=Popcnt.X64.IsSupported,vector=Vector.IsHardwareAccelerated,rows,semantics="Output writes plus eager exact Count; fresh allocates output, copy_mutate really clones input. All costs included. Raw arrays only, not a full RuntimeTagSet benchmark."};
        File.WriteAllText(args[0],JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("DENSE_BENCH rows="+rows.Count+" avx2="+Avx2.IsSupported+" popcnt="+Popcnt.X64.IsSupported);return 0;
    }
}
