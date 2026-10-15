using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using GameplayTags;
internal static class Benchmark
{
    static long sink;
    static GameplayTagContainer Build(GameplayTag[] tags) { var c = new GameplayTagContainer(); foreach(var t in tags)c.AddTag(t); return c; }
    static void Main(string[] args)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        string op=args[0];int n=int.Parse(args[1]), calls=int.Parse(args[2]);
        var registrations=Enumerable.Range(0,4096).Select(i=>new GameplayTagRegistration($"P.G{i/32:D3}.T{i:D5}")).ToArray();
        GameplayTagManager.Initialize(registrations);
        var all=registrations.Select(r=>GameplayTagManager.RequestTag(r.Name)).ToArray();
        var left=all.Take(n).ToArray();var right=all.Skip(n/2).Take(n).ToArray();
        var random=(GameplayTag[])left.Clone();var rng=new Random(941+n);
        for(int i=random.Length-1;i>0;i--){int j=rng.Next(i+1);var t=random[i];random[i]=random[j];random[j]=t;}
        var a=Build(left);var b=Build(right);var d=Build(left);var counted=new GameplayTagCountContainer();
        GameplayTagContainer last=null;int cursor=0;var hit=left[n/2];var miss=all[4095];
        Action run;
        switch(op)
        {
            case "hit":run=()=>sink+=a.HasTagExact(hit)?1:0;break;
            case "miss":run=()=>sink+=a.HasTagExact(miss)?1:0;break;
            case "hierarchy":var parent=GameplayTagManager.RequestTag("P");run=()=>sink+=a.HasTag(parent)?1:0;break;
            case "delete-add":run=()=>{var t=random[(cursor++ & int.MaxValue)%n];a.RemoveTag(t);a.AddTag(t);sink+=a.ExplicitTagCount;};break;
            case "enumerate":run=()=>{long sum=0;foreach(var t in a.GetExplicitTags())sum+=t.Name.Length;sink+=sum;};break;
            case "new-shuffled":run=()=>{last=Build(random);sink+=last.ExplicitTagCount;};break;
            case "new-sorted":run=()=>{last=Build(left);sink+=last.ExplicitTagCount;};break;
            case "copy":run=()=>{GameplayTagContainer.Copy(d,a);sink+=d.ExplicitTagCount;};break;
            case "union-new":run=()=>{last=GameplayTagContainer.Union(a,b);sink+=last.ExplicitTagCount;};break;
            case "intersection-new":run=()=>{last=GameplayTagContainer.Intersection(a,b);sink+=last.ExplicitTagCount;};break;
            case "intersection-reuse":run=()=>{GameplayTagContainer.Intersection(d,a,b);sink+=d.ExplicitTagCount;};break;
            case "clear-refill":run=()=>{d.Clear();foreach(var t in random)d.AddTag(t);sink+=d.ExplicitTagCount;};break;
            case "copy-clear":run=()=>{GameplayTagContainer.Copy(d,a);d.Clear();sink+=d.ExplicitTagCount;};break;
            case "count-cycle":run=()=>{counted.AddTag(hit);counted.RemoveTag(hit);sink+=counted.ExplicitTagCount;};break;
            default:throw new ArgumentException(op);
        }
        for(int i=0;i<128;i++)run();
        long allocated=GC.GetAllocatedBytesForCurrentThread();int g0=GC.CollectionCount(0),g1=GC.CollectionCount(1),g2=GC.CollectionCount(2);
        long start=Stopwatch.GetTimestamp();for(int i=0;i<calls;i++)run();long ticks=Stopwatch.GetTimestamp()-start;
        long bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
        g0=GC.CollectionCount(0)-g0;g1=GC.CollectionCount(1)-g1;g2=GC.CollectionCount(2)-g2;
        if(a.ExplicitTagCount!=n || b.ExplicitTagCount!=n)throw new Exception("input mutated");
        foreach(var t in left)if(!a.HasTagExact(t))throw new Exception("input identity");
        var expected=op=="union-new"?left.Concat(right).Select(t=>t.Name).Distinct().OrderBy(x=>x,StringComparer.Ordinal).ToArray():
            op.StartsWith("intersection")?left.Select(t=>t.Name).Intersect(right.Select(t=>t.Name)).OrderBy(x=>x,StringComparer.Ordinal).ToArray():
            op=="copy-clear"?Array.Empty<string>():left.Select(t=>t.Name).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        var result=last ?? d;
        if(op.Contains("new")||op=="copy"||op=="copy-clear"||op=="clear-refill"||op=="intersection-reuse")
        {
            var actual=result.GetExplicitTags().Select(t=>t.Name).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            if(!actual.SequenceEqual(expected))throw new Exception("output content");
        }
        if(counted.TagCount!=0)throw new Exception("count cycle");
        double ns=ticks*1e9/Stopwatch.Frequency/calls,ms=ticks*1000.0/Stopwatch.Frequency;
        Console.WriteLine($"{op},{n},{calls},{ns:F6},{(double)bytes/calls:F6},{ms:F3},{g0},{g1},{g2},PASS,{sink}");
    }
}
