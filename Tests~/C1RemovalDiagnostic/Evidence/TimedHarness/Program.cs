using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using GameplayTags;
using UnityEngine;

// One invocation, one cell, one numeric sample. No cross-cell warm state.
internal static class Program
{
    static long sink;
    static readonly FieldInfo rawName = typeof(GameplayTag).GetField("m_Name", BindingFlags.Instance|BindingFlags.NonPublic);
    static readonly FieldInfo storage = typeof(GameplayTagContainer).GetField("m_GameplayTags", BindingFlags.Instance|BindingFlags.NonPublic);
    static string Name(int n) => "T" + n.ToString("D5");
    static GameplayTag Tag(int n) => new GameplayTag(new string(Name(n).ToCharArray()));
    static GameplayTagContainer Build(GameplayTag[] values, int capacity)
    { var c = new GameplayTagContainer(capacity); foreach(var t in values)c.AddTag(t); return c; }
    static void Assert(GameplayTagContainer c, GameplayTag[] wanted)
    {
        if(c.Count != wanted.Length)throw new Exception("count");
        for(int i=0;i<wanted.Length;i++)
        {
            if(!string.Equals(c[i].Name,wanted[i].Name,StringComparison.Ordinal))throw new Exception("content/order");
            if(!object.ReferenceEquals(c[i].Name,wanted[i].Name))throw new Exception("representative identity");
            if(i>0 && string.CompareOrdinal(c[i-1].Name,c[i].Name)>=0)throw new Exception("uniqueness");
        }
        int j=0;foreach(var t in c){if(!object.ReferenceEquals(t.Name,wanted[j++].Name))throw new Exception("enumeration");}
        if(j!=wanted.Length)throw new Exception("enumeration count");
        var list=(List<GameplayTag>)storage.GetValue(c);
        var items=(GameplayTag[])typeof(List<GameplayTag>).GetField("_items",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(list);
        for(int i=list.Count;i<items.Length;i++)if(rawName.GetValue(items[i])!=null)throw new Exception("uncleared tail references");
    }
    static long Consume(GameplayTagContainer c) => c.Count + (c.Count==0?0:c[c.Count-1].Name.Length);
    static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch(Exception e){Console.Error.WriteLine("FAIL " + e);return 1;}
    }
    static void Run(string[] args)
    {
        // mode, key, operation, sourceN, targetN, capacity, shape, context, calls
        if(args.Length!=9)throw new ArgumentException("Expected 9 arguments");
        string mode=args[0],key=args[1],op=args[2],shape=args[6],context=args[7];
        int n=int.Parse(args[3]),tn=int.Parse(args[4]),capacity=int.Parse(args[5]),calls=int.Parse(args[8]);
        if(calls<1)throw new ArgumentException("calls");
        if(IntPtr.Size!=8)throw new Exception("This bounded diagnostic requires a 64-bit process");
        bool pooled=context=="preprepared-once";
        const int maxPoolTargets=16384;
        const long maxPoolBackingBytes=128L*1024*1024, maxPoolSetupAllocatedBytes=144L*1024*1024;
        if(pooled && (op!="copy" || shape!="lifecycle-intersection-refs" || calls>maxPoolTargets || (long)calls*capacity*IntPtr.Size>maxPoolBackingBytes))throw new ArgumentException("pool geometry or memory cap");
        if(mode!="measure" && mode!="validate")throw new ArgumentException("mode");
        if(context!="new" && context!="retained" && !pooled)throw new ArgumentException("context");
        if(op!="copy" && op!="append" && op!="right" && op!="lifecycle")throw new ArgumentException("operation");
        var settings=ScriptableObject.CreateInstance<GameplayTagSettings>();
        settings.ReplaceAll(Enumerable.Range(0,8192).Select(i=>new GameplayTagDefinition(Name(i),"","",false,true)).ToList(),new List<GameplayTagRedirect>(),new List<GameplayTagSource>());
        GameplayTagManager.Initialize(settings,true);
        var seedInput=Enumerable.Range(0,tn).Select(i=>Tag(op=="copy"?2*i+2:2*i)).ToArray();
        if(op=="copy" && shape=="lifecycle-intersection-refs")seedInput=Enumerable.Range(0,tn).Select(i=>Tag(2*i)).ToArray();
        GameplayTag[] sourceInput;
        if(op=="lifecycle") sourceInput=Enumerable.Range(0,n).Select(i=>Tag(2*(i+n/2))).ToArray();
        else if(op=="copy" && shape=="lifecycle-intersection-refs")
        {
            if(n!=512 || tn!=1024)throw new Exception("lifecycle copy geometry");
            sourceInput=seedInput.Skip(512).ToArray(); // Intersection preserves the lhs representatives.
        }
        else if(op=="copy") sourceInput=Enumerable.Range(0,n).Select(i=>Tag(shape=="same-names"?2*i+2:2*i+3)).ToArray();
        else
        {
            sourceInput=Enumerable.Range(0,n).Select(i=>Tag(2*i+2)).ToArray();
            if(n>0 && shape!="contained")
            {
                int[] positions=shape=="first"?new[]{0}:shape=="middle"?new[]{n/2}:shape=="last"?new[]{n-1}:new[]{0,n/2,n-1};
                foreach(int p in positions)sourceInput[p]=Tag(2*p+1);
                sourceInput=sourceInput.OrderBy(t=>t.Name,StringComparer.Ordinal).ToArray();
            }
        }
        if(op=="append" || op=="right")
        {
            if(n>0 && (string.CompareOrdinal(sourceInput[0].Name,seedInput[0].Name)<=0 || string.CompareOrdinal(sourceInput[n-1].Name,seedInput[tn-1].Name)>0))throw new Exception("containment endpoint eligibility");
            var missing=sourceInput.Select((t,i)=>new{t,i}).Where(x=>!seedInput.Any(t=>t.Name==x.t.Name)).Select(x=>x.i).ToArray();
            int[] want=shape=="contained"?Array.Empty<int>():shape=="first"?new[]{0}:shape=="middle"?new[]{n/2}:shape=="last"?new[]{n-1}:new[]{0,n/2,n-1};
            if(!missing.SequenceEqual(want))throw new Exception("missing-position geometry");
        }
        var source=Build(sourceInput,shape=="lifecycle-intersection-refs"?tn:n);var seed=Build(seedInput,tn);
        var expected=op=="copy"?sourceInput:
            (op=="right"?sourceInput.Concat(seedInput):seedInput.Concat(sourceInput)).GroupBy(t=>t.Name,StringComparer.Ordinal).Select(g=>g.First()).OrderBy(t=>t.Name,StringComparer.Ordinal).ToArray();
        GameplayTagContainer target=Build(seedInput,capacity),last=target,lifeUnion=null,lifeIntersection=null;
        var lifecycleIntersection=seedInput.Where(t=>sourceInput.Any(x=>x.Name==t.Name)).ToArray();
        var shuffled=(GameplayTag[])seedInput.Clone();var random=new Random(117+n);
        for(int i=shuffled.Length-1;i>0;i--){int j=random.Next(i+1);var t=shuffled[i];shuffled[i]=shuffled[j];shuffled[j]=t;}
        GameplayTagContainer[] pool=null;
        Action<GameplayTagContainer> mutate = d => {
            if(op=="copy")d.CopyFrom(source);
            else if(op=="append")d.AppendTags(source);
            else if(op=="right")d.SetUnion(source,d);
            else if(op!="lifecycle")throw new Exception("operation");
        };
        Action<int> step = i => {
            if(op=="lifecycle")
            {
                var c=Build(shuffled,0);var hit=seedInput[n/2];sink+=c.HasTagExact(hit)?1:0;c.RemoveTag(hit);c.AddTag(hit);
                foreach(var t in c)sink+=t.Name.Length;
                lifeUnion=GameplayTagContainer.Union(c,source);lifeIntersection=GameplayTagContainer.IntersectionExact(c,source);
                c.CopyFrom(lifeIntersection);c.Clear();last=c;sink+=lifeUnion.Count;return;
            }
            var d=pooled?pool[i]:context=="new"?Build(seedInput,capacity):target;
            // Restoring target is part of every retained whole-public workflow.
            if(context=="retained")d.CopyFrom(seed);
            mutate(d);sink+=Consume(d);last=d;
        };
        // Fixture checks and 64 warmups use separate disposable objects. No prepared target
        // is copied or restored before its one measured use. Pool construction is not timed.
        if(op!="lifecycle"){var check=Build(seedInput,capacity);mutate(check);Assert(check,expected);}
        long poolSetupAllocatedBytes=0;
        int poolSetupGc0=0,poolSetupGc1=0,poolSetupGc2=0;
        if(pooled)
        {
            pool=new GameplayTagContainer[1];
            for(int i=0;i<64;i++){pool[0]=Build(seedInput,capacity);step(0);}
            int beforePoolGc0=GC.CollectionCount(0),beforePoolGc1=GC.CollectionCount(1),beforePoolGc2=GC.CollectionCount(2);
            long beforePool=GC.GetAllocatedBytesForCurrentThread();
            pool=new GameplayTagContainer[calls];
            for(int i=0;i<calls;i++)pool[i]=Build(seedInput,capacity);
            poolSetupAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-beforePool;
            poolSetupGc0=GC.CollectionCount(0)-beforePoolGc0;poolSetupGc1=GC.CollectionCount(1)-beforePoolGc1;poolSetupGc2=GC.CollectionCount(2)-beforePoolGc2;
            if(poolSetupAllocatedBytes>maxPoolSetupAllocatedBytes)throw new Exception("pool setup allocation budget exceeded");
        }
        else for(int i=0;i<64;i++)step(0);
        if(mode=="validate")
        {
            for(int i=0;i<calls;i++)step(i);
            Verify();Console.WriteLine(JsonSerializer.Serialize(new{kind="validation",key,calls,status="PASS",sink}));return;
        }
        int g0=GC.CollectionCount(0),g1=GC.CollectionCount(1),g2=GC.CollectionCount(2);
        long allocated=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
        for(int i=0;i<calls;i++)step(i);
        long ticks=Stopwatch.GetTimestamp()-start;
        long bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
        int gc0=GC.CollectionCount(0)-g0,gc1=GC.CollectionCount(1)-g1,gc2=GC.CollectionCount(2)-g2;
        // Durable numeric record precedes validation, reflection, JSON payload census.
        Console.WriteLine(JsonSerializer.Serialize(new{kind="sample",key,op,n,targetCount=tn,initial_result_count=op=="lifecycle"?0:tn,initial_result_capacity=op=="lifecycle"?0:capacity,capacity,shape,context,calls,classification=pooled?"preprepared-single-copy-diagnostic":"whole-public-workflow",pool_setup_allocated_bytes=poolSetupAllocatedBytes,pool_setup_gc0=poolSetupGc0,pool_setup_gc1=poolSetupGc1,pool_setup_gc2=poolSetupGc2,pool_target_limit=pooled?(int?)maxPoolTargets:null,pool_backing_byte_limit=pooled?(long?)maxPoolBackingBytes:null,pool_setup_allocation_byte_limit=pooled?(long?)maxPoolSetupAllocatedBytes:null,pool_reference_vector_payload_bytes=pooled?(long)calls*IntPtr.Size:0,short_sample=ticks*1000.0/Stopwatch.Frequency<250,ticks,frequency=Stopwatch.Frequency,elapsed_ms=ticks*1000.0/Stopwatch.Frequency,ns_per_op=ticks*1e9/Stopwatch.Frequency/calls,bytes,bytes_per_op=(double)bytes/calls,gc0,gc1,gc2,sink}));
        Console.Out.Flush();
        Verify();
        if((op=="append" || op=="right") && context=="new" && bytes!=16496L*calls)throw new Exception("C2 new allocation guard expected16496B/op");
        var actual=(List<GameplayTag>)storage.GetValue(last);
        long poolCapacity=pooled?pool.Sum(d=>(long)((List<GameplayTag>)storage.GetValue(d)).Capacity):0;
        Console.WriteLine(JsonSerializer.Serialize(new{kind="validation",key,status="PASS",validated_outputs=pooled?calls:1,pool_setup_allocated_bytes=poolSetupAllocatedBytes,pool_setup_gc0=poolSetupGc0,pool_setup_gc1=poolSetupGc1,pool_setup_gc2=poolSetupGc2,pool_actual_capacity_total=poolCapacity,pool_actual_backing_payload_bytes=poolCapacity*IntPtr.Size,pointer_size=IntPtr.Size,retained_count=actual.Count,retained_capacity=actual.Capacity,array_payload_bytes=(long)actual.Capacity*IntPtr.Size,source_count=source.Count,source_capacity=((List<GameplayTag>)storage.GetValue(source)).Capacity,seed_count=seed.Count,seed_capacity=((List<GameplayTag>)storage.GetValue(seed)).Capacity,lifecycle_union_count=lifeUnion?.Count,lifecycle_union_capacity=lifeUnion==null?(int?)null:((List<GameplayTag>)storage.GetValue(lifeUnion)).Capacity,lifecycle_intersection_count=lifeIntersection?.Count,lifecycle_intersection_capacity=lifeIntersection==null?(int?)null:((List<GameplayTag>)storage.GetValue(lifeIntersection)).Capacity}));
        Console.Out.Flush();
        void Verify()
        {
            Assert(source,sourceInput);Assert(seed,seedInput);
            if(op=="lifecycle")
            {
                Assert(last,Array.Empty<GameplayTag>());Assert(lifeUnion,expected);Assert(lifeIntersection,lifecycleIntersection);
                if(n==1024 && (lifeUnion.Count!=1536 || lifeIntersection.Count!=512 || ((List<GameplayTag>)storage.GetValue(last)).Capacity!=1024 || ((List<GameplayTag>)storage.GetValue(lifeUnion)).Capacity!=2048 || ((List<GameplayTag>)storage.GetValue(lifeIntersection)).Capacity!=1024))throw new Exception("lifecycle1024 storage geometry");
                return;
            }
            if(pooled)foreach(var d in pool)
            {
                Assert(d,expected);
                if(object.ReferenceEquals(d,source) || object.ReferenceEquals(d,seed) || ((List<GameplayTag>)storage.GetValue(d)).Capacity!=capacity)throw new Exception("prepared target identity or capacity");
            }
            else Assert(last,expected);
            if(op=="append" || op=="right")
            {
                int wantedCapacity=context=="new"?1025:2048;
                if(last.Count!=1025 || ((List<GameplayTag>)storage.GetValue(last)).Capacity!=wantedCapacity)throw new Exception("C2 storage guard");
            }
            if(shape=="lifecycle-intersection-refs" && (source.Count!=512 || ((List<GameplayTag>)storage.GetValue(source)).Capacity!=1024 || ((List<GameplayTag>)storage.GetValue(last)).Capacity!=1024))throw new Exception("lifecycle copy storage");
        }
    }
}
