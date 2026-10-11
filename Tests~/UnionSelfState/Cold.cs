using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using GameplayTags;
internal static class Program
{
    static readonly Func<long> Allocated=Counter();
    static Func<long> Counter(){var m=typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",Type.EmptyTypes);if(m==null)throw new NotSupportedException("Real per-thread allocation counter required");return (Func<long>)Delegate.CreateDelegate(typeof(Func<long>),m);}
    sealed class PoolAudit:EventListener
    {
        internal bool SourceObserved,InApply;internal List<string> Events=new List<string>();
        protected override void OnEventSourceCreated(EventSource source){if(source.Name=="System.Buffers.ArrayPoolEventSource"){SourceObserved=true;EnableEvents(source,EventLevel.Verbose,EventKeywords.All);}}
        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if(!InApply)return;
            var payload=new List<string>();if(e.Payload!=null)for(int i=0;i<e.Payload.Count;i++)payload.Add("{\"Name\":"+Shared.Q(e.PayloadNames==null?i.ToString():e.PayloadNames[i])+",\"Value\":"+Shared.Q(e.Payload[i]==null?null:e.Payload[i].ToString())+"}");
            (Events??(Events=new List<string>())).Add("{\"Id\":"+e.EventId+",\"Name\":"+Shared.Q(e.EventName)+",\"Payload\":["+string.Join(",",payload)+"]}");
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]static void Apply(GameplayTagContainer o)
    {
#if REUSABLE_UNION
        GameplayTagContainer.Union(o,o,o);
#else
        GameplayTagContainer.Copy(o,GameplayTagContainer.Union(o,o));
#endif
    }
    static void Main(string[] args)
    {
        bool auditing=args[0]=="audit";if(!auditing&&args[0]!="allocation")throw new Exception("mode");
        int n=int.Parse(args[1]);string history=args[2];bool leaf=args[3]=="hierarchical";
        var audit=auditing?new PoolAudit():null;Shared.Initialize();var o=Shared.Normal(n,history,leaf);
        // Only clone retained arrays here: no snapshot formatting, hashing, public
        // query traversal, Union/Copy call, or ArrayPool rent before measured Apply.
        var before=new Shared.Snap(o,false);
        Allocated();GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        long retainedBefore=GC.GetTotalMemory(true);if(audit!=null)audit.InApply=true;
        long counterBefore=Allocated();Apply(o);long counterAfter=Allocated();
        if(audit!=null)audit.InApply=false;long retainedAfter=GC.GetTotalMemory(true);
        var after=new Shared.Snap(o);
        if(o.TagCount!=(leaf?3:n)||o.ExplicitTagCount!=(leaf?1:n))throw new Exception("Cardinality");
        if(leaf?!o.HasTagExact(Shared.Leaf):Shared.Flat.Take(n).Any(t=>!o.HasTagExact(t)))throw new Exception("Membership");
        Console.WriteLine("{\"Mode\":"+Shared.Q(args[0])+",\"Count\":"+n+",\"Shape\":"+Shared.Q(args[3])+",\"History\":"+Shared.Q(history)+",\"Backend\":"+Shared.Q(Type.GetType("Mono.Runtime")!=null?"Standalone Mono default":".NET")+",\"CodePreparation\":\"None; first Apply/Union/Copy in a fresh process; setup AddTag/RemoveTag/EnsureCapacity only\",\"Before\":"+before.Json(true)+",\"After\":"+after.Json(true)+(auditing?",\"PoolSourceObserved\":"+(audit.SourceObserved?"true":"false")+",\"ObservedApplyPoolEvents\":["+string.Join(",",audit.Events)+"]":",\"CounterBefore\":"+counterBefore+",\"CounterAfter\":"+counterAfter+",\"FirstCallAllocatedBytes\":"+(counterAfter-counterBefore)+",\"RetainedHeapDeltaBytes\":"+(retainedAfter-retainedBefore))+"}");
        if(audit!=null)audit.Dispose();GC.KeepAlive(o);
    }
}
