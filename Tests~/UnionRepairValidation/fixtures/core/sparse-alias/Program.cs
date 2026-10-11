using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameplayTags;
internal static class SparseAlias {
 static GameplayTag[] tags;
 static void Check(bool v,string s){if(!v)throw new Exception(s);}
 static void Apply(GameplayTagContainer output,GameplayTagContainer other,bool left){
#if REUSABLE
  if(left)GameplayTagContainer.Union(output,output,other);else GameplayTagContainer.Union(output,other,output);
#else
  GameplayTagContainer.Copy(output,left?GameplayTagContainer.Union(output,other):GameplayTagContainer.Union(other,output));
#endif
 }
 static string Shape(GameplayTagContainer c){var s=((IGameplayTagContainer)c).Indices.Storage;return "{\"Entries\":"+s.Entries.Length+",\"ExplicitIndices\":"+s.ExplicitIndices.Length+",\"Buckets\":"+((Array)typeof(TagStorage).GetField("m_Buckets",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(s)).Length+",\"ActiveMask\":"+typeof(TagStorage).GetField("m_BucketMask",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(s)+",\"Count\":"+s.Count+",\"ExplicitCount\":"+s.ExplicitCount+"}";}
 public static void Main(string[] args){
  GameplayTagManager.Initialize(Enumerable.Range(0,1025).Select(i=>new GameplayTagRegistration("Flat.L"+i)).ToArray());tags=Enumerable.Range(0,1025).Select(i=>GameplayTagManager.RequestTag("Flat.L"+i)).ToArray();
  var counter=(Func<long>)Delegate.CreateDelegate(typeof(Func<long>),typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",Type.EmptyTypes));
  var warm=new GameplayTagContainer();warm.AddTag(tags[0]);var small=new GameplayTagContainer();small.AddTag(tags[1024]);Apply(warm,small,true);counter();
  var rows=new List<string>();
  foreach(bool left in new[]{true,false}){
   var output=new GameplayTagContainer(8192);for(int i=0;i<1024;i++)output.AddTag(tags[i]);for(int i=1;i<1024;i++)output.RemoveTag(tags[i]);
   Check(output.ExplicitTagCount==1&&output.TagCount==2,"sparse setup");string before=Shape(output);
   long bytes=counter();Apply(output,small,left);bytes=counter()-bytes;string after=Shape(output);
   Check(output.ExplicitTagCount==2&&output.TagCount==3,"union cardinality");Check(output.HasTagExact(tags[0])&&output.HasTagExact(tags[1024]),"union names");Check(small.ExplicitTagCount==1&&small.TagCount==2,"source immutable");
   // Explicitly exercise downstream hit/miss and Clear, without timing claims.
   for(int i=0;i<1025;i++){Check(output.HasTagExact(tags[i])==(i==0||i==1024),"lookup exact");Check(output.HasTag(tags[i])==(i==0||i==1024),"lookup hierarchical");}
   output.Clear();Check(output.TagCount==0&&output.ExplicitTagCount==0,"clear");string cleared=Shape(output);
   for(int i=0;i<1024;i++)output.AddTag(tags[i]);Check(output.ExplicitTagCount==1024&&output.TagCount==1025,"regrow");string regrown=Shape(output);
   for(int i=1023;i>=0;i--)output.RemoveTag(tags[i]);Check(output.TagCount==0&&output.ExplicitTagCount==0,"full drain");
   rows.Add("{\"Alias\":\""+(left?"left":"right")+"\",\"AllocatedBytes\":"+bytes+",\"Before\":"+before+",\"After\":"+after+",\"Cleared\":"+cleared+",\"Regrown\":"+regrown+",\"Drained\":"+Shape(output)+"}");
  }
  int pos=Array.IndexOf(args,"--out");System.IO.File.WriteAllText(args[pos+1],"{\"Rows\":["+string.Join(",",rows)+"],\"Correctness\":\"PASS\"}");Console.WriteLine("PASS sparse alias both positions: union, all hit/miss lookups, Clear, regrow, drain; no timing claims.");
 }
}
