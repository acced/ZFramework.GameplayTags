using System;
using System.Collections.Generic;
using System.Linq;
using GameplayTags;

public static class OptimizationChecks
{
    public static long Assertions;
    static void Check(bool value, string message)
    { Assertions++; if (!value) throw new Exception(message); }
    static bool Has(IEnumerable<GameplayTag> members, GameplayTag tag) =>
        members.Any(t => t.Name == tag.Name || t.Name.StartsWith(tag.Name + ".", StringComparison.Ordinal));
    static GameplayTagContainer Set(IEnumerable<GameplayTag> tags)
    { var result = new GameplayTagContainer(); foreach (var tag in tags) result.AddTag(tag); return result; }
    static void Verify(GameplayTagContainer holder, HashSet<GameplayTag> expected, GameplayTag[] all)
    {
        Check(holder.ExplicitTagCount == expected.Count, "explicit count");
        Check(new HashSet<GameplayTag>(holder.GetExplicitTags().ToArray()).SetEquals(expected), "explicit enumeration");
        foreach (var tag in all)
        {
            Check(holder.HasTagExact(tag) == expected.Contains(tag), "exact membership");
            Check(holder.HasTag(tag) == Has(expected, tag), "hierarchical membership");
        }
    }
    public static void Run()
    {
        var declarations = new List<GameplayTagRegistration>();
        for (int root = 0; root < 10; root++)
            for (int leaf = 0; leaf < 9; leaf++) declarations.Add(new GameplayTagRegistration($"G{root:D2}.T{leaf:D2}"));
        for (int i = 0; i < 80; i++) declarations.Add(new GameplayTagRegistration("Deep." + string.Join(".", Enumerable.Range(0, i + 1).Select(x => "D" + x))));
        declarations.Add(new GameplayTagRegistration("A!x.Child"));
        declarations.Add(new GameplayTagRegistration("A.B.C"));
        GameplayTagManager.Initialize(declarations.ToArray());
        var all = GameplayTagManager.GetAllTags().ToArray();
        var random = new Random(20261019);
        for (int trial = 0; trial < 180; trial++)
        {
            var a = new HashSet<GameplayTag>(all.Where(_ => random.Next(3) == 0));
            var b = new HashSet<GameplayTag>(all.Where(_ => random.Next(3) == 0));
            var left = Set(a); var right = Set(b); var output = new GameplayTagContainer();
            var union = new HashSet<GameplayTag>(a); union.UnionWith(b);
            GameplayTagContainer.Union(output, left, right); Verify(output, union, all);
            GameplayTagContainer.Union(left, left, right); Verify(left, union, all);
            left = Set(a); right = Set(b);
            GameplayTagContainer.Union(right, left, right); Verify(right, union, all);
            var intersection = new HashSet<GameplayTag>(a); intersection.IntersectWith(b);
            left = Set(a); right = Set(b);
            GameplayTagContainer.Intersection(output, left, right); Verify(output, intersection, all);
            GameplayTagContainer.Intersection(left, left, right); Verify(left, intersection, all);
            left = Set(a); GameplayTagContainer.Intersection(right, left, right); Verify(right, intersection, all);
            var difference = new HashSet<GameplayTag>(a); difference.ExceptWith(b);
            left = Set(a); right = Set(b); left.RemoveTags(right); Verify(left, difference, all);
            left = Set(a); left.RemoveTags(left); Verify(left, new HashSet<GameplayTag>(), all);
            var counted = new GameplayTagCountContainer();
            var counts = a.ToDictionary(t => t, t => random.Next(1, 5));
            foreach (var pair in counts) counted.AddTag(pair.Key, pair.Value);
            counted.RemoveTags(Set(b));
            foreach (var tag in b) if (counts.ContainsKey(tag)) counts[tag]--;
            foreach (var tag in all)
            {
                Check(counted.GetExplicitTagCount(tag) == (counts.TryGetValue(tag, out int n) ? n : 0), "batch counted exact");
                int expected = counts.Where(p => Has(new[]{p.Key}, tag)).Sum(p => p.Value);
                Check(counted.GetTagCount(tag) == expected, "batch counted ancestors");
            }
            counted.RemoveTags(counted);
            foreach (var key in counts.Keys.ToArray()) if (counts[key] > 0) counts[key]--;
            foreach (var tag in all)
                Check(counted.GetTagCount(tag) == counts.Where(p => Has(new[]{p.Key}, tag)).Sum(p => p.Value), "self counted remove");
        }
        CheckRules(all, random);
        CheckEvents(all);
        CheckPool();
        // Repeat listener tests with a registry exceeding the direct-index threshold.
        GameplayTagManager.Initialize(Enumerable.Range(0, 700).Select(i => new GameplayTagRegistration("Large.T" + i.ToString("D3"))).ToArray());
        CheckEvents(GameplayTagManager.GetAllTags().ToArray());
    }

