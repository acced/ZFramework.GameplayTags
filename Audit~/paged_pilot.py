#!/usr/bin/env python3
"""Direct-addressed bitmap pilot. Production Runtime is unchanged; no release approval."""
import argparse, csv, hashlib, json, os, pathlib, re, shutil, statistics, subprocess, tempfile, zipfile
from integer_audit import ALEX_CORE, archive_tree, project
ROOT=pathlib.Path(__file__).resolve().parent.parent
AUDIT=ROOT/'Audit~'
PINS={'main':'934b14a47ddf0b6367bf6720be58c18cbcb09896','optimize':'a99f53d4697df6f577961ff1ee1bad14caa221d1',
      'auto':'84729cc3b6e1b4681328806df2d7c8a1d8f568f6','indexed':'3781d1b6c66997ec37fcb6223cb1a1d60c7e19fb',
      'packed':'4c5cada6faa8635a69d31a7a074bd79646bafee0','alex':'28e4218f459ecf0e9fb4f0bca90805c05eb962c2'}
BASELINES=['main','optimize','alex','auto','flat','indexed','packed']
CONFIGS={'page64':'','page128':'PAGE128','page256':'PAGE256','page512':'PAGE512','page64-exact':'EXACT_RESERVE','page256-exact':'PAGE256;EXACT_RESERVE'}
PRIMARY={'exact','union','copy_append','copy_remove'}

def save(path,value):path.write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
def replace_once(text,old,new):
    if text.count(old)!=1:raise RuntimeError('Fixture anchor mismatch: '+old[:100])
    return text.replace(old,new)
