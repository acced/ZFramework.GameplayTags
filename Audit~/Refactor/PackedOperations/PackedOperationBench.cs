using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameplayTags;
using Set=GameplayTags.RuntimeTagSet;
internal static class PackedOperationBench
{
 const int Seed=20261002,Units=16;
 static long assertions,checksum;static int failures,samples;static double targetMs;static object allocationSink;
 static readonly List<object> Rows=new List<object>();
#if GAMEPLAYTAGS_FORCE_PORTABLE
 const bool Portable=true;
#else
 const bool Portable=false;
#endif
 static void Check(bool b,string m){assertions++;if(!b)throw new InvalidOperationException(m);}
 static string Hash(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
 sealed class Spec
 {
  public int universe,n,m,overlap,op;public string family,capacity,order,pattern,mode;
  public string Key=>string.Join("/",universe,n,m,overlap,family,pattern,capacity,order,mode,Operation(op));
 }
 sealed class Actor{public Set a,b,work,result;}
 sealed class Fixture
 {
  public Spec spec;public TagRegistry registry;public int[] x,y,expected;public Actor[] actors;public string digest;
  public object Details=>new{actualRegistryCount=registry.Count,requestedStorage=spec.mode.ToString(),activeActors=spec.order=="hot"?1:Units,
   retainedInstances=Units,sourceInputDigest=digest,actors=actors.Select((a,i)=>new{id=i,left=Shape(a.a),right=Shape(a.b),work=Shape(a.work),result=Shape(a.result)}).ToArray()};
 }
 static object Shape(Set s)=>s==null?null:new{storage=s.Storage.ToString(),members=s.Count,capacity=s.Capacity,bufferBytes=s.BufferBytes,recordCount=s.RecordCount,recordCapacity=s.RecordCapacity,reservedMemberCapacity=s.ReservedMemberCapacity};
 static readonly string[] Operations={"union.into","intersection.into","difference.into","append.reuse","remove.reuse","union.left_alias","union.right_alias","intersection.left_alias","intersection.right_alias","difference.left_alias","difference.right_alias"};
 static string Operation(int op)=>Operations[op];
 static IEnumerable<Spec> Plan()
 {
  foreach(var cell in new[]{(65536,4096,4096,0,"clusters4"),(65536,4096,4096,50,"clusters4"),(65536,4096,4096,100,"clusters4"),
   (65536,4096,4096,0,"scattered"),(65536,4096,4096,50,"scattered"),(65536,4096,4096,100,"scattered"),
   (65536,4096,4096,5,"clusters4"),(65536,4096,4096,5,"scattered"),(65536,4096,4096,50,"contiguous"),
   (262144,16384,16384,50,"clusters4"),(262144,4096,128,50,"clusters4"),(262144,128,4096,50,"clusters4")})
   foreach(string mode in new[]{"Compressed","Sparse","Dense","Bulk"})
   for(int op=0;op<(mode=="Compressed"?11:5);op++)
    yield return new Spec{universe=cell.Item1,n=cell.Item2,m=cell.Item3,overlap=cell.Item4,pattern=cell.Item5,family="declared_kernel_shape",capacity="upper",order="rotating16",mode=mode,op=op};
  foreach(var cell in new[]{(65536,4096,4096,50,"clusters4"),(65536,4096,4096,5,"scattered"),(262144,4096,128,50,"clusters4"),(262144,128,4096,50,"clusters4")})
   foreach(int op in new[]{0,1,2,5,6,7,8,9,10})
    yield return new Spec{universe=cell.Item1,n=cell.Item2,m=cell.Item3,overlap=cell.Item4,pattern=cell.Item5,family="tight_capacity",capacity="exact_union",order="rotating16",mode="Compressed",op=op};
  foreach(var cell in new[]{(65536,4096,4096,50,"clusters4"),(65536,4096,4096,5,"scattered"),(262144,4096,128,50,"clusters4"),(262144,128,4096,50,"clusters4")})
   foreach(int op in new[]{0,1,2,3,4})
    yield return new Spec{universe=cell.Item1,n=cell.Item2,m=cell.Item3,overlap=cell.Item4,pattern=cell.Item5,family="tight_records",capacity="actual_union_records",order="rotating16",mode="Compressed",op=op};
  foreach(var cell in new[]{(65536,4096,4096,5,"scattered",1),(65536,4096,4096,100,"clusters4",2),(65536,4096,4096,0,"clusters4",1),(65536,4096,4096,100,"scattered",2)})
   yield return new Spec{universe=cell.Item1,n=cell.Item2,m=cell.Item3,overlap=cell.Item4,pattern=cell.Item5,family="small_filter_result",capacity="actual_result_records",order="rotating16",mode="Compressed",op=cell.Item6};
 }
 static int[] Candidates(int[] leaves,string pattern,int seed,int count)
 {
  if(pattern=="contiguous"){int start=(leaves.Length-2*count)/3;return leaves.Skip(start).Concat(leaves.Take(start)).ToArray();}
  if(pattern=="clusters4")return Enumerable.Range(0,leaves.Length).Select(i=>leaves[(i%4)*(leaves.Length/4)+i/4]).ToArray();
  var result=(int[])leaves.Clone();uint state=(uint)seed;
  for(int i=result.Length-1;i>0;i--){uint bound=(uint)i+1,threshold=unchecked(0u-bound)%bound,value;do{state^=state<<13;state^=state>>17;state^=state<<5;value=state;}while(value<threshold);int j=(int)(value%bound);int temp=result[i];result[i]=result[j];result[j]=temp;}return result;
 }
 static (TagRegistry,int[]) MakeRegistry(int u)
 {
  string[] names=Enumerable.Range(0,u).Select(i=>"Bench.G"+(i/64).ToString("D5")+".T"+i.ToString("D7")).ToArray();var settings=UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
  settings.ReplaceAll(names.Select(n=>new GameplayTagDefinition(n,"","Default",false,true)).ToList(),new List<GameplayTagRedirect>(),new List<GameplayTagSource>{new GameplayTagSource("Default","",false)});
  var registry=TagRegistry.Create(settings);return(registry,names.Select(n=>registry.Resolve(n).RuntimeIndex).OrderBy(i=>i).ToArray());
 }
 static Set Input(TagRegistry r,int[] ids,string mode,int capacity=-1)
 {
  if(mode=="Bulk")return Set.FromTagsForBulk(r,ids.Select(r.GetTagAt).ToArray());
  var set=new Set(r,capacity<0?ids.Length:capacity,Enum.Parse<TagSetStorage>(mode));foreach(int id in ids)set.AddTag(r.GetTagAt(id));return set;
 }
 static Fixture Create(Spec spec,TagRegistry registry,int[] leaves)
 {
  int[] x=Candidates(leaves,spec.pattern,Seed+spec.n,spec.n).Take(spec.n).OrderBy(id=>id).ToArray();var owned=new HashSet<int>(x);int shared=Math.Min(spec.n,spec.m)*spec.overlap/100;
  int[] y=Enumerable.Range(0,shared).Select(i=>x[(int)((long)i*x.Length/Math.Max(1,shared))]).Concat(Candidates(leaves,spec.pattern,Seed+spec.n+spec.m+29,spec.m).Where(id=>!owned.Contains(id)).Take(spec.m-shared)).OrderBy(id=>id).ToArray();
  int[] union=x.Union(y).OrderBy(i=>i).ToArray();int[] expected=spec.op==1||spec.op==7||spec.op==8?x.Intersect(y).ToArray():spec.op==2||spec.op==4||spec.op==9||spec.op==10?x.Except(y).ToArray():union;int reserve=spec.capacity=="actual_result_records"?expected.Select(id=>id>>4).Distinct().Count():spec.capacity=="actual_union_records"?union.Select(id=>id>>4).Distinct().Count():spec.capacity=="exact_union"?union.Length:Math.Min(registry.Count,spec.n+spec.m);
  var actors=new Actor[Units];for(int i=0;i<actors.Length;i++)
  {var a=Input(registry,x,spec.mode);var b=Input(registry,y,spec.mode);Set work;
   if(spec.mode=="Bulk"){var initial=Set.UnionForBulk(a,b);work=new Set(registry,reserve,initial.Storage);}else work=new Set(registry,reserve,Enum.Parse<TagSetStorage>(spec.mode));
   actors[i]=new Actor{a=a,b=b,work=work};}

  var f=new Fixture{spec=spec,registry=registry,x=x,y=y,expected=expected,actors=actors,digest=Hash(string.Join(",",x)+"|"+string.Join(",",y))};
  foreach(var a in actors){Verify(a.a,x,registry);Verify(a.b,y,registry);}return f;
 }
 static void Verify(Set s,int[] expected,TagRegistry registry)
 {
  Check(s!=null&&ReferenceEquals(s.Registry,registry),"Missing/foreign output");Check(s.Count==expected.Length,"Wrong Count");int i=0;
  foreach(var t in s)Check(i<expected.Length&&t.RuntimeIndex==expected[i++],"Wrong membership/order");Check(i==expected.Length,"Wrong enumeration length");
 }
 static int Run(Fixture f,int sweeps)
 {
  int value=0;bool hot=f.spec.order=="hot";int op=f.spec.op;
  for(int sweep=0;sweep<sweeps;sweep++)for(int i=0;i<Units;i++)
  {
   Actor a=f.actors[hot?0:i];
   switch(op)
   {
    case 0:Set.UnionInto(a.a,a.b,a.work);break;
    case 1:Set.IntersectionExactInto(a.a,a.b,a.work);break;
    case 2:Set.DifferenceExactInto(a.a,a.b,a.work);break;
    case 3:a.work.CopyFrom(a.a);a.work.AppendTags(a.b);break;
    case 4:a.work.CopyFrom(a.a);a.work.RemoveTags(a.b);break;
    case 5:a.work.CopyFrom(a.a);Set.UnionInto(a.work,a.b,a.work);break;
    case 6:a.work.CopyFrom(a.b);Set.UnionInto(a.a,a.work,a.work);break;
    case 7:a.work.CopyFrom(a.a);Set.IntersectionExactInto(a.work,a.b,a.work);break;
    case 8:a.work.CopyFrom(a.b);Set.IntersectionExactInto(a.a,a.work,a.work);break;
    case 9:a.work.CopyFrom(a.a);Set.DifferenceExactInto(a.work,a.b,a.work);break;
    default:a.work.CopyFrom(a.b);Set.DifferenceExactInto(a.a,a.work,a.work);break;
   }
   a.result=a.work;
   value=unchecked(value+a.result.Count*((i&3)+1)+17);
  }
  return value;
 }
 static void Actual(Fixture f,int value,int sweeps)
 {
  Check(value==unchecked((f.expected.Length*40+17*Units)*sweeps),"Actual batch checksum differs");int active=f.spec.order=="hot"?1:Units;
  for(int i=0;i<active;i++)Verify(f.actors[i].result,f.expected,f.registry);
  foreach(var a in f.actors){Verify(a.a,f.x,f.registry);Verify(a.b,f.y,f.registry);}
 }
 static void Measure(Fixture f)
 {
  int sweeps=0,done=0,limit=0;long estimate=0;double calibrationMs=0;var ns=new List<double>();var allocated=new List<double>();var ticks=new List<long>();var batch=new List<double>();var returned=new List<int>();var gc=new List<int[]>();
  string status="measured",error=null;bool zero=true;
  try
  {
   int v=Run(f,1);Actual(f,v,1);long before=GC.GetAllocatedBytesForCurrentThread();v=Run(f,1);estimate=GC.GetAllocatedBytesForCurrentThread()-before;Actual(f,v,1);
   limit=estimate>0?(int)Math.Max(1,Math.Min(1<<14,(32L<<20)/estimate)):1<<14;sweeps=1;
   while(true){long start=Stopwatch.GetTimestamp();v=Run(f,sweeps);long elapsed=Stopwatch.GetTimestamp()-start;Actual(f,v,sweeps);calibrationMs=elapsed*1000.0/Stopwatch.Frequency;if(calibrationMs>=targetMs||sweeps>=limit)break;sweeps=Math.Min(limit,sweeps*2);}
   for(int sample=0;sample<samples;sample++)
   {
    int g0=GC.CollectionCount(0),g1=GC.CollectionCount(1),g2=GC.CollectionCount(2);long a0=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();int value=Run(f,sweeps);long elapsed=Stopwatch.GetTimestamp()-start,bytes=GC.GetAllocatedBytesForCurrentThread()-a0;
    ns.Add(elapsed*(1e9/Stopwatch.Frequency)/(sweeps*Units));allocated.Add(bytes/(double)(sweeps*Units));ticks.Add(elapsed);batch.Add(elapsed*1000.0/Stopwatch.Frequency);returned.Add(value);gc.Add(new[]{GC.CollectionCount(0)-g0,GC.CollectionCount(1)-g1,GC.CollectionCount(2)-g2});done++;
    Actual(f,value,sweeps);if(zero)Check(bytes==0,"Prepared distinct/append-self operation allocated");checksum^=value;
   }
  }
  catch(Exception e){failures++;status="failed";error=e.ToString();}
  Rows.Add(new{key=f.spec.Key,operation=Operation(f.spec.op),universe=f.spec.universe,leftCount=f.spec.n,rightCount=f.spec.m,overlap=f.spec.overlap,family=f.spec.family,pattern=f.spec.pattern,capacityPolicy=f.spec.capacity,order=f.spec.order,requestedStorage=f.spec.mode.ToString(),f.digest,status,error,
   iterations=sweeps,units=Units,samplesCompleted=done,ns,allocated_bytes=allocated,elapsed_ticks=ticks,batch_ms=batch,returned_values=returned,gc_collections=gc,
   iterationLimit=limit,estimatedBytesPerSweep=estimate,calibrationMs,allocationCapBytes=32L<<20,limitReached=sweeps==limit,timed_output_validated=status=="measured",zero_allocation_required=zero,details=f.Details});
 }
 static object Controls()
 {
  Check(GCSettings.LatencyMode==GCLatencyMode.Batch,"BatchGC required");var a=new int[2048];var b=new int[2048];a[2]=23;for(int i=0;i<100;i++)Array.Copy(a,b,a.Length);
  long x=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++)Array.Copy(a,b,a.Length);long negative=GC.GetAllocatedBytesForCurrentThread()-x;x=GC.GetAllocatedBytesForCurrentThread();allocationSink=new byte[4096];long positive=GC.GetAllocatedBytesForCurrentThread()-x;
  Check(negative==0&&b[2]==23,"Negative allocation control");Check(positive>=4096,"Positive allocation control");return new{negative_copy_bytes=negative,positive_new_array_bytes=positive};
 }
 static int Main(string[] args)
 {
  string output=args[0],source=args[1],label=args[2];int round=int.Parse(args[3]);samples=int.Parse(args[4]);targetMs=double.Parse(args[5],System.Globalization.CultureInfo.InvariantCulture);Check(samples>=3&&targetMs>0,"Sampling parameters");var controls=Controls();var plan=Plan().ToArray();Check(plan.Length==372,"Declared row count");Check(plan.Select(s=>s.Key).Distinct().Count()==372,"Duplicate plan key");
  var registries=new Dictionary<int,(TagRegistry,int[])>();foreach(int u in new[]{65536,262144})registries[u]=MakeRegistry(u);
  // Every declared requested layout shares one process and one callsite; reverse the entire fixed row order on odd rounds.
  var order=round%2==0?plan:plan.Reverse().ToArray();foreach(var spec in order){var pair=registries[spec.universe];Measure(Create(spec,pair.Item1,pair.Item2));if(Rows.Count%32==0)Console.WriteLine(label+" rows="+Rows.Count+" failures="+failures);}
  File.WriteAllText(output,JsonSerializer.Serialize(new{source,label,round,samples,targetMs,seed=Seed,failures,assertions,checksum,controls,rows=Rows,expectedKeys=plan.Select(s=>s.Key).ToArray(),executionKeys=order.Select(s=>s.Key).ToArray(),
   runtime=RuntimeInformation.FrameworkDescription,architecture=RuntimeInformation.ProcessArchitecture.ToString(),os=RuntimeInformation.OSDescription,stopwatchFrequency=Stopwatch.Frequency,gcMode=GCSettings.LatencyMode.ToString(),gcConcurrent=Environment.GetEnvironmentVariable("DOTNET_gcConcurrent"),serverGC=GCSettings.IsServerGC,readyToRun=Environment.GetEnvironmentVariable("DOTNET_ReadyToRun"),tieredCompilation=Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),tieredPgo=Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
   portableKernelsForced=Portable,vectorHardwareAccelerated=System.Numerics.Vector.IsHardwareAccelerated,vectorIntWidth=System.Numerics.Vector<int>.Count,denseWordsPerPackedRecord=RuntimeBitOperations.DenseWordsPerPackedRecord,avx2Supported=System.Runtime.Intrinsics.X86.Avx2.IsSupported,arm64AdvSimdSupported=System.Runtime.Intrinsics.Arm.AdvSimd.Arm64.IsSupported,
   executablePath=typeof(PackedOperationBench).Assembly.Location,executableSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(PackedOperationBench).Assembly.Location))).ToLowerInvariant(),runtimeSourceRoot=args[6],sourceInventorySha256=args[7],harnessSha256=args[8],runnerSha256=args[9],
   validation_protocol="actual-final-actor-output-before-reset-packed-operations-v1",scope="Focused existing public set-operation mechanism screen. All rows rotate16 separately allocated instances. Prepared Into includes eager Count; append/remove and every alias row charge CopyFrom reset inside each operation. Destinations disclose upper/exact-union member reservation or exact union-record capacity. Record-tight cases include distinct and reset-plus-mutation operators; alias cases retain union-member reservation. Forced Compressed/Sparse/Dense and explicit Bulk setup are distinct rows; Bulk source/output selection occurs in setup and is disclosed, so no lifecycle claim. No allocating result operator, private kernel call or per-row fastest layout oracle.",
   limitations="Managed host only; BatchGC measurement control. Source A/A, row-order and rotating-instance results are required before attribution. New fixed xorshift scattered inputs target the earlier adverse shapes; this is not an exact GHA input/host replay. Raw GC/allocation/tails retained; no Unity/IL2CPP/mobile or universal performance claim."},new JsonSerializerOptions{WriteIndented=true}));GC.KeepAlive(allocationSink);return failures==0?0:1;
 }
}
