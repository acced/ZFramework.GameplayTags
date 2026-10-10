using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using GameplayTags;

internal static partial class Program
{
    private static long s_Assertions;
    private static readonly List<string> s_Names = new List<string>();
    private static string s_Deep;
    private static readonly Func<long> AllocatedBytes = CreateAllocationCounter();

    private static int Main(string[] args)
    {
        try
        {
            Register(args);
            if (args.Length != 0 && args[0] == "--bench")
                RunBenchmarks(args);
            else
            {
                SortTests();
                RandomizedIntersections();
                DeepAndWideTrees();
                AllocationTests();
                ParallelWorkspaces();
                Console.WriteLine("PASS intersection tests: {0} assertions; seed 982451653.", s_Assertions);
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Register(string[] args)
    {
        var registrations = new List<GameplayTagRegistration>();
        for (int i = 0; i < 3072; i++)
        {
            string name = "Bench.G" + (i / 32).ToString("D3") + ".T" + i.ToString("D4");
            s_Names.Add(name);
            registrations.Add(new GameplayTagRegistration(name));
        }
        var random = new Random(872341);
        var forest = new List<string>();
        for (int i = 0; i < 600; i++)
        {
            string name = i < 20 ? "Forest" + i : forest[random.Next(forest.Count)] + ".N" + i;
            forest.Add(name);
            s_Names.Add(name);
            registrations.Add(new GameplayTagRegistration(name));
        }
        // A fixed selected workload can be measured with a much larger registry.
        int padding = args.Length > 1 ? int.Parse(args[1]) : 0;
        for (int i = 0; i < padding; i++)
            registrations.Add(new GameplayTagRegistration("Unused.T" + i.ToString("D7")));
        s_Deep = "Deep";
        for (int i = 1; i < 3000; i++)
            s_Deep += ".N";
        registrations.Add(new GameplayTagRegistration(s_Deep));
        GameplayTagManager.Initialize(registrations.ToArray());
    }

    private static void Check(bool condition, string context)
    {
        Interlocked.Increment(ref s_Assertions);
        if (!condition)
            throw new Exception("Assertion failed: " + context);
    }

    private static GameplayTagContainer Set(IEnumerable<string> names)
    {
        var result = new GameplayTagContainer();
        foreach (string name in names)
            result.AddTag(GameplayTagManager.RequestTag(name));
        return result;
    }

    private static GameplayTagCountContainer Counted(IEnumerable<string> names)
    {
        var result = new GameplayTagCountContainer();
        int i = 0;
        foreach (string name in names)
        {
            int amount = 2 + i++ % 3;
            for (int j = 0; j < amount; j++)
                result.AddTag(GameplayTagManager.RequestTag(name));
        }
        return result;
    }

    private static HashSet<string> ExplicitNames(IGameplayTagContainer container)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (GameplayTag tag in container.GetExplicitTags())
            names.Add(tag.Name);
        return names;
    }

    private static void Validate(GameplayTagContainer actual, HashSet<string> expected)
    {
        // This oracle uses names and separators, never registry parent IDs/intervals.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string name in expected)
        {
            string ancestor = name;
            while (true)
            {
                counts.TryGetValue(ancestor, out int count);
                counts[ancestor] = count + 1;
                int separator = ancestor.LastIndexOf('.');
                if (separator < 0)
                    break;
                ancestor = ancestor.Substring(0, separator);
            }
        }
        Check(actual.ExplicitTagCount == expected.Count, "explicit cardinality");
        Check(actual.TagCount == counts.Count, "closure cardinality");
        Check(actual.IsEmpty == (expected.Count == 0), "empty");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        TagStorage storage = actual.Indices.Storage;
        foreach (GameplayTag tag in actual.GetTags())
        {
            Check(seen.Add(tag.Name), "unique closure enumeration");
            Check(counts.TryGetValue(tag.Name, out int count), "no spurious ancestor/descendant");
            Check(storage.GetCount(tag.RuntimeIndex) == count, "ancestor contribution");
            Check(actual.HasTag(tag), "query after build");
            Check(actual.HasTagExact(tag) == expected.Contains(tag.Name), "exact query");
        }
        seen.Clear();
        foreach (GameplayTag tag in actual.GetExplicitTags())
        {
            Check(seen.Add(tag.Name) && expected.Contains(tag.Name), "explicit enumeration");
            Check(storage.GetExplicitCount(tag.RuntimeIndex) == 1, "count source normalized to set");
        }
        foreach (string name in counts.Keys)
            Check(actual.HasTag(GameplayTagManager.RequestTag(name)), "complete closure");
    }

    private static void Shuffle<T>(IList<T> values, Random random)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            T temporary = values[i]; values[i] = values[j]; values[j] = temporary;
        }
    }

