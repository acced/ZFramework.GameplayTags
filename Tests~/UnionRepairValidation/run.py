#!/usr/bin/env python3
"""Frozen Union repair semantic CI adapter. No benchmark command is exposed."""
import argparse
import datetime
import gzip
import hashlib
import itertools
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import threading
import tempfile
import time
sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def sha(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for data in iter(lambda: stream.read(1048576), b''):
            h.update(data)
    return h.hexdigest()


def write(path, value):
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    Path(path).write_text(json.dumps(value, indent=2, sort_keys=True) + '\n')


def files(root):
    root = Path(root)
    require(not root.is_symlink(), 'Symlink root forbidden: ' + str(root))
    result = {}
    for p in sorted(root.rglob('*')):
        require(not p.is_symlink(), 'Symlink input forbidden: ' + str(p))
        if p.is_file():
            result[p.relative_to(root).as_posix()] = sha(p)
    return result


def verify_package():
    expected = json.loads((HERE / 'package-manifest.json').read_text())['Files']
    actual = files(HERE)
    actual.pop('package-manifest.json', None)
    require(actual == expected, 'Package file set/hash mismatch')
    return actual


def version(path):
    return tuple(int(x) for x in re.findall(r'\d+', path.name.split('-')[0]))


class Runner:
    def __init__(self, args, plan, package):
        self.args, self.plan, self.package = args, plan, package
        self.out = args.out.resolve()
        require(not self.out.exists(), '--out must be a new path; never overwrite prior evidence')
        for source in [HERE, args.candidate_source.resolve()]:
            require(self.out != source and source not in self.out.parents, 'Output must be outside package/candidate source')
        self.out.mkdir(parents=True)
        self.private_directory = tempfile.TemporaryDirectory(prefix='union-semantic-private-')
        self.work = Path(self.private_directory.name).resolve()
        require(self.work != self.out and self.out not in self.work.parents and self.work not in self.out.parents,
                'Private working directory must be outside published evidence')
        self.started = time.monotonic()
        self.env = os.environ.copy()
        self.explicit_configuration = dict(DOTNET_CLI_USE_MSBUILD_SERVER='0', MSBUILDDISABLENODEREUSE='1',
                        DOTNET_TieredCompilation='0', COMPlus_TieredCompilation='0',
                        DOTNET_gcServer='0', COMPlus_gcServer='0', DOTNET_CLI_TELEMETRY_OPTOUT='1')
        self.env.update(self.explicit_configuration)
        self.backend = 'mono' if args.mono else 'net'
        self.report = {'Status': 'running', 'StartedUTC': datetime.datetime.now(datetime.timezone.utc).isoformat(),
                       'Backend': self.backend, 'Commands': [], 'Runs': [], 'Comparisons': [],
                       'PackageHashesBefore': package, 'PlanSHA256': sha(HERE / 'plan.json'),
                       'ReproductionCLI': ['python3', '${PACKAGE}/run.py', '--dotnet', '${DOTNET}',
                           '--git-repository', '${REPOSITORY}', '--candidate-source', '${CANDIDATE}',
                           '--candidate-snapshot-commit', args.candidate_snapshot_commit, '--out', '${OUTPUT}']
                           + (['--mono', '${MONO}'] if args.mono else []),
                       'TimingBenchmarks': False, 'ColdRevalidation': False,
                       'ExplicitRuntimeConfiguration': self.explicit_configuration,
                       'InheritedEnvironmentValuesPublished': False,
                       'Limits': plan['Limits'], 'HostHashes': {}}
        self.save()
        self.built = {}
        self.inputs = {}
        self.binary_hashes = {}

    def public_identity(self, value):
        """Keep reproducible logical roots without publishing machine-local paths."""
        exact_arguments = [(self.args.git_repository, '${REPOSITORY}'),
                           (self.args.candidate_source, '${CANDIDATE}'),
                           (self.args.dotnet, '${DOTNET}'), (self.args.out, '${OUTPUT}')]
        if self.args.mono:
            exact_arguments.append((self.args.mono, '${MONO}'))
        for path, label in exact_arguments:
            if value == str(path):
                return label
        roots = [(str(HERE), '${PACKAGE}'), (str(self.out), '${OUTPUT}'),
                 (str(self.work), '${WORK}'),
                 (str(self.args.git_repository.resolve()), '${REPOSITORY}'),
                 (str(self.args.candidate_source.resolve()), '${CANDIDATE}'),
                 (str(self.args.dotnet.resolve()), '${DOTNET}'),
                 (str(self.args.dotnet.resolve().parent), '${DOTNET_ROOT}')]
        if self.args.mono:
            roots.append((str(self.args.mono.resolve()), '${MONO}'))
        for root, label in sorted(roots, key=lambda item: len(item[0]), reverse=True):
            value = value.replace(root, label)
        return value

    def public_view(self, value):
        if isinstance(value, dict):
            return {self.public_identity(str(k)): self.public_view(v)
                    for k, v in value.items() if not str(k).startswith('_')}
        if isinstance(value, (list, tuple)):
            return [self.public_view(v) for v in value]
        if isinstance(value, (str, Path)):
            return self.public_identity(str(value))
        return value

    def save(self):
        write(self.out / 'report.json', self.public_view(self.report))

    def stream_path(self, entry):
        return Path(entry['_PrivatePath']) if '_PrivatePath' in entry else self.out / entry['Path']

    def remaining(self, timeout):
        left = self.plan['TotalTimeoutSeconds'] - (time.monotonic() - self.started)
        require(left > 0, 'Overall bounded semantic deadline reached')
        return min(timeout, left)

    def command(self, name, command, timeout, cwd=None):
        """Drain both pipes concurrently and enforce the deadline from process start."""
        command = list(map(str, command))
        # No command output is public until its fixed semantic protocol is validated.
        public_stream = False
        directory = self.work / 'logs' / name
        directory.mkdir(parents=True, exist_ok=False)
        record = {'Name': name, 'Command': command, 'WorkingDirectory': str(cwd) if cwd else None,
                  'StartedUTC': datetime.datetime.now(datetime.timezone.utc).isoformat(),
                  'DeadlineSeconds': self.remaining(timeout), 'ExitCode': None, 'TimedOut': False}
        self.report['Commands'].append(record)
        self.save()
        process = None
        errors = []
        collectors = []
        def drain(stream, label):
            path = directory / (label + '.gz')
            h, count = hashlib.sha256(), 0
            try:
                with path.open('xb') as raw, gzip.GzipFile(filename='', fileobj=raw, mode='wb', compresslevel=1, mtime=0) as z:
                    for data in iter(lambda: stream.read(1048576), b''):
                        h.update(data); count += len(data); z.write(data)
                record[label] = {'Bytes': count, 'SHA256': h.hexdigest(), 'GzipSHA256': sha(path),
                                 'PublishedRawSemanticStream': public_stream}
                if public_stream:
                    record[label]['Path'] = str(path.relative_to(self.out))
                else:
                    record[label]['_PrivatePath'] = str(path)
            except BaseException as error:
                errors.append({'Stream': label, 'ErrorType': type(error).__name__})
            finally:
                stream.close()
        try:
            process = subprocess.Popen(command, cwd=cwd, env=self.env, stdout=subprocess.PIPE,
                                       stderr=subprocess.PIPE, start_new_session=True)
            for stream, label in [(process.stdout, 'Stdout'), (process.stderr, 'Stderr')]:
                t = threading.Thread(target=drain, args=(stream, label), daemon=True)
                collectors.append(t); t.start()
            try:
                record['ExitCode'] = process.wait(timeout=record['DeadlineSeconds'])
            except subprocess.TimeoutExpired:
                record['TimedOut'] = True
                os.killpg(process.pid, signal.SIGKILL)
                record['ExitCode'] = process.wait(timeout=30)
            for t in collectors:
                t.join(timeout=30)
            require(not any(t.is_alive() for t in collectors), 'Output drainer did not terminate')
            require(not errors, 'Output persistence failed: ' + repr(errors))
            require(record['ExitCode'] == 0 and not record['TimedOut'], 'Command failed or timed out: ' + name)
        finally:
            if process is not None and (process.poll() is None or any(t.is_alive() for t in collectors)):
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                if process.poll() is None:
                    process.wait(timeout=30)
                for t in collectors:
                    t.join(timeout=30)
            record['FinishedUTC'] = datetime.datetime.now(datetime.timezone.utc).isoformat()
            if errors: record['PersistenceErrors'] = errors
            self.save()
        return record

    def text(self, command, label='Stdout'):
        entry = command[label]
        require(entry['Bytes'] <= 32 * 1024 * 1024, 'Refusing to load large output into memory')
        with gzip.open(self.stream_path(entry), 'rt', encoding='utf-8') as f:
            return f.read()

    def git(self, *arguments):
        key = 'git-' + str(len(self.report['Commands']))
        return self.command(key, ['git', '-C', self.args.git_repository, *arguments], 120)

    def extract(self, commit, prefix, target, expected):
        """Extract exact tracked blobs, rejecting symlinks/submodules and path traversal."""
        listing = self.text(self.git('ls-tree', '-rz', commit, '--', prefix))
        entries = {}
        for row in listing.split('\0'):
            if not row: continue
            header, name = row.split('\t', 1)
            mode, kind, oid = header.split()
            require(mode in ('100644', '100755') and kind == 'blob', 'Only regular Git blobs permitted')
            path = Path(name)
            require(not path.is_absolute() and '..' not in path.parts, 'Unsafe Git path')
            require(name.startswith(prefix.rstrip('/') + '/'), 'Git prefix mismatch')
            rel = name[len(prefix.rstrip('/')) + 1:]
            entries[rel] = oid
        require(set(entries) == set(expected), 'Tracked source file set differs: ' + prefix)
        target.mkdir(parents=True, exist_ok=False)
        for rel, oid in entries.items():
            record = self.git('cat-file', 'blob', oid)
            dest = target / rel
            dest.parent.mkdir(parents=True, exist_ok=True)
            with gzip.open(self.stream_path(record['Stdout']), 'rb') as src, dest.open('xb') as dst:
                shutil.copyfileobj(src, dst)
            require(sha(dest) == expected[rel], 'Git source hash differs: ' + rel)
        return entries

    def prepare_sources(self):
        repo = Path(self.text(self.git('rev-parse', '--show-toplevel')).strip()).resolve()
        require(repo == self.args.git_repository.resolve(), '--git-repository must be its worktree root')
        candidate = self.args.candidate_source.resolve()
        require(candidate != repo and repo in candidate.parents, 'Candidate must be a separate snapshot directory inside the Git worktree')
        require(candidate != repo / 'Runtime', 'Root Runtime is not the candidate package snapshot')
        snapshot = self.args.candidate_snapshot_commit
        require(re.fullmatch('[0-9a-fA-F]{40}', snapshot) is not None, 'Candidate snapshot commit must be an exact 40-hex commit')
        resolved = self.text(self.git('rev-parse', '--verify', snapshot + '^{commit}')).strip()
        require(resolved.lower() == snapshot.lower(), 'Candidate commit mismatch')
        pins = json.loads((HERE / 'provenance/source-provenance.json').read_text())['Sources']
        self.sources = {}
        source_log = {'CandidateSnapshotCommit': resolved, 'CandidateSnapshotPath': candidate.relative_to(repo).as_posix(),
                      'RootRuntimeUsedAsCandidate': False, 'Sources': {}}
        for label in ['original', 'previous', 'candidate']:
            expected = {k.removeprefix('Runtime/'): v for k, v in pins[label].items()}
            require(len(expected) == len(pins[label]) and all(k.startswith('Runtime/') for k in pins[label]), 'Invalid pinned source manifest')
            commit = resolved if label == 'candidate' else self.plan['PackageSourcePins'][label]
            prefix = candidate.relative_to(repo).as_posix() + '/Runtime' if label == 'candidate' else 'Runtime'
            target = self.work / 'sources' / label / 'Runtime'
            blobs = self.extract(commit, prefix, target, expected)
            if label == 'candidate':
                require(files(candidate / 'Runtime') == expected, 'Working candidate differs from pinned final Runtime')
            self.sources[label] = target.parent
            source_log['Sources'][label] = {'Commit': commit, 'GitPrefix': prefix, 'BlobIDs': blobs,
                                            'Hashes': files(target), 'FrozenExact': True}
        self.report['SourceProvenance'] = source_log
        self.source_hashes = {k: files(v / 'Runtime') for k, v in self.sources.items()}
        self.candidate_hashes = files(candidate / 'Runtime')
        self.save()

    def toolchain(self):
        self.dotnet = self.args.dotnet.resolve()
        require(self.dotnet.is_file(), 'Missing dotnet host')
        self.report['HostHashes'][str(self.dotnet)] = sha(self.dotnet)
        self.command('dotnet-info', [self.dotnet, '--info'], 60)
        sdk = sorted((p for p in (self.dotnet.parent / 'sdk').glob('*') if p.is_dir() and version(p) >= (8,)), key=version)
        packs = sorted((self.dotnet.parent / 'packs/Microsoft.NETCore.App.Ref').glob('8.*'), key=version)
        runtime = sorted((self.dotnet.parent / 'shared/Microsoft.NETCore.App').glob('8.*'), key=version)
        require(sdk and packs and runtime, '.NET SDK >=8 plus .NET 8 runtime/reference pack required next to resolved host')
        self.csc = sdk[-1] / 'Roslyn/bincore/csc.dll'
        self.netrefs = sorted((packs[-1] / 'ref/net8.0').glob('*.dll'))
        self.standardrefs = sorted((self.dotnet.parent / 'packs/NETStandard.Library.Ref/2.1.0/ref/netstandard2.1').glob('*.dll'))
        require(self.netrefs and self.standardrefs and self.csc.is_file(), 'Missing compiler/reference pack')
        self.runtime = runtime[-1].name
        self.generator_api = self.dotnet.parent / 'sdk/8.0.425/Roslyn/bincore'
        require(all((self.generator_api / n).is_file() for n in ['Microsoft.CodeAnalysis.dll', 'Microsoft.CodeAnalysis.CSharp.dll']),
                'SDK 8.0.425 Roslyn API assemblies required for the unchanged net8 generator fixture')
        self.add_inputs([self.csc, self.dotnet, *self.netrefs, *self.standardrefs,
                         *[self.csc.parent / n for n in ['csc.deps.json', 'csc.runtimeconfig.json', 'Microsoft.CodeAnalysis.dll', 'Microsoft.CodeAnalysis.CSharp.dll']],
                         *[runtime[-1] / n for n in ['libcoreclr.so', 'libhostpolicy.so', 'System.Private.CoreLib.dll']]])
        self.extra_mono = []
        if self.args.mono:
            self.args.mono = self.args.mono.resolve()
            require(self.args.mono.is_file(), 'Missing Mono executable')
            self.report['HostHashes'][str(self.args.mono)] = sha(self.args.mono)
            mono_version = self.text(self.command('mono-version', [self.args.mono, '--version'], 60))
            match = re.search(r'Mono JIT compiler version ([0-9.]+)', mono_version)
            self.report['MonoVersion'] = match.group(1) if match else 'unrecognized version format'
            deps = self.work / 'dependencies'; deps.mkdir()
            for name in ['Dependencies.csproj', 'NuGet.Config']:
                shutil.copyfile(HERE / name, deps / name)
            packages = deps / 'packages'
            self.command('restore-pinned-mono-references', [self.dotnet, 'restore', deps / 'Dependencies.csproj',
                         '--configfile', deps / 'NuGet.Config', '--packages', packages, '-p:NuGetAudit=false',
                         '-p:RestoreIgnoreFailedSources=false', '-p:BaseIntermediateOutputPath=' + str(deps / 'obj') + '/'], 300)
            self.monorefs = [packages / 'microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2' / x
                            for x in ['mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Runtime.Serialization.dll', 'System.Xml.dll']]
            self.extra_mono = [packages / x for x in ['system.memory/4.5.5/lib/net461/System.Memory.dll',
                'system.buffers/4.5.1/lib/net461/System.Buffers.dll',
                'system.runtime.compilerservices.unsafe/4.5.3/lib/net461/System.Runtime.CompilerServices.Unsafe.dll',
                'system.numerics.vectors/4.5.0/lib/net46/System.Numerics.Vectors.dll']]
            require(all(p.is_file() for p in self.monorefs + self.extra_mono), 'Pinned Mono dependency missing')
            self.add_inputs(self.monorefs + self.extra_mono)
        self.report['Toolchain'] = {'SDK': sdk[-1].name, 'DotnetRuntime': self.runtime,
                                  'ReferencePack': packs[-1].name, 'MonoDefaultFlags': [],
                                  'GeneratorAPISDK': '8.0.425',
                                  'GeneratorAPIHashes': {n: sha(self.generator_api / n) for n in ['Microsoft.CodeAnalysis.dll', 'Microsoft.CodeAnalysis.CSharp.dll']},
                                  'RecordedLocalReference': '.NET 8.0.31 / Mono 6.12.0.199; actual CI host reported above',
                                  'ReferenceHashes': self.inputs}
        self.save()

    def add_inputs(self, paths):
        for p in paths:
            self.inputs[str(p)] = sha(p)

    def build(self, name, variant, programs, defines=(), backend=None, portable=False,
              executable=True, references=(), include_unity=False, without_runtime=False):
        backend = backend or self.backend
        cache = (variant, tuple(programs), tuple(defines), backend, portable, executable, tuple(map(str, references)), include_unity, without_runtime)
        if cache in self.built:
            return self.built[cache]
        directory = self.work / 'build' / name
        directory.mkdir(parents=True, exist_ok=False)
        assembly = directory / (('Test.exe' if backend == 'mono' else 'Test.dll') if executable else ('GameplayTags.Runtime.dll' if name == 'runtime-netstandard' else 'GameplayTags.UnityCompileCheck.dll'))
        paths = [HERE / p for p in programs]
        if not without_runtime:
            paths = sorted(p for p in (self.sources[variant] / 'Runtime').rglob('*.cs')
                           if include_unity or p.name != 'GameObjectGameplayTagContainer.cs') + paths
        refs = list(self.standardrefs if portable else (self.monorefs + self.extra_mono if backend == 'mono' else self.netrefs)) + list(references)
        flags = ['-nologo', '-optimize+', '-langversion:9.0', '-nullable:disable', '-deterministic+',
                 '-target:exe' if executable else '-target:library', '-out:"' + str(assembly) + '"']
        flags += ['-r:"' + str(p) + '"' for p in refs] + ['"' + str(p) + '"' for p in paths]
        if defines: flags += ['-define:' + ';'.join(defines)]
        response = directory / 'compile.rsp'; response.write_text('\n'.join(flags) + '\n')
        self.command('build-' + name, [self.dotnet, self.csc, '@' + str(response)], self.plan['BuildTimeoutSeconds'])
        for p in (self.extra_mono if backend == 'mono' else []) + list(references):
            require(directory / p.name != assembly, 'Dependency would overwrite target assembly')
            shutil.copyfile(p, directory / p.name)
        if backend != 'mono' and executable:
            write(assembly.with_suffix('.runtimeconfig.json'), {'runtimeOptions': {'tfm': 'net8.0',
                  'framework': {'name': 'Microsoft.NETCore.App', 'version': self.runtime},
                  'configProperties': {'System.Runtime.TieredCompilation': False, 'System.GC.Server': False}}})
        self.add_inputs(paths + refs)
        build = {'Assembly': assembly, 'Command': [self.args.mono if backend == 'mono' else self.dotnet, assembly]}
        write(directory / 'inputs.json', {'Files': {str(p): sha(p) for p in paths + refs},
              'Defines': list(defines), 'AssemblySHA256': sha(assembly), 'ResponseSHA256': sha(response)})
        build['BinaryHashes'] = {str(p): sha(p) for p in directory.iterdir()
                                 if p.is_file() and (p.suffix in ['.dll', '.exe', '.pdb'] or p.name.endswith('.runtimeconfig.json'))}
        self.binary_hashes.update(build['BinaryHashes'])
        write(directory / 'binary-hashes.json', build['BinaryHashes'])
        self.report.setdefault('Builds', []).append({'Name': name, 'Files': {str(p): sha(p) for p in paths + refs},
            'Defines': list(defines), 'AssemblySHA256': sha(assembly), 'BinaryHashes': build['BinaryHashes'],
            'ResponseSHA256': sha(response)})
        self.save()
        self.built[cache] = build
        return build

    def verify_build(self, build):
        for path, value in build['BinaryHashes'].items():
            require(sha(path) == value, 'Emitted target/dependency changed: ' + path)

    def invoke(self, name, build, arguments=(), check=None, kind='semantic'):
        self.verify_build(build)
        result = self.command('run-' + name, build['Command'] + list(arguments), self.plan['ProcessTimeoutSeconds'])
        self.verify_build(build)
        row = {'Name': name, 'Kind': kind, 'CommandName': result['Name'],
               'ExecutedBinaryHashes': build['BinaryHashes'],
               'Stdout': result['Stdout'], 'Stderr': result['Stderr'], 'Passed': False}
        self.report['Runs'].append(row); self.save()
        check = check or {}
        for key, expected in check.get('Expected', {}).items():
            label = key.removesuffix('SHA256')
            require(result[label]['SHA256'] == expected, 'Frozen stream digest differs: ' + name + '/' + label)
        if 'Text' in check:
            require(self.text(result).strip() == check['Text'], 'Incomplete text summary: ' + name)
        if check.get('Counts'):
            err = self.text(result, 'Stderr')
            for field, expected in check['Counts'].items():
                require(field + '=' + str(expected) + ';' in err, 'Incomplete legacy count: ' + field)
            with gzip.open(self.stream_path(result['Stdout']), 'rb') as f:
                require(sum(1 for _ in f) == check['Counts']['Observations'], 'Legacy observation count mismatch')
        row['ExpectedCoverage'] = {k: v for k, v in check.items() if k in ['Cases', 'Observations', 'Checks', 'Counts']}
        row['CoverageVerification'] = 'Frozen full-stream digests and unchanged in-process fixture assertions'
        row['Passed'] = True; self.save()
        return result

    def compare(self, name, a, b):
        # Full uncompressed byte comparisons, not only digest equality.
        for label in ['Stdout', 'Stderr']:
            with gzip.open(self.stream_path(a[label]), 'rb') as aa, gzip.open(self.stream_path(b[label]), 'rb') as bb:
                while True:
                    self.remaining(1)
                    x, y = aa.read(1048576), bb.read(1048576)
                    require(x == y, 'Full stream mismatch: ' + name + '/' + label)
                    if not x: break
        self.report['Comparisons'].append({'Name': name, 'UncompressedBytesEqual': True,
             'StdoutSHA256': a['Stdout']['SHA256'], 'StderrSHA256': a['Stderr']['SHA256']}); self.save()

    def core(self):
        prefix = 'fixtures/core/'
        def run(name, files, variant='candidate', defines=(), args=(), **options):
            built = self.build(name, variant, [prefix + p for p in files], defines, **options)
            return self.invoke(name, built, args, kind='core')
        if self.backend == 'net':
            lib = self.build('runtime-netstandard', 'candidate', [], portable=True, executable=False)
            run('main', ['fixed-harness/Tests~/Program.cs', 'fixed-harness/Tests~/FixtureRegistration.cs'],
                references=[lib['Assembly']], without_runtime=True)
            run('deep-main', ['fixed-harness/Tests~/DeepHierarchy/Program.cs'], references=[lib['Assembly']], without_runtime=True)
            run('registry', ['fixed-harness/Tests~/Registry/Program.cs'])
            roslyn = self.generator_api
            run('generator', ['fixed-harness/SourceGenerator~/GameplayTagsSourceGenerator.cs',
                             'fixed-harness/SourceGenerator~/Tests/GeneratorCheck.cs'],
                without_runtime=True, references=[roslyn / 'Microsoft.CodeAnalysis.dll', roslyn / 'Microsoft.CodeAnalysis.CSharp.dll'])
            self.build('unity-stub-compile', 'candidate', [prefix + 'fixed-harness/Tests~/UnityStubs.cs'],
                       ['UNITY_5_3_OR_NEWER'], portable=True, executable=False, include_unity=True)
        else:
            run('main', ['fixed-harness/Tests~/Program.cs', 'fixed-harness/Tests~/FixtureRegistration.cs'])
        for variant in self.plan['Variants']:
            defines = ['REUSABLE_UNION'] if variant != 'original' else []
            run('focused-' + variant, ['Tests/Program.cs'], variant, defines)
            run('deep-' + variant, ['Tests/DeepProgram.cs'], variant)
            run('evolution-test-' + variant, ['evolution-harness/Tests~/Evolution/Program.cs'], variant, args=['test', '0'])
            run('api-test-' + variant, ['Benchmarks/Program.cs'], variant, defines, args=['test'])

    def matrix(self):
        proxy = {}
        for fixture in self.plan['Runs']:
            results = {}
            for variant in fixture['Labels']:
                name = fixture['Name'] + '-' + variant
                defines = list(fixture['Defines'])
                if fixture['ReusableDefine'] and variant != 'original': defines.append('REUSABLE_UNION')
                if variant == 'candidate' and fixture.get('CandidateExtraDefine'): defines.append(fixture['CandidateExtraDefine'])
                build = self.build(name, variant, [fixture['Fixture']], defines)
                args = list(fixture['Arguments'])
                check = fixture['Check']
                stream = self.work / 'binary-state' / (name + '.gz')
                if fixture['Kind'] == 'binary-state':
                    stream.parent.mkdir(exist_ok=True)
                    args.append(str(stream))
                result = self.invoke(name, build, args, check, fixture['Kind'])
                results[variant] = result
                if fixture['Kind'] == 'binary-state':
                    summary = json.loads(self.text(result))
                    for field, expected in check['BoundarySummary'].items():
                        require(summary.get(field) == expected, 'Boundary count differs: ' + field)
                    h, size = hashlib.sha256(), 0
                    with gzip.open(stream, 'rb') as f:
                        for data in iter(lambda: f.read(1048576), b''):
                            self.remaining(1)
                            h.update(data); size += len(data)
                    require(h.hexdigest() == check['RawStateSHA256'] and size == check['RawStateBytes'], 'Boundary full-state bytes differ from sealed reference')
                    self.report.setdefault('BinaryStates', []).append({'Variant': variant, 'Path': str(stream.relative_to(self.work)),
                        '_PrivatePath': str(stream), 'PublishedRawSemanticStream': False,
                        'GzipSHA256': sha(stream), 'RawSHA256': h.hexdigest(), 'RawBytes': size, 'Summary': summary})
                    self.save()
                if check.get('WarmProxyNoRegression'):
                    values = {}
                    for line in self.text(result).splitlines():
                        m = re.fullmatch(r'(left|right) warm bytes/operation=([0-9.]+)', line)
                        require(m is not None, 'Malformed value-proxy output')
                        values[m[1]] = float(m[2])
                    require(set(values) == {'left', 'right'}, 'Missing proxy orientation')
                    proxy[variant] = values
            if fixture['CompareVariants']:
                for a, b in itertools.combinations(fixture['Labels'], 2): self.compare(fixture['Name'] + ':' + a + ':' + b, results[a], results[b])
        require(all(proxy['candidate'][side] <= proxy['previous'][side] for side in ['left', 'right']), 'Generic value-proxy warm allocation increased')
        self.report['WarmProxy'] = proxy
        states = self.report['BinaryStates']
        for a, b in itertools.combinations(states, 2):
            with gzip.open(self.stream_path(a), 'rb') as aa, gzip.open(self.stream_path(b), 'rb') as bb:
                while True:
                    self.remaining(1)
                    x, y = aa.read(1048576), bb.read(1048576)
                    require(x == y, 'Boundary byte comparison differs')
                    if not x: break
            self.report['Comparisons'].append({'Name': 'large-selected-alias:' + a['Variant'] + ':' + b['Variant'],
                                               'UncompressedBytesEqual': True, 'RawSHA256': a['RawSHA256']})
        self.save()

    def metadata(self):
        reader = self.build('metadata-reader', 'candidate', ['fixtures/metadata/Inspect.cs'], backend='net', without_runtime=True)
        data = {}
        for variant in self.plan['Variants']:
            target = self.build('metadata-' + variant, variant, ['fixtures/metadata/Fixture.cs'])
            self.verify_build(reader); self.verify_build(target)
            record = self.command('inspect-' + variant, reader['Command'] + [target['Assembly']], 120)
            self.verify_build(reader); self.verify_build(target)
            data[variant] = json.loads(self.text(record))
            require(data[variant]['OfflinePEReader'] and not data[variant]['TargetAssemblyLoaded'], 'Metadata reader loaded target')
        def normalized(method):
            x = {k: v for k, v in method.items() if k not in ['Token', 'RVA', 'SignatureRaw']}
            if x['Body'] is not None:
                x['Body'] = {k: v for k, v in x['Body'].items() if k not in ['RawSHA256', 'RawBase64', 'LocalSignatureToken', 'FullBodySHA256']}
            return x
        final = {m['Key']: m for m in data['candidate']['Methods']}
        ledger = []
        for variant in ['original', 'previous']:
            old = data[variant]
            prior_storage = [m for m in old['Methods'] if m['Type'] == 'GameplayTags.TagStorage']
            final_storage = [m for m in data['candidate']['Methods'] if m['Type'] == 'GameplayTags.TagStorage']
            positions = {m['Key']: i for i, m in enumerate(final_storage)}
            for ordinal, method in enumerate(prior_storage):
                current = final[method['Key']]
                require(normalized(method) == normalized(current), 'Preexisting storage body/metadata changed')
                if variant == 'previous':
                    require(method['Token'] == current['Token'] and ordinal == positions[method['Key']], 'f53 storage token/order changed')
                ledger.append({'Reference': variant, 'Method': method['Key'], 'BeforeToken': method['Token'],
                    'FinalToken': current['Token'], 'GlobalTokenDelta': current['Token'] - method['Token'],
                    'BeforeWithinTypeOrdinal': ordinal, 'FinalWithinTypeOrdinal': positions[method['Key']],
                    'NormalizedEqual': True, 'RawBodyEqual': (method['Body'] or {}).get('RawSHA256') == (current['Body'] or {}).get('RawSHA256')})
            factories = [m for m in old['Methods'] if m['Type'] == 'GameplayTags.GameplayTagContainer' and m['Name'] == 'Union' and '->GameplayTags.GameplayTagContainer' in m['Key']]
            require(len(factories) == 1 and normalized(factories[0]) == normalized(final[factories[0]['Key']]), 'Factory IL changed')
        for variant, d in data.items():
            storage = next(t for t in d['Types'] if t['Key'] == 'GameplayTags.TagStorage')
            require(storage['BeforeFieldInit'], 'BeforeFieldInit changed')
            methods = [m for m in d['Methods'] if m['Type'] == 'GameplayTags.TagStorage']
            require(not any(m['Name'] == '.cctor' for m in methods), 'New storage static constructor')
            ctor = next(m for m in methods if m['Name'] == '.ctor')
            require(ctor['Attributes'] == 6278 and ctor['ImplAttributes'] == 0 and ctor['Body']['Length'] == 40, 'Constructor metadata/body changed')
        self.report['Metadata'] = {'Passed': True, 'Ledger': ledger, 'TargetAssembliesExecuted': False,
                                  'TargetAssembliesLoaded': False, 'RawOperandChangesNotNormalizedAwayWithoutRecording': True}
        self.save()

    def publish_validated_streams(self):
        """Copy exact accepted protocols only; never rewrite bytes used by a gate."""
        checks = {fixture['Name'] + '-' + variant: fixture['Check']
                  for fixture in self.plan['Runs'] for variant in fixture['Labels']}
        def copy(entry, relative):
            self.remaining(1)
            source = self.stream_path(entry)
            require(sha(source) == entry['GzipSHA256'], 'Validated stream changed before publication')
            target = self.out / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            with source.open('rb') as src, target.open('xb') as dst:
                for data in iter(lambda: src.read(1048576), b''):
                    self.remaining(1)
                    dst.write(data)
            require(sha(target) == entry['GzipSHA256'], 'Published stream copy differs')
            entry.update(Path=relative, PublishedRawSemanticStream=True)
        for row in self.report['Runs']:
            check = checks.get(row['Name'], {})
            labels = set()
            if check.get('Expected'):
                labels.update(key.removesuffix('SHA256') for key in check['Expected'])
            elif check.get('Text') or check.get('WarmProxyNoRegression'):
                labels.add('Stdout')
                if row['Stderr']['Bytes'] == 0:
                    labels.add('Stderr')
            # Core exit/assertion results and boundary summaries remain status/hash-only;
            # the boundary's canonical binary state itself is published below.
            for label in ['Stdout', 'Stderr']:
                if label in labels:
                    copy(row[label], 'logs/' + row['CommandName'] + '/' + label + '.gz')
                else:
                    row[label]['PublicOmissionReason'] = 'No fixed raw-publication protocol; status and hash retained'
        for state in self.report['BinaryStates']:
            copy(state, state['Path'])
        self.report['RawPublicationPolicy'] = 'Only fixed-protocol streams after every semantic/source/binary gate succeeds'
        self.save()

    def write_upload_allowlist(self):
        allowed = {}
        for path in sorted(self.out.rglob('*')):
            require(not path.is_symlink(), 'Artifact output must not contain symlinks')
            if not path.is_file() or path.name == 'upload-allowlist.json':
                continue
            relative = path.relative_to(self.out).as_posix()
            permitted = relative in ['report.json', 'completion.json', 'preflight-failure.json']
            permitted = permitted or re.fullmatch(r'logs/run-[A-Za-z0-9_-]+/(Stdout|Stderr)\.gz', relative) is not None
            permitted = permitted or re.fullmatch(r'binary-state/large-selected-alias-(original|previous|candidate)\.gz', relative) is not None
            require(permitted, 'Unexpected public artifact file')
            allowed[relative] = sha(path)
        write(self.out / 'upload-allowlist.json', {'Files': allowed,
              'Excluded': ['build trees', 'PDBs', 'response files', 'restore metadata',
                           'non-test command transcripts', 'unvalidated fixture diagnostics'],
              'PrivateCapturesRetainedAfterExit': False})

    def finish(self):
        self.remaining(1)
        require(verify_package() == self.package, 'Package changed during run')
        for label, source in self.sources.items():
            require(files(source / 'Runtime') == self.source_hashes[label], 'Extracted source changed')
        require(files(self.args.candidate_source.resolve() / 'Runtime') == self.candidate_hashes, 'Candidate source changed')
        for path, value in self.inputs.items(): require(sha(path) == value, 'Compiler/reference/source changed: ' + path)
        for path, value in self.report['HostHashes'].items(): require(sha(path) == value, 'Host changed: ' + path)
        for path, value in self.binary_hashes.items(): require(sha(path) == value, 'Emitted binary changed: ' + path)
        self.remaining(1)
        expected = self.plan['MatrixTargetProcesses'] + self.plan['CoreTargetProcesses'][self.backend]
        require(len(self.report['Runs']) == expected and all(r['Passed'] for r in self.report['Runs']), 'Fixed semantic process count incomplete')
        self.publish_validated_streams()
        self.remaining(1)
        self.report.update(Status='passed', ExpectedTargetProcesses=expected, TargetProcesses=len(self.report['Runs']),
                           InputsUnchanged=True, FinishedUTC=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                           CompilerAndReferenceHashes=self.inputs, EmittedBinaryHashes=self.binary_hashes,
                           CrossRuntimeComparison='Separate jobs each require the same frozen exact semantic digests; this job did not run the other backend')
        self.save()
        # The completion receipt is created only after every success and immutability gate.
        write(self.out / 'completion.json', {'Status': 'passed', 'Backend': self.backend, 'ReportSHA256': sha(self.out / 'report.json'),
             'PackageManifestSHA256': sha(HERE / 'package-manifest.json'), 'CandidateSnapshotCommit': self.args.candidate_snapshot_commit,
             'CandidateRuntimeHashes': self.candidate_hashes, 'TargetProcesses': expected, 'TimingBenchmarks': False, 'ColdRevalidation': False})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', type=Path, required=True)
    parser.add_argument('--mono', type=Path, help='Select Mono target execution only; .NET remains the compiler/metadata-reader host')
    parser.add_argument('--git-repository', type=Path, required=True)
    parser.add_argument('--candidate-source', type=Path, required=True, help='Committed package snapshot containing Runtime/, never repository root')
    parser.add_argument('--candidate-snapshot-commit', required=True, help='Exact 40-hex commit containing the candidate snapshot path')
    parser.add_argument('--out', type=Path, required=True, help='New evidence directory outside all source packages')
    args = parser.parse_args()
    runner = None
    try:
        package = verify_package()
        plan = json.loads((HERE / 'plan.json').read_text())
        runner = Runner(args, plan, package)
        require(sys.platform.startswith('linux'), 'Frozen exact-output contract currently supports Linux CI only')
        runner.prepare_sources()
        runner.toolchain()
        runner.metadata()
        runner.core()
        runner.matrix()
        runner.finish()
        print('PASS fixed Union repair semantics:', runner.backend, len(runner.report['Runs']), 'target processes; no benchmark or cold matrix')
    except BaseException:
        error_type = sys.exc_info()[0].__name__
        if runner is not None:
            runner.report.update(Status='failed', Error={'Type': error_type,
                'Stage': runner.report['Commands'][-1]['Name'] if runner.report['Commands'] else 'preflight',
                'PrivateTracebackPublished': False}, FinishedUTC=datetime.datetime.now(datetime.timezone.utc).isoformat())
            runner.save()
        else:
            out = args.out.resolve()
            sources = [HERE, args.candidate_source.resolve()]
            if not out.exists() and not any(out == p or p in out.parents for p in sources):
                out.mkdir(parents=True)
                write(out / 'preflight-failure.json', {'Status': 'failed', 'ErrorType': error_type, 'NoTargetExecuted': True})
        print('Validation failed: ' + error_type + '; see the output report for stage, exit and stream hashes.', file=sys.stderr)
        return 1
    finally:
        if runner is not None:
            try:
                runner.write_upload_allowlist()
            finally:
                runner.private_directory.cleanup()


if __name__ == '__main__':
    sys.exit(main())
