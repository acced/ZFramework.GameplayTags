using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using GameplayTags;

internal static class Program
{
    const int CohortSize = 128, PilotIntervals = 8, WarmupIntervals = 4;
    static readonly FieldInfo MaskField = typeof(TagStorage).GetField("m_BucketMask", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo BucketsField = typeof(TagStorage).GetField("m_Buckets", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly Func<long> Allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>),
        typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static));
    static GameplayTag[] Tags;
    static int Checks;
    sealed class Case
    {
        internal string Id;
        internal int Keep, Reserve, Grow, Mask;
        internal Case(string id, int keep, int reserve, int grow, int mask)
        { Id = id; Keep = keep; Reserve = reserve; Grow = grow; Mask = mask; }
    }
    static readonly Case[] Cases = {
        new Case("empty-retained", 0, 8192, 1024, 16383),
        new Case("tiny8-linear", 8, 0, 8, 0),
        new Case("fresh9-hashed", 9, 0, 9, 31),
        new Case("fresh63", 63, 0, 63, 127),
        new Case("fresh64", 64, 0, 64, 127),
        new Case("fresh1024", 1024, 0, 1024, 2047),
        new Case("retained-sparse16", 16, 8192, 1024, 16383),
        new Case("density127", 127, 512, 512, 1023),
        new Case("density128", 128, 512, 512, 1023)
    };
    sealed class Fixture
    {
        internal Case Spec;
        internal GameplayTagContainer Target;
        internal TagStorage.Entry[] Entries;
        internal int[] Explicit, Buckets;
    }
    static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string N(double n) => n.ToString("R", CultureInfo.InvariantCulture);
    static void Check(bool value, string message) { Checks++; if (!value) throw new InvalidOperationException(message); }
    static int Mask(Fixture f) => (int)MaskField.GetValue(f.Target.Indices.Storage);
    static void Fill(Fixture f) { for (int i = 0; i < f.Spec.Keep; i++) f.Target.AddTag(Tags[i]); }
    static void Live(Fixture f) => Check(f.Target.TagCount == f.Spec.Keep && f.Target.ExplicitTagCount == f.Spec.Keep, "exact live refill count");
    static void Empty(Fixture f) => Check(f.Target.TagCount == 0 && f.Target.ExplicitTagCount == 0 && f.Target.IsEmpty, "Clear count reset");
    static void Identity(Fixture f)
    {
        TagStorage s = f.Target.Indices.Storage;
        Check(ReferenceEquals(s.Entries, f.Entries) && ReferenceEquals(s.ExplicitIndices, f.Explicit) &&
            ReferenceEquals(BucketsField.GetValue(s), f.Buckets) && Mask(f) == f.Spec.Mask, "owned arrays and active mask retained");
    }
    // No Clear, Copy, target-method preparation or target warmup occurs here.
    static Fixture Prepare(Case spec)
    {
        var f = new Fixture { Spec = spec, Target = new GameplayTagContainer() };
        if (spec.Reserve != 0) f.Target.EnsureCapacity(spec.Reserve);
        for (int i = 0; i < spec.Grow; i++) f.Target.AddTag(Tags[i]);
        for (int i = spec.Keep; i < spec.Grow; i++) f.Target.RemoveTag(Tags[i]);
        f.Entries = f.Target.Indices.Storage.Entries; f.Explicit = f.Target.Indices.Storage.ExplicitIndices;
        f.Buckets = (int[])BucketsField.GetValue(f.Target.Indices.Storage);
        Live(f); Identity(f);
        for (int i = 0; i < Tags.Length; i++) Check(f.Target.HasTagExact(Tags[i]) == (i < spec.Keep), "prepared exact membership");
        return f;
    }
    static string Hash(byte[] bytes)
    {
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    static string StateHash(Fixture f, bool expectedClear, bool capacityOnly = false)
    {
        using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
        {
            writer.Write(Mask(f)); writer.Write(f.Entries.Length); writer.Write(f.Explicit.Length); writer.Write(f.Buckets.Length);
            if (!capacityOnly)
            {
                writer.Write(expectedClear ? 0 : f.Target.TagCount); writer.Write(expectedClear ? 0 : f.Target.ExplicitTagCount);
                foreach (TagStorage.Entry e in f.Entries) { writer.Write(e.Id); writer.Write(e.Count); writer.Write(e.ExplicitCount); writer.Write(e.ExplicitIndex); }
                foreach (int value in f.Explicit) writer.Write(value);
                int active = f.Spec.Mask == 0 ? 0 : f.Spec.Mask + 1;
                for (int i = 0; i < f.Buckets.Length; i++) writer.Write(expectedClear && i < active ? 0 : f.Buckets[i]);
            }
            writer.Flush(); return Hash(stream.ToArray());
        }
    }
    static string CohortHash(Fixture[] cohort, bool expectedClear)
    {
        string joined = string.Join("", cohort.Select(f => StateHash(f, expectedClear)));
        return Hash(System.Text.Encoding.ASCII.GetBytes(joined));
    }
    static string Geometry(Fixture f) => ",\"Keep\":" + f.Spec.Keep + ",\"ActiveMask\":" + Mask(f) +
        ",\"EntriesCapacity\":" + f.Entries.Length + ",\"ExplicitCapacity\":" + f.Explicit.Length + ",\"BucketsCapacity\":" + f.Buckets.Length;
    static long[] ClockOverhead()
    {
        Stopwatch.GetTimestamp(); Allocated(); GC.CollectionCount(0); GC.CollectionCount(1); GC.CollectionCount(2);
        var ticks = new long[32];
        for (int i = 0; i < ticks.Length; i++) { long start = Stopwatch.GetTimestamp(); ticks[i] = Stopwatch.GetTimestamp() - start; }
        Array.Sort(ticks); return new[] { ticks[0], ticks[ticks.Length / 2], ticks[ticks.Length - 1] };
    }
    static string Clock(long[] clock) => ",\"StopwatchFrequency\":" + Stopwatch.Frequency +
        ",\"TimestampMinTicks\":" + clock[0] + ",\"TimestampMedianTicks\":" + clock[1] + ",\"TimestampMaxTicks\":" + clock[2];
    static long ClearCohort(Fixture[] cohort)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < cohort.Length; i++) cohort[i].Target.Clear();
        return Stopwatch.GetTimestamp() - start;
    }
    static void Refill(Fixture[] cohort) { foreach (Fixture f in cohort) { Fill(f); Live(f); } }
    static void ObserveEmpty(Fixture[] cohort) { foreach (Fixture f in cohort) Empty(f); }
    static Fixture[] BuildCohort(Case spec) => Enumerable.Range(0, CohortSize).Select(_ => Prepare(spec)).ToArray();
    static void Preflight()
    {
        foreach (Case spec in Cases)
        {
            Fixture[] cohort = BuildCohort(spec); string expected = CohortHash(cohort, true);
            // Untimed fixture validation does not call any measurement route.
            foreach (Fixture f in cohort) f.Target.Clear();
            ObserveEmpty(cohort); foreach (Fixture f in cohort) Identity(f);
            Check(CohortHash(cohort, false) == expected, "expected full Clear transformation");
            Refill(cohort); foreach (Fixture f in cohort) f.Target.Clear();
            ObserveEmpty(cohort); foreach (Fixture f in cohort) Identity(f);
            string actual = CohortHash(cohort, false); Check(actual == expected, "refill/Clear full-state continuation");
            Console.WriteLine("{\"Mode\":\"preflight\",\"Id\":" + Q(spec.Id) + ",\"Checks\":" + Checks + ",\"Cohort\":" + CohortSize +
                Geometry(cohort[0]) + ",\"ExpectedStateHash\":" + Q(expected) + ",\"AfterStateHash\":" + Q(actual) + "}");
        }
    }
    static void WarmMeasure(Case spec, int intervals, string mode)
    {
        Fixture[] cohort = BuildCohort(spec); string expected = CohortHash(cohort, true);
        for (int i = 0; i < WarmupIntervals; i++) { Refill(cohort); ClearCohort(cohort); ObserveEmpty(cohort); }
        foreach (Fixture f in cohort) Identity(f);
        long[] clock = ClockOverhead(), ticks = new long[intervals]; long bytes = 0; int gc0 = 0, gc1 = 0, gc2 = 0;
        long wallStart = Stopwatch.GetTimestamp();
        for (int i = 0; i < intervals; i++)
        {
            Refill(cohort);
            long allocated = Allocated(); int a = GC.CollectionCount(0), b = GC.CollectionCount(1), c = GC.CollectionCount(2);
            ticks[i] = ClearCohort(cohort);
            bytes += Allocated() - allocated;
            gc0 += GC.CollectionCount(0) - a; gc1 += GC.CollectionCount(1) - b; gc2 += GC.CollectionCount(2) - c;
            ObserveEmpty(cohort);
        }
        long wallTicks = Stopwatch.GetTimestamp() - wallStart;
        foreach (Fixture f in cohort) Identity(f);
        string actual = CohortHash(cohort, false); Check(actual == expected, "measured Clear expected full-state transformation");
        long total = ticks.Sum(), minimum = ticks.Min();
        Console.WriteLine("{\"Mode\":" + Q(mode) + ",\"Id\":" + Q(spec.Id) + ",\"Intervals\":" + intervals + ",\"Cohort\":" + CohortSize +
            ",\"ClearTicks\":" + total + ",\"MinimumIntervalTicks\":" + minimum + ",\"IntervalTicks\":[" + string.Join(",", ticks) + "]" +
            ",\"ClearMs\":" + N(total * 1000.0 / Stopwatch.Frequency) + ",\"WallMs\":" + N(wallTicks * 1000.0 / Stopwatch.Frequency) +
            ",\"Ns\":" + N(total * 1000000000.0 / Stopwatch.Frequency / intervals / CohortSize) + ",\"Bytes\":" + bytes +
            ",\"Gc0\":" + gc0 + ",\"Gc1\":" + gc1 + ",\"Gc2\":" + gc2 + Clock(clock) + Geometry(cohort[0]) +
            ",\"ExpectedStateHash\":" + Q(expected) + ",\"AfterStateHash\":" + Q(actual) + "}");
    }
    static void Cold(Case spec)
    {
        Fixture fixture = Prepare(spec); string before = StateHash(fixture, false), expected = StateHash(fixture, true);
        string capacityBefore = StateHash(fixture, false, true); long[] clock = ClockOverhead();
        // First public Clear call in this process. Caller JIT may precede the timestamp.
        long allocated = Allocated(); int a = GC.CollectionCount(0), b = GC.CollectionCount(1), c = GC.CollectionCount(2);
        long start = Stopwatch.GetTimestamp(); fixture.Target.Clear(); long ticks = Stopwatch.GetTimestamp() - start;
        long bytes = Allocated() - allocated; int gc0 = GC.CollectionCount(0) - a, gc1 = GC.CollectionCount(1) - b, gc2 = GC.CollectionCount(2) - c;
        Empty(fixture); Identity(fixture);
        string actual = StateHash(fixture, false), capacityAfter = StateHash(fixture, false, true);
        Check(actual == expected && capacityBefore == capacityAfter, "cold expected state and retained capacity");
        Console.WriteLine("{\"Mode\":\"cold\",\"Id\":" + Q(spec.Id) + ",\"ClearTicks\":" + ticks +
            ",\"Ns\":" + N(ticks * 1000000000.0 / Stopwatch.Frequency) + ",\"Bytes\":" + bytes +
            ",\"Gc0\":" + gc0 + ",\"Gc1\":" + gc1 + ",\"Gc2\":" + gc2 + Clock(clock) + Geometry(fixture) +
            ",\"BeforeStateHash\":" + Q(before) + ",\"ExpectedStateHash\":" + Q(expected) + ",\"AfterStateHash\":" + Q(actual) +
            ",\"BeforeCapacityHash\":" + Q(capacityBefore) + ",\"AfterCapacityHash\":" + Q(capacityAfter) + "}");
    }
    static int Main(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException("preflight, pilot, timing, or cold is required");
        GameplayTagManager.Initialize(Enumerable.Range(0, 1024).Select(i => new GameplayTagRegistration("Node" + i)).ToArray());
        Tags = Enumerable.Range(0, 1024).Select(i => GameplayTagManager.RequestTag("Node" + i)).ToArray();
        Check(Tags.All(tag => tag.HierarchyLevel == 1), "root-only fixtures");
        if (args[0] == "preflight" && args.Length == 1) { Preflight(); return 0; }
        if (args[0] == "cold" && args.Length == 2) { Cold(Cases.Single(c => c.Id == args[1])); return 0; }
        if (args[0] == "pilot" && args.Length == 1) { foreach (Case spec in Cases) WarmMeasure(spec, PilotIntervals, "pilot"); return 0; }
        if (args[0] != "timing" || args.Length != 3 || args[1] != "--iterations-file") throw new ArgumentException("timing --iterations-file <file> required");
        string[] lines = File.ReadAllLines(args[2]); Check(lines.Length == Cases.Length, "fixed row count");
        for (int i = 0; i < Cases.Length; i++)
        {
            string[] fields = lines[i].Split('\t'); Check(fields.Length == 2 && fields[0] == Cases[i].Id, "fixed row order");
            int count = int.Parse(fields[1], CultureInfo.InvariantCulture); Check(count >= 1 && count <= 4096, "fixed interval bounds");
            WarmMeasure(Cases[i], count, "timing");
        }
        return 0;
    }
}
