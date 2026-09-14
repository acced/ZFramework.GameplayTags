using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GameplayTags
{
    /// <summary>
    /// 一组标签，按名称序数升序存放，所以去重、相等判定和 <see cref="ToString"/> 都是确定的。
    ///
    /// 层级查询二分定位后只比较相邻的一小段前缀，不预先展开祖先集合，
    /// 省下的是每次注册表变化后重建缓存的复杂度。
    /// </summary>
    [Serializable]
    public sealed class GameplayTagContainer : ISerializationCallbackReceiver, IEquatable<GameplayTagContainer>
    {
        [SerializeField] private List<GameplayTag> m_GameplayTags = new List<GameplayTag>();

        public int Count => m_GameplayTags.Count;
        public bool IsEmpty => m_GameplayTags.Count == 0;
        public GameplayTag this[int index] => m_GameplayTags[index];

        public GameplayTagContainer()
        {
        }

        public GameplayTagContainer(int capacity)
        {
            m_GameplayTags = new List<GameplayTag>(capacity);
        }

        public GameplayTagContainer(GameplayTag tag)
        {
            AddTag(tag);
        }

        public GameplayTagContainer(GameplayTagContainer other)
        {
            AppendTags(other);
        }

        public GameplayTag GetTagAt(int index) => m_GameplayTags[index];

        /// <summary>加入一个已注册的标签；未注册的会被拒绝，并按设置提醒一次。</summary>
        public bool AddTag(GameplayTag tag)
        {
            if (!GameplayTagManager.IsRegistered(tag))
            {
                GameplayTagManager.WarnUnregistered(tag.Name);
                return false;
            }

            int index = IndexOf(tag.Name);
            if (index >= 0)
                return false;

            m_GameplayTags.Insert(~index, tag);
            return true;
        }

        public bool AddTag(string name) =>
            GameplayTagManager.TryRequestTag(name, out GameplayTag tag) && AddTag(tag);

        public bool RemoveTag(GameplayTag tag)
        {
            int index = IndexOf(tag.Name);
            if (index < 0)
                return false;

            m_GameplayTags.RemoveAt(index);
            return true;
        }

        public bool RemoveTags(GameplayTagContainer other)
        {
            if (other == null || other.Count == 0)
                return false;

            if (ReferenceEquals(this, other))
            {
                bool hadTags = m_GameplayTags.Count > 0;
                Clear();
                return hadTags;
            }

            bool changed = false;
            for (int i = 0; i < other.Count; i++)
                changed |= RemoveTag(other[i]);
            return changed;
        }

        public void AppendTags(GameplayTagContainer other)
        {
            if (other == null || ReferenceEquals(this, other))
                return;

            for (int i = 0; i < other.Count; i++)
                AddTag(other[i]);
        }

        public void CopyFrom(GameplayTagContainer other)
        {
            if (ReferenceEquals(this, other))
                return;

            Clear();
            AppendTags(other);
        }

        public void Clear() => m_GameplayTags.Clear();

        /// <summary>容器里是否有标签等于 <paramref name="tag"/> 或落在它下面。</summary>
        public bool HasTag(GameplayTag tag)
        {
            string parent = tag.Name;
            if (parent.Length == 0)
                return false;

            int index = IndexOf(parent);
            if (index >= 0)
                return true;

            // 列表按序数序排列，后代都以 parent 为前缀因而严格大于它，只可能挨着插入点往后连成一段；
            // 一旦碰到不带这个前缀的名字，后面就不可能再出现后代了。
            for (int i = ~index; i < m_GameplayTags.Count; i++)
            {
                string name = m_GameplayTags[i].Name;
                if (!GameplayTagName.HasPrefix(name, parent))
                    return false;
                if (name[parent.Length] == '.')
                    return true;
            }
            return false;
        }

        public bool HasTagExact(GameplayTag tag) => IndexOf(tag.Name) >= 0;

        /// <summary>
        /// 找 <paramref name="name"/> 在有序列表中的位置，找不到时返回插入点的按位取反，语义同
        /// <see cref="List{T}.BinarySearch(T)"/>。手写是为了直接调 <see cref="string.CompareOrdinal(string,string)"/>，
        /// 绕开 Mono 上 Comparer&lt;GameplayTag&gt;.Default 的虚调用——这是容器最热的一条路径。
        /// </summary>
        private int IndexOf(string name)
        {
            int low = 0;
            int high = m_GameplayTags.Count - 1;
            while (low <= high)
            {
                int mid = low + ((high - low) >> 1);
                int order = string.CompareOrdinal(m_GameplayTags[mid].Name, name);
                if (order == 0)
                    return mid;
                if (order < 0)
                    low = mid + 1;
                else
                    high = mid - 1;
            }
            return ~low;
        }

        public bool HasAny(GameplayTagContainer other) => HasAny(other, false);
        public bool HasAnyExact(GameplayTagContainer other) => HasAny(other, true);
        public bool HasAll(GameplayTagContainer other) => HasAll(other, false);
        public bool HasAllExact(GameplayTagContainer other) => HasAll(other, true);

        public bool MatchesQuery(GameplayTagQuery query) => query != null && query.Matches(this);

        /// <summary>本容器中层级上命中 <paramref name="other"/> 任一标签的那些标签。</summary>
        public GameplayTagContainer Filter(GameplayTagContainer other) => Filter(other, false);

        public GameplayTagContainer FilterExact(GameplayTagContainer other) => Filter(other, true);

        public Enumerator GetEnumerator() => new Enumerator(this);

        public bool Equals(GameplayTagContainer other)
        {
            if (ReferenceEquals(this, other))
                return true;
            if (other == null || m_GameplayTags.Count != other.m_GameplayTags.Count)
                return false;

            for (int i = 0; i < m_GameplayTags.Count; i++)
            {
                if (!m_GameplayTags[i].Equals(other.m_GameplayTags[i]))
                    return false;
            }
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as GameplayTagContainer);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < m_GameplayTags.Count; i++)
                    hash = (hash * 31) + m_GameplayTags[i].GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            if (m_GameplayTags.Count == 0)
                return "{}";

            var builder = new StringBuilder("{");
            for (int i = 0; i < m_GameplayTags.Count; i++)
            {
                if (i > 0)
                    builder.Append(", ");
                builder.Append(m_GameplayTags[i].Name);
            }
            return builder.Append('}').ToString();
        }

        public static GameplayTagContainer Union(GameplayTagContainer lhs, GameplayTagContainer rhs)
        {
            var result = new GameplayTagContainer((lhs?.Count ?? 0) + (rhs?.Count ?? 0));
            result.AppendTags(lhs);
            result.AppendTags(rhs);
            return result;
        }

        public static GameplayTagContainer IntersectionExact(GameplayTagContainer lhs, GameplayTagContainer rhs)
        {
            var result = new GameplayTagContainer();
            if (lhs == null || rhs == null)
                return result;

            for (int i = 0; i < lhs.Count; i++)
            {
                if (rhs.HasTagExact(lhs[i]))
                    result.m_GameplayTags.Add(lhs[i]);
            }
            return result;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            // Inspector 可以塞进空条目、重复项和乱序，这里把升序去重的不变式恢复回来。
            m_GameplayTags ??= new List<GameplayTag>();
            m_GameplayTags.RemoveAll(tag => !tag.IsValid);
            m_GameplayTags.Sort();
            for (int i = m_GameplayTags.Count - 1; i > 0; i--)
            {
                if (m_GameplayTags[i].Equals(m_GameplayTags[i - 1]))
                    m_GameplayTags.RemoveAt(i);
            }
        }

        private bool HasAny(GameplayTagContainer other, bool exact)
        {
            if (other == null)
                return false;

            for (int i = 0; i < other.Count; i++)
            {
                if (exact ? HasTagExact(other[i]) : HasTag(other[i]))
                    return true;
            }
            return false;
        }

        private bool HasAll(GameplayTagContainer other, bool exact)
        {
            if (other == null)
                return true;

            for (int i = 0; i < other.Count; i++)
            {
                if (!(exact ? HasTagExact(other[i]) : HasTag(other[i])))
                    return false;
            }
            return true;
        }

        private GameplayTagContainer Filter(GameplayTagContainer other, bool exact)
        {
            var result = new GameplayTagContainer(m_GameplayTags.Count);
            if (other == null)
                return result;

            // 源列表本身有序，顺序追加即可保持不变式。
            for (int i = 0; i < m_GameplayTags.Count; i++)
            {
                if (MatchesAnyOf(m_GameplayTags[i], other, exact))
                    result.m_GameplayTags.Add(m_GameplayTags[i]);
            }
            return result;
        }

        /// <summary><paramref name="tag"/> 是否等于 <paramref name="other"/> 中某个标签或落在它下面。</summary>
        private static bool MatchesAnyOf(GameplayTag tag, GameplayTagContainer other, bool exact)
        {
            for (int i = 0; i < other.Count; i++)
            {
                if (exact ? tag.Equals(other[i]) : tag.MatchesTag(other[i]))
                    return true;
            }
            return false;
        }

        public struct Enumerator
        {
            private readonly GameplayTagContainer m_Container;
            private int m_Index;

            internal Enumerator(GameplayTagContainer container)
            {
                m_Container = container;
                m_Index = -1;
            }

            public GameplayTag Current => m_Container[m_Index];

            public bool MoveNext() => ++m_Index < m_Container.Count;
        }
    }
}
