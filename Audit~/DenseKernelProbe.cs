using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

// An experiment, not a shipping backend. Both kernels are portable integer arithmetic.
// Carry-save / Harley-Seal background: https://arxiv.org/abs/1611.07612
internal static class DenseKernelProbe
{
    private static int sink;
    private static long assertions;
    private static void Check(bool value) { assertions++; if (!value) throw new Exception("Bit-count oracle mismatch"); }
    private static int Pop(ulong x)
    {
        unchecked
        {
            x -= (x >> 1) & 0x5555555555555555UL;
            x = (x & 0x3333333333333333UL) + ((x >> 2) & 0x3333333333333333UL);
            x = (x + (x >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((x * 0x0101010101010101UL) >> 56);
        }
    }
    private static int Scalar(ulong[] words, int length)
    {
        int count = 0;
        for (int i = 0; i < length; i++) count += Pop(words[i]);
        return count;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Carry(out ulong high, ref ulong low, ulong a, ulong b, ulong c)
    {
        ulong different = a ^ b;
        high = (a & b) | (different & c);
        low = different ^ c;
    }
    private static int CarrySave(ulong[] words, int length)
    {
        ulong ones = 0, twos = 0, fours = 0, eights = 0;
        int count = 0, i = 0;
        for (; i + 15 < length; i += 16)
        {
            Carry(out ulong t0, ref ones, ones, words[i], words[i + 1]);
            Carry(out ulong t1, ref ones, ones, words[i + 2], words[i + 3]);
            Carry(out ulong f0, ref twos, twos, t0, t1);
            Carry(out t0, ref ones, ones, words[i + 4], words[i + 5]);
            Carry(out t1, ref ones, ones, words[i + 6], words[i + 7]);
            Carry(out ulong f1, ref twos, twos, t0, t1);
            Carry(out ulong e0, ref fours, fours, f0, f1);
            Carry(out t0, ref ones, ones, words[i + 8], words[i + 9]);
            Carry(out t1, ref ones, ones, words[i + 10], words[i + 11]);
            Carry(out f0, ref twos, twos, t0, t1);
            Carry(out t0, ref ones, ones, words[i + 12], words[i + 13]);
            Carry(out t1, ref ones, ones, words[i + 14], words[i + 15]);
            Carry(out f1, ref twos, twos, t0, t1);
            Carry(out ulong e1, ref fours, fours, f0, f1);
            Carry(out ulong sixteens, ref eights, eights, e0, e1);
            count += Pop(sixteens);
        }
        count = count * 16 + Pop(eights) * 8 + Pop(fours) * 4 + Pop(twos) * 2 + Pop(ones);
        for (; i < length; i++) count += Pop(words[i]);
        return count;
    }
    private static int UnionScalar(ulong[] a, ulong[] b, ulong[] result)
    {
        int count = 0, extra = 0, i = 0;
        for (; i + 1 < a.Length; i += 2)
        {
            ulong x = a[i] | b[i], y = a[i + 1] | b[i + 1];
            result[i] = x; result[i + 1] = y;
            count += Pop(x); extra += Pop(y);
        }
        if (i < a.Length) { ulong x = a[i] | b[i]; result[i] = x; count += Pop(x); }
        return count + extra;
    }
    private static int UnionCarry(ulong[] a, ulong[] b, ulong[] result)
    {
        for (int i = 0; i < a.Length; i++) result[i] = a[i] | b[i];
        return CarrySave(result, result.Length);
    }
    private static object Measure(string kernel, int words, Func<int> action)
    {
        int iterations = Math.Max(2000, 1000000 / Math.Max(1, words));
        for (int i = 0; i < 256; i++) sink ^= action();
        var ns = new double[9]; var allocation = new long[9];
        for (int sample = 0; sample < ns.Length; sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) sink ^= action();
            long elapsed = Stopwatch.GetTimestamp() - start;
            allocation[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
            ns[sample] = elapsed * (1e9 / Stopwatch.Frequency) / iterations;
        }
        return new { kernel, words, iterations, ns, allocation_total = allocation };
    }
    private static ulong Next(ref ulong state)
    {
        state ^= state << 13; state ^= state >> 7; state ^= state << 17;
        return state;
    }
    private static int Main(string[] args)
    {
        ulong state = 9282026;
        for (int length = 0; length <= 320; length++)
        {
            var a = new ulong[length]; var b = new ulong[length]; var result = new ulong[length];
            for (int pass = 0; pass < 4; pass++)
            {
                for (int i = 0; i < length; i++)
                {
                    a[i] = pass == 0 ? 0 : pass == 1 ? ulong.MaxValue : Next(ref state);
                    b[i] = pass == 2 ? 1UL << (i & 63) : Next(ref state);
                }
                Check(Scalar(a, length) == CarrySave(a, length));
                int expected = UnionScalar(a, b, result);
                Check(expected == UnionCarry(a, b, result));
                for (int i = 0; i < length; i++) Check(result[i] == (a[i] | b[i]));
            }
        }
        var rows = new List<object>();
        foreach (int length in new[] { 1, 8, 16, 64, 159, 1041, 4161, 16384 })
        {
            var a = new ulong[length]; var b = new ulong[length]; var result = new ulong[length];
            for (int i = 0; i < length; i++) { a[i] = Next(ref state); b[i] = Next(ref state); }
            Func<int>[] actions = { () => Scalar(a, length), () => CarrySave(a, length),
                () => UnionScalar(a, b, result), () => UnionCarry(a, b, result) };
            string[] names = { "scalar-count", "carry-save-count", "fused-union-swar", "union-then-carry-save" };
            for (int i = 0; i < actions.Length; i++) rows.Add(Measure(names[i], length, actions[i]));
        }
        File.WriteAllText(args[0], JsonSerializer.Serialize(new { assertions, rows, sink,
            limitation = "Portable kernel experiment only. Directory maintenance, object allocation and full set API are not measured here; do not substitute for complete benchmarks." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("KERNEL ORACLE assertions=" + assertions + " failures=0");
        return 0;
    }
}
