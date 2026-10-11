// Untimed reusable-output borrowed FIFO boundary differential.
// Diagnostic mode invokes private method on separate publicly constructed clones only.
// All state construction and continuation mutations use public container APIs.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using GameplayTags;
internal static class Program
{
    static readonly int[] Sizes={8,9,15,16,17,31,32,33,63,64,65,127,128,129,255,256,257,511,512,513,1023,1024,1025};
    static readonly string[] Modes={"fresh-growth","reserved-nogrowth","drained-nogrowth","overlap-growth","source-larger-fallback","left-alias","right-alias"};
    static readonly string[] Orders={"ascending","descending","shuffled"};
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly FieldInfo Buckets=typeof(TagStorage).GetField("m_Buckets",Fields);
    static readonly FieldInfo Mask=typeof(TagStorage).GetField("m_BucketMask",Fields);
    static GameplayTag[] Tags, Fillers, SeedTags;
    static GameplayTag Old, Spare;
    static int Cases, Observations, ImmutabilityChecks;
    sealed class NormalView : GameplayTagContainer, IGameplayTagContainer
    {
        readonly IGameplayTagContainer inner;
        internal NormalView(IGameplayTagContainer inner) { this.inner=inner; }
        GameplayTagContainerIndices IGameplayTagContainer.Indices => inner.Indices;
    }
    static void Check(bool value,string why){if(!value)throw new Exception(why);}
    static string Encode(string value)=>Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    static string Capture(Func<string> f){try{return f();}catch(Exception ex){return "THROW:"+ex.GetType().FullName+":"+ex.Message;}}
    static string Enumeration(IGameplayTagContainer c,bool exact)
    {
        var result=new List<int>();var enumerator=exact?c.GetExplicitTags():c.GetTags();
        while(enumerator.MoveNext())
        {
            Check(result.Count<c.Indices.Storage.Entries.Length+c.Indices.Storage.ExplicitIndices.Length+2,"enumeration limit");
            result.Add(enumerator.Current.RuntimeIndex);
        }
        return string.Join(",",result);
    }
    static string Snapshot(IGameplayTagContainer c)
    {
        var s=c.Indices.Storage;var b=new StringBuilder();
        b.Append("Count=").Append(s.Count).Append(";ExplicitCount=").Append(s.ExplicitCount).Append(";Mask=").Append(Mask.GetValue(s));
        b.Append(";Entries=").Append(s.Entries.Length).Append(':');
        foreach(var e in s.Entries)b.Append(e.Id).Append(',').Append(e.Count).Append(',').Append(e.ExplicitCount).Append(',').Append(e.ExplicitIndex).Append(';');
        b.Append("ExplicitIndices=").Append(s.ExplicitIndices.Length).Append(':').Append(string.Join(",",s.ExplicitIndices));
        int[] buckets=(int[])Buckets.GetValue(s);b.Append(";Buckets=").Append(buckets.Length).Append(':').Append(string.Join(",",buckets));
        b.Append(";Public=").Append(Capture(()=>c.IsEmpty+"/"+c.TagCount+"/"+c.ExplicitTagCount));
        b.Append(";Tags=").Append(Capture(()=>Enumeration(c,false))).Append(";ExplicitTags=").Append(Capture(()=>Enumeration(c,true)));
        foreach(var tag in new[]{Tags[0],Tags[Tags.Length/2],Tags[Tags.Length-1],Old,Spare})
        {
            b.Append(";Probe").Append(tag.RuntimeIndex).Append('=').Append(Capture(()=>c.HasTag(tag)+"/"+c.HasTagExact(tag)+"/"+s.GetCount(tag.RuntimeIndex)+"/"+s.GetExplicitCount(tag.RuntimeIndex)));
        }
        if(c is GameplayTagContainer normal)b.Append(";Serialized=").Append(normal.m_SerializedExplicitTags==null?"null":normal.m_SerializedExplicitTags.Capacity+":"+string.Join(",",normal.m_SerializedExplicitTags));
        if(c is GameplayTagCountContainer counted)b.Append(";RootCounts=").Append(Capture(()=>counted.GetTagCount(Tags[0])+"/"+counted.GetExplicitTagCount(Tags[0])));
        return b.ToString();
    }
    static object[] Refs(IGameplayTagContainer c){var s=c.Indices.Storage;return new object[]{s,s.Entries,s.ExplicitIndices,Buckets.GetValue(s)};}
    static string Identity(object[] before,IGameplayTagContainer after){var a=Refs(after);return string.Join(",",before.Select((x,i)=>ReferenceEquals(x,a[i])?"same":"changed"));}
    static void Unchanged(string id,string before,object[] refs,IGameplayTagContainer after)
    {Check(before==Snapshot(after)&&Identity(refs,after)=="same,same,same,same",id+" source changed");ImmutabilityChecks++;}
    static void Observe(string id,string stage,IGameplayTagContainer c){Observations++;Console.WriteLine("OBS\t"+id+"\t"+stage+"\t"+Encode(Snapshot(c)));}
    static bool Operation(string id,string operation,Action action)
    {
        try{action();Console.WriteLine("OP\t"+id+"\t"+operation+"\tOK");return true;}
        catch(Exception ex){Console.WriteLine("OP\t"+id+"\t"+operation+"\tTHROW\t"+Encode(ex.GetType().FullName+":"+ex.Message));return false;}
    }
    static int[] SourceOrder(int size,string order)
    {
        var result=new int[size];for(int i=0;i<size;i++)result[i]=order=="descending"?size-1-i:i;
        if(order=="shuffled")
        {
            uint state=0x051ced42;
            for(int i=size-1;i>0;i--){state=unchecked(state*1664525u+1013904223u);int j=(int)(state%(uint)(i+1));int t=result[i];result[i]=result[j];result[j]=t;}
        }
        return result;
    }
    static void Register()
    {
        const int depth=1026;var names=new string[depth];var seeds=new string[depth];names[0]="Chain";seeds[0]="OtherChain";
        for(int i=1;i<depth;i++){names[i]=names[i-1]+".N";seeds[i]=seeds[i-1]+".N";}
        var registrations=new List<GameplayTagRegistration>{new GameplayTagRegistration(names[depth-1]),new GameplayTagRegistration(seeds[depth-1]),new GameplayTagRegistration("Old"),new GameplayTagRegistration("Spare")};
        for(int i=0;i<4096;i++)registrations.Add(new GameplayTagRegistration("Seed"+i));
        GameplayTagManager.Initialize(registrations.ToArray());
        Tags=names.Select(GameplayTagManager.RequestTag).ToArray();SeedTags=seeds.Select(GameplayTagManager.RequestTag).ToArray();Fillers=new GameplayTag[4096];
        for(int i=0;i<Fillers.Length;i++)Fillers[i]=GameplayTagManager.RequestTag("Seed"+i);
        Old=GameplayTagManager.RequestTag("Old");Spare=GameplayTagManager.RequestTag("Spare");
    }
    static int Capacity(int n){int result=8;while(result<n)result*=2;return result;}
    static void Drain(GameplayTagContainer c,int n)
    {for(int i=0;i<n;i++)c.AddTag(Fillers[i]);for(int i=0;i<n;i++)c.RemoveTag(Fillers[i]);}
    static void Apply(GameplayTagContainer output,IGameplayTagContainer a,IGameplayTagContainer b)
    {
#if REUSABLE_UNION
        GameplayTagContainer.Union(output,a,b);
#else
        GameplayTagContainer.Copy(output,GameplayTagContainer.Union(a,b));
#endif
    }
    static void Run(int size,string mode,string order,bool diagnostic)
    {
        string id=size+"/"+mode+"/"+order;Cases++;
        if(!diagnostic)Console.WriteLine("CASE\t"+id);
        int capacity=Capacity(size);var seed=new GameplayTagContainer();var source=new GameplayTagContainer();
        seed.AddTag(Old);seed.RemoveTag(Old);source.AddTag(Old);source.RemoveTag(Old);
        if(mode=="drained-nogrowth"){Drain(seed,capacity*2);Drain(source,capacity*2);}
        seed.AddTag(SeedTags[size]);
        int explicitSeed=mode=="source-larger-fallback"?1:capacity;
        for(int i=1;i<explicitSeed;i++)seed.AddTag(Fillers[i-1]);
        if(mode=="overlap-growth"){seed.RemoveTag(Fillers[explicitSeed-2]);seed.AddTag(Tags[0]);}
        int[] sequence=SourceOrder(size,order);
        if(!diagnostic)Console.WriteLine("ORDER\t"+id+"\t"+string.Join(",",sequence));
        foreach(int index in sequence)source.AddTag(Tags[index]);
        Check(source.Indices.Storage.ExplicitCount==size&&source.TagCount==size,"wrong source E/closure");
        Check(seed.TagCount>source.TagCount,"factory seed would swap");
        var result=new GameplayTagContainer();
        if(mode=="reserved-nogrowth"||mode=="drained-nogrowth")result.EnsureCapacity(capacity*2);
        if(mode=="drained-nogrowth")Drain(result,capacity*2);
        result.AddTag(Old);result.OnBeforeSerialize();
        if(mode=="left-alias")result=seed;
        if(mode=="right-alias")result=source;
        int beforeExplicitCapacity=(mode=="left-alias"||mode=="right-alias")?Capacity(seed.ExplicitTagCount):Math.Max(result.Indices.Storage.ExplicitIndices.Length,Capacity(seed.ExplicitTagCount));
        int missing=size-(mode=="overlap-growth"?1:0);
        bool expectedAdmission=size>8&&size<=seed.ExplicitTagCount;
        int growth=seed.ExplicitTagCount+missing>beforeExplicitCapacity?1:0;
        if(diagnostic)
        {
            var pending=new GameplayTagContainer();
            if(mode!="left-alias"&&mode!="right-alias")
            {
                if(mode=="reserved-nogrowth"||mode=="drained-nogrowth")pending.EnsureCapacity(capacity*2);
                if(mode=="drained-nogrowth")Drain(pending,capacity*2);
                pending.AddTag(Old);
            }
            GameplayTagContainer.Copy(pending,seed);
            var method=typeof(TagStorage).GetMethod("TryAddUnionIntrusive",Fields);
            Check(method!=null,"borrowed method missing");
            string sourceBefore=Snapshot(source);object[] sourceRefs=Refs(source);
            string before=Snapshot(pending);object[] refs=Refs(pending);
            bool admitted=(bool)method.Invoke(pending.Indices.Storage,new object[]{source.Indices.Storage});
            Check(admitted==expectedAdmission,"unexpected admission "+id);
            if(!admitted)Check(before==Snapshot(pending)&&Identity(refs,pending)=="same,same,same,same","rejected method changed destination");
            Unchanged(id+"/diagnostic-source",sourceBefore,sourceRefs,source);
            if(admitted)
            {
                var reference=new GameplayTagContainer();
                if(mode!="left-alias"&&mode!="right-alias")
                {if(mode=="reserved-nogrowth"||mode=="drained-nogrowth")reference.EnsureCapacity(capacity*2);if(mode=="drained-nogrowth")Drain(reference,capacity*2);reference.AddTag(Old);}
                GameplayTagContainer.Copy(reference,seed);reference.Indices.Storage.AddUnion(source.Indices.Storage);
                Check(Snapshot(reference)==Snapshot(pending),"private full-array result differs "+id);
                Check(pending.Indices.Storage.ExplicitIndices.Length==(growth==1?beforeExplicitCapacity*2:beforeExplicitCapacity),"explicit growth count differs");
            }
            Console.WriteLine("DIAG\t"+id+"\tAdmitted="+admitted+"\tExpected="+expectedAdmission+"\tExplicitCapacityBefore="+beforeExplicitCapacity+"\tExplicitCapacityAfter="+pending.Indices.Storage.ExplicitIndices.Length+"\tGrowth="+growth+"\tFullStateAndSource=PASS");
            return;
        }
        Console.WriteLine("ROUTE\t"+id+"\tE="+size+"\tSourceCount="+source.TagCount+"\tSeedCount="+seed.TagCount+"\tSeedExplicit="+seed.ExplicitTagCount+"\tInitialExplicitCapacity="+beforeExplicitCapacity+"\tExpectedAdmission="+(expectedAdmission?1:0)+"\tExpectedGrowth="+growth);
        string seedBefore=Snapshot(seed),sourceBeforePublic=Snapshot(source);object[] seedRefs=Refs(seed),sourceRefsPublic=Refs(source);
        Observe(id,"seed-before",seed);Observe(id,"source-before",source);Observe(id,"output-before",result);
        bool created=Operation(id,"reusable-union",()=>Apply(result,seed,source));Check(created,"union unexpectedly threw");Observe(id,"union",result);
        if(mode!="left-alias")Unchanged(id+"/seed",seedBefore,seedRefs,seed);
        if(mode!="right-alias")Unchanged(id+"/source",sourceBeforePublic,sourceRefsPublic,source);
        Observe(id,"seed-after",seed);Observe(id,"source-after",source);
        for(int i=0;i<3;i++){int index=size-1-i;Operation(id,"remove-deepest-"+(i+1),()=>result.RemoveTag(Tags[index]));Observe(id,"remove-"+(i+1),result);}
        Operation(id,"duplicate-root-add",()=>result.AddTag(Tags[0]));Observe(id,"duplicate-root-add",result);
        Operation(id,"root-remove",()=>result.RemoveTag(Tags[0]));Observe(id,"root-remove",result);
        Operation(id,"root-readd",()=>result.AddTag(Tags[0]));Observe(id,"root-readd",result);
        string beforeCopy=Snapshot(result);object[] resultRefs=Refs(result);
        var copy=new GameplayTagContainer(capacity*4);copy.AddTag(Old);copy.OnBeforeSerialize();
        Operation(id,"copy-retained",()=>GameplayTagContainer.Copy(copy,result));Observe(id,"copy-retained",copy);Unchanged(id+"/copy",beforeCopy,resultRefs,result);
        Operation(id,"clear",()=>result.Clear());Observe(id,"clear",result);
        Operation(id,"regrow",()=>{result.AddTag(Spare);result.AddTag(Tags[size-1]);});Observe(id,"regrow",result);
        Operation(id,"second-union",()=>Apply(result,seed,source));Observe(id,"second-union",result);
    }

