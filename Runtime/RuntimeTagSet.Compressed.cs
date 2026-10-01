using System;

namespace GameplayTags
{
    public sealed partial class RuntimeTagSet
    {
        // Kept beside the logical count; object-size effects are measured in the benchmark host.
        // -1 is an ordinary ID/bitmap set; >=0 is the live packed-record prefix in m_Ids.
        private int m_PackedUsed = -1;
        private const int PackedMask = 0xFFFF;
        private const int PackedUniverseLimit = 1 << 20;
        private int PackedMaximum => (Registry.Count + 15) >> 4;
        private static int PackedKey(int id) => unchecked(((id >> 4) << 16) ^ int.MinValue);
        private static int RecordKey(int record) => record & ~PackedMask;
        private static int RecordBase(int record) => (int)((unchecked((uint)(record ^ int.MinValue)) >> 16) << 4);
        private static int RecordPop(int record) => Bits.Count((uint)(record & PackedMask));
        public int RecordCount => m_PackedUsed >= 0 ? m_PackedUsed : 0;
        public int RecordCapacity => m_PackedUsed >= 0 ? m_Ids.Length : 0;
        /// <summary>Worst-case arbitrary-member reservation; packed current Count may be larger.</summary>
        public int ReservedMemberCapacity => m_PackedUsed >= 0
            ? (m_Ids.Length >= PackedMaximum ? Registry.Count : m_Ids.Length) : Capacity;

