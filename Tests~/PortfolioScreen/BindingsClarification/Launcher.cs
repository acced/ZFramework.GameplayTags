using System;
using System.Globalization;
using System.Reflection;

internal static class DiagnosticLauncher
{
    static readonly BindingFlags Methods = BindingFlags.Static | BindingFlags.NonPublic;
    static int Main(string[] args)
    {
        try
        {
            if (args.Length != 4) throw new ArgumentException("assembly, row index, count and mode required");
            int index = int.Parse(args[1], CultureInfo.InvariantCulture);
            int count = int.Parse(args[2], CultureInfo.InvariantCulture);
            if ((index != 7 && index != 8) || count <= 0 || (args[3] != "timing" && args[3] != "preflight"))
                throw new ArgumentException("fixed unchanged control required");
            Assembly original = Assembly.LoadFrom(args[0]);
            Type program = original.GetType("Program", true);
            program.GetMethod("Init", Methods).Invoke(null, null);
            object row = program.GetMethod("Make", Methods).Invoke(null, new object[] { index });
            // Loading, reflection, registration, fixture construction and this invocation occur
            // outside Warm's unchanged internal timer and allocation-counter boundary.
            program.GetMethod("Warm", Methods).Invoke(null, new object[] { row, count, args[3] });
            return 0;
        }
        catch (TargetInvocationException exception)
        {
            Console.Error.WriteLine(exception.InnerException ?? exception);
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}
