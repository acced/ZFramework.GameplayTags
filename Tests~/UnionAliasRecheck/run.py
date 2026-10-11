#!/usr/bin/env python3
"""Independent, fixed-input reusable Union API comparison; never changes the main Evolution matrix."""
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
FROZEN = HERE.parent / 'UnionOutput'
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

def compare(reports, left, right):
    rows = []
    a_reports, b_reports = reports[left], reports[right]
    for original in a_reports[0]['Rows']:
        key = original['Id']
        a = [next(r for r in report['Rows'] if r['Id'] == key) for report in a_reports]
        b = [next(r for r in report['Rows'] if r['Id'] == key) for report in b_reports]
        result = {'Before': left, 'After': right, 'Id': key}
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
            result.update(BeforeNs=statistics.median(r['Ns'] for r in a), AfterNs=statistics.median(r['Ns'] for r in b),
                          PairedGeometricSpeedup=math.exp(mean), Approx99Lower=lower, Approx99Upper=upper,
                          BeforeBytes=old_bytes, AfterBytes=new_bytes, Status=status)
        rows.append(result)
    return rows

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', type=Path, required=True)
    parser.add_argument('--mono', type=Path)
    for name in ('before','after'): parser.add_argument('--'+name,type=Path,required=True)
    for name in ('before-revision','after-revision'): parser.add_argument('--'+name,required=True)
    parser.add_argument('--mode', choices=['aa','ab'], required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--rounds', dest='samples', type=int, choices=[1,14], default=14)
    parser.add_argument('--batch-ms', type=int, default=200)
    parser.add_argument('--iterations-file', type=Path, required=True)
    parser.add_argument('--test-only', action='store_true')
    args = parser.parse_args()
    args.variant=['original='+str(args.before),'candidate='+str(args.after)]
    args.revision=['original='+args.before_revision,'candidate='+args.after_revision]
    args.api_variant=['candidate']
    if args.samples < 1 or args.batch_ms < 50: parser.error('Use positive samples and at least a 50ms calibration target')
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
    if set(args.api_variant) - set(variants): parser.error('Unknown API variant')
    out = args.out.resolve()
    if out.exists() and any(out.iterdir()): parser.error('Output must be new/empty')
    out.mkdir(parents=True, exist_ok=True)
    def source_files():
        files = [FROZEN / 'Program.cs', FROZEN / 'UnionOutputBenchmarks.csproj', HERE / 'README.md', HERE / 'iterations.provenance.json', args.iterations_file.resolve(), Path(__file__).resolve()]
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
                    Sources={k: str(v) for k, v in variants.items()}, ApiVariants=args.api_variant,
                    SourceAndHarnessHashes=initial_hashes, Commands=sys.argv, Mode='standalone Mono' if args.mono else 'standalone .NET 8',
                    RuntimeCompilation='Runtime and supplemental harness are compiled together, identically for every variant',
                    Settings={'TieredCompilation': False, 'ServerGC': False}, ExecutionOrder=[], Samples=args.samples, TestOnly=args.test_only, PairMode=args.mode, SlotSources={'original':'original','candidate':'original' if args.mode=='aa' else 'candidate'}, NoPilot=True)
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
        manifest['ObservedBenchmarkRuntime'] = 'Not emitted by unchanged correctness-only mode' if args.test_only else 'Pending first measured report'
        manifest['Status'] = 'building'
        save_manifest()
        for label, source in variants.items():
            build = out / label; obj = build / 'obj'; target = build / 'bin'
            command = [args.dotnet.resolve(), 'build', FROZEN / 'UnionOutputBenchmarks.csproj', '-c', 'Release',
                       '-p:UseSharedCompilation=false', '-p:MonoTarget=' + ('true' if args.mono else 'false'),
                       '-p:RuntimeRoot=' + str(source / 'Runtime'), '-p:BaseIntermediateOutputPath=' + str(obj) + '/',
                       '-p:MSBuildProjectExtensionsPath=' + str(obj) + '/', '-o', target]
            if label in args.api_variant: command += ['-p:DefineConstants=REUSABLE_UNION']
            result = run(command, capture_output=True, env=env)
            (out / (label + '-build.txt')).write_text(result.stdout + result.stderr)
            assembly = target / ('UnionOutputBenchmarks.exe' if args.mono else 'UnionOutputBenchmarks.dll')
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
            print(result.stdout, flush=True)
        if not args.test_only:
            labels = list(variants)
            fixed = {}
            for line in args.iterations_file.read_text().splitlines():
                key, number = line.split('\t')
                if key in fixed or int(number)<=0: raise RuntimeError('Invalid iteration map')
                fixed[key]=int(number)
            provenance=json.loads((HERE/'iterations.provenance.json').read_text())
            if hashlib.sha256(args.iterations_file.read_bytes()).hexdigest()!=provenance['IterationMapSHA256']: raise RuntimeError('Map differs from frozen f53 Mono map')
            if len(fixed)!=25: raise RuntimeError('Expected25 timed rows')
            iteration_file=out/'iterations.tsv';iteration_file.write_bytes(args.iterations_file.read_bytes())
            iteration_hash=hashlib.sha256(iteration_file.read_bytes()).hexdigest()
            manifest['IterationCalibration']='Exact existing f53 Mono baseline-only map; no pilot; all25 counts unchanged'
            manifest['IterationOverrideSHA256']=iteration_hash
            manifest['ExpectedRows']=provenance['ExpectedRows']
            raw = out / 'raw'; raw.mkdir(); reports = {label: [] for label in labels}
            for round_index in range(args.samples):
                order = labels if round_index % 2 == 0 else list(reversed(labels))
                for label in order:
                    manifest['ExecutionOrder'].append([round_index, label])
                    if hashlib.sha256(iteration_file.read_bytes()).hexdigest()!=iteration_hash: raise RuntimeError('Map changed')
                    slot='original' if args.mode=='aa' else label
                    result = run([host, assemblies[slot], 'bench', iteration_file], capture_output=True, env=env)
                    (raw / f'{label}-{round_index}.json').write_text(result.stdout)
                    if result.stderr: (raw / f'{label}-{round_index}.stderr.txt').write_text(result.stderr)
                    reports[label].append(json.loads(result.stdout))
            first = reports[labels[0]][0]
            manifest['ObservedBenchmarkRuntime'] = {key: first[key] for key in ('Runtime', 'Framework')}
            expected_rows = [(r['Id'], r['Iterations'], r['Cohort']) for r in first['Rows']]
            if [list(r) for r in expected_rows]!=provenance['ExpectedRows']: raise RuntimeError('Frozen28 workload rows/counts/cohorts changed')
            if any(r['Iterations']!=fixed[r['Id']] for r in first['Rows'] if r['Iterations']): raise RuntimeError('Map/report count mismatch')
            for label, values in reports.items():
                for report in values:
                    for key in ('Schema', 'Runtime', 'Framework', 'RegistryCount', 'Checksum'):
                        if report[key] != first[key]: raise RuntimeError(f'Mismatch {label}: {key}')
                    if [(r['Id'], r['Iterations'], r['Cohort']) for r in report['Rows']] != expected_rows: raise RuntimeError('Workload rows differ')
            pairs = {(labels[0], label) for label in labels[1:]}
            pairs.update(zip(labels, labels[1:]))
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
