#!/usr/bin/env python3
"""Dense-only audit. Shipping code is never rewritten in the test host. All non-winners remain visible."""
import argparse, hashlib, json, os, pathlib, shutil, statistics, subprocess, tempfile, zipfile
from integer_audit import archive_tree, project, REFS, TREES, ALEX_CORE
from checks import verify_package, compile_documentation, compile_split_assemblies

ROOT = pathlib.Path(__file__).resolve().parent.parent
AUTO = '84729cc3b6e1b4681328806df2d7c8a1d8f568f6'
AUTO_TREE = 'f0131462eb38065f833f898b7123e41dda9e0978'

def legacy_dense_fixture(text):
    """Migrate the former 2x2x2 storage test dimension to one Dense implementation, not fake Sparse support."""
    changes = []
    def change(old, new):
        nonlocal text
        if old not in text: raise RuntimeError('Legacy fixture changed; reconcile adaptation: '+old)
        changes.append({'old':old,'new':new,'occurrences':text.count(old)})
        text = text.replace(old,new)
    change('private static readonly TagSetStorage[] Modes = { TagSetStorage.Sparse, TagSetStorage.Dense };',
           'private enum FixtureStorage { Dense }\n    private static readonly FixtureStorage[] Modes = { FixtureStorage.Dense };')
    change('Throws<ArgumentOutOfRangeException>(() => new RuntimeTagSet(large, 0, (TagSetStorage)99));',
           'Check(typeof(RuntimeTagSet).Assembly.GetType("GameplayTags.TagSetStorage") == null);')
    change('TagSetStorage', 'FixtureStorage')
    # Correct the string reflection target altered by the mechanical type rename.
    change('GameplayTags.FixtureStorage', 'GameplayTags.TagSetStorage')
    change('FixtureStorage.Sparse', 'FixtureStorage.Dense')
    change('FixtureStorage mode, int capacity = 0', 'FixtureStorage mode = FixtureStorage.Dense, int capacity = 0')
    change(', FixtureStorage.Dense)', ')')
    for capacity,mode in [('Math.Max(capacity, input.Length)','mode'),('registry.Count','mode'),
                           ('registry.Count','outputMode'),('0','mode'),('registry.Count','rm')]:
        change('new RuntimeTagSet(registry, '+capacity+', '+mode+')','new RuntimeTagSet(registry, '+capacity+')')
    change('Modes[random.Next(2)]','Modes[random.Next(Modes.Length)]')
    change('Check(set.Storage == mode, "storage changed implicitly");',
           'Check(set.Capacity == 0 || set.Capacity == registry.Count, "invalid Dense reservation");')
    change('Check(new RuntimeTagSet(large, 8).Storage == FixtureStorage.Dense);','Check(new RuntimeTagSet(large, 8).Capacity == large.Count);')
    change('Check(new RuntimeTagSet(large, 1024).Storage == FixtureStorage.Dense);','Check(new RuntimeTagSet(large, 1024).Capacity == large.Count);')
    change('BufferBytes == 1256','BufferBytes == 1288')
    change('empty.Capacity == 0 && one.Capacity == 1','empty.Capacity == 0 && one.Capacity == registry.Count')
    change('eight-storage-combinations-exhaustive-six-element-sets','dense-exhaustive-six-element-sets-and-aliases')
    change('continuous-random-mutations-and-explicit-storage','continuous-random-mutations-and-dense-reservation')
    change('frozen-query-random-reference-and-both-storages','frozen-query-random-reference-dense')
    return text, changes

