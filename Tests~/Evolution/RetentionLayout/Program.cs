using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using GameplayTags;

// Original Program.cs is compiled unchanged: invoke its exact Register/Inputs helpers.
internal static class RetentionLayout
{
    sealed class Identity : IEqualityComparer<object> {
        public new bool Equals(object a,object b)=>ReferenceEquals(a,b);
        public int GetHashCode(object x)=>RuntimeHelpers.GetHashCode(x);
    }
    static string Q(string s)=>"\""+s.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    static FieldInfo[] Fields(Type t) {
        var fields=new List<FieldInfo>();
        for(var p=t;p!=null;p=p.BaseType) fields.AddRange(p.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly));
        return fields.OrderBy(f=>f.DeclaringType.FullName+"."+f.Name,StringComparer.Ordinal).ToArray();
    }
    static string Name(Type t)=>t.FullName;
    static void Layout(Type t, SortedDictionary<string,string> layouts) {
        if(layouts.ContainsKey(Name(t)))return;
        layouts[Name(t)]=t.IsArray?"array:"+Name(t.GetElementType()):"kind:"+(t.IsValueType?"value":"reference")+";layout:"+t.Attributes+";pack:"+(t.StructLayoutAttribute==null?0:t.StructLayoutAttribute.Pack)+";size:"+(t.StructLayoutAttribute==null?0:t.StructLayoutAttribute.Size)+";fields:"+string.Join(";",Fields(t).Select(f=>Name(f.DeclaringType)+"."+f.Name+":"+Name(f.FieldType)));
        if(t.IsArray) Layout(t.GetElementType(),layouts);
        else if(!t.IsPrimitive && !t.IsEnum) foreach(var f in Fields(t)) Layout(f.FieldType,layouts);
    }
    static string Audit(string id,object root) {
        var seen=new Dictionary<object,int>(new Identity());var nodes=new List<string>();var layouts=new SortedDictionary<string,string>(StringComparer.Ordinal);
        Action<object,string> visit=null;
        visit=(value,path)=>{
            if(value==null)return;
            Type t=value.GetType();Layout(t,layouts);
            if(t.IsPrimitive||t.IsEnum)return;
            if(t.IsValueType){foreach(var f in Fields(t)) if(!f.FieldType.IsPrimitive&&!f.FieldType.IsEnum)visit(f.GetValue(value),path+"."+f.Name);return;}
            int index;
            if(seen.TryGetValue(value,out index)){nodes.Add(path+"=ref:"+index);return;}
            index=seen.Count;seen.Add(value,index);
            string shape=path+"=node:"+index+":"+Name(t);
            var array=value as Array;
            if(array!=null)shape+=";lengths:"+string.Join(",",Enumerable.Range(0,array.Rank).Select(array.GetLength))+";lower:"+string.Join(",",Enumerable.Range(0,array.Rank).Select(array.GetLowerBound));
            if(value is string)shape+=";chars:"+((string)value).Length;
            nodes.Add(shape);
            if(array!=null){Type e=t.GetElementType();if(!e.IsPrimitive&&!e.IsEnum){int i=0;foreach(var v in array)visit(v,path+"["+(i++)+"]");}}
            else if(!(value is string))foreach(var f in Fields(t))if(!f.FieldType.IsPrimitive&&!f.FieldType.IsEnum)visit(f.GetValue(value),path+"."+f.DeclaringType.FullName+"."+f.Name);
        };
        visit(root,"root");
        return "{\"Id\":"+Q(id)+",\"OwnedObjectCount\":"+seen.Count+",\"Nodes\":["+string.Join(",",nodes.Select(Q))+"],\"Layouts\":{"+string.Join(",",layouts.Select(k=>Q(k.Key)+":"+Q(k.Value)))+"}}";
    }
    public static void Main(string[] args) {
        var flags=BindingFlags.Static|BindingFlags.NonPublic;
        typeof(Program).GetMethod("Register",flags).Invoke(null,new object[]{0});
        var rows=new List<string>();
        foreach(int n in new[]{4,16,128,1024})foreach(int overlap in new[]{0,50,100}) {
            object[] input={n,overlap,null,null};typeof(Program).GetMethod("Inputs",flags).Invoke(null,input);
            var a=(GameplayTagContainer)input[2];var b=(GameplayTagContainer)input[3];
            var expected=(HashSet<string>)typeof(Program).GetMethod("Names",flags).Invoke(null,new object[]{a});expected.IntersectWith((HashSet<string>)typeof(Program).GetMethod("Names",flags).Invoke(null,new object[]{b}));
            string id="n"+n+"/overlap"+overlap;
            var result=GameplayTagContainer.Intersection(a,b);
            typeof(Program).GetMethod("Validate",flags).Invoke(null,new object[]{result,expected});
            rows.Add(Audit(id+"/retained-result",result));
            var w=new GameplayTagIntersectionWorkspace();var r=new GameplayTagContainer();GameplayTagContainer.Intersection(r,a,b,w);
            typeof(Program).GetMethod("Validate",flags).Invoke(null,new object[]{r,expected});
            rows.Add(Audit(id+"/retained-result-workspace",new object[]{r,w}));
        }
        string output="{\"Schema\":1,\"Runtime\":"+Q(Environment.Version.ToString())+",\"Backend\":"+Q(Type.GetType("Mono.Runtime")==null?".NET":"Standalone Mono")+",\"Rows\":["+string.Join(",",rows)+"]}";
        int pos=Array.IndexOf(args,"--out");if(pos<0)throw new ArgumentException("--out required");System.IO.File.WriteAllText(args[pos+1],output);
        Console.WriteLine("PASS: 24 exact retention fixtures, semantic validation and reference-deduplicated owned graphs. No byte-size inference.");
    }
}
