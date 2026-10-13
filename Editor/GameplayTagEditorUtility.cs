using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GameplayTags.Editor
{
    /// <summary>
    /// Settings 资产的生命周期与全部写操作。
    ///
    /// 每个 Try* 都在配置的副本上施加改动，整体校验通过之后才写回，
    /// 所以失败时原配置一个字节都没动，不需要快照回滚。
    /// 撤销由调用方的 <see cref="Undo.RecordObject"/> 负责。
    /// </summary>
    public static class GameplayTagEditorUtility
    {
        private static GameplayTagSettings s_LastSettings;
        private static int s_LastRevision = int.MinValue;

        public static GameplayTagSettings GetSettings(bool createIfMissing)
        {
            GameplayTagSettings settings = AssetDatabase.LoadAssetAtPath<GameplayTagSettings>(
                GameplayTagSettings.DefaultAssetPath);
            if (settings == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:GameplayTagSettings");
                if (guids.Length > 0)
                    settings = AssetDatabase.LoadAssetAtPath<GameplayTagSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            if (settings == null && createIfMissing)
                settings = CreateSettings();
            return settings;
        }

        public static GameplayTagSettings CreateSettings()
        {
            EnsureAssetDirectory(Path.GetDirectoryName(GameplayTagSettings.DefaultAssetPath));

            GameplayTagSettings settings = ScriptableObject.CreateInstance<GameplayTagSettings>();
            TryAddSource(settings, "Default", string.Empty, false, out _);
            AssetDatabase.CreateAsset(settings, GameplayTagSettings.DefaultAssetPath);
            AssetDatabase.SaveAssets();
            Selection.activeObject = settings;
            InitializeManager(settings, true);
            return settings;
        }

        public static void InitializeManager(GameplayTagSettings settings, bool force)
        {
            if (settings == null)
                return;
            if (!force && ReferenceEquals(settings, s_LastSettings) && s_LastRevision == settings.Revision)
                return;

            GameplayTagManager.ReinitializeForEditor(settings);
            s_LastSettings = settings;
            s_LastRevision = settings.Revision;
        }

        public static void SaveAndReinitialize(GameplayTagSettings settings)
        {
            if (settings == null)
                return;

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            InitializeManager(settings, true);
        }

        public static void EnsureAssetDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory) || AssetDatabase.IsValidFolder(directory))
                return;

            string[] segments = directory.Replace('\\', '/').Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[i]);
                current = next;
            }
        }

        public static bool TryAddTag(GameplayTagSettings settings, string name, out string error) =>
            TryAddTag(settings, name, string.Empty, "Default", false, true, out error);

        public static bool TryAddTag(
            GameplayTagSettings settings,
            string name,
            string devComment,
            string source,
            bool restricted,
            bool allowNonRestrictedChildren,
            out string error)
        {
            if (!GameplayTagName.TryNormalize(name, out string normalized, out error))
                return false;

            List<GameplayTagDefinition> tags = CloneDefinitions(settings.Tags);
            List<GameplayTagSource> sources = CloneSources(settings.Sources);
            if (FindTag(tags, normalized) >= 0)
            {
                error = "Gameplay Tag already exists: " + normalized;
                return false;
            }
            if (ContainsTagIgnoringCase(tags, normalized))
            {
                error = "A Gameplay Tag with different letter casing already exists: " + normalized;
                return false;
            }

            string normalizedSource = source?.Trim() ?? string.Empty;
            if (!TryUseSource(sources, normalizedSource, out error))
                return false;

            tags.Add(new GameplayTagDefinition(
                normalized,
                devComment ?? string.Empty,
                normalizedSource,
                restricted,
                allowNonRestrictedChildren));
            return TryApply(settings, tags, CloneRedirects(settings.Redirects), sources, out error);
        }

        public static bool TrySetTagMetadata(
            GameplayTagSettings settings,
            string name,
            string devComment,
            string source,
            bool restricted,
            bool allowNonRestrictedChildren,
            out string error)
        {
            List<GameplayTagDefinition> tags = CloneDefinitions(settings.Tags);
            List<GameplayTagSource> sources = CloneSources(settings.Sources);
            int index = FindTag(tags, name);
            if (index < 0)
            {
                error = "Gameplay Tag does not exist: " + name;
                return false;
            }

            if (!TryEditSource(sources, tags[index].Source, out error))
                return false;

            string normalizedSource = source?.Trim() ?? string.Empty;
            if (!TryUseSource(sources, normalizedSource, out error))
                return false;

            tags[index].SetMetadata(
                devComment ?? string.Empty,
                normalizedSource,
                restricted,
                allowNonRestrictedChildren);
            return TryApply(settings, tags, CloneRedirects(settings.Redirects), sources, out error);
        }

        public static bool TryRenameTag(
            GameplayTagSettings settings,
            string oldName,
            string newName,
            bool renameChildren,
            bool createRedirects,
            out string error)
        {
            if (!GameplayTagName.TryNormalize(oldName, out string normalizedOld, out error) ||
                !GameplayTagName.TryNormalize(newName, out string normalizedNew, out error))
                return false;

            List<GameplayTagDefinition> tags = CloneDefinitions(settings.Tags);
            List<GameplayTagRedirect> redirects = CloneRedirects(settings.Redirects);
            List<GameplayTagSource> sources = CloneSources(settings.Sources);
            if (FindTag(tags, normalizedOld) < 0)
            {
                error = "Gameplay Tag does not exist: " + normalizedOld;
                return false;
            }
            if (!renameChildren && HasDefinedChildren(tags, normalizedOld))
            {
                error = "The tag has children. Enable renameChildren to rename the hierarchy safely.";
                return false;
            }
            if (!string.Equals(normalizedOld, normalizedNew, StringComparison.Ordinal) &&
                (GameplayTagName.IsEqualOrChildOf(normalizedNew, normalizedOld) ||
                 GameplayTagName.IsEqualOrChildOf(normalizedOld, normalizedNew)))
            {
                error = "A Gameplay Tag hierarchy cannot be renamed into itself or one of its ancestors.";
                return false;
            }

            // 先把改名计划算出来，才能判断新名字是否撞上不参与改名的既有标签。
            var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < tags.Count; i++)
            {
                string current = tags[i].Name;
                if (!string.Equals(current, normalizedOld, StringComparison.Ordinal) &&
                    !(renameChildren && GameplayTagName.IsEqualOrChildOf(current, normalizedOld)))
                    continue;

                if (!TryEditSource(sources, tags[i].Source, out error))
                    return false;

                renamed[current] = normalizedNew + current.Substring(normalizedOld.Length);
            }

            var finalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < tags.Count; i++)
            {
                if (!renamed.TryGetValue(tags[i].Name, out string finalName))
                    finalName = tags[i].Name;

                if (!finalNames.Add(finalName))
                {
                    error = "Renaming would collide with the existing tag: " + finalName;
                    return false;
                }
            }

            for (int i = 0; i < tags.Count; i++)
            {
                if (!renamed.TryGetValue(tags[i].Name, out string finalName))
                    continue;

                string previous = tags[i].Name;
                tags[i].SetName(finalName);
                if (createRedirects)
                    SetRedirect(redirects, previous, finalName);
            }

            return TryApply(settings, tags, redirects, sources, out error);
        }

        public static bool TryRemoveTag(GameplayTagSettings settings, string name, bool removeChildren, out string error)
        {
            if (!GameplayTagName.TryNormalize(name, out string normalized, out error))
                return false;

            List<GameplayTagDefinition> tags = CloneDefinitions(settings.Tags);
            List<GameplayTagRedirect> redirects = CloneRedirects(settings.Redirects);
            List<GameplayTagSource> sources = CloneSources(settings.Sources);
            if (FindTag(tags, normalized) < 0)
            {
                error = "Gameplay Tag does not exist: " + normalized;
                return false;
            }
            if (!removeChildren && HasDefinedChildren(tags, normalized))
            {
                error = "The tag has children. Enable removeChildren to remove the hierarchy.";
                return false;
            }

            for (int i = tags.Count - 1; i >= 0; i--)
            {
                string current = tags[i].Name;
                if (!string.Equals(current, normalized, StringComparison.Ordinal) &&
                    !(removeChildren && GameplayTagName.IsEqualOrChildOf(current, normalized)))
                    continue;

                if (!TryEditSource(sources, tags[i].Source, out error))
                    return false;
                tags.RemoveAt(i);
            }

            for (int i = redirects.Count - 1; i >= 0; i--)
            {
                if (GameplayTagName.IsEqualOrChildOf(redirects[i].OldName, normalized) ||
                    GameplayTagName.IsEqualOrChildOf(redirects[i].NewName, normalized))
                    redirects.RemoveAt(i);
            }

            PruneInvalidRedirects(redirects, tags);
            return TryApply(settings, tags, redirects, sources, out error);
        }

        public static bool TryAddRedirect(GameplayTagSettings settings, string oldName, string newName, out string error)
        {
            if (!GameplayTagName.TryNormalize(oldName, out string normalizedOld, out error) ||
                !GameplayTagName.TryNormalize(newName, out string normalizedNew, out error))
                return false;

            if (string.Equals(normalizedOld, normalizedNew, StringComparison.Ordinal))
            {
                error = "A Gameplay Tag redirect cannot target itself.";
                return false;
            }

            // 源仍是活标签、目标不存在、成环、大小写冲突都由 Validate 在写回前拦下。
            List<GameplayTagRedirect> redirects = CloneRedirects(settings.Redirects);
            SetRedirect(redirects, normalizedOld, normalizedNew);
            return TryApply(settings, CloneDefinitions(settings.Tags), redirects, CloneSources(settings.Sources), out error);
        }

        public static bool TryRemoveRedirect(GameplayTagSettings settings, string oldName)
        {
            List<GameplayTagRedirect> redirects = CloneRedirects(settings.Redirects);
            if (redirects.RemoveAll(redirect => string.Equals(redirect.OldName, oldName, StringComparison.Ordinal)) == 0)
                return false;

            List<GameplayTagDefinition> tags = CloneDefinitions(settings.Tags);
            PruneInvalidRedirects(redirects, tags);
            return TryApply(settings, tags, redirects, CloneSources(settings.Sources), out _);
        }

        public static bool TryAddSource(
            GameplayTagSettings settings,
            string name,
            string owner,
            bool readOnly,
            out string error)
        {
            string normalized = name?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
            {
                error = "Gameplay Tag source cannot be empty.";
                return false;
            }

            List<GameplayTagSource> sources = CloneSources(settings.Sources);
            if (FindSource(sources, normalized) != null)
            {
                error = "Gameplay Tag source already exists: " + normalized;
                return false;
            }

            sources.Add(new GameplayTagSource(normalized, owner ?? string.Empty, readOnly));
            return TryApply(settings, CloneDefinitions(settings.Tags), CloneRedirects(settings.Redirects), sources, out error);
        }

        /// <summary>校验副本，只有全部通过才写回 <paramref name="settings"/>。</summary>
        public static bool TryApply(
            GameplayTagSettings settings,
            List<GameplayTagDefinition> tags,
            List<GameplayTagRedirect> redirects,
            List<GameplayTagSource> sources,
            out string error)
        {
            var errors = new List<string>();
            if (!GameplayTagSettings.Validate(tags, redirects, sources, errors))
            {
                error = errors[0];
                return false;
            }

            settings.ReplaceAll(tags, redirects, sources);
            error = null;
            return true;
        }

        public static List<GameplayTagDefinition> CloneDefinitions(IReadOnlyList<GameplayTagDefinition> source)
        {
            var result = new List<GameplayTagDefinition>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null)
                    result.Add(source[i].Clone());
            }
            return result;
        }

        public static List<GameplayTagRedirect> CloneRedirects(IReadOnlyList<GameplayTagRedirect> source)
        {
            var result = new List<GameplayTagRedirect>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null)
                    result.Add(new GameplayTagRedirect(source[i].OldName, source[i].NewName));
            }
            return result;
        }

        public static List<GameplayTagSource> CloneSources(IReadOnlyList<GameplayTagSource> source)
        {
            var result = new List<GameplayTagSource>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null)
                    result.Add(new GameplayTagSource(source[i].Name, source[i].Owner, source[i].ReadOnly));
            }
            return result;
        }

        /// <summary>把标签归入某个分组：分组必须可写，不存在时按需创建。</summary>
        private static bool TryUseSource(List<GameplayTagSource> sources, string name, out string error)
        {
            if (!TryEditSource(sources, name, out error))
                return false;

            if (!string.IsNullOrEmpty(name) && FindSource(sources, name) == null)
                sources.Add(new GameplayTagSource(name, string.Empty, false));
            return true;
        }

        /// <summary>确认某个分组下的标签允许改动，只读分组一律拒绝。</summary>
        private static bool TryEditSource(List<GameplayTagSource> sources, string name, out string error)
        {
            if (!IsSourceReadOnly(sources, name))
            {
                error = null;
                return true;
            }

            error = "Gameplay Tag source is read-only: " + name;
            return false;
        }

        public static bool IsSourceReadOnly(List<GameplayTagSource> sources, string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            GameplayTagSource existing = FindSource(sources, name);
            return existing != null && existing.ReadOnly;
        }

        private static GameplayTagSource FindSource(List<GameplayTagSource> sources, string name)
        {
            for (int i = 0; i < sources.Count; i++)
            {
                if (string.Equals(sources[i].Name, name, StringComparison.Ordinal))
                    return sources[i];
            }
            return null;
        }

        private static int FindTag(List<GameplayTagDefinition> tags, string name)
        {
            if (string.IsNullOrEmpty(name))
                return -1;

            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i].Name, name, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        private static bool ContainsTagIgnoringCase(List<GameplayTagDefinition> tags, string name)
        {
            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool HasDefinedChildren(List<GameplayTagDefinition> tags, string name)
        {
            for (int i = 0; i < tags.Count; i++)
            {
                string current = tags[i].Name;
                if (current.Length > name.Length && GameplayTagName.IsEqualOrChildOf(current, name))
                    return true;
            }
            return false;
        }

        private static void SetRedirect(List<GameplayTagRedirect> redirects, string oldName, string newName)
        {
            for (int i = 0; i < redirects.Count; i++)
            {
                if (string.Equals(redirects[i].OldName, oldName, StringComparison.Ordinal))
                {
                    redirects[i].Set(oldName, newName);
                    return;
                }
            }

            redirects.Add(new GameplayTagRedirect(oldName, newName));
        }

        /// <summary>丢弃链条最终落不到活标签上的重定向；删除操作会连锁产生这类孤儿。</summary>
        private static void PruneInvalidRedirects(
            List<GameplayTagRedirect> redirects,
            List<GameplayTagDefinition> tags)
        {
            bool removed;
            do
            {
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int i = 0; i < redirects.Count; i++)
                    map[redirects[i].OldName] = redirects[i].NewName;

                removed = redirects.RemoveAll(redirect => !ResolvesToDefinedTag(redirect.OldName, map, tags)) > 0;
            }
            while (removed);
        }

        private static bool ResolvesToDefinedTag(
            string oldName,
            Dictionary<string, string> redirects,
            List<GameplayTagDefinition> tags)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string current = oldName;
            while (redirects.TryGetValue(current, out string next))
            {
                if (!visited.Add(current))
                    return false;
                current = next;
            }

            for (int i = 0; i < tags.Count; i++)
            {
                if (GameplayTagName.IsEqualOrChildOf(tags[i].Name, current))
                    return true;
            }
            return false;
        }
    }

    [InitializeOnLoad]
    internal static class GameplayTagEditorBootstrap
    {
        static GameplayTagEditorBootstrap()
        {
            EditorApplication.delayCall += Initialize;
        }

        private static void Initialize()
        {
            GameplayTagSettings settings = GameplayTagEditorUtility.GetSettings(false);
            if (settings != null)
                GameplayTagEditorUtility.InitializeManager(settings, true);
        }
    }
}
