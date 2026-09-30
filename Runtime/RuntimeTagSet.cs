using System;

namespace GameplayTags
{
    /// <summary>
    /// Mutable bitmap-only set. Each directory entry covers 4096 IDs; its mask ranks
    /// the nonzero 64-bit member words in a packed buffer. Empty blocks consume no
    /// member storage. One directory entry and one word fit inline. No member-ID list,
    /// shared mutable storage, pool, deferred Count or implicit Sparse compatibility.
    /// Single owner: mutation during public enumeration is not supported.
    /// </summary>
    public sealed class RuntimeTagSet
    {
        private struct Group
        {
            internal int Key;
            internal int Offset;
            internal ulong Mask;
            internal Group(int key, int offset, ulong mask)
            { Key = key; Offset = offset; Mask = mask; }
        }

        private Group[] m_Groups;
        private ulong[] m_Words;
        private Group m_InlineGroup;
        private ulong m_InlineWord;
        private int m_GroupCount, m_WordCount, m_Count;
        public TagRegistry Registry { get; }
        public int Count => m_Count;
        public bool IsEmpty => m_Count == 0;
        // EnsureCapacity explicitly reserves for ANY placement of this many members.
        public int Capacity => Math.Max(m_Count, ReservedMemberCapacity);
        public int ReservedMemberCapacity
        {
            get
            {
                int groups = GroupCapacity >= MaxGroups ? Registry.Count : GroupCapacity;
                int words = WordCapacity >= Registry.WordCount ? Registry.Count : WordCapacity;
                return Math.Min(groups, words);
            }
        }
        // Includes inline bitmap/directory payload, not object/array headers or registry.
        public long BufferBytes => 24L + (m_Groups == null ? 0 : 16L * m_Groups.Length)
            + (m_Words == null ? 0 : 8L * m_Words.Length);
        private int MaxGroups => (int)(((long)Registry.Count + 4095) >> 12);
        private int GroupCapacity => m_Groups == null ? Math.Min(1, MaxGroups) : m_Groups.Length;
        private int WordCapacity => m_Words == null ? Math.Min(1, Registry.WordCount) : m_Words.Length;
        private Group ReadGroup(int index) => m_Groups == null ? m_InlineGroup : m_Groups[index];
        private ulong ReadWord(int index) => m_Words == null ? m_InlineWord : m_Words[index];
        private void WriteGroup(int index, Group value)
        { if (m_Groups == null) m_InlineGroup = value; else m_Groups[index] = value; }
        private void WriteWord(int index, ulong value)
        { if (m_Words == null) m_InlineWord = value; else m_Words[index] = value; }

