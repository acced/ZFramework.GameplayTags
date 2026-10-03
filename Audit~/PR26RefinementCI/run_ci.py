#!/usr/bin/env python3
"""Relocatable PR26 managed correctness and full public-call performance runner.

Frozen C# workload bodies, original runners and analyzers are copied unchanged.
The adapter controls source selection, SDK relocation, architecture and receipts.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import struct
import subprocess
import sys
import time
import zipfile

ROOT = Path(__file__).resolve().parent
PINNED_SDK = '8.0.425'
ARCHES = {'x86_64': 'x64', 'amd64': 'x64', 'aarch64': 'arm64', 'arm64': 'arm64'}
SUITES = 'independent,full,bitcount,original,interop,bridge,bridge-mutation'
EXPECTED_CELLS = {'query': 111, 'builder': 90, 'mixed': 225, 'bridge': 54}
CONFIGURATIONS = {'net8-normal': ('net8.0', False), 'net8-checked': ('net8.0', True),
                  'netstandard-checked': ('netstandard2.1', True)}


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read(path):
    return json.loads(Path(path).read_text())


def write(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + '\n')


def runtime_inventory(source):
    return {str(p.relative_to(source)): sha(p) for p in sorted((source / 'Runtime').glob('*.cs'))}


def verify_payload():
    manifest = read(ROOT / 'payload-sha256.json')
    require(manifest['schema'] == 1, 'Unsupported payload manifest')
    actual = {str(p.relative_to(ROOT)) for p in ROOT.rglob('*')
              if (p.is_file() or p.is_symlink()) and p.name != 'payload-sha256.json'
              and p.relative_to(ROOT).parts[0] != 'historical-data'
              and '__pycache__' not in p.relative_to(ROOT).parts}
    require(actual == set(manifest['files']), 'Missing or unmanifested payload files: ' +
            ', '.join(sorted(actual.symmetric_difference(manifest['files']))))
    for name, digest in manifest['files'].items():
        rel = Path(name)
        require(not rel.is_absolute() and '..' not in rel.parts, 'Unsafe payload path')
        path = ROOT / rel
        require(path.is_file() and not path.is_symlink(), 'Missing/nonregular input: ' + name)
        require(sha(path) == digest, 'Input bytes changed: ' + name)
    selection = read(ROOT / 'source-selection.json')
    require(selection['measured_commit'] == '98311092b7e8892e5632c2863a391187f4367cf8', 'Wrong ancestry')
    require(selection['generated_variant'] == 'empty', 'Wrong PR26 generated baseline')
    for name, record in selection['sources'].items():
        actual = runtime_inventory(ROOT / record['path'])
        require(actual == record['runtime_sha256'], 'Wrong Runtime source selection: ' + name)
        digest = hashlib.sha256(json.dumps(actual, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
        require(digest == record['runtime_set_sha256'], 'Wrong Runtime set digest: ' + name)
        proof = ROOT / 'provenance' / (name + '-source-proof.json')
        require(sha(proof) == record['original_source_proof_sha256'], 'Wrong source proof')
        original = read(proof)['production_inputs']
        require(all(original['source/' + p] == h for p, h in actual.items()), 'Original integration proof disagrees')
    ancestry = read(ROOT / 'provenance/baseline/source-identity.json')
    require(sha(ROOT / 'provenance/baseline/source-identity.json') ==
            read(ROOT / 'provenance/baseline-provenance.json')['baseline_manifest_sha256'], 'Ancestry manifest mismatch')
    for relative in ['generated/DirectArraySet.cs', 'generated/BulkBuilders.cs']:
        require(sha(ROOT / 'provenance/baseline' / relative) == ancestry['files'][relative], 'Generated baseline changed')
    historical = []
    for group in ['query', 'builder', 'mixed']:
        for backend in ['default', 'nohw']:
            summary = read(ROOT / 'historical/summaries' / ('final-' + group + '-' + backend + '.json'))
            historical.extend(dict(run=summary['name'], **r) for r in summary['rows'])
    require(len(historical) == 852, 'Historical matrix incomplete')
    losses = [r for r in historical if r['verdict'] == 'clear-regression']
    invalid = [r for r in historical if r['verdict'] == 'invalid-sample']
    require(len(losses) == 4 and len(invalid) == 6, 'Historical losses/invalid controls were lost')
    return dict(payload_manifest_sha256=sha(ROOT / 'payload-sha256.json'),
                source_selection_sha256=sha(ROOT / 'source-selection.json'),
                source_runtime_set_sha256={k: v['runtime_set_sha256'] for k, v in selection['sources'].items()},
                checked_files=len(manifest['files']), historical_rows=852,
                historical_losses=[{'run': r['run'], 'layout': r['layout'], 'id': r['id']} for r in losses],
                historical_invalid_controls=[{'run': r['run'], 'layout': r['layout'], 'id': r['id']} for r in invalid])


def native_architecture(dotnet, expected):
    require(platform.system() == 'Linux', 'This protocol requires native Linux with taskset')
    machine = platform.machine().lower()
    require(ARCHES.get(machine) == expected, 'Host architecture does not match requested shard')
    require(shutil.which('taskset') is not None, 'taskset is required')
    require(hasattr(os, 'sched_getaffinity') and os.sched_getaffinity(0), 'CPU affinity unavailable')
    with dotnet.open('rb') as stream:
        header = stream.read(20)
    require(header[:4] == b'\x7fELF' and header[4] == 2, 'dotnet must be a native 64-bit ELF executable')
    order = '<' if header[5] == 1 else '>'
    elf_machine = struct.unpack(order + 'H', header[18:20])[0]
    require(elf_machine == {'x64': 62, 'arm64': 183}[expected], 'dotnet ELF architecture differs from host')
    require(not any(k.startswith('QEMU_') for k in os.environ), 'Emulated QEMU execution is outside this protocol')
    return {'expected': expected, 'uname_machine': machine, 'dotnet_elf_machine': elf_machine,
            'platform': platform.platform(), 'affinity': sorted(os.sched_getaffinity(0)),
            'boot_id': Path('/proc/sys/kernel/random/boot_id').read_text().strip(),
            'cpuinfo': Path('/proc/cpuinfo').read_text(),
            'runner_arch': os.environ.get('RUNNER_ARCH'), 'runner_os': os.environ.get('RUNNER_OS'),
            'note': 'Native ISA hosted VM, not a claim of bare-metal isolation or native Unity execution.'}


class Session:
    def __init__(self, args):
        self.args = args
        proof = verify_payload()
        self.work = args.work.resolve()
        require(self.work != ROOT and ROOT not in self.work.parents, 'Work must be outside immutable CI payload')
        require(not self.work.exists() or not any(self.work.iterdir()), 'Use a new or empty --work directory')
        self.work.mkdir(parents=True, exist_ok=True)
        self.dotnet = Path(args.dotnet or shutil.which('dotnet') or '').resolve()
        require(self.dotnet.is_file() and os.access(self.dotnet, os.X_OK), 'A complete .NET 8 SDK is required')
        require((self.dotnet.parent / 'sdk').is_dir(), 'dotnet must resolve to a complete SDK installation')
        self.env = {k: v for k, v in os.environ.items() if not k.startswith(('DOTNET_', 'COMPlus_')) and k != 'PYTHONOPTIMIZE'}
        self.env.update(DOTNET_ROOT=str(self.dotnet.parent), DOTNET_CLI_TELEMETRY_OPTOUT='1',
                        DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',
                        DOTNET_NOLOGO='1', DOTNET_CLI_HOME=str(self.work / 'cli-home'),
                        PYTHONDONTWRITEBYTECODE='1')
        architecture = native_architecture(self.dotnet, args.expect_arch)
        versions = subprocess.check_output([str(self.dotnet), '--list-sdks'], env=self.env, text=True)
        supported = [line.split()[0] for line in versions.splitlines() if re.match(r'^8\.0\.\d+\s', line)]
        require(PINNED_SDK in supported, 'Required .NET SDK ' + PINNED_SDK + ' is not installed')
        version = PINNED_SDK
        write(self.work / 'global.json', {'sdk': {'version': version, 'rollForward': 'disable'}})
        self.project = self.work / 'pr26-refinement'
        shutil.copytree(ROOT / 'inputs', self.project)
        self.source = self.project / 'sources'
        shutil.copytree(ROOT / 'source', self.source)
        mapping = self.work / 'comparison-materials/dotnet-sdk'
        mapping.parent.mkdir(parents=True)
        mapping.symlink_to(self.dotnet.parent, target_is_directory=True)
        self.commands = []
        self.receipt = dict(status='running', command=args.command, arguments=vars(args) | {'work': str(self.work)},
                            identity=proof, architecture=architecture, sdk=version,
                            dotnet=str(self.dotnet), started_utc=time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
                            github={k: os.environ.get(k) for k in ['GITHUB_SHA', 'GITHUB_REF', 'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT']},
                            commands=self.commands, source_relocation={k: str(self.source / k) for k in ['selected', 'baseline', 'bridge-before']})
        write(self.work / 'run-receipt.json', self.receipt)
        self.run([str(self.dotnet), '--info'], 'dotnet-info', cwd=self.work)
        self.verify_copies()

    def verify_copies(self):
        for path in (ROOT / 'inputs').rglob('*'):
            if path.is_file():
                require(sha(path) == sha(self.project / path.relative_to(ROOT / 'inputs')), 'Relocated input changed')
        for path in (ROOT / 'source').rglob('*'):
            if path.is_file():
                require(sha(path) == sha(self.source / path.relative_to(ROOT / 'source')), 'Relocated source changed')
        for name, record in read(ROOT / 'source-selection.json')['sources'].items():
            require(runtime_inventory(self.source / name) == record['runtime_sha256'], 'Relocated Runtime changed')

    def run(self, command, label, cwd=None, env=None):
        log = self.work / 'logs' / (label + '.log')
        log.parent.mkdir(exist_ok=True)
        row = {'label': label, 'command': list(map(str, command)), 'cwd': str(cwd or self.project), 'status': 'running'}
        self.commands.append(row)
        write(self.work / 'run-receipt.json', self.receipt)
        print('RUN', label, flush=True)
        started = time.monotonic()
        try:
            with log.open('w') as stream:
                result = subprocess.run(row['command'], cwd=cwd or self.project, env=env or self.env,
                                        stdout=stream, stderr=subprocess.STDOUT, timeout=3000)
            row.update(returncode=result.returncode, elapsed_s=time.monotonic() - started,
                       log_sha256=sha(log), status='passed' if result.returncode == 0 else 'failed')
            write(self.work / 'run-receipt.json', self.receipt)
            if result.returncode:
                print(log.read_text()[-12000:], file=sys.stderr)
                raise RuntimeError(label + ' failed; see ' + str(log))
        except subprocess.TimeoutExpired:
            row.update(status='timeout', elapsed_s=time.monotonic() - started)
            write(self.work / 'run-receipt.json', self.receipt)
            raise

    def script(self, script, arguments, label):
        path = self.project / script
        self.verify_copies()
        self.run([sys.executable, str(path), *map(str, arguments)], label, cwd=path.parent)
        self.verify_copies()

    def feature_probe(self):
        folder = self.work / 'features'
        folder.mkdir()
        shutil.copyfile(ROOT / 'FeatureProbe.cs', folder / 'FeatureProbe.cs')
        (folder / 'FeatureProbe.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>disable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="FeatureProbe.cs"/></ItemGroup></Project>')
        self.run([str(self.dotnet), 'build', str(folder / 'FeatureProbe.csproj'), '-c', 'Release', '-o', str(folder / 'bin'),
                  '--configfile', str(self.project / 'benchmark/NuGet.Config'), '--disable-build-servers'], 'feature-build')
        for backend in ['default', 'nohw']:
            env = dict(self.env)
            if backend == 'nohw':
                env.update(DOTNET_EnableHWIntrinsic='0', COMPlus_EnableHWIntrinsic='0')
            self.run([str(self.dotnet), str(folder / 'bin/FeatureProbe.dll')], 'features-' + backend, env=env)
            result = read(self.work / 'logs' / ('features-' + backend + '.log'))
            require(result['process_architecture'].lower() == self.args.expect_arch, 'Managed process architecture mismatch')
            require(result['os_architecture'].lower() == self.args.expect_arch, 'Managed OS architecture mismatch')
            require(result['runtime'].startswith('.NET 8.'), 'Wrong managed runtime')
            if backend == 'nohw':
                require(not any(result[k] for k in ['vector_accelerated', 'avx2', 'popcnt', 'popcnt_x64', 'advsimd', 'advsimd_arm64']), 'Hardware-disable controls not active')
            write(folder / (backend + '.json'), result)

    def integration(self, variant, configuration, suites, facade=None):
        framework, checked = CONFIGURATIONS[configuration]
        label = variant + '-' + configuration
        args = ['--source', self.source / variant, '--label', label, '--framework', framework, '--suites', suites]
        if checked:
            args.append('--checked')
        if facade:
            args.extend(['--facade-library', facade])
        self.script('independent-tests/integration.py', args, 'correctness-' + label)
        result = self.project / 'independent-tests/integration-results' / label
        summary = read(result / 'summary.json')
        require(len(summary) == len(suites.split(',')) * 2 and all(r['exit'] == 0 for r in summary), 'Incomplete correctness execution')
        binding = read(result / 'build-binding.json')
        require(binding['source'] == str(self.source / variant), 'Compiler bound wrong source')
        require(binding['framework'] == framework and binding['checked'] == checked, 'Wrong build configuration')
        require(not binding['experimental_defines'] and binding['facade_assembly_separate'], 'Wrong assembly or defines')
        return result

    def host(self, name, admitted, variant, harness):
        facade = next(Path(p) for p in read(admitted / 'build-binding.json')['references'] if p.endswith('/UnityApiFacade.dll'))
        self.script('benchmark/build_reference.py', ['--name', name, '--runtime-source', self.source / variant / 'Runtime',
                    '--runtime-dll', admitted / 'runtime/bin/GameplayTags.dll', '--facade-dll', facade,
                    '--harness', harness], 'host-' + name)

    def correctness(self, configuration):
        self.feature_probe()
        selected = self.integration('selected', configuration, SUITES)
        facade = next(Path(p) for p in read(selected / 'build-binding.json')['references'] if p.endswith('/UnityApiFacade.dll'))
        if configuration == 'net8-normal':
            self.integration('baseline', configuration, SUITES, facade)
        self.script('independent-tests/integration_faults.py', ['--admitted', selected, '--label', 'selected-' + configuration + '-faults'], 'faults-' + configuration)
        self.script('benchmark/test_integrity.py', [], 'analyzer-integrity')

    def benchmark(self, group, backend):
        self.feature_probe()
        selected = self.integration('selected', 'net8-normal', 'interop,bridge,bridge-mutation')
        facade = next(Path(p) for p in read(selected / 'build-binding.json')['references'] if p.endswith('/UnityApiFacade.dll'))
        if group == 'bridge':
            before = self.integration('bridge-before', 'net8-normal', 'interop,bridge,bridge-mutation', facade)
            self.host('bridge-legacy', before, 'bridge-before', 'BridgeProbe.cs')
            self.host('bridge-before', before, 'bridge-before', 'BridgeProbe.cs')
            self.host('bridge-selected', selected, 'selected', 'BridgeProbe.cs')
            runner, analyzer = 'run_bridge_factor.py', 'analyze_bridge_factor.py'
            arguments = ['--baseline', 'bridge-legacy', '--v2', 'bridge-before', '--v3', 'bridge-selected']
        else:
            baseline = self.integration('baseline', 'net8-normal', 'interop,bridge', facade)
            self.host('baseline', baseline, 'baseline', 'Probe.Reference.cs')
            self.host('selected', selected, 'selected', 'Probe.Reference.cs')
            runner, analyzer = 'run_reference.py', 'analyze.py'
            arguments = ['--baseline', 'baseline', '--candidate', 'selected', '--group', group, '--rounds', '3', '--layouts', 'Micro,Dense,Auto']
        name = 'ci-' + self.args.expect_arch + '-' + group + '-' + backend
        self.script('benchmark/' + runner, arguments + ['--backend', backend, '--name', name], 'measure-' + name)
        self.script('benchmark/' + analyzer, [name], 'analyze-' + name)
        folder = self.project / 'benchmark/results' / name
        summary = read(folder / 'summary.json')
        require(len(summary['rows']) == EXPECTED_CELLS[group], 'Incomplete shard summary')
        manifest = read(folder / 'manifest.json')
        for run in manifest['runs']:
            data = read(folder / (run['label'] + '.json'))
            # First-factory metadata deliberately omits architecture; timed and calibration rows include it.
            if 'architecture' in data:
                require(data['architecture'].lower() == self.args.expect_arch, 'Result architecture mismatch')
            elif run['label'].startswith('r'):
                raise RuntimeError('Timed result omitted architecture')
        if group != 'bridge':
            self.script('benchmark/test_manifest_integrity.py', [name], 'manifest-integrity')
        report = {'name': name, 'architecture': self.args.expect_arch, 'backend': backend, 'group': group,
                  'counts': summary['counts'], 'cells': len(summary['rows']),
                  'all_invalid_and_regression_rows_retained': True,
                  'status': 'completed measurements and integrity checks; inspect verdicts before drawing performance conclusions',
                  'limitations': ['Hosted VM measurements are noisy, not Unity/Mono/IL2CPP/Burst performance.',
                                  'Historical four regressions and six invalid cells remain separate and unchanged.',
                                  'No performance superiority threshold is used as a CI exit-code gate. Invalid samples remain unranked.']}
        write(self.work / 'shard-summary.json', report)
        print(json.dumps(report, indent=2))
        if self.env.get('GITHUB_STEP_SUMMARY'):
            with open(self.env['GITHUB_STEP_SUMMARY'], 'a') as stream:
                stream.write('### PR26 ' + name + '\n\n' + json.dumps(summary['counts']) + '\n\n')
                stream.write('All losses, invalid samples and batch tails are retained in the artifact. A green job means execution/integrity passed, not universal performance superiority.\n')

    def finish(self, error=None):
        self.verify_copies()
        require(verify_payload() == self.receipt['identity'], 'Frozen payload changed during execution')
        self.receipt.update(status='failed' if error else 'passed', error=str(error) if error else None,
                            finished_utc=time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()))
        write(self.work / 'run-receipt.json', self.receipt)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='command', required=True)
    sub.add_parser('verify')
    archive = sub.add_parser('archive')
    archive.add_argument('--work', type=Path, required=True)
    archive.add_argument('--output', type=Path, required=True)
    for name in ['preflight', 'correctness', 'benchmark']:
        p = sub.add_parser(name)
        p.add_argument('--work', type=Path, required=True)
        p.add_argument('--dotnet')
        p.add_argument('--expect-arch', choices=['x64', 'arm64'], required=True)
        if name == 'preflight':
            p.add_argument('--smoke', choices=list(CONFIGURATIONS))
        elif name == 'correctness':
            p.add_argument('--configuration', choices=list(CONFIGURATIONS), required=True)
        else:
            p.add_argument('--group', choices=list(EXPECTED_CELLS), required=True)
            p.add_argument('--backend', choices=['default', 'nohw'], required=True)
    args = parser.parse_args()
    if args.command == 'archive':
        work, output = args.work.resolve(), args.output.resolve()
        require(output != work and work not in output.parents, 'Archive must be outside work')
        output.parent.mkdir(parents=True, exist_ok=True)
        entries = {}
        with zipfile.ZipFile(output, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
            if work.is_dir():
                for path in sorted(work.rglob('*')):
                    relative = path.relative_to(work)
                    if not path.is_file() or path.is_symlink() or any(p in {'home', 'cli-home', 'obj', '__pycache__', 'comparison-materials'} for p in relative.parts):
                        continue
                    require(sum(r['size'] for r in entries.values()) + path.stat().st_size < 512 * 1024 * 1024, 'Diagnostics exceed 512MiB')
                    entries[str(relative)] = {'sha256': sha(path), 'size': path.stat().st_size}
                    archive.write(path, str(relative))
            archive.writestr('artifact-files.json', json.dumps(entries, indent=2) + '\n')
        print(json.dumps({'archive': str(output), 'sha256': sha(output), 'files': len(entries)}, indent=2))
        return
    if args.command == 'verify':
        print(json.dumps(verify_payload(), indent=2))
        return
    session = Session(args)
    try:
        if args.command == 'correctness':
            session.correctness(args.configuration)
        elif args.command == 'benchmark':
            session.benchmark(args.group, args.backend)
        else:
            session.feature_probe()
            session.script('benchmark/test_integrity.py', [], 'analyzer-integrity')
            if args.smoke:
                admitted = session.integration('selected', args.smoke, 'interop,bridge,bridge-mutation')
                # Untimed host generation/reference binding is valid for both Runtime targets.
                session.host('preflight-reference', admitted, 'selected', 'Probe.Reference.cs')
                session.host('preflight-bridge', admitted, 'selected', 'BridgeProbe.cs')
        session.finish()
    except BaseException as error:
        session.finish(error)
        raise


if __name__ == '__main__':
    main()
