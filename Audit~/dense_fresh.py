#!/usr/bin/env python3
"""Fresh-result acceptance and allocation controls. Never replace fresh results with prepared output."""
import argparse
import hashlib
import json
import math
import os
import pathlib
import statistics
import subprocess
import tempfile
from integer_audit import project
from checks import source_digest

ROOT = pathlib.Path(__file__).resolve().parent.parent


def validate_costs(payload):
    if payload.get('positive_control_bytes', 0) <= 0:
        raise RuntimeError('Fresh-result allocation counter positive control failed')
    keys = set()
    for row in payload['rows']:
        key = (row['operation'], row['universe'], row['size'], row['distribution'])
        if key in keys or row.get('measurement_protocol') != 'settled-setup-v2':
            raise RuntimeError('Duplicate row or wrong measurement protocol')
        keys.add(key)
        for field in ('ns', 'allocated_bytes'):
            values = row[field]
            if len(values) != 7 or any(not isinstance(x, (int, float)) or not math.isfinite(x) or x <= 0 for x in values):
                raise RuntimeError('Invalid fresh-result measurement: ' + str(key))
    expected = {(op, u, n, d)
                for u in (10000, 65536, 262144)
                for n in ((1, 8, 32, 128, 1024) if u == 262144 else (8, 1024))
                for d in ('contiguous', 'scattered')
                for op in ('payload.zero_array', 'set.reserve_empty', 'set.copy',
                           'set.union', 'set.copy_append', 'set.copy_remove')}
    if keys != expected:
        raise RuntimeError('Incomplete or changed fresh-result cost matrix')
    return keys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--results', required=True)
    parser.add_argument('--rounds', type=int, default=3)
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    if args.rounds < 1:
        parser.error('rounds must be positive')
    results = pathlib.Path(args.results).resolve()
    output = results / 'fresh-results'
    output.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ, DOTNET_TieredCompilation='0', DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1')
    sdks = [line.split()[0] for line in subprocess.check_output([args.dotnet, '--list-sdks'], text=True).splitlines()
            if line.startswith('8.0.') and '-' not in line.split()[0]]
    if not sdks:
        raise RuntimeError('Install a .NET 8 SDK')
    sdk = max(sdks, key=lambda s: tuple(map(int, s.split('.'))))
    def run(command, log, cwd):
        p = subprocess.run(list(map(str, command)), cwd=cwd, env=env, text=True,
                           stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=600)
        (output / log).write_text(p.stdout, encoding='utf-8')
        print(p.stdout, flush=True)
        if p.returncode:
            raise RuntimeError(log + ' failed')
    with tempfile.TemporaryDirectory(prefix='dense-fresh-') as directory:
        work = pathlib.Path(directory)
        (work / 'global.json').write_text(json.dumps({'sdk': {'version': sdk, 'rollForward': 'disable'}}))
        (work / 'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
        for name in ('DenseFreshTests', 'DenseFreshCosts'):
            host = work / name
            host.mkdir()
            proj = host / (name + '.csproj')
            project(proj, [ROOT / 'Runtime/**/*.cs', ROOT / 'Audit~/UnityStubs.cs', ROOT / 'Audit~' / (name + '.cs')])
            run([args.dotnet, 'build', proj, '-c', 'Release', '-o', host / 'out'], name + '-build.log', host)
            dll = host / 'out' / (name + '.dll')
            for r in range(1 if name == 'DenseFreshTests' else args.rounds):
                stem = 'tests' if name == 'DenseFreshTests' else 'costs-r' + str(r)
                run([args.dotnet, 'exec', dll, output / (stem + '.json')], stem + '.log', host)
    table = {}
    expected = None
    for r in range(args.rounds):
        payload = json.loads((output / ('costs-r' + str(r) + '.json')).read_text())
        keys = validate_costs(payload)
        if expected is not None and keys != expected:
            raise RuntimeError('Fresh-result workload changed across rounds')
        expected = keys
        for row in payload['rows']:
            key = (row['operation'], row['universe'], row['size'], row['distribution'])
            table.setdefault(key, []).append(row)
    costs = [dict(operation=k[0], definitions=k[1], members=k[2], distribution=k[3],
                  median_ns=statistics.median(statistics.median(r['ns']) for r in rows),
                  bytes_per_operation=statistics.median(statistics.median(r['allocated_bytes']) for r in rows),
                  payload_bytes=rows[0]['payload_bytes'], rounds=rows)
             for k, rows in sorted(table.items())]
    (output / 'cost-summary.json').write_text(json.dumps(costs, indent=2))
    # Project, do not re-rank a selectively filtered fixture or reuse old CI numbers.
    full = json.loads((results / 'comparison.json').read_text())
    selected = [r for r in full if r['definitions'] == 262144 and r['members'] in (1, 8, 32, 128)
                and r['operation'] in ('exact', 'union', 'copy_append', 'copy_remove')]
    if len(selected) != 32:
        raise RuntimeError('Missing large-registry/small-member cases in the full comparison')
    (output / 'large-registry-small-members.json').write_text(json.dumps(selected, indent=2))
    summary = dict(source_digest=source_digest(ROOT), sdk=sdk, rounds=args.rounds,
                   runtime_blob_sha256=hashlib.sha256((ROOT / 'Runtime/RuntimeTagSet.cs').read_bytes()).hexdigest(),
                   correctness=json.loads((output / 'tests.json').read_text()),
                   diagnostic_rows=len(costs), large_small_cases=len(selected),
                   large_small_wins=sum(r['first'] for r in selected),
                   all_large_small_targets_met=all(r['first'] for r in selected),
                   release_approved=False, native_unity_executed=False,
                   note='Full Dense storage is unchanged. Allocation controls are explanatory, not subtracted from operation times. Prepared loops and fresh-result loops are separate contracts.')
    (output / 'summary.json').write_text(json.dumps(summary, indent=2))
    print(json.dumps(summary, indent=2), flush=True)


if __name__ == '__main__':
    main()