    static void CheckRules(GameplayTag[] all, Random random)
    {
        var parent = GameplayTagManager.RequestTag("G00");
        var child = GameplayTagManager.RequestTag("G00.T00");
        var sibling = GameplayTagManager.RequestTag("G00.T01");
        var simplified = new GameplayTagRequirements(Array.Empty<GameplayTag>(), new[]{parent, child, child});
        Check(simplified.RequiredTagCount == 1 && simplified.GetRequiredTag(0) == child, "required canonicalization");
        simplified = new GameplayTagRequirements(new[]{parent, child, child}, Array.Empty<GameplayTag>());
        Check(simplified.ForbiddenTagCount == 1 && simplified.GetForbiddenTag(0) == parent, "forbidden canonicalization");
        var impossible = new GameplayTagRequirements(new[]{parent}, new[]{child});
        Check(impossible.IsImpossible && !impossible.IsEmpty && impossible.RequiredTagCount == 0, "contradiction marker");
        Check(!new GameplayTagRequirements(new[]{child},new[]{parent}).IsImpossible, "asymmetric implication");
        Check(GameplayTagRequirements.Create(child, parent, sibling).Matches(Set(new[]{sibling})), "factory implication");
        for (int run = 0; run < 30; run++)
        {
            var rules = new GameplayTagRequirements[120];
            var forbidden = new GameplayTag[120][]; var required = new GameplayTag[120][];
            for (int i = 0; i < rules.Length; i++)
            {
                forbidden[i] = Enumerable.Range(0, random.Next(4)).Select(_=>all[random.Next(all.Length)]).ToArray();
                required[i] = Enumerable.Range(0, random.Next(7)).Select(_=>all[random.Next(all.Length)]).ToArray();
                rules[i] = new GameplayTagRequirements(forbidden[i], required[i]);
            }
            var group = new GameplayTagRuleSet(rules); var cache = group.CreateCache();
            var a = new GameplayTagCountContainer(); var b = new GameplayTagContainer();
            var states = new HashSet<GameplayTag>();
            var result = new bool[rules.Length];
            for (int step = 0; step < 70; step++)
            {
                var tag = all[random.Next(all.Length)];
                if (states.Remove(tag)) { a.RemoveTag(tag, a.GetExplicitTagCount(tag)); }
                else { states.Add(tag); a.AddTag(tag, 2); }
                group.Evaluate(a, result);
                var cached = cache.Evaluate(a);
                for (int i = 0; i < rules.Length; i++)
                {
                    bool expected = !forbidden[i].Any(t=>Has(states,t)) && required[i].All(t=>Has(states,t));
                    Check(result[i] == expected && cached[i] == expected, "group and cache vs name reference");
                }
                b.Clear(); b.AddTag(all[(step * 3) % all.Length]);
                group.Evaluate(a,b,result); cached=cache.Evaluate(a,b);
                for (int i=0;i<rules.Length;i++) Check(result[i]==rules[i].Matches(a,b) && cached[i]==result[i], "dual cache");
            }
            a.Clear(); cache.Evaluate(a);
            var key = all[10]; a.AddTag(key,2); cache.Evaluate(a); a.RemoveTag(key); cachedCheck();
            void cachedCheck() { var snapshot=cache.Evaluate(a); for(int i=0;i<rules.Length;i++) Check(snapshot[i]==rules[i].Matches(a),"stack cache"); }
            // Callback queries must see committed current state even before other callbacks run.
            bool nested=false;
            a.OnAnyTagNewOrRemove += (tag,count) => { if (!nested) { nested=true; a.AddTag(all[12]); } cachedCheck(); };
            a.AddTag(all[11]); cachedCheck();
            var other=Set(all.Take(20)); var destination=Set(all.Take(5)); cache.Evaluate(destination);
            GameplayTagContainer.Intersection(destination, other, other);
            var values=cache.Evaluate(destination); for(int i=0;i<rules.Length;i++) Check(values[i]==rules[i].Matches(destination),"copy invalidates cache");
        }
    }

