#!/usr/bin/env python3
"""Portable fixed rejection screen. Logs stay private; only explicit JSON allowlist is public."""
import argparse
import hashlib
import io
import json
import math
import os
from pathlib import Path
import re
import shutil
import signal
import statistics
import subprocess
import tarfile
import time
import traceback
import build_support

HERE = Path(__file__).resolve().parent
PUBLIC_FILES = ('status.json', 'provenance.json', 'config.json', 'correctness.json', 'allocation.json', 'raw/allocations.json', 'raw/timing.json', 'summary.json')

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def tree(root): return {str(p.relative_to(root)): sha(p) for p in sorted(root.rglob('*')) if p.is_file()}
def save(path, value): path.write_text(json.dumps(value, indent=2, allow_nan=False) + '\n')
def resolve_tool(value): return Path(shutil.which(value) or value).resolve()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--git-root', type=Path, default=HERE.parents[2])
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--work', type=Path, required=True)
    parser.add_argument('--backend', choices=('net', 'mono'), required=True)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--mono', default='mono')
    parser.add_argument('--nuget', type=Path, default=Path(os.environ.get('NUGET_PACKAGES', Path.home() / '.nuget/packages')))
    parser.add_argument('--deadline-seconds', type=int, default=600)
    args = parser.parse_args()
    if not 0 < args.deadline_seconds <= 600: parser.error('Deadline must be 1–600 seconds; no automatic extension.')
    deadline = time.monotonic() + args.deadline_seconds
    public = args.out.resolve(); private = args.work.resolve()
    if public == private or public in private.parents or private in public.parents: parser.error('--out and --work must be separate non-nested directories')
    public.mkdir(parents=True, exist_ok=False); (public / 'raw').mkdir()
    private.mkdir(parents=True, exist_ok=False)
    dotnet, mono, nuget = resolve_tool(args.dotnet), resolve_tool(args.mono), args.nuget.resolve()
    tool_hashes = {}; artifact_hashes = {}; tool_files = {}
    commands = []; stage = 'input-verification'; status = 'incomplete'; correctness = []; cold = []; timing = []; comparisons = []; allocations = []; provenance = {}
    pins = json.loads((HERE / 'source-pins.json').read_text())
    frozen = json.loads((HERE / 'frozen.json').read_text())
    iterations_file = HERE / ('iterations/' + args.backend + '.tsv')
    iterations = dict((name, int(value)) for name, value in (line.split('\t') for line in iterations_file.read_text().splitlines()))
    config = {'Purpose': 'rejection-only', 'AcceptanceEvidence': False, 'Backend': args.backend, 'Rounds': 4, 'TargetBatchMs': 200, 'DeadlineSeconds': args.deadline_seconds,
        'IterationRule': 'max(original, ceil(original * 200 / fastest of four saved baseline-only BatchMs))', 'Iterations': iterations,
        'Order': [v for i in range(4) for v in (['before', 'after'] if i % 2 == 0 else ['after', 'before'])],
        'Compiler': {'Optimize': True, 'LanguageVersion': '9.0', 'Deterministic': True}, 'ExpectedVersions': {'SDK': '8.0.425', 'CLR': '8.0.31', 'Mono': '6.8.0.105'}, 'RuntimeConfig': {'System.Runtime.TieredCompilation': False, 'System.GC.Server': False, 'rollForward': 'Disable'},
        'Limits': ['No adaptive pilot or resampling', 'No full acceptance suite', 'No Unity Editor Mono or IL2CPP claim', 'First bulk allocation uses warmed code and cold snapshot state']}

    def invoke(name, command):
        remaining = deadline - time.monotonic()
        if remaining <= 0: raise TimeoutError('fixed deadline')
        command = list(map(str, command)); stdout = private / (name + '.stdout'); stderr = private / (name + '.stderr')
        entry = {'Name': name, 'Command': command}; started = time.monotonic()
        with stdout.open('wb') as a, stderr.open('wb') as b:
            process = subprocess.Popen(command, stdout=a, stderr=b, start_new_session=True)
            def reap_group():
                try: os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError: pass
                process.wait()
            try:
                entry['ExitCode'] = process.wait(timeout=remaining)
                if entry['ExitCode'] != 0: reap_group()
            except subprocess.TimeoutExpired:
                reap_group()
                entry['ExitCode'] = None; entry['TimedOut'] = True
            except BaseException:
                reap_group()
                raise
        entry['ElapsedSeconds'] = time.monotonic() - started; commands.append(entry); save(private / 'commands.json', commands)
        return stdout.read_bytes(), entry['ExitCode']

    def checked(name, command):
        stdout, code = invoke(name, command)
        if code != 0: raise RuntimeError('command failed')
        return stdout

    def verify():
        for name, expected in frozen['Files'].items():
            if sha(HERE / name) != expected: raise RuntimeError('prepared input hash mismatch')
        if sha(HERE / 'candidate.patch') != pins['CandidatePatchSHA256']: raise RuntimeError('candidate patch mismatch')
        if list(iterations) != pins['Rows']: raise RuntimeError('fixed row order mismatch')
        if any(iterations[row['Id']] < row['Iterations'] for row in pins['OriginalIterations']): raise RuntimeError('iteration count decreased')

    def verify_execution():
        if time.monotonic() >= deadline: raise TimeoutError('fixed deadline')
        if {name: sha(path) for name, path in tool_files.items()} != tool_hashes: raise RuntimeError('compiler/reference/runtime input changed')
        if {v: tree(private / ('build-' + v)) for v in ('before', 'after')} != artifact_hashes: raise RuntimeError('built artifact changed')
        if time.monotonic() >= deadline: raise TimeoutError('fixed deadline')

    def target(name, command, kind, variant, round=None):
        verify_execution()
        stdout, code = invoke(name, command); values = []
        fields = {'correctness': {'Mode', 'Status', 'Checks', 'Digest'}, 'cold': {'Mode', 'Id', 'Cohort', 'Bytes', 'SnapshotCapacity'},
                  'timing': {'Mode', 'Id', 'Iterations', 'Ns', 'BatchMs', 'Bytes', 'Checksum'}}[kind]
        for line in stdout.decode().splitlines():
            row = json.loads(line)
            if set(row) != fields: raise RuntimeError('unexpected target result schema')
            for key, value in row.items():
                if key not in ('Mode', 'Status', 'Digest', 'Id') and (not isinstance(value, (int, float)) or not math.isfinite(value)):
                    raise RuntimeError('invalid target numeric result')
            if kind == 'correctness':
                if row['Mode'] != 'correctness' or row['Status'] != 'PASS' or row['Checks'] != 468567 or row['Digest'] != 'b76530c0ef95992e': raise RuntimeError('correctness failed')
            elif kind == 'timing':
                if row['Mode'] != 'timing-screen' or row['Id'] not in iterations or row['Iterations'] != iterations[row['Id']]: raise RuntimeError('timing row mismatch')
            elif row['Mode'] != 'first-bulk-call-allocation' or not re.fullmatch('(add|remove)/listener(True|False)/n(0|1|8|64|1024)', row['Id']): raise RuntimeError('allocation row mismatch')
            values.append(row)
        item = {'Variant': variant, 'Complete': code == 0, 'Rows': values}
        if round is not None: item['Round'] = round
        {'correctness': correctness, 'cold': cold, 'timing': timing}[kind].append(item)
        if code != 0: raise RuntimeError('target failed')
        if len(values) != {'correctness': 1, 'cold': 20, 'timing': 35}[kind]: raise RuntimeError('missing target rows')
        if kind == 'timing' and [r['Id'] for r in values] != pins['Rows']: raise RuntimeError('timing order mismatch')
        expected_cold = [op + '/listener' + str(listener) + '/n' + str(n) for n in (0, 1, 8, 64, 1024) for listener in (False, True) for op in ('add', 'remove')]
        if kind == 'cold' and ([r['Id'] for r in values] != expected_cold or any(r['Cohort'] != 32 for r in values)): raise RuntimeError('allocation order mismatch')
        verify_execution()
        return values

    try:
        verify()
        stage = 'source-materialization'
        archive = checked('archive-baseline', ['git', '-C', args.git_root.resolve(), 'archive', pins['BaselineCommit'], 'Runtime'])
        before = private / 'before'; before.mkdir()
        with tarfile.open(fileobj=io.BytesIO(archive)) as tar:
            for member in tar.getmembers():
                path = Path(member.name)
                if path.is_absolute() or '..' in path.parts or path.parts[0] != 'Runtime': raise RuntimeError('unexpected archive member')
                if member.isdir(): (before / path).mkdir(parents=True, exist_ok=True)
                elif member.isfile():
                    dest = before / path; dest.parent.mkdir(parents=True, exist_ok=True); dest.write_bytes(tar.extractfile(member).read())
                else: raise RuntimeError('nonregular archive member')
        after = private / 'after'; shutil.copytree(before, after)
        shutil.copy2(HERE / 'candidate/GameplayTagHierarchicalContanier.cs', after / 'Runtime/GameplayTagHierarchicalContanier.cs')
        for label in ('before', 'after'):
            if tree(private / label) != pins['SourceHashes'][label]: raise RuntimeError('runtime source mismatch')
        stage = 'toolchain-and-build'
        sdk, framework, refpack = build_support.toolchain(dotnet)
        compiler = checked('compiler-version', [dotnet, sdk / 'Roslyn/bincore/csc.dll', '-version']).decode().strip()
        compiler_version = re.match(r'[0-9][0-9A-Za-z.+-]*', compiler).group(0)
        runtime_version = framework.name
        if args.backend == 'mono':
            raw = checked('mono-version', [mono, '--version']).decode()
            runtime_version = re.search(r'Mono JIT compiler version ([0-9.]+)', raw).group(1)
            if runtime_version != '6.8.0.105': raise RuntimeError('unexpected Mono version')
        provenance = {'Backend': args.backend, 'SDKVersion': sdk.name, 'CompilerVersion': compiler_version, 'RuntimeVersion': runtime_version, 'ReferencePackVersion': 'net472/1.0.3' if args.backend == 'mono' else refpack.name,
            'HarnessSource': 'Tests~/PortfolioScreen/Hierarchical/Harness/Program.cs', 'HarnessSHA256': frozen['Files']['Harness/Program.cs'], 'CandidatePatchSHA256': pins['CandidatePatchSHA256']}
        tool_files = build_support.input_files(args.backend, dotnet, mono, nuget)
        tool_hashes = {name: sha(path) for name, path in tool_files.items()}
        bins = {v: build_support.build(private / v, HERE / 'Harness/Program.cs', private / ('build-' + v), args.backend, dotnet, mono, nuget, checked) for v in ('before', 'after')}
        artifact_hashes = {v: tree(private / ('build-' + v)) for v in ('before', 'after')}
        stage = 'focused-correctness'
        first = target('test-before', bins['before'] + ['test'], 'correctness', 'before')
        second = target('test-after', bins['after'] + ['test'], 'correctness', 'after')
        if first != second: raise RuntimeError('correctness mismatch')
        stage = 'first-bulk-allocation'
        for variant in ('before', 'after'): target('cold-' + variant, bins[variant] + ['cold'], 'cold', variant)
        stage = 'fixed-timing'
        for round in range(4):
            for variant in (('before', 'after') if round % 2 == 0 else ('after', 'before')):
                target('timing-' + variant + '-' + str(round), bins[variant] + ['bench', '--iterations-file', iterations_file], 'timing', variant, round)
        stage = 'result-validation'
        for index, name in enumerate(pins['Rows']):
            left = [x['Rows'][index] for x in timing if x['Variant'] == 'before']; right = [x['Rows'][index] for x in timing if x['Variant'] == 'after']
            if any(a['Checksum'] != b['Checksum'] for a, b in zip(left, right)): raise RuntimeError('checksum mismatch')
            ratios = [a['Ns'] / b['Ns'] for a, b in zip(left, right)]; ratio = math.exp(statistics.mean(map(math.log, ratios)))
            comparisons.append({'Id': name, 'BeforeMedianNs': statistics.median(r['Ns'] for r in left), 'AfterMedianNs': statistics.median(r['Ns'] for r in right), 'PairedRatios': ratios, 'GeometricSpeedup': ratio,
                'ShortestBatchMs': min(r['BatchMs'] for r in left + right), 'ScreenRegressionSignal': ratio < .97, 'AllocationIncrease': any(b['Bytes'] > a['Bytes'] for a, b in zip(left, right))})
        for a, b in zip(cold[0]['Rows'], cold[1]['Rows']):
            if a['Id'] != b['Id']: raise RuntimeError('cold row mismatch')
            allocations.append({'Id': a['Id'], 'BeforeBytes': a['Bytes'], 'AfterBytes': b['Bytes'], 'DeltaBytes': b['Bytes'] - a['Bytes'], 'BeforeSnapshotCapacity': a['SnapshotCapacity'], 'AfterSnapshotCapacity': b['SnapshotCapacity']})
        verify()
        for label in ('before', 'after'):
            if tree(private / label) != pins['SourceHashes'][label]: raise RuntimeError('source changed during run')
        verify_execution()
        status = 'completed'; stage = 'completed'
    except BaseException:
        (private / 'failure.txt').write_text(traceback.format_exc())
    finally:
        provenance.update({'BaselineCommit': pins['BaselineCommit'], 'SourceHashes': pins['SourceHashes'],
            'PreparedFileHashes': {'Tests~/PortfolioScreen/Hierarchical/' + k: v for k, v in frozen['Files'].items()},
            'ToolInputHashes': tool_hashes, 'ArtifactHashes': artifact_hashes})
        payloads = {'status.json': {'Status': status, 'Stage': stage, 'AcceptanceEvidence': False}, 'provenance.json': provenance,
            'config.json': config, 'correctness.json': correctness, 'allocation.json': allocations, 'raw/allocations.json': cold, 'raw/timing.json': timing,
            'summary.json': {'Status': status, 'AcceptanceEvidence': False, 'Backend': args.backend, 'CompletedTimingProcesses': sum(x['Complete'] for x in timing), 'Comparisons': comparisons}}
        if time.monotonic() >= deadline:
            status = 'incomplete'; stage = 'deadline'
            payloads['status.json'].update(Status=status, Stage=stage)
            payloads['summary.json']['Status'] = status
        if set(payloads) != set(PUBLIC_FILES): raise RuntimeError('public allowlist mismatch')
        for name in PUBLIC_FILES: save(public / name, payloads[name])
        if set(str(p.relative_to(public)) for p in public.rglob('*') if p.is_file()) != set(PUBLIC_FILES): raise RuntimeError('unexpected public artifact')
        if time.monotonic() >= deadline and status == 'completed':
            status = 'incomplete'; stage = 'deadline'
            payloads['status.json'].update(Status=status, Stage=stage); payloads['summary.json']['Status'] = status
            save(public / 'status.json', payloads['status.json']); save(public / 'summary.json', payloads['summary.json'])
    print('Hierarchical rejection screen: ' + status + '. Public JSON uses the explicit allowlist; no acceptance claim.')
    return 0 if status == 'completed' else 1

if __name__ == '__main__': raise SystemExit(main())
