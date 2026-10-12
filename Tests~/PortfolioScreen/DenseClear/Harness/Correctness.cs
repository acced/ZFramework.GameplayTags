using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameplayTags;
internal static class ThresholdChecks {
 static readonly FieldInfo Mask=typeof(TagStorage).GetField("m_BucketMask",BindingFlags.Instance|BindingFlags.NonPublic);
 static readonly FieldInfo Buckets=typeof(TagStorage).GetField("m_Buckets",BindingFlags.Instance|BindingFlags.NonPublic);
 static GameplayTag[] tags;
 static void Check(bool b,string message){if(!b)throw new Exception(message);}
 static string CheckCase(int keep,int reserve,int grow,string shape){
  var c=new GameplayTagContainer();if(reserve!=0)c.EnsureCapacity(reserve);
  for(int i=0;i<grow;i++)c.AddTag(tags[i]);for(int i=keep;i<grow;i++)c.RemoveTag(tags[i]);
  var storage=c.Indices.Storage;int mask=(int)Mask.GetValue(storage);var buckets=(int[])Buckets.GetValue(storage);var entries=storage.Entries;var explicitIndices=storage.ExplicitIndices;
  Check(c.TagCount==keep&&c.ExplicitTagCount==keep,"root-only exact live count");
  if(shape=="retained-sparse")Check(mask+1>=16384&&mask+1>keep*8,"true sparse ACTIVE index, not Copy compaction");
  if(shape=="density-boundary")Check(mask==1023,"density boundary fixed active1024");
  bool bulk=keep>=64&&mask!=0&&mask+1<=keep*8;
  for(int pass=0;pass<3;pass++){
   c.Clear();Check(c.TagCount==0&&c.ExplicitTagCount==0,"Clear cardinality");
   Check(ReferenceEquals(entries,storage.Entries)&&ReferenceEquals(explicitIndices,storage.ExplicitIndices)&&ReferenceEquals(buckets,Buckets.GetValue(storage))&&mask==(int)Mask.GetValue(storage),"Clear preserves retained arrays and active mask");
   foreach(var tag in tags)Check(!c.HasTag(tag)&&!c.HasTagExact(tag),"Clear removes all observable membership");
   // Full regrowth to original high-water mark, followed by a reverse drain.
   for(int i=grow-1;i>=0;i--)c.AddTag(tags[i]);Check(c.TagCount==grow&&c.ExplicitTagCount==grow,"full regrow");
   for(int i=0;i<grow;i++)Check(c.HasTagExact(tags[i]),"regrown membership");
   for(int i=grow-1;i>=0;i--)c.RemoveTag(tags[i]);Check(c.TagCount==0&&c.ExplicitTagCount==0,"full drain");
   for(int i=0;i<keep;i++)c.AddTag(tags[i]);
  }
  return "{\"Shape\":\""+shape+"\",\"LiveCount\":"+keep+",\"ActiveMask\":"+mask+",\"EntriesCapacity\":"+entries.Length+",\"ExplicitCapacity\":"+explicitIndices.Length+",\"BucketsCapacity\":"+buckets.Length+",\"Min64PredicateWouldUseBulk\":"+(bulk?"true":"false")+"}";
 }
 static void Main(string[] args){
  GameplayTagManager.Initialize(Enumerable.Range(0,1024).Select(i=>new GameplayTagRegistration("Node"+i)).ToArray());tags=Enumerable.Range(0,1024).Select(i=>GameplayTagManager.RequestTag("Node"+i)).ToArray();var rows=new List<string>();
  foreach(int n in new[]{8,9,15,16,17,31,32,33,63,64,65}){rows.Add(CheckCase(n,0,n,"fresh"));rows.Add(CheckCase(n,8192,1024,"retained-sparse"));}
  foreach(int n in new[]{127,128,129})rows.Add(CheckCase(n,512,512,"density-boundary"));
  int pos=Array.IndexOf(args,"--out");System.IO.File.WriteAllText(args[pos+1],"{\"Checks\":\"PASS\",\"Rows\":["+string.Join(",",rows)+"]}");Console.WriteLine("PASS25 generic live-count/density/sparse-index boundaries; Clear, array/mask retention, full regrow/drain. No timings.");
 }
}
