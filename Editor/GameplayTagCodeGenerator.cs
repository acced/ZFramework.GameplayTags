using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;

namespace GameplayTags.Editor
{
    internal static class GameplayTagCodeGenerator
    {
        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while", "__arglist", "__makeref", "__reftype", "__refvalue", "record", "init", "required", "file", "scoped", "global"
        };
        public static void Generate(GameplayTagSettings settings)
        {
            string path = GetOutputPath(settings);
            string content = BuildSource(settings);
            if (File.Exists(path) && File.ReadAllText(path) == content) return;
            GameplayTagEditorUtility.EnsureAssetDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        public static void GenerateInteractive(GameplayTagSettings settings)
        {
            try { Generate(settings); }
            catch (ArgumentException e) { EditorUtility.DisplayDialog("Gameplay Tags", e.Message, "OK"); }
            catch (GameplayTagRegistryException e) { EditorUtility.DisplayDialog("Gameplay Tags", e.Message, "OK"); }
            catch (IOException e) { EditorUtility.DisplayDialog("Gameplay Tags", e.Message, "OK"); }
            catch (UnauthorizedAccessException e) { EditorUtility.DisplayDialog("Gameplay Tags", e.Message, "OK"); }
        }
        internal static string GetOutputPath(GameplayTagSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            string path = settings.GeneratedCodePath.Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".cs", StringComparison.Ordinal))
                throw new ArgumentException("Generated code must be a .cs file inside Assets/.");
            foreach (string segment in path.Split('/')) if (segment.Length == 0 || segment == "." || segment == "..")
                throw new ArgumentException("Generated code path cannot contain empty, '.' or '..' segments.");
            string assets = Path.GetFullPath("Assets") + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            StringComparison comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!full.StartsWith(assets, comparison)) throw new ArgumentException("Generated code path escapes Assets/.");
            return path;
        }
        /// <summary>Pure source generation. Does not initialize/rebuild the live registry or touch files.</summary>
        internal static string BuildSource(GameplayTagSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var errors = new List<string>();
            if (!settings.Validate(errors)) throw new GameplayTagRegistryException(errors);
            string className = Identifier(settings.GeneratedClassName, "GameplayTags");
            string[] segments = settings.GeneratedNamespace.Split('.');
            if (string.IsNullOrWhiteSpace(settings.GeneratedNamespace)) throw new ArgumentException("Use an application namespace for generated tags.");
            for (int i = 0; i < segments.Length; i++) segments[i] = Identifier(segments[i], "Generated");
            if (segments[0] == "GameplayTags" || segments[0] == "System" || segments[0] == "UnityEngine" || segments[0] == "UnityEditor")
                throw new ArgumentException("Generated tags must use an application namespace, not a framework namespace.");
            string namespaceName = string.Join(".", segments);
            var defined = new HashSet<string>(StringComparer.Ordinal);
            var comments = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < settings.Tags.Count; i++) { defined.Add(settings.Tags[i].Name); comments.Add(settings.Tags[i].Name, settings.Tags[i].DevComment); }
            var names = new List<string>(settings.IncludeImplicitParentTagsInGeneratedCode ? GameplayTagSettings.CollectResolvableNames(defined) : defined);
            names.Sort(StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal) { className, "ContentHash" };
            var builder = new StringBuilder(4096);
            builder.Append("// <auto-generated> ZFramework Gameplay Tags. Do not edit. </auto-generated>\nnamespace ").Append(namespaceName).Append("\n{\n    public static class ").Append(className).Append("\n    {\n        public const uint ContentHash = 0x").Append(settings.ComputeContentHash().ToString("X8", CultureInfo.InvariantCulture)).Append("u;\n");
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                string basis = Identifier(name.Replace('.', '_'), "Tag"), identifier = basis;
                int suffix = 2;
                while (!used.Add(identifier)) identifier = basis + "_" + (suffix++).ToString(CultureInfo.InvariantCulture);
                if (comments.TryGetValue(name, out string comment) && comment.Length > 0)
                    builder.Append("        /// <summary>").Append(Xml(comment)).Append("</summary>\n");
                builder.Append("        public static readonly global::GameplayTags.GameplayTag ").Append(identifier)
                    .Append(" = global::GameplayTags.GameplayTagManager.RequestTag(\"").Append(name.Replace("\\", "\\\\").Replace("\"", "\\\""))
                    .Append("\", true);\n");
            }
            return builder.Append("    }\n}\n").ToString();
        }
        private static string Identifier(string value, string fallback)
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            var builder = new StringBuilder(value.Length + 1);
            for (int i = 0; i < value.Length; i++) builder.Append(char.IsLetterOrDigit(value[i]) || value[i] == '_' ? value[i] : '_');
            if (char.IsDigit(builder[0])) builder.Insert(0, '_');
            string result = builder.ToString();
            return Keywords.Contains(result) ? "_" + result : result;
        }
        private static string Xml(string value)
        {
            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '&') builder.Append("&amp;");
                else if (c == '<') builder.Append("&lt;");
                else if (c == '>') builder.Append("&gt;");
                else if (char.IsControl(c) || c == '\u2028' || c == '\u2029') builder.Append(' ');
                else builder.Append(c);
            }
            return builder.ToString();
        }
    }
}
