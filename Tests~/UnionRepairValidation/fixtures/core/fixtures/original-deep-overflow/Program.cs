using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using GameplayTags;
internal static class Program
{
    const int Depth = 64;
    static readonly string[] Histories={"amount3","max-once","max-twice","zero-wrap","zero-wrap-readd1","zero-wrap-readd1-remove1"};
    static readonly string[] Orders={"ascending","descending","shuffled"};
    static readonly FieldInfo Buckets=typeof(TagStorage).GetField("m_Buckets",BindingFlags.Instance|BindingFlags.NonPublic);
    static readonly FieldInfo Mask=typeof(TagStorage).GetField("m_BucketMask",BindingFlags.Instance|BindingFlags.NonPublic);
    sealed class NormalView : GameplayTagContainer, IGameplayTagContainer
    {
        readonly IGameplayTagContainer inner;
        internal NormalView(IGameplayTagContainer inner) { this.inner = inner; }
        GameplayTagContainerIndices IGameplayTagContainer.Indices => inner.Indices;
    }
    static string Encode(string value)=>Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    static string Capture(Func<string> f)
    {try{return f();}catch(Exception ex){return "THROW:"+ex.GetType().FullName+":"+ex.Message;}}
    static string Names(GameplayTagEnumerator tags)
    {var list=new List<string>();foreach(var t in tags)list.Add(t.Name);return string.Join(",",list);}
    static string Public(IGameplayTagContainer container,GameplayTag root)
    {
        var lines=new List<string>();
        lines.Add("IsEmpty="+Capture(()=>container.IsEmpty.ToString()));
        lines.Add("TagCount="+Capture(()=>container.TagCount.ToString()));
        lines.Add("ExplicitTagCount="+Capture(()=>container.ExplicitTagCount.ToString()));
        lines.Add("Tags="+Capture(()=>Names(container.GetTags())));
        lines.Add("ExplicitTags="+Capture(()=>Names(container.GetExplicitTags())));
        if(container is GameplayTagCountContainer counted)
        {
            lines.Add("RootCount="+Capture(()=>counted.GetTagCount(root).ToString()));
            lines.Add("RootExplicitCount="+Capture(()=>counted.GetExplicitTagCount(root).ToString()));
        }
        if(container is GameplayTagContainer normal)
        {
            lines.Add("HasRoot="+Capture(()=>normal.HasTag(root).ToString()));
            lines.Add("HasRootExact="+Capture(()=>normal.HasTagExact(root).ToString()));
            lines.Add("Serialized="+(normal.m_SerializedExplicitTags==null?"null":string.Join(",",normal.m_SerializedExplicitTags)));
        }
        return string.Join("\n",lines);
    }
    static string Storage(TagStorage s)
    {
        var b=new StringBuilder();b.Append("Count=").Append(s.Count).Append(";ExplicitCount=").Append(s.ExplicitCount).Append(";Mask=").Append(Mask.GetValue(s));
        b.Append(";Entries=").Append(s.Entries.Length).Append(':');
        for(int i=0;i<s.Entries.Length;i++){var e=s.Entries[i];b.Append(e.Id).Append(',').Append(e.Count).Append(',').Append(e.ExplicitCount).Append(',').Append(e.ExplicitIndex).Append(';');}
        b.Append("ExplicitIndices=").Append(s.ExplicitIndices.Length).Append(':');foreach(int n in s.ExplicitIndices)b.Append(n).Append(',');
        var buckets=(int[])Buckets.GetValue(s);b.Append(";Buckets=").Append(buckets.Length).Append(':');foreach(int n in buckets)b.Append(n).Append(',');
        return b.ToString();
    }
    static void Observe(string id,string stage,IGameplayTagContainer c,GameplayTag root)
    {Console.WriteLine("OBS\t"+id+"\t"+stage+"\t"+Encode(Public(c,root))+"\t"+Encode(Storage(c.Indices.Storage)));}
    static bool Operation(string id,string operation,Action action)
    {
        try{action();Console.WriteLine("OP\t"+id+"\t"+operation+"\tOK");return true;}
        catch(Exception ex){Console.WriteLine("OP\t"+id+"\t"+operation+"\tTHROW\t"+Encode(ex.GetType().FullName+":"+ex.Message));return false;}
    }
    static int[] SourceOrder(string order)
    {
        var indices=new int[Depth];for(int i=0;i<Depth;i++)indices[i]=order=="descending"?Depth-1-i:i;
        if(order=="shuffled")
        {
            uint state=0x051ced42;
            for(int i=Depth-1;i>0;i--){state=unchecked(state*1664525u+1013904223u);int j=(int)(state%(uint)(i+1));int tmp=indices[i];indices[i]=indices[j];indices[j]=tmp;}
        }
        return indices;
    }
    static void Main()
    {
        var names=new string[Depth];names[0]="Chain";for(int i=1;i<Depth;i++)names[i]=names[i-1]+".N";
        var registrations=new List<GameplayTagRegistration>{new GameplayTagRegistration(names[Depth-1])};
        for(int i=0;i<65;i++)registrations.Add(new GameplayTagRegistration("Seed"+i));
        GameplayTagManager.Initialize(registrations.ToArray());
        var root=GameplayTagManager.RequestTag(names[0]);var tags=new GameplayTag[Depth];for(int i=0;i<Depth;i++)tags[i]=GameplayTagManager.RequestTag(names[i]);
        int cases=0;
        foreach(string history in Histories)foreach(string order in Orders)
        {
            string id=history+"/"+order;cases++;Console.WriteLine("CASE\t"+id);
            var seed=new GameplayTagCountContainer();
            int[] amounts=history=="amount3"?new[]{3}:history=="max-once"?new[]{int.MaxValue}:history=="max-twice"?new[]{int.MaxValue,int.MaxValue}:history=="zero-wrap"?new[]{int.MaxValue,int.MaxValue,2}:new[]{int.MaxValue,int.MaxValue,2,1};
            for(int i=0;i<amounts.Length;i++)
            {
                int amount=amounts[i];Operation(id,"seed-add-root-"+i+"-amount-"+amount,()=>seed.AddTag(root,amount));Observe(id,"seed-after-add-"+i,seed,root);
            }
            if(history=="zero-wrap-readd1-remove1")
            {Operation(id,"seed-remove-root-amount-1",()=>seed.RemoveTag(root,1));Observe(id,"seed-after-remove1",seed,root);}
            for(int i=0;i<65;i++){int n=i;Operation(id,"seed-add-filler-"+n,()=>seed.AddTag(GameplayTagManager.RequestTag("Seed"+n)));}
            var source=new GameplayTagContainer();int[] sequence=SourceOrder(order);Console.WriteLine("ORDER\t"+id+"\t"+string.Join(",",sequence));
            foreach(int index in sequence){int n=index;Operation(id,"source-add-depth-"+n,()=>source.AddTag(tags[n]));}
            Observe(id,"seed-before-union",seed,root);Observe(id,"source-before-union",source,root);
            IGameplayTagContainer view=new NormalView(seed);GameplayTagContainer result=null;
            bool created=Operation(id,"factory-union",()=>result=GameplayTagContainer.Union(view,source));
            if(created)
            {
                Observe(id,"union",result,root);
                for(int i=0;i<3;i++){int n=i;Operation(id,"remove-deepest-"+(i+1),()=>result.RemoveTag(tags[Depth-1-n]));Observe(id,"remove-"+(i+1),result,root);}
            }
            Observe(id,"seed-after-union-and-removals",seed,root);Observe(id,"source-after-union-and-removals",source,root);
        }
        Console.Error.WriteLine("SUMMARY histories=6 sourceOrders=3 cases="+cases+" depth="+Depth+" seedFillers=65");
    }
}
