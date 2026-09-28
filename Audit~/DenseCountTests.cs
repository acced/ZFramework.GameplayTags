using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using GameplayTags;

// Verify the exact private shipping CountWords implementation, not a copied test algorithm.
internal static class DenseCountTests
{
    private static ulong Next(ref ulong state)
    {
        state ^= state << 13; state ^= state >> 7; state ^= state << 17;
        return state;
    }
    private static int Main(string[] args)
    {
        Type bits = typeof(RuntimeTagSet).GetNestedType("Bits", BindingFlags.NonPublic);
        MethodInfo method = bits.GetMethod("CountWords", BindingFlags.Static | BindingFlags.NonPublic);
        var count = (Func<ulong[], int, int>)method.CreateDelegate(typeof(Func<ulong[], int, int>));
        ulong state = 9282026;
        int assertions = 0;
        int[] additional = { 1023, 1024, 1025, 1041, 4095, 4096, 4097, 4161, 16384 };
        for (int scenario = 0; scenario <= 320 + additional.Length; scenario++)
        {
            int length = scenario <= 320 ? scenario : additional[scenario - 321];
            var words = new ulong[length + 19];
            // Sentinel words model occupancy summaries. CountWords must ignore all of them.
            for (int i = length; i < words.Length; i++) words[i] = ulong.MaxValue;
            for (int pattern = 0; pattern < 6; pattern++)
            {
                int expected = 0;
                for (int i = 0; i < length; i++)
                {
                    ulong value;
                    if (pattern == 0) value = 0;
                    else if (pattern == 1) value = ulong.MaxValue;
                    else if (pattern == 2) value = 1UL << (i & 63);
                    else if (pattern == 3) value = ~(1UL << (i & 63));
                    else if (pattern == 4) value = (i & 1) == 0 ? 0x5555555555555555UL : 0xAAAAAAAAAAAAAAAAUL;
                    else value = Next(ref state);
                    words[i] = value;
                    for (int b = 0; b < 64; b++) if ((value & (1UL << b)) != 0) expected++;
                }
                assertions++;
                if (count(words, length) != expected) throw new Exception("Production count mismatch, length=" + length + ", pattern=" + pattern);
                assertions++;
                for (int i = length; i < words.Length; i++)
                    if (words[i] != ulong.MaxValue) throw new Exception("Count mutated sentinel summary words");
            }
        }
        File.WriteAllText(args[0], JsonSerializer.Serialize(new { assertions, failures = 0,
            implementation = "RuntimeTagSet.Bits.CountWords from shipping source", native_execution = false }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PRODUCTION BIT-COUNT assertions=" + assertions + " failures=0");
        return 0;
    }
}
