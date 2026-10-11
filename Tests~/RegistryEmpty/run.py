#!/usr/bin/env python3
"""Frozen empty-registry allocation/graph diagnostics; no elapsed-time measurement."""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys

HERE = Path(__file__).resolve().parent
DRIVERS = {'public': 'PublicColdGraphProgram.cs', 'small': 'SmallColdProgram.cs', 'forest': 'ColdAllocationProgram.cs'}
SAMPLES = 6
OVERRIDES = {'DOTNET_TieredCompilation': '0', 'COMPlus_TieredCompilation': '0', 'DOTNET_gcServer': '0', 'COMPlus_gcServer': '0'}
ENV_KEYS = ('PATH', 'LANG', 'LC_ALL', 'TZ', 'DOTNET_ROOT', 'DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'MONO_PATH',
            'MONO_CFG_DIR', 'MONO_ENV_OPTIONS', 'MONO_GC_PARAMS', 'MONO_THREADS_SUSPEND', 'LD_LIBRARY_PATH')


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def encoded_hash(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


def write(path, value):
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + '\n')


def utc():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def capture(command, prefix, env=None, context=None):
    result = subprocess.run([str(x) for x in command], capture_output=True, text=True, env=env)
    prefix.with_suffix('.stdout.txt').write_text(result.stdout)
    prefix.with_suffix('.stderr.txt').write_text(result.stderr)
    write(prefix.with_suffix('.process.json'), {'Command': [str(x) for x in command], 'ExitCode': result.returncode,
          'RecordedUTC': utc(), 'Context': context, 'StdoutSHA256': digest(prefix.with_suffix('.stdout.txt')),
          'StderrSHA256': digest(prefix.with_suffix('.stderr.txt'))})
    result.check_returncode()
    return result.stdout


def version_key(path):
    return tuple(int(x) for x in re.findall(r'\d+', path.name))


def latest(path, pattern):
    matches = sorted(path.glob(pattern), key=version_key)
    if not matches:
        raise RuntimeError('Required installed toolchain directory missing: ' + str(path / pattern))
    return matches[-1]


def source_manifest(root, revision):
    files = {str(p.relative_to(root)): digest(p) for p in sorted((root / 'Runtime').rglob('*')) if p.is_file()}
    if not any(name.endswith('.cs') for name in files):
        raise RuntimeError('No Runtime C# sources: ' + str(root))
    data = {'Root': str(root), 'DeclaredRevision': revision, 'FileSHA256': files, 'TreeSHA256': encoded_hash(files),
            'RevisionMeaning': 'Caller-supplied full revision; exact source hashes are authoritative for archives.'}
    top = subprocess.run(['git', '-C', str(root), 'rev-parse', '--show-toplevel'], capture_output=True, text=True)
    if top.returncode == 0 and Path(top.stdout.strip()).resolve() == root:
        head = subprocess.check_output(['git', '-C', str(root), 'rev-parse', 'HEAD'], text=True).strip()
        data['ActualGitHead'] = head
        data['GitStatus'] = subprocess.check_output(['git', '-C', str(root), 'status', '--porcelain'], text=True)
        data['RuntimeStatus'] = subprocess.check_output(['git', '-C', str(root), 'status', '--porcelain', '--', 'Runtime'], text=True)
        if data['RuntimeStatus']:
            raise RuntimeError('Exact source checkout has Runtime changes: ' + str(root))
        data['RuntimeDiffSHA256'] = hashlib.sha256(subprocess.check_output(['git', '-C', str(root), 'diff', 'HEAD', '--', 'Runtime'])).hexdigest()
        if head != revision:
            raise RuntimeError('Declared revision differs from exact source checkout HEAD: ' + str(root))
    else:
        data['ActualGitHead'] = None  # Never infer a source archive's revision from an ancestor repository.
    return data


def compile_one(args, backend, source, driver, output, sdk, runtime, references, packages, frozen):
    output.mkdir(parents=True)
    for package in packages:
        shutil.copy2(package, output / package.name)
    sources = [] if source is None else [p for p in sorted((source / 'Runtime').rglob('*.cs')) if p.name != 'GameObjectGameplayTagContainer.cs']
    sources += [HERE / driver]
    assembly = output / ('Probe.exe' if backend == 'mono' else 'Probe.dll')
    flags = ['-nologo', '-optimize+', '-langversion:9.0', '-nullable:disable', '-deterministic+', '-target:exe', '-out:"' + str(assembly) + '"']
    flags += ['-r:"' + str(p) + '"' for p in references] + ['"' + str(p) + '"' for p in sources]
    response = output / 'compile.rsp'; response.write_text('\n'.join(flags))
    command = [args.dotnet, sdk / 'Roslyn/bincore/csc.dll', '@' + str(response)]
    capture(command, output / 'compile')  # Compile only: no driver or oracle runs here.
    if backend == 'dotnet':
        write(assembly.with_suffix('.runtimeconfig.json'), {'runtimeOptions': {'tfm': 'net8.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': runtime.name}}})
    artifacts = [p for p in output.iterdir() if p.is_file() and p.suffix in ('.dll', '.exe', '.json', '.rsp')]
    for path in sources + references + artifacts:
        frozen[str(path)] = digest(path)
    run = [str(args.mono if backend == 'mono' else args.dotnet), str(assembly)]
    manifest = {'Driver': driver, 'Source': str(source) if source else None, 'Command': run,
                'AssemblySHA256': digest(assembly), 'SourceHashes': {str(p): digest(p) for p in sources},
                'ReferenceHashes': {str(p): digest(p) for p in references}, 'CompileCommand': [str(x) for x in command]}
    write(output / 'manifest.json', manifest)
    return manifest