        /// <summary>
        /// Explicit allocating bulk preparation. Localized memberships use packed 16-ID records;
        /// scattered large memberships use dense words when their word count is at most twice
        /// the member count. Tiny inputs retain the ordinary preparation path. Conversion costs
        /// are paid here, never hidden in a later query or mutation. Packed mode supports U<=2^20.
        /// </summary>
        public static RuntimeTagSet FromTagsForBulk(TagRegistry registry, ReadOnlySpan<RuntimeTag> tags, int capacity = 0)
        {
            if (tags.Length < 64) return FromTags(registry, tags, capacity, TagSetStorage.Auto);
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            var ordered = TryPrepareOrderedBulk(registry, tags, capacity);
            if (ordered != null) return ordered;
            var result = FromTags(registry, tags, capacity, TagSetStorage.Sparse);
            int records = 0, previous = -1;
            for (int i = 0; i < result.m_Count; i++)
            { int key = result.m_Ids[i] >> 4; if (key != previous) { records++; previous = key; } }
            TagSetStorage selected = ChooseBulkStorage(registry, result.m_Count, records);
            if (selected == TagSetStorage.Compressed)
            {
                int slots = Math.Max(records, Math.Min(capacity, result.PackedMaximum));
                var packed = slots == 0 ? Array.Empty<int>() : new int[slots];
                int used = 0;
                for (int i = 0; i < result.m_Count; i++)
                {
                    int id = result.m_Ids[i], key = PackedKey(id), bit = 1 << (id & 15);
                    if (used != 0 && RecordKey(packed[used - 1]) == key) packed[used - 1] |= bit;
                    else packed[used++] = key | bit;
                }
                result.m_Ids = packed; result.m_PackedUsed = used;
                return result;
            }
            if (selected == TagSetStorage.Dense) return result.ToStorage(TagSetStorage.Dense, capacity);
            return result;
        }
        // A validation/count pass makes an already ordered input directly materializable:
        // no temporary ID array, no duplicate representation, no deferred cardinality work.
        private static RuntimeTagSet TryPrepareOrderedBulk(TagRegistry registry, ReadOnlySpan<RuntimeTag> tags, int capacity)
        {
            int previous = -1, count = 0, records = 0, previousBlock = -1;
            for (int i = 0; i < tags.Length; i++)
            {
                RuntimeTag tag = tags[i];
                if (tag.Owner == null) continue;
                registry.RequireSame(tag.Owner);
                int id = tag.Id;
                if (id < previous) return null;
                if (id == previous) continue;
                count++; previous = id;
                int block = id >> 4;
                if (block != previousBlock) { records++; previousBlock = block; }
            }
            TagSetStorage mode = ChooseBulkStorage(registry, count, records);
            var result = new RuntimeTagSet(registry, mode == TagSetStorage.Compressed ? capacity : Math.Max(count, capacity), mode);
            if (mode == TagSetStorage.Compressed) result.ReservePacked(records);
            if (mode == TagSetStorage.Dense && count == tags.Length)
                return MaterializeUniqueDense(result, tags, count);
            int used = 0; previous = -1;
            for (int i = 0; i < tags.Length; i++)
            {
                RuntimeTag tag = tags[i];
                if (tag.Owner == null || tag.Id == previous) continue;
                int id = tag.Id; previous = id;
                if (mode == TagSetStorage.Dense) result.m_Words[id >> 6] |= 1UL << (id & 63);
                else if (mode == TagSetStorage.Sparse) result.m_Ids[used++] = id;
                else
                {
                    int key = PackedKey(id), bit = 1 << (id & 15);
                    if (used != 0 && RecordKey(result.m_Ids[used - 1]) == key) result.m_Ids[used - 1] |= bit;
                    else result.m_Ids[used++] = key | bit;
                }
            }
            result.m_Count = count;
            if (mode == TagSetStorage.Compressed) result.m_PackedUsed = used;
            return result;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static RuntimeTagSet MaterializeUniqueDense(RuntimeTagSet result, ReadOnlySpan<RuntimeTag> tags, int count)
        {
            ulong[] words = result.m_Words;
            for (int i = 0; i < tags.Length; i++)
            { int id = tags[i].Id; words[id >> 6] |= 1UL << (id & 63); }
            result.m_Count = count;
            return result;
        }
        private static TagSetStorage ChooseBulkStorage(TagRegistry registry, int count, int records)
        {
            if (registry.Count <= PackedUniverseLimit && count >= 64 && (long)records * 4 <= count)
            {
                // A narrow bitmap can beat scalar record merging despite its larger payload.
                // Compare physical work units, with a separately tested portable threshold.
                if (registry.WordCount <= (long)RuntimeBitOperations.DenseWordsPerPackedRecord * records)
                    return TagSetStorage.Dense;
                return TagSetStorage.Compressed;
            }
            return count >= 64 && registry.WordCount <= 2L * count ? TagSetStorage.Dense : TagSetStorage.Sparse;
        }
        private TagSetStorage SelectBulkStorage(out int records)
        {
            // Existing tiny memberships do not pay a shape scan or an unnecessary migration.
            if (m_Count < 64) { records = m_PackedUsed >= 0 ? m_PackedUsed : 0; return Storage; }
            // Every occupied record holds at most 16 members. This lower bound can prove
            // a dense recommendation without scanning an already suitable bitmap.
            if (Registry.WordCount <= RuntimeBitOperations.DenseWordsPerPackedRecord * ((m_Count + 15L) >> 4))
            { records = 0; return TagSetStorage.Dense; }
            records = Registry.Count > PackedUniverseLimit ? 0 : CountPackedRecords(this);
            return ChooseBulkStorage(Registry, m_Count, records);
        }
        /// <summary>
        /// Returns an explicit bulk-preparation recommendation without mutation or allocation.
        /// Scans occupied blocks for larger non-compressed sets. Tiny existing sets retain their
        /// current storage. This is a workload hint, not a guarantee that conversion amortizes.
        /// </summary>
        public TagSetStorage SelectBulkStorage() => SelectBulkStorage(out _);
        /// <summary>
        /// Allocates an independent set in the recommended bulk layout, streaming existing
        /// members directly without exporting handle arrays. The input is never returned.
        /// Call SelectBulkStorage first if a caller prefers to retain an already suitable input.
        /// </summary>
        public RuntimeTagSet ToStorageForBulk(int capacity = 0)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            TagSetStorage mode = SelectBulkStorage(out int records);
            var result = new RuntimeTagSet(Registry, mode == TagSetStorage.Compressed ? capacity : Math.Max(capacity, m_Count), mode);
            if (mode == TagSetStorage.Compressed)
            {
                result.ReservePacked(records);
                var scan = new RecordEnumerator(this);
                while (scan.MoveNext()) result.m_Ids[result.m_PackedUsed++] = scan.Current;
                result.m_Count = m_Count;
            }
            else result.CopyFrom(this);
            return result;
        }
        public static RuntimeTagSet UnionForBulk(RuntimeTagSet left, RuntimeTagSet right)
        {
            Required(left).Require(right);
            TagSetStorage mode;
            if (left.m_Words != null || right.m_Words != null) mode = TagSetStorage.Dense;
            else if (left.m_PackedUsed >= 0 || right.m_PackedUsed >= 0) mode = TagSetStorage.Compressed;
            else mode = left.Registry.WordCount <= 2L * (left.Count + (long)right.Count) && left.Count + (long)right.Count >= 64
                ? TagSetStorage.Dense : TagSetStorage.Sparse;
            return Union(left, right, mode);
        }
        private void ReservePacked(int records)
        {
            if (records <= m_Ids.Length) return;
            if (records > PackedMaximum) throw new ArgumentOutOfRangeException(nameof(records));
            int grown = (int)Math.Min(PackedMaximum, Math.Max(4L, 2L * m_Ids.Length));
            Array.Resize(ref m_Ids, Math.Max(records, grown));
        }
        private int FindPacked(int key)
        {
            int high = m_PackedUsed - 1;
            if (high < 0) return ~0;
            int last = RecordKey(m_Ids[high]);
            if (key >= last) return key == last ? high : ~(high + 1);
            int first = RecordKey(m_Ids[0]);
            if (key <= first) return key == first ? 0 : ~0;
            if ((unchecked((uint)(last - first)) >> 16) == (uint)high)
                return (int)(unchecked((uint)(key - first)) >> 16);
            return FindPackedBinary(key);
        }
        private int FindPackedBinary(int key)
        {
            int low = 0, high = m_PackedUsed - 1;
            while (low <= high)
            {
                int mid = low + ((high - low) >> 1), found = RecordKey(m_Ids[mid]);
                if (found == key) return mid;
                if (found < key) low = mid + 1; else high = mid - 1;
            }
            return ~low;
        }
        private bool PackedContains(int id)
        {
            int length = m_PackedUsed;
            if (length == 0) return false;
            int first = RecordBase(m_Ids[0]) >> 4, last = RecordBase(m_Ids[length - 1]) >> 4;
            int offset = (id >> 4) - first;
            if (unchecked((uint)offset) > (uint)(last - first)) return false;
            // Sorted unique keys occupy every block iff their endpoint distance is length-1.
            // This derives a direct index without a directory, cache, or extra object field.
            int index = last - first + 1 == length ? offset : FindPackedBinary(PackedKey(id));
            return index >= 0 && (m_Ids[index] & (1 << (id & 15))) != 0;
        }
        private bool PackedAdd(int id)
        {
            int key = PackedKey(id), bit = 1 << (id & 15), index = FindPacked(key);
            if (index >= 0)
            {
                if ((m_Ids[index] & bit) != 0) return false;
                m_Ids[index] |= bit; m_Count++; return true;
            }
            index = ~index; ReservePacked(m_PackedUsed + 1);
            Array.Copy(m_Ids, index, m_Ids, index + 1, m_PackedUsed - index);
            m_Ids[index] = key | bit; m_PackedUsed++; m_Count++; return true;
        }
        private bool PackedRemove(int id)
        {
            int index = FindPacked(PackedKey(id)), bit = 1 << (id & 15);
            if (index < 0 || (m_Ids[index] & bit) == 0) return false;
            int value = m_Ids[index] & ~bit;
            if ((value & PackedMask) != 0) m_Ids[index] = value;
            else { m_PackedUsed--; Array.Copy(m_Ids, index + 1, m_Ids, index, m_PackedUsed - index); }
            m_Count--; return true;
        }
        private bool PackedRange(int start, int end)
        {
            if (start == end || m_PackedUsed == 0) return false;
            int index = FindPacked(PackedKey(start)); if (index < 0) index = ~index;
            int last = (end - 1) >> 4;
            for (int checkedRecords = 0; checkedRecords < 2 && index < m_PackedUsed; checkedRecords++, index++)
            {
                int record = m_Ids[index], block = RecordBase(record) >> 4;
                if (block > last) return false;
                int mask = record & PackedMask;
                if (block == (start >> 4)) mask &= PackedMask << (start & 15);
                if (block == last && (end & 15) != 0) mask &= (1 << (end & 15)) - 1;
                if (mask != 0) return true;
            }
            return false;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void EmitRecord(int record)
        {
            int mask = record & PackedMask;
            if (m_PackedUsed >= 0)
            {
                if (m_PackedUsed == m_Ids.Length) ReservePacked(m_PackedUsed + 1);
                m_Ids[m_PackedUsed++] = record; m_Count += RecordPop(record);
            }
            else
            {
                int first = RecordBase(record);
                while (mask != 0)
                { int bit = Bits.Lowest((uint)mask); AppendOrdered(first + bit); mask &= mask - 1; }
            }
        }
        private struct RecordEnumerator
        {
            private readonly int[] ids;
            private readonly ulong[] words;
            private readonly int length, kind;
            private readonly bool reverse;
            private int cursor;
            private ulong remaining;
            internal int Current { get; private set; }
            internal RecordEnumerator(RuntimeTagSet set, bool backwards = false)
            {
                ids = set.m_Ids; words = set.m_Words; reverse = backwards;
                kind = set.m_PackedUsed >= 0 ? 2 : words != null ? 1 : 0;
                length = kind == 2 ? set.m_PackedUsed : kind == 1 ? words.Length : set.m_Count;
                cursor = reverse ? length - 1 : 0; remaining = 0; Current = 0;
            }
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
            internal bool MoveNext()
            {
                if (kind == 2)
                {
                    if (unchecked((uint)cursor) >= (uint)length) return false;
                    Current = ids[cursor]; cursor += reverse ? -1 : 1; return true;
                }
                if (kind == 0)
                {
                    if (unchecked((uint)cursor) >= (uint)length) return false;
                    int id = ids[cursor], key = PackedKey(id), mask = 0;
                    do { mask |= 1 << (ids[cursor] & 15); cursor += reverse ? -1 : 1; }
                    while (unchecked((uint)cursor) < (uint)length && PackedKey(ids[cursor]) == key);
                    Current = key | mask; return true;
                }
                while (remaining == 0)
                {
                    if (unchecked((uint)cursor) >= (uint)length) return false;
                    remaining = words[cursor];
                    if (remaining == 0) cursor += reverse ? -1 : 1;
                }
                int quarter = (reverse ? Bits.Highest(remaining) : Bits.Lowest(remaining)) >> 4;
                int bits = (int)((remaining >> (quarter * 16)) & PackedMask);
                Current = PackedKey((cursor << 6) + (quarter << 4)) | bits;
                remaining &= ~((ulong)PackedMask << (quarter * 16));
                if (remaining == 0) cursor += reverse ? -1 : 1;
                return true;
            }
        }
        // A monotonically ordered record writer is alias-safe for dense intersections:
        // overwrite/clear only words that input iterators have already consumed.
        private struct DenseRecordWriter
        {
            private readonly ulong[] words;
            private int pendingIndex, cleared;
            private ulong pending;
            internal int Count;
            internal DenseRecordWriter(ulong[] target) { words = target; pendingIndex = -1; cleared = 0; pending = 0; Count = 0; }
            internal void Add(int record) { AddBits(record); Count += RecordPop(record); }
            internal void AddBits(int record)
            {
                int first = RecordBase(record), word = first >> 6;
                ulong mask = (ulong)(record & PackedMask) << (first & 63);
                if (word != pendingIndex)
                { Flush(); pendingIndex = word; pending = mask; }
                else pending |= mask;
            }
            private void Flush()
            {
                if (pendingIndex < 0) return;
                if (pendingIndex > cleared) Array.Clear(words, cleared, pendingIndex - cleared);
                words[pendingIndex] = pending; cleared = pendingIndex + 1;
            }
            internal void Finish() { Flush(); if (cleared < words.Length) Array.Clear(words, cleared, words.Length - cleared); }
        }
        private static int CountPackedRecords(RuntimeTagSet set)
        {
            if (set.m_PackedUsed >= 0) return set.m_PackedUsed;
            int count = 0;
            if (set.m_Words == null)
            {
                int previous = -1;
                for (int i = 0; i < set.m_Count; i++)
                { int block = set.m_Ids[i] >> 4; if (block != previous) { count++; previous = block; } }
            }
            else
            {
                for (int i = 0; i < set.m_Words.Length; i++)
                    count += RuntimeBitOperations.CountNonEmptyQuarters(set.m_Words[i]);
            }
            return count;
        }
        private static int PackedUnionRecordCount(RuntimeTagSet left, RuntimeTagSet right)
        {
            if (left.m_PackedUsed >= 0 && right.m_PackedUsed >= 0) return CountPackedUnionDirect(left, right);
            var a = new RecordEnumerator(left); var b = new RecordEnumerator(right);
            bool x = a.MoveNext(), y = b.MoveNext(); int count = 0;
            while (x && y)
            {
                int ak = RecordKey(a.Current), bk = RecordKey(b.Current);
                if (ak <= bk) x = a.MoveNext(); if (bk <= ak) y = b.MoveNext(); count++;
            }
            while (x) { count++; x = a.MoveNext(); } while (y) { count++; y = b.MoveNext(); }
            return count;
        }
        private void CopyPackedAware(RuntimeTagSet other)
        {
            if (m_Words != null && other.m_PackedUsed >= 0)
            { CopyPackedToDense(other); return; }
            if (m_PackedUsed >= 0 && other.m_PackedUsed >= 0)
            { ReservePacked(other.m_PackedUsed); Array.Copy(other.m_Ids, m_Ids, other.m_PackedUsed); m_PackedUsed = other.m_PackedUsed; m_Count = other.m_Count; return; }
            var scan = new RecordEnumerator(other);
            if (m_Words != null)
            {
                var writer = new DenseRecordWriter(m_Words); while (scan.MoveNext()) writer.Add(scan.Current);
                writer.Finish(); m_Count = writer.Count; return;
            }
            if (m_PackedUsed >= 0) { ReservePacked(CountPackedRecords(other)); m_PackedUsed = 0; }
            else EnsureCapacity(other.m_Count);
            m_Count = 0; while (scan.MoveNext()) EmitRecord(scan.Current);
        }
        private void AppendPackedAware(RuntimeTagSet other)
        {
            if (m_Words != null && other.m_PackedUsed >= 0)
            { ApplyPackedToDense(other, false); return; }
            if (m_PackedUsed >= 0 && other.m_PackedUsed < 0 && other.m_Words == null &&
                other.m_Count <= 64 && (long)other.m_Count * 32 <= m_Count)
            { for (int i = 0; i < other.m_Count; i++) PackedAdd(other.m_Ids[i]); return; }
            if (m_PackedUsed >= 0 && other.m_PackedUsed >= 0) { AppendPackedDirect(other); return; }
            if (m_PackedUsed >= 0 && other.m_Words == null) { AppendPackedSparseDirect(other); return; }
            if (m_Words != null)
            {
                var scan = new RecordEnumerator(other); int added = 0;
                while (scan.MoveNext())
                {
                    int first = RecordBase(scan.Current), word = first >> 6;
                    ulong mask = (ulong)(scan.Current & PackedMask) << (first & 63), old = m_Words[word];
                    added += Bits.Count(mask & ~old); m_Words[word] = old | mask;
                }
                m_Count += added; return;
            }
            if (m_PackedUsed < 0)
            {
                int total = CountUnion(this, other); if (total == m_Count) return;
                EnsureCapacity(total); int read = m_Count - 1, write = total - 1;
                var scan = new IdEnumerator(other, true); bool exists = scan.MoveNext();
                while (read >= 0 && exists)
                {
                    int a = m_Ids[read], b = scan.Current;
                    if (a > b) m_Ids[write--] = m_Ids[read--];
                    else if (a < b) { m_Ids[write--] = b; exists = scan.MoveNext(); }
                    else { m_Ids[write--] = m_Ids[read--]; exists = scan.MoveNext(); }
                }
                while (exists) { m_Ids[write--] = scan.Current; exists = scan.MoveNext(); }
                m_Count = total; return;
            }
            int records = PackedUnionRecordCount(this, other), originalCount = m_Count, duplicates = 0;
            ReservePacked(records); int own = m_PackedUsed - 1, destination = records - 1;
            var incoming = new RecordEnumerator(other, true); bool has = incoming.MoveNext();
            while (own >= 0 && has)
            {
                int a = m_Ids[own], b = incoming.Current, ak = RecordKey(a), bk = RecordKey(b);
                if (ak > bk) { m_Ids[destination--] = a; own--; }
                else if (ak < bk) { m_Ids[destination--] = b; has = incoming.MoveNext(); }
                else { m_Ids[destination--] = a | b; duplicates += RecordPop(a & b); own--; has = incoming.MoveNext(); }
            }
            while (has) { m_Ids[destination--] = incoming.Current; has = incoming.MoveNext(); }
            m_PackedUsed = records; m_Count = originalCount + other.m_Count - duplicates;
        }
        private bool RemovePackedAware(RuntimeTagSet other)
        {
            int before = m_Count;
            if (m_Words != null && other.m_PackedUsed >= 0)
            { ApplyPackedToDense(other, true); return before != m_Count; }
            if (m_PackedUsed >= 0 && other.m_PackedUsed < 0 && other.m_Words == null &&
                other.m_Count <= 64 && (long)other.m_Count * 32 <= m_Count)
            { for (int i = 0; i < other.m_Count; i++) PackedRemove(other.m_Ids[i]); return before != m_Count; }
            if (m_Words != null)
            {
                var scan = new RecordEnumerator(other); int removed = 0;
                while (scan.MoveNext())
                {
                    int first = RecordBase(scan.Current), word = first >> 6;
                    ulong mask = (ulong)(scan.Current & PackedMask) << (first & 63), old = m_Words[word];
                    removed += Bits.Count(old & mask); m_Words[word] = old & ~mask;
                }
                m_Count -= removed;
            }
            else PackedBinaryInto(this, other, this, 2);
            return before != m_Count;
        }
        // op: union, intersection, difference. Input/output aliases are handled at public boundaries.
        private static void PackedBinaryInto(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result, int op)
        {
            if (op == 0 && result.m_Words != null)
            { DensePackedUnion(left, right, result); return; }
            if (op == 1 && TinyPackedIntersection(left, right, result)) return;
            if (op == 2 && TinyPackedDifference(left, right, result)) return;
            if (op == 0 && result.m_PackedUsed >= 0)
            {
                RuntimeTagSet packed = left.m_PackedUsed >= 0 ? left : right;
                RuntimeTagSet sparse = ReferenceEquals(packed, left) ? right : left;
                if (packed.m_PackedUsed >= 0 && sparse.m_PackedUsed < 0 && sparse.m_Words == null &&
                    sparse.m_Count <= 64 && (long)sparse.m_Count * 32 <= packed.m_Count)
                { result.CopyPackedAware(packed); result.AppendPackedAware(sparse); return; }
            }
            if (left.m_PackedUsed >= 0 && right.m_Words != null && op != 0)
            { FilterPackedByDense(left, right, result, op == 2); return; }
            if (right.m_PackedUsed >= 0 && left.m_Words != null)
            {
                if (op == 1) { FilterPackedByDense(right, left, result, false); return; }
                if (op == 2 && result.m_Words != null)
                { result.CopyFrom(left); result.RemovePackedAware(right); return; }
            }
            if (op == 2 && left.m_PackedUsed >= 0 && right.m_PackedUsed < 0 && right.m_Words == null &&
                right.m_Count <= 64 && (long)right.m_Count * 32 <= left.m_Count && !ReferenceEquals(result, right))
            { if (!ReferenceEquals(result, left)) result.CopyFrom(left); result.RemovePackedAware(right); return; }
            if (left.m_PackedUsed >= 0 && right.m_PackedUsed >= 0 && result.m_PackedUsed >= 0)
            { BinaryPackedDirect(left, right, result, op); return; }
            if (op == 0 && result.m_PackedUsed >= 0 && left.m_Words == null && right.m_Words == null)
            {
                RuntimeTagSet packed = left.m_PackedUsed >= 0 ? left : right;
                RuntimeTagSet sparse = ReferenceEquals(packed, left) ? right : left;
                if (packed.m_PackedUsed >= 0 && sparse.m_PackedUsed < 0)
                { UnionPackedSparseDirect(packed, sparse, result); return; }
            }
            // Independent union output can grow only as actual records are emitted. Existing
            // actual-result capacity therefore needs neither a counting pass nor a resize.
            var a = new RecordEnumerator(left); var b = new RecordEnumerator(right);
            bool hasA = a.MoveNext(), hasB = b.MoveNext();
            var dense = new DenseRecordWriter(result.m_Words);
            int total = left.m_Count + (op == 0 ? right.m_Count : 0), common = 0, write = 0;
            bool packedOutput = result.m_PackedUsed >= 0;
            int[] destination = result.m_Ids;
            while (hasA || (op == 0 && hasB))
            {
                int emitted = 0, mask = 0;
                if (!hasA) { emitted = b.Current; mask = emitted & PackedMask; hasB = b.MoveNext(); }
                else if (!hasB)
                {
                    if (op != 1) { emitted = a.Current; mask = emitted & PackedMask; }
                    hasA = a.MoveNext();
                }
                else
                {
                    int x = a.Current, y = b.Current, xk = RecordKey(x), yk = RecordKey(y);
                    if (xk < yk)
                    { if (op != 1) { emitted = x; mask = x & PackedMask; } hasA = a.MoveNext(); }
                    else if (xk > yk)
                    { if (op == 0) { emitted = y; mask = y & PackedMask; } hasB = b.MoveNext(); }
                    else
                    {
                        common += RecordPop(x & y);
                        mask = (op == 0 ? x | y : op == 1 ? x & y : x & ~y) & PackedMask;
                        emitted = xk | mask; hasA = a.MoveNext(); hasB = b.MoveNext();
                    }
                }
                if (mask != 0)
                { if (result.m_Words != null) dense.AddBits(emitted); else WriteRecordBuffered(result, emitted, packedOutput, ref destination, ref write); }
            }
            if (result.m_Words != null) dense.Finish();
            else if (packedOutput) result.m_PackedUsed = write;
            result.m_Count = op == 1 ? common : total - common;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static void WriteRecordBuffered(RuntimeTagSet result, int record, bool packed, ref int[] buffer, ref int write)
        {
            if (packed)
            {
                if (write == buffer.Length) { result.ReservePacked(write + 1); buffer = result.m_Ids; }
                buffer[write++] = record;
                return;
            }
            int first = RecordBase(record), mask = record & PackedMask;
            while (mask != 0)
            {
                if (write == buffer.Length) { result.EnsureCapacity(write + 1); buffer = result.m_Ids; }
                buffer[write++] = first + Bits.Lowest((uint)mask);
                mask &= mask - 1;
            }
        }
        private static void DensePackedUnion(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result)
        {
            // Independent output only: public union aliases route through AppendTags.
            RuntimeTagSet first = right.m_Words != null ? right : left;
            RuntimeTagSet second = ReferenceEquals(first, left) ? right : left;
            if (first.m_Words != null) Array.Copy(first.m_Words, result.m_Words, first.m_Words.Length);
            else
            {
                if (result.m_Count != 0) Array.Clear(result.m_Words, 0, result.m_Words.Length);
                var source = new RecordEnumerator(first);
                while (source.MoveNext())
                {
                    int start = RecordBase(source.Current);
                    result.m_Words[start >> 6] |= (ulong)(source.Current & PackedMask) << (start & 63);
                }
            }
            result.m_Count = first.m_Count;
            result.AppendPackedAware(second);
        }
        private static void FilterPackedByDense(RuntimeTagSet packed, RuntimeTagSet dense, RuntimeTagSet result, bool difference)
        {
            if (result.m_Words != null)
            { FilterPackedToDense(packed, dense, result, difference); return; }
            int[] records = packed.m_Ids; int length = packed.m_PackedUsed;
            var writer = new DenseRecordWriter(result.m_Words);
            if (result.m_Words == null) { result.m_Count = 0; if (result.m_PackedUsed >= 0) result.m_PackedUsed = 0; }
            for (int i = 0; i < length; i++)
            {
                int record = records[i], start = RecordBase(record);
                int condition = unchecked((int)(dense.m_Words[start >> 6] >> (start & 63)));
                int bits = record & (difference ? ~condition : condition) & PackedMask;
                if (bits == 0) continue;
                int kept = RecordKey(record) | bits;
                if (result.m_Words != null) writer.Add(kept); else result.EmitRecord(kept);
            }
            if (result.m_Words != null) { writer.Finish(); result.m_Count = writer.Count; }
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static ulong ReadPackedWord(int[] records, int length, int firstBlock, ref int read, out int word)
        {
            if (firstBlock >= 0 && read <= length - 4 && ((firstBlock + read) & 3) == 0)
            {
                word = (firstBlock + read) >> 2;
                ulong direct = RuntimeBitOperations.PackFourRecordMasks(records, read);
                read += 4;
                return direct;
            }
            int record = records[read++], start = RecordBase(record);
            word = start >> 6;
            ulong mask = (ulong)(record & PackedMask) << (start & 63);
            while (read < length)
            {
                record = records[read]; start = RecordBase(record);
                if ((start >> 6) != word) break;
                mask |= (ulong)(record & PackedMask) << (start & 63); read++;
            }
            return mask;
        }
        private static int PackedContiguousStart(RuntimeTagSet packed)
        {
            int length = packed.m_PackedUsed;
            if (length < 8) return -1;
            int first = RecordBase(packed.m_Ids[0]) >> 4;
            return (RecordBase(packed.m_Ids[length - 1]) >> 4) - first + 1 == length ? first : -1;
        }
        private void CopyPackedToDense(RuntimeTagSet packed)
        {
            int read = 0, cleared = 0, length = packed.m_PackedUsed;
            int firstBlock = PackedContiguousStart(packed);
            int[] records = packed.m_Ids; ulong[] words = m_Words;
            while (read < length)
            {
                ulong mask = ReadPackedWord(records, length, firstBlock, ref read, out int word);
                if (word > cleared) Array.Clear(words, cleared, word - cleared);
                words[word] = mask; cleared = word + 1;
            }
            if (cleared < words.Length) Array.Clear(words, cleared, words.Length - cleared);
            m_Count = packed.m_Count;
        }
        private void ApplyPackedToDense(RuntimeTagSet packed, bool remove)
        {
            int read = 0, changed = 0, length = packed.m_PackedUsed;
            int firstBlock = PackedContiguousStart(packed);
            int[] records = packed.m_Ids; ulong[] words = m_Words;
            while (read < length)
            {
                ulong mask = ReadPackedWord(records, length, firstBlock, ref read, out int word), old = words[word];
                changed += Bits.Count(remove ? old & mask : mask & ~old);
                words[word] = remove ? old & ~mask : old | mask;
            }
            m_Count += remove ? -changed : changed;
        }
        private static void FilterPackedToDense(RuntimeTagSet packed, RuntimeTagSet dense, RuntimeTagSet result, bool difference)
        {
            int read = 0, cleared = 0, count = 0, length = packed.m_PackedUsed;
            int firstBlock = PackedContiguousStart(packed);
            int[] records = packed.m_Ids; ulong[] words = result.m_Words, conditions = dense.m_Words;
            while (read < length)
            {
                ulong mask = ReadPackedWord(records, length, firstBlock, ref read, out int word);
                // Consume this original condition word before any aliased output write.
                ulong kept = mask & (difference ? ~conditions[word] : conditions[word]);
                if (word > cleared) Array.Clear(words, cleared, word - cleared);
                words[word] = kept; cleared = word + 1; count += Bits.Count(kept);
            }
            if (cleared < words.Length) Array.Clear(words, cleared, words.Length - cleared);
            result.m_Count = count;
        }
        private static bool TinyPackedIntersection(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result)
        {
            RuntimeTagSet packed = left.m_PackedUsed >= 0 ? left : right;
            RuntimeTagSet sparse = ReferenceEquals(packed, left) ? right : left;
            if (packed.m_PackedUsed < 0 || sparse.m_PackedUsed >= 0 || sparse.m_Words != null ||
                sparse.m_Count > 64 || (long)sparse.m_Count * 32 > packed.m_Count) return false;
            int length = sparse.m_Count; int[] ids = sparse.m_Ids; ulong matched = 0;
            // Finish every input read needed for membership before resetting either alias.
            for (int i = 0; i < length; i++) if (packed.PackedContains(ids[i])) matched |= 1UL << i;
            result.Clear();
            for (int i = 0; i < length; i++) if ((matched & (1UL << i)) != 0) result.AppendOrdered(ids[i]);
            return true;
        }
        private static bool TinyPackedDifference(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result)
        {
            if (left.m_PackedUsed >= 0 || left.m_Words != null || right.m_PackedUsed < 0 ||
                left.m_Count > 64 || (long)left.m_Count * 32 > right.m_Count) return false;
            int length = left.m_Count; int[] ids = left.m_Ids; ulong kept = 0;
            for (int i = 0; i < length; i++) if (!right.PackedContains(ids[i])) kept |= 1UL << i;
            result.Clear();
            for (int i = 0; i < length; i++) if ((kept & (1UL << i)) != 0) result.AppendOrdered(ids[i]);
            return true;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static int ReadSparseRecordForward(int[] ids, int length, ref int read)
        {
            int block = ids[read] >> 4, mask = 0;
            do { mask |= 1 << (ids[read++] & 15); } while (read < length && (ids[read] >> 4) == block);
            return unchecked((block << 16) ^ int.MinValue) | mask;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static int ReadSparseRecordBackward(int[] ids, ref int read)
        {
            int block = ids[read] >> 4, mask = 0;
            do { mask |= 1 << (ids[read--] & 15); } while (read >= 0 && (ids[read] >> 4) == block);
            return unchecked((block << 16) ^ int.MinValue) | mask;
        }
        private static int CountPackedSparseUnionRecords(RuntimeTagSet packed, RuntimeTagSet sparse)
        {
            int a = 0, incoming = 0, shared = 0, previous = -1;
            for (int b = 0; b < sparse.m_Count; b++)
            {
                int block = sparse.m_Ids[b] >> 4;
                if (block == previous) continue;
                previous = block; incoming++;
                int key = unchecked((block << 16) ^ int.MinValue);
                while (a < packed.m_PackedUsed && RecordKey(packed.m_Ids[a]) < key) a++;
                if (a < packed.m_PackedUsed && RecordKey(packed.m_Ids[a]) == key) shared++;
            }
            return packed.m_PackedUsed + incoming - shared;
        }
        private static void UnionPackedSparseDirect(RuntimeTagSet packed, RuntimeTagSet sparse, RuntimeTagSet result)
        {
            int[] x = packed.m_Ids, ids = sparse.m_Ids, output = result.m_Ids;
            int an = packed.m_PackedUsed, bn = sparse.m_Count, a = 0, read = 0, write = 0, common = 0;
            bool hasB = read < bn;
            int y = hasB ? ReadSparseRecordForward(ids, bn, ref read) : 0;
            while (a < an && hasB)
            {
                int first = x[a], ak = RecordKey(first), bk = RecordKey(y), emitted;
                if (ak < bk) { emitted = first; a++; }
                else if (ak > bk) { emitted = y; hasB = read < bn; if (hasB) y = ReadSparseRecordForward(ids, bn, ref read); }
                else
                {
                    emitted = first | y; common += RecordPop(first & y); a++;
                    hasB = read < bn; if (hasB) y = ReadSparseRecordForward(ids, bn, ref read);
                }
                if (write == output.Length) { result.ReservePacked(write + 1); output = result.m_Ids; }
                output[write++] = emitted;
            }
            if (a < an)
            {
                result.ReservePacked(write + an - a); output = result.m_Ids;
                Array.Copy(x, a, output, write, an - a); write += an - a;
            }
            while (hasB)
            {
                if (write == output.Length) { result.ReservePacked(write + 1); output = result.m_Ids; }
                output[write++] = y; hasB = read < bn; if (hasB) y = ReadSparseRecordForward(ids, bn, ref read);
            }
            result.m_PackedUsed = write; result.m_Count = packed.m_Count + sparse.m_Count - common;
        }
        private void AppendPackedSparseDirect(RuntimeTagSet sparse)
        {
            int records = CountPackedSparseUnionRecords(this, sparse);
            ReservePacked(records);
            int[] own = m_Ids, ids = sparse.m_Ids;
            int a = m_PackedUsed - 1, read = sparse.m_Count - 1, write = records - 1, common = 0;
            bool hasB = read >= 0;
            int y = hasB ? ReadSparseRecordBackward(ids, ref read) : 0;
            while (a >= 0 && hasB)
            {
                int x = own[a], ak = RecordKey(x), bk = RecordKey(y);
                if (ak > bk) { own[write--] = x; a--; }
                else if (ak < bk) { own[write--] = y; hasB = read >= 0; if (hasB) y = ReadSparseRecordBackward(ids, ref read); }
                else
                {
                    own[write--] = x | y; common += RecordPop(x & y); a--;
                    hasB = read >= 0; if (hasB) y = ReadSparseRecordBackward(ids, ref read);
                }
            }
            while (hasB)
            { own[write--] = y; hasB = read >= 0; if (hasB) y = ReadSparseRecordBackward(ids, ref read); }
            m_PackedUsed = records; m_Count += sparse.m_Count - common;
        }
        private static int CountPackedUnionDirect(RuntimeTagSet left, RuntimeTagSet right)
        {
            int i = 0, j = 0, count = 0, a = left.m_PackedUsed, b = right.m_PackedUsed;
            int[] x = left.m_Ids, y = right.m_Ids;
            while (i < a && j < b)
            {
                int first = RecordKey(x[i]), second = RecordKey(y[j]);
                if (first <= second) i++;
                if (second <= first) j++;
                count++;
            }
            return count + a - i + b - j;
        }
        private void AppendPackedDirect(RuntimeTagSet other)
        {
            if (UnionContiguousPacked(this, other, this, true)) return;
            int records = CountPackedUnionDirect(this, other);
            ReservePacked(records);
            int i = m_PackedUsed - 1, j = other.m_PackedUsed - 1, write = records - 1, duplicates = 0;
            int[] x = m_Ids, y = other.m_Ids;
            while (i >= 0 && j >= 0)
            {
                int a = x[i], b = y[j], ak = RecordKey(a), bk = RecordKey(b);
                if (ak > bk) { x[write--] = a; i--; }
                else if (ak < bk) { x[write--] = b; j--; }
                else { x[write--] = a | b; duplicates += RecordPop(a & b); i--; j--; }
            }
            if (j >= 0) Array.Copy(y, 0, x, 0, j + 1);
            m_PackedUsed = records; m_Count += other.m_Count - duplicates;
        }
        private static void BinaryPackedDirect(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result, int op)
        {
            if (op == 0 ? UnionContiguousPacked(left, right, result, false) :
                FilterContiguousPacked(left, right, result, op == 2)) return;
            int a = left.m_PackedUsed, b = right.m_PackedUsed, i = 0, j = 0, write = 0, common = 0;
            int total = left.m_Count;
            if (op == 0)
            {
                int upper = Math.Min(result.PackedMaximum, a + b);
                if (result.m_Ids.Length < upper) result.ReservePacked(CountPackedUnionDirect(left, right));
                total += right.m_Count;
            }
            int[] x = left.m_Ids, y = right.m_Ids;
            while (i < a && j < b)
            {
                int first = x[i], second = y[j], ak = RecordKey(first), bk = RecordKey(second);
                int emitted = 0;
                if (ak < bk) { if (op != 1) emitted = first; i++; }
                else if (ak > bk) { if (op == 0) emitted = second; j++; }
                else
                {
                    int overlap = first & second & PackedMask;
                    common += RecordPop(overlap);
                    int bits = (op == 0 ? first | second : op == 1 ? overlap : first & ~second) & PackedMask;
                    if (bits != 0) emitted = ak | bits;
                    i++; j++;
                }
                if ((emitted & PackedMask) == 0) continue;
                if (write == result.m_Ids.Length) result.ReservePacked(write + 1);
                result.m_Ids[write++] = emitted;
            }
            if (op != 1 && i < a)
            {
                result.ReservePacked(write + a - i);
                Array.Copy(x, i, result.m_Ids, write, a - i); write += a - i;
            }
            if (op == 0 && j < b)
            {
                Array.Copy(y, j, result.m_Ids, write, b - j); write += b - j;
            }
            result.m_PackedUsed = write;
            result.m_Count = op == 1 ? common : total - common;
        }
        private static bool PackedAnyDirect(RuntimeTagSet left, RuntimeTagSet right)
        {
            int i = 0, j = 0;
            while (i < left.m_PackedUsed && j < right.m_PackedUsed)
            {
                int a = left.m_Ids[i], b = right.m_Ids[j], ak = RecordKey(a), bk = RecordKey(b);
                if (ak < bk) i++;
                else if (ak > bk) j++;
                else { if ((a & b & PackedMask) != 0) return true; i++; j++; }
            }
            return false;
        }
        private static bool PackedAllDirect(RuntimeTagSet superset, RuntimeTagSet subset)
        {
            int i = 0;
            for (int j = 0; j < subset.m_PackedUsed; j++)
            {
                int expected = subset.m_Ids[j], key = RecordKey(expected);
                while (i < superset.m_PackedUsed && RecordKey(superset.m_Ids[i]) < key) i++;
                if (i == superset.m_PackedUsed || RecordKey(superset.m_Ids[i]) != key ||
                    (expected & ~superset.m_Ids[i] & PackedMask) != 0) return false;
            }
            return true;
        }
        private void FilterPackedInPlace(RuntimeTagSet conditions)
        {
            int write = 0, count = 0;
            for (int i = 0; i < m_PackedUsed; i++)
            {
                int record = m_Ids[i], pending = record & PackedMask, kept = 0, first = RecordBase(record);
                while (pending != 0)
                {
                    int bit = Bits.Lowest((uint)pending), ancestor = first + bit;
                    while (ancestor >= 0 && !conditions.ContainsId(ancestor)) ancestor = Registry.Parents[ancestor];
                    if (ancestor >= 0) kept |= 1 << bit;
                    pending &= pending - 1;
                }
                if (kept != 0) { m_Ids[write++] = RecordKey(record) | kept; count += Bits.Count((uint)kept); }
            }
            m_PackedUsed = write; m_Count = count;
        }
        private static void PackedDifferenceRight(RuntimeTagSet left, RuntimeTagSet right)
        {
            // Keep only actual exclusions A∩B, whose record keys are a subset of A's.
            // With |records(A)| slots, a backwards output can never overtake unread exclusions.
            int leftCount = left.m_Count, leftRecords = CountPackedRecords(left);
            PackedBinaryInto(left, right, right, 1);
            int removed = right.m_Count, index = right.m_PackedUsed - 1;
            right.ReservePacked(leftRecords);
            int write = leftRecords - 1;
            var scan = new RecordEnumerator(left, true);
            while (scan.MoveNext())
            {
                int record = scan.Current, key = RecordKey(record), mask = record & PackedMask;
                if (index >= 0 && RecordKey(right.m_Ids[index]) == key) mask &= ~right.m_Ids[index--];
                mask &= PackedMask;
                if (mask != 0) right.m_Ids[write--] = key | mask;
            }
            int used = leftRecords - 1 - write;
            if (used != 0) Array.Copy(right.m_Ids, write + 1, right.m_Ids, 0, used);
            right.m_PackedUsed = used; right.m_Count = leftCount - removed;
        }
    }
}
