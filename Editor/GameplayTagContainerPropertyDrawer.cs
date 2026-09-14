using UnityEditor;
using UnityEngine;

namespace GameplayTags.Editor
{
    [CustomPropertyDrawer(typeof(GameplayTagContainer))]
    internal sealed class GameplayTagContainerPropertyDrawer : PropertyDrawer
    {
        private const float Spacing = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
                return line;

            SerializedProperty tags = property.FindPropertyRelative("m_GameplayTags");
            int count = tags == null ? 0 : tags.arraySize;
            return line + Spacing + count * (line + Spacing) + line + Spacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            float line = EditorGUIUtility.singleLineHeight;
            Rect row = new Rect(position.x, position.y, position.width, line);
            property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, label, true);
            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            SerializedProperty tags = property.FindPropertyRelative("m_GameplayTags");
            if (tags == null)
            {
                EditorGUI.EndProperty();
                return;
            }

            EditorGUI.indentLevel++;
            for (int i = 0; i < tags.arraySize; i++)
            {
                row.y += line + Spacing;
                Rect fieldRect = new Rect(row.x, row.y, row.width - 24f, line);
                Rect removeRect = new Rect(row.xMax - 22f, row.y, 22f, line);
                EditorGUI.PropertyField(fieldRect, tags.GetArrayElementAtIndex(i), new GUIContent("Tag " + i));
                if (GUI.Button(removeRect, "−", EditorStyles.miniButton))
                {
                    tags.DeleteArrayElementAtIndex(i);
                    break;
                }
            }

            row.y += line + Spacing;
            if (GUI.Button(row, "Add Gameplay Tag", EditorStyles.miniButton))
            {
                int index = tags.arraySize;
                tags.arraySize++;
                GameplayTagPropertyDrawer.SetTag(tags.GetArrayElementAtIndex(index), GameplayTag.None);
            }
            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }
    }
}
