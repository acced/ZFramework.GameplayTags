using System;
using System.Runtime.CompilerServices;
using GameplayTags;

public static class RuleTests
{
    static GameplayTagRequirements sink;
    public static void Main()
    {
        RuleChecks.Initialize();
        Console.WriteLine("PASS fixed-rules assertions="+RuleChecks.Run());
        var f=new[]{RuleChecks.Leaves[70]};var r=new[]{RuleChecks.Leaves[0],RuleChecks.Leaves[1]};
        for(int i=0;i<100;i++)sink=new GameplayTagRequirements(f,r);
        long start=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++)sink=new GameplayTagRequirements(f,r);
        long bytes=GC.GetAllocatedBytesForCurrentThread()-start;
        RuleChecks.Require(bytes==0,"small rules have zero heap allocation with reused inputs");
        Console.WriteLine("Small creation bytes/10000="+bytes+" struct-size="+Unsafe.SizeOf<GameplayTagRequirements>());
        var large=new GameplayTag[16];Array.Fill(large,RuleChecks.Leaves[0]);
        for(int i=0;i<100;i++)sink=new GameplayTagRequirements(null,large);
        start=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++)sink=new GameplayTagRequirements(null,large);
        Console.WriteLine("16 conditions creation .NET bytes/op="+(GC.GetAllocatedBytesForCurrentThread()-start)/10000.0);
        GC.KeepAlive(sink);
    }
}
