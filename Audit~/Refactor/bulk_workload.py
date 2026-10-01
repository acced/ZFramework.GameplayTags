#!/usr/bin/env python3
"""Actual bulk-policy lifecycle costs. Every process uses explicitly verified BatchGC."""
import argparse, hashlib, json, math, os, platform, shutil, statistics, subprocess, sys, time
from pathlib import Path
from xml.sax.saxutils import escape
HERE=Path(__file__).resolve().parent;ROOT=HERE.parent.parent
KEYS=('operation','universe','members','rightMembers','overlap','leftPattern','rightPattern','sharingPattern')
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def inventory_digest(value):return hashlib.sha256(json.dumps(value,sort_keys=True,separators=(',',':')).encode()).hexdigest()
def key(row):return tuple(row[k] for k in KEYS)
def percentile(x,p):return sorted(x)[max(0,math.ceil(len(x)*p)-1)]
OPERATIONS=('prepare.from_resolved','convert.from_existing','query.exact_mixed','query.hierarchy_hit',
    'predicate.any_exact','predicate.all_exact','predicate.any_hierarchy','predicate.all_hierarchy',
    'query.freeze','query.frozen_match','mutation.prepared_pair','bulk.union_new','bulk.union_into',
    'bulk.intersection_into','bulk.difference_reuse','bulk.append_reuse',
    'fresh.lifecycle.h0','fresh.lifecycle.h1','fresh.lifecycle.h32','existing.lifecycle.h0','existing.lifecycle.h1','existing.lifecycle.h32')
