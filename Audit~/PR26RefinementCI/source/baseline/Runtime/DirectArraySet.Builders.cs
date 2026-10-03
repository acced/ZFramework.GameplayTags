// Independently owned builders for the Micro16/dense Runtime set.
using System;
using System.Runtime.CompilerServices;
using Entry = System.UInt32;
namespace GameplayTags.Experiments
{
    public sealed partial class DirectArraySet
    {
        /// <summary>Strict sorted unique handles, independently owned result.
        /// Fresh storage can be filled while validating: an invalid call publishes no result.
        /// The caller must not mutate the input concurrently.</summary>
        public static DirectArraySet FromSortedUnique(TagRegistry registry,
            ReadOnlySpan<RuntimeTag> tags, int capacity = 0, BitmapLayout storage = BitmapLayout.Auto)
        {
            if (tags.IsEmpty) return new DirectArraySet(registry, capacity, storage);
            int reserved = ValidateBuildOptions(registry, capacity, storage, tags.Length);
            if (tags.Length == 1)
            {
                RuntimeTag tag = tags[0];
                RequireHandle(registry, tag);
                var one = new DirectArraySet(registry, reserved, WantsDense(registry, reserved, storage));
                one.StoreSingle(tag.Id);
                return one;
            }
            var result = new DirectArraySet(registry, reserved, WantsDense(registry, reserved, storage));
            result.ValidateAndFillSorted(tags);
            return result;
        }

        /// <summary>Unordered input with duplicates; strict handle ownership.
        /// Dense sets bits directly.
        /// Micro and oversized duplicate input use an owned sorting workspace.
        /// No caller memory is borrowed, sorted or retained.</summary>
        public static DirectArraySet FromUnordered(TagRegistry registry,
            ReadOnlySpan<RuntimeTag> tags, int capacity = 0, BitmapLayout storage = BitmapLayout.Auto)
        {
            if (tags.IsEmpty) return new DirectArraySet(registry, capacity, storage);
            int reserved = ValidateBuildOptions(registry, capacity, storage, tags.Length);
            if (tags.Length == 1)
            {
                RuntimeTag tag = tags[0];
                RequireHandle(registry, tag);
                var one = new DirectArraySet(registry, reserved, WantsDense(registry, reserved, storage));
                one.StoreSingle(tag.Id);
                return one;
            }
            bool makeDense = WantsDense(registry, reserved, storage);
            if (makeDense)
            {
                var result = new DirectArraySet(registry, reserved, true);
                ulong[] words = result.dense;
                int unique = 0;
                for (int i = 0; i < tags.Length; i++)
                {
                    RuntimeTag tag = tags[i];
                    RequireHandle(registry, tag);
                    int w = tag.Id >> 6; ulong bit = 1UL << (tag.Id & 63);
                    ulong before = words[w];
                    unique += (before & bit) == 0 ? 1 : 0;
                    words[w] = before | bit;
                }
                result.count = unique;
                return result;
            }
            int[] ids = new int[tags.Length];
            for (int i = 0; i < tags.Length; i++)
            { RuntimeTag tag = tags[i]; RequireHandle(registry, tag); ids[i] = tag.Id; }
            Array.Sort(ids);
            int size = CompactUnique(ids, ids.Length);
            var compact = new DirectArraySet(registry, reserved, false);
            compact.FillSortedIds(ids, size);
            return compact;
        }

        /// <summary>Overwrite, not append. Complete admission precedes every mutation.
        /// Empty and singleton inputs skip only work that is mathematically unnecessary.
        /// Sufficient capacity implies no managed allocation; storage kind never changes.</summary>
        public void ResetFromSortedUnique(ReadOnlySpan<RuntimeTag> tags)
        {
            ValidateSortedHandles(Registry, tags);
            EnsureCapacity(tags.Length);
            Clear();
            FillSortedHandles(tags);
        }

