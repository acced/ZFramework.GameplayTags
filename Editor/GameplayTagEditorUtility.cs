using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GameplayTags.Editor
{
    /// <summary>Editor-only transactions. Callers record Undo; successful TryApply transfers ownership of its lists.</summary>
    public static class GameplayTagEditorUtility
    {
        private static GameplayTagSettings s_LastSettings;
        private static int s_LastRevision = int.MinValue;
        public static GameplayTagSettings GetSettings(bool createIfMissing)
        {
            var settings = AssetDatabase.LoadAssetAtPath<GameplayTagSettings>(GameplayTagSettings.DefaultAssetPath);
            return settings == null && createIfMissing ? CreateSettings() : settings;
        }
        public static GameplayTagSettings CreateSettings()
        {
            var existing = GetSettings(false);
            if (existing != null) return existing;
            EnsureAssetDirectory(Path.GetDirectoryName(GameplayTagSettings.DefaultAssetPath));
            var settings = ScriptableObject.CreateInstance<GameplayTagSettings>();
            if (!TryAddSource(settings, "Default", string.Empty, false, out string error)) throw new InvalidOperationException(error);
            AssetDatabase.CreateAsset(settings, GameplayTagSettings.DefaultAssetPath);
            AssetDatabase.SaveAssets();
            Selection.activeObject = settings;
            InitializeManager(settings, true);
            return settings;
        }
        public static void InitializeManager(GameplayTagSettings settings, bool force)
        {
            if (settings == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!force && GameplayTagManager.IsInitialized && ReferenceEquals(GameplayTagManager.Settings, settings) &&
                ReferenceEquals(settings, s_LastSettings) && settings.Revision == s_LastRevision) return;
            GameplayTagManager.ReinitializeForEditor(settings);
            s_LastSettings = settings;
            s_LastRevision = settings.Revision;
        }
        public static void SaveAndReinitialize(GameplayTagSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            InitializeManager(settings, true);
        }
        public static void EnsureAssetDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return;
            string[] segments = directory.Replace('\\', '/').Split('/');
            if (segments[0] != "Assets") throw new ArgumentException("Asset directories must start at Assets.", nameof(directory));
            for (int i = 1; i < segments.Length; i++) if (segments[i].Length == 0 || segments[i] == "." || segments[i] == "..")
                throw new ArgumentException("Asset directories cannot contain empty or relative segments.", nameof(directory));
            string current = "Assets";
            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, segments[i]);
                current = next;
            }
        }
        public static bool TryAddTag(GameplayTagSettings settings, string name, out string error) => TryAddTag(settings, name, string.Empty, "Default", false, true, out error);
        public static bool TryAddTag(GameplayTagSettings settings, string name, string devComment, string source, bool restricted, bool allowNonRestrictedChildren, out string error)
        {
            if (!GameplayTagName.TryNormalize(name, out string normalized, out error)) return false;
            var tags = CloneDefinitions(settings.Tags);
            if (FindTag(tags, normalized) >= 0) { error = "Gameplay Tag already exists: " + normalized; return false; }
            var sources = CloneSources(settings.Sources);
            source = source?.Trim() ?? string.Empty;
            if (!TryUseSource(sources, source, out error)) return false;
            tags.Add(new GameplayTagDefinition(normalized, devComment ?? string.Empty, source, restricted, allowNonRestrictedChildren));
            return TryApply(settings, tags, CloneRedirects(settings.Redirects), sources, out error);
        }
        public static bool TrySetTagMetadata(GameplayTagSettings settings, string name, string devComment, string source, bool restricted, bool allowNonRestrictedChildren, out string error)
        {
            if (!GameplayTagName.TryNormalize(name, out string normalized, out error)) return false;
            var tags = CloneDefinitions(settings.Tags);
            int index = FindTag(tags, normalized);
            if (index < 0) { error = "Gameplay Tag does not exist: " + normalized; return false; }
            var sources = CloneSources(settings.Sources);
            if (!TryEditSource(sources, tags[index].Source, out error)) return false;
            source = source?.Trim() ?? string.Empty;
            if (!TryUseSource(sources, source, out error)) return false;
            tags[index].SetMetadata(devComment ?? string.Empty, source, restricted, allowNonRestrictedChildren);
            return TryApply(settings, tags, CloneRedirects(settings.Redirects), sources, out error);
        }
        public static bool TryRenameTag(GameplayTagSettings settings, string oldName, string newName, bool renameChildren, bool createRedirects, out string error)
        {
            if (!GameplayTagName.TryNormalize(oldName, out string oldTag, out error) || !GameplayTagName.TryNormalize(newName, out string newTag, out error)) return false;
            var tags = CloneDefinitions(settings.Tags);
            if (FindTag(tags, oldTag) < 0) { error = "Gameplay Tag does not exist: " + oldTag; return false; }
            if (oldTag == newTag) { error = null; return true; }
            if (!renameChildren && HasDefinedChildren(tags, oldTag)) { error = "The tag has children; enable renameChildren."; return false; }
            if (GameplayTagName.IsEqualOrChildOf(newTag, oldTag) || GameplayTagName.IsEqualOrChildOf(oldTag, newTag))
            { error = "A hierarchy cannot be renamed into itself or one of its ancestors."; return false; }
            var sources = CloneSources(settings.Sources);
            var redirects = CloneRedirects(settings.Redirects);
            var explicitNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < tags.Count; i++) explicitNames.Add(tags[i].Name);
            var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string name in GameplayTagSettings.CollectResolvableNames(explicitNames))
                if (GameplayTagName.IsEqualOrChildOf(name, oldTag)) renamed.Add(name, newTag + name.Substring(oldTag.Length));
            for (int i = 0; i < tags.Count; i++)
            {
                if (!renamed.TryGetValue(tags[i].Name, out string replacement)) continue;
                if (!TryEditSource(sources, tags[i].Source, out error)) return false;
                tags[i].SetName(replacement);
            }
            // Existing incoming redirects survive a rename even when no additional aliases are requested.
            for (int i = 0; i < redirects.Count; i++)
                if (renamed.TryGetValue(redirects[i].NewName, out string replacement)) redirects[i].Set(redirects[i].OldName, replacement);
            if (createRedirects) foreach (var pair in renamed) SetRedirect(redirects, pair.Key, pair.Value);
            return TryApply(settings, tags, redirects, sources, out error);
        }
        public static bool TryRemoveTag(GameplayTagSettings settings, string name, bool removeChildren, out string error)
        {
            if (!GameplayTagName.TryNormalize(name, out string normalized, out error)) return false;
            var tags = CloneDefinitions(settings.Tags);
            if (FindTag(tags, normalized) < 0) { error = "Gameplay Tag does not exist: " + normalized; return false; }
            if (!removeChildren && HasDefinedChildren(tags, normalized)) { error = "The tag has children; enable removeChildren."; return false; }
            var sources = CloneSources(settings.Sources);
            for (int i = tags.Count - 1; i >= 0; i--)
            {
                if (!GameplayTagName.IsEqualOrChildOf(tags[i].Name, normalized)) continue;
                if (!TryEditSource(sources, tags[i].Source, out error)) return false;
                tags.RemoveAt(i);
            }
            var redirects = CloneRedirects(settings.Redirects);
            PruneInvalidRedirects(redirects, tags);
            return TryApply(settings, tags, redirects, sources, out error);
        }
        public static bool TryAddRedirect(GameplayTagSettings settings, string oldName, string newName, out string error)
        {
            if (!GameplayTagName.TryNormalize(oldName, out string oldTag, out error) || !GameplayTagName.TryNormalize(newName, out string newTag, out error)) return false;
            var redirects = CloneRedirects(settings.Redirects);
            SetRedirect(redirects, oldTag, newTag);
            return TryApply(settings, CloneDefinitions(settings.Tags), redirects, CloneSources(settings.Sources), out error);
        }
        public static bool TryRemoveRedirect(GameplayTagSettings settings, string oldName)
        {
            var redirects = CloneRedirects(settings.Redirects);
            int index = redirects.FindIndex(r => string.Equals(r.OldName, oldName, StringComparison.Ordinal));
            if (index < 0) return false;
            redirects.RemoveAt(index);
            var tags = CloneDefinitions(settings.Tags);
            PruneInvalidRedirects(redirects, tags);
            if (!TryApply(settings, tags, redirects, CloneSources(settings.Sources), out string error)) throw new InvalidOperationException(error);
            return true;
        }
        public static bool TryAddSource(GameplayTagSettings settings, string name, string owner, bool readOnly, out string error)
        {
            name = name?.Trim() ?? string.Empty;
            if (name.Length == 0) { error = "Source name cannot be empty."; return false; }
            var sources = CloneSources(settings.Sources);
            if (FindSource(sources, name) != null) { error = "Source already exists: " + name; return false; }
            sources.Add(new GameplayTagSource(name, owner ?? string.Empty, readOnly));
            return TryApply(settings, CloneDefinitions(settings.Tags), CloneRedirects(settings.Redirects), sources, out error);
        }
        public static bool TryApply(GameplayTagSettings settings, List<GameplayTagDefinition> tags, List<GameplayTagRedirect> redirects, List<GameplayTagSource> sources, out string error)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var errors = new List<string>();
            if (!GameplayTagSettings.Validate(tags, redirects, sources, errors)) { error = string.Join("\n", errors); return false; }
            settings.ReplaceAll(tags, redirects, sources);
            error = null;
            return true;
        }
        public static List<GameplayTagDefinition> CloneDefinitions(IReadOnlyList<GameplayTagDefinition> source)
        {
            var result = new List<GameplayTagDefinition>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] == null) throw new InvalidOperationException("Repair the null tag definition before editing this configuration.");
                result.Add(source[i].Clone());
            }
            return result;
        }
        public static List<GameplayTagRedirect> CloneRedirects(IReadOnlyList<GameplayTagRedirect> source)
        {
            var result = new List<GameplayTagRedirect>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] == null) throw new InvalidOperationException("Repair the null redirect before editing this configuration.");
                result.Add(new GameplayTagRedirect(source[i].OldName, source[i].NewName));
            }
            return result;
        }
        public static List<GameplayTagSource> CloneSources(IReadOnlyList<GameplayTagSource> source)
        {
            var result = new List<GameplayTagSource>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] == null) throw new InvalidOperationException("Repair the null source before editing this configuration.");
                result.Add(new GameplayTagSource(source[i].Name, source[i].Owner, source[i].ReadOnly));
            }
            return result;
        }
        private static bool TryUseSource(List<GameplayTagSource> sources, string name, out string error)
        {
            if (!TryEditSource(sources, name, out error)) return false;
            if (name.Length > 0 && FindSource(sources, name) == null) sources.Add(new GameplayTagSource(name, string.Empty, false));
            return true;
        }
        private static bool TryEditSource(List<GameplayTagSource> sources, string name, out string error)
        { error = IsSourceReadOnly(sources, name) ? "Gameplay Tag source is read-only: " + name : null; return error == null; }
        public static bool IsSourceReadOnly(List<GameplayTagSource> sources, string name) => FindSource(sources, name)?.ReadOnly ?? false;
        private static GameplayTagSource FindSource(List<GameplayTagSource> sources, string name)
        { for (int i = 0; i < sources.Count; i++) if (sources[i].Name == name) return sources[i]; return null; }
        private static int FindTag(List<GameplayTagDefinition> tags, string name)
        { for (int i = 0; i < tags.Count; i++) if (tags[i].Name == name) return i; return -1; }
        private static bool HasDefinedChildren(List<GameplayTagDefinition> tags, string name)
        { for (int i = 0; i < tags.Count; i++) if (tags[i].Name.Length > name.Length && GameplayTagName.IsEqualOrChildOf(tags[i].Name, name)) return true; return false; }
        private static void SetRedirect(List<GameplayTagRedirect> redirects, string oldName, string newName)
        {
            for (int i = 0; i < redirects.Count; i++) if (redirects[i].OldName == oldName) { redirects[i].Set(oldName, newName); return; }
            redirects.Add(new GameplayTagRedirect(oldName, newName));
        }
        private static void PruneInvalidRedirects(List<GameplayTagRedirect> redirects, List<GameplayTagDefinition> tags)
        {
            var explicitNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < tags.Count; i++) explicitNames.Add(tags[i].Name);
            var active = GameplayTagSettings.CollectResolvableNames(explicitNames);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < redirects.Count; i++) map[redirects[i].OldName] = redirects[i].NewName;
            var known = new Dictionary<string, bool>(StringComparer.Ordinal);
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var path = new List<string>();
            foreach (string source in map.Keys)
            {
                visiting.Clear(); path.Clear(); string current = source; bool valid;
                while (true)
                {
                    if (known.TryGetValue(current, out valid)) break;
                    if (!map.TryGetValue(current, out string next)) { valid = active.Contains(current); break; }
                    if (!visiting.Add(current)) { valid = false; break; }
                    path.Add(current); current = next;
                }
                for (int i = 0; i < path.Count; i++) known[path[i]] = valid;
            }
            redirects.RemoveAll(r => !known[r.OldName]);
        }
    }
    [InitializeOnLoad]
    internal static class GameplayTagEditorBootstrap
    {
        static GameplayTagEditorBootstrap()
        {
            EditorApplication.delayCall += Refresh;
            Undo.undoRedoPerformed += Refresh;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }
        private static void OnPlayModeChanged(PlayModeStateChange state)
        { if (state == PlayModeStateChange.EnteredEditMode) Refresh(); }
        private static void Refresh()
        {
            var settings = GameplayTagEditorUtility.GetSettings(false);
            if (settings == null) return;
            try { GameplayTagEditorUtility.InitializeManager(settings, true); }
            catch (GameplayTagRegistryException error) { Debug.LogError(error.Message); }
        }
    }
}
