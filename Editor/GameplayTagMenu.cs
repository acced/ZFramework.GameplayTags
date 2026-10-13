using UnityEditor;

namespace GameplayTags.Editor
{
    internal static class GameplayTagMenu
    {
        [MenuItem("Tools/ZFramework/Gameplay Tags/Manager", priority = 100)]
        private static void OpenManager()
        {
            GameplayTagManagerWindow.Open();
        }

        [MenuItem("Tools/ZFramework/Gameplay Tags/Create Settings", priority = 101)]
        private static void CreateSettings()
        {
            GameplayTagEditorUtility.GetSettings(true);
        }

        [MenuItem("Tools/ZFramework/Gameplay Tags/Generate C# API", priority = 102)]
        private static void GenerateCode()
        {
            GameplayTagSettings settings = GameplayTagEditorUtility.GetSettings(true);
            GameplayTagCodeGenerator.Generate(settings);
        }

        [MenuItem("Tools/ZFramework/Gameplay Tags/Validate", priority = 103)]
        private static void Validate()
        {
            GameplayTagBuildValidator.ValidateInteractive();
        }
    }
}
