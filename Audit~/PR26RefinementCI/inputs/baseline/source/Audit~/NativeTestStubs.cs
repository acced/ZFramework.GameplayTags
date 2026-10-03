// Compile-only shapes. No native test is executed by the .NET audit.
using System;
using System.IO;

namespace NUnit.Framework
{
    [AttributeUsage(AttributeTargets.Method)] public sealed class TestAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class SetUpAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class TearDownAttribute : Attribute { }
    public delegate void TestDelegate();
    public static class Assert
    {
        public static void IsTrue(bool value, string message = null) => throw new NotSupportedException();
        public static void IsFalse(bool value, string message = null) => throw new NotSupportedException();
        public static void IsNotNull(object value) => throw new NotSupportedException();
        public static void AreEqual(object expected, object actual, string message = null) => throw new NotSupportedException();
        public static void AreNotEqual(object expected, object actual) => throw new NotSupportedException();
        public static void Greater(long value, long expected, string message = null) => throw new NotSupportedException();
        public static T Throws<T>(TestDelegate action) where T : Exception => throw new NotSupportedException();
    }
    public static class TestContext { public static TextWriter Progress => throw new NotSupportedException(); }
}
namespace UnityEngine.TestTools
{
    [AttributeUsage(AttributeTargets.Method)] public sealed class UnityTestAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class UnityTearDownAttribute : Attribute { }
    public sealed class EnterPlayMode { public EnterPlayMode(bool expectDomainReload = true) { } }
    public sealed class ExitPlayMode { }
}
namespace UnityEngine
{
    public static class JsonUtility
    {
        public static T FromJson<T>(string json) => throw new NotSupportedException("Native Unity test only.");
        public static string ToJson(object value) => throw new NotSupportedException("Native Unity test only.");
        public static void FromJsonOverwrite(string json, object value) => throw new NotSupportedException("Native Unity test only.");
    }
    public static class SystemInfo { public static string processorType => throw new NotSupportedException(); }
}
namespace UnityEditor
{
    public static class EditorSettings
    {
        public static bool enterPlayModeOptionsEnabled;
        public static EnterPlayModeOptions enterPlayModeOptions;
    }
}
