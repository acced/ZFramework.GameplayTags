#!/usr/bin/env python3
"""Compile Runtime/Editor as distinct actual libraries; hosts contain tests only."""
import argparse,hashlib,json,os,pathlib,re,subprocess,xml.sax.saxutils
root=pathlib.Path(__file__).resolve().parent;sdk=root.parents[1]/'comparison-materials/dotnet-sdk/dotnet';baseline=root.parent/'baseline/source'
ap=argparse.ArgumentParser();ap.add_argument('--source',required=True);ap.add_argument('--label',required=True);ap.add_argument('--framework',choices=['net8.0','netstandard2.1'],required=True);ap.add_argument('--checked',action='store_true');ap.add_argument('--suites',default='independent,full,bitcount,original,interop,bridge,bridge-mutation');ap.add_argument('--bridge',default=str(root.parent/'runtime-integration/BridgeTests.cs'));ap.add_argument('--facade-library');args=ap.parse_args()
source=pathlib.Path(args.source).resolve();out=root/'integration-results'/args.label
if out.exists() and any(out.iterdir()):raise RuntimeError('Refusing to overwrite nonempty result label')
out.mkdir(parents=True,exist_ok=True)
env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_ReadyToRun='0',DOTNET_NOLOGO='1',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_CLI_HOME=str(root/'home'))
compiled={};summary=[];framework='.NETCoreApp,Version=v8.0' if args.framework=='net8.0' else '.NETStandard,Version=v2.1'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def build(folder,assembly,target,files,refs=(),exe=False,unsafe=False):
    d=out/folder;d.mkdir(parents=True,exist_ok=True);before={str(p):sha(p) for p in files};compiled.update(before)
    items=''.join('<Compile Include="'+xml.sax.saxutils.escape(str(p))+'"/>' for p in files)+''.join('<Reference Include="'+p.stem+'"><HintPath>'+xml.sax.saxutils.escape(str(p))+'</HintPath></Reference>' for p in refs)
    project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>'+target+'</TargetFramework><OutputType>'+('Exe' if exe else 'Library')+'</OutputType><AssemblyName>'+assembly+'</AssemblyName><EnableDefaultCompileItems>false</EnableDefaultCompileItems><LangVersion>9.0</LangVersion><AllowUnsafeBlocks>'+str(unsafe).lower()+'</AllowUnsafeBlocks><Optimize>true</Optimize><Deterministic>true</Deterministic><CheckForOverflowUnderflow>'+str(args.checked).lower()+'</CheckForOverflowUnderflow>'+('<StartupObject>IntegrationEntry</StartupObject>' if exe else '')+'</PropertyGroup><ItemGroup>'+items+'</ItemGroup></Project>'
    (d/'Probe.csproj').write_text(project);(d/'sources.before.json').write_text(json.dumps(before,indent=2))
    run=subprocess.run([str(sdk),'build',str(d/'Probe.csproj'),'-c','Release','-o',str(d/'bin'),'--configfile',str(root/'NuGet.Config'),'--disable-build-servers'],env=env,capture_output=True,text=True)
    (d/'build.log').write_text(run.stdout+run.stderr)
    if run.returncode:print(run.stdout+run.stderr);raise SystemExit(run.returncode)
    assert before=={str(p):sha(p) for p in files},'Source changed during compilation'
    return d/'bin'/(assembly+'.dll')
if args.facade_library:
    facade=pathlib.Path(args.facade_library).resolve();assert facade.is_file()
else:
    facadeSource=out/'UnityApiFacade.cs';facadeSource.write_bytes((baseline/'Audit~/UnityStubs.cs').read_bytes())
    facade=build('facade','UnityApiFacade',args.framework,[facadeSource])
runtimeFiles=sorted((source/'Runtime').glob('*.cs'));assert runtimeFiles and all(p.parent==source/'Runtime' for p in runtimeFiles)
runtime=build('runtime','GameplayTags',args.framework,runtimeFiles,[facade],unsafe=args.framework=='net8.0')
editorFiles=sorted((source/'Editor').glob('*.cs'));editor=build('editor','GameplayTags.Editor',args.framework,editorFiles,[facade,runtime])
sampleFiles=sorted((source/'Samples~').rglob('*.cs'))
if sampleFiles:build('samples','GameplayTags.Samples',args.framework,sampleFiles,[facade,runtime])
nativeFiles=sorted((source/'Tests/Runtime').glob('*.cs'))
if nativeFiles:
    nativeFacade=build('native-test-facade','NativeTestFacade','net8.0',[baseline/'Audit~/NativeTestStubs.cs'],[facade])
    build('native-compile-only','GameplayTags.Runtime.Tests','net8.0',nativeFiles,[facade,runtime,editor,nativeFacade])
