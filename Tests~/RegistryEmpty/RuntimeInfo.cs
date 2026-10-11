using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
internal static class RuntimeInfo
{
    static void Main()
    {
        using(var process=System.Diagnostics.Process.GetCurrentProcess()) Console.WriteLine("RuntimeExecutable="+process.MainModule.FileName);
        Console.WriteLine("RuntimeVersion="+Environment.Version);
        Console.WriteLine("Is64BitProcess="+Environment.Is64BitProcess);
        Console.WriteLine("OperatingSystem="+Environment.OSVersion);
        Type mono=Type.GetType("Mono.Runtime");
        if(mono!=null)Console.WriteLine("MonoDisplayName="+mono.GetMethod("GetDisplayName",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null));
        Console.WriteLine("Assembly.Core="+typeof(object).Assembly.Location);
        Console.WriteLine("Assembly.Linq="+typeof(Enumerable).Assembly.Location);
        Console.WriteLine("Assembly.Serialization="+typeof(DataContractSerializer).Assembly.Location);
        Console.WriteLine("Assembly.System="+typeof(Uri).Assembly.Location);
        Console.WriteLine("Assembly.Xml="+typeof(System.Xml.XmlReader).Assembly.Location);
    }
}
