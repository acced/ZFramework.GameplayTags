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
using UnityEngine;

// Fixed three-source factory-repair public API timings (200 ms B1-only calibration).
// Reflection is restricted to untimed serialized-state preparation
// and an explicitly incomplete retained-array payload census.
internal static class Benchmark
{
    const int Universe = 4096;
    static long sink;
    static GameplayTag[] tags;
    static GameplayTagSettings settings;
    static readonly FieldInfo namesField = typeof(GameplayTagContainer).GetField("m_GameplayTags", BindingFlags.Instance | BindingFlags.NonPublic);
    sealed class Case {
        public string Key, Adapter="native"; public int N; public bool InputPool;
        public Action<int> Prepare = _ => {}; public Action<int> Run;
        public Func<GameplayTagContainer> Retained; public Action Verify=()=>{};
    }
    static string NameAt(int i) => i==0?"A":i==1?"B":(i<2048?"A.T":"B.T")+i.ToString("D4");
    static void FreshRegistry() { GameplayTagManager.Initialize(settings,true);tags=Enumerable.Range(0,Universe).Select(i=>GameplayTag.Parse(NameAt(i))).ToArray(); }
    static void RegisteredTargetIntersection(ref GameplayTagContainer d,GameplayTagContainer a,GameplayTagContainer b) {
#if PHASE_APIS
        d.SetIntersectionExact(a,b);
#else
        d.CopyFrom(a);
        for(int i=d.Count-1;i>=0;i--)if(!b.HasTagExact(d[i]))d.RemoveTag(d[i]);
#endif
    }
    static GameplayTagContainer Build(GameplayTag[] input) { var c=new GameplayTagContainer(); foreach(var t in input)c.AddTag(t); return c; }
    static long Enumerate(GameplayTagContainer c) { long value=0; foreach(var t in c)value+=t.Name.Length; return value; }
    static GameplayTagContainer Serialized(GameplayTag[] input) { var c=new GameplayTagContainer();namesField.SetValue(c,new List<GameplayTag>(input));((ISerializationCallbackReceiver)c).OnAfterDeserialize();return c; }
    static void TargetIntersection(ref GameplayTagContainer d,GameplayTagContainer a,GameplayTagContainer b) {
#if PHASE_APIS
        d.SetIntersectionExact(a,b);
#else
        // No original public overwrite API preserves unknown serialized matches.
        // Retain the exact result, replacing the reference; allocation is measured.
        d=GameplayTagContainer.IntersectionExact(a,b);
#endif
    }
    static string IntersectionAdapter {
        get {
#if PHASE_APIS
            return "native-target-reuse";
#else
            return "old-substitute-allocating-exact-result";
#endif
        }
    }
    static void AssertNames(GameplayTagContainer c,IEnumerable<GameplayTag> expected,string context) {
        var want=expected.Select(t=>t.Name).Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        var actual=new List<string>();foreach(var t in c)actual.Add(t.Name);var got=actual.ToArray();
        if(c.Count!=want.Length || !got.SequenceEqual(want,StringComparer.Ordinal))throw new InvalidOperationException("BENCHMARK CORRECTNESS FAILURE: "+context);
        for(int i=0;i<got.Length;i++)if(c[i].Name!=want[i])throw new InvalidOperationException("BENCHMARK INDEX FAILURE: "+context);
    }
    static IEnumerable<Case> Cases(int n) {
        var input=tags.Take(n).OrderBy(t=>t.Name,StringComparer.Ordinal).ToArray();
        var random=(GameplayTag[])input.Clone(); var rng=new Random(117+n);
        for(int i=random.Length-1;i>0;i--){int j=rng.Next(i+1);var t=random[i];random[i]=random[j];random[j]=t;}
        var a=Build(input);var b=Build(tags.Skip(n/2).Take(n).ToArray());var d=Build(input);
        var hit=tags[n==0?0:n/2]; var miss=tags[Universe-1];
        var rightInput=tags.Skip(n/2).Take(n).ToArray();
        var intersectInput=input.Where(t=>rightInput.Any(r=>r.Name==t.Name)).ToArray();
        GameplayTagContainer[] pool=null;GameplayTagContainer lastResult=null,lastOther=null,lastLifecycle=null;bool lastQuery=false;
        Case Checked(Case c) {
            c.Verify=()=>{
                AssertNames(a,input,c.Key+" left source");AssertNames(b,rightInput,c.Key+" right source");
                if(c.Key.StartsWith("new-add-"))AssertNames(lastResult,input,c.Key);
                else if(c.Key.StartsWith("clear-reused-add-"))AssertNames(c.Retained(),input,c.Key);
                else if(c.Key=="new-union")AssertNames(lastResult,input.Concat(rightInput),c.Key);
                else if(c.Key=="new-exact-intersection")AssertNames(lastResult,intersectInput,c.Key);
                else if(c.Key.StartsWith("target-intersection"))AssertNames(d,intersectInput,c.Key);
                else if(c.Key=="target-copy-nonempty"||c.Key=="reused-add-existing")AssertNames(d,input,c.Key);
                else if(c.Key=="full-lifecycle"){AssertNames(lastResult,input.Concat(rightInput),c.Key+" union");AssertNames(lastOther,intersectInput,c.Key+" intersection");AssertNames(lastLifecycle,Array.Empty<GameplayTag>(),c.Key+" cleared");}
                if(c.Key.StartsWith("has-exact")||c.Key.StartsWith("hierarchy-")||c.Key=="first-binding-exact"||c.Key=="post-rebuild-first-exact"){bool expected=n>0&&!c.Key.Contains("miss");if(lastQuery!=expected)throw new InvalidOperationException("BENCHMARK QUERY FAILURE: "+c.Key);}
                if(c.InputPool){if(pool==null||pool.Length==0)throw new InvalidOperationException("Missing input pool");foreach(var item in pool)AssertNames(item,c.Key=="clear-nonempty"?Array.Empty<GameplayTag>():input,c.Key+" pool");}
            };return c;
        }
        foreach(var order in new[]{("ascending",input),("descending",input.Reverse().ToArray()),("shuffled",random),("duplicates",random.Concat(random).ToArray())}) {
            var data=order.Item2;var reused=Build(input);
            yield return Checked(new Case{N=n,Key="new-add-"+order.Item1,Run=_=>{var c=Build(data);lastResult=c;sink+=c.Count;},Retained=()=>lastResult});
            yield return Checked(new Case{N=n,Key="clear-reused-add-"+order.Item1,Run=_=>{reused.Clear();foreach(var t in data)reused.AddTag(t);sink+=reused.Count;},Retained=()=>reused});
        }
        yield return Checked(new Case{N=n,Key="reused-add-existing",Run=_=>{foreach(var t in random)d.AddTag(t);sink+=d.Count;},Retained=()=>d});
        yield return Checked(new Case{N=n,Key="new-union",Run=_=>{var c=GameplayTagContainer.Union(a,b);lastResult=c;sink+=c.Count;},Retained=()=>lastResult});
        yield return Checked(new Case{N=n,Key="new-exact-intersection",Run=_=>{var c=GameplayTagContainer.IntersectionExact(a,b);lastResult=c;sink+=c.Count;},Retained=()=>lastResult});
        yield return Checked(new Case{N=n,Key="target-copy-nonempty",Run=_=>{d.CopyFrom(a);sink+=d.Count;},Retained=()=>d});
        yield return Checked(new Case{N=n,Key="target-intersection-nonempty",Adapter=IntersectionAdapter,Run=_=>{TargetIntersection(ref d,a,b);sink+=d.Count;},Retained=()=>d});
        yield return Checked(new Case{N=n,Key=n==0?"has-exact-empty":"has-exact-hit",Run=_=>{lastQuery=a.HasTagExact(hit);sink+=lastQuery?1:0;},Retained=()=>a});
        yield return Checked(new Case{N=n,Key="has-exact-miss",Run=_=>{lastQuery=a.HasTagExact(miss);sink+=lastQuery?1:0;},Retained=()=>a});
        var hierarchy=Build(tags.Skip(2).Take(n).ToArray());var ancestor=tags[0];var unrelated=tags[1];
        yield return Checked(new Case{N=n,Key=n==0?"hierarchy-exact-empty":"hierarchy-exact-at-end",Run=_=>{lastQuery=a.HasTag(n==0?hit:input[n-1]);sink+=lastQuery?1:0;},Retained=()=>a});
        yield return Checked(new Case{N=n,Key="hierarchy-unrelated-parent-miss",Run=_=>{lastQuery=hierarchy.HasTag(unrelated);sink+=lastQuery?1:0;},Retained=()=>hierarchy});
        yield return Checked(new Case{N=n,Key=n==0?"hierarchy-ancestor-empty":"hierarchy-ancestor-hit",Run=_=>{lastQuery=hierarchy.HasTag(ancestor);sink+=lastQuery?1:0;},Retained=()=>hierarchy});
        yield return Checked(new Case{N=n,Key="target-intersection-registered-composition",Adapter=IntersectionAdapter=="native-target-reuse"?"native-target-reuse":"old-substitute-registered-copy-remove",Run=_=>{RegisteredTargetIntersection(ref d,a,b);sink+=d.Count;},Retained=()=>d});
        yield return Checked(new Case{N=n,Key="target-intersection-registered-clear-scan-add",Adapter="public-registered-clear-scan-add",Run=_=>{d.Clear();foreach(var t in a)if(b.HasTagExact(t))d.AddTag(t);sink+=d.Count;},Retained=()=>d});
        int cursor=0;
        yield return Checked(new Case{N=n,Key="delete-add",Run=_=>{if(n>0){var t=input[(cursor++ & int.MaxValue)%n];a.RemoveTag(t);a.AddTag(t);}sink+=a.Count;},Retained=()=>a});
        yield return Checked(new Case{N=n,Key="ordered-enumeration",Run=_=>{sink+=Enumerate(a);},Retained=()=>a});
        yield return Checked(new Case{N=n,Key="delete-add-first-enumeration",Run=_=>{if(n>0){var t=input[(cursor++ & int.MaxValue)%n];a.RemoveTag(t);a.AddTag(t);}sink+=Enumerate(a);},Retained=()=>a});
        yield return Checked(new Case{N=n,Key="index-name-mapping",Run=_=>{for(int i=0;i<a.Count;i++)sink+=a[i].Name.Length;},Retained=()=>a});
        yield return Checked(new Case{N=n,Key="clear-nonempty",InputPool=true,Prepare=count=>{pool=new GameplayTagContainer[count];for(int i=0;i<count;i++)pool[i]=Build(input);},Run=i=>{pool[i].Clear();sink+=pool[i].Count;},Retained=()=>pool[0]});
        yield return Checked(new Case{N=n,Key="first-binding-exact",InputPool=true,Prepare=count=>{pool=new GameplayTagContainer[count];for(int i=0;i<count;i++)pool[i]=Serialized(input);},Run=i=>{lastQuery=pool[i].HasTagExact(hit);sink+=lastQuery?1:0;},Retained=()=>pool[0]});
        yield return Checked(new Case{N=n,Key="post-rebuild-first-exact",InputPool=true,Prepare=count=>{pool=new GameplayTagContainer[count];for(int i=0;i<count;i++)pool[i]=Build(input);GameplayTagManager.Initialize(settings,true);},Run=i=>{lastQuery=pool[i].HasTagExact(hit);sink+=lastQuery?1:0;},Retained=()=>pool[0]});
        yield return Checked(new Case{N=n,Key="dirty-before-serialize",InputPool=true,Prepare=count=>{pool=new GameplayTagContainer[count];for(int i=0;i<count;i++)pool[i]=Build(input);},Run=i=>{((ISerializationCallbackReceiver)pool[i]).OnBeforeSerialize();sink+=pool[i].Count;},Retained=()=>pool[0]});
        yield return Checked(new Case{N=n,Key="after-deserialize-normalize",InputPool=true,Prepare=count=>{pool=new GameplayTagContainer[count];for(int i=0;i<count;i++){pool[i]=new GameplayTagContainer();namesField.SetValue(pool[i],new List<GameplayTag>(random.Concat(random)));}},Run=i=>{((ISerializationCallbackReceiver)pool[i]).OnAfterDeserialize();sink+=pool[i].Count;},Retained=()=>pool[0]});
        yield return Checked(new Case{N=n,Key="full-lifecycle",Run=_=>{var c=Build(random);sink+=c.HasTagExact(hit)?1:0;if(n>0){c.RemoveTag(hit);c.AddTag(hit);}sink+=Enumerate(c);var u=GameplayTagContainer.Union(c,b);var x=GameplayTagContainer.IntersectionExact(c,b);c.CopyFrom(x);c.Clear();lastResult=u;lastOther=x;lastLifecycle=c;sink+=u.Count;},Retained=()=>Build(random)});
    }

