#!/usr/bin/env python3
"""Run existing fault fixtures against separate, instrumented actual Runtime DLLs."""
import argparse, hashlib, json, os, pathlib, subprocess, xml.sax.saxutils

root = pathlib.Path(__file__).resolve().parent
sdk = root.parents[1] / 'comparison-materials/dotnet-sdk/dotnet'
parser = argparse.ArgumentParser()
parser.add_argument('--admitted', required=True)
parser.add_argument('--label', required=True)
args = parser.parse_args()
admitted = pathlib.Path(args.admitted).resolve()
binding = json.loads((admitted / 'build-binding.json').read_text())
source = pathlib.Path(binding['source'])
out = root / 'integration-results' / args.label
if out.exists() and any(out.iterdir()):
    raise RuntimeError('Refusing to overwrite nonempty output')
out.mkdir(parents=True, exist_ok=True)

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

clean = admitted / 'runtime/bin/GameplayTags.dll'
assert sha(clean) == binding['runtime_sha256']
facade = next(pathlib.Path(p) for p in binding['references'] if p.endswith('/UnityApiFacade.dll'))
assert sha(facade) == binding['references'][str(facade)]
inventory = json.loads((admitted / 'sources.after.json').read_text())
inputs = {p: sha(pathlib.Path(p)) for p in binding['runtime_sources']}
assert all(inventory[p] == digest for p, digest in inputs.items())
framework = binding['framework']
framework_attribute = '.NETCoreApp,Version=v8.0' if framework == 'net8.0' else '.NETStandard,Version=v2.1'
env = dict(os.environ, DOTNET_CLI_HOME=str(root / 'home'), DOTNET_NOLOGO='1',
           DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_TieredCompilation='0', DOTNET_ReadyToRun='0')
summary = []
compiled = {}

def build(folder, target, files, refs, executable=False):
    folder.mkdir(parents=True, exist_ok=True)
    name = 'GameplayTags.Tests' if executable else 'GameplayTags'
    esc = lambda value: xml.sax.saxutils.escape(str(value))
    items = ''.join('<Compile Include="' + esc(p) + '"/>' for p in files)
    items += ''.join('<Reference Include="' + p.stem + '"><HintPath>' + esc(p) + '</HintPath></Reference>' for p in refs)
    project = ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>' + target + '</TargetFramework>'
               '<OutputType>' + ('Exe' if executable else 'Library') + '</OutputType><AssemblyName>' + name + '</AssemblyName>'
               '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><LangVersion>9.0</LangVersion>'
               '<AllowUnsafeBlocks>' + str(target == 'net8.0' and not executable).lower() + '</AllowUnsafeBlocks>'
               '<CheckForOverflowUnderflow>' + str(binding['checked']).lower() + '</CheckForOverflowUnderflow>'
               '<Optimize>true</Optimize><Deterministic>true</Deterministic>'
               + ('<StartupObject>FaultEntry</StartupObject>' if executable else '')
               + '</PropertyGroup><ItemGroup>' + items + '</ItemGroup></Project>')
    project_path = folder / 'Probe.csproj'
    project_path.write_text(project)
    before = {str(p): sha(p) for p in files}
    compiled.update(before)
    (folder / 'sources.before.json').write_text(json.dumps(before, indent=2))
    result = subprocess.run([str(sdk), 'build', str(project_path), '-c', 'Release', '-o', str(folder / 'bin'),
                             '--configfile', str(root / 'NuGet.Config'), '--disable-build-servers'],
                            env=env, capture_output=True, text=True)
    (folder / 'build.log').write_text(result.stdout + result.stderr)
    if result.returncode:
        print(result.stdout + result.stderr)
        raise SystemExit(result.returncode)
    assert before == {str(p): sha(p) for p in files}
    return folder / 'bin' / (name + '.dll')

