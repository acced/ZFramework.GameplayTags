#!/usr/bin/env python3
"""Fixed binding rejection screen. Public evidence uses an explicit JSON allowlist."""
import argparse
import hashlib
import importlib.util
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
import sys
import tarfile
import time
import traceback

HERE = Path(__file__).resolve().parent
SUPPORT = HERE.parent / 'Hierarchical/build_support.py'
REFERENCES = HERE.parent / 'References.csproj'
PUBLIC_FILES = ('status.json', 'provenance.json', 'config.json', 'correctness.json',
                'raw/cold.json', 'raw/pilot.json', 'iterations.json', 'raw/timing.json', 'summary.json')
RUNTIME_FIELDS = {'RuntimeVersion', 'ServerGC', 'TieringConfigured', 'ReadyToRunRequested', 'TieringRequested'}
STATE_FIELDS = {'OwnedSubscriptions', 'ExternalSubscriptions', 'TagCount'}
PROBE_FIELDS = {p + q for p in ('BeforeProbe', 'AfterProbe') for q in ('Events', 'Checksum', 'ExternalEvents', 'ExternalChecksum')}
MEASURE_FIELDS = {'Ticks', 'StopwatchFrequency', 'Ns', 'Bytes', 'Gc0', 'Gc1', 'Gc2', 'Events', 'Checksum', 'ExternalEvents', 'ExternalChecksum'}


def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def tree(root): return {str(p.relative_to(root)): sha(p) for p in sorted(root.rglob('*')) if p.is_file()}
def save(path, value): path.write_text(json.dumps(value, indent=2, allow_nan=False) + '\n')
def resolve(value): return Path(shutil.which(value) or value).resolve()
class TargetFailure(RuntimeError): pass


