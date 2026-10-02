// Extra tests exercise the new operation-local Dense traversal, not merely foreach.
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using GameplayTags;
using GameplayTags.Experiments;
using Set = GameplayTags.Experiments.DirectArraySet;
using Layout = GameplayTags.Experiments.BitmapLayout;

internal static partial class MixedProbe
{
    static object LocalCursorTests()
    {
        long start = assertions;
        var r = Registry(257);
        var full = new Set(r,257,Layout.Dense);
        int[] localIds = { 0, 239, 241, 255, 256 };
        var local = Make(r,localIds,Layout.Micro,257);
        var target = new Set(r,257,Layout.Micro);
        long capacity = target.BufferBytes;
        // Exhaustive record masks exercise copy, mixed union, then in-place append.
        for (int mask=0; mask<65536; mask++)
        {
            full.Clear();
            var expected = new List<int>();
            for (int bit=0; bit<16; bit++) if ((mask&(1<<bit))!=0)
            { full.AddTag(r.GetTagAt(240+bit)); expected.Add(240+bit); }
            target.CopyFrom(full); Verify(target,expected);
            if ((mask&1)==0) Set.UnionInto(full,local,target);
            else Set.UnionInto(local,full,target);
            Verify(target,expected.Concat(localIds));
            target.CopyFrom(local); target.AppendTags(full);
            Verify(target,expected.Concat(localIds));
            Check(target.BufferBytes==capacity,"exhaustive path grew reserved output");
            // Independent construction follows the same sorted write order/capacity policy.
            if ((mask&1023)==0)
            {
                var result=Set.Union(full,local,Layout.Micro);
                Verify(result,expected.Concat(localIds));
                result.Clear(); Verify(local,localIds);
            }
        }
        // Long empty spans, high keys, the first/last slice of words and partial tail.
        var high=Registry(262145);
        foreach(int offset in new[]{0,48,64,65520,65536,262112})
        foreach(int mask in new[]{0,1,2,32768,65535,21845,43690,257})
        {
            int[] ids=Enumerable.Range(0,16).Where(i=>(mask&(1<<i))!=0)
                .Select(i=>offset+i).Concat(new[]{262144}).Distinct().OrderBy(i=>i).ToArray();
            int[] peerIds={0,15,16,63,64,65535,131072,262143};
            var a=Make(high,ids,Layout.Dense);
            var b=Make(high,peerIds,Layout.Micro,64);
            var output=Make(high,new[]{2,127,2048,262144},Layout.Micro,64);
            var expected=ids.Concat(peerIds);
            output.CopyFrom(a);Verify(output,ids);
            Set.UnionInto(a,b,output);Verify(output,expected);
            Set.UnionInto(b,a,output);Verify(output,expected);
            var alias=new Set(b);Set.UnionInto(a,alias,alias);Verify(alias,expected);
            var alias2=new Set(b);Set.UnionInto(alias2,a,alias2);Verify(alias2,expected);
            Verify(b,peerIds);Verify(a,ids);
        }
        // Continued mutation after mixed operations must not depend on stale cursor state.
        var random=new Random(2026100208);
        var mutable=new Set(r,257,Layout.Micro);
        var model=new HashSet<int>();
        for(int step=0;step<2000;step++)
        {
            full.Clear();var incoming=new HashSet<int>();
            for(int i=0;i<12;i++){int id=random.Next(257);incoming.Add(id);full.AddTag(r.GetTagAt(id));}
            if(step%3==0){mutable.CopyFrom(full);model=new HashSet<int>(incoming);}
            else if(step%3==1){mutable.AppendTags(full);model.UnionWith(incoming);}
            else {mutable.RemoveTags(full);model.ExceptWith(incoming);}
            int probe=random.Next(257);
            if((step&1)==0){mutable.AddTag(r.GetTagAt(probe));model.Add(probe);}
            else {mutable.RemoveTag(r.GetTagAt(probe));model.Remove(probe);}
            Verify(mutable,model);
        }
        // Prepared Copy/Append cover the helpers not included in prior Union-only allocation probe.
        full=Make(r,new[]{0,63,64,127,240,256},Layout.Dense);
        for(int i=0;i<128;i++){target.CopyFrom(full);target.AppendTags(full);}
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<2048;i++){target.CopyFrom(full);target.AppendTags(full);}
        allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
        Check(allocated==0,"local cursor allocated managed memory");
        Type cursor=typeof(Set).GetNestedType("DenseRecordCursor",BindingFlags.NonPublic);
        int privateBytes=cursor==null?0:(int)typeof(Unsafe).GetMethod("SizeOf").MakeGenericMethod(cursor).Invoke(null,null);
        return new { assertions=assertions-start,failures=0,preparedBytes=allocated,
            privateDenseCursorBytes=privateBytes,publicEnumeratorBytes=Unsafe.SizeOf<Set.Enumerator>() };
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int CopyLoop(Set source,Set result,int repeats)
    {
        for(int i=0;i<repeats;i++) result.CopyFrom(source);
        sink=result;return result.Count;
    }
}
