#if PROBE_STUBS
// Compile-only contracts for the sample. These do NOT simulate a Unity runtime.
namespace UnityEditor
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    internal sealed class MenuItem : System.Attribute
    {
        internal MenuItem(string path) { }
    }
}
namespace UnityEngine
{
    internal static class Debug
    {
        internal static void Log(object value) { }
        internal static void LogWarning(object value) { }
    }
}
namespace UnityEngine.Profiling
{
    internal static class Profiler
    {
        internal static void BeginSample(string name) { }
        internal static void EndSample() { }
    }
}
#endif
