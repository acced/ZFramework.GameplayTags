using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using GameplayTags;
using UnityEngine;

// Shared by B0 and current runtime. Reflection is limited to serialized fields,
// whose names/types are the Editor/asset contract, not runtime implementation.
internal static class Program {
 static int assertions;
 static readonly StringBuilder transcript = new StringBuilder();
 static readonly string[] universe = { "A", "A!", "A-.X", "A.B", "A.B.C", "A.BB", "A.Z", "B", "B.Child", "C.X.Y", "State.Burning", "State.Frozen", "Ω.火" };
 static void Check(bool condition, string why) { assertions++; if (!condition) throw new Exception(why); }
 static void Equal<T>(T actual,T expected,string why) { Check(EqualityComparer<T>.Default.Equals(actual,expected),why+": actual="+actual+" expected="+expected); }
 static GameplayTag Raw(string s) { object boxed = default(GameplayTag); Field(typeof(GameplayTag),"m_Name").SetValue(boxed,s); return (GameplayTag)boxed; }
 static FieldInfo Field(Type t,string name) { var f=t.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic); Check(f!=null,"serialized field "+name); Check(Attribute.IsDefined(f,typeof(SerializeField)),"SerializeField "+name); return f; }
 static GameplayTagContainer Serialized(params string[] names) { var c=new GameplayTagContainer(); Field(typeof(GameplayTagContainer),"m_GameplayTags").SetValue(c,names==null?null:names.Select(Raw).ToList()); ((ISerializationCallbackReceiver)c).OnAfterDeserialize(); return c; }
 static GameplayTagSettings Settings(IEnumerable<string> names, params GameplayTagRedirect[] redirects) { var s=ScriptableObject.CreateInstance<GameplayTagSettings>(); s.ReplaceAll(names.Select(n=>new GameplayTagDefinition(n,"","",false,true)).ToList(),redirects.ToList(),new List<GameplayTagSource>()); return s; }
 static void Init(IEnumerable<string> names=null, params GameplayTagRedirect[] redirects) => GameplayTagManager.Initialize(Settings(names??universe,redirects),true);
 static string[] Names(GameplayTagContainer c) { var a=new List<string>(); foreach(var t in c) a.Add(t.Name); return a.ToArray(); }
 static SortedSet<string> Set(IEnumerable<string> s) => new SortedSet<string>(s,StringComparer.Ordinal);
 static string[] Registered(IEnumerable<string> s) => s.Where(GameplayTagManager.IsRegistered).Distinct(StringComparer.Ordinal).OrderBy(n=>n,StringComparer.Ordinal).ToArray();
 static void State(GameplayTagContainer c,IEnumerable<string> expected,string why) {
  var e=Set(expected).ToArray(); var actual=Names(c); Equal(string.Join("|",actual),string.Join("|",e),why); Equal(c.Count,e.Length,why+" count"); Equal(c.IsEmpty,e.Length==0,why+" empty");
  int hash=17; for(int i=0;i<e.Length;i++) { Equal(c[i].Name,e[i],why+" index"); Equal(c.GetTagAt(i).Name,e[i],why+" GetTagAt"); unchecked { hash=hash*31+Raw(e[i]).GetHashCode(); } }
  Equal(c.GetHashCode(),hash,why+" legacy hash"); Equal(c.ToString(),"{"+string.Join(", ",e)+"}",why+" text");
  var reverse=Serialized(e.Reverse().ToArray()); Check(c.Equals(reverse)&&c.Equals((object)reverse),why+" equality"); Check(!c.Equals((GameplayTagContainer)null),why+" null equality");
  transcript.Append(why).Append(':').AppendJoin('|',actual).AppendLine();
 }
 static void SetUnion(GameplayTagContainer d,GameplayTagContainer a,GameplayTagContainer b) {
#if PHASE_APIS
  d.SetUnion(a,b);
#else
  var r=GameplayTagContainer.Union(a,b); d.Clear(); Field(typeof(GameplayTagContainer),"m_GameplayTags").SetValue(d,Names(r).Select(Raw).ToList());
#endif
 }
 static void SetIntersection(GameplayTagContainer d,GameplayTagContainer a,GameplayTagContainer b) {
#if PHASE_APIS
  d.SetIntersectionExact(a,b);
#else
  var r=GameplayTagContainer.IntersectionExact(a,b); Field(typeof(GameplayTagContainer),"m_GameplayTags").SetValue(d,Names(r).Select(Raw).ToList());
#endif
 }
 static void Intersect(GameplayTagContainer d,GameplayTagContainer b) {
#if PHASE_APIS
  d.IntersectWithExact(b);
#else
  SetIntersection(d,d,b);
#endif
 }
 static void Except(GameplayTagContainer d,GameplayTagContainer b) {
#if PHASE_APIS
  d.ExceptWithExact(b);
#else
  d.RemoveTags(b);
#endif
 }
 static bool Hierarchy(string child,string parent) => parent.Length>0 && (child==parent || child.StartsWith(parent+".",StringComparison.Ordinal));
 static void Serialization() {
  GameplayTagManager.ResetForTests(); Resources.LoadCalls=0;
  var c=Serialized("Unknown.Child","A.B","",null,"Unknown.Child","A!","A"); ((ISerializationCallbackReceiver)c).OnBeforeSerialize();
  Check(!GameplayTagManager.IsInitialized,"serialization must not initialize registry"); Equal(Resources.LoadCalls,0,"serialization resources calls");
  State(c,new[]{"A","A!","A.B","Unknown.Child"},"serialization normalization");
  State(Serialized((string[])null),Array.Empty<string>(),"null serialized list");
  Check(!GameplayTagManager.IsInitialized,"read-only identity operations must not initialize registry");
  Init(); State(new GameplayTagContainer(c),new[]{"A","A!","A.B"},"copy constructor revalidates");
  var copy=Serialized("B");copy.CopyFrom(c); State(copy,new[]{"A","A!","A.B"},"copy revalidates");
  c.CopyFrom(c); State(c,new[]{"A","A!","A.B","Unknown.Child"},"self copy preserves");
  c.AppendTags(c); State(c,new[]{"A","A!","A.B","Unknown.Child"},"self append preserves");
  var other=Serialized("Unknown.Child","State.Frozen"); State(GameplayTagContainer.IntersectionExact(c,other),new[]{"Unknown.Child"},"intersection preserves unknown"); State(c.FilterExact(other),new[]{"Unknown.Child"},"filter preserves unknown");
  State(GameplayTagContainer.Union(c,other),new[]{"A","A!","A.B","State.Frozen"},"union revalidates");
  var dest=Serialized("Other.Unknown");dest.AppendTags(c);State(dest,new[]{"Other.Unknown","A","A!","A.B"},"append keeps target unknown");
  Check(c.HasTagExact(Raw("Unknown.Child")),"unknown exact");Check(c.HasTag(Raw("Unknown")),"unknown hierarchy");Check(c.RemoveTag(Raw("Unknown.Child")),"unknown removal");
 }
 static void IdentityAndRebuild() {
  Init(); var old=GameplayTag.Parse("A.B");var removed=GameplayTag.Parse("B.Child"); var c=new GameplayTagContainer(old); c.AddTag(removed); int hash=c.GetHashCode();
  Init(new[]{"0.Before","A.B","B.Other"},new GameplayTagRedirect("B.Child","B.Other"));
  Equal(old,GameplayTag.Parse("A.B"),"old handle identity"); Equal(old.GetHashCode(),GameplayTag.Parse("A.B").GetHashCode(),"old hash"); Check(old.MatchesTag(GameplayTag.Parse("A")),"old hierarchy");
  State(c,new[]{"A.B","B.Child"},"rebuild existing names"); Equal(c.GetHashCode(),hash,"rebuild hash stable"); Check(c.HasTagExact(removed),"removed old exact");Check(!c.HasTagExact(GameplayTag.Parse("B.Other")),"no blanket redirect");
  Equal(GameplayTag.Parse("B.Child").Name,"B.Other","request redirect");Check(!new GameplayTagContainer().AddTag(removed),"old unregistered handle rejected"); State(new GameplayTagContainer(c),new[]{"A.B"},"rebuild copy revalidates");
  var redirected=new GameplayTagContainer();Check(redirected.AddTag("B.Child"),"string add follows redirect");State(redirected,new[]{"B.Other"},"redirect string add");
  Init(new[]{"0.Before","A.B","B.Child"});Check(new GameplayTagContainer().AddTag(removed),"old handle reappears"); Init();
  foreach(var a in universe) foreach(var b in universe) { var x=GameplayTag.Parse(a);var y=GameplayTag.Parse(b); Equal(x.MatchesTag(y),Hierarchy(a,b),"pair hierarchy "+a+" "+b);Equal(Math.Sign(x.CompareTo(y)),Math.Sign(string.CompareOrdinal(a,b)),"ordinal");Equal(x.Equals(y),a==b,"tag equality"); }
  State(Serialized("A!","A.B"),new[]{"A!","A.B"},"ordinal interleave");Check(Serialized("A!","A.B").HasTag(Raw("A")),"skip punctuation nonchild");Check(!Serialized("A!").HasTag(Raw("A")),"punctuation not child");
 }
 static void Aliases() {
  Init(); string[][] inputs={Array.Empty<string>(),new[]{"A"},new[]{"A","A.B","Unknown"},new[]{"B","Unknown"},new[]{"Unknown"}};
  foreach(var av in inputs) foreach(var bv in inputs) for(int mode=0;mode<4;mode++) {
   var a=Serialized(av);var b=mode==3?a:Serialized(bv);var aa=Names(a);var bb=Names(b);var d=mode==0?Serialized("State.Frozen"):mode==2?b:a;
   SetUnion(d,a,b);State(d,Registered(aa.Concat(bb)),"alias union "+mode);
   a=Serialized(av);b=mode==3?a:Serialized(bv);aa=Names(a);bb=Names(b);d=mode==0?Serialized("State.Frozen"):mode==2?b:a;
   SetIntersection(d,a,b);State(d,aa.Intersect(bb,StringComparer.Ordinal),"alias intersection "+mode);
   a=Serialized(av);b=mode==3?a:Serialized(bv);aa=Names(a);bb=Names(b);Except(a,b);State(a,aa.Except(bb,StringComparer.Ordinal),"alias except "+mode);
   a=Serialized(av);b=mode==3?a:Serialized(bv);aa=Names(a);bb=Names(b);Intersect(a,b);State(a,aa.Intersect(bb,StringComparer.Ordinal),"alias intersect with "+mode);
  }
  var x=Serialized("A","Unknown");SetUnion(x,x,null);State(x,new[]{"A"},"null union");SetIntersection(x,x,null);State(x,Array.Empty<string>(),"null intersection");x.CopyFrom(null);Check(!x.RemoveTags(null),"null remove");Check(!x.HasAny(null)&&!x.HasAnyExact(null)&&x.HasAll(null)&&x.HasAllExact(null),"null query semantics");
#if PHASE_APIS
  x.EnsureCapacity(128);
#endif
  State(x,Array.Empty<string>(),"capacity empty"); x.AddTag("A");
#if PHASE_APIS
  x.EnsureCapacity(0);
#endif
  State(x,new[]{"A"},"capacity retain");
 }
 static void SerializationRoundTrip() {
  Init(); var c=new GameplayTagContainer();c.AddTag("B");c.AddTag("A.B");c.RemoveTag(Raw("B"));c.AddTag("State.Frozen");
  ((ISerializationCallbackReceiver)c).OnBeforeSerialize();
  var saved=(List<GameplayTag>)Field(typeof(GameplayTagContainer),"m_GameplayTags").GetValue(c);
  Equal(string.Join("|",saved.Select(t=>t.Name)),"A.B|State.Frozen","serialized mirror after mutation");
  Field(typeof(GameplayTagContainer),"m_GameplayTags").SetValue(c,new List<GameplayTag>{Raw("Unknown"),Raw("A!"),Raw("A!"),default(GameplayTag)});
  ((ISerializationCallbackReceiver)c).OnAfterDeserialize();State(c,new[]{"A!","Unknown"},"editor replacement invalidates runtime state");
  Check(!c.HasTagExact(Raw("A.B")),"editor removed stale member");
 }
 static void Throws<T>(Action action,string why) where T:Exception { try { action(); } catch(Exception ex) { Equal(ex.GetType(),typeof(T),why+" exception type"); return; } throw new Exception(why+" did not throw"); }
 static void UninitializedAndExceptions() {
  Init();var c=new GameplayTagContainer();c.AddTag("A.B");c.AddTag("B.Child");var a=GameplayTag.Parse("A");var ab=GameplayTag.Parse("A.B");var b=GameplayTag.Parse("B.Child");
  Check(c.HasTagExact(ab),"warm bound exact");GameplayTagManager.ResetForTests();Resources.LoadCalls=0;
  Check(c.HasTagExact(ab),"reset bound exact");Check(c.HasTag(a),"reset bound hierarchy");Check(c.RemoveTag(b),"reset bound remove");State(c,new[]{"A.B"},"reset bound names preserved");Check(!GameplayTagManager.IsInitialized&&Resources.LoadCalls==0,"reset queries and remove noninitializing");
  ((ISerializationCallbackReceiver)c).OnBeforeSerialize();((ISerializationCallbackReceiver)c).OnBeforeSerialize();((ISerializationCallbackReceiver)c).OnAfterDeserialize();((ISerializationCallbackReceiver)c).OnAfterDeserialize();State(c,new[]{"A.B"},"repeated callbacks preserve names");Check(!GameplayTagManager.IsInitialized&&Resources.LoadCalls==0,"repeated callbacks noninitializing");
  Init();c.AddTag("State.Frozen");((ISerializationCallbackReceiver)c).OnBeforeSerialize();c.Clear();((ISerializationCallbackReceiver)c).OnBeforeSerialize();((ISerializationCallbackReceiver)c).OnBeforeSerialize();
  Equal(((List<GameplayTag>)Field(typeof(GameplayTagContainer),"m_GameplayTags").GetValue(c)).Count,0,"clear serialized mirror releases names");((ISerializationCallbackReceiver)c).OnAfterDeserialize();State(c,Array.Empty<string>(),"clear callback remains empty");
  foreach(var value in new[]{c,Serialized("A.B"),new GameplayTagContainer(GameplayTag.Parse("A.B"))}) {
   value.HasTagExact(ab);Throws<ArgumentOutOfRangeException>(()=>{var ignored=value[-1];},"negative index");Throws<ArgumentOutOfRangeException>(()=>{var ignored=value[value.Count];},"past end index");Throws<ArgumentOutOfRangeException>(()=>value.GetTagAt(-1),"negative GetTagAt");Throws<ArgumentOutOfRangeException>(()=>value.GetTagAt(value.Count),"past end GetTagAt");
   var enumerator=value.GetEnumerator();Throws<ArgumentOutOfRangeException>(()=>{var ignored=enumerator.Current;},"enumerator before first");while(enumerator.MoveNext()){}Throws<ArgumentOutOfRangeException>(()=>{var ignored=enumerator.Current;},"enumerator after end");
  }
  var emptyEnumerator=default(GameplayTagContainer.Enumerator);Throws<NullReferenceException>(()=>emptyEnumerator.MoveNext(),"default enumerator MoveNext");Throws<NullReferenceException>(()=>{var ignored=emptyEnumerator.Current;},"default enumerator Current");
 }
 sealed class WarningFailure : Exception {}
 static void ObservableCallbacks() {
  Init();var source=new GameplayTagContainer(GameplayTag.Parse("A"));var target=new GameplayTagContainer(GameplayTag.Parse("B"));GameplayTagManager.ResetForTests();int seen=-1;Action observer=()=>seen=target.Count;GameplayTagManager.RegistryChanged+=observer;
  target.CopyFrom(source);Equal(seen,0,"CopyFrom clears target before registry initialization event");State(target,Array.Empty<string>(),"copy empty default registry");GameplayTagManager.RegistryChanged-=observer;
  Init();source=Serialized("A","B.Unknown","State.Frozen");target=Serialized("B");int warningCalls=0;
  Debug.WarningHook=message=>{if(message.ToString().Contains("B.Unknown")){warningCalls++;State(target,new[]{"A","B"},"append callback observes sorted partial state");}};
  try{target.AppendTags(source);}finally{Debug.WarningHook=null;}Equal(warningCalls,1,"append unknown warning once");State(target,new[]{"A","B","State.Frozen"},"append after warning");
  Init();target=Serialized("B");Debug.WarningHook=message=>{if(message.ToString().Contains("B.Unknown")){State(target,new[]{"A","B"},"append throwing callback partial state");throw new WarningFailure();}};
  try{Throws<WarningFailure>(()=>target.AppendTags(source),"append warning propagates exception");}finally{Debug.WarningHook=null;}State(target,new[]{"A","B"},"append warning throw retains preceding insertion");
 }
 static void MutatingCallbacks() {
  Init(new[]{"A","Z","ZZ"});var source=Serialized("A","U","Z");var target=Serialized("Z");int count=0;
  Debug.WarningHook=message=>{if(message.ToString().Contains(": U")){count++;target.AddTag("ZZ");}};
  try{target.CopyFrom(source);}finally{Debug.WarningHook=null;}Equal(count,1,"copy mutating warning observed");State(target,new[]{"A","Z","ZZ"},"copy warning target mutation retains ordering");
  Init(new[]{"A","Z","ZZ"});source=Serialized("A","U","Z");count=0;
  Debug.WarningHook=message=>{if(message.ToString().Contains(": U")){count++;source.RemoveTag(Raw("U"));source.AddTag("ZZ");}};
  GameplayTagContainer union;try{union=GameplayTagContainer.Union(source,null);}finally{Debug.WarningHook=null;}Equal(count,1,"union mutating warning observed");State(union,new[]{"A","ZZ"},"union warning follows live source iteration");State(source,new[]{"A","Z","ZZ"},"union warning mutated source");
  source=Serialized("A");GameplayTagManager.ResetForTests();Resources.DefaultAsset=Settings(new[]{"A"});count=0;Action clear=()=>{count++;source.Clear();};GameplayTagManager.RegistryChanged+=clear;
  try{union=GameplayTagContainer.Union(source,null);}finally{GameplayTagManager.RegistryChanged-=clear;Resources.DefaultAsset=null;}Equal(count,1,"union initialization observer called");State(union,new[]{"A"},"union initialization retains already captured argument");State(source,Array.Empty<string>(),"union initialization clears live source");
 }
 static void LiveEnumeration() {
  for(int mode=0;mode<2;mode++) {
   Init();var c=mode==0?Serialized("A","B","State.Frozen"):new GameplayTagContainer();if(mode==1){c.AddTag("A");c.AddTag("B");c.AddTag("State.Frozen");}c.HasTagExact(Raw("B"));
   var e=c.GetEnumerator();Check(e.MoveNext(),"live first move");Equal(e.Current.Name,"A","live first current");c.AddTag("A.B");Equal(e.Current.Name,"A","live add after current");c.RemoveTag(Raw("A"));Equal(e.Current.Name,"A.B","live remove shifts current");
   Check(e.MoveNext(),"live next move");Equal(e.Current.Name,"B","live second current");c.AddTag("A");Equal(e.Current.Name,"A.B","live insert shifts current");
   Init(new[]{"0.Before","A.B","B.Other","State.Frozen"});Equal(e.Current.Name,"A.B","live rebuild preserves current identity");c.RemoveTag(Raw("A.B"));Equal(e.Current.Name,"B","live remove after rebuild");
   c.Clear();Throws<ArgumentOutOfRangeException>(()=>{var ignored=e.Current;},"live clear current throws");Check(!e.MoveNext(),"live clear MoveNext observes count");
   Init();foreach(var n in new[]{"A","A.B","B","State.Frozen"})c.AddTag(n);Check(e.MoveNext(),"live enumeration resumes after repopulate");Equal(e.Current.Name,"State.Frozen","live resumed current index");Check(!e.MoveNext(),"live end");Throws<ArgumentOutOfRangeException>(()=>{var ignored=e.Current;},"live end current throws");State(c,new[]{"A","A.B","B","State.Frozen"},"live enum final "+mode);
  }
 }
 static void Queries() {
  Init();var c=Serialized("A.B");var leaf=GameplayTagQueryExpression.AnyTagsMatch().AddTag(GameplayTag.Parse("A"));var root=GameplayTagQueryExpression.AllExpressionsMatch().AddExpression(leaf);var q=new GameplayTagQuery(root);
  Check(q.Matches(c)&&c.MatchesQuery(q),"initial mutable query");leaf.Tags.Clear();Check(!q.Matches(c),"mutated child tags immediate");leaf.AddTag(GameplayTag.Parse("B"));Check(!q.Matches(c),"mutated child tag absent");leaf.SetType(GameplayTagQueryExpressionType.NoTagsMatch);Check(q.Matches(c),"mutated type immediate");root.AddExpression(GameplayTagQueryExpression.AnyTagsMatch());Check(!q.Matches(c),"new child immediate");q.SetRoot(leaf,"description");Check(q.Matches(c),"root replaced");Equal(q.ToString(),"description","description");q.Clear();Check(q.IsEmpty&&!q.Matches(c)&&!c.MatchesQuery(null),"query clear");
 }
 static void LargeAndSkewed() {
  var registered=Enumerable.Range(0,1100).Select(i=>"Bulk.T"+i.ToString("D4")).ToArray();Init(registered);
  var a=new GameplayTagContainer();foreach(var n in registered.Take(1024).Reverse())Check(a.AddTag(n),"descending build");
  var b=new GameplayTagContainer();foreach(var n in registered.Where((n,i)=>i%3==0))b.AddTag(n);
  var aa=Names(a);var bb=Names(b);State(a,aa.OrderBy(n=>n,StringComparer.Ordinal),"large descending build");
  var target=new GameplayTagContainer(a);SetIntersection(target,a,b);State(target,aa.Intersect(bb),"large reused intersection");
  SetUnion(target,a,b);State(target,aa.Union(bb),"large reused union");
  var small=Serialized(registered[0],registered[500],registered[1099]);
  SetIntersection(target,a,small);State(target,new[]{registered[0],registered[500]},"large target small result");
  SetIntersection(target,small,a);State(target,new[]{registered[0],registered[500]},"skew swapped");
  a.RemoveTags(b);State(a,aa.Except(bb),"large remove tags");a.Clear();foreach(var n in registered.Take(8))a.AddTag(n);State(a,registered.Take(8),"clear reuse retained capacity");
 }
 static void NamesAndRegistry() {
  Init();var all=GameplayTagManager.GetAllTags().Select(t=>t.Name).ToArray();var expected=Set(universe);
  foreach(var n in universe){var p=n;while(p.Contains('.')){p=p.Substring(0,p.LastIndexOf('.'));expected.Add(p);}}
  Equal(string.Join("|",all),string.Join("|",expected),"registry ordinal implicit parents");Equal(GameplayTagManager.TagCount,expected.Count,"registry tag count");
  foreach(var invalid in new[]{"", ".A", "A.", "A..B", "A B", "A/B", "A:B"}) {Check(!GameplayTagName.TryNormalize(invalid,out _,out _),"invalid normalize "+invalid);Check(!GameplayTag.TryParse(invalid,out _),"invalid parse "+invalid);}
  Check(GameplayTag.TryParse(" A.B ",out var trimmed)&&trimmed.Name=="A.B","request trim");Check(!GameplayTagManager.IsRegistered(" A.B "),"registered exact input");Check(!GameplayTag.TryParse("a.b",out _),"parse case sensitive");
  var previousSettings=GameplayTagManager.Settings;int events=0;Action handler=()=>events++;GameplayTagManager.RegistryChanged+=handler;
  bool rejected=false;try{GameplayTagManager.Initialize(Settings(new[]{"Duplicate","Duplicate"}),true);}catch(GameplayTagRegistryException){rejected=true;}
  Check(rejected,"invalid rebuild rejected");Check(ReferenceEquals(GameplayTagManager.Settings,previousSettings),"failed rebuild settings rollback");Equal(events,0,"failed rebuild no event");Equal(string.Join("|",GameplayTagManager.GetAllTags().Select(t=>t.Name)),string.Join("|",all),"failed rebuild registry rollback");GameplayTagManager.RegistryChanged-=handler;
  object edited=GameplayTag.Parse("A.B");Field(typeof(GameplayTag),"m_Name").SetValue(edited,"B.Child");var editedTag=(GameplayTag)edited;Check(editedTag.MatchesTag(GameplayTag.Parse("B")),"edited serialized tag invalidates cached handle");Check(!editedTag.MatchesTag(GameplayTag.Parse("A")),"edited tag discards old ancestry");Equal(editedTag,GameplayTag.Parse("B.Child"),"edited tag identity");
  Equal(GameplayTag.None.Name,"","none name");Check(!GameplayTag.None.IsValid&&!GameplayTag.None.MatchesTag(GameplayTag.None),"none semantics");
  Equal(GameplayTag.Parse("A.B.C").GetDirectParent().Name,"A.B","direct parent");Equal(GameplayTag.Parse("A.B.C").LeafName,"C","leaf");Equal(GameplayTag.Parse("A.B.C").MatchesTagDepth(GameplayTag.Parse("A.BB")),1,"match depth partial segment");
 }
 static void Randomized() {
  Init();var rng=new Random(19790531);var cs=Enumerable.Range(0,5).Select(_=>new GameplayTagContainer()).ToArray();var oracle=Enumerable.Range(0,5).Select(_=>Set(Array.Empty<string>())).ToArray();var probes=universe.Concat(new[]{"Unknown","Unknown.Child",""}).ToArray();
  for(int step=0;step<2500;step++) {
   int i=rng.Next(5),j=rng.Next(5),k=rng.Next(5),op=rng.Next(11);string n=probes[rng.Next(probes.Length)];var a=oracle[i].ToArray();var b=oracle[j].ToArray();var d=oracle[k].ToArray();
   switch(op) {
    case 0: Equal(cs[i].AddTag(Raw(n)),GameplayTagManager.IsRegistered(n)&&oracle[i].Add(n),"random add");break;
    case 1:Equal(cs[i].RemoveTag(Raw(n)),oracle[i].Remove(n),"random remove");break;
    case 2:cs[i].Clear();oracle[i].Clear();break;
    case 3:cs[i].CopyFrom(cs[j]);if(i!=j)oracle[i]=Set(Registered(b));break;
    case 4:cs[i].AppendTags(cs[j]);if(i!=j)oracle[i].UnionWith(Registered(b));break;
    case 5:Equal(cs[i].RemoveTags(cs[j]),a.Except(b,StringComparer.Ordinal).Count()!=a.Length,"random remove tags");oracle[i].ExceptWith(b);break;
    case 6:SetUnion(cs[i],cs[j],cs[k]);oracle[i]=Set(Registered(b.Concat(d)));break;
    case 7:SetIntersection(cs[i],cs[j],cs[k]);oracle[i]=Set(b.Intersect(d,StringComparer.Ordinal));break;
    case 8:Intersect(cs[i],cs[j]);oracle[i].IntersectWith(b);break;
    case 9:Except(cs[i],cs[j]);oracle[i].ExceptWith(b);break;
    case 10:var raw=Enumerable.Range(0,rng.Next(12)).Select(_=>probes[rng.Next(probes.Length)]).ToArray();cs[i]=Serialized(raw);oracle[i]=Set(raw.Where(v=>v.Length>0));break;
   }
   State(cs[i],oracle[i],"random "+step+" op "+op);foreach(var p in probes) {Equal(cs[i].HasTagExact(Raw(p)),oracle[i].Contains(p),"random exact");Equal(cs[i].HasTag(Raw(p)),oracle[i].Any(v=>Hierarchy(v,p)),"random hierarchy");}
   Equal(cs[i].HasAnyExact(cs[j]),oracle[j].Any(oracle[i].Contains),"random any exact");Equal(cs[i].HasAllExact(cs[j]),oracle[j].All(oracle[i].Contains),"random all exact");Equal(cs[i].HasAny(cs[j]),oracle[j].Any(p=>oracle[i].Any(v=>Hierarchy(v,p))),"random any");Equal(cs[i].HasAll(cs[j]),oracle[j].All(p=>oracle[i].Any(v=>Hierarchy(v,p))),"random all");
   State(cs[i].FilterExact(cs[j]),oracle[i].Intersect(oracle[j]),"random filter exact");State(cs[i].Filter(cs[j]),oracle[i].Where(v=>oracle[j].Any(p=>Hierarchy(v,p))),"random filter hierarchy");
   State(GameplayTagContainer.Union(cs[i],cs[j]),Registered(oracle[i].Concat(oracle[j])),"random union");State(GameplayTagContainer.IntersectionExact(cs[i],cs[j]),oracle[i].Intersect(oracle[j]),"random intersection");
  }
 }
 static int Main() {try { Serialization();IdentityAndRebuild();Aliases();SerializationRoundTrip();UninitializedAndExceptions();ObservableCallbacks();MutatingCallbacks();LiveEnumeration();Queries();NamesAndRegistry();LargeAndSkewed();Randomized();Console.WriteLine("PASS assertions="+assertions);Console.WriteLine("TRANSCRIPT "+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(transcript.ToString()))));return 0;}catch(Exception ex){Console.Error.WriteLine("FAIL after "+assertions+" assertions: "+ex);return 1;} }
}
