using System;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using GameplayTags;

internal static class Program
{
    private static GameplayTag[] Tags;
    private static readonly Func<long> Allocated = Counter();
    private static Func<long> Counter()
    {
        MethodInfo method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
        if (method == null) throw new NotSupportedException("A real per-thread allocation counter is required.");
        return (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
    }
    private sealed class PoolAudit : EventListener
    {
        internal bool SourceObserved;
        internal int Allocations;
        internal List<int> Sizes = new List<int>();
        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "System.Buffers.ArrayPoolEventSource") { SourceObserved = true; EnableEvents(source, EventLevel.Verbose, EventKeywords.All); }
        }
        protected override void OnEventWritten(EventWrittenEventArgs value)
        {
            if (value.EventName != "BufferAllocated") return;
            Allocations++;
            if (value.Payload != null && value.Payload.Count > 1) (Sizes ?? (Sizes = new List<int>())).Add(Convert.ToInt32(value.Payload[1]));
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Apply(GameplayTagContainer output, IGameplayTagContainer aliasView, IGameplayTagContainer source, bool left)
    {
#if REUSABLE_UNION
        if (left) GameplayTagContainer.Union(output, aliasView, source);
        else GameplayTagContainer.Union(output, source, aliasView);
#else
        GameplayTagContainer fresh = left ? GameplayTagContainer.Union(aliasView, source) : GameplayTagContainer.Union(source, aliasView);
        GameplayTagContainer.Copy(output, fresh);
#endif
    }
    private static void Reset(GameplayTagContainer output, GameplayTag[] small)
    {
        output.Clear();
        foreach (GameplayTag tag in small) output.AddTag(tag);
    }
    private static string Capacity(IGameplayTagContainer value)
    {
        TagStorage storage = value.Indices.Storage;
        int[] buckets = (int[])typeof(TagStorage).GetField("m_Buckets", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(storage);
        int mask = (int)typeof(TagStorage).GetField("m_BucketMask", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(storage);
        return "{\"Entries\":" + storage.Entries.Length + ",\"ExplicitIndices\":" + storage.ExplicitIndices.Length + ",\"Buckets\":" + buckets.Length + ",\"BucketMask\":" + mask + "}";
    }
    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    private static void Main(string[] args)
    {
        // One fixture per fresh process. AddTag-only setup never touches the pool.
        if (args.Length != 7) throw new ArgumentException("allocation|audit, source-explicit-count, alias-explicit-count, left|right, disjoint|equal-closure|prefer-set, plain|alias-proxy|both-proxy|counted|alias-proxy-counted, fresh|reserved");
        bool auditMode = args[0] == "audit"; if (!auditMode && args[0] != "allocation") throw new ArgumentException("unknown mode");
        int count = int.Parse(args[1]), smallCount = int.Parse(args[2]);
        bool left = args[3] == "left"; if (!left && args[3] != "right") throw new ArgumentException("unknown alias position");
        string shape = args[4], kind = args[5], outputMode = args[6];
        bool counted = kind.Contains("counted");
        PoolAudit audit = auditMode ? new PoolAudit() : null;
        string[] names = Enumerable.Range(0, count).Select(i => "R.G" + i % 32 + ".N" + i).ToArray();
        string[] smallNames = Enumerable.Range(0, smallCount).Select(i => "Seed" + i).ToArray();
        if (shape == "equal-closure")
        {
            names = new string[count]; names[0] = "Chain";
            for (int i = 1; i < count; i++) names[i] = names[i - 1] + ".N";
            smallNames = names.Skip(count - smallCount).ToArray();
        }
        else if (shape == "prefer-set")
            smallNames = Enumerable.Range(0, smallCount).Select(i => "Seed" + i + string.Concat(Enumerable.Repeat(".N", count + 64))).ToArray();
        else if (shape != "disjoint") throw new ArgumentException("unknown shape");
        GameplayTagManager.Initialize(names.Concat(smallNames).Concat(new[] { "Old" }).Select(n => new GameplayTagRegistration(n)).ToArray());
        Tags = names.Select(GameplayTagManager.RequestTag).ToArray();
        GameplayTag[] small = smallNames.Select(GameplayTagManager.RequestTag).ToArray();
        IGameplayTagContainer source = counted ? (IGameplayTagContainer)new GameplayTagCountContainer() : new GameplayTagContainer();
        foreach (GameplayTag tag in Tags) { if (counted) ((GameplayTagCountContainer)source).AddTag(tag, 3); else source.AddTag(tag); }
        var expected = new HashSet<string>(names.Concat(smallNames), StringComparer.Ordinal);
        var closure = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in expected) { closure.Add(name); for (int i = 0; i < name.Length; i++) if (name[i] == '.') closure.Add(name.Substring(0, i)); }
        // Both variants begin with an identical distinct output. Fresh mode
        // starts with only Old; reserved mode preallocates the result closure.
        var ordinary = new GameplayTagContainer(); Reset(ordinary, small);
        var output = outputMode == "reserved" ? new GameplayTagContainer(closure.Count) : new GameplayTagContainer();
        output.AddTag(GameplayTagManager.RequestTag("Old"));
        IGameplayTagContainer aliasView = kind.Contains("proxy") ? new Proxy(ordinary) : (IGameplayTagContainer)ordinary;
        IGameplayTagContainer sourceView = kind == "both-proxy" ? new Proxy(source) : source;
        Allocated(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long retainedBefore = GC.GetTotalMemory(true);
        int allocatedEventsBefore = audit == null ? 0 : audit.Allocations;
        long firstBytes = Allocated(); Apply(output, aliasView, sourceView, left); firstBytes = Allocated() - firstBytes;
        int poolEvents = audit == null ? 0 : audit.Allocations - allocatedEventsBefore;
        long retainedAfter = GC.GetTotalMemory(true); GC.KeepAlive(source); GC.KeepAlive(output);
        if (output.ExplicitTagCount != expected.Count || output.TagCount != closure.Count) throw new Exception("union cardinality mismatch");
        if (!new HashSet<string>(output.GetTags().Select(t => t.Name), StringComparer.Ordinal).SetEquals(closure)) throw new Exception("union closure mismatch");
        if (!new HashSet<string>(output.GetExplicitTags().Select(t => t.Name), StringComparer.Ordinal).SetEquals(expected)) throw new Exception("union name mismatch");
        if (!new HashSet<string>(source.GetExplicitTags().Select(t => t.Name), StringComparer.Ordinal).SetEquals(names)) throw new Exception("source mutated");
        if (counted) foreach (GameplayTag tag in Tags) if (((GameplayTagCountContainer)source).GetExplicitTagCount(tag) != 3) throw new Exception("counted source changed");
        output.Clear(); output.AddTag(GameplayTagManager.RequestTag("Old")); long warmBytes = Allocated(); Apply(output, aliasView, sourceView, left); warmBytes = Allocated() - warmBytes;
        string outputCapacity = Capacity(output), sourceCapacity = Capacity(source);
        foreach (GameplayTag tag in expected.Select(GameplayTagManager.RequestTag)) { output.AddTag(tag); output.RemoveTag(tag); }
        if (!output.IsEmpty || output.TagCount != 0) throw new Exception("normalized drain failed");
        // Event payload allocation never contaminates reported allocation runs.
        Console.WriteLine("{\"Schema\":1,\"Framework\":" + Quote(Type.GetType("Mono.Runtime") != null ? "Standalone Mono" : ".NET") +
            ",\"Mode\":" + Quote(args[0]) + ",\"LargeExplicitCount\":" + count + ",\"SmallExplicitCount\":" + smallCount + ",\"AliasPosition\":" + Quote(args[3]) +
            ",\"OutputMode\":" + Quote(outputMode) + ",\"Shape\":" + Quote(shape) + ",\"SourceKind\":" + Quote(kind) +
            ",\"CodePreparation\":\"None; first invocation in a fresh process\"" +
            ",\"OutputCapacity\":" + outputCapacity + ",\"SourceCapacity\":" + sourceCapacity +
            (auditMode ? ",\"PoolSourceObserved\":" + (audit.SourceObserved ? "true" : "false") + ",\"FirstCallBufferAllocatedEvents\":" + poolEvents + ",\"ObservedAllocationSizes\":[" + string.Join(",", audit.Sizes) + "]"
                       : ",\"FirstCallAllocatedBytes\":" + firstBytes + ",\"WarmCallAllocatedBytes\":" + warmBytes + ",\"RetainedHeapDeltaBytes\":" + (retainedAfter - retainedBefore)) + "}");
        if (audit != null) audit.Dispose(); GC.KeepAlive(source); GC.KeepAlive(output);
    }
    private sealed class Proxy : IGameplayTagContainer
    {
        private readonly IGameplayTagContainer Inner;
        private readonly IGameplayTagContainer View;
        private readonly int Mode;
        public Proxy(IGameplayTagContainer inner, IGameplayTagContainer view = null, int mode = 0) { Inner = inner; View = view ?? inner; Mode = mode; }
        public bool IsEmpty => Inner.IsEmpty;
        public int ExplicitTagCount => Inner.ExplicitTagCount;
        public int TagCount => Inner.TagCount;
        public GameplayTagContainerIndices Indices => Inner.Indices;
        public void AddTag(GameplayTag tag) => Inner.AddTag(tag);
        public void RemoveTag(GameplayTag tag) => Inner.RemoveTag(tag);
        public GameplayTagEnumerator GetTags() => Inner.GetTags();
        public GameplayTagEnumerator GetExplicitTags()
        {
            if (Mode == 3) return default;
            GameplayTagEnumerator result = Mode == 2 ? View.GetTags() : View.GetExplicitTags();
            if (Mode == 1) result.MoveNext();
            return result;
        }
        public void AddTags<T>(in T other) where T : IGameplayTagContainer => Inner.AddTags(other);
        public void RemoveTags<T>(in T other) where T : IGameplayTagContainer => Inner.RemoveTags(other);
        public void GetParentTags(GameplayTag tag, List<GameplayTag> output) => Inner.GetParentTags(tag, output);
        public void GetChildTags(GameplayTag tag, List<GameplayTag> output) => Inner.GetChildTags(tag, output);
        public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> output) => Inner.GetExplicitParentTags(tag, output);
        public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> output) => Inner.GetExplicitChildTags(tag, output);
        public void Clear() => Inner.Clear();
        public IEnumerator<GameplayTag> GetEnumerator() => ((IEnumerable<GameplayTag>)Inner).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
