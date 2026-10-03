// Exact-membership Micro16/dense set, included in the GameplayTags Runtime assembly.
// Inspired by address-plus-bitmap records (tinyset); independently implemented.
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Entry = System.UInt32;

namespace GameplayTags.Experiments
{

    /// <summary>
    /// One membership representation per instance: sorted address+mask records OR
    /// contiguous dense words. No raw member-ID array, pool, COW, deferred Count,
    /// runtime auto-conversion or global scratch. Single owner; no concurrent mutation.
    /// </summary>
    public sealed partial class DirectArraySet
    {
        private const int Shift = 4, MaskBits = 16, EntryBytes = 4;
        private const Entry Mask = 0xFFFFU;
        private Entry[] entries;
        private readonly ulong[] dense;
        private Entry inline;
        private int used, count;
        public TagRegistry Registry { get; }
        public int Count => count;
        public BitmapLayout Layout => dense == null ? BitmapLayout.Micro : BitmapLayout.Dense;
        private int EntryCapacity => entries == null ? 1 : entries.Length;
        private int MaxEntries => (int)(((long)Registry.Count + (1 << Shift) - 1) >> Shift);
        public int ReservedMemberCapacity => dense != null || EntryCapacity >= MaxEntries
            ? Registry.Count : EntryCapacity;
        // Payload only; measured B/op separately includes ALL objects and array headers.
        public long BufferBytes => EntryBytes + (dense != null ? 8L * dense.Length : entries == null ? 0 : (long)EntryBytes * entries.Length);
        private Entry Read(int at) => entries == null ? inline : entries[at];
        private void Write(int at, Entry value) { if (entries == null) inline = value; else entries[at] = value; }

