#!/usr/bin/env python3
"""Compact dense Clear rejection screen. Only the explicit JSON allowlist is public."""
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
PUBLIC_FILES = ('status.json', 'provenance.json', 'config.json', 'correctness.json',
                'raw/pilot.json', 'iterations.json', 'raw/timing.json', 'summary.json')

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def tree(root): return {str(p.relative_to(root)): sha(p) for p in sorted(root.rglob('*')) if p.is_file()}
def save(path, value): path.write_text(json.dumps(value, indent=2, allow_nan=False) + '\n')
def resolve(value): return Path(shutil.which(value) or value).resolve()


def main():
    entered = time.monotonic()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--git-root', type=Path, required=True)
    parser.add_argument('--backend', choices=('net', 'mono'), required=True)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--mono', default='mono')
    parser.add_argument('--nuget', type=Path, default=Path(os.environ.get('NUGET_PACKAGES', Path.home() / '.nuget/packages')))
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--work', type=Path, required=True)
    parser.add_argument('--deadline-seconds', type=int, default=600)
    parser.add_argument('--preflight-only', action='store_true', help='Compile and run correctness/fixtures; no pilot or timing')
    args = parser.parse_args()
    support_path = args.git_root.resolve() / 'Tests~/PortfolioScreen/Hierarchical/build_support.py'
    if not 0 < args.deadline_seconds <= 600: parser.error('Deadline must be 1-600 seconds; no extension.')
    deadline = entered + args.deadline_seconds
    public, private = args.out.resolve(), args.work.resolve()
    if public == private or public in private.parents or private in public.parents:
        parser.error('--out and --work must be separate, new, non-nested directories')
    public.mkdir(parents=True, exist_ok=False); (public / 'raw').mkdir()
    private.mkdir(parents=True, exist_ok=False)
    dotnet, mono, nuget = resolve(args.dotnet), resolve(args.mono), args.nuget.resolve()
    pins = json.loads((HERE / 'source-pins.json').read_text())
    frozen = json.loads((HERE / 'frozen.json').read_text())
    config = json.loads((HERE / 'config.json').read_text())
    row_ids = [r['id'] for r in config['Rows']]
    seeds = {r['id']: r['pilot_iterations'] for r in config['Rows']}
    iteration_hash = None
    commands, correctness, fixtures, pilot, timing, comparisons = [], [], [], [], [], []
    iterations, provenance, tools_hashes, artifact_hashes, tools_files = {}, {}, {}, {}, {}
    status, stage = 'incomplete', 'input-verification'

    def checked(name, command):
        remaining = deadline - time.monotonic()
        if remaining <= 0: raise TimeoutError('fixed overall deadline')
        entry = {'Name': name, 'Command': list(map(str, command))}; commands.append(entry)
        stdout, stderr = private / (name + '.stdout'), private / (name + '.stderr')
        started = time.monotonic()
        with stdout.open('wb') as a, stderr.open('wb') as b:
            process = subprocess.Popen(entry['Command'], stdout=a, stderr=b, start_new_session=True)
            def cleanup():
                try: os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError: pass
                process.wait()
            try:
                entry['ExitCode'] = process.wait(timeout=remaining)
                if entry['ExitCode'] != 0: cleanup()
            except subprocess.TimeoutExpired:
                cleanup(); entry['ExitCode'] = None; entry['TimedOut'] = True
            except BaseException:
                cleanup(); entry['ExitCode'] = process.returncode; entry['Interrupted'] = True
                save(private / 'commands.json', commands)
                raise
        entry['ElapsedSeconds'] = time.monotonic() - started
        save(private / 'commands.json', commands)
        return stdout.read_bytes(), entry['ExitCode']

    def require(name, command):
        output, code = checked(name, command)
        if code != 0: raise RuntimeError('command failed at ' + name)
        return output

    def verify_inputs():
        for name, expected in frozen['Files'].items():
            if sha(HERE / name) != expected: raise RuntimeError('prepared source changed')
        if sha(support_path) != frozen['BuildSupportSHA256']: raise RuntimeError('build support changed')
        if sha(HERE / 'candidate.patch') != pins['CandidatePatchSHA256']: raise RuntimeError('patch mismatch')
        if len(row_ids) != 12 or row_ids != pins['Rows']: raise RuntimeError('fixed rows changed')

    def verify_iteration_map():
        if iteration_hash is not None and (not (private / 'iterations.tsv').is_file() or sha(private / 'iterations.tsv') != iteration_hash):
            raise RuntimeError('frozen iteration map changed')

    def verify_execution():
        if time.monotonic() >= deadline: raise TimeoutError('fixed overall deadline')
        verify_iteration_map()
        if {key: sha(path) for key, path in tools_files.items()} != tools_hashes: raise RuntimeError('tool inputs changed')
        if {name: tree(private / ('build-' + name)) for name in artifact_hashes} != artifact_hashes: raise RuntimeError('built artifact changed')
        if time.monotonic() >= deadline: raise TimeoutError('fixed overall deadline')

    def target(name, command, mode, variant, round_number=None):
        verify_execution()
        output, code = checked(name, command); values = []
        item = {'Variant': variant, 'Complete': False, 'Rows': values}
        if round_number is not None: item['Round'] = round_number
        {'preflight': fixtures, 'pilot': pilot, 'timing': timing}[mode].append(item)
        geometry = {'Keep','InitialMask','WarmedMask','FinalMask','EntriesCapacity','ExplicitCapacity','BucketsCapacity'}
        allowed = ({'Mode','Id','Checks','ChecksumPerCycle'} | geometry) if mode == 'preflight' else (
            {'Mode','Id','Iterations','BatchMs','Ns','Bytes','Checksum','Gc0','Gc1','Gc2'} | geometry)
        for line in output.decode().splitlines():
            row = json.loads(line)
            if set(row) != allowed or row['Mode'] != mode or row['Id'] not in row_ids: raise RuntimeError('unexpected row schema')
            for key, value in row.items():
                if key not in ('Mode', 'Id') and (type(value) not in (int,float) or not math.isfinite(value)):
                    raise RuntimeError('invalid numeric result')
            values.append(row)
            fixed = config['Rows'][row_ids.index(row['Id'])]
            expected_geometry = {'Keep':fixed['keep'], 'InitialMask':fixed['active_mask'],
                'WarmedMask':fixed['active_mask'], 'FinalMask':fixed['active_mask'],
                'EntriesCapacity':fixed['entries_capacity'], 'ExplicitCapacity':fixed['explicit_capacity'],
                'BucketsCapacity':fixed['bucket_capacity']}
            if any(row[key] != value for key,value in expected_geometry.items()):
                raise RuntimeError('actual fixture geometry differs from frozen config')
            if mode == 'preflight':
                if row['ChecksumPerCycle'] != fixed['keep'] * 2 + 1: raise RuntimeError('fixture checksum differs')
            else:
                expected_count = seeds[row['Id']] if mode == 'pilot' else iterations[row['Id']]
                if row['Iterations'] != expected_count or row['BatchMs'] <= 0 or row['Ns'] <= 0: raise RuntimeError('invalid measured count/time')
                if row['Checksum'] != expected_count * (fixed['keep'] * 2 + 1): raise RuntimeError('measured checksum differs')
                if row['Bytes'] != 0: raise RuntimeError('nonzero warm allocation')
        if code != 0 or [r['Id'] for r in values] != row_ids: raise RuntimeError('target failed or rows missing')
        item['Complete'] = True
        verify_execution()
        return values

    try:
        verify_inputs()
        stage = 'source-materialization'
        archive = require('archive-baseline', ['git', '-C', args.git_root.resolve(), 'archive', pins['BaselineCommit'], 'Runtime'])
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
        shutil.copy2(HERE / 'candidate/TagStorage.cs', after / 'Runtime/TagStorage.cs')
        for variant in ('before','after'):
            if tree(private / variant) != pins['SourceHashes'][variant]: raise RuntimeError('runtime source mismatch')
        stage = 'toolchain-and-build'
        spec = importlib.util.spec_from_file_location('dense_clear_existing_build_support', support_path)
        sys.dont_write_bytecode = True
        support = importlib.util.module_from_spec(spec); spec.loader.exec_module(support)
        sdk, framework, refpack = support.toolchain(dotnet)
        compiler_text = require('compiler-version', [dotnet, sdk / 'Roslyn/bincore/csc.dll', '-version']).decode().strip()
        compiler_version = re.match(r'[0-9][0-9A-Za-z.+-]*', compiler_text).group(0)
        runtime_version = framework.name
        if args.backend == 'mono':
            raw = require('mono-version', [mono, '--version']).decode()
            runtime_version = re.search(r'Mono JIT compiler version ([0-9.]+)', raw).group(1)
            if not args.preflight_only and runtime_version != config['ExpectedCIMonoVersion']: raise RuntimeError('unexpected CI Mono version')
        provenance = {'Backend': args.backend, 'SDKVersion': sdk.name, 'CompilerVersion': compiler_version,
            'RuntimeVersion': runtime_version, 'ReferencePackVersion': 'net472/1.0.3' if args.backend == 'mono' else refpack.name,
            'BaselineCommit': pins['BaselineCommit'], 'CandidatePatchSHA256': pins['CandidatePatchSHA256'],
            'SourceHashes': pins['SourceHashes'],
            'PreparedFileHashes': {'Tests~/PortfolioScreen/DenseClear/' + name: value for name,value in frozen['Files'].items()},
            'SharedBuildSupport': {'Tests~/PortfolioScreen/Hierarchical/build_support.py': frozen['BuildSupportSHA256']}}
        tools_files = support.input_files(args.backend, dotnet, mono, nuget)
        tools_hashes = {key: sha(path) for key,path in tools_files.items()}
        binaries = {}
        for kind, program in (('screen','Screen.cs'),('correctness','Correctness.cs')):
            for variant in ('before','after'):
                label = kind + '-' + variant
                binaries[label] = support.build(private / variant, HERE / 'Harness' / program,
                    private / ('build-' + label), args.backend, dotnet, mono, nuget,
                    lambda name, command, kind=kind: require(kind + '-' + name, command))
        artifact_hashes = {label: tree(private / ('build-' + label)) for label in binaries}
        save(private / 'execution-identities.json', {'ToolInputHashes': tools_hashes, 'ArtifactHashes': artifact_hashes})
        stage = 'threshold-correctness'
        semantic_rows = []
        expected_shapes = [(shape,count) for count in (8,9,15,16,17,31,32,33,63,64,65)
                           for shape in ('fresh','retained-sparse')] + [('density-boundary',n) for n in (127,128,129)]
        for variant in ('before','after'):
            verify_execution()
            result_file = private / ('threshold-' + variant + '.json')
            require('correctness-' + variant, binaries['correctness-' + variant] + ['--out',result_file])
            result = json.loads(result_file.read_text())
            if set(result) != {'Checks','Rows'} or result['Checks'] != 'PASS' or len(result['Rows']) != 25:
                raise RuntimeError('threshold correctness failed')
            expected_keys = {'Shape','LiveCount','ActiveMask','EntriesCapacity','ExplicitCapacity','BucketsCapacity','Min64PredicateWouldUseBulk'}
            for row,(shape,count) in zip(result['Rows'],expected_shapes):
                if set(row) != expected_keys or row['Shape'] != shape or row['LiveCount'] != count:
                    raise RuntimeError('unexpected threshold case or schema')
                if type(row['Min64PredicateWouldUseBulk']) != bool: raise RuntimeError('invalid threshold dispatch flag')
                for key in ('LiveCount','ActiveMask','EntriesCapacity','ExplicitCapacity','BucketsCapacity'):
                    if type(row[key]) != int or row[key] < 0: raise RuntimeError('invalid threshold geometry')
            semantic_rows.append(result['Rows'])
            correctness.append({'Variant':variant,'ThresholdCases':25,'Checks':'PASS','Rows':result['Rows']})
            verify_execution()
        if semantic_rows[0] != semantic_rows[1]: raise RuntimeError('threshold results differ')
        stage = 'fixture-preflight'
        a = target('preflight-before', binaries['screen-before'] + ['preflight'], 'preflight', 'before')
        b = target('preflight-after', binaries['screen-after'] + ['preflight'], 'preflight', 'after')
        if a != b: raise RuntimeError('fixture preflight differs')
        if not args.preflight_only:
            stage = 'original-only-pilot'
            raw = target('pilot-before', binaries['screen-before'] + ['pilot'], 'pilot', 'before')
            for row in raw:
                value = max(seeds[row['Id']], math.ceil(seeds[row['Id']] * config['TargetBatchMs'] / row['BatchMs']))
                if value > 2147483647: raise RuntimeError('iteration map exceeds supported bound')
                iterations[row['Id']] = value
            iteration_file = private / 'iterations.tsv'
            iteration_file.write_text(''.join(name + '\t' + str(iterations[name]) + '\n' for name in row_ids))
            iteration_hash = sha(iteration_file)
            save(public / 'raw/pilot.json', pilot)
            save(public / 'iterations.json', {'Backend':args.backend,'Iterations':iterations,'MapSHA256':iteration_hash,'FrozenBeforeCandidateTiming':True})
            stage = 'fixed-timing'
            for round_number, order in enumerate(config['RoundOrder']):
                for variant in order:
                    if sha(iteration_file) != iteration_hash: raise RuntimeError('frozen iteration map changed')
                    target('timing-' + variant + '-' + str(round_number), binaries['screen-' + variant] +
                        ['timing','--iterations-file',iteration_file], 'timing',variant,round_number)
            verify_iteration_map()
            for index,name in enumerate(row_ids):
                left = [item['Rows'][index] for item in timing if item['Variant']=='before']
                right = [item['Rows'][index] for item in timing if item['Variant']=='after']
                if any(x['Checksum'] != y['Checksum'] for x,y in zip(left,right)): raise RuntimeError('timing semantics differ')
                ratios = [x['Ns']/y['Ns'] for x,y in zip(left,right)]
                ratio = math.exp(statistics.mean(map(math.log,ratios)))
                comparisons.append({'Id':name,'BeforeMedianNs':statistics.median(x['Ns'] for x in left),
                    'AfterMedianNs':statistics.median(x['Ns'] for x in right),'PairedRatios':ratios,'GeometricSpeedup':ratio,
                    'ShortestBatchMs':min(x['BatchMs'] for x in left+right),'ShortBatch':any(x['BatchMs']<200 for x in left+right),
                    'ScreenRegressionSignal':ratio<0.97,'AfterSlowerPairs':sum(y['Ns']>x['Ns'] for x,y in zip(left,right))})
        verify_inputs(); verify_execution()
        for variant in ('before','after'):
            if tree(private / variant) != pins['SourceHashes'][variant]: raise RuntimeError('runtime source changed')
        status, stage = 'completed', 'completed'
    except BaseException:
        (private / 'failure.txt').write_text(traceback.format_exc())
    finally:
        try:
            verify_iteration_map()
        except BaseException:
            status, stage = 'incomplete', 'iteration-integrity'
            with (private / 'failure.txt').open('a') as failure: failure.write(traceback.format_exc())
        if time.monotonic() >= deadline: status, stage = 'incomplete', 'deadline'
        payloads = {
            'status.json':{'Status':status,'Stage':stage,'Mode':'preflight-only' if args.preflight_only else 'rejection-screen','AcceptanceEvidence':False},
            'provenance.json':provenance,
            'config.json':dict(config,Backend=args.backend,DeadlineSeconds=args.deadline_seconds,PreflightOnly=args.preflight_only),
            'correctness.json':{'Suite':correctness,'Fixtures':fixtures},
            'raw/pilot.json':pilot,
            'iterations.json':{'Backend':args.backend,'Iterations':iterations,'MapSHA256':iteration_hash,'FrozenBeforeCandidateTiming':iteration_hash is not None},
            'raw/timing.json':timing,
            'summary.json':{'Status':status,'AcceptanceEvidence':False,'Backend':args.backend,'CompletedTimingProcesses':sum(item['Complete'] for item in timing),'Comparisons':comparisons}}
        if set(payloads) != set(PUBLIC_FILES): raise RuntimeError('public allowlist mismatch')
        for name in PUBLIC_FILES: save(public/name,payloads[name])
        if set(str(p.relative_to(public)) for p in public.rglob('*') if p.is_file()) != set(PUBLIC_FILES): raise RuntimeError('unexpected public file')
        try:
            verify_iteration_map()
        except BaseException:
            status, stage = 'incomplete', 'iteration-integrity'
            with (private / 'failure.txt').open('a') as failure: failure.write(traceback.format_exc())
        if time.monotonic() >= deadline: status, stage = 'incomplete', 'deadline'
        if payloads['status.json']['Status'] != status or payloads['status.json']['Stage'] != stage:
            payloads['status.json'].update(Status=status,Stage=stage);payloads['summary.json']['Status']=status
            save(public/'status.json',payloads['status.json']);save(public/'summary.json',payloads['summary.json'])
    print('Dense Clear screen: ' + status + '. Explicit public JSON allowlist only; no acceptance claim.')
    return 0 if status == 'completed' else 1

if __name__ == '__main__': raise SystemExit(main())
