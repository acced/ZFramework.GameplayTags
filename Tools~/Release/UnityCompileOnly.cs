// Compile-only declarations. Never run the Unity integration tests against these.
// Real serialization/callback behavior must be exercised by the Unity Test Runner.
using System;
namespace UnityEngine
{
    public static class Object
    {
        public static void DestroyImmediate(object value) => throw new NotSupportedException("Unity required");
    }
    public static class JsonUtility
    {
        public static T FromJson<T>(string json) => throw new NotSupportedException("Unity required");
        public static string ToJson(object value) => throw new NotSupportedException("Unity required");
    }
}
