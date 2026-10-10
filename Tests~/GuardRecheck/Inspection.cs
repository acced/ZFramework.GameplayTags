using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
internal static class Inspection {
 static readonly Dictionary<short,OpCode> Codes=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)).ToDictionary(o=>o.Value);
 static readonly HashSet<string> Seen=new HashSet<string>();
 static string Hash(byte[] b){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(b)).Replace("-","").ToLowerInvariant();}
 static string Member(MemberInfo m)=>m is Type?((Type)m).FullName:m.DeclaringType.FullName+"::"+m.ToString();
 static string Normal(MethodInfo method,byte[] bytes){
  var text=new StringBuilder();int i=0;while(i<bytes.Length){int offset=i;short v=bytes[i++];if(v==254)v=(short)(0xfe00|bytes[i++]);OpCode op=Codes[v];text.Append(offset).Append(':').Append(op.Name).Append(' ');int n=0;
   switch(op.OperandType){
    case OperandType.InlineNone:break;
    case OperandType.ShortInlineBrTarget:case OperandType.ShortInlineI:case OperandType.ShortInlineVar:n=1;break;
    case OperandType.InlineVar:n=2;break;
    case OperandType.InlineI8:case OperandType.InlineR:n=8;break;
    case OperandType.InlineSwitch:n=4+4*BitConverter.ToInt32(bytes,i);break;
    case OperandType.InlineString:text.Append(method.Module.ResolveString(BitConverter.ToInt32(bytes,i)));i+=4;break;
    case OperandType.InlineField:case OperandType.InlineMethod:case OperandType.InlineType:case OperandType.InlineTok:
     text.Append(Member(method.Module.ResolveMember(BitConverter.ToInt32(bytes,i),method.DeclaringType.GetGenericArguments(),method.GetGenericArguments())));i+=4;break;
    case OperandType.InlineSig:text.Append(BitConverter.ToString(method.Module.ResolveSignature(BitConverter.ToInt32(bytes,i))));i+=4;break;
    default:n=4;break;
   }
   if(n!=0){text.Append(BitConverter.ToString(bytes,i,n));i+=n;}text.AppendLine();
  }
  return text.ToString().Replace(method.Module.Assembly.FullName,"<tested-assembly>");
 }
 public static void Write(Action action,string path){
  Assembly assembly=action.Method.DeclaringType.Assembly;
  var methods=new List<MethodInfo>{action.Method};
  foreach(string name in new[]{"GameplayTags.GameplayTagContainer","GameplayTags.TagStorage","Program"}){
   Type t=assembly.GetType(name,true);methods.AddRange(t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly));
  }
  using(var output=new StreamWriter(path,true))foreach(var method in methods){
   string identity=assembly.FullName+"/"+Member(method);if(!Seen.Add(identity))continue;var body=method.GetMethodBody();if(body==null)continue;byte[] bytes=body.GetILAsByteArray();string normalized=Normal(method,bytes);
   output.WriteLine("METHOD "+identity);output.WriteLine("ASSEMBLY "+assembly.Location);output.WriteLine("MVID "+method.Module.ModuleVersionId);output.WriteLine("RAW_SHA256 "+Hash(bytes));output.WriteLine("NORMALIZED_SHA256 "+Hash(Encoding.UTF8.GetBytes(normalized)));output.WriteLine("FLAGS "+method.GetMethodImplementationFlags()+" MAXSTACK "+body.MaxStackSize+" INITLOCALS "+body.InitLocals);output.WriteLine("LOCALS "+string.Join(";",body.LocalVariables.Select(v=>v.LocalType.FullName+":"+v.IsPinned)));output.WriteLine(normalized);
  }
 }
}
