#!/usr/bin/env python3
"""Literal array-builder candidates, fixed controls and all raw results. No auto-promotion."""
import argparse,csv,hashlib,json,math,os,pathlib,statistics,subprocess,sys,tempfile
ROOT=pathlib.Path(__file__).resolve().parents[2]
HERE=ROOT/'Audit~/ArrayBuild'
sys.path[:0]=[str(HERE),str(ROOT/'Audit~'),str(ROOT/'Audit~/Triad')]
from build_candidates import candidates
from integer_audit import project
from micro_pilot import fixture
AUTO='84729cc3b6e1b4681328806df2d7c8a1d8f568f6'
MICRO='9be85fcd1f8d1f594ceb5bc98f2ce234b398e0e0'
BASE='d45f21ffc5858a7ce9be68af9322924a774e40b4'
BULK={'union','copy_append','copy_remove','union_into','copy_append_reuse','copy_remove_reuse'}

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',required=True);parser.add_argument('--rounds',type=int,default=3)
    args=parser.parse_args()
    if args.rounds<3:parser.error('At least three complete rounds')
    out=pathlib.Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True)
    generated=out/'generated';generated.mkdir(exist_ok=True)
    env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1',DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1')
    if hasattr(os,'sched_setaffinity'):os.sched_setaffinity(0,{min(os.sched_getaffinity(0))})
    def git(*items):return subprocess.check_output(['git',*items],cwd=ROOT)
    def save(name,data):(out/name).write_text(json.dumps(data,ensure_ascii=False,indent=2))
    def run(command,name,cwd,extra=None,quiet=False):
        proc=subprocess.run(list(map(str,command)),cwd=cwd,env=dict(env,**(extra or {})),stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,timeout=1500)
        (out/name).write_text(proc.stdout)
        if not quiet or proc.returncode:print(proc.stdout,flush=True)
        if proc.returncode:raise RuntimeError(name+' failed: '+str(proc.returncode))
        return proc.stdout
    def build(folder,name,files,symbol='',framework='net8.0',exe=True,reference=None):
        folder.mkdir(parents=True,exist_ok=True);p=folder/(name+'.csproj')
        project(p,files,symbol,framework=framework,executable=exe)
        text=p.read_text().replace('<Optimize>true</Optimize>','<Optimize>true</Optimize><AllowUnsafeBlocks>true</AllowUnsafeBlocks>')
        if reference:text=text.replace('</Project>','<ItemGroup><Reference Include="PortableArrays"><HintPath>'+str(reference)+'</HintPath></Reference></ItemGroup></Project>')
        p.write_text(text)
        run(['dotnet','build',p,'-c','Release','-p:BaseIntermediateOutputPath=obj-'+name+'/', '-o',folder/name],name+'-build.log',folder,quiet=True)
        return folder/name/(name+'.dll')
    protected=[]
    for folder in ('Runtime','Editor','Samples~','Tests','package.json'):
        if git('diff','--name-only',AUTO,'HEAD','--',folder).strip():raise RuntimeError('Production modified '+folder)
        protected.extend(git('ls-tree','-r','--name-only',AUTO,'--',folder).decode().splitlines())
    for path,ref in [('Audit~/MicroBitmapSet.cs',MICRO),('Audit~/FourWay.cs',AUTO),('Audit~/Triad/run_triad.py',BASE),('Audit~/Fusion/DenseFusion.cs',BASE)]:
        if git('show',ref+':'+path)!=(ROOT/path).read_bytes():raise RuntimeError('Changed fixed control '+path)
    source=git('show',MICRO+':Audit~/MicroBitmapSet.cs').decode()
    original=git('show',AUTO+':Audit~/FourWay.cs').decode()
    files=candidates(source,generated)
    backend=ROOT/'Audit~/Fusion/DenseFusion.cs'
    save('sources.json',{'head':git('rev-parse','HEAD').decode().strip(),'tree':git('rev-parse','HEAD^{tree}').decode().strip(),
         'references':{label:{'commit':ref,'tree':git('rev-parse',ref+'^{tree}').decode().strip()} for label,ref in [('auto',AUTO),('micro',MICRO),('fusion',BASE)]},
         'protected_files':len(protected),'generated':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in files.values()},
         'backend_sha256':hashlib.sha256(backend.read_bytes()).hexdigest()})
    save('host.json',{'affinity':sorted(os.sched_getaffinity(0)) if hasattr(os,'sched_getaffinity') else [],'machine':os.uname().machine,
         'cpuinfo':pathlib.Path('/proc/cpuinfo').read_text()[:9000] if pathlib.Path('/proc/cpuinfo').exists() else '', 'python':sys.version})
    common=[ROOT/'Runtime/**/*.cs',ROOT/'Audit~/UnityStubs.cs',ROOT/'Audit~/MicroBitmapSet.cs']
    with tempfile.TemporaryDirectory(prefix='array-builder-') as tmp:
        work=pathlib.Path(tmp)
        (work/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
        (work/'global.json').write_text(json.dumps({'sdk':{'version':'8.0.425','rollForward':'disable'}}))
        run(['dotnet','--info'],'environment.txt',work,quiet=True)
        # Unchanged full baseline suite for the control and both literal candidates.
        for label,sourcefile in files.items():
            test=generated/(sourcefile.stem+'Tests.cs');test.write_text((ROOT/'Audit~/MicroTests.cs').read_text().replace('MicroBitmapSet',sourcefile.stem))
            dll=build(work/(label+'-tests'),sourcefile.stem+'Tests',common+[sourcefile,backend,test])
            run(['dotnet','exec',dll,out/(label+'-tests.json')],label+'-tests.log',work)
            run(['dotnet','exec',dll,out/(label+'-nohw-tests.json')],label+'-nohw-tests.log',work,{'DOTNET_EnableHWIntrinsic':'0'})
            targeted=generated/(sourcefile.stem+'BuilderChecks.cs');targeted.write_text((HERE/'BuilderChecks.template.cs').read_text().replace('__TYPE__',sourcefile.stem))
            targeted_dll=build(work/(label+'-builder'),sourcefile.stem+'BuilderChecks',common+[sourcefile,backend,targeted])
            run(['dotnet','exec',targeted_dll,out/(label+'-builder-tests.json')],label+'-builder-tests.log',work)
        friend=generated/'PortableFriend.cs';friend.write_text('[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("PortableArraysTests")]\n')
        portable=build(work/'portable','PortableArrays',common+[files['growth'],backend,friend],framework='netstandard2.1',exe=False)
        portable_test=build(work/'portable-test','PortableArraysTests',[generated/'DirectGrowthSetTests.cs'],reference=portable)
        run(['dotnet','exec',portable_test,out/'portable-tests.json'],'portable-tests.log',work)
        specs={'small':{'auto':('auto',None,'Auto'),'micro':('micro',None,'Auto'),'kernel':('micro',files['control'],'Auto'),'arrays':('micro',files['arrays'],'Auto'),'growth':('micro',files['growth'],'Auto')},
               'large':{'auto':('auto',None,'Auto'),'auto_dense':('auto',None,'Dense'),'micro':('micro',None,'Dense'),'kernel':('micro',files['control'],'Dense'),'arrays':('micro',files['arrays'],'Dense'),'growth':('micro',files['growth'],'Dense')}}
        builds={}
        for stage,items in specs.items():
            for label,(kind,sourcefile,mode) in items.items():
                for contract in ('fresh','prepared'):
                    text=fixture(original,kind,contract=='prepared')
                    sizes='new[] { 0, 1, 8, 32, 128, 1024, 4096 }'
                    if text.count(sizes)!=1:raise RuntimeError('Fixture size template changed')
                    text=text.replace(sizes,'new[] { 0, 1, 7, 8, 9, 31, 32, 33, 127, 128, 129 }' if stage=='small' else 'new[] { 128, 1024, 4096 }')
                    text=text.replace('Math.Max(24, Math.Min(1000, 131072 / (size + 1)))','Math.Max(500, Math.Min(20000, 1048576 / (size + 1)))')
                    if sourcefile:text=text.replace('GameplayTags.Experiments.MicroBitmapSet','GameplayTags.Experiments.'+sourcefile.stem)
                    tag=stage+'-'+label+'-'+contract;literal=generated/(tag+'.cs');literal.write_text(text)
                    includes=[ROOT/'Runtime/**/*.cs',ROOT/'Audit~/UnityStubs.cs',literal]
                    if kind=='micro':includes.append(ROOT/'Audit~/MicroBitmapSet.cs')
                    if sourcefile:includes.extend([sourcefile,backend])
                    builds[(stage,label,contract)]=(build(work/tag,'Bench_'+tag.replace('-','_'),includes,'CANDIDATE'),mode)
        save('plan.json',{'rounds':args.rounds,'variants':{stage:{key:mode for key,(_,_,mode) in spec.items()} for stage,spec in specs.items()},
             'changes':'arrays: fixed buffers in count/merge/remove, block-copy tails; growth: only adds direct merge into replacement buffer when expansion is necessary',
             'unchanged':'scope, source ownership, capacity policy, aliases, SIMD query, Dense backend, counted materialization and baseline fixture',
             'historical_contract':'half overlap, existing half subset; fresh/prepared separate; same sizes and iterations as previous Fusion',
             'native_unity':'not_run','all_historical_references_rerun':False})
        order=list(builds)
        for r in range(args.rounds):
            jobs=order[r%len(order):]+order[:r%len(order)]
            if r&1:jobs.reverse()
            for key in jobs:
                dll,mode=builds[key];tag='-'.join(key)+'-r'+str(r)
                run(['dotnet','exec',dll,out/(tag+'.json'),key[1],mode],tag+'.log',work,quiet=True)
                print('MEASURED '+tag,flush=True)
        tables={};digests={};raw_count=samples=prepared_samples=0
        for key in builds:
            grouped={}
            for r in range(args.rounds):
                data=json.loads((out/('-'.join(key)+'-r'+str(r)+'.json')).read_text());seen=set()
                for row in data['rows']:
                    k=(row['operation'],row['size'],row['universe'],row['distribution'])
                    if row['status']!='measured' or k in seen:raise RuntimeError('Invalid measured row')
                    seen.add(k)
                    if len(row['ns'])!=7 or len(row['allocated_bytes'])!=7:raise RuntimeError('Missing samples')
                    if any(not math.isfinite(v) or v<0 for v in row['ns']):raise RuntimeError('Invalid latency')
                    shape=(key[0],)+k[1:]
                    if shape in digests and digests[shape]!=row['inputDigest']:raise RuntimeError('Unequal inputs')
                    digests[shape]=row['inputDigest']
                    if key[2]=='prepared':
                        if any(v!=0 for v in row['allocated_bytes']):raise RuntimeError('Prepared allocation: '+str(key)+str(k))
                        prepared_samples+=7
                    grouped.setdefault(k,[]).append(row);raw_count+=1;samples+=7
            table={}
            for k,rows in grouped.items():
                if len(rows)!=args.rounds:raise RuntimeError('Missing round')
                med=[statistics.median(row['ns']) for row in rows]
                table[k]={'ns':statistics.median(med),'round_medians':med,'bytes':statistics.median(v for row in rows for v in row['allocated_bytes']),
                          'input_layout':rows[0]['input_storage'],'input_digest':rows[0]['inputDigest']}
            tables[key]=table
        result=[];gates={}
        for (stage,label,contract),table in tables.items():
            controls=['auto','micro','kernel'] if stage=='small' else ['auto','auto_dense','micro','kernel']
            for k,value in sorted(table.items()):
                refs={name:tables[(stage,name,contract)][k] for name in controls}
                lower=value['ns']<min(ref['ns'] for ref in refs.values())
                margin=all(value['round_medians'][r]<=.95*min(ref['round_medians'][r] for ref in refs.values()) for r in range(args.rounds))
                row=dict(stage=stage,variant=label,contract=contract,operation=k[0],members=k[1],definitions=k[2],distribution=k[3],value=value,
                         references=refs,lower_than_controls=lower,margin_controls_every_round=margin,
                         change_from_kernel=value['ns']/refs['kernel']['ns']-1)
                result.append(row)
                if label in ('arrays','growth') and k[0] in BULK:
                    g=gates.setdefault('/'.join([stage,label,contract]),{'cases':0,'lower_median':0,'every_round_margin':0,'lower_than_kernel':0})
                    g['cases']+=1;g['lower_median']+=int(lower);g['every_round_margin']+=int(margin);g['lower_than_kernel']+=int(value['ns']<refs['kernel']['ns'])
        save('comparison.json',result);save('gates.json',gates)
        with (out/'comparison.csv').open('w',newline='') as f:
            writer=csv.writer(f);writer.writerow(['stage','variant','contract','operation','members','definitions','distribution','ns','r0','r1','r2','bytes','layout','kernel_ns','kernel_change','lower_all','margin_all'])
            for row in result:
                v=row['value'];writer.writerow([row[k] for k in ['stage','variant','contract','operation','members','definitions','distribution']]+[v['ns'],*v['round_medians'],v['bytes'],v['input_layout'],row['references']['kernel']['ns'],row['change_from_kernel'],row['lower_than_controls'],row['margin_controls_every_round']])
        save('decision.json',{'production_promoted':False,'release_approved':False,'native_unity':'not_run','raw_rows':raw_count,'timing_samples':samples,
             'prepared_zero_samples':prepared_samples,'aggregate_rows':len(result),'rounds':args.rounds,'note':'No cherry-picking. Unchanged query and Dense code timing differences are not new algorithmic gains.'})
        print('ARRAY_GATES '+json.dumps(gates),flush=True)
        for stage,n,u in [('small',8,262144),('large',1024,10000)]:
            for dist in ['scattered'] if stage=='small' else ['contiguous','scattered']:
                for contract in ('fresh','prepared'):
                    ops=['exact','union','copy_append','copy_remove'] if contract=='fresh' else ['exact','union_into','copy_append_reuse','copy_remove_reuse']
                    focus={label:{op:tables[(stage,label,contract)][(op,n,u,dist)] for op in ops} for label in specs[stage]}
                    print('ARRAY_FOCUS '+json.dumps({'stage':stage,'members':n,'definitions':u,'distribution':dist,'contract':contract,'data':focus}),flush=True)
        print('ARRAY_DECISION '+(out/'decision.json').read_text(),flush=True)

if __name__=='__main__':main()
