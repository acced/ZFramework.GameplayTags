// Audit-only API facade. NEVER include this file in Unity or a Player assembly.
// GUI/serialization/Undo declarations provide compile shapes only, NOT Unity behavior.
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object { public string name; public static void DestroyImmediate(Object value) { } }
    public class ScriptableObject : Object { public static T CreateInstance<T>() where T : ScriptableObject => (T)Activator.CreateInstance(typeof(T), true); }
    public class MonoBehaviour : Object { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeReference : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class CreateAssetMenuAttribute : Attribute { public string fileName, menuName; }
    [AttributeUsage(AttributeTargets.Method)] public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { } }
    public enum RuntimeInitializeLoadType { BeforeSceneLoad, SubsystemRegistration }
    public interface ISerializationCallbackReceiver { void OnBeforeSerialize(); void OnAfterDeserialize(); }
    public static class Resources
    {
        public static readonly Dictionary<string, Object> Values = new Dictionary<string, Object>();
        public static T Load<T>(string path) where T : Object => Values.TryGetValue(path, out var value) ? value as T : null;
    }
    public static class Debug { public static void Log(object value) { } public static void LogWarning(object value) { } public static void LogError(object value) { } public static void LogException(Exception value) { } }
    public static class Application { public static string dataPath => System.IO.Path.Combine(Environment.CurrentDirectory, "Assets"); public static bool isPlaying; }
    public struct Vector2 { public float x,y; public Vector2(float x,float y) { this.x=x; this.y=y; } }
    public struct Rect { public float x,y,width,height; public float xMax => x+width; public Rect(float x,float y,float width,float height) { this.x=x; this.y=y; this.width=width; this.height=height; } }
    public struct Color { public Color(float r,float g,float b) { } public static Color white => default; }
    public class GUIContent { public GUIContent(string text) { } public GUIContent(string text,string tooltip) { } }
    public class GUIStyle { }
    public class GUISkin { public GUIStyle FindStyle(string name) => new GUIStyle(); }
    public static class GUI { public static Color color, backgroundColor; public static GUISkin skin=new GUISkin(); public static bool Button(Rect r,string text,GUIStyle style)=>false; }
    public class GUILayoutOption { }
    public static class GUILayout
    {
        public static bool Button(string text,params object[] options)=>false;
        public static bool Button(GUIContent content,params object[] options)=>false;
        public static GUILayoutOption Width(float v)=>null; public static GUILayoutOption Height(float v)=>null; public static GUILayoutOption ExpandWidth(bool v)=>null;
        public static void Space(float v) { } public static void FlexibleSpace() { } public static void Label(string value,params object[] options) { }
        public static int Toolbar(int selected,string[] labels,params object[] options)=>selected;
    }
    public static class Mathf { public static int Clamp(int v,int lo,int hi)=>Math.Max(lo,Math.Min(hi,v)); }
    public static class GUIUtility { public static void ExitGUI() { } }
}
namespace UnityEditor
{
    using UnityEngine;
    using Object=UnityEngine.Object;
    [AttributeUsage(AttributeTargets.Method)] public sealed class MenuItem : Attribute { public MenuItem(string value) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class InitializeOnLoadAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class InitializeOnEnterPlayModeAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class CustomPropertyDrawer : Attribute { public CustomPropertyDrawer(Type type) { } }
    [AttributeUsage(AttributeTargets.Method)] public sealed class SettingsProviderAttribute : Attribute { }
    public enum SettingsScope { Project }
    public enum ImportAssetOptions { ForceUpdate }
    public enum MessageType { Warning, Info, Error }
    public enum PlayModeStateChange { EnteredEditMode, ExitingEditMode, EnteredPlayMode, ExitingPlayMode }
    public enum EnterPlayModeOptions { None, DisableDomainReload, DisableSceneReload }
    public class SettingsProvider { public string label; public Action<string> guiHandler; public IEnumerable<string> keywords; public SettingsProvider(string path,SettingsScope scope) { } }
    public static class EditorApplication { public static Action delayCall; public static Action<PlayModeStateChange> playModeStateChanged; public static bool isPlayingOrWillChangePlaymode; }
    public static class Undo { public static Action undoRedoPerformed; public static void RecordObject(Object obj,string label) { } }
    public static class Selection { public static Object activeObject; }
    public static class AssetDatabase
    {
        public static readonly Dictionary<string,Object> Values=new Dictionary<string,Object>();
        public static T LoadAssetAtPath<T>(string path) where T:Object => Values.TryGetValue(path,out var v) ? v as T : null;
        public static string[] FindAssets(string filter)=>new List<string>(Values.Keys).ToArray();
        public static string GUIDToAssetPath(string guid)=>guid;
        public static bool IsValidFolder(string path)=>System.IO.Directory.Exists(path);
        public static string CreateFolder(string parent,string name) { string p=System.IO.Path.Combine(parent,name); System.IO.Directory.CreateDirectory(p); return p; }
        public static void CreateAsset(Object obj,string path) { Values.Add(path,obj); }
        public static string GetAssetPath(Object obj) { foreach(var pair in Values) if(ReferenceEquals(pair.Value,obj)) return pair.Key; return string.Empty; }
        public static void SaveAssets() { } public static void Refresh() { } public static void ImportAsset(string path,ImportAssetOptions options) { }
    }
    public static class EditorUtility
    {
        public static void SetDirty(Object obj) { }
        public static bool DisplayDialog(string title,string message,string ok,string cancel=null)=>false;
        public static string SaveFilePanel(string a,string b,string c,string d)=>string.Empty;
        public static string OpenFilePanel(string a,string b,string c)=>string.Empty;
    }
    public class EditorWindow : ScriptableObject
    { public GUIContent titleContent; public Vector2 minSize; public static T GetWindow<T>() where T:EditorWindow=>CreateInstance<T>(); public void Show() { } public void Close() { } public void Repaint() { } }
    public class PopupWindowContent { public EditorWindow editorWindow; public virtual Vector2 GetWindowSize()=>default; public virtual void OnGUI(Rect r) { } }
    public static class PopupWindow { public static void Show(Rect r,PopupWindowContent c) { } }
    public class PropertyDrawer { public virtual float GetPropertyHeight(SerializedProperty p,GUIContent l)=>0; public virtual void OnGUI(Rect r,SerializedProperty p,GUIContent l) { } }
    public sealed class SerializedObject : IDisposable
    {
        public Object targetObject; public SerializedObject(Object obj) { targetObject=obj; }
        public SerializedProperty FindProperty(string name)=>new SerializedProperty { serializedObject=this, propertyPath=name };
        public void Update() { } public bool ApplyModifiedProperties()=>true; public void Dispose() { }
    }
    public class SerializedProperty
    {
        public bool isExpanded; public int arraySize, enumValueIndex, intValue, depth;
        public string stringValue, propertyPath; public object managedReferenceValue; public SerializedObject serializedObject;
        public SerializedProperty FindPropertyRelative(string name)=>new SerializedProperty { serializedObject=serializedObject, propertyPath=propertyPath+"."+name };
        public SerializedProperty GetArrayElementAtIndex(int index)=>new SerializedProperty { serializedObject=serializedObject };
        public void DeleteArrayElementAtIndex(int index) { }
    }
    public static class EditorStyles { public static GUIStyle miniButton=new GUIStyle(), toolbar=new GUIStyle(), toolbarButton=new GUIStyle(), helpBox=new GUIStyle(), boldLabel=new GUIStyle(), popup=new GUIStyle(); }
    public static class EditorGUIUtility { public static float singleLineHeight=18; }
    public static class EditorGUI
    {
        public static int indentLevel;
        public struct DisabledScope : IDisposable { public DisabledScope(bool b) { } public void Dispose() { } }
        public static void BeginProperty(Rect r,GUIContent l,SerializedProperty p) { } public static void EndProperty() { }
        public static Rect PrefixLabel(Rect r,GUIContent l)=>r; public static bool Foldout(Rect r,bool expanded,GUIContent label,bool toggle)=>expanded;
        public static float GetPropertyHeight(SerializedProperty p,bool children)=>18;
        public static void PropertyField(Rect r,SerializedProperty p,GUIContent l,bool children=false) { }
        public static void LabelField(Rect r,string a,string b) { }
        public static void BeginChangeCheck() { } public static bool EndChangeCheck()=>false;
    }
    public static class EditorGUILayout
    {
        public static void BeginHorizontal(params object[] a) { } public static void EndHorizontal() { }
        public static void BeginVertical(params object[] a) { } public static void EndVertical() { }
        public static Vector2 BeginScrollView(Vector2 v,params object[] a)=>v; public static void EndScrollView() { }
        public static void HelpBox(string s,MessageType t) { }
        public static string TextField(string value,GUIStyle style)=>value; public static string TextField(string value)=>value; public static string TextField(string label,string value)=>value;
        public static int Popup(string label,int index,string[] options)=>index; public static bool Toggle(string label,bool v)=>v;
        public static void Space(float f=0) { } public static void LabelField(string s,params object[] a) { } public static void SelectableLabel(string s,params object[] a) { }
        public static void PropertyField(SerializedProperty p) { }
    }
}
namespace UnityEditor.Build
{
    public interface IPreprocessBuildWithReport { int callbackOrder {get;} void OnPreprocessBuild(Reporting.BuildReport report); }
    public class BuildFailedException:Exception { public BuildFailedException(string message):base(message) { } }
}
namespace UnityEditor.Build.Reporting { public class BuildReport { } }
