using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GameplayTags
{
    /// <summary>
    /// Sorted unique name snapshots. Single owner; no concurrent mutation or mutation during enumeration.
    /// Resolve serialized names once at a loading boundary. Bulk operations do not re-query the registry.
    /// </summary>
    [Serializable]
    public sealed class GameplayTagContainer : ISerializationCallbackReceiver, IEquatable<GameplayTagContainer>
    {
        [SerializeField] private List<GameplayTag> m_GameplayTags;
        public int Count => m_GameplayTags.Count;
        public int Capacity { get => m_GameplayTags.Capacity; set => m_GameplayTags.Capacity = value; }
        public bool IsEmpty => Count == 0;
        public GameplayTag this[int index] => m_GameplayTags[index];
        public GameplayTagContainer() : this(0) { }
        public GameplayTagContainer(int capacity) { m_GameplayTags = new List<GameplayTag>(capacity); }
        public GameplayTagContainer(GameplayTag tag) : this(1) { AddTag(tag); }
        public GameplayTagContainer(GameplayTagContainer other) : this(other?.Count ?? 0) { CopyFrom(other); }
        public GameplayTag GetTagAt(int index) => m_GameplayTags[index];

        public bool AddTag(GameplayTag tag)
        {
            if (!GameplayTagManager.IsRegistered(tag))
            {
                GameplayTagManager.WarnUnregistered(tag.Name);
                return false;
            }
            return Insert(tag);
        }
        public bool AddTag(string name) => GameplayTagManager.TryRequestTag(name, out GameplayTag tag) && Insert(tag);
        private bool Insert(GameplayTag tag)
        {
            int index = IndexOf(tag.Name);
            if (index >= 0) return false;
            m_GameplayTags.Insert(~index, tag);
            return true;
        }
        public bool RemoveTag(GameplayTag tag)
        {
            int index = IndexOf(tag.Name);
            if (index < 0) return false;
            m_GameplayTags.RemoveAt(index);
            return true;
        }
        public void Clear() => m_GameplayTags.Clear();
        public void CopyFrom(GameplayTagContainer other)
        {
            if (ReferenceEquals(this, other)) return;
            Clear();
            if (other != null) m_GameplayTags.AddRange(other.m_GameplayTags);
        }

        public void AppendTags(GameplayTagContainer other)
        {
            if (other == null || other.Count == 0 || ReferenceEquals(this, other)) return;
            if (Count == 0) { CopyFrom(other); return; }
            int start = IndexOf(other[0].Name);
            if (start < 0) start = ~start;
            if (start == Count) { m_GameplayTags.AddRange(other.m_GameplayTags); return; }
            int oldCount = Count, i = start, j = 0, added = 0;
            while (i < oldCount && j < other.Count)
            {
                int order = m_GameplayTags[i].CompareTo(other[j]);
                if (order < 0) i++;
                else if (order > 0) { added++; j++; }
                else { i++; j++; }
            }
            added += other.Count - j;
            if (added == 0) return;
            int count = checked(oldCount + added);
            if (Capacity < count)
            {
                // Preserve amortized growth for repeated small batches, with only one resize for a large batch.
                int growth = Capacity <= int.MaxValue / 2 ? Capacity * 2 : count;
                Capacity = Math.Max(count, growth);
            }
            while (Count < count) m_GameplayTags.Add(default);
            i = oldCount - 1; j = other.Count - 1;
            int write = count - 1;
            while (i >= start && j >= 0)
            {
                int order = m_GameplayTags[i].CompareTo(other[j]);
                if (order > 0) m_GameplayTags[write--] = m_GameplayTags[i--];
                else if (order < 0) m_GameplayTags[write--] = other[j--];
                else { m_GameplayTags[write--] = m_GameplayTags[i--]; j--; }
            }
            while (j >= 0) m_GameplayTags[write--] = other[j--];
        }
        public bool RemoveTags(GameplayTagContainer other)
        {
            if (other == null || other.Count == 0 || Count == 0) return false;
            if (ReferenceEquals(this, other)) { Clear(); return true; }
            // Avoid a managed full-list compaction for tiny removals; List performs the native tail move.
            if (other.Count <= 2)
            {
                bool changed = false;
                for (int k = 0; k < other.Count; k++) changed |= RemoveTag(other[k]);
                return changed;
            }
            int start = IndexOf(other[0].Name);
            if (start < 0) start = ~start;
            int read = start, write = start, j = 0, count = Count;
            while (read < count && j < other.Count)
            {
                int order = m_GameplayTags[read].CompareTo(other[j]);
                if (order < 0) { if (write != read) m_GameplayTags[write] = m_GameplayTags[read]; write++; read++; }
                else if (order > 0) j++;
                else { read++; j++; }
            }
            if (write == read) return false;
            while (read < count) m_GameplayTags[write++] = m_GameplayTags[read++];
            m_GameplayTags.RemoveRange(write, count - write);
            return true;
        }

        public bool HasTag(GameplayTag tag)
        {
            string parent = tag.Name;
            if (parent.Length == 0) return false;
            int index = IndexOf(parent);
            if (index >= 0) return true;
            index = ~index;
            if (index == Count) return false;
            string name = m_GameplayTags[index].Name;
            if (!GameplayTagName.HasPrefix(name, parent)) return false;
            char next = name[parent.Length];
            if (next == '.') return true;
            if (next > '.') return false;
            // Punctuation such as A!x can precede A.B. Locate virtual parent+'.' without allocating it.
            int low = index + 1, high = Count;
            while (low < high)
            {
                int mid = low + ((high - low) >> 1);
                name = m_GameplayTags[mid].Name;
                int order = string.CompareOrdinal(name, 0, parent, 0, parent.Length);
                if (order == 0) order = name.Length == parent.Length ? -1 : name[parent.Length] - '.';
                if (order < 0) low = mid + 1; else high = mid;
            }
            return low < Count && m_GameplayTags[low].MatchesTag(tag);
        }
        // An empty set needs neither a name read nor a binary-search call.
        public bool HasTagExact(GameplayTag tag) => m_GameplayTags.Count != 0 && IndexOf(tag.Name) >= 0;
        private int IndexOf(string name)
        {
            int low = 0, high = Count - 1;
            while (low <= high)
            {
                int mid = low + ((high - low) >> 1);
                int order = string.CompareOrdinal(m_GameplayTags[mid].Name, name);
                if (order == 0) return mid;
                if (order < 0) low = mid + 1; else high = mid - 1;
            }
            return ~low;
        }
        public bool HasAny(GameplayTagContainer other) => HasAny(other, false);
        public bool HasAnyExact(GameplayTagContainer other) => HasAny(other, true);
        public bool HasAll(GameplayTagContainer other) => HasAll(other, false);
        public bool HasAllExact(GameplayTagContainer other) => HasAll(other, true);
        private bool HasAny(GameplayTagContainer other, bool exact)
        {
            if (other == null) return false;
            for (int i = 0; i < other.Count; i++) if (exact ? HasTagExact(other[i]) : HasTag(other[i])) return true;
            return false;
        }
        private bool HasAll(GameplayTagContainer other, bool exact)
        {
            if (other == null) return true;
            for (int i = 0; i < other.Count; i++) if (!(exact ? HasTagExact(other[i]) : HasTag(other[i]))) return false;
            return true;
        }
        public bool MatchesQuery(FrozenGameplayTagQuery query) => query != null && query.Matches(this);

        public GameplayTagContainer Filter(GameplayTagContainer other)
        { var result = new GameplayTagContainer(); FilterInto(other, result); return result; }
        public GameplayTagContainer FilterExact(GameplayTagContainer other)
        { var result = new GameplayTagContainer(); FilterExactInto(other, result); return result; }
        public static GameplayTagContainer Union(GameplayTagContainer left, GameplayTagContainer right)
        { var result = new GameplayTagContainer(); UnionInto(left, right, result); return result; }
        public static GameplayTagContainer IntersectionExact(GameplayTagContainer left, GameplayTagContainer right)
        { var result = new GameplayTagContainer(); IntersectionExactInto(left, right, result); return result; }

        /// <summary>Overwrites result. Either input can be result; sufficient actual-result capacity means zero allocation.</summary>
        public static void UnionInto(GameplayTagContainer left, GameplayTagContainer right, GameplayTagContainer result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (ReferenceEquals(result, right)) { result.AppendTags(left); return; }
            result.CopyFrom(left);
            result.AppendTags(right);
        }
        public void FilterExactInto(GameplayTagContainer other, GameplayTagContainer result) => IntersectionExactInto(this, other, result);
        /// <summary>Overwrites result. Both input aliases are supported.</summary>
        public static void IntersectionExactInto(GameplayTagContainer left, GameplayTagContainer right, GameplayTagContainer result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (left == null || right == null) { result.Clear(); return; }
            if (ReferenceEquals(left, right)) { result.CopyFrom(left); return; }
            bool alias = ReferenceEquals(result, left) || ReferenceEquals(result, right);
            if (!alias) result.Clear();
            int write = 0;
            GameplayTagContainer small = left.Count <= right.Count ? left : right;
            GameplayTagContainer large = ReferenceEquals(small, left) ? right : left;
            // Search the smaller set only when sizes are very asymmetric; never search a buffer being overwritten.
            if (small.Count < large.Count / 16 && !ReferenceEquals(result, large))
            {
                int count = small.Count;
                for (int i = 0; i < count; i++) if (large.HasTagExact(small[i])) Write(result, small[i], alias, ref write);
            }
            else
            {
                // Local list references and prefix bounds avoid repeated container indirection and scans.
                // Output still grows with actual matches; Into never reserves a worst-case input bound.
                List<GameplayTag> a = left.m_GameplayTags, b = right.m_GameplayTags, output = result.m_GameplayTags;
                int i = 0, j = 0, aCount = a.Count, bCount = b.Count;
                // Tiny inputs scan directly; binary positioning has its own fixed cost.
                if (aCount > 8 && bCount > 8)
                {
                    int order = a[0].CompareTo(b[0]);
                    if (order < 0) { i = left.IndexOf(b[0].Name); if (i < 0) i = ~i; }
                    else if (order > 0) { j = right.IndexOf(a[0].Name); if (j < 0) j = ~j; }
                }
                while (i < aCount && j < bCount)
                {
                    GameplayTag tag = a[i];
                    int order = tag.CompareTo(b[j]);
                    if (order < 0) i++;
                    else if (order > 0) j++;
                    else
                    {
                        if (alias) output[write] = tag; else output.Add(tag);
                        write++; i++; j++;
                    }
                }
            }
            if (alias) result.m_GameplayTags.RemoveRange(write, result.Count - write);
        }
        private static void Write(GameplayTagContainer target, GameplayTag tag, bool overwrite, ref int index)
        {
            if (overwrite) target.m_GameplayTags[index] = tag; else target.m_GameplayTags.Add(tag);
            index++;
        }
        /// <summary>Overwrites result. May overwrite this, but not a separate condition container.</summary>
        public void FilterInto(GameplayTagContainer other, GameplayTagContainer result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (ReferenceEquals(other, result) && !ReferenceEquals(this, result))
                throw new ArgumentException("A hierarchy filter cannot overwrite its separate condition container.", nameof(result));
            if (ReferenceEquals(this, other)) { result.CopyFrom(this); return; }
            if (other == null) { result.Clear(); return; }
            bool alias = ReferenceEquals(this, result);
            if (!alias) result.Clear();
            int count = Count, write = 0;
            for (int i = 0; i < count; i++)
            {
                GameplayTag tag = m_GameplayTags[i];
                for (int j = 0; j < other.Count; j++)
                {
                    if (!tag.MatchesTag(other[j])) continue;
                    Write(result, tag, alias, ref write);
                    break;
                }
            }
            if (alias) m_GameplayTags.RemoveRange(write, Count - write);
        }

        /// <summary>Loading boundary: resolve redirects atomically. Unknown names throw without altering this container.</summary>
        public void ResolveRegisteredTags()
        {
            if (Count == 0) return;
            var resolved = new List<GameplayTag>(Capacity);
            for (int i = 0; i < Count; i++) resolved.Add(GameplayTagManager.RequestTag(m_GameplayTags[i].Name));
            Normalize(resolved);
            m_GameplayTags = resolved;
        }
        void ISerializationCallbackReceiver.OnBeforeSerialize() { }
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            m_GameplayTags ??= new List<GameplayTag>();
            Normalize(m_GameplayTags);
        }
        private static void Normalize(List<GameplayTag> tags)
        {
            tags.Sort();
            int write = 0;
            for (int read = 0; read < tags.Count; read++)
            {
                GameplayTag tag = tags[read];
                if (!tag.IsValid || (write > 0 && tags[write - 1] == tag)) continue;
                tags[write++] = tag;
            }
            tags.RemoveRange(write, tags.Count - write);
        }
        public bool Equals(GameplayTagContainer other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null || Count != other.Count) return false;
            for (int i = 0; i < Count; i++) if (m_GameplayTags[i] != other[i]) return false;
            return true;
        }
        public override bool Equals(object obj) => Equals(obj as GameplayTagContainer);
        public override int GetHashCode()
        {
            unchecked { int hash = 17; for (int i = 0; i < Count; i++) hash = hash * 31 + m_GameplayTags[i].GetHashCode(); return hash; }
        }
        public override string ToString()
        {
            if (Count == 0) return "{}";
            var builder = new StringBuilder("{");
            for (int i = 0; i < Count; i++) { if (i > 0) builder.Append(", "); builder.Append(m_GameplayTags[i].Name); }
            return builder.Append('}').ToString();
        }
        public Enumerator GetEnumerator() => new Enumerator(this);
        public struct Enumerator
        {
            private readonly GameplayTagContainer m_Container;
            private int m_Index;
            internal Enumerator(GameplayTagContainer container) { m_Container = container; m_Index = -1; }
            public GameplayTag Current => m_Container[m_Index];
            public bool MoveNext() => ++m_Index < m_Container.Count;
        }
    }
}
