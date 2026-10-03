using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GameplayTags;
using GameplayTags.Experiments;
internal static class BridgeMutationTests
{
    static long checks;static int checksum;
    static void Check(bool ok,string reason) {checks++;if(!ok)throw new Exception(reason);}
    static TagRegistry Registry(bool initialize)
    {
        var s=UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        var names=Enumerable.Range(0,140).Select(i=>"Root.Leaf"+i.ToString("D3")).Concat(new[]{"Marker"});
        s.ReplaceAll(names.Select(n=>new GameplayTagDefinition(n,"","Default",false,true)).ToList(),new List<GameplayTagRedirect>(),new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});
        if(!initialize)return TagRegistry.Create(s);GameplayTagManager.Initialize(s,true);return GameplayTagManager.CurrentRegistry;
    }
    static int Main(string[] args)
    {
        var r=Registry(true);var alien=Registry(false);var root=r.Resolve("Root");var low=r.Resolve("Root.Leaf000");var high=r.Resolve("Root.Leaf139");var marker=r.Resolve("Marker");
        var parentTag=GameplayTagManager.RequestTag("Root");
        var positive=new GameplayTagQuery(GameplayTagQueryExpression.AnyTagsMatch().AddTag(parentTag)).Freeze(r);
        var negative=new GameplayTagQuery(GameplayTagQueryExpression.NoTagsMatch().AddTag(parentTag)).Freeze(r);
        var all=new GameplayTagQuery(GameplayTagQueryExpression.AllTagsMatch().AddTag(parentTag)).Freeze(r);
        var allocationRows=new List<object>();
        foreach(var layout in new[]{BitmapLayout.Micro,BitmapLayout.Dense,BitmapLayout.Auto})
        {
            var set=new DirectArraySet(r,r.Count,layout);
            foreach(bool retainOne in new[]{false,true})
            {
                set.Clear();for(int id=0;id<r.Count;id++)set.AddTag(r.GetTagAt(id));
                for(int id=0;id<r.Count;id++)if(!retainOne||id!=high.RuntimeIndex)set.RemoveTag(r.GetTagAt(id));
                for(int start=0;start<=r.Count;start++)for(int end=start;end<=r.Count;end++)
                    Check(set.AnyInRange(start,end)==(retainOne&&start<=high.RuntimeIndex&&high.RuntimeIndex<end),"inactive-tail half-open range");
                Check(set.HasTag(root)==retainOne&&set.Matches(positive)==retainOne&&set.Matches(negative)!=retainOne,"inactive-tail hierarchy/query");
            }
            set.Clear();
            for(int type=1;type<=6;type++)
            {
                var foreignConstant=new GameplayTagQuery(new GameplayTagQueryExpression((GameplayTagQueryExpressionType)type)).Freeze(alien);
                foreach(bool populated in new[]{false,true})
                {
                    if(populated)set.AddTag(low);bool rejected=false;try{set.Matches(foreignConstant);}catch(ArgumentException){rejected=true;}
                    Check(rejected,"constant foreign query bypassed registry admission");set.Clear();
                }
            }
            Action body=()=>
            {
                Check(!set.HasTag(root)&&!set.Matches(positive)&&set.Matches(negative)&&!set.Matches(all),"prepared empty negative path");
                set.AddTag(low);Check(set.HasTag(root)&&set.Matches(positive)&&!set.Matches(negative)&&set.Matches(all),"prepared low mutation hit");checksum++;
                set.RemoveTag(low);Check(!set.HasTag(root)&&!set.Matches(positive)&&set.Matches(negative),"prepared removed low miss");
                set.AddTag(high);Check(set.HasTag(root)&&set.Matches(positive)&&!set.Matches(negative),"prepared high mutation hit");checksum++;
                set.RemoveTag(high);set.AddTag(marker);Check(!set.HasTag(root)&&!set.Matches(positive)&&set.Matches(negative),"prepared unrelated member miss");set.RemoveTag(marker);
            };
            for(int i=0;i<128;i++)body();long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<2048;i++)body();long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            Check(allocated==0,"mutation/miss bridge allocated");set.AssertInvariants();Check(set.Count==0,"final prepared state");allocationRows.Add(new {layout=layout.ToString(),iterations=2048,allocated_bytes=allocated,includes="add/remove low/high, empty and unrelated misses, positive/negative/all frozen paths"});
        }
        var result=new {pass=true,checks,checksum,allocationRows};string json=JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[0],json);Console.WriteLine(json);return 0;
    }
}
