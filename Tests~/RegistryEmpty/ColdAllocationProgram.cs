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
        Fixture[] fixtures = Fixtures();
        if (args.Length == 0)
        {
            foreach (Fixture item in fixtures) Verify(item.Registrations, Build(item.Registrations));
            Console.WriteLine("PASS cold registry fixture oracle: " + fixtures.Length + " fixtures; original frozen registry oracle, root-only, child-first promotions and duplicates.");
            return;
        }
        if (args.Length != 2 || args[0] != "cold") throw new ArgumentException("cold <fixture index>");
        int fixtureIndex = int.Parse(args[1], CultureInfo.InvariantCulture);
        Fixture fixture = fixtures[fixtureIndex];
        ulong fixtureHash = HashFixture(fixture.Registrations);
        // Inputs and the allocation-counter delegate are initialized outside the
        // interval. No registry build, registration or snapshot verification has
        // run in this fresh process before the single measured build.
        long initializeCounter = AllocatedBytes();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = AllocatedBytes();
        s_Last = Build(fixture.Registrations);
        long allocated = AllocatedBytes() - before;
        Verify(fixture.Registrations, s_Last);
        int distinctDeclarations = fixture.Registrations.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count();
        Console.WriteLine(Serialize(new {
            Workload = "registry-declared-duplicate-cold-v1", CaseId = fixture.Id,
            Runtime = RuntimeName(), PointerSizeBytes = IntPtr.Size,
            Measurement = "first registry build in fresh process; inputs excluded; no warmup",
            BuildCount = 1, DeclarationCount = fixture.Registrations.Length,
            DistinctDeclaredNames = distinctDeclarations, RegistryTagCount = s_Last.Tags.Length - 1,
            FixtureChecksum = fixtureHash.ToString("X16"), OutputChecksum = HashOutput(s_Last).ToString("X16"),
            FrozenLayoutChecksum = HashFrozenLayout(s_Last).ToString("X16"),
            HierarchyPathSlots = s_Last.HierarchyIndices.Length,
            FrozenArrayPayloadBytes = s_Last.PayloadBytes,
            DefinitionSizeBytes = Unsafe.SizeOf<GameplayTagDefinition>(), TagSizeBytes = Unsafe.SizeOf<GameplayTag>(),
            AllocatedBytes = allocated, Oracle = "passed", TimingMeasured = false,
            CounterInitializedBeforeMeasurement = initializeCounter >= 0
        }));
        GC.KeepAlive(s_Last);
    }

    private static ulong HashFrozenLayout(Snapshot value)
    {
        ulong hash = HashOutput(value);
        foreach (int number in value.Parents.Concat(value.SubtreeEnds).Concat(value.HierarchyIndices))
            hash = unchecked((hash ^ (uint)number) * 1099511628211UL);
        foreach (GameplayTag tag in value.Tags.Concat(value.HierarchyTags))
        {
            HashString(ref hash, tag.Name ?? string.Empty);
            hash = unchecked((hash ^ (uint)tag.RuntimeIndex) * 1099511628211UL);
        }
        foreach (GameplayTagDefinition definition in value.Definitions)
        {
            HashString(ref hash, definition.Label ?? string.Empty);
            hash = unchecked((hash ^ (uint)definition.HierarchyLevel) * 1099511628211UL);
            hash = unchecked((hash ^ (uint)definition.HierarchyOffset) * 1099511628211UL);
        }
        return hash;
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
        var shortRoots = Enumerable.Range(0, 4096).Select(i =>
            new GameplayTagRegistration("R" + i.ToString("D7"))).ToArray();
        string padding = new string('X', 248);
        var longRoots = Enumerable.Range(0, 4096).Select(i =>
            new GameplayTagRegistration("R" + i.ToString("D7") + padding)).ToArray();
        var promotions = new List<GameplayTagRegistration>();
        for (int root = 0; root < 64; root++)
            for (int leaf = 0; leaf < 16; leaf++)
                promotions.Add(new GameplayTagRegistration("Promote.R" + root.ToString("D2") + ".G" + (leaf % 4).ToString("D2") + ".L" + leaf.ToString("D2"), "leaf", (GameplayTagFlags)1));
        for (int root = 0; root < 64; root++)
            for (int group = 0; group < 4; group++)
                promotions.Add(new GameplayTagRegistration("Promote.R" + root.ToString("D2") + ".G" + group.ToString("D2"), "group", (GameplayTagFlags)2));
        for (int root = 0; root < 64; root++)
            promotions.Add(new GameplayTagRegistration("Promote.R" + root.ToString("D2"), "root", (GameplayTagFlags)4));
        promotions.Add(new GameplayTagRegistration("Promote", "top", (GameplayTagFlags)8));
        var promotionDuplicates = Enumerable.Range(0, 8).SelectMany(pass => promotions.Select(item =>
            new GameplayTagRegistration(item.Name, "Pass " + (7 - pass), (GameplayTagFlags)(1 << (pass % 4))))).ToArray();
        var rootDuplicates = Enumerable.Range(0, 8).SelectMany(pass => shortRoots.Select(item =>
            new GameplayTagRegistration(item.Name, "Pass " + (7 - pass), (GameplayTagFlags)(1 << (pass % 4))))).ToArray();
        return new[]
        {
            new Fixture { Id = "shallow-forest4096", Registrations = forest.ToArray(), Iterations = 1 },
            new Fixture { Id = "trunk-depth128-leaves256", Registrations = branches, Iterations = 1 },
            new Fixture { Id = "chain-depth3000", Registrations = new[] { new GameplayTagRegistration(deep) }, Iterations = 1 },
            new Fixture { Id = "duplicates-depth8-leaves1024-x8", Registrations = duplicates.ToArray(), Iterations = 1 },
            new Fixture { Id = "unique-roots4096-name8", Registrations = shortRoots, Iterations = 1 },
            new Fixture { Id = "unique-roots4096-name256", Registrations = longRoots, Iterations = 1 },
            new Fixture { Id = "unique-child-first-promotions1345", Registrations = promotions.ToArray(), Iterations = 1 },
            new Fixture { Id = "duplicates-child-first-promotions1345-x8", Registrations = promotionDuplicates, Iterations = 1 },
            new Fixture { Id = "duplicates-roots4096-x8", Registrations = rootDuplicates, Iterations = 1 },
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
