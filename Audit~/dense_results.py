#!/usr/bin/env python3
"""Reject invalid benchmark results and compare this pass with the first indexed-Dense candidate."""
import argparse
import hashlib
import json
import math
import os
import pathlib
import statistics
import subprocess
import tempfile
import zipfile
from dense_audit import benchmark_fixture
from integer_audit import archive_tree, project

ROOT = pathlib.Path(__file__).resolve().parent.parent
INITIAL = '5b151da228ddebbd8119a2975e674ad6d28cb424'
INITIAL_TREE = 'f56086cbf54aea03e1966b7dc9c15dd77bd01e16'

def check_rows(payload, expected=None):
    rows = payload['rows']
    keys = set()
    for row in rows:
        key = (row['operation'], row['size'], row['universe'], row['distribution'])
        if key in keys: raise RuntimeError('Duplicate benchmark row: '+str(key))
        keys.add(key)
        if row['status'] != 'measured': raise RuntimeError('Benchmark correctness failed: '+str(row))
        samples = row['ns']
        allocations = row['allocated_bytes']
        if len(samples) != 7 or len(allocations) != 7:
            raise RuntimeError('Missing timing/allocation samples: '+str(key))
        if any(not isinstance(x, (int,float)) or not math.isfinite(x) or x <= 0 for x in samples):
            raise RuntimeError('Invalid timing: '+str(key))
        if any(not isinstance(x, (int,float)) or not math.isfinite(x) or x < 0 for x in allocations):
            raise RuntimeError('Invalid allocation sample: '+str(key))
        if not row.get('inputDigest'): raise RuntimeError('Missing input identity')
    if len(keys) != 160 or (expected is not None and keys != expected):
        raise RuntimeError('Benchmark coverage changed')
    return keys

def verify(results):
    summary=json.loads((results/'summary.json').read_text())
    rounds=summary['rounds']
    if rounds<1: raise RuntimeError('Timing was not executed')
    for prefix, variants in [('', ['main','optimize','alex','auto','flat-dense','candidate']),
                             ('prepared-', ['auto','flat-dense','candidate'])]:
        expected=None;digests={}
        for variant in variants:
            for r in range(rounds):
                payload=json.loads((results/(prefix+variant+'-r'+str(r)+'.json')).read_text())
                expected=check_rows(payload,expected)
                for row in payload['rows']:
                    key=(row['operation'],row['size'],row['universe'],row['distribution'])
                    if key in digests and digests[key]!=row['inputDigest']:raise RuntimeError('Mismatched inputs')
                    digests[key]=row['inputDigest']
                    if prefix and variant=='candidate' and any(x != 0 for x in row['allocated_bytes']):
                        raise RuntimeError('Prepared Dense benchmark allocated: '+str(key))
    (results/'benchmark-validation.json').write_text(json.dumps({'passed':True,'rounds':rounds,
        'rows_per_variant':160,'allocating_variants':6,'prepared_variants':3,
        'candidate_prepared_allocation_bytes':0,'native_executed':False},indent=2))
    print('BENCHMARK INTEGRITY: all rows measured, complete, finite and input-matched; every prepared candidate sample is 0 B.',flush=True)
    return summary

def compare_runs(directory, prefix, rounds):
    tables={};digests={};keys=None
    for variant in ('initial','current'):
        table={}
        for r in range(rounds):
            payload=json.loads((directory/(prefix+variant+'-'+str(r)+'.json')).read_text())
            keys=check_rows(payload,keys)
            for row in payload['rows']:
                key=(row['operation'],row['size'],row['universe'],row['distribution'])
                if key in digests and digests[key]!=row['inputDigest']:raise RuntimeError('Different refinement inputs')
                digests[key]=row['inputDigest'];table.setdefault(key,[]).append(row)
        tables[variant]=table
    result=[]
    for key in sorted(keys):
        a=tables['initial'][key];b=tables['current'][key]
        an=statistics.median(statistics.median(r['ns']) for r in a)
        bn=statistics.median(statistics.median(r['ns']) for r in b)
        result.append(dict(operation=key[0],members=key[1],definitions=key[2],distribution=key[3],
            initial_ns=an,current_ns=bn,ratio=bn/an,initial_rounds=a,current_rounds=b))
    return result

