#!/usr/bin/env python3
"""Semantic-only collector portability repair; no performance targets."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import sys
import time

HERE=Path(__file__).resolve().parent
PUBLIC_SEAL='0f2c191c3d35d8e22afdad3e3a69962d26272c748bf0d4d00a10f9fdb252ec95'
PUBLIC=('status.json','provenance.json','config.json','correctness.json','count-map.json','raw/cold.json','raw/warm.json','summary.json')
STOP="""        verify()
        if (status['Builds'],status['SemanticTargets'],status['ColdTargets'],status['WarmTargets'],status['PilotTargets'])!=(2,2,0,0,0): raise RuntimeError('Semantic-only budget')
        status['State']='complete'; status['Stage']='completed-semantic-only'; status['SemanticOnly']=True
        raise GateStop()
"""
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def save(path,value): path.write_text(json.dumps(value,indent=2,allow_nan=False)+'\n')
def adapt(source):
    start="    started = time.monotonic()\n"; stop="        status['Stage']='cold'\n"; verify="    def verify():\n"; entry="HERE/'Harness/Entry.cs'"; wait="                    active_proc.wait()\n"
    if any(source.count(x)!=1 for x in (start,stop,verify,entry,wait)): raise ValueError('Fixed adapter anchors changed')
    return source.replace(start,'    started = REPAIR_STARTED\n',1).replace(stop,STOP+stop,1).replace(verify,verify+'        REPAIR_VERIFY()\n',1).replace(entry,"REPAIR_HARNESS/'Entry.cs'",1).replace(wait,wait+"                    record['ExitCode']=active_proc.returncode; save(private/'commands.json',commands)\n",1)

def main():
    started=time.monotonic()
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--backend',choices=('net','mono'),required=True)
    ap.add_argument('--package',type=Path,default=HERE.parent/'BindingRecordIds')
    for name in ('dotnet','nuget','git-root','out','work','expected-seal'): ap.add_argument('--'+name,required=True)
    ap.add_argument('--mono',default='mono')
    ap.add_argument('--deadline-seconds',type=int,choices=(120,),default=120)
    args=ap.parse_args();package=args.package.resolve()
    seal=sha(HERE/'frozen.json')
    if seal!=args.expected_seal:ap.error('Repair seal mismatch')
    frozen=json.loads((HERE/'frozen.json').read_text())
    if sha(package/'frozen.json')!=PUBLIC_SEAL:ap.error('Original package seal mismatch')
    pins=json.loads((package/'frozen.json').read_text())
    out=Path(args.out).resolve();work=Path(args.work).resolve()
    if out.exists() or work.exists() or out==work or out in work.parents or work in out.parents or any(base==folder or base in folder.parents for base in (HERE,package) for folder in (out,work)):ap.error('New separate output/work directories required')
    fixture_hashes={name[len('Harness/'):]:value for name,value in pins['Files'].items() if name.startswith('Harness/')}
    fixture_hashes['MetadataGate.cs']=sha(HERE/'MetadataGate.cs');harness=work/'harness'
    def repair_verify():
        if sha(HERE/'frozen.json')!=seal or {str(p.relative_to(HERE)) for p in HERE.rglob('*') if p.is_file()}!=set(frozen['Files'])|{'frozen.json'} or {n:sha(HERE/n) for n in frozen['Files']}!=frozen['Files']:raise RuntimeError('Repair input changed')
        if sha(package/'frozen.json')!=PUBLIC_SEAL or {n:sha(package/n) for n in pins['Files']}!=pins['Files']:raise RuntimeError('Original input changed')
        if harness.exists() and {str(p.relative_to(harness)):sha(p) for p in harness.rglob('*') if p.is_file()}!=fixture_hashes:raise RuntimeError('Repair fixture changed')
    repair_verify()
    if tuple((HERE/'upload-allowlist.txt').read_text().splitlines())!=PUBLIC:ap.error('Public allowlist mismatch')
    work.mkdir(parents=True,exist_ok=False);shutil.copytree(package/'Harness',harness);shutil.copy2(HERE/'MetadataGate.cs',harness/'MetadataGate.cs');repair_verify()
    sys.dont_write_bytecode=True;sys.path.insert(0,str(HERE));sys.path.insert(0,str(package))
    from diagnostics import project_target
    namespace={'__file__':str(package/'run.py'),'__name__':'semantic_repair_reuse','REPAIR_STARTED':started,'REPAIR_HARNESS':harness,'REPAIR_VERIFY':repair_verify}
    exec(compile(adapt((package/'run.py').read_text()),'<sealed-semantic-repair-coordinator>','exec'),namespace)
    sys.argv=['semantic-repair','--backend',args.backend,'--dotnet',args.dotnet,'--mono',args.mono,'--nuget',args.nuget,'--git-root',args.git_root,'--out',str(out),'--work',str(work/'artifacts'),'--expected-seal',PUBLIC_SEAL,'--deadline-seconds','120']
    wrapper_failure=None
    try:namespace['main']()
    except BaseException as error:wrapper_failure={TimeoutError:'TIMEOUT',ValueError:'VALUE_ERROR',RuntimeError:'RUNTIME_ERROR',OSError:'IO_ERROR',KeyboardInterrupt:'INTERRUPTED'}.get(type(error),'UNCLASSIFIED')
    def read(path):return path.read_bytes() if path.is_file() else None
    def read_json(path,default):
        try:return json.loads(read(path) or b'null') or default
        except (ValueError,TypeError):return default
    artifacts=work/'artifacts';commands=read_json(artifacts/'commands.json',[])
    diagnostics=[]
    for variant in ('original','candidate'):
        name='semantic-'+variant;command=next((c for c in commands if c.get('Name')==name),None)
        data={key:read(artifacts/file) for key,file in {'Stdout':name+'.stdout','Stderr':name+'.stderr','Snapshot':variant+'.snapshot.bin','Layout':variant+'.layout.json','Metadata':variant+'.metadata.json'}.items()}
        diagnostics.append(project_target(variant,args.backend,command,data))
    try:repair_verify();intact=True
    except (OSError,RuntimeError):intact=False
    out.mkdir(parents=True,exist_ok=True);(out/'raw').mkdir(exist_ok=True)
    payloads={name:read_json(out/name,[] if name.startswith('raw/') else {}) for name in PUBLIC}
    status=payloads['status.json'];correctness=payloads['correctness.json']
    status.update(RepairSeal=seal,OriginalPackageSeal=PUBLIC_SEAL,SemanticOnly=True,Backend=args.backend,AcceptanceEvidence=False,InputHashesVerifiedAfter=intact,WrapperFailureCode=wrapper_failure,WithinDeadline=time.monotonic()-started<120)
    correctness.update(SemanticDiagnostics=diagnostics,MetadataCollectorSHA256=frozen['Files']['MetadataGate.cs'],OriginalMetadataCollectorSHA256=pins['Files']['Harness/MetadataGate.cs'])
    payloads['summary.json'].update(AcceptanceEvidence=False,LatencyPassEstablished=False,AutomaticContinuation=False)
    payloads['config.json'].update(SemanticOnly=True,DeadlineSeconds=120,RequestedDeadlineSeconds=120,MaxBuilds=2,SemanticProcesses=2,ColdProcesses=0,WarmProcesses=0,PilotProcesses=0)
    complete=status.get('State')=='complete' and status.get('Stage')=='completed-semantic-only' and (status.get('Builds'),status.get('SemanticTargets'),status.get('ColdTargets'),status.get('WarmTargets'),status.get('PilotTargets'))==(2,2,0,0,0) and payloads['raw/cold.json']==[] and payloads['raw/warm.json']==[]
    if not complete or not intact or not status['WithinDeadline'] or wrapper_failure:status['State']='incomplete'
    for name in PUBLIC:save(out/name,payloads[name])
    if {str(p.relative_to(out)) for p in out.rglob('*') if p.is_file()}!=set(PUBLIC):raise RuntimeError('Public allowlist mismatch')
    print('Metadata collector semantic repair: '+status['State']+'. No performance or qualification evidence.')
    return 0 if status['State']=='complete' else 1

if __name__=='__main__':raise SystemExit(main())
