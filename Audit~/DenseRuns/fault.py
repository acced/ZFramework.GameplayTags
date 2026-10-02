#!/usr/bin/env python3
"""Separate fault-injection executables; never substitute instrumented timings."""
import pathlib,json,sys,subprocess,hashlib,os
from xml.sax.saxutils import escape
ROOT=pathlib.Path(__file__).resolve().parents[2]
out=pathlib.Path(sys.argv[1]).resolve();m=json.loads((out/'manifest.json').read_text());work=out/'fault';work.mkdir(exist_ok=True)
(work/'global.json').write_text(json.dumps({'sdk':{'version':m['sdk'],'rollForward':'disable'}}))
(work/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
records={}
for name in ('monotone','stream','rotate','buffer32'):
    folder=work/name;folder.mkdir(exist_ok=True)
    original=out/'generated'/name/'DirectArraySet.cs';code=original.read_text()
    anchor='            if (needed <= EntryCapacity) return;'
    assert code.count(anchor)==1
    code=code.replace(anchor,anchor+'\n            if (FailGrowth >= 0 && FailGrowth-- == 0) throw new OutOfMemoryException("injected growth failure");')
    code=code.replace('        private Entry[] entries;','        internal static int FailGrowth = -1;\n        private Entry[] entries;')
    core=folder/'DirectArraySet.cs';core.write_text(code)
    src=list((ROOT/'Runtime').rglob('*.cs'))+[ROOT/'Audit~/UnityStubs.cs',ROOT/'Audit~/Fusion/DenseFusion.cs',ROOT/'Audit~/MicroBitmapSet.cs',core,out/'generated'/name/'BulkBuilders.cs',ROOT/'Audit~/DenseRuns/FaultProbe.cs']
    project=folder/'Fault.csproj'
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><EnableDefaultCompileItems>false</EnableDefaultCompileItems><LangVersion>9.0</LangVersion><AllowUnsafeBlocks>true</AllowUnsafeBlocks><Optimize>true</Optimize><DefineConstants>BULK_MICRO</DefineConstants></PropertyGroup><ItemGroup>'+''.join('<Compile Include="'+escape(str(p))+'"/>' for p in src)+'</ItemGroup></Project>')
    p=subprocess.run(['dotnet','build',str(project),'-c','Release','-o',str(folder/'bin')],cwd=work,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True);(folder/'build.log').write_text(p.stdout)
    if p.returncode:raise RuntimeError(p.stdout)
    p=subprocess.run(['dotnet',str(folder/'bin/Fault.dll'),name,str(folder/'result.json')],cwd=work,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True);(folder/'run.log').write_text(p.stdout)
    if p.returncode:raise RuntimeError(p.stdout)
    records[name]=dict(core_sha256=hashlib.sha256(original.read_bytes()).hexdigest(),**json.loads((folder/'result.json').read_text()))
print('FAULT_RESULTS '+json.dumps(records));(out/'FaultResults.json').write_text(json.dumps(records,indent=2))
