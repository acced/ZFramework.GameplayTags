using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using GameplayTags;

internal static class Check
{
    static void Assert(bool test, string text) { if (!test) throw new Exception(text); }
    static void Main()
    {
        Assert(GameplayTag.None.ChildTags.Length == 0, "None descendants before init");
        GameplayTagManager.Initialize(
            new GameplayTagRegistration("A0.B"),
            new GameplayTagRegistration("A.z.Leaf", flags: (GameplayTagFlags)4),
            new GameplayTagRegistration("A.B.C"),
            new GameplayTagRegistration("a.B"),
            new GameplayTagRegistration("A.B", "middle"));
        string[] names = GameplayTagManager.GetAllTags().ToArray().Select(t => t.Name).ToArray();
        Assert(string.Join(",", names) == "A,A.B,A.B.C,A.z,A.z.Leaf,A0,A0.B,a,a.B", "DFS order");
        var a = GameplayTagManager.RequestTag("A");
        var abc = GameplayTagManager.RequestTag("A.B.C");
        Assert(a.ChildTags.Length == 4 && a.ChildTags[3].Name == "A.z.Leaf", "contiguous descendants");
        Assert(abc.ParentTag.Name == "A.B" && abc.ParentTags.Length == 2 && abc.HierarchyTags.Length == 3, "parents");
        Assert(abc.HierarchyTags[0] == a && abc.HierarchyTags[2] == abc, "pool prefix");
        Assert(abc.IsChildOf(a) && a.IsParentOf(abc), "strict relationship");
        Assert(!a.IsChildOf(a) && !a.IsParentOf(a) && !abc.IsChildOf(GameplayTag.None), "strict none");
        Assert(!GameplayTag.None.IsParentOf(a) && !GameplayTag.None.IsChildOf(a), "none");
        Assert(!GameplayTagManager.RequestTag("A0.B").IsChildOf(a), "prefix boundary");
        Assert((int)a.Flags == 4 && (int)GameplayTagManager.RequestTag("A.B").Flags == 0, "implicit flags");
        Assert(GameplayTagManager.RequestTag(null) == GameplayTag.None && !GameplayTagManager.RequestTag(null, out _), "missing");
        Assert(GameplayTagManager.RequestTag("a") != a, "ordinal");
        Assert(!a.Equals((object)a.Name) && a.Equals((object)a), "boxed equality contract");
        var rehydrated = new GameplayTag { m_Name = "A.B.C" }; rehydrated.OnAfterDeserialize();
        Assert(rehydrated == abc, "deserialize"); rehydrated.m_Name = "bad"; rehydrated.OnBeforeSerialize();
        Assert(rehydrated.m_Name == abc.Name, "serialize canonical name");
        var serializer = new DataContractSerializer(typeof(GameplayTag));
        using (var stream = new MemoryStream())
        {
            serializer.WriteObject(stream, abc);
            stream.Position = 0;
            Assert((GameplayTag)serializer.ReadObject(stream) == abc, "DataContractSerializer round trip");
        }
        foreach (Type attr in new[]{typeof(OnSerializingAttribute), typeof(OnDeserializedAttribute)})
        {
            var method=typeof(GameplayTag).GetMethods(BindingFlags.NonPublic|BindingFlags.Instance).Single(m=>m.IsDefined(attr));
            Assert(method.ReturnType==typeof(void)&&method.GetParameters().Length==1&&method.GetParameters()[0].ParameterType==typeof(StreamingContext), "serialization signature");
        }
        Assert(!GameplayTagUtility.IsValidName(".A") && !GameplayTagUtility.IsValidName("A..B") && !GameplayTagUtility.IsValidName("A.") && GameplayTagUtility.IsValidName("1a.中_文"), "validation");
        for(int seed=0;seed<50;seed++)
        {
            var random=new Random(seed); var declarations=Enumerable.Range(0,100).Select(_ => string.Join(".", Enumerable.Range(0,random.Next(1,8)).Select(x=>"N"+random.Next(5)))).ToArray();
            CheckBuild(declarations);
        }
        // The implementation is iterative and shares one path for a deep chain.
        var deep = new GameplayTagRegistrationContext(); deep.RegisterTag(string.Join(".",Enumerable.Repeat("A",3000)));
        deep.Build(out var defs, out var tags, out var parents, out var ends, out var paths, out var pathTags, out var dictionary);
        Assert(defs.Length==3001 && paths.Length==3000 && ends[1]==3001, "deep chain pool");
        Console.WriteLine("Registry checks passed: DFS, all-pairs reference oracle, pooled paths, ordinal names, metadata, serialization, 3000-deep chain.");
    }
    static void CheckBuild(string[] declarations)
    {
        var builder = new GameplayTagRegistrationContext(); foreach(var name in declarations) builder.RegisterTag(name);
        builder.Build(out var defs, out var tags, out var parents, out var ends, out var paths, out var pathTags, out var dictionary);
        for(int i=1;i<tags.Length;i++)
        {
            int dot=tags[i].Name.LastIndexOf('.'); string parent=dot<0?null:tags[i].Name.Substring(0,dot);
            Assert(parent==tags[parents[i]].Name,"random immediate parent");
            ref readonly var def=ref defs[i]; Assert(paths[def.HierarchyOffset+def.HierarchyLevel-1]==i,"random pool self");
            for(int j=1;j<tags.Length;j++)
                Assert((j>i&&j<ends[i])==tags[j].Name.StartsWith(tags[i].Name+".",StringComparison.Ordinal),"random interval oracle");
        }
        var reverse = new GameplayTagRegistrationContext(); foreach(var name in declarations.Reverse()) reverse.RegisterTag(name);
        reverse.Build(out _,out var other,out _,out _,out _,out _,out _);
        Assert(tags.Select(t=>t.Name).SequenceEqual(other.Select(t=>t.Name)),"deterministic ids");
    }
}
