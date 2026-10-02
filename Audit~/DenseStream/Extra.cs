using System;
using System.Linq;
using GameplayTags;
using GameplayTags.Experiments;
using Set=GameplayTags.Experiments.DirectArraySet;
using Layout=GameplayTags.Experiments.BitmapLayout;

internal static partial class MixedProbe
{
    static object StreamingTests()
    {
        long initial=assertions;
        var r=Registry(1025);
        int[][] left={ new int[0],new[]{0},new[]{64,128},new[]{0,64,128,256},new[]{0,16,32,48,64,512,1024} };
        // Same-key new bits, matched prefix then an interior gap, pure tail,
        // matched prefix then tail, no new records, and gaps across word boundaries.
        int[][] right={ new int[0],new[]{1},new[]{64,129},new[]{0,65,96,128,1024},new[]{513,1024},new[]{0,17,65,513},new[]{0,16,32,48,64,512,1024} };
        long cases=0;
        foreach(var x in left) foreach(var y in right)
        foreach(int reserve in new[]{0,1,4,16,65})
        {
            var a=Make(r,x,Layout.Micro,reserve);
            var b=Make(r,y,Layout.Dense);
            var expected=x.Concat(y).Distinct().OrderBy(i=>i).ToArray();
            var scalar=new Set(a);foreach(int id in y)scalar.AddTag(r.GetTagAt(id));
            var actual=new Set(a);actual.AppendTags(b);
            Verify(actual,expected);Verify(a,x);Verify(b,y);
            Check(actual.BufferBytes==scalar.BufferBytes,"prefix transition changed growth");
            var alias=new Set(a);Set.UnionInto(b,alias,alias);Verify(alias,expected);
            for(int k=0;k<4;k++) {actual.AppendTags(b);Verify(actual,expected);}
            var prepared=Make(r,x,Layout.Micro,expected.Select(i=>i>>4).Distinct().Count());
            long capacity=prepared.BufferBytes,start=GC.GetAllocatedBytesForCurrentThread();
            prepared.AppendTags(b);
            Check(GC.GetAllocatedBytesForCurrentThread()-start==0,"prefix prepared allocation");
            Check(prepared.BufferBytes==capacity,"prefix prepared growth");Verify(prepared,expected);
            cases++;
        }
        var random=new Random(2026100251);
        for(int t=0;t<1000;t++)
        {
            var x=Enumerable.Range(0,1025).Where(i=>random.Next(16)==0).ToArray();
            var y=Enumerable.Range(0,1025).Where(i=>random.Next(12)==0).ToArray();
            var a=Make(r,x,Layout.Micro);var b=Make(r,y,Layout.Dense);
            a.AppendTags(b);Verify(a,x.Concat(y));Verify(b,y);
            foreach(int id in y) a.RemoveTag(r.GetTagAt(id));
            Verify(a,x.Except(y));a.AppendTags(b);Verify(a,x.Concat(y));cases++;
        }
        return new{assertions=assertions-initial,failures=0,cases};
    }
}
