using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace GameplayTags
{
    public enum TagSetStorage { Auto, Sparse, Dense, Compressed }

    /// <summary>
    /// Single-owner runtime set. Exactly one member buffer is live: sorted IDs/packed records in int[], or dense ulong[].
    /// Storage is chosen at construction and never changes implicitly. No concurrent mutation or mutation during enumeration.
    /// </summary>
    public sealed partial class RuntimeTagSet
    {
        private int[] m_Ids;
        private readonly ulong[] m_Words;
        private int m_Count;
        public TagRegistry Registry { get; }
        public int Count => m_Count;
        public bool IsEmpty => m_Count == 0;
        public TagSetStorage Storage => m_PackedUsed >= 0 ? TagSetStorage.Compressed : m_Words == null ? TagSetStorage.Sparse : TagSetStorage.Dense;
        public int Capacity => m_PackedUsed >= 0 ? Math.Max(m_Count, ReservedMemberCapacity) : m_Words == null ? m_Ids.Length : Registry.Count;
        public long BufferBytes => m_Words == null ? 4L * m_Ids.Length : 8L * m_Words.Length;

        public RuntimeTagSet(TagRegistry registry, int capacity = 0, TagSetStorage storage = TagSetStorage.Auto)
        {
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (storage < TagSetStorage.Auto || storage > TagSetStorage.Compressed) throw new ArgumentOutOfRangeException(nameof(storage));
            capacity = Math.Min(capacity, registry.Count);
            if (storage == TagSetStorage.Compressed)
            {
                if (registry.Count > PackedUniverseLimit) throw new ArgumentOutOfRangeException(nameof(registry), "Compressed storage supports at most 2^20 registry IDs.");
                m_PackedUsed = 0;
                capacity = Math.Min(capacity, PackedMaximum);
                m_Ids = capacity == 0 ? Array.Empty<int>() : new int[capacity];
                return;
            }
            bool dense = storage == TagSetStorage.Dense || (storage == TagSetStorage.Auto && registry.Count != 0 && 8L * registry.WordCount <= 4L * capacity);
            if (dense) m_Words = registry.WordCount == 0 ? Array.Empty<ulong>() : new ulong[registry.WordCount];
            else m_Ids = capacity == 0 ? Array.Empty<int>() : new int[capacity];
        }
        public RuntimeTagSet(RuntimeTagSet source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            Registry = source.Registry;
            m_Count = source.m_Count;
            m_PackedUsed = source.m_PackedUsed;
            if (source.m_Words != null)
            {
                m_Words = source.m_Words.Length == 0 ? Array.Empty<ulong>() : new ulong[source.m_Words.Length];
                Array.Copy(source.m_Words, m_Words, m_Words.Length);
            }
            else
            {
                m_Ids = source.m_Ids.Length == 0 ? Array.Empty<int>() : new int[source.m_Ids.Length];
                Array.Copy(source.m_Ids, m_Ids, m_PackedUsed >= 0 ? m_PackedUsed : m_Count);
            }
        }
        /// <summary>
        /// Load resolved handles in one pass, sorting once when needed. The new set owns its
        /// storage; invalid default handles are ignored and foreign handles are rejected.
        /// </summary>
        public static RuntimeTagSet FromTags(TagRegistry registry, ReadOnlySpan<RuntimeTag> tags,
            int capacity = 0, TagSetStorage storage = TagSetStorage.Auto)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            var result = new RuntimeTagSet(registry, Math.Max(tags.Length, capacity), storage);
            if (result.m_PackedUsed >= 0 || result.m_Words != null || tags.Length > result.m_Ids.Length)
            {
                for (int i = 0; i < tags.Length; i++) result.AddTag(tags[i]);
                return result;
            }
            int count = 0;
            bool ordered = true;
            for (int i = 0; i < tags.Length; i++)
            {
                RuntimeTag tag = tags[i];
                if (!result.Accept(tag)) continue;
                if (count != 0 && tag.Id <= result.m_Ids[count - 1]) ordered = false;
                result.m_Ids[count++] = tag.Id;
            }
            if (ordered) { result.m_Count = count; return result; }
            Array.Sort(result.m_Ids, 0, count);
            int write = 0;
            for (int i = 0; i < count; i++)
                if (write == 0 || result.m_Ids[i] != result.m_Ids[write - 1]) result.m_Ids[write++] = result.m_Ids[i];
            result.m_Count = write;
            return result;
        }
        /// <summary>Explicit allocating conversion. Existing sets never change storage implicitly.</summary>
        public RuntimeTagSet ToStorage(TagSetStorage storage, int capacity = 0)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            var result = new RuntimeTagSet(Registry, Math.Max(m_Count, capacity), storage);
            result.CopyFrom(this);
            return result;
        }
        /// <summary>Resolve once, sort once, and deduplicate at the authoring/loading boundary.</summary>
        internal void LoadAuthoring(GameplayTagContainer source)
        {
            if (m_PackedUsed >= 0)
            { for (int i = 0; i < source.Count; i++) AddId(Registry.Resolve(source[i].Name).Id); return; }
            if (m_Words != null)
            {
                int count = 0;
                for (int i = 0; i < source.Count; i++)
                {
                    int id = Registry.Resolve(source[i].Name).Id, word = id >> 6;
                    ulong bit = 1UL << (id & 63), before = m_Words[word];
                    if ((before & bit) == 0) { m_Words[word] = before | bit; count++; }
                }
                m_Count = count;
                return;
            }
            EnsureCapacity(source.Count);
            // More authoring aliases than canonical IDs cannot fit in a universe-bounded buffer.
            if (source.Count > m_Ids.Length)
            {
                for (int i = 0; i < source.Count; i++) AddId(Registry.Resolve(source[i].Name).Id);
                return;
            }
            bool ordered = true;
            for (int i = 0; i < source.Count; i++)
            {
                int id = Registry.Resolve(source[i].Name).Id;
                if (i != 0 && id <= m_Ids[i - 1]) ordered = false;
                m_Ids[i] = id;
            }
            if (ordered) { m_Count = source.Count; return; }
            Array.Sort(m_Ids, 0, source.Count);
            int write = 0;
            for (int i = 0; i < source.Count; i++)
                if (write == 0 || m_Ids[i] != m_Ids[write - 1]) m_Ids[write++] = m_Ids[i];
            m_Count = write;
        }
        private static RuntimeTagSet Required(RuntimeTagSet value) => value ?? throw new ArgumentNullException(nameof(value));
        private void Require(RuntimeTagSet other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            Registry.RequireSame(other.Registry);
        }
        private bool Accept(RuntimeTag tag)
        {
            if (tag.Owner == null) return false;
            Registry.RequireSame(tag.Owner);
            return true;
        }
        public void EnsureCapacity(int capacity)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (m_PackedUsed >= 0) { ReservePacked(Math.Min(capacity, PackedMaximum)); return; }
            if (m_Words != null || capacity <= m_Ids.Length) return;
            capacity = Math.Min(capacity, Registry.Count);
            int grown = (int)Math.Min(Registry.Count, Math.Max(4L, 2L * m_Ids.Length));
            Array.Resize(ref m_Ids, Math.Max(capacity, grown));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasTagExact(RuntimeTag tag)
        {
            if (!ReferenceEquals(Registry, tag.Owner))
            {
                if (tag.Owner == null) return false;
                Registry.RequireSame(tag.Owner);
            }
            return ContainsId(tag.Id);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool ContainsId(int id) => m_PackedUsed >= 0 ? PackedContains(id) : m_Words != null
            ? (m_Words[id >> 6] & (1UL << (id & 63))) != 0
            : ContainsSparseId(id);
        public bool HasTag(RuntimeTag tag) => Accept(tag) && AnyInRange(tag.Id, Registry.Ends[tag.Id]);
        public bool AddTag(RuntimeTag tag) => Accept(tag) && AddId(tag.Id);
        public bool RemoveTag(RuntimeTag tag) => Accept(tag) && RemoveId(tag.Id);
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal bool AddId(int id)
        {
            if (m_PackedUsed >= 0) return PackedAdd(id);
            if (m_Words != null)
            {
                int word = id >> 6;
                ulong bit = 1UL << (id & 63), before = m_Words[word];
                if ((before & bit) != 0) return false;
                m_Words[word] = before | bit;
                m_Count++;
                return true;
            }
            return AddSparseId(id);
        }
        private bool AddSparseId(int id)
        {
            // Ordered loading and append-heavy mutation should not binary-search existing members.
            if (m_Count == 0 || id > m_Ids[m_Count - 1])
            {
                EnsureCapacity(m_Count + 1);
                m_Ids[m_Count++] = id;
                return true;
            }
            int index = IndexOf(id);
            if (index >= 0) return false;
            index = ~index;
            EnsureCapacity(m_Count + 1);
            Array.Copy(m_Ids, index, m_Ids, index + 1, m_Count - index);
            m_Ids[index] = id;
            m_Count++;
            return true;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private bool RemoveId(int id)
        {
            if (m_PackedUsed >= 0) return PackedRemove(id);
            if (m_Words != null)
            {
                int word = id >> 6;
                ulong bit = 1UL << (id & 63), before = m_Words[word];
                if ((before & bit) == 0) return false;
                m_Words[word] = before & ~bit;
                m_Count--;
                return true;
            }
            return RemoveSparseId(id);
        }
        private bool RemoveSparseId(int id)
        {
            int index = IndexOf(id);
            if (index < 0) return false;
            m_Count--;
            Array.Copy(m_Ids, index + 1, m_Ids, index, m_Count - index);
            return true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ContainsSparseId(int id)
        {
            int n = m_Count;
            if (n > 32) return IndexOf(id) >= 0;
            int[] values = m_Ids;
            if (n <= 2)
                return n == 2 ? (values[0] == id | values[1] == id) : n == 1 && values[0] == id;
            int width = Vector<int>.Count;
            if (n < width)
            {
                bool found = false;
                for (int i = 0; i < n; i++) found |= values[i] == id;
                return found;
            }
            if (Vector.IsHardwareAccelerated)
            {
                var target = new Vector<int>(id);
                var found = Vector<int>.Zero;
                int i = 0;
                for (; i <= n - width; i += width)
                    found = Vector.BitwiseOr(found, Vector.Equals(new Vector<int>(values, i), target));
                if (!Vector.EqualsAll(found, Vector<int>.Zero)) return true;
                for (; i < n; i++) if (values[i] == id) return true;
                return false;
            }
            return IndexOf(id) >= 0;
        }
        private int IndexOf(int id)
        {
            int low = 0, high = m_Count - 1;
            while (low <= high)
            {
                int mid = low + ((high - low) >> 1), value = m_Ids[mid];
                if (value == id) return mid;
                if (value < id) low = mid + 1; else high = mid - 1;
            }
            return ~low;
        }
        internal bool AnyInRange(int start, int end)
        {
            if (m_Count == 0 || start == end) return false;
            if (m_PackedUsed >= 0) return PackedRange(start, end);
            if (m_Words == null)
            {
                int at = IndexOf(start);
                if (at >= 0) return true;
                at = ~at;
                return at < m_Count && m_Ids[at] < end;
            }
            int first = start >> 6, last = (end - 1) >> 6;
            ulong lowMask = ulong.MaxValue << (start & 63);
            ulong highMask = (end & 63) == 0 ? ulong.MaxValue : (1UL << (end & 63)) - 1;
            if (first == last) return (m_Words[first] & lowMask & highMask) != 0;
            if ((m_Words[first] & lowMask) != 0) return true;
            for (int i = first + 1; i < last; i++) if (m_Words[i] != 0) return true;
            return (m_Words[last] & highMask) != 0;
        }
        public void Clear()
        {
            if (m_Count == 0) return;
            if (m_PackedUsed >= 0) m_PackedUsed = 0;
            if (m_Words != null) Array.Clear(m_Words, 0, m_Words.Length);
            m_Count = 0;
        }
        public void CopyFrom(RuntimeTagSet other)
        {
            Require(other);
            if (ReferenceEquals(this, other)) return;
            if (m_PackedUsed >= 0 || other.m_PackedUsed >= 0) { CopyPackedAware(other); return; }
            if (m_Words != null && other.m_Words != null)
                Array.Copy(other.m_Words, m_Words, m_Words.Length);
            else if (m_Words == null && other.m_Words == null)
            {
                EnsureCapacity(other.m_Count);
                Array.Copy(other.m_Ids, m_Ids, other.m_Count);
            }
            else
            {
                Clear();
                EnsureCapacity(other.m_Count);
                var iterator = new IdEnumerator(other);
                while (iterator.MoveNext()) AppendOrdered(iterator.Current);
            }
            m_Count = other.m_Count;
        }
        // Used only for known unique ascending IDs, not an external admission API.
        private void AppendOrdered(int id)
        {
            if (m_PackedUsed >= 0)
            {
                int key = PackedKey(id), bit = 1 << (id & 15);
                if (m_PackedUsed != 0 && RecordKey(m_Ids[m_PackedUsed - 1]) == key) m_Ids[m_PackedUsed - 1] |= bit;
                else { ReservePacked(m_PackedUsed + 1); m_Ids[m_PackedUsed++] = key | bit; }
                m_Count++; return;
            }
            if (m_Words != null) m_Words[id >> 6] |= 1UL << (id & 63);
            else
            {
                if (m_Count == m_Ids.Length) EnsureCapacity(m_Count + 1);
                m_Ids[m_Count] = id;
            }
            m_Count++;
        }
        public void AppendTags(RuntimeTagSet other)
        {
            Require(other);
            if (other.m_Count == 0 || ReferenceEquals(this, other)) return;
            if (m_Count == 0) { CopyFrom(other); return; }
            if (m_PackedUsed >= 0 || other.m_PackedUsed >= 0) { AppendPackedAware(other); return; }
            if (m_Words != null)
            {
                if (other.m_Words != null)
                {
                    m_Count = RuntimeBitOperations.Union(m_Words, other.m_Words, m_Words);
                }
                else for (int i = 0; i < other.m_Count; i++) AddId(other.m_Ids[i]);
                return;
            }
            if (other.m_Words == null && m_Ids[m_Count - 1] < other.m_Ids[0])
            {
                EnsureCapacity(m_Count + other.m_Count);
                Array.Copy(other.m_Ids, 0, m_Ids, m_Count, other.m_Count);
                m_Count += other.m_Count;
                return;
            }
            if (other.m_Words == null)
            {
                if (other.m_Ids[other.m_Count - 1] < m_Ids[0])
                {
                    EnsureCapacity(m_Count + other.m_Count);
                    Array.Copy(m_Ids, 0, m_Ids, other.m_Count, m_Count);
                    Array.Copy(other.m_Ids, 0, m_Ids, 0, other.m_Count);
                    m_Count += other.m_Count;
                    return;
                }
                AppendSparse(other);
                return;
            }
            int unionCount = CountUnion(this, other);
            if (unionCount == m_Count) return;
            EnsureCapacity(unionCount);
            int read = m_Count - 1, write = unionCount - 1;
            var reverse = new IdEnumerator(other, true);
            bool incomingExists = reverse.MoveNext();
            while (read >= 0 && incomingExists)
            {
                int own = m_Ids[read], incoming = reverse.Current;
                if (own > incoming) m_Ids[write--] = m_Ids[read--];
                else if (own < incoming) { m_Ids[write--] = incoming; incomingExists = reverse.MoveNext(); }
                else { m_Ids[write--] = m_Ids[read--]; incomingExists = reverse.MoveNext(); }
            }
            while (incomingExists) { m_Ids[write--] = reverse.Current; incomingExists = reverse.MoveNext(); }
            m_Count = unionCount;
        }
        private void AppendSparse(RuntimeTagSet other)
        {
            if ((long)m_Count * 32 < other.m_Count && SparseAll(other, this))
            { CopyFrom(other); return; }
            if ((long)other.m_Count * 32 < m_Count && SparseAll(this, other)) return;
            int count = CountUnionSparse(this, other);
            if (count == m_Count) return;
            EnsureCapacity(count);
            int a = m_Count - 1, b = other.m_Count - 1, write = count - 1;
            int[] incoming = other.m_Ids;
            while (a >= 0 && b >= 0)
            {
                int x = m_Ids[a], y = incoming[b];
                if (x > y) { m_Ids[write--] = x; a--; }
                else if (x < y) { m_Ids[write--] = y; b--; }
                else { m_Ids[write--] = x; a--; b--; }
            }
            if (b >= 0) Array.Copy(incoming, 0, m_Ids, 0, b + 1);
            m_Count = count;
        }
        private static int CountUnionSparse(RuntimeTagSet left, RuntimeTagSet right)
        {
            if (left.m_Count == 0) return right.m_Count;
            if (right.m_Count == 0) return left.m_Count;
            if (left.m_Ids[left.m_Count - 1] < right.m_Ids[0] || right.m_Ids[right.m_Count - 1] < left.m_Ids[0])
                return checked(left.m_Count + right.m_Count);
            int a = 0, b = 0, duplicates = 0;
            int[] x = left.m_Ids, y = right.m_Ids;
            while (a < left.m_Count && b < right.m_Count)
            {
                if (x[a] < y[b]) a++;
                else if (x[a] > y[b]) b++;
                else { duplicates++; a++; b++; }
            }
            return checked(left.m_Count + (right.m_Count - duplicates));
        }
        public bool RemoveTags(RuntimeTagSet other)
        {
            Require(other);
            if (m_Count == 0 || other.m_Count == 0) return false;
            if (ReferenceEquals(this, other)) { Clear(); return true; }
            if (m_PackedUsed >= 0 || other.m_PackedUsed >= 0) return RemovePackedAware(other);
            int beforeCount = m_Count;
            if (m_Words != null)
            {
                if (other.m_Words != null)
                {
                    m_Count = RuntimeBitOperations.Except(m_Words, other.m_Words, m_Words);
                }
                else for (int i = 0; i < other.m_Count; i++) RemoveId(other.m_Ids[i]);
            }
            else
            {
                int write = 0;
                if (other.m_Words != null)
                {
                    for (int i = 0; i < m_Count; i++) if (!other.ContainsId(m_Ids[i])) m_Ids[write++] = m_Ids[i];
                }
                else
                {
                    if ((long)m_Count * 32 < other.m_Count || (long)other.m_Count * 32 < m_Count)
                    {
                        SparseDifference(this, other, this);
                        return beforeCount != m_Count;
                    }
                    int i = 0, j = 0;
                    while (i < m_Count && j < other.m_Count)
                    {
                        int left = m_Ids[i], right = other.m_Ids[j];
                        if (left < right) { m_Ids[write++] = left; i++; }
                        else if (left > right) j++;
                        else { i++; j++; }
                    }
                    if (i < m_Count) { Array.Copy(m_Ids, i, m_Ids, write, m_Count - i); write += m_Count - i; }
                }
                m_Count = write;
            }
            return beforeCount != m_Count;
        }
        public bool HasAnyExact(RuntimeTagSet other)
        {
            Require(other);
            if (m_Count == 0 || other.m_Count == 0) return false;
            if (m_PackedUsed >= 0 && other.m_PackedUsed >= 0) return PackedAnyDirect(this, other);
            if (m_Words != null && other.m_Words != null)
            {
                return RuntimeBitOperations.IsAny(m_Words, other.m_Words);
            }
            if (m_PackedUsed < 0 && other.m_PackedUsed < 0 && m_Ids != null && other.m_Ids != null) return SparseAny(this, other);
            RuntimeTagSet small = m_Count <= other.m_Count ? this : other;
            RuntimeTagSet large = ReferenceEquals(small, this) ? other : this;
            var iterator = new IdEnumerator(small);
            while (iterator.MoveNext()) if (large.ContainsId(iterator.Current)) return true;
            return false;
        }
        public bool HasAllExact(RuntimeTagSet other)
        {
            Require(other);
            if (other.m_Count == 0) return true;
            if (other.m_Count > m_Count) return false;
            if (m_PackedUsed >= 0 && other.m_PackedUsed >= 0) return PackedAllDirect(this, other);
            if (m_Words != null && other.m_Words != null)
            {
                return RuntimeBitOperations.IsSubset(other.m_Words, m_Words);
            }
            if (m_PackedUsed < 0 && other.m_PackedUsed < 0 && m_Ids != null && other.m_Ids != null) return SparseAll(this, other);
            var iterator = new IdEnumerator(other);
            while (iterator.MoveNext()) if (!ContainsId(iterator.Current)) return false;
            return true;
        }
        public bool HasAny(RuntimeTagSet conditions)
        {
            Require(conditions);
            var iterator = new IdEnumerator(conditions);
            while (iterator.MoveNext()) if (AnyInRange(iterator.Current, Registry.Ends[iterator.Current])) return true;
            return false;
        }
        public bool HasAll(RuntimeTagSet conditions)
        {
            Require(conditions);
            var iterator = new IdEnumerator(conditions);
            while (iterator.MoveNext()) if (!AnyInRange(iterator.Current, Registry.Ends[iterator.Current])) return false;
            return true;
        }
        public bool MatchesQuery(FrozenGameplayTagQuery query) => query != null && query.Matches(this);
        public static RuntimeTagSet Union(RuntimeTagSet left, RuntimeTagSet right, TagSetStorage storage = TagSetStorage.Auto)
        {
            Required(left).Require(right);
            int maximum = (int)Math.Min(left.Registry.Count, (long)left.m_Count + right.m_Count);
            var result = new RuntimeTagSet(left.Registry, storage == TagSetStorage.Compressed ? 0 : maximum, storage);
            if (maximum == 0) return result;
            if (storage == TagSetStorage.Compressed)
            {
                result.ReservePacked(Math.Min(result.PackedMaximum, CountPackedRecords(left) + CountPackedRecords(right)));
                if (ReferenceEquals(left, right) || right.m_Count == 0) result.CopyFrom(left);
                else if (left.m_Count == 0) result.CopyFrom(right);
                else PackedBinaryInto(left, right, result, 0);
                return result;
            }
            UnionInto(left, right, result);
            return result;
        }
        public static void UnionInto(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result)
        {
            Required(left).Require(right);
            left.Require(result);
            if (ReferenceEquals(result, left)) { result.AppendTags(right); return; }
            if (ReferenceEquals(result, right)) { result.AppendTags(left); return; }
            if (ReferenceEquals(left, right) || right.m_Count == 0) { result.CopyFrom(left); return; }
            if (left.m_Count == 0) { result.CopyFrom(right); return; }
            if (left.m_PackedUsed >= 0 || right.m_PackedUsed >= 0 || result.m_PackedUsed >= 0)
            { PackedBinaryInto(left, right, result, 0); return; }
            if (result.m_Words != null)
            {
                if (left.m_Words != null && right.m_Words != null)
                {
                    result.m_Count = RuntimeBitOperations.Union(left.m_Words, right.m_Words, result.m_Words);
                }
                else if (left.m_Words != null) WriteDenseUnion(left, right, result);
                else if (right.m_Words != null) WriteDenseUnion(right, left, result);
                else WriteDenseUnion(left, right, result);
                return;
            }
            if (left.m_Words == null && right.m_Words == null)
            {
                long upper = Math.Min(left.Registry.Count, (long)left.m_Count + right.m_Count);
                if (result.Capacity < upper) result.EnsureCapacity(CountUnionSparse(left, right));
                int leftIndex = 0, rightIndex = 0, write = 0;
                int[] x = left.m_Ids, y = right.m_Ids, destination = result.m_Ids;
                if (x[left.m_Count - 1] < y[0] || y[right.m_Count - 1] < x[0])
                {
                    RuntimeTagSet first = x[0] < y[0] ? left : right;
                    RuntimeTagSet second = ReferenceEquals(first, left) ? right : left;
                    Array.Copy(first.m_Ids, 0, destination, 0, first.m_Count);
                    Array.Copy(second.m_Ids, 0, destination, first.m_Count, second.m_Count);
                    result.m_Count = first.m_Count + second.m_Count;
                    return;
                }
                while (leftIndex < left.m_Count && rightIndex < right.m_Count)
                {
                    int first = x[leftIndex], second = y[rightIndex];
                    if (first < second) { destination[write++] = first; leftIndex++; }
                    else if (first > second) { destination[write++] = second; rightIndex++; }
                    else { destination[write++] = first; leftIndex++; rightIndex++; }
                }
                if (leftIndex < left.m_Count) { Array.Copy(x, leftIndex, destination, write, left.m_Count - leftIndex); write += left.m_Count - leftIndex; }
                if (rightIndex < right.m_Count) { Array.Copy(y, rightIndex, destination, write, right.m_Count - rightIndex); write += right.m_Count - rightIndex; }
                result.m_Count = write;
                return;
            }
            long maximum = Math.Min(left.Registry.Count, (long)left.m_Count + right.m_Count);
            // A count pass is needed only when the caller's capacity cannot hold the upper bound.
            // Never resize a buffer whose capacity already holds the ACTUAL result.
            if (result.Capacity < maximum) result.EnsureCapacity(CountUnion(left, right));
            result.m_Count = 0;
            var a = new IdEnumerator(left);
            var b = new IdEnumerator(right);
            bool hasA = a.MoveNext(), hasB = b.MoveNext();
            while (hasA && hasB)
            {
                int x = a.Current, y = b.Current;
                if (x < y) { result.m_Ids[result.m_Count++] = x; hasA = a.MoveNext(); }
                else if (x > y) { result.m_Ids[result.m_Count++] = y; hasB = b.MoveNext(); }
                else { result.m_Ids[result.m_Count++] = x; hasA = a.MoveNext(); hasB = b.MoveNext(); }
            }
            while (hasA) { result.m_Ids[result.m_Count++] = a.Current; hasA = a.MoveNext(); }
            while (hasB) { result.m_Ids[result.m_Count++] = b.Current; hasB = b.MoveNext(); }
        }
        // Independent output has already passed identity/alias checks. Fuse dense copy (or
        // sparse materialization) with incoming sparse members and publish Count just once.
        private static void WriteDenseUnion(RuntimeTagSet first, RuntimeTagSet second, RuntimeTagSet result)
        {
            ulong[] words = result.m_Words;
            if (first.m_Words != null) Array.Copy(first.m_Words, words, words.Length);
            else
            {
                if (result.m_Count != 0) Array.Clear(words, 0, words.Length);
                int[] ids = first.m_Ids;
                for (int i = 0; i < first.m_Count; i++)
                { int id = ids[i]; words[id >> 6] |= 1UL << (id & 63); }
            }
            int count = first.m_Count;
            int[] incoming = second.m_Ids;
            for (int i = 0; i < second.m_Count; i++)
            {
                int id = incoming[i], word = id >> 6;
                ulong bit = 1UL << (id & 63), before = words[word];
                if ((before & bit) == 0) { words[word] = before | bit; count++; }
            }
            result.m_Count = count;
        }
        private static int CountUnion(RuntimeTagSet left, RuntimeTagSet right)
        {
            var a = new IdEnumerator(left);
            var b = new IdEnumerator(right);
            bool hasA = a.MoveNext(), hasB = b.MoveNext();
            int count = 0;
            while (hasA && hasB)
            {
                int x = a.Current, y = b.Current;
                if (x < y) hasA = a.MoveNext();
                else if (x > y) hasB = b.MoveNext();
                else { hasA = a.MoveNext(); hasB = b.MoveNext(); }
                count++;
            }
            while (hasA) { count++; hasA = a.MoveNext(); }
            while (hasB) { count++; hasB = b.MoveNext(); }
            return count;
        }
        public static RuntimeTagSet IntersectionExact(RuntimeTagSet left, RuntimeTagSet right, TagSetStorage storage = TagSetStorage.Auto)
        {
            Required(left).Require(right);
            var result = new RuntimeTagSet(left.Registry, storage == TagSetStorage.Compressed ? 0 : Math.Min(left.Count, right.Count), storage);
            if (storage == TagSetStorage.Compressed) result.ReservePacked(Math.Min(CountPackedRecords(left), CountPackedRecords(right)));
            IntersectionExactInto(left, right, result);
            return result;
        }
        public void FilterExactInto(RuntimeTagSet conditions, RuntimeTagSet result) => IntersectionExactInto(this, conditions, result);
        public static void IntersectionExactInto(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result)
        {
            Required(left).Require(right);
            left.Require(result);
            if (left.m_Count == 0 || right.m_Count == 0) { result.Clear(); return; }
            if (ReferenceEquals(result, left)) { result.IntersectWith(right); return; }
            if (ReferenceEquals(result, right)) { result.IntersectWith(left); return; }
            if (left.m_PackedUsed >= 0 || right.m_PackedUsed >= 0 || result.m_PackedUsed >= 0)
            { PackedBinaryInto(left, right, result, 1); return; }
            if (result.m_Words != null && left.m_Words != null && right.m_Words != null)
            {
                result.m_Count = RuntimeBitOperations.Intersect(left.m_Words, right.m_Words, result.m_Words);
                return;
            }
            result.Clear();
            if (left.m_Ids != null && right.m_Ids != null)
            {
                SparseIntersection(left, right, result);
                return;
            }
            RuntimeTagSet small = left.Count <= right.Count ? left : right;
            RuntimeTagSet large = ReferenceEquals(small, left) ? right : left;
            var iterator = new IdEnumerator(small);
            while (iterator.MoveNext()) if (large.ContainsId(iterator.Current)) result.AppendOrdered(iterator.Current);
        }
        private void IntersectWith(RuntimeTagSet other)
        {
            if (m_Count == 0 || ReferenceEquals(this, other)) return;
            if (other.m_Count == 0) { Clear(); return; }
            if (m_PackedUsed >= 0 || other.m_PackedUsed >= 0) { PackedBinaryInto(this, other, this, 1); return; }
            if (m_Words == null)
            {
                if (other.m_Ids != null && (long)m_Count * 32 >= other.m_Count)
                { SparseIntersection(this, other, this); return; }
                int write = 0;
                for (int i = 0; i < m_Count; i++) if (other.ContainsId(m_Ids[i])) m_Ids[write++] = m_Ids[i];
                m_Count = write;
            }
            else
            {
                if (other.m_Words != null)
                {
                    m_Count = RuntimeBitOperations.Intersect(m_Words, other.m_Words, m_Words);
                    return;
                }
                int count = 0, cursor = 0;
                for (int i = 0; i < m_Words.Length; i++)
                {
                    ulong mask = 0;
                    if (other.m_Words != null) mask = other.m_Words[i];
                    else while (cursor < other.m_Count && (other.m_Ids[cursor] >> 6) == i)
                    { mask |= 1UL << (other.m_Ids[cursor++] & 63); }
                    ulong word = m_Words[i] & mask;
                    m_Words[i] = word;
                    count += Bits.Count(word);
                }
                m_Count = count;
            }
        }
        /// <summary>Independent exact difference; unlike copy+remove this is a direct output operation.</summary>
        public static RuntimeTagSet DifferenceExact(RuntimeTagSet left, RuntimeTagSet right, TagSetStorage storage = TagSetStorage.Auto)
        {
            Required(left).Require(right);
            var result = new RuntimeTagSet(left.Registry, storage == TagSetStorage.Compressed ? 0 : left.Count, storage);
            if (storage == TagSetStorage.Compressed) result.ReservePacked(CountPackedRecords(left));
            DifferenceExactInto(left, right, result);
            return result;
        }
        /// <summary>
        /// Writes exact difference. Both input aliases are supported. A separate right-output
        /// alias with sparse output uses its own reserved tail to preserve exclusions. Reserve
        /// union-sized capacity to make that alias allocation-free; otherwise its buffer grows.
        /// </summary>
        public static void DifferenceExactInto(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result)
        {
            Required(left).Require(right);
            left.Require(result);
            if (left.m_Count == 0) { result.Clear(); return; }
            if (right.m_Count == 0) { result.CopyFrom(left); return; }
            if (ReferenceEquals(left, right)) { result.Clear(); return; }
            if (ReferenceEquals(result, left)) { result.RemoveTags(right); return; }
            if (left.m_PackedUsed >= 0 || right.m_PackedUsed >= 0 || result.m_PackedUsed >= 0)
            {
                if (ReferenceEquals(result, right))
                {
                    if (result.m_PackedUsed >= 0) PackedDifferenceRight(left, result);
                    else if (result.m_Words != null) PackedBinaryInto(left, right, result, 2);
                    else DifferenceIntoSparseRight(left, result);
                }
                else PackedBinaryInto(left, right, result, 2);
                return;
            }
            if (left.m_Words != null && right.m_Words != null && result.m_Words != null)
            {
                result.m_Count = RuntimeBitOperations.Except(left.m_Words, right.m_Words, result.m_Words);
                return;
            }
            if (ReferenceEquals(result, right))
            {
                if (result.m_Words != null) DifferenceIntoDenseRight(left, result);
                else DifferenceIntoSparseRight(left, result);
                return;
            }
            result.Clear();
            if (left.m_Ids != null && right.m_Ids != null)
            {
                SparseDifference(left, right, result);
                return;
            }
            var iterator = new IdEnumerator(left);
            while (iterator.MoveNext())
                if (!right.ContainsId(iterator.Current)) result.AppendOrdered(iterator.Current);
        }
        private static void DifferenceIntoDenseRight(RuntimeTagSet left, RuntimeTagSet right)
        {
            // Dense/dense was handled by the word kernel. Here the left input is sparse.
            // Read each old exclusion word before overwriting it; zero untouched word gaps.
            int[] ids = left.m_Ids;
            ulong[] words = right.m_Words;
            int i = 0, cleared = 0, count = 0;
            while (i < left.m_Count)
            {
                int wordIndex = ids[i] >> 6;
                if (wordIndex > cleared) Array.Clear(words, cleared, wordIndex - cleared);
                ulong mask = 0;
                do { mask |= 1UL << (ids[i++] & 63); }
                while (i < left.m_Count && (ids[i] >> 6) == wordIndex);
                ulong word = mask & ~words[wordIndex];
                words[wordIndex] = word;
                count += Bits.Count(word);
                cleared = wordIndex + 1;
            }
            if (cleared < words.Length) Array.Clear(words, cleared, words.Length - cleared);
            right.m_Count = count;
        }
        private static int CountExactIntersection(RuntimeTagSet left, RuntimeTagSet right)
        {
            if (left.m_Count == 0 || right.m_Count == 0) return 0;
            if (left.m_Count > right.m_Count) { var swap = left; left = right; right = swap; }
            int count = 0;
            if (left.m_PackedUsed < 0 && right.m_PackedUsed < 0 && left.m_Ids != null && right.m_Ids != null)
            {
                int i = 0, j = 0;
                bool skew = (long)left.m_Count * 32 < right.m_Count;
                while (i < left.m_Count && j < right.m_Count)
                {
                    int a = left.m_Ids[i], b = right.m_Ids[j];
                    if (a < b) i++;
                    else if (a > b) j = skew ? LowerBound(right.m_Ids, j + 1, right.m_Count, a) : j + 1;
                    else { count++; i++; j++; }
                }
                return count;
            }
            var scan = new IdEnumerator(left);
            while (scan.MoveNext()) if (right.ContainsId(scan.Current)) count++;
            return count;
        }
        private static void DifferenceIntoSparseRight(RuntimeTagSet left, RuntimeTagSet right)
        {
            if (left.m_Count == 0) { right.Clear(); return; }
            if (right.m_Count == 0) { right.CopyFrom(left); return; }
            int exclusions = right.m_Count;
            int upper = (int)Math.Min(right.Registry.Count, (long)left.m_Count + exclusions);
            int offset;
            if (right.m_Ids.Length >= upper)
            {
                // Existing spare capacity proves the regions cannot overlap. Avoid a count
                // pass when the caller has already paid for the union's upper bound.
                offset = right.m_Ids.Length - exclusions;
            }
            else
            {
                int differenceCount = left.m_Count - CountExactIntersection(left, right);
                if (differenceCount == 0) { right.Clear(); return; }
                // (A \ B) and original B are disjoint; |A\B|+|B|=|A union B| <= U.
                right.EnsureCapacity(differenceCount + exclusions);
                offset = differenceCount;
            }
            int[] destination = right.m_Ids;
            Array.Copy(destination, 0, destination, offset, exclusions);
            int at = offset, end = offset + exclusions, write = 0;
            var scan = new IdEnumerator(left);
            while (scan.MoveNext())
            {
                int id = scan.Current;
                while (at < end && destination[at] < id) at++;
                if (at == end || destination[at] != id) destination[write++] = id;
            }
            right.m_Count = write;
        }
        // Lower bound from a monotone cursor. No per-member binary search of already-consumed data.
        private static int LowerBound(int[] values, int from, int count, int target)
        {
            int high = count;
            while (from < high)
            {
                int mid = from + ((high - from) >> 1);
                if (values[mid] < target) from = mid + 1; else high = mid;
            }
            return from;
        }
        private static bool SparseAny(RuntimeTagSet left, RuntimeTagSet right)
        {
            if (left.m_Count == 0 || right.m_Count == 0) return false;
            if (left.m_Ids[left.m_Count - 1] < right.m_Ids[0] || right.m_Ids[right.m_Count - 1] < left.m_Ids[0]) return false;
            if (left.m_Count > right.m_Count) { var swap = left; left = right; right = swap; }
            int i = 0, j = 0;
            bool skew = (long)left.m_Count * 32 < right.m_Count;
            while (i < left.m_Count && j < right.m_Count)
            {
                int a = left.m_Ids[i], b = right.m_Ids[j];
                if (a == b) return true;
                if (a < b) i++;
                else j = skew ? LowerBound(right.m_Ids, j + 1, right.m_Count, a) : j + 1;
            }
            return false;
        }
        private static bool SparseAll(RuntimeTagSet left, RuntimeTagSet right)
        {
            int i = 0, j = 0;
            bool skew = (long)right.m_Count * 32 < left.m_Count;
            while (j < right.m_Count)
            {
                if (i == left.m_Count) return false;
                int a = left.m_Ids[i], b = right.m_Ids[j];
                if (a > b) return false;
                if (a == b) { i++; j++; }
                else i = skew ? LowerBound(left.m_Ids, i + 1, left.m_Count, b) : i + 1;
            }
            return true;
        }
        private static void SparseIntersection(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result)
        {
            // Compaction is safe when result aliases either input: write never overtakes a read.
            if (left.m_Count > right.m_Count) { var swap = left; left = right; right = swap; }
            int n = left.m_Count, m = right.m_Count, i = 0, j = 0, write = 0;
            if (n == 0 || m == 0 || left.m_Ids[n - 1] < right.m_Ids[0] || right.m_Ids[m - 1] < left.m_Ids[0])
            { result.m_Count = 0; return; }
            bool skew = (long)n * 32 < m;
            while (i < n && j < m)
            {
                int a = left.m_Ids[i], b = right.m_Ids[j];
                if (a < b) i++;
                else if (a > b) j = skew ? LowerBound(right.m_Ids, j + 1, m, a) : j + 1;
                else
                {
                    if (result.m_Ids != null)
                    {
                        if (write == result.m_Ids.Length) result.EnsureCapacity(write + 1);
                        result.m_Ids[write++] = a;
                    }
                    else { result.m_Words[a >> 6] |= 1UL << (a & 63); write++; }
                    i++; j++;
                }
            }
            result.m_Count = write;
        }
        private static void SparseDifference(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result)
        {
            int n = left.m_Count, m = right.m_Count, i = 0, j = 0, write = 0;
            if (n == 0 || m == 0 || left.m_Ids[n - 1] < right.m_Ids[0] || right.m_Ids[m - 1] < left.m_Ids[0])
            { result.CopyFrom(left); return; }
            bool skipLeft = (long)m * 32 < n;
            bool skipRight = (long)n * 32 < m;
            if (!skipLeft && !skipRight && result.m_Ids != null && result.m_Ids.Length >= n)
            {
                int[] x = left.m_Ids, y = right.m_Ids, destination = result.m_Ids;
                while (i < n && j < m)
                {
                    int a = x[i], b = y[j];
                    if (a < b) { destination[write++] = a; i++; }
                    else if (a > b) j++;
                    else { i++; j++; }
                }
                if (i < n) { Array.Copy(x, i, destination, write, n - i); write += n - i; }
                result.m_Count = write;
                return;
            }
            while (i < n)
            {
                int end;
                if (j == m) end = n;
                else if (left.m_Ids[i] < right.m_Ids[j])
                    end = skipLeft ? LowerBound(left.m_Ids, i + 1, n, right.m_Ids[j]) : i + 1;
                else if (left.m_Ids[i] > right.m_Ids[j])
                { j = skipRight ? LowerBound(right.m_Ids, j + 1, m, left.m_Ids[i]) : j + 1; continue; }
                else { i++; j++; continue; }
                if (result.m_Ids != null)
                {
                    result.EnsureCapacity(write + end - i);
                    if (end - i == 1) result.m_Ids[write] = left.m_Ids[i];
                    else if (!ReferenceEquals(left, result) || i != write)
                        Array.Copy(left.m_Ids, i, result.m_Ids, write, end - i);
                    write += end - i;
                }
                else
                {
                    for (int k = i; k < end; k++)
                    { int id = left.m_Ids[k]; result.m_Words[id >> 6] |= 1UL << (id & 63); write++; }
                }
                i = end;
            }
            result.m_Count = write;
        }
        /// <summary>Hierarchy filter. Output may alias the source, but not a separate condition set.</summary>
        public void FilterInto(RuntimeTagSet conditions, RuntimeTagSet result)
        {
            Require(conditions);
            Require(result);
            if (ReferenceEquals(result, conditions) && !ReferenceEquals(result, this))
                throw new ArgumentException("Hierarchy filter cannot overwrite its separate condition set.", nameof(result));
            if (ReferenceEquals(this, conditions)) { result.CopyFrom(this); return; }
            if (m_Count == 0 || conditions.m_Count == 0) { result.Clear(); return; }
            bool alias = ReferenceEquals(this, result);
            if (alias && m_PackedUsed >= 0) { FilterPackedInPlace(conditions); return; }
            // DFS condition intervals are ordered and may nest. Carry the furthest covered
            // endpoint instead of walking each member's parent chain repeatedly.
            if (conditions.m_PackedUsed < 0 && conditions.m_Ids != null && conditions.m_Count <= m_Count)
            {
                var scan = new IdEnumerator(this);
                if (!alias) result.Clear();
                int condition = 0, end = -1, destination = 0;
                while (scan.MoveNext())
                {
                    int id = scan.Current;
                    while (condition < conditions.m_Count && conditions.m_Ids[condition] <= id)
                    {
                        int next = Registry.Ends[conditions.m_Ids[condition++]];
                        if (next > end) end = next;
                    }
                    bool keep = id < end;
                    if (!alias) { if (keep) result.AppendOrdered(id); }
                    else if (m_Words != null) { if (!keep) RemoveId(id); }
                    else if (keep) m_Ids[destination++] = id;
                }
                if (alias && m_Words == null) m_Count = destination;
                return;
            }
            var iterator = new IdEnumerator(this);
            if (!alias) result.Clear();
            int write = 0;
            while (iterator.MoveNext())
            {
                int id = iterator.Current, ancestor = id;
                while (ancestor >= 0 && !conditions.ContainsId(ancestor)) ancestor = Registry.Parents[ancestor];
                bool keep = ancestor >= 0;
                if (!alias) { if (keep) result.AppendOrdered(id); }
                else if (m_Words != null) { if (!keep) RemoveId(id); }
                else if (keep) m_Ids[write++] = id;
            }
            if (alias && m_Words == null) m_Count = write;
        }
        public bool SetEquals(RuntimeTagSet other)
        {
            Require(other);
            if (m_Count != other.m_Count) return false;
            if (m_Words != null && other.m_Words != null)
            {
                for (int i = 0; i < m_Words.Length; i++) if (m_Words[i] != other.m_Words[i]) return false;
                return true;
            }
            var a = new IdEnumerator(this);
            var b = new IdEnumerator(other);
            while (a.MoveNext()) { b.MoveNext(); if (a.Current != b.Current) return false; }
            return true;
        }
        public Enumerator GetEnumerator() => new Enumerator(this);
        public struct Enumerator
        {
            private readonly TagRegistry m_Registry;
            private IdEnumerator m_Iterator;
            internal Enumerator(RuntimeTagSet set) { m_Registry = set.Registry; m_Iterator = new IdEnumerator(set); }
            public RuntimeTag Current => new RuntimeTag(m_Registry, m_Iterator.Current);
            public bool MoveNext() => m_Iterator.MoveNext();
        }
        private struct IdEnumerator
        {
            private readonly int[] m_Values;
            private readonly ulong[] m_Bitmap;
            private readonly int m_Length;
            private readonly bool m_Reverse;
            private readonly bool m_Packed;
            private int m_Cursor;
            private ulong m_Bits;
            internal int Current { get; private set; }
            internal IdEnumerator(RuntimeTagSet set, bool reverse = false)
            {
                m_Values = set.m_Ids;
                m_Bitmap = set.m_Words;
                m_Packed = set.m_PackedUsed >= 0;
                m_Length = m_Packed ? set.m_PackedUsed : set.m_Count;
                m_Reverse = reverse;
                m_Cursor = reverse ? (m_Bitmap == null ? m_Length : m_Bitmap.Length) : -1;
                m_Bits = 0;
                Current = -1;
            }
            internal bool MoveNext()
            {
                if (m_Packed)
                {
                    while (m_Bits == 0)
                    {
                        m_Cursor += m_Reverse ? -1 : 1;
                        if (unchecked((uint)m_Cursor) >= (uint)m_Length) return false;
                        m_Bits = (uint)(m_Values[m_Cursor] & PackedMask);
                    }
                    int packedBit = m_Reverse ? Bits.Highest(m_Bits) : Bits.Lowest(m_Bits);
                    m_Bits &= ~(1UL << packedBit);
                    Current = RecordBase(m_Values[m_Cursor]) + packedBit;
                    return true;
                }
                if (m_Values != null)
                {
                    m_Cursor += m_Reverse ? -1 : 1;
                    if (unchecked((uint)m_Cursor) >= (uint)m_Length) return false;
                    Current = m_Values[m_Cursor];
                    return true;
                }
                while (m_Bits == 0)
                {
                    m_Cursor += m_Reverse ? -1 : 1;
                    if (unchecked((uint)m_Cursor) >= (uint)m_Bitmap.Length) return false;
                    m_Bits = m_Bitmap[m_Cursor];
                }
                int bit = m_Reverse ? Bits.Highest(m_Bits) : Bits.Lowest(m_Bits);
                m_Bits &= ~(1UL << bit);
                Current = (m_Cursor << 6) + bit;
                return true;
            }
        }
        private static class Bits
        {
            internal static int Count(ulong value) => RuntimeBitOperations.PopCount(value);
            internal static int Lowest(ulong value) => RuntimeBitOperations.TrailingZeroCount(value);
            internal static int Highest(ulong value)
            {
                int shift = 0;
                if (value >= 1UL << 32) { value >>= 32; shift += 32; }
                if (value >= 1UL << 16) { value >>= 16; shift += 16; }
                if (value >= 1UL << 8) { value >>= 8; shift += 8; }
                if (value >= 1UL << 4) { value >>= 4; shift += 4; }
                if (value >= 1UL << 2) { value >>= 2; shift += 2; }
                return shift + (value >= 2 ? 1 : 0);
            }
        }
    }
}
