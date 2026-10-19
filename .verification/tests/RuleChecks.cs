using System;
using System.Collections.Generic;
using System.Linq;
using GameplayTags;

public static class RuleChecks
{
    public static long Assertions;
    public static GameplayTag[] All;
    public static GameplayTag[] Leaves;
    public static void Require(bool ok, string message)
    {
        Assertions++;
        if (!ok) throw new Exception(message);
    }
    public static void Initialize()
    {
        GameplayTagManager.Initialize(Enumerable.Range(0,90)
            .Select(i=>new GameplayTagRegistration("Group"+(i/9).ToString("D2")+".Tag"+(i%9).ToString("D2"))).ToArray());
        All=GameplayTagManager.GetAllTags().ToArray();
        Leaves=Enumerable.Range(0,90).Select(i=>GameplayTagManager.RequestTag("Group"+(i/9).ToString("D2")+".Tag"+(i%9).ToString("D2"))).ToArray();
        Require(All.Length==100,"100 registered tags including ancestors");
    }
    public static GameplayTagContainer Set(GameplayTag[] tags)
    {
        var c=new GameplayTagContainer();foreach(var t in tags)c.AddTag(t);return c;
    }
    public static GameplayTagRequirements Build(GameplayTag[] forbidden,GameplayTag[] required)
    {
#if FIXED_RULES
        return new GameplayTagRequirements(forbidden,required);
#else
        return new GameplayTagRequirements(forbidden==null?null:Set(forbidden),required==null?null:Set(required));
#endif
    }
    static bool Has(GameplayTag[] members,GameplayTag query)
    {
        foreach(var t in members)if(t.Name==query.Name || t.Name.StartsWith(query.Name+".",StringComparison.Ordinal))return true;
        return false;
    }
    public static bool Reference(GameplayTag[] forbidden,GameplayTag[] required,GameplayTag[] a,GameplayTag[] b)
    {
        if(forbidden!=null)foreach(var t in forbidden)if(Has(a,t)||Has(b,t))return false;
        if(required!=null)foreach(var t in required)if(!Has(a,t)&&!Has(b,t))return false;
        return true;
    }
    public static long Run()
    {
        var none=Array.Empty<GameplayTag>();
        var empty=Set(none);var leaf=Leaves[0];var sibling=Leaves[1];var parent=GameplayTagManager.RequestTag("Group00");
        var holder=Set(new[]{leaf});
        Require(default(GameplayTagRequirements).Matches(empty),"default empty rule");
        Require(Build(null,null).IsEmpty,"null inputs mean no conditions");
        Require(Build(none,new[]{parent}).Matches(holder),"child satisfies required ancestor");
        Require(!Build(new[]{parent},none).Matches(holder),"child triggers forbidden ancestor");
        Require(!Build(none,new[]{sibling}).Matches(holder),"shared ancestor is not sibling presence");
        Require(!Build(none,new[]{leaf}).Matches(Set(new[]{parent})),"ancestor does not satisfy child");
        Require(!Build(new[]{leaf},new[]{leaf}).Matches(holder),"conflicting rule is false");
        Require(Build(new[]{sibling},none).Matches(empty),"forbidden-only matches empty");
        Require(!Build(none,new[]{leaf}).Matches(empty),"required-only rejects empty");
        var split=Build(none,new[]{Leaves[0],Leaves[10]});
        Require(split.Matches(Set(new[]{Leaves[0]}),Set(new[]{Leaves[10]})),"requirements split between holders");
        Require(!Build(new[]{parent},none).Matches(empty,holder),"forbidden in second holder");
        Require(!Build(new[]{parent},none).Matches(holder,empty),"forbidden in first holder");
        var live=Build(none,new[]{leaf});
        Require(live.Matches(holder),"live state initial");holder.RemoveTag(leaf);
        Require(!live.Matches(holder),"not a cached boolean");holder.AddTag(leaf);
        Require(live.Matches(holder),"live state restored");

        // Cross inline/overflow boundaries, parent/leaf conditions, duplicates,
        // empty/missing/hit paths and both one-holder and split-holder matching.
        foreach(int length in new[]{0,1,2,3,4,5,8,16,64,128})
        foreach(int seed in Enumerable.Range(0,120)){
            var random=new Random(seed+1000*length);
            var a=All.OrderBy(_=>random.Next()).Take(random.Next(31)).ToArray();
            var b=All.OrderBy(_=>random.Next()).Take(random.Next(31)).ToArray();
            var f=Enumerable.Range(0,seed%(length+1)).Select(_=>All[random.Next(All.Length)]).ToArray();
            var r=Enumerable.Range(0,length-f.Length).Select(_=>All[random.Next(All.Length)]).ToArray();
            CheckVariants(f,r,a,b);
            // Produce successful cases as well as failures, rather than mostly
            // short-circuiting on random missing or forbidden tags.
            CheckVariants(none,a.Take(Math.Min(length,a.Length)).ToArray(),a,b);
            CheckVariants(f,none,none,none);
        }

#if FIXED_RULES
        foreach(int count in new[]{0,1,4,5,16,100}){
            var input=Leaves.Take(count).ToArray();
            var original=(GameplayTag[])input.Clone();
            var rule=new GameplayTagRequirements(none,input);var copy=rule;
            for(int i=0;i<input.Length;i++)input[i]=Leaves[89];
            Require(rule.RequiredTagCount==original.Length && rule.ForbiddenTagCount==0,"condition counts");
            for(int i=0;i<original.Length;i++)Require(rule.GetRequiredTag(i)==original[i],"input mutation cannot change snapshot");
            Require(rule.Matches(Set(original)) && copy.Matches(Set(original)),"immutable copy");
        }
        var three=GameplayTagRequirements.Create(Leaves[30],Leaves[0],Leaves[10]);
        Require(three.Matches(Set(new[]{Leaves[0],Leaves[10]})),"three-tag factory");
        Require(!three.Matches(Set(new[]{Leaves[0],Leaves[10],Leaves[30]})),"three-tag factory forbidden");
        Require(three.GetForbiddenTag(0)==Leaves[30] && three.GetRequiredTag(0)==Leaves[0] && three.GetRequiredTag(1)==Leaves[10],"factory preserves order");
        var fromSpans=GameplayTagRequirements.Create(new[]{Leaves[30]}.AsSpan(),new[]{Leaves[0],Leaves[10]}.AsSpan());
        Require(fromSpans.Matches(Set(new[]{Leaves[0],Leaves[10]})),"span factory");
#endif
        return Assertions;
    }
    static void CheckVariants(GameplayTag[] f,GameplayTag[] r,GameplayTag[] a,GameplayTag[] b)
    {
        var rule=Build(f,r);var left=Set(a);var right=Set(b);
        bool single=Reference(f,r,a,Array.Empty<GameplayTag>()),dual=Reference(f,r,a,b);
        Require(rule.Matches(left)==single,"single holder vs name reference");
        Require(rule.Matches(left,right)==dual,"split holder vs name reference");
        Require(rule.Matches(right,left)==dual,"split holder symmetry");
        Require(rule.Matches(left,left)==single,"same holder twice");
        var counts=new GameplayTagCountContainer();foreach(var t in a)counts.AddTag(t,3);
        Require(rule.Matches(counts)==single,"counted holder");
        Require(rule.Matches(counts,right)==dual,"mixed holder types");
        IGameplayTagContainer abstraction=left;
        Require(rule.Matches(abstraction)==single,"interface holder");
    }
}
