#!/usr/bin/env python3
"""Fixed cold-only event-cleanup rejection screen; explicit public JSON allowlist."""
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
import subprocess
import sys
import tarfile
import time
import traceback

HERE = Path(__file__).resolve().parent
SUPPORT = HERE.parent / 'Hierarchical/build_support.py'
REFERENCES = HERE.parent / 'References.csproj'
PUBLIC_FILES = ('status.json', 'provenance.json', 'config.json', 'correctness.json', 'raw/cold.json', 'summary.json')
RUNTIME_FIELDS = {'RuntimeVersion', 'ServerGC', 'TieringConfigured', 'ReadyToRunRequested', 'TieringRequested'}
STATE_SUFFIXES = {'TagCount', 'ExplicitCount', 'EntryCapacity', 'ExplicitCapacity', 'BucketCapacity', 'CallbackCount', 'CallbackCapacity', 'EventCapacity', 'EventCount', 'NextEvent', 'Dispatching', 'PendingReferences'}
STATE_FIELDS = {prefix + suffix for prefix in ('Before', 'After') for suffix in STATE_SUFFIXES}
MEASURE_FIELDS = {'Ticks', 'StopwatchFrequency', 'Ns', 'Bytes', 'Gc0', 'Gc1', 'Gc2'}
SEMANTIC_FIELDS = {'Depth', 'Callbacks', 'ExpectedEvents', 'ExpectedChecksum', 'Events', 'Checksum', 'OperationThrew', 'HierarchyCountsCorrect', 'SemanticOk'} | STATE_FIELDS


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
    parser.add_argument('--preflight-only', action='store_true', help='Build; correctness, twelve fixtures and four untimed first-operation schemas only. No timing.')
    args = parser.parse_args()
    if not 0 < args.deadline_seconds <= 600: parser.error('Deadline must be 1-600 seconds; no extension.')
    deadline = entered + args.deadline_seconds
    def deadline_signal(signum, frame): raise TimeoutError('fixed overall deadline or termination')
    signal.signal(signal.SIGALRM, deadline_signal); signal.signal(signal.SIGTERM, deadline_signal)
    signal.setitimer(signal.ITIMER_REAL, max(0.001, deadline - time.monotonic()))
    public, private = args.out.resolve(), args.work.resolve()
    if public == private or public in private.parents or private in public.parents:
        parser.error('--out and --work must be separate, new, non-nested directories')
    public.mkdir(parents=True, exist_ok=False); (public / 'raw').mkdir()
    private.mkdir(parents=True, exist_ok=False)
    dotnet, mono, nuget = resolve(args.dotnet), resolve(args.mono), args.nuget.resolve()
    pins = json.loads((HERE / 'source-pins.json').read_text())
    frozen = json.loads((HERE / 'frozen.json').read_text()); frozen_hash = sha(HERE / 'frozen.json')
    config = json.loads((HERE / 'config.json').read_text())
    row_ids = [row['id'] for row in config['Rows']]; cold_ids = config['ColdRows']
    commands, correctness, fixtures, cold_preflights, cold = [], [], [], [], []
    provenance, tool_files, tool_hashes, artifact_hashes, source_trees = {}, {}, {}, {}, {}
    status, stage = 'incomplete', 'input-verification'
    child_env = dict(os.environ)
    for prefix in ('DOTNET_', 'COMPlus_'):
        child_env[prefix + 'ReadyToRun'] = '1'; child_env[prefix + 'TieredCompilation'] = '0'; child_env[prefix + 'gcServer'] = '0'
    child_env['DOTNET_ROLL_FORWARD'] = 'Disable'

    def checked(name, command):
        remaining = deadline - time.monotonic()
        if remaining <= 0: raise TimeoutError('fixed overall deadline')
        entry = {'Name': name, 'Command': list(map(str, command))}; commands.append(entry)
        started = time.monotonic()
        with (private / (name + '.stdout')).open('wb') as a, (private / (name + '.stderr')).open('wb') as b:
            process = subprocess.Popen(entry['Command'], stdout=a, stderr=b, start_new_session=True, env=child_env)
            try: entry['ExitCode'] = process.wait(timeout=remaining)
            except subprocess.TimeoutExpired:
                entry['ExitCode'] = None; entry['TimedOut'] = True
            except BaseException:
                entry['Interrupted'] = True; raise
            finally:
                try: os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError: pass
                process.wait()
                entry['ElapsedSeconds'] = time.monotonic() - started; save(private / 'commands.json', commands)
        return (private / (name + '.stdout')).read_bytes(), entry.get('ExitCode')

    def require(name, command):
        output, code = checked(name, command)
        if code != 0: raise RuntimeError('command failed at ' + name)
        return output

    def verify_inputs():
        if sha(HERE / 'frozen.json') != frozen_hash: raise RuntimeError('frozen manifest changed')
        if {name: sha(HERE / name) for name in frozen['Files']} != frozen['Files']: raise RuntimeError('prepared inputs changed')
        if sha(SUPPORT) != frozen['BuildSupportSHA256'] or sha(REFERENCES) != frozen['ReferencesSHA256']: raise RuntimeError('shared inputs changed')
        if sha(HERE / 'candidate.patch') != pins['CandidatePatchSHA256']: raise RuntimeError('patch mismatch')
        if row_ids != pins['Rows'] or len(row_ids) != 12 or cold_ids != pins['ColdRows'] or len(cold_ids) != 4: raise RuntimeError('fixed rows changed')
        if config['ColdRoundOrder'] != [['before','after'],['after','before'],['before','after'],['after','before']] or config['ColdProcesses'] != 32:
            raise RuntimeError('fixed cold plan changed')

    def verify_execution():
        if time.monotonic() >= deadline: raise TimeoutError('fixed overall deadline')
        verify_inputs()
        if {key: sha(path) for key, path in tool_files.items()} != tool_hashes: raise RuntimeError('tool inputs changed')
        if {name: tree(private / ('build-' + name)) for name in artifact_hashes} != artifact_hashes: raise RuntimeError('compiled outputs changed')
        if {name: tree(private / name) for name in source_trees} != source_trees: raise RuntimeError('materialized sources changed')
        if time.monotonic() >= deadline: raise TimeoutError('fixed overall deadline')

    def validate_runtime(row):
        if not isinstance(row['RuntimeVersion'], str) or not re.fullmatch(r'[0-9]+(?:\.[0-9]+){2,3}', row['RuntimeVersion']): raise ValueError('invalid runtime report')
        if row['ServerGC'] != 0: raise ValueError('server GC enabled')
        if args.backend == 'net':
            if row['RuntimeVersion'] != config['CLRVersion'] or row['TieringConfigured'] != 0 or row['ReadyToRunRequested'] != 1 or row['TieringRequested'] != 0:
                raise ValueError('unexpected .NET runtime configuration')
        elif row['ReadyToRunRequested'] != -1 or row['TieringRequested'] != -1 or row['TieringConfigured'] not in (-1, 0):
            raise ValueError('unexpected Mono runtime configuration')

    def parse_row(row, mode, expected_ids, collected):
        if mode == 'preflight': allowed = {'Mode','Id','Checks','Depth','Callbacks','Leaves','EventsPerPair','ChecksumPerPair','Gaps'}
        else: allowed = {'Mode','Id'} | RUNTIME_FIELDS | MEASURE_FIELDS | SEMANTIC_FIELDS
        if set(row) != allowed or row['Mode'] != mode or row['Id'] not in expected_ids: raise ValueError('unexpected row schema')
        for key, value in row.items():
            if key in ('Mode', 'Id', 'RuntimeVersion', 'Gaps'): continue
            if type(value) not in (int, float) or not math.isfinite(value): raise ValueError('invalid number')
        if mode != 'preflight' and (not isinstance(row['RuntimeVersion'], str) or not re.fullmatch(r'[0-9]+(?:\.[0-9]+){2,3}', row['RuntimeVersion'])):
            raise ValueError('invalid runtime string')
        collected.append(row)  # Preserve schema-valid numerical results even if semantic/runtime checks fail.
        if mode == 'preflight':
            fixed = config['Rows'][row_ids.index(row['Id'])]
            if row['Depth'] != fixed['depth'] or row['Callbacks'] != fixed['callback_entries'] or row['Checks'] != 78:
                raise ValueError('fixture geometry differs')
            if row['Gaps'] != ([2048,2048,2048,2048] if row['Id'] == 'huge-gaps/d4' else []): raise ValueError('gap fixture differs')
            return
        validate_runtime(row)
        for key, value in row.items():
            if key not in ('Mode','Id','RuntimeVersion','TieringConfigured','ReadyToRunRequested','TieringRequested') and value < 0:
                raise ValueError('negative measurement/state')
        fixed = next(item for item in config['ColdExpectations'] if item['Id'] == row['Id'])
        for key, value in fixed.items():
            if row[key] != value: raise ValueError('cold state differs: ' + key)
        if row['Events'] != row['ExpectedEvents'] or row['Checksum'] != row['ExpectedChecksum']: raise ValueError('cold callback output differs')
        if mode == 'cold':
            if row['Ticks'] <= 0 or row['StopwatchFrequency'] <= 0 or row['Ns'] <= 0: raise ValueError('invalid clock sample')
        elif any(row[key] != 0 for key in MEASURE_FIELDS): raise ValueError('untimed preflight contains measurement')

    def target(name, command, mode, variant, round_number=None, fixture_id=None):
        verify_execution()
        item = {'Variant':variant, 'Complete':False, 'Rows':[], 'ExitCode':None}
        if round_number is not None: item['Round'] = round_number
        if fixture_id is not None: item['Fixture'] = fixture_id
        {'preflight':fixtures, 'cold-preflight':cold_preflights, 'cold':cold}[mode].append(item)
        output, code = checked(name, command); item['ExitCode'] = code
        expected = row_ids if mode == 'preflight' else [fixture_id]
        valid = True
        for line in output.decode(errors='replace').splitlines():
            try: parse_row(json.loads(line), mode, expected, item['Rows'])
            except (ValueError, TypeError, KeyError): valid = False
        item['Complete'] = valid and code == 0 and [row['Id'] for row in item['Rows']] == expected
        verify_execution()
        if not item['Complete']: raise TargetFailure('target failed or schema/rows invalid')
        return item['Rows']

    def summarize():
        result = []
        for name in cold_ids:
            left = [item['Rows'][0] for item in cold if item['Complete'] and item['Fixture'] == name and item['Variant'] == 'before']
            right = [item['Rows'][0] for item in cold if item['Complete'] and item['Fixture'] == name and item['Variant'] == 'after']
            row = {'Id':name, 'BeforeSamples':len(left), 'AfterSamples':len(right), 'Complete':len(left) == len(right) == 4}
            if row['Complete']:
                row.update(SemanticsAgree=all(all(a[key] == b[key] for key in SEMANTIC_FIELDS) for a,b in zip(left,right)),
                    BeforeBytes=[a['Bytes'] for a in left], AfterBytes=[b['Bytes'] for b in right],
                    CandidateExtraAllocationPairs=sum(b['Bytes'] > a['Bytes'] for a,b in zip(left,right)),
                    CandidateOverBaselineRatios=[b['Ns'] / a['Ns'] for a,b in zip(left,right)],
                    AfterSlowerPairs=sum(b['Ns'] > a['Ns'] for a,b in zip(left,right)),
                    LatencyReviewRequired=any(b['Ns'] > a['Ns'] for a,b in zip(left,right)),
                    ObservedGC=any(item['Gc0'] + item['Gc1'] + item['Gc2'] > 0 for item in left + right))
            result.append(row)
        return result

    try:
        verify_inputs(); stage = 'source-materialization'
        archive = require('archive-baseline', ['git','-C',args.git_root.resolve(),'archive',pins['BaselineCommit'],'Runtime'])
        before = private / 'before'; before.mkdir()
        with tarfile.open(fileobj=io.BytesIO(archive)) as tar:
            for member in tar.getmembers():
                path = Path(member.name)
                if path.is_absolute() or '..' in path.parts or path.parts[0] != 'Runtime': raise RuntimeError('unexpected archive member')
                if member.isdir(): (before / path).mkdir(parents=True, exist_ok=True)
                elif member.isfile():
                    dest = before / path; dest.parent.mkdir(parents=True, exist_ok=True); dest.write_bytes(tar.extractfile(member).read())
                else: raise RuntimeError('nonregular archive member')
        shutil.copytree(before, private / 'after')
        shutil.copy2(HERE / 'candidate/GameplayTagCountContainer.cs', private / 'after/Runtime/GameplayTagCountContainer.cs')
        for variant in ('before','after'):
            source_trees[variant] = tree(private / variant)
            if source_trees[variant] != pins['SourceHashes'][variant]: raise RuntimeError('source identities mismatch')
        stage = 'toolchain-and-build'
        spec = importlib.util.spec_from_file_location('event_cleanup_existing_build_support', SUPPORT)
        sys.dont_write_bytecode = True
        support = importlib.util.module_from_spec(spec); spec.loader.exec_module(support)
        sdk, framework, refpack = support.toolchain(dotnet)
        compiler_text = require('compiler-version', [dotnet,sdk / 'Roslyn/bincore/csc.dll','-version']).decode().strip()
        compiler_version = re.match(r'[0-9][0-9A-Za-z.+-]*', compiler_text).group(0)
        runtime_version = framework.name
        if args.backend == 'mono':
            raw = require('mono-version', [mono,'--version']).decode()
            runtime_version = re.search(r'Mono JIT compiler version ([0-9.]+)', raw).group(1)
            if runtime_version != config['ExpectedCIMonoVersion']: raise RuntimeError('unexpected pinned Mono version')
        tool_files = support.input_files(args.backend, dotnet, mono, nuget)
        tool_hashes = {key:sha(path) for key,path in tool_files.items()}
        binaries = {}
        for kind in ('screen','correctness'):
            for variant in ('before','after'):
                label = kind + '-' + variant
                binaries[label] = support.build(private / variant, HERE / 'Harness' / ('Screen.cs' if kind == 'screen' else 'Correctness.cs'),
                    private / ('build-' + label), args.backend, dotnet, mono, nuget,
                    lambda name,command,kind=kind: require(kind + '-' + name,command))
        artifact_hashes = {label:tree(private / ('build-' + label)) for label in binaries}
        provenance = {'Backend':args.backend, 'SDKVersion':sdk.name, 'CompilerVersion':compiler_version,
            'RuntimeVersion':runtime_version, 'ReferencePackVersion':'net472/1.0.3' if args.backend == 'mono' else refpack.name,
            'RuntimeSettings':config['RuntimeSettings'], 'BaselineCommit':pins['BaselineCommit'], 'CandidatePatchSHA256':pins['CandidatePatchSHA256'],
            'SourceHashes':pins['SourceHashes'], 'PreparedFileHashes':{'Tests~/PortfolioScreen/EventCleanup/' + key:value for key,value in frozen['Files'].items()},
            'SharedBuildSupportSHA256':frozen['BuildSupportSHA256'], 'SharedReferencesSHA256':frozen['ReferencesSHA256'],
            'ToolInputHashes':tool_hashes, 'CompiledOutputHashes':{label:{key:value for key,value in values.items() if key != 'compile.rsp'} for label,values in artifact_hashes.items()}}
        save(private / 'execution-identities.json', {'ToolInputHashes':tool_hashes,'ArtifactHashes':artifact_hashes,'SourceHashes':source_trees})
        stage = 'correctness'
        outputs = []
        for variant in ('before','after'):
            verify_execution(); output = require('correctness-' + variant,binaries['correctness-' + variant]).decode(); verify_execution()
            match = re.search(r'([0-9,]+) assertions; (\d+) failed groups;', output)
            if not match or int(match[1].replace(',','')) != config['CorrectnessAssertions'] or int(match[2]) != 0: raise RuntimeError('correctness checks failed')
            outputs.append(output); correctness.append({'Variant':variant,'Checks':config['CorrectnessAssertions'],'FailedGroups':0,'OutputSHA256':hashlib.sha256(output.encode()).hexdigest()})
        if outputs[0] != outputs[1]: raise RuntimeError('correctness outputs differ')
        stage = 'fixture-preflight'
        a = target('preflight-before',binaries['screen-before'] + ['preflight'],'preflight','before')
        b = target('preflight-after',binaries['screen-after'] + ['preflight'],'preflight','after')
        if a != b: raise RuntimeError('twelve fixture preflights differ')
        stage = 'cold-schema-preflight'
        for index,name in enumerate(cold_ids):
            rows = []
            for variant in ('before','after'):
                rows.append(target('cold-schema-' + str(index) + '-' + variant,binaries['screen-' + variant] + ['cold-preflight',name],'cold-preflight',variant,fixture_id=name))
            if rows[0] != rows[1]: raise RuntimeError('cold schema preflight differs')
        if not args.preflight_only:
            stage = 'first-add-cold-only'
            for index,name in enumerate(cold_ids):
                for round_number,order in enumerate(config['ColdRoundOrder']):
                    for variant in order:
                        try:
                            target('cold-' + str(index) + '-' + str(round_number) + '-' + variant,binaries['screen-' + variant] + ['cold',name],'cold',variant,round_number,name)
                        except TargetFailure: pass  # Retain failure and finish declared cold sequence; never substitute a sample.
            if len(cold) != config['ColdProcesses'] or not all(item['Complete'] for item in cold):
                stage = 'cold-incomplete'; raise RuntimeError('declared cold sequence incomplete')
        verify_execution(); status,stage = 'completed','completed'
    except BaseException:
        (private / 'failure.txt').write_text(traceback.format_exc())
    finally:
        try: verify_execution()
        except BaseException:
            if status == 'completed': stage = 'final-integrity'
            status = 'incomplete'
            with (private / 'failure.txt').open('a') as error: error.write(traceback.format_exc())
        if time.monotonic() >= deadline: status,stage = 'incomplete','deadline'
        payloads = {
            'status.json':{'Status':status,'Stage':stage,'Mode':'preflight-only' if args.preflight_only else 'cold-only-rejection-screen','AcceptanceEvidence':False},
            'provenance.json':provenance,
            'config.json':dict(config,Backend=args.backend,DeadlineSeconds=args.deadline_seconds,PreflightOnly=args.preflight_only),
            'correctness.json':{'Suites':correctness,'Fixtures':fixtures,'ColdSchemaPreflights':cold_preflights},
            'raw/cold.json':cold,
            'summary.json':{'Status':status,'AcceptanceEvidence':False,'Backend':args.backend,'CompletedColdProcesses':sum(item['Complete'] for item in cold),
                'ColdLatencyAcceptanceEstablished':False,'WarmTimingExecuted':False,'ColdComparisons':summarize()}}
        if set(payloads) != set(PUBLIC_FILES): raise RuntimeError('public allowlist mismatch')
        for name in PUBLIC_FILES: save(public / name,payloads[name])
        if set(str(path.relative_to(public)) for path in public.rglob('*') if path.is_file()) != set(PUBLIC_FILES): raise RuntimeError('unexpected public file')
        try: verify_execution()
        except BaseException:
            if status == 'completed': stage = 'final-integrity'
            status = 'incomplete'
            with (private / 'failure.txt').open('a') as error: error.write(traceback.format_exc())
        if time.monotonic() >= deadline: status,stage = 'incomplete','deadline'
        if payloads['status.json']['Status'] != status or payloads['status.json']['Stage'] != stage:
            payloads['status.json'].update(Status=status,Stage=stage); payloads['summary.json']['Status'] = status
            save(public / 'status.json',payloads['status.json']); save(public / 'summary.json',payloads['summary.json'])
    signal.setitimer(signal.ITIMER_REAL,0)
    print('Event cleanup cold screen: ' + status + '. Explicit public JSON allowlist only; AcceptanceEvidence=false.')
    return 0 if status == 'completed' else 1

if __name__ == '__main__': raise SystemExit(main())
