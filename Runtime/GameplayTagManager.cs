using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GameplayTags
{
    /// <summary>Main-thread authoring/loading service. Capture CurrentRegistry once per runtime context.</summary>
    public static class GameplayTagManager
    {
        private static TagRegistry s_Registry;
        private static GameplayTagSettings s_Settings;
        private static bool s_MissingSettingsWarningIssued;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static readonly HashSet<string> UnregisteredWarnings = new HashSet<string>(StringComparer.Ordinal);
#endif
        public static event Action RegistryChanged;
        public static bool IsInitialized => s_Registry != null;
        public static TagRegistry CurrentRegistry { get { EnsureInitialized(); return s_Registry; } }
        public static int TagCount => CurrentRegistry.Count;
        public static GameplayTagSettings Settings { get { EnsureInitialized(); return s_Settings; } }
        public static void Initialize(GameplayTagSettings settings) => Initialize(settings, false);
        public static void Initialize(GameplayTagSettings settings, bool forceRebuild)
        {
            if (IsInitialized && !forceRebuild) return;
            TagRegistry registry = TagRegistry.Create(settings);
            s_Registry = registry;
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
            return default;
        }
        public static bool TryRequestTag(string name, out GameplayTag tag)
        {
            if (!string.IsNullOrEmpty(name) && CurrentRegistry.TryResolve(name, out RuntimeTag resolved))
            { tag = new GameplayTag(resolved.Name); return true; }
            tag = default;
            return false;
        }
        public static bool IsRegistered(GameplayTag tag) => IsRegistered(tag.Name);
        public static bool IsRegistered(string name) => CurrentRegistry.IsRegistered(name);
        public static IReadOnlyList<GameplayTag> GetAllTags() => CurrentRegistry.AuthoringTags;
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
            s_Registry = null;
            s_Settings = null;
            s_MissingSettingsWarningIssued = false;
            RegistryChanged = null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UnregisteredWarnings.Clear();
#endif
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
