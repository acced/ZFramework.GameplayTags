using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using GameplayTags;

internal static class Program
{
    const int WarmupCalls = 64;
    const int AllocationCalls = 256;
    static object Sink;
    static long Checksum;
    static readonly Process Process = Process.GetCurrentProcess();
    static readonly Func<long> Allocated = AllocationCounter();
    static readonly FieldInfo Buckets = typeof(TagStorage).GetField("m_Buckets", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly string StorageFields = string.Join(";", typeof(TagStorage).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Select(f => f.Name + ":" + f.FieldType.FullName).OrderBy(x => x, StringComparer.Ordinal));
    static readonly Spec[] Specs = MakeSpecs();
    static Func<long> AllocationCounter()
    {
        MethodInfo method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
        if (method == null) throw new NotSupportedException("Real per-thread allocation counter required.");
        return (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
    }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    sealed class Spec { public string Id; public int N; public string Kind; }
    static Spec[] MakeSpecs()
    {
        var result = new List<Spec> {
            new Spec { Id="removetags/empty/self", N=0, Kind="empty-self" },
            new Spec { Id="removetags/empty/distinct", N=0, Kind="empty-distinct" }
        };
        foreach (int n in new[] {1,4,16,1024}) foreach (string kind in new[] {"self","equal-distinct","disjoint"})
            result.Add(new Spec { Id="removetags/n"+n+"/"+kind+"-restore-pair", N=n, Kind=kind });
        foreach (int n in new[] {4,1024}) foreach (string kind in new[] {"counted-equal","value-proxy-equal","reference-proxy-equal"})
            result.Add(new Spec { Id="removetags/n"+n+"/"+kind+"-restore-pair", N=n, Kind=kind });
        Check(result.Count==20 && result.Select(s=>s.Id).Distinct().Count()==20, "exact twenty unique rows");
        return result.ToArray();
    }
    static string Leaf(int n) => "Flat.G" + n%32 + ".L" + n;
    static GameplayTag Tag(string name) => GameplayTagManager.RequestTag(name);
    static GameplayTagContainer Set(IEnumerable<string> names) { var c=new GameplayTagContainer(); foreach(string n in names)c.AddTag(Tag(n)); return c; }
    static HashSet<string> Names(IGameplayTagContainer source)
    {
        var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(GameplayTag t in source.GetExplicitTags())Check(names.Add(t.Name),"duplicate explicit name");
        return names;
    }
    static HashSet<string> Closure(IEnumerable<string> names)
    {
        var closure=new HashSet<string>(StringComparer.Ordinal);
        foreach(string name in names) { closure.Add(name); for(int i=0;i<name.Length;i++)if(name[i]=='.')closure.Add(name.Substring(0,i)); }
        return closure;
    }
    static void Assert(IGameplayTagContainer value, HashSet<string> expected)
    {
        HashSet<string> closure=Closure(expected), actual=new HashSet<string>(StringComparer.Ordinal);
        Check(value.ExplicitTagCount==expected.Count && value.TagCount==closure.Count,"cardinality");
        Check(Names(value).SetEquals(expected),"explicit names");
        foreach(GameplayTag t in value.GetTags())Check(actual.Add(t.Name),"duplicate closure name");
        Check(actual.SetEquals(closure),"ordinal prefix closure");
    }
    sealed class Fixture
    {
        public Spec Spec; public GameplayTagContainer Work, Seed; public IGameplayTagContainer Owner;
        public GameplayTagCountContainer Counted; public HashSet<string> SeedNames, SourceNames, Expected;
        public Action Step; public long Weight;
    }
    static Fixture Create(Spec spec)
    {
        string[] left=Enumerable.Range(0,spec.N).Select(i=>Leaf((i*719)%spec.N)).ToArray();
        var f=new Fixture { Spec=spec, Work=new GameplayTagContainer(), Seed=Set(left), SeedNames=new HashSet<string>(left,StringComparer.Ordinal) };
        f.Expected=spec.Kind=="disjoint" ? new HashSet<string>(left,StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
        f.Weight=1+f.Expected.Count+Closure(f.Expected).Count;
        if(spec.Kind=="empty-self") { f.Owner=f.Work; f.Step=()=>{f.Work.RemoveTags(f.Work);Observe(f.Work);}; }
        else if(spec.Kind=="empty-distinct") { var other=new GameplayTagContainer();f.Owner=other;f.Step=()=>{f.Work.RemoveTags(other);Observe(f.Work);}; }
        else if(spec.Kind=="self") { f.Owner=f.Work;f.Step=()=>{GameplayTagContainer.Copy(f.Work,f.Seed);f.Work.RemoveTags(f.Work);Observe(f.Work);}; }
        else if(spec.Kind=="counted-equal")
        {
            var other=new GameplayTagCountContainer();foreach(string name in left.Reverse())other.AddTag(Tag(name),3);
            f.Owner=other;f.Counted=other;f.Step=()=>{GameplayTagContainer.Copy(f.Work,f.Seed);f.Work.RemoveTags(other);Observe(f.Work);};
        }
        else
        {
            string[] right=spec.Kind=="disjoint" ? Enumerable.Range(0,spec.N).Select(i=>Leaf(1024+(i*719)%spec.N)).Reverse().ToArray() : left.Reverse().ToArray();
            var other=Set(right);f.Owner=other;
            if(spec.Kind=="value-proxy-equal") { var proxy=new ValueProxy(other);f.Step=()=>{GameplayTagContainer.Copy(f.Work,f.Seed);f.Work.RemoveTags(proxy);Observe(f.Work);}; }
            else if(spec.Kind=="reference-proxy-equal") { var proxy=new ReferenceProxy(other);f.Step=()=>{GameplayTagContainer.Copy(f.Work,f.Seed);f.Work.RemoveTags(proxy);Observe(f.Work);}; }
            else f.Step=()=>{GameplayTagContainer.Copy(f.Work,f.Seed);f.Work.RemoveTags(other);Observe(f.Work);};
        }
        f.SourceNames=ReferenceEquals(f.Owner,f.Work) ? f.Expected : Names(f.Owner);
        Check(ReferenceEquals(f.Work.Indices.Storage,f.Owner.Indices.Storage)==(spec.Kind=="self"||spec.Kind=="empty-self"),"declared alias relation");
        return f;
    }
    static void Validate(Fixture f)
    {
        Assert(f.Work,f.Expected);Assert(f.Seed,f.SeedNames);Assert(f.Owner,f.SourceNames);
        if(f.Counted!=null)
        {
            foreach(string name in f.SourceNames)Check(f.Counted.GetExplicitTagCount(Tag(name))==3,"counted explicit contributions unchanged");
            foreach(string name in Closure(f.SourceNames))Check(f.Counted.GetTagCount(Tag(name))==3*f.SourceNames.Count(n=>n==name||n.StartsWith(name+".",StringComparison.Ordinal)),"counted closure contributions unchanged");
        }
        var drain=f.Work.Clone();Assert(drain,f.Expected);
        foreach(string name in f.Expected.OrderBy(x=>x,StringComparer.Ordinal))drain.RemoveTag(Tag(name));
        Check(drain.TagCount==0&&drain.ExplicitTagCount==0,"normalized full drain");
    }
    sealed class Shape
    {
        public TagStorage Storage; public TagStorage.Entry[] Entries; public int[] Explicit, Bucket;
        public static Shape Capture(GameplayTagContainer c)
        {
            TagStorage s=c.Indices.Storage;
            return new Shape {Storage=s,Entries=s.Entries,Explicit=s.ExplicitIndices,Bucket=(int[])Buckets.GetValue(s)};
        }
        public void Same(GameplayTagContainer c)
        {
            Shape other=Capture(c);
            Check(ReferenceEquals(Storage,other.Storage)&&ReferenceEquals(Entries,other.Entries)&&ReferenceEquals(Explicit,other.Explicit)&&ReferenceEquals(Bucket,other.Bucket),"warm retained storage/arrays changed");
        }
    }
    sealed class Row
    {
        public string Id {get;set;} public int Iterations {get;set;} public double Ns {get;set;} public double Bytes {get;set;}
        public double WallMilliseconds {get;set;} public double CpuMilliseconds {get;set;}
        public int Gen0 {get;set;} public int Gen1 {get;set;} public int Gen2 {get;set;}
        public long MeasuredChecksum {get;set;} public long ExpectedMeasuredChecksum {get;set;}
        public long ColdBytes {get;set;} public long WarmProbeBytes {get;set;} public int WarmProbeCalls {get;set;}
        public int BeforeEntries {get;set;} public int BeforeExplicit {get;set;} public int BeforeBuckets {get;set;}
        public int Entries {get;set;} public int Explicit {get;set;} public int Buckets {get;set;}
        public string RetainedFields {get;set;}
    }
    static Row Layout(string id,Shape before,Shape after) => new Row {Id=id,BeforeEntries=before.Entries.Length,BeforeExplicit=before.Explicit.Length,BeforeBuckets=before.Bucket.Length,Entries=after.Entries.Length,Explicit=after.Explicit.Length,Buckets=after.Bucket.Length,RetainedFields=StorageFields};
    static void Observe(GameplayTagContainer c) { Checksum=unchecked(Checksum+1+c.TagCount+c.ExplicitTagCount);Sink=c; }
    static void Preflight(Fixture f) { f.Step();Validate(f); }
    static Row WarmCheck(Fixture f)
    {
        Preflight(f);for(int i=0;i<WarmupCalls;i++)f.Step();Shape shape=Shape.Capture(f.Work);long before=Allocated();
        for(int i=0;i<AllocationCalls;i++)f.Step();long bytes=Allocated()-before;
        Check(bytes==0,"warm allocation");shape.Same(f.Work);Validate(f);
        Row row=Layout(f.Spec.Id,shape,Shape.Capture(f.Work));row.WarmProbeBytes=bytes;row.WarmProbeCalls=AllocationCalls;return row;
    }
    static Row ColdCheck(Fixture f)
    {
        Shape before=Shape.Capture(f.Work);Allocated();long allocated=Allocated();f.Step();long bytes=Allocated()-allocated;
        Shape after=Shape.Capture(f.Work);Validate(f);for(int i=0;i<WarmupCalls;i++)f.Step();
        allocated=Allocated();for(int i=0;i<AllocationCalls;i++)f.Step();long warm=Allocated()-allocated;
        Check(warm==0,"cold-mode warm allocation");after.Same(f.Work);Validate(f);
        Row row=Layout(f.Spec.Id,before,after);row.ColdBytes=bytes;row.WarmProbeBytes=warm;row.WarmProbeCalls=AllocationCalls;return row;
    }
    static Row Measure(Fixture f,int iterations)
    {
        Check(iterations>0,"positive iterations");for(int i=0;i<WarmupCalls;i++)f.Step();Shape shape=Shape.Capture(f.Work);
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        long cpu=Process.TotalProcessorTime.Ticks, allocated=Allocated(), checksum=Checksum;int g0=GC.CollectionCount(0),g1=GC.CollectionCount(1),g2=GC.CollectionCount(2);
        long start=Stopwatch.GetTimestamp();for(int i=0;i<iterations;i++)f.Step();long ticks=Stopwatch.GetTimestamp()-start;
        long bytes=Allocated()-allocated;int end0=GC.CollectionCount(0),end1=GC.CollectionCount(1),end2=GC.CollectionCount(2);cpu=Process.TotalProcessorTime.Ticks-cpu;
        var row=Layout(f.Spec.Id,shape,shape);row.Iterations=iterations;row.Ns=ticks*(1e9/Stopwatch.Frequency)/iterations;row.Bytes=(double)bytes/iterations;
        row.WallMilliseconds=ticks*1000.0/Stopwatch.Frequency;row.CpuMilliseconds=cpu/10000.0;row.Gen0=end0-g0;row.Gen1=end1-g1;row.Gen2=end2-g2;
        row.MeasuredChecksum=unchecked(Checksum-checksum);row.ExpectedMeasuredChecksum=unchecked(f.Weight*iterations);
        Check(row.MeasuredChecksum==row.ExpectedMeasuredChecksum,"measured checksum");Check(bytes==0,"measured warm allocation");shape.Same(f.Work);Validate(f);return row;
    }
    static string Quote(string s)=>"\""+s.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    static string Json(Row r)=>"{"+string.Join(",",typeof(Row).GetProperties().Select(p=>Quote(p.Name)+":"+(p.PropertyType==typeof(string)?Quote((string)p.GetValue(r)):Convert.ToString(p.GetValue(r),CultureInfo.InvariantCulture))))+"}";
    static void Report(string mode,List<Row> rows,List<Row> attempts=null)
    {
        Console.WriteLine("{\"Schema\":1,\"Mode\":"+Quote(mode)+",\"Runtime\":"+Quote(Environment.Version.ToString())+",\"Framework\":"+Quote(Type.GetType("Mono.Runtime")!=null?"Standalone Mono":".NET")+",\"RegistryCount\":"+GameplayTagManager.TagCount+",\"Checksum\":"+Checksum+",\"Rows\":["+string.Join(",",rows.Select(Json))+"],\"CalibrationAttempts\":["+string.Join(",",(attempts??new List<Row>()).Select(Json))+"]}");
    }
    static void Main(string[] args)
    {
        string mode=args.Length==0?"test":args[0];
        if(mode=="list"){foreach(Spec s in Specs)Console.WriteLine(s.Id);return;}
        GameplayTagManager.Initialize(Enumerable.Range(0,2048).Select(i=>new GameplayTagRegistration(Leaf(i))).ToArray());
        if(mode=="cold") {Check(args.Length==2,"cold requires row ID");Spec s=Specs.Single(x=>x.Id==args[1]);Report(mode,new List<Row>{ColdCheck(Create(s))});return;}
        var rows=new List<Row>();var attempts=new List<Row>();Dictionary<string,int> fixedCounts=null;double target=0;
        if(mode=="bench")
        {
            Check(args.Length==2,"bench requires fixed counts");fixedCounts=new Dictionary<string,int>();
            foreach(string line in System.IO.File.ReadAllLines(args[1])) {string[] parts=line.Split('\t');Check(parts.Length==2,"iteration row");int n=int.Parse(parts[1],CultureInfo.InvariantCulture);Check(n>0,"positive count");fixedCounts.Add(parts[0],n);}
            Check(fixedCounts.Count==Specs.Length&&Specs.All(s=>fixedCounts.ContainsKey(s.Id)),"exact complete iteration map");
        }
        else if(mode=="calibrate") {Check(args.Length==2,"calibrate requires target milliseconds");target=double.Parse(args[1],CultureInfo.InvariantCulture);Check(target>=200&&target<=5000,"calibration target 200..5000ms");}
        else Check(mode=="test","known mode");
        foreach(Spec spec in Specs)
        {
            Fixture f=Create(spec);
            if(mode=="test") {rows.Add(WarmCheck(f));continue;}
            Preflight(f);
            if(mode=="bench") {rows.Add(Measure(f,fixedCounts[spec.Id]));continue;}
            int count=spec.N>=1024?64:10000;
            while(true)
            {
                Row r=Measure(f,count);attempts.Add(r);
                if(r.WallMilliseconds>=target) {rows.Add(r);break;}
                double next=Math.Max(count+1,Math.Ceiling(count*(target/Math.Max(.001,r.WallMilliseconds))*1.10));
                Check(next<=int.MaxValue,"calibration count limit before target duration");count=(int)next;
            }
        }
        Report(mode,rows,attempts);GC.KeepAlive(Sink);
    }
    private sealed class ReferenceProxy : IGameplayTagContainer
    {
        readonly IGameplayTagContainer owner;
        public ReferenceProxy(IGameplayTagContainer owner){this.owner=owner;}
        public bool IsEmpty=>owner.IsEmpty;public int TagCount=>owner.TagCount;public int ExplicitTagCount=>owner.ExplicitTagCount;
        public GameplayTagContainerIndices Indices=>owner.Indices;public GameplayTagEnumerator GetTags()=>owner.GetTags();public GameplayTagEnumerator GetExplicitTags()=>owner.GetExplicitTags();
        public IEnumerator<GameplayTag> GetEnumerator()=>owner.GetEnumerator();IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        public void AddTag(GameplayTag tag)=>throw new NotSupportedException();public void RemoveTag(GameplayTag tag)=>throw new NotSupportedException();
        public void AddTags<T>(in T other)where T:IGameplayTagContainer=>throw new NotSupportedException();public void RemoveTags<T>(in T other)where T:IGameplayTagContainer=>throw new NotSupportedException();public void Clear()=>throw new NotSupportedException();
        public void GetParentTags(GameplayTag tag,List<GameplayTag> output)=>throw new NotSupportedException();public void GetChildTags(GameplayTag tag,List<GameplayTag> output)=>throw new NotSupportedException();
        public void GetExplicitParentTags(GameplayTag tag,List<GameplayTag> output)=>throw new NotSupportedException();public void GetExplicitChildTags(GameplayTag tag,List<GameplayTag> output)=>throw new NotSupportedException();
    }
    private readonly struct ValueProxy : IGameplayTagContainer
    {
        readonly IGameplayTagContainer owner;
        public ValueProxy(IGameplayTagContainer owner){this.owner=owner;}
        public bool IsEmpty=>owner.IsEmpty;public int TagCount=>owner.TagCount;public int ExplicitTagCount=>owner.ExplicitTagCount;
        public GameplayTagContainerIndices Indices=>owner.Indices;public GameplayTagEnumerator GetTags()=>owner.GetTags();public GameplayTagEnumerator GetExplicitTags()=>owner.GetExplicitTags();
        public IEnumerator<GameplayTag> GetEnumerator()=>owner.GetEnumerator();IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        public void AddTag(GameplayTag tag)=>throw new NotSupportedException();public void RemoveTag(GameplayTag tag)=>throw new NotSupportedException();
        public void AddTags<T>(in T other)where T:IGameplayTagContainer=>throw new NotSupportedException();public void RemoveTags<T>(in T other)where T:IGameplayTagContainer=>throw new NotSupportedException();public void Clear()=>throw new NotSupportedException();
        public void GetParentTags(GameplayTag tag,List<GameplayTag> output)=>throw new NotSupportedException();public void GetChildTags(GameplayTag tag,List<GameplayTag> output)=>throw new NotSupportedException();
        public void GetExplicitParentTags(GameplayTag tag,List<GameplayTag> output)=>throw new NotSupportedException();public void GetExplicitChildTags(GameplayTag tag,List<GameplayTag> output)=>throw new NotSupportedException();
    }
}
