using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace GameplayTags.Editor
{
   public class GameplayTagTreeViewItem : TreeViewItem
   {
      public string Name { get; }
      public string Description { get; }
      public string DisplayName => GameplayTagUtility.GetLabel(Name);
      public bool IsIncluded { get; set; }
      public bool IsExplicitIncluded { get; set; }

      public GameplayTagTreeViewItem(int id, string name, string description)
         : base(id, GameplayTagUtility.GetHierarchyLevelFromName(name) - 1, GameplayTagUtility.GetLabel(name))
      {
         Name = name;
         Description = description;
      }
   }
   public abstract class GameplayTagTreeViewBase : TreeViewPopupContent.TreeView
   {
      public bool IsEmpty => m_IsEmpty;

      private static Styles s_Styles;
      private SearchField m_SearchField;
      private bool m_IsEmpty;
      private readonly Dictionary<string, GameplayTagTreeViewItem> itemsByName = new(StringComparer.Ordinal);
      protected IEnumerable<GameplayTagTreeViewItem> AllTagItems => itemsByName.Values;

      public GameplayTagTreeViewBase(TreeViewState treeViewState)
         : base(treeViewState)
      {
         m_SearchField = new SearchField();
         showAlternatingRowBackgrounds = true;

         Reload();
      }

      public override float GetTotalHeight()
      {
         return base.GetTotalHeight() + EditorStyles.toolbar.fixedHeight;
      }

      public override void OnGUI(Rect rect)
      {
         s_Styles ??= new Styles();

         Rect toolbarRect = rect;
         toolbarRect.height = EditorStyles.toolbar.fixedHeight;
         ToolbarGUI(toolbarRect);

         rect.yMin += toolbarRect.height;
         base.OnGUI(rect);
      }

      private void ToolbarGUI(Rect rect)
      {
         GUILayout.BeginArea(rect);
         GUILayout.BeginHorizontal(EditorStyles.toolbar);

         if (ToolbarButton("Expand All"))
            ExpandAll();

         if (ToolbarButton("Collapse All"))
            CollapseAll();

         OnToolbarGUI();

         searchString = m_SearchField.OnToolbarGUI(searchString);

         GUILayout.EndHorizontal();
         GUILayout.EndArea();
      }

      protected virtual void OnToolbarGUI()
      { }

      protected bool ToolbarButton(string text)
      {
         return GUILayout.Button(text, s_Styles.ToolbarButton, GUILayout.ExpandWidth(false));
      }

      protected override bool DoesItemMatchSearch(TreeViewItem item, string search)
      {
         GameplayTagTreeViewItem tagItem = item as GameplayTagTreeViewItem;
         return tagItem.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
      }

      protected override TreeViewItem BuildRoot()
      {
         TreeViewItem root = new(-2, -1, "<Root>");
         m_IsEmpty = true;

         List<TreeViewItem> items = new();

         itemsByName.Clear();
         root.children = new List<TreeViewItem>();
         int id = 1;
         foreach (var entry in GameplayTagEditorCatalog.Read())
         {
            var item = new GameplayTagTreeViewItem(id++, entry.Name, entry.Description);
            items.Add(item);
            itemsByName.Add(entry.Name, item);
            m_IsEmpty = false;
         }

         SetupParentsAndChildrenFromDepths(root, items);
         return root;
      }

      protected GameplayTagTreeViewItem FindItem(string name) =>
         name != null && itemsByName.TryGetValue(name, out var item) ? item : null;
      protected class Styles
      {
         public readonly GUIStyle SearchField;
         public readonly GUIStyle ToolbarButton;

         public Styles()
         {
            SearchField = new GUIStyle("SearchTextField");

            ToolbarButton = new GUIStyle(EditorStyles.toolbarButton);
            ToolbarButton.fontSize = 11;
         }
      }
   }
}