    private static void SortTests()
    {
#if !BASELINE
        var random = new Random(982451653);
        for (int n = 0; n < 400; n++)
        {
            int length = n < 40 ? n : random.Next(1, 10000);
            for (int mode = 0; mode < 6; mode++)
            {
                var actual = new int[length + 12];
                for (int i = 0; i < actual.Length; i++)
                    actual[i] = mode == 0 ? i : mode == 1 ? -i : mode == 2 ? 7 :
                        mode == 3 ? (i % 3 == 0 ? int.MinValue : int.MaxValue) :
                        mode == 4 ? Math.Min(i, actual.Length - i) : random.Next(-100000, 100000);
                var expected = (int[])actual.Clone();
                Array.Sort(expected, 5, length);
                Int32Sort.Sort(actual, 5, length);
                for (int i = 0; i < actual.Length; i++)
                    Check(actual[i] == expected[i], "integer sort/range sentinel");
            }
        }
        // Directly exercise the introsort worst-case fallback as well as its callers.
        MethodInfo heap = typeof(Int32Sort).GetMethod("HeapSort", BindingFlags.NonPublic | BindingFlags.Static);
        for (int length = 2; length < 128; length++)
        {
            var values = new int[length + 4];
            for (int i = 0; i < values.Length; i++) values[i] = random.Next();
            var expected = (int[])values.Clone();
            Array.Sort(expected, 2, length);
            heap.Invoke(null, new object[] { values, 2, length });
            for (int i = 0; i < values.Length; i++) Check(values[i] == expected[i], "heap fallback");
        }
#endif
        Console.WriteLine("PASS integer sort correctness (baseline uses BCL)");
    }

