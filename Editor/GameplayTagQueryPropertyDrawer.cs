using UnityEditor;
using UnityEngine;

namespace GameplayTags.Editor
{
    [CustomPropertyDrawer(typeof(GameplayTagQuery))]
    internal sealed class GameplayTagQueryPropertyDrawer : PropertyDrawer
    {
        private const float Spacing = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
                return line;

            SerializedProperty root = property.FindPropertyRelative("m_RootExpression");
            float rootHeight = root == null || root.managedReferenceValue == null
                ? line
                : EditorGUI.GetPropertyHeight(root, true);
            return line + Spacing + line + Spacing + rootHeight + Spacing + line;
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

            EditorGUI.indentLevel++;
            SerializedProperty description = property.FindPropertyRelative("m_UserDescription");
            SerializedProperty root = property.FindPropertyRelative("m_RootExpression");

            row.y += line + Spacing;
            EditorGUI.PropertyField(row, description, new GUIContent("Description"));
            row.y += line + Spacing;

            if (root.managedReferenceValue == null)
            {
                if (GUI.Button(row, "Create Root Expression", EditorStyles.miniButton))
                    root.managedReferenceValue = GameplayTagQueryExpression.AllExpressionsMatch();
            }
            else
            {
                float rootHeight = EditorGUI.GetPropertyHeight(root, true);
                Rect rootRect = new Rect(row.x, row.y, row.width, rootHeight);
                EditorGUI.PropertyField(rootRect, root, new GUIContent("Root"), true);
                row.y += rootHeight - line;
            }

            // 表达式树就是求值时用的东西，直接打印即可，不存在编译产物与源不同步的问题。
            row.y += line + Spacing;
            EditorGUI.LabelField(
                row,
                "Expression",
                root.managedReferenceValue is GameplayTagQueryExpression expression
                    ? expression.ToString()
                    : "<empty>");

            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }
    }

    [CustomPropertyDrawer(typeof(GameplayTagQueryExpression))]
    internal sealed class GameplayTagQueryExpressionPropertyDrawer : PropertyDrawer
    {
        private const float Spacing = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight;
            if (property.managedReferenceValue == null)
                return line;

            SerializedProperty type = property.FindPropertyRelative("m_Type");
            GameplayTagQueryExpressionType expressionType =
                (GameplayTagQueryExpressionType)type.enumValueIndex;

            float height = line;
            if (IsTagExpression(expressionType))
            {
                SerializedProperty tags = property.FindPropertyRelative("m_Tags");
                height += Spacing + EditorGUI.GetPropertyHeight(tags, true);
            }
            else if (IsExpressionExpression(expressionType))
            {
                SerializedProperty expressions = property.FindPropertyRelative("m_Expressions");
                for (int i = 0; i < expressions.arraySize; i++)
                    height += Spacing + EditorGUI.GetPropertyHeight(expressions.GetArrayElementAtIndex(i), true);
                height += Spacing + line;
            }
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.managedReferenceValue == null)
            {
                if (GUI.Button(position, "Create Expression", EditorStyles.miniButton))
                    property.managedReferenceValue = GameplayTagQueryExpression.AllTagsMatch();
                return;
            }

            EditorGUI.BeginProperty(position, label, property);
            float line = EditorGUIUtility.singleLineHeight;
            Rect row = new Rect(position.x, position.y, position.width, line);
            SerializedProperty type = property.FindPropertyRelative("m_Type");
            EditorGUI.PropertyField(row, type, label);
            GameplayTagQueryExpressionType expressionType =
                (GameplayTagQueryExpressionType)type.enumValueIndex;

            if (IsTagExpression(expressionType))
            {
                SerializedProperty tags = property.FindPropertyRelative("m_Tags");
                float height = EditorGUI.GetPropertyHeight(tags, true);
                row.y += line + Spacing;
                row.height = height;
                EditorGUI.PropertyField(row, tags, new GUIContent("Tags"), true);
            }
            else if (IsExpressionExpression(expressionType))
            {
                SerializedProperty expressions = property.FindPropertyRelative("m_Expressions");
                for (int i = 0; i < expressions.arraySize; i++)
                {
                    SerializedProperty child = expressions.GetArrayElementAtIndex(i);
                    float childHeight = EditorGUI.GetPropertyHeight(child, true);
                    row.y += row.height + Spacing;
                    row.height = childHeight;
                    Rect fieldRect = new Rect(row.x, row.y, row.width - 24f, childHeight);
                    Rect removeRect = new Rect(row.xMax - 22f, row.y, 22f, line);
                    EditorGUI.PropertyField(fieldRect, child, new GUIContent("Expression " + i), true);
                    if (GUI.Button(removeRect, "−", EditorStyles.miniButton))
                    {
                        expressions.DeleteArrayElementAtIndex(i);
                        break;
                    }
                }

                row.y += row.height + Spacing;
                row.height = line;
                if (GUI.Button(row, "Add Child Expression", EditorStyles.miniButton))
                {
                    int index = expressions.arraySize;
                    expressions.arraySize++;
                    expressions.GetArrayElementAtIndex(index).managedReferenceValue =
                        GameplayTagQueryExpression.AllTagsMatch();
                }
            }

            EditorGUI.EndProperty();
        }

        private static bool IsTagExpression(GameplayTagQueryExpressionType type)
        {
            return type == GameplayTagQueryExpressionType.AnyTagsMatch ||
                   type == GameplayTagQueryExpressionType.AllTagsMatch ||
                   type == GameplayTagQueryExpressionType.NoTagsMatch;
        }

        private static bool IsExpressionExpression(GameplayTagQueryExpressionType type)
        {
            return type == GameplayTagQueryExpressionType.AnyExpressionsMatch ||
                   type == GameplayTagQueryExpressionType.AllExpressionsMatch ||
                   type == GameplayTagQueryExpressionType.NoExpressionsMatch;
        }
    }
}
