using System;
class OptimizationTests
{
    static int Main()
    {
        try { OptimizationChecks.Run(); AdditionalChecks.Run(); Console.WriteLine("PASS optimization assertions="+OptimizationChecks.Assertions+" additional="+AdditionalChecks.Assertions); return 0; }
        catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
