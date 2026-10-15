using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GameplayTags
{
    /// <summary>
    /// 运行期标签注册表：一个名称集合（父级自动补全）加一张重定向表。
    ///
    /// 注册表只负责回答「这个名字注册过吗」和「这个旧名字现在叫什么」。
    /// 层级匹配不经过这里，见 <see cref="GameplayTagName.IsEqualOrChildOf"/>。
    /// </summary>
    public static class GameplayTagManager
    {
        private static readonly object SyncRoot = new object();
        private static readonly HashSet<string> UnregisteredWarnings = new HashSet<string>(StringComparer.Ordinal);

        private static HashSet<string> s_Names;
        private static GameplayTag[] s_SortedTags;
        private static Dictionary<string, string> s_Redirects;
        private static GameplayTagSettings s_Settings;
        private static bool s_MissingSettingsWarningIssued;

        public static event Action RegistryChanged;

        public static bool IsInitialized { get { return s_Names != null; } }
        public static int TagCount { get { EnsureInitialized(); return s_SortedTags.Length; } }
        public static GameplayTagSettings Settings { get { EnsureInitialized(); return s_Settings; } }

        public static void Initialize(GameplayTagSettings settings) => Initialize(settings, false);

        public static void Initialize(GameplayTagSettings settings, bool forceRebuild)
        {
            lock (SyncRoot)
            {
                if (s_Names != null && !forceRebuild)
                    return;

                Build(settings, out HashSet<string> names, out GameplayTag[] sortedTags, out Dictionary<string, string> redirects);
                s_Names = names;
                s_SortedTags = sortedTags;
                s_Redirects = redirects;
                s_Settings = settings;
                UnregisteredWarnings.Clear();
            }

            RegistryChanged?.Invoke();
        }

        public static void EnsureInitialized()
        {
            if (s_Names != null)
                return;

            GameplayTagSettings settings = GameplayTagSettings.LoadDefault();
            if (settings == null && !s_MissingSettingsWarningIssued)
            {
                s_MissingSettingsWarningIssued = true;
                Debug.LogWarning(
                    "GameplayTagSettings was not found at Resources/" +
                    GameplayTagSettings.ResourcesPath + ". The registry will be empty.");
            }

            Initialize(settings, false);
        }

        public static GameplayTag RequestTag(string name) => RequestTag(name, true);

        public static GameplayTag RequestTag(string name, bool errorIfNotFound)
        {
            if (TryRequestTag(name, out GameplayTag tag))
                return tag;

            if (errorIfNotFound)
                throw new KeyNotFoundException("Gameplay Tag is not registered: " + (name ?? "<null>"));
            return GameplayTag.None;
        }

        /// <summary>解析标签名，必要时跟随重定向。返回的标签一定是注册表里的规范名。</summary>
        public static bool TryRequestTag(string name, out GameplayTag tag)
        {
            tag = GameplayTag.None;
            if (string.IsNullOrEmpty(name))
                return false;

            EnsureInitialized();
            if (!GameplayTagName.TryNormalize(name, out string normalized, out _))
                return false;

            if (!s_Names.Contains(normalized))
            {
                // 重定向表在构建时已压平并确认过目标存在。
                if (!s_Redirects.TryGetValue(normalized, out string target))
                    return false;
                normalized = target;
            }

            tag = new GameplayTag(normalized);
            return true;
        }

        public static bool IsRegistered(GameplayTag tag) => IsRegistered(tag.Name);

        public static bool IsRegistered(string name)
        {
            EnsureInitialized();
            return !string.IsNullOrEmpty(name) && s_Names.Contains(name);
        }

        /// <summary>按名称序数排序的全部标签，含自动补全的隐式父级。</summary>
        public static IReadOnlyList<GameplayTag> GetAllTags()
        {
            EnsureInitialized();
            return s_SortedTags;
        }

        /// <summary>每个未注册名字只提醒一次，避免逐帧刷屏。</summary>
        internal static void WarnUnregistered(string name)
        {
            if (string.IsNullOrEmpty(name))
                return;

            bool shouldWarn;
            lock (SyncRoot)
            {
                shouldWarn = s_Settings != null &&
                             s_Settings.WarnOnInvalidSerializedTags &&
                             UnregisteredWarnings.Add(name);
            }

            if (shouldWarn)
                Debug.LogWarning("Gameplay Tag is not registered and will be ignored: " + name);
        }

        internal static void ReinitializeForEditor(GameplayTagSettings settings) => Initialize(settings, true);

        internal static void ResetForTests()
        {
            lock (SyncRoot)
            {
                s_Names = null;
                s_SortedTags = null;
                s_Redirects = null;
                s_Settings = null;
                s_MissingSettingsWarningIssued = false;
                UnregisteredWarnings.Clear();
                RegistryChanged = null;
            }
        }

        /// <summary>
        /// 校验交给 <see cref="GameplayTagSettings.Validate"/>：重复、大小写冲突、restricted 层级、
        /// 重定向自指与成环都在那里判定，这里只负责把通过校验的配置摊成运行期结构。
        /// </summary>
        private static void Build(
            GameplayTagSettings settings,
            out HashSet<string> names,
            out GameplayTag[] sortedTags,
            out Dictionary<string, string> redirects)
        {
            names = new HashSet<string>(StringComparer.Ordinal);
            redirects = new Dictionary<string, string>(StringComparer.Ordinal);
            if (settings == null)
            {
                sortedTags = Array.Empty<GameplayTag>();
                return;
            }

            var errors = new List<string>();
            if (!settings.Validate(errors))
                throw new GameplayTagRegistryException(errors);

            IReadOnlyList<GameplayTagDefinition> definitions = settings.Tags;
            for (int i = 0; i < definitions.Count; i++)
            {
                if (!GameplayTagName.TryNormalize(definitions[i].Name, out string name, out _))
                    continue;

                // 名字已存在意味着它的祖先链也已建好，可以直接收尾。
                while (!string.IsNullOrEmpty(name) && names.Add(name))
                    name = GameplayTagName.GetParent(name);
            }

            var ordered = new List<string>(names);
            ordered.Sort(StringComparer.Ordinal);
            sortedTags = new GameplayTag[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
                sortedTags[i] = new GameplayTag(ordered[i]);

            BuildRedirects(settings.Redirects, names, redirects);
        }

        private static void BuildRedirects(
            IReadOnlyList<GameplayTagRedirect> configured,
            HashSet<string> names,
            Dictionary<string, string> redirects)
        {
            var raw = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < configured.Count; i++)
            {
                if (GameplayTagName.TryNormalize(configured[i].OldName, out string source, out _) &&
                    GameplayTagName.TryNormalize(configured[i].NewName, out string target, out _))
                    raw[source] = target;
            }

            // 把重定向链一次性压平，运行期查表就不用再跟随了。
            foreach (KeyValuePair<string, string> pair in raw)
            {
                string current = pair.Value;
                int hops = raw.Count;
                while (hops-- > 0 && raw.TryGetValue(current, out string next))
                    current = next;

                if (names.Contains(current))
                    redirects[pair.Key] = current;
            }
        }
    }

    /// <summary>注册表构建失败时抛出，聚合本次构建收集到的全部错误。</summary>
    public sealed class GameplayTagRegistryException : Exception
    {
        private readonly string[] m_Errors;

        public IReadOnlyList<string> Errors { get { return m_Errors; } }

        public GameplayTagRegistryException(IList<string> errors)
            : base(BuildMessage(errors))
        {
            m_Errors = new string[errors?.Count ?? 0];
            for (int i = 0; i < m_Errors.Length; i++)
                m_Errors[i] = errors[i];
        }

        private static string BuildMessage(IList<string> errors)
        {
            if (errors == null || errors.Count == 0)
                return "Gameplay Tag registry could not be built.";

            var builder = new StringBuilder("Gameplay Tag registry contains errors:");
            for (int i = 0; i < errors.Count; i++)
                builder.Append("\n - ").Append(errors[i]);
            return builder.ToString();
        }
    }

    internal static class GameplayTagRuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            GameplayTagSettings settings = GameplayTagSettings.LoadDefault();
            if (settings == null || settings.AutoInitialize)
                GameplayTagManager.Initialize(settings);
        }
    }
}
