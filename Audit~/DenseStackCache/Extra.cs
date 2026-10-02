using System;
using System.Linq;
using GameplayTags;
using GameplayTags.Experiments;
using Set=GameplayTags.Experiments.DirectArraySet;
using Layout=GameplayTags.Experiments.BitmapLayout;

internal static partial class MixedProbe
{
    static readonly int[] CacheSizes={0,1,2,7,8,15,16,31,32,33,63,64,65};
    static readonly string[] CacheShapes={"scattered","packed","tail","same_key_new_bits","large_left"};
    static (int[] x,int[] y) CacheInputs(int u,int n,string shape,int seed)
    {
        if(shape=="tail") return (Enumerable.Range(0,n).ToArray(),Enumerable.Range(u-n,n).ToArray());
        if(shape=="same_key_new_bits")
            return (Enumerable.Range(0,n).Select(i=>64+i*16).ToArray(),Enumerable.Range(0,n).Select(i=>65+i*16).ToArray());
        if(shape=="packed")
            return (Enumerable.Range(u/3,n).ToArray(),Enumerable.Range(u/3+n/2,n).ToArray());
        if(shape=="large_left")
        {
            int[] seq2=Sequence(u,4096+n,"scattered",seed);
            int[] large=seq2.Take(4096).OrderBy(i=>i).ToArray();
            int[] small=large.Take(n/2).Concat(seq2.Skip(4096).Take(n-n/2)).OrderBy(i=>i).ToArray();
            return (large,small);
        }
        int[] seq=Sequence(u,n*2,"scattered",seed);
        int[] x=seq.Take(n).OrderBy(i=>i).ToArray();
        int[] y=x.Take(n/2).Concat(seq.Skip(n).Take(n-n/2)).OrderBy(i=>i).ToArray();
        return (x,y);
    }
    static object StackCacheTests()
    {
        long startAssertions=assertions,cases=0;
        foreach(int u in new[]{10000,262145})
        {
            var r=Registry(u);
            foreach(int seed in new[]{2026100271,2026100293})
            foreach(int n in CacheSizes)
            foreach(string shape in CacheShapes)
            {
                var input=CacheInputs(u,n,shape,seed);
                int[] x=input.x,y=input.y,expected=x.Concat(y).Distinct().OrderBy(i=>i).ToArray();
                int records=expected.Select(i=>i>>4).Distinct().Count();
                var b=Make(r,y,Layout.Dense);
                foreach(int reserve in new[]{0,n,records})
                {
                    var a=Make(r,x,Layout.Micro,reserve);
                    var warm=new Set(a);warm.AppendTags(b);
                    var actual=new Set(a);var scalar=new Set(a);
                    long before=GC.GetAllocatedBytesForCurrentThread();actual.AppendTags(b);
                    long actualBytes=GC.GetAllocatedBytesForCurrentThread()-before;
                    before=GC.GetAllocatedBytesForCurrentThread();
                    for(int i=0;i<y.Length;i++)scalar.AddTag(r.GetTagAt(y[i]));
                    long scalarBytes=GC.GetAllocatedBytesForCurrentThread()-before;
                    Check(actualBytes==scalarBytes,"cached allocation staircase");
                    Check(actual.BufferBytes==scalar.BufferBytes,"cached capacity staircase");
                    Verify(actual,expected);Verify(a,x);Verify(b,y);
                    var alias=new Set(a);Set.UnionInto(b,alias,alias);Verify(alias,expected);
                    var prepared=Make(r,x,Layout.Micro,records);
                    long capacity=prepared.BufferBytes;before=GC.GetAllocatedBytesForCurrentThread();
                    prepared.AppendTags(b);
                    Check(GC.GetAllocatedBytesForCurrentThread()-before==0,"cached sufficient capacity allocated");
                    Check(prepared.BufferBytes==capacity,"cached sufficient capacity grew");Verify(prepared,expected);
                    // Repeated cache/fallback usage may not read stale stack slots.
                    for(int repeat=0;repeat<3;repeat++){actual.RemoveTags(b);Verify(actual,x.Except(y));actual.AppendTags(b);Verify(actual,expected);}
                    cases++;
                }
            }
        }
        return new{assertions=assertions-startAssertions,failures=0,cases};
    }
    static void CacheBoundary()
    {
        foreach(int u in new[]{10000,262144})
        {
            var r=Registry(u);
            foreach(int n in new[]{7,8,15,16,31,32,33,63,64})
            foreach(string dist in CacheShapes)
            {
                var input=CacheInputs(u,n,dist,2026100271);
                int[] x=input.x,y=input.y;int records=x.Concat(y).Select(i=>i>>4).Distinct().Count();
                var a=Make(r,x,Layout.Micro,n);var b=Make(r,y,Layout.Dense);
                var output=new Set(r,records,Layout.Micro);
                var shape=new{u,n,seed=2026100271,dist,left="Micro",right="Dense",digest=Digest(x),peerDigest=Digest(y),words=(u+63)/64,blocks=x.Select(i=>i>>4).Distinct().Count(),leftMembers=x.Length,rightMembers=y.Length,rightRecords=y.Select(i=>i>>4).Distinct().Count()};
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
