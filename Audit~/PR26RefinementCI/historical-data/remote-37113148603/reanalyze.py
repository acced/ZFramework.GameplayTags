#!/usr/bin/env python3
"""Recompute published PR26 remote timing results from retained raw archives.

Python 3.9+, standard library only. No extraction, .NET, network, subprocess,
original-workspace reads or writes. This verifies the retained raw subset and
statistics; omitted historical binaries/source/build dependencies are NOT
revalidated. Their historical audit status is a receipt, not a fresh proof.
"""
import sys
sys.dont_write_bytecode = True
import argparse,hashlib,io,json,tarfile
from pathlib import Path
import statistics_methods as methods

ROOT=Path(__file__).resolve().parent
RUN='37113148603'
COMMIT='fc555ca1ce637c5d04274945ce90734afc89f2f1'
LIMIT='Retained raw-data and statistical integrity only; omitted binaries and full source/compiler/dependency provenance are not revalidated. New Linux hosted-VM .NET8 x64/ARM64 observations remain separate from historical local data and do not establish native Unity, Mono, IL2CPP, Burst or device performance.'

def require(v,message):
    if not v:raise ValueError(message)

def digest(data):return hashlib.sha256(data).hexdigest()

def read_json(data,label):return methods.load_json(data,label)

class RawArchives:
    def __init__(self,root,inventory_name="raw-inventory.json"):
        self.root=root
        inv=read_json((root/inventory_name).read_bytes(),inventory_name)
        self.inventory=inv;self.data={};self.bytes=0
        expected=inv['files'];seen=set()
        for name,meta in inv['archives'].items():
            methods.safe_relative(name,'Archive')
            require('/' not in name and name.endswith('.tar.gz'),'Invalid archive name')
            path=root/name;require(not path.is_symlink(),'Symlink archive')
            data=path.read_bytes()
            require(len(data)==meta['size'] and digest(data)==meta['sha256'],'Raw archive bytes differ: '+name)
            count=0
            with tarfile.open(fileobj=io.BytesIO(data),mode='r:gz') as archive:
                for member in archive:
                    methods.safe_relative(member.name,'Tar member')
                    require(member.isfile() and member.name not in seen and member.name in expected,'Unsafe/unexpected/duplicate raw member')
                    entry=expected[member.name]
                    require(entry['archive']==name and entry['member']==member.name,'Raw member mapping differs')
                    b=archive.extractfile(member).read()
                    require(len(b)==entry['size']==member.size and digest(b)==entry['sha256'],'Raw member bytes differ: '+member.name)
                    self.data[member.name]=b;seen.add(member.name);count+=1;self.bytes+=len(b)
            require(count==meta['file_count'],'Raw archive count differs')
        require(seen==set(expected),'Missing raw inventory members')
        self.artifacts=inv['artifacts']

class Shard(methods.ArchivedEvidence):
    def __init__(self,raw,name):
        self.raw=raw;self.name=name;self.verified_raw_files=0;self.verified_processes=0
    def read(self,key):
        candidates=['pr26-refinement/'+key,self.name+'/'+key]
        matches=[key for key in candidates if key in self.raw.data]
        require(len(matches)==1,'Missing/ambiguous retained data: '+key)
        return self.raw.data[matches[0]]

class Derived:
    def __init__(self,root):
        inv=read_json((root/'derived-inventory.json').read_bytes(),'derived-inventory.json')
        name=inv['archive'];methods.safe_relative(name,'Derived archive');require('/' not in name,'Unsafe derived archive')
        data=(root/name).read_bytes()
        require(len(data)==inv['archive_size'] and digest(data)==inv['archive_sha256'],'Derived archive bytes differ')
        self.data={}
        with tarfile.open(fileobj=io.BytesIO(data),mode='r:gz') as archive:
            for m in archive:
                methods.safe_relative(m.name,'Derived member')
                require(m.isfile() and m.name not in self.data and m.name in inv['files'],'Unsafe/unexpected/duplicate derived member')
                b=archive.extractfile(m).read();entry=inv['files'][m.name]
                require(len(b)==entry['size']==m.size and digest(b)==entry['sha256'],'Derived member bytes differ')
                self.data[m.name]=b
        require(set(self.data)==set(inv['files']),'Missing derived members')
    def read(self,key):return self.data[key]



