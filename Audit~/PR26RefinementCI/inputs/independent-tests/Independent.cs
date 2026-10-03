using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GameplayTags;
using Set = GameplayTags.Experiments.DirectArraySet;
using Layout = GameplayTags.Experiments.BitmapLayout;

internal static class Independent
{
    static long checks, cases;
    static readonly List<object> results = new List<object>();
    static readonly Layout[] layouts = { Layout.Micro, Layout.Dense, Layout.Auto };
    static object sink;
    static void Check(bool ok, string label) { checks++; if (!ok) throw new Exception(label); }
    static void Throws<T>(Action action, string label) where T : Exception
    { bool caught=false; try { action(); } catch(T) { caught=true; } Check(caught, label); }
    static TagRegistry Registry(int n)
    {
        var settings=UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        settings.ReplaceAll(Enumerable.Range(0,n).Select(i=>new GameplayTagDefinition("T"+i.ToString("D7"),"","Default",false,true)).ToList(),new List<GameplayTagRedirect>(),new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});
        return TagRegistry.Create(settings);
    }
    // Only numeric addressing is tested with this owner; real registry construction is tested separately.
    static TagRegistry AddressOwner(int n)
    {
        var r=(TagRegistry)RuntimeHelpers.GetUninitializedObject(typeof(TagRegistry));
        typeof(TagRegistry).GetField("m_Names",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(r,new string[n]);
        return r;
    }
    static Set Make(TagRegistry r,IEnumerable<int> values,Layout layout,int reserve=0)
    { var s=new Set(r,reserve,layout); foreach(int id in values) s.AddTag(r.GetTagAt(id)); return s; }
    static int[] Snapshot(Set s)
    { var a=new List<int>(); foreach(var tag in s) { Check(ReferenceEquals(tag.Registry,s.Registry),"enumerator owner"); a.Add(tag.RuntimeIndex); } return a.ToArray(); }
    static void Verify(Set s,IEnumerable<int> expected)
    {
        s.AssertInvariants(); checks++;
        int[] ids=expected.Distinct().OrderBy(x=>x).ToArray();
        Check(s.Count==ids.Length,"count"); Check(Snapshot(s).SequenceEqual(ids),"ordered members");
        foreach(int id in ids) Check(s.HasTagExact(s.Registry.GetTagAt(id)),"present query");
    }
    static void Group(string label,Action work)
    {
        long first=checks, firstCases=cases;
        try { work(); results.Add(new { label,pass=true,checks=checks-first,cases=cases-firstCases }); Console.WriteLine("PASS "+label); }
        catch(Exception e) { results.Add(new { label,pass=false,checks=checks-first,cases=cases-firstCases,error=e.ToString() }); Console.WriteLine("FAIL "+label+" "+e); }
    }
    static RuntimeTag[] Handles(TagRegistry r,IEnumerable<int> ids) => ids.Select(r.GetTagAt).ToArray();
    static void FactoryCase(TagRegistry r,int[] ids,int capacity,Layout layout)
    {
        var input=Handles(r,ids);var before=(RuntimeTag[])input.Clone();
        var set=Set.FromUnordered(r,input,capacity,layout);
        Verify(set,ids);Check(input.SequenceEqual(before),"factory modified input");
        var capacityOracle=new Set(r,Math.Min(r.Count,Math.Max(capacity,ids.Length)),layout);
        Check(set.Layout==capacityOracle.Layout,"factory representation");
        Check(set.ReservedMemberCapacity==capacityOracle.ReservedMemberCapacity,"factory reservation");
        Check(set.BufferBytes==capacityOracle.BufferBytes,"factory buffer budget");
        if(input.Length>0) input[0]=default;
        Verify(set,ids);
        var copy=new Set(set);copy.Clear();Verify(set,ids);
        cases++;
    }
    static void Builders(bool microOnly=false)
    {
        var targetLayouts=microOnly?new[]{Layout.Micro}:layouts;
        foreach(int n in new[]{0,1,15,16,17,31,32,63,64,65,257,4099})
        {
            var r=Registry(n); int maxRecords=(n+15)/16;
            var lengths=new[]{0,1,2,3,8,15,16,17,31,32,33,maxRecords-1,maxRecords,maxRecords+1,n+3}.Where(x=>x>=0).Distinct();
            foreach(int raw in lengths)
            {
                if(n==0&&raw!=0) continue;
                int[] duplicate=Enumerable.Range(0,raw).Select(i=>n==0?0:(i%5==0?n-1:(i*17+3)%n)).Reverse().ToArray();
                foreach(int capacity in new[]{0,1,8,n,n+7}.Distinct()) foreach(var layout in targetLayouts) FactoryCase(r,duplicate,capacity,layout);
            }
        }
        var high=AddressOwner(1<<20);
        int[] boundary={0,1,15,16,31,32,32767,32768,65535,65536,524287,524288,524289,1048559,1048560,1048574,1048575};
        foreach(var layout in targetLayouts) foreach(int capacity in new[]{0,1,64,65536})
            FactoryCase(high,boundary.Reverse().Concat(boundary).Concat(new[]{1048575,15,524288}).ToArray(),capacity,layout);
        foreach(var layout in targetLayouts) Throws<ArgumentOutOfRangeException>(()=>Set.FromUnordered(AddressOwner((1<<20)+1),Array.Empty<RuntimeTag>(),0,layout),"oversize domain accepted");
    }
    static void StrictAdmission()
    {
        var r=Registry(257);var foreign=Registry(257);
        foreach(var layout in layouts) foreach(int raw in new[]{1,2,8,16,17,18,64,258}) foreach(int badAt in new[]{0,raw/2,raw-1}.Distinct()) foreach(bool alien in new[]{false,true})
        {
            var input=Handles(r,Enumerable.Range(0,raw).Select(i=>(i*31)%r.Count));
            input[badAt]=alien?foreign.GetTagAt(1):default;var before=(RuntimeTag[])input.Clone();
            Throws<ArgumentException>(()=>Set.FromUnordered(r,input,0,layout),"invalid unordered handle accepted");
            Check(input.SequenceEqual(before),"invalid factory changed input");cases++;
        }
        foreach(var layout in layouts)
        {
            var target=Make(r,new[]{0,17,256},layout,64);var before=Snapshot(target);long bytes=target.BufferBytes;
            foreach(var bad in new[]{new[]{r.GetTagAt(1),default},new[]{r.GetTagAt(1),foreign.GetTagAt(2)},Handles(r,new[]{1,1}),Handles(r,new[]{2,1})})
            {
                Throws<ArgumentException>(()=>target.ResetFromSortedUnique(bad),"bad reset accepted");Verify(target,before);Check(target.BufferBytes==bytes,"invalid reset allocated/replaced");
                Throws<ArgumentException>(()=>Set.FromSortedUnique(r,bad,0,layout),"bad sorted factory accepted");
            }
            Throws<ArgumentNullException>(()=>Set.FromUnordered(null,Array.Empty<RuntimeTag>(),0,layout),"null empty owner accepted");
            Throws<ArgumentOutOfRangeException>(()=>Set.FromUnordered(r,Array.Empty<RuntimeTag>(),-1,layout),"negative capacity accepted");
            Throws<ArgumentOutOfRangeException>(()=>Set.FromUnordered(r,Array.Empty<RuntimeTag>(),0,(Layout)42),"bad layout accepted");
        }
    }
    static void Queries()
    {
        var r=AddressOwner(1<<20);var foreign=AddressOwner(1<<20);
        foreach(int offset in new[]{0,32768,524160,1047424}) foreach(int records in new[]{0,1,2,3,7,8,9,15,16,17,24,31,32,33,63})
        {
            int[] ids=Enumerable.Range(0,records).SelectMany(i=>new[]{offset+i*16+(i%16),offset+i*16+15}).Distinct().ToArray();
            // Leave records in inactive storage, including possible vector tail lanes.
            var s=Make(r,Enumerable.Range(0,64).Select(i=>offset+i*16+14),Layout.Micro,128);s.Clear();
            foreach(int id in ids) s.AddTag(r.GetTagAt(id));
            var oracle=new HashSet<int>(ids);
            for(int i=0;i<64*16;i++) Check(s.HasTagExact(r.GetTagAt(offset+i))==oracle.Contains(offset+i),"query key/bit or inactive tail");
            Check(!s.HasTagExact(default),"default query");Throws<ArgumentException>(()=>s.HasTagExact(foreign.GetTagAt(offset)),"foreign query");
            if(records>0) { s.RemoveTag(r.GetTagAt(ids[ids.Length-1])); oracle.Remove(ids[ids.Length-1]); }
            Verify(s,oracle);cases++;
        }
    }
    static void CopyAndAliases()
    {
        foreach(int n in new[]{0,1,17,257,4099})
        {
            var r=Registry(n);var foreign=Registry(n);
            foreach(int size in new[]{0,1,2,8,16,32,33,128,n}.Where(x=>x<=n).Distinct())
            {
                int[] ids=Enumerable.Range(0,size).Select(i=>n-1-i).OrderBy(x=>x).ToArray();
                foreach(var sl in layouts) foreach(var tl in layouts) foreach(int reserve in new[]{0,1,n})
                {
                    var source=Make(r,ids,sl);var target=Make(r,n==0?Array.Empty<int>():new[]{0,n-1}.Distinct(),tl,reserve);long oldBytes=target.BufferBytes;
                    target.CopyFrom(source);Verify(target,ids);Verify(source,ids);
                    Check(target.BufferBytes>=oldBytes,"copy shrunk buffer");target.CopyFrom(target);Verify(target,ids);
                    if(n>0) { target.RemoveTag(r.GetTagAt(ids.Length>0?ids[0]:0));Verify(source,ids); }
                    target.CopyFrom(source);var empty=new Set(r,0,sl);
                    Set.UnionInto(source,source,target);Verify(target,ids);
                    Set.UnionInto(empty,source,target);Verify(target,ids);Set.UnionInto(source,empty,target);Verify(target,ids);
                    var left=new Set(source);Set.UnionInto(left,empty,left);Verify(left,ids);var right=new Set(source);Set.UnionInto(empty,right,right);Verify(right,ids);
                    var previous=Snapshot(target);
                    Throws<ArgumentException>(()=>target.CopyFrom(new Set(foreign,0,sl)),"foreign empty copy");Verify(target,previous);
                    Throws<ArgumentException>(()=>Set.UnionInto(source,new Set(foreign,0,sl),target),"foreign empty union");Verify(target,previous);
                    Throws<ArgumentNullException>(()=>target.CopyFrom(null),"null copy");Verify(target,previous);
                    long bytes=target.BufferBytes;target.CopyFrom(empty);Verify(target,Array.Empty<int>());Check(target.BufferBytes==bytes,"empty copy changed buffer");
                    if(n>0) { target.AddTag(r.GetTagAt(n-1));Verify(target,new[]{n-1}); }
                    cases++;
                }
            }
        }
    }
    static void PreparedAllocations()
    {
        var r=Registry(4099);var tags=Handles(r,Enumerable.Range(0,64).Select(i=>i*63));
        foreach(var la in layouts) foreach(var lb in layouts) foreach(var lc in layouts)
        {
            var a=Make(r,tags.Take(32).Select(t=>t.RuntimeIndex),la,128);
            var b=Make(r,tags.Skip(16).Take(32).Select(t=>t.RuntimeIndex),lb,128);
            var c=new Set(r,256,lc);var empty=new Set(r,0,lb);
            Action body=()=> { c.CopyFrom(a);c.AppendTags(b);c.RemoveTags(b);Set.UnionInto(a,b,c);c.ResetFromSortedUnique(tags);c.CopyFrom(empty);c.CopyFrom(a);if(!c.HasTagExact(tags[0])) throw new Exception("query");sink=c; };
            for(int i=0;i<128;i++) body();
            long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1024;i++) body();long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
            Check(bytes==0,"prepared allocations "+bytes);Verify(a,tags.Take(32).Select(t=>t.RuntimeIndex));Verify(b,tags.Skip(16).Take(32).Select(t=>t.RuntimeIndex));cases++;
        }
        long initial=GC.GetAllocatedBytesForCurrentThread();sink=new byte[128];Check(GC.GetAllocatedBytesForCurrentThread()-initial>=128,"allocation positive control");
    }
    static int Main(string[] args)
    {
        Group("unordered-builders-ownership-capacity-duplicates-high-keys",()=>Builders());
        Group("micro-only-builder-checked-compatible",()=>Builders(true));
        Group("strict-admission-late-default-and-foreign",StrictAdmission);
        Group("query-keys-bits-vector-edges-and-inactive-tail",Queries);
        Group("copy-empty-small-large-admission-and-aliases",CopyAndAliases);
        Group("prepared-zero-managed-allocation-all-layout-triples",PreparedAllocations);
        var result=new { checks,cases,hardware=Vector.IsHardwareAccelerated,vectorUint=Vector<uint>.Count,results };
        string json=JsonSerializer.Serialize(result,new JsonSerializerOptions { WriteIndented=true });File.WriteAllText(args[0],json);Console.WriteLine(json);
        return results.Any(x=>(bool)x.GetType().GetProperty("pass").GetValue(x)==false)?1:0;
    }
}
