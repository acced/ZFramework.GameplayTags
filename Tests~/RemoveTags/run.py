#!/usr/bin/env python3
"""Frozen twenty-row RemoveTags supplement; never changes the main Evolution matrix."""
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

def run(command, **kwargs):
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
        # Stream raw bytes to disk, including nonzero exits and interrupted runs.
        with stdout_path.open('wb') as stdout, stderr_path.open('wb') as stderr:
            result = subprocess.run(command, stdout=stdout, stderr=stderr, check=False, **kwargs)
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

EXPECTED_ROWS = ['removetags/empty/self', 'removetags/empty/distinct']
EXPECTED_ROWS += [f'removetags/n{n}/{kind}-restore-pair' for n in (1,4,16,1024) for kind in ('self','equal-distinct','disjoint')]
EXPECTED_ROWS += [f'removetags/n{n}/{kind}-restore-pair' for n in (4,1024) for kind in ('counted-equal','value-proxy-equal','reference-proxy-equal')]

def parse_report(raw, mode, single_id=None, fixed=None):
    report = json.loads(raw)
    if report['Schema'] != 1 or report['Mode'] != mode: raise RuntimeError('Unexpected report schema/mode')
    rows = report['Rows']
    ids = [single_id] if single_id else EXPECTED_ROWS
    if [r['Id'] for r in rows] != ids: raise RuntimeError('Incomplete, reordered or duplicate row IDs')
    for r in rows:
        if r['WarmProbeBytes'] != 0: raise RuntimeError('Warm probe allocation')
        if mode in ('bench','calibrate'):
            if r['Iterations'] <= 0 or not math.isfinite(r['Ns']) or r['Ns'] <= 0: raise RuntimeError('Invalid measured duration/count')
            if r['Bytes'] != 0 or r['MeasuredChecksum'] != r['ExpectedMeasuredChecksum']: raise RuntimeError('Warm allocation/checksum failure')
            if fixed is not None and r['Iterations'] != fixed[r['Id']]: raise RuntimeError('Fixed count mismatch')
        elif r['Iterations'] != 0: raise RuntimeError('Untimed mode reported timing iterations')
    return report

def same_envelope(a,b,label):
    for key in ('Schema','Runtime','Framework','RegistryCount','Checksum'):
        if a[key] != b[key]: raise RuntimeError(f'Envelope mismatch {label}: {key}')

