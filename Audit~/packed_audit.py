#!/usr/bin/env python3
"""Packed bitmap audit. No shipping source rewriting; no automatic performance or native approval."""
import argparse, hashlib, json, math, os, pathlib, shutil, statistics, subprocess, tempfile, zipfile
from integer_audit import project, archive_tree, REFS, TREES, ALEX_CORE
from checks import compile_split_assemblies, source_digest
ROOT = pathlib.Path(__file__).resolve().parent.parent
AUTO = '84729cc3b6e1b4681328806df2d7c8a1d8f568f6'
AUTO_TREE = 'f0131462eb38065f833f898b7123e41dda9e0978'


def fixture(text, packed=False, prepared=False):
    text = text.replace('private static void BenchmarkUniverse(int universe)', 'private static void BenchmarkUniverse(int universe, int[] sizes = null)')
    text = text.replace('foreach (int size in new[] { 0, 8, 128, 1024, 4096 })', 'foreach (int size in sizes ?? new[] { 0, 8, 128, 1024, 4096 })')
    text = text.replace('BenchmarkUniverse(65536);', 'BenchmarkUniverse(65536);\n        BenchmarkUniverse(262144, new[] { 1, 8, 32, 128, 1024, 4096 });')
    # Identical finalizer/GC preparation for every library; no subtraction or sample removal.
    text = text.replace('var allocations = new double[7];', 'var allocations = new double[7];\n            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();')
    if prepared:
        text = text.replace('private static int checksum;', 'private static int checksum;\n    private static Set work;')
        text = text.replace('Set a = Input(x), b = Input(y), subset = Input(removed);', 'Set a = Input(x), b = Input(y), subset = Input(removed);\n                work = new Set(registry, Math.Max(1, size * 2), storage);')
        text = text.replace('return Set.Union(a, b, storage);', 'Set.UnionInto(a, b, work); return work;')
        text = text.replace('var result = new Set(a);', 'var result = work; result.CopyFrom(a);')
        for a,b in [('"union"','"union_into"'),('"copy_append"','"copy_append_reuse"'),('"copy_remove"','"copy_remove_reuse"')]: text=text.replace(a,b)
    if packed:
        text = text.replace('private static TagSetStorage storage;', '')
        text = text.replace(', storage)', ')')
        text = text.replace('return set.Storage.ToString();', 'return "ranked-packed-bitmap";')
        text = text.replace('storage = args.Length > 2 ? (TagSetStorage)Enum.Parse(typeof(TagSetStorage), args[2]) : TagSetStorage.Auto;', '')
    return text


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--references',required=True); p.add_argument('--output',required=True)
    p.add_argument('--rounds',type=int,default=3); p.add_argument('--smoke',action='store_true'); p.add_argument('--dotnet',default='dotnet')
    args=p.parse_args()
    if args.rounds<1:p.error('rounds must be positive')
    output=pathlib.Path(args.output).resolve();output.mkdir(parents=True,exist_ok=True)
    env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1')
    def save(name,value): (output/name).write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
    def run(cmd,log,cwd):
        x=subprocess.run(list(map(str,cmd)),cwd=cwd,env=env,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=1200)
        (output/log).write_text(x.stdout,encoding='utf-8');print(x.stdout,flush=True)
        if x.returncode:raise RuntimeError(log+' failed: '+str(x.returncode))
        return 0
    with tempfile.TemporaryDirectory(prefix='packed-bitmap-') as directory:
        work=pathlib.Path(directory)
        (work/'global.json').write_text('{"sdk":{"version":"8.0.425","rollForward":"disable"}}')
        (work/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
        host=work/'tests';host.mkdir()
        production=[ROOT/(d+'/**/*.cs') for d in ('Runtime','Editor','Samples~')]
        project(host/'Audit.csproj',production+[ROOT/'Audit~/UnityStubs.cs',ROOT/'Audit~/PackedTests.cs'])
        run([args.dotnet,'build',host/'Audit.csproj','-c','Release','-o',host/'out'],'packed-build.log',host)
        dll=host/'out/Audit.dll'
        run([args.dotnet,'exec',dll,output/'packed-tests.json'],'packed-tests.log',host)
        if args.smoke:
            save('summary.json',{'managed_checks':'passed','smoke_only':True,'release_approved':False});return
        for name in ['UnityStubs.cs','NativeTestStubs.cs']:shutil.copy2(ROOT/'Audit~'/name,host/name)
        save('split-assemblies.json',compile_split_assemblies(ROOT,host,args.dotnet,run))
        sources={};inventory={}
        for variant,commit in dict(REFS,auto=AUTO).items():
            zpath=pathlib.Path(args.references)/(variant+'.zip');target=work/variant;target.mkdir()
            with zipfile.ZipFile(zpath) as z:
                expected=AUTO_TREE if variant=='auto' else TREES[variant]
                if z.comment.decode('ascii')!=commit or archive_tree(z)!=expected:raise RuntimeError('Wrong pinned reference '+variant)
                for entry in z.infolist():
                    path=pathlib.PurePosixPath(entry.filename)
                    if path.is_absolute() or '..' in path.parts or (entry.external_attr>>16)&0o170000==0o120000:raise RuntimeError('Unsafe archive')
                z.extractall(target)
            sources[variant]=target;inventory[variant]={'commit':commit,'tree':expected,'zip_sha256':hashlib.sha256(zpath.read_bytes()).hexdigest()}
        for old in sources['main'].rglob('*'):
            if old.is_file() and (old.suffix=='.meta' or old.name=='LICENSE'):
                if (ROOT/old.relative_to(sources['main'])).read_bytes()!=old.read_bytes():raise RuntimeError('Changed original GUID/license')
        for f in (ROOT/'Runtime').glob('*.cs'):
            if 'TagSetStorage' in f.read_text() or 'm_Ids' in f.read_text():raise RuntimeError('Legacy Sparse selector/member array remains')
        inventory['candidate']={'commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'source_digest':source_digest(ROOT)}
        save('inventory.json',inventory)
        builds={};original=(ROOT/'Audit~/FourWay.cs').read_text()
        for variant,src in list(sources.items())+[('candidate',ROOT)]:
            h=work/('bench-'+variant);h.mkdir();code=fixture(original,packed=variant=='candidate')
            (h/'Bench.cs').write_text(code);(output/('fixture-'+variant+'.cs')).write_text(code)
            includes=[src/'Runtime'/n for n in ALEX_CORE]+[ROOT/'Audit~/AlexDependencies.cs'] if variant=='alex' else [src/'Runtime/**/*.cs',ROOT/'Audit~/UnityStubs.cs']
            symbol='ALEX' if variant=='alex' else 'CANDIDATE' if variant in ('auto','candidate') else ''
            project(h/'Bench.csproj',includes+[h/'Bench.cs'],symbol)
            run([args.dotnet,'build',h/'Bench.csproj','-c','Release','-o',h/'out'],variant+'-build.log',h)
            builds[variant]=(h/'out/Bench.dll',h)
            if variant in ('auto','candidate'):
                (h/'Prepared.cs').write_text(fixture(original,packed=variant=='candidate',prepared=True))
                project(h/'Prepared.csproj',includes+[h/'Prepared.cs'],symbol)
                run([args.dotnet,'build',h/'Prepared.csproj','-c','Release','-p:BaseIntermediateOutputPath=obj-prepared/','-o',h/'prepared'],variant+'-prepared-build.log',h)
                builds[variant+'-prepared']=(h/'prepared/Prepared.dll',h)
        variants=['main','optimize','alex','auto','flat-dense','candidate']
        for round in range(args.rounds):
            order=variants[round%len(variants):]+variants[:round%len(variants)]
            if round%2:order.reverse()
            for variant in order:
                dll,cwd=builds['auto' if variant=='flat-dense' else variant]
                run([args.dotnet,'exec',dll,output/(variant+'-r'+str(round)+'.json'),variant,'Dense' if variant=='flat-dense' else 'Auto'],variant+'-r'+str(round)+'.log',cwd)
            for variant in (['auto','candidate'] if round%2==0 else ['candidate','auto']):
                dll,cwd=builds[variant+'-prepared']
                run([args.dotnet,'exec',dll,output/('prepared-'+variant+'-r'+str(round)+'.json'),'prepared-'+variant,'Auto'],'prepared-'+variant+'-r'+str(round)+'.log',cwd)
        def compare(prefix,which):
            tables={};keys=None;digests={}
            for variant in which:
                table={}
                for round in range(args.rounds):
                    data=json.loads((output/(prefix+variant+'-r'+str(round)+'.json')).read_text());seen=set()
                    for row in data['rows']:
                        key=(row['operation'],row['size'],row['universe'],row['distribution'])
                        if key in seen:raise RuntimeError('Duplicate benchmark row')
                        seen.add(key)
                        if key in digests and digests[key]!=row['inputDigest']:raise RuntimeError('Input mismatch')
                        digests[key]=row['inputDigest'];table.setdefault(key,[]).append(row)
                        if row['status']=='measured':
                            if len(row['ns'])!=7 or len(row['allocated_bytes'])!=7:raise RuntimeError('Missing samples')
                            if any(not math.isfinite(x) or x<=0 for x in row['ns']):raise RuntimeError('Invalid timing')
                            if any(not math.isfinite(x) or x<0 for x in row['allocated_bytes']):raise RuntimeError('Invalid allocation')
                            if prefix and any(x!=0 for x in row['allocated_bytes']):raise RuntimeError('Prepared operation allocated')
                        elif variant=='candidate':raise RuntimeError('Candidate benchmark correctness failed: '+str(row))
                    if keys is None:keys=seen
                    if seen!=keys:raise RuntimeError('Missing benchmark cases')
                tables[variant]=table
            result=[]
            for key in sorted(keys):
                metrics={}
                for variant in which:
                    raw=tables[variant][key];ok=all(r['status']=='measured' for r in raw)
                    metrics[variant]={'status':'measured' if ok else 'failed','rounds':raw}
                    if ok:metrics[variant].update(median_ns=statistics.median(statistics.median(r['ns']) for r in raw),bytes_per_op=statistics.median(statistics.median(r['allocated_bytes']) for r in raw))
                good=all(v['status']=='measured' for v in metrics.values())
                ratio=metrics['candidate']['median_ns']/min(v['median_ns'] for k,v in metrics.items() if k!='candidate') if good else None
                result.append({'operation':key[0],'members':key[1],'definitions':key[2],'distribution':key[3],'ratio_to_fastest':ratio,'first':ratio is not None and ratio<1,'variants':metrics})
            return result
        comparison=compare('',variants);prepared=compare('prepared-',['auto','candidate'])
        save('comparison.json',comparison);save('prepared-comparison.json',prepared)
        headline=[r for r in comparison if r['operation']!='exact_miss' and r['definitions']==262144 and r['members'] in (1,8,32,128)]
        summary={'managed_checks':'passed','source_digest':source_digest(ROOT),'protocol':'settled-setup-v2','rounds':args.rounds,
            'total':len(comparison),'wins':sum(r['first'] for r in comparison),'prepared_total':len(prepared),'prepared_wins':sum(r['first'] for r in prepared),
            'large_small_total':len(headline),'large_small_wins':sum(r['first'] for r in headline),'all_targets_met':all(r['first'] for r in comparison),
            'release_approved':False,'native_unity':'not_run','il2cpp':'not_run',
            'non_winners':[{k:v for k,v in r.items() if k!='variants'} for r in comparison if not r['first']]}
        save('summary.json',summary);print(json.dumps(summary,indent=2),flush=True)
        for universe,size in [(10000,1024),(262144,1),(262144,8),(262144,128)]:
            for r in comparison:
                if r['definitions']==universe and r['members']==size and r['operation']!='exact_miss':
                    print('RESULT',r['operation'],universe,size,r['distribution'],json.dumps({k:{x:y for x,y in v.items() if x!='rounds'} for k,v in r['variants'].items()}),flush=True)
if __name__=='__main__':main()