    static string Raw(TagStorage s)
    {
        var b=new StringBuilder();b.Append(s.Count).Append('/').Append(s.ExplicitCount).Append('/').Append(Mask.GetValue(s));
        b.Append(";Entries:");if(s.Entries==null)b.Append("null");else{b.Append(s.Entries.Length).Append(':');foreach(var e in s.Entries)b.Append(e.Id).Append(',').Append(e.Count).Append(',').Append(e.ExplicitCount).Append(',').Append(e.ExplicitIndex).Append(';');}
        b.Append(";Explicit:").Append(s.ExplicitIndices==null?"null":s.ExplicitIndices.Length+":"+string.Join(",",s.ExplicitIndices));
        int[] buckets=(int[])Buckets.GetValue(s);b.Append(";Buckets:").Append(buckets==null?"null":buckets.Length+":"+string.Join(",",buckets));return b.ToString();
    }
    static object[] RawRefs(TagStorage s)=>new object[]{s.Entries,s.ExplicitIndices,Buckets.GetValue(s)};
    static bool SameRefs(TagStorage s,object[] before){var after=RawRefs(s);return before.Where((v,i)=>!ReferenceEquals(v,after[i])).Count()==0;}
    static TagStorage Clone(TagStorage s)
    {var r=new TagStorage();r.Count=s.Count;r.ExplicitCount=s.ExplicitCount;r.Entries=(TagStorage.Entry[])s.Entries.Clone();r.ExplicitIndices=(int[])s.ExplicitIndices.Clone();Buckets.SetValue(r,((int[])Buckets.GetValue(s)).Clone());Mask.SetValue(r,Mask.GetValue(s));return r;}
    static int Occupied(TagStorage s){var a=(int[])Buckets.GetValue(s);for(int i=0;i<a.Length;i++)if(a[i]!=0)return i;throw new Exception("no occupied slot");}
    static void Main()
    {
        Register();var sc=new GameplayTagContainer();var dc=new GameplayTagContainer();dc.AddTag(SeedTags[17]);
        for(int i=0;i<17;i++){sc.AddTag(Tags[i]);dc.AddTag(Fillers[i]);}
        var source=sc.Indices.Storage;var dest=dc.Indices.Storage;
        var gate=typeof(TagStorage).GetMethod("HasIntrusiveUnionState",Fields);var method=typeof(TagStorage).GetMethod("TryAddUnionIntrusive",Fields);Check(gate!=null&&method!=null,"missing private methods");
        int n=0;
        Action<string,TagStorage,bool,bool> checkGate=(name,state,destination,expected)=>
        {string before=Raw(state);var refs=RawRefs(state);bool actual=(bool)gate.Invoke(state,new object[]{GameplayTagManager.TagCount,destination});Check(actual==expected&&before==Raw(state)&&SameRefs(state,refs),name+" gate mismatch/mutation");Console.WriteLine("GATE\t"+name+"\tAdmitted="+actual+"\tFullArrayRestored=True\tArrayReferencesSame=True");n++;};
        checkGate("healthy-source",Clone(source),false,true);checkGate("healthy-destination",Clone(dest),true,true);
        var corruptions=new List<KeyValuePair<string,Action<TagStorage>>>{
            new KeyValuePair<string,Action<TagStorage>>("missing-ancestor",s=>{int id=s.Entries[0].Id;while(GameplayTagManager.GetParentIndex(id)!=0)id=GameplayTagManager.GetParentIndex(id);int i=s.Find(id);s.RemoveExplicit(id,s.Entries[i].ExplicitCount);s.RemoveTotal(id,s.Entries[i].Count);}),
            new KeyValuePair<string,Action<TagStorage>>("full-buckets",s=>{var a=(int[])Buckets.GetValue(s);for(int i=0;i<a.Length;i++)a[i]=1;}),
            new KeyValuePair<string,Action<TagStorage>>("entries-null",s=>s.Entries=null),
            new KeyValuePair<string,Action<TagStorage>>("explicit-null",s=>s.ExplicitIndices=null),
            new KeyValuePair<string,Action<TagStorage>>("buckets-null",s=>Buckets.SetValue(s,null)),
            new KeyValuePair<string,Action<TagStorage>>("count-eight",s=>s.Count=8),
            new KeyValuePair<string,Action<TagStorage>>("count-negative",s=>s.Count=-1),
            new KeyValuePair<string,Action<TagStorage>>("count-over-array",s=>s.Count=s.Entries.Length+1),
            new KeyValuePair<string,Action<TagStorage>>("explicit-zero",s=>s.ExplicitCount=0),
            new KeyValuePair<string,Action<TagStorage>>("explicit-negative",s=>s.ExplicitCount=-1),
            new KeyValuePair<string,Action<TagStorage>>("explicit-over-count",s=>s.ExplicitCount=s.Count+1),
            new KeyValuePair<string,Action<TagStorage>>("explicit-short-array",s=>Array.Resize(ref s.ExplicitIndices,s.ExplicitCount-1)),
            new KeyValuePair<string,Action<TagStorage>>("mask-zero",s=>Mask.SetValue(s,0)),
            new KeyValuePair<string,Action<TagStorage>>("mask-negative",s=>Mask.SetValue(s,-1)),
            new KeyValuePair<string,Action<TagStorage>>("mask-over-array",s=>Mask.SetValue(s,((int[])Buckets.GetValue(s)).Length*2-1)),
            new KeyValuePair<string,Action<TagStorage>>("mask-not-power-two",s=>Mask.SetValue(s,((int)Mask.GetValue(s))-1)),
            new KeyValuePair<string,Action<TagStorage>>("bucket-capacity-too-small",s=>Mask.SetValue(s,15)),
            new KeyValuePair<string,Action<TagStorage>>("bucket-negative",s=>((int[])Buckets.GetValue(s))[Occupied(s)]=-1),
            new KeyValuePair<string,Action<TagStorage>>("bucket-over-count",s=>((int[])Buckets.GetValue(s))[Occupied(s)]=s.Count+1),
            new KeyValuePair<string,Action<TagStorage>>("bucket-missing-occupied",s=>((int[])Buckets.GetValue(s))[Occupied(s)]=0),
            new KeyValuePair<string,Action<TagStorage>>("bucket-duplicate-entry",s=>{var a=(int[])Buckets.GetValue(s);int i=Occupied(s),j=i+1;while(j<a.Length&&a[j]==0)j++;if(j==a.Length){j=0;while(j<i&&a[j]==0)j++;}a[j]=a[i];}),
            new KeyValuePair<string,Action<TagStorage>>("id-zero",s=>s.Entries[0].Id=0),
            new KeyValuePair<string,Action<TagStorage>>("id-negative",s=>s.Entries[0].Id=-1),
            new KeyValuePair<string,Action<TagStorage>>("id-unregistered",s=>s.Entries[0].Id=GameplayTagManager.TagCount+1),
            new KeyValuePair<string,Action<TagStorage>>("id-duplicate",s=>s.Entries[1].Id=s.Entries[0].Id),
            new KeyValuePair<string,Action<TagStorage>>("entry-explicit-two",s=>s.Entries[s.ExplicitIndices[0]].ExplicitCount=2),
            new KeyValuePair<string,Action<TagStorage>>("entry-explicit-negative",s=>s.Entries[s.ExplicitIndices[0]].ExplicitCount=-1),
            new KeyValuePair<string,Action<TagStorage>>("entry-explicit-zero",s=>s.Entries[s.ExplicitIndices[0]].ExplicitCount=0),
            new KeyValuePair<string,Action<TagStorage>>("entry-slot-negative",s=>s.Entries[s.ExplicitIndices[0]].ExplicitIndex=-1),
            new KeyValuePair<string,Action<TagStorage>>("entry-slot-over-count",s=>s.Entries[s.ExplicitIndices[0]].ExplicitIndex=s.ExplicitCount),
            new KeyValuePair<string,Action<TagStorage>>("entry-slot-wrong-owner",s=>s.Entries[s.ExplicitIndices[0]].ExplicitIndex=1),
            new KeyValuePair<string,Action<TagStorage>>("explicit-index-negative",s=>s.ExplicitIndices[0]=-1),
            new KeyValuePair<string,Action<TagStorage>>("explicit-index-over-count",s=>s.ExplicitIndices[0]=s.Count),
            new KeyValuePair<string,Action<TagStorage>>("explicit-duplicate-index",s=>s.ExplicitIndices[1]=s.ExplicitIndices[0])
        };
        foreach(var change in corruptions)foreach(bool asSource in new[]{true,false})
        {
            var d=Clone(dest);var s=Clone(source);var malformed=asSource?s:d;change.Value(malformed);string name=(asSource?"source/":"destination/")+change.Key;bool expected=asSource&&(change.Key=="entry-explicit-two"||change.Key=="entry-explicit-negative");checkGate(name,malformed,!asSource,expected);
            string db=Raw(d),sb=Raw(s);var dr=RawRefs(d);var sr=RawRefs(s);var reference=expected?Clone(d):null;bool admitted=(bool)method.Invoke(d,new object[]{s});
            Check(admitted==expected&&sb==Raw(s)&&SameRefs(s,sr),name+" admission/source mismatch");
            if(expected){reference.AddUnion(s);Check(Raw(reference)==Raw(d),name+" fullstate difference");}
            else Check(db==Raw(d)&&SameRefs(d,dr),name+" rejection mutated destination");
            Console.WriteLine("RESULT\t"+name+"\tAdmitted="+admitted+"\tFullState=PASS\tSourceUnchanged=True");n++;
        }
        foreach(string kind in new[]{"source-e-eight","source-e-over-destination","same-storage"})
        {
            var d=Clone(dest);var s=Clone(source);if(kind=="source-e-eight")s.ExplicitCount=8;if(kind=="source-e-over-destination")d.ExplicitCount=16;if(kind=="same-storage")s=d;
            string db=Raw(d),sb=Raw(s);var dr=RawRefs(d);var sr=RawRefs(s);bool admitted=(bool)method.Invoke(d,new object[]{s});
            Check(!admitted&&db==Raw(d)&&sb==Raw(s)&&SameRefs(d,dr)&&SameRefs(s,sr),kind+" precheck mutated state");Console.WriteLine("REJECT\t"+kind+"\tReturned=False\tBothFullArraysRestored=True\tBothReferencesSame=True");n++;
        }
        Console.Error.WriteLine("SUMMARY checks="+n+" corruptions="+corruptions.Count+" sides=2 healthy=2 prechecks=3 mutations=0 replacements=0");
    }
}
