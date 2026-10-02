using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using GameplayTags;
using GameplayTags.Experiments;
using Set=GameplayTags.Experiments.DirectArraySet;
using Layout=GameplayTags.Experiments.BitmapLayout;
internal static class WorkProbe
{
    static void Main(string[] args)
    {
        const int u=262144;
        var settings=UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        settings.ReplaceAll(Enumerable.Range(0,u).Select(i=>new GameplayTagDefinition("T"+i.ToString("D7"),"","Default",false,true)).ToList(),new List<GameplayTagRedirect>(),new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});
        var registry=TagRegistry.Create(settings);var rows=new List<object>();
        void Inspect(string shape,int[] left,int[] right)
        {
            left=left.Distinct().OrderBy(i=>i).ToArray();right=right.Distinct().OrderBy(i=>i).ToArray();
            var a=Set.FromSortedUnique(registry,left.Select(registry.GetTagAt).ToArray(),0,Layout.Micro);
            var b=Set.FromSortedUnique(registry,right.Select(registry.GetTagAt).ToArray(),0,Layout.Dense);
            var target=new Set(a);
            Set.WorkWordReads=Set.WorkEmits=Set.WorkWrites=Set.WorkFinds=Set.WorkSuffixMoves=Set.WorkGrowthCopies=Set.WorkCacheWrites=Set.WorkCacheReads=0;
            target.AppendTags(b);
            long reads=Set.WorkWordReads,emits=Set.WorkEmits,writes=Set.WorkWrites,finds=Set.WorkFinds,moves=Set.WorkSuffixMoves,growth=Set.WorkGrowthCopies,cw=Set.WorkCacheWrites,cr=Set.WorkCacheReads;
            var expected=left.Concat(right).Distinct().OrderBy(i=>i).ToArray();target.AssertInvariants();int at=0;
            foreach(var tag in target)if(at==expected.Length||tag.RuntimeIndex!=expected[at++])throw new Exception("diagnostic output");
            if(at!=expected.Length||target.Count!=expected.Length)throw new Exception("diagnostic count");
            if(args[0]=="cache32" && right.Length<=32 && right.Length>0 && left.Length>0)
            {
                if(reads!=u/64 || cw!=right.Select(i=>i>>4).Distinct().Count() || cw>32 || moves!=0)
                    throw new Exception("cache work invariants");
            }
            rows.Add(new{variant=args[0],shape,sizes=left.Length+"/"+right.Length,wordReads=reads,emits,writes,finds,suffixMoves=moves,growthCopies=growth,cacheWrites=cw,cacheReads=cr});
        }
        foreach(int n in new[]{8,31,32,33,128,4096})
        {
            Inspect("interleave",Enumerable.Range(0,n).Select(i=>i*64).ToArray(),Enumerable.Range(0,n).Select(i=>i*64+32).ToArray());
            Inspect("tail",Enumerable.Range(0,n).ToArray(),Enumerable.Range(u-n,n).ToArray());
            Inspect("subset",Enumerable.Range(0,n).Select(i=>i*2).ToArray(),Enumerable.Range(0,n).Where(i=>(i&1)==0).Select(i=>i*2).ToArray());
        }
        foreach(int n in new[]{8,32,33})
        {
            var random=new Random(2026100271);var values=new HashSet<int>();while(values.Count<2*n)values.Add(random.Next(u));
            var seq=values.ToArray();var x=seq.Take(n).ToArray();var y=x.Take(n/2).Concat(seq.Skip(n).Take(n-n/2)).ToArray();
            Inspect("random-full-domain",x,y);
            Inspect("large-left-small-tail",Enumerable.Range(0,4096).Select(i=>i*32).ToArray(),Enumerable.Range(u-n,n).ToArray());
        }
        Inspect("same-key-new-bits",Enumerable.Range(0,32).Select(i=>i*32).ToArray(),Enumerable.Range(0,32).Select(i=>i*32+1).ToArray());
        File.WriteAllText(args[1],JsonSerializer.Serialize(rows));
    }
}