def correctness_records(root):
    archived=RawArchives(root,'correctness-inventory.json')
    require(len(archived.artifacts)==6,'Incomplete correctness artifacts')
    jobs=[];all_pins=[]
    for artifact_id,meta in archived.artifacts.items():
        def original(name):
            found=[member for member,entry in archived.inventory['files'].items()
                   if str(entry['artifact_id'])==str(artifact_id) and entry['original_member']==name]
            require(len(found)==1,'Missing/duplicate correctness receipt: '+name)
            return read_json(archived.data[found[0]],name)
        receipt=original('run-receipt.json');args=receipt['arguments']
        require(receipt['status']=='passed' and receipt['command']=='correctness','Failed/incomplete correctness receipt')
        require(receipt['github']['GITHUB_SHA']==COMMIT and receipt['github']['GITHUB_RUN_ID']==RUN and receipt['github']['GITHUB_RUN_ATTEMPT']=='1','Wrong correctness run identity')
        require(receipt['sdk']=='8.0.425','Wrong recorded correctness SDK')
        configuration=args['configuration'];arch=args['expect_arch']
        require(configuration in ['net8-normal','net8-checked','netstandard-checked'] and arch in ['x64','arm64'],'Unexpected correctness configuration')
        for backend in ['default','nohw']:
            features=original('features/'+backend+'.json')
            require(features['process_architecture'].lower()==arch and features['os_architecture'].lower()==arch,'Wrong recorded correctness process architecture')
            if backend=='nohw':require(not any(features[k] for k in ['vector_accelerated','avx2','popcnt','popcnt_x64','advsimd','advsimd_arm64']),'Incorrect nohw feature receipt')
        process_rows=[];summary_paths=[]
        for member,entry in archived.inventory['files'].items():
            source=entry['original_member']
            if str(entry['artifact_id'])!=str(artifact_id) or not source.startswith('pr26-refinement/independent-tests/integration-results/') or not source.endswith('/summary.json'):continue
            data=read_json(archived.data[member],source)
            require(isinstance(data,list) and data and all(x['exit']==0 for x in data),'Failed/incomplete correctness process summary')
            process_rows.extend(data);summary_paths.append(source)
        require(len(process_rows)==(32 if configuration=='net8-normal' else 18),'Wrong correctness process coverage')
        all_pins.append(receipt['identity'])
        jobs.append({'artifact_id':str(artifact_id),'architecture':arch,'configuration':configuration,
                     'processes':len(process_rows),'status':'recorded_pass','summary_members':summary_paths})
    require({(x['architecture'],x['configuration']) for x in jobs}=={(a,c) for a in ['x64','arm64'] for c in ['net8-normal','net8-checked','netstandard-checked']},'Missing correctness coordinates')
    require(sum(x['processes'] for x in jobs)==136 and all(x==all_pins[0] for x in all_pins),'Correctness count/source-pin consistency differs')
    return {'artifacts':6,'recorded_processes':136,'retained_files':len(archived.data),'retained_bytes':archived.bytes,
            'jobs':sorted(jobs,key=lambda x:(x['architecture'],x['configuration'])),'recorded_source_pins':all_pins[0],
            'scope':'Original nonbinary correctness records only; no tests rerun and omitted DLL bytes not revalidated'}


