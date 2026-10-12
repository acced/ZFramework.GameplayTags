#!/usr/bin/env python3
"""Paired, identical-public-API final candidate audit. A green run is not acceptance."""
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
import time

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
BASELINE = '7037e76a43127939ab9545b2e1056441202961e6'


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, allow_nan=False) + '\n')


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--baseline', type=Path, required=True)
    p.add_argument('--candidate', type=Path, default=REPO)
    p.add_argument('--backend', choices=('net', 'mono'), required=True)
    p.add_argument('--dotnet', default=shutil.which('dotnet'))
    p.add_argument('--mono', default=shutil.which('mono'))
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--samples', type=int, default=14)
    p.add_argument('--target-ms', type=int, default=50)
    p.add_argument('--cold-rounds', type=int, default=4)
    args = p.parse_args()
    if not args.dotnet or (args.backend == 'mono' and not args.mono):
        p.error('Installed .NET SDK and, for Mono, an installed Mono host are required.')
    if args.samples < 2 or args.target_ms < 20 or args.cold_rounds < 1:
        p.error('samples >= 2, target-ms >= 20 and cold-rounds >= 1 are required')
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=False)
    env = dict(os.environ)
    for prefix in ('DOTNET_', 'COMPlus_'):
        env[prefix + 'TieredCompilation'] = '0'
        env[prefix + 'gcServer'] = '0'
    sources = {'before': args.baseline.resolve(), 'after': args.candidate.resolve()}
    source_hashes = {label: {str(f.relative_to(src)): sha(f) for f in sorted((src/'Runtime').rglob('*.cs'))} for label, src in sources.items()}
    harness_hashes = {f.name: sha(f) for f in (HERE/'Program.cs', HERE/'Cold.cs', HERE/'run.py', HERE/'FinalConsolidation.csproj')}
    commands = {}
    for label, src in sources.items():
        for probe in ('warm', 'cold'):
            dest = out/'build'/label/probe
            obj = out/'obj'/label/probe
            command = [args.dotnet, 'build', str(HERE/'FinalConsolidation.csproj'), '-c', 'Release',
                       '-p:RuntimeRoot='+str(src/'Runtime'), '-p:BaseIntermediateOutputPath='+str(obj)+'/',
                       '-p:MSBuildProjectExtensionsPath='+str(obj)+'/', '-o', str(dest)]
            if probe == 'cold': command.append('-p:ColdProbe=true')
            if args.backend == 'mono': command.append('-p:MonoTarget=true')
            result = subprocess.run(command, env=env, text=True, capture_output=True)
            (out/(label+'-'+probe+'-build.log')).write_text(result.stdout+result.stderr)
            result.check_returncode()
            commands[label, probe] = [args.mono if args.backend == 'mono' else args.dotnet,
                str(dest/('FinalConsolidation.exe' if args.backend == 'mono' else 'FinalConsolidation.dll'))]
    assembly_hashes = {str(f.relative_to(out)): sha(f) for f in (out/'build').rglob('*') if f.suffix in ('.dll', '.exe')}
    # Pin both versions to one allowed CPU. This is process affinity only; never
    # alter host frequency, OS protections, GC implementation or hardware counters.
    affinity = None
    if hasattr(os, 'sched_getaffinity'):
        allowed = sorted(os.sched_getaffinity(0)); affinity = allowed[len(allowed)//2]
        os.sched_setaffinity(0, {affinity})
    def run(label, probe, arguments):
        result = subprocess.run(commands[label, probe]+list(map(str, arguments)), env=env,
                                text=True, capture_output=True, check=True)
        return result
    for label in sources:
        result = run(label, 'warm', ['test'])
        (out/(label+'-tests.log')).write_text(result.stdout+result.stderr)
    pilot = json.loads(run('before', 'warm', ['bench']).stdout)
    save(out/'pilot.json', pilot)
    counts = {r['Id']: max(100, min(50000000, math.ceil(args.target_ms*1e6/max(1,r['Ns'])))) for r in pilot['Rows']}
    iteration_file = out/'iterations.tsv'
    iteration_file.write_text(''.join(f'{k}\t{v}\n' for k,v in counts.items()))
    reports = {label: [] for label in sources}
    order = []
    for i in range(args.samples):
        for label in (['before','after'] if i%2 == 0 else ['after','before']):
            report = json.loads(run(label, 'warm', ['bench',iteration_file]).stdout)
            reports[label].append(report); order.append([i,label])
            save(out/'raw'/f'{label}-{i:02}.json', report)
            print(f'{args.backend} pair {i+1}/{args.samples} {label}', flush=True)
    first = reports['before'][0]
    for group in reports.values():
        for report in group:
            if report['Checksum'] != first['Checksum'] or [(r['Id'],r['Iterations']) for r in report['Rows']] != [(r['Id'],r['Iterations']) for r in first['Rows']]:
                raise RuntimeError('Different observable results or timed workloads')
    comparisons = []
    for row in first['Rows']:
        key = row['Id']
        b = [next(r for r in d['Rows'] if r['Id']==key) for d in reports['before']]
        a = [next(r for r in d['Rows'] if r['Id']==key) for d in reports['after']]
        logs = [math.log(x['Ns']/y['Ns']) for x,y in zip(b,a)]
        mean = statistics.mean(logs)
        # t(13, 0.995) for exactly 14 pairs. Other sample counts are exploratory.
        width = 3.012*statistics.stdev(logs)/math.sqrt(len(logs)) if len(logs)==14 else None
        lower, upper = (math.exp(mean-width),math.exp(mean+width)) if width is not None else (None,None)
        old_bytes, new_bytes = statistics.median(r['Bytes'] for r in b),statistics.median(r['Bytes'] for r in a)
        status = 'inconclusive'
        if upper is not None and upper < 1/1.03: status='material-timing-regression'
        elif lower is not None and lower > 1.03: status='timing-improvement'
        if new_bytes > old_bytes: status='allocation-regression'
        comparisons.append(dict(Id=key,BeforeNs=statistics.median(r['Ns'] for r in b),AfterNs=statistics.median(r['Ns'] for r in a),
            PairedSpeedup=math.exp(mean),Lower99=lower,Upper99=upper,BeforeBytes=old_bytes,AfterBytes=new_bytes,
            MinBatchMs=min(r['WallMilliseconds'] for r in b+a),SlowerPairs=sum(x['Ns']<y['Ns'] for x,y in zip(b,a)),Status=status))
    save(out/'comparison.json', comparisons)
    cold=[]
    for i in range(args.cold_rounds):
        for mode,n in [('fresh',1),('fresh-deep',128),('clear',0),('clear',1024),('copy',1024),('intersection',1024),('union',1024),('buff',1)]:
            pair=[]
            for label in (['before','after'] if i%2==0 else ['after','before']):
                row=json.loads(run(label,'cold',[mode,n]).stdout)
                row.update(Variant=label,Round=i);cold.append(row);pair.append(row)
            for key in ('ExplicitCount','TagCount','Events'):
                if pair[0][key]!=pair[1][key]:raise RuntimeError('First-use state differs')
    save(out/'cold.json',cold)
    for label,src in sources.items():
        if source_hashes[label] != {str(f.relative_to(src)):sha(f) for f in sorted((src/'Runtime').rglob('*.cs'))}:
            raise RuntimeError('Runtime source changed during measurement')
    if assembly_hashes != {str(f.relative_to(out)):sha(f) for f in (out/'build').rglob('*') if f.suffix in ('.dll','.exe')}:
        raise RuntimeError('Binary changed during measurement')
    cpuinfo=Path('/proc/cpuinfo')
    cpu = next((s for s in cpuinfo.read_text().splitlines() if s.startswith('model name')),None) if cpuinfo.exists() else None
    save(out/'identity.json',dict(BaselineCommit=BASELINE,SourceHashes=source_hashes,HarnessHashes=harness_hashes,AssemblyHashes=assembly_hashes,
        Backend=args.backend,Runtime=first['Runtime'],Framework=first['Framework'],CPUModel=cpu,AffinityCPU=affinity,
        Platform=platform.platform(),Samples=args.samples,TargetMs=args.target_ms,ExecutionOrder=order,
        ColdRounds=args.cold_rounds,Scope='same public API; no Unity execution; statistical flags are not universal acceptance'))
    print('Evidence complete. Performance and first-use regressions must be reviewed separately.', flush=True)

if __name__ == '__main__':
    main()
