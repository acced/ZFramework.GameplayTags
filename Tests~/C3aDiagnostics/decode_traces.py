#!/usr/bin/env python3
"""Offline trace decoding only; never launches the workload binaries."""
import argparse, hashlib, json, os, pathlib, shutil, signal, subprocess
HERE=pathlib.Path(__file__).resolve().parent
VERSIONS=('B11','C1','current792')
def sha(p):return hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()
def main():
    p=argparse.ArgumentParser();p.add_argument('--traces',required=True,type=pathlib.Path);p.add_argument('--out',required=True,type=pathlib.Path);p.add_argument('--dotnet',default=os.environ.get('DOTNET') or shutil.which('dotnet'));p.add_argument('--trace-tool-directory',type=pathlib.Path,required=True,help='Official dotnet-trace8.0.547301 tools/net6.0/any directory');a=p.parse_args()
    out=a.out.resolve();out.mkdir(parents=True,exist_ok=False)
    complete=json.loads((a.traces/'complete.json').read_text())
    if complete.get('stage')!='trace' or complete.get('targets')!=3:raise SystemExit('Complete three-target trace stage required')
    if not a.dotnet:raise SystemExit('Set DOTNET or --dotnet to an official .NET8 SDK executable')
    dotnet=pathlib.Path(a.dotnet).resolve();lib=a.trace_tool_directory.resolve();trace=lib/'dotnet-trace.dll'
    provenance=json.loads((HERE/'TOOL_PROVENANCE.json').read_text())['dotnet_trace']
    if sha(lib/'Microsoft.Diagnostics.Tracing.TraceEvent.dll')!=provenance['traceevent_sha256']:raise SystemExit('TraceEvent library differs from provenance')
    env={k:v for k,v in os.environ.items() if not k.startswith(('DOTNET_','COMPlus_','CORECLR_','COR_'))}
    env.update(DOTNET_ROOT=str(dotnet.parent),DOTNET_CLI_HOME=str(out/'dotnet-home'),DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',DOTNET_GENERATE_ASPNET_CERTIFICATE='false')
    state={'protected':False,'pending':False}
    def stopped(sig,frame):
        state['pending']=True
        if not state['protected']:raise SystemExit('Offline analysis interrupted; child group will be reaped')
    signal.signal(signal.SIGTERM,stopped);signal.signal(signal.SIGINT,stopped)
    def run(cmd,label,seconds):
        child=None
        try:
            with (out/(label+'.stdout')).open('x') as stdout,(out/(label+'.stderr')).open('x') as stderr:
                state['protected']=True
                try:child=subprocess.Popen(cmd,stdout=stdout,stderr=stderr,env=env,start_new_session=True)
                finally:state['protected']=False
                if state['pending']:raise SystemExit('Offline analysis interrupted during launch')
                code=child.wait(timeout=seconds)
        finally:
            state['protected']=True
            if child is not None and child.poll() is None:
                try:os.killpg(child.pid,signal.SIGKILL)
                except ProcessLookupError:pass
                child.wait()
            (out/(label+'.receipt.json')).write_text(json.dumps({'command':cmd,'timeout_seconds':seconds,'returncode':None if child is None else child.returncode,'offline_only':True},indent=2)+'\n')
            state['protected']=False
        if state['pending']:raise SystemExit('Offline analysis interrupted; child reaped')
        if code:raise SystemExit('Offline analysis failed: '+label)
    run([str(dotnet),'build',str(HERE/'decoder/PauseDecoder.csproj'),'-c','Release','-v','quiet','-p:TraceToolPath='+str(lib),'-p:BaseIntermediateOutputPath='+str(out/'obj')+'/','-p:OutputPath='+str(out/'bin')+'/','-p:UseSharedCompilation=false','--disable-build-servers'],'decoder-build',60)
    for v in VERSIONS:
        nettrace=(a.traces/(v+'.nettrace')).resolve()
        run([str(dotnet),str(out/'bin/PauseDecoder.dll'),str(nettrace),str(out/(v+'-events'))],v+'-decode',30)
        run([str(dotnet),str(trace),'report',str(nettrace),'topN','--number','30','--inclusive','--verbose'],v+'-sampled-stacks',30)
    (out/'complete.json').write_text(json.dumps({'offline_only':True,'decoder_source_sha256':sha(HERE/'decoder/Program.cs'),'traceevent_sha256':sha(lib/'Microsoft.Diagnostics.Tracing.TraceEvent.dll'),'traces':{v:sha(a.traces/(v+'.nettrace')) for v in VERSIONS},'limitations':'Managed thread sample evidence, not precise on-CPU. Full-process pause events, no phase attribution. No GC-count or elapsed-time causal inference.'},indent=2)+'\n')
if __name__=='__main__':main()
