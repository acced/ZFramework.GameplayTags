using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Set=GameplayTags.Experiments.DirectArraySet;
internal static class CountBits
{
    static long checks;
    static int Oracle(ulong value) { int count=0;for(int i=0;i<64;i++) { count+=(int)(value&1UL);value>>=1; }return count; }
    static void Equal(int actual,int expected,string label) {checks++;if(actual!=expected)throw new Exception(label+": "+actual+" != "+expected);}
    static int Main(string[] args)
    {
        var pop32=(Func<uint,int>)typeof(Set).GetMethod("Pop32",BindingFlags.NonPublic|BindingFlags.Static).CreateDelegate(typeof(Func<uint,int>));
        var pop64=(Func<ulong,int>)typeof(Set).GetMethod("Pop64",BindingFlags.NonPublic|BindingFlags.Static).CreateDelegate(typeof(Func<ulong,int>));
        for(uint value=0;value<65536;value++)
        {
            int expected=Oracle(value);
            Equal(pop32(value),expected,"exhaustive low16");Equal(pop32(value<<16),expected,"exhaustive high16");
            Equal(pop64((ulong)value<<32),expected,"exhaustive middle-high16");Equal(pop64((ulong)value<<48),expected,"exhaustive high16-64");
            Equal(pop64(value|((ulong)value<<16)|((ulong)value<<32)|((ulong)value<<48)),4*expected,"exhaustive repeated masks");
        }
        for(int bit=0;bit<64;bit++)
        {
            ulong value=1UL<<bit;Equal(pop64(value),1,"single64");Equal(pop64(~value),63,"complement64");
            if(bit<32) {uint v=1U<<bit;Equal(pop32(v),1,"single32");Equal(pop32(~v),31,"complement32");}
        }
        foreach(ulong value in new[]{0UL,ulong.MaxValue,0xAAAAAAAAAAAAAAAAUL,0x5555555555555555UL,0x8000000080000000UL,0x00000000FFFFFFFFUL,0xFFFFFFFF00000000UL})
        {Equal(pop64(value),Oracle(value),"fixed64");uint low=unchecked((uint)value);Equal(pop32(low),Oracle(low),"fixed32");}
        ulong state=0x987654321ABCDEF0UL;
        for(int i=0;i<100000;i++)
        {
            state^=state<<13;state^=state>>7;state^=state<<17;
            Equal(pop64(state),Oracle(state),"random64");uint low=unchecked((uint)state);Equal(pop32(low),Oracle(low),"random32");
        }
        var result=new {pass=true,checks,exhaustive16=65536,random64=100000,random32=100000,hardware=Vector.IsHardwareAccelerated,oracle="independent 64-step bit loop",lastState=state};
        string json=JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[0],json);Console.WriteLine(json);return 0;
    }
}
