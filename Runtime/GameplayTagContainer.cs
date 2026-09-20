using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GameplayTags
{
    /// <summary>
    /// Serializable authoring names, not the combat container. Convert once with ToRuntime.
    /// Existing m_GameplayTags serialization and Inspector drawers are preserved.
    /// </summary>
    [Serializable]
    public sealed class GameplayTagContainer : ISerializationCallbackReceiver, IEquatable<GameplayTagContainer>
    {
        [SerializeField] private List<GameplayTag> m_GameplayTags;
        public int Count => m_GameplayTags.Count;
        public bool IsEmpty => Count == 0;
        public int Capacity { get => m_GameplayTags.Capacity; set => m_GameplayTags.Capacity = value; }
        public GameplayTag this[int index] => m_GameplayTags[index];
        public GameplayTag GetTagAt(int index) => m_GameplayTags[index];
        public GameplayTagContainer() : this(0) { }
        public GameplayTagContainer(int capacity) { m_GameplayTags = new List<GameplayTag>(capacity); }
        public GameplayTagContainer(GameplayTag tag) : this(1) { AddTag(tag); }
        public GameplayTagContainer(GameplayTagContainer other) : this(other?.Count ?? 0) { CopyFrom(other); }
        public bool AddTag(GameplayTag tag)
        {
            if (!GameplayTagManager.IsRegistered(tag)) { GameplayTagManager.WarnUnregistered(tag.Name); return false; }
            return Insert(tag);
        }
        public bool AddTag(string name) => GameplayTagManager.TryRequestTag(name, out GameplayTag tag) && Insert(tag);
        private bool Insert(GameplayTag tag)
        {
            int index = m_GameplayTags.BinarySearch(tag);
            if (index >= 0) return false;
            m_GameplayTags.Insert(~index, tag);
            return true;
        }
        public bool RemoveTag(GameplayTag tag)
        {
            int index = m_GameplayTags.BinarySearch(tag);
            if (index < 0) return false;
            m_GameplayTags.RemoveAt(index);
            return true;
        }
        public bool HasTagExact(GameplayTag tag) => m_GameplayTags.BinarySearch(tag) >= 0;
        public void Clear() => m_GameplayTags.Clear();
        public void CopyFrom(GameplayTagContainer other)
        {
            if (ReferenceEquals(this, other)) return;
            Clear();
            if (other != null) m_GameplayTags.AddRange(other.m_GameplayTags);
        }
        public void AppendTags(GameplayTagContainer other)
        {
            if (other == null || ReferenceEquals(this, other)) return;
            for (int i = 0; i < other.Count; i++) Insert(other[i]);
        }
        /// <summary>Loading boundary. The result owns independent integer storage and a registry snapshot.</summary>
        public RuntimeTagSet ToRuntime(TagRegistry registry, int capacity = 0, TagSetStorage storage = TagSetStorage.Auto)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            var result = new RuntimeTagSet(registry, Math.Max(Count, capacity), storage);
            for (int i = 0; i < Count; i++) result.AddId(registry.Resolve(m_GameplayTags[i].Name).Id);
            return result;
        }
        public RuntimeTagSet ToRuntime() => ToRuntime(GameplayTagManager.CurrentRegistry);
        /// <summary>Optional authoring-name migration; failure leaves this definition unchanged.</summary>
        public void ResolveRegisteredTags()
        {
            if (Count == 0) return;
            TagRegistry registry = GameplayTagManager.CurrentRegistry;
            var resolved = new List<GameplayTag>(Capacity);
            for (int i = 0; i < Count; i++) resolved.Add(new GameplayTag(registry.Resolve(m_GameplayTags[i].Name).Name));
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
            for (int i = 0; i < Count; i++) if (this[i] != other[i]) return false;
            return true;
        }
        public override bool Equals(object obj) => Equals(obj as GameplayTagContainer);
        public override int GetHashCode()
        {
            unchecked { int hash = 17; for (int i = 0; i < Count; i++) hash = hash * 31 + this[i].GetHashCode(); return hash; }
        }
        public override string ToString()
        {
            var builder = new StringBuilder("{");
            for (int i = 0; i < Count; i++) { if (i > 0) builder.Append(", "); builder.Append(this[i].Name); }
            return builder.Append('}').ToString();
        }
        public List<GameplayTag>.Enumerator GetEnumerator() => m_GameplayTags.GetEnumerator();
    }
}
