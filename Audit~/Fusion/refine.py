#!/usr/bin/env python3
"""Refine retained SIMD/Dense candidates; compile, test and measure, never auto-promote."""
import argparse,csv,hashlib,json,os,pathlib,statistics,subprocess,sys,tempfile
ROOT=pathlib.Path(__file__).resolve().parents[2]
HERE=ROOT/'Audit~/Fusion'
sys.path[:0]=[str(ROOT/'Audit~'),str(ROOT/'Audit~/Triad')]
from integer_audit import project
from micro_pilot import fixture
from run_triad import generate,replace_body
AUTO='84729cc3b6e1b4681328806df2d7c8a1d8f568f6'
MICRO='9be85fcd1f8d1f594ceb5bc98f2ce234b398e0e0'
PREVIOUS='5cb16ca6b1a39b18439c4851ad42bed3cb8d9d70'
PRIMARY={'exact','union','copy_append','copy_remove','union_into','copy_append_reuse','copy_remove_reuse'}

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',required=True);p.add_argument('--rounds',type=int,default=3)
    args=p.parse_args()
    if args.rounds<3:p.error('At least three complete rounds required')
    out=pathlib.Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True);gen=out/'generated';gen.mkdir(exist_ok=True)
    env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1',DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1')
    if hasattr(os,'sched_setaffinity'):os.sched_setaffinity(0,{min(os.sched_getaffinity(0))})
    def git(*a):return subprocess.check_output(['git',*a],cwd=ROOT)
    def save(name,data):(out/name).write_text(json.dumps(data,ensure_ascii=False,indent=2))
    def run(cmd,name,cwd,extra=None,quiet=False):
        proc=subprocess.run(list(map(str,cmd)),cwd=cwd,env=dict(env,**(extra or {})),stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,timeout=1200)
        (out/name).write_text(proc.stdout)
        if not quiet or proc.returncode:print(proc.stdout,flush=True)
        if proc.returncode:raise RuntimeError(name+' failed: '+str(proc.returncode))
        return proc.stdout
    def build(folder,name,files,framework='net8.0',exe=True,reference=None):
        folder.mkdir(parents=True,exist_ok=True);f=folder/(name+'.csproj')
        project(f,files,'',framework=framework,executable=exe)
        text=f.read_text().replace('<Optimize>true</Optimize>','<Optimize>true</Optimize><AllowUnsafeBlocks>true</AllowUnsafeBlocks>')
        if reference:text=text.replace('</Project>','<ItemGroup><Reference Include="PortableFusion"><HintPath>'+str(reference)+'</HintPath></Reference></ItemGroup></Project>')
        f.write_text(text)
        run(['dotnet','build',f,'-c','Release','-p:BaseIntermediateOutputPath=obj-'+name+'/', '-o',folder/name],name+'-build.log',folder)
        return folder/name/(name+'.dll')
    for folder in ('Runtime','Editor','Samples~','Tests','package.json'):
        if git('diff','--name-only',AUTO,'HEAD','--',folder).strip():raise RuntimeError('Production changed: '+folder)
    for path in ('Audit~/MicroBitmapSet.cs','Audit~/FourWay.cs','Audit~/Triad/DenseVectorKernels.cs','Audit~/Triad/run_triad.py'):
        ref=MICRO if path.endswith('MicroBitmapSet.cs') else AUTO if path.endswith('FourWay.cs') else PREVIOUS
        if git('show',ref+':'+path)!=(ROOT/path).read_bytes():raise RuntimeError('Changed control source '+path)
    original=git('show',AUTO+':Audit~/FourWay.cs').decode();micro=git('show',MICRO+':Audit~/MicroBitmapSet.cs').decode()
    old=generate(micro,gen)
    text=old['simd'].read_text().replace('OrderedSimdBitmapSet','FusedKernelSet')
    text=replace_body(text,'private static int DenseUnion(ulong[] a, ulong[] b, ulong[] output)','            return DenseFusion.Union(a,b,output);')
    text=replace_body(text,'private static int DenseDifference(ulong[] a, ulong[] b)','            return DenseFusion.Difference(a,b,a);')
    kernel=gen/'FusedKernelSet.cs';kernel.write_text(text)
    combined=text.replace('FusedKernelSet','FusedBitmapSet')
    combined=replace_body(combined,'public static FusedBitmapSet Union(','''            if (a == null) throw new ArgumentNullException(nameof(a));
            a.Require(b);
            int bound = (int)Math.Min(a.Registry.Count, (long)a.count + b.count);
            bool useDense = layout == BitmapLayout.Dense || (layout == BitmapLayout.Auto && bound != 0
                && 8L * a.Registry.WordCount <= (long)EntryBytes * bound);
            var result = new FusedBitmapSet(a.Registry, 0,
                useDense ? BitmapLayout.Dense : layout == BitmapLayout.Auto ? BitmapLayout.Micro : layout);
            // Independent, freshly allocated output. No old output state or aliases exist.
            if (useDense && a.dense != null && b.dense != null && a.count != 0 && b.count != 0 && !ReferenceEquals(a,b))
            {
                result.count = DenseFusion.Union(a.dense,b.dense,result.dense);
                return result;
            }
            UnionCore(a,b,result);
            return result;''')
    fused=gen/'FusedBitmapSet.cs';fused.write_text(combined)
    save('sources.json',{'head':git('rev-parse','HEAD').decode().strip(),'tree':git('rev-parse','HEAD^{tree}').decode().strip(),'auto':AUTO,'micro':MICRO,'previous':PREVIOUS,'production_unchanged':True,
        'generated':{q.name:hashlib.sha256(q.read_bytes()).hexdigest() for q in gen.glob('*.cs')},'fusion_backend':hashlib.sha256((HERE/'DenseFusion.cs').read_bytes()).hexdigest()})
    save('host.json',{'affinity':sorted(os.sched_getaffinity(0)) if hasattr(os,'sched_getaffinity') else None,'cpuinfo':pathlib.Path('/proc/cpuinfo').read_text()[:7000] if pathlib.Path('/proc/cpuinfo').exists() else '', 'python':sys.version})
    common=[ROOT/'Runtime/**/*.cs',ROOT/'Audit~/UnityStubs.cs',ROOT/'Audit~/MicroBitmapSet.cs']
    with tempfile.TemporaryDirectory(prefix='fusion-') as tmp:
        w=pathlib.Path(tmp);(w/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>');(w/'global.json').write_text(json.dumps({'sdk':{'version':'8.0.425','rollForward':'disable'}}))
        run(['dotnet','--info'],'environment.txt',w)
        checks=build(w/'checks','FusionChecks',[HERE/'DenseFusion.cs',HERE/'FusionChecks.cs'])
        run(['dotnet','exec',checks,out/'fusion-tests.json'],'fusion-tests.log',w)
        run(['dotnet','exec',checks,out/'fusion-tests-nohw.json'],'fusion-tests-nohw.log',w,{'DOTNET_EnableHWIntrinsic':'0'})
        for source in (kernel,fused):
            t=gen/(source.stem+'Tests.cs');t.write_text((ROOT/'Audit~/MicroTests.cs').read_text().replace('MicroBitmapSet',source.stem))
            dll=build(w/source.stem,source.stem+'Tests',common+[source,HERE/'DenseFusion.cs',t])
            run(['dotnet','exec',dll,out/(source.stem+'-tests.json')],source.stem+'-tests.log',w)
            run(['dotnet','exec',dll,out/(source.stem+'-tests-nohw.json')],source.stem+'-tests-nohw.log',w,{'DOTNET_EnableHWIntrinsic':'0'})
        portable=build(w/'portable','PortableFusion',common+[fused,HERE/'DenseFusion.cs'],framework='netstandard2.1',exe=False)
        # Actually execute the separately compiled Standard 2.1 scalar backend, not just compile it.
        portabletest=build(w/'portable-tests','PortableFusionTests',[gen/'FusedBitmapSetTests.cs'],reference=portable)
        run(['dotnet','exec',portabletest,out/'portable-full-tests.json'],'portable-full-tests.log',w)
        run(['dotnet','exec',checks,out/'disasm-tests.json'],'fusion-disassembly.log',w,{'COMPlus_JitDisasm':'GameplayTags.Experiments.DenseFusion:*'},quiet=True)
        asm=(out/'fusion-disassembly.log').read_text().lower();save('instruction-markers.json',{x:asm.count(x) for x in ('vpshufb','vpsadbw','popcnt','cnt ','uaddlp','addv','union','intersection','difference')})
        items={
            'auto':('auto',None,'Auto'),
            'micro':('micro',None,'Auto'),
            'simd':('micro',old['simd'],'Auto'),
            'integrated':('micro',fused,'Auto'),
        }
        big={
            'auto_auto':('auto',None,'Auto'),
            'auto_dense':('auto',None,'Dense'),
            'micro_dense':('micro',None,'Dense'),
            'previous_avx':('micro',old['vector'],'Dense'),
            'kernel_only':('micro',kernel,'Dense'),
            'integrated_dense':('micro',fused,'Dense'),
        }
        builds={}
        for stage,specs in [('small',items),('large',big)]:
            for label,(kind,source,mode) in specs.items():
                for contract in ('fresh','prepared'):
                    text=fixture(original,kind,contract=='prepared')
                    old_sizes='new[] { 0, 1, 8, 32, 128, 1024, 4096 }'
                    if old_sizes not in text:raise RuntimeError('Fixture changed')
                    text=text.replace(old_sizes,'new[] { 0, 1, 7, 8, 9, 31, 32, 33, 127, 128, 129 }' if stage=='small' else 'new[] { 128, 1024, 4096 }')
                    text=text.replace('Math.Max(24, Math.Min(1000, 131072 / (size + 1)))','Math.Max(500, Math.Min(20000, 1048576 / (size + 1)))')
                    if source is not None:text=text.replace('GameplayTags.Experiments.MicroBitmapSet','GameplayTags.Experiments.'+source.stem)
                    tag=stage+'-'+label+'-'+contract;src=gen/(tag+'.cs');src.write_text(text)
                    inc=[ROOT/'Runtime/**/*.cs',ROOT/'Audit~/UnityStubs.cs',src]
                    if kind=='micro':inc.append(ROOT/'Audit~/MicroBitmapSet.cs')
                    if source is not None:inc.append(source)
                    if source==old['vector']:inc.append(ROOT/'Audit~/Triad/DenseVectorKernels.cs')
                    if source in (fused,kernel):inc.append(HERE/'DenseFusion.cs')
                    folder=w/tag;folder.mkdir();proj=folder/'Bench.csproj';project(proj,inc,'CANDIDATE')
                    proj.write_text(proj.read_text().replace('<Optimize>true</Optimize>','<Optimize>true</Optimize><AllowUnsafeBlocks>true</AllowUnsafeBlocks>'))
                    run(['dotnet','build',proj,'-c','Release','-o',folder/'out'],tag+'-build.log',folder)
                    builds[(stage,label,contract)]=(folder/'out/Bench.dll',folder,mode)
        # Same-assembly generic caller, paired distributions and explicit GC telemetry.
        subjects=[];adapters=[]
        for label,typ,mode in [('auto','RuntimeTagSet','TagSetStorage.Dense'),('micro','MicroBitmapSet','BitmapLayout.Dense'),('previous_avx','DenseAvxSet','BitmapLayout.Dense'),('kernel_only','FusedKernelSet','BitmapLayout.Dense'),('integrated','FusedBitmapSet','BitmapLayout.Dense')]:
            adapters.append('    static int[] Values_'+label+'('+typ+' s) { var ids=new System.Collections.Generic.List<int>(); foreach(var t in s) ids.Add(t.Id); ids.Sort(); return ids.ToArray(); }')
            subjects.append('            subjects.AddRange(Make("'+label+'",registry,x,y,r=>new '+typ+'(r,0,'+mode+'),(s,t)=>s.AddTag(t),(a,b)=>'+typ+'.Union(a,b,'+mode+'),s=>new '+typ+'(s),s=>s.Count,Values_'+label+'));')
        diag=gen/'CommonUnionBench.cs';diag.write_text((HERE/'CommonUnionBench.template.cs').read_text().replace('@@ADAPTERS@@','\n'.join(adapters)).replace('@@SUBJECTS@@','\n'.join(subjects)))
        diagnostic=build(w/'diagnostic','CommonUnionBench',common+[old['vector'],kernel,fused,HERE/'DenseFusion.cs',ROOT/'Audit~/Triad/DenseVectorKernels.cs',diag])
        order=list(builds)
        save('plan.json',{'rounds':args.rounds,'small':list(items),'large':list(big),'protocol':'Historical fixture unchanged except disclosed sizes and iterations identical to PR7. Same-assembly diagnostic is separate; no subtraction.',
            'integration':'ordered SIMD + specialized fused backend + independent fresh Dense Union path','native_unity':'not_run','dense_backend_standard21':'scalar only','selection':'constructor payload-based; no raw member ID arrays in candidates'})
        for r in range(args.rounds):
            rotated=order[r%len(order):]+order[:r%len(order)]
            if r&1:rotated.reverse()
            for stage,label,contract in rotated:
                dll,folder,mode=builds[(stage,label,contract)];tag=stage+'-'+label+'-'+contract+'-r'+str(r)
                run(['dotnet','exec',dll,out/(tag+'.json'),label,mode],tag+'.log',folder)
            run(['dotnet','exec',diagnostic,out/('common-r'+str(r)+'.json'),r],'common-r'+str(r)+'.log',w)
        tables={};digests={};raw_count=samples=prepared_samples=0
        for spec in builds:
            grouped={}
            for r in range(args.rounds):
                data=json.loads((out/('-'.join(spec)+'-r'+str(r)+'.json')).read_text());seen=set()
                for row in data['rows']:
                    if row['status']!='measured':raise RuntimeError('Invalid row '+str(row))
                    key=(row['operation'],row['size'],row['universe'],row['distribution'])
                    if key in seen:raise RuntimeError('Duplicate row')
                    seen.add(key);shape=(spec[0],)+key[1:]
                    if shape in digests and digests[shape]!=row['inputDigest']:raise RuntimeError('Unequal inputs')
                    digests[shape]=row['inputDigest']
                    if len(row['ns'])!=7 or len(row['allocated_bytes'])!=7:raise RuntimeError('Incomplete sample set')
                    if spec[2]=='prepared':
                        if any(v!=0 for v in row['allocated_bytes']):raise RuntimeError('Prepared allocation '+str(spec)+str(key))
                        prepared_samples+=7
                    raw_count+=1;samples+=7;grouped.setdefault(key,[]).append(row)
            tab={}
            for key,rows in grouped.items():
                if len(rows)!=args.rounds:raise RuntimeError('Missing round')
                med=[statistics.median(x['ns']) for x in rows]
                tab[key]={'ns':statistics.median(med),'round_medians':med,'bytes':statistics.median(b for row in rows for b in row['allocated_bytes']),'input_layout':rows[0]['input_storage'],'input_digest':rows[0]['inputDigest'],'output_layout':rows[0].get('result_storage')}
            tables[spec]=tab
        result=[];summary={}
        for (stage,label,contract),tab in tables.items():
            controls=['auto','micro'] if stage=='small' else ['auto_dense','micro_dense','previous_avx']
            hits=margin=total=0
            for key,val in sorted(tab.items()):
                refs={c:tables[(stage,c,contract)][key] for c in controls}
                win=val['ns']<min(x['ns'] for x in refs.values())
                stable=all(val['round_medians'][r]<=.95*min(x['round_medians'][r] for x in refs.values()) for r in range(args.rounds))
                if key[0] in PRIMARY:total+=1;hits+=win;margin+=stable
                result.append({'stage':stage,'variant':label,'contract':contract,'operation':key[0],'members':key[1],'definitions':key[2],'distribution':key[3],'value':val,'references':refs,'lower_than_controls':win,'margin_every_round':stable})
            summary['/'.join((stage,label,contract))]={'primary':total,'lower_medians':hits,'margin_every_round':margin,'all_pass':margin==total}
        save('comparison.json',result);save('summary.json',summary)
        with (out/'comparison.csv').open('w',newline='') as f:
            writer=csv.writer(f);writer.writerow(['stage','variant','contract','operation','members','definitions','distribution','ns','bytes','r0','r1','r2','lower_than_controls','margin_every_round'])
            for row in result:writer.writerow([row[k] for k in ('stage','variant','contract','operation','members','definitions','distribution')]+[row['value']['ns'],row['value']['bytes']]+row['value']['round_medians']+[row['lower_than_controls'],row['margin_every_round']])
        diagrows={}
        for r in range(args.rounds):
            data=json.loads((out/('common-r'+str(r)+'.json')).read_text())
            for row in data['rows']:
                key=tuple(row[k] for k in ('variant','operation','distribution','protocol'));diagrows.setdefault(key,[]).append(row)
        ds=[]
        for key,rows in sorted(diagrows.items()):
            if len(rows)!=args.rounds:raise RuntimeError('Incomplete diagnostics')
            med=[statistics.median(x['ns']) for x in rows]
            ds.append(dict(zip(('variant','operation','distribution','protocol'),key),ns=statistics.median(med),round_medians=med,bytes=statistics.median(v for row in rows for v in row['allocated_bytes']),gc_collections={g:sum(sum(row[g]) for row in rows) for g in ('gen0','gen1','gen2')}))
        save('common-summary.json',ds)
        decision={'production_promoted':False,'release_approved':False,'native_unity':'not_run','host_architecture':os.uname().machine,'container_rows':len(result),'raw_rows':raw_count,'timing_samples':samples,'zero_prepared_samples':prepared_samples,'summary':summary}
        save('decision.json',decision)
        print('FUSION_SUMMARY '+json.dumps(decision),flush=True)
        for row in result:
            if row['operation'] in PRIMARY and row['contract']=='fresh' and ((row['members']==1024 and row['definitions']==10000 and row['stage']=='large') or (row['members']==8 and row['definitions']==262144 and row['distribution']=='scattered' and row['stage']=='small')):
                print('FUSION_FOCUS '+json.dumps(row),flush=True)
        for row in ds:
            if row['operation']=='union_fresh':print('COMMON_FOCUS '+json.dumps(row),flush=True)
if __name__=='__main__':main()
