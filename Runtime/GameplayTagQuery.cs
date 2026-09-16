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
        internal static FrozenGameplayTagQuery Create(GameplayTagQueryExpression root, string description)
        {
            // A leaf has no active graph edges: resolve it without allocating traversal state.
            Node node = root == null ? null : root.UsesTags
                ? Builder.BuildTags(root)
                : new Builder().Build(root, 1).Value;
            return new FrozenGameplayTagQuery(node, description);
        }

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
            // A default Built means "on the current DFS path"; completed values always have a Node.
            private readonly Dictionary<GameplayTagQueryExpression, Built> m_Built = new Dictionary<GameplayTagQueryExpression, Built>();
            private static readonly Comparison<GameplayTag> HierarchyOrder = CompareHierarchy;

            internal Built Build(GameplayTagQueryExpression source, int depth)
            {
                if (source == null) throw new InvalidOperationException("Query contains a null expression.");
                if (depth > MaxDepth) throw new InvalidOperationException("Query exceeds 64 expression levels.");
                if (m_Built.TryGetValue(source, out Built prior))
                {
                    if (prior.Value == null) throw new InvalidOperationException("Query contains a cycle.");
                    if (depth + prior.Height - 1 > MaxDepth) throw new InvalidOperationException("Shared query path exceeds 64 levels.");
                    return prior;
                }
                m_Built.Add(source, default);
                int work = 1, height = 1;
                Node result;
                if (source.UsesTags)
                {
                    result = BuildTags(source);
                    work += source.Tags.Count;
                }
                else if (source.UsesExpressions)
                {
                    if (source.Expressions == null) throw new InvalidOperationException("Query has a null child payload.");
                    int count = source.Expressions.Count;
                    if (count >= MaxWork) throw new InvalidOperationException("Query exceeds its expanded work limit.");
                    // Singleton All/Any groups need validation, but no temporary child array.
                    if (count == 1 && source.Type != GameplayTagQueryExpressionType.NoExpressionsMatch)
                    {
                        Built child = Build(source.Expressions[0], depth + 1);
                        work += child.Work;
                        height = child.Height + 1;
                        result = child.Value;
                    }
                    else
                    {
                        var children = count == 0 ? Array.Empty<Node>() : new Node[count];
                        // Validate EVERY active child before folding constants. Short-circuiting cannot hide invalid input.
                        for (int i = 0; i < count; i++)
                        {
                            Built child = Build(source.Expressions[i], depth + 1);
                            work += child.Work;
                            if (work > MaxWork) throw new InvalidOperationException("Query exceeds 65536 expanded node/tag visits.");
                            height = Math.Max(height, child.Height + 1);
                            children[i] = child.Value;
                        }
                        result = ReduceChildren(source.Type, children);
                    }
                }
                else throw new InvalidOperationException("Query contains an undefined expression type.");
                if (work > MaxWork) throw new InvalidOperationException("Query exceeds 65536 expanded node/tag visits.");
                var built = new Built(result, height, work);
                m_Built[source] = built;
                return built;
            }

            internal static Node BuildTags(GameplayTagQueryExpression source)
            {
                if (source.Tags == null) throw new InvalidOperationException("Query has a null tag payload.");
                int count = source.Tags.Count;
                if (count >= MaxWork) throw new InvalidOperationException("Query exceeds its expanded work limit.");
                if (count == 0) return source.Type == GameplayTagQueryExpressionType.AnyTagsMatch ? Node.False : Node.True;

                var tags = new GameplayTag[count];
                for (int i = 0; i < count; i++)
                    tags[i] = GameplayTagManager.RequestTag(source.Tags[i].Name);
                if (count > 1)
                {
                    // Hierarchy order ('.' before other characters) makes each subtree contiguous,
                    // even for legal names such as A!x, A-x and A.B. No parent substrings or HashSets.
                    Array.Sort(tags, HierarchyOrder);
                    bool all = source.Type == GameplayTagQueryExpressionType.AllTagsMatch;
                    int write = 1;
                    for (int read = 1; read < count; read++)
                    {
                        GameplayTag tag = tags[read];
                        if (tag.MatchesTag(tags[write - 1]))
                        {
                            if (all) tags[write - 1] = tag; // All keeps the most specific requirement.
                        }
                        else tags[write++] = tag;
                    }
                    if (write != count) Array.Resize(ref tags, write);
                }
                return new Node(source.Type, tags, null);
            }

            private static int CompareHierarchy(GameplayTag left, GameplayTag right)
            {
                string a = left.Name, b = right.Name;
                int length = Math.Min(a.Length, b.Length);
                for (int i = 0; i < length; i++)
                {
                    if (a[i] == b[i]) continue;
                    if (a[i] == '.') return -1;
                    if (b[i] == '.') return 1;
                    return a[i] - b[i];
                }
                return a.Length.CompareTo(b.Length);
            }

            private static Node ReduceChildren(GameplayTagQueryExpressionType type, Node[] children)
            {
                bool all = type == GameplayTagQueryExpressionType.AllExpressionsMatch;
                bool none = type == GameplayTagQueryExpressionType.NoExpressionsMatch;
                int count = 0;
                Node single = null;
                bool unchanged = true;
                for (int i = 0; i < children.Length; i++)
                {
                    Node child = children[i];
                    if (ReferenceEquals(child, Node.True))
                    {
                        if (!all) return none ? Node.False : Node.True;
                        unchanged = false;
                        continue;
                    }
                    if (ReferenceEquals(child, Node.False))
                    {
                        if (all) return Node.False;
                        unchanged = false;
                        continue;
                    }
                    if (!none && child.Type == type)
                    {
                        count += child.Children.Length;
                        unchanged = false;
                    }
                    else { count++; single = child; }
                }
                if (count == 0) return all || none ? Node.True : Node.False;
                // Same-kind child groups are already reduced and have at least two children.
                if (!none && count == 1) return single;
                if (unchanged) return new Node(type, null, children);

                var reduced = new Node[count];
                int write = 0;
                for (int i = 0; i < children.Length; i++)
                {
                    Node child = children[i];
                    if (ReferenceEquals(child, Node.True) || ReferenceEquals(child, Node.False)) continue;
                    if (!none && child.Type == type)
                    {
                        Array.Copy(child.Children, 0, reduced, write, child.Children.Length);
                        write += child.Children.Length;
                    }
                    else reduced[write++] = child;
                }
                return new Node(type, null, reduced);
            }
        }
    }
}
