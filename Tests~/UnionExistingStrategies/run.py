#!/usr/bin/env python3
"""Frozen existing-API strategy supplement; preserves the original UnionOutput package and all controls."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import statistics
import subprocess
import sys
import re
import traceback
import time
from datetime import datetime, timezone

HERE = Path(__file__).resolve().parent
AUDIT = None

def stamp():
    return datetime.now(timezone.utc).isoformat()

def save_manifest():
    path = AUDIT['out'] / 'environment.json'
    temporary = path.with_suffix('.json.tmp')
    temporary.write_text(json.dumps(AUDIT['manifest'], indent=2))
    os.replace(temporary, path)

def digest(paths):
    return {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}

def verify_iterations():
    if AUDIT is None or 'iteration_file' not in AUDIT: return
    path = AUDIT['iteration_file']
    if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != AUDIT['iteration_sha256']:
        raise RuntimeError('Shared iteration map changed; discard run')

def run(command, **kwargs):
    verify_iterations()
    command = list(map(str, command))
    print('+ ' + ' '.join(command), flush=True)
    kwargs.pop('capture_output', None)
    index = len(AUDIT['manifest']['CommandLogs'])
    raw = AUDIT['out'] / 'command-logs'
    raw.mkdir(exist_ok=True)
    stdout_path, stderr_path = raw / f'{index:04d}.stdout.txt', raw / f'{index:04d}.stderr.txt'
    record = dict(Command=command, StartedUTC=stamp(), Status='running',
                  Stdout=str(stdout_path), Stderr=str(stderr_path))
    AUDIT['manifest']['CommandLogs'].append(record)
    save_manifest()
    try:
        # The budget starts before builds and covers every stage, including cold
        # processes, pilots, warmups, retained cohorts and setup inside children.
        remaining = AUDIT['deadline'] - time.monotonic()
        if remaining <= 0: raise TimeoutError('Whole-run budget exhausted before child execution')
        timeout = min(AUDIT['child_timeout'], remaining)
        record['TimeoutSeconds'] = timeout
        save_manifest()
        with stdout_path.open('wb') as stdout, stderr_path.open('wb') as stderr:
            result = subprocess.run(command, stdout=stdout, stderr=stderr, check=False, timeout=timeout, **kwargs)
        record.update(ReturnCode=result.returncode, Status='completed' if result.returncode == 0 else 'failed')
        completed = subprocess.CompletedProcess(command, result.returncode,
                    stdout_path.read_text(errors='replace'), stderr_path.read_text(errors='replace'))
        completed.check_returncode()
        return completed
    except BaseException as error:
        record.update(Status='failed', ErrorType=type(error).__name__, Error=str(error))
        raise
    finally:
        record['FinishedUTC'] = stamp()
        save_manifest()
        verify_iterations()

def compare(reports, left, right):
    rows = []
    a_reports, b_reports = reports[left], reports[right]
    for original in a_reports[0]['Rows']:
        key = original['Id']
        a = [next(r for r in report['Rows'] if r['Id'] == key) for report in a_reports]
        b = [next(r for r in report['Rows'] if r['Id'] == key) for report in b_reports]
        result = {'Before': left, 'After': right, 'Id': key, 'Scope': 'deep-diagnostic-not-noninferiority' if key.startswith('deep/') else 'core-comparison'}
        if original['Cohort']:
            before = statistics.median(r['RetainedBytes'] for r in a) / original['Cohort']
            after = statistics.median(r['RetainedBytes'] for r in b) / original['Cohort']
            result.update(BeforeRetainedPerObject=before, AfterRetainedPerObject=after,
                          Status='retained-review' if after > before * 1.05 + 16 else 'retained-no-flag')
        else:
            logs = [math.log(x['Ns'] / y['Ns']) for x, y in zip(a, b)]
            mean = statistics.mean(logs)
            width = 3.012 * statistics.stdev(logs) / math.sqrt(len(logs)) if len(logs) >= 14 else float('inf')
            lower = math.exp(mean - width) if math.isfinite(width) else 0
            upper = math.exp(mean + width) if math.isfinite(width) else None
            old_bytes = statistics.median(r['Bytes'] for r in a)
            new_bytes = statistics.median(r['Bytes'] for r in b)
            status = 'inconclusive'
            if upper is not None and upper < 1 / 1.03: status = 'timing-regression'
            elif lower > 1.03: status = 'timing-improvement'
            if any(r['WallMilliseconds'] < 50 for r in a + b): status = 'short-batch-review'
            if new_bytes > old_bytes: status = 'allocation-regression'
            if key.startswith('deep/'):
                result['AllocationIncreased'] = new_bytes > old_bytes
                result['TimingConclusion'] = 'Diagnostic only; never an acceptance or noninferiority result'
                status = 'deep-diagnostic-short-batch' if any(r['WallMilliseconds'] < 50 for r in a + b) else 'deep-diagnostic'
            result.update(BeforeNs=statistics.median(r['Ns'] for r in a), AfterNs=statistics.median(r['Ns'] for r in b),
                          PairedGeometricSpeedup=math.exp(mean), Approx99Lower=lower, Approx99Upper=upper,
                          BeforeBytes=old_bytes, AfterBytes=new_bytes, Status=status)
        rows.append(result)
    return rows

def main():
    main_started = time.monotonic()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', type=Path, required=True)
    parser.add_argument('--mono', type=Path)
    parser.add_argument('--variant', action='append', required=True, help='LABEL=/absolute/source/root; repeat in baseline, previous, candidate order')
    parser.add_argument('--revision', action='append', required=True, help='LABEL=40hex exact declared source revision; exactly one for every variant')
    parser.add_argument('--strategy', action='append', required=True, help='LABEL=factory-copy|literal-left|literal-right|seed-alias-aware|clear-add-both|reusable')
    parser.add_argument('--cold-replicates', type=int, default=3)
    parser.add_argument('--cold-only', action='store_true')
    parser.add_argument('--skip-cold', action='store_true', help='Warm timing only; cannot establish cold allocation behavior')
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--samples', type=int, default=14)
    parser.add_argument('--time-budget-seconds', type=int, default=10800)
    parser.add_argument('--child-timeout-seconds', type=int, default=300)
    parser.add_argument('--batch-ms', type=int, default=200)
    parser.add_argument('--iterations-file', type=Path)
    parser.add_argument('--test-only', action='store_true')
    args = parser.parse_args()
    if args.samples < 1 or args.batch_ms < 50: parser.error('Use positive samples and at least a 50ms calibration target')
    if args.time_budget_seconds <= 0 or args.child_timeout_seconds <= 0: parser.error('Time budgets must be positive')
    verification = subprocess.run([sys.executable, str(HERE / 'verify.py')], capture_output=True, text=True)
    if verification.returncode: parser.error(verification.stdout + verification.stderr)
    print(verification.stdout.strip(), flush=True)
    variants = {}
    for value in args.variant:
        if '=' not in value: parser.error('Variant must be LABEL=/absolute/source/root')
        label, source = value.split('=', 1)
        if not label or label in variants or any(c not in 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_' for c in label): parser.error('Variant labels must be unique safe names')
        variants[label] = Path(source).resolve()
    revisions = {}
    for value in args.revision:
        if '=' not in value: parser.error('Revision must be LABEL=40hex')
        label, revision = value.split('=', 1)
        if label not in variants or label in revisions or not re.fullmatch(r'[0-9a-fA-F]{40}', revision):
            parser.error('Each revision must identify one known unique variant with a full 40-hex revision')
        revisions[label] = revision.lower()
    if set(revisions) != set(variants): parser.error('Provide exactly one full revision for every variant')
    constants = {'factory-copy': '', 'literal-left': 'CHAIN_LEFT', 'literal-right': 'CHAIN_RIGHT', 'seed-alias-aware': 'CHAIN_SEED', 'clear-add-both': 'CLEAR_ADD', 'reusable': 'REUSABLE_UNION'}
    strategies = {}
    for value in args.strategy:
        if '=' not in value: parser.error('Strategy must be LABEL=STRATEGY')
        label, strategy = value.split('=', 1)
        if label not in variants or label in strategies or strategy not in constants: parser.error('Each strategy requires one known label and supported strategy')
        strategies[label] = strategy
    if set(strategies) != set(variants): parser.error('Provide exactly one strategy for every variant')
    if args.cold_replicates < 1: parser.error('Use at least one cold replicate')
    if args.cold_only and (args.skip_cold or args.test_only): parser.error('cold-only conflicts with skip-cold/test-only')
    original_revision = 'f7c99546dbf029d70896c0f05e5119b19ffd6d26'
    old_strategies = {'factory-copy', 'literal-left', 'literal-right', 'seed-alias-aware', 'clear-add-both'}
    calibration_labels = [label for label in variants if revisions[label] == original_revision and strategies[label] in old_strategies]
    if not args.test_only and not args.cold_only:
        if len(calibration_labels) != 5 or {strategies[label] for label in calibration_labels} != old_strategies:
            parser.error('Timing requires exactly one original-f7 variant for each of the five old strategies; accepted/candidate data cannot calibrate counts')
    deep_caps = {128: 4096, 1024: 64, 3000: 8}
    out = args.out.resolve()
    if out.exists() and any(out.iterdir()): parser.error('Output must be new/empty')
    out.mkdir(parents=True, exist_ok=True)
    def source_files():
        files = [HERE / 'Program.cs', HERE / 'Comparator.csproj', HERE / 'README.md', Path(__file__).resolve(), HERE / 'SCOPE.md', HERE / 'variants.json', HERE / 'run-default.py', HERE / 'freeze.py', HERE / 'verify.py', HERE / 'MANIFEST.json', HERE / 'source-provenance.json']
        for source in variants.values(): files += sorted((source / 'Runtime').glob('**/*.cs'))
        return files
    for source in variants.values():
        if not (source / 'Runtime').is_dir() or not list((source / 'Runtime').glob('**/*.cs')):
            parser.error('Each source must contain a nonempty Runtime C# tree')
    initial_hashes = digest(source_files())
    host = args.mono.resolve() if args.mono else args.dotnet.resolve()
    env = os.environ.copy()
    env.update(DOTNET_TieredCompilation='0', DOTNET_gcServer='0', COMPlus_TieredCompilation='0', COMPlus_gcServer='0')
    manifest = dict(Schema=3, ProtocolVersion=4, Status='prepared', StartedUTC=stamp(), DeclaredRevisions=revisions,
                    RevisionProvenance='Explicit declarations; exact source hashes are authoritative for archive snapshots',
                    WorkingDirectory=str(Path.cwd()), CommandLogs=[], SourceHashesAuthoritative=True, Platform=platform.platform(), Machine=platform.machine(), Python=sys.version,
                    Sources={k: str(v) for k, v in variants.items()}, Strategies=strategies,
                    SourceAndHarnessHashes=initial_hashes, Commands=sys.argv, Mode='standalone Mono' if args.mono else 'standalone .NET 8',
                    RuntimeCompilation='Runtime and supplemental harness are compiled together, identically for every variant',
                    ResourceBounds={'WholeRunSeconds': args.time_budget_seconds, 'ChildSeconds': args.child_timeout_seconds, 'DeepIterationCaps': deep_caps, 'DeepPilotCalls': 2, 'DeepWarmupCalls': 4, 'CorePilotCalls': 128, 'CoreWarmupCalls': 64, 'RetainedCohort': 32}, CalibrationLabels=calibration_labels,
                    Settings={'TieredCompilation': False, 'ServerGC': False}, ExecutionOrder=[], Samples=args.samples, TestOnly=args.test_only, ColdReplicates=args.cold_replicates, ColdOnly=args.cold_only, SkipCold=args.skip_cold, PackagePurpose='Supplemental stronger existing-API comparison; never replaces any accepted evidence; no universal noninferiority claim')
    manifest['Affinity'] = sorted(os.sched_getaffinity(0)) if hasattr(os, 'sched_getaffinity') else 'unavailable'
    if Path('/proc/cpuinfo').exists(): manifest['CPUModel'] = next((s.split(':', 1)[1].strip() for s in Path('/proc/cpuinfo').read_text().splitlines() if s.startswith('model name')), 'unavailable')
    manifest['LogicalCPUCount'] = os.cpu_count()
    global AUDIT
    AUDIT = {'out': out, 'manifest': manifest, 'started': main_started, 'deadline': main_started + args.time_budget_seconds, 'child_timeout': args.child_timeout_seconds}
    save_manifest()
    (out / 'preliminary-environment.json').write_text(json.dumps(manifest, indent=2))
    assemblies = {}
    binary_hashes = {}
    built_targets = []
    def binary_files():
        return sorted({p for target in built_targets for p in target.glob('**/*') if p.is_file()})
    failure = None
    try:
        manifest['SDKInfo'] = run([args.dotnet.resolve(), '--info'], capture_output=True).stdout
        manifest['HostVersion'] = run([host, '--version'], capture_output=True).stdout.strip()
        manifest['HostVersionMeaning'] = 'Mono engine version' if args.mono else '.NET CLI SDK version, not observed benchmark CLR version'
        manifest['InstalledDotnetRuntimes'] = run([args.dotnet.resolve(), '--list-runtimes'], capture_output=True).stdout
        manifest['ObservedBenchmarkRuntime'] = 'Pending correctness report'
        manifest['Status'] = 'building'
        save_manifest()
        for label, source in variants.items():
            build = out / label; obj = build / 'obj'; target = build / 'bin'
            command = [args.dotnet.resolve(), 'build', HERE / 'Comparator.csproj', '-c', 'Release',
                       '-p:UseSharedCompilation=false', '-p:MonoTarget=' + ('true' if args.mono else 'false'),
                       '-p:RuntimeRoot=' + str(source / 'Runtime'), '-p:BaseIntermediateOutputPath=' + str(obj) + '/',
                       '-p:MSBuildProjectExtensionsPath=' + str(obj) + '/', '-o', target]
            if constants[strategies[label]]: command += ['-p:DefineConstants=' + constants[strategies[label]]]
            result = run(command, capture_output=True, env=env)
            (out / (label + '-build.txt')).write_text(result.stdout + result.stderr)
            assembly = target / ('Comparator.exe' if args.mono else 'Comparator.dll')
            assemblies[label] = assembly
            built_targets.append(target)
            binary_hashes.update(digest(sorted(p for p in target.glob('**/*') if p.is_file())))
            manifest['AssemblyHashes'] = dict(binary_hashes)
            save_manifest()
        manifest['Status'] = 'executing'
        save_manifest()
        for label, assembly in assemblies.items():
            result = run([host, assembly, 'test'], capture_output=True, env=env)
            (out / (label + '-tests.txt')).write_text(result.stdout + result.stderr)
            report = json.loads(result.stdout)
            if report.get('Status') != 'PASS' or report.get('Strategy') != strategies[label]: raise RuntimeError('Correctness report/strategy mismatch')
            manifest.setdefault('ObservedBenchmarkRuntime', {})
            manifest['ObservedBenchmarkRuntime'] = {key: report[key] for key in ('Runtime', 'Framework')}
            print(f'{label}: PASS {report["FixtureCount"]} fixtures, {report["Assertions"]} assertions', flush=True)
        fixture_maps = {}
        for label, assembly in assemblies.items():
            result = run([host, assembly, 'list'], capture_output=True, env=env)
            fixture_maps[label] = json.loads(result.stdout)
        first_fixtures = next(iter(fixture_maps.values()))
        expected_fixtures = [(f['Id'], f['Domain'], f['ColdEligible']) for f in first_fixtures]
        for fixtures in fixture_maps.values():
            if [(f['Id'], f['Domain'], f['ColdEligible']) for f in fixtures] != expected_fixtures: raise RuntimeError('Fixture/domain drift')
        (out / 'fixtures-and-routes.json').write_text(json.dumps(fixture_maps, indent=2))
        if not args.test_only and not args.skip_cold:
            cold = out / 'cold'; cold.mkdir()
            labels = list(variants)
            for round_index in range(args.cold_replicates):
                order = labels if round_index % 2 == 0 else list(reversed(labels))
                for fixture in first_fixtures:
                    if not fixture['ColdEligible']: continue
                    for label in order:
                        manifest['ExecutionOrder'].append(['cold', round_index, label, fixture['Id']])
                        result = run([host, assemblies[label], 'cold', fixture['Id']], capture_output=True, env=env)
                        report = json.loads(result.stdout)
                        if report['Strategy'] != strategies[label] or report['Id'] != fixture['Id']: raise RuntimeError('Cold report identity mismatch')
                        safe = fixture['Id'].replace('/', '__')
                        (cold / f'{label}-{round_index}-{safe}.json').write_text(result.stdout)
        if not args.test_only and not args.cold_only:
            labels = list(variants)
            pilots = out / 'pilots'; pilots.mkdir()
            calibration = {}
            for label in calibration_labels:
                manifest['ExecutionOrder'].append(['original-only-pilot', label])
                pilot = run([host, assemblies[label], 'bench'], capture_output=True, env=env)
                (pilots / (label + '.json')).write_text(pilot.stdout)
                calibration[label] = json.loads(pilot.stdout)
            first_pilot = calibration[calibration_labels[0]]
            pilot_rows = [(r['Id'], r['Iterations'], r['Cohort']) for r in first_pilot['Rows']]
            for report in calibration.values():
                if [(r['Id'], r['Iterations'], r['Cohort']) for r in report['Rows']] != pilot_rows: raise RuntimeError('Original calibration workload drift')
            fixed, calibration_plan = {}, []
            for row in first_pilot['Rows']:
                if not row['Iterations']: continue
                key = row['Id']
                ns = {label: next(r['Ns'] for r in report['Rows'] if r['Id'] == key) for label, report in calibration.items()}
                worst = max(ns.values())
                count = max(row['Iterations'], min(50000000, math.ceil(args.batch_ms * 1000000 / max(worst, 1))))
                match = re.match(r'^deep/d(128|1024|3000)/', key)
                cap = deep_caps[int(match.group(1))] if match else 50000000
                fixed[key] = min(count, cap)
                calibration_plan.append({'Id': key, 'OriginalNs': ns, 'SlowestOriginalNs': worst, 'CalibratedIterationsBeforeOptionalOverride': fixed[key], 'PredeclaredCap': cap, 'Scope': 'deep-diagnostic-not-noninferiority' if match else 'core-comparison'})
            (out / 'calibration-plan.json').write_text(json.dumps(calibration_plan, indent=2))
            if args.iterations_file:
                override = {}
                for line in args.iterations_file.read_text().splitlines():
                    key, number = line.split('\t')
                    if key in override: raise RuntimeError('Duplicate iteration override row')
                    override[key] = int(number)
                if set(override) != set(fixed) or any(n <= 0 for n in override.values()): raise RuntimeError('Override must contain every timed row exactly once')
                for key, count in override.items():
                    match = re.match(r'^deep/d(128|1024|3000)/', key)
                    if count > (deep_caps[int(match.group(1))] if match else 50000000): raise RuntimeError('Override exceeds predeclared iteration cap')
                fixed = override
                manifest['IterationOverrideSHA256'] = hashlib.sha256(args.iterations_file.read_bytes()).hexdigest()
            iteration_file = out / 'iterations.tsv'
            iteration_file.write_text(''.join(f'{key}\t{n}\n' for key, n in fixed.items()))
            iteration_sha256 = hashlib.sha256(iteration_file.read_bytes()).hexdigest()
            AUDIT['iteration_file'] = iteration_file
            AUDIT['iteration_sha256'] = iteration_sha256
            manifest['SharedIterationMapSHA256'] = iteration_sha256
            manifest['SharedIterationMapVerification'] = 'Before and after every child command, and at exit'
            manifest['IterationCalibration'] = 'Explicit reviewed override' if args.iterations_file else 'Five original-f7-only pilots; slowest original ns/op per row; one immutable shared map with predeclared deep caps; no guaranteed target duration'
            save_manifest()
            manifest['TargetBatchMilliseconds'] = args.batch_ms
            raw = out / 'raw'; raw.mkdir(); reports = {label: [] for label in labels}
            for round_index in range(args.samples):
                order = labels if round_index % 2 == 0 else list(reversed(labels))
                for label in order:
                    manifest['ExecutionOrder'].append([round_index, label])
                    result = run([host, assemblies[label], 'bench', iteration_file], capture_output=True, env=env)
                    (raw / f'{label}-{round_index}.json').write_text(result.stdout)
                    if result.stderr: (raw / f'{label}-{round_index}.stderr.txt').write_text(result.stderr)
                    reports[label].append(json.loads(result.stdout))
            first = reports[labels[0]][0]
            manifest['ObservedBenchmarkRuntime'] = {key: first[key] for key in ('Runtime', 'Framework')}
            expected_rows = [(r['Id'], r['Iterations'], r['Cohort']) for r in first['Rows']]
            if len({r[0] for r in expected_rows}) != len(expected_rows): raise RuntimeError('Duplicate row IDs')
            for label, values in reports.items():
                for report in values:
                    for key in ('Schema', 'Runtime', 'Framework', 'RegistryCount', 'Checksum'):
                        if report[key] != first[key]: raise RuntimeError(f'Mismatch {label}: {key}')
                    if [(r['Id'], r['Iterations'], r['Cohort']) for r in report['Rows']] != expected_rows: raise RuntimeError('Workload rows differ')
            pairs = {(left, right) for i, left in enumerate(labels) for right in labels[i + 1:]}
            comparison = [row for left, right in sorted(pairs) for row in compare(reports, left, right)]
            (out / 'comparison.json').write_text(json.dumps(comparison, indent=2))
            (out / 'flat1024-focus.json').write_text(json.dumps([row for row in comparison if row['Id'].startswith('flat/n1024/')], indent=2))
            manifest['PerformanceAcceptance'] = 'Not evaluated; capture completion is not performance acceptance. Deep rows are diagnostic only; review all core intervals, allocations, and short-batch flags.'
            manifest['ComparisonStatusCounts'] = {status: sum(row.get('Status') == status for row in comparison) for status in sorted({row.get('Status') for row in comparison})}
    except BaseException as error:
        failure = error
        manifest.update(Status='failed', FailureType=type(error).__name__, Failure=str(error))
        (out / 'failure.txt').write_text(traceback.format_exc())
        raise
    finally:
        violations = []
        try:
            verify_iterations()
            if 'iteration_file' in AUDIT: manifest['FinalSharedIterationMapSHA256'] = hashlib.sha256(AUDIT['iteration_file'].read_bytes()).hexdigest()
        except Exception as error:
            violations.append('Iteration-map validation failed: ' + repr(error))
        try:
            manifest['FinalSourceAndHarnessHashes'] = digest(source_files())
            if manifest['FinalSourceAndHarnessHashes'] != initial_hashes:
                violations.append('Source or harness changed')
        except Exception as error:
            violations.append('Source/hash validation failed: ' + repr(error))
        try:
            manifest['FinalAssemblyHashes'] = digest(binary_files())
            if manifest['FinalAssemblyHashes'] != binary_hashes:
                violations.append('Built binaries or runtime configuration changed')
        except Exception as error:
            violations.append('Binary/hash validation failed: ' + repr(error))
        manifest['TotalElapsedSeconds'] = time.monotonic() - main_started
        if manifest['TotalElapsedSeconds'] > args.time_budget_seconds: violations.append('Whole-run time budget exceeded; incomplete/failed evidence')
        manifest['ImmutabilityViolations'] = violations
        manifest['FinishedUTC'] = stamp()
        if violations:
            manifest.update(Status='failed', Validation='Invalid evidence: ' + '; '.join(violations))
        elif failure is None:
            manifest.update(Status='completed', Validation='Public API correctness passed and source/harness/binary immutability verified; timing review flags are not automatic acceptance.')
        else:
            manifest['Validation'] = 'Execution failed; raw command output retained; source/harness/completed-binary immutability verified.'
        save_manifest()
        if violations and failure is None:
            raise RuntimeError('; '.join(violations) + '; discard run')
    print('Saved supplemental existing-chain evidence: ' + str(out))

if __name__ == '__main__':
    try: main()
    except subprocess.CalledProcessError as error:
        if error.stdout: print(error.stdout, file=sys.stderr)
        if error.stderr: print(error.stderr, file=sys.stderr)
        sys.exit(error.returncode or 1)
