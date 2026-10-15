using System;
using System.Collections;
using System.Collections.Generic;

namespace GameplayTags
{
    /// <summary>Allocation-free concrete enumeration; mutation is detected on both access paths.</summary>
    public struct GameplayTagEnumerator : IEnumerator<GameplayTag>, IEnumerable<GameplayTag>
    {
        private readonly TagStorage m_Storage;
        private readonly bool m_ExplicitOnly;
#if DEBUG || GAMEPLAYTAGS_CHECKED_ENUMERATION
        private readonly long m_Version;
#endif
        private int m_Index;

        internal GameplayTagEnumerator(TagStorage storage, bool explicitOnly = false)
        {
            m_Storage = storage;
            m_ExplicitOnly = explicitOnly;
#if DEBUG || GAMEPLAYTAGS_CHECKED_ENUMERATION
            m_Version = storage == null ? 0 : storage.Version;
#endif
            m_Index = -1;
        }
        private readonly void CheckVersion()
        {
#if DEBUG || GAMEPLAYTAGS_CHECKED_ENUMERATION
            if (m_Storage != null && m_Storage.Version != m_Version)
                throw new InvalidOperationException("The container changed during enumeration.");
#endif
        }
        public readonly GameplayTag Current
        {
            get
            {
                CheckVersion();
#if DEBUG || GAMEPLAYTAGS_CHECKED_ENUMERATION
                int count = m_Storage == null ? 0 : m_ExplicitOnly ? m_Storage.ExplicitCount : m_Storage.Count;
                if ((uint)m_Index >= (uint)count) throw new InvalidOperationException("Enumerator is not on an element.");
#endif
                return GameplayTagManager.GetTagFromRuntimeIndex(
                    m_Storage.Entries[m_ExplicitOnly ? m_Storage.ExplicitIndices[m_Index] : m_Index].Id);
            }
        }
        readonly object IEnumerator.Current => Current;
        public bool MoveNext()
        {
            CheckVersion();
            if (m_Storage == null) return false;
            int count = m_ExplicitOnly ? m_Storage.ExplicitCount : m_Storage.Count;
            if (m_Index < count) m_Index++;
            return m_Index < count;
        }
        public void Reset() { CheckVersion(); m_Index = -1; }
        public readonly void Dispose() { }
        public readonly GameplayTagEnumerator GetEnumerator() => this;
        readonly IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator() => this;
        readonly IEnumerator IEnumerable.GetEnumerator() => this;
    }
}
