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
    static object BulkTests()
    {
        long first = assertions;
        var r = Registry(257);
        var source = new Set(r,257,Layout.Dense);
        var target = new Set(r,257,Layout.Micro);
        int[] anchors = { 0, 64, 128, 192, 256 };
        var left = Make(r,anchors,Layout.Micro,257);
        // Every 16-bit mask in EACH slice, through changed copy and reverse merge.
        for(int slice=0;slice<4;slice++)
        for(int mask=0;mask<65536;mask++)
        {
            source.Clear();
            var expected = new List<int>();
            for(int bit=0;bit<16;bit++) if((mask&(1<<bit))!=0)
            { int id=128+slice*16+bit; source.AddTag(r.GetTagAt(id)); expected.Add(id); }
            target.CopyFrom(source); Verify(target,expected);
            target.CopyFrom(left); target.AppendTags(source); Verify(target,expected.Concat(anchors));
        }
        long growthCases=0;
        var random=new Random(2026100219);
        foreach(int size in new[]{65,257,8193})
        {
            var registry=Registry(size);
            for(int trial=0;trial<80;trial++)
            {
                int nx=trial%20, ny=trial%13==0?size:trial*3;
                int[] x=Enumerable.Range(0,size).OrderBy(_=>random.Next()).Take(nx).OrderBy(i=>i).ToArray();
                int[] y=Enumerable.Range(0,size).OrderBy(_=>random.Next()).Take(Math.Min(ny,size)).OrderBy(i=>i).ToArray();
                var dense=Make(registry,y,Layout.Dense);
                foreach(int reserve in new[]{0,1,2,3,4,8,31,size})
                {
                    var template=Make(registry,x,Layout.Micro,reserve);
                    var warm=new Set(template); warm.AppendTags(dense);
                    var actual=new Set(template); var reference=new Set(template);
                    long b=GC.GetAllocatedBytesForCurrentThread(); actual.AppendTags(dense);
                    long actualBytes=GC.GetAllocatedBytesForCurrentThread()-b;
                    b=GC.GetAllocatedBytesForCurrentThread();
                    for(int i=0;i<y.Length;i++) reference.AddTag(registry.GetTagAt(y[i]));
                    long referenceBytes=GC.GetAllocatedBytesForCurrentThread()-b;
                    Verify(actual,x.Concat(y)); Verify(reference,x.Concat(y));
                    Check(actual.BufferBytes==reference.BufferBytes,"bulk capacity differs from scalar insert");
                    Check(actualBytes==referenceBytes,"bulk allocation differs from scalar insert");
                    growthCases++;
                }
                int exact=x.Concat(y).Select(i=>i>>4).Distinct().Count();
                var prepared=Make(registry,x,Layout.Micro,exact);
                long capacity=prepared.BufferBytes;
                long start=GC.GetAllocatedBytesForCurrentThread(); prepared.AppendTags(dense);
                long bytes=GC.GetAllocatedBytesForCurrentThread()-start;
                Check(bytes==0 && prepared.BufferBytes==capacity,"actual-result reservation allocated");
                Verify(prepared,x.Concat(y));
                var alias=Make(registry,x,Layout.Micro);
                Set.UnionInto(dense,alias,alias);Verify(alias,x.Concat(y));Verify(dense,y);
            }
        }
        Type reverse=typeof(Set).GetNestedType("DenseReverseCursor",BindingFlags.NonPublic);
        int reverseBytes=reverse==null?0:(int)typeof(Unsafe).GetMethod("SizeOf").MakeGenericMethod(reverse).Invoke(null,null);
        return new {assertions=assertions-first,failures=0,growthCases,reverseCursorBytes=reverseBytes};
    }
    static void AppendScale()
    {
        foreach(int u in new[]{10000,262144})
        {
            var registry=Registry(u);
            foreach(int n in new[]{8,128,4096})
            foreach(string dist in new[]{"interleave","prepend","tail","subset"})
            {
                int[] x,y;
                if(dist=="interleave") { x=Enumerable.Range(0,n).Select(i=>i*2).ToArray();y=Enumerable.Range(0,n).Select(i=>i*2+1).ToArray(); }
                else if(dist=="prepend") { x=Enumerable.Range(u-n,n).ToArray();y=Enumerable.Range(0,n).ToArray(); }
                else if(dist=="tail") { x=Enumerable.Range(0,n).ToArray();y=Enumerable.Range(u-n,n).ToArray(); }
                else { x=Enumerable.Range(0,n).Select(i=>i*2).ToArray();y=x.Where((v,i)=>(i&1)==0).ToArray(); }
                if(dist=="interleave" && u==262144)
                {x=Enumerable.Range(0,n).Select(i=>i*64).ToArray(); y=Enumerable.Range(0,n).Select(i=>i*64+32).ToArray();}
                var a=Make(registry,x,Layout.Micro);var b=Make(registry,y,Layout.Dense);
                int recordCount=x.Concat(y).Select(i=>i>>4).Distinct().Count();
                var result=new Set(registry,recordCount,Layout.Micro);
                var shape=new{u,n,seed=2026100219,dist,left="Micro",right="Dense",digest=Digest(x),peerDigest=Digest(y),words=(u+63)/64,blocks=x.Select(i=>i>>4).Distinct().Count()};
                foreach(bool fresh in new[]{true,false})
                {
                    ModifyLoop(a,b,result,false,fresh,1);Verify(sink,x.Concat(y));
                    Measure(shape,fresh?"copy_append":"reuse_copy_append","Micro",rep=>ModifyLoop(a,b,result,false,fresh,rep),!fresh);
                    Verify(sink,x.Concat(y)); Verify(a,x); Verify(b,y);
                }
            }
        }
    }
}
