#!/usr/bin/env python3
"""Preserve all 25 legacy Intersection cases against original and predecessor."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import re
import subprocess
import sys

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[1]
INTERSECTION=ROOT/'Tests~/Intersection'

def hashes(paths):return {str(p):hashlib.sha256(Path(p).read_bytes()).hexdigest() for p in paths}
def run(args,**kwargs):
    print('+ '+' '.join(map(str,args)),flush=True)
    return subprocess.run(list(map(str,args)),check=True,text=True,**kwargs)
def expected_cases():
    result={f'E{size}.Overlap{overlap}.{kind}' for size in (64,1024) for overlap in (0,50,100) for kind in ('Default.Reused','Default.New','Workspace.Reused')}
    result.update(('E1024.Ordered.Default.Reused','E1024.Copy.Reused','E1024.Union.New','E1024.Query','E1024.RemoveAdd','Sort.BCL.Segment','Sort.Specialized.Segment'))
    return result

def validate_report(path,raw,rounds):
    report=json.loads(path.read_text());expected=expected_cases()
    if set(report['samples'])!={'before','after'}:raise RuntimeError('Missing legacy variant')
    if report['environment']['before']!=report['environment']['after']:raise RuntimeError('Legacy runtime/registry configuration mismatch')
    for label in ('before','after'):
        if set(report['samples'][label])!=expected:raise RuntimeError('Legacy case matrix differs from all 25 frozen cases')
        for name,row in report['samples'][label].items():
            if len(row['ns'])!=rounds or len(row['bytes'])!=rounds:raise RuntimeError('Incomplete legacy samples')
            if any(not math.isfinite(x) or x<=0 for x in row['ns']):raise RuntimeError('Invalid legacy timing')
            if any(not math.isfinite(x) or x<0 for x in row['bytes']):raise RuntimeError('Invalid legacy allocation')
            if row['iterations']!=report['samples']['before'][name]['iterations']:raise RuntimeError('Legacy per-case iteration mismatch')
    transcripts=[json.loads(p.read_text()) for p in raw.glob('*.json')]
    if len(transcripts)!=2*rounds:raise RuntimeError('Missing raw process transcript')
    sinks=[]
    for transcript in transcripts:
        if transcript['ExitCode']!=0:raise RuntimeError('Failed legacy process transcript')
        lines=[line for line in transcript['Stdout'].splitlines() if line.startswith('SINK\t')]
        if len(lines)!=1:raise RuntimeError('Missing/duplicate legacy observed checksum')
        sinks.append(lines[0])
    if len(set(sinks))!=1:raise RuntimeError('Legacy process observed checksums differ')
    return {'Cases':len(expected),'IndependentProcessPairs':rounds,'RawProcesses':len(transcripts),'ObservedChecksum':sinks[0],'Padding':report['padding']}

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet',type=Path,required=True)
    parser.add_argument('--mono',type=Path)
    parser.add_argument('--before',type=Path,required=True)
    parser.add_argument('--previous',type=Path)
    parser.add_argument('--after',type=Path,required=True)
    parser.add_argument('--before-revision')
    parser.add_argument('--previous-revision')
    parser.add_argument('--after-revision')
    parser.add_argument('--out',type=Path,required=True)
    parser.add_argument('--smoke',action='store_true',help='Two normal pairs; padding still four pairs. Not acceptance timing evidence.')
    parser.add_argument('--compile-only',action='store_true')
    args=parser.parse_args()
    for label in ('before','previous','after'):
        value=getattr(args,label+'_revision')
        if value and not re.fullmatch('[0-9a-f]{40}',value):parser.error('Declared revisions must be full lowercase commit SHAs')
    out=args.out.resolve()
    if out.exists() and any(out.iterdir()):parser.error('Output must be new or empty')
    out.mkdir(parents=True,exist_ok=True)
    sources={'before':args.before.resolve()}
    if args.previous:sources['previous']=args.previous.resolve()
    sources['after']=args.after.resolve()
    checked=[Path(__file__).resolve(),INTERSECTION/'Program.cs',INTERSECTION/'Benchmarks.cs',INTERSECTION/'paired.py',INTERSECTION/'GameplayTags.Intersection.Tests.csproj']
    source_fingerprints={};provenance={}
    for label,source in sources.items():
        files=sorted((source/'Runtime').glob('**/*.cs'))
        if not files:raise RuntimeError('Runtime source missing: '+str(source))
        checked+=files
        source_fingerprints[label]={str(p.relative_to(source)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
        entry={'DeclaredRevision':getattr(args,label+'_revision'),'SourceHashesAuthoritative':True}
        top=subprocess.run(['git','-C',str(source),'rev-parse','--show-toplevel'],text=True,capture_output=True)
        if top.returncode==0 and Path(top.stdout.strip()).resolve()==source:
            entry['Head']=subprocess.check_output(['git','-C',str(source),'rev-parse','HEAD'],text=True).strip()
            entry['RuntimeStatus']=subprocess.check_output(['git','-C',str(source),'status','--porcelain','--','Runtime'],text=True)
            entry['RuntimeDiffSHA256']=hashlib.sha256(subprocess.check_output(['git','-C',str(source),'diff','HEAD','--','Runtime'])).hexdigest()
        else:entry['Git']='Archive: no ancestor repository HEAD inferred'
        provenance[label]=entry
    frozen=hashes(checked)
    manifest={'Protocol':'unchanged Intersection paired.py; all 25 scenarios; no BASELINE macro','Sources':{k:str(v) for k,v in sources.items()},'Provenance':provenance,'SourceHashes':frozen,'RuntimeFiles':source_fingerprints,'Platform':platform.platform(),'Affinity':sorted(os.sched_getaffinity(0)) if hasattr(os,'sched_getaffinity') else None,'Commands':[],'SmokeOnly':args.smoke,'Runtime':'standalone Mono' if args.mono else 'standalone .NET8','PaddingGuard':{'UnusedRegistryTags':50000,'IndependentPairs':4},'Validation':[]}
    def save():(out/'environment.json').write_text(json.dumps(manifest,indent=2))
    save();assemblies={}
    for label,source in sources.items():
        build=out/(label+'-build');obj=out/(label+'-obj')
        call=[str(args.dotnet.resolve()),'build',str(INTERSECTION/'GameplayTags.Intersection.Tests.csproj'),'-c','Release','-p:Baseline=false',f'-p:RuntimeRoot={source/"Runtime"}',f'-p:BaseIntermediateOutputPath={obj}/',f'-p:MSBuildProjectExtensionsPath={obj}/','-o',str(build)]
        if args.mono:call+=['-p:MonoTarget=true']
        manifest['Commands'].append(call);save();run(call)
        assemblies[label]=build/('GameplayTags.Intersection.Tests.exe' if args.mono else 'GameplayTags.Intersection.Tests.dll')
    manifest['AssemblyHashes']=hashes(sorted({p for a in assemblies.values() for p in a.parent.glob('*') if p.suffix in ('.dll','.exe')}))
    host=(args.mono or args.dotnet).resolve()
    info=subprocess.run([str(host),'--version'],text=True,capture_output=True)
    manifest['RuntimeVersion']=info.stdout
    info=subprocess.run([str(args.dotnet.resolve()),'--info'],text=True,capture_output=True)
    manifest['DotnetInfo']={'Stdout':info.stdout,'Stderr':info.stderr,'ExitCode':info.returncode}
    save()
    if hashes(checked)!=frozen:raise RuntimeError('Source changed during legacy compilation')
    if args.compile_only:return
    env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_gcServer='0',COMPlus_TieredCompilation='0',COMPlus_gcServer='0')
    # Every selected assembly runs the complete existing correctness/allocation suite
    # before the first timed process begins.
    for label,assembly in assemblies.items():
        call=[str(host),str(assembly)];manifest['Commands'].append(call);save()
        with (out/(label+'-tests.txt')).open('w') as log:run(call,stdout=log,stderr=subprocess.STDOUT,env=env)
    wrapper=out/'capture-runtime.py'
    wrapper.write_text('''#!/usr/bin/env python3
import json,os,subprocess,sys,uuid
from pathlib import Path
command=[os.environ['LEGACY_HOST']]+sys.argv[1:]
p=subprocess.run(command,text=True,capture_output=True)
raw=Path(os.environ['LEGACY_RAW_DIR']);raw.mkdir(parents=True,exist_ok=True)
(raw/(uuid.uuid4().hex+'.json')).write_text(json.dumps({'Command':command,'ExitCode':p.returncode,'Stdout':p.stdout,'Stderr':p.stderr},indent=2))
sys.stdout.write(p.stdout);sys.stderr.write(p.stderr)
sys.exit(p.returncode if p.returncode>=0 else 128-p.returncode)
''')
    wrapper.chmod(0o755);manifest['RuntimeCaptureSHA256']=hashes([wrapper]);save()
    baselines=['before']
    if 'previous' in sources and source_fingerprints['previous']!=source_fingerprints['before']:baselines.append('previous')
    elif 'previous' in sources:manifest['PreviousDeduplicated']='Runtime bytes identical to original; original comparison covers both'
    for baseline in baselines:
        for padding,rounds,suffix in ((0,2 if args.smoke else 14,'normal'),(50000,4,'padding50000')):
            output=out/(baseline+'-vs-after-'+suffix+'.json');raw=out/(baseline+'-vs-after-'+suffix+'-raw');raw.mkdir()
            call=[sys.executable,str(INTERSECTION/'paired.py'),'--runtime',str(wrapper),'--before',str(assemblies[baseline]),'--after',str(assemblies['after']),'--rounds',str(rounds),'--padding',str(padding),'--output',str(output)]
            manifest['Commands'].append(call);save()
            pair_env=dict(env,LEGACY_HOST=str(host),LEGACY_RAW_DIR=str(raw))
            with output.with_suffix('.stdout.txt').open('w') as stdout,output.with_suffix('.stderr.txt').open('w') as stderr:run(call,stdout=stdout,stderr=stderr,env=pair_env)
            manifest['Validation'].append({'Baseline':baseline,**validate_report(output,raw,rounds)});save()
    if hashes(checked)!=frozen:raise RuntimeError('Source or unchanged legacy harness changed during measurements')
    if hashes(manifest['AssemblyHashes'])!=manifest['AssemblyHashes']:raise RuntimeError('Legacy assembly changed during measurements')
    manifest['Status']='All legacy correctness suites and complete25-case paired raw/checksum/metadata checks passed; performance interpretation remains review-only.'
    save();print('Saved unchanged legacy matrix: '+str(out),flush=True)

if __name__=='__main__':main()
