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
            if (mask != 0)
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

        // Add a distinct explicit set to a normal set. Each new explicit tag
        // contributes to itself immediately, then queues only its direct parent.
        // Shared parents combine pending contributions before forwarding them.
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
                    for (int j = 0; j < path.Length; j++)
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

        internal void BuildFromExplicitIds(int explicitCount, bool alreadySorted = false)
        {
            if (explicitCount == 0)
            {
                Clear();
                return;
            }
            if (!alreadySorted)
                Int32Sort.Sort(ExplicitIndices, 0, explicitCount);

            // Fresh outputs size the live closure first to avoid geometric entry
            // allocations. A reused output streams into its retained buffer instead.
            if (Entries.Length == 0)
                EnsureOverwriteCapacity(CountSortedClosure(explicitCount), explicitCount);

            // Count temporarily stores the number of selected tags processed when
            // this subtree was entered. At exit, the prefix difference is exactly
            // its explicit contribution count. ExplicitIndex is the DFS parent link.
            int next = 0, parent = -1, depth = 0;
            Entry[] entries = Entries;
            for (int i = 0; i < explicitCount; i++)
            {
                int id = ExplicitIndices[i];
                while (parent >= 0 && id >= GameplayTagManager.GetSubtreeEnd(entries[parent].Id))
                {
                    entries[parent].Count = i - entries[parent].Count;
                    parent = entries[parent].ExplicitIndex;
                    depth--;
                }
                ReadOnlySpan<int> path = GameplayTagManager.GetHierarchyIndices(id);
                int needed = next + path.Length - depth;
                if (needed > entries.Length)
                    entries = GrowBuildEntries(needed, next);
                while (depth < path.Length)
                {
                    entries[next] = new Entry { Id = path[depth++], Count = i, ExplicitIndex = parent };
                    parent = next++;
                }
                entries[parent].ExplicitCount = 1;
                ExplicitIndices[i] = parent;
            }
            while (parent >= 0)
            {
                entries[parent].Count = explicitCount - entries[parent].Count;
                parent = entries[parent].ExplicitIndex;
            }
            for (int i = 0; i < explicitCount; i++)
                entries[ExplicitIndices[i]].ExplicitIndex = i;
            Count = next;
            ExplicitCount = explicitCount;
            ResetIndex(BucketCapacityFor(next));
            BuildIndex();
        }

        private int CountSortedClosure(int explicitCount)
        {
            int total = 0, depth = 0;
            ReadOnlySpan<int> previous = default;
            for (int i = 0; i < explicitCount; i++)
            {
                int id = ExplicitIndices[i];
                while (depth != 0 && id >= GameplayTagManager.GetSubtreeEnd(previous[depth - 1]))
                    depth--;
                ReadOnlySpan<int> path = GameplayTagManager.GetHierarchyIndices(id);
                total += path.Length - depth;
                depth = path.Length;
                previous = path;
            }
            return total;
        }

        // No old index is consulted while bulk-building. Preserve just the live
        // prefix on growth, not the old container's Count or stale hash contents.
        private Entry[] GrowBuildEntries(int capacity, int written)
        {
            var replacement = new Entry[ArrayCapacityFor(capacity)];
            if (written != 0)
                Array.Copy(Entries, replacement, written);
            Entries = replacement;
            return replacement;
        }

        internal void BuildFromUnsortedExplicitIds(int explicitCount, GameplayTagIntersectionWorkspace workspace)
        {
            if (explicitCount == 0)
            {
                Clear();
                return;
            }
            workspace.PrepareLookup();
            EnsureExplicitCapacity(explicitCount);
            int[] positions = workspace.Positions;
            int[] ids = workspace.SelectedIds;
            Entry[] entries = Entries;
            int next = 0;

            // Sparse-set validation makes leftover workspace positions harmless:
            // the position must point inside THIS build and match the requested ID.
            // An existing node already has a complete path to the root, so stop there.
            for (int i = 0; i < explicitCount; i++)
            {
                int first = next, child = -1;
                for (int id = ids[i]; id != 0; id = GameplayTagManager.GetParentIndex(id))
                {
                    int position = positions[id];
                    if ((uint)position < (uint)next && entries[position].Id == id)
                    {
                        if (child < 0)
                            first = position;
                        else
                        {
                            entries[child].ExplicitIndex = position;
                            entries[position].ExplicitCount++;
                        }
                        break;
                    }
                    if (next == entries.Length)
                        entries = GrowBuildEntries(next + 1, next);
                    int index = next++;
                    entries[index] = new Entry { Id = id, ExplicitIndex = -1 };
                    positions[id] = index;
                    if (child >= 0)
                    {
                        entries[child].ExplicitIndex = index;
                        entries[index].ExplicitCount = 1;
                    }
                    child = index;
                }
                // Selected IDs are unique, even for a counted input.
                entries[first].Count = 1;
                ExplicitIndices[i] = first;
            }

            // ExplicitCount temporarily counts unfinished direct children.
            // -1 marks a finished node. Each edge forwards its contribution once;
            // the last finished child can immediately finish its parent, without
            // a heap queue, recursion, sorting or a scan of the complete registry.
            for (int i = 0; i < next; i++)
            {
                int node = i;
                while (node >= 0 && entries[node].ExplicitCount == 0)
                {
                    int parent = entries[node].ExplicitIndex;
                    entries[node].ExplicitCount = -1;
                    if (parent >= 0)
                    {
                        entries[parent].Count += entries[node].Count;
                        entries[parent].ExplicitCount--;
                    }
                    node = parent;
                }
            }
            for (int i = 0; i < next; i++)
                entries[i].ExplicitCount = 0;
            for (int i = 0; i < explicitCount; i++)
            {
                int index = ExplicitIndices[i];
                entries[index].ExplicitCount = 1;
                entries[index].ExplicitIndex = i;
            }
            Count = next;
            ExplicitCount = explicitCount;
            ResetIndex(BucketCapacityFor(next));
            BuildIndex();
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

        // A normal same-storage union preserves dense contents, but factory-plus-
        // Copy compacts a sparse active index and clears logically empty storage.
        internal void PrepareSameStorageUnion()
        {
            if (ExplicitCount == 0 || Count == 0)
            {
                Clear();
                return;
            }
            int sourceBucketCount = m_BucketMask == 0 ? 0 : m_BucketMask + 1;
            if (sourceBucketCount > Math.Max(32, Count * 8))
            {
                ResetIndex(BucketCapacityFor(Count));
                BuildIndex();
            }
        }

        public TagStorage() { }

        // Keep reusable-output small unions on their exact hierarchy-order path
        // without entering the separate pool-bearing large-union method.
        internal void AddSmallUnionForOutput(TagStorage source)
        {
            for (int i = 0; i < source.ExplicitCount; i++)
            {
                int id = source.Entries[source.ExplicitIndices[i]].Id;
                if (!TryAddExplicit(id))
                    continue;
                ReadOnlySpan<int> path = GameplayTagManager.GetHierarchyIndices(id);
                for (int j = 0; j < path.Length; j++)
                    AddTotal(path[j]);
            }
        }

        // A healthy destination starts with only the explicit low bit. Bound
        // positive pending mass and dense indices before borrowing its high bits.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal bool TryAddUnionIntrusive(TagStorage source)
        {
            int explicitCount = source.ExplicitCount;
            if (explicitCount <= LinearCapacity || ReferenceEquals(this, source) ||
                !GameplayTagManager.IsInitialized)
                return false;
            int registeredCount = GameplayTagManager.TagCount;
            if (!HasIntrusiveUnionState(registeredCount, true) ||
                !source.HasIntrusiveUnionState(registeredCount, false))
                return false;
            long maximumCount = (long)Count + source.Count;
            if (maximumCount > int.MaxValue)
                return false;
            int pendingBits = 0, linkBits = 0;
            for (uint value = (uint)explicitCount; value != 0; value >>= 1)
                pendingBits++;
            for (uint value = (uint)maximumCount; value != 0; value >>= 1)
                linkBits++;
            if (pendingBits + linkBits > 31)
                return false;
            uint pendingMask = ((1u << pendingBits) - 1u) << 1;
            int linkShift = pendingBits + 1;
            bool completed = false;
            try
            {
                int head = 0, tail = 0, queued = 0;
                for (int i = 0; i < source.ExplicitCount; i++)
                {
                    int id = source.Entries[source.ExplicitIndices[i]].Id;
                    int index = Find(id);
                    if (index < 0)
                        index = Insert(id);
                    else if ((Entries[index].ExplicitCount & 1) != 0)
                        continue;
                    Entries[index].ExplicitCount |= 1;
                    AddExplicitIndex(index);
                    Entries[index].Count++;
                    int parent = GameplayTagManager.GetParentIndex(id);
                    if (parent != 0)
                        QueueIntrusiveContribution(parent, 1, pendingMask, linkShift, ref head, ref tail, ref queued);
                }

                while (queued != 0)
                {
                    int index = head - 1;
                    uint state = (uint)Entries[index].ExplicitCount;
                    head = (int)(state >> linkShift);
                    queued--;
                    if (queued == 0)
                        tail = 0;
                    int pending = (int)((state & pendingMask) >> 1);
                    Entries[index].ExplicitCount &= 1;
                    Entries[index].Count += pending;
                    int parent = GameplayTagManager.GetParentIndex(Entries[index].Id);
                    if (parent != 0)
                        QueueIntrusiveContribution(parent, pending, pendingMask, linkShift, ref head, ref tail, ref queued);
                }
                completed = true;
            }
            finally
            {
                // A successful drain already cleared every link. On ordinary
                // unwind, restore only the representation, retaining old pending
                // contributions and other partial mutations exactly as they stand.
                if (!completed)
                {
                    uint keepMask = pendingMask | 1u;
                    for (int i = 0; i < Count; i++)
                        Entries[i].ExplicitCount = (int)((uint)Entries[i].ExplicitCount & keepMask);
                }
            }
            return true;
        }

        private bool HasIntrusiveUnionState(int registeredCount, bool destination)
        {
            if (Entries == null || ExplicitIndices == null || m_Buckets == null ||
                Count <= 0 || Count > Entries.Length || ExplicitCount < 0 ||
                ExplicitCount > Count || ExplicitCount > ExplicitIndices.Length)
                return false;
            int mask = m_BucketMask;
            if (mask == 0)
            {
                if (Count > LinearCapacity)
                    return false;
            }
            else
            {
                long bucketCount = (long)mask + 1;
                if (mask < 0 || bucketCount > m_Buckets.Length ||
                    (bucketCount & (bucketCount - 1)) != 0 || bucketCount <= Count)
                    return false;
                int occupied = 0;
                for (int i = 0; i <= mask; i++)
                {
                    int encoded = m_Buckets[i];
                    if ((uint)encoded > (uint)Count)
                        return false;
                    if (encoded != 0)
                        occupied++;
                }
                if (occupied != Count)
                    return false;
            }
            for (int i = 0; i < Count; i++)
            {
                int id = Entries[i].Id;
                int explicitCount = Entries[i].ExplicitCount;
                if (id <= 0 || id > registeredCount || Find(id) != i ||
                    (destination && explicitCount != 0 && explicitCount != 1))
                    return false;
                if (explicitCount != 0)
                {
                    int slot = Entries[i].ExplicitIndex;
                    if ((uint)slot >= (uint)ExplicitCount || ExplicitIndices[slot] != i)
                        return false;
                }
                int parent = GameplayTagManager.GetParentIndex(id);
                if (parent != 0 && Find(parent) < 0)
                    return false;
            }
            for (int i = 0; i < ExplicitCount; i++)
            {
                int index = ExplicitIndices[i];
                if ((uint)index >= (uint)Count || Entries[index].ExplicitCount == 0 ||
                    Entries[index].ExplicitIndex != i)
                    return false;
            }
            return true;
        }

        private void QueueIntrusiveContribution(int id, int amount, uint pendingMask, int linkShift,
            ref int head, ref int tail, ref int queued)
        {
            int index = Find(id);
            if (index < 0)
                index = Insert(id);
            if (((uint)Entries[index].ExplicitCount & pendingMask) == 0)
            {
                int encoded = index + 1;
                if (tail == 0)
                    head = encoded;
                else
                {
                    int previous = tail - 1;
                    Entries[previous].ExplicitCount = unchecked((int)(
                        ((uint)Entries[previous].ExplicitCount & (pendingMask | 1u)) |
                        ((uint)encoded << linkShift)));
                }
                tail = encoded;
                queued++;
            }
            Entries[index].ExplicitCount = unchecked(Entries[index].ExplicitCount + (amount << 1));
        }

        // Before a normal seed copy, skip intermediate explicit buffers only
        // when the same conservative intrusive domain proves the final count.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void ReserveUnionExplicitCapacity(TagStorage seed, TagStorage source)
        {
            if (seed == null || source == null || ReferenceEquals(seed, source) ||
                ReferenceEquals(this, source) || Entries == null ||
                ExplicitIndices == null || m_Buckets == null)
                return;
            int seedExplicitCount = seed.ExplicitCount;
            int sourceExplicitCount = source.ExplicitCount;
            if (seedExplicitCount <= 0 || sourceExplicitCount <= LinearCapacity)
                return;
            long maximumExplicitCount = checked((long)seedExplicitCount + sourceExplicitCount);
            if (maximumExplicitCount > int.MaxValue)
                return;

            long copiedCapacity = ExplicitIndices.Length;
            int copyAllocations = 0;
            if (copiedCapacity < seedExplicitCount)
            {
                // Bound the rounding loop before doing any structural lookup.
                if (seedExplicitCount > (1 << 30))
                    return;
                copiedCapacity = LinearCapacity;
                while (copiedCapacity < seedExplicitCount)
                    copiedCapacity = checked(copiedCapacity * 2);
                copyAllocations = 1;
            }
            // Zero or one possible buffer cannot have an intermediate allocation
            // removed by reserving early. This also skips warmed retained outputs.
            if (maximumExplicitCount <= copiedCapacity ||
                (copyAllocations == 0 && maximumExplicitCount <=
                    Math.Max((long)LinearCapacity, checked(copiedCapacity * 2))))
                return;

            if (!GameplayTagManager.IsInitialized)
                return;
            int registeredCount = GameplayTagManager.TagCount;
            if (!seed.HasIntrusiveUnionState(registeredCount, true) ||
                !source.HasIntrusiveUnionState(registeredCount, false))
                return;
            long maximumCount = (long)seed.Count + source.Count;
            if (maximumCount > int.MaxValue)
                return;
            int pendingBits = 0, linkBits = 0;
            for (uint value = (uint)sourceExplicitCount; value != 0; value >>= 1)
                pendingBits++;
            for (uint value = (uint)maximumCount; value != 0; value >>= 1)
                linkBits++;
            if (pendingBits + linkBits > 31)
                return;

            long finalExplicitCount = seedExplicitCount;
            for (int i = 0; i < sourceExplicitCount; i++)
            {
                int id = source.Entries[source.ExplicitIndices[i]].Id;
                int index = seed.Find(id);
                if (index < 0 || seed.Entries[index].ExplicitCount == 0)
                    finalExplicitCount++;
            }
            long finalCapacity = copiedCapacity;
            int allocations = copyAllocations;
            while (finalCapacity < finalExplicitCount)
            {
                long nextCapacity = Math.Max((long)LinearCapacity, checked(finalCapacity * 2));
                if (nextCapacity > int.MaxValue)
                    return;
                finalCapacity = nextCapacity;
                allocations++;
            }
            if (allocations > 1)
                Array.Resize(ref ExplicitIndices, (int)finalCapacity);
        }

        // Bound transient stack scratch to 4 KiB. Larger unions keep the pooled
        // array path; this nonrecursive frame does not grow with hierarchy depth.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void AddUnionWithStackQueue(TagStorage source)
        {
            Span<int> queue = stackalloc int[ArrayCapacityFor(source.ExplicitCount)];
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
                    QueueStackContribution(parent, 1, queue, ref tail, ref queued);
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
                    QueueStackContribution(parent, pending, queue, ref tail, ref queued);
            }
        }

        private void QueueStackContribution(int id, int amount, Span<int> queue, ref int tail, ref int queued)
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
        internal bool CanReuseSelectedUnionSeed(TagStorage source)
        {
            // Bound CopyFrom's Count*8 and every possible union growth calculation.
            const int maximumCount = int.MaxValue / 8;
            if (Count <= 0 || ExplicitCount <= 0 || Count > maximumCount ||
                source.Count < 0 || source.Count > maximumCount - Count)
                return false;
            int registeredCount = GameplayTagManager.TagCount;
            return HasReusableUnionState(registeredCount) && source.HasReusableUnionState(registeredCount);
        }

        private bool HasReusableUnionState(int registeredCount)
        {
            if ((uint)Count > (uint)Entries.Length || (uint)ExplicitCount > (uint)ExplicitIndices.Length)
                return false;
            for (int i = 0; i < ExplicitCount; i++)
            {
                int index = ExplicitIndices[i];
                if ((uint)index >= (uint)Count || Entries[index].ExplicitCount != 1 || Entries[index].ExplicitIndex != i)
                    return false;
            }
            for (int i = 0; i < Count; i++)
            {
                Entry entry = Entries[i];
                if (entry.Id <= 0 || entry.Id > registeredCount || entry.Count <= 0 ||
                    (entry.ExplicitCount != 0 && entry.ExplicitCount != 1) || Find(entry.Id) != i)
                    return false;
                if (entry.ExplicitCount == 1 && ((uint)entry.ExplicitIndex >= (uint)ExplicitCount ||
                    ExplicitIndices[entry.ExplicitIndex] != i))
                    return false;
                int parent = GameplayTagManager.GetParentIndex(entry.Id);
                if (parent != 0 && Find(parent) < 0)
                    return false;
            }
            return true;
        }

        // IDs and FIFO coexist for at most 8 KiB of stack payload, plus frames.
        // This is bounded, nonrecursive scratch, not a caller-stack guarantee.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void SetUnionFromSelectedSeed(TagStorage seed)
        {
            int count = ExplicitCount;
            Span<int> ids = stackalloc int[count];
            for (int i = 0; i < count; i++)
                ids[i] = Entries[ExplicitIndices[i]].Id;
            CopyFrom(seed);
            if (count <= 8)
            {
                for (int i = 0; i < count; i++)
                {
                    int id = ids[i];
                    if (!TryAddExplicit(id))
                        continue;
                    ReadOnlySpan<int> path = GameplayTagManager.GetHierarchyIndices(id);
                    for (int j = 0; j < path.Length; j++)
                        AddTotal(path[j]);
                }
                return;
            }

            Span<int> queue = stackalloc int[ArrayCapacityFor(count)];
            int tail = 0, queued = 0;
            for (int i = 0; i < count; i++)
            {
                int id = ids[i];
                int index = Find(id);
                if (index < 0)
                    index = Insert(id);
                else if ((Entries[index].ExplicitCount & 1) != 0)
                    continue;
                Entries[index].ExplicitCount |= 1;
                AddExplicitIndex(index);
                Entries[index].Count++;
                int parent = GameplayTagManager.GetParentIndex(id);
                if (parent != 0)
                    QueueSnapshotContribution(parent, 1, queue, ref tail, ref queued);
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
                    QueueSnapshotContribution(parent, pending, queue, ref tail, ref queued);
            }
        }

        private void QueueSnapshotContribution(int id, int amount, Span<int> queue, ref int tail, ref int queued)
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

    }
}
