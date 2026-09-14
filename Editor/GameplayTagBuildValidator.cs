using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace GameplayTags.Editor
{
    internal sealed class GameplayTagBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder { get { return -1000; } }

        public void OnPreprocessBuild(BuildReport report)
        {
            string error;
            if (!Validate(out error, false))
                throw new BuildFailedException(error);
        }

        public static void ValidateInteractive()
        {
            string error;
            if (Validate(out error, false))
                EditorUtility.DisplayDialog("Gameplay Tags", "Validation passed.", "OK");
            else
                EditorUtility.DisplayDialog("Gameplay Tags Validation", error, "OK");
        }

        public static bool Validate(out string error, bool generateCode)
        {
            GameplayTagSettings settings = GameplayTagEditorUtility.GetSettings(false);
            if (settings == null)
            {
                error = "GameplayTagSettings is missing. Create it at " + GameplayTagSettings.DefaultAssetPath + ".";
                return false;
            }

            var errors = new List<string>();
            settings.Validate(errors);
            try
            {
                GameplayTagEditorUtility.InitializeManager(settings, true);
            }
            catch (Exception exception)
            {
                errors.Add(exception.Message);
            }

            if (errors.Count > 0)
            {
                var builder = new StringBuilder();
                for (int i = 0; i < errors.Count; i++)
                    builder.Append("- ").Append(errors[i]).AppendLine();
                error = builder.ToString();
                return false;
            }

            if (generateCode)
                GameplayTagCodeGenerator.Generate(settings);
            error = null;
            return true;
        }
    }
}
