#!/usr/bin/env python3
"""Frozen public-API correctness and paired standalone .NET 8 audit; no Unity claims."""
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

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent

def version(path):
    return tuple(int(x) for x in path.name.split('-')[0].split('.') if x.isdigit())

def digest(paths):
    return {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}

def run(command, **kwargs):
    print('+ ' + ' '.join(map(str, command)), flush=True)
    return subprocess.run(list(map(str, command)), check=True, text=True, **kwargs)

def compile_variant(dotnet, source, output, mono=None):
    if mono:
        output.mkdir(parents=True)
        target=output/'Evolution';obj=output/'obj'
        run([dotnet,'build',HERE/'GameplayTags.Evolution.Tests.csproj','-c','Release','-p:MonoTarget=true',f'-p:RuntimeRoot={source/"Runtime"}',f'-p:BaseIntermediateOutputPath={obj}/',f'-p:MSBuildProjectExtensionsPath={obj}/','-o',target])
        return target/'GameplayTags.Evolution.Tests.exe'
    run([sys.executable, ROOT/'Tests~/run.py', 'build', '--dotnet', dotnet, '--source', source, '--output-dir', output])
    sdks=sorted((dotnet.parent/'sdk').glob('*'),key=version)
    packs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)
    runtimes=sorted((dotnet.parent/'shared/Microsoft.NETCore.App').glob('8.*'),key=version)
    runtime=output/'GameplayTags.Runtime/GameplayTags.Runtime.dll'
    target=output/'Evolution';target.mkdir()
    assembly=target/'Evolution.dll'
    refs=list((packs[-1]/'ref/net8.0').glob('*.dll'))+[runtime]
    flags=['-nologo','-optimize+','-langversion:9.0','-nullable:disable','-deterministic+','-target:exe',f'-out:"{assembly}"']
    flags += [f'-r:"{p}"' for p in sorted(refs)]
    flags += [f'"{HERE/"Program.cs"}"']
    rsp=target/'compile.rsp';rsp.write_text('\n'.join(flags))
    run([dotnet,sdks[-1]/'Roslyn/bincore/csc.dll','@'+str(rsp)])
    shutil.copy2(runtime,target/runtime.name)
    assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':runtimes[-1].name},'configProperties':{'System.Runtime.TieredCompilation':False,'System.GC.Server':False}}},indent=2))
    return assembly

