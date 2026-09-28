using System;

namespace GameplayTags
{
    /// <summary>
    /// Dense-only, single-owner bitmap bound to an immutable registry. The one buffer contains
    /// member words followed by 64-way nonzero-word summaries. Summaries are an index, not a
    /// second member representation. No concurrent mutation or mutation during public enumeration.
    /// Prepare with a positive capacity before allocation-free mutation. Empty sets need no buffer.
    /// </summary>
    public sealed class RuntimeTagSet
    {
        private ulong[] m_Data;
        private readonly int m_WordCount;
        private int m_Count;
        private int m_ActiveWords;
        public TagRegistry Registry { get; }
        public int Count => m_Count;
        public bool IsEmpty => m_Count == 0;
        public int Capacity => m_Data.Length == 0 ? 0 : Registry.Count;
        /// <summary>Member buffer plus all summary levels; excludes the object and shared registry.</summary>
        public long BufferBytes => 8L * m_Data.Length;

        public RuntimeTagSet(TagRegistry registry, int capacity = 0)
        {
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_WordCount = registry.WordCount;
            m_Data = Array.Empty<ulong>();
            EnsureCapacity(capacity);
        }
        // Copy contents, not unused reservation. Empty copies still have independent ownership.
        public RuntimeTagSet(RuntimeTagSet source)
            : this(Required(source).Registry, source.Count) { CopyFrom(source); }
        private static RuntimeTagSet Required(RuntimeTagSet set) => set ?? throw new ArgumentNullException(nameof(set));
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
            if (capacity == 0 || m_WordCount == 0 || m_Data.Length != 0) return;
            int length = m_WordCount, total = length;
            while (length > 1) { length = (length + 63) >> 6; total = checked(total + length); }
            m_Data = new ulong[total];
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
        internal bool ContainsId(int id) => m_Count != 0 && (m_Data[id >> 6] & (1UL << (id & 63))) != 0;
        public bool HasTag(RuntimeTag tag) => Accept(tag) && AnyInRange(tag.Id, Registry.Ends[tag.Id]);
        public bool AddTag(RuntimeTag tag) => Accept(tag) && AddId(tag.Id);
        public bool RemoveTag(RuntimeTag tag) => Accept(tag) && RemoveId(tag.Id);
        internal bool AddId(int id)
        {
            EnsureCapacity(1);
            int word = id >> 6;
            ulong bit = 1UL << (id & 63), before = m_Data[word];
            if ((before & bit) != 0) return false;
            StoreWord(word, before | bit);
            m_Count++;
            return true;
        }
        private bool RemoveId(int id)
        {
            if (m_Count == 0) return false;
            int word = id >> 6;
            ulong bit = 1UL << (id & 63), before = m_Data[word];
            if ((before & bit) == 0) return false;
            StoreWord(word, before & ~bit);
            m_Count--;
            return true;
        }
        // Cardinality belongs to the caller. Index changes propagate only on zero/nonzero transitions.
        private void StoreWord(int word, ulong value)
        {
            ulong before = m_Data[word];
            m_Data[word] = value;
            if ((before == 0) == (value == 0)) return;
            bool occupied = value != 0;
            m_ActiveWords += occupied ? 1 : -1;
            int child = word, offset = m_WordCount, length = m_WordCount;
            while (length > 1)
            {
                int parent = child >> 6;
                ulong mask = 1UL << (child & 63), old = m_Data[offset + parent];
                ulong next = occupied ? old | mask : old & ~mask;
                m_Data[offset + parent] = next;
                if ((old == 0) == (next == 0)) break;
                child = parent;
                length = (length + 63) >> 6;
                offset += length;
            }
        }
        // Find a nonzero word at this level. A summary lookup skips 64 children at once.
        private int NextAtLevel(int offset, int length, int start)
        {
            if (start >= length) return -1;
            if (m_Data[offset + start] != 0) return start;
            if (length <= 1) return -1;
            int summary = offset + length, parentLength = (length + 63) >> 6;
            int parent = start >> 6;
            ulong candidates = m_Data[summary + parent] & (ulong.MaxValue << (start & 63));
            if (candidates != 0) return (parent << 6) + Bits.Lowest(candidates);
            parent = NextAtLevel(summary, parentLength, parent + 1);
            return parent < 0 ? -1 : (parent << 6) + Bits.Lowest(m_Data[summary + parent]);
        }
        private int NextWord(int start) => m_Count == 0 ? -1 : NextAtLevel(0, m_WordCount, start);
        internal bool AnyInRange(int start, int end)
        {
            if (m_Count == 0 || start >= end) return false;
            int first = start >> 6, last = (end - 1) >> 6;
            ulong lowMask = ulong.MaxValue << (start & 63);
            ulong highMask = (end & 63) == 0 ? ulong.MaxValue : (1UL << (end & 63)) - 1;
            if (first == last) return (m_Data[first] & lowMask & highMask) != 0;
            if ((m_Data[first] & lowMask) != 0 || (m_Data[last] & highMask) != 0) return true;
            int next = NextAtLevel(0, m_WordCount, first + 1);
            return next >= 0 && next < last;
        }
        public void Clear()
        {
            if (m_Count == 0) return;
            if (UseLinear(m_ActiveWords)) Array.Clear(m_Data, 0, m_Data.Length);
            else
            {
                int word = NextWord(0);
                while (word >= 0)
                {
                    StoreWord(word, 0);
                    word = NextAtLevel(0, m_WordCount, word + 1);
                }
            }
            m_Count = 0;
            m_ActiveWords = 0;
        }
        // A kernel choice, not a representation change. Both paths operate on the same Dense words.
        private bool UseLinear(int activeWords) => activeWords >= (m_WordCount + 3) / 4;
        public void CopyFrom(RuntimeTagSet other)
        {
            Require(other);
            if (ReferenceEquals(this, other)) return;
            if (other.m_Count == 0) { Clear(); return; }
            EnsureCapacity(1);
            if (UseLinear(other.m_ActiveWords)) Array.Copy(other.m_Data, m_Data, m_Data.Length);
            else
            {
                Clear();
                for (int word = other.NextWord(0); word >= 0; word = other.NextWord(word + 1))
                    StoreWord(word, other.m_Data[word]);
            }
            m_Count = other.m_Count;
            m_ActiveWords = other.m_ActiveWords;
        }
        public void AppendTags(RuntimeTagSet other)
        {
            Require(other);
            if (other.m_Count == 0 || ReferenceEquals(this, other)) return;
            if (m_Count == 0) { CopyFrom(other); return; }
            int added = 0;
            if (UseLinear(other.m_ActiveWords))
            {
                for (int word = 0; word < m_WordCount; word++)
                {
                    ulong old = m_Data[word], incoming = other.m_Data[word];
                    added += Bits.Count(incoming & ~old);
                    m_Data[word] = old | incoming;
                }
                MergeSummary(other);
            }
            else
            {
                for (int word = other.NextWord(0); word >= 0; word = other.NextWord(word + 1))
                {
                    ulong old = m_Data[word], incoming = other.m_Data[word];
                    added += Bits.Count(incoming & ~old);
                    StoreWord(word, old | incoming);
                }
            }
            m_Count += added;
        }
        // Nonzero(a|b) == nonzero(a)|nonzero(b), at every summary level. No data re-scan.
        private void MergeSummary(RuntimeTagSet other)
        {
            if (m_WordCount == 1) { m_ActiveWords = 1; return; }
            int bottomEnd = m_WordCount + ((m_WordCount + 63) >> 6);
            for (int i = m_WordCount; i < bottomEnd; i++)
            {
                m_ActiveWords += Bits.Count(other.m_Data[i] & ~m_Data[i]);
                m_Data[i] |= other.m_Data[i];
            }
            for (int i = bottomEnd; i < m_Data.Length; i++) m_Data[i] |= other.m_Data[i];
        }
        public bool RemoveTags(RuntimeTagSet other)
        {
            Require(other);
            if (m_Count == 0 || other.m_Count == 0) return false;
            if (ReferenceEquals(this, other)) { Clear(); return true; }
            RuntimeTagSet scan = m_ActiveWords <= other.m_ActiveWords ? this : other;
            int removed = 0;
            for (int word = scan.NextWord(0); word >= 0; word = scan.NextWord(word + 1))
            {
                ulong old = m_Data[word], mask = other.m_Data[word];
                removed += Bits.Count(old & mask);
                StoreWord(word, old & ~mask);
            }
            m_Count -= removed;
            return removed != 0;
        }
        public bool HasAnyExact(RuntimeTagSet other)
        {
            Require(other);
            if (m_Count == 0 || other.m_Count == 0) return false;
            RuntimeTagSet scan = m_ActiveWords <= other.m_ActiveWords ? this : other;
            for (int word = scan.NextWord(0); word >= 0; word = scan.NextWord(word + 1))
                if ((m_Data[word] & other.m_Data[word]) != 0) return true;
            return false;
        }
        public bool HasAllExact(RuntimeTagSet other)
        {
            Require(other);
            if (other.m_Count > m_Count) return false;
            if (other.m_Count == 0 || ReferenceEquals(this, other)) return true;
            for (int word = other.NextWord(0); word >= 0; word = other.NextWord(word + 1))
                if ((m_Data[word] & other.m_Data[word]) != other.m_Data[word]) return false;
            return true;
        }
        public bool HasAny(RuntimeTagSet conditions)
        {
            Require(conditions);
            var iterator = conditions.GetEnumerator();
            while (iterator.MoveNext())
            {
                int id = iterator.Current.Id;
                if (AnyInRange(id, Registry.Ends[id])) return true;
            }
            return false;
        }
        public bool HasAll(RuntimeTagSet conditions)
        {
            Require(conditions);
            var iterator = conditions.GetEnumerator();
            while (iterator.MoveNext())
            {
                int id = iterator.Current.Id;
                if (!AnyInRange(id, Registry.Ends[id])) return false;
            }
            return true;
        }
        public bool MatchesQuery(FrozenGameplayTagQuery query) => query != null && query.Matches(this);
        public static RuntimeTagSet Union(RuntimeTagSet left, RuntimeTagSet right)
        {
            Required(left).Require(right);
            var result = new RuntimeTagSet(left.Registry, left.m_Count == 0 ? right.m_Count : left.m_Count);
            UnionInto(left, right, result);
            return result;
        }
        public static void UnionInto(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result)
        {
            Required(left).Require(right);
            left.Require(result);
            if (ReferenceEquals(result, left)) { result.AppendTags(right); return; }
            if (ReferenceEquals(result, right)) { result.AppendTags(left); return; }
            if (left.m_Count == 0) { result.CopyFrom(right); return; }
            if (right.m_Count == 0) { result.CopyFrom(left); return; }
            if (!result.UseLinear(Math.Max(left.m_ActiveWords, right.m_ActiveWords)))
            {
                result.CopyFrom(left);
                result.AppendTags(right);
                return;
            }
            result.EnsureCapacity(1);
            int count = 0;
            for (int i = 0; i < result.m_WordCount; i++)
            {
                ulong word = left.m_Data[i] | right.m_Data[i];
                result.m_Data[i] = word;
                count += Bits.Count(word);
            }
            int active = result.m_WordCount == 1 ? 1 : 0;
            int bottomEnd = result.m_WordCount + ((result.m_WordCount + 63) >> 6);
            for (int i = result.m_WordCount; i < result.m_Data.Length; i++)
            {
                ulong summary = left.m_Data[i] | right.m_Data[i];
                result.m_Data[i] = summary;
                if (i < bottomEnd) active += Bits.Count(summary);
            }
            result.m_ActiveWords = active;
            result.m_Count = count;
        }
        public static RuntimeTagSet IntersectionExact(RuntimeTagSet left, RuntimeTagSet right)
        {
            Required(left).Require(right);
            var result = new RuntimeTagSet(left.Registry);
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
            result.Clear();
            if (left.m_Count == 0 || right.m_Count == 0) return;
            RuntimeTagSet scan = left.m_ActiveWords <= right.m_ActiveWords ? left : right;
            int count = 0;
            for (int word = scan.NextWord(0); word >= 0; word = scan.NextWord(word + 1))
            {
                ulong bits = left.m_Data[word] & right.m_Data[word];
                if (bits == 0) continue;
                result.EnsureCapacity(1);
                result.StoreWord(word, bits);
                count += Bits.Count(bits);
            }
            result.m_Count = count;
        }
        private void IntersectWith(RuntimeTagSet other)
        {
            if (ReferenceEquals(this, other)) return;
            if (other.m_Count == 0) { Clear(); return; }
            int count = 0;
            for (int word = NextWord(0); word >= 0; word = NextAtLevel(0, m_WordCount, word + 1))
            {
                ulong value = m_Data[word] & other.m_Data[word];
                StoreWord(word, value);
                count += Bits.Count(value);
            }
            m_Count = count;
        }
        /// <summary>Overwrite result. May alias this source, but not a separate condition set.</summary>
        public void FilterInto(RuntimeTagSet conditions, RuntimeTagSet result)
        {
            Require(conditions);
            Require(result);
            if (ReferenceEquals(result, conditions) && !ReferenceEquals(this, result))
                throw new ArgumentException("Hierarchy filter cannot overwrite its separate condition set.", nameof(result));
            if (ReferenceEquals(this, conditions)) { result.CopyFrom(this); return; }
            bool alias = ReferenceEquals(this, result);
            if (!alias) result.Clear();
            int count = 0;
            for (int word = NextWord(0); word >= 0; word = NextAtLevel(0, m_WordCount, word + 1))
            {
                ulong candidates = m_Data[word], kept = 0;
                while (candidates != 0)
                {
                    int bit = Bits.Lowest(candidates), id = (word << 6) + bit, ancestor = id;
                    candidates &= candidates - 1;
                    while (ancestor >= 0 && !conditions.ContainsId(ancestor)) ancestor = Registry.Parents[ancestor];
                    if (ancestor >= 0) kept |= 1UL << bit;
                }
                if (kept != 0) result.EnsureCapacity(1);
                if (alias || kept != 0) result.StoreWord(word, kept);
                count += Bits.Count(kept);
            }
            result.m_Count = count;
        }
        public bool SetEquals(RuntimeTagSet other)
        {
            Require(other);
            if (m_Count != other.m_Count) return false;
            if (m_Count == 0 || ReferenceEquals(this, other)) return true;
            for (int word = NextWord(0); word >= 0; word = NextWord(word + 1))
                if (m_Data[word] != other.m_Data[word]) return false;
            return true;
        }
        public Enumerator GetEnumerator() => new Enumerator(this);
        public struct Enumerator
        {
            private readonly RuntimeTagSet m_Set;
            private int m_Word;
            private ulong m_Bits;
            private int m_Current;
            internal Enumerator(RuntimeTagSet set) { m_Set = set; m_Word = -1; m_Bits = 0; m_Current = -1; }
            public RuntimeTag Current => new RuntimeTag(m_Set.Registry, m_Current);
            public bool MoveNext()
            {
                if (m_Bits == 0)
                {
                    m_Word = m_Set.NextWord(m_Word + 1);
                    if (m_Word < 0) { m_Word = m_Set.m_WordCount; return false; }
                    m_Bits = m_Set.m_Data[m_Word];
                }
                m_Current = (m_Word << 6) + Bits.Lowest(m_Bits);
                m_Bits &= m_Bits - 1;
                return true;
            }
        }
        private static class Bits
        {
            // Portable SWAR and De Bruijn scan: identical code ships in netstandard2.1 and the audit.
            // References and all-64-positions regression are recorded in Documentation~/DENSE_ONLY.md.
            private static readonly byte[] Positions = {
                0,1,48,2,57,49,28,3,61,58,50,42,38,29,17,4,
                62,55,59,36,53,51,43,22,45,39,33,30,24,18,12,5,
                63,47,56,27,60,41,37,16,54,35,52,21,44,32,23,11,
                46,26,40,15,34,20,31,10,25,14,19,9,13,8,7,6 };
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
            internal static int Lowest(ulong value) => Positions[unchecked(((value & (0UL - value)) * 0x03F79D71B4CB0A89UL) >> 58)];
        }
    }
}
