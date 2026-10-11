#!/usr/bin/env python3
"""Frozen same-storage Union structure, Count=0 guard, and first-use allocation probes; no timing."""
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
import time
import traceback

HERE = Path(__file__).resolve().parent


def stamp():
    return datetime.now(timezone.utc).isoformat()


def digest(paths):
    return {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(set(paths))}


def labels(values, known, parser, kind):
    result = {}
    for value in values:
        label, separator, item = value.partition('=')
        if not separator or label not in known or label in result:
            parser.error(kind + ' requires LABEL=VALUE for a known unique label')
        result[label] = item
    return result


def pairs(variants):
    names = list(variants)
    return list(dict.fromkeys([(names[0], x) for x in names[1:]] + list(zip(names, names[1:]))))


def structure_rows(data, program, section):
    rows = [json.loads(line) for line in data.splitlines()]
    expected = dict(Complete=True, Cases=program['Cases'], StagesPerCase=5,
                    AllRetainedArrayElementsComparedExactly=True)
    if not rows or rows[-1] != expected or len(rows) != program['Cases'] * 5 + 1:
        raise RuntimeError(section + ': missing or unexpected completion/row count')
    rows = rows[:-1]
    identities = {(r['Id'], r['Phase']) for r in rows}
    if len(identities) != len(rows):
        raise RuntimeError(section + ': duplicate case/stage')
    cases = {r['Id'] for r in rows}
    if len(cases) != program['Cases'] or any({r['Phase'] for r in rows if r['Id'] == case} != {'before', 'union', 'clear', 'regrow', 'drain'} for case in cases):
        raise RuntimeError(section + ': incomplete case stages')
    if section == 'guard':
        for row in rows:
            snapshot = row['Snapshot']
            counts = snapshot['Count'], snapshot['ExplicitCount']
            if row['Phase'] == 'before' and counts != (0, 1):
                raise RuntimeError('Guard did not start at Count=0/global ExplicitCount=1')
            if row['Phase'] == 'union' and counts != ((2, 1) if '/proxy-proxy/' in row['Id'] else (0, 0)):
                raise RuntimeError('Guard output differs from frozen normal/proxy control')
    return rows


