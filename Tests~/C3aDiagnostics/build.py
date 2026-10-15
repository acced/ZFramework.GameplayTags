#!/usr/bin/env python3
"""Source-verified build-only reproduction. No target invocation or network fetch."""
import argparse,hashlib,json,os,pathlib,shutil,signal,subprocess
HERE=pathlib.Path(__file__).resolve().parent
REPO=HERE.parent.parent
VERSIONS=('B11','C1','current792')
def sha(p):return hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()
def verify(root,expected):
    actual={p.name:sha(p) for p in sorted(root.glob('*.cs'))}
    if actual!=expected:raise SystemExit('Runtime source inventory differs: '+str(root))
def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--out',type=pathlib.Path,required=True);p.add_argument('--dotnet',default=os.environ.get('DOTNET') or shutil.which('dotnet'));p.add_argument('--current792-runtime',type=pathlib.Path,help='Optional exact seven-file Runtime source directory if the historical git object is unavailable');a=p.parse_args()
    if not a.dotnet:raise SystemExit('Set DOTNET or pass --dotnet to an official .NET8 SDK executable')
    dotnet=pathlib.Path(a.dotnet).resolve()
    if not dotnet.is_file():raise SystemExit('dotnet executable missing')
    out=a.out.resolve();out.mkdir(parents=True,exist_ok=False)
    sources=json.loads((HERE/'SOURCE_MANIFEST.json').read_text());runtimes={}
    for v in VERSIONS:
        entry=sources[v]
        if v=='current792' and a.current792_runtime:
            root=a.current792_runtime.resolve();verify(root,entry['files']);runtimes[v]=root
        elif entry['repository_path'] and (REPO/entry['repository_path']).is_dir():
            root=REPO/entry['repository_path'];verify(root,entry['files']);runtimes[v]=root
        elif v=='current792':
            root=out/'source-current792';root.mkdir()
            for name,expected in entry['files'].items():
                if a.current792_runtime:data=(a.current792_runtime/name).read_bytes()
                else:
                    cmd=['git','-C',str(REPO),'show',entry['commit']+':Runtime/'+name]
                    try:data=subprocess.check_output(cmd,stderr=subprocess.PIPE,timeout=10)
                    except (subprocess.CalledProcessError,subprocess.TimeoutExpired) as e:raise SystemExit('Exact current792 source unavailable. Supply --current792-runtime or obtain the documented commit from the repository origin yourself. No automatic network fetch is performed.') from e
                if hashlib.sha256(data).hexdigest()!=expected:raise SystemExit('Historical source hash mismatch: '+name)
                (root/name).write_bytes(data)
            verify(root,entry['files']);runtimes[v]=root
        else:raise SystemExit('Required vendored source directory missing: '+v)
    env={k:v for k,v in os.environ.items() if not k.startswith(('DOTNET_','COMPlus_','CORECLR_','COR_'))}
    env.update(DOTNET_ROOT=str(dotnet.parent),DOTNET_CLI_HOME=str(out/'dotnet-home'),DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',DOTNET_GENERATE_ASPNET_CERTIFICATE='false')
    manifest={'dotnet':str(dotnet),'source_manifest_sha256':sha(HERE/'SOURCE_MANIFEST.json'),'harness':{p.name:sha(p) for p in (HERE/'harness').iterdir() if p.is_file()},'runtime_paths':{v:str(p) for v,p in runtimes.items()},'versions':{}}
    signal_state={'protected':False,'pending':False}
    def stopped(sig,frame):
        signal_state['pending']=True
        if not signal_state['protected']:raise SystemExit('Build interrupted; active child group will be reaped')
    signal.signal(signal.SIGTERM,stopped);signal.signal(signal.SIGINT,stopped)
    for v in VERSIONS:
        binpath=out/('bin-'+v)
        cmd=[str(dotnet),'build',str(HERE/'harness/C3aNaturalDiagnostics.csproj'),'-c','Release','-v','quiet','-p:RuntimePath='+str(runtimes[v]),'-p:BaseIntermediateOutputPath='+str(out/('obj-'+v))+'/','-p:OutputPath='+str(binpath)+'/','-p:UseSharedCompilation=false','--disable-build-servers']
        with (out/(v+'.build.log')).open('x') as log:
            child=None
            try:
                signal_state['protected']=True
                try:child=subprocess.Popen(cmd,stdout=log,stderr=subprocess.STDOUT,env=env,start_new_session=True)
                finally:signal_state['protected']=False
                if signal_state['pending']:raise SystemExit('Interrupted during build launch')
                code=child.wait(timeout=60)
            finally:
                signal_state['protected']=True
                if child is not None and child.poll() is None:
                    try:os.killpg(child.pid,signal.SIGKILL)
                    except ProcessLookupError:pass
                    child.wait()
                signal_state['protected']=False
            if signal_state['pending']:raise SystemExit('Build interrupted; child reaped')
        if code:raise SystemExit('Build failed: '+v)
        for name,root in runtimes.items():verify(root,sources[name]['files'])
        manifest['versions'][v]={'dll':str(binpath/'C3aNaturalDiagnostics.dll'),'files':{str(p):sha(p) for p in sorted(binpath.iterdir()) if p.is_file()}}
    (out/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    print('Built three binaries. No diagnostic targets were launched.')
if __name__=='__main__':main()
