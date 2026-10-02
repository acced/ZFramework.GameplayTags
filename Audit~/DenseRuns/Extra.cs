using System;
using System.Linq;
using System.Collections.Generic;
using GameplayTags;
using GameplayTags.Experiments;
using Set = GameplayTags.Experiments.DirectArraySet;
using Layout = GameplayTags.Experiments.BitmapLayout;

internal static partial class MixedProbe
{
    static object RunMergeTests()
    {
        long before = assertions;
        int cases = 0;
        var registry = Registry(65537);
        var random = new Random(2026100221);
        foreach(int nx in new[]{0,1,2,15,31,32,33,63,64,65,127,257,1024,2048})
        foreach(int ny in new[]{0,1,2,31,32,33,64,257,1024})
        foreach(int shape in new[]{0,1,2,3,4})
        {
            int[] x,y;
            if(shape==0)
            {
                x=Enumerable.Range(0,nx).Select(i=>i*32).ToArray();
                y=Enumerable.Range(0,ny).Select(i=>i*32+16).ToArray();
            }
            else if(shape==1)
            {
                x=Enumerable.Range(0,nx).Select(i=>65536-i*16).OrderBy(i=>i).ToArray();
                y=Enumerable.Range(0,ny).Select(i=>i*16+1).ToArray();
            }
            else if(shape==2)
            {
                x=Enumerable.Range(0,nx).Select(i=>i*16).ToArray();
                y=Enumerable.Range(0,ny).Select(i=>65536-i*16).OrderBy(i=>i).ToArray();
            }
            else if(shape==3)
            {
                x=Enumerable.Range(0,nx).Select(i=>i*32).ToArray();
                y=Enumerable.Range(0,ny).Select(i=>i*32+1).ToArray();
            }
            else
            {
                x=Enumerable.Range(0,65537).OrderBy(_=>random.Next()).Take(nx).OrderBy(i=>i).ToArray();
                y=Enumerable.Range(0,65537).OrderBy(_=>random.Next()).Take(ny).OrderBy(i=>i).ToArray();
            }
            var a=Make(registry,x,Layout.Micro);
            var b=Make(registry,y,Layout.Dense);
            int[] union=x.Concat(y).Distinct().OrderBy(i=>i).ToArray();
            var result=new Set(a);result.AppendTags(b);Verify(result,union);
            result.AppendTags(b);Verify(result,union);
            Set.UnionInto(b,a,result);Verify(result,union);
            Verify(a,x);Verify(b,y);
            var alias=new Set(a);Set.UnionInto(b,alias,alias);Verify(alias,union);
            int exact=union.Select(i=>i>>4).Distinct().Count();
            var reuse=Make(registry,x,Layout.Micro,exact);
            long capacity=reuse.BufferBytes;
            long bytes=GC.GetAllocatedBytesForCurrentThread();reuse.AppendTags(b);
            bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
            Check(bytes==0 && reuse.BufferBytes==capacity, bytes==0 && reuse.BufferBytes==capacity ? "run merge actual-capacity contract" : $"allocation case nx={nx} ny={ny} shape={shape} bytes={bytes} before={capacity} after={reuse.BufferBytes} expectedRecords={exact}");
            Verify(reuse,union);
            if(union.Length>0){reuse.RemoveTag(registry.GetTagAt(union[0]));Verify(reuse,union.Skip(1));}
            cases++;
        }
        return new {assertions=assertions-before,cases,failures=0};
    }

    static void FocusAppend()
    {
        foreach(int u in new[]{10000,262144})
        {
            var registry=Registry(u);
            foreach(int seed in new[]{20261031,20261113})
            foreach(string dist in new[]{"contiguous","clusters4","scattered"})
            foreach(int n in new[]{0,1,8,128,4096})
            {
                int[] seq=Sequence(u,2*n,dist,seed);
                int[] x=seq.Take(n).OrderBy(i=>i).ToArray();
                int[] y=x.Take(n/2).Concat(seq.Skip(n).Take(n-n/2)).OrderBy(i=>i).ToArray();
                var a=Make(registry,x,Layout.Micro,Math.Min(u,2*n+17));
                var b=Make(registry,y,Layout.Dense,Math.Min(u,2*n+17));
                var output=new Set(registry,Math.Min(u,2*n+17),Layout.Micro);
                var shape=new{u,n,seed,dist,left="Micro",right="Dense",digest=Digest(x),peerDigest=Digest(y),rightMembers=b.Count,words=(u+63)/64,blocks=x.Select(i=>i>>4).Distinct().Count()};
                foreach(bool fresh in new[]{true,false})
                {
                    ModifyLoop(a,b,output,false,fresh,1);Verify(sink,x.Concat(y));
                    Measure(shape,fresh?"copy_append":"reuse_copy_append","Micro",rep=>ModifyLoop(a,b,output,false,fresh,rep),!fresh);
                    Verify(sink,x.Concat(y));Verify(a,x);Verify(b,y);
                }
            }
        }
    }

    static void HoldoutAppend()
    {
        // Declared before observing candidate timings. Different domain/counts/seeds.
        foreach(int u in new[]{65537,524289})
        {
            var registry=Registry(u);
            foreach(int n in new[]{7,33,257,2048})
            foreach(int factor in new[]{1,4})
            foreach(string dist in new[]{"scattered","clusters4"})
            {
                int ny=Math.Max(1,n/factor);
                int[] seq=Sequence(u,n+ny,dist,2026100421+n+factor);
                int[] x=seq.Take(n).OrderBy(i=>i).ToArray();
                int[] y=x.Take(ny/3).Concat(seq.Skip(n).Take(ny-ny/3)).OrderBy(i=>i).ToArray();
                var a=Make(registry,x,Layout.Micro,n+17);
                var b=Make(registry,y,Layout.Dense);
                var output=new Set(registry,n+ny+17,Layout.Micro);
                var shape=new{u,n,seed=2026100421+n+factor,dist,left="Micro",right="Dense",digest=Digest(x),peerDigest=Digest(y),rightMembers=ny,words=(u+63)/64,blocks=x.Select(i=>i>>4).Distinct().Count()};
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
