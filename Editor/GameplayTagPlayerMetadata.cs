using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace GameplayTags.Editor
{
    // Read the actual, pre-stripping Player binaries. Never load a Player assembly
    // into the Editor domain or execute its attribute constructors/static initializers.
    internal static class GameplayTagPlayerMetadata
    {
        internal const string RegistryType = "GameplayTags.Generated.GameplayTagRegistry";
        private const string AttributeType = "GameplayTags.GameplayTagAttribute";
        private const string RuntimeAssembly = "ZFramework.GameplayTags";

        internal sealed class Snapshot
        {
            internal GameplayTagRegistration[] Registrations;
            internal string CompiledFingerprint;
            internal int RegistryDefinitions;
            internal bool RuntimeFound;
        }

        internal static Snapshot Read(IEnumerable<string> playerAssemblyPaths)
        {
            string[] paths = playerAssemblyPaths.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).ToArray();
            using var resolver = new DefaultAssemblyResolver();
            foreach (string directory in paths.Select(Path.GetDirectoryName).Distinct(StringComparer.Ordinal))
                resolver.AddSearchDirectory(directory);
            var modules = new List<AssemblyDefinition>();
            var identities = new Dictionary<string, Guid>(StringComparer.Ordinal);
            var types = new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);
            var result = new Snapshot();
            try
            {
                foreach (string path in paths)
                {
                    AssemblyDefinition assembly;
                    try
                    {
                        assembly = AssemblyDefinition.ReadAssembly(path,
                            new ReaderParameters { ReadSymbols = false, InMemory = true, ReadingMode = ReadingMode.Deferred, AssemblyResolver = resolver });
                    }
                    catch (BadImageFormatException) { continue; } // A native plugin is not a managed declaration source.
                    string name = assembly.Name.Name;
                    Guid mvid = assembly.MainModule.Mvid;
                    if (identities.TryGetValue(name, out Guid previous))
                    {
                        assembly.Dispose();
                        if (previous != mvid)
                            throw new InvalidOperationException("Different Player assemblies have the same identity: " + name);
                        continue;
                    }
                    identities.Add(name, mvid);
                    modules.Add(assembly);
                    foreach (ModuleDefinition module in assembly.Modules)
                        foreach (TypeDefinition type in module.Types) AddTypes(name, type, types);
                }
                result.RuntimeFound = types.ContainsKey(RuntimeAssembly + ":" + AttributeType);
                if (!result.RuntimeFound)
                    throw new InvalidOperationException("The Player assembly list does not contain ZFramework.GameplayTags. No registry was generated.");

                var registrations = new List<GameplayTagRegistration>();
                foreach (AssemblyDefinition assembly in modules)
                {
                    foreach (CustomAttribute attribute in assembly.CustomAttributes)
                    {
                        if (!IsTagAttribute(attribute.AttributeType, types)) continue;
                        if (attribute.AttributeType.FullName != AttributeType || ScopeName(attribute.AttributeType) != RuntimeAssembly)
                            throw new NotSupportedException("Derived gameplay-tag attribute '" + attribute.AttributeType.FullName +
                                "' cannot be evaluated without executing Player code. Use GameplayTagAttribute directly or explicit registration.");
                        var arguments = attribute.ConstructorArguments;
                        string name = (string)arguments[0].Value;
                        string description = arguments.Count > 1 ? (string)arguments[1].Value : null;
                        int flags = arguments.Count > 2 ? Convert.ToInt32(arguments[2].Value) : 0;
                        foreach (CustomAttributeNamedArgument property in attribute.Properties)
                        {
                            switch (property.Name)
                            {
                                case "TagName": name = (string)property.Argument.Value; break;
                                case "Description": description = (string)property.Argument.Value; break;
                                case "Flags": flags = Convert.ToInt32(property.Argument.Value); break;
                            }
                        }
                        registrations.Add(new GameplayTagRegistration(name, description, (GameplayTagFlags)flags));
                    }
                    foreach (ModuleDefinition module in assembly.Modules)
                    {
                        TypeDefinition provider = module.GetType(RegistryType);
                        if (provider == null) continue;
                        result.RegistryDefinitions++;
                        foreach (FieldDefinition field in provider.Fields)
                            if (field.Name == "DeclarationFingerprint" && field.HasConstant)
                                result.CompiledFingerprint = field.Constant as string;
                    }
                }
                result.Registrations = registrations.ToArray();
                return result;
            }
            finally { foreach (AssemblyDefinition assembly in modules) assembly.Dispose(); }
        }

        private static void AddTypes(string assembly, TypeDefinition type, Dictionary<string, TypeDefinition> types)
        {
            types.Add(assembly + ":" + type.FullName, type);
            foreach (TypeDefinition child in type.NestedTypes) AddTypes(assembly, child, types);
        }

        private static string ScopeName(TypeReference type) => type.Scope is AssemblyNameReference reference
            ? reference.Name : type.Module.Assembly.Name.Name;

        private static bool IsTagAttribute(TypeReference type, Dictionary<string, TypeDefinition> types)
        {
            while (type != null)
            {
                string key = ScopeName(type) + ":" + type.FullName;
                if (key == RuntimeAssembly + ":" + AttributeType) return true;
                if (!types.TryGetValue(key, out TypeDefinition definition)) return false;
                type = definition.BaseType;
            }
            return false;
        }
    }
}
