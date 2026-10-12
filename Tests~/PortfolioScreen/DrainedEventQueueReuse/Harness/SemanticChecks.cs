using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using GameplayTags;

internal static class DrainedQueueCheck
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly List<OnTagCountChangedDelegate> Catalog = new List<OnTagCountChangedDelegate>();
    private static BinaryWriter Writer;
    private static StreamWriter Raw;
    private static bool Reuse;
    private static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
    private static object Field(object value, string name) => value.GetType().GetField(name, Fields).GetValue(value);
    private static int Number(object value, string name) => (int)Field(value, name);
    private static GameplayTag T(string name) => GameplayTagManager.RequestTag(name);
    private static OnTagCountChangedDelegate Remember(OnTagCountChangedDelegate callback) { Catalog.Add(callback); return callback; }
    // Reused callback-zero-shift delegate encoder. Captured invocation lists use fixture identities.
    private static void WriteDelegate(BinaryWriter writer, Delegate value)
    {
        Delegate[] items = value == null ? Array.Empty<Delegate>() : value.GetInvocationList();
        writer.Write(items.Length);
        foreach (Delegate item in items)
        {
            int id = Catalog.FindIndex(known => known.Equals(item));
            Check(id >= 0, "Every observed delegate has a fixed fixture identity");
            writer.Write(id);
        }
    }
    private static int Capacity(GameplayTagCountContainer c) => ((Array)Field(c, "m_Events")).Length;
    private static int Pending(GameplayTagCountContainer c) => Number(c, "m_EventCount") - Number(c, "m_NextEvent");
    private static int Growth(int n) { int result = 0; while (result < n) result = result == 0 ? 8 : result * 2; return result; }
    private static void Drained(GameplayTagCountContainer c)
    {
        Check(Number(c, "m_EventCount") == 0 && Number(c, "m_NextEvent") == 0 && Number(c, "m_BatchDepth") == 0 && !(bool)Field(c, "m_Dispatching"), "Finished queue ownership resets");
    }
    // Reuses the complete callback/storage encoders from callback-zero-shift. Only the queue
    // encoding changes: compare the logical suffix; record physical indices/capacity separately.
    private static void Snapshot(string label, GameplayTagCountContainer c)
    {
        Writer.Write(label);
        var callbacks = (Array)Field(c, "m_Callbacks");
        Writer.Write(callbacks.Length); Writer.Write(Number(c, "m_CallbackCount"));
        foreach (object entry in callbacks)
        {
            Writer.Write(Number(entry, "Id")); object pair = Field(entry, "Callbacks");
            WriteDelegate(Writer, (Delegate)Field(pair, "OnNewOrRemove"));
            WriteDelegate(Writer, (Delegate)Field(pair, "OnAnyChange"));
        }
        WriteDelegate(Writer, (Delegate)Field(c, "OnAnyTagNewOrRemove"));
        WriteDelegate(Writer, (Delegate)Field(c, "OnAnyTagCountChange"));
        int next = Number(c, "m_NextEvent"), count = Number(c, "m_EventCount"), batch = Number(c, "m_BatchDepth");
        bool dispatching = (bool)Field(c, "m_Dispatching");
        var events = (Array)Field(c, "m_Events");
        Check(0 <= next && next <= count && count <= events.Length, "Queue range invariant");
        Writer.Write(batch); Writer.Write(dispatching); Writer.Write(count - next);
        for (int i = 0; i < events.Length; i++)
        {
            object item = events.GetValue(i); int id = Number(item, "TagId"), n = Number(item, "Count");
            var callback = (Delegate)Field(item, "Callback");
            if (i >= next && i < count)
            {
                Writer.Write(id); Writer.Write(n); WriteDelegate(Writer, callback);
                Check(callback != null, "Pending notification has its captured delegate");
            }
            else Check(id == 0 && n == 0 && callback == null, "All consumed/inactive slots are default");
        }
        Raw.WriteLine(label + "|" + events.Length + "|" + next + "|" + count + "|" + batch + "|" + dispatching + "|" + (count - next));
        int[] snapshot = (int[])Field(c, "m_Snapshot"); Writer.Write(snapshot.Length); foreach (int n in snapshot) Writer.Write(n);
        var storage = c.Indices.Storage;
        Writer.Write(storage.Count); Writer.Write(storage.ExplicitCount); Writer.Write(Number(storage, "m_BucketMask"));
        Writer.Write(storage.Entries.Length);
        foreach (var e in storage.Entries) { Writer.Write(e.Id); Writer.Write(e.Count); Writer.Write(e.ExplicitCount); Writer.Write(e.ExplicitIndex); }
        Writer.Write(storage.ExplicitIndices.Length); foreach (int n in storage.ExplicitIndices) Writer.Write(n);
        var buckets = (int[])Field(storage, "m_Buckets"); Writer.Write(buckets.Length); foreach (int n in buckets) Writer.Write(n);
        Writer.Write(c.IsEmpty); Writer.Write(c.TagCount); Writer.Write(c.ExplicitTagCount);
        var all = c.GetTags().ToArray(); Writer.Write(all.Length);
        foreach (GameplayTag tag in all) { Writer.Write(tag.Name); Writer.Write(c.GetTagCount(tag)); Writer.Write(c.GetExplicitTagCount(tag)); }
        var explicitTags = c.GetExplicitTags().ToArray(); Writer.Write(explicitTags.Length); foreach (GameplayTag tag in explicitTags) Writer.Write(tag.Name);
    }
    private static void Trace(string label, List<string> actual, IEnumerable<string> expected)
    {
        string[] oracle = expected.ToArray(); Check(actual.SequenceEqual(oracle), label + " exact public callback trace");
        Writer.Write(label); Writer.Write(actual.Count); foreach (string item in actual) Writer.Write(item);
    }
    private static bool Checkpoint(int n, int limit) => n <= 10 || n == limit || (n & (n - 1)) == 0 || (n > 1 && ((n - 1) & (n - 2)) == 0);
    private static void Chain(int limit)
    {
        string label = "chain-" + limit; var c = new GameplayTagCountContainer(); var trace = new List<string>(); int depth = 0, maximum = 0;
        var callback = Remember((tag, count) =>
        {
            depth++; maximum = Math.Max(maximum, depth); trace.Add(tag.Name + ":" + count);
            Check(tag == T("A") && c.GetTagCount(tag) == count && c.GetExplicitTagCount(tag) == count, "Chain committed state");
            Check(Pending(c) == 0, "Single chain consumes last notification before callback");
            Check(Number(c, "m_NextEvent") == (Reuse ? ((count - 1) % 8) + 1 : count), "Variant-specific drained physical cursor");
            if (Checkpoint(count, limit)) Snapshot(label + "-before-" + count, c);
            if (count < limit)
            {
                c.AddTag(tag); Check(trace.Count == count && Pending(c) == 1, "Nested add is deferred");
                if (Checkpoint(count, limit)) Snapshot(label + "-after-" + count, c);
            }
            depth--;
        });
        c.OnAnyTagCountChange += callback; Snapshot(label + "-fresh", c); c.AddTag(T("A"));
        Check(maximum == 1 && c.GetTagCount(T("A")) == limit, "Finite chain completes without recursive callback dispatch");
        Check(Capacity(c) == (Reuse ? 8 : Growth(limit)), "Declared retained-capacity mechanism");
        Drained(c); Snapshot(label + "-done", c); Trace(label, trace, Enumerable.Range(1, limit).Select(n => "A:" + n));
        c.OnAnyTagCountChange -= callback; c.Clear(); Drained(c); Snapshot(label + "-silent-clear", c);
    }
    private static void Multicast()
    {
        var c = new GameplayTagCountContainer(); var trace = new List<string>();
        OnTagCountChangedDelegate tail = null;
        var replacement = Remember((tag, count) => trace.Add("replacement:" + tag.Name + ":" + count));
        var head = Remember((tag, count) =>
        {
            trace.Add("head:" + tag.Name + ":" + count);
            if (tag != T("A")) return;
            if (count < 8) c.AddTag(tag);
            if (count == 8)
            {
                Snapshot("multicast-full-before", c);
                c.OnAnyTagCountChange -= tail; c.OnAnyTagCountChange += replacement;
                c.AddTag(tag); Check(Pending(c) == 1, "First multicast handler enqueues after full drain");
                Snapshot("multicast-replaced-after", c);
            }
        });
        tail = Remember((tag, count) =>
        {
            trace.Add("tail:" + tag.Name + ":" + count);
            if (tag == T("A") && count == 8)
            {
                c.AddTag(T("R0")); Check(Pending(c) == 2, "Removed later multicast handler still runs and appends in order");
                Snapshot("multicast-later-handler", c);
            }
        });
        c.OnAnyTagCountChange += head + tail; c.AddTag(T("A"));
        var expected = new List<string>(); for (int n = 1; n <= 8; n++) { expected.Add("head:A:" + n); expected.Add("tail:A:" + n); }
        expected.AddRange(new[] { "head:A:9", "replacement:A:9", "head:R0:1", "replacement:R0:1" });
        Trace("multicast", trace, expected); Drained(c); Snapshot("multicast-done", c);
    }
    private sealed class DeliberateException : Exception { }
    private static void Throwing(int fanout)
    {
        string label = "throw-" + fanout; var c = new GameplayTagCountContainer(); var trace = new List<string>(); var expectedException = new DeliberateException();
        var head = Remember((tag, count) =>
        {
            trace.Add("head:" + tag.Name + ":" + count);
            if (tag == T("A") && count < 8) c.AddTag(tag);
            if (tag == T("A") && count == 8)
            {
                Snapshot(label + "-before", c);
                for (int i = 0; i < fanout; i++) c.AddTag(T("R0"));
                Check(Pending(c) == fanout, "Throw retains exactly newly appended logical suffix before cleanup");
                Snapshot(label + "-pending", c); throw expectedException;
            }
        });
        var tail = Remember((tag, count) => trace.Add("tail:" + tag.Name + ":" + count));
        c.OnAnyTagCountChange += head + tail; Exception actual = null;
        try { c.AddTag(T("A")); } catch (DeliberateException error) { actual = error; }
        Check(ReferenceEquals(actual, expectedException), "Exact callback exception identity");
        var expected = new List<string>(); for (int n = 1; n < 8; n++) { expected.Add("head:A:" + n); expected.Add("tail:A:" + n); } expected.Add("head:A:8");
        Trace(label, trace, expected); Check(c.GetTagCount(T("A")) == 8 && c.GetTagCount(T("R0")) == fanout, "Committed inner mutations survive throw");
        Check(Capacity(c) == Growth((Reuse ? 0 : 8) + fanout), "Reset then fanout follows unchanged positive-pending resize");
        Drained(c); Snapshot(label + "-done", c); trace.Clear(); c.OnAnyTagCountChange -= head; c.AddTag(T("A"));
        Trace(label + "-recovery", trace, new[] { "tail:A:9" }); Drained(c); Snapshot(label + "-recovered", c);
    }
    private static void PendingFallback()
    {
        var c = new GameplayTagCountContainer(); var trace = new List<string>();
        var callback = Remember((tag, count) =>
        {
            trace.Add(tag.Name + ":" + count);
            if (tag == T("A") && count == 7)
            {
                Check(Capacity(c) == 8 && Number(c, "m_NextEvent") == 7 && Number(c, "m_EventCount") == 8, "Exactly one pending at full boundary");
                Snapshot("one-pending-before", c); c.AddTag(T("R0"));
                Check(Capacity(c) == 16 && Number(c, "m_NextEvent") == 7 && Number(c, "m_EventCount") == 9, "Both sources keep original one-pending resize");
                Snapshot("one-pending-after", c);
            }
        });
        c.OnAnyTagCountChange += callback; c.BeginBatch(); for (int i = 0; i < 8; i++) c.AddTag(T("A"));
        Snapshot("one-pending-batched", c); c.EndBatchWithoutDispatch(); c.FlushEvents();
        Trace("one-pending", trace, Enumerable.Range(1, 8).Select(n => "A:" + n).Concat(new[] { "R0:1" })); Drained(c); Snapshot("one-pending-done", c);
    }
    private static void NestedBatchDiscard()
    {
        var c = new GameplayTagCountContainer(); var trace = new List<string>();
        var callback = Remember((tag, count) =>
        {
            trace.Add(tag.Name + ":" + count); if (tag != T("A")) return;
            if (count < 8) c.AddTag(tag);
            if (count == 8)
            {
                c.BeginBatch(); c.BeginBatch(); c.AddTag(T("R0")); Snapshot("nested-depth-two", c);
                c.DiscardPendingEvents(); Check(Pending(c) == 1, "Active dispatcher owns pending suffix");
                c.EndBatchWithoutDispatch(); c.FlushEvents(); Check(Pending(c) == 1 && trace.Count == 8, "Nested flush remains suppressed");
                try { c.AddTag(T("R1")); throw new DeliberateException(); }
                catch (DeliberateException) { c.DiscardPendingEvents(); }
                Check(Pending(c) == 2, "Caught inner failure cannot discard active dispatcher work");
                Snapshot("nested-caught-before-end", c); c.EndBatchWithoutDispatch(); Snapshot("nested-after-end", c);
            }
        });
        c.OnAnyTagCountChange += callback; c.AddTag(T("A"));
        Trace("nested-active", trace, Enumerable.Range(1, 8).Select(n => "A:" + n).Concat(new[] { "R0:1", "R1:1" }));
        Drained(c); Snapshot("nested-active-done", c); trace.Clear();
        c.BeginBatch(); c.AddTag(T("R0")); c.AddTag(T("R1")); Snapshot("inactive-discard-before", c);
        c.DiscardPendingEvents(); Check(Pending(c) == 0, "Inactive discard clears pending callbacks"); Snapshot("inactive-discard-after", c);
        c.EndBatchWithoutDispatch(); c.FlushEvents(); Check(trace.Count == 0, "Discarded notifications do not dispatch");
        c.AddTag(T("R0")); Trace("inactive-discard-recovery", trace, new[] { "R0:3" }); Drained(c); Snapshot("inactive-discard-recovered", c);
    }
    private static GameplayTagCountContainer Underlying(GameplayTagHierarchicalContainer c) => (GameplayTagCountContainer)Field(c, "m_UnderlyingContainer");
    private static void HierarchySnapshot(string label, GameplayTagHierarchicalContainer child, GameplayTagHierarchicalContainer middle, GameplayTagCountContainer parent)
    {
        Check(ReferenceEquals(child.Parent, middle) && ReferenceEquals(middle.Parent, parent), "Hierarchy links remain intact");
        Snapshot(label + "-child", Underlying(child)); Snapshot(label + "-middle", Underlying(middle)); Snapshot(label + "-parent", parent);
        foreach (var h in new[] { child, middle }) { var a = (int[])Field(h, "m_Snapshot"); Writer.Write(a.Length); foreach (int id in a) Writer.Write(id); }
    }
    private static void HierarchyFanout()
    {
        var parent = new GameplayTagCountContainer(); var middle = new GameplayTagHierarchicalContainer { Parent = parent }; var child = new GameplayTagHierarchicalContainer { Parent = middle };
        var source = new GameplayTagContainer(); source.AddTag(T("R0.B0.L0")); source.AddTag(T("R1.B0.L0")); var trace = new List<string>();
        var parentCallback = Remember((tag, count) => trace.Add("parent:" + tag.Name + ":" + count));
        var middleCallback = Remember((tag, count) => trace.Add("middle:" + tag.Name + ":" + count));
        var specific = Remember((tag, count) => trace.Add("specific:" + tag.Name + ":" + count));
        var childCallback = Remember((tag, count) =>
        {
            trace.Add("child:" + tag.Name + ":" + count);
            if (tag != T("A")) return;
            Check(child.GetTagCount(tag) == count && middle.GetTagCount(tag) == count && parent.GetTagCount(tag) == count, "Hierarchy commits counts before child callback");
            if (count < 8) child.AddTag(tag);
            if (count == 8)
            {
                HierarchySnapshot("hierarchy-full-before", child, middle, parent); child.AddTags(source);
                Check(Pending(Underlying(child)) == 8, "Hierarchical bulk fanout remains deferred in active child");
                Check(Capacity(Underlying(child)) == (Reuse ? 8 : 16), "Hierarchy full drain reuses only empty child queue");
                HierarchySnapshot("hierarchy-after-inner-bulk", child, middle, parent);
            }
        });
        parent.OnAnyTagCountChange += parentCallback; middle.OnAnyTagCountChange += middleCallback; child.OnAnyTagCountChange += childCallback;
        child.RegisterTagEventCallback(T("R0.B0.L0"), GameplayTagEventType.AnyCountChange, specific);
        child.RegisterTagEventCallback(T("R1.B0.L0"), GameplayTagEventType.AnyCountChange, specific);
        child.AddTag(T("A"));
        var expected = new List<string> { "child:A:1", "middle:A:1", "middle:A:2", "parent:A:1", "parent:A:2" };
        for (int n = 2; n < 8; n++) { expected.Add("child:A:" + n); expected.Add("middle:A:" + (n + 1)); expected.Add("parent:A:" + (n + 1)); }
        expected.Add("child:A:8"); string[] names = { "R0", "R0.B0", "R0.B0.L0", "R1", "R1.B0", "R1.B0.L0" };
        foreach (string prefix in new[] { "middle:", "parent:" }) foreach (string name in names) expected.Add(prefix + name + ":1");
        foreach (string name in names) { if (name.EndsWith(".L0", StringComparison.Ordinal)) expected.Add("specific:" + name + ":1"); expected.Add("child:" + name + ":1"); }
        Trace("hierarchy-fanout", trace, expected);
        foreach (var c in new[] { Underlying(child), Underlying(middle), parent }) { Drained(c); Check(c.GetExplicitTagCount(T("A")) == 8 && c.GetExplicitTagCount(T("R0.B0.L0")) == 1 && c.GetExplicitTagCount(T("R1.B0.L0")) == 1, "Exact propagated multiplicity"); }
        HierarchySnapshot("hierarchy-done", child, middle, parent);
    }
    // Unchanged exact-typed public call contexts. No Runtime inlining controls are introduced.
    [MethodImpl(MethodImplOptions.NoInlining)] private static int NativeAdd(GameplayTagCountContainer c, GameplayTag t) { c.AddTag(t, 1); return c.GetTagCount(t); }
    [MethodImpl(MethodImplOptions.NoInlining)] private static int NativeRemove(GameplayTagCountContainer c, GameplayTag t) { c.RemoveTag(t, 1); return c.GetTagCount(t); }
    [MethodImpl(MethodImplOptions.NoInlining)] private static int NativeClear(GameplayTagCountContainer c) { c.Clear(); return c.TagCount; }
    [MethodImpl(MethodImplOptions.NoInlining)] private static int NativeBulk(GameplayTagCountContainer c, GameplayTagContainer source) { c.AddTags(source); c.RemoveTags(source); return c.TagCount; }
    private static void NaturalCallers()
    {
        var c = new GameplayTagCountContainer(); int calls = 0; OnTagCountChangedDelegate callback = (tag, count) => calls++;
        c.OnAnyTagCountChange += callback; var source = new GameplayTagContainer(); source.AddTag(T("A"));
        Check(NativeAdd(c, T("A")) == 1 && NativeRemove(c, T("A")) == 0, "Ordinary exact mutation callers");
        Check(NativeBulk(c, source) == 0 && NativeAdd(c, T("A")) == 1 && NativeClear(c) == 0 && calls == 6, "Ordinary bulk and Clear callers");
        Drained(c);
    }
    private static int Main(string[] args)
    {
        if (args.Length != 5 || (args[4] != "original" && args[4] != "candidate")) throw new ArgumentException("snapshot, representation, metadata, raw queue and exact variant required");
        Reuse = args[4] == "candidate"; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        int result = (int)typeof(Program).GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        if (result != 0) return result;
        using (Writer = new BinaryWriter(new FileStream(args[0], FileMode.CreateNew)))
        using (Raw = new StreamWriter(new FileStream(args[3], FileMode.CreateNew)))
        {
            Raw.WriteLine("label|capacity|next|count|batch|dispatching|pending");
            foreach (int n in new[] { 7, 8, 9, 17, 1025 }) Chain(n);
            Multicast(); Throwing(1); Throwing(10); PendingFallback(); NestedBatchDiscard(); HierarchyFanout();
        }
        NaturalCallers();
        using (var writer = new StreamWriter(new FileStream(args[1], FileMode.CreateNew)))
            writer.WriteLine("Only Enqueue body differs; private queue indices/capacities/array identities and resource-failure behavior intentionally differ; public traces and logical pending suffix compare exactly");
        MetadataGate.Write(args[2]);
        Console.WriteLine("PASS drained queue: finite chain, multicast snapshot, throw recovery, hierarchy fanout, nested batch/discard, full one-pending resize");
        Console.WriteLine("PASS unchanged core including BindingAndPooling; logical suffix and full storage/callback state; ordinary public callers exercised; no portable native capture; untimed only");
        return 0;
    }
}