def comparisons(reports):
    if 'after' not in reports:return []
    pairs=[('before','after')]
    if 'previous' in reports:pairs.append(('previous','after'))
    result=[]
    for baseline,candidate in pairs:
        for original in reports[baseline][0]['Rows']:
            key=original['Id']
            left=[next(r for r in p['Rows'] if r['Id']==key) for p in reports[baseline]]
            right=[next(r for r in p['Rows'] if r['Id']==key) for p in reports[candidate]]
            if 'retained-' in key:
                a=statistics.median(r['RetainedBytes'] for r in left)/original['Cohort']
                b=statistics.median(r['RetainedBytes'] for r in right)/original['Cohort']
                status='retained-review' if b>a*1.05+16 else 'retained-no-flag'
                result.append(dict(Baseline=baseline,Variant=candidate,Id=key,BeforeRetainedPerObject=a,AfterRetainedPerObject=b,Status=status))
                continue
            logs=[math.log(a['Ns']/b['Ns']) for a,b in zip(left,right)]
            mean=statistics.mean(logs)
            # Conservative approximate 99% interval (t=3.012 for 14 pairs).
            width=3.012*statistics.stdev(logs)/math.sqrt(len(logs)) if len(logs)>=14 else float('inf')
            lower=math.exp(mean-width) if math.isfinite(width) else 0
            upper=math.exp(mean+width) if math.isfinite(width) else None
            before_bytes=statistics.median(r['Bytes'] for r in left)
            after_bytes=statistics.median(r['Bytes'] for r in right)
            status='inconclusive'
            if upper is not None and upper<1/1.03:status='timing-regression'
            elif lower>1.03:status='timing-improvement'
            if any(r['WallMilliseconds']<50 for r in left+right):status='short-batch-review'
            if after_bytes>before_bytes:status='allocation-regression'
            result.append(dict(Baseline=baseline,Variant=candidate,Id=key,BeforeNs=statistics.median(r['Ns'] for r in left),AfterNs=statistics.median(r['Ns'] for r in right),PairedGeometricSpeedup=math.exp(mean),Approx99Lower=lower,Approx99Upper=upper,BeforeBytes=before_bytes,AfterBytes=after_bytes,Status=status))
    return result

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet',default=shutil.which('dotnet'))
    parser.add_argument('--before',type=Path,required=True)
    parser.add_argument('--before-revision',help='Explicit provenance label for frozen archive; file hashes remain authoritative')
    parser.add_argument('--after-revision')
    parser.add_argument('--mono',type=Path,help='Standalone Mono host; optional net472 build using existing installed package cache')
    parser.add_argument('--after',type=Path)
    parser.add_argument('--previous',type=Path,help='Last accepted frozen runtime; measured in same forward/reverse rounds')
    parser.add_argument('--previous-revision')
    parser.add_argument('--out',type=Path,required=True)
    parser.add_argument('--samples',type=int,default=14)
    parser.add_argument('--padding',type=int,default=0)
    parser.add_argument('--test-only',action='store_true')
    parser.add_argument('--batch-ms',type=int,default=200,help='baseline pilot target duration; shared fixed per-row iterations')
    parser.add_argument('--iterations-file',type=Path,help='Reuse a complete reviewed TSV iteration map, e.g. raise counts for a much faster deep-union candidate')
    args=parser.parse_args()
    if args.previous and not args.after:parser.error('--previous requires --after')
    if not args.dotnet:parser.error('Pass the installed .NET 8 SDK host; this runner does not install anything')
    if args.samples<1 or args.padding<0 or args.batch_ms<50:parser.error('samples must be positive, padding nonnegative, batch-ms at least 50')
    dotnet=Path(args.dotnet).resolve();out=args.out.resolve()
    if out.exists() and any(out.iterdir()):parser.error('Output directory must be absent or empty')
    out.mkdir(parents=True,exist_ok=True)
    sources={'before':args.before.resolve()}
    if args.previous:sources['previous']=args.previous.resolve()
    if args.after:sources['after']=args.after.resolve()
    files=[HERE/'Program.cs',HERE/'run.py',HERE/'GameplayTags.Evolution.Tests.csproj',ROOT/'Tests~/run.py']
    for source in sources.values():files+=sorted((source/'Runtime').glob('**/*.cs'))
    before_hash=digest(files)
    manifest={'Platform':platform.platform(),'Machine':platform.machine(),'Python':sys.version,'Dotnet':str(dotnet),'Sources':{k:str(v) for k,v in sources.items()},'FileHashes':before_hash,'Padding':args.padding,'Samples':args.samples,'Mode':'standalone Mono' if args.mono else 'standalone .NET 8','Settings':{'TieredCompilation':False,'ServerGC':False},'Commands':sys.argv,'Versions':{},'ExecutionOrder':[]}
    for label,source in sources.items():
        root=subprocess.run(['git','-C',str(source),'rev-parse','--show-toplevel'],text=True,capture_output=True)
        provenance={'DeclaredRevision':{'before':args.before_revision,'previous':args.previous_revision,'after':args.after_revision}[label],'SourceHashesAuthoritative':True}
        if root.returncode==0 and Path(root.stdout.strip()).resolve()==source:
            p=subprocess.run(['git','-C',str(source),'rev-parse','HEAD'],text=True,capture_output=True,check=True)
            status=subprocess.run(['git','-C',str(source),'status','--porcelain','--','Runtime'],capture_output=True,check=True)
            diff=subprocess.run(['git','-C',str(source),'diff','HEAD','--','Runtime'],capture_output=True,check=True)
            provenance.update(Head=p.stdout.strip(),RuntimeDirty=bool(status.stdout),RuntimeStatus=status.stdout.decode(),RuntimeDiffSHA256=hashlib.sha256(diff.stdout).hexdigest())
        else:provenance['Git']='Archive or non-repository root; no ancestor repository HEAD used'
        manifest['Versions'][label]=provenance
    assemblies={label:compile_variant(dotnet,source,out/label,args.mono) for label,source in sources.items()}
    host=args.mono.resolve() if args.mono else dotnet
    manifest['AssemblyHashes']=digest(sorted({p for assembly in assemblies.values() for p in assembly.parent.glob('*') if p.suffix in ('.dll','.exe')}))
    host_version=subprocess.run([str(host),'--version'],text=True,capture_output=True)
    manifest['HostVersion']=host_version.stdout.strip()
    manifest['Processor']=platform.processor()
    manifest['Affinity']=sorted(os.sched_getaffinity(0)) if hasattr(os,'sched_getaffinity') else 'unavailable'
    cpuinfo=Path('/proc/cpuinfo')
    if cpuinfo.exists():manifest['CPUModel']=next((line.split(':',1)[1].strip() for line in cpuinfo.read_text().splitlines() if line.startswith('model name')),'unavailable')
    sdk_info=subprocess.run([str(dotnet),'--info'],text=True,capture_output=True)
    manifest['DotnetInfo']={'ExitCode':sdk_info.returncode,'Stdout':sdk_info.stdout,'Stderr':sdk_info.stderr}
    env=os.environ.copy();env['DOTNET_TieredCompilation']='0';env['DOTNET_gcServer']='0';env['COMPlus_TieredCompilation']='0';env['COMPlus_gcServer']='0'
    # All compilation and correctness runs finish before any timing starts.
    for label,assembly in assemblies.items():
        p=run([host,assembly,'test',args.padding],capture_output=True,env=env)
        (out/(label+'-tests.txt')).write_text(p.stdout+p.stderr);print(p.stdout,flush=True)
    reports={label:[] for label in sources}
    if not args.test_only:
        (out/'raw').mkdir()
        pilot=run([host,assemblies['before'],'bench',args.padding],capture_output=True,env=env)
        (out/'pilot.json').write_text(pilot.stdout)
        calibration=json.loads(pilot.stdout)
        fixed={r['Id']:max(r['Iterations'],min(50000000,math.ceil(args.batch_ms*1000000/max(r['Ns'],1)))) for r in calibration['Rows'] if r['Iterations']}
        if args.iterations_file:
            override={}
            for line in args.iterations_file.read_text().splitlines():
                key,value=line.split('\t')
                if key in override:raise RuntimeError('Duplicate iteration override ID')
                override[key]=int(value)
            if set(override)!=set(fixed) or any(v<1 for v in override.values()):raise RuntimeError('Iteration override must contain every timed row exactly once, positive counts only')
            fixed=override
            manifest['IterationOverrideSHA256']=hashlib.sha256(args.iterations_file.read_bytes()).hexdigest()
        iterations_file=out/'iterations.tsv';iterations_file.write_text(''.join(f'{key}\t{value}\n' for key,value in fixed.items()))
        manifest['IterationCalibration']='Reviewed TSV override' if args.iterations_file else 'Before-only pilot; one immutable iteration count per row shared by both variants and all rounds'
        manifest['TargetBatchMilliseconds']=args.batch_ms
        for round_index in range(args.samples):
            labels=list(assemblies)
            if round_index%2:labels.reverse()
            for label in labels:
                manifest['ExecutionOrder'].append([round_index,label])
                p=run([host,assemblies[label],'bench',args.padding,iterations_file],capture_output=True,env=env)
                (out/'raw'/f'{label}-{round_index}.json').write_text(p.stdout)
                if p.stderr:(out/'raw'/f'{label}-{round_index}.stderr.txt').write_text(p.stderr)
                report=json.loads(p.stdout)
                rows=report['Rows'];ids=[r['Id'] for r in rows]
                if len(ids)!=len(set(ids)):raise RuntimeError('Duplicate row ID')
                reports[label].append(report)
        first=reports['before'][0]
        for label,version_reports in reports.items():
            for report in version_reports:
                for key in ('Schema','Runtime','Framework','RegistryCount','Checksum'):
                    if report[key]!=first[key]:raise RuntimeError(f'Mismatch {label}: {key}')
                if [(r['Id'],r['Iterations'],r['Cohort']) for r in report['Rows']] != [(r['Id'],r['Iterations'],r['Cohort']) for r in first['Rows']]:raise RuntimeError('Workload rows differ')
        (out/'comparison.json').write_text(json.dumps(comparisons(reports),indent=2))
    if digest(files)!=before_hash:raise RuntimeError('Source or harness changed during validation; discard the run')
    manifest['Validation']='Correctness passed; immutable sources and matching workloads verified. Timing classifications are review flags, not blanket acceptance.'
    (out/'environment.json').write_text(json.dumps(manifest,indent=2))
    print('Saved verified audit: '+str(out))

if __name__=='__main__':
    try:main()
    except subprocess.CalledProcessError as error:
        if error.stdout:print(error.stdout,file=sys.stderr)
        if error.stderr:print(error.stderr,file=sys.stderr)
        sys.exit(error.returncode or 1)
