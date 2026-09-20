using System;

namespace GameplayTags
{
    public enum TagSetStorage { Auto, Sparse, Dense }

    /// <summary>
    /// Single-owner runtime set. Exactly one member buffer is live: sorted int[] or ulong[].
    /// Storage is chosen at construction and never changes implicitly. No concurrent mutation or mutation during enumeration.
    /// </summary>
    public sealed class RuntimeTagSet
    {
        private int[] m_Ids;
        private readonly ulong[] m_Words;
        private int m_Count;
        public TagRegistry Registry { get; }
        public int Count => m_Count;
        public bool IsEmpty => m_Count == 0;
        public TagSetStorage Storage => m_Words == null ? TagSetStorage.Sparse : TagSetStorage.Dense;
        public int Capacity => m_Words == null ? m_Ids.Length : Registry.Count;
        public long BufferBytes => m_Words == null ? 4L * m_Ids.Length : 8L * m_Words.Length;

        public RuntimeTagSet(TagRegistry registry, int capacity = 0, TagSetStorage storage = TagSetStorage.Auto)
        {
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (storage < TagSetStorage.Auto || storage > TagSetStorage.Dense) throw new ArgumentOutOfRangeException(nameof(storage));
            capacity = Math.Min(capacity, registry.Count);
            bool dense = storage == TagSetStorage.Dense || (storage == TagSetStorage.Auto && registry.Count != 0 && 8L * registry.WordCount <= 4L * capacity);
            if (dense) m_Words = registry.WordCount == 0 ? Array.Empty<ulong>() : new ulong[registry.WordCount];
            else m_Ids = capacity == 0 ? Array.Empty<int>() : new int[capacity];
        }
        public RuntimeTagSet(RuntimeTagSet source)
            : this(Required(source).Registry, source.Capacity, source.Storage) { CopyFrom(source); }
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
            if (m_Words != null || capacity <= m_Ids.Length) return;
            capacity = Math.Min(capacity, Registry.Count);
            int grown = (int)Math.Min(Registry.Count, Math.Max(4L, 2L * m_Ids.Length));
            Array.Resize(ref m_Ids, Math.Max(capacity, grown));
        }
        public bool HasTagExact(RuntimeTag tag)
        {
            if (!ReferenceEquals(Registry, tag.Owner))
            {
                if (tag.Owner == null) return false;
                Registry.RequireSame(tag.Owner);
            }
            return ContainsId(tag.Id);
        }
        internal bool ContainsId(int id) => m_Words != null
            ? (m_Words[id >> 6] & (1UL << (id & 63))) != 0
            : IndexOf(id) >= 0;
        public bool HasTag(RuntimeTag tag) => Accept(tag) && AnyInRange(tag.Id, Registry.Ends[tag.Id]);
        public bool AddTag(RuntimeTag tag) => Accept(tag) && AddId(tag.Id);
        public bool RemoveTag(RuntimeTag tag) => Accept(tag) && RemoveId(tag.Id);
        internal bool AddId(int id)
        {
            if (m_Words != null)
            {
                int word = id >> 6;
                ulong bit = 1UL << (id & 63), before = m_Words[word];
                if ((before & bit) != 0) return false;
                m_Words[word] = before | bit;
                m_Count++;
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
        private bool RemoveId(int id)
        {
            if (m_Words != null)
            {
                int word = id >> 6;
                ulong bit = 1UL << (id & 63), before = m_Words[word];
                if ((before & bit) == 0) return false;
                m_Words[word] = before & ~bit;
                m_Count--;
                return true;
            }
            int index = IndexOf(id);
            if (index < 0) return false;
            m_Count--;
            Array.Copy(m_Ids, index + 1, m_Ids, index, m_Count - index);
            return true;
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
            if (m_Words != null) Array.Clear(m_Words, 0, m_Words.Length);
            m_Count = 0;
        }
        public void CopyFrom(RuntimeTagSet other)
        {
            Require(other);
            if (ReferenceEquals(this, other)) return;
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
            if (m_Words != null)
            {
                if (other.m_Words != null)
                {
                    int added = 0;
                    for (int i = 0; i < m_Words.Length; i++)
                    {
                        ulong before = m_Words[i], incoming = other.m_Words[i];
                        added += Bits.Count(incoming & ~before);
                        m_Words[i] = before | incoming;
                    }
                    m_Count += added;
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
        public bool RemoveTags(RuntimeTagSet other)
        {
            Require(other);
            if (m_Count == 0 || other.m_Count == 0) return false;
            if (ReferenceEquals(this, other)) { Clear(); return true; }
            int beforeCount = m_Count;
            if (m_Words != null)
            {
                if (other.m_Words != null)
                {
                    int removed = 0;
                    for (int i = 0; i < m_Words.Length; i++)
                    {
                        ulong old = m_Words[i], incoming = other.m_Words[i];
                        removed += Bits.Count(old & incoming);
                        m_Words[i] = old & ~incoming;
                    }
                    m_Count -= removed;
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
            if (m_Words != null && other.m_Words != null)
            {
                for (int i = 0; i < m_Words.Length; i++) if ((m_Words[i] & other.m_Words[i]) != 0) return true;
                return false;
            }
            RuntimeTagSet small = m_Count <= other.m_Count ? this : other;
            RuntimeTagSet large = ReferenceEquals(small, this) ? other : this;
            var iterator = new IdEnumerator(small);
            while (iterator.MoveNext()) if (large.ContainsId(iterator.Current)) return true;
            return false;
        }
        public bool HasAllExact(RuntimeTagSet other)
        {
            Require(other);
            if (other.m_Count > m_Count) return false;
            if (m_Words != null && other.m_Words != null)
            {
                for (int i = 0; i < m_Words.Length; i++) if ((m_Words[i] & other.m_Words[i]) != other.m_Words[i]) return false;
                return true;
            }
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
            var result = new RuntimeTagSet(left.Registry, maximum, storage);
            UnionInto(left, right, result);
            return result;
        }
        public static void UnionInto(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result)
        {
            Required(left).Require(right);
            left.Require(result);
            if (ReferenceEquals(result, left)) { result.AppendTags(right); return; }
            if (ReferenceEquals(result, right)) { result.AppendTags(left); return; }
            if (result.m_Words != null)
            {
                if (left.m_Words != null && right.m_Words != null)
                {
                    int count = 0;
                    for (int i = 0; i < result.m_Words.Length; i++)
                    {
                        ulong word = left.m_Words[i] | right.m_Words[i];
                        result.m_Words[i] = word;
                        count += Bits.Count(word);
                    }
                    result.m_Count = count;
                }
                else if (left.m_Words != null) { result.CopyFrom(left); result.AppendTags(right); }
                else if (right.m_Words != null) { result.CopyFrom(right); result.AppendTags(left); }
                else { result.CopyFrom(left); result.AppendTags(right); }
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
            var result = new RuntimeTagSet(left.Registry, Math.Min(left.Count, right.Count), storage);
            IntersectionExactInto(left, right, result);
            return result;
        }
        public void FilterExactInto(RuntimeTagSet conditions, RuntimeTagSet result) => IntersectionExactInto(this, conditions, result);
        public static void IntersectionExactInto(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result)
        {
            Required(left).Require(right);
            left.Require(result);
            if (ReferenceEquals(result, left)) { result.IntersectWith(right); return; }
            if (ReferenceEquals(result, right)) { result.IntersectWith(left); return; }
            if (result.m_Words != null && left.m_Words != null && right.m_Words != null)
            {
                int count = 0;
                for (int i = 0; i < result.m_Words.Length; i++)
                {
                    ulong word = left.m_Words[i] & right.m_Words[i];
                    result.m_Words[i] = word;
                    count += Bits.Count(word);
                }
                result.m_Count = count;
                return;
            }
            result.Clear();
            RuntimeTagSet small = left.Count <= right.Count ? left : right;
            RuntimeTagSet large = ReferenceEquals(small, left) ? right : left;
            var iterator = new IdEnumerator(small);
            while (iterator.MoveNext()) if (large.ContainsId(iterator.Current)) result.AppendOrdered(iterator.Current);
        }
        private void IntersectWith(RuntimeTagSet other)
        {
            if (ReferenceEquals(this, other)) return;
            if (other.m_Count == 0) { Clear(); return; }
            if (m_Words == null)
            {
                int write = 0;
                for (int i = 0; i < m_Count; i++) if (other.ContainsId(m_Ids[i])) m_Ids[write++] = m_Ids[i];
                m_Count = write;
            }
            else
            {
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
        /// <summary>Hierarchy filter. Output may alias the source, but not a separate condition set.</summary>
        public void FilterInto(RuntimeTagSet conditions, RuntimeTagSet result)
        {
            Require(conditions);
            Require(result);
            if (ReferenceEquals(result, conditions) && !ReferenceEquals(result, this))
                throw new ArgumentException("Hierarchy filter cannot overwrite its separate condition set.", nameof(result));
            if (ReferenceEquals(this, conditions)) { result.CopyFrom(this); return; }
            bool alias = ReferenceEquals(this, result);
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
            private int m_Cursor;
            private ulong m_Bits;
            internal int Current { get; private set; }
            internal IdEnumerator(RuntimeTagSet set, bool reverse = false)
            {
                m_Values = set.m_Ids;
                m_Bitmap = set.m_Words;
                m_Length = set.m_Count;
                m_Reverse = reverse;
                m_Cursor = reverse ? (m_Bitmap == null ? m_Length : m_Bitmap.Length) : -1;
                m_Bits = 0;
                Current = -1;
            }
            internal bool MoveNext()
            {
                if (m_Values != null)
                {
                    m_Cursor += m_Reverse ? -1 : 1;
                    if ((uint)m_Cursor >= (uint)m_Length) return false;
                    Current = m_Values[m_Cursor];
                    return true;
                }
                while (m_Bits == 0)
                {
                    m_Cursor += m_Reverse ? -1 : 1;
                    if ((uint)m_Cursor >= (uint)m_Bitmap.Length) return false;
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
            // Portable SWAR: the same code is benchmarked and shipped on .NET Standard 2.1 / IL2CPP.
            internal static int Count(ulong value)
            {
                unchecked
                {
                    value -= (value >> 1) & 0x5555555555555555UL;
                    value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
                    value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
                    return (int)((value * 0x0101010101010101UL) >> 56);
                }
            }
            internal static int Lowest(ulong value)
            {
                int shift = 0;
                if ((value & 0xFFFFFFFFUL) == 0) { value >>= 32; shift += 32; }
                if ((value & 0xFFFFUL) == 0) { value >>= 16; shift += 16; }
                if ((value & 0xFFUL) == 0) { value >>= 8; shift += 8; }
                if ((value & 0xFUL) == 0) { value >>= 4; shift += 4; }
                if ((value & 3UL) == 0) { value >>= 2; shift += 2; }
                return shift + ((value & 1UL) == 0 ? 1 : 0);
            }
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