    static void CheckEvents(GameplayTag[] all)
    {
        var holder=new GameplayTagCountContainer(); var log=new List<string>();
        OnTagCountChangedDelegate localNew=(t,n)=>log.Add("LN:"+t.Name+":"+n);
        OnTagCountChangedDelegate localAny=(t,n)=>log.Add("LA:"+t.Name+":"+n);
        OnTagCountChangedDelegate globalNew=(t,n)=>log.Add("GN:"+t.Name+":"+n);
        OnTagCountChangedDelegate globalAny=(t,n)=>log.Add("GA:"+t.Name+":"+n);
        holder.OnAnyTagNewOrRemove+=globalNew; holder.OnAnyTagCountChange+=globalAny;
        for(int subscribed=0;subscribed<all.Length;subscribed++)
        {
            holder.RegisterTagEventCallback(all[subscribed],GameplayTagEventType.NewOrRemoved,localNew);
            holder.RegisterTagEventCallback(all[subscribed],GameplayTagEventType.AnyCountChange,localAny);
        }
        var active=new HashSet<GameplayTag>(all);
        for(int remaining=all.Length;remaining>=0;remaining--)
        {
            var key=all[(remaining*17)%all.Length];
            foreach(var change in new[]{1,1,-1,-1})
            {
                int before=holder.GetExplicitTagCount(key), after=before+change;
                var path=all.Where(t=>Has(new[]{key},t)).OrderBy(t=>t.Name.Length).ToArray();
                var wanted=new List<string>();
                foreach(var tag in path)
                {
                    bool transition=before==0||after==0;
                    if(transition){if(active.Contains(tag))wanted.Add("LN:"+tag.Name+":"+after);wanted.Add("GN:"+tag.Name+":"+after);}
                    if(active.Contains(tag))wanted.Add("LA:"+tag.Name+":"+after);
                    wanted.Add("GA:"+tag.Name+":"+after);
                }
                log.Clear();
                if(change>0)holder.AddTag(key);else holder.RemoveTag(key);
                Check(log.SequenceEqual(wanted),"event ordering across listener-index thresholds");
            }
            if(remaining==0)break;
            var removed=all[remaining-1]; active.Remove(removed);
            holder.RemoveTagEventCallback(removed,GameplayTagEventType.AnyCountChange,localAny);
            holder.RemoveTagEventCallback(removed,GameplayTagEventType.NewOrRemoved,localNew);
        }
        holder.RemoveAllTagEventCallbacks();
        bool shouldThrow=true;
        OnTagCountChangedDelegate fail=(t,n)=>{if(shouldThrow)throw new InvalidOperationException("expected");};
        holder.OnAnyTagCountChange+=fail;
        bool threw=false;try{holder.AddTag(all[all.Length-1]);}catch(InvalidOperationException){threw=true;}
        Check(threw && holder.HasTag(all[all.Length-1]),"callback exception keeps committed state");
        shouldThrow=false;holder.RemoveTag(all[all.Length-1]); Check(holder.IsEmpty,"callback recovery");
    }

    static void CheckPool()
    {
        GameplayTagContainerPool.Configure(GameplayTagContainerPool.RetentionPolicy.Unbounded);
        var big = new GameplayTagContainer(10000);
        GameplayTagContainerPool.Release(big);
        Check(ReferenceEquals(big, GameplayTagContainerPool.Get()), "default large objects are reusable");
        var objects = Enumerable.Range(0, 100).Select(_ => new GameplayTagContainer(32)).ToArray();
        foreach (var c in objects) GameplayTagContainerPool.Release(c);
        Check(GameplayTagContainerPool.RetainedCount == 100, "unbounded is not capped at 64");
        long bytes = objects.Sum(c => c.RetainedDataBytes);
        Check(GameplayTagContainerPool.RetainedDataBytes == bytes, "byte accounting");
        foreach (var c in objects) GameplayTagContainerPool.Get();
        Check(GameplayTagContainerPool.RetainedCount == 0 && GameplayTagContainerPool.RetainedDataBytes == 0, "take accounting");
        GameplayTagContainerPool.Configure(new GameplayTagContainerPool.RetentionPolicy(2));
        foreach (var c in objects.Take(3)) GameplayTagContainerPool.Release(c);
        Check(GameplayTagContainerPool.RetainedCount == 2, "caller count policy");
        GameplayTagContainerPool.Configure(new GameplayTagContainerPool.RetentionPolicy(10, bytes / 100 - 1));
        GameplayTagContainerPool.Release(objects[0]);
        Check(GameplayTagContainerPool.RetainedCount == 0, "caller item policy");
        GameplayTagContainerPool.Configure(new GameplayTagContainerPool.RetentionPolicy(10, long.MaxValue, 2 * bytes / 100));
        foreach (var c in objects.Take(3)) GameplayTagContainerPool.Release(c);
        Check(GameplayTagContainerPool.RetainedCount == 2, "caller total policy");
        GameplayTagContainerPool.Configure(GameplayTagContainerPool.RetentionPolicy.Disabled);
        GameplayTagContainerPool.Release(big);
        Check(GameplayTagContainerPool.RetainedCount == 0, "disabled policy");
        GameplayTagContainerPool.Configure(GameplayTagContainerPool.RetentionPolicy.Unbounded);
        bool independent = false;
        var thread = new System.Threading.Thread(() => {
            independent = GameplayTagContainerPool.Policy.MaxCount == int.MaxValue;
            GameplayTagContainerPool.Configure(GameplayTagContainerPool.RetentionPolicy.Disabled);
        });
        thread.Start(); thread.Join();
        Check(independent && GameplayTagContainerPool.Policy.MaxCount == int.MaxValue, "thread-local policy");
        using (GameplayTagContainerPool.Get(out var c)) c.AddTag(GameplayTagManager.GetAllTags()[0]);
        var reused = GameplayTagContainerPool.Get(); Check(reused.IsEmpty, "lease clears members");
        GameplayTagContainerPool.Clear();
    }
}
