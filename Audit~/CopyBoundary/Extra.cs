using System;
using System.Linq;
using System.Runtime.CompilerServices;
using GameplayTags;
using GameplayTags.Experiments;
using Set=GameplayTags.Experiments.DirectArraySet;
using Layout=GameplayTags.Experiments.BitmapLayout;
internal static partial class MixedProbe
{
    static object BoundaryTests()
    {
        long first=assertions;int cases=0;
        foreach(int u in new[]{0,1,17,257})
        {
            var r=Registry(u);var foreign=Registry(u);
            foreach(var sourceMode in layouts)
            foreach(var targetMode in layouts)
            foreach(int capacity in new[]{0,1,u})
            foreach(bool dirty in new[]{false,true})
            {
                int[] ids=dirty&&u>0?new[]{0,u-1}.Distinct().ToArray():Array.Empty<int>();
                var source=new Set(r,0,sourceMode);
                var target=Make(r,ids,targetMode,capacity);
                long size=target.BufferBytes;
                target.CopyFrom(source);Verify(target,Array.Empty<int>());
                Check(target.BufferBytes==size,"empty copy changed reservation");
                target.CopyFrom(target);Verify(target,Array.Empty<int>());
                if(u>0){target.AddTag(r.GetTagAt(u-1));Verify(target,new[]{u-1});}
                int[] before=Snapshot(target);bool rejected=false;
                try{target.CopyFrom(new Set(foreign,0,sourceMode));}
                catch(ArgumentException){rejected=true;}
                Check(rejected,"empty foreign source bypassed registry check");Verify(target,before);
                rejected=false;try{target.CopyFrom(null);}catch(ArgumentNullException){rejected=true;}
                Check(rejected,"null copy was not rejected");Verify(target,before);
                Set.UnionInto(source,source,target);Verify(target,Array.Empty<int>());
                if(u>0)target.AddTag(r.GetTagAt(0));
                target.CopyFrom(source);Verify(target,Array.Empty<int>());Verify(source,Array.Empty<int>());
                cases++;
            }
        }
        return new{assertions=assertions-first,failures=0,cases};
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int EmptyCaseLoop(Set source,Set output,RuntimeTag tag,int op,bool dirty,int repeats)
    {
        for(int i=0;i<repeats;i++)
        {
            if(dirty)output.AddTag(tag); // Included in all variants, not free reset.
            if(op==0)output.CopyFrom(source);
            else if(op==1){output.Clear();output.AppendTags(source);}
            else Set.UnionInto(source,source,output);
            sink=output;
        }
        return sink.Count;
    }
    static void EmptyCases()
    {
        foreach(int u in new[]{1,257,10000,262144})
        {
            var r=Registry(u);var tag=r.GetTagAt(u-1);
            foreach(var sm in layouts)
            foreach(var tm in layouts)
            foreach(bool dirty in new[]{false,true})
            for(int op=0;op<3;op++)
            {
                var s=new Set(r,0,sm);var result=new Set(r,Math.Min(u,8),tm);
                var shape=new{u,n=0,seed=2026102501,dist=dirty?"dirty-target":"empty-target",left=sm.ToString(),right=tm.ToString(),digest=Digest(Array.Empty<int>()),peerDigest=Digest(Array.Empty<int>()),words=(u+63)/64,blocks=0};
                string operation=op==0?"empty_copy":op==1?"empty_clear_append":"empty_same_union";
                int selected=op;EmptyCaseLoop(s,result,tag,op,dirty,1);Verify(result,Array.Empty<int>());
                Measure(shape,operation,tm.ToString(),rep=>EmptyCaseLoop(s,result,tag,selected,dirty,rep),true);
                Verify(result,Array.Empty<int>());Verify(s,Array.Empty<int>());
            }
        }
    }
}
