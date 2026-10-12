using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using GameplayTags;

// Read-only snapshot, deliberately independent of JSON packages and harness declarations.
// Compile this SAME file into both Test assemblies, then call MetadataGate.Write(path).
// This source was prepared without compiling or running either target.
internal static class MetadataGate
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private static readonly Dictionary<ushort, OpCode> Opcodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(OpCode))
        .Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => unchecked((ushort)o.Value));

    internal static void Write(string outputPath)
    {
        Assembly assembly = typeof(TagStorage).Assembly;
        if (assembly.GetName().Name != "Test") throw new InvalidOperationException("Expected assembly name Test.");
        Type[] types = assembly.GetTypes().Where(IsRuntimeType).OrderBy(TypeName, StringComparer.Ordinal).ToArray();
        if (types.Length == 0) throw new InvalidOperationException("No GameplayTags Runtime types found.");
        var root = Obj("schema", "two-capacity-metadata-v1", "assembly", assembly.FullName,
            "selection", "GameplayTags namespace (including nested/generated types); harness must stay outside it",
            "types", types.Select(CaptureType).ToArray(),
            "excluded_types", assembly.GetTypes().Where(t => !IsRuntimeType(t)).OrderBy(TypeName, StringComparer.Ordinal)
                .Select(t => Obj("id", TypeName(t), "metadata_token", Token(t.MetadataToken),
                    "reason", t.Name.StartsWith("<PrivateImplementationDetails>", StringComparison.Ordinal) ||
                        (t.DeclaringType != null && t.DeclaringType.Name.StartsWith("<PrivateImplementationDetails>", StringComparison.Ordinal))
                        ? "Global compiler implementation details can combine Runtime and harness contributions; ownership is not inferred."
                        : "Outside the GameplayTags Runtime namespace; harness and support declarations are not compared as Runtime.")).ToArray());
        // CreateNew avoids silently replacing a previously captured evidence file.
        using (var writer = new StreamWriter(new FileStream(outputPath, FileMode.CreateNew), new UTF8Encoding(false)))
        { Json(writer, root); writer.WriteLine(); }
    }

    private static bool IsRuntimeType(Type type)
    {
        Type root = type;
        while (root.DeclaringType != null) root = root.DeclaringType;
        return root.Namespace == "GameplayTags" || (root.Namespace != null && root.Namespace.StartsWith("GameplayTags.", StringComparison.Ordinal));
    }

    private static Dictionary<string, object> CaptureType(Type type)
    {
        MethodBase[] methods = type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared))
            .Concat(type.TypeInitializer == null ? new MethodBase[0] : new MethodBase[] { type.TypeInitializer })
            .GroupBy(m => m.MetadataToken).Select(g => g.First()).OrderBy(m => m.MetadataToken).ToArray();
        var layout = type.StructLayoutAttribute;
        return Obj("id", TypeName(type), "metadata_token", Token(type.MetadataToken),
            "attributes", (int)type.Attributes, "attributes_text", type.Attributes.ToString(),
            "beforefieldinit", (type.Attributes & TypeAttributes.BeforeFieldInit) != 0,
            "base_type", QType(type.BaseType), "declaring_type", QType(type.DeclaringType),
            "interfaces", type.GetInterfaces().Select(QType).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            "generic_parameters", type.IsGenericTypeDefinition ? type.GetGenericArguments().Select(GenericParameter).ToArray() : new object[0],
            "layout", layout == null ? null : Obj("kind", (int)layout.Value, "pack", layout.Pack, "size", layout.Size, "charset", (int)layout.CharSet),
            "custom_attributes", Attributes(type.GetCustomAttributesData()),
            "fields", type.GetFields(Declared).OrderBy(f => f.MetadataToken).Select((f, i) => CaptureField(f, i)).ToArray(),
            "properties", type.GetProperties(Declared).OrderBy(p => p.MetadataToken).Select(CaptureProperty).ToArray(),
            "events", type.GetEvents(Declared).OrderBy(e => e.MetadataToken).Select(CaptureEvent).ToArray(),
            "methods", methods.Select((m, i) => CaptureMethod(m, i)).ToArray(),
            "interface_map", InterfaceMap(type));
    }

    private static object[] InterfaceMap(Type type)
    {
        if (type.IsInterface || type.IsGenericParameter) return new object[0];
        var result = new List<object>();
        foreach (Type iface in type.GetInterfaces().OrderBy(QType, StringComparer.Ordinal))
        {
            InterfaceMapping map = type.GetInterfaceMap(iface);
            for (int i = 0; i < map.InterfaceMethods.Length; i++)
                result.Add(Obj("interface", QType(iface), "declaration", MethodReference(map.InterfaceMethods[i]),
                    "implementation", MethodReference(map.TargetMethods[i])));
        }
        return result.OrderBy(Serialize, StringComparer.Ordinal).ToArray();
    }

    private static object CaptureField(FieldInfo field, int ordinal)
    {
        return Obj("id", TypeName(field.DeclaringType) + "::" + field.Name, "ordinal", ordinal,
            "metadata_token", Token(field.MetadataToken), "attributes", (int)field.Attributes,
            "attributes_text", field.Attributes.ToString(), "field_type", QType(field.FieldType),
            "required_modifiers", field.GetRequiredCustomModifiers().Select(QType).ToArray(),
            "optional_modifiers", field.GetOptionalCustomModifiers().Select(QType).ToArray(),
            "literal_value", field.IsLiteral ? Value(field.GetRawConstantValue()) : null,
            "custom_attributes", Attributes(field.GetCustomAttributesData()));
    }

    private static object CaptureProperty(PropertyInfo property)
    {
        return Obj("id", TypeName(property.DeclaringType) + "::" + property.Name + "(" +
            string.Join(",", property.GetIndexParameters().Select(p => TypeName(p.ParameterType))) + ")",
            "metadata_token", Token(property.MetadataToken), "attributes", (int)property.Attributes,
            "property_type", QType(property.PropertyType),
            "required_modifiers", property.GetRequiredCustomModifiers().Select(QType).ToArray(),
            "optional_modifiers", property.GetOptionalCustomModifiers().Select(QType).ToArray(),
            "index_parameters", property.GetIndexParameters().Select(CaptureParameter).ToArray(),
            "accessors", property.GetAccessors(true).Select(MethodId).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            "custom_attributes", Attributes(property.GetCustomAttributesData()));
    }

    private static object CaptureEvent(EventInfo item)
    {
        return Obj("id", TypeName(item.DeclaringType) + "::" + item.Name, "metadata_token", Token(item.MetadataToken),
            "attributes", (int)item.Attributes, "event_type", QType(item.EventHandlerType),
            "add", OptionalMethodId(item.GetAddMethod(true)), "remove", OptionalMethodId(item.GetRemoveMethod(true)),
            "raise", OptionalMethodId(item.GetRaiseMethod(true)),
            "other_methods", item.GetOtherMethods(true).Select(MethodId).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            "custom_attributes", Attributes(item.GetCustomAttributesData()));
    }

    private static object CaptureMethod(MethodBase method, int ordinal)
    {
        MethodInfo info = method as MethodInfo;
        return Obj("id", MethodId(method), "name", method.Name, "ordinal", ordinal,
            "metadata_token", Token(method.MetadataToken), "signature", MethodReference(method),
            "attributes", (int)method.Attributes, "attributes_text", method.Attributes.ToString(),
            "implementation_flags", (int)method.GetMethodImplementationFlags(),
            "implementation_flags_text", method.GetMethodImplementationFlags().ToString(),
            "custom_attributes", Attributes(method.GetCustomAttributesData()),
            "parameters", method.GetParameters().Select(CaptureParameter).ToArray(),
            "return_parameter", info == null ? null : CaptureParameter(info.ReturnParameter),
            "generic_parameters", method.IsGenericMethodDefinition ? method.GetGenericArguments().Select(GenericParameter).ToArray() : new object[0],
            "base_definition", info == null ? null : MethodId(info.GetBaseDefinition()),
            "body", CaptureBody(method));
    }

    private static object CaptureParameter(ParameterInfo parameter)
    {
        return Obj("name", parameter.Name, "position", parameter.Position, "metadata_token", Token(parameter.MetadataToken),
            "parameter_type", QType(parameter.ParameterType), "attributes", (int)parameter.Attributes,
            "required_modifiers", parameter.GetRequiredCustomModifiers().Select(QType).ToArray(),
            "optional_modifiers", parameter.GetOptionalCustomModifiers().Select(QType).ToArray(),
            "has_default", parameter.HasDefaultValue, "default_value", parameter.HasDefaultValue ? Value(parameter.RawDefaultValue) : null,
            "custom_attributes", Attributes(parameter.GetCustomAttributesData()));
    }

    private static object GenericParameter(Type type)
    {
        return Obj("name", type.Name, "position", type.GenericParameterPosition, "attributes", (int)type.GenericParameterAttributes,
            "constraints", type.GetGenericParameterConstraints().Select(QType).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            "custom_attributes", Attributes(type.GetCustomAttributesData()));
    }

    private static object CaptureBody(MethodBase method)
    {
        MethodBody body = method.GetMethodBody();
        if (body == null) return null;
        byte[] bytes = body.GetILAsByteArray();
        Type[] typeArgs = method.DeclaringType.GetGenericArguments();
        Type[] methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
        string localSignature = body.LocalSignatureMetadataToken == 0 ? null :
            new SignatureDecoder(method.Module, method.Module.ResolveSignature(body.LocalSignatureMetadataToken), typeArgs, methodArgs).Read();
        return Obj("max_stack", body.MaxStackSize, "init_locals", body.InitLocals,
            "local_signature_token", Token(body.LocalSignatureMetadataToken),
            "local_signature_raw", body.LocalSignatureMetadataToken == 0 ? null : Hex(method.Module.ResolveSignature(body.LocalSignatureMetadataToken)),
            "local_signature", localSignature,
            "locals", body.LocalVariables.Select(v => Obj("index", v.LocalIndex, "type", QType(v.LocalType), "pinned", v.IsPinned)).ToArray(),
            "exception_regions", body.ExceptionHandlingClauses.Select(c => Obj("flags", (int)c.Flags,
                "try_offset", c.TryOffset, "try_length", c.TryLength, "handler_offset", c.HandlerOffset,
                "handler_length", c.HandlerLength, "filter_offset", c.Flags == ExceptionHandlingClauseOptions.Filter ? (object)c.FilterOffset : null,
                "catch_type", c.Flags == ExceptionHandlingClauseOptions.Clause ? QType(c.CatchType) : null)).ToArray(),
            "il_length", bytes.Length, "il_raw", Hex(bytes), "instructions", DecodeIL(method, bytes));
    }

    // Extends the counted-self-index Inspection.cs opcode decoder: every operand category,
    // generic context, switch/branch targets, signed immediates, and raw token evidence.
    private static object[] DecodeIL(MethodBase method, byte[] bytes)
    {
        var rows = new List<object>();
        Type[] typeArgs = method.DeclaringType.GetGenericArguments();
        Type[] methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
        for (int p = 0; p < bytes.Length;)
        {
            int start = p;
            ushort code = bytes[p++];
            if (code == 0xfe) code = (ushort)(0xfe00 | bytes[p++]);
            OpCode op;
            if (!Opcodes.TryGetValue(code, out op)) throw new InvalidDataException("Unknown opcode " + code);
            int operandOffset = p;
            object operand = null;
            int? token = null;
            switch (op.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineI: operand = (int)(sbyte)bytes[p++]; break;
                case OperandType.ShortInlineVar: operand = (int)bytes[p++]; break;
                case OperandType.InlineVar: operand = (int)BitConverter.ToUInt16(bytes, p); p += 2; break;
                case OperandType.InlineI: operand = BitConverter.ToInt32(bytes, p); p += 4; break;
                case OperandType.InlineI8: operand = BitConverter.ToInt64(bytes, p).ToString(CultureInfo.InvariantCulture); p += 8; break;
                case OperandType.ShortInlineR: operand = "float32-bits:" + Hex(bytes.Skip(p).Take(4).ToArray()); p += 4; break;
                case OperandType.InlineR: operand = "float64-bits:" + Hex(bytes.Skip(p).Take(8).ToArray()); p += 8; break;
                case OperandType.ShortInlineBrTarget: operand = p + 1 + (sbyte)bytes[p]; p++; break;
                case OperandType.InlineBrTarget: operand = p + 4 + BitConverter.ToInt32(bytes, p); p += 4; break;
                case OperandType.InlineSwitch:
                    int count = BitConverter.ToInt32(bytes, p); p += 4;
                    if (count < 0 || count > (bytes.Length - p) / 4) throw new InvalidDataException("Invalid switch.");
                    int end = p + count * 4;
                    var targets = new int[count];
                    for (int i = 0; i < count; i++) { targets[i] = end + BitConverter.ToInt32(bytes, p); p += 4; }
                    operand = targets; break;
                case OperandType.InlineMethod: case OperandType.InlineField: case OperandType.InlineType: case OperandType.InlineTok:
                    token = BitConverter.ToInt32(bytes, p); p += 4;
                    operand = MemberReference(method.Module.ResolveMember(token.Value, typeArgs, methodArgs)); break;
                case OperandType.InlineString:
                    token = BitConverter.ToInt32(bytes, p); p += 4;
                    operand = method.Module.ResolveString(token.Value); break;
                case OperandType.InlineSig:
                    token = BitConverter.ToInt32(bytes, p); p += 4;
                    operand = new SignatureDecoder(method.Module, method.Module.ResolveSignature(token.Value), typeArgs, methodArgs).Read(); break;
                default: throw new InvalidDataException("Unsupported operand " + op.OperandType);
            }
            if (p > bytes.Length) throw new InvalidDataException("Truncated IL operand.");
            rows.Add(Obj("offset", start, "opcode", op.Name, "opcode_value", (int)code,
                "operand_kind", op.OperandType.ToString(), "operand_offset", operandOffset,
                "size", p - start, "operand", operand,
                "metadata_token", token.HasValue ? Token(token.Value) : null,
                "operand_raw", Hex(bytes.Skip(operandOffset).Take(p - operandOffset).ToArray())));
        }
        return rows.ToArray();
    }

    private static object MemberReference(MemberInfo member)
    {
        Type type = member as Type;
        if (type != null) return Obj("kind", "type", "type", QType(type));
        MethodBase method = member as MethodBase;
        if (method != null) return MethodReference(method);
        FieldInfo field = member as FieldInfo;
        if (field != null) return Obj("kind", "field", "declaring_type", QType(field.DeclaringType), "name", field.Name,
            "field_type", QType(field.FieldType), "required_modifiers", field.GetRequiredCustomModifiers().Select(QType).ToArray(),
            "optional_modifiers", field.GetOptionalCustomModifiers().Select(QType).ToArray());
        throw new InvalidDataException("Unexpected token member kind " + member.MemberType);
    }

    private static object MethodReference(MethodBase method)
    {
        MethodInfo info = method as MethodInfo;
        return Obj("kind", "method", "declaring_type", QType(method.DeclaringType), "name", method.Name,
            "static", method.IsStatic, "calling_convention", (int)method.CallingConvention,
            "generic_arguments", method.IsGenericMethod ? method.GetGenericArguments().Select(QType).ToArray() : new string[0],
            "parameters", method.GetParameters().Select(p => Obj("type", QType(p.ParameterType),
                "required_modifiers", p.GetRequiredCustomModifiers().Select(QType).ToArray(),
                "optional_modifiers", p.GetOptionalCustomModifiers().Select(QType).ToArray())).ToArray(),
            "return_type", QType(info == null ? typeof(void) : info.ReturnType),
            "return_required_modifiers", info == null ? new string[0] : info.ReturnParameter.GetRequiredCustomModifiers().Select(QType).ToArray(),
            "return_optional_modifiers", info == null ? new string[0] : info.ReturnParameter.GetOptionalCustomModifiers().Select(QType).ToArray());
    }

    private static string OptionalMethodId(MethodBase method) { return method == null ? null : MethodId(method); }
    private static string MethodId(MethodBase method)
    {
        MethodInfo info = method as MethodInfo;
        return TypeName(method.DeclaringType) + "::" + method.Name +
            (method.IsGenericMethod ? "``" + method.GetGenericArguments().Length : "") + "(" +
            string.Join(",", method.GetParameters().Select(p => TypeName(p.ParameterType))) + "):" +
            TypeName(info == null ? typeof(void) : info.ReturnType);
    }

    private static string TypeName(Type type) { return FormatType(type, false); }
    private static string QType(Type type) { return FormatType(type, true); }
    private static string FormatType(Type type, bool qualified)
    {
        if (type == null) return null;
        if (type.IsGenericParameter) return (type.DeclaringMethod == null ? "!" : "!!") + type.GenericParameterPosition;
        if (type.IsByRef) return FormatType(type.GetElementType(), qualified) + "&";
        if (type.IsPointer) return FormatType(type.GetElementType(), qualified) + "*";
        if (type.IsArray)
        {
            string suffix = type.GetArrayRank() == 1 ? (type == type.GetElementType().MakeArrayType() ? "[]" : "[*]") : "[" + new string(',', type.GetArrayRank() - 1) + "]";
            return FormatType(type.GetElementType(), qualified) + suffix;
        }
        string prefix = qualified ? "[" + type.Assembly.FullName + "]" : "";
        if (type.IsGenericType)
            return prefix + type.GetGenericTypeDefinition().FullName + "<" + string.Join(",", type.GetGenericArguments().Select(t => FormatType(t, qualified))) + ">";
        if (type.FullName == null) throw new InvalidDataException("Unnameable signature type " + type);
        return prefix + type.FullName;
    }

    private static object[] Attributes(IList<CustomAttributeData> attributes)
    {
        return attributes.Select(a => (object)Obj("type", QType(a.AttributeType), "constructor", MethodReference(a.Constructor),
            "arguments", a.ConstructorArguments.Select(AttributeValue).ToArray(),
            "named_arguments", a.NamedArguments.Select(n => Obj("name", n.MemberName, "field", n.IsField, "value", AttributeValue(n.TypedValue)))
                .OrderBy(Serialize, StringComparer.Ordinal).ToArray())).OrderBy(Serialize, StringComparer.Ordinal).ToArray();
    }
    private static object AttributeValue(CustomAttributeTypedArgument value)
    {
        var array = value.Value as IList<CustomAttributeTypedArgument>;
        return Obj("type", QType(value.ArgumentType), "value", array == null ? Value(value.Value) : array.Select(AttributeValue).ToArray());
    }
    private static object Value(object value)
    {
        if (value == null) return null;
        if (value is Type) return Obj("type_reference", QType((Type)value));
        if (value is string || value is bool) return value;
        if (value is char) return Obj("char_code", (int)(char)value);
        if (value is float) return Obj("float32_bits", Hex(BitConverter.GetBytes((float)value)));
        if (value is double) return Obj("float64_bits", Hex(BitConverter.GetBytes((double)value)));
        if (value is DateTime) return Obj("datetime_ticks", ((DateTime)value).Ticks.ToString(CultureInfo.InvariantCulture), "kind", ((DateTime)value).Kind.ToString());
        return Obj("runtime_type", QType(value.GetType()), "value", Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    // ECMA-335 standalone method/local signatures. TypeDefOrRef operands are resolved
    // before comparison, while their original bytes remain in local_signature_raw.
    private sealed class SignatureDecoder
    {
        private readonly Module module; private readonly byte[] bytes; private readonly Type[] ta, ma; private int p;
        internal SignatureDecoder(Module module, byte[] bytes, Type[] typeArgs, Type[] methodArgs)
        { this.module = module; this.bytes = bytes; ta = typeArgs; ma = methodArgs; }
        private byte Byte() { if (p >= bytes.Length) throw new InvalidDataException("Truncated signature."); return bytes[p++]; }
        private uint Compressed()
        {
            byte b = Byte();
            if ((b & 0x80) == 0) return b;
            if ((b & 0xc0) == 0x80) return (uint)(((b & 0x3f) << 8) | Byte());
            if ((b & 0xe0) == 0xc0) return (uint)(((b & 0x1f) << 24) | (Byte() << 16) | (Byte() << 8) | Byte());
            throw new InvalidDataException("Invalid compressed signature integer.");
        }
        private int SignedCompressed()
        {
            int start = p; uint value = Compressed(); int bits = p - start == 1 ? 7 : (p - start == 2 ? 14 : 29);
            int result = (int)(value >> 1);
            if ((value & 1) != 0) result |= -1 << (bits - 1);
            return result;
        }
        internal string Read()
        {
            string result = Signature();
            if (p != bytes.Length) throw new InvalidDataException("Unconsumed signature bytes.");
            return result;
        }
        private string Signature()
        {
            byte convention = Byte();
            if ((convention & 15) == 7)
            {
                uint count = Compressed(); var locals = new List<string>();
                for (uint i = 0; i < count; i++) locals.Add(Type());
                return "locals(" + string.Join(",", locals) + ")";
            }
            uint genericCount = (convention & 0x10) != 0 ? Compressed() : 0;
            uint parameterCount = Compressed(); string result = Type(); var parameters = new List<string>();
            for (uint i = 0; i < parameterCount; i++)
            {
                if (p < bytes.Length && bytes[p] == 0x41) { p++; parameters.Add("sentinel"); }
                parameters.Add(Type());
            }
            return "callconv=" + convention + ";generic=" + genericCount + ";(" + string.Join(",", parameters) + ")->" + result;
        }
        private string TypeToken()
        {
            uint encoded = Compressed(); int table;
            switch (encoded & 3) { case 0: table = 0x02000000; break; case 1: table = 0x01000000; break; case 2: table = 0x1b000000; break; default: throw new InvalidDataException("Invalid TypeDefOrRef."); }
            return QType(module.ResolveType(table | checked((int)(encoded >> 2)), ta, ma));
        }
        private string Type()
        {
            byte kind = Byte();
            switch (kind)
            {
                case 0x01: return "void"; case 0x02: return "bool"; case 0x03: return "char";
                case 0x04: return "i1"; case 0x05: return "u1"; case 0x06: return "i2"; case 0x07: return "u2";
                case 0x08: return "i4"; case 0x09: return "u4"; case 0x0a: return "i8"; case 0x0b: return "u8";
                case 0x0c: return "r4"; case 0x0d: return "r8"; case 0x0e: return "string";
                case 0x0f: return "ptr(" + Type() + ")"; case 0x10: return "byref(" + Type() + ")";
                case 0x11: return "valuetype(" + TypeToken() + ")"; case 0x12: return "class(" + TypeToken() + ")";
                case 0x13: return "!" + Compressed(); case 0x1e: return "!!" + Compressed();
                case 0x14:
                    string element = Type(); uint rank = Compressed(); uint sizesCount = Compressed(); var sizes = new List<string>();
                    for (uint i = 0; i < sizesCount; i++) sizes.Add(Compressed().ToString(CultureInfo.InvariantCulture));
                    uint boundsCount = Compressed(); var bounds = new List<string>();
                    for (uint i = 0; i < boundsCount; i++) bounds.Add(SignedCompressed().ToString(CultureInfo.InvariantCulture));
                    return "array(" + element + ";rank=" + rank + ";sizes=" + string.Join(",", sizes) + ";bounds=" + string.Join(",", bounds) + ")";
                case 0x15:
                    byte category = Byte();
                    if (category != 0x11 && category != 0x12) throw new InvalidDataException("Invalid generic instance category.");
                    string generic = TypeToken(); uint count = Compressed(); var arguments = new List<string>();
                    for (uint i = 0; i < count; i++) arguments.Add(Type());
                    return "generic(" + category + ";" + generic + "<" + string.Join(",", arguments) + ">)";
                case 0x16: return "typedbyref"; case 0x18: return "nativeint"; case 0x19: return "nativeuint";
                case 0x1b: return "fnptr(" + Signature() + ")"; case 0x1c: return "object";
                case 0x1d: return "szarray(" + Type() + ")";
                case 0x1f: return "modreq(" + TypeToken() + ";" + Type() + ")";
                case 0x20: return "modopt(" + TypeToken() + ";" + Type() + ")";
                case 0x45: return "pinned(" + Type() + ")";
                default: throw new InvalidDataException("Unsupported signature element 0x" + kind.ToString("X2"));
            }
        }
    }

    private static string Token(int token) { return "0x" + token.ToString("X8", CultureInfo.InvariantCulture); }
    private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    private static Dictionary<string, object> Obj(params object[] pairs)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        for (int i = 0; i < pairs.Length; i += 2) result.Add((string)pairs[i], pairs[i + 1]);
        return result;
    }
    private static string Serialize(object value) { using (var writer = new StringWriter(CultureInfo.InvariantCulture)) { Json(writer, value); return writer.ToString(); } }
    private static void Json(TextWriter writer, object value)
    {
        if (value == null) { writer.Write("null"); return; }
        string text = value as string;
        if (text != null)
        {
            writer.Write('"');
            foreach (char c in text)
            {
                if (c == '"' || c == '\\') { writer.Write('\\'); writer.Write(c); }
                else if (c < 32 || char.IsSurrogate(c)) { writer.Write("\\u"); writer.Write(((int)c).ToString("x4", CultureInfo.InvariantCulture)); }
                else writer.Write(c);
            }
            writer.Write('"'); return;
        }
        if (value is bool) { writer.Write((bool)value ? "true" : "false"); return; }
        var map = value as IDictionary<string, object>;
        if (map != null)
        {
            writer.Write('{'); bool first = true;
            foreach (var pair in map.OrderBy(x => x.Key, StringComparer.Ordinal))
            { if (!first) writer.Write(','); first = false; Json(writer, pair.Key); writer.Write(':'); Json(writer, pair.Value); }
            writer.Write('}'); return;
        }
        var sequence = value as IEnumerable;
        if (sequence != null)
        {
            writer.Write('['); bool first = true;
            foreach (object item in sequence) { if (!first) writer.Write(','); first = false; Json(writer, item); }
            writer.Write(']'); return;
        }
        if (value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint || value is long || value is ulong)
        { writer.Write(Convert.ToString(value, CultureInfo.InvariantCulture)); return; }
        throw new InvalidDataException("Unsupported JSON value " + value.GetType());
    }
}
