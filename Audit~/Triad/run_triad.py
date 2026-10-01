#!/usr/bin/env python3
"""Three isolated, executable experiments. No production migration; no promised speedup."""
import argparse,csv,hashlib,json,os,pathlib,statistics,subprocess,sys,tempfile
ROOT=pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Audit~'))
from integer_audit import project
from micro_pilot import fixture
AUTO='84729cc3b6e1b4681328806df2d7c8a1d8f568f6'
MICRO='9be85fcd1f8d1f594ceb5bc98f2ce234b398e0e0'
HERE=ROOT/'Audit~/Triad'

def replace_body(text,signature,body):
    if text.count(signature)!=1:raise RuntimeError('Unexpected method: '+signature)
    start=text.index('{',text.index(signature));depth=1;end=start+1
    while depth:
        depth+=(text[end]=='{')-(text[end]=='}');end+=1
    return text[:start]+'{\n'+body+'\n        }'+text[end:]

def generate(source,out):
    enum='    public enum BitmapLayout { Auto, Micro, Dense }\n'
    if source.count(enum)!=1:raise RuntimeError('Unexpected baseline enum')
    def clone(name):return source.replace(enum,'').replace('MicroBitmapSet',name)
    simd=clone('OrderedSimdBitmapSet').replace('using System;','using System;\nusing System.Numerics;',1)
    simd=replace_body(simd,'public bool HasTagExact(RuntimeTag tag)', '''            if (!ReferenceEquals(Registry, tag.Owner))
            { if (tag.Owner == null) return false; Registry.RequireSame(tag.Owner); }
            int id = tag.Id;
            if (dense != null) return (dense[id >> 6] & (1UL << (id & 63))) != 0;
            uint key = Key(id), bit = Bit(id);
            // Only inspect valid records. No padded/uninitialized lanes, no out-of-bounds loads.
            if (Vector.IsHardwareAccelerated && entries != null && used >= Vector<uint>.Count && used <= 32)
            {
                var keys = new Vector<uint>(key); var keyMask = new Vector<uint>(0xFFFF0000U);
                var target = new Vector<uint>(bit); int i = 0;
                for (; i <= used - Vector<uint>.Count; i += Vector<uint>.Count)
                {
                    var records = new Vector<uint>(entries, i);
                    var matches = Vector.Equals(records & keyMask, keys) & Vector.Equals(records & target, target);
                    if (!Vector.EqualsAll(matches, Vector<uint>.Zero)) return true;
                }
                for (; i < used; i++) if ((entries[i] & 0xFFFF0000U) == key && (entries[i] & bit) != 0) return true;
                return false;
            }
            int at = Find(key); return at >= 0 && (Read(at) & bit) != 0;''')
    files={}
    files['simd']=out/'OrderedSimdBitmapSet.cs';files['simd'].write_text(simd)
    for label,name,method in [('hardware','DenseHardwareSet','Hardware'),('vector','DenseAvxSet','VectorFused'),('twopass','DenseTwoPassSet','VectorTwoPass')]:
        t=clone(name)
        t=replace_body(t,'private static int DenseUnion(ulong[] a, ulong[] b, ulong[] output)', '            return DenseVectorKernels.'+method+'(a, b, output, 0);')
        t=replace_body(t,'private static int DenseDifference(ulong[] a, ulong[] b)', '            return DenseVectorKernels.'+method+'(a, b, a, 2);')
        files[label]=out/(name+'.cs');files[label].write_text(t)
    return files

