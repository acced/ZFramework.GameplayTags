using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GameplayTags
{
    /// <summary>Immutable, registry-scoped runtime identity. Serialize GameplayTag names, never these indices.</summary>
    public readonly struct RuntimeTag : IEquatable<RuntimeTag>
    {
        internal readonly TagRegistry Owner;
        internal readonly int Id;
        internal RuntimeTag(TagRegistry owner, int id) { Owner = owner; Id = id; }
        public static RuntimeTag None => default;
        public bool IsValid => Owner != null;
        public TagRegistry Registry => Owner;
        public int RuntimeIndex => Owner == null ? -1 : Id;
        public string Name => Owner == null ? string.Empty : Owner.NameAt(Id);
        public RuntimeTag GetDirectParent() => Owner == null || Owner.Parents[Id] < 0
            ? default : new RuntimeTag(Owner, Owner.Parents[Id]);
        public bool MatchesTag(RuntimeTag parent)
        {
            if (Owner == null || parent.Owner == null) return false;
            Owner.RequireSame(parent.Owner);
            return Id >= parent.Id && Id < Owner.Ends[parent.Id];
        }
        public bool MatchesTagExact(RuntimeTag other) => Equals(other);
        public bool Equals(RuntimeTag other) => ReferenceEquals(Owner, other.Owner) && Id == other.Id;
        public override bool Equals(object obj) => obj is RuntimeTag other && Equals(other);
        public override int GetHashCode() => Owner == null ? 0 : unchecked(RuntimeHelpers.GetHashCode(Owner) * 397 ^ Id);
        public override string ToString() => Name;
        public static bool operator ==(RuntimeTag left, RuntimeTag right) => left.Equals(right);
        public static bool operator !=(RuntimeTag left, RuntimeTag right) => !left.Equals(right);
    }

    /// <summary>
    /// Immutable snapshot: compact DFS indices, canonical names and half-open subtree intervals.
    /// Building another snapshot never changes existing handles, sets or frozen queries.
    /// </summary>
    public sealed class TagRegistry
    {
        private readonly Dictionary<string, int> m_Indices;
        private readonly string[] m_Names;
        private readonly IReadOnlyList<GameplayTag> m_AuthoringTags;
        internal readonly int[] Parents;
        internal readonly int[] Ends;
        public int Count => m_Names.Length;
        internal int WordCount => (Count >> 6) + ((Count & 63) == 0 ? 0 : 1);
        internal IReadOnlyList<GameplayTag> AuthoringTags => m_AuthoringTags;

        private TagRegistry(Dictionary<string, int> indices, string[] names, int[] parents, int[] ends)
        {
            m_Indices = indices;
            m_Names = names;
            Parents = parents;
            Ends = ends;
            var tags = new GameplayTag[names.Length];
            for (int i = 0; i < tags.Length; i++) tags[i] = new GameplayTag(names[i]);
            Array.Sort(tags);
            m_AuthoringTags = Array.AsReadOnly(tags);
        }

        /// <summary>Main-thread loading boundary; validation completes before a snapshot is published.</summary>
        public static TagRegistry Create(GameplayTagSettings settings)
        {
            if (settings != null)
            {
                var errors = new List<string>();
                if (!settings.Validate(errors)) throw new GameplayTagRegistryException(errors);
            }
            var nodes = new Dictionary<string, BuildNode>(StringComparer.Ordinal);
            var root = new BuildNode(string.Empty, null) { Id = -1 };
            if (settings != null)
            {
                for (int i = 0; i < settings.Tags.Count; i++)
                {
                    string name = settings.Tags[i].Name;
                    BuildNode parent = root;
                    int offset = 0;
                    while (offset < name.Length)
                    {
                        int separator = name.IndexOf('.', offset);
                        string prefix = separator < 0 ? name : name.Substring(0, separator);
                        if (!nodes.TryGetValue(prefix, out BuildNode node))
                        {
                            node = new BuildNode(prefix, parent);
                            nodes.Add(prefix, node);
                            if (parent.Children == null) parent.Children = new List<BuildNode>();
                            parent.Children.Add(node);
                        }
                        parent = node;
                        if (separator < 0) break;
                        offset = separator + 1;
                    }
                }
            }
            var names = new string[nodes.Count];
            var parents = new int[nodes.Count];
            var ends = new int[nodes.Count];
            var indices = new Dictionary<string, int>(nodes.Count, StringComparer.Ordinal);
            var stack = new Stack<BuildNode>();
            PushChildren(root, stack);
            int cursor = 0;
            while (stack.Count != 0)
            {
                BuildNode node = stack.Pop();
                node.Id = cursor;
                names[cursor] = node.Name;
                parents[cursor] = node.Parent.Id;
                ends[cursor] = cursor + 1;
                indices.Add(node.Name, cursor++);
                PushChildren(node, stack);
            }
            for (int i = names.Length - 1; i >= 0; i--)
                if (parents[i] >= 0 && ends[parents[i]] < ends[i]) ends[parents[i]] = ends[i];

            if (settings != null && settings.Redirects.Count != 0)
            {
                var redirects = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int i = 0; i < settings.Redirects.Count; i++)
                    redirects.Add(settings.Redirects[i].OldName, settings.Redirects[i].NewName);
                var path = new List<string>();
                foreach (string source in redirects.Keys)
                {
                    path.Clear();
                    string current = source;
                    int id;
                    // Settings.Validate proved acyclicity and a final active target. Memoize whole paths once.
                    while (!indices.TryGetValue(current, out id))
                    {
                        path.Add(current);
                        current = redirects[current];
                    }
                    for (int i = 0; i < path.Count; i++) indices.Add(path[i], id);
                }
            }
            return new TagRegistry(indices, names, parents, ends);
        }

        public RuntimeTag Resolve(string name)
        {
            if (TryResolve(name, out RuntimeTag tag)) return tag;
            throw new KeyNotFoundException("Gameplay Tag is not registered: " + (name ?? "<null>"));
        }
        public bool TryResolve(string name, out RuntimeTag tag)
        {
            if (!string.IsNullOrEmpty(name) && m_Indices.TryGetValue(name.Trim(), out int id))
            { tag = new RuntimeTag(this, id); return true; }
            tag = default;
            return false;
        }
        public RuntimeTag GetTagAt(int runtimeIndex)
        {
            if ((uint)runtimeIndex >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(runtimeIndex));
            return new RuntimeTag(this, runtimeIndex);
        }
        public bool IsRegistered(string name) => name != null && m_Indices.TryGetValue(name, out int id)
            && string.Equals(name, m_Names[id], StringComparison.Ordinal);
        internal string NameAt(int id) => m_Names[id];
        internal void RequireSame(TagRegistry other)
        {
            if (!ReferenceEquals(this, other)) throw new ArgumentException("Runtime tags, sets and queries must belong to the same registry snapshot.");
        }
        private sealed class BuildNode
        {
            internal readonly string Name;
            internal readonly BuildNode Parent;
            internal List<BuildNode> Children;
            internal int Id;
            internal BuildNode(string name, BuildNode parent) { Name = name; Parent = parent; }
        }
        private static void PushChildren(BuildNode parent, Stack<BuildNode> stack)
        {
            if (parent.Children == null) return;
            parent.Children.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            for (int i = parent.Children.Count - 1; i >= 0; i--) stack.Push(parent.Children[i]);
        }
    }
}
