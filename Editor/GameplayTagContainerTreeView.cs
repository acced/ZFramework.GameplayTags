using System;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace GameplayTags.Editor
{
   public class GameplayTagContainerTreeView : GameplayTagTreeViewBase
   {
      private static GUIContent s_TempContent = new();
      private SerializedProperty m_ExplicitTagsProperty;

      public GameplayTagContainerTreeView(TreeViewState treeViewState, SerializedProperty explicitTagsProperty)
      : base(treeViewState)
      {
         m_ExplicitTagsProperty = explicitTagsProperty;
         m_ExplicitTagsProperty.serializedObject.Update();

         ExpandIncludedTagItems();
         UpdateIncludedTags();
      }

      protected override void OnToolbarGUI()
      {
         if (ToolbarButton("Clear All"))
         {
            m_ExplicitTagsProperty.arraySize = 0;
            m_ExplicitTagsProperty.serializedObject.ApplyModifiedProperties();
            UpdateIncludedTags();
         }
      }

      protected override void RowGUI(RowGUIArgs args)
      {
         float indent = GetContentIndent(args.item);
         Rect rect = args.rowRect;
         rect.xMin += indent - (hasSearch ? 14 : 0);

         GameplayTagTreeViewItem item = args.item as GameplayTagTreeViewItem;
         using (new EditorGUI.DisabledGroupScope(item.IsIncluded && !item.IsExplicitIncluded))
         {
            s_TempContent.text = hasSearch ? item.Name : args.label;
            s_TempContent.tooltip = item.Description;

            EditorGUI.BeginChangeCheck();
            bool added = EditorGUI.ToggleLeft(rect, s_TempContent, item.IsIncluded);

            if (EditorGUI.EndChangeCheck())
            {
               if (added)
               {
                  m_ExplicitTagsProperty.InsertArrayElementAtIndex(0);
                  SerializedProperty element = m_ExplicitTagsProperty.GetArrayElementAtIndex(0);
                  element.stringValue = item.Name;
               }
               else
               {
                  for (int i = 0; i < m_ExplicitTagsProperty.arraySize; i++)
                  {
                     SerializedProperty element = m_ExplicitTagsProperty.GetArrayElementAtIndex(i);
                     if (string.Equals(element.stringValue, item.Name, StringComparison.Ordinal))
                     {
                        m_ExplicitTagsProperty.DeleteArrayElementAtIndex(i);
                        break;
                     }
                  }
               }

               m_ExplicitTagsProperty.serializedObject.ApplyModifiedProperties();
               m_ExplicitTagsProperty.serializedObject.Update();
               UpdateIncludedTags();
            }
         }
      }

      private void UpdateIncludedTags()
      {
         foreach (var item in AllTagItems)
         {
            item.IsExplicitIncluded = false;
            item.IsIncluded = false;
         }
         for (int i = 0; i < m_ExplicitTagsProperty.arraySize; i++)
         {
            var item = FindItem(m_ExplicitTagsProperty.GetArrayElementAtIndex(i).stringValue);
            if (item == null) continue;
            item.IsExplicitIncluded = true;
            for (var current = item; current != null; current = current.parent as GameplayTagTreeViewItem)
               current.IsIncluded = true;
         }
      }

      private void ExpandIncludedTagItems()
      {
         for (int i = 0; i < m_ExplicitTagsProperty.arraySize; i++)
         {
            var item = FindItem(m_ExplicitTagsProperty.GetArrayElementAtIndex(i).stringValue);
            for (var parent = item?.parent as GameplayTagTreeViewItem; parent != null;
               parent = parent.parent as GameplayTagTreeViewItem)
               SetExpanded(parent.id, true);
         }
      }
   }
}
