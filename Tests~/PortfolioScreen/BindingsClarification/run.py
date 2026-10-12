#!/usr/bin/env python3
"""One fixed AA/AB clarification of untouched binding-screen controls and first Bind."""
import argparse
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import re
import signal
import statistics
import subprocess
import sys
import time
import traceback

HERE = Path(__file__).resolve().parent
ORIGINAL = HERE.parent / 'Bindings'
SUPPORT = HERE.parent / 'Hierarchical/build_support.py'
PUBLIC_FILES = ('status.json', 'config.json', 'provenance.json', 'preflight.json', 'raw/samples.json', 'summary.json')


def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def tree(path): return {str(p.relative_to(path)): sha(p) for p in sorted(path.rglob('*')) if p.is_file()}
def save(path, value): path.write_text(json.dumps(value, indent=2, allow_nan=False) + '\n')
def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module); return module


def main():
    entered = time.monotonic()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--backend', choices=('net', 'mono'), required=True)
    parser.add_argument('--git-root', type=Path, default=HERE.parents[2])
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--mono', default='mono')
    parser.add_argument('--nuget', type=Path, default=Path(os.environ.get('NUGET_PACKAGES', Path.home() / '.nuget/packages')))
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--work', type=Path, required=True)
    parser.add_argument('--deadline-seconds', type=int, default=600)
    parser.add_argument('--preflight-only', action='store_true', help='Untimed existing semantic checks and launcher fixture parity; validate the fixed cold command plan without executing cold samples.')
    args = parser.parse_args()
    if not 0 < args.deadline_seconds <= 600: parser.error('fixed maximum deadline is 600 seconds')
    deadline = entered + args.deadline_seconds
    def interrupted(signum, frame): raise TimeoutError('whole-coordinator deadline or termination')
    def arm():
        signal.signal(signal.SIGALRM, interrupted); signal.signal(signal.SIGTERM, interrupted)
        signal.setitimer(signal.ITIMER_REAL, max(0.001, deadline - time.monotonic()))
    arm()
    public, private = args.out.resolve(), args.work.resolve()
    if public == private or public in private.parents or private in public.parents: parser.error('new separate non-nested out/work required')
    public.mkdir(parents=True, exist_ok=False); (public / 'raw').mkdir()
    private.mkdir(parents=True, exist_ok=False)
    sys.dont_write_bytecode = True
    frozen = json.loads((HERE / 'frozen.json').read_text()); frozen_hash = sha(HERE / 'frozen.json')
    config = json.loads((HERE / 'config.json').read_text())
    original_frozen = json.loads((ORIGINAL / 'frozen.json').read_text())
    original_config = json.loads((ORIGINAL / 'config.json').read_text())
    counts = config['OriginalIterationMaps'][args.backend]['Iterations']
    map_path = private / 'original-iterations.tsv'
    map_path.write_text(''.join(row['id'] + '\t' + str(counts[row['id']]) + '\n' for row in original_config['Rows']))
    map_hash = sha(map_path)
    original_work = private / 'original-work'
    original_out = private / 'original-out'
    commands, samples, launcher_preflight, comparisons = [], [], [], []
    identities, tool_files, tool_hashes, original_preflight, provenance = {}, {}, {}, {}, {}
    status, stage = 'incomplete', 'input-verification'
    runtime_env = dict(os.environ)
    for prefix in ('DOTNET_', 'COMPlus_'):
        runtime_env[prefix + 'ReadyToRun'] = '1'; runtime_env[prefix + 'TieredCompilation'] = '0'; runtime_env[prefix + 'gcServer'] = '0'
    runtime_env['DOTNET_ROLL_FORWARD'] = 'Disable'

    def verify_inputs():
        if sha(HERE / 'frozen.json') != frozen_hash: raise RuntimeError('supplement manifest changed')
        if {key: sha(HERE / key) for key in frozen['Files']} != frozen['Files']: raise RuntimeError('supplement input changed')
        if sha(ORIGINAL / 'frozen.json') != config['OriginalFrozenSHA256']: raise RuntimeError('original manifest changed')
        if {key: sha(ORIGINAL / key) for key in original_frozen['Files']} != original_frozen['Files']: raise RuntimeError('original screen input changed')
        if sha(SUPPORT) != original_frozen['BuildSupportSHA256'] or sha(HERE.parent / 'References.csproj') != original_frozen['ReferencesSHA256']: raise RuntimeError('original build input changed')
        if sha(map_path) != map_hash or map_hash != config['OriginalIterationMaps'][args.backend]['MapSHA256']: raise RuntimeError('original map changed')

    def verify():
        if time.monotonic() >= deadline: raise TimeoutError('whole-coordinator deadline')
        verify_inputs()
        if {key: sha(path) for key, path in tool_files.items()} != tool_hashes: raise RuntimeError('toolchain changed')
        if {key: tree(private / key) for key in identities} != identities: raise RuntimeError('source or compiled tree changed')
        if time.monotonic() >= deadline: raise TimeoutError('whole-coordinator deadline')

    def checked(name, command):
        verify()
        item = {'Name': name, 'Command': list(map(str, command))}; commands.append(item)
        with (private / (name + '.stdout')).open('wb') as out, (private / (name + '.stderr')).open('wb') as err:
            process = subprocess.Popen(item['Command'], stdout=out, stderr=err, env=runtime_env, start_new_session=True)
            try: item['ExitCode'] = process.wait(timeout=max(0.001, deadline - time.monotonic()))
            except subprocess.TimeoutExpired: item['ExitCode'] = None
            finally:
                try: os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError: pass
                process.wait(); save(private / 'commands.json', commands)
        output = (private / (name + '.stdout')).read_bytes()
        verify(); return output, item.get('ExitCode')

    def parse_observation(output, family, fixture, mode):
        # Only fixed labels, runtime version and finite numerical fields can enter public raw samples.
        value = json.loads(output)
        runtime = {'RuntimeVersion', 'ServerGC', 'TieringConfigured', 'ReadyToRunRequested', 'TieringRequested'}
        state = {'OwnedSubscriptions', 'ExternalSubscriptions', 'TagCount'}
        probe = {p + s for p in ('BeforeProbe', 'AfterProbe') for s in ('Events', 'Checksum', 'ExternalEvents', 'ExternalChecksum')}
        measured = {'Ticks', 'StopwatchFrequency', 'Ns', 'Bytes', 'Gc0', 'Gc1', 'Gc2', 'Events', 'Checksum', 'ExternalEvents', 'ExternalChecksum'}
        allowed = {'Mode', 'Id'} | runtime | state
        if mode == 'preflight': allowed |= probe | {'Binds', 'Tags'}
        elif family == 'warm': allowed |= measured | probe | {'Iterations', 'BatchMs'}
        else: allowed |= measured | {'Binds', 'BindingCapacityBefore', 'CallbackCapacityBefore'}
        if set(value) != allowed or value['Mode'] != mode or value['Id'] != fixture: raise ValueError('unexpected observation schema')
        if not isinstance(value['RuntimeVersion'], str) or re.fullmatch(r'[0-9]+(?:\.[0-9]+){2,3}', value['RuntimeVersion']) is None: raise ValueError('invalid runtime version')
        if any(type(v) not in (int, float) or not math.isfinite(v) for k, v in value.items() if k not in ('Mode', 'Id', 'RuntimeVersion')): raise ValueError('non-numerical observation')
        return value

    def validate_observation(row, family, fixture, mode):
        if row['ServerGC'] != 0 or row['OwnedSubscriptions'] != 0: return False
        if args.backend == 'net' and (row['RuntimeVersion'] != '8.0.31' or row['TieringConfigured'] != 0 or row['ReadyToRunRequested'] != 1 or row['TieringRequested'] != 0): return False
        if args.backend == 'mono' and (row['TieringConfigured'] not in (-1, 0) or row['ReadyToRunRequested'] != -1 or row['TieringRequested'] != -1): return False
        if mode != 'preflight' and any(row[k] <= 0 for k in ('Ticks', 'StopwatchFrequency', 'Ns')): return False
        if mode != 'preflight' and any(row[k] < 0 for k in ('Bytes', 'Gc0', 'Gc1', 'Gc2')): return False
        if family == 'warm':
            if row['TagCount'] != 0 or row['ExternalSubscriptions'] != (2 if fixture == 'direct-dispatch' else 0): return False
            if mode == 'timing' and (row['Iterations'] != counts[fixture] or row['BatchMs'] <= 0): return False
            return all(row['BeforeProbe' + key] == row['AfterProbe' + key] for key in ('Events', 'Checksum', 'ExternalEvents', 'ExternalChecksum'))
        return row['Binds'] == 1 and row['BindingCapacityBefore'] == 0 and row['TagCount'] == int(fixture == 'first-present') and row['ExternalSubscriptions'] == (2 if fixture == 'first-external-multicast' else 0)

    try:
        verify_inputs()
        stage = 'original-build-and-preflight'
        # Invoke the frozen coordinator in-process so its child process-group cleanup owns every build/test.
        # Its deadline is the same remaining whole-run budget, not a new 600-second budget.
        original_runner = load_module('binding_frozen_coordinator', ORIGINAL / 'run.py')
        saved_argv = sys.argv
        sys.argv = [str(ORIGINAL / 'run.py'), '--backend', args.backend, '--git-root', str(args.git_root.resolve()),
            '--dotnet', args.dotnet, '--mono', args.mono, '--nuget', str(args.nuget.resolve()),
            '--out', str(original_out), '--work', str(original_work), '--preflight-only',
            '--deadline-seconds', str(max(1, math.floor(deadline - time.monotonic())))]
        try: code = original_runner.main()
        finally: sys.argv = saved_argv; arm()
        if code != 0: raise RuntimeError('frozen original preflight failed')
        original_preflight = json.loads((original_out / 'correctness.json').read_text())
        original_provenance = json.loads((original_out / 'provenance.json').read_text())
        if json.loads((original_out / 'status.json').read_text())['Status'] != 'completed': raise RuntimeError('original preflight incomplete')
        support = load_module('binding_frozen_build_support', SUPPORT)
        dotnet, mono, nuget = original_runner.resolve(args.dotnet), original_runner.resolve(args.mono), args.nuget.resolve()
        tool_files = support.input_files(args.backend, dotnet, mono, nuget)
        tool_hashes = {key: sha(path) for key, path in tool_files.items()}
        if tool_hashes != original_provenance['ToolInputHashes']: raise RuntimeError('original preflight toolchain drift')
        for child in original_work.iterdir():
            if child.is_dir(): identities[str(child.relative_to(private))] = tree(child)
        stage = 'launcher-build'
        launcher_dir = private / 'launcher'; launcher_dir.mkdir()
        launcher = launcher_dir / ('DiagnosticHost.exe' if args.backend == 'mono' else 'DiagnosticHost.dll')
        sdk, _, _ = support.toolchain(dotnet)
        refs, _ = support.references(args.backend, dotnet, nuget)
        response = launcher_dir / 'compile.rsp'
        flags = ['-nologo', '-optimize+', '-langversion:9.0', '-nullable:disable', '-deterministic+', '-target:exe', '-out:"' + str(launcher) + '"']
        flags += ['-r:"' + str(p) + '"' for p in refs] + ['"' + str(HERE / 'Launcher.cs') + '"']
        response.write_text('\n'.join(flags))
        _, code = checked('compile-launcher', [dotnet, sdk / 'Roslyn/bincore/csc.dll', '@' + str(response)])
        if code != 0: raise RuntimeError('launcher compilation failed')
        # A separately named assembly avoids confusing its identity with the untouched Test assembly.
        launcher.with_suffix('.runtimeconfig.json').write_bytes((original_work / 'build-screen-before/Test.runtimeconfig.json').read_bytes())
        identities['launcher'] = tree(launcher_dir)
        binaries = {v: original_work / ('build-screen-' + v) / ('Test.exe' if args.backend == 'mono' else 'Test.dll') for v in ('before', 'after')}
        host = mono if args.backend == 'mono' else dotnet
        fixture_indexes = {row['Id']: row['Index'] for row in config['WarmRows']}
        def command(family, fixture, source, mode='timing'):
            if family == 'cold': return [host, binaries[source], 'cold', fixture]
            return [host, launcher, binaries[source], str(fixture_indexes[fixture]), str(counts[fixture]), mode]
        plan = []
        for family, fixtures in (('warm', [row['Id'] for row in config['WarmRows']]), ('cold', config['ColdRows'])):
            for fixture in fixtures:
                for pair, slots in enumerate(config['PairOrders']):
                    for comparison in config['ModeOrders'][pair]:
                        for slot in slots:
                            source = config['SlotSources'][comparison][slot]
                            plan.append({'Family': family, 'Id': fixture, 'Comparison': comparison, 'Pair': pair, 'Slot': slot, 'Source': source})
        if len(plan) != 80: raise RuntimeError('fixed invocation count differs')
        if any(command('cold', fixture, 'before')[1] != binaries['before'] for fixture in config['ColdRows']): raise RuntimeError('cold route does not use original entry point')
        stage = 'launcher-fixture-preflight'
        for fixture in fixture_indexes:
            for source in ('before', 'after'):
                output, code = checked('launcher-preflight-' + fixture + '-' + source, command('warm', fixture, source, 'preflight'))
                observation = parse_observation(output, 'warm', fixture, 'preflight')
                matching = next(row for process in original_preflight['Fixtures'] if process['Variant'] == source for row in process['Rows'] if row['Id'] == fixture)
                valid = code == 0 and validate_observation(observation, 'warm', fixture, 'preflight') and observation == matching
                launcher_preflight.append({'Id': fixture, 'Source': source, 'Valid': valid, 'Observation': observation})
                if not valid: raise RuntimeError('launcher preflight differs from original fixture')
        provenance = {'OriginalScreenCommit': config['OriginalScreenCommit'], 'OriginalFrozenSHA256': config['OriginalFrozenSHA256'],
            'OriginalProvenance': original_provenance, 'PreparedFiles': frozen['Files'], 'SupplementManifestSHA256': frozen_hash,
            'OriginalMapSHA256': map_hash, 'BeforeBinarySHA256': sha(binaries['before']), 'AfterBinarySHA256': sha(binaries['after']),
            'LauncherSHA256': sha(launcher), 'LauncherRuntimeConfigSHA256': sha(launcher.with_suffix('.runtimeconfig.json')),
            'AASlotsUseSamePhysicalBeforeAssemblyAndDependencies': True}
        if not args.preflight_only:
            stage = 'fixed-diagnostic-samples'
            for ordinal, spec in enumerate(plan):
                item = dict(spec, Ordinal=ordinal, Complete=False, ExitCode=None, Observation=None)
                samples.append(item)
                output, code = checked('sample-' + str(ordinal), command(spec['Family'], spec['Id'], spec['Source']))
                item['ExitCode'] = code
                try:
                    item['Observation'] = parse_observation(output, spec['Family'], spec['Id'], 'timing' if spec['Family'] == 'warm' else 'cold')
                    item['Complete'] = code == 0 and validate_observation(item['Observation'], spec['Family'], spec['Id'], 'timing' if spec['Family'] == 'warm' else 'cold')
                except (ValueError, TypeError, KeyError): pass
                # No ordinary numerical/semantic result changes the fixed sequence or triggers replacement samples.
            stage = 'diagnostic-summary'
            excluded = {'Ticks', 'StopwatchFrequency', 'Ns', 'Bytes', 'BatchMs', 'Gc0', 'Gc1', 'Gc2'}
            for family, fixtures in (('warm', fixture_indexes), ('cold', config['ColdRows'])):
                for fixture in fixtures:
                    for comparison in ('AA', 'AB'):
                        selected = [v for v in samples if v['Family'] == family and v['Id'] == fixture and v['Comparison'] == comparison]
                        result = {'Family': family, 'Id': fixture, 'Comparison': comparison, 'Complete': len(selected) == 8 and all(v['Complete'] for v in selected)}
                        if result['Complete']:
                            left = [next(v['Observation'] for v in selected if v['Pair'] == pair and v['Slot'] == 'left') for pair in range(4)]
                            right = [next(v['Observation'] for v in selected if v['Pair'] == pair and v['Slot'] == 'right') for pair in range(4)]
                            ratios = [b['Ns'] / a['Ns'] for a, b in zip(left, right)]
                            result.update(RightOverLeftRatios=ratios, GeometricRightOverLeft=math.exp(statistics.mean(map(math.log, ratios))),
                                RightSlowerPairs=sum(b['Ns'] > a['Ns'] for a, b in zip(left, right)),
                                LeftBytes=[v['Bytes'] for v in left], RightBytes=[v['Bytes'] for v in right],
                                RightExtraAllocationPairs=sum(b['Bytes'] > a['Bytes'] for a, b in zip(left, right)),
                                SemanticsAgree=all(all(a[k] == b[k] for k in a if k not in excluded) for a, b in zip(left, right)),
                                ShortBatch=family == 'warm' and any(v['BatchMs'] < 200 for v in left + right),
                                ObservedGC=any(v['Gc0'] + v['Gc1'] + v['Gc2'] > 0 for v in left + right))
                        comparisons.append(result)
            if not all(v['Complete'] and v['SemanticsAgree'] for v in comparisons): raise RuntimeError('diagnostic observations incomplete or semantically adverse')
        verify(); status, stage = 'completed', 'completed'
    except BaseException:
        (private / 'failure.txt').write_text(traceback.format_exc())
    finally:
        try: verify()
        except BaseException:
            if status == 'completed': stage = 'final-integrity'
            status = 'incomplete'
            with (private / 'failure.txt').open('a') as failure: failure.write(traceback.format_exc())
        if time.monotonic() >= deadline: status, stage = 'incomplete', 'deadline'
        payloads = {
            'status.json': {'Status': status, 'Stage': stage, 'AcceptanceEvidence': False, 'PreflightOnly': args.preflight_only},
            'config.json': dict(config, Backend=args.backend, DeadlineSeconds=args.deadline_seconds),
            'provenance.json': provenance,
            'preflight.json': {'Original': original_preflight, 'Launcher': launcher_preflight},
            'raw/samples.json': samples,
            'summary.json': {'Status': status, 'AcceptanceEvidence': False, 'OriginalContextCleared': False, 'AASubtractionApplied': False,
                'PlannedMeasuredProcesses': 0 if args.preflight_only else 80, 'CompletedMeasuredProcesses': sum(v['Complete'] for v in samples), 'Comparisons': comparisons}}
        if set(payloads) != set(PUBLIC_FILES): raise RuntimeError('public allowlist differs')
        for name, value in payloads.items(): save(public / name, value)
        if set(str(p.relative_to(public)) for p in public.rglob('*') if p.is_file()) != set(PUBLIC_FILES): raise RuntimeError('unexpected public output')
        try: verify()
        except BaseException:
            status, stage = 'incomplete', 'final-integrity'
            payloads['status.json'].update(Status=status, Stage=stage); payloads['summary.json']['Status'] = status
            save(public / 'status.json', payloads['status.json']); save(public / 'summary.json', payloads['summary.json'])
        signal.setitimer(signal.ITIMER_REAL, 0)
    print('Binding clarification: ' + status + '; no qualification or original-context clearance.')
    return 0 if status == 'completed' else 1

if __name__ == '__main__': raise SystemExit(main())