def refinement(archive, results, dotnet, rounds):
    destination=results/'refinement';destination.mkdir(exist_ok=True)
    env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1')
    def run(command, log, cwd):
        p=subprocess.run(list(map(str,command)),cwd=cwd,env=env,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=1200)
        (destination/log).write_text(p.stdout,encoding='utf-8');print(p.stdout,flush=True)
        if p.returncode:raise RuntimeError(log+' failed')
    with tempfile.TemporaryDirectory(prefix='dense-refinement-') as directory:
        work=pathlib.Path(directory)
        (work/'global.json').write_text(json.dumps({'sdk':{'version':'8.0.425','rollForward':'disable'}}))
        (work/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
        previous=work/'initial-source';previous.mkdir()
        with zipfile.ZipFile(archive) as z:
            if z.comment.decode('ascii')!=INITIAL or archive_tree(z)!=INITIAL_TREE:raise RuntimeError('Incorrect initial Dense reference')
            for entry in z.infolist():
                path=pathlib.PurePosixPath(entry.filename)
                if path.is_absolute() or '..' in path.parts or (entry.external_attr>>16)&0o170000==0o120000:raise RuntimeError('Invalid archive path')
            z.extractall(previous)
        source=(ROOT/'Audit~/FourWay.cs').read_text(encoding='utf-8')
        builds={}
        for variant,root in [('initial',previous),('current',ROOT)]:
            host=work/variant;host.mkdir()
            for prepared in (False,True):
                name='prepared' if prepared else 'allocating'
                fixture=host/(name+'.cs');fixture.write_text(benchmark_fixture(source,True,prepared),encoding='utf-8')
                (destination/(variant+'-'+name+'-fixture.cs')).write_text(fixture.read_text(),encoding='utf-8')
                proj=host/(name+'.csproj')
                project(proj,[root/'Runtime/**/*.cs',ROOT/'Audit~/UnityStubs.cs',fixture],'CANDIDATE')
                run([dotnet,'build',proj,'-c','Release','-p:BaseIntermediateOutputPath=obj-'+name+'/','-o',host/name],variant+'-'+name+'-build.log',host)
                builds[(variant,prepared)]=(host/name/(name+'.dll'),host)
        for r in range(rounds):
            order=('initial','current') if r%2==0 else ('current','initial')
            for prepared in (False,True):
                prefix='prepared-' if prepared else ''
                for variant in order:
                    dll,host=builds[(variant,prepared)]
                    run([dotnet,'exec',dll,destination/(prefix+variant+'-'+str(r)+'.json'),variant,'Auto'],prefix+variant+'-'+str(r)+'.log',host)
    all_rows={}
    for prefix in ('','prepared-'):
        rows=compare_runs(destination,prefix,rounds)
        (destination/(prefix+'comparison.json')).write_text(json.dumps(rows,indent=2))
        all_rows[prefix or 'allocating']=dict(cases=len(rows),faster=sum(r['ratio']<1 for r in rows),
            alerts=[{k:r[k] for k in ('operation','members','definitions','distribution','initial_ns','current_ns','ratio')} for r in rows if r['ratio']>1.05])
        print('REFINEMENT '+(prefix or 'allocating')+' '+json.dumps(all_rows[prefix or 'allocating']),flush=True)
        for r in rows:
            if (r['definitions'],r['members']) in ((262144,8),(10000,1024),(65536,4096)) and r['operation']!='exact_miss':
                print('REFINEMENT SELECTED '+json.dumps({k:r[k] for k in ('operation','members','definitions','distribution','initial_ns','current_ns','ratio')}),flush=True)
    (destination/'summary.json').write_text(json.dumps(dict(initial=INITIAL,initial_archive_sha256=hashlib.sha256(archive.read_bytes()).hexdigest(),
        rounds=rounds,comparisons=all_rows,release_approved=False),indent=2))

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--results',required=True)
    parser.add_argument('--initial')
    parser.add_argument('--dotnet',default='dotnet')
    parser.add_argument('--rounds',type=int,default=3)
    args=parser.parse_args()
    if args.rounds<1:parser.error('rounds must be positive')
    results=pathlib.Path(args.results).resolve()
    summary=verify(results)
    for name in ('comparison.json','prepared-comparison.json'):
        rows=json.loads((results/name).read_text())
        print('FINAL '+name,flush=True)
        for row in rows:
            if (row['definitions'],row['members']) in ((262144,8),(10000,1024),(65536,4096)) and row['operation']!='exact_miss':
                print(json.dumps({k:row[k] for k in ('operation','members','definitions','distribution','ratio_to_fastest')} |
                    {'ns':{v:round(d['median_ns'],3) for v,d in row['variants'].items()},
                     'bytes':{v:d['bytes_per_operation'] for v,d in row['variants'].items()}}),flush=True)
    if args.initial:refinement(pathlib.Path(args.initial).resolve(),results,args.dotnet,args.rounds)
    print('Release remains unapproved. Losing rows are not correctness failures and are not suppressed.',flush=True)
    return 0

if __name__=='__main__':raise SystemExit(main())
