using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace GameplayTags.Editor
{
    internal sealed class GameplayTagBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (!Validate(out string error, false)) throw new BuildFailedException(error);
        }
        public static void ValidateInteractive()
        {
            bool valid = Validate(out string error, false);
            EditorUtility.DisplayDialog("Gameplay Tags Validation", valid ? "Validation passed." : error, "OK");
        }
        public static bool Validate(out string error, bool generateCode)
        {
            GameplayTagSettings settings = GameplayTagEditorUtility.GetSettings(false);
            if (settings == null)
            {
                error = "Create GameplayTagSettings at " + GameplayTagSettings.DefaultAssetPath + ".";
                return false;
            }
            try
            {
                if (generateCode) GameplayTagCodeGenerator.Generate(settings);
                string path = GameplayTagCodeGenerator.GetOutputPath(settings);
                string expected = GameplayTagCodeGenerator.BuildSource(settings);
                if (!File.Exists(path) || !string.Equals(File.ReadAllText(path).Replace("\r\n", "\n"), expected, StringComparison.Ordinal))
                {
                    error = "Generated Gameplay Tags source is missing or stale. Regenerate " + path + " before building.";
                    return false;
                }
                error = null;
                return true;
            }
            catch (GameplayTagRegistryException e) { error = e.Message; return false; }
            catch (ArgumentException e) { error = e.Message; return false; }
            catch (IOException e) { error = e.Message; return false; }
            catch (UnauthorizedAccessException e) { error = e.Message; return false; }
        }
    }
}
