using System;
using System.Linq;
using System.Runtime.CompilerServices;
using GameplayTags;
using GameplayTags.Experiments;
using Set=GameplayTags.Experiments.DirectArraySet;
using Layout=GameplayTags.Experiments.BitmapLayout;
internal static partial class MixedProbe
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int CopyCaseLoop(Set source,Set empty,Set output,int op,int repeats)
    {
        for(int i=0;i<repeats;i++)
        {
            if(op==0){sink=new Set(source.Registry,0,Layout.Micro);sink.CopyFrom(source);}
            else if(op==1){output.CopyFrom(source);sink=output;}
            else if(op==2){output.Clear();output.AppendTags(source);sink=output;}
            else if(op==3){Set.UnionInto(source,source,output);sink=output;}
            else if(op==4){Set.UnionInto(empty,source,output);sink=output;}
            else sink=Set.Union(empty,source,Layout.Micro);
        }
        return sink.Count;
    }
    static void CopyCases()
    {
        string[] ops={"fresh_copy","prepared_copy","empty_append","union_same_into","union_empty_into","union_empty_fresh"};
        foreach(int u in new[]{10000,262144})
        {
            var r=Registry(u);var empty=new Set(r,0,Layout.Micro);
            foreach(int n in new[]{0,1,2,8,32,33,128,4096})
            foreach(string dist in new[]{"contiguous","scattered"})
            {
                int[] x=Sequence(u,n,dist,2026102317).OrderBy(i=>i).ToArray();
                var source=Make(r,x,Layout.Dense);
                var output=Make(r,new[]{u-1},Layout.Micro,Math.Max(n,1));
                var shape=new{u,n,seed=2026102317,dist,left="Dense",right="empty",digest=Digest(x),peerDigest=Digest(Array.Empty<int>()),words=(u+63)/64,blocks=x.Select(i=>i>>4).Distinct().Count()};
                for(int op=0;op<ops.Length;op++)
                {
                    int selected=op;CopyCaseLoop(source,empty,output,op,1);Verify(sink,x);
                    Measure(shape,ops[op],"Micro",rep=>CopyCaseLoop(source,empty,output,selected,rep),op>=1&&op<=4);
                    Verify(sink,x);Verify(source,x);Verify(empty,Array.Empty<int>());
                }
            }
        }
    }
}
