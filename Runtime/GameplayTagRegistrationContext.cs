using System;
using System.Collections.Generic;

namespace GameplayTags
{
    /// <summary>A declaration supplied to <see cref="GameplayTagManager.Initialize"/>.</summary>
    public readonly struct GameplayTagRegistration
    {
        public GameplayTagRegistration(string name, string description = null,
            GameplayTagFlags flags = GameplayTagFlags.None)
        {
            Name = name;
            Description = description;
            Flags = flags;
        }

        public string Name { get; }
        public string Description { get; }
        public GameplayTagFlags Flags { get; }
    }

    /// <summary>Startup-only mutable builder. Discarded after the registry is published.</summary>
    internal sealed class GameplayTagRegistrationContext
    {
        private readonly Node m_Root = new Node(null, null, null);
        private readonly Dictionary<string, Node> m_ByName = new Dictionary<string, Node>(StringComparer.Ordinal);

        internal void RegisterTag(string name, string description = null,
            GameplayTagFlags flags = GameplayTagFlags.None)
        {
            Node parent = m_Root;
            int start = 0;
            while (true)
            {
                int dot = name.IndexOf('.', start);
                int end = dot < 0 ? name.Length : dot;
                string prefix = dot < 0 ? name : name.Substring(0, end);
                if (!m_ByName.TryGetValue(prefix, out Node node))
                {
                    node = new Node(prefix, name.Substring(start, end - start), parent);
                    m_ByName.Add(prefix, node);
                    if (parent.Children == null)
                        parent.Children = new List<Node>();
                    parent.Children.Add(node);
                }

                parent = node;
                if (dot < 0)
                    break;
                start = dot + 1;
            }

            // Declarations are trusted input. ValidateName is an explicit authoring-time
            // operation, not repeated here or in lookups. Duplicate declarations merge
            // flags and select an ordinal description so assembly/input order is irrelevant.
            string text = description ?? string.Empty;
            if (!parent.IsDeclared || string.CompareOrdinal(text, parent.Description) < 0)
                parent.Description = text;
            parent.IsDeclared = true;
            parent.DeclaredFlags |= flags;
        }

        internal void Build(out GameplayTagDefinition[] definitions, out GameplayTag[] tags,
            out int[] parents, out int[] subtreeEnds, out int[] hierarchyIndices,
            out GameplayTag[] hierarchyTags, out Dictionary<string, int> indicesByName)
        {
            var order = new List<Node>(m_ByName.Count + 1) { m_Root };
            var pending = new Stack<Node>();
            PushChildren(m_Root, pending);
            while (pending.Count != 0)
            {
                Node node = pending.Pop();
                node.Index = order.Count;
                node.SubtreeEnd = node.Index + 1;
                order.Add(node);
                PushChildren(node, pending);
            }

            // Real DFS preorder, rather than sorting full names: "A.B" must stay
            // inside A's interval even in the presence of roots such as "A0".
            // Reverse preorder computes every subtree's exclusive upper bound.
            int pathLength = 0;
            for (int i = order.Count - 1; i > 0; --i)
            {
                Node node = order[i];
                node.AggregateFlags |= node.DeclaredFlags;
                node.Parent.AggregateFlags |= node.AggregateFlags;
                if (node.SubtreeEnd > node.Parent.SubtreeEnd)
                    node.Parent.SubtreeEnd = node.SubtreeEnd;
                if (node.Children == null)
                    pathLength += node.Depth;
            }

            hierarchyIndices = new int[pathLength];
            hierarchyTags = new GameplayTag[pathLength];
            int offset = 0;
            for (int i = 1; i < order.Count; ++i)
            {
                Node leaf = order[i];
                if (leaf.Children != null)
                    continue;

                // Store each root-to-leaf path once. All its previously unassigned
                // ancestors share prefixes of that path. A chain of N tags therefore
                // takes N slots, not N*(N+1)/2 independently copied ancestor slots.
                Node node = leaf;
                int position = offset + leaf.Depth;
                while (node != m_Root)
                {
                    hierarchyIndices[--position] = node.Index;
                    hierarchyTags[position] = new GameplayTag(node.Name, node.Index);
                    if (node.HierarchyOffset < 0)
                        node.HierarchyOffset = offset;
                    node = node.Parent;
                }
                offset += leaf.Depth;
            }

            int count = order.Count;
            definitions = new GameplayTagDefinition[count];
            tags = new GameplayTag[count];
            parents = new int[count];
            subtreeEnds = new int[count];
            indicesByName = new Dictionary<string, int>(count - 1, StringComparer.Ordinal);
            definitions[0] = GameplayTagDefinition.None;
            subtreeEnds[0] = 1; // None is not an ancestor of any registered tag.
            for (int i = 1; i < count; ++i)
            {
                Node node = order[i];
                GameplayTagFlags flags = node.IsDeclared ? node.DeclaredFlags : node.AggregateFlags;
                definitions[i] = new GameplayTagDefinition(node.Name, node.Description,
                    node.Label, flags, i, node.Parent.Index, node.SubtreeEnd,
                    node.Depth, node.HierarchyOffset);
                tags[i] = new GameplayTag(node.Name, i);
                parents[i] = node.Parent.Index;
                subtreeEnds[i] = node.SubtreeEnd;
                indicesByName.Add(node.Name, i);
            }
        }

        private static void PushChildren(Node node, Stack<Node> pending)
        {
            if (node.Children == null)
                return;
            node.Children.Sort(NodeLabelComparer.Instance);
            for (int i = node.Children.Count - 1; i >= 0; --i)
                pending.Push(node.Children[i]);
        }

        private sealed class NodeLabelComparer : IComparer<Node>
        {
            internal static readonly NodeLabelComparer Instance = new NodeLabelComparer();
            public int Compare(Node x, Node y) => string.CompareOrdinal(x.Label, y.Label);
        }

        private sealed class Node
        {
            internal Node(string name, string label, Node parent)
            {
                Name = name;
                Label = label;
                Parent = parent;
                Depth = parent == null ? 0 : parent.Depth + 1;
            }

            internal readonly string Name;
            internal readonly string Label;
            internal readonly Node Parent;
            internal readonly int Depth;
            internal List<Node> Children;
            internal string Description = string.Empty;
            internal GameplayTagFlags DeclaredFlags;
            internal GameplayTagFlags AggregateFlags;
            internal bool IsDeclared;
            internal int Index;
            internal int SubtreeEnd;
            internal int HierarchyOffset = -1;
        }
    }
}
