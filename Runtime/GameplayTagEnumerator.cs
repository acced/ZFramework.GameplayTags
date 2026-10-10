using System.Collections;
using System.Collections.Generic;

namespace GameplayTags
{
    /// <summary>Allocation-free concrete enumeration. Mutating the source invalidates the enumerator.</summary>
    public struct GameplayTagEnumerator : IEnumerator<GameplayTag>, IEnumerable<GameplayTag>
    {
        private readonly TagStorage m_Storage;
        private readonly bool m_ExplicitOnly;
        private int m_Index;

        internal GameplayTagEnumerator(TagStorage storage, bool explicitOnly = false)
        {
            m_Storage = storage;
            m_ExplicitOnly = explicitOnly;
            m_Index = -1;
        }

        public readonly GameplayTag Current => GameplayTagManager.GetTagFromRuntimeIndex(
            m_Storage.Entries[m_ExplicitOnly ? m_Storage.ExplicitIndices[m_Index] : m_Index].Id);
        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (m_Storage == null)
                return false;
            return ++m_Index < (m_ExplicitOnly ? m_Storage.ExplicitCount : m_Storage.Count);
        }

        public void Reset() => m_Index = -1;
        public readonly void Dispose() { }
        public readonly GameplayTagEnumerator GetEnumerator() => this;
        readonly IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator() => this;
        readonly IEnumerator IEnumerable.GetEnumerator() => this;
    }
}
