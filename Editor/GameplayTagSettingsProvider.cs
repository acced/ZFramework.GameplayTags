using UnityEditor;
using UnityEngine;

namespace GameplayTags.Editor
{
    internal static class GameplayTagSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreateProvider() => new SettingsProvider("Project/ZFramework/Gameplay Tags", SettingsScope.Project)
        {
            label = "Gameplay Tags",
            guiHandler = DrawSettings,
            keywords = new[] { "Gameplay", "Tags", "GAS", "ZFramework" }
        };
        private static void DrawSettings(string searchContext)
        {
            GameplayTagSettings settings = GameplayTagEditorUtility.GetSettings(false);
            if (settings == null)
            {
                EditorGUILayout.HelpBox("Create GameplayTagSettings at " + GameplayTagSettings.DefaultAssetPath, MessageType.Warning);
                if (GUILayout.Button("Create Gameplay Tag Settings", GUILayout.Width(240f))) GameplayTagEditorUtility.CreateSettings();
                return;
            }
            using (var serialized = new SerializedObject(settings))
            {
                serialized.Update();
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(serialized.FindProperty("m_AutoInitialize"));
                EditorGUILayout.PropertyField(serialized.FindProperty("m_IncludeImplicitParentTagsInGeneratedCode"));
                EditorGUILayout.PropertyField(serialized.FindProperty("m_WarnOnInvalidSerializedTags"));
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Generated API", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serialized.FindProperty("m_GeneratedNamespace"));
                EditorGUILayout.PropertyField(serialized.FindProperty("m_GeneratedClassName"));
                EditorGUILayout.PropertyField(serialized.FindProperty("m_GeneratedCodePath"));
                if (EditorGUI.EndChangeCheck())
                {
                    serialized.ApplyModifiedProperties();
                    EditorUtility.SetDirty(settings);
                    GameplayTagEditorUtility.InitializeManager(settings, true);
                }
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Defined Tags", settings.Tags.Count.ToString());
            EditorGUILayout.LabelField("Registered Tags", GameplayTagManager.TagCount.ToString());
            EditorGUILayout.LabelField("Redirects", settings.Redirects.Count.ToString());
            if (GUILayout.Button("Show Diagnostic Content Hash", GUILayout.Width(240f)))
                EditorUtility.DisplayDialog("Gameplay Tags", "0x" + settings.ComputeContentHash().ToString("X8"), "OK");
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Tag Manager", GUILayout.Width(180f))) GameplayTagManagerWindow.Open();
            if (GUILayout.Button("Generate C# API", GUILayout.Width(180f))) GameplayTagCodeGenerator.GenerateInteractive(settings);
            EditorGUILayout.EndHorizontal();
        }
    }
}
