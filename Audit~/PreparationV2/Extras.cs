using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GameplayTags;
#if BULK_MICRO
using Set = GameplayTags.Experiments.DirectArraySet;
using Layout = GameplayTags.Experiments.BitmapLayout;
#else
using Set = GameplayTags.RuntimeTagSet;
using Layout = GameplayTags.TagSetStorage;
#endif
internal static partial class BulkProbe
{
    static object MoreTests()
    {
#if PREPARATION_V2
        long start=assertions;var reg=Registry(513);var foreign=Registry(3);
        var modes=new[]{(Layout)1,Layout.Dense};var random=new Random(20261023);
        foreach(Layout from in modes)foreach(Layout to in modes)
        {
            layout=from;
            for(int trial=0;trial<256;trial++)
            {
                int[] ids=Enumerable.Range(0,reg.Count).Where(_=>random.Next(8)==0).ToArray();
                if(trial==0)ids=Array.Empty<int>();
                if(trial==1)ids=new[]{513};
                if(trial==2)ids=new[]{0,1,15,16,31,32,63,64,255,256,511,513};
                var source=Set.FromSortedUnique(reg,Tags(reg,ids),400,from);
                int[] before=Snapshot(source);long bytes=source.BufferBytes;
                foreach(int reserve in new[]{0,1,17,400,2000})
                {
                    var actual=source.CopyAsStorage(to,reserve);
                    var reference=new Set(reg,Math.Max(reserve,source.Count),to);reference.CopyFrom(source);
                    Verify(actual,ids);SameStorage(actual,reference);Verify(source,before);
                    Check(source.BufferBytes==bytes,"conversion changed source reservation");
                    Check(!ReferenceEquals(actual,source),"conversion shared wrapper");
                    actual.Clear();Verify(source,before);
                    actual=source.CopyAsStorage(to,reserve);
                    source.Clear();Verify(actual,before);source.ResetFromSortedUnique(Tags(reg,before));
                }
                Throws<ArgumentOutOfRangeException>(()=>source.CopyAsStorage(Layout.Auto));
                Throws<ArgumentOutOfRangeException>(()=>source.CopyAsStorage((Layout)99));
                Throws<ArgumentOutOfRangeException>(()=>source.CopyAsStorage(to,-1));
                Verify(source,before);Check(source.BufferBytes==bytes,"invalid conversion modified source");
            }
        }
        foreach(Layout mode in new[]{Layout.Auto,(Layout)1,Layout.Dense})
        {
            layout=mode;
            var work=new Set(reg,1,mode);work.AddTag(reg.GetTagAt(513));long bytes=work.BufferBytes;
            var bad=Tags(reg,Enumerable.Range(0,100));bad[99]=foreign.GetTagAt(1);
            Throws<ArgumentException>(()=>work.ResetFromSortedUnique(bad));
            Verify(work,new[]{513});Check(work.BufferBytes==bytes,"late invalid grew output");
            Throws<ArgumentException>(()=>work.ResetFromSortedUnique(new[]{default(RuntimeTag)}));
            Verify(work,new[]{513});
            work.ResetFromSortedUnique(new[]{reg.GetTagAt(0)});Verify(work,new[]{0});
            work.ResetFromSortedUnique(Array.Empty<RuntimeTag>());Verify(work,Array.Empty<int>());
            work.ResetFromSortedUnique(new[]{reg.GetTagAt(513)});Verify(work,new[]{513});
            // Oversized duplicate stream cannot fit in the final Array buffer before dedup.
            var repeated=Tags(reg,Enumerable.Range(0,2048).Select(i=>i%17));
            var saved=(RuntimeTag[])repeated.Clone();
            var built=Set.FromUnordered(reg,repeated,3,mode);
            Verify(built,Enumerable.Range(0,17));Check(repeated.SequenceEqual(saved),"unordered input mutated");
            SameStorage(built,Legacy(reg,repeated,3));
            // Failure anywhere in fresh admission must not mutate caller storage.
            var invalid=(RuntimeTag[])repeated.Clone();invalid[invalid.Length-1]=foreign.GetTagAt(1);
            Throws<ArgumentException>(()=>Set.FromUnordered(reg,invalid,0,mode));
            Throws<ArgumentException>(()=>Set.FromSortedUnique(reg,Tags(reg,new[]{0,1,1}),0,mode));
            Check(repeated.SequenceEqual(saved),"failure modified input");
        }
        // Dirty prepared targets exercise real cross-representation CopyFrom contracts.
        var allocation=new List<object>();
        foreach(Layout from in modes)foreach(Layout to in modes)
        {
            var a=Set.FromSortedUnique(reg,Tags(reg,Enumerable.Range(0,129).Select(i=>i*3)),0,from);
            var target=new Set(reg,reg.Count,to);
            for(int i=0;i<100;i++)target.CopyFrom(a);
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            long b=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<1000;i++)target.CopyFrom(a);
            long delta=GC.GetAllocatedBytesForCurrentThread()-b;Check(delta==0,"prepared conversion allocated");
            Verify(target,Snapshot(a));allocation.Add(new{from=from.ToString(),to=to.ToString(),bytes=delta});
        }
        return new{assertions=assertions-start,failures=0,preparedConversions=allocation,
            conversion="independent target; capacity=max(requested,Count), not source Capacity",crossFamily="not implemented"};
#else
        return new{assertions=0,notApplicable="PR12 control has no CopyAsStorage"};
#endif
    }
