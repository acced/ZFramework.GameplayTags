using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using EvolutionAdjacent;

internal static class Host
{
    private static readonly Process Process=Process.GetCurrentProcess();
    private static readonly Func<long> Allocated=Counter();
    private static Func<long> Counter()
    {
        var method=typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",Type.EmptyTypes);
        if(method==null)throw new NotSupportedException("Allocation counter unavailable");
        return (Func<long>)Delegate.CreateDelegate(typeof(Func<long>),method);
    }
    private static string Q(string s)=>"\""+s.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n")+"\"";
    private static string N(double n)=>n.ToString("R",CultureInfo.InvariantCulture);
    private static string Json(Sample s)=>"{\"Id\":"+Q(s.Id)+",\"Iterations\":"+s.Iterations+",\"Ns\":"+N(s.Ns)+",\"Bytes\":"+N(s.Bytes)+",\"WallMilliseconds\":"+N(s.WallMilliseconds)+",\"CpuMilliseconds\":"+N(s.CpuMilliseconds)+",\"Gen0\":"+s.Gen0+",\"Gen1\":"+s.Gen1+",\"Gen2\":"+s.Gen2+",\"WarmupChecksum\":"+s.WarmupChecksum+",\"MeasuredChecksum\":"+s.MeasuredChecksum+"}";
    private static Sample Measure(Request request,int iterations)
    {
        // Exactly the existing single Action invocation per operation. No reflection,
        // bridge call, cross-thread request or extra wrapper occurs inside the timer.
        Action action=request.Action;
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        long checksum=request.Checksum();
        long cpu=Process.TotalProcessorTime.Ticks;
        int g0=GC.CollectionCount(0),g1=GC.CollectionCount(1),g2=GC.CollectionCount(2);
        long allocation=Allocated(),start=Stopwatch.GetTimestamp();
        for(int i=0;i<iterations;i++)action();
        long ticks=Stopwatch.GetTimestamp()-start;long bytes=Allocated()-allocation;
        long cpuTicks=Process.TotalProcessorTime.Ticks-cpu;
        return new Sample{Id=request.Id,Iterations=iterations,Ns=ticks*1e9/Stopwatch.Frequency/iterations,Bytes=(double)bytes/iterations,WallMilliseconds=ticks*1000.0/Stopwatch.Frequency,CpuMilliseconds=cpuTicks/10000.0,Gen0=GC.CollectionCount(0)-g0,Gen1=GC.CollectionCount(1)-g1,Gen2=GC.CollectionCount(2)-g2,MeasuredChecksum=unchecked(request.Checksum()-checksum)};
    }
    private static void Main(string[] args)
    {
        var options=new Dictionary<string,string>();for(int i=0;i<args.Length;i+=2)options.Add(args[i],args[i+1]);
        int round=int.Parse(options["--round"]),padding=int.Parse(options["--padding"]);
        bool aa=options["--mode"]=="aa",inspect=options["--inspect"]=="1";
        var iterations=new Dictionary<string,int>();foreach(var line in File.ReadAllLines(options["--iterations"])) {var split=line.Split('\t');iterations.Add(split[0],int.Parse(split[1]));}
        string[] labels=options.ContainsKey("--assembly-previous")?new[]{"before","previous","after"}:new[]{"before","after"};
        var endpoints=labels.Take(aa?1:labels.Length).Select(_=>new Endpoint()).ToArray();var threads=new List<Thread>();
        using(var writer=new StreamWriter(options["--out"]))
        {
            writer.AutoFlush=true;
            writer.WriteLine("{\"Type\":\"environment\",\"Protocol\":\"guard-recheck-v1\",\"Round\":"+round+",\"Runtime\":"+Q(Environment.Version.ToString())+",\"StandaloneMono\":"+(Type.GetType("Mono.Runtime")!=null?"true":"false")+",\"PointerSize\":"+IntPtr.Size+",\"Padding\":"+padding+",\"Labels\":["+string.Join(",",labels.Select(Q))+"]}");
            for(int index=0;index<endpoints.Length;index++)
            {
                int selected=index;
                var thread=new Thread(()=>
                {
                    Bridge.Current=endpoints[selected];
                    try
                    {
                        Assembly assembly=Assembly.LoadFrom(options["--assembly-"+labels[selected]]);
                        MethodInfo main=assembly.GetType("Program",true).GetMethod("Main",BindingFlags.Static|BindingFlags.NonPublic);
                        main.Invoke(null,new object[]{new[]{"bench",padding.ToString(CultureInfo.InvariantCulture)}});
                    }
                    catch(Exception e){Bridge.Fail(e is TargetInvocationException && e.InnerException!=null?e.InnerException:e);}
                });
                thread.IsBackground=true;threads.Add(thread);thread.Start();
            }
            var seen=new HashSet<string>();int caseIndex=0; int traversed=0;
            try
            {
                while(true)
                {
                    foreach(var endpoint in endpoints)endpoint.Ready.WaitOne();
                    foreach(var endpoint in endpoints)if(endpoint.Pending.Error!=null)throw endpoint.Pending.Error;
                    if(endpoints.Any(e=>e.Pending.Complete))
                    {
                        if(!endpoints.All(e=>e.Pending.Complete))throw new Exception("Variant case counts differ");
                        long final=endpoints[0].Pending.FinalChecksum;int count=endpoints[0].Pending.RegistryCount;
                        if(endpoints.Any(e=>e.Pending.FinalChecksum!=final||e.Pending.RegistryCount!=count))throw new Exception("Final semantic checksum or registry size differs");
                        if(!seen.SetEquals(iterations.Keys))throw new Exception("Iteration file does not exactly match timed case IDs");
                        writer.WriteLine("{\"Type\":\"complete\",\"Cases\":"+caseIndex+",\"Checksum\":"+final+",\"RegistryCount\":"+count+",\"RetainedProtocol\":\"omitted: isolated runner required\"}");
                        foreach(var endpoint in endpoints)endpoint.Resume.Set();
                        foreach(var thread in threads)thread.Join();
                        break;
                    }
                    string id=endpoints[0].Pending.Id;
                    if(endpoints.Any(e=>e.Pending.Id!=id))throw new Exception("Variant case IDs/order differ");
                    if(!seen.Add(id)||!iterations.ContainsKey(id)||iterations[id]<1)throw new Exception("Missing/duplicate/invalid iteration entry: "+id);
                    traversed++;
                    bool selected=id.StartsWith("n4/overlap",StringComparison.Ordinal)&&(id.EndsWith("/intersection-reused",StringComparison.Ordinal)||id.EndsWith("/workspace-reused",StringComparison.Ordinal));
                    if(!selected) {
                        foreach(var endpoint in endpoints){for(int w=0;w<65;w++)endpoint.Pending.Action();endpoint.Pending.Result=new Sample{Id=id,Iterations=65};}
                        foreach(var endpoint in endpoints)endpoint.Resume.Set();
                        continue;
                    }
                    if(inspect)foreach(var endpoint in endpoints)Inspection.Write(endpoint.Pending.Action,options["--out"]+".il.txt");
                    int[] order=Enumerable.Range(0,labels.Length).ToArray();if((round+caseIndex)%2!=0)Array.Reverse(order);
                    var warmup=new long[labels.Length];
                    // Both producers are parked. Warm each action before either is measured.
                    foreach(int index in order)
                    {
                        Request r=endpoints[aa?0:index].Pending;long before=r.Checksum();
                        for(int i=0;i<64;i++)r.Action();
                        warmup[index]=unchecked(r.Checksum()-before);
                    }
                    var samples=new Sample[labels.Length];
                    foreach(int index in order)
                    {
                        samples[index]=Measure(endpoints[aa?0:index].Pending,inspect?1:iterations[id]);samples[index].WarmupChecksum=warmup[index];endpoints[aa?0:index].Pending.Result=samples[index];
                    }
                    if(samples.Any(s=>s.WarmupChecksum!=samples[0].WarmupChecksum||s.MeasuredChecksum!=samples[0].MeasuredChecksum))throw new Exception("Per-case observed checksum differs: "+id);
                    writer.WriteLine("{\"Type\":\"case\",\"Id\":"+Q(id)+",\"Order\":["+string.Join(",",order.Select(i=>Q(labels[i])))+"],\"Samples\":{"+string.Join(",",Enumerable.Range(0,labels.Length).Select(i=>Q(labels[i])+":"+Json(samples[i])))+"}}");
                    caseIndex++;
                    // Release only after every version's timing finished. Both producers
                    // may now perform postflight and prepare their next matching case.
                    foreach(var endpoint in endpoints)endpoint.Resume.Set();
                }
            }
            catch(Exception error)
            {
                writer.WriteLine("{\"Type\":\"failure\",\"Message\":"+Q(error.ToString())+"}");throw;
            }
        }
    }
}
