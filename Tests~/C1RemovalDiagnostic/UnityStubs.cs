using System;
namespace UnityEngine {
 [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute {}
 [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeReference : Attribute {}
 [AttributeUsage(AttributeTargets.Class)] public sealed class CreateAssetMenuAttribute : Attribute { public string fileName; public string menuName; }
 [AttributeUsage(AttributeTargets.Method)] public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) {} }
 public enum RuntimeInitializeLoadType { BeforeSceneLoad }
 public class ScriptableObject { public static T CreateInstance<T>() where T:ScriptableObject,new() => new T(); }
 public interface ISerializationCallbackReceiver { void OnBeforeSerialize(); void OnAfterDeserialize(); }
 public static class Resources { public static int LoadCalls; public static object DefaultAsset; public static T Load<T>(string name) where T:class { LoadCalls++; return DefaultAsset as T; } }
 public static class Debug { public static Action<object> WarningHook; public static void LogWarning(object message) { WarningHook?.Invoke(message); } }
}
