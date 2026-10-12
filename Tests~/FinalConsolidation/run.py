#!/usr/bin/env python3
"""Paired current-branch comparison. CI completion is not a claim of universal speedup."""
import argparse, hashlib, json, math, os, platform, statistics, subprocess
from pathlib import Path
HERE = Path(__file__).resolve().parent

def call(args, **kw):
    p = subprocess.run(list(map(str, args)), check=True, text=True, capture_output=True, **kw)
    return p.stdout

def hashes(root):
    return {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in sorted((root / 'Runtime').glob('**/*.cs'))}

def compare(data):
    result = []
    for row in data['before'][0]['Rows']:
        key = row['Id']
        a = [next(r for r in p['Rows'] if r['Id'] == key) for p in data['before']]
        b = [next(r for r in p['Rows'] if r['Id'] == key) for p in data['after']]
        assert all(x['Iterations'] == y['Iterations'] for x, y in zip(a, b))
        logs = [math.log(x['Ns'] / y['Ns']) for x, y in zip(a, b)]
        mean = statistics.mean(logs)
        width = 3.012 * statistics.stdev(logs) / math.sqrt(len(logs)) if len(logs) == 14 else None
        low = math.exp(mean - width) if width is not None else None
        high = math.exp(mean + width) if width is not None else None
        ba, bb = statistics.median(x['Bytes'] for x in a), statistics.median(x['Bytes'] for x in b)
        status = 'unresolved'
        if low is not None and low > 1.03: status = 'improvement'
        if high is not None and high < 1 / 1.03: status = 'regression'
        if bb > ba: status = 'allocation-increase'
        minimum = min(x['WallMilliseconds'] for x in a + b)
        result.append(dict(Id=key, BeforeNs=statistics.median(x['Ns'] for x in a),
            AfterNs=statistics.median(x['Ns'] for x in b), PairedSpeedup=math.exp(mean),
            Approx99Lower=low, Approx99Upper=high, BeforeBytes=ba, AfterBytes=bb,
            SlowerPairs=sum(x['Ns'] < y['Ns'] for x,y in zip(a,b)),
            MinimumBatchMs=minimum, ShortBatch=minimum < 50, Review=status))
    return result

def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--before', type=Path, required=True)
    ap.add_argument('--after', type=Path, required=True)
    ap.add_argument('--out', type=Path, required=True)
    ap.add_argument('--dotnet', default='dotnet')
    ap.add_argument('--mono', default=None)
    ap.add_argument('--samples', type=int, default=14)
    ap.add_argument('--target-ms', type=int, default=150)
    args = ap.parse_args()
    out = args.out.resolve(); out.mkdir(parents=True, exist_ok=False)
    sources = {'before':args.before.resolve(), 'after':args.after.resolve()}
    source_hashes = {k:hashes(v) for k,v in sources.items()}
    env = dict(os.environ, DOTNET_TieredCompilation='0', COMPlus_TieredCompilation='0', DOTNET_gcServer='0', COMPlus_gcServer='0')
    manifest = dict(Platform=platform.platform(), CPU=next((l.split(':',1)[1].strip() for l in Path('/proc/cpuinfo').read_text().splitlines() if l.startswith('model name')), 'unknown') if Path('/proc/cpuinfo').exists() else platform.processor(),
        Host=args.mono or args.dotnet, Samples=args.samples, SourceHashes=source_hashes,
        HarnessHashes={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in HERE.glob('*') if p.is_file()},
        AllocationScope='Current-thread managed allocated bytes, not GC event count, native allocations or retained heap', ExecutionOrder=[])
    manifest['DotnetInfo'] = call([args.dotnet, '--info'])
    if args.mono: manifest['MonoVersion'] = call([args.mono, '--version'])
    assemblies = {}; cold = {}
    for label, source in sources.items():
        for is_cold in [False, True]:
            tag = label + ('-cold' if is_cold else '')
            build = out / tag; obj = out / (tag + '-obj')
            cmd = [args.dotnet, 'build', HERE/'GameplayTags.FinalConsolidation.csproj', '-c','Release',
                '-p:RuntimeRoot='+str(source/'Runtime'), '-p:BaseIntermediateOutputPath='+str(obj)+'/',
                '-p:MSBuildProjectExtensionsPath='+str(obj)+'/', '-o',build]
            if args.mono: cmd.append('-p:MonoTarget=true')
            if is_cold: cmd.append('-p:ColdProbe=true')
            (out/(tag+'-build.txt')).write_text(call(cmd, env=env))
            executable = build / ('GameplayTags.FinalConsolidation' + ('.exe' if args.mono else '.dll'))
            (cold if is_cold else assemblies)[label] = executable
    host = args.mono or args.dotnet
    for label, assembly in assemblies.items():
        result = call([host, assembly, 'test'], env=env)
        (out/(label+'-tests.txt')).write_text(result); print(result, flush=True)
    pilot = json.loads(call([host, assemblies['before'], 'bench'], env=env))
    (out/'pilot.json').write_text(json.dumps(pilot, indent=2))
    counts = {r['Id']:max(1,min(50000000,math.ceil(args.target_ms*1e6/max(1,r['Ns'])))) for r in pilot['Rows']}
    count_file = out/'iterations.tsv'; count_file.write_text(''.join(k+'\t'+str(v)+'\n' for k,v in counts.items()))
    data = {'before':[], 'after':[]}; (out/'raw').mkdir()
    for round_id in range(args.samples):
        for label in (['before','after'] if round_id%2==0 else ['after','before']):
            manifest['ExecutionOrder'].append([round_id,label])
            report = json.loads(call([host, assemblies[label], 'bench', count_file], env=env))
            data[label].append(report)
            (out/'raw'/(label+'-'+str(round_id)+'.json')).write_text(json.dumps(report))
        print('Final supplement pair',round_id+1,'/',args.samples,flush=True)
    assert len({p['Checksum'] for rows in data.values() for p in rows}) == 1, 'Mismatched work'
    rows = compare(data); (out/'comparison.json').write_text(json.dumps(rows, indent=2))
    cold_data = []
    for mode in ['set-shallow','set-deep','count','copy','intersection','workspace','union','empty-clear']:
        for round_id in range(3):
            for label in (['before','after'] if round_id%2==0 else ['after','before']):
                record = json.loads(call([host,cold[label],mode],env=env)); record.update(Variant=label, Pair=round_id)
                cold_data.append(record)
    (out/'cold.json').write_text(json.dumps(cold_data,indent=2))
    assert source_hashes == {k:hashes(v) for k,v in sources.items()}, 'Sources changed during timing'
    manifest['AssemblyHashes'] = {str(p.relative_to(out)):hashlib.sha256(p.read_bytes()).hexdigest() for p in out.glob('*/*') if p.suffix in ('.dll','.exe')}
    (out/'environment.json').write_text(json.dumps(manifest,indent=2))
    for row in rows: print(json.dumps(row,sort_keys=True))

if __name__ == '__main__':
    try: main()
    except subprocess.CalledProcessError as exc:
        print(exc.stdout or ''); print(exc.stderr or ''); raise
