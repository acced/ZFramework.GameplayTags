#!/usr/bin/env python3
"""Four fixed source versions; genuine runtime C# checks. Never Unity/IL2CPP release approval."""
import argparse, hashlib, json, os, pathlib, shutil, statistics, subprocess, tempfile, zipfile
from xml.sax.saxutils import escape
from checks import verify_package, compile_documentation, compile_split_assemblies

ROOT = pathlib.Path(__file__).resolve().parent.parent
REFS = {
    'main': '934b14a47ddf0b6367bf6720be58c18cbcb09896',
    'optimize': 'a99f53d4697df6f577961ff1ee1bad14caa221d1',
    'alex': '28e4218f459ecf0e9fb4f0bca90805c05eb962c2',
}
TREES = {'main':'936bb858731b7ae0d1ec0a087de8afe1d96b1b40', 'optimize':'8a593dd469e4dbf6e182cc4c3d48582c39b73c94', 'alex':'530e7109d534ee358f601e312bac7e3b08e129d4'}
ALEX_CORE = ['BinarySearchUtility.cs', 'GameplayTag.cs', 'GameplayTagAttribute.cs',
    'GameplayTagContainer.cs', 'GameplayTagContainerDebugView.cs', 'GameplayTagContainerExtensionMethods.cs',
    'GameplayTagContainerUtility.cs', 'GameplayTagCountContainer.cs', 'GameplayTagDefinition.cs', 'GameplayTagEnumerator.cs',
    'GameplayTagFlags.cs', 'GameplayTagManager.cs', 'GameplayTagRegistrationContext.cs', 'GameplayTagUtility.cs']

def archive_tree(archive):
    root = {}
    for entry in archive.infolist():
        if entry.is_dir(): continue
        path = pathlib.PurePosixPath(entry.filename)
        node = root
        for part in path.parts[:-1]: node = node.setdefault(part, {})
        data = archive.read(entry.filename)
        mode = b'100755' if (entry.external_attr >> 16) & 0o111 else b'100644'
        node[path.name] = (mode, hashlib.sha1(b'blob '+str(len(data)).encode()+b'\0'+data).digest())
    def encode(node):
        content = bytearray()
        for name,value in sorted(node.items(), key=lambda pair:(pair[0]+('/' if isinstance(pair[1],dict) else '')).encode()):
            mode,digest = (b'40000',encode(value)) if isinstance(value,dict) else value
            content.extend(mode+b' '+name.encode()+b'\0'+digest)
        return hashlib.sha1(b'tree '+str(len(content)).encode()+b'\0'+content).digest()
    return encode(root).hex()

def project(path, includes, symbol='', framework='net8.0', references=(), executable=True):
    items = ''.join('<Compile Include="'+escape(str(p))+'"/>' for p in includes)
    items += ''.join('<Reference Include="'+escape(p.stem)+'"><HintPath>'+escape(str(p))+'</HintPath></Reference>' for p in references)
    path.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>'+framework+'</TargetFramework>'
        '<OutputType>'+('Exe' if executable else 'Library')+'</OutputType><LangVersion>9.0</LangVersion>'
        '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>disable</ImplicitUsings>'
        '<Nullable>disable</Nullable><Optimize>true</Optimize><DefineConstants>'+symbol+'</DefineConstants>'
        '</PropertyGroup><ItemGroup>'+items+'</ItemGroup></Project>')

