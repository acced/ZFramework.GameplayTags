// Standalone independent packed-record kernel oracles. Compile together with
// RuntimeBitOperations.cs and RuntimeBitOperations.Packed.cs. No unsafe setting is needed.
using System;
using System.IO;
using GameplayTags;

internal static class PackedKernelTests
{
    private static long assertions, allocatedBytes, allocationPositiveControl;
    private static int sink;
    private static object allocationSink;
    private static void Check(bool value, string detail)
    { assertions++; if (!value) throw new InvalidOperationException("Packed kernel invariant: " + detail); }
    private static int Count(int mask)
    { int count = 0; for (int bit = 0; bit < 16; bit++) if ((mask & (1 << bit)) != 0) count++; return count; }
    private static int Key(int block) => unchecked((block << 16) ^ int.MinValue);
    private static int NextMask(Random random) => random.Next(1, 65536);
    private static int RunOperation(int operation, int[] a, int ao, int[] b, int bo,
        int[] output, int oo, int length, bool backwards, out int written)
    {
        if (operation == 0)
        { written = length; return RuntimeBitOperations.UnionPackedRange(a, ao, b, bo, output, oo, length, backwards); }
        return RuntimeBitOperations.FilterPackedRange(a, ao, b, bo, output, oo, length, operation == 2, out written);
    }
    private static void CheckWordMasks()
    {
        var a = new int[5]; var b = new int[6]; var output = new int[7];
        for (int mask = 0; mask <= 65535; mask++)
        for (int pair = 0; pair < 3; pair++)
        {
            int other = pair == 0 ? mask : pair == 1 ? mask ^ 65535 : 1 << (mask & 15);
            int key = Key(mask);
            a[1] = key | mask; b[2] = key | other;
            for (int operation = 0; operation < 3; operation++)
            {
                Array.Fill(output, unchecked((int)0xABCD1234));
                int expectedMask = operation == 0 ? mask | other : operation == 1 ? mask & other : mask & ~other & 65535;
                int common = RunOperation(operation, a, 1, b, 2, output, 3, 1, false, out int written);
                Check(common == Count(mask & other), "exhaustive mask count");
                int expectedWritten = operation == 0 || expectedMask != 0 ? 1 : 0;
                Check(written == expectedWritten, "exhaustive mask written");
                if (written != 0) Check(output[3] == (key | expectedMask), "exhaustive key/mask");
                Check(output[2] == unchecked((int)0xABCD1234) && output[4] == unchecked((int)0xABCD1234), "exhaustive canary");
            }
        }
    }
    private static void Trial(int length, int shape, int alias, int shift, int operation, bool backwards, Random random)
    {
        int size = length + 64, ao = 16, bo = 20, oo = 3;
        var a = new int[size]; var b = new int[size]; var output = new int[size];
        Array.Fill(a, unchecked((int)0xEFFE1357)); Array.Fill(b, unchecked((int)0xABCD2468));
        Array.Fill(output, unchecked((int)0xFACE9876));
        for (int i = 0; i < length; i++)
        {
            int x, y;
            switch (shape)
            {
                case 0: x = y = 65535; break;
                case 1: x = 0xAAAA; y = 0x5555; break;
                case 2: x = y = 1 << (i & 15); break;
                case 3: x = NextMask(random); y = NextMask(random); break;
                case 4: x = (i & 3) == 0 ? 0 : (i & 3) == 1 ? 65535 : 0xAAAA;
                    y = (i & 3) == 0 ? 65535 : (i & 3) == 1 ? 0 : (i & 3) == 2 ? 0x5555 : 0xAAAA; break;
                case 5: x = 65535; y = (i & 15) < 8 ? 65535 : 1; break;
                case 6: x = y = 0; break;
                default: x = 0x8001; y = (i % 9) == 0 ? 0x8000 : 0x7FFE; break;
            }
            int key = Key((32760 + i) & 65535);
            a[ao + i] = key | x; b[bo + i] = key | y;
        }
        if (alias >= 3 && alias <= 4) { b = a; bo = ao; }
        else if (alias == 5)
        {
            // Overlapping inputs as well as output. Equal keys remain the actual precondition;
            // deliberately constant keys make this stronger than consecutive-key callers need.
            b = a;
            for (int i = 0; i < length + bo - ao; i++) a[ao + i] = Key(65535) | NextMask(random);
        }
        if (alias == 1 || alias == 4 || alias == 5) { output = a; oo = backwards ? Math.Max(ao, alias == 5 ? bo : ao) + shift : ao - shift; }
        else if (alias == 2) { output = b; oo = backwards ? bo + shift : bo - shift; }
        var beforeA = (int[])a.Clone(); var beforeB = (int[])b.Clone(); var expected = (int[])output.Clone();
        int expectedWritten = 0, expectedCommon = 0;
        for (int i = 0; i < length; i++)
        {
            int x = beforeA[ao + i], y = beforeB[bo + i];
            Check((x & ~65535) == (y & ~65535), "fixture corresponding keys");
            expectedCommon += Count(x & y);
            int mask = (operation == 0 ? x | y : operation == 1 ? x & y : x & ~y) & 65535;
            if (operation == 0 || mask != 0) expected[oo + expectedWritten++] = (x & ~65535) | mask;
        }
        int common = RunOperation(operation, a, ao, b, bo, output, oo, length, backwards, out int written);
        Check(common == expectedCommon, "array count"); Check(written == expectedWritten, "array written");
        for (int i = 0; i < size; i++)
        {
            Check(output[i] == expected[i], "output/key/order/canary");
            if (!ReferenceEquals(output, a)) Check(a[i] == beforeA[i], "unaliased left mutation");
            if (!ReferenceEquals(output, b)) Check(b[i] == beforeB[i], "unaliased right mutation");
        }
    }
    private static void CheckBuffers(int length, Random random, bool fullAliases = true)
    {
        int[] shifts = fullAliases ? new[] { 0, 1, 3, 7 } : new[] { 0, 1 };
        for (int shape = 0; shape < 8; shape++)
        for (int alias = 0; alias < 6; alias++)
        foreach (int shift in shifts)
        for (int operation = 0; operation < 3; operation++)
        {
            Trial(length, shape, alias, shift, operation, false, random);
            if (operation == 0) Trial(length, shape, alias, shift, operation, true, random);
        }
    }
    private static int Exercise(int[] originalA, int[] originalB, int[] a, int[] b, int[] output)
    {
        const int length = 257;
        Array.Copy(originalA, a, a.Length); Array.Copy(originalB, b, b.Length);
        int count = RuntimeBitOperations.UnionPackedRange(a, 16, b, 20, output, 3, length, false);
        count ^= RuntimeBitOperations.UnionPackedRange(a, 16, b, 20, a, 17, length, true);
        Array.Copy(originalA, a, a.Length);
        count ^= RuntimeBitOperations.FilterPackedRange(a, 16, b, 20, output, 3, length, false, out int written);
        count ^= written;
        count ^= RuntimeBitOperations.FilterPackedRange(a, 16, b, 20, b, 19, length, false, out written);
        count ^= written; Array.Copy(originalB, b, b.Length);
        count ^= RuntimeBitOperations.FilterPackedRange(a, 16, b, 20, a, 15, length, true, out written);
        return count ^ written;
    }
    private static void CheckAllocations()
    {
        // Background GC can produce spurious allocation-accounting deltas on affected runtimes.
        // Do not discard such a delta: require the explicit Batch-GC measurement contract.
        Check(System.Runtime.GCSettings.LatencyMode == System.Runtime.GCLatencyMode.Batch, "Batch GC required for allocation assertion");
        var originalA = new int[321]; var originalB = new int[321]; var a = new int[321]; var b = new int[321]; var output = new int[321];
        for (int i = 0; i < 257; i++) { originalA[16 + i] = Key(i) | 65535; originalB[20 + i] = Key(i) | ((i & 3) == 0 ? 65535 : 0xAAAA); }
        for (int i = 0; i < 100; i++) sink = Exercise(originalA, originalB, a, b, output);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) sink ^= Exercise(originalA, originalB, a, b, output);
        allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocatedBytes == 0, "prepared kernel allocation");
        before = GC.GetAllocatedBytesForCurrentThread(); allocationSink = new byte[37];
        allocationPositiveControl = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocationPositiveControl >= 37, "positive allocation control");
    }
    internal static long Run()
    {
        assertions = 0; var random = new Random(20261001);
        CheckWordMasks();
        for (int length = 0; length <= 65; length++) CheckBuffers(length, random);
        foreach (int length in new[] { 127, 128, 129, 255, 256, 257, 511, 512, 513, 1023, 1024, 1025, 4095, 4096, 4097 }) CheckBuffers(length, random, false);
        // Max packed universe and accumulator-size boundary, with output/input aliases.
        for (int shape = 0; shape < 4; shape++) for (int alias = 0; alias < 3; alias++) for (int op = 0; op < 3; op++)
        { Trial(65536, shape, alias, 1, op, false, random); if (op == 0) Trial(65536, shape, alias, 1, op, true, random); }
        CheckAllocations(); return assertions;
    }
    public static void Main(string[] args)
    {
        Run();
        string json = "{\"assertions\":" + assertions + ",\"failures\":0,\"allocatedBytes\":" + allocatedBytes
            + ",\"allocationPositiveControl\":" + allocationPositiveControl + ",\"gcMode\":\"" + System.Runtime.GCSettings.LatencyMode + "\"}";
        Console.WriteLine("PACKED_KERNEL_TESTS " + json);
        if (args.Length != 0) File.WriteAllText(args[0], json);
    }
}
