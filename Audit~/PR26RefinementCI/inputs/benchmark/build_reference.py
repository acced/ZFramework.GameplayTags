#!/usr/bin/env python3
"""Build a matched baseline library or bind an already-admitted Runtime DLL, then a public-call host."""
from pathlib import Path
import argparse,hashlib,json,os,subprocess,xml.sax.saxutils
ROOT=Path(__file__).resolve().parent
SDK=ROOT.parent.parent/'comparison-materials/dotnet-sdk/dotnet'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def esc(p):return xml.sax.saxutils.escape(str(p))
def main():
 p=argparse.ArgumentParser();p.add_argument('--name',required=True);p.add_argument('--runtime-source',required=True);p.add_argument('--facade-dll',required=True);p.add_argument('--runtime-dll');p.add_argument('--harness',default='Probe.Reference.cs');args=p.parse_args()
 source=Path(args.runtime_source).resolve();facade=Path(args.facade_dll).resolve();root=ROOT/'builds'/args.name;root.mkdir(parents=True,exist_ok=True)
 files=sorted(source.glob('*.cs'));before={str(f):sha(f) for f in files};assert files and facade.exists()
 env=dict(os.environ,DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',DOTNET_GENERATE_ASPNET_CERTIFICATE='false',DOTNET_CLI_HOME=str(ROOT/'home'))
 def build(project,output,label):
  result=subprocess.run([str(SDK),'build',str(project),'-c','Release','-o',str(output),'--configfile',str(ROOT/'NuGet.Config'),'-p:UseSharedCompilation=false'],env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
  (root/(label+'.log')).write_text(result.stdout);print(label,result.returncode,result.stdout[-1800:],flush=True)
  if result.returncode:raise SystemExit(result.returncode)
 reference='<Reference Include="'+facade.stem+'"><HintPath>'+esc(facade)+'</HintPath></Reference>'
 if args.runtime_dll:
  runtime=Path(args.runtime_dll).resolve();admitted=runtime.parents[2]/'build-binding.json';binding=json.loads(admitted.read_text());assert source==(Path(binding['source'])/'Runtime').resolve(),'Source root differs from compiler binding';assert sha(runtime)==binding['runtime_sha256'];assert {str(f) for f in files}==set(binding['runtime_sources']),'Source list differs from compiler binding';admitted_sources=runtime.parents[2]/'sources.after.json';inventory=json.loads(admitted_sources.read_text());assert all(before[str(f)]==inventory[str(f)] for f in files),'Current source differs from admitted compiler inventory'
 else:
  lib=root/'library';lib.mkdir(exist_ok=True)
  project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><AssemblyName>GameplayTags</AssemblyName><EnableDefaultCompileItems>false</EnableDefaultCompileItems><AllowUnsafeBlocks>true</AllowUnsafeBlocks><Optimize>true</Optimize><Deterministic>true</Deterministic><CheckForOverflowUnderflow>false</CheckForOverflowUnderflow><LangVersion>latest</LangVersion></PropertyGroup><ItemGroup>'+''.join('<Compile Include="'+esc(f)+'"/>' for f in files)+reference+'</ItemGroup></Project>'
  (lib/'GameplayTags.csproj').write_text(project);build(lib/'GameplayTags.csproj',lib/'bin','runtime-build');runtime=lib/'bin/GameplayTags.dll'
 harness=ROOT/args.harness;project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><AssemblyName>GameplayTags.Tests</AssemblyName><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><Deterministic>true</Deterministic><CheckForOverflowUnderflow>false</CheckForOverflowUnderflow><LangVersion>latest</LangVersion></PropertyGroup><ItemGroup><Compile Include="'+esc(harness)+'"/>'+reference+'<Reference Include="GameplayTags"><HintPath>'+esc(runtime)+'</HintPath></Reference></ItemGroup></Project>'
 (root/'Probe.csproj').write_text(project);build(root/'Probe.csproj',root/'bin','host-build')
 assert before=={str(f):sha(f) for f in files},'Runtime source changed during host build'
 deps={str(root/'bin'/runtime.name):sha(runtime),str(root/'bin'/facade.name):sha(facade)}
 assert all(sha(Path(f))==h for f,h in deps.items()),'Copied dependency differs'
 identity=dict(variant=args.name,snapshot=str(source),harness=str(harness),source_sha256=dict(before,**{str(harness):sha(harness)}),project_sha256=sha(root/'Probe.csproj'),dll=str(root/'bin/GameplayTags.Tests.dll'),dll_sha256=sha(root/'bin/GameplayTags.Tests.dll'),runtime_dll=str(runtime),runtime_dll_sha256=sha(runtime),facade_dll=str(facade),facade_dll_sha256=sha(facade),dependency_sha256=deps,sdk=str(SDK),cross_assembly_calls=True,host_friend_access='GameplayTags.Tests for fixture creation only; every timed API call is public')
 if args.runtime_dll:
  identity['admitted_build_binding']={'path':str(admitted),'sha256':sha(admitted)};identity['admitted_source_manifest']={'path':str(admitted_sources),'sha256':sha(admitted_sources)}
 (root/'identity.json').write_text(json.dumps(identity,indent=2))
if __name__=='__main__':main()
