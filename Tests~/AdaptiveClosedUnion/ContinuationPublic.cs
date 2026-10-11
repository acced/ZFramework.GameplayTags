using System;
using System.Collections.Generic;
using GameplayTags;
internal static class Program
{
    sealed class NormalView : GameplayTagContainer, IGameplayTagContainer
    {
        readonly IGameplayTagContainer inner;
        internal NormalView(IGameplayTagContainer value) { inner=value; }
        GameplayTagContainerIndices IGameplayTagContainer.Indices => inner.Indices;
    }
    static void Main()
    {
        const int fillers=1023, depth=1063, tail=64;
        var names=new string[depth]; names[0]="Path"; for(int i=1;i<depth;i++)names[i]=names[i-1]+".N";
        var registrations=new List<GameplayTagRegistration>();registrations.Add(new GameplayTagRegistration(names[depth-1]));registrations.Add(new GameplayTagRegistration("Keep"));registrations.Add(new GameplayTagRegistration("Wrapped"));
        for(int i=0;i<fillers;i++)registrations.Add(new GameplayTagRegistration("Filler"+i));
        GameplayTagManager.Initialize(registrations.ToArray());
        var seed=new GameplayTagCountContainer();seed.AddTag(GameplayTagManager.RequestTag("Keep"));
        for(int i=0;i<fillers;i++)seed.AddTag(GameplayTagManager.RequestTag("Filler"+i));
        var wrapped=GameplayTagManager.RequestTag("Wrapped");
        seed.AddTag(wrapped,int.MaxValue);seed.AddTag(wrapped,int.MaxValue);seed.AddTag(wrapped,2);seed.AddTag(wrapped,1);seed.RemoveTag(wrapped,1);
        for(int i=fillers-1;i>=0;i--)seed.RemoveTag(GameplayTagManager.RequestTag("Filler"+i));
        Console.WriteLine("seed\t"+seed.TagCount+"\t"+seed.ExplicitTagCount+"\tstale="+seed.Indices.Storage.ExplicitIndices[1]);
        var source=new GameplayTagCountContainer();for(int i=depth-tail;i<depth;i++)source.AddTag(GameplayTagManager.RequestTag(names[i]));
        IGameplayTagContainer view=new NormalView(seed);
        var healthySeed=new GameplayTagContainer();healthySeed.AddTag(GameplayTagManager.RequestTag("Keep"));
        var result=GameplayTagContainer.Union(healthySeed,source);
        Console.WriteLine("healthy-before-copy\t"+result.TagCount+"\t"+result.ExplicitTagCount);
        GameplayTagContainer.Copy(result,view);
        Console.WriteLine("union\t"+result.TagCount+"\t"+result.ExplicitTagCount);
        int n=0;foreach(var tag in result.GetExplicitTags())
        {
            int level=1;foreach(char c in tag.Name)if(c=='.')level++;
            if(n<3)Console.WriteLine("explicit\t"+n+"\tlevel="+level+"\texact="+result.HasTagExact(tag));n++;
        }
        Console.WriteLine("enumerated\t"+n+"\tsources\t"+seed.TagCount+"/"+seed.ExplicitTagCount+"\t"+source.TagCount+"/"+source.ExplicitTagCount);
    }
}
