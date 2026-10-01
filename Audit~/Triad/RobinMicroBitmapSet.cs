// Experimental small-set kernel. Not included in the Unity Runtime assembly.
// Independently implemented Robin Hood table of (16-bit interval key, 16-bit bitmap).
using System;
using System.Runtime.CompilerServices;

namespace GameplayTags.Experiments
{
    public sealed class RobinMicroBitmapSet
    {
        private uint[] slots;
        private uint inline;
        private int used, count, shift;
        public TagRegistry Registry { get; }
        public int Count => count;
        public BitmapLayout Layout => BitmapLayout.Micro;
        public int SlotCount => slots == null ? 1 : slots.Length;
        public int UsedRecords => used;
        public long BufferBytes => 4L + (slots == null ? 0 : 4L * slots.Length);
        private int MaxRecords => (Registry.Count + 15) >> 4;
        private int Limit => slots == null ? 1 : slots.Length - (slots.Length >> 2);
        public int ReservedMemberCapacity => Limit >= MaxRecords ? Registry.Count : Limit;

        public RobinMicroBitmapSet(TagRegistry registry, int capacity = 0, BitmapLayout layout = BitmapLayout.Auto)
        {
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            if (registry.Count > (1 << 20)) throw new ArgumentOutOfRangeException(nameof(registry));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (layout != BitmapLayout.Auto && layout != BitmapLayout.Micro)
                throw new ArgumentException("This isolated pilot is micro-only, not a Dense dispatcher.", nameof(layout));
            EnsureCapacity(capacity);
        }
        public RobinMicroBitmapSet(RobinMicroBitmapSet source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            Registry = source.Registry; used = source.used; count = source.count; shift = source.shift;
            inline = source.inline;
            // Copy table layout including empty slots. All allocation/copy work is timed.
            if (source.slots != null) slots = (uint[])source.slots.Clone();
        }
        private void Require(RobinMicroBitmapSet other)
        { if (other == null) throw new ArgumentNullException(nameof(other)); Registry.RequireSame(other.Registry); }
        private bool Accept(RuntimeTag tag)
        { if (tag.Owner == null) return false; Registry.RequireSame(tag.Owner); return true; }
        public void EnsureCapacity(int members)
        {
            if (members < 0) throw new ArgumentOutOfRangeException(nameof(members));
            ReserveRecords(Math.Min(members, MaxRecords));
        }
        private void ReserveRecords(int needed)
        {
            if (needed <= Limit) return;
            int size = 4, bits = 2;
            while (size - (size >> 2) < needed) { size <<= 1; bits++; }
            uint[] old = slots;
            slots = new uint[size]; shift = 32 - bits;
            if (old == null) { if (used != 0) PlaceUnique(inline); }
            else for (int i = 0; i < old.Length; i++) if (old[i] != 0) PlaceUnique(old[i]);
            inline = 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Home(uint key) => (int)(unchecked(key * 2654435769U) >> shift);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Find(uint key)
        {
            if (slots == null) return used != 0 && (inline >> 16) == key ? 0 : -1;
            int mask = slots.Length - 1, at = Home(key), distance = 0;
            while (true)
            {
                uint current = slots[at];
                if (current == 0) return -1;
                uint k = current >> 16;
                if (k == key) return at;
                if (((at - Home(k)) & mask) < distance) return -1;
                at = (at + 1) & mask; distance++;
            }
        }
        private void PlaceUnique(uint entry)
        {
            int mask = slots.Length - 1, at = Home(entry >> 16), distance = 0;
            while (true)
            {
                uint current = slots[at];
                if (current == 0) { slots[at] = entry; return; }
                int resident = (at - Home(current >> 16)) & mask;
                if (resident < distance) { slots[at] = entry; entry = current; distance = resident; }
                at = (at + 1) & mask; distance++;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasTagExact(RuntimeTag tag)
        {
            if (!ReferenceEquals(Registry, tag.Owner))
            { if (tag.Owner == null) return false; Registry.RequireSame(tag.Owner); }
            int at = Find((uint)tag.Id >> 4);
            return at >= 0 && (((slots == null ? inline : slots[at]) & (1U << (tag.Id & 15))) != 0);
        }
        public bool AddTag(RuntimeTag tag)
        {
            if (!Accept(tag)) return false;
            int before = count;
            AddRecord(((uint)tag.Id >> 4 << 16) | (1U << (tag.Id & 15)));
            return count != before;
        }
        private void AddRecord(uint record)
        {
            int at = Find(record >> 16);
            if (at >= 0)
            {
                uint old = slots == null ? inline : slots[at];
                count += Pop(record & ~old & 65535U);
                if (slots == null) inline = old | record; else slots[at] = old | record;
                return;
            }
            ReserveRecords(used + 1);
            if (slots == null) inline = record; else PlaceUnique(record);
            used++; count += Pop(record & 65535U);
        }
        private void EraseAt(int at)
        {
            used--;
            if (slots == null) { inline = 0; return; }
            int mask = slots.Length - 1, next = (at + 1) & mask;
            while (slots[next] != 0 && ((next - Home(slots[next] >> 16)) & mask) != 0)
            { slots[at] = slots[next]; at = next; next = (next + 1) & mask; }
            slots[at] = 0;
        }
        private void RemoveRecord(uint record)
        {
            int at = Find(record >> 16); if (at < 0) return;
            uint old = slots == null ? inline : slots[at];
            uint removed = old & record & 65535U;
            if (removed == 0) return;
            count -= Pop(removed); uint after = old & ~removed;
            if ((after & 65535U) == 0) EraseAt(at);
            else if (slots == null) inline = after; else slots[at] = after;
        }
        public bool RemoveTag(RuntimeTag tag)
        {
            if (!Accept(tag)) return false;
            int before = count;
            RemoveRecord(((uint)tag.Id >> 4 << 16) | (1U << (tag.Id & 15)));
            return count != before;
        }
        public void Clear()
        { if (slots != null && used != 0) Array.Clear(slots, 0, slots.Length); inline = 0; used = count = 0; }
        public void CopyFrom(RobinMicroBitmapSet source)
        { Require(source); if (!ReferenceEquals(this, source)) CopyCore(source); }
        private void CopyCore(RobinMicroBitmapSet source)
        {
            if (source.slots != null && slots != null && slots.Length == source.slots.Length)
            { Array.Copy(source.slots, slots, slots.Length); used = source.used; count = source.count; return; }
            Clear(); ReserveRecords(source.used);
            if (source.used == 0) return;
            if (source.slots == null)
            { if (slots == null) inline = source.inline; else PlaceUnique(source.inline); }
            else for (int i = 0; i < source.slots.Length; i++) if (source.slots[i] != 0) PlaceUnique(source.slots[i]);
            used = source.used; count = source.count;
        }
        private int NewRecords(RobinMicroBitmapSet other)
        {
            int additions = 0;
            if (other.slots == null) return other.used != 0 && Find(other.inline >> 16) < 0 ? 1 : 0;
            for (int i = 0; i < other.slots.Length; i++)
                if (other.slots[i] != 0 && Find(other.slots[i] >> 16) < 0) additions++;
            return additions;
        }
        public void AppendTags(RobinMicroBitmapSet other) { Require(other); AppendCore(other); }
        private void AppendCore(RobinMicroBitmapSet other)
        {
            if (ReferenceEquals(this, other) || other.used == 0) return;
            if (used == 0) { CopyCore(other); return; }
            // Do not allocate when actual result fits the caller's reserved capacity.
            if ((long)used + other.used > Limit) ReserveRecords(used + NewRecords(other));
            if (other.slots == null) AddRecord(other.inline);
            else for (int i = 0; i < other.slots.Length; i++) if (other.slots[i] != 0) AddRecord(other.slots[i]);
        }
        public void RemoveTags(RobinMicroBitmapSet other)
        {
            Require(other);
            if (ReferenceEquals(this, other)) { Clear(); return; }
            if (other.slots == null) { if (other.used != 0) RemoveRecord(other.inline); }
            else for (int i = 0; i < other.slots.Length; i++) if (other.slots[i] != 0) RemoveRecord(other.slots[i]);
        }
        public static RobinMicroBitmapSet Union(RobinMicroBitmapSet a, RobinMicroBitmapSet b, BitmapLayout layout = BitmapLayout.Auto)
        {
            if (a == null) throw new ArgumentNullException(nameof(a)); a.Require(b);
            var result = new RobinMicroBitmapSet(a.Registry, 0, layout);
            result.ReserveRecords(Math.Min(result.MaxRecords, a.used + b.used));
            result.CopyCore(a); result.AppendCore(b); return result;
        }
        public static void UnionInto(RobinMicroBitmapSet a, RobinMicroBitmapSet b, RobinMicroBitmapSet output)
        {
            if (a == null) throw new ArgumentNullException(nameof(a)); a.Require(b); a.Require(output);
            if (ReferenceEquals(output, a)) { output.AppendCore(b); return; }
            if (ReferenceEquals(output, b)) { output.AppendCore(a); return; }
            output.CopyCore(a); output.AppendCore(b);
        }
        public Enumerator GetEnumerator() => new Enumerator(this);
        // Allocation/slot order, NOT sorted DFS enumeration. Not production compatible.
        public struct Enumerator
        {
            private readonly RobinMicroBitmapSet set;
            private int slot, start, id;
            private uint pending;
            internal Enumerator(RobinMicroBitmapSet value) { set = value; slot = -1; start = id = 0; pending = 0; }
            public RuntimeTag Current => new RuntimeTag(set.Registry, id);
            public bool MoveNext()
            {
                while (pending == 0)
                {
                    uint record;
                    if (set.slots == null)
                    { if (++slot != 0 || set.used == 0) return false; record = set.inline; }
                    else
                    { if (++slot >= set.slots.Length) return false; record = set.slots[slot]; }
                    pending = record & 65535U; start = (int)(record >> 16) << 4;
                }
                int bit = 0; uint v = pending;
                while ((v & 1U) == 0) { v >>= 1; bit++; }
                pending &= pending - 1; id = start + bit; return true;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Pop(uint x)
        {
            unchecked { x -= (x >> 1) & 0x55555555U; x = (x & 0x33333333U) + ((x >> 2) & 0x33333333U);
                x = (x + (x >> 4)) & 0x0F0F0F0FU; return (int)((x * 0x01010101U) >> 24); }
        }
        public int ProbeCount(RuntimeTag tag)
        {
            if (!Accept(tag)) return 0;
            if (slots == null) return 1;
            uint key = (uint)tag.Id >> 4; int at = Home(key), mask = slots.Length - 1, d = 0;
            while (true)
            { uint e = slots[at]; if (e == 0 || (e >> 16) == key || ((at - Home(e >> 16)) & mask) < d) return d + 1; at = (at + 1) & mask; d++; }
        }
        public void AssertInvariants()
        {
            int members = 0, records = 0;
            if (used > Limit) throw new Exception("Overloaded Robin Hood table");
            int length = slots == null ? (used == 0 ? 0 : 1) : slots.Length;
            for (int i = 0; i < length; i++)
            {
                uint e = slots == null ? inline : slots[i]; if (e == 0) continue;
                if ((e & 65535U) == 0 || Find(e >> 16) != i) throw new Exception("Broken probe chain or duplicate key");
                int start = (int)(e >> 16) << 4, remaining = Registry.Count - start;
                if (remaining <= 0 || (remaining < 16 && ((e & 65535U) >> remaining) != 0)) throw new Exception("Registry tail");
                members += Pop(e & 65535U); records++;
            }
            if (members != count || records != used) throw new Exception("Incorrect Count/record count");
        }
    }
}
