using System;
using System.Runtime.CompilerServices;

namespace GameplayTags
{
    // Packed entries own the data; buckets contain dense indices + 1 (zero is empty).
    // Small collections avoid a hash allocation and scan at most eight entries.
    // Larger collections use linear probing and backward-shift deletion: no tombstones,
    // no registry-sized per-container arrays, and no object per tag.
    internal sealed class TagStorage
    {
        internal struct Entry
        {
            public int Id;
            public int Count;
            public int ExplicitCount;
            public int ExplicitIndex;
        }

        internal const int LinearCapacity = 8;
        internal Entry[] Entries = Array.Empty<Entry>();
        internal int[] ExplicitIndices = Array.Empty<int>();
        internal int Count;
        internal int ExplicitCount;
        private int[] m_Buckets = Array.Empty<int>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Hash(int id)
        {
            uint hash = unchecked((uint)id * 0x9e3779b9u);
            return (int)(hash ^ (hash >> 16));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int Find(int id)
        {
            int[] buckets = m_Buckets;
            Entry[] entries = Entries;
            if (buckets.Length == 0)
            {
                for (int i = 0; i < Count; i++)
                    if (entries[i].Id == id)
                        return i;
                return -1;
            }

            int mask = buckets.Length - 1;
            int slot = Hash(id) & mask;
            int encoded;
            while ((encoded = buckets[slot]) != 0)
            {
                if (entries[encoded - 1].Id == id)
                    return encoded - 1;
                slot = (slot + 1) & mask;
            }
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool Contains(int id) => Find(id) >= 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool ContainsExplicit(int id)
        {
            int index = Find(id);
            return index >= 0 && Entries[index].ExplicitCount != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetCount(int id)
        {
            int index = Find(id);
            return index < 0 ? 0 : Entries[index].Count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetExplicitCount(int id)
        {
            int index = Find(id);
            return index < 0 ? 0 : Entries[index].ExplicitCount;
        }

        internal int AddExplicit(int id, int amount = 1)
        {
            int index = Find(id);
            if (index < 0)
                index = Insert(id);
            if (Entries[index].ExplicitCount == 0)
                AddExplicitIndex(index);
            return Entries[index].ExplicitCount += amount;
        }

        // Set insertion and its duplicate semantics require just one lookup.
        internal bool TryAddExplicit(int id)
        {
            int index = Find(id);
            if (index < 0)
                index = Insert(id);
            else if (Entries[index].ExplicitCount != 0)
                return false;
            Entries[index].ExplicitCount = 1;
            AddExplicitIndex(index);
            return true;
        }

        private void AddExplicitIndex(int index)
        {
            if (ExplicitCount == ExplicitIndices.Length)
                Array.Resize(ref ExplicitIndices, Math.Max(LinearCapacity, ExplicitCount * 2));
            Entries[index].ExplicitIndex = ExplicitCount;
            ExplicitIndices[ExplicitCount++] = index;
        }

        internal int AddTotal(int id, int amount = 1)
        {
            int index = Find(id);
            if (index < 0)
                index = Insert(id);
            return Entries[index].Count += amount;
        }

        internal int RemoveExplicit(int id, int amount = 1)
        {
            int index = Find(id);
            if (index < 0 || Entries[index].ExplicitCount == 0)
                return -1;
            int count = Entries[index].ExplicitCount -= amount;
            if (count == 0)
            {
                int hole = Entries[index].ExplicitIndex;
                int movedIndex = ExplicitIndices[--ExplicitCount];
                ExplicitIndices[hole] = movedIndex;
                Entries[movedIndex].ExplicitIndex = hole;
            }
            return count;
        }

        // The caller has already removed a present explicit contribution. Every
        // ancestor is therefore present and has at least 'amount' contributions.
        internal int RemoveTotal(int id, int amount = 1)
        {
            int index = Find(id);
            int count = Entries[index].Count -= amount;
            if (count == 0)
                RemoveAt(index);
            return count;
        }

        private int Insert(int id)
        {
            EnsureEntryCapacity(Count + 1);
            int index = Count++;
            Entries[index] = new Entry { Id = id };
            if (m_Buckets.Length != 0)
            {
                int mask = m_Buckets.Length - 1;
                int slot = Hash(id) & mask;
                while (m_Buckets[slot] != 0)
                    slot = (slot + 1) & mask;
                m_Buckets[slot] = index + 1;
            }
            return index;
        }

        private void RemoveAt(int index)
        {
            int[] buckets = m_Buckets;
            if (buckets.Length != 0)
            {
                int mask = buckets.Length - 1;
                int slot = Hash(Entries[index].Id) & mask;
                while (buckets[slot] != index + 1)
                    slot = (slot + 1) & mask;

                // Move entries whose probe sequence crosses the hole. Comparing
                // modular distances also handles clusters wrapping around slot zero.
                int hole = slot;
                for (int scan = (hole + 1) & mask; buckets[scan] != 0; scan = (scan + 1) & mask)
                {
                    int home = Hash(Entries[buckets[scan] - 1].Id) & mask;
                    if (((scan - home) & mask) >= ((scan - hole) & mask))
                    {
                        buckets[hole] = buckets[scan];
                        hole = scan;
                    }
                }
                buckets[hole] = 0;
            }

            int last = --Count;
            if (index == last)
                return;

            Entry moved = Entries[last];
            Entries[index] = moved;
            if (moved.ExplicitCount != 0)
                ExplicitIndices[moved.ExplicitIndex] = index;
            if (buckets.Length != 0)
            {
                int mask = buckets.Length - 1;
                int slot = Hash(moved.Id) & mask;
                while (buckets[slot] != last + 1)
                    slot = (slot + 1) & mask;
                buckets[slot] = index + 1;
            }
        }

        // Reserve the maximum number of distinct tags INCLUDING ancestors.
        internal void EnsureCapacity(int capacity)
        {
            EnsureEntryCapacity(capacity);
            if (ExplicitIndices.Length < capacity)
            {
                int size = Math.Max(LinearCapacity, ExplicitIndices.Length * 2);
                while (size < capacity)
                    size *= 2;
                Array.Resize(ref ExplicitIndices, size);
            }
        }

        private void EnsureEntryCapacity(int capacity)
        {
            if (Entries.Length < capacity)
            {
                int size = Math.Max(LinearCapacity, Entries.Length * 2);
                while (size < capacity)
                    size *= 2;
                Array.Resize(ref Entries, size);
            }

            if (capacity <= LinearCapacity || m_Buckets.Length >= capacity * 2)
                return;

            int bucketCount = 32;
            while (bucketCount < capacity * 2)
                bucketCount *= 2;
            m_Buckets = new int[bucketCount];
            int mask = bucketCount - 1;
            for (int i = 0; i < Count; i++)
            {
                int slot = Hash(Entries[i].Id) & mask;
                while (m_Buckets[slot] != 0)
                    slot = (slot + 1) & mask;
                m_Buckets[slot] = i + 1;
            }
        }

        internal void Clear()
        {
            // Only occupied buckets need clearing; a large, mostly empty retained
            // buffer must not turn Clear into an operation over its capacity.
            int[] buckets = m_Buckets;
            if (buckets.Length != 0)
            {
                int mask = buckets.Length - 1;
                // Match the encoded dense index, deliberately scanning past holes
                // made earlier in this loop (ordinary Find would stop too soon).
                for (int i = 0; i < Count; i++)
                {
                    int slot = Hash(Entries[i].Id) & mask;
                    while (buckets[slot] != i + 1)
                        slot = (slot + 1) & mask;
                    buckets[slot] = 0;
                }
            }
            Count = 0;
            ExplicitCount = 0;
        }

        internal void CopyFrom(TagStorage other)
        {
            if (ReferenceEquals(this, other))
                return;
            Clear();
            EnsureCapacity(other.Count);
            Array.Copy(other.Entries, Entries, other.Count);
            Array.Copy(other.ExplicitIndices, ExplicitIndices, other.ExplicitCount);
            Count = other.Count;
            ExplicitCount = other.ExplicitCount;
            if (m_Buckets.Length == 0)
                return;
            int mask = m_Buckets.Length - 1;
            for (int i = 0; i < Count; i++)
            {
                int slot = Hash(Entries[i].Id) & mask;
                while (m_Buckets[slot] != 0)
                    slot = (slot + 1) & mask;
                m_Buckets[slot] = i + 1;
            }
        }
    }
}
