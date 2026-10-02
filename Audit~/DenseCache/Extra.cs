using System;
using System.Linq;
using System.Collections.Generic;
using GameplayTags;
using GameplayTags.Experiments;
using Set = GameplayTags.Experiments.DirectArraySet;
using Layout = GameplayTags.Experiments.BitmapLayout;

internal static partial class MixedProbe
{
    static void CacheInputs(int u,int n,string shape,int seed,out int[] x,out int[] y)
    {
        if(shape=="scattered")
        {
            int[] seq=Sequence(u,2*n,shape,seed);
            x=seq.Take(n).OrderBy(i=>i).ToArray();
            y=x.Take(n/2).Concat(seq.Skip(n).Take(n-n/2)).OrderBy(i=>i).ToArray();
        }
        else if(shape=="clustered") { x=Enumerable.Range(u/3,n).ToArray();y=Enumerable.Range(u/3+n/2,n).ToArray(); }
        else if(shape=="tail") { x=Enumerable.Range(0,n).ToArray();y=Enumerable.Range(u-n,n).ToArray(); }
        else if(shape=="same-key") { x=Enumerable.Range(0,n).Select(i=>i*16).ToArray();y=Enumerable.Range(0,n).Select(i=>i*16+1).ToArray(); }
        else throw new ArgumentException(nameof(shape));
    }
    static object CacheTests()
    {
        long start=assertions;int cases=0;
        foreach(int u in new[]{10000,262145})
        {
            var r=Registry(u);
            foreach(int n in new[]{0,1,2,7,8,15,16,17,31,32,33,63,64,65,127,128,129})
            foreach(string shape in new[]{"scattered","clustered","tail","same-key"})
            {
                CacheInputs(u,n,shape,2026100233,out var x,out var y);
                var source=Make(r,y,Layout.Dense);var expected=x.Concat(y).Distinct().OrderBy(v=>v).ToArray();
                foreach(int reserve in new[]{0,1,32,2*n+17})
                {
                    var template=Make(r,x,Layout.Micro,reserve);
                    var warm=new Set(template);warm.AppendTags(source);
                    var actual=new Set(template);var scalar=new Set(template);
                    long b=GC.GetAllocatedBytesForCurrentThread();actual.AppendTags(source);
                    long allocated=GC.GetAllocatedBytesForCurrentThread()-b;
                    b=GC.GetAllocatedBytesForCurrentThread();
                    foreach(int id in y)scalar.AddTag(r.GetTagAt(id));
                    long scalarBytes=GC.GetAllocatedBytesForCurrentThread()-b;
                    Verify(actual,expected);Verify(scalar,expected);
                    Check(actual.BufferBytes==scalar.BufferBytes && allocated==scalarBytes,"cache growth/allocated differs from scalar");
                    var otherAlias=new Set(template);Set.UnionInto(source,otherAlias,otherAlias);Verify(otherAlias,expected);
                    actual.RemoveTags(source);Verify(actual,x.Except(y));
                    actual.AppendTags(source);Verify(actual,expected);Verify(source,y);Verify(template,x);
                    cases++;
                }
                int records=expected.Select(i=>i>>4).Distinct().Count();
                var output=Make(r,x,Layout.Micro,records);
                long before=GC.GetAllocatedBytesForCurrentThread();output.AppendTags(source);
                Check(GC.GetAllocatedBytesForCurrentThread()==before,"exact-result capacity allocated");Verify(output,expected);
            }
        }
        // Repeat cache-size/cross-boundary calls on the same thread. No accumulating localloc.
        var small=Registry(4097);
        var target=new Set(small,256,Layout.Micro);
        var left=Make(small,Enumerable.Range(0,32).Select(i=>i*64).ToArray(),Layout.Micro,256);
        var rhs=Make(small,Enumerable.Range(0,32).Select(i=>i*64+32).ToArray(),Layout.Dense);
        for(int i=0;i<100;i++){target.CopyFrom(left);target.AppendTags(rhs);}
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        long first=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++){target.CopyFrom(left);target.AppendTags(rhs);checksum^=target.Count;}
        long prepared=GC.GetAllocatedBytesForCurrentThread()-first;
        Check(prepared==0,"repeated cache path allocated");Verify(target,Snapshot(left).Concat(Snapshot(rhs)));
        return new{assertions=assertions-start,failures=0,cases,preparedBytes=prepared,cacheLimit=32};
    }
    static void CacheBoundary()
    {
        foreach(int u in new[]{10000,262144})
        {
            var r=Registry(u);
            foreach(int n in new[]{1,2,7,8,15,16,17,31,32,33,64,65})
            foreach(string dist in new[]{"scattered","clustered","tail","same-key"})
            {
                CacheInputs(u,n,dist,2026100233,out var x,out var y);
                var a=Make(r,x,Layout.Micro,2*n+17);var b=Make(r,y,Layout.Dense);
                var output=new Set(r,2*n+17,Layout.Micro);
                var shape=new{u,n,seed=2026100233,dist,left="Micro",right="Dense",digest=Digest(x),peerDigest=Digest(y),words=(u+63)/64,blocks=x.Select(i=>i>>4).Distinct().Count(),rightMembers=y.Length,rightBlocks=y.Select(i=>i>>4).Distinct().Count()};
                foreach(bool fresh in new[]{true,false})
                {
                    ModifyLoop(a,b,output,false,fresh,1);Verify(sink,x.Concat(y));
                    Measure(shape,fresh?"copy_append":"reuse_copy_append","Micro",rep=>ModifyLoop(a,b,output,false,fresh,rep),!fresh);
                    Verify(sink,x.Concat(y));Verify(a,x);Verify(b,y);
                }
            }
        }
    }
}
