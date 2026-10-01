using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GameplayTags;
using GameplayTags.Experiments;

internal static class CopyRegression
{
    private static int assertions;
    private static void Check(RobinMicroBitmapSet s, params int[] ids)
    {
        s.AssertInvariants(); var actual = new HashSet<int>();
        foreach(var tag in s) actual.Add(tag.RuntimeIndex);
        assertions++;
        if(s.Count != ids.Length || !actual.SetEquals(ids)) throw new Exception("copy contents/count");
    }
    public static int Main(string[] args)
    {
        bool old = args.Length != 0 && args[0] == "old";
        var settings = UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        settings.ReplaceAll(Enumerable.Range(0,65536).Select(i=>new GameplayTagDefinition("T"+i.ToString("D7"),"","Default",false,true)).ToList(),
            new List<GameplayTagRedirect>(),new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});
        TagRegistry registry=TagRegistry.Create(settings);
        try
        {
            foreach(int capacity in new[]{4,8,128}) foreach(int key in new[]{0,16,4096,65520})
            {
                var source = new RobinMicroBitmapSet(registry,capacity);
                source.AddTag(registry.GetTagAt(key));source.AddTag(registry.GetTagAt(key+3));source.AddTag(registry.GetTagAt(key+15));
                int unrelated=(key+32)%registry.Count;
                source.AddTag(registry.GetTagAt(unrelated));source.RemoveTag(registry.GetTagAt(unrelated));
                var target = new RobinMicroBitmapSet(registry);
                target.CopyFrom(source);Check(target,key,key+3,key+15);
                target.RemoveTag(registry.GetTagAt(key));Check(source,key,key+3,key+15);
                target.CopyFrom(source);Check(target,key,key+3,key+15);
                var empty=new RobinMicroBitmapSet(registry);
                Check(RobinMicroBitmapSet.Union(source,empty),key,key+3,key+15);
                Check(RobinMicroBitmapSet.Union(empty,source),key,key+3,key+15);
                empty.AppendTags(source);Check(empty,key,key+3,key+15);
                var dirty=new RobinMicroBitmapSet(registry);dirty.AddTag(registry.GetTagAt(unrelated));
                dirty.CopyFrom(source);Check(dirty,key,key+3,key+15);
                RobinMicroBitmapSet.UnionInto(source,source,dirty);Check(dirty,key,key+3,key+15);
                source.Clear();source.AddTag(registry.GetTagAt(key+3));target.Clear();
                target.CopyFrom(source);Check(target,key+3);
                target.CopyFrom(new RobinMicroBitmapSet(registry,128));Check(target);
            }
            if(old){Console.WriteLine("ERROR expected original NullReferenceException was not reproduced");return 1;}
            Console.WriteLine("COPY_REGRESSION "+JsonSerializer.Serialize(new{assertions,failed=0,old_source_reproduction=false}));return 0;
        }
        catch(NullReferenceException ex)
        {
            if(!old)throw;
            Console.WriteLine("EXPECTED_OLD_FAILURE NullReferenceException "+ex.StackTrace);
            return 0;
        }
    }
}