documentation=source/'Documentation~/DIRECT_ARRAY_RUNTIME.md'
if documentation.exists():
    blocks=re.findall(r'```csharp\n(.*?)```',documentation.read_text(),re.S);assert blocks,'Missing documented public consumer'
    snippets=[]
    for i,text in enumerate(blocks):
        p=out/('PublicConsumer'+str(i)+'.cs');p.write_text(text);snippets.append(p)
    build('nonfriend-public-consumer','PublicGameplayTagsConsumer',args.framework,snippets,[facade,runtime])
refs=[facade,runtime,editor];bindings={str(p):sha(p) for p in refs}
for suite in args.suites.split(','):
    d=out/suite;d.mkdir(exist_ok=True)
    if suite=='full':text=(baseline/'Audit~/MicroTests.cs').read_text().replace('MicroBitmapSet','DirectArraySet');entry='MicroTests'
    elif suite=='original':text=(baseline/'Audit~/IntegerTests.cs').read_text();entry='IntegerTests'
    elif suite=='bridge':text=pathlib.Path(args.bridge).read_text();entry='BridgeTests'
    elif suite=='bridge-mutation':text=(root/'BridgeMutationTests.cs').read_text();entry='BridgeMutationTests'
    else:
        name={'independent':'Independent','bitcount':'CountBits','interop':'IntegrationInterop'}[suite];text=(root/(name+'.cs')).read_text();entry=name
    fixture=d/(entry+'.cs');fixture.write_text(text);files=[fixture]
    if suite=='original':
        support=d/'IntegerTestSupport.cs';support.write_bytes((baseline/'Audit~/IntegerTestSupport.cs').read_bytes());files.append(support)
    wrapper=d/'IntegrationEntry.cs';wrapper.write_text('''using System;using System.IO;using System.Linq;using System.Reflection;using System.Security.Cryptography;using GameplayTags;using GameplayTags.Experiments;
internal static class IntegrationEntry { static int Main(string[] args) { try {
var runtime=typeof(RuntimeTagSet).Assembly;if(runtime!=typeof(DirectArraySet).Assembly||runtime.GetName().Name!="GameplayTags")throw new Exception("Selected and original types must share actual Runtime assembly");
if(runtime.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>().FrameworkName!="'''+framework+'''")throw new Exception("Wrong runtime target framework");
if(runtime.GetTypes().Any(t=>t.Namespace!=null&&(t.Namespace.StartsWith("UnityEngine")||t.Namespace.StartsWith("UnityEditor"))))throw new Exception("Facade types leaked into Runtime");
using(var h=SHA256.Create()){var actual=BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(runtime.Location))).Replace("-","").ToLowerInvariant();if(actual!=Environment.GetEnvironmentVariable("EXPECTED_RUNTIME_SHA256"))throw new Exception("Loaded Runtime bytes differ");}
var method=Assembly.GetExecutingAssembly().GetType("'''+entry+'''").GetMethod("Main",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);var value=method.Invoke(null,new object[]{args});return value is int code?code:0;
}catch(Exception e){Console.WriteLine(e);return 1;} } }
''');files.append(wrapper)
    host=build(suite,'GameplayTags.Tests','net8.0',files,refs,exe=True)
    deployed={}
    for reference in refs:
        p=host.parent/reference.name;assert p.is_file() and sha(p)==bindings[str(reference)] and sha(reference)==bindings[str(reference)],'Intended/deployed reference mismatch';deployed[str(p)]=sha(p)
    (d/'library-binding.json').write_text(json.dumps(dict(intended=bindings,deployed=deployed,loaded_runtime_check=True,framework=framework),indent=2))
    for nohw in (False,True):
        mode='nohw' if nohw else 'default';work=d/mode;work.mkdir();dest=work/'original' if suite=='original' else work/'result.json'
        runenv=dict(env,EXPECTED_RUNTIME_SHA256=bindings[str(runtime)],**({'DOTNET_EnableHWIntrinsic':'0'} if nohw else {}))
        assert all(sha(pathlib.Path(p))==h for p,h in bindings.items()) and all(sha(pathlib.Path(p))==h for p,h in deployed.items())
        process=subprocess.run([str(sdk),str(host),str(dest)],cwd=work,env=runenv,capture_output=True,text=True);(work/'run.log').write_text(process.stdout+process.stderr)
        row=dict(suite=suite,mode=mode,exit=process.returncode,host_sha256=sha(host),runtime_sha256=sha(runtime));summary.append(row);print(json.dumps(row),flush=True)
        (out/'summary.json').write_text(json.dumps(summary,indent=2))
after={p:sha(pathlib.Path(p)) for p in compiled};assert compiled==after,'Source changed after execution';(out/'sources.after.json').write_text(json.dumps(after,indent=2))
(out/'build-binding.json').write_text(json.dumps(dict(source=str(source),framework=args.framework,checked=args.checked,experimental_defines=[],facade_assembly_separate=True,runtime_sources=[str(p) for p in runtimeFiles],runtime_sha256=sha(runtime),references=bindings),indent=2))
raise SystemExit(1 if any(x['exit'] for x in summary) else 0)
