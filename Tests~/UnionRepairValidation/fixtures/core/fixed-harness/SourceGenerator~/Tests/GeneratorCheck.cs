using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using GameplayTags;
internal static class GeneratorCheck
{
    static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);
    const string Preamble = @"using System; using Alias = GameplayTags.GameplayTagAttribute;
namespace GameplayTags {
    public struct GameplayTag { public string Name; } public enum GameplayTagFlags { None=0 }
    public static class GameplayTagManager {
        public static bool Initialized;
        public static void Initialize() { Initialized = true; }
        public static GameplayTag RequestTag(string name) {
            if (!Initialized) throw new InvalidOperationException(""Generated tag resolved before explicit initialization."");
            return new GameplayTag { Name = name };
        }
    }
    public static class Startup {
        public static GameplayTag Run() {
            GameplayTagManager.Initialize();
            return AllGameplayTags.Root.Tag.Leaf.Get();
        }
    }
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple=true)]
    public class GameplayTagAttribute : Attribute {
        public GameplayTagAttribute(string tagName, string description=null, GameplayTagFlags flags=GameplayTagFlags.None) {}
        public string TagName { get; set; }
    }
}
internal static class Constants { public const string TagName = ""Const.Root""; }
namespace Other { [AttributeUsage(AttributeTargets.Assembly)] public class GameplayTagAttribute : Attribute { public GameplayTagAttribute(string name){} } }
";
    static void Assert(bool condition,string message) { if (!condition) throw new Exception(message); }
    static CSharpCompilation Compilation(string attrs) {
        var refs=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(p=>MetadataReference.CreateFromFile(p));
        return CSharpCompilation.Create("Check",new[]{CSharpSyntaxTree.ParseText("using Alias = GameplayTags.GameplayTagAttribute;\n"+attrs, ParseOptions),CSharpSyntaxTree.ParseText(Preamble, ParseOptions)},refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
    static string Generate(string attrs) {
        GeneratorDriver driver=CSharpGeneratorDriver.Create(new ISourceGenerator[] { new GameplayTagSourceGenerator() }, parseOptions: ParseOptions);
        driver=driver.RunGeneratorsAndUpdateCompilation(Compilation(attrs),out var output,out var diagnostics);
        var errors=output.GetDiagnostics().Concat(diagnostics).Where(d=>d.Severity==DiagnosticSeverity.Error).ToArray();
        Assert(errors.Length==0,string.Join("\n",errors.Select(x=>x.ToString())));
        using (var bytes = new MemoryStream())
        {
            var emitted = output.Emit(bytes);
            Assert(emitted.Success, string.Join("\n", emitted.Diagnostics));
            Assembly assembly = Assembly.Load(bytes.ToArray());
            foreach (Type type in assembly.GetTypes())
                if (type.GetField("s_Tag", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null)
                    Assert((type.Attributes & TypeAttributes.BeforeFieldInit) == 0 && type.TypeInitializer != null,
                        "Generated handles must initialize on first access: " + type.FullName);
            object tag = assembly.GetType("GameplayTags.Startup").GetMethod("Run").Invoke(null, null);
            Assert((string)tag.GetType().GetField("Name").GetValue(tag) == "Root.Tag.Leaf", "explicit initialization precedes Get");
        }
        return driver.GetRunResult().GeneratedTrees.Single().GetText().ToString();
    }
    static void Main() {
        var attrs=new[]{
            "[assembly: Alias(Constants.TagName)]",
            "[assembly: global::GameplayTags.GameplayTag(\"Root.Tag.Leaf\")]",
            "[assembly: Alias(tagName: \"Root.class\")]",
            "[assembly: Alias(\"ignored\", TagName=\"Override.1foo\")]",
            "[assembly: Other.GameplayTag(\"Unrelated.Bad\")]",
            "[assembly: Alias(\"Get.Get.Get\")]",
            "[assembly: Alias(\"class.class.class\")]",
            "[assembly: Alias(\"1x.1x.1x\")]",
            "[assembly: Alias(\"__gt_004700650074\")]",
            "[assembly: Alias(\"AllGameplayTags.AllGameplayTags\")]",
            "[assembly: Alias(\"Root.s_Tag\")]",
            "[assembly: Alias(\"中文.你好\")]"
        };
        string generated=Generate(string.Join("\n",attrs));
        Assert(generated==Generate(string.Join("\n",attrs.Reverse())),"non-deterministic output");
        Assert(generated.Contains("RequestTag(\"Const.Root\")")&&generated.Contains("RequestTag(\"Root.Tag.Leaf\")")&&generated.Contains("RequestTag(\"Override.1foo\")"),"semantic tags missing");
        Assert(!generated.Contains("ignored")&&!generated.Contains("Unrelated.Bad"),"invalid short-name/property parsing");
        GeneratorDriver driver=CSharpGeneratorDriver.Create(new ISourceGenerator[] { new GameplayTagSourceGenerator() }, parseOptions: ParseOptions);
        driver=driver.RunGeneratorsAndUpdateCompilation(Compilation("[assembly: Alias(\"A..B\")]"),out _,out var diagnostics);
        Assert(diagnostics.Any(d=>d.Id=="GT001"&&d.Severity==DiagnosticSeverity.Error),"invalid-name diagnostic");
        Console.WriteLine("Source generator checks passed: semantic attributes, deterministic output, identifier collisions, GT001, explicit startup ordering, no beforefieldinit.");
    }
}
