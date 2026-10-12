#!/usr/bin/env python3
"""Bounded isolated Clear and first-use rejection probe. Only the explicit JSON allowlist is public."""
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
                'raw/pilot.json', 'iterations.json', 'raw/timing.json', 'raw/cold.json', 'summary.json')

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
    parser.add_argument('--preflight-only', action='store_true', help='Compile, check fixtures and exercise one cold route/sample; no pilot or warm timing')
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
    seeds = {r['id']: config['PilotIntervals'] for r in config['Rows']}
    cold_ids = config['ColdRows']
    iteration_hash = None
    commands, fixtures, pilot, timing, cold, comparisons, cold_comparisons = [], [], [], [], [], [], []
    count_components = {}
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
        if len(row_ids) != 9 or row_ids != pins['Rows'] or len(cold_ids)!=8 or cold_ids!=pins['ColdRows']: raise RuntimeError('fixed rows changed')

    def verify_iteration_map():
        if iteration_hash is not None and (not (private / 'iterations.tsv').is_file() or sha(private / 'iterations.tsv') != iteration_hash):
            raise RuntimeError('frozen iteration map changed')

    def verify_execution():
        if time.monotonic() >= deadline: raise TimeoutError('fixed overall deadline')
        verify_iteration_map()
        if {key: sha(path) for key, path in tools_files.items()} != tools_hashes: raise RuntimeError('tool inputs changed')
        if {name: tree(private / ('build-' + name)) for name in artifact_hashes} != artifact_hashes: raise RuntimeError('built artifact changed')
        if time.monotonic() >= deadline: raise TimeoutError('fixed overall deadline')

    def target(name, command, mode, variant, round_number=None, fixture_id=None):
        verify_execution()
        output, code = checked(name, command); values = []
        item = {'Variant':variant,'Complete':False,'Rows':values}
        if round_number is not None: item['Round'] = round_number
        if fixture_id is not None: item['Fixture'] = fixture_id
        {'preflight':fixtures,'pilot':pilot,'timing':timing,'cold':cold}[mode].append(item)
        geometry = {'Keep','ActiveMask','EntriesCapacity','ExplicitCapacity','BucketsCapacity'}
        hashes = {'ExpectedStateHash','AfterStateHash'}
        clock = {'StopwatchFrequency','TimestampMinTicks','TimestampMedianTicks','TimestampMaxTicks'}
        allowed = {'Mode','Id'} | geometry | hashes
        if mode == 'preflight': allowed |= {'Checks','Cohort'}
        else:
            allowed |= clock | {'ClearTicks','Ns','Bytes','Gc0','Gc1','Gc2'}
            if mode == 'cold': allowed |= {'BeforeStateHash','BeforeCapacityHash','AfterCapacityHash'}
            else: allowed |= {'Intervals','Cohort','MinimumIntervalTicks','IntervalTicks','ClearMs','WallMs'}
        expected_ids = [fixture_id] if mode == 'cold' else row_ids
        for line in output.decode().splitlines():
            row = json.loads(line)
            if set(row) != allowed or row['Mode'] != mode or row['Id'] not in expected_ids: raise RuntimeError('unexpected row schema')
            for key,value in row.items():
                if key in ('Mode','Id'): continue
                if key.endswith('Hash'):
                    if not isinstance(value,str) or re.fullmatch('[0-9a-f]{64}',value) is None: raise RuntimeError('invalid state hash')
                elif key == 'IntervalTicks':
                    if not isinstance(value,list) or any(type(v)!=int or v<0 for v in value): raise RuntimeError('invalid interval ticks')
                elif type(value) not in (int,float) or not math.isfinite(value) or value < 0:
                    raise RuntimeError('invalid numeric result')
            values.append(row)
            fixed = config['Rows'][row_ids.index(row['Id'])]
            expected_geometry = {'Keep':fixed['keep'],'ActiveMask':fixed['active_mask'],'EntriesCapacity':fixed['entries_capacity'],
                'ExplicitCapacity':fixed['explicit_capacity'],'BucketsCapacity':fixed['bucket_capacity']}
            if any(row[key]!=value for key,value in expected_geometry.items()): raise RuntimeError('fixture geometry changed')
            if row['ExpectedStateHash'] != row['AfterStateHash']: raise RuntimeError('Clear state differs from expected transformation')
            if mode == 'preflight':
                if row['Cohort'] != config['CohortSize']: raise RuntimeError('unexpected cohort')
            else:
                if row['StopwatchFrequency'] <= 0: raise RuntimeError('invalid stopwatch frequency')
                if mode == 'cold':
                    if row['BeforeCapacityHash'] != row['AfterCapacityHash']: raise RuntimeError('cold capacity changed')
                else:
                    count = seeds[row['Id']] if mode == 'pilot' else iterations[row['Id']]
                    if row['Intervals'] != count or row['Cohort'] != config['CohortSize'] or len(row['IntervalTicks']) != count:
                        raise RuntimeError('measured interval count changed')
                    if sum(row['IntervalTicks']) != row['ClearTicks'] or min(row['IntervalTicks']) != row['MinimumIntervalTicks']:
                        raise RuntimeError('interval totals differ')
        if code != 0 or [r['Id'] for r in values] != expected_ids: raise RuntimeError('target failed or rows missing')
        item['Complete'] = True; verify_execution()
        return values

    def summarize_cold():
        result = []
        for name in cold_ids:
            before = [v['Rows'][0] for v in cold if v['Complete'] and v['Fixture']==name and v['Variant']=='before']
            after = [v['Rows'][0] for v in cold if v['Complete'] and v['Fixture']==name and v['Variant']=='after']
            if not before and not after: continue
            entry = {'Id':name,'BeforeSamples':len(before),'AfterSamples':len(after),'Complete':len(before)==len(after)==3}
            if before and len(before)==len(after):
                fields = ('BeforeStateHash','ExpectedStateHash','AfterStateHash','BeforeCapacityHash','AfterCapacityHash')
                if any(any(a[k]!=b[k] for k in fields) for a,b in zip(before,after)): raise RuntimeError('cold source state hashes differ')
                entry.update(BeforeNs=[v['Ns'] for v in before],AfterNs=[v['Ns'] for v in after],
                    BeforeBytes=[v['Bytes'] for v in before],AfterBytes=[v['Bytes'] for v in after],
                    CandidateExtraAllocationPairs=sum(b['Bytes']>a['Bytes'] for a,b in zip(before,after)),
                    EqualNonzeroAllocationPairs=sum(a['Bytes']==b['Bytes'] and a['Bytes']>0 for a,b in zip(before,after)),
                    AfterSlowerPairs=sum(b['Ns']>a['Ns'] for a,b in zip(before,after)),
                    TimerResolutionWeak=any(v['ClearTicks'] <= config['TimerDominanceMultiple']*max(1,v['TimestampMedianTicks']) for v in before+after),
                    ObservedGC=any(v['Gc0']+v['Gc1']+v['Gc2']>0 for v in before+after))
            result.append(entry)
        return result

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
            'PreparedFileHashes': {'Tests~/PortfolioScreen/DenseClearProbe/' + name: value for name,value in frozen['Files'].items()},
            'SharedBuildSupport': {'Tests~/PortfolioScreen/Hierarchical/build_support.py': frozen['BuildSupportSHA256']}}
        tools_files = support.input_files(args.backend, dotnet, mono, nuget)
        tools_hashes = {key: sha(path) for key,path in tools_files.items()}
        binaries = {}
        for variant in ('before','after'):
            binaries[variant] = support.build(private / variant,HERE / 'Harness/Probe.cs',private / ('build-' + variant),
                args.backend,dotnet,mono,nuget,lambda name,command: require(name,command))
        artifact_hashes = {label:tree(private / ('build-' + label)) for label in binaries}
        save(private / 'execution-identities.json',{'ToolInputHashes':tools_hashes,'ArtifactHashes':artifact_hashes})
        stage = 'fixture-preflight'
        a = target('preflight-before',binaries['before']+['preflight'],'preflight','before')
        b = target('preflight-after',binaries['after']+['preflight'],'preflight','after')
        if a != b: raise RuntimeError('fixture preflight differs')
        stage = 'first-clear-cold'
        selected_cold = [config['PreflightColdRoute']] if args.preflight_only else cold_ids
        orders = [['before','after']] if args.preflight_only else config['ColdRoundOrder']
        for name in selected_cold:
            for round_number,order in enumerate(orders):
                for variant in order:
                    target('cold-'+name+'-'+variant+'-'+str(round_number),binaries[variant]+['cold',name],
                        'cold',variant,round_number,name)
        cold_comparisons = summarize_cold()
        if not args.preflight_only:
            # Retain every cold sample and summary before the fail-stop decision.
            save(public/'raw/cold.json',cold)
            save(public/'summary.json',{'Status':'incomplete','AcceptanceEvidence':False,'Backend':args.backend,
                'CompletedTimingProcesses':0,'CompletedColdProcesses':sum(item['Complete'] for item in cold),
                'Comparisons':[],'ColdComparisons':cold_comparisons})
            if len(cold_comparisons)!=len(cold_ids) or any(not row['Complete'] for row in cold_comparisons):
                stage = 'cold-incomplete'
                raise RuntimeError('full cold groups are incomplete; no warm pilot or timing')
            if any(row['CandidateExtraAllocationPairs']>0 for row in cold_comparisons):
                stage = 'cold-allocation-adverse'
                raise RuntimeError('candidate cold allocation increased; no warm pilot or timing')
            stage = 'original-only-pilot'
            raw = target('pilot-before',binaries['before']+['pilot'],'pilot','before')
            valid = True
            for row in raw:
                timer = max(1,row['TimestampMedianTicks'])
                resolution = row['ClearMs']>0 and row['WallMs']>0 and row['MinimumIntervalTicks']>row['TimestampMedianTicks'] and (
                    row['ClearTicks'] >= config['PilotResolutionMultiple'] * seeds[row['Id']] * timer)
                component = {'PilotResolutionValid':resolution,'AbsoluteCapIntervals':config['MaximumIntervals']}
                if resolution:
                    clear_count = math.ceil(seeds[row['Id']] * config['TargetClearMs'] / row['ClearMs'])
                    wall_count = math.floor(seeds[row['Id']] * config['EstimatedWallBudgetMs'] / row['WallMs'])
                    value = max(1,min(config['MaximumIntervals'],clear_count,wall_count))
                    component.update(ClearTargetIntervals=clear_count,WallBudgetIntervals=wall_count,SelectedIntervals=value,
                        ClearTargetCapped=value<clear_count,WallEstimateBelowOneInterval=wall_count<1)
                    iterations[row['Id']] = value
                else:
                    component.update(ClearTargetIntervals=None,WallBudgetIntervals=None,SelectedIntervals=None,ClearTargetCapped=True)
                    valid = False
                count_components[row['Id']] = component
            save(public/'raw/pilot.json',pilot)
            save(public/'iterations.json',{'Backend':args.backend,'Iterations':iterations,'Components':count_components,
                'MapSHA256':None,'FrozenBeforeCandidateTiming':False})
            if not valid: raise RuntimeError('baseline pilot lacks required resolution; no rerun or candidate timing')
            iteration_file = private/'iterations.tsv'
            iteration_file.write_text(''.join(name+'\t'+str(iterations[name])+'\n' for name in row_ids))
            iteration_hash = sha(iteration_file)
            save(public/'iterations.json',{'Backend':args.backend,'Iterations':iterations,'Components':count_components,
                'MapSHA256':iteration_hash,'FrozenBeforeCandidateTiming':True})
            stage = 'fixed-isolated-clear'
            for round_number,order in enumerate(config['RoundOrder']):
                for variant in order:
                    target('timing-'+variant+'-'+str(round_number),binaries[variant]+['timing','--iterations-file',iteration_file],
                        'timing',variant,round_number)
            verify_iteration_map()
            for index,name in enumerate(row_ids):
                left=[item['Rows'][index] for item in timing if item['Variant']=='before']
                right=[item['Rows'][index] for item in timing if item['Variant']=='after']
                if any(a['AfterStateHash']!=b['AfterStateHash'] for a,b in zip(left,right)): raise RuntimeError('warm source state hashes differ')
                resolution=all(v['Ns']>0 and v['MinimumIntervalTicks']>v['TimestampMedianTicks'] for v in left+right)
                ratios=[a['Ns']/b['Ns'] for a,b in zip(left,right)] if resolution else []
                ratio=math.exp(statistics.mean(map(math.log,ratios))) if ratios else None
                comparisons.append({'Id':name,'BeforeMedianNs':statistics.median(v['Ns'] for v in left),
                    'AfterMedianNs':statistics.median(v['Ns'] for v in right),'PairedRatios':ratios,'GeometricSpeedup':ratio,
                    'AfterSlowerPairs':sum(b['Ns']>a['Ns'] for a,b in zip(left,right)),
                    'ScreenRegressionSignal':ratio is not None and ratio<0.97,
                    'CandidateExtraAllocationPairs':sum(b['Bytes']>a['Bytes'] for a,b in zip(left,right)),
                    'EqualNonzeroAllocationPairs':sum(a['Bytes']==b['Bytes'] and a['Bytes']>0 for a,b in zip(left,right)),
                    'ShortestClearMs':min(v['ClearMs'] for v in left+right),'LongestWallMs':max(v['WallMs'] for v in left+right),
                    'ShortClearBatch':any(v['ClearMs']<config['TargetClearMs'] for v in left+right),
                    'TimerResolutionWeak':not resolution or any(statistics.median(v['IntervalTicks'])<=config['TimerDominanceMultiple']*max(1,v['TimestampMedianTicks']) for v in left+right),
                    'ObservedGC':any(v['Gc0']+v['Gc1']+v['Gc2']>0 for v in left+right),
                    'CountComponents':count_components[name]})
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
            'status.json':{'Status':status,'Stage':stage,'Mode':'preflight-plus-one-cold-route' if args.preflight_only else 'isolated-clear-rejection-probe','AcceptanceEvidence':False},
            'provenance.json':provenance,
            'config.json':dict(config,Backend=args.backend,DeadlineSeconds=args.deadline_seconds,PreflightOnly=args.preflight_only),
            'correctness.json':{'Fixtures':fixtures},
            'raw/pilot.json':pilot,
            'iterations.json':{'Backend':args.backend,'Iterations':iterations,'Components':count_components,'MapSHA256':iteration_hash,'FrozenBeforeCandidateTiming':iteration_hash is not None},
            'raw/timing.json':timing,
            'raw/cold.json':cold,
            'summary.json':{'Status':status,'AcceptanceEvidence':False,'Backend':args.backend,'CompletedTimingProcesses':sum(item['Complete'] for item in timing),'CompletedColdProcesses':sum(item['Complete'] for item in cold),'Comparisons':comparisons,'ColdComparisons':cold_comparisons}}
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
    print('Isolated Clear probe: ' + status + '. Explicit public JSON allowlist only; no acceptance claim.')
    return 0 if status == 'completed' else 1

if __name__ == '__main__': raise SystemExit(main())
