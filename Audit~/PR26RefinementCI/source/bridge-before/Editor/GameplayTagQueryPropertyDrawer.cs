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
            if (!property.isExpanded) return line;
            SerializedProperty root = property.FindPropertyRelative("m_RootExpression");
            float rootHeight = root == null || root.managedReferenceValue == null ? line : EditorGUI.GetPropertyHeight(root, true);
            return line * 3 + Spacing * 3 + rootHeight;
        }
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            float line = EditorGUIUtility.singleLineHeight;
            Rect row = new Rect(position.x, position.y, position.width, line);
            property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, label, true);
            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                row.y += line + Spacing;
                EditorGUI.PropertyField(row, property.FindPropertyRelative("m_UserDescription"), new GUIContent("Description"));
                row.y += line + Spacing;
                SerializedProperty root = property.FindPropertyRelative("m_RootExpression");
                if (root.managedReferenceValue == null)
                {
                    if (GUI.Button(row, "Create Root Expression", EditorStyles.miniButton)) root.managedReferenceValue = GameplayTagQueryExpression.AllExpressionsMatch();
                }
                else
                {
                    row.height = EditorGUI.GetPropertyHeight(root, true);
                    EditorGUI.PropertyField(row, root, new GUIContent("Root"), true);
                }
                row.y += row.height + Spacing;
                row.height = line;
                EditorGUI.LabelField(row, "Execution", "Freeze once after loading; source edits do not mutate existing matchers.");
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }
    }

    [CustomPropertyDrawer(typeof(GameplayTagQueryExpression))]
    internal sealed class GameplayTagQueryExpressionPropertyDrawer : PropertyDrawer
    {
        private const float Spacing = 2f;
        // This is a display-depth bound, not the runtime query's validated path limit.
        private const int MaxPropertyDepth = 96;
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight;
            if (property.depth > MaxPropertyDepth || property.managedReferenceValue == null || !property.isExpanded) return line;
            var type = (GameplayTagQueryExpressionType)property.FindPropertyRelative("m_Type").intValue;
            float height = line * 2 + Spacing;
            if (IsTags(type)) height += Spacing + EditorGUI.GetPropertyHeight(property.FindPropertyRelative("m_Tags"), true);
            else if (IsExpressions(type))
            {
                SerializedProperty children = property.FindPropertyRelative("m_Expressions");
                for (int i = 0; i < children.arraySize; i++) height += Spacing + EditorGUI.GetPropertyHeight(children.GetArrayElementAtIndex(i), true);
                height += Spacing + line;
            }
            return height;
        }
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.depth > MaxPropertyDepth)
            { EditorGUI.LabelField(position, "Expression", "Nested display limit; inspect or restructure this subtree separately."); return; }
            if (property.managedReferenceValue == null)
            {
                if (GUI.Button(position, "Create Expression", EditorStyles.miniButton)) property.managedReferenceValue = GameplayTagQueryExpression.AllTagsMatch();
                return;
            }
            EditorGUI.BeginProperty(position, label, property);
            float line = EditorGUIUtility.singleLineHeight;
            Rect row = new Rect(position.x, position.y, position.width, line);
            property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, label, true);
            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                row.y += line + Spacing;
                SerializedProperty type = property.FindPropertyRelative("m_Type");
                EditorGUI.PropertyField(row, type, new GUIContent("Type"));
                var expressionType = (GameplayTagQueryExpressionType)type.intValue;
                if (IsTags(expressionType))
                {
                    SerializedProperty tags = property.FindPropertyRelative("m_Tags");
                    row.y += line + Spacing;
                    row.height = EditorGUI.GetPropertyHeight(tags, true);
                    EditorGUI.PropertyField(row, tags, new GUIContent("Tags"), true);
                }
                else if (IsExpressions(expressionType))
                {
                    SerializedProperty children = property.FindPropertyRelative("m_Expressions");
                    bool removed = false;
                    for (int i = 0; i < children.arraySize; i++)
                    {
                        SerializedProperty child = children.GetArrayElementAtIndex(i);
                        row.y += row.height + Spacing;
                        row.height = EditorGUI.GetPropertyHeight(child, true);
                        EditorGUI.PropertyField(new Rect(row.x, row.y, row.width - 24f, row.height), child, new GUIContent("Expression " + i), true);
                        if (GUI.Button(new Rect(row.xMax - 22f, row.y, 22f, line), "−", EditorStyles.miniButton))
                        { children.DeleteArrayElementAtIndex(i); removed = true; break; }
                    }
                    row.y += row.height + Spacing;
                    row.height = line;
                    if (!removed && GUI.Button(row, "Add Child Expression", EditorStyles.miniButton))
                    {
                        int index = children.arraySize++;
                        children.GetArrayElementAtIndex(index).managedReferenceValue = GameplayTagQueryExpression.AllTagsMatch();
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }
        private static bool IsTags(GameplayTagQueryExpressionType type) => type >= GameplayTagQueryExpressionType.AnyTagsMatch && type <= GameplayTagQueryExpressionType.NoTagsMatch;
        private static bool IsExpressions(GameplayTagQueryExpressionType type) => type >= GameplayTagQueryExpressionType.AnyExpressionsMatch && type <= GameplayTagQueryExpressionType.NoExpressionsMatch;
    }
}
