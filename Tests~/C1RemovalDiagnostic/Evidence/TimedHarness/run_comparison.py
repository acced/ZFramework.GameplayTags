#!/usr/bin/env python3
"""Reviewed, sequential, fresh-process runner. No build or source edits."""
import argparse,hashlib,json,math,os,pathlib,signal,subprocess,time
from verify_sources import inventory as source_inventory
HERE=pathlib.Path(__file__).resolve().parent

def sha(p):return hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()
def dump(p,v):
    with p.open('x') as f:f.write(json.dumps(v,indent=2)+'\n')
def load(p):return json.loads(p.read_text())
def main():
    a=argparse.ArgumentParser()
    a.add_argument('action',choices=['freeze','fixtures','calibrate','compare'])
    a.add_argument('--manifest',type=pathlib.Path,required=True,help='Build-only manifest with exactly R and RMinusC1 binaries.')
    a.add_argument('--out',type=pathlib.Path,required=True)
    a.add_argument('--reviewed',action='store_true',help='Reviewer approval AND semantic pass required before timing')
    args=a.parse_args();os.umask(0o077); manifest=load(args.manifest);out=args.out.resolve()
    if args.action in ('calibrate','compare') and not args.reviewed:raise SystemExit('Timing blocked pending review + semantic approval.')
    cells=load(HERE/'cells.json')
    if set(manifest['versions'])!={'R','RMinusC1'}:raise SystemExit('Require exactly R and RMinusC1.')
    if len(cells)!=12 or len({c['key'] for c in cells})!=12:raise SystemExit('Require12 unique cells.')
    def inventory():
        sources=source_inventory()
        if sha(args.manifest.parent/'sdk-runtime.txt')!=manifest['sdk_runtime_sha256'] or sha(args.manifest.parent/'global.json')!=manifest['sdk_selection_sha256']:raise SystemExit('SDK receipt or selection changed')
        if {name:sha(HERE/name) for name in manifest['compile_input_files']}!=manifest['compile_input_files']:raise SystemExit('Harness compilation input changed since build')
        for version,entry in manifest['versions'].items():
            if entry['source_id']!=sources[version]['source_id'] or entry['source_files']!=sources[version]['files']:
                raise SystemExit('Build/source identity mismatch: '+version)
        if sha(manifest['dotnet'])!=manifest['dotnet_sha256']:raise SystemExit('dotnet host changed')
        if sha(HERE/'source_pins.json')!=manifest['source_pins_sha256']:raise SystemExit('Source pins changed since build')
        result={'sources':sources,'dotnet_sha256':sha(manifest['dotnet']),'harness':{p.name:sha(p) for p in sorted(HERE.iterdir()) if p.suffix in ('.cs','.csproj','.py','.json','.md','.Config','.sh','.sha256','.patch')},'binaries':{},'manifest_sha256':sha(args.manifest)}
        for version,entry in manifest['versions'].items():
            dll=pathlib.Path(entry['dll']).resolve()
            result['binaries'][version]={'physical_path':str(dll),'source_id':entry['source_id'],'files':{str(p):sha(p) for p in sorted(dll.parent.iterdir()) if p.is_file()}}
        return result
    if args.action=='freeze':
        out.mkdir(parents=True,exist_ok=False);dump(out/'freeze.json',inventory());dump(out/'cells.json',cells)
        dump(out/'environment.json',{'uname':list(os.uname()),'cpu_count':os.cpu_count(),'dotnet':manifest['dotnet'],'started_utc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),'tiered_compilation':'0','pointer_size_required':8,'cpu_affinity':sorted(os.sched_getaffinity(0))})
        print('Frozen manifest, rules, cells and binary bytes. No timing.');return
    frozen=load(out/'freeze.json')
    if inventory()!=frozen:raise SystemExit('Frozen source/binary/harness/manifest changed. Use a separately reviewed new directory.')
    env={k:v for k,v in os.environ.items() if not k.startswith(('DOTNET_','COMPlus_','CORECLR_','COR_'))}
    env.update(DOTNET_TieredCompilation='0',COMPlus_TieredCompilation='0',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1')
    dump(out/(args.action+'.runtime-environment.json'), {'removed_names':sorted(k for k in os.environ if k.startswith(('DOTNET_','COMPlus_','CORECLR_','COR_'))),'tiered_compilation':'0','additional_loader_variable_names':sorted(k for k in env if k.startswith('LD_'))})
    started=time.monotonic();deadline=595
    signal_state={'protected':False,'pending':False}
    def interrupted(signum,frame):
        signal_state['pending']=True
        if not signal_state['protected']:raise SystemExit('Stage interrupted; child group will be reaped and raw preserved')
    signal.signal(signal.SIGTERM,interrupted);signal.signal(signal.SIGINT,interrupted)
    def invoke(c,version,calls,label,mode='measure'):
        if time.monotonic()-started>deadline:raise SystemExit('Stage wall budget exceeded; completed raw preserved, stage incomplete.')
        log=out/(label+'.jsonl');err=out/(label+'.stderr');meta=out/(label+'.launch.json')
        if log.exists() or err.exists() or meta.exists():raise SystemExit('Refusing to replace prior raw '+label)
        entry=manifest['versions'][version]
        cmd=[manifest['dotnet'],entry['dll'],mode,c['key'],c['op'],str(c['n']),str(c['target']),str(c['capacity']),c['shape'],c['context'],str(calls)]
        dump(meta,dict(command=cmd,physical_dll=str(pathlib.Path(entry['dll']).resolve()),dll_sha256=sha(entry['dll']),label=label,version=version,mode=mode,calls=calls,tiered_compilation=0,utc=time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime())))
        child=None;terminal='launch-failed';child_start=time.monotonic()
        try:
            with log.open('x') as stdout,err.open('x') as stderr:
                signal_state['protected']=True
                try:
                    child=subprocess.Popen(cmd,stdout=stdout,stderr=stderr,env=env,start_new_session=True)
                finally:
                    signal_state['protected']=False
                terminal='running'
                if signal_state['pending']:
                    terminal='interrupted';raise SystemExit('Interrupted during child launch')
                try:
                    returncode=child.wait(timeout=max(.001,min(60,deadline-(time.monotonic()-started))))
                    terminal='exited'
                except subprocess.TimeoutExpired:
                    terminal='timeout';raise
                except BaseException:
                    terminal='interrupted';raise
        finally:
            signal_state['protected']=True
            try:
                if child is not None and child.poll() is None:
                    try:os.killpg(child.pid,signal.SIGKILL)
                    except ProcessLookupError:pass
                    child.wait()
                dump(out/(label+'.terminal.json'),dict(label=label,status=terminal,returncode=None if child is None else child.returncode,elapsed_seconds=time.monotonic()-child_start))
            finally:
                signal_state['protected']=False
            if signal_state['pending']:raise SystemExit('Interrupted; child reaped and terminal receipt preserved')
        if returncode:raise SystemExit('Child failed; all raw retained: '+label)
        rows=[json.loads(s) for s in log.read_text().splitlines() if s.strip()]
        sample=next((r for r in rows if r['kind']=='sample'),None)
        if not any(r['kind']=='validation' and r['status']=='PASS' for r in rows):raise SystemExit('Missing validation '+label)
        if mode=='measure' and sample is None:raise SystemExit('Missing numeric sample '+label)
        return sample
    if args.action=='fixtures':
        marker=out/'fixtures.complete.json'
        if marker.exists():raise SystemExit('Fixtures already complete')
        for c in cells:
            for version in ['R','RMinusC1']:
                invoke(c,version,1,'fixture-'+c['key']+'-'+version,mode='validate')
        if inventory()!=frozen:raise SystemExit('Frozen inputs changed during fixtures')
        dump(marker,{'status':'complete','processes':24,'cells':12,'calls':1,'timing':False,'note':'Benchmark fixtures only; separate semantic pass still required.'})
        return
    if not (out/'fixtures.complete.json').exists():raise SystemExit('Complete all24 untimed fixtures first')
    if args.action=='calibrate':
        if (out/'counts.json').exists():raise SystemExit('Counts already frozen')
        counts={}
        for c in cells:
            cap=min(67108864,1073741824//(8*(c['n']+c['target']+1)))
            if c['context']=='preprepared-once':cap=min(cap,16384,134217728//(8*c['capacity']))
            count=min(cap,1024);pilots=[]
            # At most four new R-only pilots; never reads old ratios/counts or RMinusC1 speed.
            for pilot in range(4):
                r=invoke(c,'R',count,'pilot-'+c['key']+'-'+str(pilot));pilots.append(r)
                if r['elapsed_ms']>=250 or count==cap or pilot==3:break
                count=min(cap,max(count+1,math.ceil(count*300/max(.001,r['elapsed_ms']))))
            counts[c['key']]={'calls':count,'baseline':'R','target_ms':250,'cap':cap,'capped':count==cap,'last_pilot_ms':pilots[-1]['elapsed_ms'],'adequate_baseline':pilots[-1]['elapsed_ms']>=250,'pilots':len(pilots),'classification':c['classification'],'pool_limited':c['context']=='preprepared-once','pool_target_cap':16384 if c['context']=='preprepared-once' else None}
        if inventory()!=frozen:raise SystemExit('Frozen inputs changed during calibration; raw retained')
        dump(out/'counts.json',counts)
        with (out/'counts.sha256').open('x') as f:f.write(sha(out/'counts.json')+'\n')
        dump(out/'calibrate.complete.json',{'status':'complete','cells':12,'pilots':sum(c['pilots'] for c in counts.values()),'elapsed_seconds':time.monotonic()-started,'counts_sha256':sha(out/'counts.json'),'note':'Short or capped counts remain flagged; no acceptance inference.'})
        return
    if sha(out/'counts.json')!=(out/'counts.sha256').read_text().strip():raise SystemExit('Counts changed')
    counts=load(out/'counts.json')
    frozen_counts_sha=sha(out/'counts.json')
    marker=out/(args.action+'.complete.json')
    if marker.exists():raise SystemExit('Stage already complete')
    if not (out/'calibrate.complete.json').exists():raise SystemExit('Complete new baseline calibration first')
    if set(counts)!={c['key'] for c in cells}:raise SystemExit('Counts/grid mismatch')
    for c in cells:
        for block in range(4):
            # Four pairs AB, BA, BA, AB; two first/second positions per version.
            order=['R','RMinusC1'] if block in (0,3) else ['RMinusC1','R']
            for version in order:
                invoke(c,version,counts[c['key']]['calls'],f'compare-{c["key"]}-{block}-{version}')
    if inventory()!=frozen or sha(out/'counts.json')!=frozen_counts_sha or (out/'counts.sha256').read_text().strip()!=frozen_counts_sha:raise SystemExit('Frozen files/counts changed during stage; raw retained, no acceptance')
    dump(marker,{'status':'complete','elapsed_seconds':time.monotonic()-started,'counts_sha256':sha(out/'counts.json'),'processes':96,'pairs':48,'note':'Completeness only, not performance acceptance.'})
if __name__=='__main__':main()
