#!/usr/bin/env python3
"""Bounded diagnostics only. --reviewed records a gate, not user permission."""
import argparse,hashlib,json,os,pathlib,shutil,signal,subprocess,time
HERE=pathlib.Path(__file__).resolve().parent
VERSIONS=('B11','C1','current792')
def sha(p):return hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()
def dump(p,x):
    with p.open('x') as f:json.dump(x,f,indent=2);f.write('\n')
def main():
    p=argparse.ArgumentParser();p.add_argument('stage',choices=['jit','trace']);p.add_argument('--manifest',type=pathlib.Path,required=True);p.add_argument('--out',type=pathlib.Path,required=True);p.add_argument('--reviewed',action='store_true');p.add_argument('--dotnet-trace',default=os.environ.get('DOTNET_TRACE') or shutil.which('dotnet-trace'));a=p.parse_args()
    if not a.reviewed:raise SystemExit('Await exact diagnostic plan review before target execution.')
    if a.stage=='trace' and not a.dotnet_trace:raise SystemExit('Official dotnet-trace8.0.547301 is required; set DOTNET_TRACE or pass --dotnet-trace. No install is attempted.')
    m=json.loads(a.manifest.read_text());out=a.out.resolve();out.mkdir(parents=True,exist_ok=False)
    if sha(HERE/'SOURCE_MANIFEST.json')!=m['source_manifest_sha256']:raise SystemExit('Source manifest changed')
    sources=json.loads((HERE/'SOURCE_MANIFEST.json').read_text())
    for v,s in sources.items():
        if {p.name:sha(p) for p in sorted(pathlib.Path(m['runtime_paths'][v]).glob('*.cs'))}!=s['files']:raise SystemExit('Source changed '+v)
    for name,h in m['harness'].items():
        if sha(HERE/'harness'/name)!=h:raise SystemExit('Harness changed '+name)
    for entry in m['versions'].values():
        for f,h in entry['files'].items():
            if sha(f)!=h:raise SystemExit('Binary changed '+f)
    env={k:v for k,v in os.environ.items() if not k.startswith(('DOTNET_','COMPlus_','CORECLR_','COR_'))}
    env.update(DOTNET_ROOT=str(pathlib.Path(m['dotnet']).resolve().parent),DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_GENERATE_ASPNET_CERTIFICATE='false',DOTNET_TieredCompilation='0',COMPlus_TieredCompilation='0')
    dump(out/'environment.json',{'runtime_removed_names':[k for k in os.environ if k.startswith(('DOTNET_','COMPlus_','CORECLR_','COR_'))],'effective':{k:v for k,v in env.items() if k.startswith(('DOTNET_','COMPlus_','CORECLR_','COR_'))},'loader_variable_names':[k for k in env if k.startswith('LD_')],'classification':'diagnostic; no formal timing samples','warmups_per_target':64,'requested_iterations':1 if a.stage=='jit' else 4096})
    signal_state={'protected':False,'pending':False}
    def stopped(sig,frame):
        signal_state['pending']=True
        if not signal_state['protected']:raise SystemExit('Interrupted; diagnostic child group will be reaped.')
    signal.signal(signal.SIGTERM,stopped);signal.signal(signal.SIGINT,stopped)
    dump(out/'prepared-runner.json',{'sha256':sha(__file__),'manifest_sha256':sha(a.manifest)})
    for v in VERSIONS:
        calls=1 if a.stage=='jit' else 4096
        cmd=[m['dotnet'],m['versions'][v]['dll'],'validate','diagnostic-actual-lifecycle-1024-copy512','lifecycle','1024','1024','1024','overlap','new',str(calls)]
        childenv=env.copy()
        if a.stage=='jit':
            childenv.update(COMPlus_JitDisasm='*',COMPlus_JitDisasmAssemblies='C3aNaturalDiagnostics',COMPlus_JitStdOutFile=str(out/(v+'.jit.asm')))
        else:
            cmd=[str(pathlib.Path(a.dotnet_trace).resolve()),'collect','--profile','cpu-sampling','--providers','Microsoft-Windows-DotNETRuntime:0x1:4','--buffersize','64','--duration','00:00:01:00','--output',str(out/(v+'.nettrace')),'--show-child-io','--']+cmd
        dump(out/(v+'.launch.json'),{'cmd':cmd,'binary_sha256':sha(m['versions'][v]['dll']),'requested_iterations':calls,'warmups':64,'total_lifecycles':64+calls,'timeout_seconds':60 if a.stage=='jit' else 90,'stage':a.stage})
        child=None;status='launch-failed';t=time.monotonic()
        try:
            with (out/(v+'.stdout')).open('x') as stdout,(out/(v+'.stderr')).open('x') as stderr:
                signal_state['protected']=True
                try:child=subprocess.Popen(cmd,stdout=stdout,stderr=stderr,env=childenv,start_new_session=True)
                finally:signal_state['protected']=False
                if signal_state['pending']:raise SystemExit('Interrupted during launch; child group will be reaped.')
                status='running'
                try:code=child.wait(timeout=60 if a.stage=='jit' else 90);status='exited'
                except subprocess.TimeoutExpired:status='timeout';raise
                except BaseException:status='interrupted';raise
        finally:
            signal_state['protected']=True
            if child is not None and child.poll() is None:
                try:os.killpg(child.pid,signal.SIGKILL)
                except ProcessLookupError:pass
                child.wait()
            dump(out/(v+'.terminal.json'),{'status':status,'returncode':None if child is None else child.returncode,'orchestration_elapsed_seconds':time.monotonic()-t,'not_a_performance_sample':True})
            signal_state['protected']=False
        if signal_state['pending']:raise SystemExit('Interrupted; child group reaped.')
        if a.stage=='trace' and time.monotonic()-t >= 60:raise SystemExit('Trace reached capture cap; preserve partial evidence and mark incomplete: '+v)
        if code:raise SystemExit('Diagnostic failed '+v)
        text=(out/(v+'.stdout')).read_text()
        if '"status":"PASS"' not in text:raise SystemExit('Validation PASS missing '+v)
        if a.stage=='jit' and (not (out/(v+'.jit.asm')).exists() or (out/(v+'.jit.asm')).stat().st_size==0):raise SystemExit('JIT output missing '+v)
        if a.stage=='trace' and (not (out/(v+'.nettrace')).exists() or (out/(v+'.nettrace')).stat().st_size==0):raise SystemExit('Trace missing '+v)
    dump(out/'complete.json',{'stage':a.stage,'targets':3,'classification':'diagnostic only','formal_timing_samples':0})
if __name__=='__main__':main()
