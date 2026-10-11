using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
internal sealed class Names : ISignatureTypeProvider<string, object>
{
    internal MetadataReader R;
    internal Dictionary<MethodDefinitionHandle,string> Methods=new Dictionary<MethodDefinitionHandle,string>();
    internal Dictionary<FieldDefinitionHandle,string> Fields=new Dictionary<FieldDefinitionHandle,string>();
    internal Names(MetadataReader r) { R=r; foreach(var t in r.TypeDefinitions) { foreach(var m in r.GetTypeDefinition(t).GetMethods())Methods[m]=Type(t);foreach(var f in r.GetTypeDefinition(t).GetFields())Fields[f]=Type(t); } }
    internal string Type(TypeDefinitionHandle h) { var t=R.GetTypeDefinition(h);var owner=t.GetDeclaringType();return owner.IsNil?R.GetString(t.Namespace)+"."+R.GetString(t.Name):Type(owner)+"+"+R.GetString(t.Name); }
    internal string Signature(MethodSignature<string> s) => "header="+s.Header.RawValue+";gen="+s.GenericParameterCount+";required="+s.RequiredParameterCount+";("+string.Join(",",s.ParameterTypes)+")->"+s.ReturnType;
    internal string Entity(EntityHandle h) {
        if(h.IsNil)return "nil";
        switch(h.Kind) {
            case HandleKind.TypeDefinition:return Type((TypeDefinitionHandle)h);
            case HandleKind.TypeReference:var t=R.GetTypeReference((TypeReferenceHandle)h);return "["+Entity(t.ResolutionScope)+"]"+R.GetString(t.Namespace)+"."+R.GetString(t.Name);
            case HandleKind.TypeSpecification:return R.GetTypeSpecification((TypeSpecificationHandle)h).DecodeSignature(this,null);
            case HandleKind.MethodDefinition:var mh=(MethodDefinitionHandle)h;var m=R.GetMethodDefinition(mh);return Methods[mh]+"::"+R.GetString(m.Name)+"|"+Signature(m.DecodeSignature(this,null));
            case HandleKind.FieldDefinition:var fh=(FieldDefinitionHandle)h;var f=R.GetFieldDefinition(fh);return Fields[fh]+"::"+R.GetString(f.Name)+"|"+f.DecodeSignature(this,null);
            case HandleKind.MemberReference:var mr=R.GetMemberReference((MemberReferenceHandle)h);return Entity(mr.Parent)+"::"+R.GetString(mr.Name)+"|"+(mr.GetKind()==MemberReferenceKind.Method?Signature(mr.DecodeMethodSignature(this,null)):mr.DecodeFieldSignature(this,null));
            case HandleKind.MethodSpecification:var ms=R.GetMethodSpecification((MethodSpecificationHandle)h);return Entity(ms.Method)+"<"+string.Join(",",ms.DecodeSignature(this,null))+">";
            case HandleKind.AssemblyReference:return R.GetString(R.GetAssemblyReference((AssemblyReferenceHandle)h).Name);
            case HandleKind.ModuleReference:return R.GetString(R.GetModuleReference((ModuleReferenceHandle)h).Name);
            case HandleKind.ModuleDefinition:return "module";
            case HandleKind.StandaloneSignature:var ss=R.GetStandaloneSignature((StandaloneSignatureHandle)h);return ss.GetKind()==StandaloneSignatureKind.LocalVariables?string.Join(",",ss.DecodeLocalSignature(this,null)):Signature(ss.DecodeMethodSignature(this,null));
            default:throw new Exception("Unsupported entity "+h.Kind);
        }
    }
    public string GetArrayType(string e,ArrayShape s)=>e+"[rank="+s.Rank+";sizes="+string.Join(",",s.Sizes)+";lower="+string.Join(",",s.LowerBounds)+"]";
    public string GetByReferenceType(string e)=>e+"&";
    public string GetFunctionPointerType(MethodSignature<string> s)=>"fnptr:"+Signature(s);
    public string GetGenericInstantiation(string g,ImmutableArray<string> a)=>g+"<"+string.Join(",",a)+">";
    public string GetGenericMethodParameter(object c,int i)=>"!!"+i;
    public string GetGenericTypeParameter(object c,int i)=>"!"+i;
    public string GetModifiedType(string m,string u,bool r)=>(r?"modreq":"modopt")+"("+m+")"+u;
    public string GetPinnedType(string e)=>"pinned "+e;
    public string GetPointerType(string e)=>e+"*";
    public string GetPrimitiveType(PrimitiveTypeCode c)=>c.ToString();
    public string GetSZArrayType(string e)=>e+"[]";
    public string GetTypeFromDefinition(MetadataReader r,TypeDefinitionHandle h,byte k)=>Type(h);
    public string GetTypeFromReference(MetadataReader r,TypeReferenceHandle h,byte k)=>Entity(h);
    public string GetTypeFromSpecification(MetadataReader r,object c,TypeSpecificationHandle h,byte k)=>Entity(h);
}
internal static class Inspect
{
    static string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
    static Dictionary<int,OpCode> Ops=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Where(x=>x.FieldType==typeof(OpCode)).Select(x=>(OpCode)x.GetValue(null)).GroupBy(x=>(int)(ushort)x.Value).ToDictionary(x=>x.Key,x=>x.First());
    static string Normalize(byte[] code,Names names) {
        var lines=new List<string>();int p=0;
        while(p<code.Length) {
            int offset=p,key=code[p++];if(key==254)key=0xfe00|code[p++];OpCode op=Ops[key];string value="";int bytes=0;
            switch(op.OperandType) {
                case OperandType.InlineNone:break;
                case OperandType.ShortInlineBrTarget:case OperandType.ShortInlineI:case OperandType.ShortInlineVar:bytes=1;break;
                case OperandType.InlineVar:bytes=2;break;
                case OperandType.InlineBrTarget:case OperandType.InlineI:case OperandType.ShortInlineR:bytes=4;break;
                case OperandType.InlineI8:case OperandType.InlineR:bytes=8;break;
                case OperandType.InlineSwitch:bytes=4+4*BitConverter.ToInt32(code,p);break;
                case OperandType.InlineString:value="string:"+names.R.GetUserString(MetadataTokens.UserStringHandle(BitConverter.ToInt32(code,p)&0xffffff));p+=4;break;
                case OperandType.InlineField:case OperandType.InlineMethod:case OperandType.InlineSig:case OperandType.InlineTok:case OperandType.InlineType:value=names.Entity(MetadataTokens.EntityHandle(BitConverter.ToInt32(code,p)));p+=4;break;
                default:throw new Exception("Unsupported operand "+op.OperandType);
            }
            if(bytes!=0){value=Convert.ToHexString(code.AsSpan(p,bytes));p+=bytes;}
            lines.Add(offset.ToString("x4")+":"+op.Name+":"+value);
        }
        if(p!=code.Length)throw new Exception("IL length mismatch");return string.Join("\n",lines);
    }
    static string Blob(MetadataReader r, BlobHandle h) => h.IsNil ? "" : Convert.ToHexString(r.GetBlobBytes(h));
    static object Constant(MetadataReader r, ConstantHandle h) { if(h.IsNil)return null;var c=r.GetConstant(h);return new{TypeCode=c.TypeCode.ToString(),Blob=Blob(r,c.Value)}; }
    static object[] Attributes(MetadataReader r, Names n, CustomAttributeHandleCollection handles) => handles.Select(h=>{var a=r.GetCustomAttribute(h);return (object)new{Constructor=n.Entity(a.Constructor),Blob=Blob(r,a.Value)};}).ToArray();
    static object[] Generics(MetadataReader r,Names n,GenericParameterHandleCollection handles) => handles.Select(h=>{var g=r.GetGenericParameter(h);return (object)new{Index=g.Index,Name=r.GetString(g.Name),Attributes=(int)g.Attributes,CustomAttributes=Attributes(r,n,g.GetCustomAttributes()),Constraints=g.GetConstraints().Select(c=>{var x=r.GetGenericParameterConstraint(c);return new{Type=n.Entity(x.Type),CustomAttributes=Attributes(r,n,x.GetCustomAttributes())};}).ToArray()};}).ToArray();
    static void Main(string[] args) {
        using var stream=File.OpenRead(args[0]);using var pe=new PEReader(stream);var r=pe.GetMetadataReader();var names=new Names(r);
        var methods=new List<object>();var types=new List<object>();var fields=new List<object>();var properties=new List<object>();var events=new List<object>();
        var owners=new Dictionary<EntityHandle,string>();
        owners[MetadataTokens.EntityHandle(0x20000001)]="assembly";owners[MetadataTokens.EntityHandle(1)]="module";
        Action<GenericParameterHandleCollection,string> registerGenerics=(handles,owner)=>{foreach(var h in handles){var g=r.GetGenericParameter(h);string key=owner+"|generic="+g.Index;owners[h]=key;foreach(var c in g.GetConstraints()){var x=r.GetGenericParameterConstraint(c);owners[c]=key+"|constraint="+names.Entity(x.Type);}}};
        foreach(var handle in r.TypeDefinitions) {
            var t=r.GetTypeDefinition(handle);string key=names.Type(handle);owners[handle]=key;registerGenerics(t.GetGenericParameters(),key);var layout=t.GetLayout();
            types.Add(new{Key=key,Token=MetadataTokens.GetToken(handle),Attributes=(int)t.Attributes,BeforeFieldInit=(t.Attributes&TypeAttributes.BeforeFieldInit)!=0,BaseType=names.Entity(t.BaseType),DeclaringType=t.GetDeclaringType().IsNil?null:names.Type(t.GetDeclaringType()),Layout=new{layout.Size,layout.PackingSize,layout.IsDefault},CustomAttributes=Attributes(r,names,t.GetCustomAttributes()),GenericParameters=Generics(r,names,t.GetGenericParameters()),Interfaces=t.GetInterfaceImplementations().Select(h=>{var x=r.GetInterfaceImplementation(h);owners[h]=key+"|interface="+names.Entity(x.Interface);return new{Type=names.Entity(x.Interface),CustomAttributes=Attributes(r,names,x.GetCustomAttributes())};}).ToArray(),MethodImplementations=t.GetMethodImplementations().Select(h=>{var x=r.GetMethodImplementation(h);return new{Body=names.Entity(x.MethodBody),Declaration=names.Entity(x.MethodDeclaration)};}).ToArray()});
            foreach(var h in t.GetProperties()){var x=r.GetPropertyDefinition(h);string pk=key+"::"+r.GetString(x.Name)+"|"+names.Signature(x.DecodeSignature(names,null));owners[h]=pk;var ac=x.GetAccessors();properties.Add(new{Key=pk,Token=MetadataTokens.GetToken(h),Attributes=(int)x.Attributes,CustomAttributes=Attributes(r,names,x.GetCustomAttributes()),DefaultValue=Constant(r,x.GetDefaultValue()),Getter=names.Entity(ac.Getter),Setter=names.Entity(ac.Setter),Others=ac.Others.Select(v=>names.Entity(v)).ToArray()});}
            foreach(var h in t.GetEvents()){var x=r.GetEventDefinition(h);string ek=key+"::"+r.GetString(x.Name)+"|"+names.Entity(x.Type);owners[h]=ek;var ac=x.GetAccessors();events.Add(new{Key=ek,Token=MetadataTokens.GetToken(h),Attributes=(int)x.Attributes,CustomAttributes=Attributes(r,names,x.GetCustomAttributes()),Adder=names.Entity(ac.Adder),Remover=names.Entity(ac.Remover),Raiser=names.Entity(ac.Raiser),Others=ac.Others.Select(v=>names.Entity(v)).ToArray()});}
        }
        foreach(var handle in r.FieldDefinitions) {
            var f=r.GetFieldDefinition(handle);string key=names.Entity(handle);owners[handle]=key;
            fields.Add(new{Key=key,Type=names.Fields[handle],Name=r.GetString(f.Name),Token=MetadataTokens.GetToken(handle),Attributes=(int)f.Attributes,SignatureRaw=Blob(r,f.Signature),CustomAttributes=Attributes(r,names,f.GetCustomAttributes()),DefaultValue=Constant(r,f.GetDefaultValue()),Offset=f.GetOffset(),RVA=f.GetRelativeVirtualAddress(),MarshallingDescriptor=Blob(r,f.GetMarshallingDescriptor())});
        }
        foreach(var handle in r.MethodDefinitions) {
            var m=r.GetMethodDefinition(handle);var type=names.Methods[handle];string key=names.Entity(handle);owners[handle]=key;registerGenerics(m.GetGenericParameters(),key);object body=null;
            if(m.RelativeVirtualAddress!=0){var b=pe.GetMethodBody(m.RelativeVirtualAddress);byte[] bytes=b.GetILBytes();string normalized=Normalize(bytes,names);body=new { Length=bytes.Length, RawSHA256=Hash(bytes), RawBase64=Convert.ToBase64String(bytes), NormalizedSHA256=Hash(Encoding.UTF8.GetBytes(normalized)), NormalizedIL=normalized, MaxStack=b.MaxStack, InitLocals=b.LocalVariablesInitialized, LocalSignatureToken=MetadataTokens.GetToken(b.LocalSignature), LocalSignature=names.Entity(b.LocalSignature), Regions=b.ExceptionRegions.Select(e=>new{Kind=e.Kind.ToString(),e.TryOffset,e.TryLength,e.HandlerOffset,e.HandlerLength,e.FilterOffset,CatchType=names.Entity(e.CatchType)}).ToArray(),FullBodySize=b.Size,FullBodySHA256=Hash(pe.GetSectionData(m.RelativeVirtualAddress).GetContent(0,b.Size).ToArray()) };}
            var import=m.GetImport();
            methods.Add(new { Key=key,Type=type,Name=r.GetString(m.Name),Token=MetadataTokens.GetToken(handle),RVA=m.RelativeVirtualAddress, Attributes=(int)m.Attributes,ImplAttributes=(int)m.ImplAttributes,SignatureRaw=Blob(r,m.Signature),CustomAttributes=Attributes(r,names,m.GetCustomAttributes()),GenericParameters=Generics(r,names,m.GetGenericParameters()),Parameters=m.GetParameters().Select(h=>{var x=r.GetParameter(h);owners[h]=key+"|parameter="+x.SequenceNumber;return new{Sequence=x.SequenceNumber,Name=r.GetString(x.Name),Attributes=(int)x.Attributes,CustomAttributes=Attributes(r,names,x.GetCustomAttributes()),DefaultValue=Constant(r,x.GetDefaultValue()),MarshallingDescriptor=Blob(r,x.GetMarshallingDescriptor())};}).ToArray(),Import=new{Attributes=(int)import.Attributes,Name=import.Name.IsNil?null:r.GetString(import.Name),Module=names.Entity(import.Module)},Body=body });
        }
        var allAttributes=r.CustomAttributes.Select(h=>{var x=r.GetCustomAttribute(h);return new{Parent=owners.TryGetValue(x.Parent,out var o)?o:names.Entity(x.Parent),ParentToken=MetadataTokens.GetToken(x.Parent),Constructor=names.Entity(x.Constructor),Blob=Blob(r,x.Value)};}).ToArray();
        var security=r.DeclarativeSecurityAttributes.Select(h=>{var x=r.GetDeclarativeSecurityAttribute(h);return new{Parent=owners.TryGetValue(x.Parent,out var o)?o:names.Entity(x.Parent),Action=(int)x.Action,PermissionSet=Blob(r,x.PermissionSet)};}).ToArray();
        var assembly=r.GetAssemblyDefinition();var module=r.GetModuleDefinition();
        Console.WriteLine(JsonSerializer.Serialize(new{AssemblySHA256=Hash(File.ReadAllBytes(args[0])),OfflinePEReader=true,TargetAssemblyLoaded=false,Assembly=new{Name=r.GetString(assembly.Name),Version=assembly.Version.ToString(),Flags=(int)assembly.Flags,HashAlgorithm=assembly.HashAlgorithm.ToString(),PublicKey=Blob(r,assembly.PublicKey),CustomAttributes=Attributes(r,names,assembly.GetCustomAttributes())},Module=new{Name=r.GetString(module.Name),Mvid=r.GetGuid(module.Mvid),CustomAttributes=Attributes(r,names,module.GetCustomAttributes())},Types=types,Fields=fields,Methods=methods,Properties=properties,Events=events,AllCustomAttributes=allAttributes,DeclarativeSecurityAttributes=security},new JsonSerializerOptions{WriteIndented=true}));
    }
}
