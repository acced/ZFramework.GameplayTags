// Untimed targeted FIFO boundary differential, derived from frozen deep-overflow/Program.cs.
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
    static readonly string[] Histories={"set","amount3","max-once","max-twice","zero-wrap","zero-wrap-readd1","zero-wrap-readd1-remove1"};
    static readonly string[] Orders={"ascending","descending","shuffled"};
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly FieldInfo Buckets=typeof(TagStorage).GetField("m_Buckets",Fields);
    static readonly FieldInfo Mask=typeof(TagStorage).GetField("m_BucketMask",Fields);
    static GameplayTag[] Tags, Fillers;
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
        const int depth=1025;var names=new string[depth];names[0]="Chain";for(int i=1;i<depth;i++)names[i]=names[i-1]+".N";
        var registrations=new List<GameplayTagRegistration>{new GameplayTagRegistration(names[depth-1]),new GameplayTagRegistration("Old"),new GameplayTagRegistration("Spare")};
        for(int i=0;i<depth+1;i++)registrations.Add(new GameplayTagRegistration("Seed"+i));
        GameplayTagManager.Initialize(registrations.ToArray());
        Tags=names.Select(GameplayTagManager.RequestTag).ToArray();Fillers=new GameplayTag[depth+1];
        for(int i=0;i<Fillers.Length;i++)Fillers[i]=GameplayTagManager.RequestTag("Seed"+i);
        Old=GameplayTagManager.RequestTag("Old");Spare=GameplayTagManager.RequestTag("Spare");
    }
    static void Run(int size,string history,string order)
    {
        string id=size+"/"+history+"/"+order;Cases++;Console.WriteLine("CASE\t"+id);
        IGameplayTagContainer seed=history=="set"?(IGameplayTagContainer)new GameplayTagContainer():new GameplayTagCountContainer();
        seed.AddTag(Old);seed.RemoveTag(Old);
        if(history=="set")seed.AddTag(Tags[0]);
        else
        {
            var counted=(GameplayTagCountContainer)seed;
            int[] amounts=history=="amount3"?new[]{3}:history=="max-once"?new[]{int.MaxValue}:history=="max-twice"?new[]{int.MaxValue,int.MaxValue}:history=="zero-wrap"?new[]{int.MaxValue,int.MaxValue,2}:new[]{int.MaxValue,int.MaxValue,2,1};
            for(int i=0;i<amounts.Length;i++){int amount=amounts[i];Check(Operation(id,"seed-add-root-"+i+"-amount-"+amount,()=>counted.AddTag(Tags[0],amount)),"seed setup failed");}
            if(history=="zero-wrap-readd1-remove1")Check(Operation(id,"seed-remove-root-1",()=>counted.RemoveTag(Tags[0],1)),"seed removal failed");
        }
        // Make the preserved seed closure larger than the source so factory seeding cannot swap them.
        for(int i=0;i<size+1;i++)seed.AddTag(Fillers[i]);
        var source=new GameplayTagContainer();source.AddTag(Old);source.RemoveTag(Old);
        int[] sequence=SourceOrder(size,order);Console.WriteLine("ORDER\t"+id+"\t"+string.Join(",",sequence));
        foreach(int index in sequence)source.AddTag(Tags[index]);
        Check(source.Indices.Storage.ExplicitCount==size,"wrong actual source explicit slots");
        Check(source.Indices.Storage.Count==size,"wrong source closure");
        Check(seed.Indices.Storage.Count>source.Indices.Storage.Count,"seed selection would swap source");
        Console.WriteLine("ROUTE\t"+id+"\tE="+source.Indices.Storage.ExplicitCount+"\tSourceCount="+source.Indices.Storage.Count+"\tSeedCount="+seed.Indices.Storage.Count);
        string seedBefore=Snapshot(seed),sourceBefore=Snapshot(source);object[] seedRefs=Refs(seed),sourceRefs=Refs(source);
        Observe(id,"seed-before",seed);Observe(id,"source-before",source);
        IGameplayTagContainer view=new NormalView(seed);GameplayTagContainer result=null;
        bool created=Operation(id,"factory-union",()=>result=GameplayTagContainer.Union(view,source));
        Check(created,"factory unexpectedly threw");Observe(id,"union",result);
        for(int i=0;i<3;i++){int index=size-1-i;Operation(id,"remove-deepest-"+(i+1),()=>result.RemoveTag(Tags[index]));Observe(id,"remove-"+(i+1),result);}
        Operation(id,"duplicate-root-add",()=>result.AddTag(Tags[0]));Observe(id,"duplicate-root-add",result);
        Operation(id,"root-remove",()=>result.RemoveTag(Tags[0]));Observe(id,"root-remove",result);
        Operation(id,"root-readd",()=>result.AddTag(Tags[0]));Observe(id,"root-readd",result);
        string beforeCopy=Snapshot(result);object[] resultRefs=Refs(result);
        var copy=new GameplayTagContainer(size*4+32);copy.AddTag(Old);copy.OnBeforeSerialize();
        Operation(id,"copy-retained",()=>GameplayTagContainer.Copy(copy,result));Observe(id,"copy-retained",copy);
        Unchanged(id+"/copy",beforeCopy,resultRefs,result);
        Operation(id,"clear",()=>result.Clear());Observe(id,"clear",result);
        Operation(id,"regrow",()=>{result.AddTag(Spare);result.AddTag(Tags[size-1]);});Observe(id,"regrow",result);
        Unchanged(id+"/seed",seedBefore,seedRefs,seed);Unchanged(id+"/source",sourceBefore,sourceRefs,source);
        Observe(id,"seed-after",seed);Observe(id,"source-after",source);
    }
    static void Main()
    {
        string schema=string.Join(",",typeof(TagStorage).GetFields(Fields).Select(f=>f.Name).OrderBy(x=>x,StringComparer.Ordinal));
        Check(schema=="Count,Entries,ExplicitCount,ExplicitIndices,m_BucketMask,m_Buckets","unobserved storage fields");
        string entry=string.Join(",",typeof(TagStorage.Entry).GetFields(Fields).Select(f=>f.Name).OrderBy(x=>x,StringComparer.Ordinal));
        Check(entry=="Count,ExplicitCount,ExplicitIndex,Id","unobserved entry fields");
        Register();foreach(int size in Sizes)foreach(string history in Histories)foreach(string order in Orders)Run(size,history,order);
        Console.Error.WriteLine("SUMMARY sizes="+Sizes.Length+" histories="+Histories.Length+" orders="+Orders.Length+" cases="+Cases+" observations="+Observations+" sourceImmutabilityChecks="+ImmutabilityChecks);
    }
}