    static IEnumerable<Case> FocusCases(int n) {
        var ordered=tags.OrderBy(t=>t.Name,StringComparer.Ordinal).ToArray();
        var left=ordered.Take(n).ToArray();
        var a=Build(left); GameplayTagContainer d=new GameplayTagContainer(n*2+4), last=null;
        Case Row(string key,Action<int> run,GameplayTag[] expected,Func<GameplayTagContainer> retained,Action extra=null) => new Case{N=n,Key=key,Run=run,Retained=retained,Verify=()=>{AssertNames(a,left,key+" source");AssertNames(retained(),expected,key);extra?.Invoke();}};
        yield return Row("focus-new-capacity",_=>{last=new GameplayTagContainer(n);sink+=last.Count;},Array.Empty<GameplayTag>(),()=>last);
        yield return Row("focus-copy",_=>{d.CopyFrom(a);sink+=d.Count;},left,()=>d);
        foreach(string shape in new[]{"contained","after","before","overlap","near-subset"}) {
            var sourceInput=shape=="contained"?left:shape=="after"?ordered.Skip(n).Take(n).ToArray():shape=="before"?ordered.Take(n).ToArray():ordered.Skip(n/2).Take(n).ToArray();
            var seedInput=shape=="before"?ordered.Skip(n).Take(n).ToArray():left;
            if(n>0 && shape=="after" && string.CompareOrdinal(seedInput[n-1].Name,sourceInput[0].Name)>=0)throw new InvalidOperationException("after geometry");
            if(n>0 && shape=="before" && string.CompareOrdinal(sourceInput[n-1].Name,seedInput[0].Name)>=0)throw new InvalidOperationException("before geometry");
            if(shape=="near-subset" && n>0){seedInput=ordered.Where((t,i)=>i%2==0).Take(n).ToArray();sourceInput=(GameplayTag[])seedInput.Clone();if(n>1)sourceInput[n-1]=ordered[n*2-3];}
            var source=Build(sourceInput);var seed=Build(seedInput);var output=new GameplayTagContainer(n*2+4);
            var expected=seedInput.Concat(sourceInput).ToArray();
            yield return Row("focus-append-"+shape,_=>{output.CopyFrom(seed);output.AppendTags(source);sink+=output.Count;},expected,()=>output,()=>{AssertNames(source,sourceInput,"append source");AssertNames(seed,seedInput,"append seed");});
        }
#if PHASE_APIS
        var otherInput=ordered.Skip(n/2).Take(n).ToArray();var other=Build(otherInput);var alias=new GameplayTagContainer(n*2+4);
        yield return Row("focus-union-alias-left",_=>{alias.CopyFrom(a);alias.SetUnion(alias,other);sink+=alias.Count;},left.Concat(otherInput).ToArray(),()=>alias,()=>AssertNames(other,otherInput,"union other"));
        yield return Row("focus-union-alias-right",_=>{alias.CopyFrom(other);alias.SetUnion(a,alias);sink+=alias.Count;},left.Concat(otherInput).ToArray(),()=>alias,()=>AssertNames(other,otherInput,"union other"));
        yield return Row("focus-union-alias-self",_=>{alias.CopyFrom(a);alias.SetUnion(alias,alias);sink+=alias.Count;},left,()=>alias);
        foreach(int hits in new[]{0,1,2}) {
            int common=Math.Min(n,hits);var input=left.Take(common).Concat(ordered.Skip(n+4).Take(n-common)).ToArray();var otherSet=Build(input);var target=new GameplayTagContainer(2);
            yield return Row("focus-intersection-small-result-"+hits,_=>{target.SetIntersectionExact(a,otherSet);sink+=target.Count;},left.Take(common).ToArray(),()=>target,()=>AssertNames(otherSet,input,"intersection other"));
        }

        if(n==1024) foreach(string pattern in new[]{"head","tail","spread","mixed"}) {
            var smallInput=pattern=="head"?left.Take(4).ToArray():pattern=="tail"?left.Skip(n-4).ToArray():pattern=="spread"?new[]{left[0],left[n/3],left[2*n/3],left[n-1]}:new[]{left[n/3],left[n-1],ordered[n+10],ordered[n+20]};
            var small=Build(smallInput);var output=new GameplayTagContainer(n);var common=smallInput.Where(t=>left.Any(x=>x.Name==t.Name)).ToArray();
            var remainder=left.Where(t=>!smallInput.Any(x=>x.Name==t.Name)).ToArray();var reverse=smallInput.Where(t=>!left.Any(x=>x.Name==t.Name)).ToArray();
            yield return Row("focus-skew-intersection-large-left-"+pattern,_=>{output.SetIntersectionExact(a,small);sink+=output.Count;},common,()=>output,()=>AssertNames(small,smallInput,"skew source"));
            yield return Row("focus-skew-intersection-small-left-"+pattern,_=>{output.SetIntersectionExact(small,a);sink+=output.Count;},common,()=>output,()=>AssertNames(small,smallInput,"skew source"));
            yield return Row("focus-skew-difference-large-left-"+pattern,_=>{output.CopyFrom(a);output.ExceptWithExact(small);sink+=output.Count;},remainder,()=>output,()=>AssertNames(small,smallInput,"skew source"));
            yield return Row("focus-skew-difference-small-left-"+pattern,_=>{output.CopyFrom(small);output.ExceptWithExact(a);sink+=output.Count;},reverse,()=>output,()=>AssertNames(small,smallInput,"skew source"));
        }
#endif
    }

