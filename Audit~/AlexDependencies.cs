using System;

// Application dependencies missing from Alex's standalone repo. The four benchmark operations never call GenericPool.
// Fail on unexpected diagnostics rather than hiding invalid benchmark inputs.
internal static class Log
{
    public static void Warn(string message) => throw new InvalidOperationException(message);
    public static void Warn(string format, params object[] values) => throw new InvalidOperationException(string.Format(format, values));
    public static void Error(string message) => throw new InvalidOperationException(message);
}
internal static class GenericPool<T> where T : new()
{
    public static Lease Get(out T value) { throw new InvalidOperationException("Pool is not part of the four benchmark operations."); }
    internal readonly struct Lease : IDisposable { public void Dispose() { } }
}
internal static class ListPool<T>
{
    public static Lease Get(out System.Collections.Generic.List<T> value)
    { throw new InvalidOperationException("Count-container pooling is not a timed benchmark operation."); }
    internal readonly struct Lease : IDisposable { public void Dispose() { } }
}
