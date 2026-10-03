using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GameplayTags;
using GameplayTags.Experiments;
internal static class IntegrationInterop
{
    static long checks;
    static void Check(bool value,string message) {checks++;if(!value)throw new Exception(message);}
    static void Reject(Action body) {bool rejected=false;try {body();}catch(ArgumentException){rejected=true;}Check(rejected,"foreign registry accepted");}
    static GameplayTagSettings Settings()
    {
        var s=UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        s.ReplaceAll(new[]{"Actor.Enemy.Boss","Actor.Enemy.Grunt","Actor.Friend","World.Zone"}.Select(n=>new GameplayTagDefinition(n,"","Default",false,true)).ToList(),new List<GameplayTagRedirect>(),new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});return s;
    }
    static int[] Snapshot(DirectArraySet s) {var ids=new List<int>();foreach(var t in s)ids.Add(t.RuntimeIndex);return ids.ToArray();}
    static int Main(string[] args)
    {
        GameplayTagManager.Initialize(Settings(),true);var r=GameplayTagManager.CurrentRegistry;
        var boss=r.Resolve("Actor.Enemy.Boss");var friend=r.Resolve("Actor.Friend");var parent=r.Resolve("Actor.Enemy");
        var query=new GameplayTagQuery(GameplayTagQueryExpression.AllTagsMatch().AddTag(GameplayTagManager.RequestTag("Actor.Enemy"))).Freeze(r);
        Check(!query.Matches(null),"existing null call changed or became ambiguous");
        foreach(var oldLayout in new[]{TagSetStorage.Sparse,TagSetStorage.Dense}) foreach(var newLayout in new[]{BitmapLayout.Micro,BitmapLayout.Dense,BitmapLayout.Auto})
        {
            var original=new RuntimeTagSet(r,8,oldLayout);original.AddTag(boss);original.AddTag(friend);
            var handles=new List<RuntimeTag>();foreach(var t in original)handles.Add(t);
            var selected=DirectArraySet.FromUnordered(r,handles.ToArray(),8,newLayout);
            Check(ReferenceEquals(selected.Registry,original.Registry),"separate owner identity");Check(selected.Count==original.Count,"transfer count");
            foreach(var t in original)Check(selected.HasTagExact(t),"lost exact membership");
            Check(original.HasTag(parent),"original hierarchy regressed");Check(!selected.HasTagExact(parent),"exact API gained implicit parents");Check(query.Matches(original),"frozen original query regressed");
            Check(selected.HasTag(parent)&&selected.Matches(query),"hierarchy/query bridge rejected descendant");Check(!selected.HasTag(default)&&!selected.Matches(null),"default/null bridge semantics");
            selected.RemoveTag(boss);Check(original.HasTagExact(boss),"selected mutation shared original storage");Check(!selected.HasTag(parent)&&!selected.Matches(query),"bridge retained removed descendant");selected.AddTag(boss);
            original.RemoveTag(friend);Check(selected.HasTagExact(friend),"original mutation shared selected storage");
            var roundTrip=new RuntimeTagSet(r,selected.Count,oldLayout);foreach(var t in selected)roundTrip.AddTag(t);
            Check(query.Matches(roundTrip)&&roundTrip.Count==2,"public-handle roundtrip");
            var alien=TagRegistry.Create(Settings());var before=Snapshot(selected);
            Reject(()=>selected.AddTag(alien.Resolve("Actor.Enemy.Boss")));Reject(()=>selected.CopyFrom(new DirectArraySet(alien,0,newLayout)));
            Reject(()=>selected.HasTag(alien.Resolve("Actor.Enemy")));
            var foreignQuery=new GameplayTagQuery(GameplayTagQueryExpression.AllTagsMatch().AddTag(GameplayTagManager.RequestTag("Actor.Enemy"))).Freeze(alien);
            Reject(()=>selected.Matches(foreignQuery));Reject(()=>new DirectArraySet(r,0,newLayout).Matches(foreignQuery));
            Check(!selected.Matches(new GameplayTagQuery().Freeze(r)),"empty query changed semantics");
            Check(before.SequenceEqual(Snapshot(selected)),"foreign failure changed target");selected.AssertInvariants();
        }
        var supportsFrozen=typeof(FrozenGameplayTagQuery).GetMethods().Any(m=>m.Name=="Matches"&&m.GetParameters().Any(p=>p.ParameterType==typeof(DirectArraySet)));
        var hasHierarchy=typeof(DirectArraySet).GetMethods().Any(m=>m.Name=="HasTag");
        var result=new {checks,pass=true,shared_runtime_handles=true,independent_storage=true,original_hierarchy_and_frozen_query_preserved=true,direct_set_has_hierarchy_api=hasHierarchy,direct_set_accepted_by_public_frozen_query_overload=supportsFrozen,direct_set_matches_query_bridge=true,interop="Public RuntimeTag enumeration transfers membership; direct allocation-free hierarchy/query bridge; no automatic RuntimeTagSet replacement"};
        var json=JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[0],json);Console.WriteLine(json);return 0;
    }
}
