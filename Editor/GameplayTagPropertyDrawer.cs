using UnityEditor;
using UnityEngine;

namespace GameplayTags.Editor
{
    [CustomPropertyDrawer(typeof(GameplayTag))]
    internal sealed class GameplayTagPropertyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            GameplayTagSettings settings = GameplayTagEditorUtility.GetSettings(false);
            if (settings != null)
                GameplayTagEditorUtility.InitializeManager(settings, false);

            EditorGUI.BeginProperty(position, label, property);
            position = EditorGUI.PrefixLabel(position, label);

            Rect clearRect = new Rect(position.xMax - 22f, position.y, 22f, position.height);
            Rect buttonRect = new Rect(position.x, position.y, position.width - 24f, position.height);
            SerializedProperty nameProperty = property.FindPropertyRelative("m_Name");
            string serializedName = nameProperty == null ? string.Empty : nameProperty.stringValue;

            GameplayTag current = GameplayTag.None;
            bool valid = settings != null &&
                         GameplayTagManager.TryRequestTag(serializedName, out current);
            Color oldColor = GUI.color;
            if (!string.IsNullOrEmpty(serializedName) && !valid)
                GUI.color = new Color(1f, 0.55f, 0.55f);

            string text = string.IsNullOrEmpty(serializedName)
                ? "None"
                : valid && !string.Equals(serializedName, current.Name, System.StringComparison.Ordinal)
                    ? current.Name + "  (redirected)"
                    : serializedName;
            if (GUI.Button(buttonRect, text, EditorStyles.popup))
            {
                Object target = property.serializedObject.targetObject;
                string path = property.propertyPath;
                GameplayTagPickerPopup.Show(buttonRect, current, delegate(GameplayTag selected)
                {
                    if (target == null)
                        return;
                    var serializedObject = new SerializedObject(target);
                    SerializedProperty tagProperty = serializedObject.FindProperty(path);
                    if (tagProperty == null)
                        return;
                    SetTag(tagProperty, selected);
                    serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(target);
                });
            }
            GUI.color = oldColor;

            if (GUI.Button(clearRect, "×", EditorStyles.miniButton))
                SetTag(property, GameplayTag.None);
            EditorGUI.EndProperty();
        }

        internal static void SetTag(SerializedProperty property, GameplayTag tag)
        {
            SerializedProperty name = property.FindPropertyRelative("m_Name");
            if (name != null)
                name.stringValue = tag.IsValid ? tag.Name : string.Empty;
        }
    }
}