def row_key(row):
    return (row['operation'], row['size'], row['universe'], row['distribution'])

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--references', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--rounds', type=int, default=5)
    parser.add_argument('--skip-bench', action='store_true')
    args = parser.parse_args()
    if args.rounds < 1: parser.error('rounds must be positive')
    output = pathlib.Path(args.output).resolve(); output.mkdir(parents=True, exist_ok=True)
    references = pathlib.Path(args.references).resolve()
    env = dict(os.environ, DOTNET_TieredCompilation='0', DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1')
    def run(command, log, cwd=ROOT):
        proc = subprocess.run(list(map(str,command)), cwd=cwd, env=env, stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT, text=True, timeout=900)
        (output/log).write_text(proc.stdout, encoding='utf-8')
        print(proc.stdout, flush=True)
        if proc.returncode: raise RuntimeError(log+' failed with '+str(proc.returncode))
        return 0
    def save(name, data):
        (output/name).write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')
    package = verify_package(ROOT)
    save('package-checks.json', package)
    try: head = subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True,stderr=subprocess.DEVNULL).strip()
    except subprocess.CalledProcessError: head = 'uncommitted-working-copy'
    installed = subprocess.check_output([args.dotnet,'--list-sdks'],text=True)
    sdks=[line.split()[0] for line in installed.splitlines() if line.startswith('8.0.') and '-' not in line.split()[0]]
    if not sdks: raise RuntimeError('A .NET 8 SDK is required')
    sdk=max(sdks,key=lambda s:tuple(map(int,s.split('.'))))
    run([args.dotnet,'--info'],'dotnet-info.txt')
    with tempfile.TemporaryDirectory(prefix='integer-audit-') as directory:
        work=pathlib.Path(directory)
        (work/'global.json').write_text(json.dumps({'sdk':{'version':sdk,'rollForward':'disable'}}))
        (work/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
        sources={}
        inventory={}
        for variant,commit in REFS.items():
            archive=references/(variant+'.zip')
            target=work/variant; target.mkdir()
            with zipfile.ZipFile(archive) as z:
                if z.comment.decode('ascii')!=commit: raise RuntimeError('Wrong reference commit: '+variant)
                for member in z.infolist():
                    path=pathlib.PurePosixPath(member.filename)
                    if path.is_absolute() or '..' in path.parts or (member.external_attr >> 16) & 0o170000 == 0o120000:
                        raise RuntimeError('Unsupported reference archive entry')
                if archive_tree(z) != TREES[variant]: raise RuntimeError('Reference bytes do not match pinned Git tree: '+variant)
                z.extractall(target)
            files={p.relative_to(target).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in target.rglob('*') if p.is_file()}
            inventory[variant]={'commit':commit,'archive_sha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'files':files}
            sources[variant]=target
        # Validate original licensing and asset GUIDs byte-for-byte; new metadata is separately checked.
        for old in sources['main'].rglob('*'):
            if old.is_file() and (old.suffix=='.meta' or old.name=='LICENSE'):
                current=ROOT/old.relative_to(sources['main'])
                if not current.is_file() or current.read_bytes()!=old.read_bytes(): raise RuntimeError('Original metadata/license changed: '+str(old))
        inventory['candidate']={'commit':head,'source_digest':package['source_digest'],
            'files':{p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest()
                for folder in ('Runtime','Editor','Samples~','Tests') for p in (ROOT/folder).rglob('*') if p.is_file()}}
        save('source-inventory.json',inventory)
        host=work/'candidate-tests';host.mkdir()
        for name in ('UnityStubs.cs','NativeTestStubs.cs'): shutil.copy2(ROOT/'Audit~'/name,host/name)
        production=[ROOT/(folder+'/**/*.cs') for folder in ('Runtime','Editor','Samples~')]
        project(host/'IntegerTests.csproj',production+[host/'UnityStubs.cs',ROOT/'Audit~/IntegerTests.cs',ROOT/'Audit~/IntegerTestSupport.cs'])
        dll=host/'out/IntegerTests.dll'
        run([args.dotnet,'build',host/'IntegerTests.csproj','-c','Release','-o',host/'out'],'candidate-build.log',host)
        run([args.dotnet,'exec',dll,output/'tests'],'candidate-tests.log',host)
        # Compile and RUN generated bindings, including a member named registry and reserved identifiers.
        fixture=output/'tests/GeneratedBindings.cs'
        harness=host/'BindingsCheck.cs'
        harness.write_text('using System; using System.Reflection; using GameplayTags; class BindingsCheck { static int Main() {'
            'var settings=UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();'
            'UnityEngine.JsonUtility.FromJsonOverwrite("{}",settings);'
            'var method=typeof(GameplayTagSettings).GetMethod("ReplaceAll",BindingFlags.NonPublic|BindingFlags.Instance);'
            'var definitions=new System.Collections.Generic.List<GameplayTagDefinition>();'
            'foreach(var name in new[]{"GameplayTagManager","RuntimeTag","__arglist","registry","Tags","ContentHash","A.B","A_B"}) '
            'definitions.Add(new GameplayTagDefinition(name,"","Default",false,true));'
            'method.Invoke(settings,new object[]{definitions,new System.Collections.Generic.List<GameplayTagRedirect>(),'
            'new System.Collections.Generic.List<GameplayTagSource>{new GameplayTagSource("Default","",false)}});'
            'var registry=TagRegistry.Create(settings); var binding=new Generated.Tags(registry); int count=0;'
            'foreach(var field in typeof(Generated.Tags).GetFields(BindingFlags.Public|BindingFlags.Instance)) {'
            'var tag=(RuntimeTag)field.GetValue(binding); if(!ReferenceEquals(tag.Registry,registry)) throw new Exception("Binding ownership"); count++;}'
            'if(count<8) throw new Exception("Missing binding fields"); Console.WriteLine("BINDINGS PASS fields="+count); return 0; }}')
        # No native JSON simulation is required to fill settings; reflection is confined to the test harness.
        harness.write_text(harness.read_text().replace('UnityEngine.JsonUtility.FromJsonOverwrite("{}",settings);',''))
        project(host/'BindingsCheck.csproj',[fixture,harness],references=[dll])
        run([args.dotnet,'build',host/'BindingsCheck.csproj','-c','Release','-p:BaseIntermediateOutputPath=obj-bindings/','-o',host/'bindings'],'generated-build.log',host)
        run([args.dotnet,'exec',host/'bindings/BindingsCheck.dll'],'generated-run.log',host)
        save('documentation.json',compile_documentation(ROOT,host,dll,args.dotnet,run))
        save('split-assemblies.json',compile_split_assemblies(ROOT,host,args.dotnet,run))
        project(host/'IntegerCosts.csproj',[ROOT/'Runtime/**/*.cs',host/'UnityStubs.cs',ROOT/'Audit~/IntegerCosts.cs'])
        run([args.dotnet,'build',host/'IntegerCosts.csproj','-c','Release','-p:BaseIntermediateOutputPath=obj-costs/','-o',host/'costs'],'costs-build.log',host)
        run([args.dotnet,'exec',host/'costs/IntegerCosts.dll',output/'cold-memory.json'],'cold-memory.log',host)
        builds={}
        for variant,root in list(sources.items())+[('candidate',ROOT)]:
            build=work/('bench-'+variant);build.mkdir()
            includes=[ROOT/'Audit~/FourWay.cs']
            if variant=='alex':
                includes += [root/'Runtime'/name for name in ALEX_CORE]+[ROOT/'Audit~/AlexDependencies.cs']
                symbol='ALEX'
            else:
                includes += [root/'Runtime/**/*.cs',ROOT/'Audit~/UnityStubs.cs']
                symbol='CANDIDATE' if variant=='candidate' else ''
            project(build/'Bench.csproj',includes,symbol)
            run([args.dotnet,'build',build/'Bench.csproj','-c','Release','-o',build/'out'],variant+'-benchmark-build.log',build)
            builds[variant]=(build/'out/Bench.dll',build)
        variants=['main','optimize','alex','candidate','candidate-sparse','candidate-dense']
        if not args.skip_bench:
            for round_id in range(args.rounds):
                order=variants[round_id%len(variants):]+variants[:round_id%len(variants)]
                if round_id%2: order.reverse()
                for variant in order:
                    executable,cwd=builds['candidate' if variant.startswith('candidate') else variant]
                    mode='Sparse' if variant.endswith('-sparse') else 'Dense' if variant.endswith('-dense') else 'Auto'
                    run([args.dotnet,'exec',executable,output/(variant+'-r'+str(round_id)+'.json'),variant,mode],
                        variant+'-r'+str(round_id)+'.log',cwd)
        summary={'head':head,'source_digest':package['source_digest'],'references':REFS,'sdk':sdk,
            'managed_checks':'passed','documentation_checks':'passed','split_assembly_checks':'passed','native_test_source':'provided_not_executed','unity_editor':'not_run','android_il2cpp':'not_run','ios_il2cpp':'not_run','release_approved':False,
            'rounds':0 if args.skip_bench else args.rounds,'timing_note':'Seven raw samples per process; same fixture and rotating fresh processes. Ratios are not statistical significance claims.'}
        if not args.skip_bench:
            table={}; expected_keys=None;input_digests={};candidate_failures=[]
            for variant in variants:
                table[variant]={}
                for round_id in range(args.rounds):
                    payload=json.loads((output/(variant+'-r'+str(round_id)+'.json')).read_text())
                    keys=[row_key(r) for r in payload['rows']]
                    if len(set(keys))!=len(keys): raise RuntimeError('Duplicate benchmark rows')
                    if expected_keys is None: expected_keys=set(keys)
                    if set(keys)!=expected_keys: raise RuntimeError('Missing benchmark rows in '+variant)
                    for row in payload['rows']:
                        key=row_key(row)
                        if key in input_digests and row['inputDigest']!=input_digests[key]: raise RuntimeError('Inputs differ: '+str(key))
                        input_digests[key]=row['inputDigest']
                        entry=table[variant].setdefault(key,{'rounds':[]})
                        entry['rounds'].append(row)
                        if variant.startswith('candidate') and row['status']!='measured':candidate_failures.append({'variant':variant,'key':key,'error':row.get('error')})
                for item in table[variant].values():
                    rows=item['rounds'];item['status']='measured' if all(r['status']=='measured' for r in rows) else 'failed'
                    if item['status']=='measured':
                        item['median_ns']=statistics.median(statistics.median(r['ns']) for r in rows)
                        item['allocated_bytes']=statistics.median(statistics.median(r['allocated_bytes']) for r in rows)
                        item['input_buffer_bytes']=rows[0]['input_member_buffer_bytes']
                        item['input_storage']=rows[0]['input_storage']
            comparisons=[]; headlines=[]
            for key in sorted(expected_keys):
                entry={'operation':key[0],'size':key[1],'universe':key[2],'distribution':key[3],
                    'variants':{v:table[v][key] for v in variants}}
                refs=[table[v][key] for v in ('main','optimize','alex')]
                if all(r['status']=='measured' for r in refs) and table['candidate'][key]['status']=='measured':
                    best=min(r['median_ns'] for r in refs)
                    ratio=table['candidate'][key]['median_ns']/best
                    entry.update(candidate_over_best_reference=ratio,candidate_first=ratio<1,margin_at_least_5_percent=ratio<=.95)
                else:entry['candidate_first']=None
                comparisons.append(entry)
                if key[1]==1024 and key[2]==10000 and key[0] in ('exact','union','copy_append','copy_remove'):headlines.append(entry)
            save('comparison.json',comparisons)
            summary['headline_rows']=len(headlines)
            summary['headline_all_first']=len(headlines)==8 and all(r.get('candidate_first') is True for r in headlines)
            summary['headline_all_5_percent_margin']=len(headlines)==8 and all(r.get('margin_at_least_5_percent') is True for r in headlines)
            summary['candidate_failures']=candidate_failures
            summary['full_matrix_candidate_not_first']=[{k:r[k] for k in ('operation','size','universe','distribution','candidate_first')} for r in comparisons if r.get('candidate_first') is not True]
            lines=['# Four-way measured results','', '| operation | n | U definitions | pattern | Z main ns | Z optimize ns | Alex ns | candidate Auto ns | Auto/reference best |','|---|---:|---:|---|---:|---:|---:|---:|---:|']
            for entry in comparisons:
                values=[str(round(entry['variants'][v]['median_ns'],3)) if entry['variants'][v]['status']=='measured' else 'FAILED' for v in ('main','optimize','alex','candidate')]
                lines.append('| '+ ' | '.join([entry['operation'],str(entry['size']),str(entry['universe']),entry['distribution']]+values+[str(round(entry.get('candidate_over_best_reference',0),4)) if entry.get('candidate_first') is not None else 'N/A'])+' |')
            (output/'COMPARISON.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
            print('HEADLINE n=1024 U=10000 explicit definitions; all are ns/operation',flush=True)
            for entry in headlines:
                print(entry['distribution'],entry['operation'], {v:round(entry['variants'][v].get('median_ns',-1),3) for v in variants},flush=True)
            if candidate_failures:
                summary['managed_checks']='failed';save('summary.json',summary);raise RuntimeError('Candidate benchmark correctness failed')
        save('summary.json',summary);print(json.dumps(summary,indent=2),flush=True)
    return 0

if __name__=='__main__':raise SystemExit(main())
