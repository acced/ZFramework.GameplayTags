using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameplayTags.Editor
{
    internal sealed class GameplayTagPickerPopup : PopupWindowContent
    {
        private readonly Action<GameplayTag> m_OnSelected;
        private readonly string m_SelectedName;
        private Vector2 m_Scroll;
        private string m_Search = string.Empty;

        private GameplayTagPickerPopup(GameplayTag selected, Action<GameplayTag> onSelected)
        {
            m_OnSelected = onSelected;
            m_SelectedName = selected.Name;
        }

        public static void Show(Rect activatorRect, GameplayTag selected, Action<GameplayTag> onSelected)
        {
            GameplayTagSettings settings = GameplayTagEditorUtility.GetSettings(false);
            if (settings == null)
            {
                EditorUtility.DisplayDialog(
                    "Gameplay Tags",
                    "Create GameplayTagSettings before selecting a tag.",
                    "OK");
                return;
            }

            GameplayTagEditorUtility.InitializeManager(settings, false);
            PopupWindow.Show(activatorRect, new GameplayTagPickerPopup(selected, onSelected));
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(420f, 500f);
        }

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            m_Search = EditorGUILayout.TextField(m_Search, GUI.skin.FindStyle("ToolbarSearchTextField"));
            if (GUILayout.Button("×", EditorStyles.toolbarButton, GUILayout.Width(25f)))
                m_Search = string.Empty;
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("None", m_SelectedName.Length == 0 ? EditorStyles.toolbarButton : EditorStyles.miniButton))
            {
                Select(GameplayTag.None);
                return;
            }

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            IReadOnlyList<GameplayTag> tags = GameplayTagManager.GetAllTags();
            for (int i = 0; i < tags.Count; i++)
            {
                GameplayTag tag = tags[i];
                if (!string.IsNullOrEmpty(m_Search) &&
                    tag.Name.IndexOf(m_Search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                int depth = GameplayTagName.GetDepth(tag.Name) - 1;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(depth * 14f);
                GUIStyle style = string.Equals(tag.Name, m_SelectedName, StringComparison.Ordinal)
                    ? EditorStyles.toolbarButton
                    : EditorStyles.miniButton;
                if (GUILayout.Button(new GUIContent(GameplayTagName.GetLeaf(tag.Name), tag.Name), style))
                {
                    Select(tag);
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private void Select(GameplayTag tag)
        {
            if (m_OnSelected != null)
                m_OnSelected(tag);
            editorWindow.Close();
        }
    }
}
