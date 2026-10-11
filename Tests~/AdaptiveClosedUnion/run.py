#!/usr/bin/env python3
"""Portable bounded closed-union correctness coordinator. Never runs timings."""
import argparse
from datetime import datetime, timezone
import gzip
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys
import time
import traceback

HERE = Path(__file__).resolve().parent
HARNESSES = {'pending-barrier': 'PublicCounterexample.cs', 'stale-public': 'StalePublic.cs',
             'continuation-public': 'ContinuationPublic.cs', 'full-state': 'ClosedState.cs',
             'gate': 'GateCases.cs'}


def stamp():
    return datetime.now(timezone.utc).isoformat()


def sha(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(chunk)
    return h.hexdigest()


def digest(paths):
    return {str(p): sha(p) for p in sorted(set(paths))}


def tree(root):
    return {p.relative_to(root).as_posix(): sha(p) for p in sorted(root.rglob('*')) if p.is_file()}


def pairs(labels):
    result = [(labels[0], label) for label in labels[1:]]
    return list(dict.fromkeys(result + list(zip(labels, labels[1:]))))


def same_bytes(left, right):
    with left.open('rb') as a, right.open('rb') as b:
        while True:
            aa, bb = a.read(1024 * 1024), b.read(1024 * 1024)
            if aa != bb:
                return False
            if not aa:
                return True


def compress_raw(raw, manifest, save):
    """Persist each verified lossless mapping before removing its raw duplicate."""
    manifest.update(Status='compressing', Compression=[])
    save()
    try:
        for path in sorted(raw.iterdir()):
            if not path.is_file():
                continue
            compressed = path.with_suffix(path.suffix + '.gz')
            with path.open('rb') as source, compressed.open('wb') as target:
                with gzip.GzipFile(filename='', fileobj=target, mode='wb', mtime=0) as zipped:
                    shutil.copyfileobj(source, zipped)
            h = hashlib.sha256()
            with gzip.open(compressed, 'rb') as stream:
                for chunk in iter(lambda: stream.read(1024 * 1024), b''):
                    h.update(chunk)
            if h.hexdigest() != manifest['RawFileHashes'][str(path)]:
                raise RuntimeError('gzip round-trip hash mismatch: ' + str(path))
            manifest['Compression'].append(dict(Original=str(path), OriginalSHA256=h.hexdigest(), OriginalBytes=path.stat().st_size,
                                                Gzip=str(compressed), GzipSHA256=sha(compressed), RoundTripVerified=True))
            save()  # The mapping is durable before the uncompressed file is removed.
            path.unlink()
        manifest.update(Status='completed', FinishedUTC=stamp())
        save()
    except BaseException as error:
        manifest.update(Status='failed', Failure='Compression failed: ' + repr(error), FinishedUTC=stamp())
        (raw.parent / 'failure.txt').write_text(traceback.format_exc())
        save()
        raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--variant', action='append', required=True, help='LABEL=SOURCE with Runtime/; first is original factory+Copy oracle')
    parser.add_argument('--revision', action='append', required=True, help='LABEL=40hex; exact commit, or base commit with --source-patch')
    parser.add_argument('--source-patch', action='append', default=[], help='LABEL=PATH; explicitly verified base-plus-patch preparation snapshot')
    parser.add_argument('--api-variant', action='append', default=[], help='REUSABLE_UNION only for these full-state variants')
    parser.add_argument('--gate-variant', action='append', required=True, help='Candidate labels with production gate; runs GateCases and admission')
    parser.add_argument('--git-repository', type=Path, help='Optional shared repository containing revisions, for detached Runtime snapshots')
    parser.add_argument('--dotnet', type=Path, required=True, help='Installed .NET SDK host')
    parser.add_argument('--mono', type=Path, help='Optional Mono executable; adds Mono to .NET, not a replacement')
    parser.add_argument('--out', type=Path, required=True, help='Fresh evidence directory outside source package')
    parser.add_argument('--gzip', action='store_true', help='After success, losslessly compress raw streams, verify round trip, then remove uncompressed copies')
    args = parser.parse_args()

    def labels(items, kind, known=None):
        result = {}
        for item in items:
            label, sep, value = item.partition('=')
            if not sep or not value or not re.fullmatch(r'[A-Za-z0-9_-]+', label) or label in result or (known is not None and label not in known):
                parser.error(kind + ' needs unique known LABEL=VALUE')
            result[label] = value
        return result

    variants = {k: Path(v).resolve() for k, v in labels(args.variant, 'Variant').items()}
    revisions = labels(args.revision, 'Revision', variants)
    patches = {k: Path(v).resolve() for k, v in labels(args.source_patch, 'Source patch', variants).items()}
    if len(variants) < 2 or set(revisions) != set(variants) or any(not re.fullmatch('[a-fA-F0-9]{40}', v) for v in revisions.values()):
        parser.error('At least two variants and exactly one 40-hex revision per variant are required')
    revisions = {k: v.lower() for k, v in revisions.items()}
    api, gate = set(args.api_variant), set(args.gate_variant)
    if len(api) != len(args.api_variant) or len(gate) != len(args.gate_variant) or (api | gate) - variants.keys() or next(iter(variants)) in (api | gate):
        parser.error('API/gate labels must be unique known non-original variants')
    for source in variants.values():
        if not (source / 'Runtime').is_dir() or not list((source / 'Runtime').rglob('*.cs')):
            parser.error('Every source must contain Runtime C# files')
    if any(not p.is_file() for p in patches.values()):
        parser.error('Every source patch must exist')
    sdk = args.dotnet.resolve()
    mono = args.mono.resolve() if args.mono else None
    if not sdk.is_file() or (mono and not mono.is_file()):
        parser.error('Supply existing .NET SDK and optional Mono executable paths')
    out = args.out.resolve()
    if out == HERE or HERE in out.parents or any(out == p / 'Runtime' or p / 'Runtime' in out.parents for p in variants.values()):
        parser.error('Output must be outside the package and all Runtime trees')
    if out.exists() and (not out.is_dir() or any(out.iterdir())):
        parser.error('Output must be new or empty')
    frozen = json.loads((HERE / 'provenance.json').read_text())
    for name, info in frozen['Harnesses'].items():
        if sha(HERE / name) != info['SHA256']:
            parser.error('Frozen harness changed: ' + name)
    out.mkdir(parents=True, exist_ok=True)
    raw = out / 'raw'; raw.mkdir()
    # An empty feed forbids package downloads. Provision/cache the pinned packages
    # before invoking this driver. No install or security setting changes occur.
    config = out / 'offline-nuget.config'
    config.write_text('<configuration><packageSources><clear /></packageSources></configuration>\n')
    env = os.environ.copy()
    env.update(DOTNET_CLI_USE_MSBUILD_SERVER='0', MSBUILDDISABLENODEREUSE='1',
               DOTNET_TieredCompilation='0', COMPlus_TieredCompilation='0',
               DOTNET_gcServer='0', COMPlus_gcServer='0', DOTNET_CLI_TELEMETRY_OPTOUT='1')

    def input_files():
        return ([p for p in HERE.rglob('*') if p.is_file() and '__pycache__' not in p.parts]
                + [p for source in variants.values() for p in (source / 'Runtime').rglob('*') if p.is_file()]
                + list(patches.values()))

    initial = digest(input_files())
    manifest = dict(Schema=1, Status='prepared', StartedUTC=stamp(), CommandLine=sys.argv,
                    Sources={k: str(v) for k, v in variants.items()}, Revisions=revisions,
                    ApiVariants=sorted(api), GateVariants=sorted(gate), Platform=platform.platform(),
                    Python=sys.version, InputHashesBefore=initial, SourceProvenance={}, Commands=[],
                    CompletedRuns=[], Comparisons=[], Builds=[], TimingPerformed=False,
                    HostHashes=digest([sdk] + ([mono] if mono else [])))
    safe_names = ('DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_ROOT_X86', 'DOTNET_ROLL_FORWARD',
                  'DOTNET_MULTILEVEL_LOOKUP', 'DOTNET_TieredCompilation', 'COMPlus_TieredCompilation',
                  'DOTNET_gcServer', 'COMPlus_gcServer', 'MONO_PATH', 'MONO_CFG_DIR', 'MONO_ENV_OPTIONS',
                  'MONO_GC_PARAMS', 'LD_LIBRARY_PATH', 'NUGET_PACKAGES')
    manifest['EffectiveSettings'] = {k: env[k] for k in safe_names if k in env}
    manifest['OtherRuntimeEnvironmentNames'] = sorted(k for k in env if k.startswith(('DOTNET_', 'COMPlus_', 'MONO_', 'COREHOST_', 'CORECLR_', 'COR_')) and k not in safe_names)

    def save():
        path = out / 'environment.json.tmp'
        path.write_text(json.dumps(manifest, indent=2) + '\n')
        path.replace(out / 'environment.json')

    save()
    (out / 'preliminary-environment.json').write_text(json.dumps(manifest, indent=2) + '\n')
    runtime_hashes, compiler_reference_hashes, binaries = {}, {}, {}
    targets, runtime_dirs = [], set()
    observed_mono_hashes = {}
    failure = None

    def run(command, name, cwd=out, trace=False):
        stdout, stderr = raw / (name + '.stdout.txt'), raw / (name + '.stderr.txt')
        tracefile = raw / (name + '.hosttrace.txt')
        childenv = env.copy()
        if trace:
            childenv.update(COREHOST_TRACE='1', COREHOST_TRACE_VERBOSITY='4', COREHOST_TRACEFILE=str(tracefile))
        record = dict(Name=name, Command=list(map(str, command)), Cwd=str(cwd), StartedUTC=stamp(), Status='running', Stdout=str(stdout), Stderr=str(stderr))
        manifest['Commands'].append(record); save()
        try:
            with stdout.open('wb') as sout, stderr.open('wb') as serr:
                with subprocess.Popen(record['Command'], cwd=cwd, env=childenv, stdout=sout, stderr=serr) as child:
                    deadline = time.monotonic() + 600
                    mapped = set()
                    while child.poll() is None:
                        # Read-only native process provenance. No fixture source,
                        # output, managed instrumentation or workload is changed.
                        if mono and str(command[0]) == str(mono):
                            proc = Path('/proc') / str(child.pid)
                            try:
                                engine = (proc / 'exe').resolve(strict=True)
                                if 'mono' in engine.name.lower():
                                    mapped.add(engine)
                                for row in (proc / 'maps').read_text().splitlines():
                                    parts = row.split(None, 5)
                                    if len(parts) == 6 and parts[5].startswith('/'):
                                        path = Path(parts[5])
                                        if path.is_file() and (path.suffix.lower() in ('.dll', '.exe') or (path.name.startswith('libmono') and '.so' in path.name)):
                                            mapped.add(path.resolve())
                            except (OSError, ProcessLookupError):
                                pass
                        if time.monotonic() >= deadline:
                            child.kill(); child.wait()
                            raise subprocess.TimeoutExpired(record['Command'], 600)
                        time.sleep(0.01)
                    result = subprocess.CompletedProcess(record['Command'], child.returncode)
                    if mapped:
                        actual = digest(mapped)
                        for path, checksum in actual.items():
                            if path in observed_mono_hashes and observed_mono_hashes[path] != checksum:
                                raise RuntimeError('Observed Mono runtime file changed: ' + path)
                        observed_mono_hashes.update(actual)
                        record['ObservedMonoRuntimeHashes'] = actual
            record.update(Exit=result.returncode, Status='completed' if result.returncode == 0 else 'failed')
            result.check_returncode()
            if trace:
                paths = set(re.findall(r"CoreCLR path = '([^']+)'", tracefile.read_text(errors='replace')))
                if len(paths) != 1:
                    raise RuntimeError('Could not identify exactly one actual CoreCLR: ' + name)
                paths = {Path(p).resolve() for p in paths}
                dirs = {p.parent for p in paths}
                if runtime_dirs and dirs != runtime_dirs:
                    raise RuntimeError('CoreCLR selection changed between fixture processes')
                runtime_dirs.update(dirs)
                if not runtime_hashes:
                    runtime_hashes.update(digest([p for d in dirs for p in d.rglob('*') if p.is_file()]))
                record['ActualCoreCLRHashes'] = digest(paths)
            return stdout, stderr
        except BaseException as error:
            record.update(Status='failed', Error=repr(error)); raise
        finally:
            record.update(FinishedUTC=stamp(), RawHashes=digest([p for p in (stdout, stderr, tracefile) if p.is_file()]))
            save()

    def text_run(command, name, cwd=out):
        return run(command, name, cwd)[0].read_text().strip()

    def compare(a, b, scope):
        for stream in ('Stdout', 'Stderr'):
            left, right = Path(a[stream]), Path(b[stream])
            manifest['Comparisons'].append(dict(Scope=scope, Stream=stream, Left=str(left), Right=str(right),
                                                LeftSHA256=sha(left), RightSHA256=sha(right), Equal=same_bytes(left, right)))
        save()

    try:
        manifest['SDKInfo'] = text_run([sdk, '--info'], 'sdk-info')
        manifest['DotnetRuntimes'] = text_run([sdk, '--list-runtimes'], 'dotnet-runtimes')
        sdks = text_run([sdk, '--list-sdks'], 'dotnet-sdks')
        version = text_run([sdk, '--version'], 'sdk-version')
        selected = [Path(base) / ver for ver, base in re.findall(r'^(\S+) \[([^\]]+)\]$', sdks, re.M) if ver == version]
        if len(selected) != 1:
            raise RuntimeError('Cannot resolve actual selected SDK ' + version)
        compiler_reference_hashes.update(digest([p for p in (selected[0] / 'Roslyn' / 'bincore').iterdir() if p.is_file()]))
        if mono:
            manifest['MonoVersion'] = text_run([mono, '--version'], 'mono-version')
            manifest['MonoHostCaveat'] = 'Exact supplied host and version recorded. If this path is a wrapper, its underlying engine hash is not inferred.'

        # Verify all source bytes, not just declarations. Materialize expected
        # revision files only under fresh evidence; never checkout/change input.
        for label, source in variants.items():
            repo = args.git_repository.resolve() if args.git_repository else source
            rev = revisions[label]
            actual = text_run(['git', '-C', repo, 'rev-parse', rev + '^{commit}'], 'revision-' + label)
            if actual != rev:
                raise RuntimeError('Revision is not the specified exact commit: ' + label)
            listing = run(['git', '-C', repo, 'ls-tree', '-rz', rev, '--', 'Runtime'], 'tree-' + label)[0].read_bytes()
            expected = out / 'expected-source' / label; expected.mkdir(parents=True)
            entries = []
            for entry in listing.split(b'\0'):
                if not entry:
                    continue
                meta, rawpath = entry.split(b'\t', 1); mode, kind, blob = meta.decode().split()
                path = rawpath.decode()
                if kind != 'blob' or mode not in ('100644', '100755') or not path.startswith('Runtime/') or '..' in Path(path).parts:
                    raise RuntimeError('Unsupported Runtime tree entry: ' + path)
                dst = expected / path; dst.parent.mkdir(parents=True, exist_ok=True)
                blobout = run(['git', '-C', repo, 'cat-file', 'blob', blob], 'blob-' + label + '-' + str(len(entries)))[0]
                shutil.copyfile(blobout, dst)
                entries.append({'Path': path, 'GitBlob': blob, 'Mode': mode, 'SHA256': sha(dst)})
            patch_hash = None
            if label in patches:
                patch_hash = sha(patches[label])
                private_git = out / 'provenance-git' / (label + '.git')
                run(['git', 'init', '--bare', private_git], 'patch-private-repository-' + label)
                apply = ['git', '--git-dir=' + str(private_git), '--work-tree=' + str(expected), 'apply']
                run(apply + ['--check', patches[label]], 'patch-check-' + label, expected)
                run(apply + [patches[label]], 'patch-apply-' + label, expected)
                if any(not p.relative_to(expected).parts[0] == 'Runtime' for p in expected.rglob('*') if p.is_file()):
                    raise RuntimeError('Preparation patch must modify only Runtime files')
            match = tree(expected / 'Runtime') == tree(source / 'Runtime')
            manifest['SourceProvenance'][label] = dict(Revision=rev, Repository=str(repo),
                Form='verified base-plus-patch snapshot' if label in patches else 'verified exact revision Runtime tree',
                Patch=str(patches[label]) if label in patches else None, PatchSHA256=patch_hash,
                OriginalGitFiles=entries, RuntimeHashes=tree(source / 'Runtime'), MatchesDeclaredSource=match)
            save()
            if not match:
                raise RuntimeError('Runtime bytes differ from declared revision/patch: ' + label)

        assemblies = {}
        for backend in ['dotnet'] + (['mono'] if mono else []):
            for label, source in variants.items():
                for section, harness in HARNESSES.items():
                    if section == 'gate' and label not in gate:
                        continue
                    build = out / 'build' / backend / label / section; build.mkdir(parents=True)
                    obj, target = build / 'obj', build / 'bin'
                    project = build / 'AdaptiveUnionCorrectness.csproj'
                    shutil.copyfile(HERE / project.name, project)
                    resolved = build / 'resolved-inputs.txt'
                    command = [sdk, 'build', project, '-c', 'Release', '--configfile', config,
                               '-p:NuGetAudit=false', '-p:UseSharedCompilation=false',
                               '-p:MonoTarget=' + str(backend == 'mono').lower(),
                               '-p:RuntimeRoot=' + str(source / 'Runtime'), '-p:HarnessFile=' + str(HERE / harness),
                               '-p:InputManifest=' + str(resolved), '-p:BaseIntermediateOutputPath=' + str(obj) + '/',
                               '-p:MSBuildProjectExtensionsPath=' + str(obj) + '/', '-o', target]
                    if section == 'full-state' and label in api:
                        command.append('-p:DefineConstants=REUSABLE_UNION')
                    run(command, 'build-' + backend + '-' + label + '-' + section)
                    targetfiles = [p for p in target.rglob('*') if p.is_file()]
                    targets.append(target); binaries.update(digest(targetfiles))
                    resolved_files = [Path(line.split('|', 1)[1]) for line in resolved.read_text().splitlines() if '|' in line]
                    compiler_reference_hashes.update(digest(resolved_files))
                    assembly = target / ('AdaptiveUnionCorrectness.exe' if backend == 'mono' else 'AdaptiveUnionCorrectness.dll')
                    if not assembly.is_file():
                        raise RuntimeError('Expected compiled assembly missing')
                    assemblies[backend, label, section] = assembly
                    manifest['Builds'].append(dict(Backend=backend, Variant=label, Section=section,
                        Harness=harness, Defines='REUSABLE_UNION' if section == 'full-state' and label in api else '',
                        ResolvedInputHashes=digest(resolved_files), BinaryHashes=digest(targetfiles)))
                    save()

        lookup = {}
        for backend in ['dotnet'] + (['mono'] if mono else []):
            for label in variants:
                for section in HARNESSES:
                    if section == 'gate' and label not in gate:
                        continue
                    modes = ['257', '769'] + (['admission'] if label in gate else []) if section == 'full-state' else ['']
                    for mode in modes:
                        key = section + ('-' + mode if mode else '')
                        stdout, stderr = run([mono if backend == 'mono' else sdk, assemblies[backend, label, section]] + ([mode] if mode else []),
                                             backend + '-' + label + '-' + key, trace=backend == 'dotnet')
                        record = dict(Backend=backend, Variant=label, Fixture=key, Stdout=str(stdout), Stderr=str(stderr),
                                      StdoutSHA256=sha(stdout), StderrSHA256=sha(stderr))
                        if section == 'full-state' and mode != 'admission':
                            counts = frozen['ExpectedCounts']['Depth' + mode]
                            check = stderr.read_text().strip()
                            if any((k + '=' + str(v) + ';') not in check for k, v in counts.items()):
                                raise RuntimeError('Incomplete full-state checks: ' + str(record))
                            with stdout.open('rb') as stream:
                                lines = sum(1 for _ in stream)
                            if lines != counts['Observations']:
                                raise RuntimeError('Wrong full-state observation count')
                            record['Counts'] = counts
                        elif section == 'gate':
                            lines = stdout.read_text().splitlines()
                            if sum(x.startswith('CASE\t') for x in lines) != 208 or lines[-1] != 'SUMMARY\tcases=208\tpassed=208\tmutations=0\tarrayReplacements=0':
                                raise RuntimeError('Gate fixture did not finish all 208 cases')
                        elif mode == 'admission':
                            admission = json.loads(stdout.read_text())
                            if not admission['Admitted'] or not admission['GateRestoredEveryFieldAndArray']:
                                raise RuntimeError('Production-state admission/restoration failed')
                            record['Admission'] = admission
                        elif not stdout.stat().st_size:
                            raise RuntimeError('Empty public regression output')
                        lookup[backend, label, key] = record
                        manifest['CompletedRuns'].append(record); save()
                print(backend + ' ' + label + ': fixed correctness probes passed', flush=True)
            for before, after in pairs(list(variants)):
                for key in ['pending-barrier', 'stale-public', 'continuation-public', 'full-state-257', 'full-state-769']:
                    compare(lookup[backend, before, key], lookup[backend, after, key], backend + ':original/all+adjacent')
            for before, after in pairs([label for label in variants if label in gate]):
                for key in ['gate', 'full-state-admission']:
                    compare(lookup[backend, before, key], lookup[backend, after, key], backend + ':gate-variants')
        if mono:
            for (backend, label, key), record in lookup.items():
                if backend == 'dotnet':
                    compare(record, lookup['mono', label, key], 'cross-runtime')
        expected_runs = (5 * len(variants) + 2 * len(gate)) * (2 if mono else 1)
        if len(lookup) != expected_runs:
            raise RuntimeError('Incomplete fixture execution matrix')
        manifest['ExpectedRuns'] = expected_runs
        if any(not c['Equal'] for c in manifest['Comparisons']):
            raise RuntimeError('Raw stdout/stderr differs; inspect retained comparisons and streams')
    except BaseException as error:
        failure = error
        manifest.update(Status='failed', Failure=repr(error))
        (out / 'failure.txt').write_text(traceback.format_exc())
    finally:
        issues = []
        for key, files, expected in [
            ('InputHashesAfter', input_files, initial),
            ('BinaryHashesAfter', lambda: [p for target in targets for p in target.rglob('*') if p.is_file()], binaries),
            ('HostHashesAfter', lambda: [sdk] + ([mono] if mono else []), manifest['HostHashes']),
            ('ObservedMonoRuntimeHashesAfter', lambda: [Path(p) for p in observed_mono_hashes], observed_mono_hashes),
            ('CompilerReferenceHashesAfter', lambda: [Path(p) for p in compiler_reference_hashes], compiler_reference_hashes),
            ('RuntimeHashesAfter', lambda: [p for d in runtime_dirs for p in d.rglob('*') if p.is_file()], runtime_hashes)]:
            try:
                manifest[key] = digest(files())
                if manifest[key] != expected:
                    issues.append(key + ' changed')
            except Exception as error:
                issues.append(key + ': ' + repr(error))
        manifest.update(ImmutabilityViolations=issues, RuntimeHashesBefore=runtime_hashes,
                        CompilerReferenceHashesBefore=compiler_reference_hashes, BinaryHashesBefore=binaries,
                        ObservedMonoRuntimeHashesBefore=observed_mono_hashes,
                        FinishedUTC=stamp(), Status='failed' if failure or issues else 'completed')
        manifest['RawFileHashes'] = digest([p for p in raw.iterdir() if p.is_file()])
        save()
    if failure or issues:
        raise RuntimeError('Correctness/evidence failed: ' + str(failure or issues))
    if args.gzip:
        compress_raw(raw, manifest, save)
    print('PASS: ' + str(len(manifest['CompletedRuns'])) + ' fixed fixture processes; ' + str(len(manifest['Comparisons'])) + ' exact raw-stream comparisons. Evidence: ' + str(out))


if __name__ == '__main__':
    main()
