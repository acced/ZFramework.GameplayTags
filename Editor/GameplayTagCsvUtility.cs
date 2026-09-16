using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;

namespace GameplayTags.Editor
{
    internal static class GameplayTagCsvUtility
    {
        private static readonly string[] Columns = { "Tag", "Comment", "Source", "Restricted", "AllowNonRestrictedChildren" };
        public static void ExportWithDialog(GameplayTagSettings settings)
        {
            string path = EditorUtility.SaveFilePanel("Export Gameplay Tags", string.Empty, "GameplayTags.csv", "csv");
            if (string.IsNullOrEmpty(path)) return;
            Export(settings, path);
        }
        public static void ImportWithDialog(GameplayTagSettings settings)
        {
            string path = EditorUtility.OpenFilePanel("Import Gameplay Tags", string.Empty, "csv");
            if (string.IsNullOrEmpty(path)) return;
            if (!Import(settings, path, out string error)) EditorUtility.DisplayDialog("Gameplay Tags CSV", error, "OK");
        }
        public static void Export(GameplayTagSettings settings, string path)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var errors = new List<string>();
            if (!settings.Validate(errors)) throw new GameplayTagRegistryException(errors);
            var builder = new StringBuilder();
            builder.Append(string.Join(",", Columns)).Append('\n');
            for (int i = 0; i < settings.Tags.Count; i++)
            {
                GameplayTagDefinition tag = settings.Tags[i];
                AppendCell(builder, tag.Name); builder.Append(',');
                AppendCell(builder, EscapeNewLines(tag.DevComment)); builder.Append(',');
                AppendCell(builder, tag.Source); builder.Append(',');
                builder.Append(tag.Restricted ? "true" : "false").Append(',');
                builder.Append(tag.AllowNonRestrictedChildren ? "true" : "false").Append('\n');
            }
            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        }
        public static bool Import(GameplayTagSettings settings, string path, out string error)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!File.Exists(path)) { error = "CSV file does not exist: " + path; return false; }
            List<string[]> rows;
            try { rows = Parse(File.ReadAllText(path, Encoding.UTF8)); }
            catch (FormatException e) { error = e.Message; return false; }
            if (rows.Count == 0) { error = "CSV file is empty."; return false; }
            string[] header = rows[0];
            if (header.Length > Columns.Length) { error = "CSV contains unknown columns."; return false; }
            for (int i = 0; i < header.Length; i++)
                if (!string.Equals(header[i].Trim().TrimStart('\ufeff'), Columns[i], StringComparison.OrdinalIgnoreCase))
                { error = "CSV header must be an ordered prefix of: " + string.Join(",", Columns); return false; }
            var tags = GameplayTagEditorUtility.CloneDefinitions(settings.Tags);
            var sources = GameplayTagEditorUtility.CloneSources(settings.Sources);
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < tags.Count; i++) indices.Add(tags[i].Name, i);
            var sourceMap = new Dictionary<string, GameplayTagSource>(StringComparer.Ordinal);
            for (int i = 0; i < sources.Count; i++) sourceMap.Add(sources[i].Name, sources[i]);
            var imported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int row = 1; row < rows.Count; row++)
            {
                string[] cells = rows[row];
                if (cells.Length != header.Length) { error = "Unexpected field count at CSV record " + (row + 1); return false; }
                if (!GameplayTagName.TryNormalize(cells[0], out string name, out error)) return false;
                if (!imported.Add(name)) { error = "Duplicate or case-conflicting CSV tag: " + name; return false; }
                string sourceName = cells.Length > 2 && cells[2].Trim().Length > 0 ? cells[2].Trim() : "Default";
                bool exists = indices.TryGetValue(name, out int index);
                if ((sourceMap.TryGetValue(sourceName, out GameplayTagSource source) && source.ReadOnly) ||
                    (exists && sourceMap.TryGetValue(tags[index].Source, out GameplayTagSource oldSource) && oldSource.ReadOnly))
                { error = "CSV cannot modify a read-only source: " + name; return false; }
                bool restricted = false, allow = true;
                if ((cells.Length > 3 && !TryBoolean(cells[3], out restricted)) || (cells.Length > 4 && !TryBoolean(cells[4], out allow)))
                { error = "CSV booleans must be true, false, 1, or 0: " + name; return false; }
                var definition = new GameplayTagDefinition(name, cells.Length > 1 ? UnescapeNewLines(cells[1]) : string.Empty, sourceName, restricted, allow);
                if (exists) tags[index] = definition;
                else { indices.Add(name, tags.Count); tags.Add(definition); }
                if (!sourceMap.ContainsKey(sourceName))
                { source = new GameplayTagSource(sourceName, string.Empty, false); sources.Add(source); sourceMap.Add(sourceName, source); }
            }
            var redirects = GameplayTagEditorUtility.CloneRedirects(settings.Redirects);
            var errors = new List<string>();
            if (!GameplayTagSettings.Validate(tags, redirects, sources, errors)) { error = string.Join("\n", errors); return false; }
            // All parsing and validation have finished: this is the only mutation boundary.
            Undo.RecordObject(settings, "Import Gameplay Tags CSV");
            settings.ReplaceAll(tags, redirects, sources);
            GameplayTagEditorUtility.SaveAndReinitialize(settings);
            error = null;
            return true;
        }
        private static bool TryBoolean(string value, out bool result)
        {
            value = value.Trim();
            if (value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) { result = true; return true; }
            if (value == "0" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) { result = false; return true; }
            result = false;
            return false;
        }
        private static List<string[]> Parse(string text)
        {
            var rows = new List<string[]>();
            var cells = new List<string>(5);
            var cell = new StringBuilder();
            bool quoted = false, closed = false;
            for (int i = 0; i <= text.Length; i++)
            {
                bool end = i == text.Length;
                char c = end ? default : text[i];
                if (quoted)
                {
                    if (end) throw new FormatException("Unterminated quoted CSV field.");
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else { quoted = false; closed = true; }
                    }
                    else cell.Append(c);
                    continue;
                }
                if (end || c == ',' || c == '\r' || c == '\n')
                {
                    cells.Add(cell.ToString()); cell.Clear(); closed = false;
                    if (c != ',' || end)
                    {
                        if (cells.Count != 1 || !string.IsNullOrWhiteSpace(cells[0])) rows.Add(cells.ToArray());
                        cells.Clear();
                        if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    }
                    continue;
                }
                if (closed) throw new FormatException("Unexpected text after a closing CSV quote.");
                if (c == '"')
                {
                    if (cell.Length != 0) throw new FormatException("CSV quotes must start at the beginning of a field.");
                    quoted = true;
                }
                else cell.Append(c);
            }
            return rows;
        }
        private static void AppendCell(StringBuilder builder, string value)
        {
            bool quoted = value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0;
            if (!quoted) { builder.Append(value); return; }
            builder.Append('"');
            for (int i = 0; i < value.Length; i++) { if (value[i] == '"') builder.Append('"'); builder.Append(value[i]); }
            builder.Append('"');
        }
        private static string EscapeNewLines(string value) => value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");
        private static string UnescapeNewLines(string value)
        {
            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length)
                {
                    char next = value[i + 1];
                    if (next == 'n' || next == 'r' || next == '\\') { builder.Append(next == 'n' ? '\n' : next == 'r' ? '\r' : '\\'); i++; continue; }
                }
                builder.Append(value[i]);
            }
            return builder.ToString();
        }
    }
}
