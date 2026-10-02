#!/usr/bin/env python3
"""Untimed JIT trace of exact archived binaries, on a separate same-architecture runner."""
from pathlib import Path
import os,sys,json,subprocess,hashlib,zipfile,re
RUN=37024028909
HEAD='98311092b7e8892e5632c2863a391187f4367cf8'
def main():
    arch=sys.argv[1];out=Path(sys.argv[2]).resolve();out.mkdir(parents=True,exist_ok=True)
    repo=os.environ['GITHUB_REPOSITORY']
    def api(path):return subprocess.check_output(['gh','api',path])
    rows=json.loads(api(f'/repos/{repo}/actions/runs/{RUN}/artifacts'))['artifacts']
    matches=[a for a in rows if a['name']==f'copy-boundary-{arch}-{RUN}' and not a['expired']];assert len(matches)==1
    a=matches[0];data=api(f'/repos/{repo}/actions/artifacts/{a["id"]}/zip');digest=hashlib.sha256(data).hexdigest()
    assert a['digest']=='sha256:'+digest
    src=out/'input';src.mkdir(exist_ok=True)
    archive=out/'input-original.zip';archive.write_bytes(data)
    with zipfile.ZipFile(archive) as z:
        for n in z.namelist():assert (src/n).resolve().is_relative_to(src)
        z.extractall(src)
    results=src/'results';m=json.loads((results/'manifest.json').read_text());assert m['head']==HEAD
    cpu={'x64':'x86_64','arm64':'aarch64'}[arch];assert m['cpu']==cpu and os.uname().machine==cpu
    (out/'global.json').write_text(json.dumps({'sdk':{'version':'8.0.425','rollForward':'disable'}}))
    assert subprocess.check_output(['dotnet','--version'],cwd=out,text=True).strip()=='8.0.425'
    (out/'runtime.txt').write_text(subprocess.check_output(['dotnet','--info'],cwd=out,text=True))
    (out/'cpu.txt').write_text(subprocess.check_output(['lscpu'],text=True))
    env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_ReadyToRun='0',DOTNET_NOLOGO='1')
    records=[]
    for n in ('control','eager','guarded','isolated','empty','boundary'):
        job=next(j for j in m['jobs'] if j['label']==n and j['stage']=='copy')
        dll=results/job['binary'];assert hashlib.sha256(dll.read_bytes()).hexdigest()==job['sha256']
        asm=out/(n+'.asm')
        variables=dict(env,DOTNET_JitStdOutFile=str(asm),DOTNET_JitDisasm='MixedProbe:CopyCaseLoop *DirectArraySet:CopyCore *DirectArraySet:CopyFrom *DirectArraySet:Clear *DirectArraySet:CopyDenseToMicro *DirectArraySet:CopyDenseGrowing')
        p=subprocess.run(['dotnet',str(dll),n,'copy',str(out/(n+'-diagnostic.json')),'0'],cwd=out,env=variables,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
        (out/(n+'-stdout.log')).write_text(p.stdout);assert p.returncode==0,p.stdout
        d=json.loads((out/(n+'-diagnostic.json')).read_text());assert d['runtime']=='.NET 8.0.31' and len(d['rows'])==192
        text=asm.read_text();assert 'MIXED_MEASURED' not in text
        core=results/'generated'/n/'DirectArraySet.cs'
        methods=[dict(method=method,bytes=int(size)) for method,body,size in re.findall(r'Assembly listing for method ([^\n]+)\n(.*?); Total bytes of code (\d+)',text,re.S)]
        if n in ('guarded','isolated','empty','boundary'):assert any('CopyDenseGrowing' in r['method'] for r in methods)
        records.append(dict(name=n,core_sha256=hashlib.sha256(core.read_bytes()).hexdigest(),binary_sha256=job['sha256'],methods=methods))
    result=dict(head=HEAD,run=RUN,architecture=arch,original_artifact=a['id'],original_sha256=digest,records=records,
        diagnostic_only=True,new_performance_samples=0,recompiled=False,same_original_machine=False,nativeUnity='not_run')
    (out/'TRACE.json').write_text(json.dumps(result,indent=2))
    print('COPY_BOUNDARY_TRACE '+json.dumps(result),flush=True)
if __name__=='__main__':main()
