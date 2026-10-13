#!/usr/bin/env python3
"""One sealed portable discovery screen; explicit numeric/hash reports only."""
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
import subprocess
import sys
import tarfile
import time
import traceback
import types

HERE = Path(__file__).resolve().parent
BASE_REVISION = 'f7c99546dbf029d70896c0f05e5119b19ffd6d26'
PUBLIC = ('status.json', 'provenance.json', 'config.json', 'correctness.json', 'raw/cold.json', 'summary.json')
ORDER = ('original', 'candidate')
MODES = ('empty', 'retry')
SHAPES = ('n0', 'n1', 'n32')
PAIR_ORDERS = (ORDER, ORDER[::-1], ORDER[::-1], ORDER)
EXPECTED_CONFIG = {
    'ColdShapes': list(SHAPES), 'DeclarationCounts': {'n0': 0, 'n1': 1, 'n32': 32},
    'AttributedAssemblyCounts': {'n0': 0, 'n1': 1, 'n32': 1},
    'PairOrders': [list(order) for order in PAIR_ORDERS], 'SemanticModes': list(MODES),
    'MaxBuilds': 4, 'SemanticProcesses': 4, 'ColdProcesses': 24, 'WarmProcesses': 0,
    'PilotProcesses': 0, 'DeadlineSeconds': 300, 'SDKVersion': '8.0.425',
    'CLRVersion': '8.0.31', 'MonoVersion': '6.8.0.105', 'NativeCapture': False, 'AcceptanceEvidence': False, 'Variants': list(ORDER), 'PointerSize': 8,
    'ForcedGC': False, 'WarmTiming': False, 'ReplacementSamples': False, 'AutomaticContinuation': False,
    'FirstBridgeJitIncluded': True, 'PreBoundaryInventoryPrimed': True, 'WholeHeapRetainedBytesMeasured': False,
}
MEASUREMENT_KEYS = {'RecordType', 'Shape', 'DeclarationCount', 'AttributedAssemblyCount', 'ObservedCount', 'Ticks', 'Frequency',
    'AllocatedBytes', 'GC0', 'GC1', 'GC2', 'PointerSize', 'OperationPass', 'OperationErrorType', 'FirstBridgeJitIncluded',
    'PreBoundaryInventoryPrimed', 'WholeHeapRetainedBytesMeasured'}
SEMANTIC_KEYS = {'RecordType', 'Shape', 'SemanticsPass', 'ErrorType', 'BindingPass', 'RegistryPass', 'PostAttributeTypePass',
    'AssemblyInventoryPass', 'ObservedCount', 'SnapshotSHA256', 'InventorySHA256', 'AssemblyName', 'DeclarationCount'}
SEMANTIC_BOOLS = ('SemanticsPass', 'BindingPass', 'RegistryPass', 'PostAttributeTypePass', 'AssemblyInventoryPass')
MAIN_IDENTITY = 'Test, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null'
INVENTORY_KEYS = {'FullName', 'Location', 'Mvid', 'IsDynamic', 'IsDefaultContext', 'IsCurrentDomain', 'IsEntry'}
RAW_SUFFIXES = ('.stdout', '.stderr', '.snapshot.bin', '.metadata.json', '.trace.txt', '.inventory.json')


class IntegrityStop(Exception):
    pass


class GateStop(Exception):
    pass


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, allow_nan=False).encode()).hexdigest()


def unique_object(pairs):
    value = {}
    for key, item in pairs:
        if key in value:
            raise ValueError('Duplicate JSON key')
        value[key] = item
    return value


def strict_json(data):
    def bad_constant(unused):
        raise ValueError('Nonfinite JSON value')
    return json.loads(data, object_pairs_hook=unique_object, parse_constant=bad_constant)


def save(path, value):
    path.write_text(json.dumps(value, indent=2, allow_nan=False) + '\n', encoding='utf-8')


def tree(path):
    if path.is_symlink():
        raise IntegrityStop('Symlinked tree')
    result = {}
    for item in sorted(path.rglob('*')):
        if item.is_symlink():
            raise IntegrityStop('Symlinked tree member')
        if item.is_file():
            result[str(item.relative_to(path))] = sha(item)
    return result


def load_verified(name, path, expected):
    """Execute the same verified bytes, rather than reopening after verification."""
    if path.is_symlink():
        raise IntegrityStop('Symlinked module')
    data = path.read_bytes()
    if hashlib.sha256(data).hexdigest() != expected:
        raise IntegrityStop('Module identity changed')
    module = types.ModuleType(name)
    module.__file__ = str(path)
    sys.modules[name] = module
    exec(compile(data, str(path), 'exec'), module.__dict__)
    return module


def runtime_lines(data, backend):
    lines = data.decode('utf-8').splitlines()
    if not lines or not lines[0].startswith('PORTABLE_RUNTIME '):
        raise ValueError('Runtime header absent')
    value = strict_json(lines[0][len('PORTABLE_RUNTIME '):])
    if type(value) is not dict or set(value) != {'EnvironmentVersion', 'MonoVersion', 'PointerSize'}:
        raise ValueError('Runtime identity schema')
    if type(value['PointerSize']) is not int or value['PointerSize'] != 8:
        raise ValueError('Runtime pointer size')
    if type(value['EnvironmentVersion']) is not str or re.fullmatch(r'[0-9]{1,5}(?:\.[0-9]{1,5}){2,3}', value['EnvironmentVersion']) is None:
        raise ValueError('Runtime version schema')
    if any(int(part) > 65535 for part in value['EnvironmentVersion'].split('.')):
        raise ValueError('Runtime version range')
    if backend == 'net' and (value['EnvironmentVersion'] != '8.0.31' or value['MonoVersion'] != ''):
        raise ValueError('Runtime identity mismatch')
    if backend == 'mono' and value['MonoVersion'] != '6.8.0.105':
        raise ValueError('Runtime identity mismatch')
    return value, lines[1:]


