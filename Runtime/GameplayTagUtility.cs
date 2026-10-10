using System;

namespace GameplayTags
{
    /// <summary>Name utilities. Parsing assumes valid input; validation is an explicit cold-path operation.</summary>
    public class GameplayTagUtility
    {
        /// <summary>Return every root-to-self prefix. The historical spelling is retained for compatibility.</summary>
        public static string[] GetHeirarchyNames(string tagName)
        {
            int count = GetHeirarchyLevelFromName(tagName);
            var names = new string[count];
            int position = 0;
            for (int i = 0; i < tagName.Length; ++i)
                if (tagName[i] == '.')
                    names[position++] = tagName.Substring(0, i);
            names[position] = tagName;
            return names;
        }

        public static string[] GetHierarchyNames(string tagName) => GetHeirarchyNames(tagName);

        public static bool TryGetParentName(string name, out string parentName)
        {
            int dot = name.LastIndexOf('.');
            parentName = dot < 0 ? null : name.Substring(0, dot);
            return dot >= 0;
        }

        public static int GetHeirarchyLevelFromName(string name)
        {
            int level = 1;
            for (int i = 0; i < name.Length; ++i)
                if (name[i] == '.')
                    ++level;
            return level;
        }

        public static int GetHierarchyLevelFromName(string name) => GetHeirarchyLevelFromName(name);

        public static string GetLabel(string name)
        {
            int dot = name.LastIndexOf('.');
            return dot < 0 ? name : name.Substring(dot + 1);
        }

        /// <summary>Validate untrusted authoring input without throwing. Never called by runtime operations.</summary>
        public static bool IsValidName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            bool needsCharacter = true;
            for (int i = 0; i < name.Length; ++i)
            {
                char c = name[i];
                if (c == '.')
                {
                    if (needsCharacter)
                        return false;
                    needsCharacter = true;
                }
                else if (c == '_' || char.IsLetterOrDigit(c))
                    needsCharacter = false;
                else
                    return false;
            }
            return !needsCharacter;
        }

        /// <summary>Opt-in validation for editors/importers. Names are ordinal and case-sensitive.</summary>
        public static void ValidateName(string name)
        {
            if (!IsValidName(name))
                throw new ArgumentException("Tag names require nonempty dot-separated labels containing only letters, digits or underscores.", nameof(name));
        }
    }
}