def compare_reports(reports, variants, mono):
    comparisons, flags = [], []
    lookup = {(r['Section'], r['Variant'], r['CaseIndex'], r['Mode']): r for r in reports}
    if len(lookup) != len(reports):
        raise RuntimeError('Duplicate fixture identity')
    keys = {label: {(r['Section'], r['CaseIndex'], r['Mode']) for r in reports if r['Variant'] == label} for label in variants}
    if any(value != keys[next(iter(variants))] for value in keys.values()):
        raise RuntimeError('Variants have different fixture identities')
    for before, after in pairs(variants):
        for r in reports:
            if r['Variant'] != after:
                continue
            old = lookup[r['Section'], before, r['CaseIndex'], r['Mode']]
            item = {k: r[k] for k in ('Section', 'Variant', 'CaseIndex', 'Mode')}
            item.update(Baseline=before, ReviewFlags=[])
            if r['Section'] != 'cold':
                # Each unchanged C# process first compares every retained array
                # element to factory+Copy. Cross-source rows include all checksums.
                item['AllRowsEqual'] = old['Rows'] == r['Rows']
                item['RowsCompared'] = len(r['Rows'])
                item['FullRetainedArraysComparedInsideUnchangedProgram'] = True
                if not item['AllRowsEqual']:
                    item['ReviewFlags'].append('structural outputs/capacities/masks/checksums differ')
            else:
                a, b = old['Observation'], r['Observation']
                item['FullBeforeSnapshotEqual'] = a['Before'] == b['Before']
                item['FullAfterSnapshotEqual'] = a['After'] == b['After']
                if not item['FullBeforeSnapshotEqual'] or not item['FullAfterSnapshotEqual']:
                    item['ReviewFlags'].append('cold outputs/full arrays/capacities/masks/checksums differ')
                if r['Mode'] == 'allocation':
                    item.update(BaselineColdBytes=a['FirstCallAllocatedBytes'], CandidateColdBytes=b['FirstCallAllocatedBytes'],
                                BaselineRetainedHeapDelta=a['RetainedHeapDeltaBytes'], CandidateRetainedHeapDelta=b['RetainedHeapDeltaBytes'])
                    item['ColdAllocationIncrease'] = b['FirstCallAllocatedBytes'] > a['FirstCallAllocatedBytes']
                    if item['ColdAllocationIncrease']:
                        item['ReviewFlags'].append('first-use managed allocation increase')
                elif mono:
                    item['PoolComparison'] = 'Not inferred on Mono; raw audit payload preserved'
                else:
                    fields = ('PoolSourceObserved', 'ObservedApplyPoolEvents')
                    item['DotnetObservedPoolPayloadEqual'] = all(a[k] == b[k] for k in fields)
                    item['BaselinePool'] = {k: a[k] for k in fields}
                    item['CandidatePool'] = {k: b[k] for k in fields}
                    item['PoolEvidenceScope'] = 'Apply-window payload only; equal empty/unobserved payload does not establish no rent or pool equivalence'
                    if not item['DotnetObservedPoolPayloadEqual']:
                        item['ReviewFlags'].append('.NET observed Apply-window pool payload difference')
            comparisons.append(item)
            flags += [{k: v for k, v in item.items() if k in ('Section', 'Variant', 'Baseline', 'CaseIndex', 'Mode')} | {'Reason': reason} for reason in item['ReviewFlags']]
    return comparisons, flags


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', type=Path, required=True)
    parser.add_argument('--mono', type=Path)
    parser.add_argument('--variant', action='append', required=True, help='LABEL=source/root; first uses factory+Copy')
    parser.add_argument('--revision', action='append', required=True, help='LABEL=40hex exact commit or declared patch base')
    parser.add_argument('--api-variant', action='append', default=[], help='Label compiled with REUSABLE_UNION')
    parser.add_argument('--source-patch', action='append', default=[], help='LABEL=path; label an uncommitted base-plus-patch snapshot honestly')
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    variants = {}
    for value in args.variant:
        label, separator, root = value.partition('=')
        if not separator or not root or not re.fullmatch(r'[A-Za-z0-9_-]+', label) or label in variants:
            parser.error('Variant requires a unique safe LABEL=source/root')
        variants[label] = Path(root).resolve()
    if len(variants) < 2:
        parser.error('Provide original factory+Copy and at least one comparison variant')
    revisions = labels(args.revision, variants, parser, 'Revision')
    if set(revisions) != set(variants) or any(not re.fullmatch(r'[a-fA-F0-9]{40}', r) for r in revisions.values()):
        parser.error('Provide exactly one full 40-hex revision per variant')
    revisions = {k: v.lower() for k, v in revisions.items()}
    patches = {k: Path(v).resolve() for k, v in labels(args.source_patch, variants, parser, 'Source patch').items()}
    api = set(args.api_variant)
    if len(api) != len(args.api_variant) or api - set(variants) or next(iter(variants)) in api:
        parser.error('API labels must be unique known variants; first variant must use factory+Copy')
    for source in variants.values():
        if not (source / 'Runtime').is_dir() or not list((source / 'Runtime').rglob('*.cs')):
            parser.error('Each source must contain Runtime C# files')
    if any(not p.is_file() for p in patches.values()):
        parser.error('Source patch must be an existing file')
    sdk = args.dotnet.resolve()
    host = args.mono.resolve() if args.mono else sdk
    if not sdk.is_file() or not host.is_file():
        parser.error('Pass existing dotnet and optional mono executable paths')
    out = args.out.resolve()
    if out.exists() and (not out.is_dir() or any(out.iterdir())):
        parser.error('Output must be new or empty')
    if out == HERE or HERE in out.parents or any(out == s / 'Runtime' or s / 'Runtime' in out.parents for s in variants.values()):
        parser.error('Keep evidence outside the package and Runtime trees')
    fixture = json.loads((HERE / 'fixtures.json').read_text())
    for name, expected in fixture['SHA256'].items():
        if digest([HERE / name])[str(HERE / name)] != expected:
            parser.error('Frozen fixture hash mismatch: ' + name)
    out.mkdir(parents=True, exist_ok=True)
    raw = out / 'raw'; raw.mkdir()
    def inputs():
        return [p for p in HERE.rglob('*') if p.is_file() and '__pycache__' not in p.parts] + [p for s in variants.values() for p in (s / 'Runtime').rglob('*') if p.is_file()] + list(patches.values())
    initial = digest(inputs())
    env = os.environ.copy()
    env.update(DOTNET_TieredCompilation='0', COMPlus_TieredCompilation='0', DOTNET_gcServer='0', COMPlus_gcServer='0',
               DOTNET_CLI_USE_MSBUILD_SERVER='0', MSBUILDDISABLENODEREUSE='1')
    safe = {'DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_ROOT_X86', 'DOTNET_ROLL_FORWARD', 'DOTNET_MULTILEVEL_LOOKUP', 'DOTNET_TieredCompilation', 'COMPlus_TieredCompilation', 'DOTNET_gcServer', 'COMPlus_gcServer', 'COMPlus_ReadyToRun', 'COMPlus_JitNoInline', 'MONO_ENV_OPTIONS', 'MONO_GC_PARAMS', 'MONO_PATH', 'MONO_CFG_DIR', 'MONO_THREADS_SUSPEND'}
    prefixed = ('DOTNET_', 'COMPlus_', 'MONO_', 'COREHOST_', 'CORECLR_', 'COR_')
    manifest = dict(Schema=1, Status='prepared', StartedUTC=stamp(), Purpose=__doc__, Sources={k: str(v) for k, v in variants.items()},
                    SourceProvenance={k: {'Revision': revisions[k], 'Form': 'base-plus-patch snapshot' if k in patches else 'declared exact revision',
                                         'Patch': str(patches[k]) if k in patches else None, 'PatchSHA256': digest([patches[k]])[str(patches[k])] if k in patches else None} for k in variants},
                    RevisionCaveat='Caller declarations are not Git verification; exact file hashes are authoritative. Patched snapshots are not exact commits.',
                    ApiVariants=sorted(api), Backend='Mono' if args.mono else '.NET', Platform=platform.platform(), Machine=platform.machine(),
                    Python=sys.version, LogicalCPUCount=os.cpu_count(), Affinity=sorted(os.sched_getaffinity(0)) if hasattr(os, 'sched_getaffinity') else None,
                    SourceAndPackageHashes=initial, HostHashes=digest([sdk, host]), CommandLine=sys.argv, CommandLogs=[], ExecutionOrder=[],
                    ComparisonPairs=pairs(variants), TimingPerformed=False, ColdModesSeparateFreshProcesses=True, ColdWarmupPerformed=False,
                    Settings={'BuildConfiguration': 'Release', 'Optimize': True, 'TieredCompilation': False, 'ServerGC': False, 'MonoOptimizations': 'Inherited host settings; no optimize switches added'},
                    EffectiveRuntimeEnvironment={k: v for k, v in env.items() if k in safe}, OtherPrefixedEnvironmentNames=sorted(k for k in env if k.startswith(prefixed) and k not in safe),
                    MonoPoolEventCaveat='Absent Mono EventSource events do not establish no rent or pool equivalence.',
                    HostHashCaveat='Supplied launchers are hashed; Linux also captures observed native process executables. No unobserved wrapper target is inferred.',
                    FixtureHostTraceOverride=None if args.mono else {'COREHOST_TRACE': '1', 'COREHOST_TRACE_VERBOSITY': '4', 'COREHOST_TRACEFILE': 'per fixture raw host trace'})
    def save():
        temporary = out / 'environment.json.tmp'
        temporary.write_text(json.dumps(manifest, indent=2) + '\n')
        os.replace(temporary, out / 'environment.json')
    save()
    (out / 'preliminary-environment.json').write_text(json.dumps(manifest, indent=2) + '\n')
    binaries, native_hashes, runtime_hashes, targets, reports = {}, {}, {}, [], []
    runtime_paths = None
    def observe_native(process, record):
        try:
            executable = Path(os.readlink('/proc/' + str(process.pid) + '/exe'))
        except (FileNotFoundError, PermissionError, OSError):
            return
        if not executable.is_file():
            return
        path = str(executable)
        if path not in native_hashes:
            native_hashes.update(digest([executable]))
        record.setdefault('ObservedNativeExecutables', {})[path] = native_hashes[path]
    def run(command, name, trace_runtime=False):
        command = list(map(str, command))
        stdout, stderr, trace = [raw / (name + suffix) for suffix in ('.stdout.txt', '.stderr.txt', '.hosttrace.txt')]
        child_env = env.copy()
        if trace_runtime and not args.mono:
            child_env.update(COREHOST_TRACE='1', COREHOST_TRACE_VERBOSITY='4', COREHOST_TRACEFILE=str(trace))
        record = dict(Command=command, StartedUTC=stamp(), Status='running', Stdout=str(stdout), Stderr=str(stderr))
        if trace_runtime and not args.mono:
            record['HostTrace'] = str(trace)
        manifest['CommandLogs'].append(record); save()
        process = None
        try:
            with stdout.open('wb') as sout, stderr.open('wb') as serr:
                process = subprocess.Popen(command, stdout=sout, stderr=serr, env=child_env)
                deadline = time.monotonic() + 300
                while True:
                    observe_native(process, record)
                    try:
                        code = process.wait(timeout=min(0.1, max(0.001, deadline - time.monotonic())))
                        break
                    except subprocess.TimeoutExpired:
                        if time.monotonic() >= deadline:
                            process.kill(); process.wait()
                            raise subprocess.TimeoutExpired(command, 300)
            record.update(ReturnCode=code, Status='completed' if code == 0 else 'failed')
            if code:
                raise subprocess.CalledProcessError(code, command)
            return stdout.read_bytes(), trace
        except BaseException as error:
            if process is not None and process.poll() is None:
                process.kill(); process.wait()
            record.update(Status='failed', ErrorType=type(error).__name__, Error=str(error)); raise
        finally:
            record.update(FinishedUTC=stamp(), RawHashes=digest([p for p in (stdout, stderr, trace) if p.is_file()]))
            manifest['ObservedNativeHostHashes'] = dict(native_hashes); save()
    def runtime_evidence(trace):
        nonlocal runtime_paths
        if args.mono:
            return {'MonoEngineVersion': manifest['HostVersion']}
        paths = sorted(set(re.findall(r"CoreCLR path = '([^']+)'", trace.read_text(errors='replace'))))
        if not paths or runtime_paths is not None and paths != runtime_paths:
            raise RuntimeError('Missing or inconsistent actual CoreCLR path in fixture trace')
        runtime_paths = paths
        hashes = digest([Path(p) for p in paths])
        if any(p in runtime_hashes and runtime_hashes[p] != h for p, h in hashes.items()):
            raise RuntimeError('CoreCLR changed during probes')
        runtime_hashes.update(hashes); manifest['ObservedCoreCLRHashes'] = dict(runtime_hashes)
        return {'CoreCLRPaths': paths, 'CoreCLRHashes': hashes}
    def preserve_report(record):
        reports.append(record)
        # Full cold arrays and structural rows remain in byte-raw streams; avoid
        # duplicating tens of megabytes into a second JSON representation.
        (out / 'observations.json').write_text(json.dumps([{k: v for k, v in r.items() if k not in ('Rows', 'Observation')} for r in reports], indent=2) + '\n')
        manifest['CompletedFixtureProcesses'] = len(reports); save()
    failure = None
    try:
        manifest['SDKInfo'] = run([sdk, '--info'], 'sdk-info')[0].decode(errors='replace')
        manifest['InstalledDotnetRuntimes'] = run([sdk, '--list-runtimes'], 'dotnet-runtimes')[0].decode(errors='replace')
        manifest['HostVersion'] = run([host, '--version'], 'host-version')[0].decode(errors='replace')
        manifest['HostVersionMeaning'] = 'Actual Mono engine version' if args.mono else 'SDK CLI version; actual CoreCLR path/hash comes from every fixture trace'
        manifest['Status'] = 'building'; save()
        assemblies = {}
        for section, program in fixture['Programs'].items():
            for label, source in variants.items():
                build = out / 'build' / section / label; obj = build / 'obj'; target = build / 'bin'
                targets.append(target)
                command = [sdk, 'build', HERE / 'UnionSelfStateProbe.csproj', '-c', 'Release', '-p:UseSharedCompilation=false',
                           '-p:MonoTarget=' + str(bool(args.mono)).lower(), '-p:RuntimeRoot=' + str(source / 'Runtime'),
                           '-p:CommonSource=' + program['CommonSource'], '-p:ProgramSource=' + program['ProgramSource'],
                           '-p:BaseIntermediateOutputPath=' + str(obj) + '/', '-p:MSBuildProjectExtensionsPath=' + str(obj) + '/', '-o', target]
                if label in api:
                    command.append('-p:DefineConstants=REUSABLE_UNION')
                run(command, 'build-' + section + '-' + label)
                binaries.update(digest([p for p in target.rglob('*') if p.is_file()]))
                assemblies[section, label] = target / ('UnionSelfStateProbe.exe' if args.mono else 'UnionSelfStateProbe.dll')
                manifest['AssemblyHashes'] = dict(binaries); save()
        manifest['Status'] = 'executing'; save()
        for section, program in fixture['Programs'].items():
            for label in variants:
                cases = [(i, mode, [mode, *map(str, case)]) for i, case in enumerate(fixture['ColdCases']) for mode in fixture['ColdModes']] if section == 'cold' else [(0, 'structure', [])]
                for case_index, mode, arguments in cases:
                    name = f'{section}-{label}-{case_index:02d}-{mode}'
                    manifest['ExecutionOrder'].append(name)
                    data, trace = run([host, assemblies[section, label], *arguments], name, trace_runtime=True)
                    record = dict(Section=section, Variant=label, CaseIndex=case_index, Mode=mode, RawStdout=str(raw / (name + '.stdout.txt')),
                                  RawSHA256=hashlib.sha256(data).hexdigest(), Runtime=runtime_evidence(trace))
                    if section == 'cold':
                        observation = json.loads(data); n, history, shape = fixture['ColdCases'][case_index]
                        expected = dict(Mode=mode, Count=n, History=history, Shape=shape, Backend='Standalone Mono default' if args.mono else '.NET',
                                        CodePreparation='None; first Apply/Union/Copy in a fresh process; setup AddTag/RemoveTag/EnsureCapacity only')
                        if any(observation.get(k) != v for k, v in expected.items()):
                            raise RuntimeError('Cold fixture identity/provenance mismatch: ' + name)
                        record['Observation'] = observation
                    else:
                        record['Rows'] = structure_rows(data, program, section)
                        record['Cases'] = program['Cases']; record['StagesPerCase'] = 5
                    preserve_report(record)
                print(f'{section} {label}: {len(cases)} fixture processes passed', flush=True)
        comparisons, flags = compare_reports(reports, variants, bool(args.mono))
        (out / 'comparison.json').write_text(json.dumps(comparisons, indent=2) + '\n')
        manifest.update(ComparisonFlags=flags, ExpectedFixtureProcesses=26 * len(variants))
        if len(reports) != 26 * len(variants):
            raise RuntimeError('Unexpected fixed fixture process count')
        if flags:
            raise RuntimeError('Review flags; inspect preserved comparison.json and raw evidence')
    except BaseException as error:
        failure = error
        manifest.update(Status='failed', FailureType=type(error).__name__, Failure=str(error))
        (out / 'failure.txt').write_text(traceback.format_exc()); raise
    finally:
        issues = []
        checks = [('FinalSourceAndPackageHashes', inputs, initial),
                  ('FinalAssemblyHashes', lambda: [p for t in targets for p in t.rglob('*') if p.is_file()], binaries),
                  ('FinalHostHashes', lambda: [sdk, host], manifest['HostHashes']),
                  ('FinalObservedNativeHostHashes', lambda: [Path(p) for p in native_hashes], native_hashes),
                  ('FinalObservedCoreCLRHashes', lambda: [Path(p) for p in runtime_hashes], runtime_hashes)]
        for key, files, expected in checks:
            try:
                manifest[key] = digest(files())
                if manifest[key] != expected:
                    issues.append(key + ' changed')
            except Exception as error:
                issues.append(key + ': ' + repr(error))
        manifest.update(ImmutabilityViolations=issues, FinishedUTC=stamp(), RawFileHashes=digest([p for p in raw.iterdir() if p.is_file()]))
        if issues:
            manifest.update(Status='failed', Validation='Evidence invalid: ' + '; '.join(issues))
        elif failure is None:
            manifest.update(Status='completed', Validation='Frozen structure/guard/cold probes passed with unchanged source/package/binary/host hashes; no timing or stable retained-heap claim')
        else:
            manifest['Validation'] = 'Failure retained with raw output and hashes; not a passing gate'
        save()
        if issues and failure is None:
            raise RuntimeError('; '.join(issues))
    print('Saved same-storage evidence: ' + str(out))


if __name__ == '__main__':
    main()