def normalized_inventory(value, expected_main, expected_fixture, shape, backend):
    if type(value) is not dict or set(value) != {'BindingModel', 'Before', 'After'}:
        raise ValueError('Inventory schema')
    expected_model = 'net-default-alc' if backend == 'net' else 'mono-current-domain'
    if value['BindingModel'] != expected_model:
        raise ValueError('Inventory binding model')
    result = {'BindingModel': expected_model}
    for boundary in ('Before', 'After'):
        entries = value[boundary]
        if type(entries) is not list or not entries:
            raise ValueError('Inventory boundary')
        normalized, mains, fixtures, entry_assemblies = [], [], [], []
        for entry in entries:
            if type(entry) is not dict or set(entry) != INVENTORY_KEYS:
                raise ValueError('Inventory entry schema')
            if any(type(entry[key]) is not str for key in ('FullName', 'Location', 'Mvid')):
                raise ValueError('Inventory string type')
            if any(type(entry[key]) is not bool for key in ('IsDynamic', 'IsCurrentDomain', 'IsEntry')):
                raise ValueError('Inventory flag type')
            if entry['IsDynamic'] or entry['IsCurrentDomain'] is not True or (entry['IsDefaultContext'] is not True if backend == 'net' else entry['IsDefaultContext'] is not None):
                raise ValueError('Unexpected loading context')
            if entry['FullName'].split(',')[0] == 'Test':
                mains.append(entry)
            if entry['FullName'].split(',')[0].startswith('DiscoveryDeclarations'):
                fixtures.append(entry)
            if entry['IsEntry']:
                entry_assemblies.append(entry)
            item = dict(entry)
            if entry['IsEntry']:
                if entry['FullName'] != MAIN_IDENTITY or entry['Location'] != str(expected_main) or not entry['Mvid']:
                    raise ValueError('Entry assembly identity')
                item['Location'] = '<EXPECTED_MAIN_ASSEMBLY>'
                item['Mvid'] = '<EXPECTED_MAIN_MVID>'
            normalized.append(item)
        if len(mains) != 1 or len(entry_assemblies) != 1 or mains[0] is not entry_assemblies[0]:
            raise ValueError('Entry assembly cardinality')
        count = int(shape[1:])
        if count == 0:
            if fixtures:
                raise ValueError('Unexpected empty-control fixture')
        else:
            identity = 'DiscoveryDeclarations' + str(count) + ', Version=0.0.0.0, Culture=neutral, PublicKeyToken=null'
            if len(fixtures) != 1 or fixtures[0]['FullName'] != identity or fixtures[0]['Location'] != str(expected_fixture):
                raise ValueError('Fixture identity')
            if fixtures[0]['IsEntry'] or not fixtures[0]['Mvid']:
                raise ValueError('Fixture identity flags')
        result[boundary] = normalized
    return result


def parse_cold(data, backend, shape, main_path, fixture_path):
    runtime, lines = runtime_lines(data['Stdout'] or b'', backend)
    if len(lines) != 2:
        raise ValueError('Cold output cardinality')
    a, s = [strict_json(line) for line in lines]
    if type(a) is not dict or set(a) != MEASUREMENT_KEYS or type(s) is not dict or set(s) != SEMANTIC_KEYS:
        raise ValueError('Cold schema')
    for key in ('DeclarationCount', 'AttributedAssemblyCount', 'Ticks', 'Frequency', 'AllocatedBytes', 'GC0', 'GC1', 'GC2', 'PointerSize'):
        maximum = (1 << 63) - 1 if key in ('Ticks', 'Frequency', 'AllocatedBytes') else (1 << 31) - 1
        if type(a[key]) is not int or a[key] < 0 or a[key] > maximum:
            raise ValueError('Counter range')
    if type(a['ObservedCount']) is not int or not -1 <= a['ObservedCount'] <= (1 << 31) - 1:
        raise ValueError('Observed count range')
    for key in ('OperationPass', 'FirstBridgeJitIncluded', 'PreBoundaryInventoryPrimed', 'WholeHeapRetainedBytesMeasured'):
        if type(a[key]) is not bool:
            raise ValueError('Measurement flag type')
    if any(type(s[key]) is not bool for key in SEMANTIC_BOOLS):
        raise ValueError('Semantic flag type')
    if any(type(s[key]) is not str for key in ('ErrorType', 'SnapshotSHA256', 'InventorySHA256', 'AssemblyName')) or type(a['OperationErrorType']) is not str:
        raise ValueError('Semantic string type')
    if any(type(s[key]) is not int for key in ('ObservedCount', 'DeclarationCount')):
        raise ValueError('Semantic count type')
    n = int(shape[1:])
    valid = (a['RecordType'] == 'measurement' and s['RecordType'] == 'semantics' and a['Shape'] == s['Shape'] == shape
        and a['OperationPass'] and a['OperationErrorType'] == '' and all(s[key] for key in SEMANTIC_BOOLS) and s['ErrorType'] == ''
        and a['DeclarationCount'] == a['ObservedCount'] == s['DeclarationCount'] == s['ObservedCount'] == n
        and a['AttributedAssemblyCount'] == (0 if n == 0 else 1) and a['PointerSize'] == 8 and a['Frequency'] > 0
        and a['FirstBridgeJitIncluded'] and a['PreBoundaryInventoryPrimed'] and not a['WholeHeapRetainedBytesMeasured']
        and s['AssemblyName'] == 'Test' and bool(data['Snapshot']) and bool(data['Inventory'])
        and s['SnapshotSHA256'] == hashlib.sha256(data['Snapshot'] or b'').hexdigest()
        and s['InventorySHA256'] == hashlib.sha256(data['Inventory'] or b'').hexdigest())
    inventory = normalized_inventory(strict_json(data['Inventory'] or b''), main_path, fixture_path, shape, backend)
    return {'Runtime': runtime, 'Measurement': a, 'Semantics': s, 'Inventory': inventory, 'Valid': bool(valid)}


