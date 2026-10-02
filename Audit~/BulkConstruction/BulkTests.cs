using System;
using System.Collections.Generic;
using System.Linq;
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
    static int[] Snapshot(Set set)
    {var result=new int[set.Count];int i=0;foreach(var tag in set)result[i++]=tag.RuntimeIndex;return result;}
    static void SameStorage(Set a,Set b)
    {
        Check(a.BufferBytes==b.BufferBytes,"same requested capacity must retain same member-buffer bytes");
#if BULK_MICRO
        Check(a.Layout==b.Layout,"layout");Check(a.ReservedMemberCapacity==b.ReservedMemberCapacity,"capacity");
#else
        Check(a.Storage==b.Storage,"storage");Check(a.Capacity==b.Capacity,"capacity");
#endif
    }
    static object Tests()
    {
        Layout saved=layout;var reg=Registry(513);var foreign=Registry(5);
        var modes=new[]{Layout.Auto,(Layout)1,Layout.Dense};
        foreach(Layout mode in modes)
        {
            layout=mode;
            foreach(int capacity in new[]{0,1,17,300})
            for(int mask=0;mask<256;mask++)
            {
                int[] edge={0,1,15,16,63,64,128,513};
                int[] ids=Enumerable.Range(0,8).Where(i=>(mask&(1<<i))!=0).Select(i=>edge[i]).ToArray();
                RuntimeTag[] input=Tags(reg,ids);
                var control=Legacy(reg,input,capacity);
                var a=Set.FromSortedUnique(reg,input,capacity,mode);Verify(a,ids);SameStorage(a,control);
                var duplicate=input.Reverse().Concat(input).ToArray();
                var b=Set.FromUnordered(reg,duplicate,capacity,mode);Verify(b,ids);SameStorage(b,Legacy(reg,duplicate,capacity));
                Check(!ReferenceEquals(a,b),"independent wrappers");
                var work=Legacy(reg,Tags(reg,new[]{2,62,127,512}),300);
                work.ResetFromSortedUnique(input);Verify(work,ids);
                var clone=new Set(a);clone.Clear();Verify(a,ids);
                if(input.Length>0){RuntimeTag old=input[0];input[0]=default(RuntimeTag);Verify(a,ids);input[0]=old;}
                a.AddTag(reg.GetTagAt(200));Verify(b,ids);a.RemoveTag(reg.GetTagAt(200));
            }
            var random=new Random(20261002);
            for(int trial=0;trial<1000;trial++)
            {
                int[] raw=Enumerable.Range(0,random.Next(100)).Select(_=>random.Next(reg.Count)).ToArray();
                int[] ids=raw.Distinct().OrderBy(i=>i).ToArray();RuntimeTag[] tags=Tags(reg,ids);
                var a=Set.FromSortedUnique(reg,tags,0,mode);var b=Set.FromUnordered(reg,Tags(reg,raw),0,mode);
                Verify(a,ids);Verify(b,ids);SameStorage(b,Legacy(reg,Tags(reg,raw)));
                int[] rhs=Enumerable.Range(0,random.Next(80)).Select(_=>random.Next(reg.Count)).Distinct().OrderBy(i=>i).ToArray();
                var r=Legacy(reg,Tags(reg,rhs));var u=Set.Union(a,r,mode);Verify(u,ids.Concat(rhs));
                var copy=new Set(a);copy.AppendTags(r);Verify(copy,ids.Concat(rhs));
                copy=new Set(a);copy.RemoveTags(r);Verify(copy,ids.Except(rhs));
                Set.UnionInto(a,r,a);Verify(a,ids.Concat(rhs));Verify(b,ids);
                var reference=new HashSet<int>(ids);
                for(int step=0;step<32;step++)
                {
                    int id=random.Next(reg.Count);
                    if(random.Next(2)==0)Check(b.AddTag(reg.GetTagAt(id))==reference.Add(id),"post-build add");
                    else Check(b.RemoveTag(reg.GetTagAt(id))==reference.Remove(id),"post-build remove");
                }
                Verify(b,reference);
            }
            // All 16-bit masks, including count != record-count; physical word/key grouping.
            for(int mask=0;mask<65536;mask++)
            {
                RuntimeTag[] tags=Enumerable.Range(0,16).Where(i=>(mask&(1<<i))!=0).Select(i=>reg.GetTagAt(16+i)).ToArray();
                var a=Set.FromSortedUnique(reg,tags,0,mode);Check(a.Count==tags.Length,"mask count");
                for(int bit=0;bit<16;bit++)Check(a.HasTagExact(reg.GetTagAt(16+bit))==((mask&(1<<bit))!=0),"mask membership");
            }
            // Rejected input must not partially clear/grow/write an existing output.
            var dirty=Legacy(reg,Tags(reg,new[]{3,62,100,500}),50);var expected=Snapshot(dirty);long before=dirty.BufferBytes;
            RuntimeTag[][] invalid={new[]{reg.GetTagAt(2),reg.GetTagAt(2)},new[]{reg.GetTagAt(3),reg.GetTagAt(1)},
                new[]{reg.GetTagAt(1),default(RuntimeTag)},new[]{reg.GetTagAt(1),foreign.GetTagAt(2)}};
            foreach(var input in invalid)
            {
                Throws<ArgumentException>(()=>Set.FromSortedUnique(reg,input,0,mode));
                Throws<ArgumentException>(()=>dirty.ResetFromSortedUnique(input));Verify(dirty,expected);
                Check(dirty.BufferBytes==before,"invalid input changed capacity");
            }
            Throws<ArgumentException>(()=>Set.FromUnordered(reg,new[]{reg.GetTagAt(1),default(RuntimeTag)},0,mode));
            Throws<ArgumentException>(()=>Set.FromUnordered(reg,new[]{foreign.GetTagAt(1)},0,mode));
            Throws<ArgumentNullException>(()=>Set.FromSortedUnique(null,Array.Empty<RuntimeTag>(),0,mode));
            Throws<ArgumentOutOfRangeException>(()=>Set.FromSortedUnique(reg,Array.Empty<RuntimeTag>(),-1,mode));
            Throws<ArgumentOutOfRangeException>(()=>Set.FromUnordered(reg,Array.Empty<RuntimeTag>(),0,(Layout)99));
            var empty=Set.FromSortedUnique(reg,default(ReadOnlySpan<RuntimeTag>),0,mode);Verify(empty,Array.Empty<int>());
            var grown=new Set(reg,0,mode);grown.ResetFromSortedUnique(Tags(reg,new[]{0,63,64,513}));Verify(grown,new[]{0,63,64,513});
            var all=Tags(reg,Enumerable.Range(0,reg.Count));var dup=all.Concat(all).ToArray();
            var saturated=Set.FromUnordered(reg,dup,0,mode);Verify(saturated,Enumerable.Range(0,reg.Count));SameStorage(saturated,Legacy(reg,dup));
        }
        // Partial final bitmap word and empty registry.
        foreach(int leaves in new[]{0,1,14,15,16,62,63,64,65,126,127,128})
        {
            var r=Registry(leaves);RuntimeTag[] tags=Tags(r,Enumerable.Range(0,r.Count));
            foreach(Layout mode in modes)
            {
                layout=mode;var s=Set.FromSortedUnique(r,tags,0,mode);Verify(s,Enumerable.Range(0,r.Count));
                s.ResetFromSortedUnique(Array.Empty<RuntimeTag>());Verify(s,Array.Empty<int>());
                s.ResetFromSortedUnique(tags);Verify(s,Enumerable.Range(0,r.Count));
            }
        }
        // Capacity sufficient for ANY distribution in these loops. No workspace/pool.
        var allocationRows=new List<object>();
        foreach(Layout mode in modes)
        {
            layout=mode;RuntimeTag[] a=Tags(reg,Enumerable.Range(0,128)),b=Tags(reg,Enumerable.Range(0,128).Select(i=>i*4));
            var work=new Set(reg,256,mode);
            for(int i=0;i<100;i++){work.ResetFromSortedUnique(a);work.ResetFromSortedUnique(b);}
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            long start=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<1000;i++){work.ResetFromSortedUnique(a);work.ResetFromSortedUnique(b);work.ResetFromSortedUnique(Array.Empty<RuntimeTag>());}
            long allocated=GC.GetAllocatedBytesForCurrentThread()-start;Check(allocated==0,"prepared reset must allocate zero");
            allocationRows.Add(new{mode=mode.ToString(),allocated,operations=3000});
        }
        long p=GC.GetAllocatedBytesForCurrentThread();sink=Legacy(reg,Array.Empty<RuntimeTag>());
        long positive=GC.GetAllocatedBytesForCurrentThread()-p;GC.KeepAlive(sink);Check(positive>0,"positive allocation control");
        // Error path is reported separately, never hidden in successful-path measurements.
        RuntimeTag[] bad={default(RuntimeTag)};
        for(int i=0;i<5;i++)try{Set.FromSortedUnique(reg,bad);}catch(ArgumentException){}
        p=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<32;i++)try{Set.FromSortedUnique(reg,bad);}catch(ArgumentException){}
        double errorBytes=(GC.GetAllocatedBytesForCurrentThread()-p)/32.0;
        layout=saved;
        return new{assertions,failures=0,positiveAllocationBytes=positive,errorBytesPerInvalidInput=errorBytes,prepared=allocationRows,
            inputContract="strict sorted unique or unordered duplicates; foreign/default handles rejected; no concurrent input mutation",
            nativeUnity="not_run",additionalInstanceFields=0};
    }
}
