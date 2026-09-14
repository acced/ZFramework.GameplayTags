using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameplayTags.Editor
{
    public sealed class GameplayTagManagerWindow : EditorWindow
    {
        private enum Page
        {
            Tags,
            Redirects,
            Sources
        }

        private GameplayTagSettings m_Settings;
        private Page m_Page;
        private Vector2 m_ListScroll;
        private Vector2 m_DetailScroll;
        private string m_Search = string.Empty;
        private string m_SelectedName = string.Empty;
        private string m_NewTagName = string.Empty;
        private string m_NewRedirectOld = string.Empty;
        private string m_NewRedirectNew = string.Empty;
        private string m_NewSourceName = string.Empty;
        private string m_NewSourceOwner = string.Empty;
        private bool m_NewSourceReadOnly;
        private string m_EditName = string.Empty;
        private string m_EditComment = string.Empty;
        private int m_EditSourceIndex;
        private bool m_EditRestricted;
        private bool m_EditAllowChildren = true;

        [MenuItem("Window/ZFramework/Gameplay Tag Manager")]
        public static void Open()
        {
            GameplayTagManagerWindow window = GetWindow<GameplayTagManagerWindow>();
            window.titleContent = new GUIContent("Gameplay Tags");
            window.minSize = new Vector2(760f, 480f);
            window.Show();
        }

        private void OnEnable()
        {
            m_Settings = GameplayTagEditorUtility.GetSettings(false);
            if (m_Settings != null)
                GameplayTagEditorUtility.InitializeManager(m_Settings, false);
        }

        private void OnGUI()
        {
            if (m_Settings == null)
            {
                EditorGUILayout.HelpBox("GameplayTagSettings does not exist.", MessageType.Warning);
                if (GUILayout.Button("Create Settings", GUILayout.Width(180f)))
                    m_Settings = GameplayTagEditorUtility.CreateSettings();
                return;
            }

            DrawToolbar();
            switch (m_Page)
            {
                case Page.Tags:
                    DrawTagsPage();
                    break;
                case Page.Redirects:
                    DrawRedirectsPage();
                    break;
                case Page.Sources:
                    DrawSourcesPage();
                    break;
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            m_Page = (Page)GUILayout.Toolbar((int)m_Page, new[] { "Tags", "Redirects", "Sources" }, EditorStyles.toolbarButton, GUILayout.Width(300f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Import CSV", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                GameplayTagCsvUtility.ImportWithDialog(m_Settings);
            if (GUILayout.Button("Export CSV", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                GameplayTagCsvUtility.ExportWithDialog(m_Settings);
            if (GUILayout.Button("Generate", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                GameplayTagCodeGenerator.Generate(m_Settings);
            if (GUILayout.Button("Validate", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                GameplayTagBuildValidator.ValidateInteractive();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTagsPage()
        {
            EditorGUILayout.BeginHorizontal();
            DrawTagList();
            DrawTagDetails();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTagList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(350f));
            EditorGUILayout.BeginHorizontal();
            m_Search = EditorGUILayout.TextField(m_Search, GUI.skin.FindStyle("ToolbarSearchTextField"));
            if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(24f)))
                m_Search = string.Empty;
            EditorGUILayout.EndHorizontal();

            m_ListScroll = EditorGUILayout.BeginScrollView(m_ListScroll, EditorStyles.helpBox);
            IReadOnlyList<GameplayTagDefinition> tags = m_Settings.Tags;
            for (int i = 0; i < tags.Count; i++)
            {
                GameplayTagDefinition definition = tags[i];
                if (definition == null || !MatchesSearch(definition.Name, m_Search))
                    continue;

                int depth = GameplayTagName.GetDepth(definition.Name) - 1;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(depth * 14f);
                GUIStyle style = string.Equals(m_SelectedName, definition.Name, StringComparison.Ordinal)
                    ? EditorStyles.toolbarButton
                    : EditorStyles.miniButton;
                string label = GameplayTagName.GetLeaf(definition.Name);
                if (definition.Restricted)
                    label += "  [R]";
                if (GUILayout.Button(new GUIContent(label, definition.Name), style, GUILayout.ExpandWidth(true)))
                    SelectTag(definition);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Add Tag", EditorStyles.boldLabel);
            m_NewTagName = EditorGUILayout.TextField(m_NewTagName);
            if (GUILayout.Button("Add", GUILayout.Height(24f)))
                AddTag();
            EditorGUILayout.EndVertical();
        }

        private void DrawTagDetails()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            if (string.IsNullOrEmpty(m_SelectedName))
            {
                EditorGUILayout.HelpBox("Select a Gameplay Tag to edit its metadata.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            GameplayTagDefinition selected;
            if (!m_Settings.TryGetTagDefinition(m_SelectedName, out selected))
            {
                m_SelectedName = string.Empty;
                EditorGUILayout.EndVertical();
                return;
            }

            m_DetailScroll = EditorGUILayout.BeginScrollView(m_DetailScroll);
            EditorGUILayout.LabelField("Tag", EditorStyles.boldLabel);
            m_EditName = EditorGUILayout.TextField("Name", m_EditName);
            m_EditComment = EditorGUILayout.TextField("Developer Comment", m_EditComment);

            string[] sources = BuildSourceNames();
            m_EditSourceIndex = Mathf.Clamp(m_EditSourceIndex, 0, Math.Max(0, sources.Length - 1));
            if (sources.Length > 0)
                m_EditSourceIndex = EditorGUILayout.Popup("Source", m_EditSourceIndex, sources);
            else
                EditorGUILayout.HelpBox("No tag source exists. Add one on the Sources page.", MessageType.Warning);

            m_EditRestricted = EditorGUILayout.Toggle("Restricted", m_EditRestricted);
            using (new EditorGUI.DisabledScope(!m_EditRestricted))
                m_EditAllowChildren = EditorGUILayout.Toggle("Allow Non-restricted Children", m_EditAllowChildren);

            EditorGUILayout.Space();
            bool registered = GameplayTagManager.TryRequestTag(m_SelectedName, out GameplayTag runtimeTag);
            EditorGUILayout.LabelField("Registered", registered ? "Yes" : "No (settings not applied yet)");
            if (registered)
            {
                GameplayTag parent = runtimeTag.GetDirectParent();
                EditorGUILayout.LabelField("Parent", parent.IsValid ? parent.Name : "None");
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Metadata", GUILayout.Height(26f)))
                ApplyMetadata();
            if (GUILayout.Button("Rename Hierarchy", GUILayout.Height(26f)))
                RenameTag();
            EditorGUILayout.EndHorizontal();

            GUI.backgroundColor = new Color(1f, 0.65f, 0.65f);
            if (GUILayout.Button("Delete Tag Hierarchy", GUILayout.Height(26f)))
                DeleteTag();
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawRedirectsPage()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Gameplay Tag Redirects", EditorStyles.boldLabel);
            m_ListScroll = EditorGUILayout.BeginScrollView(m_ListScroll);
            IReadOnlyList<GameplayTagRedirect> redirects = m_Settings.Redirects;
            for (int i = 0; i < redirects.Count; i++)
            {
                GameplayTagRedirect redirect = redirects[i];
                if (redirect == null)
                    continue;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.SelectableLabel(redirect.OldName, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                GUILayout.Label("→", GUILayout.Width(22f));
                EditorGUILayout.SelectableLabel(redirect.NewName, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                {
                    Undo.RecordObject(m_Settings, "Remove Gameplay Tag Redirect");
                    GameplayTagEditorUtility.TryRemoveRedirect(m_Settings, redirect.OldName);
                    GameplayTagEditorUtility.SaveAndReinitialize(m_Settings);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Add or Replace Redirect", EditorStyles.boldLabel);
            m_NewRedirectOld = EditorGUILayout.TextField("Old Tag", m_NewRedirectOld);
            m_NewRedirectNew = EditorGUILayout.TextField("New Tag", m_NewRedirectNew);
            if (GUILayout.Button("Save Redirect", GUILayout.Height(25f)))
            {
                Undo.RecordObject(m_Settings, "Add Gameplay Tag Redirect");
                if (!GameplayTagEditorUtility.TryAddRedirect(m_Settings, m_NewRedirectOld, m_NewRedirectNew, out string error))
                    EditorUtility.DisplayDialog("Gameplay Tags", error, "OK");
                else
                {
                    m_NewRedirectOld = string.Empty;
                    m_NewRedirectNew = string.Empty;
                    GameplayTagEditorUtility.SaveAndReinitialize(m_Settings);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSourcesPage()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Gameplay Tag Sources", EditorStyles.boldLabel);
            IReadOnlyList<GameplayTagSource> sources = m_Settings.Sources;
            for (int i = 0; i < sources.Count; i++)
            {
                GameplayTagSource source = sources[i];
                if (source == null)
                    continue;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(source.Name, GUILayout.Width(220f));
                EditorGUILayout.LabelField(string.IsNullOrEmpty(source.Owner) ? "No owner" : source.Owner);
                GUILayout.Label(source.ReadOnly ? "Read-only" : "Editable", GUILayout.Width(80f));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Add Source", EditorStyles.boldLabel);
            m_NewSourceName = EditorGUILayout.TextField("Name", m_NewSourceName);
            m_NewSourceOwner = EditorGUILayout.TextField("Owner", m_NewSourceOwner);
            m_NewSourceReadOnly = EditorGUILayout.Toggle("Read-only", m_NewSourceReadOnly);
            if (GUILayout.Button("Add Source", GUILayout.Height(25f)))
            {
                Undo.RecordObject(m_Settings, "Add Gameplay Tag Source");
                if (!GameplayTagEditorUtility.TryAddSource(
                        m_Settings, m_NewSourceName, m_NewSourceOwner, m_NewSourceReadOnly, out string error))
                    EditorUtility.DisplayDialog("Gameplay Tags", error, "OK");
                else
                {
                    m_NewSourceName = string.Empty;
                    m_NewSourceOwner = string.Empty;
                    m_NewSourceReadOnly = false;
                    GameplayTagEditorUtility.SaveAndReinitialize(m_Settings);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void AddTag()
        {
            Undo.RecordObject(m_Settings, "Add Gameplay Tag");
            if (!GameplayTagEditorUtility.TryAddTag(m_Settings, m_NewTagName, out string error))
            {
                EditorUtility.DisplayDialog("Gameplay Tags", error, "OK");
                return;
            }

            string added = m_NewTagName.Trim();
            m_NewTagName = string.Empty;
            GameplayTagEditorUtility.SaveAndReinitialize(m_Settings);
            if (m_Settings.TryGetTagDefinition(added, out GameplayTagDefinition definition))
                SelectTag(definition);
        }

        private void ApplyMetadata()
        {
            Undo.RecordObject(m_Settings, "Edit Gameplay Tag");
            if (!GameplayTagEditorUtility.TrySetTagMetadata(
                m_Settings,
                m_SelectedName,
                m_EditComment,
                GetSelectedSourceName(),
                m_EditRestricted,
                m_EditAllowChildren,
                out string error))
            {
                EditorUtility.DisplayDialog("Gameplay Tags", error, "OK");
                return;
            }

            GameplayTagEditorUtility.SaveAndReinitialize(m_Settings);
        }

        private void RenameTag()
        {
            if (string.Equals(m_SelectedName, m_EditName, StringComparison.Ordinal))
                return;

            string oldName = m_SelectedName;
            Undo.RecordObject(m_Settings, "Rename Gameplay Tag Hierarchy");
            if (!GameplayTagEditorUtility.TryRenameTag(m_Settings, oldName, m_EditName, true, true, out string error))
            {
                EditorUtility.DisplayDialog("Gameplay Tags", error, "OK");
                return;
            }

            m_SelectedName = m_EditName.Trim();
            GameplayTagEditorUtility.SaveAndReinitialize(m_Settings);
            if (m_Settings.TryGetTagDefinition(m_SelectedName, out GameplayTagDefinition definition))
                SelectTag(definition);
        }

        private void DeleteTag()
        {
            if (!EditorUtility.DisplayDialog(
                "Delete Gameplay Tag",
                "Delete '" + m_SelectedName + "' and all of its explicitly defined children?",
                "Delete",
                "Cancel"))
                return;

            Undo.RecordObject(m_Settings, "Delete Gameplay Tag Hierarchy");
            if (!GameplayTagEditorUtility.TryRemoveTag(m_Settings, m_SelectedName, true, out string error))
            {
                EditorUtility.DisplayDialog("Gameplay Tags", error, "OK");
                return;
            }

            m_SelectedName = string.Empty;
            GameplayTagEditorUtility.SaveAndReinitialize(m_Settings);
        }

        private void SelectTag(GameplayTagDefinition definition)
        {
            m_SelectedName = definition.Name;
            m_EditName = definition.Name;
            m_EditComment = definition.DevComment;
            m_EditRestricted = definition.Restricted;
            m_EditAllowChildren = definition.AllowNonRestrictedChildren;

            string[] sources = BuildSourceNames();
            m_EditSourceIndex = 0;
            for (int i = 0; i < sources.Length; i++)
            {
                if (string.Equals(sources[i], definition.Source, StringComparison.Ordinal))
                {
                    m_EditSourceIndex = i;
                    break;
                }
            }
        }

        private string[] BuildSourceNames()
        {
            IReadOnlyList<GameplayTagSource> sources = m_Settings.Sources;
            string[] names = new string[sources.Count];
            for (int i = 0; i < sources.Count; i++)
                names[i] = sources[i] == null ? string.Empty : sources[i].Name;
            return names;
        }

        private string GetSelectedSourceName()
        {
            IReadOnlyList<GameplayTagSource> sources = m_Settings.Sources;
            return sources.Count == 0
                ? string.Empty
                : sources[Mathf.Clamp(m_EditSourceIndex, 0, sources.Count - 1)].Name;
        }

        private static bool MatchesSearch(string value, string search)
        {
            return string.IsNullOrEmpty(search) ||
                   value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
