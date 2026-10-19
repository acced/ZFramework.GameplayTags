using System;
using System.IO;
using System.Linq;
using GameplayTags;
using GameplayTags.Editor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

static class Program
{
    static int checks;
    static void Check(bool value,string message) { checks++; if(!value)throw new Exception(message); }
    static void Main(string[] args)
    {
        string mode=args[0]; string root=args[1]; Directory.CreateDirectory(root); Directory.SetCurrentDirectory(root); Directory.CreateDirectory("Assets");
        string[] files=args.Skip(2).Select(Path.GetFullPath).ToArray();
        if(mode=="seed") { GameplayTagRegistryGenerator.Generate(); Console.WriteLine("PASS opt-in seed"); return; }
        if(mode=="missing-runtime") { bool thrown=false; try { GameplayTagPlayerMetadata.Read(Array.Empty<string>()); }catch(InvalidOperationException){thrown=true;} Check(thrown,"missing input must not generate an empty valid table"); }
        else if(mode=="derived") { bool thrown=false; try { GameplayTagPlayerMetadata.Read(files); }catch(NotSupportedException){thrown=true;} Check(thrown,"computed derived declaration cannot be silently skipped"); }
        else
        {
            var snapshot=GameplayTagPlayerMetadata.Read(files);
            var names=snapshot.Registrations.Select(t=>t.Name).ToArray();
            Check(names.Contains("Shared.Base") && names.Contains("Named.Override"),"declarations and named overrides");
            Check(!names.Contains("Ignored.Constructor"),"named name wins");
            Check(!names.Contains("Editor.Only"),"actual Player fixture excludes UNITY_EDITOR");
            var named=snapshot.Registrations.First(t=>t.Name=="Named.Override");
            Check((int)named.Flags==8 && named.Description.Contains("\n") && named.Description.Contains("中文"),"metadata values");
            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name.StartsWith("PlayerFixture",StringComparison.Ordinal)),"Player assemblies were not loaded");
            Check(GameplayTagRegistryGenerator.GenerateSource(snapshot.Registrations)==GameplayTagRegistryGenerator.GenerateSource(snapshot.Registrations.Reverse()),"deterministic ordering");
            Check(!GameplayTagRegistryGenerator.GenerateSource(snapshot.Registrations).Contains("RuntimeInitializeOnLoadMethod"),"no competing auto-start callback");
            Check(names.Contains(mode.Contains("android")?"Platform.Android":"Platform.Desktop"),"target-conditioned declaration");
            Check(names.Contains("Content.Dlc")==mode.Contains("dlc"),"extra build define");
            if(mode.Contains("plugin"))Check(names.Contains("Plugin.Only"),"precompiled managed plugin declaration");
            bool expected=mode.StartsWith("accept",StringComparison.Ordinal);
            if(mode.StartsWith("build",StringComparison.Ordinal))
            {
                bool stopped=false;
                try { new GameplayTagRegistryBuildCheck().OnPostBuildPlayerScriptDLLs(new BuildReport{Files=files.Select(p=>new BuildFile{path=p}).ToArray()}); }
                catch(BuildFailedException){stopped=true;}
                Check(stopped,"first/stale compilation is stopped and source refreshed");
            }
            else Check(GameplayTagRegistryGenerator.GenerateFromPlayerAssemblies(files)==expected,"compiled/source fingerprint agreement");
            string source=File.ReadAllText(GameplayTagRegistryGenerator.OutputPath);
            Check(source.Contains("DeclarationFingerprint") && source.Contains("Named.Override"),"generated source content");
        }
        Console.WriteLine("PASS metadata "+mode+" checks="+checks);
    }
}
