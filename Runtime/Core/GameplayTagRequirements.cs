using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace GameplayTags
{
    /// <summary>
    /// An immutable runtime snapshot of required and forbidden tags. Up to four
    /// conditions are stored inline; additional conditions share one private integer
    /// array. Inputs must be resolved tags from the current registry. Rebuild these
    /// rules after registry initialization; runtime IDs must not be serialized.
    /// </summary>
    /// <remarks>
    /// Required tags use hierarchical matching and must all be present. Forbidden
    /// tags must all be absent. Copies share only immutable overflow data, never the
    /// input arrays. Default is an empty rule that matches every holder.
    /// </remarks>
    [DebuggerDisplay("Required = {RequiredTagCount}, Forbidden = {ForbiddenTagCount}")]
    public readonly struct GameplayTagRequirements
    {
        private readonly int m_ForbiddenCount;
        private readonly int m_RequiredCount;
        private readonly int m_Id0;
        private readonly int m_Id1;
        private readonly int m_Id2;
        private readonly int m_Id3;
        private readonly int[] m_Overflow;

        public int ForbiddenTagCount => m_ForbiddenCount;
        public int RequiredTagCount => Math.Max(0, m_RequiredCount);
        /// <summary>The required and forbidden conditions contradict each other.</summary>
        public bool IsImpossible => m_RequiredCount < 0;
        public bool IsEmpty => (m_ForbiddenCount | m_RequiredCount) == 0;

        /// <summary>
        /// Copy explicit conditions from tag spans. Empty spans mean no conditions.
        /// Inputs must be resolved tags from the current registry. Duplicate and implied
        /// conditions are removed. Getters expose effective conditions in runtime ID order.
        /// A contradictory rule has IsImpossible set and zero effective conditions.
        /// </summary>
        public GameplayTagRequirements(ReadOnlySpan<GameplayTag> forbidden,
            ReadOnlySpan<GameplayTag> required)
        {
            this = default;
            int count = forbidden.Length + required.Length;
            int[] rented = count > 128 ? ArrayPool<int>.Shared.Rent(count) : null;
            Span<int> scratch = rented == null ? stackalloc int[128] : rented;
            try
            {
                Span<int> f = scratch.Slice(0, forbidden.Length);
                Span<int> r = scratch.Slice(forbidden.Length, required.Length);
                for (int i = 0; i < f.Length; i++) f[i] = forbidden[i].RuntimeIndex;
                for (int i = 0; i < r.Length; i++) r[i] = required[i].RuntimeIndex;
                TagStorage.SortIds(f);
                TagStorage.SortIds(r);
                int nf = 0, nr = 0;
                for (int i = 0; i < f.Length; i++)
                    if (f[i] != 0 && (nf == 0 || !IsAncestor(f[nf - 1], f[i]))) f[nf++] = f[i];
                for (int i = 0; i < r.Length; i++)
                {
                    if (r[i] == 0) { m_RequiredCount = -1; return; }
                    if (i + 1 == r.Length || !IsAncestor(r[i], r[i + 1])) r[nr++] = r[i];
                }
                int fi = 0;
                for (int i = 0; i < nr; i++)
                {
                    while (fi < nf && GameplayTagManager.GetSubtreeEnd(f[fi]) <= r[i]) fi++;
                    if (fi < nf && IsAncestor(f[fi], r[i])) { m_RequiredCount = -1; return; }
                }
                m_ForbiddenCount = nf;
                m_RequiredCount = nr;
                f = f.Slice(0, nf); r = r.Slice(0, nr);
                count = nf + nr;
                m_Id0 = count > 0 ? ReadId(f, r, 0) : 0;
                m_Id1 = count > 1 ? ReadId(f, r, 1) : 0;
                m_Id2 = count > 2 ? ReadId(f, r, 2) : 0;
                m_Id3 = count > 3 ? ReadId(f, r, 3) : 0;
                m_Overflow = count > 4 ? new int[count - 4] : null;
                for (int i = 4; i < count; i++) m_Overflow[i - 4] = ReadId(f, r, i);
            }
            finally { if (rented != null) ArrayPool<int>.Shared.Return(rented); }
        }

        private GameplayTagRequirements(int forbidden, int required, int additionalRequired)
        {
            this = default;
            if (required == 0 || additionalRequired == 0 ||
                IsAncestor(forbidden, required) || IsAncestor(forbidden, additionalRequired))
            { m_RequiredCount = -1; return; }
            m_ForbiddenCount = forbidden == 0 ? 0 : 1;
            if (IsAncestor(required, additionalRequired)) required = additionalRequired;
            else if (!IsAncestor(additionalRequired, required))
            {
                m_RequiredCount = 2;
                if (required > additionalRequired)
                { int tmp = required; required = additionalRequired; additionalRequired = tmp; }
            }
            if (m_RequiredCount == 0) m_RequiredCount = 1;
            if (m_ForbiddenCount == 1)
            { m_Id0 = forbidden; m_Id1 = required; m_Id2 = m_RequiredCount == 2 ? additionalRequired : 0; }
            else
            { m_Id0 = required; m_Id1 = m_RequiredCount == 2 ? additionalRequired : 0; }
        }

        private static bool IsAncestor(int parent, int child) =>
            child >= parent && child < GameplayTagManager.GetSubtreeEnd(parent);

        private static int ReadId(ReadOnlySpan<int> f, ReadOnlySpan<int> r, int index) =>
            index < f.Length ? f[index] : r[index - f.Length];

        /// <summary>
        /// Build directly from tag spans, without temporary tag containers. Input
        /// arrays are not retained. Redundant conditions are removed once at construction.
        /// Empty spans mean no conditions.
        /// </summary>
        public static GameplayTagRequirements Create(ReadOnlySpan<GameplayTag> forbidden,
            ReadOnlySpan<GameplayTag> required) => new GameplayTagRequirements(forbidden, required);

        /// <summary>
        /// Allocation-free construction for one forbidden and two required tags.
        /// This overload needs neither temporary arrays nor temporary containers.
        /// </summary>
        public static GameplayTagRequirements Create(GameplayTag forbidden,
            GameplayTag required, GameplayTag additionalRequired) =>
            new GameplayTagRequirements(forbidden.RuntimeIndex, required.RuntimeIndex,
                additionalRequired.RuntimeIndex);

        /// <summary>Get an effective condition; index is local to the forbidden list.</summary>
        public GameplayTag GetForbiddenTag(int index)
        {
            return GameplayTagManager.GetTagFromRuntimeIndex(GetId(index));
        }

        /// <summary>Get an effective condition; index is local to the required list.</summary>
        public GameplayTag GetRequiredTag(int index)
        {
            return GameplayTagManager.GetTagFromRuntimeIndex(GetId(m_ForbiddenCount + index));
        }

        /// <summary>Check current holder state. Matching does not allocate or cache the result.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Matches<T>(in T container) where T : IGameplayTagContainer
        {
            if (m_RequiredCount <= 0 && m_ForbiddenCount == 0)
                return m_RequiredCount == 0;
            TagStorage storage = container.Indices.Storage;
            if (storage == null)
                return m_RequiredCount == 0;
            for (int i = 0; i < m_ForbiddenCount; i++)
                if (storage.Contains(GetId(i)))
                    return false;
            int end = m_ForbiddenCount + m_RequiredCount;
            for (int i = m_ForbiddenCount; i < end; i++)
                if (!storage.Contains(GetId(i)))
                    return false;
            return true;
        }

        /// <summary>
        /// Required tags may be split across holders; forbidden tags may occur in
        /// neither. Does not create a union or any temporary holder.
        /// </summary>
        public bool Matches<T, U>(in T staticContainer, in U dynamicContainer)
            where T : IGameplayTagContainer where U : IGameplayTagContainer
        {
            if (m_RequiredCount <= 0 && m_ForbiddenCount == 0)
                return m_RequiredCount == 0;
            TagStorage first = staticContainer.Indices.Storage;
            TagStorage second = dynamicContainer.Indices.Storage;
            for (int i = 0; i < m_ForbiddenCount; i++)
            {
                int id = GetId(i);
                if ((first != null && first.Contains(id)) || (second != null && second.Contains(id)))
                    return false;
            }
            int end = m_ForbiddenCount + m_RequiredCount;
            for (int i = m_ForbiddenCount; i < end; i++)
            {
                int id = GetId(i);
                if ((first == null || !first.Contains(id)) && (second == null || !second.Contains(id)))
                    return false;
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetId(int index)
        {
            switch (index)
            {
                case 0: return m_Id0;
                case 1: return m_Id1;
                case 2: return m_Id2;
                case 3: return m_Id3;
                default: return m_Overflow[index - 4];
            }
        }

    }
}
