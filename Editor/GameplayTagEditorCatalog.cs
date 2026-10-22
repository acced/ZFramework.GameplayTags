using System;
using System.Collections.Generic;

namespace GameplayTags.Editor
{
    // Name-only editor data: opening a picker must never initialize or replace the runtime registry.
    internal static class GameplayTagEditorCatalog
    {
        internal sealed class Item
        {
            public string Name;
            public string Description;
            public bool IsExplicit;
            public string Label => GameplayTagUtility.GetLabel(Name);
        }

        public static List<Item> Read()
        {
            var items = new SortedDictionary<string, Item>(StringComparer.Ordinal);
            GameplayTagSettings settings = GameplayTagSettings.Find();
            if (settings == null) return new List<Item>();
            foreach (GameplayTagRegistration entry in settings.CreateRegistrations())
            {
                foreach (string name in GameplayTagUtility.GetHierarchyNames(entry.Name))
                    if (!items.ContainsKey(name)) items.Add(name, new Item { Name = name });
                items[entry.Name].Description = entry.Description;
                items[entry.Name].IsExplicit = true;
            }
            return new List<Item>(items.Values);
        }

        public static string Description(string name)
        {
            GameplayTagSettings settings = GameplayTagSettings.Find();
            if (settings != null)
                foreach (GameplayTagSettings.Entry entry in settings.Entries)
                    if (entry.Name == name) return entry.Description;
            return null;
        }
    }
}
