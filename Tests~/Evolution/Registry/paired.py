#!/usr/bin/env python3
"""Fixed startup fixtures in alternating paired processes; compile before timing."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import statistics
import subprocess
import sys

HERE = Path(__file__).resolve().parent


def file_hashes(paths):
    return {str(path): hashlib.sha256(Path(path).read_bytes()).hexdigest() for path in paths}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--before', type=Path, required=True)
    parser.add_argument('--after', type=Path, required=True)
    parser.add_argument('--before-revision', help='declared provenance label for a source archive; hashes remain authoritative')
    parser.add_argument('--after-revision', help='declared provenance label for a source archive; hashes remain authoritative')
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--rounds', type=int, default=14)
    parser.add_argument('--dotnet', type=Path, default=Path('/tmp/gameplaytags-dotnet/dotnet'))
    parser.add_argument('--mono', type=Path)
    parser.add_argument('--nuget', type=Path, default=Path('/tmp/gameplaytags-nuget'))
    parser.add_argument('--quick', action='store_true', help='smoke only, never acceptance evidence')
    args = parser.parse_args()
    if args.rounds < 1:
        parser.error('rounds must be positive')
    out = args.out.resolve()
    if out.exists() and any(out.iterdir()):
        parser.error('output must be new or empty')
    out.mkdir(parents=True, exist_ok=True)
    sources = {'before': args.before.resolve(), 'after': args.after.resolve()}
    revisions = {'before': args.before_revision, 'after': args.after_revision}
    manifests = {}
    for label, source in sources.items():
        command = [sys.executable, str(HERE / 'run.py'), '--source', str(source), '--out', str(out / (label + '.json')),
                   '--variant', label, '--dotnet', str(args.dotnet), '--samples', '1', '--compile-only']
        if args.mono:
            command += ['--mono', str(args.mono), '--nuget', str(args.nuget)]
        if args.quick:
            command += ['--quick']
        subprocess.run(command, check=True)
        manifests[label] = json.loads((out / (label + '.manifest.json')).read_text())
        if revisions[label] is not None:
            manifests[label]['DeclaredRevision'] = revisions[label]
            (out / (label + '.manifest.json')).write_text(json.dumps(manifests[label], indent=2))
    # Also freeze this coordinator, and save every unmodified per-process report.
    frozen = file_hashes([HERE / 'paired.py'])
    for manifest in manifests.values():
        frozen.update(manifest['SourceHashes'])
    raw = out / 'raw'
    raw.mkdir()
    reports = {label: [] for label in sources}
    order = []
    env = dict(os.environ, DOTNET_TieredCompilation='0', DOTNET_gcServer='0', COMPlus_TieredCompilation='0', COMPlus_gcServer='0')
    for round_index in range(args.rounds):
        labels = list(sources) if round_index % 2 == 0 else list(reversed(sources))
        for label in labels:
            command = list(manifests[label]['Command'])
            path = raw / f'{label}-{round_index}.json'
            command[command.index('--out') + 1] = str(path)
            command[command.index('--round') + 1] = str(round_index)
            completed = subprocess.run(command, capture_output=True, text=True, check=True, env=env)
            (raw / f'{label}-{round_index}.stdout.txt').write_text(completed.stdout)
            if completed.stderr:
                (raw / f'{label}-{round_index}.stderr.txt').write_text(completed.stderr)
            reports[label].append(json.loads(path.read_text()))
            order.append({'Round': round_index, 'Variant': label, 'Command': command})
        print(f'Registry paired round {round_index + 1}/{args.rounds}', flush=True)
    if frozen != file_hashes(frozen):
        raise RuntimeError('Source or harness changed during measurement; discard this run')
    reference = reports['before'][0]
    expected = {row['CaseId']: row for row in reference['Measurements']}
    for label, samples in reports.items():
        for report in samples:
            for key in ('Runtime', 'OperatingSystem', 'PointerSizeBytes', 'StopwatchFrequency', 'Samples', 'Quick', 'TieredCompilation', 'ServerGC', 'Workload'):
                if report[key] != reference[key]:
                    raise RuntimeError(f'Configuration mismatch: {label} {key}')
            rows = {row['CaseId']: row for row in report['Measurements']}
            if rows.keys() != expected.keys():
                raise RuntimeError('Fixture IDs differ')
            for key, row in rows.items():
                for field in ('Operation', 'Unit', 'DeclarationCount', 'RegistryTagCount', 'FixtureChecksum', 'OutputChecksum', 'IterationsPerSample', 'RetainedCohort', 'Checksum'):
                    if row[field] != expected[key][field]:
                        raise RuntimeError(f'Workload mismatch: {label} {key} {field}')
    comparisons = []
    for key in expected:
        left = [next(row for row in report['Measurements'] if row['CaseId'] == key) for report in reports['before']]
        right = [next(row for row in report['Measurements'] if row['CaseId'] == key) for report in reports['after']]
        logs = [math.log(a['NanosecondsPerOperation'] / b['NanosecondsPerOperation']) for a, b in zip(left, right)]
        mean = statistics.mean(logs)
        width = 3.012 * statistics.stdev(logs) / math.sqrt(len(logs)) if len(logs) >= 14 else math.inf
        lower = math.exp(mean - width) if math.isfinite(width) else None
        upper = math.exp(mean + width) if math.isfinite(width) else None
        before_bytes = statistics.median(row['AllocatedBytesPerOperation'] for row in left)
        after_bytes = statistics.median(row['AllocatedBytesPerOperation'] for row in right)
        before_payload = statistics.median(row['FrozenArrayPayloadBytes'] for row in left)
        after_payload = statistics.median(row['FrozenArrayPayloadBytes'] for row in right)
        flags = []
        if lower is not None and lower > 1.03:
            flags.append('timing-improvement')
        if upper is not None and upper < 1 / 1.03:
            flags.append('timing-regression')
        if after_bytes < before_bytes:
            flags.append('allocation-improvement')
        if after_bytes > before_bytes:
            flags.append('allocation-regression')
        if after_payload < before_payload:
            flags.append('frozen-array-payload-improvement')
        if after_payload > before_payload:
            flags.append('frozen-array-payload-regression')
        if any(min(row['MeasuredBatchMilliseconds']) < 50 for row in left + right):
            flags.append('short-batch-review')
        unreliable = any(row['RetainedDeltaBelowArrayPayload'] for row in left + right)
        if unreliable:
            flags.append('retained-gc-deltas-unreliable')
        comparisons.append({
            'CaseId': key, 'BeforeNs': statistics.median(row['NanosecondsPerOperation'] for row in left),
            'AfterNs': statistics.median(row['NanosecondsPerOperation'] for row in right),
            'PairedGeometricSpeedup': math.exp(mean), 'Approx99Lower': lower, 'Approx99Upper': upper,
            'BeforeAllocatedBytes': before_bytes, 'AfterAllocatedBytes': after_bytes,
            'BeforeFrozenArrayPayloadBytes': before_payload, 'AfterFrozenArrayPayloadBytes': after_payload,
            'BeforeRetainedBytes': statistics.median(row['RetainedBytesPerRegistry'] for row in left),
            'AfterRetainedBytes': statistics.median(row['RetainedBytesPerRegistry'] for row in right),
            'RetainedDeltasReliableEnoughForReview': not unreliable, 'Flags': flags})
    (out / 'comparison.json').write_text(json.dumps(comparisons, indent=2))
    (out / 'environment.json').write_text(json.dumps({'Sources': manifests, 'FrozenHashes': frozen, 'ExecutionOrder': order,
        'Rounds': args.rounds, 'Quick': args.quick, 'DeclaredRevisions': revisions,
        'Validation': 'Independent correctness oracles passed in every process; immutable source/configuration/fixture checks passed. Review flags are not blanket acceptance. Declared revision labels are provenance supplied by the caller; source hashes are authoritative.'}, indent=2))
    print(json.dumps(comparisons, indent=2))


if __name__ == '__main__':
    main()
