#!/usr/bin/env python3
"""Real managed compilation/tests. Native Unity/IL2CPP acceptance remains separate."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
from xml.sax.saxutils import escape

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
sys.path.insert(0, str(ROOT / 'Audit~'))
from checks import verify_package, compile_documentation, compile_split_assemblies

def project(path, includes, framework='net8.0', executable=True, references=(), name=None, defines=''):
    path.parent.mkdir(parents=True, exist_ok=True)
    items=''.join('<Compile Include="'+escape(str(p))+'"/>' for p in includes)
    items+=''.join('<Reference Include="'+escape(p.stem)+'"><HintPath>'+escape(str(p))+'</HintPath></Reference>' for p in references)
    path.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>'+framework+'</TargetFramework>'
        '<OutputType>'+('Exe' if executable else 'Library')+'</OutputType><LangVersion>9.0</LangVersion>'
        '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>disable</ImplicitUsings>'
        '<Nullable>disable</Nullable><Optimize>true</Optimize><AllowUnsafeBlocks>false</AllowUnsafeBlocks>'
        '<AssemblyName>'+(name or path.stem)+'</AssemblyName><DefineConstants>'+defines+'</DefineConstants>'
        '</PropertyGroup><ItemGroup>'+items+'</ItemGroup></Project>')

def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--dotnet',default='dotnet');ap.add_argument('--output',type=Path,required=True)
    args=ap.parse_args();out=args.output.resolve();out.mkdir(parents=True,exist_ok=True)
    env=dict(os.environ,DOTNET_gcConcurrent='0',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1',DOTNET_TieredCompilation='0')
    results=[]
    def run(cmd,log,cwd=ROOT,extra=None):
        result=subprocess.run(list(map(str,cmd)),cwd=cwd,env=dict(env,**(extra or {})),text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
        (out/log).write_text(result.stdout);print(result.stdout,flush=True)
        if result.returncode: raise RuntimeError(log+' failed: '+str(result.returncode))
    def build(folder,includes,framework='net8.0',executable=True,references=(),name=None,defines=''):
        path=out/folder;path.mkdir(parents=True,exist_ok=True)
        shutil.copy2(ROOT/'Audit~/NuGet.Config',path/'NuGet.Config')
        proj=path/(folder+'.csproj');project(proj,includes,framework,executable,references,name,defines)
        run([args.dotnet,'build',proj,'-c','Release','-o',path/'bin'],folder+'-build.log',path)
        return path/'bin'/((name or folder)+'.dll')
    def execute(dll,label,arguments=(),extra=None):
        run([args.dotnet,'exec',dll,*arguments],label+'.log',out,extra)
        results.append({'name':label,'passed':True})
    package=verify_package(ROOT);(out/'package-checks.json').write_text(json.dumps(package,indent=2))
    runtime=[ROOT/'Runtime/**/*.cs'];facade=ROOT/'Audit~/UnityStubs.cs'
    integer=build('IntegerTests',runtime+[ROOT/'Editor/**/*.cs',ROOT/'Samples~/**/*.cs',facade,ROOT/'Audit~/IntegerTests.cs',ROOT/'Audit~/IntegerTestSupport.cs'])
    execute(integer,'integer-hardware',[out/'integer-hardware']);execute(integer,'integer-nohw',[out/'integer-nohw'],{'DOTNET_EnableHWIntrinsic':'0'})
    for label,source in [('RefactorTests',HERE/'RuntimeRefactorTests.cs'),('DenseTests',HERE/'DenseKernelTests.cs'),('PackedTests',HERE/'PackedKernelTests.cs')]:
        dll=build(label,runtime+[facade,source],name='GameplayTags.Tests')
        execute(dll,label+'-hardware',[out/(label+'-hardware'+('.json' if label.startswith(('Dense','Packed')) else ''))]);execute(dll,label+'-nohw',[out/(label+'-nohw'+('.json' if label.startswith(('Dense','Packed')) else ''))],{'DOTNET_EnableHWIntrinsic':'0'})
    # Compile the shipped fallback as an actual Standard 2.1 library, then execute
    # independent public-API and kernel tests against that binary from a net8 host.
    portable=build('RuntimePortable',runtime+[facade],framework='netstandard2.1',executable=False,name='GameplayTags')
    for label,source in [('RefactorPortableTests',HERE/'RuntimeRefactorTests.cs'),('DensePortableTests',HERE/'DenseKernelTests.cs'),('PackedPortableTests',HERE/'PackedKernelTests.cs')]:
        dll=build(label,[source],references=[portable],name='GameplayTags.Tests',defines='GAMEPLAYTAGS_EXPECT_PORTABLE_RUNTIME')
        execute(dll,label,[out/(label+('.json' if label.startswith(('Dense','Packed')) else ''))])
    forced=build('DenseForcedTests',runtime+[facade,HERE/'DenseKernelTests.cs'],name='GameplayTags.Tests',defines='GAMEPLAYTAGS_FORCE_PORTABLE')
    execute(forced,'dense-forced-portable')
    forced_packed=build('PackedForcedTests',runtime+[facade,HERE/'PackedKernelTests.cs'],name='GameplayTags.Tests',defines='GAMEPLAYTAGS_FORCE_PORTABLE')
    execute(forced_packed,'packed-forced-portable',[out/'packed-forced-portable.json'])
    host=out/'assembly-checks';host.mkdir(exist_ok=True)
    for name in ('UnityStubs.cs','NativeTestStubs.cs','NuGet.Config'):shutil.copy2(ROOT/'Audit~'/name,host/name)
    docs=compile_documentation(ROOT,host,integer,args.dotnet,run)
    assemblies=compile_split_assemblies(ROOT,host,args.dotnet,run)
    summary={'measurement_gc_concurrent':'0','package':package,'executed_managed_suites':results,'documentation':docs,'assemblies':assemblies,
             'native_unity':'not_executed','native_il2cpp':'not_executed','release_approved':False}
    (out/'verification.json').write_text(json.dumps(summary,indent=2)+'\n')
    print('VERIFICATION PASS: managed only; native release gates remain open',flush=True)

if __name__=='__main__':main()
