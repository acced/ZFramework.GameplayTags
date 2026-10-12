using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GameplayTags
{
    /// <summary>Allocation-free concrete enumeration. Mutating the source invalidates the enumerator.</summary>
    public struct GameplayTagEnumerator : IEnumerator<GameplayTag>, IEnumerable<GameplayTag>
    {
        private readonly TagStorage m_Storage;
        // High bit chooses the explicit index; low bits snapshot the valid
        // iteration bound. Mutating the source still invalidates this enumerator.
        private readonly int m_CountAndMode;
        private int m_Index;

        internal GameplayTagEnumerator(TagStorage storage, bool explicitOnly = false)
        {
            m_Storage = storage;
            int count = storage == null ? 0 : (explicitOnly ? storage.ExplicitCount : storage.Count);
            m_CountAndMode = explicitOnly ? count | int.MinValue : count;
            m_Index = -1;
        }

        public readonly GameplayTag Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GameplayTagManager.GetTagFromRuntimeIndex(
                m_Storage.Entries[m_CountAndMode < 0 ? m_Storage.ExplicitIndices[m_Index] : m_Index].Id);
        }
        readonly object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext() => ++m_Index < (m_CountAndMode & int.MaxValue);

        public void Reset() => m_Index = -1;
        public readonly void Dispose() { }
        public readonly GameplayTagEnumerator GetEnumerator() => this;
        readonly IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator() => this;
        readonly IEnumerator IEnumerable.GetEnumerator() => this;
    }
}