        public DirectArraySet(TagRegistry registry, int capacity = 0, BitmapLayout layout = BitmapLayout.Auto)
        {
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (layout < BitmapLayout.Auto || layout > BitmapLayout.Dense) throw new ArgumentOutOfRangeException(nameof(layout));
            if (registry.Count > (1 << 20))
                throw new ArgumentOutOfRangeException(nameof(registry), "DirectArraySet supports at most 2^20 registered IDs.");
            capacity = Math.Min(capacity, registry.Count);
            if (layout == BitmapLayout.Dense || (layout == BitmapLayout.Auto && registry.Count != 0
                && 8L * registry.WordCount <= (long)EntryBytes * capacity))
                dense = registry.WordCount == 0 ? Array.Empty<ulong>() : new ulong[registry.WordCount];
            else if (capacity > 1) ReserveEntries(Math.Min(capacity, MaxEntries), true);
        }
        public DirectArraySet(DirectArraySet source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            Registry = source.Registry;
            count = source.count; used = source.used;
            if (source.dense != null) dense = (ulong[])source.dense.Clone();
            else if (source.entries == null) inline = used == 0 ? 0 : source.inline;
            else { entries = new Entry[source.entries.Length]; Array.Copy(source.entries, entries, used); }
        }
        private void Require(DirectArraySet other)
        { if (other == null) throw new ArgumentNullException(nameof(other)); Registry.RequireSame(other.Registry); }
        private bool Accept(RuntimeTag tag)
        { if (tag.Owner == null) return false; Registry.RequireSame(tag.Owner); return true; }
        public void EnsureCapacity(int members)
        {
            if (members < 0) throw new ArgumentOutOfRangeException(nameof(members));
            if (dense == null) ReserveEntries(Math.Min(members, MaxEntries));
        }
        private void ReserveEntries(int needed, bool exact = false)
        {
            if (needed <= EntryCapacity) return;
            int size = exact ? needed : (int)Math.Min(MaxEntries, Math.Max(needed, Math.Max(4L, 2L * EntryCapacity)));
            var next = new Entry[size];
            if (entries != null) Array.Copy(entries, next, used);
            else if (used != 0) next[0] = inline;
            entries = next;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Entry Key(int id) => ((Entry)(uint)id >> Shift) << MaskBits;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Entry Bit(int id) => (Entry)1 << (id & ((1 << Shift) - 1));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Find(Entry key)
        {
            if (used == 1)
            {
                Entry k = Read(0) & ~Mask;
                return k == key ? 0 : k > key ? -1 : -2;
            }
            int lo = 0, hi = used - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                Entry k = entries[mid] & ~Mask;
                if (k == key) return mid;
                if (k < key) lo = mid + 1; else hi = mid - 1;
            }
            return ~lo;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasTagExact(RuntimeTag tag)
        {
            if (!ReferenceEquals(Registry, tag.Owner))
            { if (tag.Owner == null) return false; Registry.RequireSame(tag.Owner); }
            int id = tag.Id;
            if (dense != null) return (dense[id >> 6] & (1UL << (id & 63))) != 0;
            uint key = Key(id), bit = Bit(id);
            // Only inspect valid records. No padded/uninitialized lanes, no out-of-bounds loads.
            if (Vector.IsHardwareAccelerated && entries != null && used >= Vector<uint>.Count && used <= 32)
            {
                var keys = new Vector<uint>(key); var keyMask = new Vector<uint>(0xFFFF0000U);
                var target = new Vector<uint>(bit); int i = 0;
                for (; i <= used - Vector<uint>.Count; i += Vector<uint>.Count)
                {
                    var records = new Vector<uint>(entries, i);
                    var matches = Vector.Equals(records & keyMask, keys) & Vector.Equals(records & target, target);
                    if (!Vector.EqualsAll(matches, Vector<uint>.Zero)) return true;
                }
                for (; i < used; i++) if ((entries[i] & 0xFFFF0000U) == key && (entries[i] & bit) != 0) return true;
                return false;
            }
            int at = Find(key); return at >= 0 && (Read(at) & bit) != 0;
        }
        public bool AddTag(RuntimeTag tag) => Accept(tag) && AddId(tag.Id);
        private bool AddId(int id)
        {
            if (dense != null)
            {
                ulong bit = 1UL << (id & 63); int w = id >> 6;
                ulong before = dense[w]; if ((before & bit) != 0) return false;
                dense[w] = before | bit; count++; return true;
            }
            Entry key = Key(id), mask = Bit(id); int at = Find(key);
            if (at >= 0)
            {
                Entry before = Read(at); if ((before & mask) != 0) return false;
                Write(at, before | mask); count++; return true;
            }
            at = ~at; ReserveEntries(used + 1);
            if (at < used) Array.Copy(entries, at, entries, at + 1, used - at);
            Write(at, key | mask); used++; count++; return true;
        }
        public bool RemoveTag(RuntimeTag tag)
        {
            if (!Accept(tag)) return false;
            int id = tag.Id;
            if (dense != null)
            {
                ulong bit = 1UL << (id & 63); int w = id >> 6;
                ulong before = dense[w]; if ((before & bit) == 0) return false;
                dense[w] = before & ~bit; count--; return true;
            }
            int at = Find(Key(id)); if (at < 0) return false;
            Entry beforeEntry = Read(at), mask = Bit(id);
            if ((beforeEntry & mask) == 0) return false;
            Entry after = beforeEntry & ~mask; count--;
            if ((after & Mask) != 0) Write(at, after);
            else { used--; if (at < used) Array.Copy(entries, at + 1, entries, at, used - at); }
            return true;
        }
        public void Clear()
        { if (dense != null && count != 0) Array.Clear(dense, 0, dense.Length); used = count = 0; }
        public void CopyFrom(DirectArraySet source)
        {
            Require(source); if (ReferenceEquals(this, source)) return;
            CopyCore(source);
        }
        private void CopyCore(DirectArraySet source)
        {
            // Admission and self-alias checks already ran. Preserve target capacity.
            if (source.count == 0) { Clear(); return; }
            if (dense != null && source.dense != null) Array.Copy(source.dense, dense, dense.Length);
            else if (dense == null && source.dense == null)
            {
                ReserveEntries(source.used);
                if (source.used == 1) Write(0, source.Read(0));
                else if (source.used > 1) Array.Copy(source.entries, entries, source.used);
                used = source.used;
            }
            else if (source.dense != null)
            {
                CopyDenseToMicro(source);
            }
            else
            {
                Clear(); var it = new Records(source);
                while (it.MoveNext()) PutRecord(it.Current);
            }
            count = source.count;
        }
        // Unique ascending records only. Internal builder, not an admission API.
        private void PutRecord(Entry entry)
        {
            if (dense == null) { ReserveEntries(used + 1); Write(used++, entry); }
            else
            {
                int id = (int)(entry >> MaskBits) << Shift;
                dense[id >> 6] |= (ulong)(entry & Mask) << (id & 63);
            }
        }
        private static int CountRecords(DirectArraySet a, DirectArraySet b)
        {

            // Resolve storage once, not through Read() for every record.
            if (a.entries != null && b.entries != null)
            {
                Entry[] x = a.entries, y = b.entries;
                int an = a.used, bn = b.used, ai = 0, bi = 0, length = 0;
                while (ai < an && bi < bn)
                {
                    Entry ak = x[ai] & ~Mask, bk = y[bi] & ~Mask;
                    if (ak <= bk) ai++;
                    if (bk <= ak) bi++;
                    length++;
                }
                return length + an - ai + bn - bi;
            }

            int i = 0, j = 0, result = 0;
            while (i < a.used && j < b.used)
            {
                Entry x = a.Read(i) & ~Mask, y = b.Read(j) & ~Mask;
                if (x <= y) i++; if (y <= x) j++; result++;
            }
            return result + a.used - i + b.used - j;
        
        }
        public static DirectArraySet Union(DirectArraySet a, DirectArraySet b, BitmapLayout layout = BitmapLayout.Auto)
        {
            if (a == null) throw new ArgumentNullException(nameof(a)); a.Require(b);
            int bound = (int)Math.Min(a.Registry.Count, (long)a.count + b.count);
            bool useDense = layout == BitmapLayout.Dense || (layout == BitmapLayout.Auto && bound != 0
                && 8L * a.Registry.WordCount <= (long)EntryBytes * bound);
            // Actual result layout is built once. No source alias exists in this path.
            var result = new DirectArraySet(a.Registry, 0, useDense ? BitmapLayout.Dense : layout == BitmapLayout.Auto ? BitmapLayout.Micro : layout);
            UnionCore(a, b, result); return result;
        }
        public static void UnionInto(DirectArraySet a, DirectArraySet b, DirectArraySet result)
        {
            if (a == null) throw new ArgumentNullException(nameof(a)); a.Require(b); a.Require(result);
            UnionCore(a, b, result);
        }
        private static void UnionCore(DirectArraySet a, DirectArraySet b, DirectArraySet result)
        {

            if (ReferenceEquals(result, a)) { result.AppendCore(b); return; }
            if (ReferenceEquals(result, b)) { result.AppendCore(a); return; }
            if (ReferenceEquals(a, b) || b.count == 0) { result.CopyCore(a); return; }
            if (a.count == 0) { result.CopyCore(b); return; }
            if (result.dense != null && a.dense != null && b.dense != null)
            { result.count = DenseUnion(a.dense, b.dense, result.dense); return; }
            // Earlier guards have handled output aliases, empty/same input and all-Dense.
            // Here exactly one source is Dense; overwrite every output word before merging.
            if (result.dense != null && (a.dense != null || b.dense != null))
            {
                DirectArraySet full = a.dense != null ? a : b;
                DirectArraySet local = a.dense != null ? b : a;
                ulong[] output = result.dense;
                Array.Copy(full.dense, output, output.Length);
                int members = full.count;
                for (int i = 0; i < local.used; i++)
                {
                    Entry entry = local.Read(i);
                    int id = (int)(entry >> MaskBits) << Shift;
                    int word = id >> 6, shift = id & 63;
                    Entry incoming = entry & Mask;
                    ulong before = output[word];
                    members += Pop(incoming & ~(Entry)(before >> shift));
                    output[word] = before | ((ulong)incoming << shift);
                }
                result.count = members;
                return;
            }
            if (result.dense == null && a.dense == null && b.dense == null)
            {
                if (a.used == 1 && b.used == 1 && (a.Read(0) & ~Mask) == (b.Read(0) & ~Mask))
                { Entry e = a.Read(0) | b.Read(0); result.Write(0, e); result.used = 1; result.count = Pop(e & Mask); return; }
                int upper = (int)Math.Min(result.MaxEntries, (long)a.used + b.used);
                // Keep actual-result-capacity guarantee for caller-owned outputs.
                // A genuinely new result uses an upper bound, with allocation in timing.
                if (result.EntryCapacity < upper)
                {
                    int needed = result.EntryCapacity > 1 ? CountRecords(a, b) : upper;
                    result.ReserveEntries(needed, true);
                }
                if (a.entries != null && b.entries != null && result.entries != null)
                {
                    int duplicatesArray = MergeArrays(a.entries, a.used, b.entries, b.used, result.entries, out int mergedRecords);
                    // Record count, unlike member count, is not derivable from duplicate bits.
                    // Derive it in the merge below without a second traversal.
                    result.used = mergedRecords;
                    result.count = a.count + b.count - duplicatesArray;
                    return;
                }
                int i = 0, j = 0, write = 0, duplicates = 0;
                while (i < a.used && j < b.used)
                {
                    Entry x = a.Read(i), y = b.Read(j), kx = x & ~Mask, ky = y & ~Mask;
                    if (kx < ky) { result.Write(write++, x); i++; }
                    else if (ky < kx) { result.Write(write++, y); j++; }
                    else { result.Write(write++, x | y); duplicates += Pop(x & y & Mask); i++; j++; }
                }
                for (; i < a.used; i++) result.Write(write++, a.Read(i));
                for (; j < b.used; j++) result.Write(write++, b.Read(j));
                result.used = write; result.count = a.count + b.count - duplicates; return;
            }
            if (result.dense == null && ((a.dense == null) != (b.dense == null)))
            {
                UnionDenseToMicro(a.dense != null ? a : b, a.dense == null ? a : b, result);
                return;
            }
            result.Clear(); var left = new Records(a); var right = new Records(b);
            bool l = left.MoveNext(), r = right.MoveNext(); int total = 0;
            while (l || r)
            {
                Entry e;
                if (!r || (l && (left.Current & ~Mask) < (right.Current & ~Mask)))
                { e = left.Current; l = left.MoveNext(); }
                else if (!l || (right.Current & ~Mask) < (left.Current & ~Mask))
                { e = right.Current; r = right.MoveNext(); }
                else { e = left.Current | right.Current; l = left.MoveNext(); r = right.MoveNext(); }
                result.PutRecord(e); total += Pop(e & Mask);
            }
            result.count = total;
        
        }
        public void AppendTags(DirectArraySet other)
        {
            Require(other); AppendCore(other);
        }
        private void AppendCore(DirectArraySet other)
        {

            if (ReferenceEquals(this, other) || other.count == 0) return;
            if (count == 0) { CopyCore(other); return; }
            if (dense != null && other.dense != null)
            { count = DenseUnion(dense, other.dense, dense); return; }
            if (dense == null && other.dense == null)
            {
                int length = CountRecords(this, other);
                if (entries != null && other.entries != null)
                {

                    ReserveEntries(length);
                    Entry[] a = entries, b = other.entries;
                    int ai = used - 1, bi = other.used - 1, wi = length - 1, duplicateBits = 0;
                    while (ai >= 0 && bi >= 0)
                    {
                        Entry x = a[ai], y = b[bi], kx = x & ~Mask, ky = y & ~Mask;
                        if (kx > ky) { a[wi--] = x; ai--; }
                        else if (ky > kx) { a[wi--] = y; bi--; }
                        else { a[wi--] = x | y; duplicateBits += Pop(x & y & Mask); ai--; bi--; }
                    }
                    while (bi >= 0) a[wi--] = b[bi--];
                    used = length;
                    count += other.count - duplicateBits;
                    return;
                }
                ReserveEntries(length);
                int i = used - 1, j = other.used - 1, w = length - 1, duplicates = 0;
                while (i >= 0 && j >= 0)
                {
                    Entry x = Read(i), y = other.Read(j), kx = x & ~Mask, ky = y & ~Mask;
                    if (kx > ky) { Write(w--, x); i--; }
                    else if (ky > kx) { Write(w--, y); j--; }
                    else { Write(w--, x | y); duplicates += Pop(x & y & Mask); i--; j--; }
                }
                while (j >= 0) Write(w--, other.Read(j--));
                used = length; count += other.count - duplicates; return;
            }
            if (other.dense != null)
            {
                AppendDenseToMicro(other);
                return;
            }
            var it = new Records(other);
            while (it.MoveNext())
            {
                Entry e = it.Current; int id = (int)(e >> MaskBits) << Shift;
                if (dense != null)
                {
                    int w = id >> 6; ulong bits = (ulong)(e & Mask) << (id & 63), old = dense[w];
                    count += Pop64(bits & ~old); dense[w] = old | bits;
                }
                else
                {
                    Entry key = e & ~Mask; int at = Find(key);
                    if (at >= 0) { Entry old = Read(at); Write(at, old | e); count += Pop(e & ~old & Mask); }
                    else
                    {
                        at = ~at; ReserveEntries(used + 1);
                        if (at < used) Array.Copy(entries, at, entries, at + 1, used - at);
                        Write(at, e); used++; count += Pop(e & Mask);
                    }
                }
            }
        
        }
        public void RemoveTags(DirectArraySet other)
        {

            Require(other); if (ReferenceEquals(this, other)) { Clear(); return; }
            if (count == 0 || other.count == 0) return;
            if (dense != null && other.dense != null)
            { count = DenseDifference(dense, other.dense); return; }
            if (dense == null && other.dense == null)
            {
                if (entries != null && other.entries != null)
                {
                    Entry[] a = entries, b = other.entries;
                    int an = used, bn = other.used, ai = 0, bi = 0, wi = 0, removedBits = 0;
                    while (ai < an && bi < bn)
                    {
                        Entry x = a[ai], y = b[bi], kx = x & ~Mask, ky = y & ~Mask;
                        if (kx < ky) { a[wi++] = x; ai++; }
                        else if (ky < kx) bi++;
                        else
                        {
                            Entry deleted = x & y & Mask;
                            Entry remaining = x & ~deleted;
                            removedBits += Pop(deleted);
                            if ((remaining & Mask) != 0) a[wi++] = remaining;
                            ai++; bi++;
                        }
                    }
                    if (ai < an)
                    {
                        if (wi != ai) Array.Copy(a, ai, a, wi, an - ai);
                        wi += an - ai;
                    }
                    used = wi;
                    count -= removedBits;
                    return;
                }
                int i = 0, j = 0, w = 0, removed = 0;
                while (i < used)
                {
                    Entry e = Read(i++), key = e & ~Mask;
                    while (j < other.used && (other.Read(j) & ~Mask) < key) j++;
                    if (j < other.used && (other.Read(j) & ~Mask) == key)
                    { Entry bits = e & other.Read(j) & Mask; e &= ~bits; removed += Pop(bits); }
                    if ((e & Mask) != 0) Write(w++, e);
                }
                used = w; count -= removed; return;
            }
            if (dense != null)
            {
                var it = new Records(other);
                while (it.MoveNext())
                {
                    Entry e = it.Current; int id = (int)(e >> MaskBits) << Shift, w = id >> 6;
                    ulong mask = (ulong)(e & Mask) << (id & 63), old = dense[w];
                    count -= Pop64(old & mask); dense[w] = old & ~mask;
                }
            }
            else
            {
                int w = 0, total = 0;
                for (int i = 0; i < used; i++)
                {
                    Entry e = Read(i); int id = (int)(e >> MaskBits) << Shift;
                    Entry bits = (e & Mask) & ~(Entry)(other.dense[id >> 6] >> (id & 63));
                    if (bits != 0) { Write(w++, (e & ~Mask) | bits); total += Pop(bits); }
                }
                used = w; count = total;
            }
        
        }
        // Enumerates in numeric (DFS-ID) order in either representation.
        public Enumerator GetEnumerator() => new Enumerator(this);
        public struct Enumerator
        {
            private readonly TagRegistry registry;
            private Records records; private Entry pending; private int id;
            public RuntimeTag Current => new RuntimeTag(registry, id);
            internal Enumerator(DirectArraySet set)
            { registry = set.Registry; records = new Records(set); pending = 0; id = -1; }
            public bool MoveNext()
            {
                if (pending == 0)
                {
                    if (!records.MoveNext()) return false;
                    pending = records.Current & Mask; id = (int)(records.Current >> MaskBits) << Shift;
                }
                int first = Low((ulong)pending); pending &= pending - 1;
                id = (id & ~((1 << Shift) - 1)) + first; return true;
            }
        }
        // All callers have already established Dense source / Micro destination,
        // common registry, exclusive output and stable inputs for this operation.
        private void CopyDenseToMicro(DirectArraySet source)
        {
            Clear();
            // Nonzero records <= members AND <= registry's possible record keys.
            // This upper bound certifies absence of buffer growth for the whole copy.
            if (EntryCapacity < Math.Min(source.count, MaxEntries))
            {
                CopyDenseGrowing(source);
                return;
            }
            var iterator = new DenseRecordCursor(source);
            while (iterator.MoveNext()) Write(used++, iterator.Current);
            // No potentially failing member allocation; CopyCore commits known Count.
        }

        private void CopyDenseGrowing(DirectArraySet source)
        {
            var iterator = new DenseRecordCursor(source);
            while (iterator.MoveNext())
            {
                Entry entry = iterator.Current;
                PutRecord(entry);
                // The successful write is now part of the observable partial copy.
                count += Pop(entry & Mask);
            }
        }

        private const int AppendBatchRecords = 32;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AppendDenseToMicro(DirectArraySet source)
        {
            // One bounded stack allocation per call, reused for ALL source sizes.
            // No source.Count crossover and no second source scan.
            Span<Entry> missing = stackalloc Entry[AppendBatchRecords];
            int n = 0, at = 0, tail = 0, missingMembers = 0;
            var iterator = new DenseRecordCursor(source);
            while (iterator.MoveNext())
            {
                Entry value = iterator.Current, key = value & ~Mask;
                while (at < used && (Read(at) & ~Mask) < key) at++;
                if (at < used && (Read(at) & ~Mask) == key)
                {
                    Entry before = Read(at);
                    Write(at++, before | value);
                    count += Pop(value & ~before & Mask);
                }
                else
                {
                    if (n == AppendBatchRecords)
                    {
                        MergeMissingBatch(missing, n, tail, missingMembers);
                        // Every buffered key is smaller than this source key.
                        // All n inserted records precede this old insertion index.
                        at += n;
                        n = missingMembers = 0;
                    }
                    missing[n++] = value;
                    missingMembers += Pop(value & Mask);
                    tail = at;
                }
            }
            if (n != 0) MergeMissingBatch(missing, n, tail, missingMembers);
        }

        private void MergeMissingBatch(Span<Entry> missing, int n, int tail, int members)
        {
            int length = used + n;
            // This is the only throwing allocation site before moving old records.
            // Preserve scalar insertion's growth ladder/total bytes/final capacity.
            while (EntryCapacity < length) ReserveEntries(EntryCapacity + 1);
            if (tail < used) Array.Copy(entries, tail, entries, tail + n, used - tail);
            int read = tail - 1, write = tail + n - 1, pending = n - 1;
            while (read >= 0 && pending >= 0)
            {
                Entry a = Read(read), b = missing[pending];
                // Keys are missing from the target; equality is impossible.
                if ((a & ~Mask) > (b & ~Mask)) { Write(write--, a); read--; }
                else { Write(write--, b); pending--; }
            }
            while (pending >= 0) Write(write--, missing[pending--]);
            used = length;
            count += members;
        }

        private static void UnionDenseToMicro(DirectArraySet full, DirectArraySet local, DirectArraySet result)
        {
            result.Clear();
            var iterator = new DenseRecordCursor(full);
            bool pending = iterator.MoveNext();
            int at = 0, total = 0;
            while (pending || at < local.used)
            {
                Entry value;
                if (!pending)
                {
                    value = local.Read(at++);
                }
                else if (at == local.used)
                {
                    value = iterator.Current;
                    pending = iterator.MoveNext();
                }
                else
                {
                    Entry x = iterator.Current, y = local.Read(at);
                    Entry kx = x & ~Mask, ky = y & ~Mask;
                    if (kx < ky) { value = x; pending = iterator.MoveNext(); }
                    else if (ky < kx) { value = y; at++; }
                    else { value = x | y; at++; pending = iterator.MoveNext(); }
                }
                // Same ordered writes, capacity growth and eager counting as control.
                result.PutRecord(value);
                total += Pop(value & Mask);
            }
            result.count = total;
        }

        // A temporary local used only by the three helpers above. In particular it
        // is NOT a field of DirectArraySet, Records or the public Enumerator.
        private struct DenseRecordCursor
        {
            private readonly ulong[] words;
            private ulong pending;
            private int nextId;
            public Entry Current;
            public DenseRecordCursor(DirectArraySet source)
            {
                words = source.dense;
                nextId = source.count == 0 ? words.Length << 6 : 0;
                pending = 0;
                Current = 0;
            }
            public bool MoveNext()
            {
                if (pending == 0)
                {
                    int word = (nextId + 63) >> 6;
                    do
                    {
                        if (word == words.Length) return false;
                        pending = words[word++];
                    } while (pending == 0);
                    nextId = (word - 1) << 6;
                }
                // At most four fixed 16-bit slices per nonzero word; no Low().
                // Shift only by MaskBits, never by 64 (C# masks shift counts).
                while (true)
                {
                    Entry bits = (Entry)pending & Mask;
                    int id = nextId;
                    pending >>= MaskBits;
                    nextId += 1 << Shift;
                    if (bits != 0) { Current = Key(id) | bits; return true; }
                }
            }
        }

        private struct Records
        {
            private readonly DirectArraySet set; private int at;
            public Entry Current;
            public Records(DirectArraySet value) { set = value; at = -1; Current = 0; }
            public bool MoveNext()
            {
                if (set.dense == null)
                { if (++at >= set.used) return false; Current = set.Read(at); return true; }
                int limit = set.MaxEntries;
                while (++at < limit)
                {
                    int id = at << Shift; Entry bits = (Entry)(set.dense[id >> 6] >> (id & 63)) & Mask;
                    if (bits != 0) { Current = ((Entry)(uint)at << MaskBits) | bits; return true; }
                }
                return false;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Pop(Entry value)
        {
            return Pop32(value);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Pop32(uint v)
        {
            unchecked { v -= (v >> 1) & 0x55555555U; v = (v & 0x33333333U) + ((v >> 2) & 0x33333333U);
                v = (v + (v >> 4)) & 0x0F0F0F0FU; return (int)((v * 0x01010101U) >> 24); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Pop64(ulong v)
        {
            unchecked { v -= (v >> 1) & 0x5555555555555555UL; v = (v & 0x3333333333333333UL) + ((v >> 2) & 0x3333333333333333UL);
                v = (v + (v >> 4)) & 0x0F0F0F0F0F0F0F0FUL; return (int)((v * 0x0101010101010101UL) >> 56); }
        }
        private static int Low(ulong v)
        {
            int n = 0; if ((uint)v == 0) { v >>= 32; n += 32; }
            if ((v & 0xFFFF) == 0) { v >>= 16; n += 16; }
            if ((v & 255) == 0) { v >>= 8; n += 8; }
            if ((v & 15) == 0) { v >>= 4; n += 4; }
            if ((v & 3) == 0) { v >>= 2; n += 2; }
            return n + ((v & 1) == 0 ? 1 : 0);
        }
        private static int DenseUnion(ulong[] a, ulong[] b, ulong[] output)
        {
            return DenseFusion.Union(a,b,output);
        }
        private static int DenseDifference(ulong[] a, ulong[] b)
        {
            return DenseFusion.Difference(a,b,a);
        }

        // Independent destination only: public aliases were handled before this call.
        // The caller reserves at least the actual merged record count.
        private static int MergeArrays(Entry[] a, int an, Entry[] b, int bn, Entry[] output, out int mergedRecords)
        {
            int i = 0, j = 0, w = 0, duplicates = 0;
            while (i < an && j < bn)
            {
                Entry x = a[i], y = b[j], kx = x & ~Mask, ky = y & ~Mask;
                if (kx < ky) { output[w++] = x; i++; }
                else if (ky < kx) { output[w++] = y; j++; }
                else { output[w++] = x | y; duplicates += Pop(x & y & Mask); i++; j++; }
            }
            if (i < an) Array.Copy(a, i, output, w, an - i);
            else if (j < bn) Array.Copy(b, j, output, w, bn - j);
            mergedRecords = w + an - i + bn - j;
            return duplicates;
        }

        public void AssertInvariants()
        {
            int total = 0;
            if (dense != null)
            {
                if (entries != null || used != 0) throw new Exception("Two active membership encodings");
                for (int i = 0; i < dense.Length; i++) total += Pop64(dense[i]);
                if (dense.Length != 0 && (Registry.Count & 63) != 0 && (dense[dense.Length - 1] >> (Registry.Count & 63)) != 0)
                    throw new Exception("Invalid tail bits");
            }
            else
            {
                if (used < 0 || used > EntryCapacity) throw new Exception("Invalid used length");
                Entry prev = 0;
                for (int i = 0; i < used; i++)
                {
                    Entry e = Read(i), key = e & ~Mask;
                    if ((e & Mask) == 0 || (i != 0 && prev >= key)) throw new Exception("Noncanonical records");
                    int baseId = (int)(e >> MaskBits) << Shift;
                    if (baseId >= Registry.Count) throw new Exception("Record outside registry");
                    int remaining = Registry.Count - baseId;
                    if (remaining < (1 << Shift) && ((e & Mask) >> remaining) != 0) throw new Exception("Record tail");
                    prev = key; total += Pop(e & Mask);
                }
            }
            if (total != count) throw new Exception("Incorrect eager Count");
        }
    }
}