for suite, entry, fixture in [
    ('fault', 'CopyFaultProbe', root.parent / 'baseline/source/Audit~/CopyCommit/Faults.cs'),
    ('unionfault', 'UnionFault', root / 'UnionFault.cs')
]:
    folder = out / suite
    folder.mkdir()
    text = fixture.read_text()
    start = text.index('namespace GameplayTags.Experiments')
    end = text.index('internal static class ' + entry)
    hook = folder / 'FaultHook.cs'
    hook.write_text('using System;\n' + text[start:end])
    host_fixture = folder / (entry + '.cs')
    host_fixture.write_text(text[:start] + text[end:])
    original_core = source / 'Runtime/DirectArraySet.cs'
    original = original_core.read_text()
    marker = 'var next = new Entry[size];'
    assert original.count(marker) == 1
    instrumented = folder / 'DirectArraySet.Fault.cs'
    instrumented.write_text(original.replace(marker, 'CopyFault.Before(); ' + marker, 1))
    files = [instrumented if pathlib.Path(p) == original_core else pathlib.Path(p) for p in binding['runtime_sources']] + [hook]
    runtime = build(folder / 'runtime', framework, files, [facade])
    runtime_hash = sha(runtime)
    wrapper = folder / 'FaultEntry.cs'
    wrapper.write_text('''using System;using System.IO;using System.Reflection;using System.Security.Cryptography;using GameplayTags;using GameplayTags.Experiments;
internal static class FaultEntry { static int Main(string[] args) { try {
var runtime=typeof(RuntimeTagSet).Assembly;
if(runtime!=typeof(DirectArraySet).Assembly||runtime.GetName().Name!="GameplayTags")throw new Exception("Wrong Runtime identity");
if(runtime.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>().FrameworkName!="''' + framework_attribute + '''")throw new Exception("Wrong Runtime target");
using(var hash=SHA256.Create()){var actual=BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(runtime.Location))).Replace("-","").ToLowerInvariant();if(actual!=Environment.GetEnvironmentVariable("EXPECTED_RUNTIME_SHA256"))throw new Exception("Wrong loaded Runtime bytes");}
var method=Assembly.GetExecutingAssembly().GetType("''' + entry + '''").GetMethod("Main",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);var result=method.Invoke(null,new object[]{args});return result is int status?status:0;
}catch(Exception error){Console.WriteLine(error);return 1;} } }
''')
    host = build(folder / 'host', 'net8.0', [host_fixture, wrapper], [facade, runtime], executable=True)
    deployed = {str(host.parent / p.name): sha(p) for p in [facade, runtime]}
    assert all(sha(pathlib.Path(p)) == digest for p, digest in deployed.items())
    proof = dict(clean_runtime_sha256=sha(clean), instrumented_runtime_sha256=runtime_hash,
                 source_sha256=inputs, original_core_sha256=sha(original_core), instrumented_core_sha256=sha(instrumented),
                 hook_sha256=sha(hook), deployed=deployed, framework=framework,
                 instrumentation='Exactly one CopyFault.Before() before var next = new Entry[size]; plus test-only internal hook',
                 experimental_defines=[])
    (folder / 'library-binding.json').write_text(json.dumps(proof, indent=2))
    for disabled in [False, True]:
        mode = 'nohw' if disabled else 'default'
        runenv = dict(env, EXPECTED_RUNTIME_SHA256=runtime_hash, EXPECT_VALID_UNION_FAULTS='1')
        for prefix in ['DOTNET_', 'COMPlus_']:
            if disabled:
                runenv[prefix + 'EnableHWIntrinsic'] = '0'
            else:
                runenv.pop(prefix + 'EnableHWIntrinsic', None)
        assert sha(runtime) == runtime_hash and sha(clean) == binding['runtime_sha256']
        assert all(sha(pathlib.Path(p)) == digest for p, digest in deployed.items())
        command = [str(sdk), str(host)] + ([args.label] if suite == 'fault' else []) + [str(folder / (mode + '.json'))]
        result = subprocess.run(command, env=runenv, capture_output=True, text=True)
        (folder / (mode + '.log')).write_text(result.stdout + result.stderr)
        record = dict(suite=suite, mode=mode, exit=result.returncode, runtime_sha256=runtime_hash,
                      clean_runtime_sha256=sha(clean), host_sha256=sha(host))
        summary.append(record)
        (out / 'summary.json').write_text(json.dumps(summary, indent=2))
        print(json.dumps(record), flush=True)
assert all(sha(pathlib.Path(p)) == digest for p, digest in compiled.items())
assert all(sha(pathlib.Path(p)) == digest for p, digest in inputs.items())
(out / 'sources.after.json').write_text(json.dumps(compiled, indent=2))
(out / 'admission.json').write_text(json.dumps(dict(admitted=str(admitted), clean_runtime_sha256=sha(clean),
                                                  source_sha256=inputs, framework=framework, checked=binding['checked']), indent=2))
raise SystemExit(1 if any(record['exit'] for record in summary) else 0)
