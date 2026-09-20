using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameplayTags;
#if CANDIDATE
using Set = GameplayTags.RuntimeTagSet;
using Tag = GameplayTags.RuntimeTag;
#else
using Set = GameplayTags.GameplayTagContainer;
using Tag = GameplayTags.GameplayTag;
#endif

internal static class FourWay
{
    private const int Seed = 20260920;
    private static object sink;
    private static int checksum;
    private static readonly List<object> rows = new List<object>();
#if CANDIDATE
    private static TagRegistry registry;
    private static TagSetStorage storage;
#endif
    private static void Initialize(string[] names)
    {
#if ALEX
        // Fixture setup only. Run the original registration algorithm, then install its output in the original manager.
        // Alex's public initializer reads assembly attributes; avoiding thousands of generated attributes changes no timed method.
        var context = new GameplayTagRegistrationContext();
        foreach (string name in names) context.RegisterTag(name);
        GameplayTagDefinition[] definitions = context.GenerateDefinitions();
        Type manager = typeof(GameplayTagManager);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        manager.GetField("s_TagsDefinitions", flags).SetValue(null, definitions);
        manager.GetField("s_Tags", flags).SetValue(null, definitions.Skip(1).Select(d => d.Tag).ToArray());
        manager.GetField("s_TagDefinitionsByName", flags).SetValue(null, definitions.ToDictionary(d => d.TagName, StringComparer.Ordinal));
        manager.GetField("s_IsInitialized", flags).SetValue(null, true);
#else
        var settings = UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        settings.ReplaceAll(names.Select(n => new GameplayTagDefinition(n, "", "Default", false, true)).ToList(),
            new List<GameplayTagRedirect>(), new List<GameplayTagSource> { new GameplayTagSource("Default", "", false) });
        GameplayTagManager.Initialize(settings, true);
#if CANDIDATE
        registry = GameplayTagManager.CurrentRegistry;
#endif
#endif
    }
    private static Tag Resolve(string name)
    {
#if CANDIDATE
        return registry.Resolve(name);
#else
        return GameplayTagManager.RequestTag(name);
#endif
    }
    private static Set Input(string[] names)
    {
#if CANDIDATE
        var set = new Set(registry, names.Length, storage);
#else
        var set = new Set();
#endif
        foreach (string name in names) set.AddTag(Resolve(name));
        return set;
    }
    private static int Count(Set set)
    {
#if ALEX
        return set.ExplicitTagCount;
#else
        return set.Count;
#endif
    }
    private static Set Union(Set a, Set b)
    {
#if CANDIDATE
        return Set.Union(a, b, storage);
#else
        return Set.Union(a, b);
#endif
    }
    private static Set CopyAppend(Set a, Set b)
    {
        var result = new Set(a);
#if ALEX
        result.AddTags(b);
#else
        result.AppendTags(b);
#endif
        return result;
    }
    private static Set CopyRemove(Set a, Set b)
    {
        var result = new Set(a);
        result.RemoveTags(b);
        return result;
    }
    private static string[] Values(Set set)
    {
        if (Count(set) == 0) return Array.Empty<string>();
        var names = new List<string>();
#if ALEX
        foreach (GameplayTag tag in set.GetExplicitTags()) names.Add(tag.Name);
#else
        foreach (var tag in set) names.Add(tag.Name);
#endif
        return names.ToArray();
    }
    private static long MemberBufferBytes(Set set)
    {
#if CANDIDATE
        return set.BufferBytes;
#elif ALEX
        return 4L * ((set.Indices.Explicit?.Capacity ?? 0) + (set.Indices.Implicit?.Capacity ?? 0));
#else
        var list = (List<GameplayTag>)typeof(Set).GetField("m_GameplayTags", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(set);
        return (long)IntPtr.Size * list.Capacity;
#endif
    }
    private static string StorageName(Set set)
    {
#if CANDIDATE
        return set.Storage.ToString();
#elif ALEX
        return "explicit+implicit-int-lists";
#else
        return "sorted-name-list";
#endif
    }
    private static void Verify(Set set, IEnumerable<string> expected)
    {
        var values = Values(set);
        var reference = new HashSet<string>(expected, StringComparer.Ordinal);
        if (values.Length != Count(set) || values.Distinct(StringComparer.Ordinal).Count() != values.Length || !reference.SetEquals(values))
            throw new InvalidOperationException("Wrong explicit members or Count; result is not eligible for a timing rank.");
    }
    private static void Measure(string operation, int size, int universe, string distribution, Action action, Action verify,
        int iterations, Set input, string inputDigest)
    {
        try
        {
            verify();
            for (int i = 0; i < 100; i++) action();
            var ns = new double[7];
            var allocations = new double[7];
            for (int sample = 0; sample < ns.Length; sample++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                for (int i = 0; i < iterations; i++) action();
                long elapsed = Stopwatch.GetTimestamp() - start;
                allocations[sample] = (double)(GC.GetAllocatedBytesForCurrentThread() - allocated) / iterations;
                ns[sample] = elapsed * (1e9 / Stopwatch.Frequency) / iterations;
                GC.KeepAlive(sink);
            }
            verify();
            rows.Add(new { operation, size, universe, distribution, inputDigest, status = "measured", iterations,
                ns, allocated_bytes = allocations, input_storage = StorageName(input), input_member_buffer_bytes = MemberBufferBytes(input) });
        }
        catch (Exception e)
        {
            rows.Add(new { operation, size, universe, distribution, inputDigest, status = "failed", error = e.ToString() });
        }
    }
    private static void BenchmarkUniverse(int universe)
    {
        // The same names and operation inputs are used by every build. Universe is explicit leaf definitions;
        // implicit parents are additionally present in all three registries.
        string[] registered = Enumerable.Range(0, universe).Select(i => "Bench.G" + (i / 64).ToString("D4", CultureInfo.InvariantCulture)
            + ".T" + i.ToString("D5", CultureInfo.InvariantCulture)).ToArray();
        Initialize(registered);
        foreach (string distribution in new[] { "contiguous", "scattered" })
        {
            string[] sequence = (string[])registered.Clone();
            if (distribution == "scattered")
            {
                var random = new Random(Seed);
                for (int i = sequence.Length - 1; i > 0; i--) { int j = random.Next(i + 1); string temp = sequence[i]; sequence[i] = sequence[j]; sequence[j] = temp; }
            }
            foreach (int size in new[] { 0, 8, 128, 1024, 4096 })
            {
                if (size * 2 > universe) continue;
                string[] x = sequence.Take(size).ToArray();
                string[] y = sequence.Skip(size / 2).Take(size).ToArray();
                string[] removed = x.Where((_, i) => (i & 1) == 0).ToArray();
                Set a = Input(x), b = Input(y), subset = Input(removed);
                Tag hit = Resolve(size == 0 ? sequence[0] : x[size / 2]);
                Tag miss = Resolve(sequence[sequence.Length - 1]);
                string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", x) + "\n--\n" + string.Join("\n", y))));
                int bulkIterations = Math.Max(24, Math.Min(1000, 131072 / (size + 1)));
                Measure("exact", size, universe, distribution,
                    () => { checksum ^= a.HasTagExact(hit) ? 1 : 0; },
                    () => { if (a.HasTagExact(hit) != (size != 0)) throw new Exception("wrong exact lookup"); Verify(a, x); },
                    300000, a, digest);
                Measure("exact_miss", size, universe, distribution,
                    () => { checksum ^= a.HasTagExact(miss) ? 1 : 0; },
                    () => { if (a.HasTagExact(miss) != x.Contains(sequence[sequence.Length - 1])) throw new Exception("wrong negative lookup"); },
                    300000, a, digest);
                Measure("union", size, universe, distribution,
                    () => { sink = Union(a, b); },
                    () => { Verify(Union(a, b), x.Concat(y)); Verify(a, x); Verify(b, y); },
                    bulkIterations, a, digest);
                Measure("copy_append", size, universe, distribution,
                    () => { sink = CopyAppend(a, b); },
                    () => { Verify(CopyAppend(a, b), x.Concat(y)); Verify(a, x); Verify(b, y); },
                    bulkIterations, a, digest);
                Measure("copy_remove", size, universe, distribution,
                    () => { sink = CopyRemove(a, subset); },
                    () => { Verify(CopyRemove(a, subset), x.Except(removed)); Verify(a, x); Verify(subset, removed); },
                    bulkIterations, a, digest);
            }
        }
    }
    private static int Main(string[] args)
    {
#if CANDIDATE
        storage = args.Length > 2 ? (TagSetStorage)Enum.Parse(typeof(TagSetStorage), args[2]) : TagSetStorage.Auto;
#endif
        BenchmarkUniverse(10000);
        BenchmarkUniverse(65536);
        var result = new { variant = args[1], seed = Seed, runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            stopwatch_frequency = Stopwatch.Frequency, checksum, rows,
            semantics = "Exact lookup is already-resolved. Union and both copy/mutate operations allocate independent results; no pooling or fusion. Explicit membership, Count and input immutability are validated outside timing. Name setup is outside timing for ALL variants.",
            limitations = "Managed .NET on shared runners, not Unity/IL2CPP. Member-buffer bytes exclude headers, registry and definitions. Failed rows remain visible and cannot rank." };
        File.WriteAllText(args[0], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("FOUR-WAY " + args[1] + " rows=" + rows.Count + " -> " + args[0]);
        return 0;
    }
}
