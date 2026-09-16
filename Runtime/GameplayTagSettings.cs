using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameplayTags
{
    [CreateAssetMenu(fileName = "GameplayTagSettings", menuName = "ZFramework/Gameplay Tags/Settings")]
    public sealed class GameplayTagSettings : ScriptableObject, ISerializationCallbackReceiver
    {
        public const string ResourcesPath = "GameplayTags/GameplayTagSettings";
        public const string DefaultAssetPath = "Assets/Resources/GameplayTags/GameplayTagSettings.asset";
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
        public bool AutoInitialize => m_AutoInitialize;
        public bool IncludeImplicitParentTagsInGeneratedCode => m_IncludeImplicitParentTagsInGeneratedCode;
        public bool WarnOnInvalidSerializedTags => m_WarnOnInvalidSerializedTags;
        public string GeneratedNamespace => m_GeneratedNamespace ?? string.Empty;
        public string GeneratedClassName => m_GeneratedClassName ?? string.Empty;
        public string GeneratedCodePath => m_GeneratedCodePath ?? string.Empty;
        public int Revision => m_Revision;
        public IReadOnlyList<GameplayTagSource> Sources => m_Sources;
        public IReadOnlyList<GameplayTagDefinition> Tags => m_Tags;
        public IReadOnlyList<GameplayTagRedirect> Redirects => m_Redirects;
        internal List<GameplayTagDefinition> TagsInternal => m_Tags;
        internal List<GameplayTagRedirect> RedirectsInternal => m_Redirects;
        internal List<GameplayTagSource> SourcesInternal => m_Sources;
        public static GameplayTagSettings LoadDefault() => Resources.Load<GameplayTagSettings>(ResourcesPath);
        public bool TryGetTagDefinition(string name, out GameplayTagDefinition definition)
        {
            for (int i = 0; i < m_Tags.Count; i++)
            {
                if (m_Tags[i] != null && string.CompareOrdinal(m_Tags[i].Name, name) == 0) { definition = m_Tags[i]; return true; }
            }
            definition = null;
            return false;
        }
        public bool Validate(List<string> errors) => Validate(m_Tags, m_Redirects, m_Sources, errors);
        /// <summary>External configuration boundary. Does not mutate or repair the supplied configuration.</summary>
        public static bool Validate(IReadOnlyList<GameplayTagDefinition> tags, IReadOnlyList<GameplayTagRedirect> redirects, IReadOnlyList<GameplayTagSource> sources, List<string> errors)
        {
            if (errors == null) throw new ArgumentNullException(nameof(errors));
            errors.Clear();
            if (tags == null || redirects == null || sources == null) { errors.Add("A Gameplay Tag configuration collection is null."); return false; }
            var sourceNames = new HashSet<string>(StringComparer.Ordinal);
            var sourceCase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sources.Count; i++)
            {
                GameplayTagSource source = sources[i];
                if (source == null) { errors.Add("Null source at index " + i); continue; }
                string name = source.Name;
                if (name.Length == 0 || name != name.Trim()) errors.Add("Source names must be nonempty and already trimmed: " + name);
                if (!sourceNames.Add(name) || !sourceCase.Add(name)) errors.Add("Duplicate or case-conflicting source: " + name);
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            var definitions = new Dictionary<string, GameplayTagDefinition>(StringComparer.Ordinal);
            for (int i = 0; i < tags.Count; i++)
            {
                GameplayTagDefinition definition = tags[i];
                if (definition == null) { errors.Add("Null tag definition at index " + i); continue; }
                if (!Canonical(definition.Name, out string error)) { errors.Add(error); continue; }
                if (!names.Add(definition.Name)) errors.Add("Duplicate Gameplay Tag: " + definition.Name);
                else definitions.Add(definition.Name, definition);
                if (definition.Source.Length > 0 && !sourceNames.Contains(definition.Source)) errors.Add("Unknown source: " + definition.Source);
            }
            HashSet<string> resolvable = CollectResolvableNames(names);
            var caseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in resolvable) if (!caseNames.Add(name)) errors.Add("Active tags, including implicit parents, cannot differ only by casing: " + name);
            foreach (GameplayTagDefinition definition in definitions.Values)
            {
                if (definition.Restricted) continue;
                string parent = GameplayTagName.GetParent(definition.Name);
                while (parent.Length > 0)
                {
                    if (definitions.TryGetValue(parent, out GameplayTagDefinition p) && p.Restricted && !p.AllowNonRestrictedChildren)
                    { errors.Add("Non-restricted tag '" + definition.Name + "' is below restricted tag '" + parent + "'."); break; }
                    parent = GameplayTagName.GetParent(parent);
                }
            }
            ValidateRedirects(redirects, resolvable, errors);
            return errors.Count == 0;
        }
        private static bool Canonical(string value, out string error)
        {
            if (!GameplayTagName.TryNormalize(value, out string normalized, out error)) return false;
            if (!string.Equals(value, normalized, StringComparison.Ordinal)) { error = "Stored tag names must already be normalized: '" + value + "'."; return false; }
            return true;
        }
        internal static HashSet<string> CollectResolvableNames(HashSet<string> definedNames)
        {
            var result = new HashSet<string>(definedNames, StringComparer.Ordinal);
            foreach (string name in definedNames)
            {
                string parent = GameplayTagName.GetParent(name);
                while (parent.Length > 0 && result.Add(parent)) parent = GameplayTagName.GetParent(parent);
            }
            return result;
        }
        private static void ValidateRedirects(IReadOnlyList<GameplayTagRedirect> configured, HashSet<string> names, List<string> errors)
        {
            var redirects = new Dictionary<string, string>(StringComparer.Ordinal);
            var caseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < configured.Count; i++)
            {
                GameplayTagRedirect redirect = configured[i];
                if (redirect == null) { errors.Add("Null redirect at index " + i); continue; }
                if (!Canonical(redirect.OldName, out string error) || !Canonical(redirect.NewName, out error)) { errors.Add(error); continue; }
                if (names.Contains(redirect.OldName)) errors.Add("Redirect source is an active tag: " + redirect.OldName);
                if (!caseNames.Add(redirect.OldName)) errors.Add("Duplicate or case-conflicting redirect source: " + redirect.OldName);
                if (!redirects.ContainsKey(redirect.OldName)) redirects.Add(redirect.OldName, redirect.NewName);
            }
            var known = new Dictionary<string, bool>(StringComparer.Ordinal);
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var path = new List<string>();
            foreach (string source in redirects.Keys)
            {
                if (known.ContainsKey(source)) continue;
                visiting.Clear(); path.Clear();
                string current = source;
                bool valid;
                while (true)
                {
                    if (known.TryGetValue(current, out valid)) break;
                    if (!redirects.TryGetValue(current, out string next))
                    {
                        valid = names.Contains(current);
                        if (!valid) errors.Add("Redirect target does not exist: " + source + " -> " + current);
                        break;
                    }
                    if (!visiting.Add(current)) { valid = false; errors.Add("Redirect cycle from: " + source); break; }
                    path.Add(current);
                    current = next;
                }
                for (int i = 0; i < path.Count; i++) known[path[i]] = valid;
            }
        }
        /// <summary>Stable diagnostic fingerprint, not a proof of generated-source equality.</summary>
        public uint ComputeContentHash()
        {
            uint hash = 2166136261u;
            hash = AppendHash(hash, "gameplaytags-v2");
            hash = AppendHash(hash, GeneratedNamespace);
            hash = AppendHash(hash, GeneratedClassName);
            hash = AppendHash(hash, GeneratedCodePath);
            hash = AppendHash(hash, m_IncludeImplicitParentTagsInGeneratedCode ? "1" : "0");
            var orderedTags = new List<GameplayTagDefinition>(m_Tags); orderedTags.Sort(CompareDefinitions);
            for (int i = 0; i < orderedTags.Count; i++)
            {
                GameplayTagDefinition d = orderedTags[i];
                if (d == null) { hash = AppendHash(hash, null); continue; }
                hash = AppendHash(hash, d.Name); hash = AppendHash(hash, d.DevComment); hash = AppendHash(hash, d.Source);
                hash = AppendHash(hash, d.Restricted ? "1" : "0"); hash = AppendHash(hash, d.AllowNonRestrictedChildren ? "1" : "0");
            }
            var orderedRedirects = new List<GameplayTagRedirect>(m_Redirects); orderedRedirects.Sort(CompareRedirects);
            for (int i = 0; i < orderedRedirects.Count; i++)
            {
                GameplayTagRedirect r = orderedRedirects[i];
                hash = AppendHash(hash, r?.OldName); hash = AppendHash(hash, r?.NewName);
            }
            return hash;
        }
        private static uint AppendHash(uint hash, string value)
        {
            unchecked
            {
                int length = value?.Length ?? -1;
                for (int i = 0; i < 4; i++) hash = (hash ^ (byte)(length >> (i * 8))) * 16777619u;
                if (value != null) for (int i = 0; i < value.Length; i++)
                { hash = (hash ^ (byte)value[i]) * 16777619u; hash = (hash ^ (byte)(value[i] >> 8)) * 16777619u; }
                return hash;
            }
        }
        internal void Touch()
        {
            m_Tags.Sort(CompareDefinitions); m_Redirects.Sort(CompareRedirects); m_Sources.Sort(CompareSources);
            unchecked { m_Revision++; if (m_Revision == 0) m_Revision = 1; }
        }
        internal void SetEditorOptions(bool autoInitialize, bool includeImplicitParentTagsInGeneratedCode, bool warnOnInvalidSerializedTags, string generatedNamespace, string generatedClassName, string generatedCodePath)
        {
            m_AutoInitialize = autoInitialize; m_IncludeImplicitParentTagsInGeneratedCode = includeImplicitParentTagsInGeneratedCode;
            m_WarnOnInvalidSerializedTags = warnOnInvalidSerializedTags; m_GeneratedNamespace = generatedNamespace;
            m_GeneratedClassName = generatedClassName; m_GeneratedCodePath = generatedCodePath; Touch();
        }
        internal void ReplaceAll(List<GameplayTagDefinition> tags, List<GameplayTagRedirect> redirects, List<GameplayTagSource> sources)
        { m_Tags = tags; m_Redirects = redirects; m_Sources = sources; Touch(); }
        private void EnsureLists()
        { m_Tags ??= new List<GameplayTagDefinition>(); m_Redirects ??= new List<GameplayTagRedirect>(); m_Sources ??= new List<GameplayTagSource>(); }
        private void OnValidate() { EnsureLists(); Touch(); }
        void ISerializationCallbackReceiver.OnBeforeSerialize() { }
        void ISerializationCallbackReceiver.OnAfterDeserialize() => EnsureLists();
        private static int CompareDefinitions(GameplayTagDefinition a, GameplayTagDefinition b) => Compare(a, b, a?.Name, b?.Name);
        private static int CompareRedirects(GameplayTagRedirect a, GameplayTagRedirect b) => Compare(a, b, a?.OldName, b?.OldName);
        private static int CompareSources(GameplayTagSource a, GameplayTagSource b) => Compare(a, b, a?.Name, b?.Name);
        private static int Compare(object a, object b, string x, string y)
        { if (ReferenceEquals(a, b)) return 0; if (a == null) return 1; if (b == null) return -1; return string.CompareOrdinal(x, y); }
    }
    [Serializable]
    public sealed class GameplayTagDefinition
    {
        [SerializeField] private string m_Name;
        [SerializeField] private string m_DevComment;
        [SerializeField] private string m_Source;
        [SerializeField] private bool m_Restricted;
        [SerializeField] private bool m_AllowNonRestrictedChildren = true;
        public string Name => m_Name ?? string.Empty;
        public string DevComment => m_DevComment ?? string.Empty;
        public string Source => m_Source ?? string.Empty;
        public bool Restricted => m_Restricted;
        public bool AllowNonRestrictedChildren => m_AllowNonRestrictedChildren;
        public GameplayTagDefinition() { }
        public GameplayTagDefinition(string name, string devComment, string source, bool restricted, bool allowNonRestrictedChildren)
        { m_Name = name; m_DevComment = devComment; m_Source = source; m_Restricted = restricted; m_AllowNonRestrictedChildren = allowNonRestrictedChildren; }
        internal GameplayTagDefinition Clone() => new GameplayTagDefinition(m_Name, m_DevComment, m_Source, m_Restricted, m_AllowNonRestrictedChildren);
        internal void SetName(string name) => m_Name = name;
        internal void SetMetadata(string devComment, string source, bool restricted, bool allowNonRestrictedChildren)
        { m_DevComment = devComment; m_Source = source; m_Restricted = restricted; m_AllowNonRestrictedChildren = allowNonRestrictedChildren; }
    }
    [Serializable]
    public sealed class GameplayTagRedirect
    {
        [SerializeField] private string m_OldName;
        [SerializeField] private string m_NewName;
        public string OldName => m_OldName ?? string.Empty;
        public string NewName => m_NewName ?? string.Empty;
        public GameplayTagRedirect() { }
        public GameplayTagRedirect(string oldName, string newName) { m_OldName = oldName; m_NewName = newName; }
        internal void Set(string oldName, string newName) { m_OldName = oldName; m_NewName = newName; }
    }
    [Serializable]
    public sealed class GameplayTagSource
    {
        [SerializeField] private string m_Name = "Default";
        [SerializeField] private string m_Owner;
        [SerializeField] private bool m_ReadOnly;
        public string Name => m_Name ?? string.Empty;
        public string Owner => m_Owner ?? string.Empty;
        public bool ReadOnly => m_ReadOnly;
        public GameplayTagSource() { }
        public GameplayTagSource(string name, string owner, bool readOnly) { m_Name = name; m_Owner = owner; m_ReadOnly = readOnly; }
    }
}