#if PREPARATION_V2
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int ConversionLoop(Set source,Set peer,Set output,Layout target,int uses,bool direct,int repeats)
    {
        if(uses<0)
        {for(int i=0;i<repeats;i++)output.CopyFrom(source);sink=output;return sink.Count;}
        for(int i=0;i<repeats;i++)
        {
            Set result;
            if(direct)result=source.CopyAsStorage(target);
            else {result=new Set(source.Registry,source.Count,target);result.CopyFrom(source);}
            sink=result;
            for(int j=0;j<uses;j++)sink=Set.Union(result,peer,target);
        }
        return sink.Count;
    }
    static void ConversionMatrix(bool smoke)
    {
        foreach(int u in smoke?new[]{10000}:new[]{10000,262144})
        {
            var reg=Registry(u);
            foreach(int seed in smoke?new[]{20261023}:new[]{20261023,20261103})
            foreach(string distribution in new[]{"contiguous","clusters4","scattered"})
            foreach(int n in smoke?new[]{0,8,128}:new[]{0,1,8,128,4096})
            {
                var sequence=Sequence(u,2*n,distribution,seed);
                int[] x=sequence.Take(n).OrderBy(i=>i).ToArray();
                int[] y=x.Take(n/2).Concat(sequence.Skip(n).Take(n-n/2)).OrderBy(i=>i).ToArray();
                foreach(Layout from in new[]{(Layout)1,Layout.Dense})foreach(Layout to in new[]{(Layout)1,Layout.Dense})
                {
                    var source=Set.FromSortedUnique(reg,Tags(reg,x),n+17,from);
                    var peer=Set.FromSortedUnique(reg,Tags(reg,y),0,to);var work=new Set(reg,n+17,to);
                    foreach(int uses in smoke?new[]{0,-1}:new[]{0,-1,1,16})
                    {
                        string operation=uses<0?"copy_into":uses==0?"new_convert":"convert_plus_"+uses+"_unions";
                        var shape=new{registryCount=reg.Count,words=(reg.Count+63)/64,source=from.ToString(),target=to.ToString(),
                            sourceBuffer=source.BufferBytes,requestedCapacity=n,blocks16=x.Select(id=>id>>4).Distinct().Count(),
                            uses,peerDigest=Digest(y),rule="explicit target; no selector; peer preparation excluded for both"};
                        foreach(bool direct in uses<0?new[]{false}:((seed+n+round)&1)==0?new[]{false,true}:new[]{true,false})
                        {
                            ConversionLoop(source,peer,work,to,uses,direct,1);Verify(sink,uses>0?x.Concat(y):x);
                            Measure(u,n,distribution,seed,from+"->"+to,operation,
                                uses<0?"copyfrom_into":direct?"direct":"copyfrom",Digest(x),
                                r=>ConversionLoop(source,peer,work,to,uses,direct,r),uses<0,shape);
                            Verify(sink,uses>0?x.Concat(y):x);Verify(source,x);Verify(peer,y);
                        }
                    }
                }
            }
        }
    }
#endif
}