def compare(records):
    comparisons, graph_comparisons = [], []
    common = ('Workload', 'Runtime', 'CaseId', 'BuildCount', 'DeclarationCount', 'DistinctDeclaredNames', 'RegistryTagCount',
              'FixtureChecksum', 'OutputChecksum', 'FrozenLayoutChecksum', 'HierarchyPathSlots', 'PointerSizeBytes', 'Cohort', 'Oracle', 'TimingMeasured')
    groups = sorted(set((r['Backend'], r['Route'], r['Case']) for r in records))
    for backend, route, case in groups:
        rows = {v: [r['Result'] for r in records if (r['Backend'], r['Route'], r['Case'], r['Variant']) == (backend, route, case, v)] for v in ('before', 'after')}
        expected = 1 if route == 'internal-retained-graph' else SAMPLES
        if any(len(values) != expected for values in rows.values()):
            raise RuntimeError('Missing fixed samples: ' + str((backend, route, case)))
        reference = rows['before'][0]
        for values in rows.values():
            for value in values:
                if any(value.get(k) != reference.get(k) for k in common):
                    raise RuntimeError('Fixture/configuration/oracle mismatch: ' + str((backend, route, case)))
        if route != 'internal-retained-graph':
            for field, phase in [('AllocatedBytes', 'first'), ('SecondAllocatedBytes', 'immediate-repeat')]:
                if field not in reference:
                    continue
                before = [v[field] for v in rows['before']]; after = [v[field] for v in rows['after']]
                comparisons.append({'Backend': backend, 'Route': route, 'Case': case, 'Phase': phase,
                                    'BeforeBytes': before, 'AfterBytes': after, 'PairedBytesSaved': [a-b for a,b in zip(before, after)],
                                    'AllSamplesEqual': before == after, 'BeforeStable': len(set(before)) == 1, 'AfterStable': len(set(after)) == 1,
                                    'FixtureChecksum': reference.get('FixtureChecksum'), 'OutputChecksum': reference.get('OutputChecksum'),
                                    'RepeatedPhaseAvailable': 'SecondAllocatedBytes' in reference})
        if 'Graph' in reference:
            before = [v['Graph'] for v in rows['before']]; after = [v['Graph'] for v in rows['after']]
            graph_comparisons.append({'Backend': backend, 'Route': route, 'Case': case, 'Cohort': reference.get('Cohort', 1),
                                      'Before': before[0], 'After': after[0], 'BeforeStable': all(v == before[0] for v in before),
                                      'AfterStable': all(v == after[0] for v in after), 'AllGraphsEqual': before == after})
    return {'DiagnosticOnly': True, 'TimingAcceptanceEligible': False, 'SamplesPerAllocationCase': SAMPLES,
            'AllocationComparisons': comparisons, 'RetainedGraphComparisons': graph_comparisons,
            'RepeatedRouteBoundary': 'Only the frozen small internal driver measures immediate repeats (empty and one-root). Public Initialize and forest are first-build-only. No missing repeat is represented as zero.',
            'GraphBoundary': 'Existing frozen typed-array calibration only; no new calibration. Output arrays/dictionary identities and existing caches, not whole-GC retained heap.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('before', 'after', 'dotnet', 'out'):
        parser.add_argument('--' + name, type=Path, required=True)
    for name in ('before-revision', 'after-revision'):
        parser.add_argument('--' + name, required=True)
    parser.add_argument('--mono', type=Path, help='also compile/run on this standalone Mono; .NET remains mandatory')
    parser.add_argument('--nuget', type=Path, default=Path(os.environ.get('NUGET_PACKAGES', '~/.nuget/packages')).expanduser())
    parser.add_argument('--compile-only', action='store_true', help='compile every backend/version/driver; do not run probes or measurements')
    args = parser.parse_args()
    for value in (args.before_revision, args.after_revision):
        if not re.fullmatch('[0-9a-f]{40}', value):
            parser.error('Revisions must be full lowercase 40-character hexadecimal SHAs')
    for name in ('before', 'after', 'dotnet', 'nuget', 'out'):
        setattr(args, name, getattr(args, name).resolve())
    args.mono = args.mono.resolve() if args.mono else None
    if args.out.exists() and any(args.out.iterdir()):
        parser.error('Output directory must be new or empty')
    args.out.mkdir(parents=True, exist_ok=True)
    fixture_provenance = json.loads((HERE / 'fixture-provenance.json').read_text())
    for filename, info in fixture_provenance.items():
        if digest(HERE / filename) != info['SHA256']:
            raise RuntimeError('Frozen driver changed: ' + filename)
    frozen = {str(p): digest(p) for p in sorted(HERE.iterdir()) if p.is_file()}
    roots = {'before': args.before, 'after': args.after}
    revisions = {'before': args.before_revision, 'after': args.after_revision}
    source_info = {label: source_manifest(root, revisions[label]) for label, root in roots.items()}
    for root in roots.values():
        frozen.update({str(p): digest(p) for p in sorted((root / 'Runtime').rglob('*')) if p.is_file()})
    env = dict(os.environ, **OVERRIDES)
    environment = {'Platform': platform.platform(), 'Machine': platform.machine(), 'Python': sys.version,
                   'CPUInfo': Path('/proc/cpuinfo').read_text().split('\n\n')[0] if Path('/proc/cpuinfo').exists() else platform.processor(),
                   'Affinity': sorted(os.sched_getaffinity(0)) if hasattr(os, 'sched_getaffinity') else None,
                   'RecordedRuntimeEnvironment': {key: env.get(key) for key in ENV_KEYS + tuple(OVERRIDES)},
                   'Note': 'Only runtime-relevant environment variables are recorded; unrelated CI secrets are not dumped.'}
    sdk = latest(args.dotnet.parent / 'sdk', '8.*')
    runtime = latest(args.dotnet.parent / 'shared/Microsoft.NETCore.App', '8.*')
    pack = latest(args.dotnet.parent / 'packs/Microsoft.NETCore.App.Ref', '8.*')
    frozen[str(args.dotnet)] = digest(args.dotnet)
    frozen[str(sdk / 'Roslyn/bincore/csc.dll')] = digest(sdk / 'Roslyn/bincore/csc.dll')
    environment['DotNetInfo'] = capture([args.dotnet, '--info'], args.out / 'dotnet-info', env)
    if args.mono:
        frozen[str(args.mono)] = digest(args.mono)
        environment['MonoVersion'] = capture([args.mono, '--version'], args.out / 'mono-version', env)
    builds, metadata = {}, {}
    for backend in ('dotnet', 'mono') if args.mono else ('dotnet',):
        packages = []
        if backend == 'mono':
            framework = args.nuget / 'microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2'
            references = [framework / name for name in ('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Runtime.Serialization.dll', 'System.Xml.dll')]
            packages = [args.nuget / name for name in ('system.memory/4.5.5/lib/net461/System.Memory.dll', 'system.buffers/4.5.1/lib/net461/System.Buffers.dll',
                        'system.runtime.compilerservices.unsafe/4.5.3/lib/net461/System.Runtime.CompilerServices.Unsafe.dll', 'system.numerics.vectors/4.5.0/lib/net46/System.Numerics.Vectors.dll')]
            references += packages
        else:
            references = sorted((pack / 'ref/net8.0').glob('*.dll'))
        metadata[backend] = compile_one(args, backend, None, 'RuntimeInfo.cs', args.out / 'build' / backend / 'runtime-info', sdk, runtime, references, packages, frozen)
        for variant, source in roots.items():
            for driver, filename in DRIVERS.items():
                builds[backend, variant, driver] = compile_one(args, backend, source, filename, args.out / 'build' / backend / variant / driver, sdk, runtime, references, packages, frozen)
    write(args.out / 'compilation-complete.json', {'CompletedUTC': utc(), 'AllVersionsAndBackendsCompiledBeforeAnyDriverExecution': True,
          'Sources': source_info, 'AssemblyCount': len(builds), 'RuntimeInfoAssemblies': len(metadata), 'CompileOnly': args.compile_only})
    if args.compile_only:
        write(args.out / 'frozen-inputs.json', frozen)
        write(args.out / 'environment.json', environment)
        print('Compiled all versions/backends; no driver/probe assembly was executed.'); return
    # Metadata reports read actual loaded implementation locations, not reference-assembly substitutes.
    for backend, build in metadata.items():
        output = capture(build['Command'], args.out / (backend + '-runtime-info'), env)
        info = dict(line.split('=', 1) for line in output.splitlines() if '=' in line)
        for key, value in info.items():
            if key.startswith('Assembly.') or key == 'RuntimeExecutable':
                frozen[value] = digest(value)
        if backend == 'dotnet':
            for name in ('libcoreclr.so', 'libclrjit.so', 'libhostpolicy.so', 'coreclr.dll', 'clrjit.dll', 'hostpolicy.dll'):
                path = Path(info['Assembly.Core']).parent / name
                if path.exists():
                    frozen[str(path)] = digest(path)
        environment[backend + 'ActualRuntimeInfo'] = info
    environment_hash = encoded_hash(environment)
    write(args.out / 'environment.json', environment)
    write(args.out / 'frozen-inputs.json', frozen)
    frozen_hash = digest(args.out / 'frozen-inputs.json')
    raw = args.out / 'raw'; raw.mkdir(); records = []
    cases = [('public-initialize', 'public', 'empty', ['public', '0']), ('public-initialize', 'public', 'one-root', ['public', '1']),
             ('public-initialize', 'public', 'forest', ['public', '2']), ('internal-build', 'small', 'empty', ['cold', '0']),
             ('internal-build', 'small', 'one-root', ['cold', '1']), ('internal-build', 'forest', 'forest', ['cold', '0'])]
    for backend in metadata:
        for round_index in range(SAMPLES):
            variants = ('before', 'after') if round_index % 2 == 0 else ('after', 'before')
            for route, driver, case, arguments in cases:
                for variant in variants:
                    build = builds[backend, variant, driver]
                    command = build['Command'] + arguments
                    prefix = raw / f'{backend}-{route}-{case}-{round_index}-{variant}'
                    value = json.loads(capture(command, prefix, env, {'Backend': backend, 'Variant': variant, 'DeclaredRevision': revisions[variant],
                        'AssemblySHA256': build['AssemblySHA256'], 'SourceTreeSHA256': source_info[variant]['TreeSHA256'],
                        'FrozenInputsSHA256': frozen_hash, 'EnvironmentSHA256': environment_hash}))
                    if value.get('Oracle') != 'passed' or value.get('TimingMeasured') is not False:
                        raise RuntimeError('Frozen oracle/timing contract failed: ' + str(prefix))
                    record = {'Backend': backend, 'Variant': variant, 'DeclaredRevision': revisions[variant], 'Route': route, 'Case': case,
                              'Round': round_index, 'AssemblySHA256': build['AssemblySHA256'], 'SourceTreeSHA256': source_info[variant]['TreeSHA256'],
                              'FrozenInputsSHA256': frozen_hash, 'EnvironmentSHA256': environment_hash, 'Command': command, 'Result': value}
                    records.append(record); write(prefix.with_suffix('.json'), record)
        for case, number in [('empty', '0'), ('one-root', '1'), ('forest', '2')]:
            for variant in ('before', 'after'):
                build = builds[backend, variant, 'public']; command = build['Command'] + ['builder-graph', number]
                prefix = raw / f'{backend}-retained-{case}-{variant}'
                value = json.loads(capture(command, prefix, env, {'Backend': backend, 'Variant': variant, 'DeclaredRevision': revisions[variant],
                        'AssemblySHA256': build['AssemblySHA256'], 'SourceTreeSHA256': source_info[variant]['TreeSHA256'],
                        'FrozenInputsSHA256': frozen_hash, 'EnvironmentSHA256': environment_hash}))
                if value.get('Oracle') != 'passed' or value.get('TimingMeasured') is not False:
                    raise RuntimeError('Graph oracle/timing contract failed: ' + str(prefix))
                record = {'Backend': backend, 'Variant': variant, 'DeclaredRevision': revisions[variant], 'Route': 'internal-retained-graph',
                          'Case': case, 'Round': 0, 'AssemblySHA256': build['AssemblySHA256'], 'SourceTreeSHA256': source_info[variant]['TreeSHA256'],
                          'FrozenInputsSHA256': frozen_hash, 'EnvironmentSHA256': environment_hash, 'Command': command, 'Result': value}
                records.append(record); write(prefix.with_suffix('.json'), record)
        print(backend + ': completed fixed allocation and graph processes', flush=True)
    if any(digest(Path(name)) != value for name, value in frozen.items()):
        raise RuntimeError('A source, harness, assembly, reference or runtime implementation changed; discard run')
    write(args.out / 'results.json', records)
    summary = compare(records); summary.update({'ImmutableInputsVerified': True, 'EnvironmentSHA256': environment_hash,
          'FrozenInputsSHA256': frozen_hash, 'FreshProcessCount': len(records), 'Sources': source_info})
    write(args.out / 'summary.json', summary)
    print('PASS fixed oracles/provenance. Review byte/graph deltas; this is not timing acceptance.')


if __name__ == '__main__':
    main()
