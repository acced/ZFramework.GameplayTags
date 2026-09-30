#!/usr/bin/env python3
"""Bitmap-record + dense pilot. Literal reference sources; no production migration or automatic approval."""
import argparse, csv, hashlib, json, os, pathlib, shutil, statistics, subprocess, sys, tempfile, zipfile
from integer_audit import project, archive_tree, ALEX_CORE
ROOT = pathlib.Path(__file__).resolve().parent.parent
PINS = {
 'main': ('934b14a47ddf0b6367bf6720be58c18cbcb09896','936bb858731b7ae0d1ec0a087de8afe1d96b1b40'),
 'optimize': ('a99f53d4697df6f577961ff1ee1bad14caa221d1','8a593dd469e4dbf6e182cc4c3d48582c39b73c94'),
 'alex': ('28e4218f459ecf0e9fb4f0bca90805c05eb962c2','530e7109d534ee358f601e312bac7e3b08e129d4'),
 'auto': ('84729cc3b6e1b4681328806df2d7c8a1d8f568f6','f0131462eb38065f833f898b7123e41dda9e0978'),
 'indexed': ('3781d1b6c66997ec37fcb6223cb1a1d60c7e19fb','b1e2d7d91dc49512ad2edfcaae6417c6a0b0fbb2'),
 'packed': ('4c5cada6faa8635a69d31a7a074bd79646bafee0','4aec56182c8079f4b59cee77000e9603c7e46bf4'),
}
CONFIGS = {'micro16':'', 'scan16':'MICRO_SCAN8', 'exact16':'MICRO_EXACT', 'micro32':'MICRO_WIDE'}
PRIMARY = {'exact','union','copy_append','copy_remove','union_into','copy_append_reuse','copy_remove_reuse'}

