using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using GameplayTags;

// Small diagnostic only. Public mutation/query API; no runtime source adaptation.
internal static class Program
{
    static readonly int[] Sizes = { 0, 1, 8, 64, 1024 };
    static readonly int[] Iterations = { 1000000, 100000, 20000, 2500, 160 };
    static GameplayTag[] Leaves;
    static GameplayTag[] All;
    static readonly Func<long> Allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static));
    static int Checks;
    static long Sink;
    static ulong Digest = 14695981039346656037UL;
    static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static void Check(bool value, string label) { Checks++; if (!value) throw new Exception(label); }
    static void Mix(string value) { unchecked { foreach (char c in value) { Digest ^= c; Digest *= 1099511628211UL; } } }
    static GameplayTag T(string name) => GameplayTagManager.RequestTag(name);
    static GameplayTagContainer Source(int n)
    {
        var source = new GameplayTagContainer();
        for (int i = 0; i < n; i++) source.AddTag(Leaves[i]);
        return source;
    }
    static GameplayTagHierarchicalContainer Target(int n, int count = 1)
    {
        var target = new GameplayTagHierarchicalContainer();
        if (count != 0) for (int i = 0; i < n; i++) target.AddTag(Leaves[i], count);
        return target;
    }
    static void Expect(IGameplayTagCountContainer value, int n, int count, string label)
    {
        var explicitNames = new HashSet<string>(Leaves.Take(n).Select(t => t.Name), StringComparer.Ordinal);
        var expected = new Dictionary<string, int>(StringComparer.Ordinal);
        if (count != 0)
            foreach (string name in explicitNames)
            {
                string item = name;
                while (true)
                {
                    expected.TryGetValue(item, out int prior); expected[item] = prior + count;
                    int dot = item.LastIndexOf('.'); if (dot < 0) break; item = item.Substring(0, dot);
                }
            }
        Check(value.ExplicitTagCount == (count == 0 ? 0 : n), label + " explicit count");
        Check(value.TagCount == expected.Count, label + " closure count");
        foreach (GameplayTag tag in All)
        {
            expected.TryGetValue(tag.Name, out int total);
            int own = count != 0 && explicitNames.Contains(tag.Name) ? count : 0;
            Check(value.GetTagCount(tag) == total, label + " total " + tag.Name);
            Check(value.GetExplicitTagCount(tag) == own, label + " own " + tag.Name);
        }
        Mix(label + ":" + PublicState(value));
    }
    static string Capture(Func<string> value)
    { try { return value(); } catch (Exception e) { return "THROW:" + e.GetType().Name; } }
    static string Names(IGameplayTagContainer value, bool explicitOnly)
    {
        var values = new List<string>();
        var e = explicitOnly ? value.GetExplicitTags() : value.GetTags();
        while (e.MoveNext()) { if (values.Count > 10000) throw new Exception("enumeration bound"); values.Add(e.Current.Name ?? "<None>"); }
        return string.Join(",", values);
    }
    static string PublicState(IGameplayTagContainer value)
    {
        var parts = new List<string> { value.IsEmpty.ToString(), value.TagCount.ToString(), value.ExplicitTagCount.ToString(), Capture(() => Names(value, false)), Capture(() => Names(value, true)) };
        if (value is IGameplayTagCountContainer counted)
            foreach (GameplayTag tag in All)
            {
                int total = counted.GetTagCount(tag), own = counted.GetExplicitTagCount(tag);
                if (total != 0 || own != 0) parts.Add(tag.Name + ":" + total + ":" + own);
            }
        return string.Join("|", parts);
    }
    static void Basic()
    {
        foreach (int n in Sizes)
        {
            var source = Source(n); string before = PublicState(source);
            var target = new GameplayTagHierarchicalContainer();
            target.AddTags(source); Expect(target,n,1,"distinct-add-"+n);
            target.AddTags(source); Expect(target,n,2,"distinct-add-existing-"+n);
            target.RemoveTags(source); Expect(target,n,1,"distinct-remove-existing-"+n);
            target.RemoveTags(source); Expect(target,n,0,"distinct-remove-last-"+n);
            target.RemoveTags(source); Expect(target,n,0,"distinct-remove-missing-"+n);
            Check(before == PublicState(source),"distinct source unchanged");
            foreach (bool proxy in new[] {false,true})
            {
                target = Target(n);
                IGameplayTagContainer alias = proxy ? (IGameplayTagContainer)new View(target) : target;
                target.AddTags(alias); Expect(target,n,2,"self-add-"+proxy+"-"+n);
                target.RemoveTags(alias); Expect(target,n,1,"self-remove-"+proxy+"-"+n);
                target.RemoveTags(alias); Expect(target,n,0,"self-remove-last-"+proxy+"-"+n);
            }
            foreach (bool customParent in new[] {false,true})
            {
                target = new GameplayTagHierarchicalContainer();
                var parent = new GameplayTagCountContainer();
                target.Parent = customParent ? (IGameplayTagCountContainer)new ParentView(parent) : parent;
                target.AddTags(source); Expect(target,n,1,"parent-add-child-"+customParent+"-"+n); Expect(parent,n,1,"parent-add-parent-"+customParent+"-"+n);
                target.RemoveTags(source); Expect(target,n,0,"parent-remove-child-"+customParent+"-"+n); Expect(parent,n,0,"parent-remove-parent-"+customParent+"-"+n);
            }
            var sharedParent = new GameplayTagCountContainer();for(int i=0;i<n;i++)sharedParent.AddTag(Leaves[i]);
            target=new GameplayTagHierarchicalContainer();target.Parent=sharedParent;
            target.AddTags(sharedParent);Expect(target,n,1,"source-is-parent-add-"+n);Expect(sharedParent,n,2,"source-is-parent-add-total-"+n);
            target.RemoveTags(sharedParent);Expect(target,n,0,"source-is-parent-remove-"+n);Expect(sharedParent,n,1,"source-is-parent-remove-total-"+n);
            var counted = new GameplayTagCountContainer(); for(int i=0;i<n;i++)counted.AddTag(Leaves[i],3);
            target = new GameplayTagHierarchicalContainer(); target.AddTags(counted); Expect(target,n,1,"counted-distinct-"+n);
            target.RemoveTags(counted); Expect(target,n,0,"counted-remove-"+n);
        }
    }
    static void Getters()
    {
        var source = Source(8); var target = new GameplayTagHierarchicalContainer();
        var view = new View(source);
        target.AddTags(view); Check(view.Calls==1,"concrete derived getter exactly once"); Expect(target,8,1,"derived-concrete");
        IGameplayTagContainer boxed = view; target.RemoveTags(boxed); Check(view.Calls==2,"interface getter exactly once"); Expect(target,8,0,"derived-interface");
        view.OnRead = () => target.Parent = new GameplayTagCountContainer();
        target.AddTags(view); Check(view.Calls==3,"stateful getter exactly once"); Expect(target.Parent,8,1,"getter-added-parent");
        view.OnRead = () => throw new InvalidOperationException("getter");
        string prior=PublicState(target); bool threw=false;
        try { target.RemoveTags(view); } catch(InvalidOperationException) { threw=true; }
        Check(threw && prior==PublicState(target),"getter throws before mutation");
        var empty = new EmptyView(); target.AddTags(empty); target.RemoveTags(empty); Check(empty.Calls==2,"default indices getter");
        target.Parent=null; view.OnRead=()=>source.Clear(); target.AddTags(view); Expect(target,8,1,"getter-cleared-source");
        target = Target(8); view = new View(target); var replace=Source(1); view.OnRead=()=>{ target.Clear(); target.AddTags(replace); };
        target.AddTags(view); Expect(target,1,2,"getter-mutated-alias");
    }
    static void Events()
    {
        foreach(int n in new[]{0,1,8,64,1024})
        foreach(bool parented in new[]{false,true})
        {
            var source=Source(n); var target=new GameplayTagHierarchicalContainer(); var parent=new GameplayTagCountContainer();
            if(parented)target.Parent=parent;
            int expectedCount=1, events=0; bool inspected=false;
            Action<string, GameplayTag, int> observe=(kind,tag,count)=>
            {
                events++; Check(target.ExplicitTagCount==(expectedCount==0?0:n),"callback fully committed explicit state");
                if(!inspected){Expect(target,n,expectedCount,"event-commit-"+n+"-"+parented+"-"+expectedCount);if(parented)Expect(parent,n,expectedCount,"parent-event-commit-"+n+"-"+expectedCount);inspected=true;}
                Mix(kind+":"+tag.Name+":"+count);
            };
            target.OnAnyTagCountChange+=(t,c)=>observe("all",t,c);
            target.OnAnyTagNewOrRemove+=(t,c)=>observe("new",t,c);
            target.RegisterTagEventCallback(Leaves[0],GameplayTagEventType.AnyCountChange,(t,c)=>observe("specific",t,c));
            if(parented)parent.OnAnyTagCountChange+=(t,c)=>observe("parent",t,c);
            target.AddTags(source); expectedCount=2; inspected=false; target.AddTags(source);
            expectedCount=1;inspected=false;target.RemoveTags(source);expectedCount=0;inspected=false;target.RemoveTags(source);
            Check(n==0?events==0:events>0,"callback occurrence"); Expect(target,n,0,"events-drained-"+n+"-"+parented);
        }
        foreach(bool remove in new[]{false,true})
        {
            var source=Source(8);var target=Target(8,remove?2:1);bool entered=false;
            target.OnAnyTagCountChange+=(tag,count)=>
            {
                Mix("reentrant:"+tag.Name+":"+count);
                if(entered)return; entered=true; Expect(target,8,remove?1:2,"reentrant-first-committed");
                target.RemoveTags(target); source.Clear();
            };
            if(remove)target.RemoveTags(source);else target.AddTags(source);
            Expect(target,8,remove?0:1,"reentrant-final");Check(entered,"reentrant entered");
        }
        foreach(bool remove in new[]{false,true})
        foreach(bool parented in new[]{false,true})
        {
            var source=Source(8);var target=Target(8,remove?1:0);var parent=new GameplayTagCountContainer();if(parented)target.Parent=parent;
            int callbacks=0;OnTagCountChangedDelegate throwing=(tag,count)=>{callbacks++;throw new InvalidOperationException("listener");};target.OnAnyTagCountChange+=throwing;
            bool threw=false;try {if(remove)target.RemoveTags(source);else target.AddTags(source);}catch(InvalidOperationException){threw=true;}
            Check(threw&&callbacks==1,"callback throw occurrence");Expect(target,8,remove?0:1,"throw-committed-"+parented+"-"+remove);
            if(parented)Expect(parent,8,remove?0:1,"throw-parent-committed-"+remove);
            target.OnAnyTagCountChange-=throwing;int recovered=0;target.OnAnyTagCountChange+=(tag,count)=>{recovered++;Mix("recovered:"+tag.Name+":"+count);};
            if(remove)target.AddTags(source);else target.RemoveTags(source);Check(recovered>0,"callback recovery");
        }
        // A custom parent can mutate the source synchronously. The snapshot fallback must preserve all original IDs.
        var original=Source(8);var child=new GameplayTagHierarchicalContainer();var countedParent=new GameplayTagCountContainer();var custom=new ParentView(countedParent);child.Parent=custom;
        custom.OnMutation=()=>original.Clear(); child.AddTags(original);Expect(child,8,1,"custom-parent-source-mutation");
        original=Source(8);child.RemoveTags(original);Expect(child,8,0,"custom-parent-source-remove-mutation");
        var chainSource=Source(8);var leaf=new GameplayTagHierarchicalContainer();var middle=new GameplayTagHierarchicalContainer();var root=new GameplayTagCountContainer();
        leaf.Parent=middle;middle.Parent=root;int chainCount=1,chainCallbacks=0;
        OnTagCountChangedDelegate chainCallback=(tag,count)=>{chainCallbacks++;Check(leaf.GetTagCount(T("Root"))==8*chainCount,"leaf committed through chain");Check(middle.GetTagCount(T("Root"))==8*chainCount,"middle committed through chain");Check(root.GetTagCount(T("Root"))==8*chainCount,"root committed through chain");Mix("chain:"+tag.Name+":"+count);};
        leaf.OnAnyTagCountChange+=chainCallback;middle.OnAnyTagCountChange+=chainCallback;root.OnAnyTagCountChange+=chainCallback;
        leaf.AddTags(chainSource);chainCount=0;leaf.RemoveTags(chainSource);Check(chainCallbacks>0,"chain callbacks");
    }
    static void Overflow()
    {
        foreach(bool retained in new[]{false,true})
        foreach(bool remove in new[]{false,true})
        foreach(bool listener in new[]{false,true})
        {
            var seed=new GameplayTagCountContainer();seed.AddTag(T("Keep"));for(int i=0;i<7;i++)seed.AddTag(T("Filler"+i));
            var wrapped=T("Wrapped");seed.AddTag(wrapped,int.MaxValue);seed.AddTag(wrapped,int.MaxValue);seed.AddTag(wrapped,2);seed.AddTag(wrapped,1);seed.RemoveTag(wrapped);
            for(int i=6;i>=0;i--)seed.RemoveTag(T("Filler"+i));
            IGameplayTagContainer normalView=new View(seed);var source=new GameplayTagContainer();
            if(retained){for(int i=0;i<16;i++)source.AddTag(T("Filler"+i));source.Clear();}
            GameplayTagContainer.Copy(source,normalView);
            var target=new GameplayTagHierarchicalContainer();target.AddTag(T("Keep"));target.AddTag(T("Wrapped"));
            string prior=PublicState(target);int events=0;if(listener)target.OnAnyTagCountChange+=(tag,count)=>{events++;Mix("overflow-event:"+tag.Name+":"+count);};
            string exception="none";try{if(remove)target.RemoveTags(source);else target.AddTags(source);}catch(Exception e){exception=e.GetType().Name;}
            if(!retained){Check(exception=="IndexOutOfRangeException","copied stale index throws");Check(prior==PublicState(target),"snapshot failure before mutation");Check(events==0,"snapshot failure before callbacks");}
            Mix("overflow:"+retained+":"+remove+":"+listener+":"+exception+":"+PublicState(source)+":"+PublicState(target));
        }
    }
    static void Test()
    {
        Basic();Getters();Events();Overflow();
        Console.WriteLine("{\"Mode\":\"correctness\",\"Status\":\"PASS\",\"Checks\":"+Checks+",\"Digest\":"+Q(Digest.ToString("x16"))+"}");
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static void Manual(GameplayTagHierarchicalContainer target, GameplayTagContainer source)
    { foreach(var tag in source.GetExplicitTags())target.AddTag(tag);foreach(var tag in source.GetExplicitTags())target.RemoveTag(tag); }
    static Action BenchAction(string kind,int n,out Action verify)
    {
        var source=Source(n);var target=Target(n,2);
        verify=()=>{Expect(target,n,2,"timing-postflight-"+kind+"-"+n);Check(source.ExplicitTagCount==n,"timing source unchanged");};
        if(kind=="parent")target.Parent=new GameplayTagCountContainer();
        if(kind=="listener")target.OnAnyTagCountChange+=(tag,count)=>{unchecked{Sink+=count;}};
        IGameplayTagContainer alias=kind=="proxy-alias"?(IGameplayTagContainer)new View(target):target;
        if(kind=="self"||kind=="proxy-alias")return ()=>{target.AddTags(alias);target.RemoveTags(alias);unchecked{Sink+=target.ExplicitTagCount;}};
        if(kind=="manual")return ()=>{Manual(target,source);unchecked{Sink+=target.ExplicitTagCount;}};
        if(kind=="remove-add")return ()=>{target.RemoveTags(source);target.AddTags(source);unchecked{Sink+=target.ExplicitTagCount;}};
        return ()=>{target.AddTags(source);target.RemoveTags(source);unchecked{Sink+=target.ExplicitTagCount;}};
    }
    static void Bench(string iterationsFile)
    {
        Dictionary<string,int> fixedIterations=null;
        if(iterationsFile!=null)
        {
            fixedIterations=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(string line in File.ReadAllLines(iterationsFile))
            {
                string[] pair=line.Split('\t');
                if(pair.Length!=2||!int.TryParse(pair[1],NumberStyles.None,CultureInfo.InvariantCulture,out int count)||count<1)
                    throw new Exception("Invalid fixed iteration row");
                fixedIterations.Add(pair[0],count);
            }
            if(fixedIterations.Count!=35)throw new Exception("Expected all 35 fixed iteration rows");
        }
        string[] kinds={"add-remove","remove-add","listener","self","proxy-alias","parent","manual"};
        foreach(string kind in kinds)
        for(int index=0;index<Sizes.Length;index++)
        {
            int n=Sizes[index],iterations=fixedIterations==null?Iterations[index]:fixedIterations[kind+"/n"+Sizes[index]];
            if(iterations<Iterations[index])throw new Exception("Fixed iteration counts must not decrease");
            Action action=BenchAction(kind,n,out Action verify);verify();
            for(int i=0;i<Math.Min(iterations,1024);i++)action();
            long beforeSink=Sink,allocated=Allocated(),start=Stopwatch.GetTimestamp();
            for(int i=0;i<iterations;i++)action();
            long elapsed=Stopwatch.GetTimestamp()-start,bytes=Allocated()-allocated;
            double ns=(double)elapsed*1e9/Stopwatch.Frequency/iterations;
            long checksum=Sink-beforeSink;verify();
            Console.WriteLine("{\"Mode\":\"timing-screen\",\"Id\":"+Q(kind+"/n"+n)+",\"Iterations\":"+iterations+",\"Ns\":"+ns.ToString("R",CultureInfo.InvariantCulture)+",\"BatchMs\":"+((double)elapsed*1000/Stopwatch.Frequency).ToString("R",CultureInfo.InvariantCulture)+",\"Bytes\":"+((double)bytes/iterations).ToString("R",CultureInfo.InvariantCulture)+",\"Checksum\":"+checksum+"}");
        }
    }
    static void Cold()
    {
        const int cohort=32;
        // Warm code on unrelated objects, keeping every measured snapshot cold.
        var warmSource=Source(8);var warmTarget=Target(8,2);
        warmTarget.AddTags(warmSource);warmTarget.RemoveTags(warmSource);
        warmTarget.OnAnyTagCountChange+=(tag,count)=>{unchecked{Sink+=count;}};
        warmTarget.AddTags(warmSource);warmTarget.RemoveTags(warmSource);
        Allocated();
        foreach(int n in Sizes)
        foreach(bool listener in new[]{false,true})
        foreach(bool remove in new[]{false,true})
        {
            var source=Source(n);var targets=new GameplayTagHierarchicalContainer[cohort];
            for(int i=0;i<cohort;i++)
            {
                var target=Target(n,2);targets[i]=target;
                if(listener){target.OnAnyTagCountChange+=(tag,count)=>{unchecked{Sink+=count;}};if(n>0){target.AddTag(Leaves[0]);target.RemoveTag(Leaves[0]);}}
            }
            long start=Allocated();
            for(int i=0;i<cohort;i++){if(remove)targets[i].RemoveTags(source);else targets[i].AddTags(source);}
            long bytes=Allocated()-start;
            int capacity=((int[])typeof(GameplayTagHierarchicalContainer).GetField("m_Snapshot",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(targets[0])).Length;
            for(int i=0;i<cohort;i++)Expect(targets[i],n,remove?1:3,"cold-state");
            Console.WriteLine("{\"Mode\":\"first-bulk-call-allocation\",\"Id\":"+Q((remove?"remove":"add")+"/listener"+listener+"/n"+n)+",\"Cohort\":"+cohort+",\"Bytes\":"+((double)bytes/cohort).ToString("R",CultureInfo.InvariantCulture)+",\"SnapshotCapacity\":"+capacity+"}");
        }
    }
    static int Main(string[] args)
    {
        var registrations=new List<GameplayTagRegistration>();
        for(int i=0;i<1024;i++)registrations.Add(new GameplayTagRegistration("Root.G"+(i%16)+".L"+i));
        registrations.Add(new GameplayTagRegistration("Keep"));registrations.Add(new GameplayTagRegistration("Wrapped"));
        for(int i=0;i<16;i++)registrations.Add(new GameplayTagRegistration("Filler"+i));
        GameplayTagManager.Initialize(registrations.ToArray());Leaves=Enumerable.Range(0,1024).Select(i=>T("Root.G"+(i%16)+".L"+i)).ToArray();All=GameplayTagManager.GetAllTags().ToArray();
        try {if(args.Length==0||args[0]=="test")Test();else if(args[0]=="bench")Bench(args.Length==3&&args[1]=="--iterations-file"?args[2]:args.Length==1?null:throw new Exception("bench arguments"));else if(args[0]=="cold")Cold();else throw new Exception("mode");return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    private class View : GameplayTagContainer, IGameplayTagContainer
    {
        readonly IGameplayTagContainer Inner;internal Action OnRead;internal int Calls;
        internal View(IGameplayTagContainer inner){Inner=inner;}
        GameplayTagContainerIndices IGameplayTagContainer.Indices {get{Calls++;OnRead?.Invoke();return Inner.Indices;}}
    }
    private sealed class EmptyView : GameplayTagContainer, IGameplayTagContainer
    {internal int Calls;GameplayTagContainerIndices IGameplayTagContainer.Indices {get{Calls++;return default;}}}
    private sealed class ParentView : IGameplayTagCountContainer
    {
        readonly GameplayTagCountContainer Inner;internal Action OnMutation;
        internal ParentView(GameplayTagCountContainer inner){Inner=inner;}
        public event OnTagCountChangedDelegate OnAnyTagCountChange{add=>Inner.OnAnyTagCountChange+=value;remove=>Inner.OnAnyTagCountChange-=value;}
        public event OnTagCountChangedDelegate OnAnyTagNewOrRemove{add=>Inner.OnAnyTagNewOrRemove+=value;remove=>Inner.OnAnyTagNewOrRemove-=value;}
        public bool IsEmpty=>Inner.IsEmpty;public int ExplicitTagCount=>Inner.ExplicitTagCount;public int TagCount=>Inner.TagCount;public GameplayTagContainerIndices Indices=>Inner.Indices;
        public int GetExplicitTagCount(GameplayTag tag)=>Inner.GetExplicitTagCount(tag);public int GetTagCount(GameplayTag tag)=>Inner.GetTagCount(tag);
        public void RegisterTagEventCallback(GameplayTag tag,GameplayTagEventType type,OnTagCountChangedDelegate callback)=>Inner.RegisterTagEventCallback(tag,type,callback);
        public void RemoveTagEventCallback(GameplayTag tag,GameplayTagEventType type,OnTagCountChangedDelegate callback)=>Inner.RemoveTagEventCallback(tag,type,callback);
        public void RemoveAllTagEventCallbacks()=>Inner.RemoveAllTagEventCallbacks();
        public void AddTag(GameplayTag tag){Inner.AddTag(tag);OnMutation?.Invoke();}public void RemoveTag(GameplayTag tag){Inner.RemoveTag(tag);OnMutation?.Invoke();}
        public GameplayTagEnumerator GetTags()=>Inner.GetTags();public GameplayTagEnumerator GetExplicitTags()=>Inner.GetExplicitTags();
        public void AddTags<T>(in T source)where T:IGameplayTagContainer=>Inner.AddTags(source);public void RemoveTags<T>(in T source)where T:IGameplayTagContainer=>Inner.RemoveTags(source);
        public void GetParentTags(GameplayTag tag,List<GameplayTag> output)=>Inner.GetParentTags(tag,output);public void GetChildTags(GameplayTag tag,List<GameplayTag> output)=>Inner.GetChildTags(tag,output);
        public void GetExplicitParentTags(GameplayTag tag,List<GameplayTag> output)=>Inner.GetExplicitParentTags(tag,output);public void GetExplicitChildTags(GameplayTag tag,List<GameplayTag> output)=>Inner.GetExplicitChildTags(tag,output);
        public void Clear()=>Inner.Clear();public IEnumerator<GameplayTag> GetEnumerator()=>Inner.GetEnumerator();IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }
}
