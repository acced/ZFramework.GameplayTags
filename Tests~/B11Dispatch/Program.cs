using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GameplayTags;

internal static class Program
{
    static long sink, assertions;
    static readonly Stopwatch Whole = Stopwatch.StartNew();
    static readonly string[] Patterns = { "separated", "interleaved", "low25", "high100" };
    static readonly List<Row> Rows = new List<Row>();
    static readonly List<string> Validations = new List<string>();
    static bool ValidationOnly;
    static StreamWriter RawWriter;
    sealed class Row { public string Case, Operation, Algorithm; public int Pair, Calls, Count, G0,G1,G2; public double Ms, Ns, Bytes; }
    static int Find(List<GameplayTag> list, string name) { int lo=0, hi=list.Count-1; while(lo<=hi) { int mid=lo+((hi-lo)>>1); int c=string.CompareOrdinal(list[mid].Name,name); if(c==0)return mid; if(c<0)lo=mid+1; else hi=mid-1; } return ~lo; }
    static bool Separated(List<GameplayTag> a,List<GameplayTag>b) => a.Count==0||b.Count==0||string.CompareOrdinal(a[a.Count-1].Name,b[0].Name)<0||string.CompareOrdinal(b[b.Count-1].Name,a[0].Name)<0;
    static void IntersectBinary(GameplayTagContainer lhs,GameplayTagContainer rhs,GameplayTagContainer result,bool range=false) {
        var a=lhs.Tags;var b=rhs.Tags;var o=result.Tags;o.Clear();result.EnsureCapacity(Math.Min(a.Count,b.Count));
        if(range&&Separated(a,b))return;
        if(a.Count<=b.Count) { for(int i=0;i<a.Count;i++)if(Find(b,a[i].Name)>=0)o.Add(a[i]); }
        else {for(int i=0;i<b.Count;i++){int k=Find(a,b[i].Name);if(k>=0)o.Add(a[k]);}}
    }
    static void ExceptBinary(GameplayTagContainer lhs,GameplayTagContainer rhs,GameplayTagContainer result) {
        var a=lhs.Tags;var b=rhs.Tags;var o=result.Tags;bool alias=ReferenceEquals(lhs,result);if(!alias){o.Clear();result.EnsureCapacity(a.Count);}int n=a.Count,w=0;
        for(int i=0;i<n;i++){var v=a[i];if(Find(b,v.Name)>=0)continue;if(alias)o[w++]=v;else o.Add(v);}if(alias)o.RemoveRange(w,o.Count-w);
    }
    static void ExceptFindRemove(GameplayTagContainer lhs,GameplayTagContainer rhs,GameplayTagContainer result) {
        var o=result.Tags;if(!ReferenceEquals(lhs,result)){o.Clear();result.EnsureCapacity(lhs.Count);o.AddRange(lhs.Tags);}
        // Descending removals reduce shifting and preserve lhs representatives.
        for(int i=rhs.Count-1;i>=0;i--){int k=Find(o,rhs[i].Name);if(k>=0)o.RemoveAt(k);}
    }
    static GameplayTagContainer Make(IEnumerable<int> keys) {var c=new GameplayTagContainer();foreach(int k in keys.OrderBy(x=>x))c.Tags.Add(new GameplayTag("Ability.Common.Branch.Tag"+k.ToString("D9",CultureInfo.InvariantCulture)));return c;}
    static (GameplayTagContainer,GameplayTagContainer) Inputs(int small,int large,string pattern) {
        var big=Make(Enumerable.Range(0,large).Select(x=>x*4)); var keys=new int[small];
        for(int i=0;i<small;i++) {int pos=(int)(((long)(i+1)*large)/(small+1)); if(pattern=="separated")keys[i]=-small+i;
          else if(pattern=="interleaved")keys[i]=pos*4+1;
          else if(pattern=="low25")keys[i]=pos*4+(i%4==0?0:1);
          else keys[i]=pos*4; }
        // Sort by actual ordinal name, including the negative-key separated run.
        var little=Make(keys);little.Tags.Sort((a,b)=>string.CompareOrdinal(a.Name,b.Name));return(little,big);
    }
    static List<GameplayTag> Expected(GameplayTagContainer a,GameplayTagContainer b,bool intersect){var set=new HashSet<string>(b.Tags.Select(x=>x.Name),StringComparer.Ordinal);return a.Tags.Where(x=>set.Contains(x.Name)==intersect).ToList();}
    static void Validate(GameplayTagContainer actual,List<GameplayTag> expected,string label){if(actual.Count!=expected.Count)throw new Exception(label+" count "+actual.Count+" != "+expected.Count);for(int i=0;i<expected.Count;i++){if(!string.Equals(actual[i].Name,expected[i].Name,StringComparison.Ordinal)||!ReferenceEquals(actual[i].Name,expected[i].Name))throw new Exception(label+" member/reference "+i);if(i>0&&string.CompareOrdinal(actual[i-1].Name,actual[i].Name)>=0)throw new Exception(label+" order");assertions++;}}
    static double Time(Action action,int calls){long st=Stopwatch.GetTimestamp();for(int i=0;i<calls;i++)action();return(Stopwatch.GetTimestamp()-st)*1000d/Stopwatch.Frequency;}
    static void Group(string id,string op,GameplayTagContainer a,GameplayTagContainer b,bool alias,string[] names,Action<GameplayTagContainer,GameplayTagContainer,GameplayTagContainer>[] algorithms) {
        if(Whole.Elapsed.TotalSeconds>180)throw new Exception("Frozen 180 second whole-run ceiling exceeded; no full experiment claim.");
        bool intersect=op=="intersection";var expected=Expected(a,b,intersect);var result=new GameplayTagContainer(Math.Max(a.Count,b.Count));var scratch=new GameplayTagContainer(a.Count);
        Action[] actions=new Action[algorithms.Length];for(int k=0;k<actions.Length;k++){int ix=k;actions[k]=()=>{var target=result;var source=a;if(alias){scratch.Tags.Clear();scratch.Tags.AddRange(a.Tags);target=scratch;source=scratch;}algorithms[ix](source,b,target);sink+=target.Count;if(target.Count>0)sink+=target[target.Count-1].Name.Length;};actions[k]();Validate(alias?scratch:result,expected,id+" "+op+" "+names[k]);}
        string digest=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",expected.Select(x=>x.Name))))).ToLowerInvariant();
        foreach(string name in names)Validations.Add(id+","+op+(alias?"-inplace-reset":"-output")+","+name+","+expected.Count+","+digest);
        if(ValidationOnly)return;
        // Baseline-derived count: four 25 ms baseline samples (~100 ms total).
        int pilot=1;double ms;do{ms=Time(actions[0],pilot);if(ms<8)pilot*=2;}while(ms<8&&pilot<(1<<25));
        int baselineCalls=Math.Max(1,Math.Min(1<<22,(int)(pilot*25d/ms)));int[] calls=new int[actions.Length];calls[0]=baselineCalls;
        for(int k=1;k<actions.Length;k++){int cp=1;double p;do{p=Time(actions[k],cp);if(p<2)cp*=2;}while(p<2&&cp<baselineCalls);calls[k]=Math.Max(1,Math.Min(baselineCalls,(int)(cp*30d/Math.Max(p,.001))));}
        for(int pair=0;pair<4;pair++)for(int offset=0;offset<actions.Length;offset++){int k=pair%2==0?offset:actions.Length-1-offset;
            long bytes=GC.GetAllocatedBytesForCurrentThread();int g0=GC.CollectionCount(0),g1=GC.CollectionCount(1),g2=GC.CollectionCount(2);double elapsed=Time(actions[k],calls[k]);long allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;
            var row=new Row {Case=id,Operation=op+(alias?"-inplace-reset":"-output"),Algorithm=names[k],Pair=pair,Calls=calls[k],Count=expected.Count,Ms=elapsed,Ns=elapsed*1e6/calls[k],Bytes=(double)allocated/calls[k],G0=GC.CollectionCount(0)-g0,G1=GC.CollectionCount(1)-g1,G2=GC.CollectionCount(2)-g2};Rows.Add(row);
            RawWriter.WriteLine(string.Join(",",row.Case,row.Operation,row.Algorithm,row.Pair,row.Calls,row.Count,row.Ms.ToString("F6",CultureInfo.InvariantCulture),row.Ns.ToString("F3",CultureInfo.InvariantCulture),row.Bytes.ToString("F3",CultureInfo.InvariantCulture),row.G0,row.G1,row.G2));RawWriter.Flush();
            Validate(alias?scratch:result,expected,id+" "+op+" "+names[k]);
        }
    }
    static int Main(string[] args) { try { Run(args); return 0; } catch(Exception ex) { Console.Error.WriteLine("FAIL "+ex); return 1; } finally { RawWriter?.Dispose(); } }
    static void Run(string[] args) {
        ValidationOnly=args.Contains("--validate");
        string output=args.Length>0?args[0]:"results";Directory.CreateDirectory(output);
        RawWriter=new StreamWriter(Path.Combine(output,"raw.csv"));RawWriter.WriteLine("case,operation,algorithm,pair,calls,expectedCount,elapsedMs,nsPerOperation,bytesPerOperation,gc0,gc1,gc2");RawWriter.Flush();
        Console.WriteLine("Runtime="+Environment.Version+"; ProcessorCount="+Environment.ProcessorCount+"; StopwatchFrequency="+Stopwatch.Frequency+"; UTC="+DateTime.UtcNow.ToString("O"));
        var sizes=new[]{(1024,1024),(4,1024),(4,100000)};
        foreach(var size in sizes)foreach(var pattern in Patterns){var (small,big)=Inputs(size.Item1,size.Item2,pattern);int dirs=size.Item1==size.Item2?1:2;
          for(int dir=0;dir<dirs;dir++){var a=dir==0?small:big;var b=dir==0?big:small;string id=a.Count+"x"+b.Count+"-"+pattern;
            Group(id,"intersection",a,b,false,new[]{"merge","binary","binary-range"},new Action<GameplayTagContainer,GameplayTagContainer,GameplayTagContainer>[] {GameplayTagContainerAlgorithms.IntersectionExactSorted,(x,y,z)=>IntersectBinary(x,y,z),(x,y,z)=>IntersectBinary(x,y,z,true)});
            foreach(bool alias in new[]{false,true})Group(id,"difference",a,b,alias,new[]{"merge","source-binary","find-remove"},new Action<GameplayTagContainer,GameplayTagContainer,GameplayTagContainer>[] {GameplayTagContainerAlgorithms.ExceptSorted,ExceptBinary,ExceptFindRemove});
            Console.WriteLine("done "+id);
          }
        }
        RawWriter.Flush();
        using(var w=new StreamWriter(Path.Combine(output,"summary.csv"))){w.WriteLine("case,operation,algorithm,medianNs,minNs,maxNs,medianBaselineOverCandidate,bytesPerOperation,gc0,gc1,gc2");foreach(var g in Rows.GroupBy(x=>(x.Case,x.Operation,x.Algorithm))){var ns=g.Select(x=>x.Ns).OrderBy(x=>x).ToArray();var ratios=g.Select(x=>Rows.Single(y=>y.Case==x.Case&&y.Operation==x.Operation&&y.Pair==x.Pair&&y.Algorithm=="merge").Ns/x.Ns).OrderBy(x=>x).ToArray();w.WriteLine(string.Join(",",g.Key.Case,g.Key.Operation,g.Key.Algorithm,((ns[1]+ns[2])/2).ToString("F3",CultureInfo.InvariantCulture),ns[0].ToString("F3",CultureInfo.InvariantCulture),ns[3].ToString("F3",CultureInfo.InvariantCulture),((ratios[1]+ratios[2])/2).ToString("F3",CultureInfo.InvariantCulture),g.Max(x=>x.Bytes),g.Sum(x=>x.G0),g.Sum(x=>x.G1),g.Sum(x=>x.G2)));}}
        string validationText="case,operation,algorithm,expectedCount,validatedNamesSha256\n"+string.Join("\n",Validations)+"\n";
        File.WriteAllText(Path.Combine(output,"validation.csv"),validationText);
        Console.WriteLine("Validation ledger SHA256="+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(validationText))).ToLowerInvariant());
        Console.WriteLine("PASS full ordered-member and lhs-reference validation; assertions="+assertions+"; rows="+Rows.Count+"; sink="+sink);
    }
}
