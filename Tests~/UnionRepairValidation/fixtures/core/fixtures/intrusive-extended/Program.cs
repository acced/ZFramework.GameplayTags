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
        const int depth=258;var names=new string[depth];var seeds=new string[depth];names[0]="Chain";seeds[0]="OtherChain";
        for(int i=1;i<depth;i++){names[i]=names[i-1]+".N";seeds[i]=seeds[i-1]+".N";}
        var registrations=new List<GameplayTagRegistration>{new GameplayTagRegistration(names[depth-1]),new GameplayTagRegistration(seeds[depth-1]),new GameplayTagRegistration("Old"),new GameplayTagRegistration("Spare")};
        for(int i=0;i<65536;i++)registrations.Add(new GameplayTagRegistration("Seed"+i));
        for(int i=0;i<16384;i++)registrations.Add(new GameplayTagRegistration((i%2==0?"Left":"Right")+".Leaf"+i));
        GameplayTagManager.Initialize(registrations.ToArray());
        Tags=names.Select(GameplayTagManager.RequestTag).ToArray();SeedTags=seeds.Select(GameplayTagManager.RequestTag).ToArray();Fillers=new GameplayTag[65536];
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

    static int Bits(int value){int n=0;for(uint x=(uint)value;x!=0;x>>=1)n++;return n;}
    static IGameplayTagContainer Seed(int size,string kind)
    {
        if(kind=="set"){var s=new GameplayTagContainer();s.AddTag(Old);s.RemoveTag(Old);s.AddTag(SeedTags[size+1]);return s;}
        var c=new GameplayTagCountContainer();c.AddTag(Old);c.RemoveTag(Old);c.AddTag(SeedTags[size+1],3);
        return kind=="counted"?(IGameplayTagContainer)c:new NormalView(c);
    }
    static IGameplayTagContainer Source(int size,int amount,string order,bool view)
    {
        var c=new GameplayTagCountContainer();c.AddTag(Old);c.RemoveTag(Old);
        foreach(int i in SourceOrder(size,order))
        {if(amount<0){c.AddTag(Tags[i],int.MaxValue);c.AddTag(Tags[i],1);}else c.AddTag(Tags[i],amount);}
        return view?(IGameplayTagContainer)new NormalView(c):c;
    }
    static void Diagnostic(string id,IGameplayTagContainer a,IGameplayTagContainer b)
    {
#if INTRUSIVE
        bool aset=a is GameplayTagContainer,bset=b is GameplayTagContainer;
        if((!aset&&bset)||(aset==bset&&a.TagCount<b.TagCount)){var t=a;a=b;b=t;}
        var d=new GameplayTagContainer();d.AddTag(Old);d.RemoveTag(Old);GameplayTagContainer.Copy(d,a);
        var reference=new GameplayTagContainer();reference.AddTag(Old);reference.RemoveTag(Old);GameplayTagContainer.Copy(reference,a);
        var ds=d.Indices.Storage;var ss=b.Indices.Storage;
        int p=Bits(ss.ExplicitCount),i=Bits(ds.Count+ss.Count);
        bool expected=ss.ExplicitCount>8&&p+i<=31;
        for(int j=0;j<ds.Count;j++)if(ds.Entries[j].ExplicitCount!=0&&ds.Entries[j].ExplicitCount!=1)expected=false;
        string before=Snapshot(d),sb=Snapshot(b);var dr=Refs(d);var sr=Refs(b);
        var method=typeof(TagStorage).GetMethod("TryAddUnionIntrusive",Fields);
        bool admitted=(bool)method.Invoke(ds,new object[]{ss});
        Check(admitted==expected,id+" admission mismatch");Unchanged(id+"/diagnostic-source",sb,sr,b);
        if(admitted){reference.Indices.Storage.AddUnion(ss);Check(Snapshot(reference)==Snapshot(d),id+" admitted fullstate mismatch");}
        else Check(before==Snapshot(d)&&Identity(dr,d)=="same,same,same,same",id+" rejection wrote state");
        Console.WriteLine("DIAG\t"+id+"\tE="+ss.ExplicitCount+"\tSeedExplicit="+a.ExplicitTagCount+"\tP="+p+"\tI="+i+"\tAdmitted="+admitted+"\tFullState=PASS");
#endif
    }
    static void Public(string id,IGameplayTagContainer a,IGameplayTagContainer b,bool diagnostic,bool large)
    {
        Cases++;if(diagnostic){Diagnostic(id,a,b);return;}
        Console.WriteLine("CASE\t"+id);var d=new GameplayTagContainer();d.EnsureCapacity(large?131072:128);d.AddTag(Old);d.OnBeforeSerialize();
        string ab=Snapshot(a),bb=Snapshot(b);var ar=Refs(a);var br=Refs(b);
        Observe(id,"source-a",a);Observe(id,"source-b",b);
        Check(Operation(id,"union",()=>Apply(d,a,b)),id+" public union threw");Observe(id,"union",d);
        var reference=new GameplayTagContainer();reference.EnsureCapacity(large?131072:128);reference.AddTag(Old);reference.OnBeforeSerialize();GameplayTagContainer.Copy(reference,GameplayTagContainer.Union(a,b));
        Check(Snapshot(d)==Snapshot(reference),id+" within-process full state mismatch");
        Unchanged(id+"/a",ab,ar,a);Unchanged(id+"/b",bb,br,b);
        if(!large){Operation(id,"remove",()=>d.RemoveTag(Tags[0]));Observe(id,"remove",d);Operation(id,"readd",()=>d.AddTag(Tags[0]));Observe(id,"readd",d);}
        var copy=new GameplayTagContainer(large?131072:256);copy.AddTag(Spare);GameplayTagContainer.Copy(copy,d);Observe(id,"copy",copy);
        // Later public counted arithmetic reaches overflow, then Copy returns a set.
        if(!large){var c=new GameplayTagCountContainer();var en=d.GetExplicitTags();while(en.MoveNext())c.AddTag(en.Current);c.AddTag(Tags[0],int.MaxValue);c.AddTag(Tags[0],int.MaxValue);Observe(id,"counted-overflow",c);GameplayTagContainer.Copy(copy,c);Observe(id,"copy-overflow",copy);}
        d.Clear();Observe(id,"clear",d);d.AddTag(Spare);d.AddTag(Tags[8]);Observe(id,"regrow",d);
    }
    static void Main(string[] args)
    {
        bool diagnostic=args.Length>0&&args[0]=="diagnostic";Register();
        foreach(int size in new[]{9,16,17,31,32,33})foreach(int amount in new[]{3,5,-1})foreach(string order in Orders)foreach(string sk in new[]{"set","counted","counted-view"})foreach(bool sv in new[]{false,true})foreach(bool reverse in new[]{false,true})
        {
            string id="counted/"+size+"/"+amount+"/"+order+"/"+sk+"/"+sv+"/"+reverse;var seed=Seed(size,sk);var source=Source(size,amount,order,sv);
            Public(id,reverse?source:seed,reverse?seed:source,diagnostic,false);
        }
        foreach(int k in new[]{15,16,17,31,32,33,63,64,65,127,128,129,255,256,257,511,512,513,1023,1024,1025})
        {
            var a=new GameplayTagContainer();var b=new GameplayTagContainer();for(int i=0;i<k-9;i++)a.AddTag(Fillers[i]);for(int i=0;i<9;i++)b.AddTag(Fillers[10000+i]);
            Public("K-boundary/"+k,a,b,diagnostic,false);
        }
        foreach(int e in new[]{8191,8192,8193,16383,16384})
        {
            var a=new GameplayTagContainer();var b=new GameplayTagContainer();for(int i=0;i<65536;i++)a.AddTag(Fillers[i]);for(int i=0;i<e;i++)b.AddTag(GameplayTagManager.RequestTag((i%2==0?"Left":"Right")+".Leaf"+i));
            Public("packing/"+e,a,b,diagnostic,true);
        }
        Console.Error.WriteLine("SUMMARY cases="+Cases+" observations="+Observations+" sourceImmutabilityChecks="+ImmutabilityChecks);
    }
}
