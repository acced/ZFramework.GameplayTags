using System;
using System.Collections.Generic;
using System.Linq;
using GameplayTags;

internal static class Program
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Main()
    {
        const int depth = 3000;
        string name = "Deep." + string.Join(".", Enumerable.Repeat("A", depth - 1));
        GameplayTagManager.Initialize(new GameplayTagRegistration(name), new GameplayTagRegistration("Other.Leaf"));
        GameplayTag leaf = GameplayTagManager.RequestTag(name);
        GameplayTag root = GameplayTagManager.RequestTag("Deep");
        GameplayTag other = GameplayTagManager.RequestTag("Other.Leaf");
        GameplayTagContainer container = new GameplayTagContainer();
        container.AddTag(leaf);
        Require(container.ExplicitTagCount == 1 && container.TagCount == depth, "deep closure count");
        int explicitCount = 0;
        foreach (GameplayTag tag in container.GetExplicitTags()) { Require(tag == leaf, "only explicit leaf"); explicitCount++; }
        Require(explicitCount == 1, "single explicit enumeration");
        Require(container.HasTag(root) && container.HasTagExact(leaf) && !container.HasTagExact(root), "deep membership");
        Require(leaf.IsChildOf(root) && root.IsParentOf(leaf) && !root.IsParentOf(other), "deep intervals");
        GameplayTagContainer required = new GameplayTagContainer { leaf };
        Require(container.HasAllExact(required) && container.HasAnyExact(required), "deep single requirement");
        required.AddTag(other);
        Require(!container.HasAllExact(required) && container.HasAnyExact(required), "deep missing requirement");
        List<GameplayTag> parents = new List<GameplayTag>();
        container.GetParentTags(leaf, parents);
        Require(parents.Count == depth - 1, "all strict deep parents");
        GameplayTagContainer copy = container.Clone();
        copy.RemoveTag(leaf);
        Require(copy.IsEmpty && copy.TagCount == 0 && container.TagCount == depth, "deep copy and removal");
        container.RemoveTags(container);
        Require(container.IsEmpty && container.TagCount == 0, "deep alias removal");

        GameplayTagCountContainer counted = new GameplayTagCountContainer();
        counted.AddTag(leaf); counted.AddTag(leaf);
        Require(counted.GetTagCount(root) == 2 && counted.GetExplicitTagCount(leaf) == 2, "deep count propagation");
        counted.RemoveTag(leaf);
        Require(counted.GetTagCount(root) == 1 && counted.TagCount == depth, "deep shared contribution retained");
        counted.Clear();
        Require(counted.IsEmpty && counted.GetTagCount(root) == 0, "deep count clear");
        Console.WriteLine("PASS 3000-level hierarchy: one explicit tag, relationships, queries, copy, aliases and counts.");
    }
}
