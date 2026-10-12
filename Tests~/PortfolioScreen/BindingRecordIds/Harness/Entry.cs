using System;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

internal static class PortableBindingEntry
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) throw new ArgumentException("semantic, cold or warm mode required");
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            Type mono = Type.GetType("Mono.Runtime"); string monoVersion = "";
            if (mono != null)
            {
                string display = (string)mono.GetMethod("GetDisplayName", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                Match match = Regex.Match(display, @"^\d+\.\d+\.\d+\.\d+");
                if (!match.Success) throw new InvalidOperationException("Unrecognized Mono version");
                monoVersion = match.Value;
            }
            Console.WriteLine("PORTABLE_RUNTIME {\"EnvironmentVersion\":\"" + Environment.Version + "\",\"MonoVersion\":\"" + monoVersion + "\",\"PointerSize\":" + IntPtr.Size + "}");
            Type target; string[] forwarded;
            if (args[0] == "semantic" && args.Length == 4) target = typeof(BindingRecordCheck);
            else if (args[0] == "cold" && args.Length == 2) target = typeof(ColdBindingRecordScreen);
            else if (args[0] == "warm" && args.Length == 4 && args[1] == "timing") target = typeof(WarmBindingLauncher);
            else throw new ArgumentException("Fixed portable mode arguments required");
            forwarded = new string[args.Length - 1]; Array.Copy(args, 1, forwarded, 0, forwarded.Length);
            return (int)target.GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { forwarded });
        }
        catch (TargetInvocationException ex) { Console.Error.WriteLine(ex.InnerException ?? ex); return 1; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
