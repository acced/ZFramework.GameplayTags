#!/usr/bin/env python3
"""AB/BA process-level benchmark; no nanosecond thresholds are used in CI."""
import argparse
import json
import os
from pathlib import Path
import platform
import statistics
import subprocess

p = argparse.ArgumentParser()
p.add_argument('--runtime', default='dotnet')
p.add_argument('--before', required=True)
p.add_argument('--after', required=True)
p.add_argument('--rounds', type=int, default=14)
p.add_argument('--padding', type=int, default=0)
p.add_argument('--output', required=True)
a = p.parse_args()
if a.rounds < 2:
    p.error('at least two rounds are required')
results = {'before': {}, 'after': {}}
environment = {}
for round_index in range(a.rounds):
    order = ('before', 'after') if round_index % 2 == 0 else ('after', 'before')
    for version in order:
        command = [a.runtime, getattr(a, version), '--bench', str(a.padding)]
        text = subprocess.check_output(command, text=True, env={**os.environ,
            'DOTNET_TieredCompilation': '0', 'DOTNET_gcServer': '0'})
        for line in text.splitlines():
            if line.startswith('ENV\t'):
                environment[version] = line
            if not line.startswith('RESULT\t'):
                continue
            _, name, time_ns, bytes_per_op, iterations = line.split('\t')
            row = results[version].setdefault(name, {'ns': [], 'bytes': [], 'iterations': int(iterations)})
            if row['iterations'] != int(iterations):
                raise RuntimeError('iteration mismatch')
            row['ns'].append(float(time_ns))
            row['bytes'].append(float(bytes_per_op))
    print(f'Paired round {round_index + 1}/{a.rounds}', flush=True)
summary = {}
for version, cases in results.items():
    for name, values in cases.items():
        if len(values['ns']) != a.rounds:
            raise RuntimeError(f'missing samples: {version} / {name}')
        row = summary.setdefault(name, {})
        row[version] = {'median_ns': statistics.median(values['ns']),
            'median_bytes': statistics.median(values['bytes']),
            'min_ns': min(values['ns']), 'max_ns': max(values['ns'])}
for name, values in summary.items():
    if 'before' in values and 'after' in values:
        values['speedup'] = values['before']['median_ns'] / values['after']['median_ns']
    print('SUMMARY\t' + name + '\t' + json.dumps(values, separators=(',', ':')))
output = Path(a.output)
output.parent.mkdir(parents=True, exist_ok=True)
report = {'rounds': a.rounds, 'padding': a.padding, 'platform': platform.platform(),
    'runtime_executable': a.runtime, 'environment': environment, 'samples': results, 'summary': summary}
output.write_text(json.dumps(report, indent=2) + '\n')
# Keep complete per-process timing/allocation samples retrievable in the job log too.
print('RAW_EVIDENCE_JSON=' + json.dumps(report, separators=(',', ':')))