def main():
    started = time.monotonic()
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--backend', choices=('net', 'mono'), required=True)
    ap.add_argument('--dotnet', default='dotnet')
    ap.add_argument('--mono', default='mono')
    ap.add_argument('--nuget', type=Path, required=True)
    ap.add_argument('--git-root', type=Path, required=True)
    ap.add_argument('--out', type=Path, required=True)
    ap.add_argument('--work', type=Path, required=True)
    ap.add_argument('--expected-seal', required=True)
    ap.add_argument('--deadline-seconds', type=int, choices=(300,), default=300)
    args = ap.parse_args()
    try:
        frozen_bytes = (HERE / 'frozen.json').read_bytes()
        seal = hashlib.sha256(frozen_bytes).hexdigest()
        if seal != args.expected_seal:
            raise ValueError('Seal mismatch')
        frozen = strict_json(frozen_bytes)
        pins = strict_json((HERE / 'source-pins.json').read_bytes())
        config = strict_json((HERE / 'config.json').read_bytes())
    except (OSError, ValueError, TypeError):
        ap.error('Required sealed package is invalid')
    public, private = args.out.resolve(), args.work.resolve()
    if public == private or public in private.parents or private in public.parents:
        ap.error('Separate nonnested new output/work directories required')
    if HERE == public or HERE in public.parents or HERE == private or HERE in private.parents:
        ap.error('Outputs must be outside package')
    public.mkdir(parents=True, exist_ok=False)
    (public / 'raw').mkdir()
    private.mkdir(parents=True, exist_ok=False)
    deadline = started + args.deadline_seconds
    dotnet = Path(shutil.which(args.dotnet) or args.dotnet).resolve()
    mono = Path(shutil.which(args.mono) or args.mono).resolve()
    nuget = args.nuget.resolve()
    status = {'State': 'incomplete', 'Stage': 'setup', 'Backend': args.backend, 'BuildAttempts': 0, 'Builds': 0,
        'SemanticTargets': 0, 'SemanticCompleted': 0, 'ColdTargets': 0, 'ColdCompleted': 0, 'ColdRows': 0,
        'WarmTargets': 0, 'PilotTargets': 0, 'AcceptanceEvidence': False, 'FrozenSHA256': seal,
        'Reason': 'none', 'DeadlineExceeded': False, 'IntegrityFailure': False, 'FinalizationFailure': False, 'FixedSequenceValidated': False}
    provenance = {'BaseRevision': BASE_REVISION, 'FrozenSHA256': seal, 'SDKVersion': '8.0.425', 'CLRVersion': '8.0.31',
        'MonoVersion': '6.8.0.105' if args.backend == 'mono' else None, 'PointerSize': 8, 'RuntimeSourceCount': 44,
        'RuntimeSourceManifestSHA256': {}, 'ToolchainManifestSHA256': None, 'ToolchainInputCount': 0,
        'BuildArtifactManifestSHA256': None, 'SharedFixtureSHA256': {}, 'SharedDependencyManifestSHA256': None,
        'ActualRuntimeVersions': [], 'BuildDiagnostics': [], 'NativeCapturePerformed': False,
        'MonoGacAndNativeClosurePinned': False}
    correctness = {'SemanticGatePass': False, 'SnapshotPairs': [], 'TracePairs': [], 'MetadataGatePass': False,
        'RuntimeTypes': None, 'RuntimeMethods': None, 'UnchangedMetadataDifferences': None,
        'UnrelatedRawILFailures': None, 'MechanismReportSHA256': None, 'SemanticDiagnostics': [], 'DiagnosticProjectionFailed': False}
    summary = {'Pairs': [], 'AllSemanticPairsPass': False, 'AnyPairedAllocationIncrease': False,
        'CandidateSlowerPairCounts': {shape: 0 for shape in SHAPES}, 'AcceptanceEvidence': False,
        'LatencyPassEstablished': False, 'AutomaticContinuation': False, 'ColdLatencyUnresolved': True,
        'Decision': 'INCOMPLETE_NO_CONTINUATION'}
    commands, artifact_hashes, raw_hashes = [], {}, {}
    pending_artifacts, pending_raw = set(), set()
    tool_files, tool_hashes = {}, {}
    sources_ready, support, projector = False, None, None
    child_scope, active_proc, canceled, finalizing = False, None, False, False
    successful_protocol = False
    cold, parsed = [], {}
    build_dirs = ('build-original', 'build-candidate', 'build-fixtures', 'shared-dependencies')
    semantic_plan = [(mode, variant, 'semantic-' + mode + '-' + variant) for mode in MODES for variant in ORDER]
    cold_plan = [(shape, pair, variant, 'cold-' + shape + '-' + str(pair) + '-' + variant)
                 for shape in SHAPES for pair, order in enumerate(PAIR_ORDERS) for variant in order]
    compile_plan = ('compile-original', 'compile-candidate', 'compile-fixture1', 'compile-fixture32')
    allowed = ('archive-baseline',) + compile_plan + tuple(row[2] for row in semantic_plan) + tuple(row[3] for row in cold_plan)
    child_env = {key: value for key, value in os.environ.items() if not key.startswith(('DOTNET_', 'COMPlus_', 'CORECLR_', 'MONO_'))}
    child_env.update(DOTNET_ROLL_FORWARD='Disable', DOTNET_MULTILEVEL_LOOKUP='0', DOTNET_ROOT=str(dotnet.parent),
                     MONO_PATH=str(private / 'shared-dependencies'), DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',
                     DOTNET_NOLOGO='1', DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_EnableDiagnostics='0')
    for prefix in ('DOTNET_', 'COMPlus_'):
        child_env[prefix + 'ReadyToRun'] = '1'
        child_env[prefix + 'TieredCompilation'] = '0'
        child_env[prefix + 'gcServer'] = '0'
    controlled_keys = ('DOTNET_ROLL_FORWARD', 'DOTNET_MULTILEVEL_LOOKUP', 'DOTNET_ROOT', 'MONO_PATH',
        'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'DOTNET_NOLOGO', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_EnableDiagnostics')
    controlled_keys += tuple(prefix + suffix for prefix in ('DOTNET_', 'COMPlus_') for suffix in ('ReadyToRun', 'TieredCompilation', 'gcServer'))
    save(private / 'child-environment-provenance.json', {'SHA256': digest(child_env),
        'ControlledOverrides': {key: child_env[key] for key in controlled_keys}})

    def artifacts_now():
        return {directory + '/' + name: value for directory in build_dirs for name, value in tree(private / directory).items()}

    def raw_now():
        result = {}
        for path in sorted(private.iterdir()):
            if path.name.endswith(RAW_SUFFIXES):
                if path.is_symlink() or not path.is_file():
                    raise IntegrityStop('Unexpected raw output member')
                result[path.name] = sha(path)
        return result

    def check_extension(actual, pinned, pending):
        if not set(pinned) <= set(actual) <= set(pinned) | pending or any(actual[name] != value for name, value in pinned.items()):
            raise IntegrityStop('Pinned output identity or inventory changed')

    def check_time():
        if canceled or time.monotonic() >= deadline:
            raise TimeoutError('Fixed deadline or termination')

    def verify(check_deadline=True):
        if check_deadline:
            check_time()
        if (HERE / 'frozen.json').read_bytes() != frozen_bytes:
            raise IntegrityStop('Frozen identity changed')
        actual = tree(HERE)
        if set(actual) != set(frozen['Files']) | {'frozen.json'} or any(actual[name] != value for name, value in frozen['Files'].items()):
            raise IntegrityStop('Prepared package changed')
        if sources_ready and any(tree(private / variant) != pins['RuntimeFiles'][variant] for variant in ORDER):
            raise IntegrityStop('Runtime tree changed')
        if support is not None and support.input_files(args.backend, dotnet, mono, nuget) != tool_files:
            raise IntegrityStop('Toolchain inventory changed')
        if any(sha(path) != tool_hashes[name] for name, path in tool_files.items()):
            raise IntegrityStop('Toolchain bytes changed')
        check_extension(artifacts_now(), artifact_hashes, pending_artifacts)
        check_extension(raw_now(), raw_hashes, pending_raw)
        shared = private / 'shared-dependencies'
        if shared.exists() and not pending_artifacts:
            expected_dependencies = {'System.Memory.dll', 'System.Buffers.dll', 'System.Runtime.CompilerServices.Unsafe.dll', 'System.Numerics.Vectors.dll'} if args.backend == 'mono' else set()
            if {path.name for path in shared.iterdir()} != expected_dependencies:
                raise IntegrityStop('Unexpected shared dependency inventory')
        if any((private / 'build-fixtures' / name).exists() for name in ('Test.dll', 'Test.exe')):
            raise IntegrityStop('Unexpected shared main assembly')
        if check_deadline:
            check_time()

    def begin_artifacts(names):
        nonlocal pending_artifacts
        verify()
        if pending_artifacts or set(names) & set(artifact_hashes):
            raise IntegrityStop('Artifact group replacement')
        pending_artifacts = set(names)

    def seal_artifacts(required):
        nonlocal artifact_hashes, pending_artifacts
        verify()
        actual = artifacts_now()
        if not set(required) <= set(actual):
            raise IntegrityStop('Required artifact group incomplete')
        artifact_hashes = actual
        pending_artifacts = set()
        save(private / 'artifact-hashes.json', artifact_hashes)
        verify()

    def alarm(*unused):
        nonlocal canceled
        canceled = True
        if child_scope or finalizing:
            if active_proc is not None:
                try:
                    os.killpg(active_proc.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
            return
        raise TimeoutError('Fixed deadline or termination')

    signal.signal(signal.SIGALRM, alarm)
    signal.signal(signal.SIGTERM, alarm)
    signal.setitimer(signal.ITIMER_REAL, max(0.001, deadline - time.monotonic()))

    def output_names(name):
        names = {name + '.stdout', name + '.stderr'}
        if name.startswith('semantic-'):
            stem = name[len('semantic-'):]
            names |= {stem + suffix for suffix in ('.snapshot.bin', '.metadata.json', '.trace.txt')}
        elif name.startswith('cold-'):
            names |= {name + suffix for suffix in ('.snapshot.bin', '.inventory.json')}
        return names

    def run(name, command, allow_failure=False):
        nonlocal child_scope, active_proc, pending_raw, raw_hashes
        verify()
        if name not in allowed or any(row['Name'] == name for row in commands) or pending_raw:
            raise IntegrityStop('Unplanned or repeated command')
        if len(commands) >= len(allowed) or name != allowed[len(commands)]:
            raise IntegrityStop('Command order changed')
        pending_raw = output_names(name)
        record = {'Name': name, 'Arguments': list(map(str, command)), 'Attempted': True, 'Completed': False, 'ExitCode': None}
        commands.append(record)
        if name.startswith('compile-'):
            status['BuildAttempts'] += 1
        if name.startswith('semantic-'):
            status['SemanticTargets'] += 1
        if name.startswith('cold-'):
            status['ColdTargets'] += 1
        save(private / 'commands.json', commands)
        code = None
        child_scope = True
        try:
            with (private / (name + '.stdout')).open('xb') as stdout, (private / (name + '.stderr')).open('xb') as stderr:
                try:
                    check_time()
                    active_proc = subprocess.Popen(record['Arguments'], stdout=stdout, stderr=stderr,
                        env=child_env, start_new_session=True)
                    check_time()
                    code = active_proc.wait(timeout=max(0.001, deadline - time.monotonic()))
                    check_time()
                except OSError:
                    (private / (name + '.spawn-failure.txt')).write_text(traceback.format_exc(), encoding='utf-8')
                finally:
                    # SIGALRM/SIGTERM keep deferring through exit capture and log persistence.
                    if active_proc is not None:
                        try:
                            os.killpg(active_proc.pid, signal.SIGKILL)
                        except ProcessLookupError:
                            pass
                        active_proc.wait()
                        record['ExitCode'] = active_proc.returncode
                        record['Completed'] = True
                        code = active_proc.returncode
                    active_proc = None
                    save(private / 'commands.json', commands)
        finally:
            try:
                actual = raw_now()
                check_extension(actual, raw_hashes, pending_raw)
                raw_hashes = actual
                pending_raw = set()
                save(private / 'raw-output-hashes.json', raw_hashes)
            finally:
                active_proc = None
                child_scope = False
        check_time()
        if not allow_failure and code != 0:
            raise GateStop()
        return code

    def blob(path):
        return path.read_bytes() if path.is_file() and not path.is_symlink() else None

    def data_for(kind, name):
        stem = name[len('semantic-'):] if kind == 'semantic' else name
        return {'Stdout': blob(private / (name + '.stdout')), 'Stderr': blob(private / (name + '.stderr')),
            'Snapshot': blob(private / (stem + '.snapshot.bin')),
            'Metadata': blob(private / (stem + '.metadata.json')) if kind == 'semantic' else None,
            'Trace': blob(private / (stem + '.trace.txt')) if kind == 'semantic' else None,
            'Inventory': blob(private / (stem + '.inventory.json')) if kind == 'cold' else None}

    def command_for(name):
        found = next((row for row in commands if row['Name'] == name), None)
        return {key: found[key] if found is not None else default
                for key, default in (('Attempted', False), ('Completed', False), ('ExitCode', None))}

    def add_runtime(value):
        if value not in provenance['ActualRuntimeVersions']:
            provenance['ActualRuntimeVersions'].append(value)

    def log_failure():
        with (private / 'failure.txt').open('a', encoding='utf-8') as stream:
            stream.write(traceback.format_exc())

    try:
        sys.dont_write_bytecode = True
        projector = load_verified('portable_discovery_diagnostics', HERE / 'diagnostics.py', frozen['Files']['diagnostics.py']).project_target
        verify()
        if tuple((HERE / 'upload-allowlist.txt').read_text().splitlines()) != PUBLIC:
            raise IntegrityStop('Public allowlist mismatch')
        if any(digest(config.get(key)) != digest(value) for key, value in EXPECTED_CONFIG.items()):
            raise IntegrityStop('Fixed protocol changed')
        if pins['BaseRevision'] != BASE_REVISION or set(pins['RuntimeFiles']) != set(ORDER):
            raise IntegrityStop('Source identity mismatch')
        for variant in ORDER:
            inventory = pins['RuntimeFiles'][variant]
            if len(inventory) != 44 or any(not name.startswith('Runtime/') or '..' in Path(name).parts or Path(name).is_absolute() or re.fullmatch('[0-9a-f]{64}', value) is None for name, value in inventory.items()):
                raise IntegrityStop('Runtime inventory mismatch')
            provenance['RuntimeSourceManifestSHA256'][variant] = digest(inventory)
        if set(pins['RuntimeFiles']['original']) != set(pins['RuntimeFiles']['candidate']) or [name for name in pins['RuntimeFiles']['original'] if pins['RuntimeFiles']['original'][name] != pins['RuntimeFiles']['candidate'][name]] != ['Runtime/GameplayTagManager.cs']:
            raise IntegrityStop('Unexpected Runtime overlay')
        status['Stage'] = 'source'
        run('archive-baseline', ['git', '-C', args.git_root.resolve(), 'archive', BASE_REVISION, 'Runtime'])
        (private / 'original').mkdir()
        seen = set()
        with tarfile.open(fileobj=io.BytesIO((private / 'archive-baseline.stdout').read_bytes())) as archive:
            for member in archive:
                path = Path(member.name)
                if path.is_absolute() or '..' in path.parts or not path.parts or path.parts[0] != 'Runtime' or member.name in seen:
                    raise IntegrityStop('Unsafe source archive')
                seen.add(member.name)
                dest = private / 'original' / path
                if member.isdir():
                    dest.mkdir(parents=True, exist_ok=True)
                elif member.isfile() and member.name in pins['RuntimeFiles']['original']:
                    dest.parent.mkdir(parents=True, exist_ok=True)
                    dest.write_bytes(archive.extractfile(member).read())
                else:
                    raise IntegrityStop('Unexpected source archive member')
        shutil.copytree(private / 'original', private / 'candidate')
        shutil.copy2(HERE / 'candidate/GameplayTagManager.cs', private / 'candidate/Runtime/GameplayTagManager.cs')
        sources_ready = True
        verify()
        status['Stage'] = 'toolchain'
        support = load_verified('portable_discovery_build_support', HERE / 'build_support.py', frozen['Files']['build_support.py'])
        tool_files = support.input_files(args.backend, dotnet, mono, nuget)
        tool_hashes = {name: sha(path) for name, path in tool_files.items()}
        save(private / 'tool-hashes.json', tool_hashes)
        provenance['ToolchainManifestSHA256'] = digest(tool_hashes)
        provenance['ToolchainInputCount'] = len(tool_hashes)
        _, packages = support.references(args.backend, dotnet, nuget)
        dependency_names = {'shared-dependencies/' + path.name for path in packages}
        begin_artifacts(dependency_names)
        (private / 'shared-dependencies').mkdir()
        for path in packages:
            copied = private / 'shared-dependencies' / path.name
            shutil.copy2(path, copied)
            if sha(copied) != sha(path):
                raise IntegrityStop('Shared dependency copy differs')
        seal_artifacts(dependency_names)
        provenance['SharedDependencyManifestSHA256'] = digest({name: value for name, value in artifact_hashes.items() if name.startswith('shared-dependencies/')})
        status['Stage'] = 'main-build'
        executables, fixtures = {}, {}
        extension = '.exe' if args.backend == 'mono' else '.dll'
        for variant in ORDER:
            prefix = 'build-' + variant + '/'
            names = {prefix + 'Test' + extension, prefix + 'compile.rsp'}
            required = {prefix + 'compile.rsp'}
            if args.backend == 'net':
                names.add(prefix + 'Test.runtimeconfig.json')
                required.add(prefix + 'Test.runtimeconfig.json')
            begin_artifacts(names)
            executables[variant], code = support.build(private / variant, HERE / 'Harness/Entry.cs', private / ('build-' + variant), args.backend, dotnet, mono, nuget, run)
            if code == 0:
                required.add(prefix + 'Test' + extension)
                status['Builds'] += 1
            seal_artifacts(required)
        status['Stage'] = 'fixture-build'
        for count in (1, 32):
            prefix = 'build-fixtures/'
            response = prefix + 'compile-fixture' + str(count) + '.rsp'
            assembly = prefix + 'DiscoveryDeclarations' + str(count) + '.dll'
            begin_artifacts({response, assembly})
            fixtures['n' + str(count)], code = support.build_fixture(HERE / ('Fixtures/Fixtures' + str(count) + '.cs'), count,
                private / 'build-fixtures', private / ('build-original/Test' + extension), args.backend, dotnet, mono, nuget, run)
            required = {response}
            if code == 0:
                required.add(assembly)
                status['Builds'] += 1
            seal_artifacts(required)
        provenance['BuildArtifactManifestSHA256'] = digest(artifact_hashes)
        provenance['SharedFixtureSHA256'] = {shape: artifact_hashes.get('build-fixtures/' + path.name) for shape, path in fixtures.items()}
        save(private / 'shared-fixture-hashes.json', provenance['SharedFixtureSHA256'])
        if status['Builds'] != 4:
            status.update(State='blocked', Stage='fixture-build', Reason='build-exit')
            summary['Decision'] = 'STOP_BUILD_FAILURE'
            raise GateStop()
        verify()  # Both shared DLLs and all copies are pinned before the first semantic target.
        status['Stage'] = 'semantic'
        for mode, variant, name in semantic_plan:
            stem = mode + '-' + variant
            run(name, executables[variant] + ['semantic', mode, private / (stem + '.snapshot.bin'),
                private / (stem + '.metadata.json'), private / (stem + '.trace.txt')], allow_failure=True)
            verify()
        # Review only after all four fresh semantic processes were attempted.
        gate = True
        for mode in MODES:
            data = {variant: data_for('semantic', 'semantic-' + mode + '-' + variant) for variant in ORDER}
            for variant in ORDER:
                cmd = command_for('semantic-' + mode + '-' + variant)
                try:
                    runtime, lines = runtime_lines(data[variant]['Stdout'] or b'', args.backend)
                    add_runtime(runtime)
                    expected = ['PASS ' + mode + ': automatic discovery, exact registry snapshot, API typed arrays and public initialized fast paths',
                        'PASS public caller contexts; no native capture, forced preparation, reset, timing or allocation sample']
                    gate = gate and cmd['Completed'] and cmd['ExitCode'] == 0 and lines == expected and bool(data[variant]['Metadata'])
                except (ValueError, TypeError, KeyError, UnicodeError):
                    gate = False
            for field, output in (('Snapshot', 'SnapshotPairs'), ('Trace', 'TracePairs')):
                equal = bool(data['original'][field]) and data['original'][field] == data['candidate'][field]
                correctness[output].append({'Mode': mode, 'Equal': equal,
                    'SHA256': hashlib.sha256(data['original'][field]).hexdigest() if equal else None})
                gate = gate and equal
        status['Stage'] = 'metadata'
        verify()
        try:
            load_verified('metadata_compare', HERE / 'metadata_compare.py', frozen['Files']['metadata_compare.py'])
            comparator = load_verified('portable_discovery_mechanism', HERE / 'mechanism_compare.py', frozen['Files']['mechanism_compare.py'])
            report = comparator.compare(private, args.backend)
            correctness['MetadataGatePass'] = report['status'] == 'PASS'
            for source, dest in (('types_compared', 'RuntimeTypes'), ('methods_compared', 'RuntimeMethods')):
                value = report[source]
                if type(value) is not int or value < 0:
                    raise ValueError('Comparator count type')
                correctness[dest] = value
            for source, dest in (('metadata_differences', 'UnchangedMetadataDifferences'), ('unrelated_raw_il_failures', 'UnrelatedRawILFailures')):
                if type(report[source]) is not list:
                    raise ValueError('Comparator difference type')
                correctness[dest] = len(report[source])
            correctness['MechanismReportSHA256'] = sha(private / 'mechanism-report.json')
        except (ValueError, TypeError, KeyError, OSError, RuntimeError):
            log_failure()
            correctness['MetadataGatePass'] = False
        correctness['SemanticGatePass'] = bool(gate and correctness['MetadataGatePass'])
        verify()
        if not correctness['SemanticGatePass']:
            status.update(State='blocked', Stage='semantic', Reason='semantics' if gate is False else 'metadata')
            summary['Decision'] = 'STOP_SEMANTIC_OR_METADATA_FAILURE'
            raise GateStop()
        status['Stage'] = 'cold'
        for shape, pair, variant, name in cold_plan:
            main_path = private / ('build-' + variant) / ('Test' + extension)
            fixture_args = ['-', '-'] if shape == 'n0' else [fixtures[shape], artifact_hashes['build-fixtures/' + fixtures[shape].name]]
            run(name, executables[variant] + ['cold', shape, private / (name + '.snapshot.bin'), private / (name + '.inventory.json'),
                main_path, artifact_hashes['build-' + variant + '/Test' + extension]] + fixture_args, allow_failure=True)
            verify()
        # Never parse, reject, retry, or replace a cold result before all 24 slots.
        status['Stage'] = 'analysis'
        for shape, pair, variant, name in cold_plan:
            data = data_for('cold', name)
            try:
                value = parse_cold(data, args.backend, shape, private / ('build-' + variant) / ('Test' + extension), fixtures.get(shape))
                value['Valid'] = value['Valid'] and command_for(name)['Completed'] and command_for(name)['ExitCode'] == 0
                parsed[name] = value
                add_runtime(value['Runtime'])
            except (ValueError, TypeError, KeyError, OSError, UnicodeError, RecursionError):
                parsed[name] = {'Valid': False}
            diagnostic = projector(variant, args.backend, command_for(name), data, 'cold', int(shape[1:]))
            number = diagnostic.get('Measurement')
            parsed[name]['CounterRangeValid'] = diagnostic.get('CounterRangeValid') is True
            if number is not None and all(type(number[key]) is int and -(1 << 63) <= number[key] < (1 << 63)
                    for key in ('Ticks', 'Frequency', 'AllocatedBytes', 'GC0', 'GC1', 'GC2')):
                # Preserve signed anomalies for diagnosis, never qualify them as benefits.
                parsed[name]['Numbers'] = number
        for shape in SHAPES:
            for pair in range(4):
                names = {variant: 'cold-' + shape + '-' + str(pair) + '-' + variant for variant in ORDER}
                rows = {variant: parsed[names[variant]] for variant in ORDER}
                a, b = rows['original'], rows['candidate']
                item = {'Shape': shape, 'Pair': pair, 'Order': list(PAIR_ORDERS[pair]), 'Complete': False, 'SemanticsMatch': False}
                if 'Numbers' in a and 'Numbers' in b:
                    av, bv = a['Numbers'], b['Numbers']
                    item['Complete'] = True
                    item['CounterRangeValidOriginal'] = a['CounterRangeValid']
                    item['CounterRangeValidCandidate'] = b['CounterRangeValid']
                    item['NumericalComparable'] = a['CounterRangeValid'] and b['CounterRangeValid']
                    item['LatencyRatioValid'] = False
                    item.update(AllocatedBytesOriginal=av['AllocatedBytes'], AllocatedBytesCandidate=bv['AllocatedBytes'],
                        AllocatedByteDeltaCandidateMinusOriginal=bv['AllocatedBytes'] - av['AllocatedBytes'],
                        OriginalTicks=av['Ticks'], CandidateTicks=bv['Ticks'], OriginalFrequency=av['Frequency'], CandidateFrequency=bv['Frequency'],
                        OriginalGC=[av[key] for key in ('GC0', 'GC1', 'GC2')], CandidateGC=[bv[key] for key in ('GC0', 'GC1', 'GC2')])
                    if av['Frequency'] > 0 and bv['Frequency'] > 0 and av['Ticks'] > 0 and bv['Ticks'] > 0:
                        old_ns, new_ns = av['Ticks'] * 1e9 / av['Frequency'], bv['Ticks'] * 1e9 / bv['Frequency']
                        item.update(OriginalNs=old_ns, CandidateNs=new_ns, CandidateSlower=new_ns > old_ns,
                                    CandidateOverOriginalLatency=new_ns / old_ns, LatencyRatioValid=all(math.isfinite(value) for value in (old_ns, new_ns, new_ns / old_ns)))
                    snapshots_equal = bool(data_for('cold', names['original'])['Snapshot']) and data_for('cold', names['original'])['Snapshot'] == data_for('cold', names['candidate'])['Snapshot']
                    inventories_equal = 'Inventory' in a and 'Inventory' in b and a['Inventory'] == b['Inventory']
                    measurement_keys = MEASUREMENT_KEYS - {'Ticks', 'Frequency', 'AllocatedBytes', 'GC0', 'GC1', 'GC2'}
                    semantic_keys = SEMANTIC_KEYS - {'InventorySHA256'}
                    item.update(SnapshotsEqual=snapshots_equal, InventoriesEqualAfterEntryOnlyNormalization=inventories_equal,
                        SemanticsMatch=bool(a['Valid'] and b['Valid'] and snapshots_equal and inventories_equal
                            and all(a['Measurement'][key] == b['Measurement'][key] for key in measurement_keys)
                            and all(a['Semantics'][key] == b['Semantics'][key] for key in semantic_keys)))
                summary['Pairs'].append(item)
        summary['AllSemanticPairsPass'] = len(summary['Pairs']) == 12 and all(item['SemanticsMatch'] for item in summary['Pairs'])
        summary['AnyPairedAllocationIncrease'] = any(item.get('AllocatedByteDeltaCandidateMinusOriginal', 0) > 0 for item in summary['Pairs'])
        summary['CandidateSlowerPairCounts'] = {shape: sum(item.get('CandidateSlower', False) for item in summary['Pairs'] if item['Shape'] == shape) for shape in SHAPES}
        verify()
        successful_protocol = True
        status.update(State='complete', Stage='complete')
        summary['Decision'] = 'REQUIRES_REVIEW_NO_AUTOMATIC_CONTINUATION'
        if not summary['AllSemanticPairsPass'] or summary['AnyPairedAllocationIncrease']:
            status.update(State='rejected', Stage='analysis', Reason='semantics')
            summary['Decision'] = 'CLOSE_COLD_SEMANTIC_OR_ALLOCATION_FAILURE'
    except GateStop:
        if status['Reason'] == 'none':
            status['Reason'] = 'source-integrity' if status['Stage'] == 'source' else 'internal'
    except IntegrityStop:
        status.update(State='incomplete', IntegrityFailure=True, Reason='source-integrity' if status['Stage'] == 'source' else 'artifact-integrity')
        status['FailureStage'] = status['Stage']
        log_failure()
    except TimeoutError:
        status.update(State='incomplete', DeadlineExceeded=True, Reason='deadline')
        status['FailureStage'] = status['Stage']
        log_failure()
    except BaseException:
        status['State'] = 'incomplete'
        status['Reason'] = 'internal'
        status['FailureStage'] = status['Stage']
        log_failure()
    finally:
        finalizing = True
        try:
            verify()
        except TimeoutError:
            status.update(State='incomplete', Stage='finalization', DeadlineExceeded=True, Reason='deadline')
            log_failure()
        except BaseException:
            status.update(State='incomplete', Stage='finalization', IntegrityFailure=True, Reason='artifact-integrity')
            log_failure()
        # Explicitly verify projector again. Never import changed code; verified
        # preload still projects each planned slot after a final integrity failure.
        try:
            if projector is None or sha(HERE / 'diagnostics.py') != frozen['Files']['diagnostics.py']:
                raise IntegrityStop('Projector identity changed or unavailable')
        except BaseException:
            status.update(State='incomplete', IntegrityFailure=True, Stage='finalization', Reason='artifact-integrity')
        def safe_project(variant, name, kind, shape):
            command = command_for(name)
            data = {key: None for key in ('Stdout', 'Stderr', 'Snapshot', 'Inventory', 'Metadata', 'Trace')}
            try:
                data = data_for(kind, name)
                if projector is None:
                    raise ValueError('Verified projector unavailable')
                return projector(variant, args.backend, command, data, kind, shape)
            except BaseException:
                status.update(State='incomplete', Stage='finalization', FinalizationFailure=True, Reason='internal')
                correctness['DiagnosticProjectionFailed'] = True
                log_failure()
                return dict(command, Variant=variant, Backend=args.backend, Kind=kind, Shape=shape, ProjectionUnavailable=True,
                    Artifacts={key: {'Present': value is not None, 'Length': len(value) if value is not None else None,
                        'SHA256': hashlib.sha256(value).hexdigest() if value is not None else None} for key, value in data.items()})
        for mode, variant, name in semantic_plan:
            correctness['SemanticDiagnostics'].append(safe_project(variant, name, 'semantic', mode))
        for ordinal, (shape, pair, variant, name) in enumerate(cold_plan):
            row = safe_project(variant, name, 'cold', int(shape[1:]))
            row.update(Ordinal=ordinal, Pair=pair, FullGatePass=bool(parsed.get(name, {}).get('Valid', False)))
            cold.append(row)
        for name in compile_plan:
            row = dict(command_for(name), Name=name)
            for label, suffix in (('Stdout', '.stdout'), ('Stderr', '.stderr')):
                try:
                    value = blob(private / (name + suffix))
                except OSError:
                    value = None
                    status.update(State='incomplete', Stage='finalization', FinalizationFailure=True, Reason='internal')
                row[label] = {'Present': value is not None, 'Length': len(value) if value is not None else None,
                    'SHA256': hashlib.sha256(value).hexdigest() if value is not None else None}
            provenance['BuildDiagnostics'].append(row)
        status['SemanticCompleted'] = sum(command_for(name)['Completed'] for _, _, name in semantic_plan)
        status['ColdCompleted'] = sum(command_for(name)['Completed'] for _, _, _, name in cold_plan)
        status['ColdRows'] = sum(bool(row.get('MeasurementParseValid')) for row in cold)
        sequence = (status['BuildAttempts'], status['Builds'], status['SemanticTargets'], status['ColdTargets'])
        status['FixedSequenceValidated'] = bool(successful_protocol and sequence == (4, 4, 4, 24)
            and len(correctness['SemanticDiagnostics']) == 4 and len(cold) == 24)
        if status['State'] in ('complete', 'rejected') and not status['FixedSequenceValidated']:
            status.update(State='incomplete', Stage='finalization', FinalizationFailure=True, Reason='internal')
        if not summary['Pairs']:
            summary['Pairs'] = [{'Shape': shape, 'Pair': pair, 'Order': list(PAIR_ORDERS[pair]), 'Complete': False, 'SemanticsMatch': False}
                                for shape in SHAPES for pair in range(4)]
        if status['IntegrityFailure'] or status['DeadlineExceeded'] or status['FinalizationFailure']:
            status['State'] = 'incomplete'
            summary['Decision'] = 'INCOMPLETE_NO_CONTINUATION'
        try:
            verify()
        except TimeoutError:
            status.update(State='incomplete', Stage='finalization', DeadlineExceeded=True, Reason='deadline')
            summary['Decision'] = 'INCOMPLETE_NO_CONTINUATION'
        except BaseException:
            status.update(State='incomplete', Stage='finalization', IntegrityFailure=True, Reason='artifact-integrity')
            summary['Decision'] = 'INCOMPLETE_NO_CONTINUATION'
        payloads = {'status.json': status, 'provenance.json': provenance,
            'config.json': dict(EXPECTED_CONFIG, Backend=args.backend, RequestedDeadlineSeconds=args.deadline_seconds),
            'correctness.json': correctness, 'raw/cold.json': cold, 'summary.json': summary}
        try:
            # Serialize every public payload under the still-active fixed deadline.
            encoded = {name: json.dumps(payloads[name], indent=2, allow_nan=False) + '\n' for name in PUBLIC}
            for name in PUBLIC:
                (public / name).write_text(encoded[name], encoding='utf-8')
            if set(tree(public)) != set(PUBLIC):
                raise IntegrityStop('Unexpected public inventory')
        except BaseException:
            status.update(State='incomplete', Stage='finalization', FinalizationFailure=True, Reason='internal')
            summary['Decision'] = 'INCOMPLETE_NO_CONTINUATION'
            log_failure()
        if canceled or time.monotonic() >= deadline:
            status.update(State='incomplete', Stage='finalization', DeadlineExceeded=True, Reason='deadline')
            summary['Decision'] = 'INCOMPLETE_NO_CONTINUATION'
        # Only sticky failure state may be rewritten here, never promoted to complete.
        if status['State'] == 'incomplete':
            save(public / 'summary.json', summary)
            save(public / 'status.json', status)
        signal.setitimer(signal.ITIMER_REAL, 0)
        if canceled or time.monotonic() >= deadline:
            status.update(State='incomplete', Stage='finalization', DeadlineExceeded=True, Reason='deadline')
            summary['Decision'] = 'INCOMPLETE_NO_CONTINUATION'
            save(public / 'summary.json', summary)
            save(public / 'status.json', status)
        finalizing = False
    print('Assembly discovery portability screen: ' + status['State'] + '. Explicit public numeric/hash reports only; AcceptanceEvidence=false.')
    return 0 if status['State'] == 'complete' else 1


if __name__ == '__main__':
    try:
        exit_code = main()
    except SystemExit:
        raise
    except BaseException:
        print('Assembly discovery screen stopped before report completion.', file=sys.stderr)
        exit_code = 1
    raise SystemExit(exit_code)
