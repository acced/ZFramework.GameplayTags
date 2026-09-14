using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameplayTags
{
    /// <summary>
    /// 标签的唯一数据源。运行期只读：增删改由 Editor 侧的 GameplayTagEditorUtility 负责。
    /// </summary>
    [CreateAssetMenu(fileName = "GameplayTagSettings", menuName = "ZFramework/Gameplay Tags/Settings")]
    public sealed class GameplayTagSettings : ScriptableObject
    {
        public const string ResourcesPath = "GameplayTags/GameplayTagSettings";
        public const string DefaultAssetPath = "Assets/Resources/GameplayTags/GameplayTagSettings.asset";

        private const uint HashOffset = 2166136261u;
        private const uint HashPrime = 16777619u;

        [SerializeField] private bool m_AutoInitialize = true;
        [SerializeField] private bool m_IncludeImplicitParentTagsInGeneratedCode = true;
        [SerializeField] private bool m_WarnOnInvalidSerializedTags = true;
        [SerializeField] private string m_GeneratedNamespace = "Game";
        [SerializeField] private string m_GeneratedClassName = "GameplayTags";
        [SerializeField] private string m_GeneratedCodePath = "Assets/GameScripts/Main/Generated/GameplayTags.gen.cs";
        [SerializeField] private int m_Revision;
        [SerializeField] private List<GameplayTagSource> m_Sources = new List<GameplayTagSource>();
        [SerializeField] private List<GameplayTagDefinition> m_Tags = new List<GameplayTagDefinition>();
        [SerializeField] private List<GameplayTagRedirect> m_Redirects = new List<GameplayTagRedirect>();

        public bool AutoInitialize { get { return m_AutoInitialize; } }
        public bool IncludeImplicitParentTagsInGeneratedCode { get { return m_IncludeImplicitParentTagsInGeneratedCode; } }
        public bool WarnOnInvalidSerializedTags { get { return m_WarnOnInvalidSerializedTags; } }
        public string GeneratedNamespace { get { return m_GeneratedNamespace ?? string.Empty; } }
        public string GeneratedClassName { get { return m_GeneratedClassName ?? string.Empty; } }
        public string GeneratedCodePath { get { return m_GeneratedCodePath ?? string.Empty; } }
        public int Revision { get { return m_Revision; } }
        public IReadOnlyList<GameplayTagSource> Sources { get { EnsureLists(); return m_Sources; } }
        public IReadOnlyList<GameplayTagDefinition> Tags { get { EnsureLists(); return m_Tags; } }
        public IReadOnlyList<GameplayTagRedirect> Redirects { get { EnsureLists(); return m_Redirects; } }

        public static GameplayTagSettings LoadDefault() => Resources.Load<GameplayTagSettings>(ResourcesPath);

        public bool TryGetTagDefinition(string name, out GameplayTagDefinition definition)
        {
            EnsureLists();
            for (int i = 0; i < m_Tags.Count; i++)
            {
                if (m_Tags[i] != null && string.CompareOrdinal(m_Tags[i].Name, name) == 0)
                {
                    definition = m_Tags[i];
                    return true;
                }
            }

            definition = null;
            return false;
        }

        public bool Validate(List<string> errors)
        {
            EnsureLists();
            return Validate(m_Tags, m_Redirects, m_Sources, errors);
        }

        /// <summary>
        /// 全量校验。注册表构建、构建前检查、以及 Editor 侧「改动落盘前先验一遍副本」都走这里。
        /// </summary>
        public static bool Validate(
            IReadOnlyList<GameplayTagDefinition> tags,
            IReadOnlyList<GameplayTagRedirect> redirects,
            IReadOnlyList<GameplayTagSource> sources,
            List<string> errors)
        {
            if (errors == null)
                throw new ArgumentNullException(nameof(errors));

            errors.Clear();

            var sourceNames = new HashSet<string>(StringComparer.Ordinal);
            var sourceNamesIgnoreCase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sources.Count; i++)
            {
                GameplayTagSource source = sources[i];
                if (source == null)
                {
                    errors.Add("Gameplay Tag source at index " + i + " is null.");
                    continue;
                }

                string sourceName = source.Name.Trim();
                if (sourceName.Length == 0)
                    errors.Add("Gameplay Tag source at index " + i + " is empty.");
                else if (!sourceNames.Add(sourceName))
                    errors.Add("Duplicate Gameplay Tag source: " + sourceName);
                else if (!sourceNamesIgnoreCase.Add(sourceName))
                    errors.Add("Gameplay Tag sources cannot differ only by letter casing: " + sourceName);
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            var namesIgnoreCase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < tags.Count; i++)
            {
                GameplayTagDefinition definition = tags[i];
                if (definition == null)
                {
                    errors.Add("Tag definition at index " + i + " is null.");
                    continue;
                }

                if (!GameplayTagName.TryNormalize(definition.Name, out string normalized, out string error))
                {
                    errors.Add("Invalid tag '" + definition.Name + "': " + error);
                    continue;
                }

                if (!names.Add(normalized))
                    errors.Add("Duplicate Gameplay Tag: " + normalized);
                if (!namesIgnoreCase.Add(normalized))
                    errors.Add("Gameplay Tags cannot differ only by letter casing: " + normalized);
                if (!string.IsNullOrEmpty(definition.Source) && !sourceNames.Contains(definition.Source))
                    errors.Add("Gameplay Tag references an unknown source: " + normalized + " -> " + definition.Source);
            }

            ValidateRestrictedTags(tags, names, errors);
            ValidateRedirects(redirects, CollectResolvableNames(names), errors);
            return errors.Count == 0;
        }

        /// <summary>配置内容的稳定哈希，用于判断生成代码是否过期。跨进程必须稳定，所以不能用 string.GetHashCode。</summary>
        public uint ComputeContentHash()
        {
            EnsureLists();
            uint hash = 0u;

            var orderedTags = new List<GameplayTagDefinition>(m_Tags);
            orderedTags.Sort(CompareDefinitions);
            for (int i = 0; i < orderedTags.Count; i++)
            {
                GameplayTagDefinition definition = orderedTags[i];
                if (definition == null)
                    continue;

                hash = AppendHash(hash, definition.Name);
                hash = AppendHash(hash, definition.DevComment);
                hash = AppendHash(hash, definition.Source);
                hash = AppendHash(hash, definition.Restricted ? "1" : "0");
                hash = AppendHash(hash, definition.AllowNonRestrictedChildren ? "1" : "0");
            }

            var orderedRedirects = new List<GameplayTagRedirect>(m_Redirects);
            orderedRedirects.Sort(CompareRedirects);
            for (int i = 0; i < orderedRedirects.Count; i++)
            {
                GameplayTagRedirect redirect = orderedRedirects[i];
                if (redirect == null)
                    continue;

                hash = AppendHash(hash, redirect.OldName);
                hash = AppendHash(hash, redirect.NewName);
            }

            return hash == 0u ? 1u : hash;
        }

        internal List<GameplayTagDefinition> TagsInternal { get { EnsureLists(); return m_Tags; } }
        internal List<GameplayTagRedirect> RedirectsInternal { get { EnsureLists(); return m_Redirects; } }
        internal List<GameplayTagSource> SourcesInternal { get { EnsureLists(); return m_Sources; } }

        /// <summary>重新排序并推进修订号，Editor 侧每次改动后调用。</summary>
        internal void Touch()
        {
            EnsureLists();
            m_Tags.Sort(CompareDefinitions);
            m_Redirects.Sort(CompareRedirects);
            m_Sources.Sort(CompareSources);

            unchecked
            {
                m_Revision++;
                if (m_Revision == 0)
                    m_Revision = 1;
            }
        }

        internal void SetEditorOptions(
            bool autoInitialize,
            bool includeImplicitParentTagsInGeneratedCode,
            bool warnOnInvalidSerializedTags,
            string generatedNamespace,
            string generatedClassName,
            string generatedCodePath)
        {
            m_AutoInitialize = autoInitialize;
            m_IncludeImplicitParentTagsInGeneratedCode = includeImplicitParentTagsInGeneratedCode;
            m_WarnOnInvalidSerializedTags = warnOnInvalidSerializedTags;
            m_GeneratedNamespace = generatedNamespace;
            m_GeneratedClassName = generatedClassName;
            m_GeneratedCodePath = generatedCodePath;
            Touch();
        }

        internal void ReplaceAll(
            List<GameplayTagDefinition> tags,
            List<GameplayTagRedirect> redirects,
            List<GameplayTagSource> sources)
        {
            m_Tags = tags ?? new List<GameplayTagDefinition>();
            m_Redirects = redirects ?? new List<GameplayTagRedirect>();
            m_Sources = sources ?? new List<GameplayTagSource>();
            Touch();
        }

        /// <summary>显式定义的名字加上它们全部的隐式父级，也就是注册表最终会包含的名字。</summary>
        internal static HashSet<string> CollectResolvableNames(HashSet<string> definedNames)
        {
            var resolvable = new HashSet<string>(definedNames, StringComparer.Ordinal);
            foreach (string name in definedNames)
            {
                string parent = GameplayTagName.GetParent(name);
                while (!string.IsNullOrEmpty(parent) && resolvable.Add(parent))
                    parent = GameplayTagName.GetParent(parent);
            }
            return resolvable;
        }

        private static void ValidateRestrictedTags(
            IReadOnlyList<GameplayTagDefinition> tags,
            HashSet<string> names,
            List<string> errors)
        {
            var definitions = new Dictionary<string, GameplayTagDefinition>(StringComparer.Ordinal);
            for (int i = 0; i < tags.Count; i++)
            {
                GameplayTagDefinition definition = tags[i];
                if (definition != null && !definitions.ContainsKey(definition.Name))
                    definitions.Add(definition.Name, definition);
            }

            foreach (string name in names)
            {
                if (!definitions.TryGetValue(name, out GameplayTagDefinition definition) || definition.Restricted)
                    continue;

                string parent = GameplayTagName.GetParent(name);
                while (!string.IsNullOrEmpty(parent))
                {
                    if (definitions.TryGetValue(parent, out GameplayTagDefinition parentDefinition) &&
                        parentDefinition.Restricted &&
                        !parentDefinition.AllowNonRestrictedChildren)
                    {
                        errors.Add(
                            "Non-restricted tag '" + name +
                            "' cannot be added below restricted tag '" + parent + "'.");
                        break;
                    }

                    parent = GameplayTagName.GetParent(parent);
                }
            }
        }

        private static void ValidateRedirects(
            IReadOnlyList<GameplayTagRedirect> configured,
            HashSet<string> resolvableNames,
            List<string> errors)
        {
            var redirects = new Dictionary<string, string>(StringComparer.Ordinal);
            var sourcesIgnoreCase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < configured.Count; i++)
            {
                GameplayTagRedirect redirect = configured[i];
                if (redirect == null)
                {
                    errors.Add("Redirect at index " + i + " is null.");
                    continue;
                }

                if (!GameplayTagName.TryNormalize(redirect.OldName, out string oldName, out string error))
                {
                    errors.Add("Invalid redirect source '" + redirect.OldName + "': " + error);
                    continue;
                }
                if (!GameplayTagName.TryNormalize(redirect.NewName, out string newName, out error))
                {
                    errors.Add("Invalid redirect target '" + redirect.NewName + "': " + error);
                    continue;
                }
                if (string.CompareOrdinal(oldName, newName) == 0)
                    errors.Add("Redirect cannot target itself: " + oldName);
                if (resolvableNames.Contains(oldName))
                    errors.Add("Redirect source is still an active Gameplay Tag: " + oldName);
                if (!sourcesIgnoreCase.Add(oldName))
                    errors.Add("Redirect sources cannot differ only by letter casing: " + oldName);
                if (redirects.ContainsKey(oldName))
                    errors.Add("Duplicate redirect source: " + oldName);
                else
                    redirects.Add(oldName, newName);
            }

            foreach (KeyValuePair<string, string> pair in redirects)
            {
                var visited = new HashSet<string>(StringComparer.Ordinal) { pair.Key };
                string current = pair.Key;
                bool cycle = false;
                while (redirects.TryGetValue(current, out string next))
                {
                    current = next;
                    if (!visited.Add(current))
                    {
                        errors.Add("Redirect cycle detected from: " + pair.Key);
                        cycle = true;
                        break;
                    }
                }

                if (!cycle && !resolvableNames.Contains(current))
                    errors.Add("Redirect target does not exist: " + pair.Key + " -> " + current);
            }
        }

        private void EnsureLists()
        {
            m_Sources ??= new List<GameplayTagSource>();
            m_Tags ??= new List<GameplayTagDefinition>();
            m_Redirects ??= new List<GameplayTagRedirect>();
        }

        private void OnValidate() => EnsureLists();

        private static uint AppendHash(uint hash, string value)
        {
            if (hash == 0u)
                hash = HashOffset;
            if (value == null)
                return (hash ^ 0xffu) * HashPrime;

            unchecked
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char character = value[i];
                    hash = (hash ^ (byte)character) * HashPrime;
                    hash = (hash ^ (byte)(character >> 8)) * HashPrime;
                }
                return hash * HashPrime;
            }
        }

        private static int CompareDefinitions(GameplayTagDefinition lhs, GameplayTagDefinition rhs) =>
            Compare(lhs, rhs, lhs?.Name, rhs?.Name);

        private static int CompareRedirects(GameplayTagRedirect lhs, GameplayTagRedirect rhs) =>
            Compare(lhs, rhs, lhs?.OldName, rhs?.OldName);

        private static int CompareSources(GameplayTagSource lhs, GameplayTagSource rhs) =>
            Compare(lhs, rhs, lhs?.Name, rhs?.Name);

        private static int Compare(object lhs, object rhs, string lhsKey, string rhsKey)
        {
            if (ReferenceEquals(lhs, rhs))
                return 0;
            if (lhs == null)
                return 1;
            if (rhs == null)
                return -1;
            return string.CompareOrdinal(lhsKey, rhsKey);
        }
    }

    [Serializable]
    public sealed class GameplayTagDefinition
    {
        [SerializeField] private string m_Name;
        [SerializeField] private string m_DevComment;
        [SerializeField] private string m_Source;
        [SerializeField] private bool m_Restricted;
        [SerializeField] private bool m_AllowNonRestrictedChildren = true;

        public string Name { get { return m_Name ?? string.Empty; } }
        public string DevComment { get { return m_DevComment ?? string.Empty; } }
        public string Source { get { return m_Source ?? string.Empty; } }
        public bool Restricted { get { return m_Restricted; } }
        public bool AllowNonRestrictedChildren { get { return m_AllowNonRestrictedChildren; } }

        public GameplayTagDefinition()
        {
        }

        public GameplayTagDefinition(
            string name,
            string devComment,
            string source,
            bool restricted,
            bool allowNonRestrictedChildren)
        {
            m_Name = name;
            m_DevComment = devComment;
            m_Source = source;
            m_Restricted = restricted;
            m_AllowNonRestrictedChildren = allowNonRestrictedChildren;
        }

        internal GameplayTagDefinition Clone() =>
            new GameplayTagDefinition(m_Name, m_DevComment, m_Source, m_Restricted, m_AllowNonRestrictedChildren);

        internal void SetName(string value) => m_Name = value;

        internal void SetMetadata(string devComment, string source, bool restricted, bool allowNonRestrictedChildren)
        {
            m_DevComment = devComment;
            m_Source = source;
            m_Restricted = restricted;
            m_AllowNonRestrictedChildren = allowNonRestrictedChildren;
        }
    }

    /// <summary>标签改名后留下的兼容映射：旧名字继续能解析到新名字。</summary>
    [Serializable]
    public sealed class GameplayTagRedirect
    {
        [SerializeField] private string m_OldName;
        [SerializeField] private string m_NewName;

        public string OldName { get { return m_OldName ?? string.Empty; } }
        public string NewName { get { return m_NewName ?? string.Empty; } }

        public GameplayTagRedirect()
        {
        }

        public GameplayTagRedirect(string oldName, string newName)
        {
            m_OldName = oldName;
            m_NewName = newName;
        }

        internal void Set(string oldName, string newName)
        {
            m_OldName = oldName;
            m_NewName = newName;
        }
    }

    /// <summary>标签的归属分组，只读分组可以防止业务改动引擎或第三方定义的标签。</summary>
    [Serializable]
    public sealed class GameplayTagSource
    {
        [SerializeField] private string m_Name = "Default";
        [SerializeField] private string m_Owner;
        [SerializeField] private bool m_ReadOnly;

        public string Name { get { return m_Name ?? string.Empty; } }
        public string Owner { get { return m_Owner ?? string.Empty; } }
        public bool ReadOnly { get { return m_ReadOnly; } }

        public GameplayTagSource()
        {
        }

        public GameplayTagSource(string name, string owner, bool readOnly)
        {
            m_Name = name;
            m_Owner = owner;
            m_ReadOnly = readOnly;
        }
    }
}
