// Additional mask and selection-boundary checks, independent of timed fixtures.
using System;
using System.Collections.Generic;
using System.Linq;
using GameplayTags;
using GameplayTags.Experiments;
internal static class MicroStress
{
    private static long assertions;
    private static void Assert(bool value) { assertions++; if(!value) throw new Exception("Micro mask or selection invariant"); }
    private static int Main()
    {
        var settings=UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        settings.ReplaceAll(Enumerable.Range(0,259).Select(i=>new GameplayTagDefinition("T"+i.ToString("D4"),"","Default",false,true)).ToList(),
            new List<GameplayTagRedirect>(),new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});
        var r=TagRegistry.Create(settings);var tags=new RuntimeTag[16];for(int i=0;i<16;i++)tags[i]=r.GetTagAt(224+i);
        foreach(var layout in new[]{BitmapLayout.Micro,BitmapLayout.Dense})
        {
            var s=new MicroBitmapSet(r,16,layout);var c=new MicroBitmapSet(r,16,BitmapLayout.Micro);
            for(int mask=0;mask<65536;mask++)
            {
                s.Clear();c.Clear();int count=0;
                for(int bit=0;bit<16;bit++) { if((mask&(1<<bit))!=0){s.AddTag(tags[bit]);count++;}else c.AddTag(tags[bit]); }
                Assert(s.Count==count);s.AssertInvariants();
                var u=MicroBitmapSet.Union(s,c,layout);Assert(u.Count==16);u.RemoveTags(c);Assert(u.Count==count);
                for(int bit=0;bit<16;bit++)Assert(u.HasTagExact(tags[bit])==((mask&(1<<bit))!=0));
                u.AssertInvariants();
            }
        }
#if MICRO_WIDE
        const int size=8;
#else
        const int size=4;
#endif
        int threshold=(r.WordCount*8+size-1)/size;
        foreach(int reserve in new[]{threshold-1,threshold,threshold+1})
        {
            var s=new MicroBitmapSet(r,reserve);var initial=s.Layout;
            Assert(initial==(reserve>=threshold?BitmapLayout.Dense:BitmapLayout.Micro));
            s.EnsureCapacity(r.Count);
            for(int cycle=0;cycle<8;cycle++)
            {
                s.Clear();for(int i=0;i<r.Count;i++)s.AddTag(r.GetTagAt((i*17)%r.Count));
                Assert(s.Count==r.Count);Assert(s.Layout==initial);s.AssertInvariants();
                for(int i=0;i<r.Count;i++)Assert(s.RemoveTag(r.GetTagAt((i*17)%r.Count)));
                Assert(s.Count==0);Assert(s.Layout==initial);s.AssertInvariants();
            }
        }
        Console.WriteLine("MICRO_STRESS assertions="+assertions+" failures=0");return 0;
    }
}
