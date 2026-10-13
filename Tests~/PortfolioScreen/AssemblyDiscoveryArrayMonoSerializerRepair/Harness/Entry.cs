using System;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

internal static class PortableDiscoveryEntry
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) throw new ArgumentException("semantic or cold mode required");
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            Type mono = Type.GetType("Mono.Runtime"); string monoVersion = "";
            if (mono != null)
            {
                string display = (string)mono.GetMethod("GetDisplayName", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                Match match = Regex.Match(display, @"^\d+\.\d+\.\d+\.\d+");
                if (!match.Success) throw new InvalidOperationException("Unrecognized Mono version");
                monoVersion = match.Value;
            }
            Console.WriteLine("PORTABLE_RUNTIME {\"EnvironmentVersion\":\"" + Environment.Version + "\",\"MonoVersion\":\"" + monoVersion + "\",\"PointerSize\":" + IntPtr.Size + "}");
            // Type selection and harness dispatch do not inspect or initialize the manager.
            Type target; int count;
            if (args[0] == "semantic" && args.Length == 5) { target = typeof(DiscoverySemanticCheck); count = 4; }
            else if (args[0] == "cold" && args.Length == 8) { target = typeof(DiscoveryArrayCheck); count = 7; }
            else throw new ArgumentException("Fixed portable mode arguments required");
            string[] forwarded = new string[count]; Array.Copy(args, 1, forwarded, 0, count);
            int result = (int)target.GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { forwarded });
            return result;
        }
        catch (TargetInvocationException ex) { Console.Error.WriteLine(ex.InnerException ?? ex); return 1; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