        /// <summary>Explicit independent copy/conversion. Auto is not a target.
        /// Target reserves max(Count, capacity), clamped to the registry, not source Capacity.
        /// The normal copy constructor continues to preserve source capacity unchanged.</summary>
        public DirectArraySet CopyAsStorage(BitmapLayout storage, int capacity = 0)
        {
            if (storage != (BitmapLayout)1 && storage != BitmapLayout.Dense)
                throw new ArgumentOutOfRangeException(nameof(storage));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            int reserved = Math.Min(Registry.Count, Math.Max(capacity, Count));
            var result = new DirectArraySet(Registry, reserved, storage == BitmapLayout.Dense);
            // Count==0 implies no member bits; new storage is already zero initialized.
            // Still allocate the independent result and honor the explicit target capacity.
            if (Count == 0) return result;
            if (dense != null && result.dense != null)
                Array.Copy(dense, result.dense, dense.Length);
            else if (dense == null && result.dense == null)
            {
                if (used == 1) result.Write(0, Read(0));
                else if (used > 1) Array.Copy(entries, result.entries, used);
                result.used = used;
            }
            else if (result.dense != null)
            {
                for (int i = 0; i < used; i++)
                {
                    Entry entry = Read(i);
                    int first = (int)((entry >> MaskBits) << Shift);
                    result.dense[first >> 6] |= (ulong)(entry & Mask) << (first & 63);
                }
            }
            else
            {
                int write = 0;
                for (int w = 0; w < dense.Length; w++)
                {
                    ulong word = dense[w];
                    if (word == 0) continue;
                    for (int bit = 0; bit < 64; bit += 1 << Shift)
                    {
                        Entry mask = (Entry)(word >> bit) & Mask;
                        if (mask != 0) result.Write(write++, Key((w << 6) + bit) | mask);
                    }
                }
                result.used = write;
            }
            result.count = count;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RequireHandle(TagRegistry registry, RuntimeTag tag)
        {
            if (!ReferenceEquals(tag.Owner, registry))
                throw new ArgumentException("All input handles must belong to this registry.", "tags");
        }
        private static int CompactUnique(int[] ids, int n)
        {
            int unique = 0;
            for (int i = 0; i < n; i++)
                if (unique == 0 || ids[i] != ids[unique - 1]) ids[unique++] = ids[i];
            return unique;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void StoreSingle(int id)
        {
            if (dense != null) dense[id >> 6] = 1UL << (id & 63);
            else { Write(0, Key(id) | Bit(id)); used = 1; }
            count = 1;
        }
        private static int ValidateBuildOptions(TagRegistry registry, int capacity,
            BitmapLayout storage, int inputCount)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (storage < BitmapLayout.Auto || storage > BitmapLayout.Dense)
                throw new ArgumentOutOfRangeException(nameof(storage));
            if (registry.Count > (1 << 20))
                throw new ArgumentOutOfRangeException(nameof(registry), "Micro16 supports at most 2^20 registered IDs.");
            return Math.Min(registry.Count, Math.Max(capacity, inputCount));
        }
        private static void ValidateSortedHandles(TagRegistry registry, ReadOnlySpan<RuntimeTag> tags)
        {
            int previous = -1;
            for (int i = 0; i < tags.Length; i++)
            {
                RuntimeTag tag = tags[i];
                if (!ReferenceEquals(tag.Owner, registry))
                    throw new ArgumentException("All input handles must belong to this registry.", nameof(tags));
                if (tag.Id <= previous)
                    throw new ArgumentException("Input must be strictly increasing and unique.", nameof(tags));
                previous = tag.Id;
            }
        }
        private static bool WantsDense(TagRegistry registry, int capacity, BitmapLayout storage)
        {
            const int bytesPerMember = EntryBytes;
            return storage == BitmapLayout.Dense || (storage == BitmapLayout.Auto && registry.Count != 0
                && 8L * registry.WordCount <= (long)bytesPerMember * capacity);
        }
        private DirectArraySet(TagRegistry registry, int capacity, bool makeDense)
        {
            Registry = registry;
            if (makeDense) dense = registry.WordCount == 0 ? Array.Empty<ulong>() : new ulong[registry.WordCount];
            else { int records = Math.Min(capacity, MaxEntries); if (records > 1) entries = new Entry[records]; }
        }
        // Only the fresh factory calls this, with length > 1 and reserved capacity.
        // Invalid partial writes remain exclusively in an unpublished new object.
        private void ValidateAndFillSorted(ReadOnlySpan<RuntimeTag> values)
        {
            int n = values.Length, previous = -1;
            ulong[] words = dense;
            if (words != null)
            {
                int at = -1; ulong mask = 0;
                for (int i = 0; i < n; i++)
                {
                    RuntimeTag tag = values[i]; RequireHandle(Registry, tag);
                    int id = tag.Id;
                    if (id <= previous) throw new ArgumentException("Input must be strictly increasing and unique.", "tags");
                    previous = id;
                    int word = id >> 6;
                    if (word != at)
                    { if (at >= 0) words[at] = mask; at = word; mask = 0; }
                    mask |= 1UL << (id & 63);
                }
                words[at] = mask;
            }
            else if (entries == null)
            {
                // n>1, capacity>=n and no entries implies the whole registry fits one record.
                Entry mask = 0;
                for (int i = 0; i < n; i++)
                { RuntimeTag tag = values[i]; RequireHandle(Registry, tag);
                    int id = tag.Id;
                    if (id <= previous) throw new ArgumentException("Input must be strictly increasing and unique.", "tags");
                    previous = id;
                    mask |= Bit(id);
                }
                inline = Key(previous) | mask; used = 1;
            }
            else
            {
                Entry[] output = entries; Entry key = 0, mask = 0;
                int write = 0;
                for (int i = 0; i < n; i++)
                {
                    RuntimeTag tag = values[i]; RequireHandle(Registry, tag);
                    int id = tag.Id;
                    if (id <= previous) throw new ArgumentException("Input must be strictly increasing and unique.", "tags");
                    previous = id;
                    Entry nextKey = Key(id);
                    if (nextKey != key && mask != 0)
                    { output[write++] = key | mask; mask = 0; }
                    key = nextKey; mask |= Bit(id);
                }
                output[write++] = key | mask; used = write;
            }
            count = n;
        }

        private void FillSortedHandles(ReadOnlySpan<RuntimeTag> values)
        {
            int n = values.Length;
            ulong[] words = dense;
            if (words != null)
            {
                int i = 0;
                while (i < n)
                {
                    int id = values[i].Id; i++;
                    int word = id >> 6;
                    ulong mask = 1UL << (id & 63);
                    while (i < n && (values[i].Id >> 6) == word)
                    { mask |= 1UL << (values[i].Id & 63); i++; }
                    words[word] = mask;
                }
            }
            else if (n != 0)
            {
                // A null entries buffer is sufficient only for a single possible record.
                // Reservation is in arbitrary member units, not an optimistic local layout.
                if (entries == null)
                {
                    Entry mask = 0;
                    for (int i = 0; i < n; i++) mask |= Bit(values[i].Id);
                    inline = Key(values[0].Id) | mask;
                    used = 1;
                }
                else
                {
                    Entry[] output = entries;
                    int i = 0, write = 0;
                    while (i < n)
                    {
                        int id = values[i].Id; i++;
                        Entry key = Key(id), mask = Bit(id);
                        while (i < n && Key(values[i].Id) == key)
                        { mask |= Bit(values[i].Id); i++; }
                        output[write++] = key | mask;
                    }
                    used = write;
                }
            }
            count = n;
        }

        private void FillSortedIds(int[] values, int n)
        {
            ulong[] words = dense;
            if (words != null)
            {
                int i = 0;
                while (i < n)
                {
                    int id = values[i]; i++;
                    int word = id >> 6;
                    ulong mask = 1UL << (id & 63);
                    while (i < n && (values[i] >> 6) == word)
                    { mask |= 1UL << (values[i] & 63); i++; }
                    words[word] = mask;
                }
            }
            else if (n != 0)
            {
                // A null entries buffer is sufficient only for a single possible record.
                // Reservation is in arbitrary member units, not an optimistic local layout.
                if (entries == null)
                {
                    Entry mask = 0;
                    for (int i = 0; i < n; i++) mask |= Bit(values[i]);
                    inline = Key(values[0]) | mask;
                    used = 1;
                }
                else
                {
                    Entry[] output = entries;
                    int i = 0, write = 0;
                    while (i < n)
                    {
                        int id = values[i]; i++;
                        Entry key = Key(id), mask = Bit(id);
                        while (i < n && Key(values[i]) == key)
                        { mask |= Bit(values[i]); i++; }
                        output[write++] = key | mask;
                    }
                    used = write;
                }
            }
            count = n;
        }

    }
}
