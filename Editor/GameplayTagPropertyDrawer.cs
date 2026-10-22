using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace GameplayTags.Editor
{
   [CustomPropertyDrawer(typeof(GameplayTag))]
   public class GameplayTagPropertyDrawer : PropertyDrawer
   {
      private static GUIContent s_TempContent = new();

      public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
      {
         label = EditorGUI.BeginProperty(position, label, property);

         position = EditorGUI.PrefixLabel(position, label);

         int oldIndentLevel = EditorGUI.indentLevel;
         EditorGUI.indentLevel = 0;

         SerializedProperty nameProperty = property.FindPropertyRelative("m_Name");
         string name = nameProperty.stringValue;

         if (!string.IsNullOrEmpty(name))
         {
            s_TempContent.text = name;
            s_TempContent.tooltip = GameplayTagEditorCatalog.Description(name);
         }
         else
         {
            s_TempContent.text = "Select...";
            s_TempContent.tooltip = null;
         }

         if (EditorGUI.DropdownButton(position, s_TempContent, FocusType.Keyboard))
         {
            GameplayTagTreeView tagTreeView = new(new TreeViewState(), property, static () =>
            {
               EditorWindow.GetWindow<PopupWindow>().Close();
            });
            tagTreeView.ShowPopupWindow(position, 280f);
         }

         EditorGUI.indentLevel = oldIndentLevel;
         EditorGUI.EndProperty();
      }
   }
}

