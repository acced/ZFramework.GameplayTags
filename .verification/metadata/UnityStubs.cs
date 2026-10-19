using System;
namespace UnityEngine { public static class Debug { public static void Log(object text) {} } }
namespace UnityEditor {
 [AttributeUsage(AttributeTargets.Method)] public class MenuItem : Attribute { public MenuItem(string path) {} }
 public static class AssetDatabase { public static void ImportAsset(string path) {} }
}
namespace UnityEditor.Build {
 public interface IPostBuildPlayerScriptDLLs { int callbackOrder { get; } void OnPostBuildPlayerScriptDLLs(Reporting.BuildReport report); }
 public class BuildFailedException : Exception { public BuildFailedException(string message) : base(message) {} }
}
namespace UnityEditor.Build.Reporting {
 public struct BuildFile { public string path; }
 public class BuildReport { public BuildFile[] Files; public BuildFile[] GetFiles()=>Files; }
}
