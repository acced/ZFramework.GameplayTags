using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using GameplayTags;

internal static class Cold
{
    private static object Sink;
    private static long Events;
    private static void Main(string[] args)
    {
        string mode = args[0]; int n = int.Parse(args[1], CultureInfo.InvariantCulture);
        string deep = "Deep"; for (int i = 1; i < 128; i++) deep += ".N";
        var declarations = new GameplayTagRegistration[2049];
        declarations[2048] = new GameplayTagRegistration(deep);
        for (int i = 0; i < 2048; i++) declarations[i] = new GameplayTagRegistration("Root.G"+(i%32)+".T"+i);
        GameplayTagManager.Initialize(declarations);
        var tags = new GameplayTag[2048];for(int i=0;i<tags.Length;i++)tags[i]=GameplayTagManager.RequestTag(declarations[i].Name);
        GameplayTag selected = mode == "fresh-deep" ? GameplayTagManager.RequestTag(deep) : tags[0];
        var a = new GameplayTagContainer(); var b = new GameplayTagContainer(); var output = new GameplayTagContainer();
        var counted = new GameplayTagCountContainer();
        Action action;
        if(mode=="fresh" || mode=="fresh-deep") action=()=>{output.AddTag(selected);Sink=output;};
        else if(mode=="clear")
        {for(int i=0;i<n;i++)output.AddTag(tags[i]);action=()=>{output.Clear();Sink=output;};}
        else if(mode=="buff")
        {counted.OnAnyTagCountChange+=(tag,c)=>{Events+=c;};action=()=>{counted.AddTag(tags[0]);Sink=counted;};}
        else
        {
            for(int i=0;i<n;i++){a.AddTag(tags[i]);b.AddTag(tags[i+n/2]);}
            if(mode=="intersection")action=()=>{GameplayTagContainer.Intersection(output,a,b);Sink=output;};
            else if(mode=="copy")action=()=>{GameplayTagContainer.Copy(output,a);Sink=output;};
            else if(mode=="union")action=()=>{GameplayTagContainer.Union(output,a,b);Sink=output;};
            else throw new ArgumentException("Unknown fixture");
        }
        var method=typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",Type.EmptyTypes);
        if(method==null)throw new NotSupportedException("Per-thread allocation counter required");
        var allocated=(Func<long>)Delegate.CreateDelegate(typeof(Func<long>),method);
        allocated();Stopwatch.GetTimestamp();GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        long before=allocated(),start=Stopwatch.GetTimestamp();action();long ticks=Stopwatch.GetTimestamp()-start,bytes=allocated()-before;
        int count=mode=="buff"?counted.ExplicitTagCount:output.ExplicitTagCount;
        int expected=mode=="clear"?0:mode=="intersection"?n/2:mode=="union"?n+n/2:mode=="copy"?n:1;
        if(count!=expected)throw new Exception("First-use state mismatch");
        Console.WriteLine("{\"Mode\":\""+mode+"\",\"N\":"+n+",\"Ns\":"+(ticks*(1e9/Stopwatch.Frequency)).ToString("R",CultureInfo.InvariantCulture)+",\"Bytes\":"+bytes+",\"ExplicitCount\":"+count+",\"TagCount\":"+(mode=="buff"?counted.TagCount:output.TagCount)+",\"Events\":"+Events+"}");GC.KeepAlive(Sink);
    }
}
