#!/usr/bin/env python3
"""Freeze sources, build real C# public-API hosts, run fresh rotating processes, retain all samples."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import shutil
import statistics
import subprocess
import sys
import tarfile
import time
from xml.sax.saxutils import escape

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
LEGACY = {'main': '934b14a47ddf0b6367bf6720be58c18cbcb09896',
          'optimize': 'a99f53d4697df6f577961ff1ee1bad14caa221d1'}
LEGACY_TREES = {'main':'936bb858731b7ae0d1ec0a087de8afe1d96b1b40','optimize':'8a593dd469e4dbf6e182cc4c3d48582c39b73c94','alex':'530e7109d534ee358f601e312bac7e3b08e129d4'}
ALEX_COMMIT = '28e4218f459ecf0e9fb4f0bca90805c05eb962c2'
ALEX_CORE = ['BinarySearchUtility.cs','GameplayTag.cs','GameplayTagAttribute.cs','GameplayTagContainer.cs','GameplayTagContainerDebugView.cs','GameplayTagContainerExtensionMethods.cs','GameplayTagContainerUtility.cs','GameplayTagCountContainer.cs','GameplayTagDefinition.cs','GameplayTagEnumerator.cs','GameplayTagFlags.cs','GameplayTagManager.cs','GameplayTagRegistrationContext.cs','GameplayTagUtility.cs']
KEYS = ('stage', 'operation', 'universe', 'leftCount', 'rightCount', 'distribution', 'overlap', 'relation')

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def inventory(root):
    return {str(p.relative_to(root)): sha(p) for p in sorted(root.rglob('*')) if p.is_file()}

def key(row):
    return tuple(row[k] for k in KEYS)

def percentile(values, p):
    import math
    return sorted(values)[max(0, min(len(values)-1, math.ceil(p*len(values))-1))]

def validate_samples(payload, rows):
    for row in rows:
        if row['status']=='failed': continue
        if row['status']!='measured': raise ValueError('Unknown row status')
        ns=row['ns'];allocated=row['allocated_bytes']
        expected=payload.get('samples',len(ns))
        if len(ns)!=expected or len(allocated)!=expected or expected<3: raise ValueError('Wrong raw sample count')
        if any(not math.isfinite(x) or x<=0 for x in ns): raise ValueError('Invalid timing sample')
        if any(not math.isfinite(x) or x<0 for x in allocated): raise ValueError('Invalid allocation sample')


def aggregate(output, variants, rounds):
    by_variant = {}; expected = None; digests = {}
    failures = []
    for variant in variants:
        groups = {}
        for round_id in range(rounds):
            payload = json.loads((output / f'{variant}-r{round_id}.json').read_text())
            rows = payload['rows']; validate_samples(payload,rows); keys = [key(row) for row in rows]
            if len(set(keys)) != len(keys): raise ValueError('Duplicate benchmark row in ' + variant)
            if expected is None: expected = set(keys)
            if set(keys) != expected: raise ValueError('Missing or extra benchmark rows in ' + variant)
            for row in rows:
                k = key(row)
                if k in digests and row['digest'] != digests[k]: raise ValueError('Input fixture mismatch: ' + repr(k))
                digests[k] = row['digest']; groups.setdefault(k, []).append(row)
                if row['status'] != 'measured': failures.append({'variant': variant, 'round': round_id, 'key': k, 'error': row.get('error')})
        by_variant[variant] = {}
        for k, rows in groups.items():
            if any(row['status'] != 'measured' for row in rows):
                by_variant[variant][k] = {'status': 'failed'}; continue
            all_ns = [sample for row in rows for sample in row['ns']]
            by_variant[variant][k] = {
                'status': 'measured',
                'median_ns': statistics.median(statistics.median(row['ns']) for row in rows),
                'round_medians_ns': [statistics.median(row['ns']) for row in rows],
                'p95_batch_mean_ns': percentile(all_ns, .95), 'max_batch_mean_ns': max(all_ns),
                'allocated_bytes': statistics.median(statistics.median(row['allocated_bytes']) for row in rows),
                'details': rows[0]['details'],
            }
    comparisons = []
    for k in sorted(expected):
        row = dict(zip(KEYS, k)); row['digest'] = digests[k]
        row['variants'] = {v: by_variant[v][k] for v in variants}
        ratios = {}
        for mode in ('Auto', 'Sparse', 'Dense'):
            before, after = row['variants']['original-' + mode], row['variants']['candidate-' + mode]
            if before['status'] == after['status'] == 'measured':
                ratios[mode] = after['median_ns'] / before['median_ns']
        row['candidate_over_original'] = ratios
        if 'candidate_portable-Auto' in variants:
            row['candidate_portable_over_original'] = {m:row['variants']['candidate_portable-'+m]['median_ns']/row['variants']['original-'+m]['median_ns']
                for m in ('Auto','Sparse','Dense') if row['variants']['candidate_portable-'+m]['status']==row['variants']['original-'+m]['status']=='measured'}
        if 'original_aa-Auto' in variants:
            row['original_aa_over_original'] = {m:row['variants']['original_aa-'+m]['median_ns']/row['variants']['original-'+m]['median_ns']
                for m in ('Auto','Sparse','Dense') if row['variants']['original_aa-'+m]['status']==row['variants']['original-'+m]['status']=='measured'}
        forced = [(row['variants']['candidate-'+m].get('median_ns', float('inf')), m) for m in ('Sparse','Dense')]
        row['fastest_forced_candidate'] = min(forced)[1]
        comparisons.append(row)
    return comparisons, failures

def report(output, comparisons, failures, args):
    (output/'comparison.json').write_text(json.dumps(comparisons, indent=2) + '\n')
    summaries = {}
    for mode in ('Auto', 'Sparse', 'Dense'):
        usable = [r for r in comparisons if mode in r['candidate_over_original']]
        ratios = [r['candidate_over_original'][mode] for r in usable]
        worst = sorted(usable, key=lambda r:r['candidate_over_original'][mode], reverse=True)[:25]
        summaries[mode] = {
            'rows': len(usable), 'median_ratio': statistics.median(ratios),
            'p95_ratio': percentile(ratios,.95), 'max_ratio': max(ratios),
            'wins_by_5_percent': sum(r <= .95 for r in ratios), 'regressions_over_5_percent': sum(r > 1.05 for r in ratios),
            'worst': [{**{k:r[k] for k in KEYS}, 'ratio':r['candidate_over_original'][mode],
                'original_ns':r['variants']['original-'+mode]['median_ns'],
                'candidate_ns':r['variants']['candidate-'+mode]['median_ns']} for r in worst],
        }
    if any('candidate_portable_over_original' in r for r in comparisons):
        for mode in ('Auto','Sparse','Dense'):
            usable=[r for r in comparisons if mode in r.get('candidate_portable_over_original',{})]
            ratios=[r['candidate_portable_over_original'][mode] for r in usable]
            worst=sorted(usable,key=lambda r:r['candidate_portable_over_original'][mode],reverse=True)[:25]
            summaries['Portable-'+mode]={'rows':len(usable),'median_ratio':statistics.median(ratios),'p95_ratio':percentile(ratios,.95),'max_ratio':max(ratios),
                'wins_by_5_percent':sum(r<=.95 for r in ratios),'regressions_over_5_percent':sum(r>1.05 for r in ratios),
                'worst':[{**{k:r[k] for k in KEYS},'ratio':r['candidate_portable_over_original'][mode],
                    'original_ns':r['variants']['original-'+mode]['median_ns'],'candidate_ns':r['variants']['candidate_portable-'+mode]['median_ns']} for r in worst]}
    aa = {}
    for mode in ('Auto','Sparse','Dense'):
        values=[r['original_aa_over_original'][mode] for r in comparisons if mode in r.get('original_aa_over_original',{})]
        if values: aa[mode]={'rows':len(values),'median_ratio':statistics.median(values),'p05_ratio':percentile(values,.05),'p95_ratio':percentile(values,.95),'min_ratio':min(values),'max_ratio':max(values)}
    architecture=json.loads((output/'original-Auto-r0.json').read_text()).get('architecture','unknown')
    result = {'architecture':architecture,'aa_original_repetition':aa, 'suite':args.suite, 'rounds':args.rounds, 'samples_per_process':args.samples,
        'managed_benchmark_failures':failures, 'layouts':summaries,
        'release_approved':False, 'unity_il2cpp':'not_run', 'arm64':'measured_coreclr_host' if architecture.lower() in ('arm64','aarch64') else 'not_run',
        'x64':'measured_coreclr_host' if architecture.lower() in ('x64','x86_64','amd64') else 'not_run',
        'warning':'Row ratios describe this finite matrix on this managed host. Batch tails are not individual-operation tails. Multiple raw samples in one process are not independent processes. No universal winning-layout or shipping claim.'}
    (output/'summary.json').write_text(json.dumps(result, indent=2)+'\n')
    lines = ['# Measured refactor comparison', '',
        f'{args.rounds} fresh process round(s), {args.samples} raw samples per row/process; suite: {args.suite}. All raw data retained.', '',
        f'CoreCLR managed host only; architecture {architecture}. No Unity/IL2CPP run. Other architectures are outside this run. p95/max are batch-mean tails, not individual-operation latency. Ratios < 1 favor candidate.', '',
        '| storage | rows | median ratio | p95 ratio | worst ratio | >5% regression rows |',
        '|---|---:|---:|---:|---:|---:|']
    for m,s in summaries.items(): lines.append(f"| {m} | {s['rows']} | {s['median_ratio']:.3f} | {s['p95_ratio']:.3f} | {s['max_ratio']:.3f} | {s['regressions_over_5_percent']} |")
    if aa:
        lines += ['', '## A/A original-executable repetition', '', 'Same original executable, independently labeled fresh processes; this measures host/process noise without any source change.', '']
        for mode,s in aa.items(): lines.append(f"- {mode}: median {s['median_ratio']:.3f}, p05 {s['p05_ratio']:.3f}, p95 {s['p95_ratio']:.3f}, range {s['min_ratio']:.3f}–{s['max_ratio']:.3f}")
    lines += ['', '## All measured rows', '', '| operation | U | left:right | pattern | overlap | Auto ratio | Sparse ratio | Dense ratio | candidate Auto bytes |', '|---|---:|---:|---|---:|---:|---:|---:|---:|']
    for r in comparisons:
        vals = [f"{r['candidate_over_original'][m]:.3f}" if m in r['candidate_over_original'] else 'FAILED' for m in ('Auto','Sparse','Dense')]
        lines.append('| ' + ' | '.join([r['operation'],str(r['universe']),f"{r['leftCount']}:{r['rightCount']}",r['distribution'],str(r['overlap'])]+vals+[str(r['variants']['candidate-Auto'].get('allocated_bytes','FAILED'))])+' |')
    (output/'REPORT.md').write_text('\n'.join(lines)+'\n')
    return result

def candidate_api_summary(output, comparisons, args):
    """Compare candidate-only APIs to equivalent existing workflows; never invent absent baselines."""
    operations={'build.FromTags.sorted':'build.resolved_sorted','build.FromTags.reverse':'build.resolved_reverse',
        'difference.direct_new':'difference.copy_remove','difference.direct_into':'difference.reuse',
        'conversion.ToStorage.from_Sparse':'convert.Sparse_to_requested','conversion.ToStorage.from_Dense':'convert.Dense_to_requested'}
    coordinate=lambda r:tuple(r[k] for k in KEYS if k not in ('stage','operation'))
    paired={(coordinate(r),r['operation']):r for r in comparisons}
    groups={};failures=[]
    for label in ('candidate','candidate_portable') if args.portable else ('candidate',):
        for mode in ('Auto','Sparse','Dense'):
            expected=None
            for round_id in range(args.rounds):
                p=output/f'{label}-newapis-{mode}-r{round_id}.json'
                payload=json.loads(p.read_text());rows=payload['candidate_only_rows'];validate_samples(payload,rows);keys=[key(r) for r in rows]
                if len(keys)!=len(set(keys)): raise ValueError('Duplicate candidate-only row')
                if expected is None: expected=set(keys)
                if set(keys)!=expected: raise ValueError('Candidate-only row matrix differs between rounds')
                for r in rows:
                    group=groups.setdefault((label,mode,key(r)),[])
                    if group and group[0]['digest']!=r['digest']: raise ValueError('Candidate-only fixture differs between rounds')
                    group.append(r)
                    if r['status']!='measured': failures.append({'file':p.name,'key':key(r),'error':r.get('error')})
    result=[]
    for (label,mode,k),rows in sorted(groups.items()):
        entry={**dict(zip(KEYS,k)),'variant':label,'requestedStorage':mode,'digest':rows[0]['digest']}
        if any(r['status']!='measured' for r in rows): entry['status']='failed';result.append(entry);continue
        entry.update(status='measured',median_ns=statistics.median(statistics.median(r['ns']) for r in rows),
            round_medians_ns=[statistics.median(r['ns']) for r in rows],allocated_bytes=statistics.median(statistics.median(r['allocated_bytes']) for r in rows),
            p95_batch_mean_ns=percentile([v for r in rows for v in r['ns']],.95))
        equivalent=operations.get(entry['operation']);entry['equivalent_existing_operation']=equivalent
        match=paired.get((coordinate(entry),equivalent))
        if match:
            if match['digest']!=entry['digest']:raise ValueError('New API / existing workflow input mismatch')
            entry['equivalent_workflows']={v:match['variants'][v] for v in ('original-'+mode,label+'-'+mode)}
            if match['variants']['original-'+mode]['status']=='measured': entry['over_existing_original']=entry['median_ns']/match['variants']['original-'+mode]['median_ns']
            if match['variants'][label+'-'+mode]['status']=='measured': entry['over_existing_same_candidate']=entry['median_ns']/match['variants'][label+'-'+mode]['median_ns']
        result.append(entry)
    (output/'candidate-api-comparison.json').write_text(json.dumps(result,indent=2)+'\n')
    return failures

def legacy_summary(output, variants, rounds):
    groups={}; digests={}; expected=None
    for variant in variants:
        for round_id in range(rounds):
            payload=json.loads((output/f'legacy-{variant}-r{round_id}.json').read_text())
            rowkeys={(r['operation'],r['size'],r['universe'],r['distribution']) for r in payload['rows']}
            if expected is None: expected=rowkeys
            if rowkeys != expected: raise ValueError('Legacy row matrix mismatch')
            for row in payload['rows']:
                k=(row['operation'],row['size'],row['universe'],row['distribution'])
                if k in digests and digests[k]!=row['inputDigest']: raise ValueError('Legacy input mismatch')
                digests[k]=row['inputDigest'];groups.setdefault(k,{}).setdefault(variant,[]).append(row)
    result=[]
    for k,group in sorted(groups.items()):
        entry=dict(zip(('operation','size','universe','distribution'),k));entry['variants']={}
        for variant,rows in group.items():
            entry['variants'][variant]={'status':'failed'} if any(r['status']!='measured' for r in rows) else {
                'status':'measured','median_ns':statistics.median(statistics.median(r['ns']) for r in rows),
                'allocated_bytes':statistics.median(statistics.median(r['allocated_bytes']) for r in rows)}
        candidate=entry['variants']['candidate']
        references=[entry['variants'][v] for v in ('main','optimize','alex') if v in entry['variants']]
        if candidate['status']=='measured' and all(r['status']=='measured' for r in references):
            entry['candidate_over_best_legacy']=candidate['median_ns']/min(r['median_ns'] for r in references)
        result.append(entry)
    (output/'legacy-comparison.json').write_text(json.dumps(result,indent=2)+'\n')
    return result

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--original', required=True, type=Path)
    parser.add_argument('--candidate', type=Path, default=ROOT)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--rounds', type=int, default=3)
    parser.add_argument('--samples', type=int, default=11)
    parser.add_argument('--target-ms', type=float, default=1)
    parser.add_argument('--suite', choices=('smoke','full'), default='full')
    parser.add_argument('--legacy', action='store_true', help='Also rebuild pinned available main/optimize competitors with historical FourWay harness (separate protocol)')
    parser.add_argument('--portable', action='store_true', help='Also compile and measure candidate with GAMEPLAYTAGS_FORCE_PORTABLE, on this same managed host')
    parser.add_argument('--aa', action='store_true', help='Include same-original-executable independent A/A repetition in every mode/round')
    parser.add_argument('--alex-source', type=Path, help='Local Git repository containing pinned Alex commit; used with --legacy')
    parser.add_argument('--summarize-only', action='store_true')
    args = parser.parse_args()
    if args.rounds < 1 or args.samples < 3 or args.target_ms <= 0: parser.error('positive rounds/duration and >=3 samples required')
    output = args.output.resolve(); output.mkdir(parents=True, exist_ok=True)
    labels=['original','candidate']
    if args.portable: labels.append('candidate_portable')
    if args.aa: labels.append('original_aa')
    variants = [f'{v}-{m}' for m in ('Auto','Sparse','Dense') for v in labels]
    if args.summarize_only:
        comparisons, failures = aggregate(output, variants, args.rounds)
        failures += candidate_api_summary(output,comparisons,args)
        report(output, comparisons, failures, args); return 1 if failures else 0
    if (output/'source-inventory.json').exists(): parser.error('Output already contains a run; use a fresh directory to preserve evidence')
    env = dict(os.environ, DOTNET_TieredCompilation='0', DOTNET_ReadyToRun='0', DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1')
    env.setdefault('DOTNET_CLI_HOME',str(output/'dotnet-home'))
    affinity={'supported':hasattr(os,'sched_setaffinity')}
    if affinity['supported']:
        previous=sorted(os.sched_getaffinity(0))
        if not previous: raise RuntimeError('No permitted CPU core')
        selected=previous[0]
        os.sched_setaffinity(0,{selected})
        verified=sorted(os.sched_getaffinity(0))
        if verified != [selected]: raise RuntimeError('CPU affinity did not take effect')
        affinity.update(previous=previous,selected=selected,verified=verified)
    else:
        affinity['limitation']='This OS does not expose sched_setaffinity; process migration is uncontrolled'
    def run(command, name, cwd=ROOT, permit_failure=False):
        start=time.time(); print('RUN',name,flush=True)
        proc = subprocess.run([str(c) for c in command], cwd=cwd, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, timeout=3600)
        (output/(name+'.log')).write_text(proc.stdout)
        print(proc.stdout[-3000:],flush=True); print('SECONDS',round(time.time()-start,3),flush=True)
        if proc.returncode and not permit_failure: raise RuntimeError(name+' failed: '+str(proc.returncode))
        return proc
    sdk_version=run([args.dotnet,'--version'],'dotnet-version').stdout.strip()
    run([args.dotnet,'--info'],'dotnet-info')
    inventories = {}; builds = {}
    harness = output/'sources/harness/Audit~/Refactor'; harness.mkdir(parents=True)
    for source in HERE.iterdir():
        if source.is_file() and source.suffix in ('.cs','.py','.csproj','.md'): shutil.copy2(source,harness/source.name)
    for filename in ('UnityStubs.cs','FourWay.cs','AlexDependencies.cs','NuGet.Config'): shutil.copy2(ROOT/'Audit~'/filename,harness.parent/filename)
    source_labels=[('original',args.original.resolve()),('candidate',args.candidate.resolve())]
    if args.portable: source_labels.append(('candidate_portable',args.candidate.resolve()))
    for label, root in source_labels:
        snapshot=output/'sources'/label; snapshot.mkdir(parents=True)
        shutil.copytree((output/'sources/candidate' if label=='candidate_portable' else root)/'Runtime',snapshot/'Runtime')
        try: commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip()
        except subprocess.CalledProcessError: commit='unknown'
        inventories[label]={'root':str(root),'git_head':commit,'snapshot_files':inventory(snapshot)}
        build=output/'build'/label;build.mkdir(parents=True)
        run([args.dotnet,'build',harness/'Refactor.csproj','-c','Release','--configfile',harness.parent/'NuGet.Config',
            '-p:SourceRoot='+str(snapshot),'-p:RefactorApi='+str(label.startswith('candidate')).lower(),'-p:AdditionalDefineConstants='+('GAMEPLAYTAGS_FORCE_PORTABLE' if label=='candidate_portable' else ''),'-p:BaseIntermediateOutputPath='+str(build/'obj')+'/', '-o',build/'bin'],label+'-build')
        builds[label]=build/'bin/Refactor.dll'
    inventories['harness']={str(p.relative_to(harness.parent)):sha(p) for p in harness.parent.rglob('*') if p.is_file()}
    (output/'source-inventory.json').write_text(json.dumps(inventories,indent=2)+'\n')
    (output/'environment.json').write_text(json.dumps({'platform':platform.platform(),'machine':platform.machine(),'python':sys.version,'dotnet_sdk_version':sdk_version,'cpu_affinity':affinity,'environment':{k:env[k] for k in ('DOTNET_TieredCompilation','DOTNET_ReadyToRun','DOTNET_CLI_TELEMETRY_OPTOUT')},'command':sys.argv},indent=2)+'\n')
    for round_id in range(args.rounds):
        # Offset and reverse process order across rounds, no concurrent timing processes.
        order=variants[round_id%len(variants):]+variants[:round_id%len(variants)]
        if round_id%2: order.reverse()
        for variant in order:
            label,storage=variant.split('-')
            run([args.dotnet,'exec',builds['original' if label=='original_aa' else label],output/f'{variant}-r{round_id}.json',label,storage,args.suite,args.samples,args.target_ms],f'{variant}-r{round_id}',permit_failure=True)
    # Candidate-only APIs run after all paired/A-A measurements, in separate fresh processes,
    # so their allocation/GC/cache pressure cannot perturb only one side of the paired matrix.
    for round_id in range(args.rounds):
        for label in ('candidate','candidate_portable') if args.portable else ('candidate',):
            for storage in ('Auto','Sparse','Dense'):
                run([args.dotnet,'exec',builds[label],output/f'{label}-newapis-{storage}-r{round_id}.json',label,storage,args.suite,args.samples,args.target_ms,'newapis'],f'{label}-newapis-{storage}-r{round_id}',permit_failure=True)
    comparisons, failures = aggregate(output, variants, args.rounds)
    failures += candidate_api_summary(output,comparisons,args)
    summary=report(output,comparisons,failures,args)
    if args.legacy:
        # A separate same-input legacy protocol, never mixed into lifecycle comparisons.
        legacy_roots = {label:output/'sources'/label for label in ('original','candidate')}
        legacy_manifest = {}
        references=dict(LEGACY)
        if args.alex_source: references['alex']=ALEX_COMMIT
        for name,commit in references.items():
            repo=args.alex_source if name=='alex' else ROOT
            tree=subprocess.check_output(['git','rev-parse',commit+'^{tree}'],cwd=repo,text=True).strip()
            if tree != LEGACY_TREES[name]: raise ValueError('Pinned legacy tree mismatch: '+name)
            root=output/'legacy-sources'/name;root.mkdir(parents=True)
            archive=output/(name+'-source.tar')
            with archive.open('wb') as stream: subprocess.run(['git','archive',commit],cwd=args.alex_source if name=='alex' else ROOT,stdout=stream,check=True)
            with tarfile.open(archive) as tar: tar.extractall(root,filter='data')
            legacy_roots[name]=root
            legacy_manifest[name]={'commit':commit,'expected_tree':LEGACY_TREES[name],'actual_tree':tree,'archive_sha256':sha(archive),'runtime_files':inventory(root/'Runtime')}
        for name,root in legacy_roots.items():
            build=output/'legacy-build'/name;build.mkdir(parents=True)
            project=build/'Legacy.csproj'
            paths=[root/'Runtime/**/*.cs',harness.parent/'UnityStubs.cs',harness.parent/'FourWay.cs']
            symbol='CANDIDATE' if name in ('original','candidate') else ''
            if name=='alex':
                paths=[root/'Runtime'/n for n in ALEX_CORE]+[harness.parent/'AlexDependencies.cs',harness.parent/'FourWay.cs']
                symbol='ALEX'
            project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>9.0</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><DefineConstants>'+symbol+'</DefineConstants></PropertyGroup><ItemGroup>'+''.join('<Compile Include="'+escape(str(p))+'"/>' for p in paths)+'</ItemGroup></Project>')
            run([args.dotnet,'build',project,'-c','Release','--configfile',harness.parent/'NuGet.Config','-o',build/'bin'],'legacy-'+name+'-build')
        legacy_order=list(legacy_roots)
        for round_id in range(args.rounds):
            order=legacy_order[round_id%len(legacy_order):]+legacy_order[:round_id%len(legacy_order)]
            if round_id%2: order.reverse()
            for name in order:
                run([args.dotnet,'exec',output/'legacy-build'/name/'bin/Legacy.dll',output/f'legacy-{name}-r{round_id}.json',name,'Auto'],f'legacy-{name}-r{round_id}')
        (output/'legacy-source-inventory.json').write_text(json.dumps(legacy_manifest,indent=2)+'\n')
        legacy_summary(output,legacy_order,args.rounds)
    print(json.dumps({m:{k:v for k,v in s.items() if k!='worst'} for m,s in summary['layouts'].items()},indent=2))
    return 1 if failures else 0
if __name__=='__main__': raise SystemExit(main())