    // Repair experiment: fixed 44 whole-public-operation cells, retaining all observers.
    static IEnumerable<Case> NarrowCases(int n) {
        foreach(var c in Cases(n))
            if(c.Key=="full-lifecycle" || c.Key=="new-union" || c.Key=="new-exact-intersection" || c.Key=="target-intersection-nonempty") yield return c;
        foreach(var c in FocusCases(n))
            if(c.Key.StartsWith("focus-intersection-small-result-",StringComparison.Ordinal)) yield return c;
        var ordered=tags.OrderBy(t=>t.Name,StringComparer.Ordinal).ToArray();
        var leftInput=ordered.Take(n).ToArray();
        foreach(string shape in new[]{"0","1","2","full"}) {
            int common=shape=="full"?n:Math.Min(n,int.Parse(shape));
            var rightInput=leftInput.Take(common).Concat(ordered.Skip(n+4).Take(n-common)).ToArray();
            var left=Build(leftInput);var right=Build(rightInput);GameplayTagContainer last=null;
            var expected=leftInput.Take(common).ToArray();string key="fresh-exact-intersection-"+shape;
            if(n>0 && common==0 && string.CompareOrdinal(leftInput[n-1].Name,rightInput[0].Name)>=0)throw new InvalidOperationException("fresh disjoint geometry");
            yield return new Case{N=n,Key=key,Run=_=>{last=GameplayTagContainer.IntersectionExact(left,right);sink+=last.Count;},Retained=()=>last,
                Verify=()=>{AssertNames(left,leftInput,key+" left source");AssertNames(right,rightInput,key+" right source");AssertNames(last,expected,key);}};
        }
    }

