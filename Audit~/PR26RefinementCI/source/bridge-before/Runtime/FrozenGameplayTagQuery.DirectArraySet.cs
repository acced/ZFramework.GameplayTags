namespace GameplayTags
{
    public sealed partial class FrozenGameplayTagQuery
    {
        // Called through DirectArraySet.Matches to keep Matches(null) source-compatible.
        internal bool MatchesDirect(Experiments.DirectArraySet owned)
        {
            if (owned == null) return false;
            Registry.RequireSame(owned.Registry);
            return m_Nodes.Length != 0 && EvaluateDirect(0, owned);
        }
        private bool EvaluateDirect(int index, Experiments.DirectArraySet owned)
        {
            RuntimeNode node = m_Nodes[index];
            int end = node.Offset + node.Count;
            switch (node.Type)
            {
                case GameplayTagQueryExpressionType.AllTagsMatch:
                    for (int i = node.Offset; i < end; i++) if (!owned.AnyInRange(m_Ranges[i].Start, m_Ranges[i].End)) return false;
                    return true;
                case GameplayTagQueryExpressionType.AnyTagsMatch:
                case GameplayTagQueryExpressionType.NoTagsMatch:
                    for (int i = node.Offset; i < end; i++) if (owned.AnyInRange(m_Ranges[i].Start, m_Ranges[i].End))
                        return node.Type == GameplayTagQueryExpressionType.AnyTagsMatch;
                    return node.Type == GameplayTagQueryExpressionType.NoTagsMatch;
                case GameplayTagQueryExpressionType.AllExpressionsMatch:
                    for (int i = node.Offset; i < end; i++) if (!EvaluateDirect(m_Edges[i], owned)) return false;
                    return true;
                default: // Builder emits only AnyExpressionsMatch / NoExpressionsMatch in this branch.
                    for (int i = node.Offset; i < end; i++) if (EvaluateDirect(m_Edges[i], owned))
                        return node.Type == GameplayTagQueryExpressionType.AnyExpressionsMatch;
                    return node.Type == GameplayTagQueryExpressionType.NoExpressionsMatch;
            }
        }
    }
}
