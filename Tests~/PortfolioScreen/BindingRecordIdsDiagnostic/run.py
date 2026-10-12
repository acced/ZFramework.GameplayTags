#!/usr/bin/env python3
"""Mono diagnostic only: exact combined sources, two builds, one original semantic target."""
import argparse
import hashlib
import json
from pathlib import Path
import sys
import time

HERE=Path(__file__).resolve().parent
PUBLIC_SEAL='0f2c191c3d35d8e22afdad3e3a69962d26272c748bf0d4d00a10f9fdb252ec95'
SEMANTIC_STOP="""        status['Stage']='semantic'
        run('semantic-original',executables['original']+['semantic',private/'original.snapshot.bin',private/'original.layout.json',private/'original.metadata.json'],allow_failure=True)
        verify()
        status['State']='complete'; status['Stage']='completed-original-semantic-diagnostic'
        raise GateStop()
"""
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def adapt(source):
    begin="        status['Stage']='semantic'\n"; end="        status['Stage']='cold'\n"
    old="                    active_proc.wait()\n"; new=old+"                    record['ExitCode']=active_proc.returncode; save(private/'commands.json',commands)\n"
    start="    started = time.monotonic()\n"
    if any(source.count(x)!=1 for x in (begin,end,old,start)): raise ValueError('Fixed adapter anchors changed')
    modified=source[:source.index(begin)]+SEMANTIC_STOP+source[source.index(end):]
    return modified.replace(old,new,1).replace(start,'    started = DIAGNOSTIC_STARTED\n',1)

def main():
    started=time.monotonic()
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--backend',choices=('mono',),required=True)
    ap.add_argument('--package',type=Path,default=HERE.parent/'BindingRecordIds')
    for name in ('dotnet','mono','nuget','git-root','out','work','expected-seal'): ap.add_argument('--'+name,required=True)
    ap.add_argument('--deadline-seconds',type=int,choices=(120,),default=120)
    args=ap.parse_args(); package=args.package.resolve()
    frozen_bytes=(HERE/'frozen.json').read_bytes(); seal=hashlib.sha256(frozen_bytes).hexdigest()
    if seal!=args.expected_seal: ap.error('Diagnostic seal mismatch')
    frozen=json.loads(frozen_bytes)
    if {str(p.relative_to(HERE)) for p in HERE.rglob('*') if p.is_file()}!=set(frozen['Files'])|{'frozen.json'}: ap.error('Diagnostic inventory mismatch')
    if {name:sha(HERE/name) for name in frozen['Files']}!=frozen['Files']: ap.error('Diagnostic input mismatch')
    if sha(package/'frozen.json')!=PUBLIC_SEAL: ap.error('Original package seal mismatch')
    pins=json.loads((package/'frozen.json').read_text())
    if {name:sha(package/name) for name in pins['Files']}!=pins['Files']: ap.error('Original package input mismatch')
    out=Path(args.out).resolve(); work=Path(args.work).resolve()
    if out==work or out in work.parents or work in out.parents or any(base==folder or base in folder.parents for base in (HERE,package) for folder in (out,work)): ap.error('New separate output/work directories required')
    out.mkdir(parents=True,exist_ok=False); work.mkdir(parents=True,exist_ok=False)
    sys.dont_write_bytecode=True; sys.path.insert(0,str(HERE)); sys.path.insert(0,str(package))
    from projection import project
    namespace={'__file__':str(package/'run.py'),'__name__':'mono_original_diagnostic_reuse','DIAGNOSTIC_STARTED':started}
    exec(compile(adapt((package/'run.py').read_text()),'<sealed-mono-diagnostic-coordinator>','exec'),namespace)
    sys.argv=['mono-original-diagnostic','--backend','mono','--dotnet',args.dotnet,'--mono',args.mono,'--nuget',args.nuget,'--git-root',args.git_root,'--out',str(work/'reports'),'--work',str(work/'artifacts'),'--expected-seal',PUBLIC_SEAL,'--deadline-seconds','120']
    wrapper_failure=None
    try: namespace['main']()
    except BaseException as error:
        wrapper_failure={TimeoutError:'TIMEOUT',ValueError:'VALUE_ERROR',RuntimeError:'RUNTIME_ERROR',OSError:'IO_ERROR',KeyboardInterrupt:'INTERRUPTED'}.get(type(error),'UNCLASSIFIED')
    def read(path): return path.read_bytes() if path.is_file() else None
    def read_json(path,default):
        try: return json.loads(read(path) or b'null') or default
        except (ValueError,TypeError): return default
    artifacts=work/'artifacts'; reports=work/'reports'
    data={key:read(artifacts/name) for key,name in {'Stdout':'semantic-original.stdout','Stderr':'semantic-original.stderr','Snapshot':'original.snapshot.bin','Layout':'original.layout.json','Metadata':'original.metadata.json'}.items()}
    result=project(read_json(reports/'status.json',{}),read_json(artifacts/'commands.json',[]),data,(read(artifacts/'failure.txt') or b'').decode('utf-8',errors='replace'))
    try: intact=sha(HERE/'frozen.json')==seal and sha(package/'frozen.json')==PUBLIC_SEAL and {name:sha(HERE/name) for name in frozen['Files']}==frozen['Files'] and {name:sha(package/name) for name in pins['Files']}==pins['Files']
    except OSError: intact=False
    result.update(DiagnosticSeal=seal,OriginalPackageSeal=PUBLIC_SEAL,InputHashesVerifiedAfter=intact,CoordinatorDeadlineSeconds=120,WithinDeadline=time.monotonic()-started<120,WrapperFailureCode=wrapper_failure)
    if not intact or not result['WithinDeadline'] or wrapper_failure: result['Outcome']='INCOMPLETE_DIAGNOSTIC'
    (out/'diagnostic.json').write_text(json.dumps(result,indent=2,allow_nan=False)+'\n')
    if {p.name for p in out.iterdir()}!={'diagnostic.json'}: raise RuntimeError('Public allowlist mismatch')
    print('Mono semantic diagnostic: '+result['Outcome']+'. No performance or candidate target executed.')
    return 0 if result['Outcome']=='NON_REPRODUCTION' else 1

if __name__=='__main__': raise SystemExit(main())