def benchmark_fixture(text, dense=False, prepared=False):
    """Same operations/data for all libraries; adapters only bridge published API differences."""
    text = text.replace('private static void BenchmarkUniverse(int universe)',
                        'private static void BenchmarkUniverse(int universe, int[] requestedSizes = null)')
    text = text.replace('foreach (int size in new[] { 0, 8, 128, 1024, 4096 })',
                        'foreach (int size in requestedSizes ?? new[] { 0, 8, 128, 1024, 4096 })')
    text = text.replace('BenchmarkUniverse(65536);',
                        'BenchmarkUniverse(65536);\n        BenchmarkUniverse(262144, new[] { 1, 8, 32, 128, 1024, 4096 });')
    if prepared:
        text = text.replace('private static int checksum;', 'private static int checksum;\n    private static Set work;')
        text = text.replace('Set a = Input(x), b = Input(y), subset = Input(removed);',
                            'Set a = Input(x), b = Input(y), subset = Input(removed);\n                work = new Set(registry, Math.Max(1, size * 2), storage);')
        text = text.replace('return Set.Union(a, b, storage);', 'Set.UnionInto(a, b, work); return work;')
        text = text.replace('var result = new Set(a);', 'var result = work; result.CopyFrom(a);')
        text = text.replace('Measure("union",', 'Measure("union_into",')
        text = text.replace('Measure("copy_append",', 'Measure("copy_append_reuse",')
        text = text.replace('Measure("copy_remove",', 'Measure("copy_remove_reuse",')
        text = text.replace('Union and both copy/mutate operations allocate independent results; no pooling or fusion.',
                            'Prepared supplement: output allocation is outside timing for ALL compared runtimes; copy and mutation still both execute. Not a substitute for allocating comparisons.')
    if dense:
        text = text.replace('private static TagSetStorage storage;', '')
        text = text.replace(', storage)', ')')
        text = text.replace('return set.Storage.ToString();', 'return "Dense-indexed";')
        text = text.replace('storage = args.Length > 2 ? (TagSetStorage)Enum.Parse(typeof(TagSetStorage), args[2]) : TagSetStorage.Auto;', '')
    return text

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--references',required=True)
    parser.add_argument('--output',required=True)
    parser.add_argument('--dotnet',default='dotnet')
    parser.add_argument('--rounds',type=int,default=3)
    parser.add_argument('--skip-bench',action='store_true')
    args=parser.parse_args()
    if args.rounds<1: parser.error('rounds must be positive')
    output=pathlib.Path(args.output).resolve();output.mkdir(parents=True,exist_ok=True)
    env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1')
    def save(name,data):
        (output/name).write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
    def run(command,log,cwd=ROOT):
        p=subprocess.run(list(map(str,command)),cwd=cwd,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,timeout=1200)
        (output/log).write_text(p.stdout,encoding='utf-8');print(p.stdout,flush=True)
        if p.returncode: raise RuntimeError(log+' failed: '+str(p.returncode))
        return 0
    package=verify_package(ROOT);save('package-checks.json',package)
    for path in (ROOT/'Runtime').glob('*.cs'):
        if 'TagSetStorage' in path.read_text() or 'm_Ids' in path.read_text():
            raise RuntimeError('Sparse/Auto shipping API remains: '+str(path))
    head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
    sdks=[line.split()[0] for line in subprocess.check_output([args.dotnet,'--list-sdks'],text=True).splitlines() if line.startswith('8.0.') and '-' not in line.split()[0]]
    sdk=max(sdks,key=lambda s:tuple(map(int,s.split('.'))))
    with tempfile.TemporaryDirectory(prefix='dense-audit-') as temp:
        work=pathlib.Path(temp)
        (work/'global.json').write_text(json.dumps({'sdk':{'version':sdk,'rollForward':'disable'}}))
        (work/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
        sources={};inventory={}
        commits=dict(REFS,auto=AUTO);trees=dict(TREES,auto=AUTO_TREE)
        for name,commit in commits.items():
            archive=pathlib.Path(args.references)/(name+'.zip');target=work/name;target.mkdir()
            with zipfile.ZipFile(archive) as z:
                if z.comment.decode('ascii')!=commit or archive_tree(z)!=trees[name]: raise RuntimeError('Wrong reference '+name)
                for entry in z.infolist():
                    p=pathlib.PurePosixPath(entry.filename)
                    if p.is_absolute() or '..' in p.parts or (entry.external_attr>>16)&0o170000==0o120000: raise RuntimeError('Unsafe reference entry')
                z.extractall(target)
            sources[name]=target
            inventory[name]={'commit':commit,'tree':trees[name],'archive_sha256':hashlib.sha256(archive.read_bytes()).hexdigest()}
        for path in sources['main'].rglob('*'):
            if path.is_file() and (path.suffix=='.meta' or path.name=='LICENSE'):
                current=ROOT/path.relative_to(sources['main'])
                if current.read_bytes()!=path.read_bytes(): raise RuntimeError('Original metadata/license changed')
        inventory['candidate']={'head':head,'source_digest':package['source_digest']}
        save('source-inventory.json',inventory)
        host=work/'tests';host.mkdir()
        for name in ['UnityStubs.cs','NativeTestStubs.cs']:shutil.copy2(ROOT/'Audit~'/name,host/name)
        migrated,adaptations=legacy_dense_fixture((ROOT/'Audit~/IntegerTests.cs').read_text(encoding='utf-8'))
        (host/'DenseLegacyTests.cs').write_text(migrated,encoding='utf-8')
        (output/'DenseLegacyTests.cs').write_text(migrated,encoding='utf-8')
        save('legacy-fixture-adaptations.json',adaptations)
        production=[ROOT/(d+'/**/*.cs') for d in ('Runtime','Editor','Samples~')]
        project(host/'Audit.csproj',production+[host/'UnityStubs.cs',host/'DenseLegacyTests.cs',ROOT/'Audit~/IntegerTestSupport.cs'])
        run([args.dotnet,'build',host/'Audit.csproj','-c','Release','-o',host/'out'],'legacy-build.log',host)
        dll=host/'out/Audit.dll'
        run([args.dotnet,'exec',dll,output/'legacy-tests'],'legacy-tests.log',host)
        project(host/'DenseIndex.csproj',[ROOT/'Runtime/**/*.cs',host/'UnityStubs.cs',ROOT/'Audit~/DenseIndexTests.cs'])
        run([args.dotnet,'build',host/'DenseIndex.csproj','-c','Release','-p:BaseIntermediateOutputPath=obj-index/','-o',host/'index'],'index-build.log',host)
        run([args.dotnet,'exec',host/'index/DenseIndex.dll',output/'index-tests.json'],'index-tests.log',host)
        save('documentation.json',compile_documentation(ROOT,host,dll,args.dotnet,run))
        save('split-assemblies.json',compile_split_assemblies(ROOT,host,args.dotnet,run))
        fixture=output/'legacy-tests/GeneratedBindings.cs'
        project(host/'Generated.csproj',[fixture],references=[dll],executable=False)
        run([args.dotnet,'build',host/'Generated.csproj','-c','Release','-p:BaseIntermediateOutputPath=obj-generated/','-o',host/'generated'],'generated-build.log',host)
        run([args.dotnet,'--info'],'dotnet-info.txt',host)
        builds={}
        original=(ROOT/'Audit~/FourWay.cs').read_text(encoding='utf-8')
        for variant,root in list(sources.items())+[('candidate',ROOT)]:
            build=work/('bench-'+variant);build.mkdir()
            fixture=build/'Bench.cs';fixture.write_text(benchmark_fixture(original,dense=variant=='candidate'),encoding='utf-8')
            (output/('fixture-'+variant+'.cs')).write_text(fixture.read_text(),encoding='utf-8')
            if variant=='alex': includes=[root/'Runtime'/n for n in ALEX_CORE]+[ROOT/'Audit~/AlexDependencies.cs'];symbol='ALEX'
            else: includes=[root/'Runtime/**/*.cs',ROOT/'Audit~/UnityStubs.cs'];symbol='CANDIDATE' if variant in ('auto','candidate') else ''
            project(build/'Bench.csproj',includes+[fixture],symbol)
            run([args.dotnet,'build',build/'Bench.csproj','-c','Release','-o',build/'out'],variant+'-build.log',build)
            builds[variant]=(build/'out/Bench.dll',build)
            if variant in ('auto','candidate'):
                prepared=build/'Prepared.cs';prepared.write_text(benchmark_fixture(original,dense=variant=='candidate',prepared=True),encoding='utf-8')
                (output/('fixture-prepared-'+variant+'.cs')).write_text(prepared.read_text(),encoding='utf-8')
                project(build/'Prepared.csproj',includes+[prepared],symbol)
                run([args.dotnet,'build',build/'Prepared.csproj','-c','Release','-p:BaseIntermediateOutputPath=obj-prepared/','-o',build/'prepared'],variant+'-prepared-build.log',build)
                builds[variant+'-prepared']=(build/'prepared/Prepared.dll',build)
        variants=['main','optimize','alex','auto','flat-dense','candidate']
        prepared_variants=['auto','flat-dense','candidate']
        if not args.skip_bench:
            for r in range(args.rounds):
                order=variants[r%len(variants):]+variants[:r%len(variants)]
                if r%2:order.reverse()
                for variant in order:
                    dll,cwd=builds['auto' if variant=='flat-dense' else variant]
                    run([args.dotnet,'exec',dll,output/(variant+'-r'+str(r)+'.json'),variant,'Dense' if variant=='flat-dense' else 'Auto'],variant+'-r'+str(r)+'.log',cwd)
                order=prepared_variants[r%3:]+prepared_variants[:r%3]
                for variant in order:
                    dll,cwd=builds[('auto' if variant=='flat-dense' else variant)+'-prepared']
                    run([args.dotnet,'exec',dll,output/('prepared-'+variant+'-r'+str(r)+'.json'),'prepared-'+variant,'Dense' if variant=='flat-dense' else 'Auto'], 'prepared-'+variant+'-r'+str(r)+'.log',cwd)
        def comparison(prefix,variants):
            tables={};expected=None;digests={}
            for variant in variants:
                table={}
                for r in range(args.rounds):
                    payload=json.loads((output/(prefix+variant+'-r'+str(r)+'.json')).read_text())
                    keys=[]
                    for row in payload['rows']:
                        key=(row['operation'],row['size'],row['universe'],row['distribution']);keys.append(key)
                        if key in digests and digests[key]!=row['inputDigest']:raise RuntimeError('Different benchmark inputs')
                        digests[key]=row['inputDigest'];table.setdefault(key,[]).append(row)
                    if len(keys)!=len(set(keys)):raise RuntimeError('Duplicate row')
                    if expected is None:expected=set(keys)
                    if set(keys)!=expected:raise RuntimeError('Missing row')
                tables[variant]=table
            rows=[]
            for key in sorted(expected):
                data={}
                for variant in variants:
                    samples=tables[variant][key]
                    ok=all(s['status']=='measured' for s in samples)
                    data[variant]={'status':'measured' if ok else 'failed','rounds':samples}
                    if ok:data[variant].update(median_ns=statistics.median(statistics.median(s['ns']) for s in samples),
                        bytes_per_operation=statistics.median(statistics.median(s['allocated_bytes']) for s in samples),buffer_bytes=samples[0]['input_member_buffer_bytes'])
                own=data['candidate'];refs=[data[v] for v in variants if v!='candidate']
                ok=own['status']=='measured' and all(x['status']=='measured' for x in refs)
                ratio=own['median_ns']/min(x['median_ns'] for x in refs) if ok else None
                rows.append({'operation':key[0],'members':key[1],'definitions':key[2],'distribution':key[3],
                    'first':ratio is not None and ratio<1,'ratio_to_fastest':ratio,'variants':data})
            return rows
        summary={'head':head,'source_digest':package['source_digest'],'sdk':sdk,'managed_checks':'passed',
                 'unity_editor':'not_run','android_il2cpp':'not_run','ios_il2cpp':'not_run','release_approved':False,
                 'rounds':0 if args.skip_bench else args.rounds,'all_performance_targets_met':False}
        if not args.skip_bench:
            full=comparison('',variants);prepared=comparison('prepared-',prepared_variants)
            save('comparison.json',full);save('prepared-comparison.json',prepared)
            slim=lambda row:{k:row[k] for k in ('operation','members','definitions','distribution','ratio_to_fastest')}
            summary.update(allocating_cases=len(full),allocating_wins=sum(r['first'] for r in full),
                           prepared_cases=len(prepared),prepared_wins=sum(r['first'] for r in prepared),
                           allocating_non_winners=[slim(r) for r in full if not r['first']],
                           prepared_non_winners=[slim(r) for r in prepared if not r['first']])
            summary['all_performance_targets_met']=all(r['first'] for r in full+prepared)
            selected=[r for r in full if (r['definitions'],r['members']) in ((262144,8),(10000,1024),(65536,4096)) and r['operation']!='exact_miss']
            save('headline.json',selected)
            print('SELECTED (ns/op; measured per process, not native Unity):',flush=True)
            for row in selected:print(json.dumps(dict(slim(row),ns={v:round(d.get('median_ns',0),3) for v,d in row['variants'].items()},bytes={v:d.get('bytes_per_operation') for v,d in row['variants'].items()})),flush=True)
        save('summary.json',summary);print(json.dumps(summary,ensure_ascii=False,indent=2),flush=True)
    return 0

if __name__=='__main__':raise SystemExit(main())