def adapt(text,kind,prepared=False):
    # Adapters change only call syntax and the common fixture, never implementation source.
    if kind in ('page','indexed','packed'):
        if kind=='page':text=replace_once(text,'using Set = GameplayTags.RuntimeTagSet;','using Set = PageExperiment.PagedBitmapSet;')
        text=replace_once(text,'    private static TagSetStorage storage;','')
        text=replace_once(text,'new Set(registry, names.Length, storage)','new Set(registry, names.Length)')
        text=replace_once(text,'return Set.Union(a, b, storage);','return Set.Union(a, b);')
        text=replace_once(text,'return set.Storage.ToString();','return "'+kind+'";')
        text=replace_once(text,'        storage = args.Length > 2 ? (TagSetStorage)Enum.Parse(typeof(TagSetStorage), args[2]) : TagSetStorage.Auto;','')
    text=replace_once(text,'        BenchmarkUniverse(65536);','        BenchmarkUniverse(65536);\n        BenchmarkUniverse(262144);')
    text=replace_once(text,'new[] { 0, 8, 128, 1024, 4096 }','new[] { 0, 1, 8, 32, 128, 1024, 4096 }')
    text=replace_once(text,'            var ns = new double[7];','            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();\n            var ns = new double[7];')
    # Additional varied-key query fixture. Its duration is PER 128 QUERIES, never mislabeled per lookup.
    text=replace_once(text,'                int bulkIterations = Math.Max(24, Math.Min(1000, 131072 / (size + 1)));',
        '''                int bulkIterations = Math.Max(24, Math.Min(1000, 131072 / (size + 1)));
                var probes = new Tag[128]; var expectedProbes = new bool[128];
                for (int p=0;p<probes.Length;p++) {
                    string name = size != 0 && (p & 1)==0 ? x[(p*17)%size] : sequence[universe-1-p];
                    probes[p]=Resolve(name); expectedProbes[p]=x.Contains(name);
                }
                Measure("exact_mix128",size,universe,distribution,
                    ()=>{for(int p=0;p<probes.Length;p++)checksum^=a.HasTagExact(probes[p])?1:0;},
                    ()=>{for(int p=0;p<probes.Length;p++)if(a.HasTagExact(probes[p])!=expectedProbes[p])throw new Exception("mixed query mismatch");},
                    2500,a,digest);''')
    if prepared:
        if kind in ('auto','flat'):
            ctor='new Set(registry, Math.Min(registry.Count,size*2+8), storage)'
        else:ctor='new Set(registry, Math.Min(registry.Count,size*2+8))'
        text=replace_once(text,'                Tag hit = Resolve', '                Set target = '+ctor+';\n                Tag hit = Resolve')
        text=replace_once(text,'() => { sink = Union(a, b); },','() => { Set.UnionInto(a,b,target); sink=target; },')
        text=replace_once(text,'() => { Verify(Union(a, b), x.Concat(y)); Verify(a, x); Verify(b, y); },',
            '() => { Set.UnionInto(a,b,target); Verify(target,x.Concat(y)); Verify(a,x); Verify(b,y); },')
        text=replace_once(text,'() => { sink = CopyAppend(a, b); },','() => { target.CopyFrom(a); target.AppendTags(b); sink=target; },')
        text=replace_once(text,'() => { Verify(CopyAppend(a, b), x.Concat(y)); Verify(a, x); Verify(b, y); },',
            '() => { target.CopyFrom(a); target.AppendTags(b); Verify(target,x.Concat(y)); Verify(a,x); Verify(b,y); },')
        text=replace_once(text,'() => { sink = CopyRemove(a, subset); },','() => { target.CopyFrom(a); target.RemoveTags(subset); sink=target; },')
        text=replace_once(text,'() => { Verify(CopyRemove(a, subset), x.Except(removed)); Verify(a, x); Verify(subset, removed); },',
            '() => { target.CopyFrom(a); target.RemoveTags(subset); Verify(target,x.Except(removed)); Verify(a,x); Verify(subset,removed); },')
    return text

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--output',required=True);parser.add_argument('--rounds',type=int,default=3)
    parser.add_argument('--skip-bench',action='store_true');args=parser.parse_args()
    if args.rounds<3:parser.error('At least three rounds required')
    out=pathlib.Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True)
    env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1')
    def run(command,log,cwd=ROOT):
        proc=subprocess.run(list(map(str,command)),cwd=cwd,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,timeout=900)
        (out/log).write_text(proc.stdout,encoding='utf-8')
        if proc.returncode:print(proc.stdout,flush=True);raise RuntimeError(log+' failed')
        for line in proc.stdout.splitlines():
            if line.startswith(('PASS ','PAGE_TESTS','PREPARED_BYTES','FOUR-WAY')):print(line,flush=True)
        return proc.stdout
    head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
    run(['dotnet','--info'],'dotnet-info.txt')
    (out/'source.zip').write_bytes(subprocess.check_output(['git','archive','--format=zip',head],cwd=ROOT))
    save(out/'environment.json',{'head':head,'pins':PINS,'rounds':args.rounds,'protocol':'settled-setup-v2',
         'page_source_sha256':hashlib.sha256((AUDIT/'PagedBitmapSet.cs').read_bytes()).hexdigest(),
         'native_unity':'not_run','production_runtime_changed':False,'promotion_approved':False})
    with tempfile.TemporaryDirectory(prefix='direct-pages-') as tmp:
        work=pathlib.Path(tmp);(work/'global.json').write_text('{"sdk":{"version":"8.0.425","rollForward":"disable"}}')
        (work/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
        sources={};inventory={}
        for name,sha in PINS.items():
            repo=ROOT/'.comparisons/alex' if name=='alex' else ROOT
            raw=subprocess.check_output(['git','archive','--format=zip',sha],cwd=repo)
            archive=out/(name+'-source.zip');archive.write_bytes(raw)
            target=work/name;target.mkdir()
            with zipfile.ZipFile(archive) as z:
                expected=subprocess.check_output(['git','rev-parse',sha+'^{tree}'],cwd=repo,text=True).strip()
                if z.comment.decode()!=sha or archive_tree(z)!=expected:raise RuntimeError('Reference tree mismatch: '+name)
                for f in z.infolist():
                    p=pathlib.PurePosixPath(f.filename)
                    if p.is_absolute() or '..' in p.parts or (f.external_attr>>16)&0o170000==0o120000:raise RuntimeError('Unsafe archive')
                z.extractall(target)
            sources[name]=target;inventory[name]={'commit':sha,'tree':expected,'archive_sha256':hashlib.sha256(raw).hexdigest()}
        # Enforce the user's requested gate: prototype first, no premature whole-project migration.
        for folder in ('Runtime','Editor','Samples~','Tests'):
            before={p.relative_to(sources['auto']).as_posix():p.read_bytes() for p in (sources['auto']/folder).rglob('*') if p.is_file()}
            after={p.relative_to(ROOT).as_posix():p.read_bytes() for p in (ROOT/folder).rglob('*') if p.is_file()}
            if before!=after:raise RuntimeError('Production changed before pilot acceptance: '+folder)
        save(out/'reference-inventory.json',inventory)
        tests={};builds={};fixture=(sources['auto']/'Audit~/FourWay.cs').read_text()
        for name,symbol in CONFIGS.items():
            host=work/name;host.mkdir()
            includes=[sources['auto']/'Runtime/**/*.cs',AUDIT/'UnityStubs.cs',AUDIT/'PagedBitmapSet.cs',AUDIT/'PagedTests.cs']
            project(host/'Tests.csproj',includes,symbol=symbol)
            run(['dotnet','build',host/'Tests.csproj','-c','Release','-o',host/'tests'],name+'-test-build.log',host)
            log=run(['dotnet',host/'tests/Tests.dll'],name+'-tests.log',host)
            m=re.search(r'PAGE_TESTS format=\S+ groups=(\d+) assertions=(\d+) failed=0',log)
            if not m:raise RuntimeError('Missing test result: '+name)
            tests[name]={'groups':int(m[1]),'assertions':int(m[2]),'failed':0}
            project(host/'Portable.csproj',includes[:-1],symbol=symbol,framework='netstandard2.1',executable=False)
            run(['dotnet','build',host/'Portable.csproj','-c','Release','-p:BaseIntermediateOutputPath=obj-portable/','-o',host/'portable'],name+'-portable-build.log',host)
        save(out/'tests.json',tests)
        if args.skip_bench:return
        variants=BASELINES+list(CONFIGS)
        for name in variants:
            kind='page' if name in CONFIGS else name
            root=sources['auto'] if kind in ('page','flat') else sources.get(kind,sources['auto'])
            host=work/('bench-'+name);host.mkdir()
            sym=CONFIGS.get(name,'')
            if kind=='alex':includes=[root/'Runtime'/f for f in ALEX_CORE]+[AUDIT/'AlexDependencies.cs'];sym='ALEX'
            else:
                includes=[root/'Runtime/**/*.cs',AUDIT/'UnityStubs.cs'];sym='CANDIDATE;'+sym if kind not in ('main','optimize') else ''
                if kind=='page':includes+=[AUDIT/'PagedBitmapSet.cs']
            for contract in ('fresh','prepared'):
                if contract=='prepared' and kind in ('main','optimize','alex'):continue
                text=adapt(fixture,kind,contract=='prepared');source=host/(contract+'.cs');source.write_text(text)
                shutil.copy2(source,out/(name+'-'+contract+'-fixture.cs'))
                proj=host/(contract+'.csproj');project(proj,includes+[source],symbol=sym)
                dest=host/contract
                run(['dotnet','build',proj,'-c','Release','-p:BaseIntermediateOutputPath=obj-'+contract+'/','-o',dest],name+'-'+contract+'-build.log',host)
                builds[(name,contract)]=(dest/(contract+'.dll'),host,'Dense' if name=='flat' else 'Auto')
        keys=list(builds)
        for round_ in range(args.rounds):
            # Every round contains every implementation, with rotated and reversed process order.
            order=keys[round_%len(keys):]+keys[:round_%len(keys)]
            if round_%2:order=list(reversed(order))
            for name,contract in order:
                dll,host,mode=builds[(name,contract)];path=out/(name+'-'+contract+'-r'+str(round_)+'.json')
                run(['dotnet',dll,path,name,mode],name+'-'+contract+'-r'+str(round_)+'.log',host)
        def key(r):return (r['operation'],r['size'],r['universe'],r['distribution'])
        data={};input_digests={}
        for name,contract in keys:
            rounds=[]
            for r in range(args.rounds):
                result=json.loads((out/(name+'-'+contract+'-r'+str(r)+'.json')).read_text());rows={key(x):x for x in result['rows']}
                if len(rows)!=len(result['rows']):raise RuntimeError('Duplicate measurement key')
                for k,row in rows.items():
                    if row['status']!='measured':raise RuntimeError('Failed row cannot rank: '+str((name,k,row)))
                    if k in input_digests and input_digests[k]!=row['inputDigest']:raise RuntimeError('Unmatched input')
                    input_digests[k]=row['inputDigest']
                    if contract=='prepared' and any(x!=0 for x in row['allocated_bytes']):raise RuntimeError('Prepared path allocated: '+str((name,k)))
                rounds.append(rows)
            data[(name,contract)]={k:{'round_medians_ns':[statistics.median(rows[k]['ns']) for rows in rounds],
                'median_ns':statistics.median(statistics.median(rows[k]['ns']) for rows in rounds),
                'bytes_per_op':statistics.median(statistics.median(rows[k]['allocated_bytes']) for rows in rounds),
                'input_buffer_bytes':rounds[0][k]['input_member_buffer_bytes']} for k in rounds[0]}
        report=[];summary={}
        for contract in ('fresh','prepared'):
            comparisons=[n for n in BASELINES if (n,contract) in data]
            for name in CONFIGS:
                ranks=[]
                for k,record in data[(name,contract)].items():
                    refs={n:data[(n,contract)][k] for n in comparisons}
                    fastest=min(v['median_ns'] for v in refs.values())
                    row={'candidate':name,'contract':contract,'operation':k[0],'members':k[1],'definitions':k[2],'distribution':k[3],
                         **record,'references':refs,'ratio_to_fastest':record['median_ns']/fastest,
                         'lower_median':record['median_ns']<fastest,
                         'five_percent_every_round':all(record['round_medians_ns'][i]<=0.95*min(v['round_medians_ns'][i] for v in refs.values()) for i in range(args.rounds))}
                    report.append(row);ranks.append(row)
                headline=[r for r in ranks if r['operation'] in PRIMARY]
                summary[name+'-'+contract]={'rows':len(ranks),'primary_rows':len(headline),'primary_lower_medians':sum(r['lower_median'] for r in headline),
                    'primary_5percent_every_round':sum(r['five_percent_every_round'] for r in headline),
                    'all_primary_targets_met':all(r['five_percent_every_round'] for r in headline)}
        save(out/'comparisons.json',report)
        summary['decision']={'production_promoted':False,'native_unity':'not_run','release_approved':False,
          'reason':'Pilot only. A green CI means correct execution and retained results, NOT performance acceptance. Inspect per-case rankings before migration.'}
        save(out/'summary.json',summary)
        print('PAGE_SUMMARY '+json.dumps(summary),flush=True)
        for r in report:
            if r['candidate'] not in ('page64','page256','page512'):continue
            if r['contract']!='fresh' or r['operation'] not in PRIMARY:continue
            if (r['definitions'],r['members'],r['distribution']) not in ((262144,8,'scattered'),(10000,1024,'contiguous'),(10000,1024,'scattered')):continue
            print('PAGE_RESULT '+json.dumps(r),flush=True)
        fields=['candidate','contract','operation','members','definitions','distribution','median_ns','bytes_per_op','ratio_to_fastest','five_percent_every_round']
        with (out/'comparisons.csv').open('w',newline='') as f:
            writer=csv.DictWriter(f,fieldnames=fields,extrasaction='ignore');writer.writeheader();writer.writerows(report)

if __name__=='__main__':main()
