// Standalone regression checks for RuntimeBitOperations. Compile this file together
// with Runtime/RuntimeBitOperations.cs; no test framework or unsafe setting is needed.
using System;
using System.IO;
using GameplayTags;

internal static class DenseKernelTests
{
    private static long assertions;
    private static long allocatedBytes;
    private static long allocationPositiveControl;

    private static void Check(bool condition)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("Dense kernel invariant failed.");
    }

    // Deliberately independent reference implementations.
    private static int Count(ulong value)
    {
        int count = 0;
        while (value != 0) { value &= value - 1; count++; }
        return count;
    }

    private static int TrailingZeros(ulong value)
    {
        if (value == 0) return 64;
        int count = 0;
        while ((value & 1) == 0) { value >>= 1; count++; }
        return count;
    }

    private static ulong Next(Random random)
        => (ulong)(uint)random.Next() << 33 ^ (ulong)(uint)random.Next() << 2 ^ (uint)random.Next(4);

    private static ulong Apply(int operation, ulong a, ulong b)
        => operation == 0 ? a | b : operation == 1 ? a & b : a & ~b;

    private static int RunOperation(int operation, ulong[] a, ulong[] b, ulong[] output)
        => operation == 0 ? RuntimeBitOperations.Union(a, b, output)
            : operation == 1 ? RuntimeBitOperations.Intersect(a, b, output)
            : RuntimeBitOperations.Except(a, b, output);

    private static void CheckPrimitives(Random random)
    {
        Check(RuntimeBitOperations.PopCount(0) == 0);
        Check(RuntimeBitOperations.PopCount(ulong.MaxValue) == 64);
        Check(RuntimeBitOperations.TrailingZeroCount(0) == 64);
        Check(RuntimeBitOperations.TrailingZeroCount(ulong.MaxValue) == 0);
        for (int bit = 0; bit < 64; bit++)
        {
            ulong value = 1UL << bit;
            Check(RuntimeBitOperations.PopCount(value) == 1);
            Check(RuntimeBitOperations.PopCount(~value) == 63);
            Check(RuntimeBitOperations.TrailingZeroCount(value) == bit);
            Check(RuntimeBitOperations.TrailingZeroCount(~value) == (bit == 0 ? 1 : 0));
        }
        for (int i = 0; i < 10000; i++)
        {
            ulong value = Next(random);
            Check(RuntimeBitOperations.PopCount(value) == Count(value));
            Check(RuntimeBitOperations.TrailingZeroCount(value) == TrailingZeros(value));
        }
    }

    private static void CheckBuffers(int length, int shape, Random random)
    {
        var originalA = new ulong[length];
        var originalB = new ulong[length];
        bool any = false, aSubsetB = true, bSubsetA = true;
        for (int i = 0; i < length; i++)
        {
            ulong a, b;
            switch (shape)
            {
                case 0: a = 0; b = Next(random); break;
                case 1: a = ulong.MaxValue; b = ulong.MaxValue; break;
                case 2: a = 0xAAAAAAAAAAAAAAAAUL; b = 0x5555555555555555UL; break;
                case 3: a = Next(random); b = Next(random); break;
                case 4: a = 1UL << (i & 63); b = a; break;
                default: a = 0; b = 0; break;
            }
            originalA[i] = a;
            originalB[i] = b;
            any |= (a & b) != 0;
            aSubsetB &= (a & ~b) == 0;
            bSubsetA &= (b & ~a) == 0;
        }
        Check(RuntimeBitOperations.IsAny(originalA, originalB) == any);
        Check(RuntimeBitOperations.IsAny(originalB, originalA) == any);
        Check(RuntimeBitOperations.IsSubset(originalA, originalB) == aSubsetB);
        Check(RuntimeBitOperations.IsSubset(originalB, originalA) == bSubsetA);
        Check(RuntimeBitOperations.IsSubset(originalA, originalA));
        for (int operation = 0; operation < 3; operation++)
        for (int alias = 0; alias < 5; alias++)
        {
            var a = (ulong[])originalA.Clone();
            var b = alias >= 3 ? a : (ulong[])originalB.Clone();
            var output = alias == 1 || alias == 4 ? a : alias == 2 ? b : new ulong[length];
            if (alias == 0 || alias == 3) Array.Fill(output, ulong.MaxValue);
            var expected = new ulong[length];
            int count = 0;
            for (int i = 0; i < length; i++)
            {
                expected[i] = Apply(operation, a[i], b[i]);
                count += Count(expected[i]);
            }
            Check(RunOperation(operation, a, b, output) == count);
            for (int i = 0; i < length; i++)
            {
                Check(output[i] == expected[i]);
                if (!ReferenceEquals(output, a)) Check(a[i] == originalA[i]);
                if (!ReferenceEquals(output, b)) Check(b[i] == (alias >= 3 ? originalA[i] : originalB[i]));
            }
        }
    }

    private static void CheckPredicateBoundaries()
    {
        // Every bit position matters, including low bits that floating-point test
        // intrinsics would miss. Exercise vector body and all scalar tail lengths.
        for (int length = 1; length <= 19; length++)
        {
            var a = new ulong[length];
            var b = new ulong[length];
            for (int word = 0; word < length; word++)
            for (int bit = 0; bit < 64; bit++)
            {
                ulong value = 1UL << bit;
                a[word] = value;
                Check(!RuntimeBitOperations.IsAny(a, b));
                Check(!RuntimeBitOperations.IsSubset(a, b));
                Check(RuntimeBitOperations.IsSubset(b, a));
                b[word] = value;
                Check(RuntimeBitOperations.IsAny(a, b));
                Check(RuntimeBitOperations.IsSubset(a, b));
                Check(RuntimeBitOperations.IsSubset(b, a));
                b[word] = ~value;
                Check(!RuntimeBitOperations.IsAny(a, b));
                Check(!RuntimeBitOperations.IsSubset(a, b));
                a[word] = 0;
                b[word] = 0;
            }
        }
    }

    private static void CheckAllocations()
    {
        var a = new ulong[4161];
        var b = new ulong[4161];
        var output = new ulong[4161];
        Array.Fill(a, 0x7FFFFFFFFFFFFFFFUL);
        Array.Fill(b, 0xAAAAAAAAAAAAAAAAUL);
        for (int i = 0; i < 100; i++) Exercise(a, b, output);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        int checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) checksum ^= Exercise(a, b, output);
        allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocatedBytes == 0);
        GC.KeepAlive(checksum);
        before = GC.GetAllocatedBytesForCurrentThread();
        var positive = new byte[128];
        GC.KeepAlive(positive);
        allocationPositiveControl = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocationPositiveControl >= 128);
    }

    private static int Exercise(ulong[] a, ulong[] b, ulong[] output)
    {
        int checksum = RuntimeBitOperations.Union(a, b, output);
        checksum ^= RuntimeBitOperations.Intersect(a, b, output);
        checksum ^= RuntimeBitOperations.Except(a, b, output);
        checksum ^= RuntimeBitOperations.IsAny(a, b) ? 1 : 0;
        checksum ^= RuntimeBitOperations.IsSubset(a, b) ? 1 : 0;
        checksum ^= RuntimeBitOperations.PopCount(output[0]);
        checksum ^= RuntimeBitOperations.TrailingZeroCount(output[0]);
        return checksum;
    }

    internal static long Run()
    {
        assertions = 0;
        var random = new Random(20261001);
        CheckPrimitives(random);
        for (int length = 0; length <= 257; length++)
            for (int shape = 0; shape < 6; shape++) CheckBuffers(length, shape, random);
        foreach (int length in new[] { 511, 512, 513, 1023, 1024, 1025, 4161, 15870, 65537 })
            for (int shape = 0; shape < 6; shape++) CheckBuffers(length, shape, random);
        CheckPredicateBoundaries();
        CheckAllocations();
        return assertions;
    }

    public static void Main(string[] args)
    {
        Run();
        string json = "{\"assertions\":" + assertions + ",\"failures\":0,\"allocatedBytes\":"
            + allocatedBytes + ",\"allocationPositiveControl\":" + allocationPositiveControl + "}";
        Console.WriteLine("DENSE_KERNEL_TESTS " + json);
        if (args.Length != 0) File.WriteAllText(args[0], json);
    }
}
