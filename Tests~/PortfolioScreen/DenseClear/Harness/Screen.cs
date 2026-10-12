using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using GameplayTags;

internal static class Program
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly FieldInfo MaskField = typeof(TagStorage).GetField("m_BucketMask", Private);
    static readonly FieldInfo BucketsField = typeof(TagStorage).GetField("m_Buckets", Private);
    static readonly Func<long> Allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>),
        typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static));
    static GameplayTag[] Tags;
    static int Checks;
    static string Q(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    static void Check(bool value, string message) { Checks++; if (!value) throw new InvalidOperationException(message); }
    static int Mask(GameplayTagContainer c) => (int)MaskField.GetValue(c.Indices.Storage);
    static int[] Buckets(GameplayTagContainer c) => (int[])BucketsField.GetValue(c.Indices.Storage);

    sealed class Row
    {
        internal string Id;
        internal int Keep, Reserve, Grow, ExpectedMask, PilotCount;
        internal GameplayTagContainer Target;
        internal TagStorage.Entry[] Entries;
        internal int[] Explicit, BucketArray, OriginalBuckets;
        internal int InitialMask;
        internal long ExpectedChecksum => Keep * 2L + 1;
    }

    static void Fill(GameplayTagContainer target, int keep)
    {
        for (int i = 0; i < keep; i++) target.AddTag(Tags[i]);
    }

    // This is the measured unit: refill through the old public API, observe, Clear.
    // The two empty rows execute no AddTag calls. No Copy occurs in a timed fixture.
    static long Cycle(Row row)
    {
        GameplayTagContainer target = row.Target;
        Fill(target, row.Keep);
        long checksum = target.TagCount + target.ExplicitTagCount + 1L;
        target.Clear();
        return checksum;
    }

    static void Retained(Row row)
    {
        var storage = row.Target.Indices.Storage;
        Check(ReferenceEquals(row.Entries, storage.Entries), "retained Entries identity");
        Check(ReferenceEquals(row.Explicit, storage.ExplicitIndices), "retained ExplicitIndices identity");
        Check(ReferenceEquals(row.BucketArray, Buckets(row.Target)), "retained bucket identity");
        Check(Mask(row.Target) == row.ExpectedMask, "retained active mask");
    }

    static void Empty(Row row)
    {
        Retained(row);
        Check(row.Target.IsEmpty && row.Target.TagCount == 0 && row.Target.ExplicitTagCount == 0, "empty cardinalities");
        foreach (GameplayTag tag in Tags)
            Check(!row.Target.HasTag(tag) && !row.Target.HasTagExact(tag), "empty full registered membership");
        int active = row.ExpectedMask == 0 ? 0 : row.ExpectedMask + 1;
        for (int i = 0; i < row.BucketArray.Length; i++)
            Check(row.BucketArray[i] == (i < active ? 0 : row.OriginalBuckets[i]), "zero active buckets, unchanged inactive suffix");
    }

    static void ClearPreservesState(Row row)
    {
        var entries = (TagStorage.Entry[])row.Entries.Clone();
        var explicitIndices = (int[])row.Explicit.Clone();
        var buckets = (int[])row.BucketArray.Clone();
        row.Target.Clear();
        Retained(row);
        Check(row.Target.TagCount == 0 && row.Target.ExplicitTagCount == 0, "Clear resets both counts");
        for (int i = 0; i < entries.Length; i++)
        {
            TagStorage.Entry a = entries[i], b = row.Entries[i];
            Check(a.Id == b.Id && a.Count == b.Count && a.ExplicitCount == b.ExplicitCount && a.ExplicitIndex == b.ExplicitIndex,
                "Clear preserves complete Entry fields, including inactive tails");
        }
        for (int i = 0; i < explicitIndices.Length; i++)
            Check(explicitIndices[i] == row.Explicit[i], "Clear preserves complete explicit mapping including tail");
        int active = row.ExpectedMask == 0 ? 0 : row.ExpectedMask + 1;
        for (int i = 0; i < buckets.Length; i++)
            Check(row.BucketArray[i] == (i < active ? 0 : buckets[i]), "Clear zeroes only active buckets and preserves suffix");
        Empty(row);
    }

    static Row Make(string id, int keep, int reserve, int grow, int mask, int pilot)
    {
        var row = new Row { Id = id, Keep = keep, Reserve = reserve, Grow = grow, ExpectedMask = mask, PilotCount = pilot,
            Target = new GameplayTagContainer() };
        if (reserve != 0) row.Target.EnsureCapacity(reserve);
        Fill(row.Target, grow);
        for (int i = keep; i < grow; i++) row.Target.RemoveTag(Tags[i]);
        Check(row.Target.TagCount == keep && row.Target.ExplicitTagCount == keep, "exact root-only live count");
        for (int i = 0; i < Tags.Length; i++)
            Check(row.Target.HasTagExact(Tags[i]) == (i < keep), "prepared membership");
        row.Entries = row.Target.Indices.Storage.Entries;
        row.Explicit = row.Target.Indices.Storage.ExplicitIndices;
        row.BucketArray = Buckets(row.Target);
        row.OriginalBuckets = (int[])row.BucketArray.Clone();
        row.InitialMask = Mask(row.Target);
        Check(row.InitialMask == mask, "actual active mask matches fixed geometry");
        ClearPreservesState(row);
        Check(Cycle(row) == row.ExpectedChecksum, "one public refill/Clear cycle checksum");
        Empty(row);
        // Full regrowth to the original high-water mark, then reverse drain, untimed.
        Fill(row.Target, grow);
        Check(row.Target.TagCount == grow && row.Target.ExplicitTagCount == grow, "full regrowth cardinalities");
        for (int i = 0; i < grow; i++) Check(row.Target.HasTagExact(Tags[i]), "full regrowth membership");
        for (int i = grow - 1; i >= 0; i--) row.Target.RemoveTag(Tags[i]);
        Empty(row);
        return row;
    }

    static bool Policy(int count, int mask) => count >= 64 && mask != 0 && mask + 1 <= (long)count * 8;

    static void ScalarChecks()
    {
        // Pure scalar checks only, not fabricated giant storage or performance evidence.
        int[] counts = { 63, 64, 65, 127, 128, 129, 268435455, 268435456, 536870912, int.MaxValue };
        long[] products = { 504L, 512L, 520L, 1016L, 1024L, 1032L, 2147483640L, 2147483648L, 4294967296L, 17179869176L };
        bool[] selected = { false, false, false, false, true, true, true, true, true, true };
        for (int i = 0; i < counts.Length; i++)
        {
            Check(checked((long)counts[i] * 8) == products[i], "wide scalar product cannot overflow at positive int Count");
            Check(Policy(counts[i], 1023) == selected[i], "wide dispatch policy boundary");
        }
        Check(!Policy(64, 0), "mask-zero policy remains excluded");
        Check(Policy(64, 511) && !Policy(63, 511) && !Policy(64, 1023), "minimum and density criteria remain independent");
    }

    static void InactiveSuffixCheck()
    {
        var target = new GameplayTagContainer(8192);
        Fill(target, 1024);
        var source = new GameplayTagContainer(); Fill(source, 64);
        // This separate untimed semantic control intentionally exposes retained stale suffix.
        // It is not one of the sparse timing fixtures and does not define their restore path.
        GameplayTagContainer.Copy(target, source);
        var row = new Row { Target = target, Keep = 64, ExpectedMask = 127,
            Entries = target.Indices.Storage.Entries, Explicit = target.Indices.Storage.ExplicitIndices,
            BucketArray = Buckets(target) };
        row.OriginalBuckets = (int[])row.BucketArray.Clone();
        Check(Mask(target) == 127 && row.BucketArray.Length == 16384, "real retained inactive bucket suffix");
        Check(row.BucketArray.Skip(128).Any(value => value != 0), "suffix actually contains stale nonzero slots");
        ClearPreservesState(row);
        Fill(target, 1024);
        Check(target.TagCount == 1024 && target.ExplicitTagCount == 1024, "grow across stale suffix safely");
        for (int i = 0; i < 1024; i++) Check(target.HasTagExact(Tags[i]), "regrown retained suffix membership");
        for (int i = 1023; i >= 0; i--) target.RemoveTag(Tags[i]);
        Check(target.IsEmpty && target.TagCount == 0, "regrown retained suffix drains");
    }

    static Row[] Fixtures()
    {
        GameplayTagManager.Initialize(Enumerable.Range(0, 1024).Select(i => new GameplayTagRegistration("Node" + i)).ToArray());
        Tags = Enumerable.Range(0, 1024).Select(i => GameplayTagManager.RequestTag("Node" + i)).ToArray();
        Check(Tags.All(tag => tag.HierarchyLevel == 1), "all tags are roots, closure equals explicit count");
        ScalarChecks(); InactiveSuffixCheck();
        return new[] {
            Make("empty-linear/empty-clear", 0, 0, 0, 0, 1048576),
            Make("empty-retained/empty-clear", 0, 8192, 1024, 16383, 1048576),
            Make("tiny8-linear/refill-clear-cycle", 8, 0, 8, 0, 131072),
            Make("fresh9-hashed/refill-clear-cycle", 9, 0, 9, 31, 65536),
            Make("fresh63/refill-clear-cycle", 63, 0, 63, 127, 8192),
            Make("fresh64/refill-clear-cycle", 64, 0, 64, 127, 8192),
            Make("fresh65/refill-clear-cycle", 65, 0, 65, 255, 8192),
            Make("fresh1024/refill-clear-cycle", 1024, 0, 1024, 2047, 1024),
            Make("density127/refill-clear-cycle", 127, 512, 512, 1023, 4096),
            Make("density128/refill-clear-cycle", 128, 512, 512, 1023, 4096),
            Make("density129/refill-clear-cycle", 129, 512, 512, 1023, 4096),
            Make("retained-sparse16/refill-clear-cycle", 16, 8192, 1024, 16383, 32768)
        };
    }

    static int Warm(Row row)
    {
        long checksum = 0;
        for (int i = 0; i < 1024; i++) checksum += Cycle(row);
        Check(checksum == row.ExpectedChecksum * 1024, "warm checksum");
        Empty(row);
        return Mask(row.Target);
    }

    static string Geometry(Row row, int warmMask, int finalMask) =>
        ",\"Keep\":" + row.Keep + ",\"InitialMask\":" + row.InitialMask + ",\"WarmedMask\":" + warmMask +
        ",\"FinalMask\":" + finalMask + ",\"EntriesCapacity\":" + row.Entries.Length +
        ",\"ExplicitCapacity\":" + row.Explicit.Length + ",\"BucketsCapacity\":" + row.BucketArray.Length;

    static void Preflight(Row[] rows)
    {
        foreach (Row row in rows)
        {
            int warmed = Warm(row);
            Console.WriteLine("{\"Mode\":\"preflight\",\"Id\":" + Q(row.Id) + ",\"Checks\":" + Checks +
                Geometry(row, warmed, Mask(row.Target)) + ",\"ChecksumPerCycle\":" + row.ExpectedChecksum + "}");
        }
    }

    static void Measure(Row row, int iterations, string mode)
    {
        int warmed = Warm(row);
        Stopwatch.GetTimestamp(); Allocated();
        GC.CollectionCount(0); GC.CollectionCount(1); GC.CollectionCount(2);
        long checksum = 0, allocated = Allocated();
        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++) checksum += Cycle(row);
        long ticks = Stopwatch.GetTimestamp() - start;
        long bytes = Allocated() - allocated;
        int dg0 = GC.CollectionCount(0) - gc0, dg1 = GC.CollectionCount(1) - gc1, dg2 = GC.CollectionCount(2) - gc2;
        Check(checksum == row.ExpectedChecksum * iterations, "measured refill/Clear checksum");
        Empty(row);
        Console.WriteLine("{\"Mode\":" + Q(mode) + ",\"Id\":" + Q(row.Id) + ",\"Iterations\":" + iterations +
            ",\"BatchMs\":" + N(ticks * 1000.0 / Stopwatch.Frequency) +
            ",\"Ns\":" + N(ticks * 1000000000.0 / Stopwatch.Frequency / iterations) +
            ",\"Bytes\":" + bytes + ",\"Checksum\":" + checksum +
            ",\"Gc0\":" + dg0 + ",\"Gc1\":" + dg1 + ",\"Gc2\":" + dg2 +
            Geometry(row, warmed, Mask(row.Target)) + "}");
    }

    static int Main(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException("preflight, pilot, or timing is required");
        Row[] rows = Fixtures();
        if (args[0] == "preflight" && args.Length == 1) { Preflight(rows); return 0; }
        if (args[0] == "pilot" && args.Length == 1)
        {
            foreach (Row row in rows) Measure(row, row.PilotCount, "pilot");
            return 0;
        }
        if (args[0] != "timing" || args.Length != 3 || args[1] != "--iterations-file")
            throw new ArgumentException("timing --iterations-file <file> is required");
        string[] lines = File.ReadAllLines(args[2]);
        Check(lines.Length == rows.Length, "exact iteration row count");
        for (int i = 0; i < rows.Length; i++)
        {
            string[] fields = lines[i].Split('\t');
            Check(fields.Length == 2 && fields[0] == rows[i].Id, "fixed iteration row order");
            int count = int.Parse(fields[1], CultureInfo.InvariantCulture);
            Check(count >= rows[i].PilotCount, "iteration count not below pilot seed");
            Measure(rows[i], count, "timing");
        }
        return 0;
    }
}
