using System;
using System.Linq;
using GameplayTags;
using GameplayTags.Experiments;
using Set=GameplayTags.Experiments.DirectArraySet;
using Layout=GameplayTags.Experiments.BitmapLayout;
internal static partial class MixedProbe
{
    static void Unequal()
    {
        foreach(int u in new[]{10000,262144})
        {
            var r=Registry(u);
            foreach(int m in new[]{128,4096})
            foreach(int n in new[]{1,8,32,33})
            foreach(string dist in new[]{"prefix-subset","prefix-newbits","interior-subset","tail"})
            {
                int[] x=Enumerable.Range(0,m).Select(i=>i*(u/m)).ToArray();
                int[] y=dist=="tail"?Enumerable.Range(u-n,n).ToArray():
                    dist=="interior-subset"?x.Skip(m/2).Take(n).ToArray():
                    dist=="prefix-newbits"?x.Take(n).Select(i=>i+1).ToArray():x.Take(n).ToArray();
                var a=Make(r,x,Layout.Micro,Math.Min(u,2*m+17));var b=Make(r,y,Layout.Dense);
                int count=x.Concat(y).Select(i=>i>>4).Distinct().Count();
                var output=new Set(r,count,Layout.Micro);
                var shape=new{u,n=m,seed=2026100233,dist,left="Micro",right="Dense",digest=Digest(x),peerDigest=Digest(y),rightMembers=n,words=(u+63)/64,blocks=x.Select(i=>i>>4).Distinct().Count()};
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
