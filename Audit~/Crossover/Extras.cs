// Isolated algorithm and allocation diagnostics, not extra production representations.
using System;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using GameplayTags;
internal static partial class Crossover
{
    static int LowerBound(int[] a,int lo,int end,int key)
    {
        if(lo>=end||a[lo]>=key)return lo;
        int step=1;
        while(step<end-lo && a[lo+step]<key)step*=2;
        int left=lo+step/2+1,right=Math.Min(end,lo+step+1);
        while(left<right){int mid=left+(right-left)/2;if(a[mid]<key)left=mid+1;else right=mid;}
        return left;
    }
    static int MergeIntersection(int[] a,int[] b,int[] output)
    {
        int i=0,j=0,w=0;
        while(i<a.Length&&j<b.Length){int x=a[i],y=b[j];if(x<y)i++;else if(y<x)j++;else{output[w++]=x;i++;j++;}}
        return w;
    }
    static int GallopIntersection(int[] a,int[] b,int[] output)
    {
        if(a.Length>b.Length){var t=a;a=b;b=t;}
        int j=0,w=0;
        for(int i=0;i<a.Length;i++){int x=a[i];j=LowerBound(b,j,b.Length,x);if(j==b.Length)break;if(b[j]==x){output[w++]=x;j++;}}
        return w;
    }
    static int VectorIntersection(int[] a,int[] b,int[] output)
    {
        if(!Vector.IsHardwareAccelerated)return MergeIntersection(a,b,output);
        if(a.Length>b.Length){var t=a;a=b;b=t;}
        int j=0,w=0,width=Vector<int>.Count;
        for(int i=0;i<a.Length;i++)
        {
            int x=a[i];
            while(j<=b.Length-width && b[j+width-1]<x)j+=width;
            if(j<=b.Length-width)
            {if(!Vector.EqualsAll(Vector.Equals(new Vector<int>(b,j),new Vector<int>(x)),Vector<int>.Zero))output[w++]=x;}
            else {while(j<b.Length&&b[j]<x)j++;if(j==b.Length)break;if(b[j]==x){output[w++]=x;j++;}}
        }
        return w;
    }
    static int MergeDifference(int[] a,int[] b,int[] output)
    {
        int i=0,j=0,w=0;
        while(i<a.Length&&j<b.Length){int x=a[i],y=b[j];if(x<y){output[w++]=x;i++;}else if(y<x)j++;else{i++;j++;}}
        Array.Copy(a,i,output,w,a.Length-i);return w+a.Length-i;
    }
    static int GallopDifference(int[] a,int[] b,int[] output)
    {
        int i=0,w=0;
        for(int j=0;j<b.Length&&i<a.Length;j++)
        {
            int at=LowerBound(a,i,a.Length,b[j]);int length=at-i;
            Array.Copy(a,i,output,w,length);w+=length;i=at;
            if(i<a.Length&&a[i]==b[j])i++;
        }
        Array.Copy(a,i,output,w,a.Length-i);return w+a.Length-i;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int RatioLoop(int[] a,int[] b,int[] output,int algorithm,bool fresh,int repeats)
    {
        int n=0;
        for(int i=0;i<repeats;i++)
        {
            int[] destination=fresh?new int[algorithm<3?Math.Min(a.Length,b.Length):a.Length]:output;
            switch(algorithm){case 0:n=MergeIntersection(a,b,destination);break;case 1:n=GallopIntersection(a,b,destination);break;
                case 2:n=VectorIntersection(a,b,destination);break;case 3:n=MergeDifference(a,b,destination);break;case 4:n=GallopDifference(a,b,destination);break;}
            arraySink=destination;
        }
        return n;
    }
    static void ExtraTests()
    {
        var rng=new Random(971);
        for(int t=0;t<1000;t++)
        {
            var a=Enumerable.Range(0,400).Where(_=>rng.Next(4)==0).ToArray();var b=Enumerable.Range(0,400).Where(_=>rng.Next(5)==0).ToArray();
            var expected=a.Intersect(b).ToArray();
            for(int k=0;k<5;k++)
            {
                int count=RatioLoop(a,b,new int[a.Length],k,false,1);
                Check(arraySink.Take(count).SequenceEqual(k<3?expected:a.Except(b)),"array algorithm");
            }
        }
    }
    static void RatioMatrix(bool smoke)
    {
        ExtraTests();
        string[] names={"intersection_merge","intersection_gallop","intersection_vector","difference_merge","difference_gallop"};
        foreach(int small in smoke?new[]{8,128}:new[]{1,8,32,128,1024})
        foreach(int ratio in new[]{1,16,256})
        {
            int large=Math.Min(65536,small*ratio);
            foreach(int overlap in new[]{0,50,100})
            {
                var a=Enumerable.Range(0,large).Select(i=>2*i+2).ToArray();
                // Spread probes throughout the large array, not just its prefix.
                var b=Enumerable.Range(0,small).Select(i=>a[(int)((long)i*large/small)]+(i*100/small<overlap?0:1)).OrderBy(i=>i).ToArray();
                string digest=Digest(a,b);var output=new int[large];
                for(int alg=0;alg<5;alg++)foreach(bool fresh in new[]{false,true})
                {
                    int algorithm=alg;
                    Measure("ratio",names[alg]+(fresh?"_fresh":"_prepared"),2*large+3,small,"spread",20261001,large+":"+small+":"+overlap,digest,
                        r=>RatioLoop(a,b,output,algorithm,fresh,r),1,!fresh,new{large,small,overlap,vectorWidth=Vector<int>.Count,contract="independent full output; not Copy+Remove"});
                }
            }
        }
    }
    // Layout-only prototypes. No mutation/query API is claimed by these classes.
    sealed class Allocation0
    {
        public readonly TagRegistry registry;public readonly int[] ids;public readonly ulong[] dense;public readonly int count;
        public Allocation0(TagRegistry r,int[] source){registry=r;count=source.Length;ids=source.Length==0?Array.Empty<int>():(int[])source.Clone();dense=null;}
    }
    sealed class Allocation4
    {
        public readonly TagRegistry registry;public readonly int[] ids;public readonly ulong[] dense;public readonly int count;
        public readonly int a,b,c,d;
        public Allocation4(TagRegistry r,int[] source){registry=r;count=source.Length;dense=null;if(count>4)ids=(int[])source.Clone();else{a=count>0?source[0]:0;b=count>1?source[1]:0;c=count>2?source[2]:0;d=count>3?source[3]:0;}}
    }
    sealed class Allocation8
    {
        public readonly TagRegistry registry;public readonly int[] ids;public readonly ulong[] dense;public readonly int count;
        public readonly int a,b,c,d,e,f,g,h;
        public Allocation8(TagRegistry r,int[] source){registry=r;count=source.Length;dense=null;if(count>8)ids=(int[])source.Clone();else{a=count>0?source[0]:0;b=count>1?source[1]:0;c=count>2?source[2]:0;d=count>3?source[3]:0;e=count>4?source[4]:0;f=count>5?source[5]:0;g=count>6?source[6]:0;h=count>7?source[7]:0;}}
    }
    static object allocationSink;
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int AllocationLoop(TagRegistry reg,int[] ids,int inline,int repeats)
    {
        if(inline==0)for(int i=0;i<repeats;i++)allocationSink=new Allocation0(reg,ids);
        else if(inline==4)for(int i=0;i<repeats;i++)allocationSink=new Allocation4(reg,ids);
        else for(int i=0;i<repeats;i++)allocationSink=new Allocation8(reg,ids);
        return ids.Length;
    }
    static void AllocationMatrix()
    {
        var reg=Registry(128);
        foreach(int n in new[]{0,1,4,5,8,9,32,128})foreach(int inline in new[]{0,4,8})
        {
            var ids=Enumerable.Range(1,n).ToArray();
            Measure("inline","construct_inline"+inline,128,n,"contiguous",20261001,"layout_only",Digest(ids,ids),r=>AllocationLoop(reg,ids,inline,r),1,false,
                new{inline,scope="allocation-layout prototype only; not a RuntimeTagSet implementation"});
            // Keep 10,000 independent instances live; references themselves are allocated before accounting.
            var live=new object[10000];long start=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<live.Length;i++){AllocationLoop(reg,ids,inline,1);live[i]=allocationSink;}
            long bytes=GC.GetAllocatedBytesForCurrentThread()-start;GC.Collect();GC.KeepAlive(live);
            rows.Add(new{variant,round,stage="retained_inline",n,inline,objects=live.Length,ownedAllocatedBytes=bytes,
                note="all instances kept alive through full collection; includes headers and overflow arrays, excludes preallocated reference holder and shared registry"});
        }
    }
}
