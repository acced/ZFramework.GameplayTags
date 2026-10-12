"""Direct-csc helper derived from the existing screen; caller owns private logging."""
import json
from pathlib import Path
import shutil


def version(path):
    return tuple(int(x) for x in path.name.split('-')[0].split('.'))


def toolchain(dotnet):
    root = Path(dotnet).resolve().parent
    sdk = root / 'sdk/8.0.425'
    runtime = root / 'shared/Microsoft.NETCore.App/8.0.31'
    refs = root / 'packs/Microsoft.NETCore.App.Ref/8.0.31'
    if not all(p.is_dir() for p in (sdk, runtime, refs)):
        raise RuntimeError('Expected SDK 8.0.425 and CLR/reference pack 8.0.31')
    return sdk, runtime, refs


def references(backend, dotnet, nuget):
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
    return refs, packages


def input_files(backend, dotnet, mono, nuget):
    root = Path(dotnet).resolve().parent
    sdk, runtime, _ = toolchain(dotnet)
    refs, _ = references(backend, dotnet, nuget)
    files = {'dotnet/dotnet': Path(dotnet)}
    for base in (sdk / 'Roslyn/bincore', runtime, root / 'host/fxr/8.0.31'):
        for path in base.rglob('*'):
            if path.is_file(): files['dotnet/' + str(path.relative_to(root))] = path
    for path in refs:
        if path.is_relative_to(root): name = 'dotnet/' + str(path.relative_to(root))
        else: name = 'nuget/' + str(path.relative_to(nuget))
        files[name] = path
    if backend == 'mono': files['mono/host'] = Path(mono)
    return files


def build(source, program, out, backend, dotnet, mono, nuget, run):
    out.mkdir(parents=True, exist_ok=False)
    assembly = out / ('Test.exe' if backend == 'mono' else 'Test.dll')
    sdk, runtime, refpack = toolchain(dotnet)
    refs, packages = references(backend, dotnet, nuget)
    for package in packages:
        shutil.copy2(package, out / package.name)
    files = sorted(p for p in (source / 'Runtime').rglob('*.cs') if p.name != 'GameObjectGameplayTagContainer.cs') + sorted(program.parent.glob('*.cs'))
    flags = ['-main:PortableBindingEntry', '-nologo', '-optimize+', '-langversion:9.0', '-nullable:disable', '-deterministic+', '-target:exe', '-out:"' + str(assembly) + '"']
    flags += ['-r:"' + str(p) + '"' for p in refs] + ['"' + str(p) + '"' for p in files]
    response = out / 'compile.rsp'
    response.write_text('\n'.join(flags))
    run('compile-' + source.name, [dotnet, sdk / 'Roslyn/bincore/csc.dll', '@' + str(response)])
    assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {
        'tfm': 'net8.0', 'rollForward': 'Disable', 'framework': {'name': 'Microsoft.NETCore.App', 'version': runtime.name},
        'configProperties': {'System.Runtime.TieredCompilation': False, 'System.GC.Server': False}}}))
    return [mono if backend == 'mono' else dotnet, assembly]
