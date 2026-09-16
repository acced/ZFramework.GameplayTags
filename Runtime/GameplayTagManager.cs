using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GameplayTags
{
    /// <summary>
    /// Main-thread registration/loading boundary. Build succeeds before publication.
    /// Containers and frozen queries are name snapshots; rebuilding does not silently rewrite them.
    /// </summary>
    public static class GameplayTagManager
    {
        private static HashSet<string> s_Names;
        private static IReadOnlyList<GameplayTag> s_SortedTags;
        private static Dictionary<string, string> s_Redirects;
        private static GameplayTagSettings s_Settings;
        private static bool s_MissingSettingsWarningIssued;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static readonly HashSet<string> UnregisteredWarnings = new HashSet<string>(StringComparer.Ordinal);
#endif
        public static event Action RegistryChanged;
        public static bool IsInitialized => s_Names != null;
        public static int TagCount { get { EnsureInitialized(); return s_SortedTags.Count; } }
        public static GameplayTagSettings Settings { get { EnsureInitialized(); return s_Settings; } }

        public static void Initialize(GameplayTagSettings settings) => Initialize(settings, false);
        public static void Initialize(GameplayTagSettings settings, bool forceRebuild)
        {
            if (IsInitialized && !forceRebuild) return;
            Build(settings, out HashSet<string> names, out GameplayTag[] tags, out Dictionary<string, string> redirects);
            s_Names = names;
            s_SortedTags = Array.AsReadOnly(tags);
            s_Redirects = redirects;
            s_Settings = settings;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UnregisteredWarnings.Clear();
#endif
            RegistryChanged?.Invoke();
        }
        public static void EnsureInitialized()
        {
            if (IsInitialized) return;
            GameplayTagSettings settings = GameplayTagSettings.LoadDefault();
            if (settings == null && !s_MissingSettingsWarningIssued)
            {
                s_MissingSettingsWarningIssued = true;
                Debug.LogWarning("GameplayTagSettings was not found at Resources/" + GameplayTagSettings.ResourcesPath + ". The registry will be empty.");
            }
            Initialize(settings);
        }
        public static GameplayTag RequestTag(string name) => RequestTag(name, true);
        public static GameplayTag RequestTag(string name, bool errorIfNotFound)
        {
            if (TryRequestTag(name, out GameplayTag tag)) return tag;
            if (errorIfNotFound) throw new KeyNotFoundException("Gameplay Tag is not registered: " + (name ?? "<null>"));
            return GameplayTag.None;
        }
        public static bool TryRequestTag(string name, out GameplayTag tag)
        {
            tag = default;
            if (string.IsNullOrEmpty(name)) return false;
            EnsureInitialized();
            // Membership in an already validated registry proves name validity. Do not scan syntax twice.
            name = name.Trim();
            if (!s_Names.TryGetValue(name, out string canonical) && !s_Redirects.TryGetValue(name, out canonical)) return false;
            tag = new GameplayTag(canonical);
            return true;
        }
        public static bool IsRegistered(GameplayTag tag) => IsRegistered(tag.Name);
        public static bool IsRegistered(string name)
        {
            EnsureInitialized();
            return !string.IsNullOrEmpty(name) && s_Names.Contains(name);
        }
        public static IReadOnlyList<GameplayTag> GetAllTags() { EnsureInitialized(); return s_SortedTags; }

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        internal static void WarnUnregistered(string name)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!string.IsNullOrEmpty(name) && s_Settings != null && s_Settings.WarnOnInvalidSerializedTags && UnregisteredWarnings.Add(name))
                Debug.LogWarning("Gameplay Tag is not registered and was rejected: " + name);
#endif
        }
        internal static void ReinitializeForEditor(GameplayTagSettings settings) => Initialize(settings, true);
        internal static void ResetForTests() => Reset();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            s_Names = null;
            s_SortedTags = null;
            s_Redirects = null;
            s_Settings = null;
            s_MissingSettingsWarningIssued = false;
            RegistryChanged = null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UnregisteredWarnings.Clear();
#endif
        }
        private static void Build(GameplayTagSettings settings, out HashSet<string> names, out GameplayTag[] tags, out Dictionary<string, string> redirects)
        {
            names = new HashSet<string>(StringComparer.Ordinal);
            redirects = new Dictionary<string, string>(StringComparer.Ordinal);
            if (settings == null) { tags = Array.Empty<GameplayTag>(); return; }
            var errors = new List<string>();
            if (!settings.Validate(errors)) throw new GameplayTagRegistryException(errors);
            IReadOnlyList<GameplayTagDefinition> definitions = settings.Tags;
            for (int i = 0; i < definitions.Count; i++)
            {
                string name = definitions[i].Name;
                while (name.Length > 0 && names.Add(name)) name = GameplayTagName.GetParent(name);
            }
            var ordered = new List<string>(names);
            ordered.Sort(StringComparer.Ordinal);
            tags = new GameplayTag[ordered.Count];
            for (int i = 0; i < ordered.Count; i++) tags[i] = new GameplayTag(ordered[i]);
            var raw = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < settings.Redirects.Count; i++) raw.Add(settings.Redirects[i].OldName, settings.Redirects[i].NewName);
            var path = new List<string>();
            foreach (string source in raw.Keys)
            {
                path.Clear();
                string current = source;
                while (raw.TryGetValue(current, out string next))
                {
                    if (redirects.TryGetValue(current, out string resolved)) { current = resolved; break; }
                    path.Add(current);
                    current = next;
                }
                names.TryGetValue(current, out string canonical); // Validate established an acyclic chain to an active name.
                for (int i = 0; i < path.Count; i++) redirects.Add(path[i], canonical);
            }
        }
    }
    public sealed class GameplayTagRegistryException : Exception
    {
        private readonly IReadOnlyList<string> m_Errors;
        public IReadOnlyList<string> Errors => m_Errors;
        public GameplayTagRegistryException(IList<string> errors) : base(BuildMessage(errors))
        {
            var copy = new string[errors?.Count ?? 0];
            for (int i = 0; i < copy.Length; i++) copy[i] = errors[i];
            m_Errors = Array.AsReadOnly(copy);
        }
        private static string BuildMessage(IList<string> errors)
        {
            var builder = new StringBuilder("Gameplay Tag registry could not be built.");
            if (errors != null) for (int i = 0; i < errors.Count; i++) builder.Append("\n - ").Append(errors[i]);
            return builder.ToString();
        }
    }
    internal static class GameplayTagRuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            GameplayTagSettings settings = GameplayTagSettings.LoadDefault();
            if (settings == null || settings.AutoInitialize) GameplayTagManager.Initialize(settings);
        }
    }
}