def main():
    entered = time.monotonic()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--git-root', type=Path, default=HERE.parents[2])
    parser.add_argument('--backend', choices=('net', 'mono'), required=True)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--mono', default='mono')
    parser.add_argument('--nuget', type=Path, default=Path(os.environ.get('NUGET_PACKAGES', Path.home() / '.nuget/packages')))
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--work', type=Path, required=True)
    parser.add_argument('--deadline-seconds', type=int, default=600)
    parser.add_argument('--preflight-only', action='store_true', help='Build all binaries; policy, core, focused semantics and fixtures only. No cold calls, pilot or timing.')
    args = parser.parse_args()
    if not 0 < args.deadline_seconds <= 600: parser.error('Deadline must be 1-600 seconds; no extension.')
    deadline = entered + args.deadline_seconds
    def deadline_signal(signum, frame): raise TimeoutError('fixed overall deadline or termination')
    signal.signal(signal.SIGALRM, deadline_signal)
    signal.signal(signal.SIGTERM, deadline_signal)
    signal.setitimer(signal.ITIMER_REAL, max(0.001, deadline - time.monotonic()))
    public, private = args.out.resolve(), args.work.resolve()
    if public == private or public in private.parents or private in public.parents:
        parser.error('--out and --work must be separate, new, non-nested directories')
    public.mkdir(parents=True, exist_ok=False); (public / 'raw').mkdir()
    private.mkdir(parents=True, exist_ok=False)
    dotnet, mono, nuget = resolve(args.dotnet), resolve(args.mono), args.nuget.resolve()
    pins = json.loads((HERE / 'source-pins.json').read_text())
    frozen = json.loads((HERE / 'frozen.json').read_text())
    frozen_hash = sha(HERE / 'frozen.json')
    config = json.loads((HERE / 'config.json').read_text())
    row_ids = [r['id'] for r in config['Rows']]; cold_ids = config['ColdRows']
    seeds = {r['id']: r['pilot_iterations'] for r in config['Rows']}
    iteration_hash = None
    commands, correctness, policy, fixtures, cold, pilot, timing, comparisons, cold_comparisons = [], [], [], [], [], [], [], [], []
    iterations, provenance, tool_hashes, artifact_hashes, tool_files, source_trees = {}, {}, {}, {}, {}, {}
    status, stage = 'incomplete', 'input-verification'
    # Fixed, disclosed runtime switches; no inherited setting can enable tiering/server GC or disable R2R.
    child_env = dict(os.environ)
    for prefix in ('DOTNET_', 'COMPlus_'):
        child_env[prefix + 'ReadyToRun'] = '1'
        child_env[prefix + 'TieredCompilation'] = '0'
        child_env[prefix + 'gcServer'] = '0'
    child_env['DOTNET_ROLL_FORWARD'] = 'Disable'

    def checked(name, command):
        remaining = deadline - time.monotonic()
        if remaining <= 0: raise TimeoutError('fixed overall deadline')
        entry = {'Name': name, 'Command': list(map(str, command))}; commands.append(entry)
        stdout, stderr = private / (name + '.stdout'), private / (name + '.stderr')
        started = time.monotonic()
        with stdout.open('wb') as a, stderr.open('wb') as b:
            process = subprocess.Popen(entry['Command'], stdout=a, stderr=b, start_new_session=True, env=child_env)
            def cleanup():
                try: os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError: pass
                process.wait()
            try:
                entry['ExitCode'] = process.wait(timeout=remaining)
            except subprocess.TimeoutExpired:
                entry['ExitCode'] = None; entry['TimedOut'] = True
            except BaseException:
                entry['Interrupted'] = True
                raise
            finally:
                cleanup()
                entry['ElapsedSeconds'] = time.monotonic() - started
                save(private / 'commands.json', commands)
        return stdout.read_bytes(), entry.get('ExitCode')

    def require(name, command):
        output, code = checked(name, command)
        if code != 0: raise RuntimeError('command failed at ' + name)
        return output

    def verify_inputs():
        if sha(HERE / 'frozen.json') != frozen_hash: raise RuntimeError('frozen manifest changed')
        if {name: sha(HERE / name) for name in frozen['Files']} != frozen['Files']: raise RuntimeError('prepared inputs changed')
        if sha(SUPPORT) != frozen['BuildSupportSHA256'] or sha(REFERENCES) != frozen['ReferencesSHA256']: raise RuntimeError('shared inputs changed')
        if sha(HERE / 'candidate.patch') != pins['CandidatePatchSHA256']: raise RuntimeError('patch mismatch')
        if len(row_ids) != 9 or row_ids != pins['Rows'] or cold_ids != pins['ColdRows']: raise RuntimeError('fixed rows changed')

    def verify_iteration_map():
        if iteration_hash is not None:
            path = private / 'iterations.tsv'
            if not path.is_file() or sha(path) != iteration_hash: raise RuntimeError('frozen iteration map changed')

    def verify_execution():
        if time.monotonic() >= deadline: raise TimeoutError('fixed overall deadline')
        verify_inputs(); verify_iteration_map()
        if {key: sha(path) for key, path in tool_files.items()} != tool_hashes: raise RuntimeError('tool inputs changed')
        if {name: tree(private / ('build-' + name)) for name in artifact_hashes} != artifact_hashes: raise RuntimeError('compiled outputs changed')
        if {name: tree(private / name) for name in source_trees} != source_trees: raise RuntimeError('materialized sources changed')
        if time.monotonic() >= deadline: raise TimeoutError('fixed overall deadline')

    def validate_runtime(row):
        if not re.fullmatch(r'[0-9]+(?:\.[0-9]+){2,3}', row['RuntimeVersion']): raise ValueError('invalid runtime report')
        if row['ServerGC'] != 0: raise ValueError('server GC enabled')
        if args.backend == 'net':
            if row['RuntimeVersion'] != config['CLRVersion'] or row['TieringConfigured'] != 0 or row['ReadyToRunRequested'] != 1 or row['TieringRequested'] != 0:
                raise ValueError('unexpected .NET runtime configuration')
        elif row['ReadyToRunRequested'] != -1 or row['TieringRequested'] != -1 or row['TieringConfigured'] not in (-1, 0):
            raise ValueError('unexpected Mono runtime configuration')

    def parse_row(row, mode, expected_ids, collected):
        base = {'Mode', 'Id'} | RUNTIME_FIELDS | STATE_FIELDS
        if mode == 'preflight': allowed = base | PROBE_FIELDS | {'Binds', 'Tags'}
        elif mode == 'cold': allowed = base | MEASURE_FIELDS | {'Binds', 'BindingCapacityBefore', 'CallbackCapacityBefore'}
        else: allowed = base | MEASURE_FIELDS | PROBE_FIELDS | {'Iterations', 'BatchMs'}
        if set(row) != allowed or row['Mode'] != mode or row['Id'] not in expected_ids: raise ValueError('unexpected row schema')
        for key, value in row.items():
            if key in ('Mode', 'Id', 'RuntimeVersion'): continue
            if type(value) not in (int, float) or not math.isfinite(value): raise ValueError('invalid number')
        if not isinstance(row['RuntimeVersion'], str) or re.fullmatch(r'[0-9]+(?:\.[0-9]+){2,3}', row['RuntimeVersion']) is None: raise ValueError('invalid runtime report')
        collected.append(row)  # Preserve every schema-valid numerical sample even when an invariant fails.
        for key, value in row.items():
            if key not in ('Mode', 'Id', 'RuntimeVersion', 'TieringConfigured', 'ReadyToRunRequested', 'TieringRequested') and value < 0: raise ValueError('negative measurement')
        validate_runtime(row)
        if row['OwnedSubscriptions'] != 0: raise ValueError('owned subscriptions survived')
        if mode == 'cold':
            if row['Binds'] != 1 or row['BindingCapacityBefore'] != 0: raise ValueError('cold BindState is not fresh')
            ext = 2 if row['Id'] == 'first-external-multicast' else 0
            present = row['Id'] == 'first-present'
            if row['ExternalSubscriptions'] != ext or row['TagCount'] != int(present) or row['Events'] != 3 or row['Checksum'] != (7 if present else 5) or row['ExternalEvents'] != ext * 4:
                raise ValueError('cold semantics differ')
            if row['CallbackCapacityBefore'] != (4 if ext else 0): raise ValueError('cold callback capacity differs')
        else:
            fixed = config['Rows'][row_ids.index(row['Id'])]
            if row['ExternalSubscriptions'] != fixed['external_listeners'] or row['TagCount'] != int(fixed['present']): raise ValueError('final state differs')
            for suffix in ('Events', 'Checksum', 'ExternalEvents', 'ExternalChecksum'):
                if row['BeforeProbe' + suffix] != row['AfterProbe' + suffix]: raise ValueError('event probe differs')
            if mode == 'preflight':
                if row['Binds'] != fixed['binds'] or row['Tags'] != fixed['tags']: raise ValueError('fixture geometry changed')
            else:
                count = seeds[row['Id']] if mode == 'pilot' else iterations[row['Id']]
                if row['Iterations'] != count or row['BatchMs'] <= 0: raise ValueError('invalid measured count/time')
        if mode != 'preflight' and (row['Ticks'] <= 0 or row['StopwatchFrequency'] <= 0 or row['Ns'] <= 0): raise ValueError('invalid clock sample')

    def target(name, command, mode, variant, round_number=None, fixture_id=None):
        verify_execution()
        item = {'Variant': variant, 'Complete': False, 'Rows': [], 'ExitCode': None}
        if round_number is not None: item['Round'] = round_number
        if fixture_id is not None: item['Fixture'] = fixture_id
        {'preflight': fixtures, 'cold': cold, 'pilot': pilot, 'timing': timing}[mode].append(item)
        output, code = checked(name, command); item['ExitCode'] = code
        expected_ids = [fixture_id] if mode == 'cold' else row_ids
        valid = True
        for line in output.decode(errors='replace').splitlines():
            try:
                row = json.loads(line); parse_row(row, mode, expected_ids, item['Rows'])
            except (ValueError, TypeError, KeyError): valid = False
        item['Complete'] = valid and code == 0 and [r['Id'] for r in item['Rows']] == expected_ids
        verify_execution()
        if not item['Complete']: raise TargetFailure('target failed or schema/rows invalid')
        return item['Rows']

    def summarize_cold():
        result = []
        semantics = ('Binds', 'BindingCapacityBefore', 'CallbackCapacityBefore', 'Events', 'Checksum', 'ExternalEvents', 'ExternalChecksum', 'OwnedSubscriptions', 'ExternalSubscriptions', 'TagCount')
        for name in cold_ids:
            left = [x['Rows'][0] for x in cold if x['Complete'] and x['Fixture'] == name and x['Variant'] == 'before']
            right = [x['Rows'][0] for x in cold if x['Complete'] and x['Fixture'] == name and x['Variant'] == 'after']
            row = {'Id': name, 'BeforeSamples': len(left), 'AfterSamples': len(right), 'Complete': len(left) == len(right) == 3}
            if row['Complete']:
                row.update(SemanticsAgree=all(all(a[k] == b[k] for k in semantics) for a, b in zip(left, right)),
                    BeforeBytes=[a['Bytes'] for a in left], AfterBytes=[b['Bytes'] for b in right],
                    CandidateExtraAllocationPairs=sum(b['Bytes'] > a['Bytes'] for a, b in zip(left, right)),
                    CandidateOverBaselineRatios=[b['Ns'] / a['Ns'] for a, b in zip(left, right)],
                    AfterSlowerPairs=sum(b['Ns'] > a['Ns'] for a, b in zip(left, right)),
                    LatencyReviewRequired=any(b['Ns'] > a['Ns'] for a, b in zip(left, right)),
                    ObservedGC=any(x['Gc0'] + x['Gc1'] + x['Gc2'] > 0 for x in left + right))
            result.append(row)
        return result

    try:
        verify_inputs()
        stage = 'source-materialization'
        archive = require('archive-baseline', ['git', '-C', args.git_root.resolve(), 'archive', pins['BaselineCommit'], 'Runtime', 'Tests~/Program.cs', 'Tests~/FixtureRegistration.cs'])
        before = private / 'before'; before.mkdir()
        with tarfile.open(fileobj=io.BytesIO(archive)) as tar:
            for member in tar.getmembers():
                path = Path(member.name)
                if path.is_absolute() or '..' in path.parts or path.parts[0] not in ('Runtime', 'Tests~'): raise RuntimeError('unexpected archive member')
                if member.isdir(): (before / path).mkdir(parents=True, exist_ok=True)
                elif member.isfile():
                    dest = before / path; dest.parent.mkdir(parents=True, exist_ok=True); dest.write_bytes(tar.extractfile(member).read())
                else: raise RuntimeError('nonregular archive member')
        after = private / 'after'; shutil.copytree(before, after)
        shutil.copy2(HERE / 'candidate/GameplayTagContainerBinds.cs', after / 'Runtime/GameplayTagContainerBinds.cs')
        for variant in ('before', 'after'):
            if tree(private / variant) != pins['SourceHashes'][variant]: raise RuntimeError('source identities mismatch')
            source_trees[variant] = tree(private / variant)
            core_source = private / ('core-source-' + variant)
            shutil.copytree(private / variant / 'Runtime', core_source / 'Runtime')
            shutil.copy2(private / variant / 'Tests~/FixtureRegistration.cs', core_source / 'Runtime/FixtureRegistration.cs')
            source_trees['core-source-' + variant] = tree(core_source)
        stage = 'toolchain-and-build'
        spec = importlib.util.spec_from_file_location('binding_existing_build_support', SUPPORT)
        sys.dont_write_bytecode = True
        support = importlib.util.module_from_spec(spec); spec.loader.exec_module(support)
        sdk, framework, refpack = support.toolchain(dotnet)
        compiler_text = require('compiler-version', [dotnet, sdk / 'Roslyn/bincore/csc.dll', '-version']).decode().strip()
        compiler_version = re.match(r'[0-9][0-9A-Za-z.+-]*', compiler_text).group(0)
        runtime_version = framework.name
        if args.backend == 'mono':
            raw = require('mono-version', [mono, '--version']).decode()
            runtime_version = re.search(r'Mono JIT compiler version ([0-9.]+)', raw).group(1)
            if runtime_version != config['ExpectedCIMonoVersion']: raise RuntimeError('unexpected pinned Mono version')
        tool_files = support.input_files(args.backend, dotnet, mono, nuget)
        tool_hashes = {key: sha(path) for key, path in tool_files.items()}
        binaries = {}
        for kind in ('screen', 'focused', 'core'):
            for variant in ('before', 'after'):
                label = kind + '-' + variant
                source = private / (('core-source-' if kind == 'core' else '') + variant)
                program = private / variant / 'Tests~/Program.cs' if kind == 'core' else HERE / 'Harness' / ('Screen.cs' if kind == 'screen' else 'Correctness.cs')
                binaries[label] = support.build(source, program, private / ('build-' + label), args.backend, dotnet, mono, nuget,
                    lambda name, command, kind=kind: require(kind + '-' + name, command))
        artifact_hashes = {label: tree(private / ('build-' + label)) for label in binaries}
        provenance = {'Backend': args.backend, 'SDKVersion': sdk.name, 'CompilerVersion': compiler_version,
            'RuntimeVersion': runtime_version, 'ReferencePackVersion': 'net472/1.0.3' if args.backend == 'mono' else refpack.name,
            'RuntimeSettings': config['RuntimeSettings'], 'BaselineCommit': pins['BaselineCommit'], 'CandidatePatchSHA256': pins['CandidatePatchSHA256'],
            'SourceHashes': pins['SourceHashes'], 'PreparedFileHashes': {'Tests~/PortfolioScreen/Bindings/' + k: v for k, v in frozen['Files'].items()},
            'SharedBuildSupportSHA256': frozen['BuildSupportSHA256'], 'SharedReferencesSHA256': frozen['ReferencesSHA256'],
            'ToolInputHashes': tool_hashes, 'CompiledOutputHashes': {label: {k: v for k, v in values.items() if k != 'compile.rsp'} for label, values in artifact_hashes.items()}}
        save(private / 'execution-identities.json', {'ToolInputHashes': tool_hashes, 'ArtifactHashes': artifact_hashes, 'SourceHashes': source_trees})
        stage = 'policy-check'
        for variant in ('before', 'after'):
            verify_execution(); output = require('policy-' + variant, binaries['screen-' + variant] + ['policy']); verify_execution()
            row = json.loads(output)
            allowed = {'Mode', 'BindILBytes', 'DelegateConstructors', 'RuntimeTypes', 'RuntimeFields', 'LayoutSHA256', 'BindILSHA256'} | RUNTIME_FIELDS
            if set(row) != allowed or row['Mode'] != 'policy': raise RuntimeError('unexpected policy schema')
            for key in ('BindILBytes', 'DelegateConstructors', 'RuntimeTypes', 'RuntimeFields'):
                if type(row[key]) != int or row[key] < 0: raise RuntimeError('invalid policy number')
            for key in ('LayoutSHA256', 'BindILSHA256'):
                if not re.fullmatch('[0-9a-f]{64}', row[key]): raise RuntimeError('invalid policy hash')
            validate_runtime(row)
            if row['DelegateConstructors'] != (2 if variant == 'before' else 1): raise RuntimeError('delegate lowering differs from candidate premise')
            policy.append(dict(row, Variant=variant))
        if any(policy[0][key] != policy[1][key] for key in ('RuntimeTypes', 'RuntimeFields', 'LayoutSHA256')): raise RuntimeError('runtime type/field layout changed')
        stage = 'correctness'
        for kind, expected in (('focused', 70), ('core', 7595855)):
            normalized = []
            for variant in ('before', 'after'):
                verify_execution(); output = require(kind + '-' + variant, binaries[kind + '-' + variant]).decode(); verify_execution()
                if kind == 'focused':
                    last = json.loads(output.splitlines()[-1]); valid = last == {'Checks': expected, 'FailedGroups': 0}
                else:
                    match = re.search(r'([0-9,]+) assertions; (\d+) failed groups;', output)
                    valid = bool(match and int(match[1].replace(',', '')) == expected and int(match[2]) == 0)
                if not valid: raise RuntimeError('correctness checks failed')
                normalized.append(output)
                correctness.append({'Suite': kind, 'Variant': variant, 'Checks': expected, 'FailedGroups': 0, 'OutputSHA256': hashlib.sha256(output.encode()).hexdigest()})
            if normalized[0] != normalized[1]: raise RuntimeError('correctness output differs')
        stage = 'fixture-preflight'
        a = target('preflight-before', binaries['screen-before'] + ['preflight'], 'preflight', 'before')
        b = target('preflight-after', binaries['screen-after'] + ['preflight'], 'preflight', 'after')
        if a != b: raise RuntimeError('fixture preflight differs')
        if not args.preflight_only:
            stage = 'first-bind'
            for name in cold_ids:
                for round_number, order in enumerate(config['ColdRoundOrder']):
                    for variant in order:
                        try:
                            target('cold-' + name + '-' + variant + '-' + str(round_number), binaries['screen-' + variant] + ['cold', name], 'cold', variant, round_number, name)
                        except TargetFailure:
                            pass  # Finish every declared cold process before ordinary result decisions; no replacement samples.
            cold_comparisons = summarize_cold()
            save(public / 'raw/cold.json', cold)
            if len(cold) != 18 or not all(r['Complete'] for r in cold_comparisons):
                stage = 'cold-incomplete'; raise RuntimeError('declared cold groups incomplete; no pilot')
            if any(not r['SemanticsAgree'] for r in cold_comparisons):
                stage = 'cold-semantics'; raise RuntimeError('cold semantics differ; no pilot')
            if any(r['CandidateExtraAllocationPairs'] for r in cold_comparisons):
                stage = 'cold-allocation-adverse'; raise RuntimeError('candidate cold allocation increased; no pilot')
            stage = 'original-only-pilot'
            raw = target('pilot-before', binaries['screen-before'] + ['pilot'], 'pilot', 'before')
            for row in raw:
                count = max(seeds[row['Id']], math.ceil(seeds[row['Id']] * config['TargetBatchMs'] / row['BatchMs']))
                if count > 2147483647: raise RuntimeError('iteration count exceeds supported bound')
                iterations[row['Id']] = count
            iteration_file = private / 'iterations.tsv'
            iteration_file.write_text(''.join(name + '\t' + str(iterations[name]) + '\n' for name in row_ids))
            iteration_hash = sha(iteration_file)
            save(public / 'raw/pilot.json', pilot)
            save(public / 'iterations.json', {'Backend': args.backend, 'Iterations': iterations, 'MapSHA256': iteration_hash, 'FrozenBeforeCandidateTiming': True})
            stage = 'fixed-warm-timing'
            for round_number, order in enumerate(config['RoundOrder']):
                for variant in order:
                    target('timing-' + variant + '-' + str(round_number), binaries['screen-' + variant] + ['timing', '--iterations-file', iteration_file], 'timing', variant, round_number)
            semantics = STATE_FIELDS | PROBE_FIELDS | {'Events', 'Checksum', 'ExternalEvents', 'ExternalChecksum', 'Iterations'}
            for index, name in enumerate(row_ids):
                left = [item['Rows'][index] for item in timing if item['Variant'] == 'before']
                right = [item['Rows'][index] for item in timing if item['Variant'] == 'after']
                ratios = [b['Ns'] / a['Ns'] for a, b in zip(left, right)]
                comparisons.append({'Id': name, 'BeforeMedianNs': statistics.median(x['Ns'] for x in left), 'AfterMedianNs': statistics.median(x['Ns'] for x in right),
                    'CandidateOverBaselineRatios': ratios, 'GeometricCandidateOverBaseline': math.exp(statistics.mean(map(math.log, ratios))),
                    'BeforeBytes': [x['Bytes'] for x in left], 'AfterBytes': [x['Bytes'] for x in right],
                    'CandidateExtraAllocationPairs': sum(b['Bytes'] > a['Bytes'] for a, b in zip(left, right)),
                    'SemanticsAgree': all(all(a[key] == b[key] for key in semantics) for a, b in zip(left, right)),
                    'AfterSlowerPairs': sum(b['Ns'] > a['Ns'] for a, b in zip(left, right)),
                    'ShortestBatchMs': min(x['BatchMs'] for x in left + right), 'ShortBatch': any(x['BatchMs'] < 200 for x in left + right),
                    'TimingReviewRequired': any(b['Ns'] > a['Ns'] for a, b in zip(left, right)),
                    'ObservedGC': any(x['Gc0'] + x['Gc1'] + x['Gc2'] > 0 for x in left + right)})
            if any(not r['SemanticsAgree'] or r['CandidateExtraAllocationPairs'] for r in comparisons):
                stage = 'warm-adverse'; raise RuntimeError('warm semantics or allocation rejection')
        verify_execution()
        status, stage = 'completed', 'completed'
    except BaseException:
        (private / 'failure.txt').write_text(traceback.format_exc())
    finally:
        try: verify_execution()
        except BaseException:
            if status == 'completed': stage = 'final-integrity'
            status = 'incomplete'
            with (private / 'failure.txt').open('a') as failure: failure.write(traceback.format_exc())
        if time.monotonic() >= deadline: status, stage = 'incomplete', 'deadline'
        payloads = {
            'status.json': {'Status': status, 'Stage': stage, 'Mode': 'preflight-only' if args.preflight_only else 'binding-rejection-screen', 'AcceptanceEvidence': False},
            'provenance.json': provenance,
            'config.json': dict(config, Backend=args.backend, DeadlineSeconds=args.deadline_seconds, PreflightOnly=args.preflight_only),
            'correctness.json': {'Policy': policy, 'Suites': correctness, 'Fixtures': fixtures},
            'raw/cold.json': cold,
            'raw/pilot.json': pilot,
            'iterations.json': {'Backend': args.backend, 'Iterations': iterations, 'MapSHA256': iteration_hash, 'FrozenBeforeCandidateTiming': iteration_hash is not None},
            'raw/timing.json': timing,
            'summary.json': {'Status': status, 'AcceptanceEvidence': False, 'Backend': args.backend,
                'CompletedColdProcesses': sum(x['Complete'] for x in cold), 'CompletedTimingProcesses': sum(x['Complete'] for x in timing),
                'ColdLatencyAcceptanceEstablished': False, 'ColdComparisons': cold_comparisons, 'Comparisons': comparisons}}
        if set(payloads) != set(PUBLIC_FILES): raise RuntimeError('public allowlist mismatch')
        for name in PUBLIC_FILES: save(public / name, payloads[name])
        if set(str(p.relative_to(public)) for p in public.rglob('*') if p.is_file()) != set(PUBLIC_FILES): raise RuntimeError('unexpected public file')
        try: verify_execution()
        except BaseException:
            if status == 'completed': stage = 'final-integrity'
            status = 'incomplete'
            with (private / 'failure.txt').open('a') as failure: failure.write(traceback.format_exc())
        if time.monotonic() >= deadline: status, stage = 'incomplete', 'deadline'
        if payloads['status.json']['Status'] != status or payloads['status.json']['Stage'] != stage:
            payloads['status.json'].update(Status=status, Stage=stage); payloads['summary.json']['Status'] = status
            save(public / 'status.json', payloads['status.json']); save(public / 'summary.json', payloads['summary.json'])
    signal.setitimer(signal.ITIMER_REAL, 0)
    print('Binding screen: ' + status + '. Explicit public JSON allowlist only; AcceptanceEvidence=false.')
    return 0 if status == 'completed' else 1

if __name__ == '__main__': raise SystemExit(main())