def main():
    ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--output',required=True);ap.add_argument('--rounds',type=int,default=3)
    args=ap.parse_args()
    if args.rounds<1:ap.error('rounds must be positive')
    out=pathlib.Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True)
    gen=out/'generated';gen.mkdir(exist_ok=True)
    env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1')
    if hasattr(os,'sched_setaffinity'):os.sched_setaffinity(0,{min(os.sched_getaffinity(0))})
    def save(name,data):(out/name).write_text(json.dumps(data,ensure_ascii=False,indent=2))
    def git(*a):return subprocess.check_output(['git',*a],cwd=ROOT)
    def run(cmd,name,cwd=ROOT,extra=None):
        proc=subprocess.run(list(map(str,cmd)),cwd=cwd,env=dict(env,**(extra or {})),text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=900)
        (out/name).write_text(proc.stdout);print(proc.stdout,flush=True)
        if proc.returncode:raise RuntimeError(name+' failed with '+str(proc.returncode))
        return proc.stdout
    def build(folder,name,includes,symbol='',framework='net8.0',exe=True):
        folder.mkdir(parents=True,exist_ok=True);p=folder/(name+'.csproj')
        project(p,includes,symbol,framework=framework,executable=exe)
        p.write_text(p.read_text().replace('<Optimize>true</Optimize>','<Optimize>true</Optimize><AllowUnsafeBlocks>true</AllowUnsafeBlocks>'))
        run(['dotnet','build',p,'-c','Release','-p:BaseIntermediateOutputPath=obj-'+name+'/', '-o',folder/name],name+'-build.log',folder)
        return folder/name/(name+'.dll')
    for folder in ('Runtime','Editor','Samples~','Tests','package.json'):
        if git('diff','--name-only',AUTO,'HEAD','--',folder).strip():raise RuntimeError('Production changed: '+folder)
    source=git('show',MICRO+':Audit~/MicroBitmapSet.cs').decode()
    if source!=(ROOT/'Audit~/MicroBitmapSet.cs').read_text():raise RuntimeError('Micro baseline changed')
    original=git('show',AUTO+':Audit~/FourWay.cs').decode()
    if original!=(ROOT/'Audit~/FourWay.cs').read_text():raise RuntimeError('Original fixture changed')
    files=generate(source,gen)
    save('source-checks.json',{'head':git('rev-parse','HEAD').decode().strip(),'tree':git('rev-parse','HEAD^{tree}').decode().strip(),
        'auto':AUTO,'micro':MICRO,'production_unchanged':True,
        'generated_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in files.values()},
        'experimental_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in HERE.glob('*') if p.is_file()}})
    save('host.json',{'affinity':sorted(os.sched_getaffinity(0)) if hasattr(os,'sched_getaffinity') else None,
        'cpuinfo':pathlib.Path('/proc/cpuinfo').read_text()[:5000] if pathlib.Path('/proc/cpuinfo').exists() else '', 'python':sys.version})
    common=[ROOT/'Runtime/**/*.cs',ROOT/'Audit~/UnityStubs.cs',ROOT/'Audit~/MicroBitmapSet.cs']
    with tempfile.TemporaryDirectory(prefix='triad-') as temp:
        work=pathlib.Path(temp);(work/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
        (work/'global.json').write_text(json.dumps({'sdk':{'version':'8.0.425','rollForward':'disable'}}))
        run(['dotnet','--info'],'environment.txt',work)
        testinc=common+[HERE/'RobinMicroBitmapSet.cs',files['simd'],HERE/'DenseVectorKernels.cs',HERE/'TriadTests.cs']
        testdll=build(work/'tests','TriadTests',testinc)
        run(['dotnet','exec',testdll,out/'tests.json'],'tests.log',work)
        run(['dotnet','exec',testdll,out/'tests-nohw.json'],'tests-nohw.log',work,{'DOTNET_EnableHWIntrinsic':'0'})
        build(work/'portable','Portable',common+[HERE/'RobinMicroBitmapSet.cs',files['simd']],framework='netstandard2.1',exe=False)
        # Run the full prior suite for each source transform; no changing test semantics.
        for label,p in files.items():
            text=(ROOT/'Audit~/MicroTests.cs').read_text().replace('MicroBitmapSet',p.stem)
            test=gen/(label+'-FullTests.cs');test.write_text(text)
            dll=build(work/('full-'+label),'Full_'+label,common+[p,HERE/'DenseVectorKernels.cs',test])
            run(['dotnet','exec',dll,out/(label+'-full-tests.json')],label+'-full-tests.log',work)
        # Machine code is collected in a separate untimed run, never mixed with samples.
        run(['dotnet','exec',testdll,out/'tests-disasm.json'],'disassembly.log',work,
            {'COMPlus_JitDisasm':'GameplayTags.Experiments.DenseVectorKernels:* GameplayTags.Experiments.OrderedSimdBitmapSet:HasTagExact GameplayTags.Experiments.RobinMicroBitmapSet:HasTagExact'})
        asm=(out/'disassembly.log').read_text().lower()
        save('instruction-markers.json',{m:asm.count(m) for m in ('vpshufb','vpsadbw','vpcmpeqd','popcnt','vectorfused','hastagexact')})
        specs={
          'auto':('auto',None,'','Auto'),
          'micro':('micro',None,'','Auto'),
          'scan8':('micro',None,'MICRO_SCAN8','Auto'),
          'robin':('micro',HERE/'RobinMicroBitmapSet.cs','','Auto'),
          'simd':('micro',files['simd'],'','Auto'),
        }
        bigspec={
          'auto_auto':('auto',None,'','Auto'),
          'auto_dense':('auto',None,'','Dense'),
          'micro_swar':('micro',None,'','Dense'),
          'dense_hardware':('micro',files['hardware'],'','Dense'),
          'dense_vector':('micro',files['vector'],'','Dense'),
          'dense_twopass':('micro',files['twopass'],'','Dense'),
        }
        builds={}
        old_sizes='new[] { 0, 1, 8, 32, 128, 1024, 4096 }'
        for stage,items in [('small',specs),('large',bigspec)]:
            for variant,(kind,extra,symbol,mode) in items.items():
                for contract in ('fresh','prepared'):
                    code=fixture(original,kind,contract=='prepared')
                    if old_sizes not in code:raise RuntimeError('Fixture sizes not found')
                    code=code.replace(old_sizes,'new[] { 0, 1, 7, 8, 9, 31, 32, 33, 127, 128, 129 }' if stage=='small' else 'new[] { 128, 1024, 4096 }')
                    # Identical sample budgets for all implementations in each stage.
                    code=code.replace('Math.Max(24, Math.Min(1000, 131072 / (size + 1)))','Math.Max(500, Math.Min(20000, 1048576 / (size + 1)))')
                    if extra is not None:code=code.replace('GameplayTags.Experiments.MicroBitmapSet','GameplayTags.Experiments.'+extra.stem)
                    tag=stage+'-'+variant+'-'+contract;h=work/tag;h.mkdir();f=gen/(tag+'.cs');f.write_text(code)
                    inc=[ROOT/'Runtime/**/*.cs',ROOT/'Audit~/UnityStubs.cs',f]
                    if kind=='micro':inc.append(ROOT/'Audit~/MicroBitmapSet.cs')
                    if extra is not None:inc.append(extra)
                    if stage=='large' and extra is not None:inc.append(HERE/'DenseVectorKernels.cs')
                    dll=build(h,'Bench_'+tag.replace('-','_'),inc,'CANDIDATE;'+symbol)
                    builds[(stage,variant,contract)]=(dll,h,mode)
        kdll=build(work/'dense-kernel','DenseBench',[HERE/'DenseVectorKernels.cs',HERE/'DenseBench.cs'])
        order=list(builds)
        save('benchmark-plan.json',{'small_variants':list(specs),'large_variants':list(bigspec),'rounds':args.rounds,
            'micro_simd_changes':'Only HasTagExact changed; numeric enumeration/constructors/set operations are literal copies.',
            'dense_changes':'Only DenseUnion/DenseDifference bodies replaced; full allocating APIs are measured.',
            'robin_scope':'micro-only, unordered enumeration, 3/4 load, Fibonacci mixing, backshift deletion. No hierarchy/runtime migration.',
            'reference_scope':'Auto and Micro literal sources; not a rerun against all earlier external libraries.',
            'small_sizes':[0,1,7,8,9,31,32,33,127,128,129],'large_sizes':[128,1024,4096],
            'definitions':[10000,65536,262144],'distribution':['contiguous','scattered'],'overlap':'half input sequence; deletion existing half subset',
            'raw_kernel':'OR/AND/AND-NOT + written result + count, separate fresh/prepared/copy contracts'})
        for round in range(args.rounds):
            rotated=order[round%len(order):]+order[:round%len(order)]
            if round&1:rotated.reverse()
            for stage,variant,contract in rotated:
                dll,h,mode=builds[(stage,variant,contract)];tag=stage+'-'+variant+'-'+contract+'-r'+str(round)
                run(['dotnet','exec',dll,out/(tag+'.json'),variant,mode],tag+'.log',h)
            run(['dotnet','exec',kdll,out/('dense-kernel-r'+str(round)+'.json')],'dense-kernel-r'+str(round)+'.log',work)
        tables={};input_digests={}
        for stage,variant,contract in builds:
            grouped={}
            for round in range(args.rounds):
                data=json.loads((out/(stage+'-'+variant+'-'+contract+'-r'+str(round)+'.json')).read_text())
                seen=set()
                for row in data['rows']:
                    if row['status']!='measured':raise RuntimeError('Failed row cannot rank: '+str(row))
                    key=(row['operation'],row['size'],row['universe'],row['distribution'])
                    if key in seen:raise RuntimeError('duplicate row')
                    seen.add(key);shape=(stage,row['size'],row['universe'],row['distribution'])
                    if shape in input_digests and input_digests[shape]!=row['inputDigest']:raise RuntimeError('Unequal inputs')
                    input_digests[shape]=row['inputDigest']
                    if contract=='prepared' and any(b!=0 for b in row['allocated_bytes']):raise RuntimeError('Prepared allocated: '+str(key))
                    grouped.setdefault(key,[]).append(row)
            table={}
            for key,rows in grouped.items():
                if len(rows)!=args.rounds:raise RuntimeError('Incomplete rounds')
                medians=[statistics.median(r['ns']) for r in rows]
                table[key]={'ns':statistics.median(medians),'round_medians':medians,'bytes':statistics.median(b for r in rows for b in r['allocated_bytes']),
                    'input_layout':rows[0]['input_storage'],'buffer_bytes':rows[0]['input_member_buffer_bytes'],'input_digest':rows[0]['inputDigest']}
            tables[(stage,variant,contract)]=table
        comparison=[];summary={}
        for stage,variant,contract in builds:
            reference='auto' if stage=='small' else 'auto_dense';baseline=tables[(stage,reference,contract)]
            ordered=tables[(stage,'micro' if stage=='small' else 'micro_swar',contract)]
            wins=stable=total=0
            for key,value in sorted(tables[(stage,variant,contract)].items()):
                r=baseline[key];m=ordered[key]
                margin=all(value['round_medians'][i]<=.95*min(r['round_medians'][i],m['round_medians'][i]) for i in range(args.rounds))
                primary=key[0] in ('exact','union','copy_append','copy_remove','union_into','copy_append_reuse','copy_remove_reuse')
                if primary:total+=1;wins+=value['ns']<min(r['ns'],m['ns']);stable+=margin
                comparison.append({'stage':stage,'variant':variant,'contract':contract,'operation':key[0],'members':key[1],'definitions':key[2],'distribution':key[3],
                    'value':value,'auto_reference':r,'micro_reference':m,'lower_than_auto':value['ns']<r['ns'],'lower_than_micro':value['ns']<m['ns'],'margin_both_every_round':margin})
            summary['/'.join((stage,variant,contract))]={'primary':total,'lower_than_both':wins,'margin_both_every_round':stable,'all_primary_pass':stable==total}
        save('comparison.json',comparison);save('summary.json',summary)
        with (out/'comparison.csv').open('w',newline='') as f:
            w=csv.writer(f);w.writerow(['stage','variant','contract','operation','members','definitions','distribution','ns','bytes','auto_ns','micro_ns','input_layout','round_medians'])
            for r in comparison:w.writerow([r[k] for k in ['stage','variant','contract','operation','members','definitions','distribution']]+[r['value']['ns'],r['value']['bytes'],r['auto_reference']['ns'],r['micro_reference']['ns'],r['value']['input_layout'],json.dumps(r['value']['round_medians'])])
        kg={}
        for round in range(args.rounds):
            data=json.loads((out/('dense-kernel-r'+str(round)+'.json')).read_text())
            for r in data['rows']:kg.setdefault((r['kernel'],r['op'],r['contract'],r['words'],r['distribution']),[]).append(r)
        kernelrows=[]
        for key,rows in sorted(kg.items()):
            if len(rows)!=args.rounds:raise RuntimeError('Incomplete kernel samples')
            med=[statistics.median(r['ns']) for r in rows]
            kernelrows.append(dict(zip(['kernel','op','contract','words','distribution'],key),ns=statistics.median(med),round_medians=med,bytes=statistics.median(b for r in rows for b in r['allocated_bytes'])))
        save('dense-kernel-summary.json',kernelrows)
        decision={'production_promoted':False,'release_approved':False,'native_unity':'not_run','arm64':'not_run',
            'reason':'Isolated candidates. See complete matrices; no universal winner inferred from focused comparisons.',
            'all_historical_baselines_rerun':False,'rounds':args.rounds,'measured_comparison_rows':len(comparison),'kernel_rows':len(kernelrows)}
        save('decision.json',decision)
        print('TRIAD_SUMMARY '+json.dumps(summary),flush=True)
        for r in comparison:
            if r['contract']=='fresh' and r['operation'] in ('exact','union','copy_append','copy_remove') and ((r['stage']=='small' and r['members']==8 and r['definitions']==262144 and r['distribution']=='scattered') or (r['stage']=='large' and r['members']==1024 and r['definitions']==10000)):
                print('TRIAD_FOCUS '+json.dumps(r),flush=True)
        for r in kernelrows:
            if r['words'] in (159,4161) and r['distribution']=='random' and r['contract']=='prepared':print('KERNEL_FOCUS '+json.dumps(r),flush=True)
        print('TRIAD_DECISION '+json.dumps(decision),flush=True)
if __name__=='__main__':main()