    private static void RandomizedIntersections()
    {
        var random = new Random(982451653);
        var output = new GameplayTagContainer();
#if !BASELINE
        var workspace = new GameplayTagIntersectionWorkspace();
        Check(workspace.ExplicitCapacity == 0 && workspace.RegisteredTagCapacity == 0, "lazy workspace");
#endif
        for (int round = 0; round < 260; round++)
        {
            var pool = new List<string>();
            int size = round % 10 == 0 ? 4 : random.Next(32, 240);
            for (int i = 0; i < size; i++)
                pool.Add(s_Names[random.Next(s_Names.Count)]);
            var an = new List<string>(); var bn = new List<string>();
            foreach (string name in pool)
            {
                if (random.Next(4) != 0) an.Add(name);
                if (random.Next(4) != 0) bn.Add(name);
            }
            Shuffle(an, random); Shuffle(bn, random);
            var a = Set(an); var b = Set(bn);
            // Deliberately damage dense order using actual swap-delete/reinsert.
            for (int i = 0; i < an.Count; i += 7)
            {
                GameplayTag tag = GameplayTagManager.RequestTag(an[i]);
                a.RemoveTag(tag); a.AddTag(tag);
            }
            HashSet<string> expected = ExplicitNames(a); expected.IntersectWith(ExplicitNames(b));
            HashSet<string> originalA = ExplicitNames(a), originalB = ExplicitNames(b);
            var ca = Counted(an); var cb = Counted(bn);
            IGameplayTagContainer[] left = { a, ca, new Proxy(a) };
            IGameplayTagContainer[] right = { b, cb, new Proxy(b) };
            for (int mode = 0; mode < left.Length; mode++)
            {
                GameplayTagContainer.Intersection(output, left[mode], right[mode]);
                Validate(output, expected);
                Validate(GameplayTagContainer.Intersection(left[mode], right[mode]), expected);
#if !BASELINE
                // The same dirty workspace is reused across different output instances.
                GameplayTagContainer.Intersection(output, left[mode], right[mode], workspace);
                Validate(output, expected);
                Validate(GameplayTagContainer.Intersection(left[mode], right[mode], workspace), expected);
#endif
            }
            var alias = a.Clone();
            GameplayTagContainer.Intersection(alias, alias, b); Validate(alias, expected);
            alias = b.Clone();
            GameplayTagContainer.Intersection(alias, a, alias); Validate(alias, expected);
            alias = a.Clone(); alias.IntersectWith(b); Validate(alias, expected);
#if !BASELINE
            alias = a.Clone(); alias.IntersectWith(b, workspace); Validate(alias, expected);
            alias = b.Clone(); GameplayTagContainer.Intersection(alias, a, alias, workspace); Validate(alias, expected);
            alias = a.Clone();
            var proxy = new Proxy(alias);
            GameplayTagContainer.Intersection(alias, proxy, b, workspace); Validate(alias, expected);
            GameplayTagContainer.Intersection(output, ca, ca, workspace); Validate(output, originalA);
#endif
            GameplayTagContainer.Intersection(output, ca, ca); Validate(output, originalA);
            // Original sources, including their counts, must remain untouched.
            Validate(a, originalA); Validate(b, originalB);
            foreach (GameplayTag tag in ca.GetExplicitTags())
                Check(ca.GetExplicitTagCount(tag) >= 2, "source count preserved");
            GameplayTagContainer.Intersection(output, a, b);
            var remaining = new List<string>(expected); Shuffle(remaining, random);
            foreach (string name in remaining)
            {
                Check(output.TryRemoveTag(GameplayTagManager.RequestTag(name)), "delete each result exactly once");
            }
            Validate(output, new HashSet<string>());
            // Exercise stale buckets, tiny results, old tails and post-build growth.
            if (round % 15 == 0) output.EnsureCapacity(8192);
            output.AddTag(GameplayTagManager.RequestTag(s_Names[0]));
            Validate(output, new HashSet<string> { s_Names[0] });
        }
        Console.WriteLine("PASS random aliases, counted sources, mutated layouts and dirty-workspace reuse");
    }

    private static void DeepAndWideTrees()
    {
        var names = new List<string> { "Deep", s_Deep };
        string name = "Deep";
        for (int i = 1; i < 3000; i++)
        {
            name += ".N";
            if (i % 211 == 0) names.Add(name);
        }
        for (int i = 0; i < 40; i++) names.Add(s_Names[i]);
        var random = new Random(41231);
#if !BASELINE
        var workspace = new GameplayTagIntersectionWorkspace();
#endif
        for (int order = 0; order < 3; order++)
        {
            if (order == 1) names.Reverse();
            if (order == 2) Shuffle(names, random);
            var a = Set(names); var b = Counted(names);
            // Exclude many tags to force general, rather than copy-minus-missing.
            for (int i = 0; i < 40; i += 2)
            {
                GameplayTag tag = GameplayTagManager.RequestTag(s_Names[i]);
                while (b.GetExplicitTagCount(tag) > 0) b.RemoveTag(tag);
            }
            HashSet<string> expected = ExplicitNames(a); expected.IntersectWith(ExplicitNames(b));
            var output = new GameplayTagContainer();
            GameplayTagContainer.Intersection(output, a, b); Validate(output, expected);
#if !BASELINE
            GameplayTagContainer.Intersection(output, a, b, workspace); Validate(output, expected);
            // Remove a deep explicit leaf plus shallow tags, then reuse the workspace.
            var alias = a.Clone(); alias.IntersectWith(b, workspace); Validate(alias, expected);
#endif
            var remaining = new List<string>(expected); Shuffle(remaining, random);
            foreach (string n in remaining) output.RemoveTag(GameplayTagManager.RequestTag(n));
            Validate(output, new HashSet<string>());
        }
        Console.WriteLine("PASS 3000-deep mixed explicit ancestors and multi-root closures");
    }

