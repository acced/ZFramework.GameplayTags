"""Direct-csc portable builds. The caller owns execution, pins, and private logs."""
import json
from pathlib import Path


SDK_VERSION = '8.0.425'
CLR_VERSION = '8.0.31'


def toolchain(dotnet):
    root = Path(dotnet).resolve().parent
    sdk = root / ('sdk/' + SDK_VERSION)
    runtime = root / ('shared/Microsoft.NETCore.App/' + CLR_VERSION)
    refs = root / ('packs/Microsoft.NETCore.App.Ref/' + CLR_VERSION)
    if not all(path.is_dir() for path in (sdk, runtime, refs, root / 'host/fxr')):
        raise RuntimeError('Required toolchain is unavailable')
    return sdk, runtime, refs


def references(backend, dotnet, nuget):
    if backend not in ('net', 'mono'):
        raise ValueError('Unsupported backend')
    _, _, refpack = toolchain(dotnet)
    if backend == 'mono':
        framework = nuget / 'microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2'
        refs = [framework / name for name in ('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Runtime.Serialization.dll', 'System.Xml.dll')]
        packages = [nuget / name for name in (
            'system.memory/4.5.5/lib/net461/System.Memory.dll',
            'system.buffers/4.5.1/lib/net461/System.Buffers.dll',
            'system.runtime.compilerservices.unsafe/4.5.3/lib/net461/System.Runtime.CompilerServices.Unsafe.dll',
            'system.numerics.vectors/4.5.0/lib/net46/System.Numerics.Vectors.dll')]
        refs += packages
    else:
        packages = []
        refs = sorted((refpack / 'ref/net8.0').glob('*.dll'))
    if not refs or any(not path.is_file() for path in refs):
        raise RuntimeError('Required reference input is unavailable')
    return refs, packages


def input_files(backend, dotnet, mono, nuget):
    root = Path(dotnet).resolve().parent
    sdk, runtime, _ = toolchain(dotnet)
    refs, _ = references(backend, dotnet, nuget)
    files = {'dotnet/dotnet': Path(dotnet)}
    # Pin every hostfxr candidate, because the dotnet host selects its loader.
    for base in (sdk / 'Roslyn/bincore', runtime, root / 'host/fxr'):
        for path in sorted(base.rglob('*')):
            if path.is_file():
                files['dotnet/' + str(path.relative_to(root))] = path
    for path in refs:
        name = 'dotnet/' + str(path.relative_to(root)) if path.is_relative_to(root) else 'nuget/' + str(path.relative_to(nuget))
        files[name] = path
    if backend == 'mono':
        files['mono/host'] = Path(mono)
    return files


def _flags(backend, assembly, target, refs, files):
    flags = ['-nologo', '-optimize+', '-langversion:9.0', '-nullable:disable', '-deterministic+', '-nostdlib+',
             '-target:' + target, '-out:"' + str(assembly) + '"']
    if target == 'exe':
        flags += ['-main:PortableDiscoveryEntry']
    if backend == 'mono':
        flags += ['-define:PORTABLE_MONO']
    return flags + ['-r:"' + str(path) + '"' for path in refs] + ['"' + str(path) + '"' for path in files]


def _compile(response, flags, dotnet, run, label):
    sdk, _, _ = toolchain(dotnet)
    response.write_text('\n'.join(flags) + '\n', encoding='utf-8')
    return run(label, [dotnet, 'exec', '--fx-version', CLR_VERSION,
                       sdk / 'Roslyn/bincore/csc.dll', '@' + str(response)], allow_failure=True)


def build(source, program, out, backend, dotnet, mono, nuget, run):
    out.mkdir(parents=True, exist_ok=False)
    assembly = out / ('Test.exe' if backend == 'mono' else 'Test.dll')
    refs, _ = references(backend, dotnet, nuget)
    files = sorted(path for path in (source / 'Runtime').rglob('*.cs') if path.name != 'GameObjectGameplayTagContainer.cs')
    files += sorted(program.parent.glob('*.cs'))
    code = _compile(out / 'compile.rsp', _flags(backend, assembly, 'exe', refs, files), dotnet, run, 'compile-' + source.name)
    if backend == 'net':
        assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {
            'tfm': 'net8.0', 'rollForward': 'Disable', 'framework': {'name': 'Microsoft.NETCore.App', 'version': CLR_VERSION},
            'configProperties': {'System.Runtime.TieredCompilation': False, 'System.GC.Server': False}}}) + '\n', encoding='utf-8')
    # Mono resolves the pinned package files via the coordinator's pinned shared
    # dependency directory in MONO_PATH. Never copy dependencies to variant-specific assembly locations.
    return [mono if backend == 'mono' else dotnet, assembly], code


def build_fixture(program, count, out, original_assembly, backend, dotnet, mono, nuget, run):
    if count not in (1, 32) or program.name != 'Fixtures' + str(count) + '.cs':
        raise RuntimeError('Outside the fixed shared declaration fixture plan')
    out.mkdir(parents=True, exist_ok=True)
    if any((out / name).exists() for name in ('Test.dll', 'Test.exe')):
        raise RuntimeError('Unexpected main assembly in shared fixture directory')
    assembly = out / ('DiscoveryDeclarations' + str(count) + '.dll')
    response = out / ('compile-fixture' + str(count) + '.rsp')
    if assembly.exists() or response.exists():
        raise RuntimeError('Shared declaration fixture already emitted')
    refs, _ = references(backend, dotnet, nuget)
    code = _compile(response, _flags(backend, assembly, 'library', refs + [original_assembly], [program]),
                    dotnet, run, 'compile-fixture' + str(count))
    return assembly, code
