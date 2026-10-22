using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameplayTags.Editor
{
    /// <summary>Editor-only authoring data. Generated code is the runtime representation.</summary>
    public sealed class GameplayTagSettings : ScriptableObject
    {
        public const string DefaultPath = "Assets/GameplayTags/Editor/GameplayTagSettings.asset";

        [Serializable]
        public sealed class Entry
        {
            public string Name;
            public string Description;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();
        public IReadOnlyList<Entry> Entries => entries;

        public GameplayTagRegistration[] CreateRegistrations()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<GameplayTagRegistration>();
            foreach (Entry entry in entries)
            {
                if (entry == null) throw new InvalidOperationException("标签配置包含空记录。");
                GameplayTagUtility.ValidateName(entry.Name);
                if (!names.Add(entry.Name))
                    throw new InvalidOperationException("重复标签：" + entry.Name);
                result.Add(new GameplayTagRegistration(entry.Name, entry.Description));
            }
            return result.OrderBy(tag => tag.Name, StringComparer.Ordinal).ToArray();
        }

        // Validate the complete proposed edit before changing the asset or creating Undo state.
        public void SaveTag(string originalName, string name, string description)
        {
            GameplayTagUtility.ValidateName(name);
            if (originalName != null && name.StartsWith(originalName + ".", StringComparison.Ordinal))
                throw new InvalidOperationException("不能将分支移动到它自己的子节点下。");
            var proposed = new List<Entry>();
            bool replaced = false;
            foreach (Entry entry in entries)
            {
                string nextName = entry.Name;
                if (originalName != null && (nextName == originalName ||
                    nextName.StartsWith(originalName + ".", StringComparison.Ordinal)))
                    nextName = name + nextName.Substring(originalName.Length);
                bool isTarget = originalName != null && entry.Name == originalName;
                proposed.Add(new Entry
                {
                    Name = nextName,
                    Description = isTarget ? description : entry.Description
                });
                replaced |= isTarget;
            }
            if (!replaced) proposed.Add(new Entry { Name = name, Description = description });
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Entry entry in proposed)
            {
                GameplayTagUtility.ValidateName(entry.Name);
                if (!names.Add(entry.Name)) throw new InvalidOperationException("重复标签：" + entry.Name);
            }
            Undo.RecordObject(this, "编辑 Gameplay Tag");
            entries = proposed;
            EditorUtility.SetDirty(this);
        }

        public void RemoveBranch(string name)
        {
            Undo.RecordObject(this, "删除 Gameplay Tag 分支");
            entries.RemoveAll(entry => entry.Name == name ||
                entry.Name.StartsWith(name + ".", StringComparison.Ordinal));
            EditorUtility.SetDirty(this);
        }

        public static GameplayTagSettings Find()
        {
            string[] paths = AssetDatabase.FindAssets("t:GameplayTagSettings", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            if (paths.Length > 1)
                throw new InvalidOperationException("项目只使用一份标签配置，请保留一个 GameplayTagSettings：\n" +
                    string.Join("\n", paths));
            return paths.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<GameplayTagSettings>(paths[0]);
        }

        public static GameplayTagSettings GetOrCreate()
        {
            GameplayTagSettings settings = Find();
            if (settings != null) return settings;
            Directory.CreateDirectory(Path.GetDirectoryName(DefaultPath));
            AssetDatabase.Refresh();
            if (AssetDatabase.LoadMainAssetAtPath(DefaultPath) != null || File.Exists(DefaultPath))
                throw new InvalidOperationException("配置路径已被其他文件占用：" + DefaultPath);
            settings = CreateInstance<GameplayTagSettings>();
            AssetDatabase.CreateAsset(settings, DefaultPath);
            AssetDatabase.SaveAssets();
            return settings;
        }
    }

    [CustomEditor(typeof(GameplayTagSettings))]
    internal sealed class GameplayTagSettingsInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("标签配置用于生成注册表和访问字段，不会进入 Player。", MessageType.Info);
            EditorGUILayout.LabelField("显式标签", ((GameplayTagSettings)target).Entries.Count.ToString());
            if (GUILayout.Button("打开标签编辑器")) GameplayTagSettingsWindow.Open();
        }
    }
}
