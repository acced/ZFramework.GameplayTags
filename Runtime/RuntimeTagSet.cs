using System;
using System.Runtime.CompilerServices;

namespace GameplayTags
{
    /// <summary>
    /// Dense-only, single-owner bitmap bound to an immutable registry. One buffer contains member
    /// words followed by 64-way nonzero-word summaries. No second member representation, concurrent
    /// mutation or mutation during public enumeration. Prepare before allocation-free mutation.
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
        /// <summary>Member buffer plus summary levels; excludes the object and shared registry.</summary>
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
        // The checked public hot path is intentionally small; the foreign-scope error stays cold.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasTagExact(RuntimeTag tag)
        {
            if (!ReferenceEquals(Registry, tag.Owner)) return ForeignTag(tag);
            return ContainsId(tag.Id);
        }
        private bool ForeignTag(RuntimeTag tag)
        {
            if (tag.Owner == null) return false;
            Registry.RequireSame(tag.Owner);
            return false;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool ContainsId(int id)
        {
            int word = id >> 6;
            ulong[] data = m_Data;
            return (uint)word < (uint)data.Length && (data[word] & (1UL << (id & 63))) != 0;
        }
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
        // Single-word updates propagate only zero/nonzero transitions.
        private void StoreWord(int word, ulong value)
        {
            ulong before = m_Data[word];
            m_Data[word] = value;
            if ((before == 0) != (value == 0)) ChangeOccupancy(word, value != 0);
        }
        private void ChangeOccupancy(int child, bool occupied)
        {
            m_ActiveWords += occupied ? 1 : -1;
            Propagate(child, m_WordCount, m_WordCount, occupied);
        }
        private void Propagate(int child, int offset, int length, bool occupied)
        {
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
        private ulong BlockMask(int block) => m_WordCount == 1
            ? (m_Data[0] == 0 ? 0UL : 1UL) : m_Data[m_WordCount + block];
        // Bulk callers have already written this block's member words. Publish its directory once,
        // not once per member word. The caller maintains eager member and occupied-word counts.
        private void PublishBlock(int block, ulong mask)
        {
            if (m_WordCount == 1) return;
            int at = m_WordCount + block;
            ulong old = m_Data[at];
            m_Data[at] = mask;
            if ((old == 0) != (mask == 0))
            {
                int length = (m_WordCount + 63) >> 6;
                Propagate(block, m_WordCount + length, length, mask != 0);
            }
        }
        // A summary lookup skips 64 children. The final one-word level terminates recursion.
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
        // This cursor carries a scalar mask, never another member collection. Internal forward
        // transforms may rewrite the current block after capturing Mask, but never future blocks.
        private struct BlockCursor
        {
            private readonly RuntimeTagSet m_Set;
            private int m_Next;
            internal int Block;
            internal ulong Mask;
            internal BlockCursor(RuntimeTagSet set) { m_Set = set; m_Next = 0; Block = -1; Mask = 0; }
            internal bool MoveNext()
            {
                if (m_Next < 0 || m_Set.m_Data.Length == 0) return false;
                if (m_Set.m_WordCount == 1)
                {
                    m_Next = -1; Block = 0; Mask = m_Set.m_Data[0] == 0 ? 0UL : 1UL;
                    return Mask != 0;
                }
                int length = (m_Set.m_WordCount + 63) >> 6;
                Block = m_Set.NextAtLevel(m_Set.m_WordCount, length, m_Next);
                if (Block < 0) { m_Next = -1; return false; }
                Mask = m_Set.m_Data[m_Set.m_WordCount + Block];
                m_Next = Block + 1;
                return true;
            }
        }
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
                var blocks = new BlockCursor(this);
                while (blocks.MoveNext())
                {
                    ulong mask = blocks.Mask;
                    while (mask != 0)
                    {
                        m_Data[(blocks.Block << 6) + Bits.Lowest(mask)] = 0;
                        mask &= mask - 1;
                    }
                    PublishBlock(blocks.Block, 0);
                }
            }
            m_Count = 0;
            m_ActiveWords = 0;
        }
        // A kernel choice, never a member-storage mode or allocation change.
        private bool UseLinear(int activeWords) => activeWords >= (m_WordCount + 7) / 8;
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
                var blocks = new BlockCursor(other);
                while (blocks.MoveNext())
                {
                    ulong mask = blocks.Mask;
                    while (mask != 0)
                    {
                        int word = (blocks.Block << 6) + Bits.Lowest(mask);
                        m_Data[word] = other.m_Data[word];
                        mask &= mask - 1;
                    }
                    PublishBlock(blocks.Block, blocks.Mask);
                }
            }
            m_Count = other.m_Count;
            m_ActiveWords = other.m_ActiveWords;
        }
        public void AppendTags(RuntimeTagSet other)
        {
            Require(other);
            if (other.m_Count == 0 || ReferenceEquals(this, other)) return;
            if (m_Count == 0) { CopyFrom(other); return; }
            if (UseLinear(other.m_ActiveWords))
            {
                ulong[] data = m_Data, incoming = other.m_Data;
                for (int i = 0; i < m_WordCount; i++) data[i] |= incoming[i];
                m_Count = Bits.CountWords(data, m_WordCount);
                MergeSummary(other);
                return;
            }
            int added = 0;
            var blocks = new BlockCursor(other);
            while (blocks.MoveNext())
            {
                int block = blocks.Block;
                ulong mask = blocks.Mask, oldMask = BlockMask(block);
                m_ActiveWords += Bits.Count(mask & ~oldMask);
                while (mask != 0)
                {
                    int word = (block << 6) + Bits.Lowest(mask);
                    ulong old = m_Data[word], incoming = other.m_Data[word];
                    added += Bits.Count(incoming & ~old);
                    m_Data[word] = old | incoming;
                    mask &= mask - 1;
                }
                PublishBlock(block, oldMask | blocks.Mask);
            }
            m_Count += added;
        }
        // Occupied(a|b) == occupied(a)|occupied(b), including every summary level.
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
            if (UseLinear(scan.m_ActiveWords))
            {
                ulong[] data = m_Data, mask = other.m_Data;
                for (int i = 0; i < m_WordCount; i++)
                {
                    ulong old = data[i], value = old & ~mask[i];
                    data[i] = value;
                    if (value == 0 && old != 0) ChangeOccupancy(i, false);
                }
                int count = Bits.CountWords(data, m_WordCount);
                removed = m_Count - count;
                m_Count = count;
                return removed != 0;
            }
            var blocks = new BlockCursor(scan);
            while (blocks.MoveNext())
            {
                int block = blocks.Block;
                ulong oldMask = BlockMask(block), mask = oldMask & other.BlockMask(block), emptied = 0;
                while (mask != 0)
                {
                    int bit = Bits.Lowest(mask), word = (block << 6) + bit;
                    ulong old = m_Data[word], incoming = other.m_Data[word], value = old & ~incoming;
                    removed += Bits.Count(old & incoming);
                    m_Data[word] = value;
                    if (value == 0) emptied |= 1UL << bit;
                    mask &= mask - 1;
                }
                if (emptied != 0)
                {
                    m_ActiveWords -= Bits.Count(emptied);
                    PublishBlock(block, oldMask & ~emptied);
                }
            }
            m_Count -= removed;
            return removed != 0;
        }
        public bool HasAnyExact(RuntimeTagSet other)
        {
            Require(other);
            if (m_Count == 0 || other.m_Count == 0) return false;
            var blocks = new BlockCursor(m_ActiveWords <= other.m_ActiveWords ? this : other);
            while (blocks.MoveNext())
            {
                ulong mask = BlockMask(blocks.Block) & other.BlockMask(blocks.Block);
                while (mask != 0)
                {
                    int word = (blocks.Block << 6) + Bits.Lowest(mask);
                    if ((m_Data[word] & other.m_Data[word]) != 0) return true;
                    mask &= mask - 1;
                }
            }
            return false;
        }
        public bool HasAllExact(RuntimeTagSet other)
        {
            Require(other);
            if (other.m_Count > m_Count) return false;
            if (other.m_Count == 0 || ReferenceEquals(this, other)) return true;
            var blocks = new BlockCursor(other);
            while (blocks.MoveNext())
            {
                ulong mask = blocks.Mask;
                if ((BlockMask(blocks.Block) & mask) != mask) return false;
                while (mask != 0)
                {
                    int word = (blocks.Block << 6) + Bits.Lowest(mask);
                    if ((m_Data[word] & other.m_Data[word]) != other.m_Data[word]) return false;
                    mask &= mask - 1;
                }
            }
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
            result.EnsureCapacity(1);
            ulong[] a = left.m_Data, b = right.m_Data, output = result.m_Data;
            int active = 0, count = 0;
            if (result.UseLinear(Math.Max(left.m_ActiveWords, right.m_ActiveWords)))
            {
                for (int i = 0; i < result.m_WordCount; i++) output[i] = a[i] | b[i];
                count = Bits.CountWords(output, result.m_WordCount);
                int bottomEnd = result.m_WordCount + ((result.m_WordCount + 63) >> 6);
                active = result.m_WordCount == 1 ? 1 : 0;
                for (int i = result.m_WordCount; i < output.Length; i++)
                {
                    ulong mask = a[i] | b[i];
                    output[i] = mask;
                    if (i < bottomEnd) active += Bits.Count(mask);
                }
            }
            else
            {
                result.Clear();
                var x = new BlockCursor(left);
                var y = new BlockCursor(right);
                bool hasX = x.MoveNext(), hasY = y.MoveNext();
                while (hasX || hasY)
                {
                    int block = !hasY || (hasX && x.Block < y.Block) ? x.Block : y.Block;
                    ulong mask = (hasX && x.Block == block ? x.Mask : 0) | (hasY && y.Block == block ? y.Mask : 0);
                    active += Bits.Count(mask);
                    ulong remaining = mask;
                    while (remaining != 0)
                    {
                        int word = (block << 6) + Bits.Lowest(remaining);
                        ulong bits = a[word] | b[word];
                        output[word] = bits;
                        count += Bits.Count(bits);
                        remaining &= remaining - 1;
                    }
                    result.PublishBlock(block, mask);
                    if (hasX && x.Block == block) hasX = x.MoveNext();
                    if (hasY && y.Block == block) hasY = y.MoveNext();
                }
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
            var blocks = new BlockCursor(left.m_ActiveWords <= right.m_ActiveWords ? left : right);
            int count = 0, active = 0;
            while (blocks.MoveNext())
            {
                int block = blocks.Block;
                ulong mask = left.BlockMask(block) & right.BlockMask(block), kept = 0;
                while (mask != 0)
                {
                    int bit = Bits.Lowest(mask), word = (block << 6) + bit;
                    ulong value = left.m_Data[word] & right.m_Data[word];
                    if (value != 0)
                    {
                        result.EnsureCapacity(1);
                        result.m_Data[word] = value;
                        count += Bits.Count(value);
                        active++;
                        kept |= 1UL << bit;
                    }
                    mask &= mask - 1;
                }
                if (kept != 0) result.PublishBlock(block, kept);
            }
            result.m_Count = count;
            result.m_ActiveWords = active;
        }
        private void IntersectWith(RuntimeTagSet other)
        {
            if (ReferenceEquals(this, other)) return;
            if (other.m_Count == 0) { Clear(); return; }
            int count = 0, active = 0;
            var blocks = new BlockCursor(this);
            while (blocks.MoveNext())
            {
                ulong mask = blocks.Mask, kept = 0;
                while (mask != 0)
                {
                    int bit = Bits.Lowest(mask), word = (blocks.Block << 6) + bit;
                    ulong value = m_Data[word] & other.m_Data[word];
                    m_Data[word] = value;
                    count += Bits.Count(value);
                    if (value != 0) { active++; kept |= 1UL << bit; }
                    mask &= mask - 1;
                }
                PublishBlock(blocks.Block, kept);
            }
            m_Count = count;
            m_ActiveWords = active;
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
            var blocks = new BlockCursor(this);
            while (blocks.MoveNext())
            {
                ulong mask = blocks.Mask;
                while (mask != 0)
                {
                    int word = (blocks.Block << 6) + Bits.Lowest(mask);
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
                    mask &= mask - 1;
                }
            }
            result.m_Count = count;
        }
        public bool SetEquals(RuntimeTagSet other)
        {
            Require(other);
            if (m_Count != other.m_Count) return false;
            if (m_Count == 0 || ReferenceEquals(this, other)) return true;
            var blocks = new BlockCursor(this);
            while (blocks.MoveNext())
            {
                ulong mask = blocks.Mask;
                if (mask != other.BlockMask(blocks.Block)) return false;
                while (mask != 0)
                {
                    int word = (blocks.Block << 6) + Bits.Lowest(mask);
                    if (m_Data[word] != other.m_Data[word]) return false;
                    mask &= mask - 1;
                }
            }
            return true;
        }
        public Enumerator GetEnumerator() => new Enumerator(this);
        public struct Enumerator
        {
            private readonly RuntimeTagSet m_Set;
            private BlockCursor m_Blocks;
            private ulong m_Words;
            private ulong m_Bits;
            private int m_Base;
            private int m_Current;
            internal Enumerator(RuntimeTagSet set)
            { m_Set = set; m_Blocks = new BlockCursor(set); m_Words = 0; m_Bits = 0; m_Base = 0; m_Current = -1; }
            public RuntimeTag Current => new RuntimeTag(m_Set.Registry, m_Current);
            public bool MoveNext()
            {
                if (m_Bits == 0)
                {
                    if (m_Words == 0)
                    {
                        if (!m_Blocks.MoveNext()) return false;
                        m_Words = m_Blocks.Mask;
                    }
                    int word = (m_Blocks.Block << 6) + Bits.Lowest(m_Words);
                    m_Words &= m_Words - 1;
                    m_Base = word << 6;
                    m_Bits = m_Set.m_Data[word];
                }
                m_Current = m_Base + Bits.Lowest(m_Bits);
                m_Bits &= m_Bits - 1;
                return true;
            }
        }
        private static class Bits
        {
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

            // Portable Harley-Seal carry-save reduction. Adapted from Wojciech Mula's
            // sse-popcount/popcnt-harley-seal.cpp. See THIRD_PARTY_NOTICES.md (BSD-2-Clause).
            // Only member words are counted. The compact occupancy directory is excluded.
            internal static int CountWords(ulong[] words, int length)
            {
                int count = 0, i = 0;
                if (length >= 64)
                {
                    ulong ones = 0, twos = 0, fours = 0, eights = 0;
                    for (; i + 15 < length; i += 16)
                    {
                        Carry(out ulong t0, ref ones, ones, words[i], words[i + 1]);
                        Carry(out ulong t1, ref ones, ones, words[i + 2], words[i + 3]);
                        Carry(out ulong f0, ref twos, twos, t0, t1);
                        Carry(out t0, ref ones, ones, words[i + 4], words[i + 5]);
                        Carry(out t1, ref ones, ones, words[i + 6], words[i + 7]);
                        Carry(out ulong f1, ref twos, twos, t0, t1);
                        Carry(out ulong e0, ref fours, fours, f0, f1);
                        Carry(out t0, ref ones, ones, words[i + 8], words[i + 9]);
                        Carry(out t1, ref ones, ones, words[i + 10], words[i + 11]);
                        Carry(out f0, ref twos, twos, t0, t1);
                        Carry(out t0, ref ones, ones, words[i + 12], words[i + 13]);
                        Carry(out t1, ref ones, ones, words[i + 14], words[i + 15]);
                        Carry(out f1, ref twos, twos, t0, t1);
                        Carry(out ulong e1, ref fours, fours, f0, f1);
                        Carry(out ulong sixteens, ref eights, eights, e0, e1);
                        count += Count(sixteens);
                    }
                    count = count * 16 + Count(eights) * 8 + Count(fours) * 4 + Count(twos) * 2 + Count(ones);
                }
                for (; i < length; i++) count += Count(words[i]);
                return count;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void Carry(out ulong high, ref ulong low, ulong a, ulong b, ulong c)
            {
                ulong different = a ^ b;
                high = (a & b) | (different & c);
                low = different ^ c;
            }
        }
    }
}
