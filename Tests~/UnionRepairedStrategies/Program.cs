using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using GameplayTags;

internal static class Program
{
    // Domain is an explicit caller guarantee, not a runtime test for healthy data.
    // Unsupported input kinds NEVER go through a chain based on guessed health.
    enum Domain { HealthyIndependent, HealthyPlainAlias, FactoryFallback }
    static object Sink;
    static long Checksum;
    static int Assertions;
    static string[] Deep;
    static readonly Process Process = Process.GetCurrentProcess();
    static readonly Func<long> Allocated = Counter();
    static readonly FieldInfo BucketField = typeof(TagStorage).GetField("m_Buckets", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo MaskField = typeof(TagStorage).GetField("m_BucketMask", BindingFlags.Instance | BindingFlags.NonPublic);
    static Dictionary<string,int> Fixed;
    static Func<long> Counter()
    {
        var method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
        if (method == null) throw new NotSupportedException("A real per-thread allocation counter is required.");
        return (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
    }
    static string Strategy
    {
        get
        {
#if REUSABLE_UNION
            return "reusable";
#elif CHAIN_LEFT
            return "literal-left";
#elif CHAIN_RIGHT
            return "literal-right";
#elif CHAIN_SEED
            return "seed-alias-aware";
#elif CLEAR_ADD
            return "clear-add-both";
#else
            return "factory-copy";
#endif
        }
    }
    static string Route(Domain domain)
    {
#if REUSABLE_UNION
        return "reusable-output-API";
#elif CHAIN_SEED
        return domain == Domain.FactoryFallback ? "factory-copy-fallback" : domain == Domain.HealthyPlainAlias ? "in-place-add-plus-cache-clear" : "seed-selected-Copy-plus-AddTags";
#elif CHAIN_LEFT
        return domain == Domain.HealthyIndependent ? "literal-Copy-left-plus-AddTags-right" : "factory-copy-fallback";
#elif CHAIN_RIGHT
        return domain == Domain.HealthyIndependent ? "literal-Copy-right-plus-AddTags-left" : "factory-copy-fallback";
#elif CLEAR_ADD
        return domain == Domain.HealthyIndependent ? "Clear-plus-AddTags-left-plus-AddTags-right" : "factory-copy-fallback";
#else
        return "factory-copy";
#endif
    }
    delegate void ReplaceOperation(GameplayTagContainer output, IGameplayTagContainer left, IGameplayTagContainer right);
    // Caller-certified domain dispatch occurs ONCE before measurement. Every
    // measured route uses the same delegate signature and invocation shape.
    static ReplaceOperation Select(Domain domain)
    {
#if REUSABLE_UNION
        return Reusable;
#elif CHAIN_SEED
        if (domain == Domain.HealthyIndependent) return Independent;
        if (domain == Domain.HealthyPlainAlias) return AliasChain;
        return Factory;
#elif CHAIN_LEFT || CHAIN_RIGHT || CLEAR_ADD
        return domain == Domain.HealthyIndependent ? (ReplaceOperation)Independent : Factory;
#else
        return Factory;
#endif
    }
    // Only untimed correctness helpers use the convenience selector.
    static void Apply(GameplayTagContainer output, IGameplayTagContainer left, IGameplayTagContainer right, Domain domain)
    { Select(domain)(output, left, right); }
#if REUSABLE_UNION
    static void Reusable(GameplayTagContainer output, IGameplayTagContainer left, IGameplayTagContainer right)
    { GameplayTagContainer.Union(output, left, right); }
#endif
    static void Independent(GameplayTagContainer output, IGameplayTagContainer left, IGameplayTagContainer right)
    {
#if CHAIN_LEFT
        GameplayTagContainer.Copy(output, left); output.AddTags(right);
#elif CHAIN_RIGHT
        GameplayTagContainer.Copy(output, right); output.AddTags(left);
#elif CLEAR_ADD
        output.Clear(); output.AddTags(left); output.AddTags(right);
#elif CHAIN_SEED
        bool leftSet = left is GameplayTagContainer, rightSet = right is GameplayTagContainer;
        // This is real heuristic work, so it remains inside the measured route.
        if ((!leftSet && rightSet) || (leftSet == rightSet && left.TagCount < right.TagCount))
        { GameplayTagContainer.Copy(output, right); output.AddTags(left); }
        else { GameplayTagContainer.Copy(output, left); output.AddTags(right); }
#else
        Factory(output, left, right);
#endif
    }
    static void AliasChain(GameplayTagContainer output, IGameplayTagContainer left, IGameplayTagContainer right)
    {
        // Exact ordinary aliases only; retain the real identity checks and cache
        // maintenance required by the public-API composition inside the timer.
        if (ReferenceEquals(output, left)) { output.AddTags(right); output.m_SerializedExplicitTags?.Clear(); return; }
        if (ReferenceEquals(output, right)) { output.AddTags(left); output.m_SerializedExplicitTags?.Clear(); return; }
        throw new Exception("Invalid caller-certified alias domain");
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Factory(GameplayTagContainer output, IGameplayTagContainer left, IGameplayTagContainer right)
    { GameplayTagContainer.Copy(output, GameplayTagContainer.Union(left, right)); }
    static void Check(bool value, string context) { Assertions++; if (!value) throw new Exception(context); }
    static GameplayTag T(string name) => GameplayTagManager.RequestTag(name);
    static string Leaf(int i) => "Flat.G" + i % 32 + ".L" + i;
    static GameplayTagContainer Set(IEnumerable<string> names)
    { var result = new GameplayTagContainer(); foreach (var name in names) result.AddTag(T(name)); return result; }
    static GameplayTagCountContainer Counted(IEnumerable<string> names, int amount)
    { var result = new GameplayTagCountContainer(); foreach (var name in names) result.AddTag(T(name), amount); return result; }
    static void Register()
    {
        Deep = new string[3000]; Deep[0] = "Deep"; for (int i=1;i<Deep.Length;i++) Deep[i]=Deep[i-1]+".N";
        var names=Enumerable.Range(0,2048).Select(Leaf).Concat(new[]{Deep[2999],"Seed"+string.Concat(Enumerable.Repeat(".N",3000)),"Old","A.Child","B.C"});
        GameplayTagManager.Initialize(names.Select(n=>new GameplayTagRegistration(n)).ToArray());
    }
    static HashSet<string> Names(IGameplayTagContainer c, bool exact = true)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var e = exact ? c.GetExplicitTags() : c.GetTags(); int visited=0;
        while(e.MoveNext()) { Check(visited++ < 20000,"bounded enumeration"); Check(names.Add(e.Current.Name),"unique enumeration"); }
        return names;
    }
    static HashSet<string> Closure(IEnumerable<string> names)
    {
        var all=new HashSet<string>(StringComparer.Ordinal);
        foreach(var name in names.OrderByDescending(n=>n.Length))
        { if(!all.Add(name))continue; for(int i=0;i<name.Length;i++) if(name[i]=='.')all.Add(name.Substring(0,i)); }
        return all;
    }
    static void AssertHealthy(GameplayTagContainer c, HashSet<string> expected, string id)
    {
        var closure=Closure(expected);
        Check(c.ExplicitTagCount==expected.Count && c.TagCount==closure.Count,id+" cardinalities");
        Check(Names(c).SetEquals(expected),id+" explicit names"); Check(Names(c,false).SetEquals(closure),id+" closure");
        foreach(var name in expected) Check(c.HasTagExact(T(name)) && c.HasTag(T(name)),id+" membership");
        Check(c.m_SerializedExplicitTags==null || c.m_SerializedExplicitTags.Count==0,id+" serialized cache cleared");
    }
    static void Drain(GameplayTagContainer c, HashSet<string> expected, string id)
    {
        foreach(var name in expected.OrderBy(n=>n,StringComparer.Ordinal)) { c.AddTag(T(name)); c.RemoveTag(T(name)); }
        Check(c.IsEmpty && c.TagCount==0,id+" normalized full drain");
    }
    sealed class Spec
    {
        internal string Id, Kind="normal", History="dirty", Alias="none";
        internal string[] Left,Right;
        internal Domain Domain=Domain.HealthyIndependent;
        internal Spec(string id,string[] l,string[] r){Id=id;Left=l;Right=r;}
    }
    sealed class Fixture
    {
        internal Spec Spec;
        internal IGameplayTagContainer Left,Right;
        internal GameplayTagContainer Output,Restore;
        internal ReplaceOperation Operation;
        internal HashSet<string> Expected;
        internal string LeftBefore,RightBefore;
        internal void Reset()
        {
            if(Restore!=null) GameplayTagContainer.Copy(Output,Restore);
        }
        internal void Act()
        { Reset(); Operation(Output,Left,Right); Observe(Output); }
    }
    static List<Spec> Specs()
    {
        var list=new List<Spec>();
        foreach(int n in new[]{4,128,1024}) foreach(int overlap in new[]{0,50,100})
        {
            var left=Enumerable.Range(0,n).Select(i=>Leaf(i*719%n)).ToArray();
            var right=Enumerable.Range(0,n).Select(i=>i<n*overlap/100?left[i]:Leaf(1024+i)).Reverse().ToArray();
            list.Add(new Spec("flat/n"+n+"/overlap"+overlap,left,right));
        }
        var small=Enumerable.Range(0,4).Select(Leaf).ToArray(); var large=Enumerable.Range(64,128).Select(Leaf).Reverse().ToArray();
        list.Add(new Spec("unbalanced/small-left",small,large)); list.Add(new Spec("unbalanced/large-left",large,small));
        var l=Enumerable.Range(0,128).Select(Leaf).ToArray();var r=Enumerable.Range(64,128).Select(Leaf).Reverse().ToArray();
        foreach(var history in new[]{"fresh","empty-reserved8192","grown1024-drained","old-deep"}) list.Add(new Spec("history/"+history,l,r){History=history});
        foreach(var kind in new[]{"counted-left","counted-right","counted-both","stable-proxy-left","stable-proxy-right","stable-proxy-both"})list.Add(new Spec("kind/"+kind,l,r){Kind=kind});
        foreach(var alias in new[]{"left","right","both"})list.Add(new Spec("alias/"+alias+"/restore-pair",l,alias=="both"?l:r){Alias=alias,Domain=Domain.HealthyPlainAlias});
        foreach(int depth in new[]{128,1024,3000})foreach(bool descending in new[]{false,true})
        {
            var deep=Deep.Take(depth).ToArray();if(descending)Array.Reverse(deep);
            var seed=new[]{"Seed"+string.Concat(Enumerable.Repeat(".N",3000))};
            list.Add(new Spec("deep/d"+depth+"/"+(descending?"descending":"ascending")+"/seed-left",seed,deep));
            list.Add(new Spec("deep/d"+depth+"/"+(descending?"descending":"ascending")+"/seed-right",deep,seed));
        }
        list.Add(new Spec("empty/both",Array.Empty<string>(),Array.Empty<string>()));
        list.Add(new Spec("empty/left",Array.Empty<string>(),small));list.Add(new Spec("empty/right",small,Array.Empty<string>()));
        list.Add(new Spec("same-source/independent",l,l){Kind="same-source"});
        list.Add(new Spec("fallback/incoherent-enumerator",l,r){Kind="incoherent",Domain=Domain.FactoryFallback});
        list.Add(new Spec("fallback/stateful-indices",l,r){Kind="stateful",Domain=Domain.FactoryFallback});
        list.Add(new Spec("fallback/proxy-alias-right/restore-pair",l,r){Kind="proxy-alias",Alias="right",Domain=Domain.FactoryFallback});
        return list;
    }
    static Fixture Make(Spec s)
    {
        var f=new Fixture{Spec=s,Operation=Select(s.Domain),Expected=new HashSet<string>(s.Left.Concat(s.Right),StringComparer.Ordinal)};
        f.Left=s.Kind.Contains("counted-left")||s.Kind=="counted-both"?(IGameplayTagContainer)Counted(s.Left,3):Set(s.Left);
        f.Right=s.Kind.Contains("counted-right")||s.Kind=="counted-both"?(IGameplayTagContainer)Counted(s.Right,5):Set(s.Right);
        if(s.Kind=="same-source")f.Right=f.Left;
        if(s.Kind=="stable-proxy-left"||s.Kind=="stable-proxy-both")f.Left=new Proxy(f.Left);
        if(s.Kind=="stable-proxy-right"||s.Kind=="stable-proxy-both")f.Right=new Proxy(f.Right);
        if(s.Kind=="incoherent")f.Right=new Proxy(f.Right,Set(new[]{"Old"}));
        if(s.Kind=="stateful")f.Right=new Proxy(f.Right,alternate:Set(new[]{"Old"}));
        f.Output=new GameplayTagContainer();
        if(s.History=="empty-reserved8192")f.Output.EnsureCapacity(8192);
        else if(s.History=="grown1024-drained")
        {foreach(var name in Enumerable.Range(0,1024).Select(Leaf))f.Output.AddTag(T(name));foreach(var name in Enumerable.Range(0,1024).Select(Leaf).Reverse())f.Output.RemoveTag(T(name));}
        else if(s.History=="old-deep")f.Output.AddTag(T(Deep[2999]));
        else if(s.History!="fresh")f.Output.AddTag(T("Old"));
        f.Output.m_SerializedExplicitTags=new List<string>{"stale-cache"};
        if(s.Alias!="none")
        {
            f.Restore=Set(s.Alias=="left"||s.Alias=="both"?s.Left:s.Right);
            GameplayTagContainer.Copy(f.Output,f.Restore);f.Output.m_SerializedExplicitTags.Add("stale-cache");
            if(s.Alias=="left"||s.Alias=="both")f.Left=f.Output;
            if(s.Alias=="right"||s.Alias=="both")f.Right=s.Kind=="proxy-alias"?(IGameplayTagContainer)new Proxy(f.Output):f.Output;
        }
        // Do not inspect arbitrary/stateful source views before the operation.
        if(s.Domain!=Domain.FactoryFallback)
        {f.LeftBefore=Semantic(f.Left);f.RightBefore=Semantic(f.Right);}
        return f;
    }
    static void Observe(GameplayTagContainer c){Checksum=unchecked(Checksum+c.ExplicitTagCount+c.TagCount);Sink=c;}
    static string Semantic(IGameplayTagContainer c)
    {return c.TagCount+"|"+c.ExplicitTagCount+"|"+string.Join(",",Names(c).OrderBy(n=>n,StringComparer.Ordinal))+"|"+string.Join(",",Names(c,false).OrderBy(n=>n,StringComparer.Ordinal));}
    static string HashInts(int[] values)
    {using(var sha=SHA256.Create()){var bytes=new byte[values.Length*4];Buffer.BlockCopy(values,0,bytes,0,bytes.Length);return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}}
    static string Owned(GameplayTagContainer c)
    {
        var s=c.Indices.Storage;var e=new int[s.Entries.Length*4];for(int i=0;i<s.Entries.Length;i++){var x=s.Entries[i];e[4*i]=x.Id;e[4*i+1]=x.Count;e[4*i+2]=x.ExplicitCount;e[4*i+3]=x.ExplicitIndex;}
        var b=(int[])BucketField.GetValue(s);int mask=(int)MaskField.GetValue(s);
        return "{\"Count\":"+s.Count+",\"ExplicitCount\":"+s.ExplicitCount+",\"EntryCapacity\":"+s.Entries.Length+",\"ExplicitCapacity\":"+s.ExplicitIndices.Length+",\"BucketCapacity\":"+b.Length+",\"ActiveBucketMask\":"+mask+
            ",\"ArrayPayloadBytes\":"+(16L*s.Entries.Length+4L*s.ExplicitIndices.Length+4L*b.Length)+",\"AllEntriesSHA256\":"+Q(HashInts(e))+",\"AllExplicitSlotsSHA256\":"+Q(HashInts(s.ExplicitIndices))+",\"AllBucketsSHA256\":"+Q(HashInts(b))+
            ",\"SerializedCacheCount\":"+(c.m_SerializedExplicitTags==null?-1:c.m_SerializedExplicitTags.Count)+",\"SerializedCacheCapacity\":"+(c.m_SerializedExplicitTags==null?-1:c.m_SerializedExplicitTags.Capacity)+"}";
    }
    static void ValidateFixture(Fixture f,bool roundtrip)
    {
        AssertHealthy(f.Output,f.Expected,f.Spec.Id);
        if(f.Spec.Domain!=Domain.FactoryFallback)
        {if(f.Spec.Alias!="left"&&f.Spec.Alias!="both")Check(Semantic(f.Left)==f.LeftBefore,"left source unchanged");if(f.Spec.Alias!="right"&&f.Spec.Alias!="both")Check(Semantic(f.Right)==f.RightBefore,"right source unchanged");}
        if(f.Left is GameplayTagCountContainer lc)foreach(var name in f.Spec.Left)Check(lc.GetExplicitTagCount(T(name))==3,"left multiplicity preserved");
        if(f.Right is GameplayTagCountContainer rc)foreach(var name in f.Spec.Right)Check(rc.GetExplicitTagCount(T(name))==5,"right multiplicity preserved");
        if(f.Spec.Kind=="stateful"||f.Spec.Kind=="incoherent")
        {var p=(Proxy)f.Right;Check(p.IndicesCalls==1,"exactly one right Indices getter");Check(p.EnumerationCalls==0,"factory semantics do not call proxy enumerator");}
        if(roundtrip)
        {
            f.Output.OnBeforeSerialize();Check(new HashSet<string>(f.Output.m_SerializedExplicitTags,StringComparer.Ordinal).SetEquals(f.Expected),"serialized explicit names");
            var copy=new GameplayTagContainer{m_SerializedExplicitTags=new List<string>(f.Output.m_SerializedExplicitTags)};copy.OnAfterDeserialize();copy.m_SerializedExplicitTags.Clear();AssertHealthy(copy,f.Expected,"callback roundtrip");Drain(copy,f.Expected,"callback roundtrip");
            // Real DataContractSerializer verifies the supported standalone boundary.
            var serializer=new DataContractSerializer(typeof(GameplayTagContainer));using(var stream=new MemoryStream())
            {serializer.WriteObject(stream,f.Output);stream.Position=0;var restored=(GameplayTagContainer)serializer.ReadObject(stream);restored.m_SerializedExplicitTags.Clear();AssertHealthy(restored,f.Expected,"DataContract roundtrip");Drain(restored,f.Expected,"DataContract roundtrip");}
            f.Output.m_SerializedExplicitTags.Clear();
        }
        Drain(f.Output,f.Expected,f.Spec.Id);
    }
    static void Tests(List<Spec> specs)
    {
        var owned=new List<string>();
        foreach(var spec in specs)
        {
            var f=Make(spec); Apply(f.Output,f.Left,f.Right,spec.Domain);
            owned.Add("{\"Id\":"+Q(spec.Id)+",\"Route\":"+Q(Route(spec.Domain))+",\"Owned\":"+Owned(f.Output)+"}");
            ValidateFixture(f,spec.Id=="flat/n128/overlap50"||spec.Id.StartsWith("alias/",StringComparison.Ordinal));
        }
        Counterexamples(); LegacyTests();
        Console.WriteLine("{\"Schema\":1,\"Mode\":\"untimed-correctness\",\"Strategy\":"+Q(Strategy)+",\"Runtime\":"+Q(Environment.Version.ToString())+",\"Framework\":"+Q(Framework)+",\"Assertions\":"+Assertions+",\"FixtureCount\":"+specs.Count+",\"OwnedStates\":["+string.Join(",",owned)+"],\"Status\":\"PASS\"}");
    }
    static void Counterexamples()
    {
        var left=Set(new[]{Leaf(0)});var right=Set(new[]{Leaf(1)});
        GameplayTagContainer.Copy(right,left);right.AddTags(right);Check(!right.HasTagExact(T(Leaf(1))),"raw alias-right loses original right");
        var alias=Set(new[]{Leaf(0)});alias.m_SerializedExplicitTags=new List<string>{"stale"};GameplayTagContainer.Copy(alias,alias);alias.AddTags(left);Check(alias.m_SerializedExplicitTags.Count==1,"raw alias-left preserves stale cache");
        var p=new Proxy(Set(new[]{Leaf(1)}),Set(new[]{Leaf(2)}));var expected=new GameplayTagContainer();Factory(expected,left,p);
        p=new Proxy(Set(new[]{Leaf(1)}),Set(new[]{Leaf(2)}));var actual=new GameplayTagContainer();GameplayTagContainer.Copy(actual,left);actual.AddTags(p);Check(!Names(expected).SetEquals(Names(actual)),"incoherent proxy differs");
        // Reversing operands is harmless for healthy sets, not for arbitrary getters.
        var log=new List<string>();var lp=new Proxy(left,onIndices:()=>log.Add("left"));var rp=new Proxy(Set(new[]{Leaf(1)}),onIndices:()=>log.Add("right"));Factory(expected,lp,rp);Check(string.Join(",",log)=="left,right","factory getter order");
        log.Clear();GameplayTagContainer.Copy(actual,rp);actual.AddTags(lp);Check(string.Join(",",log)=="right,left","right-first changes getter order");
        // The right getter can observe output before replacement in factory, but
        // after replacement in the literal left-first public sequence.
        var old=Set(new[]{"Old"});var observed=new Proxy(Set(new[]{Leaf(1)}),onIndices:()=>Check(old.HasTagExact(T("Old")),"factory reads operands before replacing output"));Factory(old,left,observed);
    }
    static GameplayTagCountContainer Overflow()
    {var c=new GameplayTagCountContainer();c.AddTag(T("A.Child"),int.MaxValue);c.AddTag(T("A.Child"),int.MaxValue);c.AddTag(T("A.Child"),2);return c;}
    static void LegacyTests()
    {
        // Public overflow plus a derived normal interface view is deliberately
        // outside chain scope. Check exact factory fallback including all tails.
        foreach(bool countedLeft in new[]{false,true})
        {
            var counted=Overflow();IGameplayTagContainer hidden=new NormalView(counted);var ordinary=Set(Enumerable.Range(0,9).Select(Leaf));
            var a=Set(new[]{"Old"});var b=Set(new[]{"Old"});a.EnsureCapacity(64);b.EnsureCapacity(64);a.m_SerializedExplicitTags=new List<string>{"stale"};b.m_SerializedExplicitTags=new List<string>{"stale"};
            IGameplayTagContainer left=countedLeft?hidden:ordinary,right=countedLeft?ordinary:hidden;
            var errorA=Attempt(()=>Apply(a,left,right,Domain.FactoryFallback));var errorB=Attempt(()=>Factory(b,left,right));
            Check(errorA==errorB,"legacy exception behavior");Check(Owned(a)==Owned(b),"legacy complete owned state");Check(RawPublic(a)==RawPublic(b),"legacy raw public behavior");
        }
        var corrupted=new GameplayTagContainer();IGameplayTagContainer view=new NormalView(Overflow());GameplayTagContainer.Copy(corrupted,view);corrupted.AddTag(T("A.Child"));corrupted.RemoveTag(T("A.Child"));
        Check(corrupted.TagCount==0 && corrupted.ExplicitTagCount==1,"public count-zero explicit state reached");
        var outA=Set(new[]{"Old"});var outB=Set(new[]{"Old"});var healthy=Set(Enumerable.Range(0,9).Select(Leaf));
        var eA=Attempt(()=>Apply(outA,corrupted,healthy,Domain.FactoryFallback));var eB=Attempt(()=>Factory(outB,corrupted,healthy));Check(eA==eB && Owned(outA)==Owned(outB)&&RawPublic(outA)==RawPublic(outB),"anomalous exact-normal fallback");
    }
    static string Attempt(Action action){try{action();return null;}catch(Exception e){return e.GetType().FullName;}}
    static string RawPublic(GameplayTagContainer c)
    {var names=new List<string>();var e=c.GetExplicitTags();int n=0;while(e.MoveNext()&&n++<20000)names.Add(e.Current.Name??"<None>");return c.TagCount+"|"+c.ExplicitTagCount+"|"+string.Join(",",names);}
    sealed class Row
    {
        public string Id {get;set;}
        public string Route {get;set;}
        public double Ns {get;set;}
        public double Bytes {get;set;}
        public int Iterations {get;set;}
        public int WarmupIterations {get;set;}
        public double WallMilliseconds {get;set;}
        public double CpuMilliseconds {get;set;}
        public int Gen0 {get;set;}
        public int Gen1 {get;set;}
        public int Gen2 {get;set;}
        public long RetainedBytes {get;set;}
        public int Cohort {get;set;}
    }
    static Row Measure(string id,string route,Action action,int iterations)
    {
        if(Fixed!=null)iterations=Fixed[id];
        int warmups=id.StartsWith("deep/",StringComparison.Ordinal)?4:64;
        for(int i=0;i<warmups;i++)action();GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        long cpu=Process.TotalProcessorTime.Ticks,bytes=Allocated();int g0=GC.CollectionCount(0),g1=GC.CollectionCount(1),g2=GC.CollectionCount(2);
        long start=Stopwatch.GetTimestamp();for(int i=0;i<iterations;i++)action();long elapsed=Stopwatch.GetTimestamp()-start;
        bytes=Allocated()-bytes;cpu=Process.TotalProcessorTime.Ticks-cpu;
        return new Row{Id=id,Route=route,Ns=elapsed*(1e9/Stopwatch.Frequency)/iterations,Bytes=(double)bytes/iterations,Iterations=iterations,WarmupIterations=warmups,WallMilliseconds=elapsed*1000.0/Stopwatch.Frequency,CpuMilliseconds=cpu/10000.0,Gen0=GC.CollectionCount(0)-g0,Gen1=GC.CollectionCount(1)-g1,Gen2=GC.CollectionCount(2)-g2};
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static Row Retained(Spec s)
    {
        const int n=32;var cohort=new object[n];var shared=Make(s);Sink=null;
        long before=GC.GetTotalMemory(true);
        for(int i=0;i<n;i++){var c=new GameplayTagContainer();shared.Operation(c,shared.Left,shared.Right);cohort[i]=c;}
        long after=GC.GetTotalMemory(true);GC.KeepAlive(cohort);GC.KeepAlive(shared);
        return new Row{Id=s.Id+"/retained-result",Route=Route(s.Domain),RetainedBytes=after-before,Cohort=n};
    }
    static void Bench(List<Spec> specs)
    {
        var rows=new List<Row>();var owned=new List<string>();
        // Arbitrary/stateful getters are correctness-only, not a repeatable warm workload.
        foreach(var s in specs.Where(x=>x.Domain!=Domain.FactoryFallback || x.Kind=="proxy-alias"))
        {
            var f=Make(s);f.Act();AssertHealthy(f.Output,f.Expected,s.Id);
            rows.Add(Measure(s.Id+"/warm",Route(s.Domain),f.Act,s.Id.StartsWith("deep/",StringComparison.Ordinal)?2:128));
            AssertHealthy(f.Output,f.Expected,s.Id);owned.Add("{\"Id\":"+Q(s.Id)+",\"Owned\":"+Owned(f.Output)+"}");
            if(s.Id=="flat/n4/overlap50"||s.Id=="flat/n128/overlap50"||s.Id=="flat/n1024/overlap50"||s.Id=="kind/counted-both")
            {
                var inputs=Make(s);
                rows.Add(Measure(s.Id+"/fresh-output-process-warm",Route(s.Domain),()=>{var c=new GameplayTagContainer();inputs.Operation(c,inputs.Left,inputs.Right);Observe(c);},128));
                rows.Add(Retained(s));
            }
        }
        Console.WriteLine("{\"Schema\":1,\"Strategy\":"+Q(Strategy)+",\"Runtime\":"+Q(Environment.Version.ToString())+",\"Framework\":"+Q(Framework)+",\"RegistryCount\":"+GameplayTagManager.TagCount+",\"Checksum\":"+Checksum+",\"OwnedStates\":["+string.Join(",",owned)+"],\"Rows\":["+string.Join(",",rows.Select(Json))+" ]}");
    }
    static void Cold(Spec s)
    {
        // One selected operation per fresh process. Setup creates the route delegate,
        // constructs/AddTags inputs via AddTag, copies aliases and observes sources.
        // It never invokes the selected operation or any Union before the marker.
        var f=Make(s);Allocated();GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        long bytes=Allocated();f.Operation(f.Output,f.Left,f.Right);bytes=Allocated()-bytes;
        string first=Owned(f.Output);AssertHealthy(f.Output,f.Expected,s.Id);
        // The first successful operation already warms this fixed shape.
        // Restore aliases outside the next allocation marker; do not call Apply again.
        f.Reset();
        long warm=Allocated();f.Operation(f.Output,f.Left,f.Right);warm=Allocated()-warm;
        string warmOwned=Owned(f.Output);AssertHealthy(f.Output,f.Expected,s.Id);
        Console.WriteLine("{\"Schema\":1,\"Strategy\":"+Q(Strategy)+",\"Id\":"+Q(s.Id)+",\"Route\":"+Q(Route(s.Domain))+",\"Runtime\":"+Q(Environment.Version.ToString())+",\"Framework\":"+Q(Framework)+",\"CodePreparation\":\"Route selected and delegate created before marker; first selected operation invocation; no explicit method preparation or operation warmup\",\"FirstCallAllocatedBytes\":"+bytes+",\"WarmOperationOnlyAllocatedBytes\":"+warm+",\"FirstOwned\":"+first+",\"WarmOwned\":"+warmOwned+"}");
    }
    static string Framework=>Type.GetType("Mono.Runtime")!=null?"Standalone Mono":".NET";
    static string Q(string s)=>s==null?"null":"\""+s.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n").Replace("\r","\\r")+"\"";
    static string Json(Row r)=>"{"+string.Join(",",typeof(Row).GetProperties().Select(p=>Q(p.Name)+":"+(p.PropertyType==typeof(string)?Q((string)p.GetValue(r)):Convert.ToString(p.GetValue(r),CultureInfo.InvariantCulture))))+"}";
    static void Main(string[] args)
    {
        Register();var specs=Specs();string mode=args.Length==0?"test":args[0];
        if(mode=="list") {Console.WriteLine("["+string.Join(",",specs.Select(s=>"{\"Id\":"+Q(s.Id)+",\"Domain\":"+Q(s.Domain.ToString())+",\"Route\":"+Q(Route(s.Domain))+",\"ColdEligible\":"+(s.Domain!=Domain.FactoryFallback||s.Kind=="proxy-alias"?"true":"false")+",\"ColdExclusionReason\":"+Q(s.Domain!=Domain.FactoryFallback||s.Kind=="proxy-alias"?null:"Stateful or incoherent proxy: correctness-only, not a repeatable warm fixture")+"}"))+" ]");return;}
        if(mode=="test"){Tests(specs);return;}
        if(mode=="cold"){Cold(specs.Single(s=>s.Id==args[1]));return;}
        if(mode!="bench")throw new ArgumentException("test|list|cold ID|bench [iterations.tsv]");
        if(args.Length>1){Fixed=new Dictionary<string,int>();foreach(var line in File.ReadAllLines(args[1])){var p=line.Split('\t');Fixed.Add(p[0],int.Parse(p[1],CultureInfo.InvariantCulture));}}
        Bench(specs);
    }
    sealed class NormalView:GameplayTagContainer,IGameplayTagContainer
    {readonly IGameplayTagContainer inner;internal NormalView(IGameplayTagContainer value){inner=value;}GameplayTagContainerIndices IGameplayTagContainer.Indices=>inner.Indices;}
    sealed class Proxy:IGameplayTagContainer
    {
        readonly IGameplayTagContainer inner,enumerator,alternate;readonly Action onIndices;internal int IndicesCalls,EnumerationCalls;
        internal Proxy(IGameplayTagContainer i,IGameplayTagContainer enumerator=null,IGameplayTagContainer alternate=null,Action onIndices=null){inner=i;this.enumerator=enumerator??i;this.alternate=alternate;this.onIndices=onIndices;}
        public bool IsEmpty=>inner.IsEmpty;public int ExplicitTagCount=>inner.ExplicitTagCount;public int TagCount=>inner.TagCount;
        public GameplayTagContainerIndices Indices{get{IndicesCalls++;onIndices?.Invoke();return alternate!=null && IndicesCalls%2==0?alternate.Indices:inner.Indices;}}
        public GameplayTagEnumerator GetTags()=>inner.GetTags();public GameplayTagEnumerator GetExplicitTags(){EnumerationCalls++;return enumerator.GetExplicitTags();}
        public void AddTag(GameplayTag t)=>inner.AddTag(t);public void RemoveTag(GameplayTag t)=>inner.RemoveTag(t);public void Clear()=>inner.Clear();
        public void AddTags<TC>(in TC c)where TC:IGameplayTagContainer=>inner.AddTags(c);public void RemoveTags<TC>(in TC c)where TC:IGameplayTagContainer=>inner.RemoveTags(c);
        public void GetParentTags(GameplayTag t,List<GameplayTag>o)=>inner.GetParentTags(t,o);public void GetChildTags(GameplayTag t,List<GameplayTag>o)=>inner.GetChildTags(t,o);
        public void GetExplicitParentTags(GameplayTag t,List<GameplayTag>o)=>inner.GetExplicitParentTags(t,o);public void GetExplicitChildTags(GameplayTag t,List<GameplayTag>o)=>inner.GetExplicitChildTags(t,o);
        public IEnumerator<GameplayTag> GetEnumerator()=>((IEnumerable<GameplayTag>)inner).GetEnumerator();IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }
}
