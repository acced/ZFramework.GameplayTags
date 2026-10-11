#!/usr/bin/env python3
"""Frozen reusable-Union first-use allocation/pool probes; no timing or calibration."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import subprocess
import sys
import traceback

HERE = Path(__file__).resolve().parent

def stamp():
    return datetime.now(timezone.utc).isoformat()

def digest(paths):
    return {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(set(paths))}

def parse_labels(values, known, parser, kind):
    result = {}
    for value in values:
        label, separator, item = value.partition('=')
        if not separator or label not in known or label in result:
            parser.error(kind + ' must be LABEL=VALUE for a known unique label')
        result[label] = item
    return result

def compare_reports(reports, labels, mono):
    """Compare original to every variant and each adjacent predecessor, without rerunning fixtures."""
    lookup = {(r['Section'], r['Variant'], r['CaseIndex'], r['Mode']): r for r in reports}
    if len(lookup) != len(reports):
        raise RuntimeError('Duplicate fixed fixture identity')
    if set(labels) != {r['Variant'] for r in reports}:
        raise RuntimeError('Comparison labels do not match observed variants')
    keys = {label: {(r['Section'], r['CaseIndex'], r['Mode']) for r in reports if r['Variant'] == label} for label in labels}
    if any(keys[label] != keys[labels[0]] for label in labels):
        raise RuntimeError('Variants have different fixed fixture identities')
    pairs = [(labels[0], label) for label in labels[1:]]
    for pair in zip(labels, labels[1:]):
        if pair not in pairs:
            pairs.append(pair)
    comparisons, violations = [], []
    for before, after in pairs:
        for record in reports:
            if record['Variant'] != after:
                continue
            old = lookup[record['Section'], before, record['CaseIndex'], record['Mode']]['Observation']; new = record['Observation']
            comparison = {k: record[k] for k in ('Section', 'Variant', 'CaseIndex', 'Mode')}
            comparison.update(Baseline=before, OutputCapacityEqual=old['OutputCapacity'] == new['OutputCapacity'], SourceCapacityEqual=old['SourceCapacity'] == new['SourceCapacity'])
            problems = []
            if not comparison['OutputCapacityEqual'] or not comparison['SourceCapacityEqual']:
                problems.append('owned capacity or active-mask difference')
            if record['Mode'] == 'allocation':
                comparison.update(BaselineCold=old['FirstCallAllocatedBytes'], CandidateCold=new['FirstCallAllocatedBytes'], BaselineWarm=old['WarmCallAllocatedBytes'], CandidateWarm=new['WarmCallAllocatedBytes'],
                                  BaselineRetainedHeapDelta=old['RetainedHeapDeltaBytes'], CandidateRetainedHeapDelta=new['RetainedHeapDeltaBytes'])
                if new['FirstCallAllocatedBytes'] > old['FirstCallAllocatedBytes'] or new['WarmCallAllocatedBytes'] > old['WarmCallAllocatedBytes']:
                    problems.append('managed allocation increase')
            elif not mono:
                fields = ('PoolSourceObserved', 'FirstCallBufferAllocatedEvents', 'ObservedAllocationSizes')
                comparison['DotnetPoolObservationEqual'] = all(old[k] == new[k] for k in fields)
                comparison['PoolEvidenceScope'] = 'Observed source; sizes are full listener-lifetime payloads' if old['PoolSourceObserved'] and new['PoolSourceObserved'] else 'Equal raw/unobserved payload is not evidence of no rent or verified pool equivalence'
                comparison['BaselinePool'] = {k: old[k] for k in fields}; comparison['CandidatePool'] = {k: new[k] for k in fields}
                if not comparison['DotnetPoolObservationEqual']:
                    problems.append('.NET first-use pool observation difference')
            else:
                comparison['PoolComparison'] = 'Not inferred on Mono; raw audit payload retained'
            comparison['ReviewFlags'] = problems; comparisons.append(comparison)
            violations += [{**{k: record[k] for k in ('Section','Variant','CaseIndex','Mode')}, 'Baseline': before, 'Reason': reason} for reason in problems]
    return comparisons, violations

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', type=Path, required=True)
    parser.add_argument('--mono', type=Path)
    parser.add_argument('--variant', action='append', required=True, help='LABEL=source/root; first is factory+Copy baseline')
    parser.add_argument('--revision', action='append', required=True, help='LABEL=40hex exact source revision, or exact base when --source-patch is supplied')
    parser.add_argument('--source-patch', action='append', default=[], help='LABEL=patch/path; explicitly identify an uncommitted base-plus-patch snapshot')
    parser.add_argument('--api-variant', action='append', default=[], help='Label compiled with REUSABLE_UNION; others use factory+Copy')
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    variants = {}
    for value in args.variant:
        label, separator, source = value.partition('=')
        if not separator or not re.fullmatch(r'[A-Za-z0-9_-]+', label) or label in variants:
            parser.error('Variant must use a unique safe LABEL=source/root')
        variants[label] = Path(source).resolve()
    if len(variants) < 2:
        parser.error('Provide a factory+Copy baseline and at least one comparison variant')
    revisions = parse_labels(args.revision, variants, parser, 'Revision')
    if set(revisions) != set(variants) or any(not re.fullmatch(r'[a-fA-F0-9]{40}', r) for r in revisions.values()):
        parser.error('Provide exactly one full 40-hex revision per variant')
    revisions = {k: v.lower() for k, v in revisions.items()}
    patches = {k: Path(v).resolve() for k, v in parse_labels(args.source_patch, variants, parser, 'Source patch').items()}
    api = set(args.api_variant)
    if len(api) != len(args.api_variant) or api - set(variants) or next(iter(variants)) in api:
        parser.error('API labels must be unique known variants; first variant must use factory+Copy')
    for source in variants.values():
        if not (source / 'Runtime').is_dir() or not list((source / 'Runtime').rglob('*.cs')):
            parser.error('Each source must contain Runtime C# files')
    for patch in patches.values():
        if not patch.is_file():
            parser.error('Source patch must be an existing file')
    sdk = args.dotnet.resolve()
    host = args.mono.resolve() if args.mono else sdk
    if not sdk.is_file() or not host.is_file():
        parser.error('Pass existing dotnet and optional mono executable paths')
    out = args.out.resolve()
    if out.exists() and any(out.iterdir()):
        parser.error('Output must be new or empty')
    out.mkdir(parents=True, exist_ok=True)
    for source in variants.values():
        if out == source / 'Runtime' or source / 'Runtime' in out.parents:
            parser.error('Output must not be inside a Runtime source tree')
    if out == HERE or HERE in out.parents:
        parser.error('Keep generated evidence outside the package directory')
    fixture = json.loads((HERE / 'fixtures.json').read_text())
    for name, expected in fixture['SHA256'].items():
        if digest([HERE / name])[str(HERE / name)] != expected:
            parser.error('Frozen fixture hash mismatch: ' + name)
    def input_files():
        files = [p for p in HERE.iterdir() if p.is_file()]
        for source in variants.values():
            files += [p for p in (source / 'Runtime').rglob('*') if p.is_file()]
        return files + list(patches.values())
    initial = digest(input_files())
    env = os.environ.copy()
    env.update(DOTNET_TieredCompilation='0', COMPlus_TieredCompilation='0', DOTNET_gcServer='0', COMPlus_gcServer='0',
               DOTNET_CLI_USE_MSBUILD_SERVER='0', MSBUILDDISABLENODEREUSE='1')
    manifest = dict(Schema=1, Status='prepared', StartedUTC=stamp(), Purpose=__doc__, Sources={k: str(v) for k, v in variants.items()},
                    SourceProvenance={k: {'Revision': revisions[k], 'Form': 'base-plus-patch snapshot' if k in patches else 'declared exact revision',
                                         'Patch': str(patches[k]) if k in patches else None,
                                         'PatchSHA256': digest([patches[k]])[str(patches[k])] if k in patches else None} for k in variants},
                    RevisionCaveat='Caller declarations are not Git verification; exact file hashes are authoritative. A patched snapshot is never labeled an exact commit.',
                    ApiVariants=sorted(api), Backend='Mono' if args.mono else '.NET', Platform=platform.platform(), Machine=platform.machine(),
                    Python=sys.version, LogicalCPUCount=os.cpu_count(), Affinity=sorted(os.sched_getaffinity(0)) if hasattr(os, 'sched_getaffinity') else None,
                    SourceAndHarnessHashes=initial, HostHashes=digest([sdk, host]), CommandLine=sys.argv, CommandLogs=[], ExecutionOrder=[],
                    Settings={'TieredCompilation': False, 'ServerGC': False}, AllocationModesSeparateProcesses=True, TimingPerformed=False,
                    MonoPoolEventCaveat='Mono audit payloads are preserved, but unavailable EventSource events never establish no rent or pool equivalence.',
                    EnvironmentOverrides={k: env[k] for k in ('DOTNET_TieredCompilation','COMPlus_TieredCompilation','DOTNET_gcServer','COMPlus_gcServer')})
    runtime_prefixes = ('DOTNET_', 'COMPlus_', 'MONO_', 'COREHOST_', 'CORECLR_', 'COR_')
    safe_settings = {'DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_ROOT_X86', 'DOTNET_MULTILEVEL_LOOKUP',
                     'DOTNET_ROLL_FORWARD', 'DOTNET_ROLL_FORWARD_TO_PRERELEASE', 'DOTNET_SHARED_STORE',
                     'DOTNET_ADDITIONAL_DEPS', 'DOTNET_STARTUP_HOOKS', 'DOTNET_EnableDiagnostics',
                     'DOTNET_TieredCompilation', 'DOTNET_gcServer', 'DOTNET_GCHeapHardLimit',
                     'COMPlus_TieredCompilation', 'COMPlus_gcServer', 'COMPlus_ReadyToRun', 'COMPlus_JitNoInline',
                     'COMPlus_GCHeapHardLimit', 'MONO_ENV_OPTIONS', 'MONO_GC_PARAMS', 'MONO_LOG_LEVEL',
                     'MONO_LOG_MASK', 'MONO_THREADS_SUSPEND', 'MONO_PATH', 'MONO_CFG_DIR',
                     'CORECLR_ENABLE_PROFILING', 'CORECLR_PROFILER', 'CORECLR_PROFILER_PATH',
                     'COR_ENABLE_PROFILING', 'COR_PROFILER', 'COR_PROFILER_PATH',
                     'COREHOST_TRACE', 'COREHOST_TRACE_VERBOSITY', 'COREHOST_TRACEFILE'}
    manifest['EffectiveRuntimeEnvironment'] = {k: v for k, v in env.items() if k in safe_settings}
    manifest['OtherPrefixedEnvironmentNames'] = sorted(k for k in env if k.startswith(runtime_prefixes) and k not in safe_settings)
    manifest['FixtureHostTraceOverride'] = None if args.mono else {'COREHOST_TRACE': '1', 'COREHOST_TRACE_VERBOSITY': '4', 'COREHOST_TRACEFILE': 'one raw hosttrace file per fixture process'}
    if Path('/proc/cpuinfo').exists():
        manifest['CPUModel'] = next((s.split(':', 1)[1].strip() for s in Path('/proc/cpuinfo').read_text().splitlines() if s.startswith('model name')), 'unavailable')
    def save():
        temporary = out / 'environment.json.tmp'
        temporary.write_text(json.dumps(manifest, indent=2) + '\n')
        os.replace(temporary, out / 'environment.json')
    save()
    (out / 'preliminary-environment.json').write_text(json.dumps(manifest, indent=2) + '\n')
    raw = out / 'raw'; raw.mkdir()
    def run(command, name, runtime_trace=False):
        command = list(map(str, command)); stdout = raw / (name + '.stdout.txt'); stderr = raw / (name + '.stderr.txt')
        child_env = env.copy(); trace = raw / (name + '.hosttrace.txt')
        # Native host tracing resolves the actually loaded CoreCLR without adding
        # managed instrumentation or changing the frozen fixture's measured body.
        if runtime_trace and not args.mono:
            child_env.update(COREHOST_TRACE='1', COREHOST_TRACE_VERBOSITY='4', COREHOST_TRACEFILE=str(trace))
        record = dict(Command=command, StartedUTC=stamp(), Status='running', Stdout=str(stdout), Stderr=str(stderr))
        if runtime_trace and not args.mono:
            record['HostTrace'] = str(trace)
        manifest['CommandLogs'].append(record); save()
        try:
            with stdout.open('wb') as sout, stderr.open('wb') as serr:
                result = subprocess.run(command, stdout=sout, stderr=serr, env=child_env, timeout=300)
            record.update(ReturnCode=result.returncode, Status='completed' if result.returncode == 0 else 'failed')
            result.check_returncode()
            return stdout.read_bytes(), stderr.read_bytes(), trace
        except BaseException as error:
            record.update(Status='failed', ErrorType=type(error).__name__, Error=str(error)); raise
        finally:
            record['FinishedUTC'] = stamp()
            record['RawHashes'] = digest([p for p in (stdout, stderr, trace) if p.is_file()]); save()
    targets = []; binaries = {}; runtime_hashes = {}; runtime_paths = None; reports = []; comparisons = []; failure = None
    def binary_files():
        return [p for target in targets for p in target.rglob('*') if p.is_file()]
    try:
        manifest['SDKInfo'] = run([sdk, '--info'], 'sdk-info')[0].decode(errors='replace')
        manifest['InstalledDotnetRuntimes'] = run([sdk, '--list-runtimes'], 'dotnet-runtimes')[0].decode(errors='replace')
        manifest['HostVersion'] = run([host, '--version'], 'host-version')[0].decode(errors='replace')
        manifest['HostVersionMeaning'] = 'Actual Mono engine version' if args.mono else 'SDK CLI version; actual CoreCLR paths are recorded per fixture process'
        manifest['Status'] = 'building'; save()
        projects = {'alias': 'UnionAliasGuardProbe', 'distinct': 'UnionDistinctOutputProbe'}
        assemblies = {}
        for section, project in projects.items():
            for label, source in variants.items():
                build = out / 'build' / section / label; obj = build / 'obj'; target = build / 'bin'
                command = [sdk, 'build', HERE / (project + '.csproj'), '-c', 'Release', '-p:UseSharedCompilation=false',
                           '-p:MonoTarget=' + str(bool(args.mono)).lower(), '-p:RuntimeRoot=' + str(source / 'Runtime'),
                           '-p:BaseIntermediateOutputPath=' + str(obj) + '/', '-p:MSBuildProjectExtensionsPath=' + str(obj) + '/', '-o', target]
                if label in api:
                    command.append('-p:DefineConstants=REUSABLE_UNION')
                run(command, 'build-' + section + '-' + label)
                targets.append(target); binaries.update(digest([p for p in target.rglob('*') if p.is_file()]))
                assemblies[section, label] = target / (project + ('.exe' if args.mono else '.dll'))
                manifest['AssemblyHashes'] = dict(binaries); save()
        manifest['Status'] = 'executing'; save()
        for section in projects:
            cases = [(c, None) for c in fixture['AliasCases']] if section == 'alias' else [(c, output) for c in fixture['DistinctCases'] for output in fixture['DistinctOutputModes']]
            for label in variants:
                for case_index, (case, output_mode) in enumerate(cases):
                    for mode in fixture['Modes']:
                        name = f'{section}-{label}-{case_index:02d}-{mode}'
                        command = [host, assemblies[section, label], mode, *case] + ([output_mode] if output_mode else [])
                        manifest['ExecutionOrder'].append(name)
                        stdout, stderr, trace = run(command, name, runtime_trace=True)
                        observation = json.loads(stdout)
                        expected = dict(Schema=1, Framework='Standalone Mono' if args.mono else '.NET', Mode=mode, LargeExplicitCount=case[0], SmallExplicitCount=case[1], AliasPosition=case[2], Shape=case[3], SourceKind=case[4], CodePreparation='None; first invocation in a fresh process')
                        if output_mode:
                            expected['OutputMode'] = output_mode
                        if any(observation.get(k) != v for k, v in expected.items()):
                            raise RuntimeError('Fixture identity/provenance mismatch: ' + name)
                        runtime = {'MonoHostVersion': manifest['HostVersion']} if args.mono else {}
                        if not args.mono:
                            trace_text = trace.read_text(errors='replace')
                            paths = re.findall(r"CoreCLR path = '([^']+)'", trace_text)
                            if not paths:
                                raise RuntimeError('Actual CoreCLR path missing from host trace: ' + name)
                            runtime['CoreCLRPaths'] = sorted(set(paths))
                            runtime['CoreCLRHashes'] = digest([Path(p) for p in runtime['CoreCLRPaths']])
                            if runtime_paths is not None and runtime_paths != runtime['CoreCLRPaths']:
                                raise RuntimeError('Different CoreCLR selected across fixed fixture processes')
                            runtime_paths = runtime['CoreCLRPaths']
                            for path, checksum in runtime['CoreCLRHashes'].items():
                                if path in runtime_hashes and runtime_hashes[path] != checksum:
                                    raise RuntimeError('CoreCLR binary changed during probes: ' + path)
                                runtime_hashes[path] = checksum
                            manifest['ObservedCoreCLRHashes'] = dict(runtime_hashes)
                        record = dict(Section=section, Variant=label, CaseIndex=case_index, Mode=mode, Observation=observation,
                                      RawSHA256=hashlib.sha256(stdout).hexdigest(), Runtime=runtime, RawStdout=str(raw / (name + '.stdout.txt')))
                        reports.append(record)
                        (out / 'observations.json').write_text(json.dumps(reports, indent=2) + '\n')
                        manifest['CompletedFixtureProcesses'] = len(reports); save()
                print(f'{section} {label}: {len(cases) * len(fixture["Modes"])} fresh fixture processes passed', flush=True)
        comparisons, violations = compare_reports(reports, list(variants), bool(args.mono))
        manifest['ComparisonPairs'] = list(dict.fromkeys((r['Baseline'], r['Variant']) for r in comparisons))
        (out / 'comparison.json').write_text(json.dumps(comparisons, indent=2) + '\n')
        manifest.update(ComparisonFlags=violations, CompletedFixtureProcesses=len(reports), ExpectedFixtureProcesses=54 * len(variants))
        if len(reports) != 54 * len(variants):
            raise RuntimeError('Unexpected fixed fixture count')
        if violations:
            raise RuntimeError('Allocation/owned-layout/pool review flags; preserve and inspect comparison.json')
    except BaseException as error:
        failure = error; manifest.update(Status='failed', FailureType=type(error).__name__, Failure=str(error))
        (out / 'failure.txt').write_text(traceback.format_exc()); raise
    finally:
        issues = []
        for key, paths, expected in [('FinalSourceAndHarnessHashes', input_files, initial), ('FinalAssemblyHashes', binary_files, binaries), ('FinalHostHashes', lambda: [sdk, host], manifest['HostHashes']), ('FinalObservedCoreCLRHashes', lambda: [Path(p) for p in runtime_hashes], runtime_hashes)]:
            try:
                manifest[key] = digest(paths())
                if manifest[key] != expected:
                    issues.append(key + ' changed')
            except Exception as error:
                issues.append(key + ': ' + repr(error))
        manifest['ImmutabilityViolations'] = issues; manifest['FinishedUTC'] = stamp()
        manifest['RawFileHashes'] = digest([p for p in raw.iterdir() if p.is_file()])
        if issues:
            manifest.update(Status='failed', Validation='Evidence invalid: ' + '; '.join(issues))
        elif failure is None:
            manifest.update(Status='completed', Validation='All fixed fresh-process probes passed; source/harness/binary hashes unchanged; no timing or stable retained-heap claim')
        else:
            manifest['Validation'] = 'Failure retained with raw output and hashes; not a passing gate'
        save()
        if issues and failure is None:
            raise RuntimeError('; '.join(issues))
    print('Saved cold evidence: ' + str(out))

if __name__ == '__main__':
    main()
