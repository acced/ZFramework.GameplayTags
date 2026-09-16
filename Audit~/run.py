#!/usr/bin/env python3
"""Managed-only audit. A green exit is NOT Unity/IL2CPP or performance release approval."""
import argparse, hashlib, io, json, os, pathlib, shutil, statistics, subprocess, tarfile, tempfile

BASELINE = '934b14a47ddf0b6367bf6720be58c18cbcb09896'
ROOT = pathlib.Path(__file__).resolve().parent.parent

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--output', default='../gameplaytags-audit')
    parser.add_argument('--rounds', type=int, default=3)
    parser.add_argument('--skip-bench', action='store_true')
    args = parser.parse_args()
    if args.rounds < 1: parser.error('--rounds must be positive')
    output = pathlib.Path(args.output).resolve(); output.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ, DOTNET_TieredCompilation='0', DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1')
    def run(command, log, cwd=ROOT, expected=True):
        proc = subprocess.run([str(x) for x in command], cwd=cwd, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=600)
        (output / log).write_text(proc.stdout, encoding='utf-8')
        print(proc.stdout, flush=True)
        if expected and proc.returncode: raise RuntimeError(f'{log}: exit {proc.returncode}')
        return proc.returncode
    head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    versions = [line.split()[0] for line in subprocess.check_output([args.dotnet,'--list-sdks'],text=True).splitlines() if line.startswith('8.0.') and '-' not in line.split()[0]]
    if not versions: raise RuntimeError('Install a .NET 8 SDK to run this audit.')
    sdk = max(versions, key=lambda value: tuple(map(int,value.split('.'))))
    with tempfile.TemporaryDirectory(prefix='gameplaytags-audit-') as directory:
        work = pathlib.Path(directory)
        (work/'global.json').write_text(json.dumps({'sdk':{'version':sdk,'rollForward':'disable'}}))
        baseline = work / 'baseline'; baseline.mkdir()
        archive = subprocess.check_output(['git', 'archive', BASELINE], cwd=ROOT)
        with tarfile.open(fileobj=io.BytesIO(archive)) as tar:
            for entry in tar.getmembers():
                p = pathlib.PurePosixPath(entry.name)
                if p.is_absolute() or '..' in p.parts or entry.issym() or entry.islnk(): raise RuntimeError('Unsupported archive entry')
            tar.extractall(baseline, filter='data')
        inventory = []
        for raw in subprocess.check_output(['git','ls-tree','-rz',BASELINE],cwd=ROOT).split(b'\0'):
            if not raw: continue
            meta, name = raw.split(b'\t',1); mode, kind, sha = meta.decode().split(); path = name.decode()
            data = (baseline / path).read_bytes()
            actual = hashlib.sha1(f'blob {len(data)}\0'.encode() + data).hexdigest()
            if actual != sha: raise RuntimeError('Baseline blob mismatch: '+path)
            if path.endswith('.meta') or path == 'LICENSE':
                if not (ROOT / path).exists() or (ROOT / path).read_bytes() != data: raise RuntimeError('Original meta/license changed: '+path)
            inventory.append({'path':path,'baseline_blob':sha,'head_sha256':hashlib.sha256((ROOT/path).read_bytes()).hexdigest() if (ROOT/path).exists() else None})
        (output/'inventory.json').write_text(json.dumps({'baseline':BASELINE,'head':head,'sdk':sdk,'files':inventory},indent=2))
        production = {}
        for variant, source in [('baseline',baseline),('candidate',ROOT)]:
            files=[p for folder in ('Runtime','Editor','Samples~') for p in (source/folder).rglob('*.cs')]
            production[variant]={'files':len(files),'lines':sum(len(p.read_text().splitlines()) for p in files),'bytes':sum(p.stat().st_size for p in files)}
        (output/'complexity.json').write_text(json.dumps(production,indent=2))
        builds = {}
        optimized = 'class FrozenGameplayTagQuery' in (ROOT/'Runtime/GameplayTagQuery.cs').read_text()
        for variant, source in [('baseline',baseline),('candidate',ROOT)]:
            host = work / (variant+'-host'); host.mkdir()
            for path in (ROOT/'Audit~').iterdir():
                if path.suffix in ('.cs','.csproj','.Config'): shutil.copy2(path,host/path.name)
            symbol = 'OPTIMIZED' if variant=='candidate' and optimized else 'BASELINE'
            run([args.dotnet,'build','Audit.csproj','-c','Release',f'-p:SourceRoot={source}',f'-p:DefineConstants={symbol}'],variant+'-build.log',host)
            dll = host/'bin/Release/net8.0/Audit.dll'; builds[variant]=(dll,host,source)
            run([args.dotnet,dll,'tests',output/(variant+'-tests.json')],variant+'-tests.log',host)
            fixture = host/'fixtures'; run([args.dotnet,dll,'generate',fixture],variant+'-generate.log',host)
            generated = []
            for index, path in enumerate(sorted(fixture.glob('*.cs'))):
                project = host/f'Fixture{index}.csproj'
                project.write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>9.0</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="{path}"/><Reference Include="Audit"><HintPath>{dll}</HintPath></Reference></ItemGroup></Project>')
                rc = run([args.dotnet,'build',project,'-c','Release',f'-p:BaseIntermediateOutputPath=obj-fixture{index}/',f'-p:OutputPath=bin-fixture{index}/'],f'{variant}-generated-{path.stem}.log',host,expected=False)
                generated.append({'case':path.stem,'compiled':rc==0})
                if variant=='candidate' and optimized and rc: raise RuntimeError('Generated candidate failed: '+path.stem)
            (output/(variant+'-generated.json')).write_text(json.dumps(generated,indent=2))
            profile = host/'Profile.csproj'
            profile.write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><LangVersion>9.0</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="{source}/Runtime/**/*.cs"/><Compile Include="UnityStubs.cs"/></ItemGroup></Project>')
            run([args.dotnet,'build',profile,'-c','Release','-p:BaseIntermediateOutputPath=obj-profile/','-p:OutputPath=bin-profile/'],variant+'-profile.log',host)
            if variant=='candidate' and optimized:
                run([args.dotnet,'build',profile,'-c','Release','-p:DefineConstants=UNITY_EDITOR','-p:BaseIntermediateOutputPath=obj-editor-profile/','-p:OutputPath=bin-editor-profile/'],'candidate-editor-profile.log',host)
                run([args.dotnet,'build','Extended.csproj','-c','Release',f'-p:SourceRoot={source}','-p:BaseIntermediateOutputPath=obj-extended/','-p:OutputPath=bin-extended/'],'extended-build.log',host)
                run([args.dotnet,host/'bin-extended/Extended.dll',output/'extended.json'],'extended-tests.log',host)
        if not args.skip_bench:
            for round_number in range(args.rounds):
                order = ['baseline','candidate'] if round_number % 2 == 0 else ['candidate','baseline']
                for variant in order:
                    dll,host,_ = builds[variant]
                    run([args.dotnet,dll,'bench',output/f'{variant}-bench-{round_number}.json'],f'{variant}-bench-{round_number}.log',host)
        comparisons = []
        if not args.skip_bench:
            collected = {}
            for variant in builds:
                table = {}
                for round_number in range(args.rounds):
                    payload=json.loads((output/f'{variant}-bench-{round_number}.json').read_text())
                    for row in payload['rows']:
                        item=table.setdefault((row['name'],row['size']),{'medians':[],'samples':[],'bytes':[]})
                        item['medians'].append(statistics.median(row['ns'])); item['samples'].append(row['ns']); item['bytes'].append(row['allocated_bytes'])
                collected[variant]=table
            for key in sorted(collected['baseline'].keys() & collected['candidate'].keys()):
                b=collected['baseline'][key]; c=collected['candidate'][key]
                bn=statistics.median(b['medians']); cn=statistics.median(c['medians'])
                comparisons.append({'name':key[0],'size':key[1],'baseline_ns':bn,'candidate_ns':cn,'ratio':cn/bn,'baseline':b,'candidate':c})
        alerts=[{'name':r['name'],'size':r['size'],'ratio':r['ratio']} for r in comparisons if r['ratio']>1.05]
        (output/'comparison.json').write_text(json.dumps(comparisons,indent=2))
        summary={'baseline':BASELINE,'head':head,'sdk':sdk,'optimized_source':optimized,'managed_checks':'passed','unity_editor':'not_run','il2cpp_android':'not_run','il2cpp_ios':'not_run','performance':'not_run' if args.skip_bench else ('review_required' if alerts else 'host_only_no_alerts'),'alerts_over_5_percent':alerts,'release_approved':False,'measurement_note':'7 samples per process; alternating fresh processes; all raw samples retained. No significance claims from shared runners. query.construct includes the wrapper, matcher delegate and, for the candidate, validation/freezing.'}
        (output/'summary.json').write_text(json.dumps(summary,indent=2)); print(json.dumps(summary,indent=2),flush=True)
    return 0

if __name__=='__main__': raise SystemExit(main())
