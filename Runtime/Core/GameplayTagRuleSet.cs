using System;
using System.Buffers;
using System.Collections.Generic;

namespace GameplayTags
{
    /// <summary>
    /// Immutable group of fixed rules. Queries each distinct condition once per evaluation.
    /// Share the group across actors; use one Cache per actor when state changes infrequently.
    /// Rebuild after registry initialization. No caller-supplied arrays are retained.
    /// </summary>
    public sealed class GameplayTagRuleSet
    {
        private struct Rule
        {
            internal int Start, Forbidden, Count;
            internal bool Impossible;
        }
        private readonly Rule[] m_Rules;
        private readonly int[] m_Ids;
        private readonly int[] m_Clauses;
        private readonly int[] m_DependencyOffsets;
        private readonly int[] m_Dependencies;
        public int Count => m_Rules.Length;
        public int DistinctConditionCount => m_Ids.Length;

        public GameplayTagRuleSet(ReadOnlySpan<GameplayTagRequirements> rules)
        {
            m_Rules = new Rule[rules.Length];
            var ids = new List<int>();
            var lookup = new Dictionary<int, int>();
            var clauses = new List<int>();
            var dependencies = new List<List<int>>();
            for (int i = 0; i < rules.Length; i++)
            {
                GameplayTagRequirements rule = rules[i];
                int count = rule.ForbiddenTagCount + rule.RequiredTagCount;
                m_Rules[i] = new Rule { Start = clauses.Count, Count = count,
                    Forbidden = rule.ForbiddenTagCount, Impossible = rule.IsImpossible };
                for (int j = 0; j < count; j++)
                {
                    int id = rule.GetId(j);
                    if (!lookup.TryGetValue(id, out int index))
                    {
                        index = ids.Count;
                        ids.Add(id); lookup.Add(id, index); dependencies.Add(new List<int>());
                    }
                    clauses.Add(index);
                    dependencies[index].Add(i);
                }
            }
            m_Ids = ids.ToArray();
            m_Clauses = clauses.ToArray();
            m_DependencyOffsets = new int[ids.Count + 1];
            var flat = new List<int>(clauses.Count);
            for (int i = 0; i < ids.Count; i++)
            { m_DependencyOffsets[i] = flat.Count; flat.AddRange(dependencies[i]); }
            m_DependencyOffsets[ids.Count] = flat.Count;
            m_Dependencies = flat.ToArray();
        }

        public void Evaluate<T>(in T holder, Span<bool> results) where T : IGameplayTagContainer =>
            EvaluateCore(holder.Indices.Storage, null, results);

        public void Evaluate<T, U>(in T first, in U second, Span<bool> results)
            where T : IGameplayTagContainer where U : IGameplayTagContainer =>
            EvaluateCore(first.Indices.Storage, second.Indices.Storage, results);

        private void EvaluateCore(TagStorage first, TagStorage second,
            Span<bool> results)
        {
            bool[] rented = m_Ids.Length > 128 ? ArrayPool<bool>.Shared.Rent(m_Ids.Length) : null;
            Span<bool> values = rented == null ? stackalloc bool[128] : rented;
            try
            {
                if (second == null)
                    for (int i = 0; i < m_Ids.Length; i++) values[i] = first.Contains(m_Ids[i]);
                else
                    for (int i = 0; i < m_Ids.Length; i++) values[i] = first.Contains(m_Ids[i]) || second.Contains(m_Ids[i]);
                for (int i = 0; i < Count; i++) results[i] = EvaluateRule(i, values);
            }
            finally { if (rented != null) ArrayPool<bool>.Shared.Return(rented); }
        }

        private bool EvaluateRule(int index, ReadOnlySpan<bool> values)
        {
            Rule rule = m_Rules[index];
            if (rule.Impossible) return false;
            int endForbidden = rule.Start + rule.Forbidden;
            for (int i = rule.Start; i < endForbidden; i++)
                if (values[m_Clauses[i]]) return false;
            for (int i = endForbidden; i < rule.Start + rule.Count; i++)
                if (!values[m_Clauses[i]]) return false;
            return true;
        }

        public Cache CreateCache() => new Cache(this);

        /// <summary>
        /// Single-owner evaluation state. No event subscriptions or manual invalidation.
        /// Unchanged presence versions reuse results. Otherwise distinct conditions are
        /// refreshed and only dependent rules are recomputed. Evaluate reads current state,
        /// including when invoked from a reentrant buff callback. Returned spans are live
        /// views invalidated by the next evaluation; do not use this cache concurrently.
        /// </summary>
        public sealed class Cache
        {
            private readonly GameplayTagRuleSet m_Owner;
            private readonly bool[] m_Values, m_Results, m_Dirty;
            private readonly int[] m_DirtyRules;
            private TagStorage m_First, m_Second;
            private ulong m_FirstVersion, m_SecondVersion;
            private bool m_Initialized;
            internal Cache(GameplayTagRuleSet owner)
            {
                m_Owner = owner;
                m_Values = new bool[owner.m_Ids.Length];
                m_Results = new bool[owner.Count];
                m_Dirty = new bool[owner.Count];
                m_DirtyRules = new int[owner.Count];
                for (int i = 0; i < owner.Count; i++) m_Results[i] = owner.EvaluateRule(i, m_Values);
            }

            public ReadOnlySpan<bool> Evaluate<T>(in T holder) where T : IGameplayTagContainer =>
                Refresh(holder.Indices.Storage, null);

            public ReadOnlySpan<bool> Evaluate<T, U>(in T first, in U second)
                where T : IGameplayTagContainer where U : IGameplayTagContainer =>
                Refresh(first.Indices.Storage, second.Indices.Storage);

            private ReadOnlySpan<bool> Refresh(TagStorage first, TagStorage second)
            {
                ulong fv = first.PresenceVersion, sv = second?.PresenceVersion ?? 0;
                if (m_Initialized && ReferenceEquals(first, m_First) && ReferenceEquals(second, m_Second) &&
                    fv == m_FirstVersion && sv == m_SecondVersion) return m_Results;
                int dirty = 0;
                for (int i = 0; i < m_Values.Length; i++)
                {
                    int id = m_Owner.m_Ids[i];
                    bool value = first.Contains(id) || (second != null && second.Contains(id));
                    if (value == m_Values[i]) continue;
                    m_Values[i] = value;
                    for (int j = m_Owner.m_DependencyOffsets[i]; j < m_Owner.m_DependencyOffsets[i + 1]; j++)
                    {
                        int rule = m_Owner.m_Dependencies[j];
                        if (m_Dirty[rule]) continue;
                        m_Dirty[rule] = true;
                        m_DirtyRules[dirty++] = rule;
                    }
                }
                for (int i = 0; i < dirty; i++)
                {
                    int rule = m_DirtyRules[i];
                    m_Results[rule] = m_Owner.EvaluateRule(rule, m_Values);
                    m_Dirty[rule] = false;
                }
                m_First = first; m_Second = second;
                m_FirstVersion = fv; m_SecondVersion = sv;
                m_Initialized = true;
                return m_Results;
            }
        }
    }
}