def compare(reports, left, right):
    output = []
    for key in EXPECTED_ROWS:
        a = [next(r for r in p['Rows'] if r['Id']==key) for p in reports[left]]
        b = [next(r for r in p['Rows'] if r['Id']==key) for p in reports[right]]
        logs = [math.log(x['Ns']/y['Ns']) for x,y in zip(a,b)]
        mean = statistics.mean(logs);width = 3.012*statistics.stdev(logs)/math.sqrt(14)
        lower,upper = math.exp(mean-width),math.exp(mean+width)
        flags=[]
        if upper < 1/1.03: flags.append('timing-regression')
        elif lower > 1.03: flags.append('timing-improvement')
        if any(r['WallMilliseconds']<150 for r in a+b): flags.append('short-batch-below150ms')
        if any(r['CpuMilliseconds']/r['WallMilliseconds']<.9 for r in a+b): flags.append('cpu-wall-review')
        if any(r['Gen0'] or r['Gen1'] or r['Gen2'] for r in a+b): flags.append('gc-review')
        if not flags: flags=['inconclusive']
        output.append(dict(Before=left,After=right,Id=key,BeforeNs=statistics.median(r['Ns'] for r in a),AfterNs=statistics.median(r['Ns'] for r in b),
            PairedGeometricSpeedup=math.exp(mean),Approx99Lower=lower,Approx99Upper=upper,Flags=flags,
            Under200msSamples=sum(r['WallMilliseconds']<200 for r in a+b),BeforeSamples=a,AfterSamples=b))
    return output

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', type=Path, required=True)
    parser.add_argument('--mono', type=Path)
    parser.add_argument('--variant', action='append', required=True, help='LABEL=/absolute/source/root; repeat in baseline, previous, candidate order')
    parser.add_argument('--revision', action='append', required=True, help='LABEL=40hex exact declared source revision; exactly one for every variant')
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--samples', type=int, default=14, choices=[14], help='Exactly fourteen independent forward/reverse rounds')
    parser.add_argument('--batch-ms', type=int, default=200)
    parser.add_argument('--test-only', action='store_true')
    args = parser.parse_args()
    if args.batch_ms < 200 or args.batch_ms > 5000: parser.error('Calibration target must be 200..5000ms')
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
    if not args.test_only and len(variants) not in (2, 3): parser.error('Timing requires two or three variants in baseline, optional prior accepted, candidate order')
    if args.test_only and len(variants) not in (1, 2, 3): parser.error('Correctness-only accepts one to three variants')
    out = args.out.resolve()
    if out.exists() and any(out.iterdir()): parser.error('Output must be new/empty')
    out.mkdir(parents=True, exist_ok=True)
    def source_files():
        files = [HERE / 'Program.cs', HERE / 'RemoveTagsBenchmarks.csproj', HERE / 'README.md', Path(__file__).resolve()]
        for source in variants.values(): files += sorted((source / 'Runtime').glob('**/*.cs'))
        return files
    for source in variants.values():
        if not (source / 'Runtime').is_dir() or not list((source / 'Runtime').glob('**/*.cs')):
            parser.error('Each source must contain a nonempty Runtime C# tree')
    initial_hashes = digest(source_files())
    host = args.mono.resolve() if args.mono else args.dotnet.resolve()
    env = os.environ.copy()
    env.update(DOTNET_TieredCompilation='0', DOTNET_gcServer='0', COMPlus_TieredCompilation='0', COMPlus_gcServer='0')
    manifest = dict(Schema=2, Status='prepared', StartedUTC=stamp(), DeclaredRevisions=revisions,
                    RevisionProvenance='Explicit declarations; exact source hashes are authoritative for archive snapshots',
                    WorkingDirectory=str(Path.cwd()), CommandLogs=[], SourceHashesAuthoritative=True, Platform=platform.platform(), Machine=platform.machine(), Python=sys.version,
                    Sources={k: str(v) for k, v in variants.items()},
                    ColdMode='One fresh process per source/row; not run in test-only mode', Rows=EXPECTED_ROWS,
                    SourceAndHarnessHashes=initial_hashes, Commands=sys.argv, Mode='standalone Mono' if args.mono else 'standalone .NET 8',
                    RuntimeCompilation='Runtime and supplemental harness are compiled together, identically for every variant',
                    Settings={'TieredCompilation': False, 'ServerGC': False}, ExecutionOrder=[], Samples=args.samples, TestOnly=args.test_only)
    manifest['Affinity'] = sorted(os.sched_getaffinity(0)) if hasattr(os, 'sched_getaffinity') else 'unavailable'
    if Path('/proc/cpuinfo').exists(): manifest['CPUModel'] = next((s.split(':', 1)[1].strip() for s in Path('/proc/cpuinfo').read_text().splitlines() if s.startswith('model name')), 'unavailable')
    manifest['LogicalCPUCount'] = os.cpu_count()
    global AUDIT
    AUDIT = {'out': out, 'manifest': manifest}
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
        manifest['ObservedBenchmarkRuntime'] = 'Pending first correctness report'
        manifest['Status'] = 'building'
        save_manifest()
        for label, source in variants.items():
            build = out / label; obj = build / 'obj'; target = build / 'bin'
            command = [args.dotnet.resolve(), 'build', HERE / 'RemoveTagsBenchmarks.csproj', '-c', 'Release',
                       '-p:UseSharedCompilation=false', '-p:MonoTarget=' + ('true' if args.mono else 'false'),
                       '-p:RuntimeRoot=' + str(source / 'Runtime'), '-p:BaseIntermediateOutputPath=' + str(obj) + '/',
                       '-p:MSBuildProjectExtensionsPath=' + str(obj) + '/', '-o', target]
            result = run(command, capture_output=True, env=env)
            (out / (label + '-build.txt')).write_text(result.stdout + result.stderr)
            assembly = target / ('RemoveTagsBenchmarks.exe' if args.mono else 'RemoveTagsBenchmarks.dll')
            assemblies[label] = assembly
            built_targets.append(target)
            binary_hashes.update(digest(sorted(p for p in target.glob('**/*') if p.is_file())))
            manifest['AssemblyHashes'] = dict(binary_hashes)
            save_manifest()
        manifest['Status'] = 'executing'
        save_manifest()
        tests = {}
        for label, assembly in assemblies.items():
            result = run([host, assembly, 'test'], capture_output=True, env=env)
            (out / (label + '-tests.json')).write_text(result.stdout)
            report = parse_report(result.stdout, 'test')
            tests[label] = report
            manifest['ObservedBenchmarkRuntime'] = {key: report[key] for key in ('Runtime', 'Framework')}
            save_manifest()
        reference = tests[next(iter(variants))]
        for label, report in tests.items():
            same_envelope(reference, report, label)
            if report['Rows'] != reference['Rows']: raise RuntimeError('Correctness/warm-layout outputs differ: ' + label)
        if not args.test_only:
            labels = list(variants)
            cold = {}
            cold_dir = out / 'cold'; cold_dir.mkdir()
            for label, assembly in assemblies.items():
                cold[label] = {}
                for row_id in EXPECTED_ROWS:
                    result = run([host, assembly, 'cold', row_id], capture_output=True, env=env)
                    report = parse_report(result.stdout, 'cold', single_id=row_id)
                    path = cold_dir / (label + '--' + row_id.replace('/', '_') + '.json')
                    path.write_text(result.stdout)
                    cold[label][row_id] = report
                    manifest['ObservedBenchmarkRuntime'] = {key: report[key] for key in ('Runtime', 'Framework')}
                    save_manifest()
            cold_comparison = []
            pairs = {(labels[0], label) for label in labels[1:]}
            pairs.update(zip(labels, labels[1:]))
            for left, right in sorted(pairs):
                for row_id in EXPECTED_ROWS:
                    a_report, b_report = cold[left][row_id], cold[right][row_id]
                    same_envelope(a_report, b_report, right)
                    a, b = a_report['Rows'][0], b_report['Rows'][0]
                    fields = ('BeforeEntries','BeforeExplicit','BeforeBuckets','Entries','Explicit','Buckets','RetainedFields','WarmProbeBytes','WarmProbeCalls')
                    if any(a[k] != b[k] for k in fields): raise RuntimeError('Cold retained layout/warm allocation differs: ' + row_id)
                    cold_comparison.append(dict(Before=left, After=right, Id=row_id, BeforeColdBytes=a['ColdBytes'], AfterColdBytes=b['ColdBytes'], Status='cold-allocation-regression' if b['ColdBytes']>a['ColdBytes'] else 'cold-allocation-no-regression'))
            (out / 'cold-comparison.json').write_text(json.dumps(cold_comparison, indent=2))
            manifest['ColdAllocationRegression'] = any(r['Status']=='cold-allocation-regression' for r in cold_comparison)
            save_manifest()
            if manifest['ColdAllocationRegression']: raise RuntimeError('Cold allocation regression; retained raw cold evidence, no timing started')
            # The first source alone chooses all counts. Its actual final per-row
            # calibration samples must reach the declared duration before freezing.
            pilot = run([host, assemblies[labels[0]], 'calibrate', str(args.batch_ms)], capture_output=True, env=env)
            (out / 'pilot.json').write_text(pilot.stdout)
            calibration = parse_report(pilot.stdout, 'calibrate')
            if any(r['WallMilliseconds'] < args.batch_ms for r in calibration['Rows']): raise RuntimeError('Baseline calibration did not reach target')
            fixed = {r['Id']: r['Iterations'] for r in calibration['Rows']}
            iteration_file = out / 'iterations.tsv'
            iteration_file.write_text(''.join(f'{key}\t{fixed[key]}\n' for key in EXPECTED_ROWS))
            iteration_hash = hashlib.sha256(iteration_file.read_bytes()).hexdigest()
            manifest['IterationCalibration'] = 'First variant only; actual final calibration >= target; one immutable count shared by every measured source and round'
            manifest['IterationCalibrationSource'] = labels[0]
            manifest['CalibrationReportSHA256'] = hashlib.sha256((out/'pilot.json').read_bytes()).hexdigest()
            manifest['IterationFileSHA256'] = iteration_hash
            manifest['FixedIterations'] = fixed
            manifest['TargetBatchMilliseconds'] = args.batch_ms
            save_manifest()
            raw = out / 'raw'; raw.mkdir(); reports = {label: [] for label in labels}
            for round_index in range(14):
                order = labels if round_index % 2 == 0 else list(reversed(labels))
                for label in order:
                    if hashlib.sha256(iteration_file.read_bytes()).hexdigest() != iteration_hash: raise RuntimeError('Iteration map changed')
                    manifest['ExecutionOrder'].append([round_index, label])
                    result = run([host, assemblies[label], 'bench', iteration_file], capture_output=True, env=env)
                    (raw / f'{label}-{round_index}.json').write_text(result.stdout)
                    if result.stderr: (raw / f'{label}-{round_index}.stderr.txt').write_text(result.stderr)
                    report = parse_report(result.stdout, 'bench', fixed=fixed)
                    if reports[labels[0]]: same_envelope(reports[labels[0]][0], report, label)
                    reports[label].append(report)
                    manifest['ObservedBenchmarkRuntime'] = {key: report[key] for key in ('Runtime', 'Framework')}
                    save_manifest()
            if hashlib.sha256(iteration_file.read_bytes()).hexdigest() != iteration_hash: raise RuntimeError('Iteration map changed at exit')
            first = reports[labels[0]][0]
            for label, values in reports.items():
                for report in values:
                    same_envelope(first, report, label)
                    for a,b in zip(first['Rows'],report['Rows']):
                        if any(a[k] != b[k] for k in ('Entries','Explicit','Buckets','RetainedFields','Iterations','ExpectedMeasuredChecksum')): raise RuntimeError('Measured workload/layout changed: '+label)
            (out / 'comparison.json').write_text(json.dumps([row for left, right in sorted(pairs) for row in compare(reports, left, right)], indent=2))
    except BaseException as error:
        failure = error
        manifest.update(Status='failed', FailureType=type(error).__name__, Failure=str(error))
        (out / 'failure.txt').write_text(traceback.format_exc())
        raise
    finally:
        violations = []
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
    print('Saved supplemental API evidence: ' + str(out))

if __name__ == '__main__':
    try: main()
    except subprocess.CalledProcessError as error:
        if error.stdout: print(error.stdout, file=sys.stderr)
        if error.stderr: print(error.stderr, file=sys.stderr)
        sys.exit(error.returncode or 1)
