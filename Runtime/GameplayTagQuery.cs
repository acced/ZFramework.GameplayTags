using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GameplayTags
{
    public enum GameplayTagQueryExpressionType
    {
        Undefined = 0, AnyTagsMatch = 1, AllTagsMatch = 2, NoTagsMatch = 3,
        AnyExpressionsMatch = 4, AllExpressionsMatch = 5, NoExpressionsMatch = 6
    }
    /// <summary>Editable serialized source. Freeze once at the loading boundary, then share the immutable result.</summary>
    [Serializable]
    public sealed class GameplayTagQuery
    {
        [SerializeReference] private GameplayTagQueryExpression m_RootExpression;
        [SerializeField] private string m_UserDescription;
        public GameplayTagQueryExpression RootExpression => m_RootExpression;
        public string UserDescription => m_UserDescription ?? string.Empty;
        public bool IsEmpty => m_RootExpression == null;
        public GameplayTagQuery() { }
        public GameplayTagQuery(GameplayTagQueryExpression rootExpression) : this(rootExpression, string.Empty) { }
        public GameplayTagQuery(GameplayTagQueryExpression rootExpression, string userDescription)
        { m_RootExpression = rootExpression; m_UserDescription = userDescription; }
        public void SetRoot(GameplayTagQueryExpression rootExpression, string userDescription)
        { m_RootExpression = rootExpression; m_UserDescription = userDescription; }
        public void Clear() { m_RootExpression = null; m_UserDescription = string.Empty; }
        public FrozenGameplayTagQuery Freeze() => FrozenGameplayTagQuery.Create(m_RootExpression, UserDescription);
        public static GameplayTagQuery MakeQuery(GameplayTagQueryExpression expression) => new GameplayTagQuery(expression);
        public static GameplayTagQuery MakeQueryMatchAnyTags(GameplayTagContainer tags) => new GameplayTagQuery(GameplayTagQueryExpression.AnyTagsMatch().AddTags(tags));
        public static GameplayTagQuery MakeQueryMatchAllTags(GameplayTagContainer tags) => new GameplayTagQuery(GameplayTagQueryExpression.AllTagsMatch().AddTags(tags));
        public static GameplayTagQuery MakeQueryMatchNoTags(GameplayTagContainer tags) => new GameplayTagQuery(GameplayTagQueryExpression.NoTagsMatch().AddTags(tags));
        public override string ToString() => string.IsNullOrEmpty(m_UserDescription) ? m_RootExpression?.ToString() ?? "<empty>" : m_UserDescription;
    }

    [Serializable]
    public sealed class GameplayTagQueryExpression : ISerializationCallbackReceiver
    {
        [SerializeField] private GameplayTagQueryExpressionType m_Type = GameplayTagQueryExpressionType.AllTagsMatch;
        [SerializeField] private GameplayTagContainer m_Tags = new GameplayTagContainer();
        [SerializeReference] private List<GameplayTagQueryExpression> m_Expressions = new List<GameplayTagQueryExpression>();
        public GameplayTagQueryExpressionType Type => m_Type;
        public GameplayTagContainer Tags => m_Tags;
        public IReadOnlyList<GameplayTagQueryExpression> Expressions => m_Expressions;
        public bool UsesTags => m_Type >= GameplayTagQueryExpressionType.AnyTagsMatch && m_Type <= GameplayTagQueryExpressionType.NoTagsMatch;
        public bool UsesExpressions => m_Type >= GameplayTagQueryExpressionType.AnyExpressionsMatch && m_Type <= GameplayTagQueryExpressionType.NoExpressionsMatch;
        public GameplayTagQueryExpression() { }
        public GameplayTagQueryExpression(GameplayTagQueryExpressionType type) { m_Type = type; }
        public static GameplayTagQueryExpression AnyTagsMatch() => new GameplayTagQueryExpression(GameplayTagQueryExpressionType.AnyTagsMatch);
        public static GameplayTagQueryExpression AllTagsMatch() => new GameplayTagQueryExpression(GameplayTagQueryExpressionType.AllTagsMatch);
        public static GameplayTagQueryExpression NoTagsMatch() => new GameplayTagQueryExpression(GameplayTagQueryExpressionType.NoTagsMatch);
        public static GameplayTagQueryExpression AnyExpressionsMatch() => new GameplayTagQueryExpression(GameplayTagQueryExpressionType.AnyExpressionsMatch);
        public static GameplayTagQueryExpression AllExpressionsMatch() => new GameplayTagQueryExpression(GameplayTagQueryExpressionType.AllExpressionsMatch);
        public static GameplayTagQueryExpression NoExpressionsMatch() => new GameplayTagQueryExpression(GameplayTagQueryExpressionType.NoExpressionsMatch);
        public GameplayTagQueryExpression SetType(GameplayTagQueryExpressionType type) { m_Type = type; return this; }
        public GameplayTagQueryExpression AddTag(GameplayTag tag)
        {
            RequireTagType();
            if (!m_Tags.AddTag(tag) && !m_Tags.HasTagExact(tag)) throw new KeyNotFoundException("Query tag is not registered: " + tag.Name);
            return this;
        }
        public GameplayTagQueryExpression AddTags(GameplayTagContainer tags) { RequireTagType(); m_Tags.AppendTags(tags); return this; }
        public GameplayTagQueryExpression AddExpression(GameplayTagQueryExpression expression)
        {
            if (!UsesExpressions) throw new InvalidOperationException("This expression does not accept children.");
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            if (ReferenceEquals(this, expression)) throw new InvalidOperationException("A query expression cannot contain itself.");
            if (!m_Expressions.Contains(expression)) m_Expressions.Add(expression);
            return this;
        }
        private void RequireTagType() { if (!UsesTags) throw new InvalidOperationException("This expression does not accept tags."); }
        void ISerializationCallbackReceiver.OnBeforeSerialize() { }
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            // Keep invalid active payloads visible to Freeze rather than silently changing their meaning.
            if (m_Tags != null) ((ISerializationCallbackReceiver)m_Tags).OnAfterDeserialize();
        }
        public override string ToString()
        {
            var builder = new StringBuilder();
            int budget = 1024;
            Describe(builder, 0, ref budget);
            return builder.ToString();
        }
        private void Describe(StringBuilder builder, int depth, ref int budget)
        {
            if (depth >= 64 || budget-- <= 0) { builder.Append("<limit>"); return; }
            builder.Append(m_Type).Append('(');
            if (UsesTags && m_Tags != null)
            {
                for (int i = 0; i < m_Tags.Count && budget > 0; i++, budget--) builder.Append(i == 0 ? "" : ", ").Append(m_Tags[i].Name);
            }
            else if (UsesExpressions && m_Expressions != null)
            {
                for (int i = 0; i < m_Expressions.Count && budget > 0; i++)
                {
                    if (i != 0) builder.Append(", ");
                    if (m_Expressions[i] == null) { builder.Append("<null>"); budget--; }
                    else m_Expressions[i].Describe(builder, depth + 1, ref budget);
                }
            }
            builder.Append(')');
        }
    }

    /// <summary>Immutable name-based execution. No graph checks, allocations, caches or source revisions in Matches.</summary>
    public sealed class FrozenGameplayTagQuery
    {
        private readonly Node m_Root;
        public string UserDescription { get; }
        public bool IsEmpty => m_Root == null;
        private FrozenGameplayTagQuery(Node root, string description) { m_Root = root; UserDescription = description; }
        public bool Matches(GameplayTagContainer tags) => tags != null && m_Root != null && m_Root.Matches(tags);
        internal static FrozenGameplayTagQuery Create(GameplayTagQueryExpression root, string description) =>
            new FrozenGameplayTagQuery(root == null ? null : new Builder().Build(root, 1).Value, description);

        private sealed class Node
        {
            internal readonly GameplayTagQueryExpressionType Type;
            internal readonly GameplayTag[] Tags;
            internal readonly Node[] Children;
            internal static readonly Node True = new Node(GameplayTagQueryExpressionType.AllTagsMatch, Array.Empty<GameplayTag>(), null);
            internal static readonly Node False = new Node(GameplayTagQueryExpressionType.AnyTagsMatch, Array.Empty<GameplayTag>(), null);
            internal Node(GameplayTagQueryExpressionType type, GameplayTag[] tags, Node[] children) { Type = type; Tags = tags; Children = children; }
            internal bool Matches(GameplayTagContainer owned)
            {
                switch (Type)
                {
                    case GameplayTagQueryExpressionType.AllTagsMatch:
                        for (int i = 0; i < Tags.Length; i++) if (!owned.HasTag(Tags[i])) return false;
                        return true;
                    case GameplayTagQueryExpressionType.AnyTagsMatch:
                    case GameplayTagQueryExpressionType.NoTagsMatch:
                        for (int i = 0; i < Tags.Length; i++) if (owned.HasTag(Tags[i])) return Type == GameplayTagQueryExpressionType.AnyTagsMatch;
                        return Type == GameplayTagQueryExpressionType.NoTagsMatch;
                    case GameplayTagQueryExpressionType.AllExpressionsMatch:
                        for (int i = 0; i < Children.Length; i++) if (!Children[i].Matches(owned)) return false;
                        return true;
                    default: // Builder emits only AnyExpressionsMatch or NoExpressionsMatch here.
                        for (int i = 0; i < Children.Length; i++) if (Children[i].Matches(owned)) return Type == GameplayTagQueryExpressionType.AnyExpressionsMatch;
                        return Type == GameplayTagQueryExpressionType.NoExpressionsMatch;
                }
            }
        }
        private readonly struct Built
        {
            internal readonly Node Value;
            internal readonly int Height, Work;
            internal Built(Node value, int height, int work) { Value = value; Height = height; Work = work; }
        }
        private sealed class Builder
        {
            private const int MaxDepth = 64, MaxWork = 65536;
            private readonly Dictionary<GameplayTagQueryExpression, Built> m_Built = new Dictionary<GameplayTagQueryExpression, Built>();
            private readonly HashSet<GameplayTagQueryExpression> m_Visiting = new HashSet<GameplayTagQueryExpression>();
            internal Built Build(GameplayTagQueryExpression source, int depth)
            {
                if (source == null) throw new InvalidOperationException("Query contains a null expression.");
                if (depth > MaxDepth) throw new InvalidOperationException("Query exceeds 64 expression levels.");
                if (m_Built.TryGetValue(source, out Built prior))
                {
                    if (depth + prior.Height - 1 > MaxDepth) throw new InvalidOperationException("Shared query path exceeds 64 levels.");
                    return prior;
                }
                if (!m_Visiting.Add(source)) throw new InvalidOperationException("Query contains a cycle.");
                int work = 1, height = 1;
                Node result;
                if (source.UsesTags)
                {
                    if (source.Tags == null) throw new InvalidOperationException("Query has a null tag payload.");
                    if (source.Tags.Count >= MaxWork) throw new InvalidOperationException("Query exceeds its expanded work limit.");
                    work += source.Tags.Count;
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < source.Tags.Count; i++) names.Add(GameplayTagManager.RequestTag(source.Tags[i].Name).Name);
                    result = ReduceTags(source.Type, names);
                }
                else if (source.UsesExpressions)
                {
                    if (source.Expressions == null) throw new InvalidOperationException("Query has a null child payload.");
                    var children = new List<Node>(source.Expressions.Count);
                    // Visit EVERY active child before folding constants or simplifying. Invalid branches cannot hide behind short-circuiting.
                    for (int i = 0; i < source.Expressions.Count; i++)
                    {
                        Built child = Build(source.Expressions[i], depth + 1);
                        work += child.Work;
                        if (work > MaxWork) throw new InvalidOperationException("Query exceeds 65536 expanded node/tag visits.");
                        height = Math.Max(height, child.Height + 1);
                        children.Add(child.Value);
                    }
                    result = ReduceChildren(source.Type, children);
                }
                else throw new InvalidOperationException("Query contains an undefined expression type.");
                m_Visiting.Remove(source);
                var built = new Built(result, height, work);
                m_Built.Add(source, built);
                return built;
            }
            private static Node ReduceTags(GameplayTagQueryExpressionType type, HashSet<string> names)
            {
                if (names.Count == 0) return type == GameplayTagQueryExpressionType.AnyTagsMatch ? Node.False : Node.True;
                if (names.Count > 1)
                {
                    var redundant = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string name in names)
                    {
                        string parent = GameplayTagName.GetParent(name);
                        while (parent.Length > 0)
                        {
                            if (names.Contains(parent))
                            {
                                if (type == GameplayTagQueryExpressionType.AllTagsMatch) redundant.Add(parent);
                                else { redundant.Add(name); break; }
                            }
                            parent = GameplayTagName.GetParent(parent);
                        }
                    }
                    names.ExceptWith(redundant);
                }
                var tags = new GameplayTag[names.Count];
                int index = 0;
                foreach (string name in names) tags[index++] = new GameplayTag(name);
                Array.Sort(tags);
                return new Node(type, tags, null);
            }
            private static Node ReduceChildren(GameplayTagQueryExpressionType type, List<Node> children)
            {
                bool all = type == GameplayTagQueryExpressionType.AllExpressionsMatch;
                bool none = type == GameplayTagQueryExpressionType.NoExpressionsMatch;
                var reduced = new List<Node>();
                for (int i = 0; i < children.Count; i++)
                {
                    Node child = children[i];
                    if (ReferenceEquals(child, Node.True))
                    { if (!all) return none ? Node.False : Node.True; continue; }
                    if (ReferenceEquals(child, Node.False))
                    { if (all) return Node.False; continue; }
                    if (!none && child.Type == type) reduced.AddRange(child.Children);
                    else reduced.Add(child);
                }
                if (reduced.Count == 0) return all || none ? Node.True : Node.False;
                if (!none && reduced.Count == 1) return reduced[0];
                return new Node(type, null, reduced.ToArray());
            }
        }
    }
}