    // Payload only: traverse runtime-owned objects, never settings or shared strings.
    // Array element sizes include reference slots but not referenced string storage.
    sealed class Census { public long Bytes, Objects; public readonly HashSet<object> Seen=new HashSet<object>(ReferenceEqualityComparer.Instance); }
    static void Visit(object o,bool excludeSnapshot,Census census) {
        if(o==null || o is string || !census.Seen.Add(o))return;
        var type=o.GetType();if(type.IsPrimitive||type.IsEnum||o is Delegate||o is GameplayTagSettings)return;
        if(excludeSnapshot && type.Name=="RegistrySnapshot")return;
        if(o is Array arr){census.Objects++;var et=type.GetElementType();census.Bytes+=(long)arr.Length*Size(et);if(!et.IsPrimitive)foreach(var x in arr)Visit(x,excludeSnapshot,census);return;}
        if(type.IsValueType)return;
        if(type.Assembly!=typeof(GameplayTagContainer).Assembly && type.Namespace!="System.Collections.Generic" && type.Namespace!="System.Collections.ObjectModel")return;
        census.Objects++;
        foreach(var f in type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))Visit(f.GetValue(o),excludeSnapshot,census);
    }
    static Census Payload(object root,bool container) { var census=new Census();Visit(root,container,census);return census; }
    static int SizeOf<T>() => Unsafe.SizeOf<T>();
    static int Size(Type t) => !t.IsValueType?IntPtr.Size:(int)typeof(Benchmark).GetMethod(nameof(SizeOf),BindingFlags.Static|BindingFlags.NonPublic).MakeGenericMethod(t).Invoke(null,null);
    static Census SharedPayload(){var census=new Census();foreach(var f in typeof(GameplayTagManager).GetFields(BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public))Visit(f.GetValue(null),false,census);return census;}
    static long FieldPayload(GameplayTagContainer c,string name){if(c==null)return 0;var f=typeof(GameplayTagContainer).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);return f==null?0:Payload(f.GetValue(c),true).Bytes;}
    static long StaleSnapshotPayload(GameplayTagContainer c){var f=typeof(GameplayTagContainer).GetField("m_Snapshot",BindingFlags.Instance|BindingFlags.NonPublic);var snapshot=f?.GetValue(c);var manager=typeof(GameplayTagManager).GetField("s_Snapshot",BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null);return snapshot==null||ReferenceEquals(snapshot,manager)?0:Payload(snapshot,false).Bytes;}
    static void VerifyRegistry(){var expected=Enumerable.Range(0,Universe).Select(NameAt).OrderBy(x=>x,StringComparer.Ordinal).ToArray();var actual=GameplayTagManager.GetAllTags().Select(t=>t.Name).ToArray();if(GameplayTagManager.TagCount!=Universe||!actual.SequenceEqual(expected,StringComparer.Ordinal))throw new InvalidOperationException("BENCHMARK REGISTRY FAILURE");}
    static void RetentionDiagnostic(string version,int n){FreshRegistry();var c=Build(tags.Take(n).ToArray());var hit=tags[0];GameplayTagManager.Initialize(settings,true);long before=StaleSnapshotPayload(c);c.HasTagExact(hit);long rebound=StaleSnapshotPayload(c);GameplayTagManager.Initialize(settings,true);c.Clear();long cleared=StaleSnapshotPayload(c);Console.Error.WriteLine($"stale_snapshot_array_payload,{version},{n},before_rebind={before},after_rebind={rebound},after_next_rebuild_and_clear={cleared}");}
    static double Measure(Case c,int count,out long bytes,out int g0,out int g1,out int g2) {
        c.Prepare(count);long allocated=GC.GetAllocatedBytesForCurrentThread();g0=GC.CollectionCount(0);g1=GC.CollectionCount(1);g2=GC.CollectionCount(2);
        long start=Stopwatch.GetTimestamp();for(int i=0;i<count;i++)c.Run(i);long end=Stopwatch.GetTimestamp();
        bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;g0=GC.CollectionCount(0)-g0;g1=GC.CollectionCount(1)-g1;g2=GC.CollectionCount(2)-g2;
        return (end-start)*1e9/Stopwatch.Frequency;
    }
    static int Main(string[] args) {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        if(args.Length<3){Console.Error.WriteLine("Benchmark calibrate|measure VERSION MAP [ROUND]");return 2;}
        bool calibrate=args[0]=="calibrate";string version=args[1],map=args[2];int round=args.Length>3?int.Parse(args[3]):0;
        settings=ScriptableObject.CreateInstance<GameplayTagSettings>();settings.ReplaceAll(Enumerable.Range(0,Universe).Select(i=>new GameplayTagDefinition(NameAt(i),"","",false,true)).ToList(),new List<GameplayTagRedirect>(),new List<GameplayTagSource>());
        FreshRegistry();Console.Error.WriteLine($"declared_universe={Universe};actual_registry_count={GameplayTagManager.TagCount};tag_record_size={Size(typeof(GameplayTag))}");
        var counts=calibrate?new Dictionary<string,int>():File.ReadAllLines(map).ToDictionary(l=>l.Split(',')[0],l=>int.Parse(l.Split(',')[1]));
        Console.Error.WriteLine("runtime="+System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription+"; os="+System.Runtime.InteropServices.RuntimeInformation.OSDescription+"; cpu="+Environment.ProcessorCount+"; stopwatch="+Stopwatch.Frequency+"; tiered="+Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"));
        if(!calibrate)Console.WriteLine("version,round,n,case,adapter,iterations,ns_per_op,bytes_per_op,gc0,gc1,gc2,retained_container_array_payload,shared_registry_array_payload,container_census_objects,shared_census_objects,tag_record_size,name_array_payload,id_array_payload,scratch_array_payload,batch_ms,short_batch,calibration_capped,status");
        var cases=new[]{0,8,64,1024}.SelectMany(NarrowCases).Select(c=>(c.N,c.Key)).ToList();
        foreach(var template in cases){FreshRegistry();var c=NarrowCases(template.N).First(x=>x.Key==template.Key);string key=c.N+":"+c.Key;c.Prepare(8);for(int i=0;i<8;i++)c.Run(i);
            int count=calibrate?1:counts[key];
            if(calibrate){int cap=c.InputPool?Math.Max(8,262144/Math.Max(1,c.N)):16777216;while(true){double time=Measure(c,count,out _,out _,out _,out _);try{c.Verify();}catch(Exception e){Console.Error.WriteLine($"CALIBRATION_FAIL,{key},{count},{time/count},{e.Message}");throw;}if(time>=200000000 || count>=cap)break;count=Math.Min(cap,count*2);}counts[key]=count;}
            else {double ns=Measure(c,count,out long bytes,out int g0,out int g1,out int g2);Exception failure=null;try{c.Verify();}catch(Exception e){failure=e;}var sample=c.Retained?.Invoke();var retained=Payload(sample,true);var shared=SharedPayload();Console.WriteLine(string.Join(",",version,round,c.N,c.Key,c.Adapter,count,(ns/count).ToString("F3"),((double)bytes/count).ToString("F3"),g0,g1,g2,retained.Bytes,shared.Bytes,retained.Objects,shared.Objects,Size(typeof(GameplayTag)),FieldPayload(sample,"m_GameplayTags"),FieldPayload(sample,"m_Ids"),FieldPayload(sample,"m_Scratch"),(ns/1e6).ToString("F3"),ns<200000000?"yes":"no",count>=(c.InputPool?Math.Max(8,262144/Math.Max(1,c.N)):16777216)?"yes":"no",failure==null?"PASS":"FAIL"));if(failure!=null)throw failure;}
        }
        if(calibrate)File.WriteAllLines(map,counts.Select(p=>p.Key+","+p.Value));
        Console.Error.WriteLine("sink="+sink);return 0;
    }
}
