using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using GameplayTags;

internal static class Program
{
    private sealed class Fixture
    {
        internal string Id;
        internal GameplayTagRegistration[] Registrations;
        internal int Iterations;
    }

    private sealed class Snapshot
    {
        internal GameplayTagDefinition[] Definitions;
        internal GameplayTag[] Tags;
        internal int[] Parents, SubtreeEnds, HierarchyIndices;
        internal GameplayTag[] HierarchyTags;
        internal Dictionary<string, int> IndicesByName;
        internal long PayloadBytes => (long)Definitions.Length * Unsafe.SizeOf<GameplayTagDefinition>() +
            (long)(Tags.Length + HierarchyTags.Length) * Unsafe.SizeOf<GameplayTag>() +
            (long)(Parents.Length + SubtreeEnds.Length + HierarchyIndices.Length) * sizeof(int);
    }

    private sealed class ModelNode
    {
        internal string Name, Parent, Label, Description = string.Empty;
        internal int Depth, DeclaredFlags, AggregateFlags;
        internal bool Declared;
    }

    private static Snapshot s_Last;
    private static Snapshot[] s_Retained;
    private static long s_Checksum;
    private static readonly Func<long> AllocatedBytes = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>),
        typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread") ?? throw new NotSupportedException("The runtime must expose per-thread allocation accounting."));

    private static void Main(string[] args)
    {
        string output = null, variant = "standalone";
        int round = 0, samples = 1;
        bool quick = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out": output = args[++i]; break;
                case "--variant": variant = args[++i]; break;
                case "--round": round = int.Parse(args[++i]); break;
                case "--samples": samples = int.Parse(args[++i]); break;
                case "--quick": quick = true; samples = 3; break;
                default: throw new ArgumentException(args[i]);
            }
        }
        Fixture[] fixtures = Fixtures();
        var results = new List<object>();
        for (int k = 0; k < fixtures.Length; k++)
        {
            Fixture fixture = fixtures[(k + round) % fixtures.Length];
            Snapshot preflight = Build(fixture.Registrations);
            Verify(fixture.Registrations, preflight);
            int count = preflight.Tags.Length - 1, slots = preflight.HierarchyIndices.Length;
            long payload = preflight.PayloadBytes;
            ulong fixtureHash = HashFixture(fixture.Registrations), outputHash = HashOutput(preflight);
            preflight = null;
            int iterations = quick ? 1 : fixture.Iterations;
            var times = new double[samples];
            var bytes = new double[samples];
            var gcCounts = new int[samples][];
            Build(fixture.Registrations);
            for (int sample = 0; sample < samples; sample++)
            {
                s_Last = null;
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
                long allocated = AllocatedBytes();
                long start = Stopwatch.GetTimestamp();
                long checksum = 0;
                for (int iteration = 0; iteration < iterations; iteration++)
                {
                    Snapshot snapshot = Build(fixture.Registrations);
                    checksum += snapshot.Tags.Length + snapshot.Tags[snapshot.Tags.Length - 1].RuntimeIndex + snapshot.SubtreeEnds[1];
                    s_Last = snapshot;
                }
                long elapsed = Stopwatch.GetTimestamp() - start;
                long used = AllocatedBytes() - allocated;
                s_Checksum = checksum;
                if (checksum != (long)iterations * (count + 1 + count + s_Last.SubtreeEnds[1]))
                    throw new Exception("Observed build checksum mismatch");
                times[sample] = elapsed * 1e9 / Stopwatch.Frequency / iterations;
                bytes[sample] = (double)used / iterations;
                gcCounts[sample] = new[] { GC.CollectionCount(0) - g0, GC.CollectionCount(1) - g1, GC.CollectionCount(2) - g2 };
            }
            Verify(fixture.Registrations, s_Last);
            s_Last = null;
            double[] retained = MeasureRetained(fixture.Registrations, 4, 3);
            results.Add(new
            {
                CaseId = "registry/" + fixture.Id,
                Operation = "RegisterAndBuild", Unit = "one complete frozen registry",
                DeclarationCount = fixture.Registrations.Length, RegistryTagCount = count,
                FixtureChecksum = fixtureHash.ToString("X16"), OutputChecksum = outputHash.ToString("X16"),
                HierarchyPathSlots = slots, FrozenArrayPayloadBytes = payload,
                DefinitionSizeBytes = Unsafe.SizeOf<GameplayTagDefinition>(), TagSizeBytes = Unsafe.SizeOf<GameplayTag>(),
                IterationsPerSample = iterations, NanosecondsPerOperation = Median(times),
                NanosecondSamples = times, MinNanoseconds = times.Min(), MaxNanoseconds = times.Max(),
                MeasuredBatchMilliseconds = times.Select(value => value * iterations / 1e6).ToArray(),
                AllocatedBytesPerOperation = Median(bytes), AllocatedByteSamples = bytes,
                RetainedBytesPerRegistry = Median(retained), RetainedByteSamples = retained, RetainedCohort = 4,
                RetainedCohortByteSamples = retained.Select(value => value * 4).ToArray(),
                RetainedDeltaBelowArrayPayload = retained.Any(value => value < payload),
                GCCollectionCounts = gcCounts, Checksum = s_Checksum,
            });
            Console.WriteLine(fixture.Id + ": " + Median(times).ToString("F1") + " ns, " +
                Median(bytes) + " allocated B, " + Median(retained) + " retained B, " + payload + " array payload B");
        }
        var report = new
        {
            Variant = variant, Round = round, Runtime = RuntimeName(),
            OperatingSystem = Environment.OSVersion.ToString(), PointerSizeBytes = IntPtr.Size,
            StopwatchFrequency = Stopwatch.Frequency, Samples = samples, Quick = quick,
            TieredCompilation = false, ServerGC = System.Runtime.GCSettings.IsServerGC,
            Workload = "registry-build-v1", Measurements = results,
        };
        string json = Serialize(report);
        if (output != null) File.WriteAllText(output, json);
        else Console.WriteLine(json);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Snapshot Build(GameplayTagRegistration[] declarations)
    {
        var builder = new GameplayTagRegistrationContext();
        foreach (GameplayTagRegistration declaration in declarations)
            builder.RegisterTag(declaration.Name, declaration.Description, declaration.Flags);
        var snapshot = new Snapshot();
        builder.Build(out snapshot.Definitions, out snapshot.Tags, out snapshot.Parents, out snapshot.SubtreeEnds,
            out snapshot.HierarchyIndices, out snapshot.HierarchyTags, out snapshot.IndicesByName);
        return snapshot;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double[] MeasureRetained(GameplayTagRegistration[] declarations, int cohort, int samples)
    {
        var result = new double[samples];
        for (int sample = 0; sample < samples; sample++)
        {
            s_Retained = new Snapshot[cohort];
            long before = GC.GetTotalMemory(true);
            FillRetained(declarations, cohort);
            long after = GC.GetTotalMemory(true);
            result[sample] = (double)(after - before) / cohort;
            s_Retained = null;
            GC.GetTotalMemory(true);
        }
        return result;
    }

    // Let this frame unwind before the full-heap snapshot. In conservative GCs,
    // stale local roots in an active fill loop can distort adjacent cohorts.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FillRetained(GameplayTagRegistration[] declarations, int cohort)
    {
        for (int i = 0; i < cohort; i++) s_Retained[i] = Build(declarations);
    }

    private static Fixture[] Fixtures()
    {
        var forest = new List<GameplayTagRegistration>();
        for (int root = 0; root < 4; root++)
            for (int group = 0; group < 16; group++)
                for (int leaf = 0; leaf < 64; leaf++)
                    forest.Add(new GameplayTagRegistration("R" + root.ToString("D2") + ".G" + group.ToString("D2") + ".L" + leaf.ToString("D2")));
        string trunk = "Trunk." + string.Join(".", Enumerable.Repeat("Segment", 126));
        var branches = Enumerable.Range(0, 256).Select(i => new GameplayTagRegistration(trunk + ".L" + i.ToString("D3"))).ToArray();
        string deep = "Deep." + string.Join(".", Enumerable.Repeat("A", 2999));
        var duplicates = new List<GameplayTagRegistration>();
        for (int pass = 0; pass < 8; pass++)
            for (int leaf = 0; leaf < 1024; leaf++)
                duplicates.Add(new GameplayTagRegistration("Duplicate.A.B.C.D.E.G" + (leaf % 16).ToString("D2") + ".L" + leaf.ToString("D4"),
                    "Description " + (7 - pass), (GameplayTagFlags)(1 << (pass % 4))));
        return new[]
        {
            new Fixture { Id = "shallow-forest4096", Registrations = forest.ToArray(), Iterations = 64 },
            new Fixture { Id = "trunk-depth128-leaves256", Registrations = branches, Iterations = 64 },
            new Fixture { Id = "chain-depth3000", Registrations = new[] { new GameplayTagRegistration(deep) }, Iterations = 8 },
            new Fixture { Id = "duplicates-depth8-leaves1024-x8", Registrations = duplicates.ToArray(), Iterations = 128 },
        };
    }

    private static void Verify(GameplayTagRegistration[] declarations, Snapshot snapshot)
    {
        var expected = new Dictionary<string, ModelNode>(StringComparer.Ordinal);
        foreach (GameplayTagRegistration declaration in declarations)
        {
            string[] parts = declaration.Name.Split('.');
            string prefix = null;
            for (int depth = 0; depth < parts.Length; depth++)
            {
                string parent = prefix;
                prefix = depth == 0 ? parts[depth] : prefix + "." + parts[depth];
                if (!expected.TryGetValue(prefix, out ModelNode node))
                    expected.Add(prefix, node = new ModelNode { Name = prefix, Parent = parent, Label = parts[depth], Depth = depth + 1 });
                node.AggregateFlags |= (int)declaration.Flags;
                if (depth == parts.Length - 1)
                {
                    string text = declaration.Description ?? string.Empty;
                    if (!node.Declared || string.CompareOrdinal(text, node.Description) < 0) node.Description = text;
                    node.Declared = true;
                    node.DeclaredFlags |= (int)declaration.Flags;
                }
            }
        }
        Require(snapshot.Tags.Length == expected.Count + 1, "tag cardinality");
        Require(snapshot.IndicesByName.Count == expected.Count, "lookup cardinality");
        Require(snapshot.Tags[0] == GameplayTag.None && snapshot.Parents[0] == 0 && snapshot.SubtreeEnds[0] == 1, "None sentinel");
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (ModelNode node in expected.Values)
        {
            string parent = node.Parent ?? string.Empty;
            if (!children.TryGetValue(parent, out List<string> siblings)) children.Add(parent, siblings = new List<string>());
            siblings.Add(node.Name);
        }
        foreach (List<string> siblings in children.Values) siblings.Sort(StringComparer.Ordinal);
        var traversal = new Stack<string>();
        List<string> roots = children[string.Empty];
        for (int root = roots.Count - 1; root >= 0; root--) traversal.Push(roots[root]);
        int ordinalId = 1;
        while (traversal.Count != 0)
        {
            string name = traversal.Pop();
            Require(snapshot.Tags[ordinalId++].Name == name, "independent DFS order");
            if (children.TryGetValue(name, out List<string> siblings))
                for (int child = siblings.Count - 1; child >= 0; child--) traversal.Push(siblings[child]);
        }
        var expectedEnds = new int[snapshot.Tags.Length];
        for (int id = 1; id < snapshot.Tags.Length; id++)
        {
            GameplayTag tag = snapshot.Tags[id];
            Require(tag.RuntimeIndex == id && snapshot.IndicesByName[tag.Name] == id, "runtime index");
            ModelNode model = expected[tag.Name];
            ref readonly GameplayTagDefinition definition = ref snapshot.Definitions[id];
            Require(definition.HierarchyLevel == model.Depth && definition.Label == model.Label, "depth and label");
            Require(definition.Description == model.Description && (int)definition.Flags == (model.Declared ? model.DeclaredFlags : model.AggregateFlags), "declaration metadata");
            int parent = model.Parent == null ? 0 : snapshot.IndicesByName[model.Parent];
            Require(snapshot.Parents[id] == parent && parent < id, "parent index");
            int current = id;
            for (int depth = model.Depth - 1; depth >= 0; depth--)
            {
                int position = definition.HierarchyOffset + depth;
                Require(snapshot.HierarchyIndices[position] == current, "index path");
                Require(snapshot.HierarchyTags[position].RuntimeIndex == current && snapshot.HierarchyTags[position].Name == snapshot.Tags[current].Name, "tag path");
                current = snapshot.Parents[current];
            }
            Require(current == 0, "path reaches root");
            expectedEnds[id] = id + 1;
        }
        for (int id = snapshot.Tags.Length - 1; id > 0; id--)
        {
            Require(snapshot.SubtreeEnds[id] == expectedEnds[id], "subtree interval");
            int parent = snapshot.Parents[id];
            expectedEnds[parent] = Math.Max(expectedEnds[parent], expectedEnds[id]);
        }
        // Ordinal sibling ordering is independent of the declaration input order.
        var previousSibling = new Dictionary<int, string>();
        for (int id = 1; id < snapshot.Tags.Length; id++)
        {
            int parent = snapshot.Parents[id];
            string label = expected[snapshot.Tags[id].Name].Label;
            if (previousSibling.TryGetValue(parent, out string previous)) Require(string.CompareOrdinal(previous, label) < 0, "ordinal sibling order");
            previousSibling[parent] = label;
        }
    }

    private static double Median(double[] values)
    {
        var ordered = (double[])values.Clone();
        Array.Sort(ordered);
        return ordered.Length % 2 == 0 ? (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2 : ordered[ordered.Length / 2];
    }

    private static ulong HashFixture(GameplayTagRegistration[] declarations)
    {
        ulong hash = 14695981039346656037UL;
        foreach (GameplayTagRegistration declaration in declarations)
        {
            HashString(ref hash, declaration.Name);
            HashString(ref hash, declaration.Description ?? string.Empty);
            hash = unchecked((hash ^ (uint)declaration.Flags) * 1099511628211UL);
        }
        return hash;
    }

    private static ulong HashOutput(Snapshot snapshot)
    {
        ulong hash = 14695981039346656037UL;
        for (int id = 1; id < snapshot.Tags.Length; id++)
        {
            HashString(ref hash, snapshot.Tags[id].Name);
            HashString(ref hash, snapshot.Definitions[id].Description);
            hash = unchecked((hash ^ (uint)snapshot.Definitions[id].Flags) * 1099511628211UL);
            hash = unchecked((hash ^ (uint)snapshot.Parents[id]) * 1099511628211UL);
            hash = unchecked((hash ^ (uint)snapshot.SubtreeEnds[id]) * 1099511628211UL);
        }
        return hash;
    }

    private static void HashString(ref ulong hash, string value)
    {
        foreach (char character in value) hash = unchecked((hash ^ character) * 1099511628211UL);
        hash = unchecked((hash ^ 0xFFFFU) * 1099511628211UL);
    }

    private static string RuntimeName()
    {
        Type mono = Type.GetType("Mono.Runtime");
        if (mono != null)
        {
            var method = mono.GetMethod("GetDisplayName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return "Standalone Mono " + (method == null ? Environment.Version.ToString() : (string)method.Invoke(null, null));
        }
        return ".NET " + Environment.Version;
    }

    // Benchmark reports use only primitives, strings, arrays and public-property
    // records. This cold serializer avoids a runtime-specific JSON dependency.
    private static string Serialize(object value)
    {
        if (value == null) return "null";
        if (value is string text) return Quote(text);
        if (value is bool flag) return flag ? "true" : "false";
        if (value.GetType().IsPrimitive || value is decimal)
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        if (value is IEnumerable sequence)
        {
            var values = new List<string>();
            foreach (object item in sequence) values.Add(Serialize(item));
            return "[" + string.Join(",", values) + "]";
        }
        return "{" + string.Join(",", value.GetType().GetProperties().Select(property =>
            Quote(property.Name) + ":" + Serialize(property.GetValue(value)))) + "}";
    }

    private static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        foreach (char character in value)
        {
            if (character == '\\' || character == '"') result.Append('\\').Append(character);
            else if (character < 32) result.Append("\\u").Append(((int)character).ToString("x4"));
            else result.Append(character);
        }
        return result.Append('"').ToString();
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Registry oracle: " + message);
    }
}