def reanalyze(root):
    raw=RawArchives(root);derived=Derived(root);correctness=correctness_records(root)
    source_pins=read_json((root/'SOURCE_PINS.json').read_bytes(),'SOURCE_PINS.json')
    require(source_pins['pins']==correctness['recorded_source_pins'],'Published source-pin receipt differs')
    compact=read_json((root/'results-summary.json').read_bytes(),'results-summary.json')
    require(str(compact['run_id'])==RUN and compact['commit']==COMMIT and compact['run_attempt']==1,'Wrong published run identity')
    expected={f'ci-{a}-{g}-{b}' for a in ['x64','arm64'] for g in ['query','builder','mixed','bridge'] for b in ['default','nohw']}
    require(set(compact['shards'])==expected and len(raw.artifacts)==16,'Incomplete shard set')
    summaries={};dimensions={};raw_count=process_count=0
    for artifact_id,meta in raw.artifacts.items():
        name=meta['shard_name'];require(name in expected and name not in summaries,'Unexpected/duplicate shard')
        evidence=Shard(raw,name);receipt=evidence.json('run-receipt.json');args=receipt['arguments']
        arch,group,backend=args['expect_arch'],args['group'],args['backend']
        require(name==f'ci-{arch}-{group}-{backend}' and receipt['command']=='benchmark' and receipt['status']=='passed','Wrong shard receipt')
        require(receipt['github']['GITHUB_SHA']==COMMIT and receipt['github']['GITHUB_RUN_ID']==RUN and receipt['github']['GITHUB_RUN_ATTEMPT']=='1','Wrong recorded CI identity')
        require(receipt['sdk']=='8.0.425','Wrong recorded SDK')
        require(receipt['identity']==correctness['recorded_source_pins'],'Correctness/performance source pins differ')
        features=evidence.json('features/'+backend+'.json')
        require(features['process_architecture'].lower()==arch and features['os_architecture'].lower()==arch,'Wrong recorded process architecture')
        if backend=='nohw':
            require(not any(features[k] for k in ['vector_accelerated','avx2','popcnt','popcnt_x64','advsimd','advsimd_arm64']),'Wrong hardware-disabled controls')
        manifest=evidence.json('benchmark/results/'+name+'/manifest.json')
        for process in manifest['runs']:
            data=evidence.json('benchmark/results/'+name+'/'+process['label']+'.json')
            if 'architecture' not in data:
                require(not process['label'].startswith('r'),'Timed process omitted architecture')
                continue
            require(data['architecture'].lower()==features['process_architecture'].lower() and data['runtime']==features['runtime'],'Raw process architecture/runtime differs from feature receipt')
            for key in ['vector_accelerated','avx2','vector_uint_lanes']:
                if key in data:require(data[key]==features[key],'Raw process feature differs: '+key)
            if 'hw_intrinsic' in data:require(data['hw_intrinsic']==features['dotnet_enable_hw_intrinsic'],'Raw hardware control differs from feature receipt')
        summary,dims=(methods.factor_summary(evidence,name) if group=='bridge' else methods.broad_summary(evidence,name))
        methods.compare(summary,read_json(derived.read('results/'+name+'.json'),name+' derived JSON'),'Derived '+name)
        # The CSV comparator expects root/summary.csv; adapt only that read key.
        class CSVView:
            def read(self,key):
                require(key==name+'/summary.csv','Unexpected derived CSV key')
                return derived.read('results/'+name+'.csv')
        methods.compare_csv(CSVView(),name,summary['rows'])
        shard=compact['shards'][name]
        require(str(shard['artifact_id'])==str(artifact_id) and shard['outer_sha256']==meta['outer_sha256'] and shard['inner_sha256']==meta['inner_sha256'],'Published artifact identity differs')
        methods.compare(summary['counts'],shard['counts'],'Published counts '+name)
        methods.compare(dims,shard['dimensions'],'Published dimensions '+name)
        require(len(summary['rows'])==shard['cells'],'Published cell count differs')
        primary='v3_verdict' if group=='bridge' else 'verdict'
        rows=lambda value:[{k:v for k,v in x.items() if k!='rounds'} for x in value]
        methods.compare(rows([x for x in summary['rows'] if x[primary]=='clear-regression']),shard['selected_vs_baseline_regressions'],'Published regression rows '+name)
        methods.compare(rows([x for x in summary['rows'] if not x['valid']]),shard['invalid_rows'],'Published invalid rows '+name)
        if group=='bridge':
            methods.compare(rows([x for x in summary['rows'] if x['versus_v2_verdict']=='clear-regression']),shard['split_vs_old_direct_regressions'],'Published split/old losses '+name)
            methods.compare(rows([x for x in summary['rows'] if x['v2_verdict']=='clear-regression']),shard['old_direct_vs_legacy_regressions'],'Published old/legacy losses '+name)
        candidate_field='v3_bytes' if group=='bridge' else 'candidate_bytes'
        allocation_rows=lambda predicate:[{k:x[k] for k in ['layout','id','baseline_bytes',candidate_field]} for x in summary['rows'] if predicate(x)]
        reductions=allocation_rows(lambda x:x[candidate_field]<x['baseline_bytes'])
        increases=allocation_rows(lambda x:x[candidate_field]>x['baseline_bytes'])
        methods.compare(reductions,shard['allocation_reductions'],'Allocation reductions '+name)
        methods.compare(increases,shard['allocation_increases'],'Allocation increases '+name)
        require(len(reductions)==shard['allocation_reducing_cells'] and len(increases)==shard['allocation_increasing_cells'],'Allocation counts differ')
        examples=allocation_rows(lambda x:group=='builder' and x['layout']=='Micro' and x['id'] in [f'builder/scatter/{n}/build/shuffled/same/0' for n in [8,64,4096]])
        methods.compare(examples,shard['allocation_examples'],'Allocation examples '+name)
        valid_rows=[x for x in summary['rows'] if x['valid']]
        p95_field='v3_p95_tail_ratio_max' if group=='bridge' else 'p95_tail_ratio_max'
        max_field='v3_max_tail_ratio_max' if group=='bridge' else 'max_tail_ratio_max'
        for field,target,selected in [('aa_drift_max','baseline_aa_drift',summary['rows']),(p95_field,'batch_p95_ratios',valid_rows),(max_field,'batch_max_ratios',valid_rows)]:
            values=[x[field] for x in selected]
            measured={'minimum':min(values),'median':methods.median(values),'maximum':max(values)} if values else {'minimum':None,'median':None,'maximum':None}
            methods.compare(measured,shard[target],'Drift/tail statistics '+name+' '+target)
        methods.compare(rows(sorted(valid_rows,key=lambda x:x[max_field],reverse=True)[:3]),shard['worst_batch_tail_cells'],'Worst tails '+name)
        methods.compare(rows([x for x in summary['rows'] if x[primary]=='clear-improvement' and x[max_field]>1.10]),shard['median_improvements_with_max_batch_ratio_above_1_10'],'Adverse tails among gains '+name)
        require(shard['prepared_allocation_max_bytes']==(None if group=='builder' else 0),'Prepared allocation statement differs')
        summaries[name]=summary;dimensions[name]=dims;raw_count+=evidence.verified_raw_files;process_count+=evidence.verified_processes
    require(set(summaries)==expected,'Missing analyzed shard')
    for key,record in compact['main_matrix_counts'].items():
        rows=[row for group in ['query','builder','mixed'] for row in summaries[f"ci-{record['architecture']}-{group}-{record['backend']}"]['rows']]
        require(len(rows)==record['cells']==426,'Main matrix size differs')
        methods.compare(methods.count_verdicts(rows),record['counts'],'Main matrix '+key)
    for mapped in compact['historical_local_regression_coordinate_map']:
        row=next(x for x in summaries[mapped['remote_shard']]['rows'] if x['layout']==mapped['layout'] and x['id']==mapped['id'])
        for a,b in [('remote_ratio','candidate_ratio_median'),('remote_verdict','verdict'),('remote_aa_drift','aa_drift_max'),('remote_max_batch_ratio','max_tail_ratio_max'),('remote_p95_batch_ratio','p95_tail_ratio_max'),('remote_valid','valid')]:
            methods.compare(row[b],mapped[a],'Mapped remote coordinate '+a)
    cells=sum(len(x['rows']) for x in summaries.values());samples=sum(x['samples'] for x in dimensions.values())
    require(cells==compact['total_recorded_cells']==1920 and samples==compact['total_timed_samples']==125496,'Published totals differ')
    return {'status':'verified','shards':len(summaries),'cells':cells,'timed_samples':samples,'raw_archive_files':len(raw.data),'raw_archive_bytes':raw.bytes,
            'manifest_bound_raw_files':raw_count,'manifest_processes':process_count,'derived_files':len(derived.data),
            'correctness_records':correctness,
            'default_main_matrix_counts':{k:v['counts'] for k,v in compact['main_matrix_counts'].items() if v['backend']=='default'},
            'nohw_control_counts':{k:v['counts'] for k,v in compact['main_matrix_counts'].items() if v['backend']=='nohw'},
            'historical_local_results_reclassified':False,'source_or_binary_provenance_revalidated':False,'benchmarks_rerun':False,'scope':LIMIT}


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--data-dir',type=Path,default=ROOT);args=p.parse_args()
    try:print(json.dumps(reanalyze(args.data_dir.resolve()),indent=2));return 0
    except (OSError,ValueError,KeyError,TypeError,IndexError,tarfile.TarError) as error:
        print('FAIL: '+str(error),file=sys.stderr);print(LIMIT,file=sys.stderr);return 1

if __name__=='__main__':sys.exit(main())