def fixture(original, kind, prepared=False, focus=False):
    t=original
    t=t.replace('foreach (int size in new[] { 0, 8, 128, 1024, 4096 })','foreach (int size in new[] { 0, 1, 8, 32, 128, 1024, 4096 })')
    t=t.replace('BenchmarkUniverse(65536);','BenchmarkUniverse(65536);\n        BenchmarkUniverse(262144);')
    if focus:
        t=t.replace('BenchmarkUniverse(10000);','').replace('BenchmarkUniverse(65536);','')
        t=t.replace('new[] { 0, 1, 8, 32, 128, 1024, 4096 }','new[] { 8, 128 }')
    t=t.replace('var allocations = new double[7];','var allocations = new double[7];\n            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();')
    # Add varied probes as a separate batch operation; do not replace fixed-key checks.
    marker='                Measure("union", size, universe, distribution,'
    extra='''                Tag[] probes = new Tag[128]; bool[] expectedProbes = new bool[128];
                for (int p = 0; p < 128; p++) {
                    string name = p % 2 == 0 && size != 0 ? x[(p * 17) % size] : sequence[sequence.Length - 1 - p];
                    probes[p] = Resolve(name); expectedProbes[p] = x.Contains(name);
                }
                Measure("exact_mix128", size, universe, distribution,
                    () => { for (int p = 0; p < probes.Length; p++) checksum ^= a.HasTagExact(probes[p]) ? 1 : 0; },
                    () => { for (int p = 0; p < probes.Length; p++) if (a.HasTagExact(probes[p]) != expectedProbes[p]) throw new Exception("mixed probe"); },
                    3000, a, digest);
'''
    assert marker in t;t=t.replace(marker,extra+marker)
    if prepared:
        t=t.replace('private static int checksum;','private static int checksum;\n    private static Set work;')
        t=t.replace('Set a = Input(x), b = Input(y), subset = Input(removed);','Set a = Input(x), b = Input(y), subset = Input(removed);\n                work = new Set(registry, Math.Max(1, size * 2), storage);')
        t=t.replace('return Set.Union(a, b, storage);','Set.UnionInto(a, b, work); return work;')
        t=t.replace('var result = new Set(a);','var result = work; result.CopyFrom(a);')
        for a,b in [('"union"','"union_into"'),('"copy_append"','"copy_append_reuse"'),('"copy_remove"','"copy_remove_reuse"')]:t=t.replace(a,b)
    if kind=='micro':
        t=t.replace('using Set = GameplayTags.RuntimeTagSet;','using Set = GameplayTags.Experiments.MicroBitmapSet;')
        t=t.replace('TagSetStorage','GameplayTags.Experiments.BitmapLayout').replace('return set.Storage.ToString();','return set.Layout.ToString();')
    elif kind in ('indexed','packed'):
        t=t.replace('private static TagSetStorage storage;','').replace(', storage)',')')
        t=t.replace('return set.Storage.ToString();','return "'+kind+'";')
        t=t.replace('storage = args.Length > 2 ? (TagSetStorage)Enum.Parse(typeof(TagSetStorage), args[2]) : TagSetStorage.Auto;','')
    t=t.replace('input_member_buffer_bytes = MemberBufferBytes(input)', 'input_member_buffer_bytes = MemberBufferBytes(input), result_storage = sink is Set ss ? StorageName(ss) : null')
    return t

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--references',required=True);p.add_argument('--output',required=True);p.add_argument('--dotnet',default='dotnet')
    p.add_argument('--cpu',type=int,default=-1);p.add_argument('--rounds',type=int,default=3);p.add_argument('--focus',action='store_true');p.add_argument('--skip-tests',action='store_true')
    p.add_argument('--tests-only',action='store_true');p.add_argument('--configs',default=','.join(CONFIGS));p.add_argument('--baseline-names',default=','.join(PINS))
    args=p.parse_args()
    if args.rounds<1:p.error('rounds must be positive')
    configs=args.configs.split(',');baselines=args.baseline_names.split(',')
    if set(configs)-CONFIGS.keys() or set(baselines)-PINS.keys():p.error('unknown variant')
    if 'auto' not in baselines:p.error('fixed Auto baseline is required')
    out=pathlib.Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True)
    env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1')
    def save(name,data):(out/name).write_text(json.dumps(data,ensure_ascii=False,indent=2))
    def run(cmd,name,cwd):
        proc=subprocess.run(list(map(str,cmd)),cwd=cwd,env=env,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=900)
        (out/name).write_text(proc.stdout);print(proc.stdout,flush=True)
        if proc.returncode:raise RuntimeError(name+' failed: '+str(proc.returncode))
        return 0
    if args.cpu >= 0:
        if not hasattr(os,'sched_setaffinity') or args.cpu not in os.sched_getaffinity(0):raise RuntimeError('Requested CPU affinity unavailable')
        os.sched_setaffinity(0,{args.cpu})
    save('host.json',{'cpu_affinity':sorted(os.sched_getaffinity(0)) if hasattr(os,'sched_getaffinity') else None,
        'python':sys.version,'cpuinfo':pathlib.Path('/proc/cpuinfo').read_text()[:3500] if pathlib.Path('/proc/cpuinfo').exists() else None})
    sources={};inventory={};builds={} 
    with tempfile.TemporaryDirectory(prefix='micro-pilot-') as temp:
        work=pathlib.Path(temp);(work/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
        sdks=subprocess.check_output([args.dotnet,'--list-sdks'],text=True)
        versions=[l.split()[0] for l in sdks.splitlines() if l.startswith('8.0.') and '-' not in l.split()[0]]
        if not versions:raise RuntimeError('.NET 8 SDK required')
        sdk=max(versions,key=lambda s:tuple(map(int,s.split('.'))));(work/'global.json').write_text(json.dumps({'sdk':{'version':sdk,'rollForward':'disable'}}))
        run([args.dotnet,'--info'],'environment.txt',work)
        for v in baselines:
            path=pathlib.Path(args.references)/(v+'.zip');target=work/v;target.mkdir()
            with zipfile.ZipFile(path) as z:
                tree=archive_tree(z)
                if tree!=PINS[v][1]:raise RuntimeError('Wrong pinned source tree '+v)
                for e in z.infolist():
                    q=pathlib.PurePosixPath(e.filename)
                    if q.is_absolute() or '..' in q.parts or ((e.external_attr>>16)&0o170000)==0o120000:raise RuntimeError('Unsafe archive')
                z.extractall(target);archived=z.comment.decode('ascii')
            inventory[v]={'commit':PINS[v][0],'tree':tree,'archive_commit':archived,'archive_sha256':hashlib.sha256(path.read_bytes()).hexdigest()};sources[v]=target
        # Literal Runtime is untouched; the experimental class is compiled alongside it.
        for folder in ('Runtime','Editor','Samples~','Tests'):
            expected={q.relative_to(sources['auto']).as_posix():q.read_bytes() for q in (sources['auto']/folder).rglob('*') if q.is_file()}
            actual={q.relative_to(ROOT).as_posix():q.read_bytes() for q in (ROOT/folder).rglob('*') if q.is_file()}
            if actual!=expected:raise RuntimeError('Premature production change: '+folder)
        if (ROOT/'package.json').read_bytes()!=(sources['auto']/'package.json').read_bytes():raise RuntimeError('Changed package')
        save('inventory.json',inventory)
        for name in ('MicroBitmapSet.cs','MicroTests.cs'):
            shutil.copy2(ROOT/'Audit~'/name,out/name)
        save('candidate-digests.json',{n:hashlib.sha256((ROOT/'Audit~'/n).read_bytes()).hexdigest() for n in ('MicroBitmapSet.cs','MicroTests.cs','micro_pilot.py')})
        for variant in configs:
            h=work/variant;h.mkdir();includes=[ROOT/'Runtime/**/*.cs',ROOT/'Audit~/UnityStubs.cs',ROOT/'Audit~/MicroBitmapSet.cs']
            symbols=CONFIGS[variant]
            if not args.skip_tests:
                project(h/'Tests.csproj',includes+[ROOT/'Audit~/MicroTests.cs'],symbols)
                run([args.dotnet,'build',h/'Tests.csproj','-c','Release','-o',h/'tests'],variant+'-test-build.log',h)
                run([args.dotnet,'exec',h/'tests/Tests.dll',out/(variant+'-tests.json')],variant+'-tests.log',h)
                project(h/'Portable.csproj',includes,symbols,framework='netstandard2.1',executable=False)
                run([args.dotnet,'build',h/'Portable.csproj','-c','Release','-p:BaseIntermediateOutputPath=obj-portable/','-o',h/'portable'],variant+'-portable.log',h)
        if args.tests_only:
            save('decision.json',{'tests_only':True,'production_promoted':False});return
        original=(ROOT/'Audit~/FourWay.cs').read_text()
        specs={v:(root,'ALEX' if v=='alex' else 'CANDIDATE' if v in ('auto','indexed','packed') else '',v) for v,root in sources.items()}
        for v in configs:specs[v]=(sources['auto'],'CANDIDATE;'+CONFIGS[v],'micro')
        for v,(root,symbols,kind) in specs.items():
            contracts=('fresh','prepared') if symbols.startswith('CANDIDATE') else ('fresh',)
            for contract in contracts:
                h=work/(v+'-'+contract);h.mkdir()
                code=fixture(original,kind,contract=='prepared',args.focus)
                (h/'Bench.cs').write_text(code);(out/('fixture-'+v+'-'+contract+'.cs')).write_text(code)
                includes=[root/'Runtime'/n for n in ALEX_CORE]+[ROOT/'Audit~/AlexDependencies.cs'] if v=='alex' else [root/'Runtime/**/*.cs',ROOT/'Audit~/UnityStubs.cs']
                if kind=='micro':includes.append(ROOT/'Audit~/MicroBitmapSet.cs')
                project(h/'Bench.csproj',includes+[h/'Bench.cs'],symbols)
                run([args.dotnet,'build',h/'Bench.csproj','-c','Release','-o',h/'out'],v+'-'+contract+'-build.log',h)
                builds[(v,contract)]=(h/'out/Bench.dll',h,'Auto')
        for contract in ('fresh','prepared'):
            dll,h,_=builds[('auto',contract)];builds[('flat',contract)]=(dll,h,'Dense')
        order=list(builds)
        for n in range(args.rounds):
            rotated=order[n%len(order):]+order[:n%len(order)]
            if n&1:rotated=list(reversed(rotated))
            for v,c in rotated:
                dll,h,mode=builds[(v,c)]
                run([args.dotnet,'exec',dll,out/(v+'-'+c+'-r'+str(n)+'.json'),v,mode],v+'-'+c+'-r'+str(n)+'.log',h)
        tables={};digests={}
        for v,c in builds:
            bykey={}
            for n in range(args.rounds):
                data=json.loads((out/(v+'-'+c+'-r'+str(n)+'.json')).read_text());seen=set()
                for row in data['rows']:
                    if row['status']!='measured':raise RuntimeError('Invalid result cannot rank: '+str(row))
                    k=(row['operation'],row['size'],row['universe'],row['distribution'])
                    if k in seen:raise RuntimeError('duplicate row')
                    seen.add(k)
                    canonical=(row['size'],row['universe'],row['distribution'])
                    if canonical in digests and digests[canonical]!=row['inputDigest']:raise RuntimeError('different inputs')
                    digests[canonical]=row['inputDigest']
                    if c=='prepared' and any(x!=0 for x in row['allocated_bytes']):raise RuntimeError('prepared allocation '+str(k))
                    bykey.setdefault(k,[]).append(row)
            tables[(v,c)]={k:{'ns_round_medians':[statistics.median(x['ns']) for x in rows],
                'median_ns':statistics.median(statistics.median(x['ns']) for x in rows),
                'bytes_per_op':statistics.median(x for row in rows for x in row['allocated_bytes']),
                'input_layout':rows[0]['input_storage'],'input_buffer_bytes':rows[0]['input_member_buffer_bytes'],
                'output_layout':rows[0]['result_storage'] if not k[0].startswith('exact') else None} for k,rows in bykey.items()}
            if any(len(rows)!=args.rounds for rows in bykey.values()):raise RuntimeError('Incomplete rounds')
        comparisons=[];summary={}
        for v in configs:
            for c in ('fresh','prepared'):
                wins=stable=primary=0
                for k,values in sorted(tables[(v,c)].items()):
                    refs={r:table[k] for (r,contract),table in tables.items() if r not in configs and contract==c}
                    best=min(x['median_ns'] for x in refs.values());lower=values['median_ns']<best
                    margin=all(values['ns_round_medians'][i]<=.95*min(x['ns_round_medians'][i] for x in refs.values()) for i in range(args.rounds))
                    if k[0] in PRIMARY: primary+=1;wins+=lower;stable+=margin
                    comparisons.append(dict(candidate=v,contract=c,operation=k[0],members=k[1],definitions=k[2],distribution=k[3],values=values,references=refs,lower_median=lower,margin_every_round=margin,ratio_to_fastest=values['median_ns']/best))
                summary[v+'-'+c]={'primary':primary,'lower_medians':wins,'margin_every_round':stable,'all_primary_targets_met':stable==primary and not args.focus and not args.skip_tests and set(baselines)==set(PINS)}
        save('comparison.json',comparisons);save('summary.json',summary)
        with (out/'comparison.csv').open('w',newline='') as f:
            writer=csv.writer(f);writer.writerow(['candidate','contract','operation','members','definitions','distribution','median_ns','bytes_per_op','ratio_to_fastest','lower_median','margin_every_round'])
            for x in comparisons:writer.writerow([x[k] for k in ('candidate','contract','operation','members','definitions','distribution')]+[x['values']['median_ns'],x['values']['bytes_per_op'],x['ratio_to_fastest'],x['lower_median'],x['margin_every_round']])
        save('decision.json',{'production_promoted':False,'all_references':set(baselines)==set(PINS),'focus_only':args.focus,'rounds':args.rounds,'native_unity':'not_run','release_approved':False,'protocol':'settled-setup-v2; no subtraction; mixed queries are 128-probe batches','summary':summary})
        print('MICRO_SUMMARY '+json.dumps(summary),flush=True)
        for x in comparisons:
            if x['contract']=='fresh' and x['operation'] in PRIMARY and ((x['members']==8 and x['definitions']==262144) or (x['members']==1024 and x['definitions']==10000)):
                print('MICRO_RESULT '+json.dumps(x),flush=True)
if __name__=='__main__':main()
