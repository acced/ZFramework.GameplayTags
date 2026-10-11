using System;
using System.Collections.Generic;
using GameplayTags;
internal static class Program
{
    sealed class NormalView : GameplayTagContainer, IGameplayTagContainer
    {
        readonly IGameplayTagContainer inner;
        internal NormalView(IGameplayTagContainer inner) { this.inner = inner; }
        GameplayTagContainerIndices IGameplayTagContainer.Indices => inner.Indices;
    }
    static void Observe(string stage, GameplayTagContainer result, GameplayTag root)
    {
        var s = result.Indices.Storage;
        int index = s.Find(root.RuntimeIndex);
        Console.WriteLine(stage + "\t" + result.TagCount + "\t" + result.ExplicitTagCount + "\t" + result.HasTag(root) + "\t" + result.HasTagExact(root) + "\t" + (index < 0 ? "missing" : s.Entries[index].Count + "/" + s.Entries[index].ExplicitCount));
    }
    static void Main()
    {
        const int depth = 64;
        string[] names = new string[depth];
        names[0] = "Chain";
        for (int i = 1; i < depth; i++) names[i] = names[i - 1] + ".N";
        var registrations = new List<GameplayTagRegistration>();
        registrations.Add(new GameplayTagRegistration(names[depth - 1]));
        for (int i = 0; i < 65; i++) registrations.Add(new GameplayTagRegistration("Seed" + i));
        GameplayTagManager.Initialize(registrations.ToArray());
        var root = GameplayTagManager.RequestTag(names[0]);
        var seed = new GameplayTagCountContainer();
        seed.AddTag(root, 3);
        for (int i = 0; i < 65; i++) seed.AddTag(GameplayTagManager.RequestTag("Seed" + i));
        var source = new GameplayTagContainer();
        var tags = new GameplayTag[depth];
        for (int i = 0; i < depth; i++) { tags[i] = GameplayTagManager.RequestTag(names[i]); source.AddTag(tags[i]); }
        Console.WriteLine("seed/source closure\t" + seed.TagCount + "\t" + source.TagCount);
        IGameplayTagContainer view = new NormalView(seed);
        var result = GameplayTagContainer.Union(view, source);
        Observe("union", result, root);
        for (int i = 0; i < 3; i++) { result.RemoveTag(tags[depth - 1 - i]); Observe("remove-" + (i + 1), result, root); }
        Console.WriteLine("sources\t" + seed.TagCount + "\t" + seed.ExplicitTagCount + "\t" + seed.GetTagCount(root) + "\t" + seed.GetExplicitTagCount(root) + "\t" + source.TagCount + "\t" + source.ExplicitTagCount);
    }
}
