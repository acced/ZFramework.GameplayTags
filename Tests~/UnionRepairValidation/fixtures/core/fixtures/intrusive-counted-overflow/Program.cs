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
        bool expectedAdmission=size>8;
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
                Check(pending.Indices.Storage.ExplicitIndices.Length==Math.Max(beforeExplicitCapacity,Capacity(seed.ExplicitTagCount+missing)),"explicit growth count differs");
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

    static void Main(string[] args)
    {
        bool diagnostic=args.Length!=0;Register();int cases=0;
        foreach(int size in new[]{9,17,65})foreach(string order in Orders)foreach(bool negative in new[]{false,true})
        {
            string id=size+"/"+order+"/"+(negative?"min-total":"max-total");cases++;if(!diagnostic)Console.WriteLine("CASE\t"+id);
            var seed=new GameplayTagCountContainer();var source=new GameplayTagContainer();var root=Tags[0];
            seed.AddTag(root,int.MaxValue);seed.AddTag(root,int.MaxValue);seed.AddTag(root,1);
            int skipped=negative?int.MaxValue:int.MaxValue-1;seed.RemoveTag(root,skipped);seed.AddTag(root,skipped);seed.AddTag(root,2);
            Check(seed.GetExplicitTagCount(root)==1&&seed.GetTagCount(root)==(negative?int.MinValue:int.MaxValue),"overflow setup mismatch");
            seed.AddTag(SeedTags[size]);for(int i=0;i<size;i++)seed.AddTag(Fillers[i]);
            foreach(int index in SourceOrder(size,order))source.AddTag(Tags[index]);
            Check(seed.TagCount>source.TagCount&&seed.ExplicitTagCount>=source.ExplicitTagCount,"wrong overflow routing");
            for(int i=0;i<seed.Indices.Storage.Count;i++)Check(seed.Indices.Storage.Entries[i].ExplicitCount==0||seed.Indices.Storage.Entries[i].ExplicitCount==1,"non-binary explicit counter");
            IGameplayTagContainer view=new NormalView(seed);var result=new GameplayTagContainer();result.AddTag(Old);result.OnBeforeSerialize();
            string a=Snapshot(seed),b=Snapshot(source);var ar=Refs(seed);var br=Refs(source);
            if(diagnostic)
            {
                var pending=new GameplayTagContainer();GameplayTagContainer.Copy(pending,view);var reference=new GameplayTagContainer();GameplayTagContainer.Copy(reference,view);
                var method=typeof(TagStorage).GetMethod("TryAddUnionIntrusive",Fields);bool admitted=(bool)method.Invoke(pending.Indices.Storage,new object[]{source.Indices.Storage});Check(admitted,"overflow 0/1 state rejected");reference.Indices.Storage.AddUnion(source.Indices.Storage);Check(Snapshot(reference)==Snapshot(pending),"overflow private fullarray difference");
                Console.WriteLine("DIAG\t"+id+"\tAdmitted=True\tFullState=PASS");
            }
            else
            {
                Observe(id,"seed-before",seed);Observe(id,"source-before",source);Check(Operation(id,"reusable-union",()=>Apply(result,view,source)),"overflow union threw");Observe(id,"union",result);
                for(int i=0;i<3;i++){int index=size-1-i;Operation(id,"remove-deepest-"+i,()=>result.RemoveTag(Tags[index]));Observe(id,"remove-"+i,result);}
                Operation(id,"root-remove",()=>result.RemoveTag(root));Observe(id,"root-remove",result);Operation(id,"root-add",()=>result.AddTag(root));Observe(id,"root-add",result);
                var copy=new GameplayTagContainer(size*4);Operation(id,"copy",()=>GameplayTagContainer.Copy(copy,result));Observe(id,"copy",copy);
                Operation(id,"clear",()=>result.Clear());Observe(id,"clear",result);Operation(id,"regrow",()=>{result.AddTag(Spare);result.AddTag(Tags[size-1]);});Observe(id,"regrow",result);
                Observe(id,"seed-after",seed);Observe(id,"source-after",source);
            }
            Unchanged(id+"/seed",a,ar,seed);Unchanged(id+"/source",b,br,source);
        }
        Console.Error.WriteLine("SUMMARY cases="+cases+" observations="+Observations+" sourceImmutabilityChecks="+ImmutabilityChecks);
    }
}
