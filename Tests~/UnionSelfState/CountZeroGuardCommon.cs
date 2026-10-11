using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using GameplayTags;

internal static class Shared
{
    internal static GameplayTag[] Flat;
    internal static GameplayTag Leaf;
    internal static string[] Registry;
    internal static readonly FieldInfo BucketsField=typeof(TagStorage).GetField("m_Buckets",BindingFlags.Instance|BindingFlags.NonPublic);
    internal static readonly FieldInfo MaskField=typeof(TagStorage).GetField("m_BucketMask",BindingFlags.Instance|BindingFlags.NonPublic);
    internal static string Q(string s) => s == null ? "null" : "\""+s.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n").Replace("\r","\\r")+"\"";
    internal static string Capture(Func<string> f) {try{return f();}catch(Exception ex){return "THROW:"+ex.GetType().FullName;}}
    internal static string Attempt(Action f) {try{f();return null;}catch(Exception ex){return ex.GetType().FullName;}}
    internal static void Initialize()
    {
        Registry=Enumerable.Range(0,1024).Select(i=>"F"+i).Concat(new[]{"A","A.Child","B","B.C","H","H.Branch","H.Branch.Leaf"}).ToArray();
        GameplayTagManager.Initialize(Registry.Select(n=>new GameplayTagRegistration(n)).ToArray());
        Flat=Enumerable.Range(0,1024).Select(i=>GameplayTagManager.RequestTag("F"+i)).ToArray();
        Leaf=GameplayTagManager.RequestTag("H.Branch.Leaf");
    }
    internal static GameplayTag T(string n)=>GameplayTagManager.RequestTag(n);
    internal static GameplayTagContainer Normal(int n,string history,bool leaf=false)
    {
        var o=new GameplayTagContainer();
        if(history=="reserved8192")o.EnsureCapacity(8192);
        if(history=="grown1024-drained")
        {
            for(int i=0;i<1024;i++)o.AddTag(Flat[i]);
            for(int i=1023;i>=(leaf?0:n);i--)o.RemoveTag(Flat[i]);
        }
        else if(history!="fresh"&&history!="reserved8192")throw new Exception("history");
        if(leaf)o.AddTag(Leaf); else for(int i=0;i<n;i++)o.AddTag(Flat[i]);
        return o;
    }
    internal static GameplayTagCountContainer Counted(string state)
    {
        var o=new GameplayTagCountContainer();var a=T("A.Child");
        if(state.StartsWith("healthy-before",StringComparison.Ordinal))o.AddTag(T("B.C"),3);
        if(state.StartsWith("many-healthy",StringComparison.Ordinal))for(int i=0;i<9;i++)o.AddTag(Flat[i],3);
        o.AddTag(a,int.MaxValue);
        if(state!="max"){o.AddTag(a,int.MaxValue);o.AddTag(a,2);}
        if(state.Contains("readd"))o.AddTag(a,1);
        if(state.Contains("remove"))o.RemoveTag(a,1);
        if(state=="zero-healthy-after")o.AddTag(T("B.C"),3);
        return o;
    }
    internal static GameplayTagContainer Legacy(string state,string history)
    {
        var o=Normal(0,history);IGameplayTagContainer view=new NormalView(Counted("zero"));
        GameplayTagContainer.Copy(o,view); o.AddTag(T("A.Child"));o.RemoveTag(T("A.Child"));
        if(o.TagCount!=0||o.ExplicitTagCount!=1)throw new Exception("Public history did not reach Count=0/global ExplicitCount=1");return o;
    }
    internal static IGameplayTagContainer[] Views(GameplayTagContainer o,string kind)
    {
        string[] k=kind.Split('-');return new[]{View(o,k[0]),View(o,k[1])};
    }
    internal static IGameplayTagContainer View(IGameplayTagContainer o,string kind)=>kind=="normal"?o:kind=="proxy"?(IGameplayTagContainer)new Proxy(o):new NormalView(o);
    internal static string Enumerate(IGameplayTagContainer o,bool exact)
    {
        var names=new List<string>();var e=exact?o.GetExplicitTags():o.GetTags();
        while(e.MoveNext()){if(names.Count>2048)return "ENUMERATION-LIMIT";names.Add(e.Current.Name??"<None>");}
        return "["+string.Join(",",names.Select(Q))+"]";
    }
    internal static string Public(GameplayTagContainer o)
    {
        var l=new List<string>{"empty="+Capture(()=>o.IsEmpty.ToString()),"total="+Capture(()=>o.TagCount.ToString()),"explicit="+Capture(()=>o.ExplicitTagCount.ToString()),"all="+Capture(()=>Enumerate(o,false)),"exact="+Capture(()=>Enumerate(o,true))};
        foreach(var t in Flat.Take(70).Concat(new[]{T("A"),T("A.Child"),T("B"),T("B.C"),T("H"),T("H.Branch"),Leaf,GameplayTag.None})) l.Add((t.Name??"None")+"="+Capture(()=>o.HasTag(t)+"/"+o.HasTagExact(t)));
        l.Add("serialized="+(o.m_SerializedExplicitTags==null?"null":"["+string.Join(",",o.m_SerializedExplicitTags.Select(Q))+"]"));
        return string.Join(";",l);
    }
    internal sealed class Snap
    {
        internal int Count,ExplicitCount,Mask;internal int[] Entries,Explicit,Buckets;internal string PublicValue;
        internal Snap(GameplayTagContainer o,bool includePublic=true)
        {
            var s=o.Indices.Storage;Count=s.Count;ExplicitCount=s.ExplicitCount;Mask=(int)MaskField.GetValue(s);
            Entries=new int[s.Entries.Length*4];for(int i=0;i<s.Entries.Length;i++){var e=s.Entries[i];Entries[4*i]=e.Id;Entries[4*i+1]=e.Count;Entries[4*i+2]=e.ExplicitCount;Entries[4*i+3]=e.ExplicitIndex;}
            Explicit=(int[])s.ExplicitIndices.Clone();Buckets=(int[])((int[])BucketsField.GetValue(s)).Clone();PublicValue=includePublic?Public(o):null;
        }
        internal static string Hash(int[] a){using(var sha=SHA256.Create()){var b=new byte[a.Length*4];Buffer.BlockCopy(a,0,b,0,b.Length);return BitConverter.ToString(sha.ComputeHash(b)).Replace("-","").ToLowerInvariant();}}
        internal string Json(bool full=false)=>"{\"Count\":"+Count+",\"ExplicitCount\":"+ExplicitCount+",\"EntriesCapacity\":"+(Entries.Length/4)+",\"ExplicitCapacity\":"+Explicit.Length+",\"BucketsCapacity\":"+Buckets.Length+",\"Mask\":"+Mask+",\"EntriesSha256\":"+Q(Hash(Entries))+",\"ExplicitSha256\":"+Q(Hash(Explicit))+",\"BucketsSha256\":"+Q(Hash(Buckets))+",\"Public\":"+Q(PublicValue)+(full?",\"AllEntryFields\":["+string.Join(",",Entries)+"],\"AllExplicitIndices\":["+string.Join(",",Explicit)+"],\"AllBuckets\":["+string.Join(",",Buckets)+"]":"")+"}";
        internal string Difference(Snap b)
        {
            if(Count!=b.Count||ExplicitCount!=b.ExplicitCount||Mask!=b.Mask)return "Count/ExplicitCount/Mask";
            if(PublicValue!=b.PublicValue)return "Public";
            if(!Entries.SequenceEqual(b.Entries))return "ALL Entry fields";
            if(!Explicit.SequenceEqual(b.Explicit))return "ALL ExplicitIndices";
            if(!Buckets.SequenceEqual(b.Buckets))return "ALL buckets";
            return null;
        }
    }
    internal sealed class NormalView:GameplayTagContainer,IGameplayTagContainer
    {
        readonly IGameplayTagContainer inner; internal NormalView(IGameplayTagContainer i){inner=i;}
        GameplayTagContainerIndices IGameplayTagContainer.Indices=>inner.Indices;
    }
    internal sealed class Proxy:IGameplayTagContainer
    {
        readonly IGameplayTagContainer inner;internal Proxy(IGameplayTagContainer i){inner=i;}
        public bool IsEmpty=>inner.IsEmpty;public int ExplicitTagCount=>inner.ExplicitTagCount;public int TagCount=>inner.TagCount;public GameplayTagContainerIndices Indices=>inner.Indices;
        public void AddTag(GameplayTag t)=>inner.AddTag(t);public void RemoveTag(GameplayTag t)=>inner.RemoveTag(t);public GameplayTagEnumerator GetTags()=>inner.GetTags();public GameplayTagEnumerator GetExplicitTags()=>inner.GetExplicitTags();
        public void AddTags<T>(in T o)where T:IGameplayTagContainer=>inner.AddTags(o);public void RemoveTags<T>(in T o)where T:IGameplayTagContainer=>inner.RemoveTags(o);
        public void GetParentTags(GameplayTag t,List<GameplayTag>o)=>inner.GetParentTags(t,o);public void GetChildTags(GameplayTag t,List<GameplayTag>o)=>inner.GetChildTags(t,o);public void GetExplicitParentTags(GameplayTag t,List<GameplayTag>o)=>inner.GetExplicitParentTags(t,o);public void GetExplicitChildTags(GameplayTag t,List<GameplayTag>o)=>inner.GetExplicitChildTags(t,o);public void Clear()=>inner.Clear();public IEnumerator<GameplayTag> GetEnumerator()=>inner.GetEnumerator();IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }
}
