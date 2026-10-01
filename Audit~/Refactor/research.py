#!/usr/bin/env python3
"""Actual candidate versus retained fused/combined and normalized DirectArray Micro kernels."""
import argparse,hashlib,json,math,os,pathlib,shutil,statistics,subprocess,sys,time
from xml.sax.saxutils import escape
ROOT=pathlib.Path(__file__).resolve().parents[2];HERE=ROOT/'Audit~/Refactor'
sys.path[:0]=[str(ROOT/'Audit~/AdaptiveResearch'),str(ROOT/'Audit~/ArrayBuild')]
import variants
from build_candidates import candidates
BASE='0587201567aa21edb60055acf5028faf8d659b39'

def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--output',type=pathlib.Path,required=True);ap.add_argument('--dotnet',default='dotnet');ap.add_argument('--rounds',type=int,default=3);ap.add_argument('--smoke',action='store_true');ap.add_argument('--portable',action='store_true');ap.add_argument('--portable-only',action='store_true');args=ap.parse_args()
    if args.rounds<1:ap.error('rounds must be positive')
    if args.portable and args.portable_only:ap.error('choose --portable or --portable-only, not both')
    out=args.output.resolve();out.mkdir(parents=True,exist_ok=True)
    if (out/'manifest.json').exists():ap.error('preserve old evidence; use new output directory')
    env=dict(os.environ,DOTNET_gcConcurrent='0',DOTNET_TieredCompilation='0',DOTNET_ReadyToRun='0',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1');env.setdefault('DOTNET_CLI_HOME',str(out/'dotnet-home'))
    if hasattr(os,'sched_setaffinity'):os.sched_setaffinity(0,{min(os.sched_getaffinity(0))})
    def run(cmd,name,allow_failure=False):
        print('RUN',name,flush=True);start=time.time();p=subprocess.run(list(map(str,cmd)),cwd=ROOT,env=env,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=3600);(out/(name+'.log')).write_text(p.stdout);print(p.stdout[-1200:],flush=True);print('SECONDS',round(time.time()-start,2),flush=True)
        if p.returncode and not allow_failure:raise RuntimeError(name+' failed '+str(p.returncode))
        return p.stdout
    def git(path):return subprocess.check_output(['git','show',BASE+':'+path],cwd=ROOT)
    sources=out/'sources';sources.mkdir(exist_ok=True)
    original=sources/'original';(original/'Runtime').mkdir(parents=True,exist_ok=True)
    baseline_paths=subprocess.check_output(['git','ls-tree','-r','--name-only',BASE,'--','Runtime'],cwd=ROOT,text=True).splitlines()
    for relative in baseline_paths:
        if relative.endswith('.cs'):
            target=original/relative;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(git(relative))
    candidate=sources/'candidate';shutil.copytree(ROOT/'Runtime',candidate/'Runtime')
    harness=sources/'ResearchComparisons.cs';shutil.copy2(HERE/'ResearchComparisons.cs',harness)
    micro_source=(ROOT/'Audit~/MicroBitmapSet.cs').read_text();gen=sources/'generated';generated=candidates(micro_source,gen)
    # Normalize physical-copy capacity exactly as the retained Crossover comparison.
    replacements={}
    for label in ('control','arrays'):
        text=generated[label].read_text();old='            else if (used <= 1) inline = used == 0 ? 0 : source.Read(0);\n            else { entries = new Entry[used]; Array.Copy(source.entries, entries, used); }';new='            else if (source.entries == null) inline = used == 0 ? 0 : source.inline;\n            else { entries = new Entry[source.entries.Length]; Array.Copy(source.entries, entries, used); }'
        if text.count(old)!=1:raise RuntimeError('micro copy fragment changed')
        text=text.replace(old,new);oldname='KernelControlSet' if label=='control' else 'DirectArraySet';text=text.replace(oldname,'ResearchMicroSet')
        dest=gen/(label+'-normalized.cs');dest.write_text(text);replacements[label]=dest
    combined=gen/'PreviousCombined.cs';combined.write_text(variants.copy(variants.simd(variants.dense((original/'Runtime/RuntimeTagSet.cs').read_text()))))
    fusion=sources/'DenseFusion.cs';shutil.copy2(ROOT/'Audit~/Fusion/DenseFusion.cs',fusion)
    # Guard-only portable variant; preserve source and exact diff in the evidence.
    portable_fusion=sources/'DenseFusionPortable.cs';portable_fusion.write_text(fusion.read_text().replace('#if NET8_0_OR_GREATER','#if NET8_0_OR_GREATER && !GAMEPLAYTAGS_FORCE_PORTABLE'))
    micro=sources/'MicroBitmapSet.cs';micro.write_text(micro_source)
    stub=sources/'UnityStubs.cs';shutil.copy2(ROOT/'Audit~/UnityStubs.cs',stub)
    common=[p for p in (original/'Runtime').glob('*.cs') if p.name not in ('RuntimeTagSet.cs','TagRegistry.cs')]+[candidate/'Runtime/TagRegistry.cs',stub]
    builds={};jobs=[]
    def build(name,files,extra=''):
        folder=out/'build'/name;folder.mkdir(parents=True,exist_ok=True);proj=folder/'Research.csproj'
        shutil.copy2(ROOT/'Audit~/NuGet.Config',folder/'NuGet.Config')
        proj.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><LangVersion>9.0</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><AllowUnsafeBlocks>true</AllowUnsafeBlocks><DefineConstants>'+extra+'</DefineConstants></PropertyGroup><ItemGroup>'+''.join('<Compile Include="'+escape(str(p))+'"/>' for p in files)+'</ItemGroup></Project>')
        run([args.dotnet,'build',proj,'-c','Release','-o',folder/'bin'],name+'-build');return folder/'bin/Research.dll'
    flavors=['portable'] if args.portable_only else ['hardware','portable'] if args.portable else ['hardware']
    for flavor in flavors:
        symbol='GAMEPLAYTAGS_FORCE_PORTABLE' if flavor=='portable' else ''
        backend=portable_fusion if flavor=='portable' else fusion
        for label in ('candidate','combined','kernel','micro'):
            name=label+'-'+flavor
            files=common+[harness]
            if label=='candidate':files += sorted((candidate/'Runtime').glob('RuntimeTagSet*.cs'))+sorted((candidate/'Runtime').glob('RuntimeBitOperations*.cs'))
            elif label=='combined':files += [combined,backend]
            else:files += [original/'Runtime/RuntimeTagSet.cs',micro,replacements['control' if label=='kernel' else 'arrays'],backend]
            builds[name]=build(name,files,(symbol+(';MICRO' if label in ('micro','kernel') else '')).strip(';'))
            modes=('Auto','Sparse','Dense','Compressed') if label=='candidate' else ('Auto','Sparse','Dense') if label=='combined' else ('Auto','Micro','Dense')
            for mode in modes:jobs.append((name+'-'+mode,name,mode))
            for mode in modes:run([args.dotnet,builds[name],name,'tests',out/(name+'-'+mode+'-tests.json'),0,'full',mode],name+'-'+mode+'-tests')
    # Byte-identical candidate control diagnoses process/address effects.
    jobs.append(('candidate-AA','candidate-'+flavors[0],'Auto'))
    manifest={'base':BASE,'candidate_git_head':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'candidate_worktree_dirty':bool(subprocess.check_output(['git','status','--porcelain','--','Runtime'],cwd=ROOT,text=True).strip()),'files':{p.relative_to(sources).as_posix():digest(p) for p in sources.rglob('*') if p.is_file()},'affinity':sorted(os.sched_getaffinity(0)) if hasattr(os,'sched_getaffinity') else None,'rounds':args.rounds,'smoke':args.smoke,'portable':args.portable or args.portable_only,'flavors':flavors,'gcConcurrent':'0','jobs':jobs,'contracts':'Set-kernel comparison on shared registry/authoring substrate, not full current assembly. Micro copy preserves physical capacity. All actual timed outputs checked. Forced portable disables explicit System.Runtime.Intrinsics kernels; System.Numerics.Vector and BCL acceleration may remain. Batch GC (DOTNET_gcConcurrent=0) for every variant; no forced collections. Separate protocol from lifecycle matrix; equal-sized operands, exact/build/union/copy-append/copy-remove only.'}
    (out/'manifest.json').write_text(json.dumps(manifest,indent=2));run([args.dotnet,'--info'],'dotnet-info')
    for round_id in range(args.rounds):
        order=jobs[round_id%len(jobs):]+jobs[:round_id%len(jobs)]
        if round_id%2:order.reverse()
        for label,buildname,mode in order:
            run([args.dotnet,builds[buildname],label,'all',out/(label+'-r'+str(round_id)+'.json'),round_id,'smoke' if args.smoke else 'full',mode],label+'-r'+str(round_id),allow_failure=True)
    grouped={};expected=None;digests={};cohort=None
    for label,buildname,mode in jobs:
        values={}
        for round_id in range(args.rounds):
            payload=json.loads((out/(label+'-r'+str(round_id)+'.json')).read_text());rows=payload['rows']
            if not rows:raise RuntimeError('empty benchmark matrix '+label)
            if len(rows)!=(284 if args.smoke else 4518):raise RuntimeError('incomplete benchmark matrix '+label+' rows='+str(len(rows)))
            if payload['variant']!=label or payload['layout']!=mode or payload['round']!=round_id or payload['stage']!='all':raise RuntimeError('payload attribution mismatch')
            if payload.get('gcLatencyMode')!='Batch' or payload.get('gcConcurrent')!='0':raise RuntimeError('GC protocol mismatch')
            if payload['portableDenseKernels']!=('portable' in buildname):raise RuntimeError('backend attribution mismatch')
            current=(payload['runtime'],payload['architecture'],payload['os'],payload['validationProtocolVersion'])
            if cohort is None:cohort=current
            if cohort!=current:raise RuntimeError('mixed runtime/architecture/protocol cohort')
            keys=[tuple(row[k] for k in ('stage','op','universe','members','distribution','seed','relation')) for row in rows]
            if len(keys)!=len(set(keys)):raise RuntimeError('duplicate row '+label)
            if expected is None:expected=set(keys)
            if set(keys)!=expected:raise RuntimeError('mismatched matrix '+label)
            for key,row in zip(keys,rows):
                if row['variant']!=label or row['round']!=round_id:raise RuntimeError('row attribution mismatch')
                if row.get('status')=='failed':
                    digests.setdefault(key,row['digest']);values.setdefault(key,[]).append(row);continue
                if row['iterations']<=0 or row['units']<=0:raise RuntimeError('invalid denominator')
                if any(len(row[k])!=7 for k in ('ns','allocated','ms','gc')):raise RuntimeError('wrong sample count')
                if any(not math.isfinite(x) or x<=0 for k in ('ns','ms') for x in row[k]):raise RuntimeError('invalid timing sample')
                if any(not math.isfinite(x) or x<0 for x in row['allocated']):raise RuntimeError('invalid allocation sample')
                for ns,ms in zip(row['ns'],row['ms']):
                    if not math.isclose(ns,ms*1e6/(row['iterations']*row['units']),rel_tol=1e-9):raise RuntimeError('timing normalization mismatch')
                if key in digests and digests[key]!=row['digest']:raise RuntimeError('fixture mismatch')
                digests[key]=row['digest'];values.setdefault(key,[]).append(row)
        grouped[label]={key:({'status':'failed','failures':[{'round':r['round'],'error':r['error']} for r in rows if r.get('status')=='failed']} if any(r.get('status')=='failed' for r in rows) else {'status':'measured','median_ns':statistics.median(statistics.median(r['ns']) for r in rows),'round_medians_ns':[statistics.median(r['ns']) for r in rows],'allocated_bytes':statistics.median(statistics.median(r['allocated']) for r in rows)}) for key,rows in values.items()}
    result=[]
    for key in sorted(expected):
        row=dict(zip(('stage','op','universe','members','distribution','seed','relation'),key));row['digest']=digests[key];row['variants']={label:grouped[label][key] for label,_,_ in jobs};result.append(row)
    (out/'comparison.json').write_text(json.dumps(result,indent=2));failures=[{'key':{k:r[k] for k in ('stage','op','universe','members','distribution','seed','relation')},'variant':v,'failures':entry['failures']} for r in result for v,entry in r['variants'].items() if entry['status']=='failed'];(out/'failures.json').write_text(json.dumps(failures,indent=2));print('RESEARCH FRONTIER COMPLETE rows='+str(len(result))+' failures='+str(len(failures)),flush=True)
    if failures:raise SystemExit(1)
if __name__=='__main__':main()
