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
    /// <summary>Serializable names and source graph. Freeze once against a runtime registry snapshot.</summary>
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
        public FrozenGameplayTagQuery Freeze() => Freeze(GameplayTagManager.CurrentRegistry);
        public FrozenGameplayTagQuery Freeze(TagRegistry registry) => FrozenGameplayTagQuery.Create(m_RootExpression, UserDescription, registry);
        public static GameplayTagQuery MakeQuery(GameplayTagQueryExpression expression) => new GameplayTagQuery(expression);
        public static GameplayTagQuery MakeQueryMatchAnyTags(GameplayTagContainer tags) => new GameplayTagQuery(GameplayTagQueryExpression.AnyTagsMatch().AddTags(tags));
        public static GameplayTagQuery MakeQueryMatchAllTags(GameplayTagContainer tags) => new GameplayTagQuery(GameplayTagQueryExpression.AllTagsMatch().AddTags(tags));
        public static GameplayTagQuery MakeQueryMatchNoTagsMatch(GameplayTagContainer tags) => new GameplayTagQuery(GameplayTagQueryExpression.NoTagsMatch().AddTags(tags));
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
            if (!UsesTags) throw new InvalidOperationException("This expression does not accept tags.");
            if (!m_Tags.AddTag(tag) && !m_Tags.HasTagExact(tag)) throw new KeyNotFoundException("Query tag is not registered: " + tag.Name);
            return this;
        }
        public GameplayTagQueryExpression AddTags(GameplayTagContainer tags)
        {
            if (!UsesTags) throw new InvalidOperationException("This expression does not accept tags.");
            m_Tags.AppendTags(tags);
            return this;
        }
        public GameplayTagQueryExpression AddExpression(GameplayTagQueryExpression expression)
        {
            if (!UsesExpressions) throw new InvalidOperationException("This expression does not accept children.");
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            if (ReferenceEquals(this, expression)) throw new InvalidOperationException("A query expression cannot contain itself.");
            if (!m_Expressions.Contains(expression)) m_Expressions.Add(expression);
            return this;
        }
        void ISerializationCallbackReceiver.OnBeforeSerialize() { }
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
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
                for (int i = 0; i < m_Tags.Count && budget > 0; i++, budget--) builder.Append(i == 0 ? "" : ", ").Append(m_Tags[i].Name);
            else if (UsesExpressions && m_Expressions != null)
                for (int i = 0; i < m_Expressions.Count && budget > 0; i++)
                {
                    if (i != 0) builder.Append(", ");
                    if (m_Expressions[i] == null) { builder.Append("<null>"); budget--; }
                    else m_Expressions[i].Describe(builder, depth + 1, ref budget);
                }
            builder.Append(')');
        }
    }

    /// <summary>Immutable compact arrays of conditions/edges. Matches performs no string work or graph validation.</summary>
    public sealed class FrozenGameplayTagQuery
    {
        private readonly RuntimeNode[] m_Nodes;
        private readonly int[] m_Edges;
        private readonly TagRange[] m_Ranges;
        public TagRegistry Registry { get; }
        public string UserDescription { get; }
        public bool IsEmpty => m_Nodes.Length == 0;
        public int NodeCount => m_Nodes.Length;
        public int RangeCount => m_Ranges.Length;
        private FrozenGameplayTagQuery(TagRegistry registry, string description, RuntimeNode[] nodes, int[] edges, TagRange[] ranges)
        { Registry = registry; UserDescription = description; m_Nodes = nodes; m_Edges = edges; m_Ranges = ranges; }
        public bool Matches(RuntimeTagSet owned)
        {
            if (owned == null) return false;
            Registry.RequireSame(owned.Registry);
            return m_Nodes.Length != 0 && Evaluate(0, owned);
        }
        private bool Evaluate(int index, RuntimeTagSet owned)
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
                    for (int i = node.Offset; i < end; i++) if (!Evaluate(m_Edges[i], owned)) return false;
                    return true;
                default: // Builder emits only AnyExpressionsMatch / NoExpressionsMatch in this branch.
                    for (int i = node.Offset; i < end; i++) if (Evaluate(m_Edges[i], owned))
                        return node.Type == GameplayTagQueryExpressionType.AnyExpressionsMatch;
                    return node.Type == GameplayTagQueryExpressionType.NoExpressionsMatch;
            }
        }
        internal static FrozenGameplayTagQuery Create(GameplayTagQueryExpression source, string description, TagRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (source == null) return new FrozenGameplayTagQuery(registry, description, Array.Empty<RuntimeNode>(), Array.Empty<int>(), Array.Empty<TagRange>());
            return new Builder(registry).Finish(source, description);
        }
        private readonly struct RuntimeNode
        {
            internal readonly GameplayTagQueryExpressionType Type;
            internal readonly int Offset, Count;
            internal RuntimeNode(GameplayTagQueryExpressionType type, int offset, int count) { Type = type; Offset = offset; Count = count; }
        }
        private readonly struct TagRange : IComparable<TagRange>
        {
            internal readonly int Start, End;
            internal TagRange(int start, int end) { Start = start; End = end; }
            public int CompareTo(TagRange other) => Start.CompareTo(other.Start);
        }
        private sealed class Node
        {
            internal readonly GameplayTagQueryExpressionType Type;
            internal readonly TagRange[] Ranges;
            internal readonly Node[] Children;
            internal Node(GameplayTagQueryExpressionType type, TagRange[] ranges, Node[] children)
            { Type = type; Ranges = ranges; Children = children; }
            internal static readonly Node True = new Node(GameplayTagQueryExpressionType.AllTagsMatch, Array.Empty<TagRange>(), null);
            internal static readonly Node False = new Node(GameplayTagQueryExpressionType.AnyTagsMatch, Array.Empty<TagRange>(), null);
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
            private readonly TagRegistry m_Registry;
            private readonly Dictionary<GameplayTagQueryExpression, Built> m_Built = new Dictionary<GameplayTagQueryExpression, Built>();
            internal Builder(TagRegistry registry) { m_Registry = registry; }
            internal FrozenGameplayTagQuery Finish(GameplayTagQueryExpression root, string description)
            {
                Node node = Build(root, 1).Value;
                var nodes = new List<RuntimeNode>();
                var edges = new List<int>();
                var ranges = new List<TagRange>();
                Pack(node, new Dictionary<Node, int>(), nodes, edges, ranges);
                return new FrozenGameplayTagQuery(m_Registry, description, nodes.ToArray(), edges.ToArray(), ranges.ToArray());
            }
            private Built Build(GameplayTagQueryExpression source, int depth)
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
                Node node;
                if (source.UsesTags)
                {
                    if (source.Tags == null) throw new InvalidOperationException("Query has a null tag payload.");
                    if (source.Tags.Count >= MaxWork) throw new InvalidOperationException("Query exceeds its expanded work limit.");
                    work += source.Tags.Count;
                    var ranges = new TagRange[source.Tags.Count];
                    for (int i = 0; i < ranges.Length; i++)
                    {
                        int id = m_Registry.Resolve(source.Tags[i].Name).Id;
                        ranges[i] = new TagRange(id, m_Registry.Ends[id]);
                    }
                    node = ReduceRanges(source.Type, ranges);
                }
                else if (source.UsesExpressions)
                {
                    if (source.Expressions == null) throw new InvalidOperationException("Query has a null child payload.");
                    if (source.Expressions.Count >= MaxWork) throw new InvalidOperationException("Query exceeds its expanded work limit.");
                    var children = new Node[source.Expressions.Count];
                    // Every active branch is validated before any constant folding, including branches that would short-circuit.
                    for (int i = 0; i < children.Length; i++)
                    {
                        Built child = Build(source.Expressions[i], depth + 1);
                        work += child.Work;
                        if (work > MaxWork) throw new InvalidOperationException("Query exceeds 65536 expanded node/tag visits.");
                        height = Math.Max(height, child.Height + 1);
                        children[i] = child.Value;
                    }
                    node = ReduceChildren(source.Type, children);
                }
                else throw new InvalidOperationException("Query contains an undefined expression type.");
                var built = new Built(node, height, work);
                m_Built[source] = built;
                return built;
            }
            private static Node ReduceRanges(GameplayTagQueryExpressionType type, TagRange[] ranges)
            {
                if (ranges.Length == 0) return type == GameplayTagQueryExpressionType.AnyTagsMatch ? Node.False : Node.True;
                Array.Sort(ranges);
                bool all = type == GameplayTagQueryExpressionType.AllTagsMatch;
                int write = 1;
                for (int i = 1; i < ranges.Length; i++)
                {
                    TagRange previous = ranges[write - 1], current = ranges[i];
                    if (all && current.Start < previous.End) ranges[write - 1] = current;
                    else if (!all && current.Start <= previous.End)
                        ranges[write - 1] = new TagRange(previous.Start, Math.Max(previous.End, current.End));
                    else ranges[write++] = current;
                }
                if (write != ranges.Length) Array.Resize(ref ranges, write);
                return new Node(type, ranges, null);
            }
            private static Node ReduceChildren(GameplayTagQueryExpressionType type, Node[] children)
            {
                bool all = type == GameplayTagQueryExpressionType.AllExpressionsMatch;
                bool none = type == GameplayTagQueryExpressionType.NoExpressionsMatch;
                var result = new List<Node>(children.Length);
                for (int i = 0; i < children.Length; i++)
                {
                    Node child = children[i];
                    if (ReferenceEquals(child, Node.True)) { if (!all) return none ? Node.False : Node.True; continue; }
                    if (ReferenceEquals(child, Node.False)) { if (all) return Node.False; continue; }
                    if (!none && child.Type == type) result.AddRange(child.Children);
                    else result.Add(child);
                }
                if (result.Count == 0) return all || none ? Node.True : Node.False;
                if (!none && result.Count == 1) return result[0];
                return new Node(type, null, result.ToArray());
            }
            private static int Pack(Node node, Dictionary<Node, int> seen, List<RuntimeNode> nodes, List<int> edges, List<TagRange> ranges)
            {
                if (seen.TryGetValue(node, out int found)) return found;
                int index = nodes.Count;
                seen.Add(node, index);
                nodes.Add(default);
                if (node.Ranges != null)
                {
                    int offset = ranges.Count;
                    ranges.AddRange(node.Ranges);
                    nodes[index] = new RuntimeNode(node.Type, offset, node.Ranges.Length);
                }
                else
                {
                    int offset = edges.Count;
                    for (int i = 0; i < node.Children.Length; i++) edges.Add(0);
                    for (int i = 0; i < node.Children.Length; i++) edges[offset + i] = Pack(node.Children[i], seen, nodes, edges, ranges);
                    nodes[index] = new RuntimeNode(node.Type, offset, node.Children.Length);
                }
                return index;
            }
        }
    }
}