    private static Func<long> CreateAllocationCounter()
    {
        MethodInfo method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
        return method == null ? null : (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
    }

    private static long Allocations(Action operation, int iterations)
    {
        if (AllocatedBytes == null)
            throw new NotSupportedException("This runtime has no per-thread allocation counter.");
        for (int i = 0; i < 200; i++) operation();
        AllocatedBytes();
        long start = AllocatedBytes();
        for (int i = 0; i < iterations; i++) operation();
        return AllocatedBytes() - start;
    }

    private static void AllocationTests()
    {
        MakeBenchmarkInputs(1024, 50, true, out GameplayTagContainer a, out GameplayTagContainer b);
        var output = new GameplayTagContainer();
        long bytes = Allocations(() => GameplayTagContainer.Intersection(output, a, b), 1000);
        Console.WriteLine("ALLOC reused default, randomized half-overlap: {0} bytes / 1000 calls", bytes);
#if !BASELINE
        Check(bytes == 0, "no default general-intersection allocation after warmup");
        var workspace = new GameplayTagIntersectionWorkspace();
        bytes = Allocations(() => GameplayTagContainer.Intersection(output, a, b, workspace), 1000);
        Check(bytes == 0, "no workspace general-intersection allocation after warmup");
        var alias = new GameplayTagContainer();
        bytes = Allocations(() => { GameplayTagContainer.Copy(alias, a); alias.IntersectWith(b); }, 1000);
        Check(bytes == 0, "alias default (including restore) allocation");
        bytes = Allocations(() => { GameplayTagContainer.Copy(alias, a); alias.IntersectWith(b, workspace); }, 1000);
        Check(bytes == 0, "alias workspace (including restore) allocation");
        var counted = Counted(ExplicitNames(a));
        bytes = Allocations(() => GameplayTagContainer.Copy(output, counted), 1000);
        Check(bytes == 0, "count normalization shares no-allocation builder");
        var sort = new int[1030];
        bytes = Allocations(() => { for (int i = 0; i < 1024; i++) sort[i + 3] = 1024 - i; Int32Sort.Sort(sort, 3, 1024); }, 1000);
        Check(bytes == 0, "dedicated integer sort allocation");
        Console.WriteLine("PASS default/workspace/aliases/count-normalization/sort: 0 warm bytes");
#endif
    }

    private static void ParallelWorkspaces()
    {
#if !BASELINE
        MakeBenchmarkInputs(128, 50, true, out GameplayTagContainer a, out GameplayTagContainer b);
        HashSet<string> expected = ExplicitNames(a); expected.IntersectWith(ExplicitNames(b));
        Parallel.For(0, 4, worker =>
        {
            var workspace = new GameplayTagIntersectionWorkspace();
            var output = new GameplayTagContainer();
            for (int i = 0; i < 20; i++)
            {
                GameplayTagContainer.Intersection(output, a, b, workspace);
                Validate(output, expected);
            }
        });
        Console.WriteLine("PASS independent concurrent workspaces over immutable inputs");
#endif
    }

    private sealed class Proxy : IGameplayTagContainer
    {
        private readonly IGameplayTagContainer m_Source;
        internal Proxy(IGameplayTagContainer source) => m_Source = source;
        public bool IsEmpty => m_Source.IsEmpty;
        public int ExplicitTagCount => m_Source.ExplicitTagCount;
        public int TagCount => m_Source.TagCount;
        public GameplayTagContainerIndices Indices => m_Source.Indices;
        public void AddTag(GameplayTag tag) => m_Source.AddTag(tag);
        public void RemoveTag(GameplayTag tag) => m_Source.RemoveTag(tag);
        public GameplayTagEnumerator GetTags() => m_Source.GetTags();
        public GameplayTagEnumerator GetExplicitTags() => m_Source.GetExplicitTags();
        public void AddTags<T>(in T other) where T : IGameplayTagContainer => m_Source.AddTags(other);
        public void RemoveTags<T>(in T other) where T : IGameplayTagContainer => m_Source.RemoveTags(other);
        public void GetParentTags(GameplayTag tag, List<GameplayTag> result) => m_Source.GetParentTags(tag, result);
        public void GetChildTags(GameplayTag tag, List<GameplayTag> result) => m_Source.GetChildTags(tag, result);
        public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> result) => m_Source.GetExplicitParentTags(tag, result);
        public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> result) => m_Source.GetExplicitChildTags(tag, result);
        public void Clear() => m_Source.Clear();
        public IEnumerator<GameplayTag> GetEnumerator() => m_Source.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
