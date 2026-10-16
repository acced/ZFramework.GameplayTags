using System;
using System.Buffers;
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
        // The active index may occupy a prefix of a larger reserved array. This
        // lets copies transfer the source index even when capacities differ.
        private int m_BucketMask;

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
            int mask = m_BucketMask;
            if (mask == 0)
            {
                for (int i = 0; i < Count; i++)
                    if (entries[i].Id == id)
                        return i;
                return -1;
            }

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
            Entries[index].Count++;
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
            int mask = m_BucketMask;
            if (mask != 0)
            {
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
            int mask = m_BucketMask;
            if (mask != 0)
            {
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
            if (mask != 0)
            {
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

            if (capacity <= LinearCapacity || m_BucketMask >= capacity * 2 - 1)
                return;

            ResetIndex(BucketCapacityFor(capacity));
            BuildIndex();
        }

        internal void Clear()
        {
            // Only occupied buckets need clearing; a large, mostly empty retained
            // buffer must not turn Clear into an operation over its capacity.
            int[] buckets = m_Buckets;
            int mask = m_BucketMask;
            if (mask != 0 && Count >= (mask + 1) / 4)
                Array.Clear(buckets, 0, mask + 1);
            else if (mask != 0)
            {
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
            int count = other.Count;
            if (count == 0)
            {
                Clear();
                return;
            }

            int sourceBucketCount = other.m_BucketMask == 0 ? 0 : other.m_BucketMask + 1;
            // The dense order and active mask fully describe the index. Retained
            // destination capacity does not force a rehash. A very sparse source
            // instead gets a compact index so copying stays proportional to data.
            bool copyBuckets = sourceBucketCount <= Math.Max(32, count * 8);
            if (copyBuckets)
            {
                if (m_Buckets.Length < sourceBucketCount)
                    m_Buckets = new int[sourceBucketCount];
                if (sourceBucketCount != 0)
                    Array.Copy(other.m_Buckets, m_Buckets, sourceBucketCount);
                m_BucketMask = other.m_BucketMask;
            }
            else
                ResetIndex(BucketCapacityFor(count));

            EnsureOverwriteCapacity(count, other.ExplicitCount);
            Array.Copy(other.Entries, Entries, count);
            Array.Copy(other.ExplicitIndices, ExplicitIndices, other.ExplicitCount);
            Count = count;
            ExplicitCount = other.ExplicitCount;
            if (!copyBuckets)
                BuildIndex();
        }

        // Add unique explicit members and propagate shared ancestor contributions in batches.
        internal void AddUnion(TagStorage source)
        {
            if (source.ExplicitCount <= 8)
            {
                for (int i = 0; i < source.ExplicitCount; i++)
                {
                    int id = source.Entries[source.ExplicitIndices[i]].Id;
                    if (!TryAddExplicit(id))
                        continue;
                    ReadOnlySpan<int> path = GameplayTagManager.GetHierarchyIndices(id);
                    for (int j = 0; j < path.Length - 1; j++)
                        AddTotal(path[j]);
                }
                return;
            }

            // At most one parent is queued per new explicit tag; each dequeue
            // can enqueue at most one parent, so peak occupancy never exceeds E.
            int[] queue = ArrayPool<int>.Shared.Rent(source.ExplicitCount);
            try
            {
                int tail = 0, queued = 0;
                for (int i = 0; i < source.ExplicitCount; i++)
                {
                    int id = source.Entries[source.ExplicitIndices[i]].Id;
                    int index = Find(id);
                    if (index < 0)
                        index = Insert(id);
                    else if ((Entries[index].ExplicitCount & 1) != 0)
                        continue;
                    // High bits temporarily hold pending ancestor contributions.
                    // An implicit parent can be promoted while already queued.
                    Entries[index].ExplicitCount |= 1;
                    AddExplicitIndex(index);
                    Entries[index].Count++;
                    int parent = GameplayTagManager.GetParentIndex(id);
                    if (parent != 0)
                        QueueContribution(parent, 1, queue, ref tail, ref queued);
                }

                int head = 0;
                while (queued != 0)
                {
                    int index = queue[head++];
                    if (head == queue.Length)
                        head = 0;
                    queued--;
                    int pending = (int)((uint)Entries[index].ExplicitCount >> 1);
                    Entries[index].ExplicitCount &= 1;
                    Entries[index].Count += pending;
                    int parent = GameplayTagManager.GetParentIndex(Entries[index].Id);
                    if (parent != 0)
                        QueueContribution(parent, pending, queue, ref tail, ref queued);
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(queue, clearArray: false);
            }
        }

        private void QueueContribution(int id, int amount, int[] queue, ref int tail, ref int queued)
        {
            int index = Find(id);
            if (index < 0)
                index = Insert(id);
            if ((Entries[index].ExplicitCount & ~1) == 0)
            {
                queue[tail++] = index;
                if (tail == queue.Length)
                    tail = 0;
                queued++;
            }
            Entries[index].ExplicitCount = unchecked(Entries[index].ExplicitCount + (amount << 1));
        }

        // Bulk operations collect unique explicit IDs into ExplicitIndices first.
        // They may reuse this buffer while filtering an aliased input forwards.
        internal void EnsureExplicitCapacity(int capacity)
        {
            if (ExplicitIndices.Length < capacity)
                Array.Resize(ref ExplicitIndices, ArrayCapacityFor(capacity));
        }

        internal void BuildFromExplicitIds(int explicitCount)
        {
            if (explicitCount == 0)
            {
                Clear();
                return;
            }
            SortIds(ExplicitIndices, explicitCount);

            // In DFS order, an ancestor <= the previous explicit ID already
            // belongs to that ID's closure. Walk only newly introduced segments,
            // once per node, to size the result without reserving discarded paths.
            int total = 0;
            int previousId = 0;
            for (int i = 0; i < explicitCount; i++)
            {
                int id = ExplicitIndices[i];
                for (int pathId = id; pathId > previousId; pathId = GameplayTagManager.GetParentIndex(pathId))
                    total++;
                previousId = id;
            }

            ResetIndex(BucketCapacityFor(total));
            EnsureOverwriteCapacity(total, explicitCount);

            // Until the final explicit-index pass, ExplicitIndex is a parent
            // dense index. The entries themselves are the DFS stack: no scratch
            // array and no per-node hash lookup is needed for ancestor counts.
            int next = 0;
            int parent = -1;
            previousId = 0;
            for (int i = 0; i < explicitCount; i++)
            {
                int id = ExplicitIndices[i];
                int ancestor = id;
                int length = 0;
                while (ancestor > previousId)
                {
                    length++;
                    ancestor = GameplayTagManager.GetParentIndex(ancestor);
                }
                while (parent >= 0 && Entries[parent].Id != ancestor)
                    parent = Entries[parent].ExplicitIndex;

                // Reserve the new path segment, then fill it from leaf to root.
                // Direct parent links avoid constructing and traversing a hierarchy
                // span for every selected tag. The emitted order remains DFS.
                int leaf = next + length - 1;
                int pathId = id;
                for (int index = leaf; index >= next; index--)
                {
                    Entries[index] = new Entry { Id = pathId, ExplicitIndex = index == next ? parent : index - 1 };
                    pathId = GameplayTagManager.GetParentIndex(pathId);
                }
                next = leaf + 1;
                parent = leaf;
                previousId = id;
                Entries[parent].Count = 1;
                Entries[parent].ExplicitCount = 1;
                ExplicitIndices[i] = parent;
            }

            for (int i = next - 1; i >= 0; i--)
            {
                int parentIndex = Entries[i].ExplicitIndex;
                if (parentIndex >= 0)
                    Entries[parentIndex].Count += Entries[i].Count;
            }
            for (int i = 0; i < explicitCount; i++)
                Entries[ExplicitIndices[i]].ExplicitIndex = i;
            Count = next;
            ExplicitCount = explicitCount;
            BuildIndex();
        }

        // Only the live prefix contains IDs; the retained tail may contain stale
        // dense indices. Generic Array.Sort's range overload allocates a comparison
        // delegate on Unity Mono. Integer heapsort needs neither boxing nor scratch
        // storage, has bounded O(n log n) work, and skips already ordered input.
        private static void SortIds(int[] ids, int count)
        {
            int ordered = 1;
            while (ordered < count && ids[ordered - 1] <= ids[ordered])
                ordered++;
            if (ordered == count)
                return;

            for (int root = count / 2 - 1; root >= 0; root--)
                SiftDown(ids, ids[root], root, count);
            for (int end = count - 1; end > 0; end--)
            {
                int value = ids[end];
                ids[end] = ids[0];
                SiftDown(ids, value, 0, end);
            }
        }

        private static void SiftDown(int[] ids, int value, int root, int count)
        {
            // This condition also keeps 2 * root + 1 within the live prefix.
            while (root < count / 2)
            {
                int child = root * 2 + 1;
                if (child + 1 < count && ids[child] < ids[child + 1])
                    child++;
                if (value >= ids[child])
                    break;
                ids[root] = ids[child];
                root = child;
            }
            ids[root] = value;
        }

        private static int ArrayCapacityFor(int count)
        {
            int capacity = LinearCapacity;
            while (capacity < count)
                capacity *= 2;
            return capacity;
        }

        private static int BucketCapacityFor(int count)
        {
            if (count <= LinearCapacity)
                return 0;
            int capacity = 32;
            while (capacity < count * 2)
                capacity *= 2;
            return capacity;
        }

        private void EnsureOverwriteCapacity(int count, int explicitCount)
        {
            // All live contents will be replaced; Array.Resize would copy data
            // that the bulk operation immediately overwrites.
            if (Entries.Length < count)
                Entries = new Entry[ArrayCapacityFor(count)];
            if (ExplicitIndices.Length < explicitCount)
                ExplicitIndices = new int[ArrayCapacityFor(explicitCount)];
        }

        // Ignore old slots outside the active prefix. Expanding into them must
        // clear the new prefix before rebuilding; those retained slots may be stale.
        private void ResetIndex(int bucketCount)
        {
            if (bucketCount == 0)
            {
                m_BucketMask = 0;
                return;
            }
            if (m_Buckets.Length < bucketCount)
                m_Buckets = new int[bucketCount];
            else
                Array.Clear(m_Buckets, 0, bucketCount);
            m_BucketMask = bucketCount - 1;
        }

        private void BuildIndex()
        {
            int mask = m_BucketMask;
            if (mask == 0)
                return;
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
