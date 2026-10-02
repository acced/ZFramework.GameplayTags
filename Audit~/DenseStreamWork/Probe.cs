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
        var registry=TagRegistry.Create(settings);
        var rows=new List<object>();
        void MeasureWork(string shape,int[] left,int[] right)
        {
            var a=Set.FromSortedUnique(registry,left.Select(registry.GetTagAt).ToArray(),0,Layout.Micro);
            var b=Set.FromSortedUnique(registry,right.Select(registry.GetTagAt).ToArray(),0,Layout.Dense);
            var target=new Set(a);
            Set.WorkWordReads=Set.WorkEmits=Set.WorkWrites=Set.WorkFinds=Set.WorkSuffixMoves=Set.WorkGrowthCopies=0;
            target.AppendTags(b);
            long reads=Set.WorkWordReads,emits=Set.WorkEmits,writes=Set.WorkWrites,finds=Set.WorkFinds,moves=Set.WorkSuffixMoves,growth=Set.WorkGrowthCopies;
            var expected=left.Concat(right).Distinct().OrderBy(i=>i).ToArray();
            target.AssertInvariants();int at=0;
            foreach(var tag in target) if(at==expected.Length||tag.RuntimeIndex!=expected[at++])throw new Exception("diagnostic changed output");
            if(at!=expected.Length||target.Count!=expected.Length)throw new Exception("diagnostic count");
            rows.Add(new{variant=args[0],shape,sizes=left.Length+"/"+right.Length,wordReads=reads,emits,writes,finds,suffixMoves=moves,growthCopies=growth});
        }
        foreach(int n in new[]{8,128,4096})
        {
            MeasureWork("interleave",Enumerable.Range(0,n).Select(i=>i*64).ToArray(),Enumerable.Range(0,n).Select(i=>i*64+32).ToArray());
            MeasureWork("tail",Enumerable.Range(0,n).ToArray(),Enumerable.Range(u-n,n).ToArray());
            MeasureWork("subset",Enumerable.Range(0,n).Select(i=>i*2).ToArray(),Enumerable.Range(0,n).Where(i=>(i&1)==0).Select(i=>i*2).ToArray());
        }
        MeasureWork("small-right-tail",Enumerable.Range(0,4096).Select(i=>i*32).ToArray(),Enumerable.Range(u-8,8).ToArray());
        MeasureWork("same-key-new-bits",Enumerable.Range(0,4096).Select(i=>i*32).ToArray(),Enumerable.Range(0,4096).Select(i=>i*32+1).ToArray());
        MeasureWork("matched-prefix-then-gap",new[]{0,64,128,256,512},new[]{1,65,96,129,1024});
        File.WriteAllText(args[1],JsonSerializer.Serialize(rows));
    }
}
