using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameplayTags;
internal static class Program
{
 static GameplayTag[] All;
 static string Deep;
 static GameplayTag T(string n)=>GameplayTagManager.RequestTag(n);
 static string Q(string s)=>"\""+s.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
 static string Snap(IGameplayTagContainer c){var s=c.Indices.Storage;var flags=BindingFlags.Instance|BindingFlags.NonPublic;var buckets=(int[])typeof(TagStorage).GetField("m_Buckets",flags).GetValue(s);int mask=(int)typeof(TagStorage).GetField("m_BucketMask",flags).GetValue(s);return c.TagCount+"/"+c.ExplicitTagCount+"/"+string.Join(",",c.GetTags().Select(t=>t.Name))+"/"+string.Join(",",c.GetExplicitTags().Select(t=>t.Name))+"/serialized="+(c is GameplayTagContainer set?(set.m_SerializedExplicitTags==null?"null":string.Join(",",set.m_SerializedExplicitTags)):"counted")+"/entries="+string.Join("|",Enumerable.Range(0,s.Count).Select(i=>{var e=s.Entries[i];return e.Id+":"+e.Count+":"+e.ExplicitCount+":"+e.ExplicitIndex;}))+"/indices="+string.Join(",",s.ExplicitIndices.Take(s.ExplicitCount))+"/capacity="+s.Entries.Length+"/"+s.ExplicitIndices.Length+"/"+buckets.Length+"/"+mask+"/activeBuckets="+string.Join(",",buckets.Take(mask==0?0:mask+1));}
 static void Prepare(GameplayTagContainer c,string mode){if(mode=="reserved")c.EnsureCapacity(8192);if(mode=="grow-drain"||mode=="cleared"){for(int i=0;i<1024;i++)c.AddTag(T("R"+i));if(mode=="cleared")c.Clear();else for(int i=0;i<1024;i++)c.RemoveTag(T("R"+i));}}
 static GameplayTagContainer Set(string name,bool sparse){var c=new GameplayTagContainer();if(sparse)Prepare(c,"grow-drain");c.AddTag(T(name));return c;}
 static GameplayTagCountContainer Counted(string name){var c=new GameplayTagCountContainer();c.AddTag(T(name),3);return c;}
 static void Validate(GameplayTagContainer c,HashSet<string> expected){var all=new HashSet<string>(expected,StringComparer.Ordinal);foreach(string name in expected)for(int j=0;j<name.Length;j++)if(name[j]=='.')all.Add(name.Substring(0,j));if(c.ExplicitTagCount!=expected.Count||c.TagCount!=all.Count)throw new Exception("cardinality");foreach(var tag in All){if(c.HasTag(tag)!=all.Contains(tag.Name)||c.HasTagExact(tag)!=expected.Contains(tag.Name))throw new Exception("hit/miss");}}
 static void Apply(GameplayTagContainer output,IGameplayTagContainer a,IGameplayTagContainer b){
#if REUSABLE_UNION
  GameplayTagContainer.Union(output,a,b);
#else
  GameplayTagContainer.Copy(output,GameplayTagContainer.Union(a,b));
#endif
 }
 static void Main(){Deep="Deep"+string.Concat(Enumerable.Repeat(".N",16));GameplayTagManager.Initialize(Enumerable.Range(0,1024).Select(i=>"R"+i).Concat(new[]{"A.Child","B.Child","Old",Deep}).Select(n=>new GameplayTagRegistration(n)).ToArray());All=GameplayTagManager.GetAllTags().ToArray();
  foreach(string shape in new[]{"normal","proxy","counted","shared-set","shared-set-proxy","shared-proxy-set","shared-proxies","shared-counted","shared-counted-proxy","tie-asymmetric","prefer-set","prefer-set-reverse","empty"})
  foreach(string mode in new[]{"fresh","reserved","grow-drain","cleared"})foreach(bool sparse in new[]{false,true})foreach(bool serialized in new[]{false,true}){
   var sa=Set("A.Child",sparse);var sb=Set("B.Child",sparse);IGameplayTagContainer a=sa,b=sb;
   switch(shape){case "proxy":a=new Proxy(sa);b=new Proxy(sb);break;case "counted":a=Counted("A.Child");b=Counted("B.Child");break;case "shared-set":b=a;break;case "shared-set-proxy":b=new Proxy(a);break;case "shared-proxy-set":b=a;a=new Proxy(a);break;case "shared-proxies":b=new Proxy(a);a=new Proxy(a);break;case "shared-counted":a=Counted("A.Child");b=a;break;case "shared-counted-proxy":a=Counted("A.Child");b=new Proxy(a);break;case "tie-asymmetric":sa.AddTag(T("A"));break;case "prefer-set":a=Counted(Deep);break;case "prefer-set-reverse":b=Counted(Deep);break;case "empty":a=new GameplayTagContainer();b=new GameplayTagContainer();break;}
   var output=new GameplayTagContainer();Prepare(output,mode);output.AddTag(T("Old"));if(serialized)output.OnBeforeSerialize();var expected=new HashSet<string>(a.GetExplicitTags().Select(t=>t.Name).Concat(b.GetExplicitTags().Select(t=>t.Name)),StringComparer.Ordinal);string beforeA=Snap(a),beforeB=Snap(b),before=Snap(output);
   Apply(output,a,b);Validate(output,expected);if(beforeA!=Snap(a)||beforeB!=Snap(b))throw new Exception("source changed");if(serialized&&output.m_SerializedExplicitTags.Count!=0)throw new Exception("cache not cleared");if(!serialized&&output.m_SerializedExplicitTags!=null)throw new Exception("null cache changed");string after=Snap(output);
   output.Clear();Validate(output,new HashSet<string>());string cleared=Snap(output);expected.Clear();for(int i=0;i<129;i++){output.AddTag(T("R"+i));expected.Add("R"+i);Validate(output,expected);}string regrown=Snap(output);for(int i=0;i<129;i++){output.RemoveTag(T("R"+i));expected.Remove("R"+i);Validate(output,expected);}string drained=Snap(output);
   Console.WriteLine("{\"Id\":"+Q(shape+"/"+mode+"/"+sparse+"/"+serialized)+",\"Before\":"+Q(before)+",\"After\":"+Q(after)+",\"Cleared\":"+Q(cleared)+",\"Regrown\":"+Q(regrown)+",\"Drained\":"+Q(drained)+"}");
  }
 }
    private sealed class Proxy : IGameplayTagContainer
    {
        private readonly IGameplayTagContainer Inner;
        internal Proxy(IGameplayTagContainer inner) { Inner = inner; }
        public bool IsEmpty => Inner.IsEmpty; public int ExplicitTagCount => Inner.ExplicitTagCount; public int TagCount => Inner.TagCount; public GameplayTagContainerIndices Indices => Inner.Indices;
        public void AddTag(GameplayTag tag) => Inner.AddTag(tag); public void RemoveTag(GameplayTag tag) => Inner.RemoveTag(tag);
        public GameplayTagEnumerator GetTags() => Inner.GetTags(); public GameplayTagEnumerator GetExplicitTags() => Inner.GetExplicitTags();
        public void AddTags<T>(in T other) where T : IGameplayTagContainer => Inner.AddTags(other); public void RemoveTags<T>(in T other) where T : IGameplayTagContainer => Inner.RemoveTags(other);
        public void GetParentTags(GameplayTag tag, List<GameplayTag> output) => Inner.GetParentTags(tag,output); public void GetChildTags(GameplayTag tag, List<GameplayTag> output) => Inner.GetChildTags(tag,output);
        public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> output) => Inner.GetExplicitParentTags(tag,output); public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> output) => Inner.GetExplicitChildTags(tag,output);
        public void Clear() => Inner.Clear(); public IEnumerator<GameplayTag> GetEnumerator() => Inner.GetEnumerator(); IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
