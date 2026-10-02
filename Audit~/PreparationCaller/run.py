#!/usr/bin/env python3
"""Untouched v1/v2/v3 source, isolated public Reset caller. Diagnostic only."""
import pathlib, sys, os, subprocess, json, importlib.util, hashlib, random, statistics, csv
from xml.sax.saxutils import escape
ROOT=pathlib.Path(__file__).resolve().parents[2]
PIN='db0ffc25448a5df399651550acf627d210482aeb'
def main():
    out=pathlib.Path(sys.argv[1]).resolve();out.mkdir(parents=True,exist_ok=True);work=out/'work';work.mkdir(exist_ok=True)
    for rel in ('Audit~/PreparationV3/generate.py',):
        assert (ROOT/rel).read_bytes()==subprocess.check_output(['git','show',PIN+':'+rel],cwd=ROOT)
    spec=importlib.util.spec_from_file_location('pinned_v3',ROOT/'Audit~/PreparationV3/generate.py');g=importlib.util.module_from_spec(spec);spec.loader.exec_module(g)
    files=g.generate(out/'generated')
    (work/'global.json').write_text(json.dumps({'sdk':{'version':'8.0.425','rollForward':'disable'}}))
    (work/'NuGet.Config').write_text('<configuration><packageSources><clear/></packageSources></configuration>')
    env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_ReadyToRun='0',DOTNET_NOLOGO='1',DOTNET_CLI_TELEMETRY_OPTOUT='1')
    os.sched_setaffinity(0,{min(os.sched_getaffinity(0))})
    def run(cmd,log,extra=None):
        p=subprocess.run(list(map(str,cmd)),cwd=work,env=dict(env,**(extra or {})),stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,timeout=180)
        (out/log).write_text(p.stdout)
        if p.returncode:raise RuntimeError(p.stdout[-12000:])
        return p.stdout
    assert run(['dotnet','--version'],'sdk.txt').strip()=='8.0.425'
    run(['dotnet','--info'],'environment.txt');run(['lscpu'],'cpu.txt')
    base=ROOT/'Runtime/RuntimeTagSet.cs';common=list((ROOT/'Runtime').rglob('*.cs'));common.remove(base)
    common += [ROOT/'Audit~/UnityStubs.cs',ROOT/'Audit~/Fusion/DenseFusion.cs']
    builds={};manifest={}
    for version in ('v1','v2','v3'):
        for family in ('runtime','micro'):
            name=family+'-'+version;folder=work/name;folder.mkdir(exist_ok=True)
            src=common+([base,ROOT/'Audit~/MicroBitmapSet.cs'] if family=='micro' else [])+files[version][family]+[ROOT/'Audit~/PreparationCaller/CallerProbe.cs']
            xml=''.join('<Compile Include="'+escape(str(p))+'"/>' for p in src)
            project=folder/'Caller.csproj'
            project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><AssemblyName>Caller</AssemblyName><EnableDefaultCompileItems>false</EnableDefaultCompileItems><LangVersion>9.0</LangVersion><Optimize>true</Optimize><AllowUnsafeBlocks>true</AllowUnsafeBlocks><DefineConstants>'+('BULK_MICRO' if family=='micro' else '')+'</DefineConstants></PropertyGroup><ItemGroup>'+xml+'</ItemGroup></Project>')
            run(['dotnet','build',project,'-c','Release','-o',folder/'bin'],name+'-build.log')
            dll=folder/'bin/Caller.dll';builds[name]=dll
            manifest[name]=dict(binary_sha256=hashlib.sha256(dll.read_bytes()).hexdigest(),
                sources={str(p.relative_to(out)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files[version][family]})
    rawrows=[]
    for rnd in range(3):
        jobs=[(name+suffix,dll) for name,dll in builds.items() for suffix in ('','AA')]
        random.Random(20261123+rnd).shuffle(jobs)
        for label,dll in jobs:
            dest=out/(label+'-r'+str(rnd)+'.json')
            print(run(['dotnet',dll,label,dest,rnd],dest.stem+'.log').strip(),flush=True)
            d=json.loads(dest.read_text());assert len(d['rows'])==24
            assert d['checks']>0
            rawrows.extend(d['rows'])
    codegen={}
    for name,dll in builds.items():
        text=run(['dotnet',dll,name,out/(name+'-codegen.json'),0],name+'-codegen.log',
            {'DOTNET_JitDisasm':'CallerProbe:ResetLoop GameplayTags.RuntimeTagSet:ResetFromSortedUnique GameplayTags.RuntimeTagSet:ValidateSortedHandles GameplayTags.Experiments.DirectArraySet:ResetFromSortedUnique GameplayTags.Experiments.DirectArraySet:ValidateSortedHandles'})
        codegen[name]=[line for line in text.splitlines() if 'Assembly listing' in line or 'Total bytes of code' in line]
    grouped={}
    for row in rawrows:
        assert all(x==0 for x in row['allocated']) and len(row['ns'])==7
        key=(row['label'],row['universe'],row['members'],row['scattered'])
        values=grouped.setdefault(key,{})
        assert row['round'] not in values
        values[row['round']]=statistics.median(row['ns'])
    data={key:[values[i] for i in range(3)] for key,values in grouped.items()}
    output=[];summary={}
    def stable(x,y):return all(.95<=a/b<=1.05 for a,b in zip(x,y))
    for key,x in data.items():
        if not key[0].endswith('-v3'):continue
        xa=data[(key[0]+'AA',)+key[1:]]
        for control in ('v1','v2'):
            name=key[0].replace('v3',control);b=data[(name,)+key[1:]];ba=data[(name+'AA',)+key[1:]]
            fast=all(max(a,aa)<=.95*min(c,ca) for a,aa,c,ca in zip(x,xa,b,ba))
            slow=all(min(a,aa)>1.05*max(c,ca) for a,aa,c,ca in zip(x,xa,b,ba))
            good=stable(x,xa) and stable(b,ba)
            row=dict(cpu=os.uname().machine,candidate=key[0],control=control,universe=key[1],members=key[2],scattered=key[3],
                candidate_ns=statistics.median(x),baseline_ns=statistics.median(b),faster5=fast,slower5=slow,AAstable=good)
            output.append(row);group=key[0]+'_vs_'+control;s=summary.setdefault(group,dict(cases=0,fast=0,slow=0,AAstable=0))
            s['cases']+=1;s['fast']+=fast;s['slow']+=slow;s['AAstable']+=good
    with (out/'Caller_Comparisons.csv').open('w',encoding='utf-8-sig',newline='') as f:
        w=csv.DictWriter(f,fieldnames=list(output[0]));w.writeheader();w.writerows(output)
    result=dict(primary_source=PIN,diagnostic_source=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),
        cpu=os.uname().machine,manifest=manifest,codegen=codegen,summary=summary,rows=output,
        raw_rows=len(rawrows),retained_samples=len(rawrows)*7,all_prepared_samples_zero=True,
        limitation='Different standalone caller and CI job, diagnostic only; do not substitute primary matrix timings. A/A screens are not confidence intervals. No proof of causal JIT-budget explanation.')
    (out/'review.json').write_text(json.dumps(result,indent=2))
    print('CALLER_SUMMARY '+json.dumps(dict(cpu=result['cpu'],primary_source=PIN,summary=summary,codegen=codegen,raw_rows=result['raw_rows'])))
    print('CALLER_FOCUS '+json.dumps([r for r in output if r['universe']==262144 and r['scattered'] and r['members'] in (1,2,8)]))
if __name__=='__main__':main()
