using System;
using System.Linq;
using GameplayTags;
using GameplayTags.Experiments;
using Set=GameplayTags.Experiments.DirectArraySet;
using Layout=GameplayTags.Experiments.BitmapLayout;
internal static partial class MixedProbe
{
    static object BatchTests()
    {
        long start=assertions;int cases=0;
        var r=Registry(8193);
        foreach(int nx in new[]{0,1,2,31,32,33,64,129})
        foreach(int ny in new[]{0,1,31,32,33,63,64,65,127,128,129})
        foreach(bool matches in new[]{false,true})
        {
            int[] x=Enumerable.Range(0,nx).Select(i=>i*32).ToArray();
            int[] y=Enumerable.Range(0,ny).Select(i=>matches && (i&1)==0?(i/2)*32+1:(matches?i/2:i)*32+16).OrderBy(i=>i).ToArray();
            var a=Make(r,x,Layout.Micro);var b=Make(r,y,Layout.Dense);
            var expected=x.Concat(y).Distinct().OrderBy(i=>i).ToArray();
            var copy=new Set(a);copy.AppendTags(b);Verify(copy,expected);
            long buffer=copy.BufferBytes;
            copy.AppendTags(b);Verify(copy,expected);Check(copy.BufferBytes==buffer,"repeat grew");
            copy.RemoveTags(b);Verify(copy,x.Except(y));copy.AppendTags(b);Verify(copy,expected);
            var alias=new Set(a);Set.UnionInto(b,alias,alias);Verify(alias,expected);Verify(a,x);Verify(b,y);
            int records=expected.Select(i=>i>>4).Distinct().Count();
            var output=Make(r,x,Layout.Micro,records);
            long before=GC.GetAllocatedBytesForCurrentThread();output.AppendTags(b);
            long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
            Check(bytes==0,"exact-result batch reservation allocated");Verify(output,expected);cases++;
        }
        return new{assertions=assertions-start,cases,failures=0};
    }
    static void BatchHoldout()
    {
        foreach(int u in new[]{65537,524289})
        {
            var r=Registry(u);
            foreach(int nx in new[]{129,2048})
            foreach(int ny in new[]{31,32,33,63,64,65})
            foreach(bool matching in new[]{false,true})
            {
                int stride=Math.Max(32,(u/(nx+1)/32)*32);
                int[] x=Enumerable.Range(0,nx).Select(i=>i*stride).ToArray();
                int[] y=Enumerable.Range(0,ny).Select(i=>x[i*nx/ny]+(matching?1:16)).ToArray();
                var a=Make(r,x,Layout.Micro);var b=Make(r,y,Layout.Dense);
                var expected=x.Concat(y).Distinct().OrderBy(i=>i).ToArray();
                int records=expected.Select(i=>i>>4).Distinct().Count();
                var output=new Set(r,records,Layout.Micro);
                var shape=new{u,n=nx,sourceMembers=ny,seed=202610021212,dist=matching?"same-key-newbits":"missing-distributed",left="Micro",right="Dense",digest=Digest(x),peerDigest=Digest(y),words=(u+63)/64,blocks=x.Select(i=>i>>4).Distinct().Count()};
                foreach(bool fresh in new[]{false,true})
                {
                    ModifyLoop(a,b,output,false,fresh,1);Verify(sink,expected);
                    Measure(shape,fresh?"copy_append":"reuse_copy_append","Micro",rep=>ModifyLoop(a,b,output,false,fresh,rep),!fresh);
                    Verify(sink,expected);Verify(a,x);Verify(b,y);
                }
            }
        }
    }
}
