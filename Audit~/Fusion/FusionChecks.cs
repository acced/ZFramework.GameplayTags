using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GameplayTags.Experiments;
internal static class FusionChecks
{
    static long assertions;
    static void Check(bool b) { assertions++; if (!b) throw new Exception("Dense fusion invariant"); }
    static int Count(ulong x) { int n = 0; while (x != 0) { x &= x - 1; n++; } return n; }
    static ulong Next(Random r) => (ulong)(uint)r.Next() << 33 ^ (ulong)(uint)r.Next() << 2 ^ (uint)r.Next(4);
    static int Run(int op, ulong[] a, ulong[] b, ulong[] o) => op == 0 ? DenseFusion.Union(a,b,o) : op == 1 ? DenseFusion.Intersection(a,b,o) : DenseFusion.Difference(a,b,o);
    public static void Main(string[] args)
    {
        var random = new Random(20261001);
        var lengths = Enumerable.Range(0, 258).Concat(new[]{511,512,513,1023,1024,1025,159,4161,15870}).Distinct().ToArray();
        foreach (int n in lengths)
        foreach (int shape in new[]{0,1,2,3})
        {
            var a = new ulong[n]; var b = new ulong[n];
            for (int i = 0; i < n; i++)
            {
                a[i] = shape == 0 ? 0 : shape == 1 ? ulong.MaxValue : shape == 2 ? 0xAAAAAAAAAAAAAAAAUL : Next(random);
                b[i] = shape == 0 ? Next(random) : shape == 1 ? ulong.MaxValue : shape == 2 ? 0x5555555555555555UL : Next(random);
            }
            for (int op = 0; op < 3; op++)
            {
                ulong[] expected = new ulong[n]; int count = 0;
                for (int i = 0; i < n; i++) { expected[i] = op == 0 ? a[i] | b[i] : op == 1 ? a[i] & b[i] : a[i] & ~b[i]; count += Count(expected[i]); }
                for (int alias = 0; alias < 3; alias++)
                {
                    var left = (ulong[])a.Clone(); var right = (ulong[])b.Clone();
                    ulong[] output = alias == 0 ? new ulong[n] : alias == 1 ? left : right;
                    if (alias == 0) Array.Fill(output, ulong.MaxValue);
                    Check(Run(op,left,right,output) == count);
                    for (int i = 0; i < n; i++) { Check(output[i] == expected[i]); if (alias != 1) Check(left[i] == a[i]); if (alias != 2) Check(right[i] == b[i]); }
                }
            }
        }
        var x = new ulong[4161]; var y = new ulong[4161]; var z = new ulong[4161];
        Array.Fill(x,0x7FFFFFFFFFFFFFFFUL); Array.Fill(y,0xAAAAAAAAAAAAAAAAUL);
        for (int i=0;i<100;i++) { DenseFusion.Union(x,y,z); DenseFusion.Intersection(x,y,z); DenseFusion.Difference(x,y,z); }
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread(); int sum=0;
        for(int i=0;i<3000;i++) { sum ^= DenseFusion.Union(x,y,z); sum ^= DenseFusion.Intersection(x,y,z); sum ^= DenseFusion.Difference(x,y,z); }
        long allocated = GC.GetAllocatedBytesForCurrentThread()-before;
        Check(allocated==0); GC.KeepAlive(sum);
        before=GC.GetAllocatedBytesForCurrentThread(); var positive=new byte[128]; GC.KeepAlive(positive);
        long positiveBytes=GC.GetAllocatedBytesForCurrentThread()-before; Check(positiveBytes>=128);
        var result=new { assertions, failures=0, allocated, positiveBytes, backend=DenseFusion.Backend, runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, architecture=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), unity_native="not_run" };
        string json=JsonSerializer.Serialize(result); Console.WriteLine("FUSION_CHECKS "+json);
        File.WriteAllText(args[0],json);
    }
}
