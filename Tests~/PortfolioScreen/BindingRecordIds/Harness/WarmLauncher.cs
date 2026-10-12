using System;
using System.Globalization;
using System.Reflection;

// Selects unchanged fixture methods; reflection is outside Warm's existing timer.
internal static class WarmBindingLauncher
{
    private static readonly BindingFlags Methods = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly int[] Selected = { 0, 1, 2, 4, 6, 7, 8 };
    private static int Main(string[] args)
    {
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            bool pilot = args.Length == 1 && args[0] == "pilot";
            if (!pilot && (args.Length != 3 || args[0] != "timing")) throw new ArgumentException("pilot or timing id count required");
            Type program = typeof(Program);
            string[] ids = (string[])program.GetField("Ids", Methods).GetValue(null);
            int[] seeds = (int[])program.GetField("Seeds", Methods).GetValue(null);
            program.GetMethod("Init", Methods).Invoke(null, null);
            if (pilot)
            {
                foreach (int index in Selected) Invoke(program, index, seeds[index], "pilot");
            }
            else
            {
                int index = Array.IndexOf(ids, args[1]);
                int count = int.Parse(args[2], CultureInfo.InvariantCulture);
                if (Array.IndexOf(Selected, index) < 0 || count < seeds[index]) throw new ArgumentException("Fixed selected row and frozen count required");
                Invoke(program, index, count, "timing");
            }
            return 0;
        }
        catch (TargetInvocationException ex) { Console.Error.WriteLine(ex.InnerException ?? ex); return 1; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Invoke(Type program, int index, int count, string mode)
    {
        object row = program.GetMethod("Make", Methods).Invoke(null, new object[] { index });
        program.GetMethod("Warm", Methods).Invoke(null, new object[] { row, count, mode });
    }
}
