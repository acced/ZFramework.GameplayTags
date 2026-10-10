#!/usr/bin/env python3
"""Separate adjacent-action protocol. Never edits the frozen Evolution harness."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import statistics
import subprocess
import sys

HERE=Path(__file__).resolve().parent
ORIGINAL=HERE.parent/'Evolution'/'Program.cs'

def hashes(paths):return {str(p):hashlib.sha256(Path(p).read_bytes()).hexdigest() for p in paths}
def version(p):return tuple(int(x) for x in p.name.split('-')[0].split('.') if x.isdigit())
def command(args,**kwargs):
    print('+ '+' '.join(map(str,args)),flush=True)
    return subprocess.run(list(map(str,args)),check=True,text=True,**kwargs)

def adapt(source):
    # Replace only the timing/retention/reporting plumbing. All fixture generation,
    # Action bodies, checksum updates and preflight/postflight statements remain verbatim.
    start=source.index('    private static Row Measure(')
    end=source.index('    [MethodImpl(MethodImplOptions.NoInlining)] private static Row Retained(',start)
    original_region=source[start:end]
    if original_region.count('private static Row Measure(')!=1:
        raise RuntimeError('Frozen timing methods do not match supported adapter structure')
    replacement='''    private static Row Measure(string id, Action action, int iterations)
    {
        if(TestOnly) {action();return new Row{Id=id};}
        var sample=EvolutionAdjacent.Bridge.Measure(id,action,iterations,()=>Checksum);
        return new Row{Id=id,Ns=sample.Ns,Bytes=sample.Bytes,Iterations=sample.Iterations,
            WallMilliseconds=sample.WallMilliseconds,CpuMilliseconds=sample.CpuMilliseconds,
            Gen0=sample.Gen0,Gen1=sample.Gen1,Gen2=sample.Gen2};
    }
'''
    result=source[:start]+replacement+source[end:]
    lines=result.splitlines(keepends=True)
    targets=[i for i,line in enumerate(lines) if 'Console.WriteLine("{\\"Schema\\":1' in line]
    if len(targets)!=1:raise RuntimeError('Frozen final report statement changed; review adapter')
    report_line=lines[targets[0]]
    lines[targets[0]]='        EvolutionAdjacent.Bridge.Complete(Checksum,GameplayTagManager.TagCount);\n'
    return ''.join(lines),{'ReplacedTimingRegionSHA256':hashlib.sha256(original_region.encode()).hexdigest(),'ReplacedReportLineSHA256':hashlib.sha256(report_line.encode()).hexdigest()}

def compare(reports):
    result=[]
    labels=reports[0][0]['Labels']
    pairs=[('before','after')]+([('previous','after')] if 'previous' in labels else [])
    rows=[[r for r in report if r['Type']=='case'] for report in reports]
    for baseline,candidate in pairs:
        for index,row in enumerate(rows[0]):
            left=[sample[index]['Samples'][baseline] for sample in rows]
            right=[sample[index]['Samples'][candidate] for sample in rows]
            logs=[math.log(a['Ns']/b['Ns']) for a,b in zip(left,right)]
            average=statistics.mean(logs)
            width=3.012*statistics.stdev(logs)/math.sqrt(len(logs)) if len(logs)>=14 else math.inf
            low=math.exp(average-width) if math.isfinite(width) else None
            high=math.exp(average+width) if math.isfinite(width) else None
            flags=[]
            if low is not None and low>1.03:flags.append('timing-improvement')
            if high is not None and high<1/1.03:flags.append('timing-regression')
            a_bytes=statistics.median(x['Bytes'] for x in left);b_bytes=statistics.median(x['Bytes'] for x in right)
            if b_bytes>a_bytes:flags.append('allocation-regression')
            if b_bytes<a_bytes:flags.append('allocation-improvement')
            if any(x['WallMilliseconds']<50 for x in left+right):flags.append('short-batch-review')
            result.append({'Baseline':baseline,'Variant':candidate,'Id':row['Id'],'BeforeNs':statistics.median(x['Ns'] for x in left),'AfterNs':statistics.median(x['Ns'] for x in right),'BeforeBytes':a_bytes,'AfterBytes':b_bytes,'PairedGeometricSpeedup':math.exp(average),'Approx99Lower':low,'Approx99Upper':high,'Flags':flags,'IndependentProcessRounds':len(logs)})
    return result

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--before',type=Path,required=True)
    parser.add_argument('--after',type=Path,required=True)
    parser.add_argument('--previous',type=Path)
    parser.add_argument('--out',type=Path,required=True)
    parser.add_argument('--iterations-file',type=Path,required=True)
    parser.add_argument('--dotnet',type=Path,default=Path('/tmp/gameplaytags-dotnet/dotnet'))
    parser.add_argument('--mono',type=Path)
    parser.add_argument('--nuget',type=Path,default=Path('/tmp/gameplaytags-nuget'))
    parser.add_argument('--rounds',type=int,default=14)
    parser.add_argument('--padding',type=int,default=0)
    parser.add_argument('--mode', choices=['aa','ab'], required=True)
    parser.add_argument('--before-revision',required=True)
    parser.add_argument('--after-revision',required=True)
    parser.add_argument('--skip-inspection',action='store_true')
    parser.add_argument('--compile-only',action='store_true')
    parser.add_argument('--generate-only',action='store_true',help='prepare adapter source and manifest, without compiler or execution')
    args=parser.parse_args()
    if args.rounds<1 or args.padding<0:parser.error('positive rounds and nonnegative padding required')
    out=args.out.resolve()
    if out.exists() and any(out.iterdir()):parser.error('Output must be new or empty')
    out.mkdir(parents=True,exist_ok=True)
    sources={'before':args.before.resolve()}
    if args.previous:sources['previous']=args.previous.resolve()
    sources['after']=args.after.resolve()
    fixed={}
    for line in args.iterations_file.read_text().splitlines():
        key,value=line.split('\t')
        if key in fixed or int(value)<1:raise RuntimeError('Iteration IDs must be unique with positive counts')
        fixed[key]=int(value)
    iteration_path=out/'iterations.tsv';iteration_path.write_text(''.join(f'{k}\t{v}\n' for k,v in fixed.items()))
    original=ORIGINAL.read_text();generated,transformation=adapt(original)
    checked=[ORIGINAL,HERE/'run.py',HERE/'Bridge.cs',HERE/'Host.cs',HERE/'Inspection.cs',args.iterations_file.resolve()]
    runtime_sources={}
    for label,source in sources.items():
        runtime_sources[label]=[p for p in sorted((source/'Runtime').glob('**/*.cs')) if p.name!='GameObjectGameplayTagContainer.cs']
        if not runtime_sources[label]:raise RuntimeError('No runtime sources: '+str(source))
        checked+=runtime_sources[label]
    frozen=hashes(checked)
    generated_files={}
    for label in sources:
        path=out/(label+'-Program.generated.cs');path.write_text(generated);generated_files[label]=path
    manifest={'Protocol':'guard-recheck-v1','Mode':args.mode,'SlotSources':{'before':'before','after':'before' if args.mode=='aa' else 'after'},'DeclaredRevisions':{'before':args.before_revision,'after':args.after_revision},'SourceHashes':frozen,'GeneratedSourceHashes':hashes(generated_files.values()),'Transformation':transformation,'Sources':{k:str(v) for k,v in sources.items()},'Iterations':fixed,'TimingBoundary':'existing Action bodies only; both producer threads parked; direct Action invocation in host; no retained measurement','OriginalHarnessRetainedRows':'original64-object cohort procedure retained between fixture groups; not used for retention claims','Commands':[],'Rounds':args.rounds,'Padding':args.padding,'Platform':platform.platform(),'Affinity':sorted(os.sched_getaffinity(0)) if hasattr(os,'sched_getaffinity') else None,'AllSameRuntimeSources':len({tuple(sorted((str(p.relative_to(sources[label])),hashlib.sha256(p.read_bytes()).hexdigest()) for p in paths)) for label,paths in runtime_sources.items()})==1}
    (out/'environment.json').write_text(json.dumps(manifest,indent=2))
    if args.generate_only:
        print('Generated reviewed adapter only; no compilation or execution.');return
    dotnet=args.dotnet.resolve();sdk=sorted((dotnet.parent/'sdk').glob('*'),key=version)[-1]
    host=out/('AdjacentHost.exe' if args.mono else 'AdjacentHost.dll')
    if args.mono:
        framework=args.nuget/'microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2'
        references=[framework/name for name in ('mscorlib.dll','System.dll','System.Core.dll','System.Runtime.Serialization.dll')]
        packages=[args.nuget/path for path in ('system.memory/4.5.5/lib/net461/System.Memory.dll','system.buffers/4.5.1/lib/net461/System.Buffers.dll','system.runtime.compilerservices.unsafe/4.5.3/lib/net461/System.Runtime.CompilerServices.Unsafe.dll','system.numerics.vectors/4.5.0/lib/net46/System.Numerics.Vectors.dll')]
        references+=packages
        for package in packages:(out/package.name).write_bytes(package.read_bytes())
    else:
        pack=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]
        references=sorted((pack/'ref/net8.0').glob('*.dll'))
    def compile(assembly,files,extra=(),executable=False):
        flags=['-nologo','-optimize+','-langversion:9.0','-nullable:disable','-deterministic+','-target:exe' if executable else '-target:library',f'-out:"{assembly}"']
        flags+=[f'-r:"{p}"' for p in references+list(extra)]+[f'"{p}"' for p in files]
        rsp=assembly.with_suffix('.rsp');rsp.write_text('\n'.join(flags))
        invocation=[str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)]
        manifest['Commands'].append(invocation);command(invocation)
    compile(host,[HERE/'Bridge.cs',HERE/'Host.cs',HERE/'Inspection.cs'],executable=True)
    assemblies={}
    for label in sources:
        assembly=out/('Evolution'+label.title()+'.dll');compile(assembly,runtime_sources[label]+[generated_files[label]],[host]);assemblies[label]=assembly
    runtime=sorted((dotnet.parent/'shared/Microsoft.NETCore.App').glob('8.*'),key=version)[-1]
    host.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':runtime.name},'configProperties':{'System.Runtime.TieredCompilation':False,'System.GC.Server':False}}},indent=2))
    manifest['AssemblyHashes']=hashes([host]+list(assemblies.values()))
    manifest['ReferenceHashes']=hashes(references)
    executable=args.mono.resolve() if args.mono else dotnet
    info=subprocess.run([str(executable),'--version'],text=True,capture_output=True)
    manifest['HostVersion']=info.stdout
    manifest['DotnetInfo']=subprocess.check_output([str(dotnet),'--info'],text=True)
    cpu=Path('/proc/cpuinfo')
    if cpu.exists():manifest['CPUModel']=next((line.split(':',1)[1].strip() for line in cpu.read_text().splitlines() if line.startswith('model name')),'unavailable')
    invocation=[str(executable),str(host),'--round','0','--mode',args.mode,'--inspect','0','--padding',str(args.padding),'--iterations',str(iteration_path),'--out',str(out/'raw-0.jsonl')]
    for label,path in assemblies.items():invocation+=['--assembly-'+label,str(path)]
    manifest['HostCommandTemplate']=invocation
    (out/'environment.json').write_text(json.dumps(manifest,indent=2))
    if hashes(checked)!=frozen:raise RuntimeError('Source changed during compilation; discard assembly set')
    if args.compile_only:
        print('Compiled all distinct identities; no host execution.');return
    reports=[];env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_gcServer='0',COMPlus_TieredCompilation='0',COMPlus_gcServer='0')
    if not args.skip_inspection:
        inspection=list(invocation);inspection[inspection.index('--inspect')+1]='1';inspection[inspection.index('--out')+1]=str(out/'inspection.jsonl')
        inspect_env=dict(env,COMPlus_JitDisasm='*SetIntersection* *ContainsExplicit* *Find* *BuildFromExplicitIds* *Benchmark* *Measure*',COMPlus_JitStdOutFile=str(out/'jit-disassembly.txt'))
        manifest['Commands'].append(inspection)
        with (out/'inspection.stdout.txt').open('w') as stdout,(out/'inspection.stderr.txt').open('w') as stderr:command(inspection,stdout=stdout,stderr=stderr,env=inspect_env)
    for round_index in range(args.rounds):
        call=list(invocation);call[call.index('--round')+1]=str(round_index);path=out/f'raw-{round_index}.jsonl';call[call.index('--out')+1]=str(path)
        manifest['Commands'].append(call)
        with (out/f'round-{round_index}.stdout.txt').open('w') as stdout,(out/f'round-{round_index}.stderr.txt').open('w') as stderr:
            command(call,stdout=stdout,stderr=stderr,env=env)
        report=[json.loads(line) for line in path.read_text().splitlines()]
        if report[-1]['Type']!='complete':raise RuntimeError('Incomplete host run; preserve failure/raw data')
        reports.append(report)
        print(f'Adjacent independent process round {round_index+1}/{args.rounds}',flush=True)
    if hashes(checked)!=frozen or hashes(generated_files.values())!=manifest['GeneratedSourceHashes']:raise RuntimeError('Source changed during sampling; discard comparison')
    if hashes([host]+list(assemblies.values()))!=manifest['AssemblyHashes']:raise RuntimeError('Compiled assembly changed during sampling; discard comparison')
    reference=reports[0]
    for report in reports:
        for key in ('Protocol','Runtime','StandaloneMono','PointerSize','Padding','Labels'):
            if report[0][key]!=reference[0][key]:raise RuntimeError('Host configuration mismatch: '+key)
        if report[-1]!=reference[-1]:raise RuntimeError('Completed checksum/registry/case metadata differ between rounds')
        for row in report:
            if row['Type']=='case':
                if any(sample['Iterations']!=fixed[row['Id']] for sample in row['Samples'].values()):raise RuntimeError('Measured iterations differ from frozen map')
        if [r['Id'] for r in report if r['Type']=='case']!=[r['Id'] for r in reference if r['Type']=='case']:raise RuntimeError('Case order differs between rounds')
    (out/'comparison.json').write_text(json.dumps(compare(reports),indent=2))
    manifest['Validation']='Original fixtures/Actions/preflight/postflight/retained procedure preserved; six selected controls timed; other Actions executed65times outside timers; same-delegate A/A or distinct-assembly A/B; supplemental only, all full-matrix flags remain.'
    (out/'environment.json').write_text(json.dumps(manifest,indent=2))

if __name__=='__main__':main()
