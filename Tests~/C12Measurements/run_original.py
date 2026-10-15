#!/usr/bin/env python3
"""Reviewed, sequential, fresh-process runner. No build or source edits."""
import argparse,hashlib,json,math,os,pathlib,signal,subprocess,time
HERE=pathlib.Path(__file__).resolve().parent

def sha(p):return hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()
def dump(p,v):
    with p.open('x') as f:f.write(json.dumps(v,indent=2)+'\n')
def load(p):return json.loads(p.read_text())
def main():
    a=argparse.ArgumentParser()
    a.add_argument('action',choices=['freeze','calibrate','aa','C1','C2'])
    a.add_argument('--manifest',type=pathlib.Path,required=True,help='JSON {dotnet, versions:{B1: {dll,source_id}, B11:..., C1:..., C1-C2:...}}')
    a.add_argument('--out',type=pathlib.Path,required=True)
    a.add_argument('--reviewed',action='store_true',help='Reviewer approval AND semantic pass required before timing')
    args=a.parse_args();os.umask(0o077); manifest=load(args.manifest);out=args.out.resolve()
    if args.action!='freeze' and not args.reviewed:raise SystemExit('Timing blocked pending review + semantic approval.')
    cells=load(HERE/'cells.json')
    def inventory():
        result={'harness':{p.name:sha(p) for p in sorted(HERE.iterdir()) if p.suffix in ('.cs','.csproj','.py','.json','.md','.Config','.sh')},'binaries':{},'manifest_sha256':sha(args.manifest)}
        for version,entry in manifest['versions'].items():
            dll=pathlib.Path(entry['dll']).resolve()
            result['binaries'][version]={'physical_path':str(dll),'source_id':entry['source_id'],'files':{str(p):sha(p) for p in sorted(dll.parent.iterdir()) if p.is_file()}}
        return result
    if args.action=='freeze':
        out.mkdir(parents=True,exist_ok=False);dump(out/'freeze.json',inventory());dump(out/'cells.json',cells)
        dump(out/'environment.json',{'uname':list(os.uname()),'cpu_count':os.cpu_count(),'dotnet':manifest['dotnet'],'started_utc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),'tiered_compilation':'0','cpu_affinity':sorted(os.sched_getaffinity(0))})
        print('Frozen manifest, rules, cells and binary bytes. No timing.');return
    frozen=load(out/'freeze.json')
    if inventory()!=frozen:raise SystemExit('Frozen source/binary/harness/manifest changed. Use a separately reviewed new directory.')
    env={k:v for k,v in os.environ.items() if not k.startswith(('DOTNET_','COMPlus_','CORECLR_','COR_'))}
    env.update(DOTNET_TieredCompilation='0',COMPlus_TieredCompilation='0',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1')
    dump(out/(args.action+'.runtime-environment.json'), {'removed_names':sorted(k for k in os.environ if k.startswith(('DOTNET_','COMPlus_','CORECLR_','COR_'))),'tiered_compilation':'0','additional_loader_variable_names':sorted(k for k in env if k.startswith('LD_'))})
    started=time.monotonic();deadline=900
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
        dump(meta,dict(command=cmd,physical_dll=str(pathlib.Path(entry['dll']).resolve()),dll_sha256=sha(entry['dll']),label=label,version=version,calls=calls,tiered_compilation=0,utc=time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime())))
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
        return sample
    if args.action=='calibrate':
        if (out/'counts.json').exists():raise SystemExit('Counts already frozen')
        counts={}
        for c in cells:
            cap=1 if c['context']=='cold-process' else 8192 if c['context'] in ('isolated','first-object') else min(67108864,1073741824//(8*(c['n']+c['target']+1)))
            count=min(cap,1024);pilots=[]
            # At most four B1-only pilots; baseline takes no contender-informed decisions.
            for pilot in range(4):
                r=invoke(c,'B1',count,'pilot-'+c['key']+'-'+str(pilot));pilots.append(r)
                if r['elapsed_ms']>=250 or count==cap or pilot==3:break
                count=min(cap,max(count+1,math.ceil(count*300/max(.001,r['elapsed_ms']))))
            counts[c['key']]={'calls':count,'baseline':'B1','target_ms':250,'cap':cap,'capped':count==cap,'last_pilot_ms':pilots[-1]['elapsed_ms'],'adequate_baseline':pilots[-1]['elapsed_ms']>=250,'pilots':len(pilots)}
        dump(out/'counts.json',counts);(out/'counts.sha256').write_text(sha(out/'counts.json')+'\n');return
    if sha(out/'counts.json')!=(out/'counts.sha256').read_text().strip():raise SystemExit('Counts changed')
    counts=load(out/'counts.json')
    marker=out/(args.action+'.complete.json')
    if marker.exists():raise SystemExit('Stage already complete')
    if args.action=='aa':
        selected=[c for c in cells if c['stage']=='control']
        selected += [next(c for c in cells if c['stage']=='C1' and c['n']==n and c['target']==n and c['context']=='retained') for n in [0,1]]
        selected += [next(c for c in cells if c['stage']=='C2' and c['op']==op and c['shape']=='last' and c['n']==1023 and c['context']=='retained') for op in ['append','right']]
        for c in selected:
            for pair in range(4):
                # A and A2 ALWAYS resolve to exactly one physical path, never copied/rebuilt.
                for label in (['A','A2'] if pair%2==0 else ['A2','A']):
                    invoke(c,'B11',counts[c['key']]['calls'],f'aa-{c["key"]}-{pair}-{label}')
    else:
        if not (out/'aa.complete.json').exists():raise SystemExit('Complete physical-binary AA first')
        if args.action=='C2' and not (out/'C1.complete.json').exists():raise SystemExit('Complete C1 first')
        selected=[c for c in cells if c['stage'] in (args.action,'control') or (args.action=='C2' and c['op']=='lifecycle')]
        versions=['B1','B11','C1'] if args.action=='C1' else ['B1','B11','C1','C1-C2']
        for c in selected:
            for block in range(4):
                # Two orders and their exact reverse: every version pair has 2/2 precedence.
                base=versions[block//2%len(versions):]+versions[:block//2%len(versions)]
                order=base if block%2==0 else list(reversed(base))
                for version in order:
                    invoke(c,version,counts[c['key']]['calls'],f'{args.action}-{c["key"]}-{block}-{version}')
    if inventory()!=frozen:raise SystemExit('Frozen files changed during stage; raw retained, no acceptance')
    dump(marker,{'status':'complete','elapsed_seconds':time.monotonic()-started,'counts_sha256':sha(out/'counts.json'),'note':'Completeness only, not performance acceptance.'})
if __name__=='__main__':main()
