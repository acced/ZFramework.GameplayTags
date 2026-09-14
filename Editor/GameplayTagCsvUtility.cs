using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GameplayTags.Editor
{
    internal static class GameplayTagCsvUtility
    {
        public static void ExportWithDialog(GameplayTagSettings settings)
        {
            string path = EditorUtility.SaveFilePanel("Export Gameplay Tags", string.Empty, "GameplayTags.csv", "csv");
            if (string.IsNullOrEmpty(path))
                return;

            Export(settings, path);
        }

        public static void ImportWithDialog(GameplayTagSettings settings)
        {
            string path = EditorUtility.OpenFilePanel("Import Gameplay Tags", string.Empty, "csv");
            if (string.IsNullOrEmpty(path))
                return;

            string error;
            if (!Import(settings, path, out error))
                EditorUtility.DisplayDialog("Gameplay Tags CSV", error, "OK");
        }

        public static void Export(GameplayTagSettings settings, string path)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var builder = new StringBuilder();
            builder.AppendLine("Tag,Comment,Source,Restricted,AllowNonRestrictedChildren");
            IReadOnlyList<GameplayTagDefinition> tags = settings.Tags;
            for (int i = 0; i < tags.Count; i++)
            {
                GameplayTagDefinition tag = tags[i];
                if (tag == null)
                    continue;

                AppendCell(builder, tag.Name);
                builder.Append(',');
                AppendCell(builder, EscapeNewLines(tag.DevComment));
                builder.Append(',');
                AppendCell(builder, tag.Source);
                builder.Append(',').Append(tag.Restricted ? "true" : "false");
                builder.Append(',').Append(tag.AllowNonRestrictedChildren ? "true" : "false");
                builder.AppendLine();
            }

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        }

        /// <summary>
        /// 把 CSV 覆盖到现有定义上。整份文件先在副本上组装并校验，任何一行出错都不会落盘。
        /// </summary>
        public static bool Import(GameplayTagSettings settings, string path, out string error)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (!File.Exists(path))
            {
                error = "CSV file does not exist: " + path;
                return false;
            }

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length == 0)
            {
                error = "CSV file is empty.";
                return false;
            }

            List<GameplayTagDefinition> tags = GameplayTagEditorUtility.CloneDefinitions(settings.Tags);
            List<GameplayTagSource> sources = GameplayTagEditorUtility.CloneSources(settings.Sources);
            var cells = new List<string>(8);
            for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
            {
                if (string.IsNullOrWhiteSpace(lines[lineIndex]))
                    continue;

                if (!TryParseLine(lines[lineIndex], cells) || cells.Count < 1)
                {
                    error = "Invalid CSV at line " + (lineIndex + 1) + ". No changes were applied.";
                    return false;
                }

                if (!GameplayTagName.TryNormalize(cells[0], out string name, out string nameError))
                {
                    error = "Line " + (lineIndex + 1) + ": " + nameError + " No changes were applied.";
                    return false;
                }

                string source = cells.Count > 2 && cells[2].Trim().Length > 0 ? cells[2].Trim() : "Default";
                int index = tags.FindIndex(tag => string.Equals(tag.Name, name, StringComparison.Ordinal));
                if (GameplayTagEditorUtility.IsSourceReadOnly(sources, source) ||
                    (index >= 0 && GameplayTagEditorUtility.IsSourceReadOnly(sources, tags[index].Source)))
                {
                    error = "Line " + (lineIndex + 1) +
                            ": Gameplay Tag source is read-only. No changes were applied.";
                    return false;
                }

                var definition = new GameplayTagDefinition(
                    name,
                    cells.Count > 1 ? UnescapeNewLines(cells[1]) : string.Empty,
                    source,
                    cells.Count > 3 && ParseBoolean(cells[3]),
                    cells.Count <= 4 || ParseBoolean(cells[4]));
                if (index >= 0)
                    tags[index] = definition;
                else
                    tags.Add(definition);

                if (!sources.Exists(item => string.Equals(item.Name, source, StringComparison.Ordinal)))
                    sources.Add(new GameplayTagSource(source, string.Empty, false));
            }

            Undo.RecordObject(settings, "Import Gameplay Tags CSV");
            if (!GameplayTagEditorUtility.TryApply(
                    settings,
                    tags,
                    GameplayTagEditorUtility.CloneRedirects(settings.Redirects),
                    sources,
                    out error))
            {
                error += " No changes were applied.";
                return false;
            }

            GameplayTagEditorUtility.SaveAndReinitialize(settings);
            return true;
        }

        private static bool TryParseLine(string line, List<string> cells)
        {
            cells.Clear();
            var builder = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char character = line[i];
                if (character == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        builder.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                    continue;
                }

                if (character == ',' && !quoted)
                {
                    cells.Add(builder.ToString());
                    builder.Length = 0;
                    continue;
                }

                builder.Append(character);
            }

            if (quoted)
                return false;
            cells.Add(builder.ToString());
            return true;
        }

        private static void AppendCell(StringBuilder builder, string value)
        {
            if (value == null)
                value = string.Empty;

            bool quote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            if (!quote)
            {
                builder.Append(value);
                return;
            }

            builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '"')
                    builder.Append("\"\"");
                else
                    builder.Append(value[i]);
            }
            builder.Append('"');
        }

        private static bool ParseBoolean(string value)
        {
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";
        }

        private static string EscapeNewLines(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static string UnescapeNewLines(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] != '\\' || i + 1 >= value.Length)
                {
                    builder.Append(value[i]);
                    continue;
                }

                char next = value[++i];
                if (next == 'n')
                    builder.Append('\n');
                else if (next == 'r')
                    builder.Append('\r');
                else
                    builder.Append(next);
            }
            return builder.ToString();
        }
    }
}