def expected_keys(suite):
    fixtures=[]
    if suite=='regression':
        for left,right in (('contiguous','scattered'),('scattered','contiguous')):
            for sharing in ('spread','suffix'):fixtures.append((65536,128,128,50,left,right,sharing))
        for n,m in ((4096,8),(8,4096)):
            for sharing in ('prefix','spread','suffix'):fixtures.append((65536,n,m,50,'contiguous','contiguous',sharing))
        for u in (65536,262144):fixtures.append((u,4096,4096,50,'contiguous','contiguous','spread'))
        for left,right in (('contiguous','scattered'),('scattered','contiguous')):
            for sharing in ('spread','suffix'):fixtures.append((262144,16384,16384,50,left,right,sharing))
        return {(op,*fixture) for fixture in fixtures for op in OPERATIONS}
    if suite=='boundary':
        for u in (65536,262144):
            registry_count=u+(u+63)//64+1;words=(registry_count+63)//64
            sizes=sorted({max(8,min(u//2,multiplier*words+delta)) for multiplier in (2,4,8,16) for delta in (-1,1)})
            for n in sizes:
                for left,right in (('contiguous','contiguous'),('scattered','scattered'),('contiguous','scattered')):
                    for sharing in ('spread','suffix'):fixtures.append((u,n,n,50,left,right,sharing))
        operations=[op for op in OPERATIONS if op.startswith(('fresh.','existing.','prepare.','convert.'))]
        return {(op,*fixture) for fixture in fixtures for op in operations}
    for u in (65536,262144):
        sizes=((128,1024,4096) if u==65536 else (128,4096,16384)) if suite=='smoke' else (8,64,128,1024,4096,16384)
        patterns=(('contiguous','contiguous'),('scattered','scattered'),('contiguous','scattered')) if suite=='smoke' else (('contiguous','contiguous'),('clusters4','clusters4'),('scattered','scattered'),('contiguous','scattered'),('scattered','contiguous'))
        for n in sizes:
            for left,right in patterns:
                for overlap in (50,) if suite=='smoke' else (0,50,100):fixtures.append((u,n,n,overlap,left,right,'spread'))
    for n,m in ((4096,8),(8,4096)):
        for pattern in ('contiguous','scattered'):
            for sharing in ('prefix','spread','suffix'):fixtures.append((65536,n,m,50,pattern,pattern,sharing))
    return {(op,*fixture) for fixture in fixtures for op in OPERATIONS}
def validate_capabilities(payload,implementation):
    fields=('vectorHardwareAccelerated','vectorIntWidth','denseWordsPerPackedRecord','portable_backend_scope')
    if any(field not in payload for field in fields):raise ValueError('Missing backend capability metadata')
    capability={field:payload[field] for field in fields}
    if type(capability['vectorHardwareAccelerated']) is not bool:raise ValueError('Invalid hardware capability boolean')
    width=capability['vectorIntWidth']
    if type(width) is not int or width<=0 or width&(width-1):raise ValueError('Invalid vector width capability')
    ratio=capability['denseWordsPerPackedRecord']
    if implementation=='current':
        if type(ratio) is not int or ratio<=0:raise ValueError('Invalid current policy capability ratio')
    elif ratio is not None:raise ValueError('Unexpected policy capability for reference implementation')
    if type(capability['portable_backend_scope']) is not str or not capability['portable_backend_scope'].strip():raise ValueError('Invalid portable backend capability scope')
    return capability

def summarize(out,jobs,args):
    grouped={};expected=expected_keys(args.suite);digests={};cohort=None;failures=[];capabilities={};capability_cohorts={}
    manifest=json.loads((out/'manifest.json').read_text()) if (out/'manifest.json').exists() else None
    requires_attribution=bool(manifest and manifest.get('attribution_protocol')=='source-and-executable-v1')
    if getattr(args,'current_aa',False) and not requires_attribution:raise ValueError('A/A requires source/executable attribution manifest')
    if requires_attribution:
        for flavor in ('hardware','portable'):
            base='current-'+flavor+'-Auto';alias=base+'-AA'
            if alias in manifest['job_attribution'] and manifest['job_attribution'][alias]!=manifest['job_attribution'][base]:
                raise ValueError('A/A labels must use the identical executable and source snapshot')
    for label,implementation,flavor,mode in jobs:
        grouped[label]={}
        for round_id in range(args.rounds):
            payload=json.loads((out/f'{label}-r{round_id}.json').read_text())
            wanted={'label':label,'round':round_id,'implementation':implementation,'strategy':mode,'suite':args.suite,'samples':args.samples,'targetMs':args.target_ms,'seed':20261029 if args.suite=='boundary' else 20261001,'gcMode':'Batch','portableKernelsForced':flavor=='portable','validation_protocol':'actual-timed-output-before-reset-BatchGC-v1'}
            for k,v in wanted.items():
                if payload.get(k)!=v:raise ValueError(f'{label}: {k} mismatch')
            if requires_attribution or payload.get('attribution_protocol')=='source-and-executable-v1':
                if not requires_attribution:raise ValueError('Missing source/executable attribution manifest')
                if payload.get('attribution_protocol')!='source-and-executable-v1':raise ValueError('Missing source/executable attribution payload')
                for field,value in manifest['job_attribution'][label].items():
                    if payload.get(field)!=value:raise ValueError(f'{label}: {field} attribution mismatch')
            capability=validate_capabilities(payload,implementation) if requires_attribution else {k:payload.get(k) for k in ('vectorHardwareAccelerated','vectorIntWidth','denseWordsPerPackedRecord','portable_backend_scope')}
            if requires_attribution:
                backend_key=(implementation,flavor)
                if backend_key in capability_cohorts and capability_cohorts[backend_key]!=capability:
                    raise ValueError('Backend capability mismatch across identical implementation/backend labels, including Auto A/A')
                capability_cohorts[backend_key]=capability
            if label in capabilities and capabilities[label]!=capability:raise ValueError('Backend capability/policy changed within label')
            capabilities[label]=capability
            identity=(payload['runtime'],payload['architecture'],payload['serverGC'],payload.get('workload_protocol','handle-export-v1'))
            if cohort is None:cohort=identity
            if identity!=cohort:raise ValueError('Mixed runtime/architecture/GC cohort')
            control=payload['controls']
            for name in ('negative_copy_bytes','positive_new_array_bytes'):
                value=control[name]
                if type(value) is not int or not 0<=value<=(1<<63)-1:raise ValueError('Allocation controls must be finite nonnegative integer byte counts')
            if control['negative_copy_bytes']!=0 or control['positive_new_array_bytes']<4096:raise ValueError('Allocation controls failed')
            rows=payload['rows']
            if not rows:raise ValueError('Empty benchmark matrix')
            keys=[key(r) for r in rows]
            if len(keys)!=len(set(keys)):raise ValueError('Duplicate row')
            if set(keys)!=expected:raise ValueError('Missing or extra row')
            observed_failures=0
            for row in rows:
                k=key(row)
                if k in digests and digests[k]!=row['digest']:raise ValueError('Inputs differ')
                digests[k]=row['digest'];grouped[label].setdefault(k,[]).append(row)
                if row['status']!='measured':
                    observed_failures+=1;failures.append({'variant':label,'round':round_id,'key':k,'error':row.get('error')});continue
                for field in ('ns','allocated_bytes','returned_values','elapsed_ticks','batch_ms','gc_collections'):
                    if len(row[field])!=args.samples:raise ValueError('Sample count mismatch')
                if row.get('completed_samples')!=args.samples:raise ValueError('Incomplete measured row')
                if any(type(row[field]) is not int or row[field]<1 for field in ('iterations','units')):raise ValueError('Invalid normalization denominator')
                if any(type(value) is not int or not -(1<<31)<=value<(1<<31) for value in row['returned_values']):raise ValueError('Returned checksums must be bounded signed integer values')
                if any(type(value) is not int or not 0<value<(1<<63) for value in row['elapsed_ticks']):raise ValueError('Invalid elapsed tick values')
                if any(type(value) not in (int,float) or not math.isfinite(value) or value<=0 for value in row['batch_ms']):raise ValueError('Invalid batch duration')
                if any(type(value) is not list or len(value)!=3 or any(type(count) is not int or not 0<=count<(1<<31) for count in value) for value in row['gc_collections']):raise ValueError('Invalid GC collection counts')
                for ns,ticks,ms in zip(row['ns'],row['elapsed_ticks'],row['batch_ms']):
                    recomputed=ticks*1e9/payload['stopwatchFrequency']/(row['iterations']*row['units'])
                    if not math.isclose(ns,recomputed,rel_tol=1e-12,abs_tol=1e-9) or not math.isclose(ms,ticks*1000/payload['stopwatchFrequency'],rel_tol=1e-12,abs_tol=1e-9):raise ValueError('Timing normalization mismatch')
                if row.get('timed_output_validated') is not True:raise ValueError('Actual output not validated')
                if any(type(x) not in (int,float) or not math.isfinite(x) or x<=0 for x in row['ns']):raise ValueError('Invalid time')
                if any(type(x) not in (int,float) or not math.isfinite(x) or x<0 for x in row['allocated_bytes']):raise ValueError('Invalid allocation')
                if type(row.get('zero_allocation_required')) is not bool:raise ValueError('Invalid zero-allocation requirement boolean')
                if row['zero_allocation_required'] and any(x!=0 for x in row['allocated_bytes']):raise ValueError('Zero-allocation row allocated')
            if payload['failures']!=observed_failures:raise ValueError('Failure inventory mismatch')
    comparison=[]
    for k in sorted(expected):
        entry={**dict(zip(KEYS,k)),'digest':digests[k],'variants':{},'ratios':{}}
        for label,_,_,_ in jobs:
            rows=grouped[label][k]
            if any(r['status']!='measured' for r in rows):entry['variants'][label]={'status':'failed'};continue
            times=[x for r in rows for x in r['ns']]
            entry['variants'][label]={'status':'measured','median_ns':statistics.median(statistics.median(r['ns']) for r in rows),
                'round_medians_ns':[statistics.median(r['ns']) for r in rows], 'round_p95_batch_mean_ns':[percentile(r['ns'],.95) for r in rows], 'round_max_batch_mean_ns':[max(r['ns']) for r in rows], 'p95_batch_mean_ns':percentile(times,.95),'max_batch_mean_ns':max(times),
                'allocated_bytes':statistics.median(statistics.median(r['allocated_bytes']) for r in rows),'details':rows[0]['details'],'scope':rows[0]['scope']}
        for flavor in ('hardware','portable'):
            policy='current-'+flavor+'-Bulk'
            if policy not in entry['variants']:continue
            for baseline in ('current-'+flavor+'-Auto','original-'+flavor+'-Auto','previous-'+flavor+'-Auto'):
                if baseline in entry['variants'] and entry['variants'][policy]['status']==entry['variants'][baseline]['status']=='measured':
                    entry['ratios'][policy+'/'+baseline]=entry['variants'][policy]['median_ns']/entry['variants'][baseline]['median_ns']
        entry['aa_ratios']={}
        for flavor in ('hardware','portable'):
            base='current-'+flavor+'-Auto';alias=base+'-AA'
            if alias in entry['variants'] and entry['variants'][alias]['status']==entry['variants'][base]['status']=='measured':
                a=entry['variants'][alias];b=entry['variants'][base]
                entry['aa_ratios'][alias+'/'+base]={'median_ratio':a['median_ns']/b['median_ns'],
                    'paired_round_ratios':[x/y for x,y in zip(a['round_medians_ns'],b['round_medians_ns'])]}
        comparison.append(entry)
    (out/'comparison.json').write_text(json.dumps(comparison,indent=2)+'\n')
    summary={'status':'failed' if failures else 'passed','failures':failures,'runtime':cohort[0],'architecture':cohort[1],
        'study_role':'predeclared boundary holdout' if args.suite=='boundary' else 'fixed regression suite' if args.suite=='regression' else 'development matrix','workload_protocol':cohort[3],'backend_capabilities':capabilities,'gc_mode':'Batch','gc_note':'DOTNET_gcConcurrent=0 uniformly; a measurement mitigation, not a production GC recommendation',
        'rounds':args.rounds,'samples':args.samples,'rows_per_process':len(expected),'release_approved':False,'unity_il2cpp':'not_run','policy_groups':{}}
    for flavor in ('hardware','portable'):
        for stage in ('fresh.lifecycle.h0','fresh.lifecycle.h1','fresh.lifecycle.h32','existing.lifecycle.h0','existing.lifecycle.h1','existing.lifecycle.h32'):
            name='current-'+flavor+'-Bulk/current-'+flavor+'-Auto'
            selected=[r for r in comparison if r['operation']==stage and name in r['ratios']]
            if not selected:continue
            ratios=[r['ratios'][name] for r in selected]
            worst=sorted(selected,key=lambda r:r['ratios'][name],reverse=True)[:12]
            summary['policy_groups'][flavor+'/'+stage]={'rows':len(ratios),'median_ratio':statistics.median(ratios),'p95_ratio':percentile(ratios,.95),'max_ratio':max(ratios),
                'wins_by_5_percent':sum(r<=.95 for r in ratios),'losses_over_5_percent':sum(r>1.05 for r in ratios),
                'worst':[{**{k:r[k] for k in KEYS},'ratio':r['ratios'][name], 'bulk_ns':r['variants']['current-'+flavor+'-Bulk']['median_ns'],
                    'auto_ns':r['variants']['current-'+flavor+'-Auto']['median_ns'], 'bulk_bytes':r['variants']['current-'+flavor+'-Bulk']['allocated_bytes'],
                    'auto_bytes':r['variants']['current-'+flavor+'-Auto']['allocated_bytes']} for r in worst]}
    summary['aa_controls']={}
    for flavor in ('hardware','portable'):
        name='current-'+flavor+'-Auto-AA/current-'+flavor+'-Auto'
        if not any(job[0]=='current-'+flavor+'-Auto-AA' for job in jobs):continue
        groups={}
        for op in ('all_rows',*OPERATIONS):
            intended=[r for r in comparison if op=='all_rows' or r['operation']==op]
            eligible=[r for r in intended if name in r['aa_ratios']]
            ratios=[r['aa_ratios'][name]['median_ratio'] for r in eligible]
            paired=[v for r in eligible for v in r['aa_ratios'][name]['paired_round_ratios']]
            groups[op]={'expected_rows':len(intended),'measured_rows':len(eligible),
                'failed_keys':[{k:r[k] for k in KEYS} for r in intended if name not in r['aa_ratios']],
                'median_ratio':statistics.median(ratios) if ratios else None,'min_ratio':min(ratios) if ratios else None,
                'p95_ratio':percentile(ratios,.95) if ratios else None,'max_ratio':max(ratios) if ratios else None,
                'paired_round_ratio_p95':percentile(paired,.95) if paired else None,'paired_round_ratio_max':max(paired) if paired else None,
                'worst':[{**{k:r[k] for k in KEYS},**r['aa_ratios'][name]} for r in sorted(eligible,key=lambda r:r['aa_ratios'][name]['median_ratio'],reverse=True)[:12]]}
        summary['aa_controls'][flavor]={'scope':'Independent fresh processes execute byte-identical current Auto DLL and source snapshot under separate labels. Ratios are AA/Auto, no subtraction or adjustment. Raw batch means, process medians and p95/max remain in payloads/comparison.json; failed keys remain explicit.','groups':groups}
    (out/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
    lines=['# Actual bulk-policy lifecycle comparison','',f"{cohort[0]}, {cohort[1]}, BatchGC; {args.rounds} fresh process rounds, {args.samples} raw samples per row.",'',
        'Inputs are resolved handles sorted/deduplicated outside timing: ordered-loading evidence only.',f'Existing-conversion protocol: {cohort[3]}. Historical handle-export and direct-conversion lifecycles are distinct workflows.','',
        'Ratios compare production bulk preparation with ordinary current Auto, using the same inputs. Preparation/conversion and initial output allocation are included in lifecycle rows. Ratios <1 favor the bulk policy. All losses remain visible. No row-specific winner chooses the policy.','',
        '| group | rows | median ratio | p95 ratio | worst ratio | >5% loss rows |','|---|---:|---:|---:|---:|---:|']
    for name,v in summary['policy_groups'].items():lines.append(f"| {name} | {v['rows']} | {v['median_ratio']:.3f} | {v['p95_ratio']:.3f} | {v['max_ratio']:.3f} | {v['losses_over_5_percent']} |")
    if summary['aa_controls']:
        lines+=['','## Same-executable current Auto A/A controls','','Independent labels launch the exact same executable for each backend. Ratios are AA/Auto, without noise subtraction. All raw tails and failed rows remain visible.','',
            '| backend / operation | measured / expected | median ratio | p95 ratio | max ratio | paired-round p95 | paired-round max |','|---|---:|---:|---:|---:|---:|---:|']
        for flavor,control in summary['aa_controls'].items():
            for op,v in control['groups'].items():
                vals=[f"{v[k]:.3f}" if v[k] is not None else 'failed' for k in ('median_ratio','p95_ratio','max_ratio','paired_round_ratio_p95','paired_round_ratio_max')]
                lines.append('| '+' | '.join([flavor+'/'+op,str(v['measured_rows'])+'/'+str(v['expected_rows'])]+vals)+' |')
    lines+=['','## Every lifecycle comparison','', '| operation | U | N | requested patterns | overlap | hardware bulk/Auto | portable bulk/Auto |','|---|---:|---:|---|---:|---:|---:|']
    for r in comparison:
        if '.lifecycle.' not in r['operation']:continue
        ratios=[str(round(r['ratios']['current-'+f+'-Bulk/current-'+f+'-Auto'],4)) if 'current-'+f+'-Bulk/current-'+f+'-Auto' in r['ratios'] else 'not run' for f in ('hardware','portable')]
        lines.append('| '+' | '.join([r['operation'],str(r['universe']),str(r['members'])+':'+str(r['rightMembers']),r['leftPattern']+'/'+r['rightPattern']+'/'+r['sharingPattern'],str(r['overlap'])]+ratios)+' |')
    lines+=['','Raw JSON records all operand/output storage modes, payload bytes, member/reserved capacities, physical record counts/capacities, managed allocation, GC counts and every actual-output-validated sample. Batch-mean tails are not individual-operation latency. No Unity/IL2CPP/mobile evidence or release approval.']
    (out/'REPORT.md').write_text('\n'.join(lines)+'\n')
    return summary

def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--original',required=True,type=Path);ap.add_argument('--current',type=Path,default=ROOT);ap.add_argument('--previous',type=Path)
    ap.add_argument('--output',required=True,type=Path);ap.add_argument('--dotnet',default='dotnet');ap.add_argument('--rounds',type=int,default=3)
    ap.add_argument('--samples',type=int,default=7);ap.add_argument('--target-ms',type=float,default=.5);ap.add_argument('--suite',choices=('smoke','full','boundary','regression'),default='smoke')
    ap.add_argument('--current-aa',action='store_true',help='Independent labels reuse the byte-identical current Auto executable on each backend');
    ap.add_argument('--portable',action='store_true');ap.add_argument('--original-forced',action='store_true');ap.add_argument('--summarize-only',action='store_true')
    args=ap.parse_args()
    if args.rounds<1 or args.samples<3 or args.target_ms<=0:ap.error('Invalid sampling')
    out=args.output.resolve();out.mkdir(parents=True,exist_ok=True)
    roots={'original':args.original.resolve(),'current':args.current.resolve()}
    if args.previous:roots['previous']=args.previous.resolve()
    flavors=('hardware','portable') if args.portable else ('hardware',)
    jobs=[]
    for flavor in flavors:
        for name in roots:
            modes=('Auto','Bulk','Sparse','Dense','Compressed') if name=='current' else ('Auto','Sparse','Dense') if args.original_forced else ('Auto',)
            for mode in modes:jobs.append((name+'-'+flavor+'-'+mode,name,flavor,mode))
        if args.current_aa:jobs.append(('current-'+flavor+'-Auto-AA','current',flavor,'Auto'))
    if args.summarize_only:return 1 if summarize(out,jobs,args)['failures'] else 0
    if (out/'manifest.json').exists():ap.error('Preserve evidence; choose a new output directory')
    env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_ReadyToRun='0',DOTNET_gcConcurrent='0',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1')
    env.setdefault('DOTNET_CLI_HOME',str(out/'dotnet-home'))
    affinity={'supported':hasattr(os,'sched_setaffinity')}
    if affinity['supported']:
        before=sorted(os.sched_getaffinity(0));os.sched_setaffinity(0,{before[0]});affinity.update(previous=before,selected=before[0],actual=sorted(os.sched_getaffinity(0)))
    def run(cmd,name,allow_failure=False):
        print('RUN',name,flush=True);start=time.time();p=subprocess.run(list(map(str,cmd)),cwd=ROOT,env=env,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=3600)
        (out/(name+'.log')).write_text(p.stdout);print(p.stdout[-1500:],flush=True);print('SECONDS',round(time.time()-start,2),flush=True)
        if p.returncode and not allow_failure:raise RuntimeError(name+' failed '+str(p.returncode))
        return p.stdout
    sdk=run([args.dotnet,'--version'],'dotnet-version').strip();run([args.dotnet,'--info'],'dotnet-info')
    sources=out/'sources';sources.mkdir();shutil.copy2(HERE/'BulkWorkload.cs',sources/'BulkWorkload.cs');shutil.copy2(__file__,sources/'bulk_workload.py')
    if (HERE/'BulkBoundaryPlan.json').exists():shutil.copy2(HERE/'BulkBoundaryPlan.json',sources/'BulkBoundaryPlan.json')
    shutil.copy2(ROOT/'Audit~/UnityStubs.cs',sources/'UnityStubs.cs');shutil.copy2(ROOT/'Audit~/NuGet.Config',sources/'NuGet.Config')
    source_inventory={};builds={}
    for name,root in roots.items():
        snapshot=sources/name;shutil.copytree(root/'Runtime',snapshot/'Runtime')
        source_inventory[name]={str(p.relative_to(snapshot)):sha(p) for p in sorted(snapshot.rglob('*')) if p.is_file()}
        for flavor in flavors:
            build=out/'build'/(name+'-'+flavor);build.mkdir(parents=True)
            symbols=[]
            if name in ('current','previous'):symbols.append('PREPARED_API')
            if name=='current':symbols.append('BULK_API')
            if flavor=='portable':symbols.append('GAMEPLAYTAGS_FORCE_PORTABLE')
            files=[snapshot/'Runtime/**/*.cs',sources/'UnityStubs.cs',sources/'BulkWorkload.cs']
            project=build/'Bulk.csproj';project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>9.0</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><AllowUnsafeBlocks>false</AllowUnsafeBlocks><DefineConstants>'+escape(';'.join(symbols))+'</DefineConstants></PropertyGroup><ItemGroup>'+''.join('<Compile Include="'+escape(str(p))+'"/>' for p in files)+'</ItemGroup></Project>')
            run([args.dotnet,'build',project,'-c','Release','--configfile',sources/'NuGet.Config','-o',build/'bin'],name+'-'+flavor+'-build')
            builds[name+'-'+flavor]=build/'bin/Bulk.dll'
    job_attribution={}
    for label,name,flavor,mode in jobs:
        job_attribution[label]={'executablePath':str(builds[name+'-'+flavor]),'executableSha256':sha(builds[name+'-'+flavor]),
            'runtimeSourceRoot':str(sources/name),'sourceInventorySha256':inventory_digest(source_inventory[name]),
            'harnessSha256':sha(sources/'BulkWorkload.cs'),'runnerSha256':sha(sources/'bulk_workload.py')}
    manifest={'attribution_protocol':'source-and-executable-v1','job_attribution':job_attribution,'source_roots':{name:str(root) for name,root in roots.items()},'source_files':source_inventory,'harness_sha256':sha(sources/'BulkWorkload.cs'),'runner_sha256':sha(sources/'bulk_workload.py'),'sdk':sdk,
        'environment':{k:env[k] for k in ('DOTNET_TieredCompilation','DOTNET_ReadyToRun','DOTNET_gcConcurrent')},'cpu_affinity':affinity,'platform':platform.platform(),'command':sys.argv,'jobs':jobs}
    (out/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    for round_id in range(args.rounds):
        order=jobs[round_id%len(jobs):]+jobs[:round_id%len(jobs)]
        if round_id%2:order=list(reversed(order))
        for label,name,flavor,mode in order:
            attribution=job_attribution[label]
            run([args.dotnet,'exec',builds[name+'-'+flavor],out/f'{label}-r{round_id}.json',name,mode,args.suite,args.samples,args.target_ms,label,round_id,
                attribution['runtimeSourceRoot'],attribution['sourceInventorySha256'],attribution['harnessSha256'],attribution['runnerSha256']],label+'-r'+str(round_id),True)
    result=summarize(out,jobs,args)
    manifest['observed_backend_capabilities']=result['backend_capabilities']
    (out/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    print(json.dumps({k:{a:b for a,b in v.items() if a!='worst'} for k,v in result['policy_groups'].items()},indent=2))
    return 1 if result['failures'] else 0
if __name__=='__main__':raise SystemExit(main())