        public RuntimeTagSet(TagRegistry registry, int capacity = 0)
        {
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            if (capacity > 1) EnsureCapacity(capacity);
            else if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        public RuntimeTagSet(RuntimeTagSet source)
        {
            Registry = Required(source).Registry;
            if (source.m_WordCount <= 1)
            {
                m_GroupCount = source.m_GroupCount; m_WordCount = source.m_WordCount; m_Count = source.m_Count;
                if (m_WordCount != 0) { m_InlineGroup = source.ReadGroup(0); m_InlineWord = source.ReadWord(0); }
                return;
            }
            EnsureLayout(source.m_GroupCount, source.m_WordCount, true);
            CopyContents(source);
        }
        private static RuntimeTagSet Required(RuntimeTagSet value) => value ?? throw new ArgumentNullException(nameof(value));
        private void Require(RuntimeTagSet other)
        { if (other == null) throw new ArgumentNullException(nameof(other)); Registry.RequireSame(other.Registry); }
        private bool Accept(RuntimeTag tag)
        { if (tag.Owner == null) return false; Registry.RequireSame(tag.Owner); return true; }
        public void EnsureCapacity(int capacity)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            capacity = Math.Min(capacity, Registry.Count);
            EnsureLayout(Math.Min(capacity, MaxGroups), Math.Min(capacity, Registry.WordCount));
        }
        private void EnsureLayout(int groups, int words, bool exact = false)
        {
            // Output builders publish logical counts only when complete. Growth must
            // preserve all existing slots, including the prefix currently being built.
            if (groups > GroupCapacity)
            {
                int size = exact ? groups : (int)Math.Min(MaxGroups, Math.Max(groups, Math.Max(4L, 2L * GroupCapacity)));
                var buffer = new Group[size];
                if (m_Groups != null) Array.Copy(m_Groups, buffer, m_Groups.Length);
                else buffer[0] = m_InlineGroup;
                m_Groups = buffer;
            }
            if (words > WordCapacity)
            {
                int size = exact ? words : (int)Math.Min(Registry.WordCount, Math.Max(words, Math.Max(4L, 2L * WordCapacity)));
                var buffer = new ulong[size];
                if (m_Words != null) Array.Copy(m_Words, buffer, m_Words.Length);
                else buffer[0] = m_InlineWord;
                m_Words = buffer;
            }
        }
        private int FindGroup(int key)
        {
            if (m_GroupCount == 1)
            {
                int current = ReadGroup(0).Key;
                return key == current ? 0 : key < current ? -1 : -2;
            }
            int low = 0, high = m_GroupCount - 1;
            while (low <= high)
            {
                int mid = low + ((high - low) >> 1), value = ReadGroup(mid).Key;
                if (value == key) return mid;
                if (value < key) low = mid + 1; else high = mid - 1;
            }
            return ~low;
        }
        public bool HasTagExact(RuntimeTag tag)
        {
            if (!ReferenceEquals(Registry, tag.Owner))
            { if (tag.Owner == null) return false; Registry.RequireSame(tag.Owner); }
            return ContainsId(tag.Id);
        }
        internal bool ContainsId(int id)
        {
            if (m_GroupCount == 0) return false;
            Group group;
            if (m_GroupCount == 1)
            {
                group = ReadGroup(0);
                if (group.Key != (id >> 12)) return false;
            }
            else
            {
                int at = FindGroup(id >> 12);
                if (at < 0) return false;
                group = ReadGroup(at);
            }
            int slot = (id >> 6) & 63;
            ulong wordBit = 1UL << slot;
            if ((group.Mask & wordBit) == 0) return false;
            int rank = group.Mask == wordBit ? 0 : group.Mask == ulong.MaxValue ? slot
                : Bits.Count(group.Mask & (wordBit - 1));
            return (ReadWord(group.Offset + rank) & (1UL << (id & 63))) != 0;
        }
        public bool HasTag(RuntimeTag tag) => Accept(tag) && AnyInRange(tag.Id, Registry.Ends[tag.Id]);
        public bool AddTag(RuntimeTag tag) => Accept(tag) && AddId(tag.Id);
        public bool RemoveTag(RuntimeTag tag) => Accept(tag) && RemoveId(tag.Id);
        internal bool AddId(int id)
        {
            int key = id >> 12, at = FindGroup(key);
            ulong directoryBit = 1UL << ((id >> 6) & 63), member = 1UL << (id & 63);
            if (at >= 0)
            {
                Group group = ReadGroup(at);
                int offset = group.Offset + Bits.Count(group.Mask & (directoryBit - 1));
                if ((group.Mask & directoryBit) != 0)
                {
                    ulong before = ReadWord(offset);
                    if ((before & member) != 0) return false;
                    WriteWord(offset, before | member); m_Count++; return true;
                }
                EnsureLayout(m_GroupCount, m_WordCount + 1);
                InsertWord(offset, member);
                group.Mask |= directoryBit;
                WriteGroup(at, group); AdjustOffsets(at + 1, 1);
            }
            else
            {
                at = ~at;
                int offset = at == m_GroupCount ? m_WordCount : ReadGroup(at).Offset;
                EnsureLayout(m_GroupCount + 1, m_WordCount + 1);
                InsertWord(offset, member);
                if (at < m_GroupCount) Array.Copy(m_Groups, at, m_Groups, at + 1, m_GroupCount - at);
                WriteGroup(at, new Group(key, offset, directoryBit));
                m_GroupCount++; AdjustOffsets(at + 1, 1);
            }
            m_Count++; return true;
        }
        private void InsertWord(int at, ulong word)
        {
            if (at < m_WordCount) Array.Copy(m_Words, at, m_Words, at + 1, m_WordCount - at);
            WriteWord(at, word); m_WordCount++;
        }
        private void AdjustOffsets(int first, int delta)
        {
            for (int i = first; i < m_GroupCount; i++)
            { Group group = ReadGroup(i); group.Offset += delta; WriteGroup(i, group); }
        }
        private bool RemoveId(int id)
        {
            int at = FindGroup(id >> 12);
            if (at < 0) return false;
            Group group = ReadGroup(at);
            ulong directoryBit = 1UL << ((id >> 6) & 63);
            if ((group.Mask & directoryBit) == 0) return false;
            int offset = group.Offset + Bits.Count(group.Mask & (directoryBit - 1));
            ulong member = 1UL << (id & 63), before = ReadWord(offset);
            if ((before & member) == 0) return false;
            ulong after = before & ~member; m_Count--;
            if (after != 0) { WriteWord(offset, after); return true; }
            m_WordCount--;
            if (offset < m_WordCount) Array.Copy(m_Words, offset + 1, m_Words, offset, m_WordCount - offset);
            group.Mask &= ~directoryBit;
            if (group.Mask == 0)
            {
                m_GroupCount--;
                if (at < m_GroupCount) Array.Copy(m_Groups, at + 1, m_Groups, at, m_GroupCount - at);
                AdjustOffsets(at, -1);
            }
            else { WriteGroup(at, group); AdjustOffsets(at + 1, -1); }
            return true;
        }
        public void Clear()
        {
            // Inactive value-type slots are never read; no references are retained there.
            m_GroupCount = m_WordCount = m_Count = 0;
        }
        public void CopyFrom(RuntimeTagSet source)
        {
            Require(source);
            if (ReferenceEquals(this, source)) return;
            EnsureLayout(source.m_GroupCount, source.m_WordCount); CopyContents(source);
        }
        private void CopyContents(RuntimeTagSet source)
        {
            if (source.m_GroupCount != 0)
            {
                if (m_Groups != null && source.m_Groups != null) Array.Copy(source.m_Groups, m_Groups, source.m_GroupCount);
                else WriteGroup(0, source.ReadGroup(0));
                if (m_Words != null && source.m_Words != null) Array.Copy(source.m_Words, m_Words, source.m_WordCount);
                else WriteWord(0, source.ReadWord(0));
            }
            m_GroupCount = source.m_GroupCount; m_WordCount = source.m_WordCount; m_Count = source.m_Count;
        }
        public void AppendTags(RuntimeTagSet other)
        {
            Require(other);
            if (ReferenceEquals(this, other) || other.m_Count == 0) return;
            if (m_Count == 0) { CopyFrom(other); return; }
            UnionCore(this, other, this);
        }
        public static RuntimeTagSet Union(RuntimeTagSet left, RuntimeTagSet right)
        {
            Required(left).Require(right);
            var result = new RuntimeTagSet(left.Registry); UnionCore(left, right, result); return result;
        }
        public static void UnionInto(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result)
        {
            Required(left).Require(right); left.Require(result);
            if (ReferenceEquals(result, left)) { result.AppendTags(right); return; }
            if (ReferenceEquals(result, right)) { result.AppendTags(left); return; }
            UnionCore(left, right, result);
        }
        private static void UnionCore(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result)
        {
            if (ReferenceEquals(left, right) || right.m_Count == 0) { result.CopyFrom(left); return; }
            if (left.m_Count == 0) { result.CopyFrom(right); return; }

            if (left.m_WordCount == 1 && right.m_WordCount == 1)
            {
                Group first = left.ReadGroup(0), second = right.ReadGroup(0);
                if (first.Key == second.Key && first.Mask == second.Mask)
                {
                    ulong word = left.ReadWord(0) | right.ReadWord(0);
                    result.WriteWord(0, word); result.WriteGroup(0, new Group(first.Key, 0, first.Mask));
                    result.m_GroupCount = result.m_WordCount = 1; result.m_Count = Bits.Count(word);
                    return;
                }
            }
            // Size directories, not individual members. Reserve only the actual result words.
            int a = 0, b = 0, groups = 0, words = 0;
            while (a < left.m_GroupCount && b < right.m_GroupCount)
            {
                Group x = left.ReadGroup(a), y = right.ReadGroup(b); ulong mask;
                if (x.Key < y.Key) { mask = x.Mask; a++; }
                else if (x.Key > y.Key) { mask = y.Mask; b++; }
                else { mask = x.Mask | y.Mask; a++; b++; }
                groups++; words += Bits.Count(mask);
            }
            for (; a < left.m_GroupCount; a++) { groups++; words += Bits.Count(left.ReadGroup(a).Mask); }
            for (; b < right.m_GroupCount; b++) { groups++; words += Bits.Count(right.ReadGroup(b).Mask); }
            result.EnsureLayout(groups, words);
            a = left.m_GroupCount - 1; b = right.m_GroupCount - 1;
            int groupWrite = groups - 1, wordWrite = words, duplicates = 0;
            // Reverse writing supports input aliases: union is a superset of both inputs.
            while (a >= 0 || b >= 0)
            {
                Group x = a >= 0 ? left.ReadGroup(a) : default;
                Group y = b >= 0 ? right.ReadGroup(b) : default;
                if (b < 0 || (a >= 0 && x.Key > y.Key))
                {
                    int length = Bits.Count(x.Mask); wordWrite -= length;
                    CopyWords(left, x.Offset, result, wordWrite, length);
                    result.WriteGroup(groupWrite--, new Group(x.Key, wordWrite, x.Mask)); a--;
                }
                else if (a < 0 || y.Key > x.Key)
                {
                    int length = Bits.Count(y.Mask); wordWrite -= length;
                    CopyWords(right, y.Offset, result, wordWrite, length);
                    result.WriteGroup(groupWrite--, new Group(y.Key, wordWrite, y.Mask)); b--;
                }
                else
                {
                    ulong mask = x.Mask | y.Mask;
                    int length = Bits.Count(mask), begin = wordWrite - length;
                    int xi = x.Offset + Bits.Count(x.Mask) - 1, yi = y.Offset + Bits.Count(y.Mask) - 1;
                    if (x.Mask == y.Mask)
                    {
                        for (int i = length - 1; i >= 0; i--)
                        {
                            ulong first = left.ReadWord(x.Offset + i), second = right.ReadWord(y.Offset + i);
                            result.WriteWord(begin + i, first | second); duplicates += Bits.Count(first & second);
                        }
                    }
                    else
                    {
                        // Reverse directory bits once, then merge low bits without a
                        // highest-bit scan or rank operation for every member word.
                        ulong xm = Bits.Reverse(x.Mask), ym = Bits.Reverse(y.Mask);
                        while (xm != 0 && ym != 0)
                        {
                            ulong xb = xm & unchecked(0UL - xm), yb = ym & unchecked(0UL - ym);
                            if (xb < yb) { result.WriteWord(--wordWrite, left.ReadWord(xi--)); xm &= xm - 1; }
                            else if (yb < xb) { result.WriteWord(--wordWrite, right.ReadWord(yi--)); ym &= ym - 1; }
                            else
                            {
                                ulong first = left.ReadWord(xi--), second = right.ReadWord(yi--);
                                result.WriteWord(--wordWrite, first | second); duplicates += Bits.Count(first & second);
                                xm &= xm - 1; ym &= ym - 1;
                            }
                        }
                        if (xm != 0)
                        { int rest = xi - x.Offset + 1; wordWrite -= rest; CopyWords(left, x.Offset, result, wordWrite, rest); }
                        if (ym != 0)
                        { int rest = yi - y.Offset + 1; wordWrite -= rest; CopyWords(right, y.Offset, result, wordWrite, rest); }
                    }
                    wordWrite = begin;
                    result.WriteGroup(groupWrite--, new Group(x.Key, begin, mask)); a--; b--;
                }
            }
            int count = (int)((long)left.m_Count + right.m_Count - duplicates);
            result.m_GroupCount = groups; result.m_WordCount = words; result.m_Count = count;
        }
        private static void CopyWords(RuntimeTagSet source, int sourceStart, RuntimeTagSet result, int destinationStart, int count)
        {
            if (source.m_Words != null && result.m_Words != null)
                Array.Copy(source.m_Words, sourceStart, result.m_Words, destinationStart, count);
            else for (int i = count - 1; i >= 0; i--) result.WriteWord(destinationStart + i, source.ReadWord(sourceStart + i));
        }
        public bool RemoveTags(RuntimeTagSet other)
        {
            Require(other);
            if (other.m_Count == 0 || m_Count == 0) return false;
            if (ReferenceEquals(this, other)) { Clear(); return true; }
            int before = m_Count; SubsetCore(this, other, this, true); return before != m_Count;
        }
        public static RuntimeTagSet IntersectionExact(RuntimeTagSet left, RuntimeTagSet right)
        {
            Required(left).Require(right);
            var result = new RuntimeTagSet(left.Registry); SubsetCore(left, right, result, false); return result;
        }
        public static void IntersectionExactInto(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result)
        { Required(left).Require(right); left.Require(result); SubsetCore(left, right, result, false); }
        public void FilterExactInto(RuntimeTagSet conditions, RuntimeTagSet result) => IntersectionExactInto(this, conditions, result);
        private static void SubsetCore(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result, bool difference)
        {
            if (ReferenceEquals(left, right)) { if (difference) result.Clear(); else result.CopyFrom(left); return; }
            if (left.m_WordCount == 1 && right.m_WordCount == 1)
            {
                Group x = left.ReadGroup(0), y = right.ReadGroup(0);
                ulong other = x.Key == y.Key && x.Mask == y.Mask ? right.ReadWord(0) : 0;
                ulong kept = difference ? left.ReadWord(0) & ~other : left.ReadWord(0) & other;
                if (kept == 0) { result.Clear(); return; }
                result.WriteWord(0, kept); result.WriteGroup(0, new Group(x.Key, 0, x.Mask));
                result.m_GroupCount = result.m_WordCount = 1; result.m_Count = Bits.Count(kept); return;
            }
            int aCount = left.m_GroupCount, bCount = right.m_GroupCount;
            int b = 0, groupWrite = 0, wordWrite = 0, count = 0;
            int groupCapacity = result.GroupCapacity, wordCapacity = result.WordCapacity;
            for (int a = 0; a < aCount; a++)
            {
                Group x = left.ReadGroup(a), y = default;
                while (b < bCount && right.ReadGroup(b).Key < x.Key) b++;
                bool match = b < bCount && (y = right.ReadGroup(b)).Key == x.Key;
                if (!difference && !match) continue;
                ulong xm = x.Mask, ym = match ? y.Mask : 0, keptMask = 0;
                int xi = x.Offset, yi = y.Offset, begin = wordWrite;
                // Ordered directory cursors replace two rank/popcount operations per word.
                while (xm != 0)
                {
                    ulong bit = xm & unchecked(0UL - xm), first = left.ReadWord(xi++), second = 0;
                    while (ym != 0 && (ym & unchecked(0UL - ym)) < bit) { ym &= ym - 1; yi++; }
                    if ((ym & bit) != 0) { second = right.ReadWord(yi++); ym &= ~bit; }
                    ulong kept = difference ? first & ~second : first & second;
                    if (kept != 0)
                    {
                        if (groupWrite == groupCapacity || wordWrite == wordCapacity)
                        {
                            result.EnsureLayout(groupWrite + 1, wordWrite + 1);
                            groupCapacity = result.GroupCapacity; wordCapacity = result.WordCapacity;
                        }
                        result.WriteWord(wordWrite++, kept); keptMask |= bit; count += Bits.Count(kept);
                    }
                    xm &= xm - 1;
                }
                if (keptMask != 0) result.WriteGroup(groupWrite++, new Group(x.Key, begin, keptMask));
            }
            result.m_GroupCount = groupWrite; result.m_WordCount = wordWrite; result.m_Count = count;
        }
        public bool HasAnyExact(RuntimeTagSet other)
        {
            Require(other); int a = 0, b = 0;
            while (a < m_GroupCount && b < other.m_GroupCount)
            {
                Group x = ReadGroup(a), y = other.ReadGroup(b);
                if (x.Key < y.Key) { a++; continue; }
                if (x.Key > y.Key) { b++; continue; }
                ulong common = x.Mask & y.Mask;
                while (common != 0)
                {
                    ulong bit = common & unchecked(0UL - common);
                    if ((ReadWord(x.Offset + Bits.Count(x.Mask & (bit - 1))) &
                        other.ReadWord(y.Offset + Bits.Count(y.Mask & (bit - 1)))) != 0) return true;
                    common &= common - 1;
                }
                a++; b++;
            }
            return false;
        }
        public bool HasAllExact(RuntimeTagSet other)
        {
            Require(other);
            if (other.m_Count > m_Count) return false;
            for (int b = 0; b < other.m_GroupCount; b++)
            {
                Group y = other.ReadGroup(b); int at = FindGroup(y.Key);
                if (at < 0) return false;
                Group x = ReadGroup(at);
                if ((x.Mask & y.Mask) != y.Mask) return false;
                ulong remaining = y.Mask; int word = y.Offset;
                while (remaining != 0)
                {
                    ulong bit = remaining & unchecked(0UL - remaining), needed = other.ReadWord(word++);
                    if ((ReadWord(x.Offset + Bits.Count(x.Mask & (bit - 1))) & needed) != needed) return false;
                    remaining &= remaining - 1;
                }
            }
            return true;
        }
        internal bool AnyInRange(int start, int end)
        {
            if (m_Count == 0 || start >= end) return false;
            int at = FindGroup(start >> 12); if (at < 0) at = ~at;
            int firstWord = start >> 6, lastWord = (end - 1) >> 6;
            for (; at < m_GroupCount; at++)
            {
                Group group = ReadGroup(at); int wordBase = group.Key << 6;
                if (wordBase > lastWord) break;
                int lo = Math.Max(0, firstWord - wordBase), hi = Math.Min(64, lastWord - wordBase + 1);
                ulong candidate = group.Mask & RangeMask(lo, hi);
                while (candidate != 0)
                {
                    ulong bit = candidate & unchecked(0UL - candidate);
                    int logicalWord = wordBase + Bits.Lowest(bit);
                    ulong members = ReadWord(group.Offset + Bits.Count(group.Mask & (bit - 1)));
                    if (logicalWord == firstWord) members &= ulong.MaxValue << (start & 63);
                    if (logicalWord == lastWord && (end & 63) != 0) members &= (1UL << (end & 63)) - 1;
                    if (members != 0) return true;
                    candidate &= candidate - 1;
                }
            }
            return false;
        }
        private static ulong RangeMask(int first, int end) => (ulong.MaxValue << first)
            & (end == 64 ? ulong.MaxValue : (1UL << end) - 1);
        public bool HasAny(RuntimeTagSet conditions)
        {
            Require(conditions); var iterator = new IdEnumerator(conditions);
            while (iterator.MoveNext()) if (AnyInRange(iterator.Current, Registry.Ends[iterator.Current])) return true;
            return false;
        }
        public bool HasAll(RuntimeTagSet conditions)
        {
            Require(conditions); var iterator = new IdEnumerator(conditions);
            while (iterator.MoveNext()) if (!AnyInRange(iterator.Current, Registry.Ends[iterator.Current])) return false;
            return true;
        }
        public bool MatchesQuery(FrozenGameplayTagQuery query) => query != null && query.Matches(this);
        /// <summary>May overwrite the source, but never a separate condition set.</summary>
        public void FilterInto(RuntimeTagSet conditions, RuntimeTagSet result)
        {
            Require(conditions); Require(result);
            if (ReferenceEquals(result, conditions) && !ReferenceEquals(this, result))
                throw new ArgumentException("Hierarchy filter cannot overwrite its separate condition set.", nameof(result));
            if (ReferenceEquals(this, conditions)) { result.CopyFrom(this); return; }
            int groups = m_GroupCount, groupWrite = 0, wordWrite = 0, count = 0;
            for (int i = 0; i < groups; i++)
            {
                Group group = ReadGroup(i); ulong mask = group.Mask, keptMask = 0;
                int read = group.Offset, begin = wordWrite;
                while (mask != 0)
                {
                    ulong directoryBit = mask & unchecked(0UL - mask), members = ReadWord(read++), kept = 0;
                    int start = (group.Key << 12) + (Bits.Lowest(directoryBit) << 6);
                    while (members != 0)
                    {
                        ulong bit = members & unchecked(0UL - members);
                        int ancestor = start + Bits.Lowest(bit);
                        while (ancestor >= 0 && !conditions.ContainsId(ancestor)) ancestor = Registry.Parents[ancestor];
                        if (ancestor >= 0) kept |= bit;
                        members &= members - 1;
                    }
                    if (kept != 0)
                    {
                        result.EnsureLayout(groupWrite + 1, wordWrite + 1);
                        result.WriteWord(wordWrite++, kept); keptMask |= directoryBit; count += Bits.Count(kept);
                    }
                    mask &= mask - 1;
                }
                if (keptMask != 0) result.WriteGroup(groupWrite++, new Group(group.Key, begin, keptMask));
            }
            result.m_GroupCount = groupWrite; result.m_WordCount = wordWrite; result.m_Count = count;
        }
        public bool SetEquals(RuntimeTagSet other)
        {
            Require(other);
            if (m_Count != other.m_Count || m_GroupCount != other.m_GroupCount) return false;
            for (int i = 0; i < m_GroupCount; i++)
            { Group a = ReadGroup(i), b = other.ReadGroup(i); if (a.Key != b.Key || a.Mask != b.Mask) return false; }
            for (int i = 0; i < m_WordCount; i++) if (ReadWord(i) != other.ReadWord(i)) return false;
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
            private readonly RuntimeTagSet m_Set;
            private readonly int m_Groups;
            private int m_Group, m_Word, m_Base, m_LogicalWord;
            private ulong m_Directory, m_Members;
            internal int Current { get; private set; }
            internal IdEnumerator(RuntimeTagSet set)
            {
                m_Set = set; m_Groups = set.m_GroupCount;
                m_Group = m_Word = m_Base = m_LogicalWord = 0;
                m_Directory = m_Members = 0; Current = -1;
            }
            internal bool MoveNext()
            {
                if (m_Members == 0)
                {
                    if (m_Directory == 0)
                    {
                        if (m_Group >= m_Groups) return false;
                        Group group = m_Set.ReadGroup(m_Group++);
                        m_Base = group.Key << 12; m_Directory = group.Mask; m_Word = group.Offset;
                    }
                    m_LogicalWord = m_Base + (Bits.Lowest(m_Directory) << 6);
                    m_Directory &= m_Directory - 1; m_Members = m_Set.ReadWord(m_Word++);
                }
                Current = m_LogicalWord + Bits.Lowest(m_Members);
                m_Members &= m_Members - 1; return true;
            }
        }
        private static class Bits
        {
            internal static ulong Reverse(ulong value)
            {
                value = ((value >> 1) & 0x5555555555555555UL) | ((value & 0x5555555555555555UL) << 1);
                value = ((value >> 2) & 0x3333333333333333UL) | ((value & 0x3333333333333333UL) << 2);
                value = ((value >> 4) & 0x0F0F0F0F0F0F0F0FUL) | ((value & 0x0F0F0F0F0F0F0F0FUL) << 4);
                value = ((value >> 8) & 0x00FF00FF00FF00FFUL) | ((value & 0x00FF00FF00FF00FFUL) << 8);
                value = ((value >> 16) & 0x0000FFFF0000FFFFUL) | ((value & 0x0000FFFF0000FFFFUL) << 16);
                return (value >> 32) | (value << 32);
            }
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
        }
    }
}
