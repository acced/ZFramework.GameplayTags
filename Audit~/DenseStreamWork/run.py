#!/usr/bin/env python3
"""Untimed structural-work diagnostics. Instrumented binaries never enter timings."""
from pathlib import Path
import sys,os,subprocess,json,hashlib,importlib.util,difflib
from xml.sax.saxutils import escape
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('stream_work_source',ROOT/'Audit~/DenseStream/generate.py')
g=importlib.util.module_from_spec(spec);spec.loader.exec_module(g)
out=Path(sys.argv[1]).resolve();out.mkdir(parents=True,exist_ok=True)
work=out/'build';work.mkdir(exist_ok=True)
(work/'global.json').write_text('{"sdk":{"version":"8.0.425","rollForward":"disable"}}')
(work/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_ReadyToRun='0',DOTNET_NOLOGO='1')
assert subprocess.check_output(['dotnet','--version'],cwd=work,env=env,text=True).strip()=='8.0.425'
files=g.generate(out/'generated')
common=list((ROOT/'Runtime').rglob('*.cs'))+[ROOT/'Audit~/UnityStubs.cs',ROOT/'Audit~/Fusion/DenseFusion.cs',ROOT/'Audit~/MicroBitmapSet.cs']
rows=[];digests={}
for name,src in files.items():
    original=src[0].read_text();digests[name]=hashlib.sha256(original.encode()).hexdigest();code=original
    anchor='public TagRegistry Registry';assert code.count(anchor)==1
    code=code.replace(anchor,'public static long WorkWordReads, WorkEmits, WorkWrites, WorkFinds, WorkSuffixMoves, WorkGrowthCopies;\n        '+anchor,1)
    for signature in ('private struct DenseRecordCursor','private struct DenseReverseCursor'):
        if signature not in code:continue
        old=g.block(code,signature)
        new=old.replace('pending = words[word++];','pending = words[word++]; WorkWordReads++;').replace('pending = words[word--];','pending = words[word--]; WorkWordReads++;').replace('return true;','WorkEmits++; return true;')
        assert new!=old;code=code.replace(old,new,1)
    for signature,statement in (('private int Find(','WorkFinds++;'),('private void Write(','WorkWrites++;')):
        old=g.block(code,signature);new=old.replace('{','{ '+statement,1);code=code.replace(old,new,1)
    old=g.block(code,'private void ReserveEntries(')
    new=old.replace('Array.Copy(entries, next, used);','{ WorkGrowthCopies += used; Array.Copy(entries, next, used); }').replace('next[0] = inline;','{ WorkGrowthCopies++; next[0] = inline; }')
    assert new!=old;code=code.replace(old,new,1)
    old=g.block(code,'private void AppendDenseToMicro(')
    new=old.replace('if (at < used) Array.Copy(entries, at, entries, at + 1, used - at);','if (at < used) { WorkSuffixMoves += used - at; Array.Copy(entries, at, entries, at + 1, used - at); }')
    code=code.replace(old,new,1)
    folder=work/name;folder.mkdir(exist_ok=True)
    patched=folder/'Instrumented.cs';patched.write_text(code)
    (folder/'instrumentation.patch').write_text(''.join(difflib.unified_diff(original.splitlines(True),code.splitlines(True),fromfile='measured-source-unmodified',tofile='untimed-counter-build')))
    sources=common+[patched,src[1],ROOT/'Audit~/DenseStreamWork/Probe.cs']
    project=folder/'Work.csproj'
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><LangVersion>9.0</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><AllowUnsafeBlocks>true</AllowUnsafeBlocks><Optimize>true</Optimize><DefineConstants>BULK_MICRO</DefineConstants></PropertyGroup><ItemGroup>'+''.join('<Compile Include="'+escape(str(p))+'"/>' for p in sources)+'</ItemGroup></Project>')
    p=subprocess.run(['dotnet','build',str(project),'-c','Release','-o',str(folder/'bin')],cwd=work,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
    (folder/'build.log').write_text(p.stdout)
    if p.returncode:raise RuntimeError(p.stdout[-10000:])
    p=subprocess.run(['dotnet',str(folder/'bin/Work.dll'),name,str(out/(name+'.json'))],cwd=work,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
    (folder/'run.log').write_text(p.stdout)
    if p.returncode:raise RuntimeError(p.stdout[-10000:])
    rows.extend(json.loads((out/(name+'.json')).read_text()))
manifest=dict(head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),original_core_hashes=digests,instrumented=True,timed=False,production_fields_added=False,rows=rows)
(out/'WORK_COUNTS.json').write_text(json.dumps(manifest,indent=2))
lines=['# 不计时的算法工作量诊断','', '四个源算法与PR20主计时构建逐字节相同（各原源码哈希随JSON保存），随后在单独二进制中加计数器。不是CPU/GC性能测量，不得把此二进制或计数当成主基准结果。计数从独立副本已经建立后开始，仅覆盖AppendTags，不覆盖克隆。','',
'WordReads仅私有Dense游标加载的64位字；Emits含每遍源记录发出；Writes是Write调用，SuffixMoves是单次插入Array.Copy搬移的记录总量，GrowthCopies是扩容复制记录总量；Finds是Find调用。它们不是周期数/缓存未命中数，也不涵盖所有机器指令。','',
'|算法|形态|左/右成员|Dense读字|发出记录|Write|Find|后缀搬移|扩容复制|','|---|---|---|---:|---:|---:|---:|---:|---:|']
for r in rows:
    lines.append('|'+ '|'.join(str(r[k]) for k in ('variant','shape','sizes','wordReads','emits','writes','finds','suffixMoves','growthCopies'))+'|')
(out/'工作量报告.md').write_text('\n'.join(lines))
print('UNTIMED_WORK_SOURCE_HASHES',json.dumps(digests))
print('UNTIMED_WORK_COUNTS',json.dumps(rows))
