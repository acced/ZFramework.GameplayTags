// Untimed, separately instrumented build. This static test hook never enters benchmarks.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using GameplayTags;
using GameplayTags.Experiments;
using Set = GameplayTags.Experiments.DirectArraySet;
class FaultProbe
{
    static int Main(string[] args)
    {
        var settings=UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        settings.ReplaceAll(Enumerable.Range(0,8193).Select(i=>new GameplayTagDefinition("T"+i.ToString("D7"),"","Default",false,true)).ToList(),new List<GameplayTagRedirect>(),new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});
        var r=TagRegistry.Create(settings);int cases=0,failures=0,changedCases=0,changedFailures=0;
        var observations=new List<object>();
        bool changed=args[0]=="rotate" || args[0]=="buffer32";
        foreach(int size in new[]{0,1,8,33,128})
        foreach(int capacity in new[]{0,1,4,31,128})
        foreach(int fail in Enumerable.Range(0,9))
        {
            Set.FailGrowth=-1;
            var original=new Set(r,capacity,BitmapLayout.Micro);
            for(int i=0;i<size;i++)original.AddTag(r.GetTagAt(i*32));
            var source=new Set(r,0,BitmapLayout.Dense);
            for(int i=0;i<257;i++)
            {
                source.AddTag(r.GetTagAt(i*16+1));
                if((i&1)==0)source.AddTag(r.GetTagAt(i*16+2));
            }
            var actual=new Set(original);var reference=new Set(original);
            bool actualThrew=false,referenceThrew=false;
            Set.FailGrowth=fail;
            try{actual.AppendTags(source);}catch(OutOfMemoryException){actualThrew=true;}
            Set.FailGrowth=fail;
            try{foreach(var tag in source)reference.AddTag(tag);}catch(OutOfMemoryException){referenceThrew=true;}
            Set.FailGrowth=-1;
            bool valid=true; string invariantError=null;
            try{actual.AssertInvariants();}catch(Exception error){valid=false;invariantError=error.Message;}
            reference.AssertInvariants();
            var a=new List<int>();foreach(var tag in actual)a.Add(tag.RuntimeIndex);
            var b=new List<int>();foreach(var tag in reference)b.Add(tag.RuntimeIndex);
            bool same=actualThrew==referenceThrew && a.SequenceEqual(b) && actual.Count==reference.Count && actual.BufferBytes==reference.BufferBytes;
            if(!valid || !same)observations.Add(new{size,capacity,fail,valid,same,invariantError,actualThrew,referenceThrew,actualCount=actual.Count,referenceCount=reference.Count});
            // Empty destination goes through unchanged CopyCore in every variant.
            // Older stream does not promise valid partial Count after a failed growth.
            if(changed && size>0){changedCases++;if(!valid || !same)changedFailures++;}
            cases++;if(actualThrew)failures++;
        }
        int maximumRecords=0;
        var merge=typeof(Set).GetMethod("MergeOwnedRuns",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        if(merge!=null)
        {
            var records=new uint[65536];
            for(int i=0;i<32768;i++){records[i]=((uint)(2*i)<<16)|1;records[i+32768]=((uint)(2*i+1)<<16)|2;}
            merge.Invoke(null,new object[]{records,32768,65536});
            for(int i=0;i<records.Length;i++)if(records[i]!=((uint)i<<16 | (uint)((i&1)==0?1:2)))throw new Exception("maximum-range merge");
            maximumRecords=records.Length;
        }
        if(changedFailures!=0)throw new Exception("new append failed failure-state contract");
        string text=JsonSerializer.Serialize(new{cases,injectedExceptions=failures,maximumRecords,changedCases,changedFailures,observations,failed=0,timed=false});
        File.WriteAllText(args[1],text);Console.WriteLine(text);return 0;
    }
}
