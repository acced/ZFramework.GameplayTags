using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameplayTags;
internal static class Program
{
 static GameplayTag T(string n)=>GameplayTagManager.RequestTag(n);
 static string Q(string s)=>s==null?"null":"\""+s.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
 static string Snap(GameplayTagContainer c){if(c==null)return "null";var s=c.Indices.Storage;var flags=BindingFlags.Instance|BindingFlags.NonPublic;return c.TagCount+"/"+c.ExplicitTagCount+"/"+string.Join(",",c.GetTags().Select(t=>t.Name))+"/"+string.Join(",",c.GetExplicitTags().Select(t=>t.Name))+"/serialized="+(c.m_SerializedExplicitTags==null?"null":string.Join(",",c.m_SerializedExplicitTags))+"/entries="+string.Join("|",Enumerable.Range(0,s.Count).Select(i=>{var e=s.Entries[i];return e.Id+":"+e.Count+":"+e.ExplicitCount+":"+e.ExplicitIndex;}))+"/indices="+string.Join(",",s.ExplicitIndices.Take(s.ExplicitCount))+"/capacity="+s.Entries.Length+"/"+s.ExplicitIndices.Length+"/"+((int[])typeof(TagStorage).GetField("m_Buckets",flags).GetValue(s)).Length+"/"+typeof(TagStorage).GetField("m_BucketMask",flags).GetValue(s);}
 static IGameplayTagContainer Operand(string kind){
  if(kind=="null")return null;if(kind=="default")return new Proxy(null);if(kind=="fault")return new Proxy(null,true);
  IGameplayTagContainer value=kind.Contains("counted")?(IGameplayTagContainer)new GameplayTagCountContainer():new GameplayTagContainer();
  if(kind!="empty")value.AddTag(T("A.Child"));if(kind.Contains("counted"))((GameplayTagCountContainer)value).AddTag(T("B"),3);
  return kind.StartsWith("proxy")?new Proxy(value):value;
 }
 static void Apply(GameplayTagContainer output,IGameplayTagContainer a,IGameplayTagContainer b){
#if REUSABLE_UNION
  GameplayTagContainer.Union(output,a,b);
#else
  GameplayTagContainer.Copy(output,GameplayTagContainer.Union(a,b));
#endif
 }
 static void Main(){GameplayTagManager.Initialize(new[]{new GameplayTagRegistration("A.Child"),new GameplayTagRegistration("B"),new GameplayTagRegistration("Old")});
  string[] kinds={"empty","set","counted","proxy-set","proxy-counted","default","null","fault"};
  foreach(string x in kinds)foreach(string y in kinds)foreach(string mode in new[]{"distinct","left","right","both","null-output"}){
   var output=mode=="null-output"?null:new GameplayTagContainer(8192);if(output!=null){output.AddTag(T("Old"));output.OnBeforeSerialize();}
   var a=Operand(x);var b=Operand(y);
   if(mode=="left"||mode=="both")a=x.StartsWith("proxy")?new Proxy(output):(IGameplayTagContainer)output;
   if(mode=="right"||mode=="both")b=y.StartsWith("proxy")?new Proxy(output):(IGameplayTagContainer)output;
   string before=Snap(output),error=null;try{Apply(output,a,b);}catch(Exception e){error=e.GetType().FullName;}
   Console.WriteLine("{\"Id\":"+Q(x+"/"+y+"/"+mode)+",\"Before\":"+Q(before)+",\"Exception\":"+Q(error)+",\"After\":"+Q(Snap(output))+"}");
  }
 }
    private sealed class Proxy : IGameplayTagContainer
    {
        private readonly IGameplayTagContainer Inner; private readonly bool Fault;
        internal Proxy(IGameplayTagContainer inner, bool fault = false) { Inner = inner; Fault = fault; }
        public bool IsEmpty => Inner == null || Inner.IsEmpty; public int ExplicitTagCount => Inner == null ? 0 : Inner.ExplicitTagCount; public int TagCount => Inner == null ? 0 : Inner.TagCount; public GameplayTagContainerIndices Indices { get { if(Fault) throw new InvalidOperationException("expected getter fault"); return Inner == null ? default : Inner.Indices; } }
        public void AddTag(GameplayTag tag) => Inner.AddTag(tag); public void RemoveTag(GameplayTag tag) => Inner.RemoveTag(tag);
        public GameplayTagEnumerator GetTags() => Inner.GetTags(); public GameplayTagEnumerator GetExplicitTags() => Inner.GetExplicitTags();
        public void AddTags<T>(in T other) where T : IGameplayTagContainer => Inner.AddTags(other); public void RemoveTags<T>(in T other) where T : IGameplayTagContainer => Inner.RemoveTags(other);
        public void GetParentTags(GameplayTag tag, List<GameplayTag> output) => Inner.GetParentTags(tag,output); public void GetChildTags(GameplayTag tag, List<GameplayTag> output) => Inner.GetChildTags(tag,output);
        public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> output) => Inner.GetExplicitParentTags(tag,output); public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> output) => Inner.GetExplicitChildTags(tag,output);
        public void Clear() => Inner.Clear(); public IEnumerator<GameplayTag> GetEnumerator() => Inner.GetEnumerator(); IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
