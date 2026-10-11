using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using GameplayTags;
internal static class Program
{
    private static int Passed;
    [MethodImpl(MethodImplOptions.NoInlining)]private static void Apply(GameplayTagContainer o,IGameplayTagContainer a,IGameplayTagContainer b)
    {
#if REUSABLE_UNION
        GameplayTagContainer.Union(o,a,b);
#else
        GameplayTagContainer.Copy(o,GameplayTagContainer.Union(a,b));
#endif
    }
    static void Verify(string id,string phase,GameplayTagContainer actual,GameplayTagContainer oracle,string ae,string oe)
    {
        var a=new Shared.Snap(actual);var o=new Shared.Snap(oracle);string difference=ae!=oe?"Exception":a.Difference(o);
        if(difference!=null){Console.WriteLine("{\"Counterexample\":true,\"Id\":"+Shared.Q(id)+",\"Phase\":"+Shared.Q(phase)+",\"Difference\":"+Shared.Q(difference)+",\"ActualException\":"+Shared.Q(ae)+",\"OracleException\":"+Shared.Q(oe)+",\"Actual\":"+a.Json(true)+",\"FactoryCopyOracle\":"+o.Json(true)+"}");Console.Out.Flush();Environment.Exit(19);}
        Console.WriteLine("{\"Id\":"+Shared.Q(id)+",\"Phase\":"+Shared.Q(phase)+",\"Exception\":"+Shared.Q(ae)+",\"Snapshot\":"+a.Json()+"}");
    }
    static void Run(string state,int n,string history,string view,bool cache)
    {
        string id=state+"/"+n+"/"+history+"/"+view+"/cache-"+cache;
        var a=state=="flat"?Shared.Normal(n,history):state=="hierarchical"?Shared.Normal(1,history,true):Shared.Legacy(state,history);
        var o=state=="flat"?Shared.Normal(n,history):state=="hierarchical"?Shared.Normal(1,history,true):Shared.Legacy(state,history);
        if(cache){a.OnBeforeSerialize();o.OnBeforeSerialize();}
        var av=Shared.Views(a,view);var ov=Shared.Views(o,view);
        if(!ReferenceEquals(av[0].Indices.Storage,av[1].Indices.Storage)||!ReferenceEquals(av[0].Indices.Storage,a.Indices.Storage))throw new Exception("not self alias");
        Verify(id,"before",a,o,null,null);
        string ae=Shared.Attempt(()=>Apply(a,av[0],av[1]));string oe=Shared.Attempt(()=>GameplayTagContainer.Copy(o,GameplayTagContainer.Union(ov[0],ov[1])));
        Verify(id,"union",a,o,ae,oe);
        ae=Shared.Attempt(()=>a.Clear());oe=Shared.Attempt(()=>o.Clear());Verify(id,"clear",a,o,ae,oe);
        ae=Shared.Attempt(()=>{foreach(var t in Shared.Flat.Take(70))a.AddTag(t);a.AddTag(Shared.Leaf);});oe=Shared.Attempt(()=>{foreach(var t in Shared.Flat.Take(70))o.AddTag(t);o.AddTag(Shared.Leaf);});Verify(id,"regrow",a,o,ae,oe);
        ae=Shared.Attempt(()=>{a.RemoveTag(Shared.Leaf);foreach(var t in Shared.Flat.Take(70).Reverse())a.RemoveTag(t);});oe=Shared.Attempt(()=>{o.RemoveTag(Shared.Leaf);foreach(var t in Shared.Flat.Take(70).Reverse())o.RemoveTag(t);});Verify(id,"drain",a,o,ae,oe);
        Passed++;
    }
    static void Main()
    {
        Shared.Initialize();
        string[] views={"normal-normal","normal-proxy","proxy-normal","proxy-proxy","derived-normal","normal-derived","derived-derived","derived-proxy","proxy-derived"};
        foreach(string h in new[]{"fresh","reserved8192","grown1024-drained"})foreach(string v in views)foreach(bool cache in new[]{false,true})
        {
            Run("public-count-zero-global-explicit-one",0,h,v,cache);
        }
        Console.WriteLine("{\"Complete\":true,\"Cases\":"+Passed+",\"StagesPerCase\":5,\"AllRetainedArrayElementsComparedExactly\":true}");
    }
}
