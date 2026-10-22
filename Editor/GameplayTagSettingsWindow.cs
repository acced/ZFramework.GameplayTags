using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace GameplayTags.Editor
{
    public sealed class GameplayTagSettingsWindow : EditorWindow
    {
        [SerializeField] private TreeViewState treeState = new TreeViewState();
        [SerializeField] private string selectedName;
        [SerializeField] private string tagName = "";
        [SerializeField] private string description = "";
        private GameplayTagSettings settings;
        private SettingsTree tree;
        private SearchField search;
        private string error;

        [MenuItem("Tools/Gameplay Tags/Tag Settings")]
        public static void Open()
        {
            var window = GetWindow<GameplayTagSettingsWindow>("Gameplay Tags");
            window.minSize = new Vector2(580, 480);
            window.Show();
        }

        private void OnEnable()
        {
            search = new SearchField();
            tree = new SettingsTree(treeState, SelectTag);
            Undo.undoRedoPerformed += Refresh;
            EditorApplication.projectChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Refresh;
            EditorApplication.projectChanged -= Refresh;
        }

        private void Refresh()
        {
            try
            {
                settings = GameplayTagSettings.Find();
                tree.SetItems(GameplayTagEditorCatalog.Read());
                if (selectedName != null && !tree.Contains(selectedName)) selectedName = null;
                if (selectedName != null)
                {
                    SelectTag(selectedName);
                    tree.SelectName(selectedName);
                }
                error = null;
            }
            catch (Exception exception) { error = exception.Message; }
            Repaint();
        }

        private void SelectTag(string name)
        {
            selectedName = name;
            tagName = name;
            GameplayTagSettings.Entry entry = settings == null ? null :
                settings.Entries.FirstOrDefault(item => item.Name == name);
            description = entry?.Description ?? "";
        }

        private void OnGUI()
        {
            if (settings == null)
            {
                EditorGUILayout.HelpBox(error ?? "创建一份独立标签配置，再编辑标签并生成代码。", MessageType.Info);
                if (GUILayout.Button("创建标签配置")) Run(() => { settings = GameplayTagSettings.GetOrCreate(); Refresh(); });
                return;
            }

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("标签配置", settings, typeof(GameplayTagSettings), false);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("展开", EditorStyles.toolbarButton)) tree.ExpandAll();
                if (GUILayout.Button("折叠", EditorStyles.toolbarButton)) tree.CollapseAll();
                tree.searchString = search.OnToolbarGUI(tree.searchString);
                if (GUILayout.Button("生成代码", EditorStyles.toolbarButton))
                    Run(() => { AssetDatabase.SaveAssets(); GameplayTagRegistryGenerator.Generate(); });
            }

            Rect treeRect = GUILayoutUtility.GetRect(0, 10000, 150, 10000, GUILayout.ExpandHeight(true));
            tree.OnGUI(treeRect);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("新增标签"))
                    {
                        selectedName = null; tagName = ""; description = "";
                        tree.SetSelection(Array.Empty<int>());
                    }
                    using (new EditorGUI.DisabledScope(selectedName == null))
                    {
                        if (GUILayout.Button("新增子标签"))
                        {
                            tagName = selectedName + "."; selectedName = null; description = "";
                        }
                        if (GUILayout.Button("删除分支") &&
                            EditorUtility.DisplayDialog("删除标签分支", "删除 " + selectedName + " 及其所有子标签？", "删除", "取消"))
                            Run(() => { settings.RemoveBranch(selectedName); selectedName = null; SaveAndRefresh(); });
                    }
                }
                tagName = EditorGUILayout.TextField("完整名称", tagName);
                description = EditorGUILayout.TextField("说明", description);
                if (GUILayout.Button(selectedName == null ? "添加标签" : "保存修改"))
                    Run(() =>
                    {
                        settings.SaveTag(selectedName, tagName.Trim(), description);
                        selectedName = tagName.Trim();
                        SaveAndRefresh();
                        tree.SelectName(selectedName);
                    });
            }
            EditorGUILayout.HelpBox("灰色节点是自动补齐的父标签。保存父节点可配置说明；重命名会更新整个分支，但不会修改场景、资产和代码中的旧引用。修改后请生成代码。", MessageType.Info);
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        private void SaveAndRefresh() { AssetDatabase.SaveAssets(); Refresh(); }
        private void Run(Action action)
        {
            try { action(); error = null; }
            catch (Exception exception) { error = exception.Message; }
        }

        private sealed class SettingsTree : TreeView
        {
            private sealed class Item : TreeViewItem
            {
                public GameplayTagEditorCatalog.Item Definition;
            }
            private List<GameplayTagEditorCatalog.Item> items = new List<GameplayTagEditorCatalog.Item>();
            private readonly Action<string> select;
            private readonly Dictionary<string, int> ids = new Dictionary<string, int>(StringComparer.Ordinal);
            private int nextId = 1;

            public SettingsTree(TreeViewState state, Action<string> select) : base(state)
            {
                this.select = select;
                showAlternatingRowBackgrounds = true;
                Reload();
            }

            public void SetItems(List<GameplayTagEditorCatalog.Item> value) { items = value; Reload(); }
            public bool Contains(string name) => items.Exists(item => item.Name == name);
            public void SelectName(string name)
            {
                if (ids.TryGetValue(name, out int id)) SetSelection(new[] { id }, TreeViewSelectionOptions.RevealAndFrame);
            }
            protected override bool CanMultiSelect(TreeViewItem item) => false;
            protected override TreeViewItem BuildRoot()
            {
                var root = new TreeViewItem(0, -1, "Root") { children = new List<TreeViewItem>() };
                var rows = new List<TreeViewItem>();
                foreach (var entry in items)
                {
                    if (!ids.TryGetValue(entry.Name, out int id)) ids.Add(entry.Name, id = nextId++);
                    rows.Add(new Item
                    {
                        id = id, depth = GameplayTagUtility.GetHierarchyLevelFromName(entry.Name) - 1,
                        displayName = entry.Label, Definition = entry
                    });
                }
                SetupParentsAndChildrenFromDepths(root, rows);
                return root;
            }
            protected override bool DoesItemMatchSearch(TreeViewItem item, string value) =>
                ((Item)item).Definition.Name.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
            protected override void SelectionChanged(IList<int> selection)
            {
                if (selection.Count > 0 && FindItem(selection[0], rootItem) is Item item) select(item.Definition.Name);
            }
            protected override void RowGUI(RowGUIArgs args)
            {
                var item = (Item)args.item;
                Rect rect = args.rowRect;
                rect.xMin += GetContentIndent(item);
                GUIContent content = new GUIContent(hasSearch ? item.Definition.Name : item.displayName, item.Definition.Description);
                Color previousColor = GUI.color;
                if (!item.Definition.IsExplicit) GUI.color = new Color(previousColor.r, previousColor.g, previousColor.b, 0.6f);
                GUI.Label(rect, content, item.Definition.IsExplicit ? EditorStyles.label : EditorStyles.miniLabel);
                GUI.color = previousColor;
            }
        }
    }
}
