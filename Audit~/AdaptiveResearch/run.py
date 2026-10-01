#!/usr/bin/env python3
"""Unrestricted adaptive pilot. Same binary A/A, pinned sources, actual public operations."""
import argparse,csv,difflib,hashlib,json,os,pathlib,random,shutil,statistics,subprocess,sys,tempfile,zipfile
ROOT=pathlib.Path(__file__).resolve().parents[2]
HERE=ROOT/'Audit~/AdaptiveResearch'
sys.path[:0]=[str(HERE),str(ROOT/'Audit~'),str(ROOT/'Audit~/ArrayBuild')]
from variants import generate,AUTO
from integer_audit import project
from micro_pilot import fixture
from build_candidates import candidates as micro_candidates
MICRO='9be85fcd1f8d1f594ceb5bc98f2ce234b398e0e0'
FUSION='d45f21ffc5858a7ce9be68af9322924a774e40b4'
DIRECT='2866adfc6bfc21a11177c6b5873fbc54afebfe13'
FIELDS=('variant','contract','operation','members','definitions','distribution')
def sha(data):return hashlib.sha256(data).hexdigest()
def main():
    p=argparse.ArgumentParser();p.add_argument('--output',required=True);p.add_argument('--rounds',type=int,default=3);args=p.parse_args()
    if args.rounds!=3:p.error('This fixed pilot uses exactly three rounds')
    out=pathlib.Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True)
    if hasattr(os,'sched_setaffinity'):os.sched_setaffinity(0,{min(os.sched_getaffinity(0))})
    env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1',DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1')
    def git(*xs):return subprocess.check_output(['git',*xs],cwd=ROOT)
    def save(name,data):(out/name).write_text(json.dumps(data,ensure_ascii=False,indent=2))
    def run(cmd,log,cwd,extra=None):
        result=subprocess.run(list(map(str,cmd)),cwd=cwd,env=dict(env,**(extra or {})),stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,timeout=1500)
        (out/log).write_text(result.stdout)
        if result.returncode:print(result.stdout,flush=True);raise RuntimeError(log+' failed')
        return result.stdout
    def build(folder,files,name='Probe',symbol='',framework='net8.0',executable=True,references=()):
        folder.mkdir(parents=True,exist_ok=True);proj=folder/(name+'.csproj')
        project(proj,files,symbol,framework=framework,executable=executable,references=references)
        proj.write_text(proj.read_text().replace('<Optimize>true</Optimize>','<Optimize>true</Optimize><AllowUnsafeBlocks>true</AllowUnsafeBlocks><Deterministic>true</Deterministic>'))
        run(['dotnet','build',proj,'-c','Release','-o',folder/'bin'],folder.name+'-build.log',folder)
        return folder/'bin'/(name+'.dll')
    for sub in ('Runtime','Editor','Samples~','Tests','package.json'):
        assert not git('diff','--name-only',AUTO,'HEAD','--',sub).strip(),'Changed production '+sub
    for path,pin in [('Audit~/MicroBitmapSet.cs',MICRO),('Audit~/Fusion/DenseFusion.cs',FUSION),('Audit~/ArrayBuild/build_candidates.py',DIRECT),('Audit~/micro_pilot.py',MICRO)]:
        assert git('show',pin+':'+path)==(ROOT/path).read_bytes(),'Changed reference '+path
    original=git('show',AUTO+':Runtime/RuntimeTagSet.cs').decode();variants=generate(original,out/'generated')
    backend=ROOT/'Audit~/Fusion/DenseFusion.cs'
    micro_files=micro_candidates(git('show',MICRO+':Audit~/MicroBitmapSet.cs').decode(),out/'direct-generated')
    old_four=git('show',AUTO+':Audit~/FourWay.cs').decode()
    snapshot={'head':git('rev-parse','HEAD').decode().strip(),'tree':git('rev-parse','HEAD^{tree}').decode().strip(),
        'refs':{label:{'commit':pin,'tree':git('rev-parse',pin+'^{tree}').decode().strip()} for label,pin in [('auto',AUTO),('micro',MICRO),('fusion',FUSION),('direct',DIRECT)]},
        'variants':{name:sha(code.encode()) for name,code in variants.items()},'backend_sha256':sha(backend.read_bytes()),
        'production_unchanged':True,'capacity_and_selection_unchanged':True}
    save('sources.json',snapshot)
    save('host.json',{'machine':os.uname().machine,'affinity':sorted(os.sched_getaffinity(0)) if hasattr(os,'sched_getaffinity') else [],
        'cpuinfo':pathlib.Path('/proc/cpuinfo').read_text()[:16000] if pathlib.Path('/proc/cpuinfo').exists() else '', 'env':{k:v for k,v in env.items() if k.startswith('DOTNET_')}})
    with tempfile.TemporaryDirectory(prefix='adaptive-') as tmp:
        work=pathlib.Path(tmp);(work/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
        (work/'global.json').write_text('{"sdk":{"version":"8.0.425","rollForward":"disable"}}')
        run(['dotnet','--info'],'environment.txt',work)
        roots={};includes={};test_paths={}
        for label,source in variants.items():
            root=work/('source-'+label);root.mkdir()
            # Materialize the exact old runtime/editor/sample sources for the full old API suite.
            for folder in ('Runtime','Editor','Samples~'):
                for rel in git('ls-tree','-r','--name-only',AUTO,'--',folder).decode().splitlines():
                    if not rel.endswith('.cs'):continue
                    q=root/rel;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(git('show',AUTO+':'+rel))
            (root/'Runtime/RuntimeTagSet.cs').write_text(source)
            roots[label]=root
            common=[root/'Runtime/**/*.cs',ROOT/'Audit~/UnityStubs.cs',backend]
            includes[label]=common
            full=common+[root/'Editor/**/*.cs',root/'Samples~/**/*.cs',ROOT/'Audit~/IntegerTests.cs',ROOT/'Audit~/IntegerTestSupport.cs']
            tests=build(work/(label+'-tests'),full,'IntegerTests')
            testdir=out/(label+'-tests');testdir.mkdir()
            print(run(['dotnet','exec',tests,testdir],label+'-tests.log',work),flush=True)
            if label in ('auto','combined'):
                nodir=out/(label+'-nohw');nodir.mkdir();print(run(['dotnet','exec',tests,nodir],label+'-nohw.log',work,{'DOTNET_EnableHWIntrinsic':'0'}),flush=True)
            checks=build(work/(label+'-checks'),common+[HERE/'AdaptiveChecks.cs'],'AdaptiveChecks')
            print(run(['dotnet','exec',checks,out/(label+'-checks.json')],label+'-checks.log',work),flush=True)
            test_paths[label]=checks
        # Compile the complete changed API with its real scalar fallback as Standard 2.1.
        portable_common=includes['combined']+[roots['combined']/'Editor/**/*.cs',roots['combined']/'Samples~/**/*.cs']
        friend=work/'Friend.cs';friend.write_text('[assembly:System.Runtime.CompilerServices.InternalsVisibleTo("PortableTests")]')
        portable=build(work/'portable',portable_common+[friend],'Portable',framework='netstandard2.1',executable=False)
        tests=build(work/'portable-tests',[ROOT/'Audit~/IntegerTests.cs',ROOT/'Audit~/IntegerTestSupport.cs'],'PortableTests',references=[portable])
        dest=out/'portable-tests';dest.mkdir();print(run(['dotnet','exec',tests,dest],'portable-tests.log',work),flush=True)
        run(['dotnet','exec',test_paths['combined'],out/'codegen-checks.json'],'codegen.log',work,
            {'DOTNET_JitDisasm':'GameplayTags.RuntimeTagSet:ContainsSparseId GameplayTags.RuntimeTagSet:.ctor GameplayTags.Experiments.DenseFusion:Union'})
        builds={};binary_hashes={}
        # DirectArray remains a reference, not silently adopted as the candidate.
        for label in [*variants,'direct']:
            for contract in ('fresh','prepared'):
                code=fixture(old_four,'micro' if label=='direct' else 'auto',contract=='prepared')
                if label=='direct':code=code.replace('GameplayTags.Experiments.MicroBitmapSet','GameplayTags.Experiments.DirectArraySet')
                code=code.replace('Math.Max(24, Math.Min(1000, 131072 / (size + 1)))','Math.Max(500, Math.Min(20000, 1048576 / (size + 1)))')
                # Extra diagnostics do not alter the action or allocation accounting.
                code=code.replace('var allocations = new double[7];','var allocations = new double[7];\n            var gc0=new int[7];var gc1=new int[7];var gc2=new int[7];')
                code=code.replace('long allocated = GC.GetAllocatedBytesForCurrentThread();','int c0=GC.CollectionCount(0),c1=GC.CollectionCount(1),c2=GC.CollectionCount(2);\n                long allocated = GC.GetAllocatedBytesForCurrentThread();')
                code=code.replace('GC.KeepAlive(sink);','gc0[sample]=GC.CollectionCount(0)-c0;gc1[sample]=GC.CollectionCount(1)-c1;gc2[sample]=GC.CollectionCount(2)-c2;\n                GC.KeepAlive(sink);')
                code=code.replace('ns, allocated_bytes = allocations,','ns, gc0, gc1, gc2, allocated_bytes = allocations,')
                literal=out/'generated'/(label+'-'+contract+'-benchmark.cs');literal.write_text(code)
                sources=includes['auto']+[ROOT/'Audit~/MicroBitmapSet.cs',micro_files['arrays']] if label=='direct' else includes[label]
                dll=build(work/(label+'-'+contract),sources+[literal],symbol='CANDIDATE')
                builds[(label,contract)]=dll;binary_hashes[label+'/'+contract]=sha(dll.read_bytes())
        # A/A is exactly the same binary and same working directory, differing only in report label.
        for contract in ('fresh','prepared'):
            builds[('autoAA',contract)]=builds[('auto',contract)]
            binary_hashes['autoAA/'+contract]=binary_hashes['auto/'+contract]
        save('binary-sha256.json',binary_hashes)
        save('plan.json',{'rounds':3,'samples_per_round':7,'matrix':'0,1,8,32,128,1024,4096 x 10000,65536,262144 x contiguous/scattered',
            'mode':'Auto for every build; no tuned input-specific mode selection','AA':'Exact same Auto binary executed under two report labels',
            'controls':['auto','autoAA','direct'],'candidates':['dense','simd','copy','combined'],'dispatch_threshold':32,
            'historical_protocol':'Same public operations, pre-resolved inputs, half-overlap, real copy then mutate, same settling and iteration budgets; only GC metadata added',
            'limits':['shared Linux .NET, not native Unity','initial pilot, not all historical implementations','no full churn/relation performance matrix','no universal performance claim']})
        order=list(builds)
        for r in range(3):
            jobs=list(order);random.Random(20261001+r).shuffle(jobs)
            for label,contract in jobs:
                tag=f'{label}-{contract}-r{r}'
                run(['dotnet','exec',builds[(label,contract)],out/(tag+'.json'),label,'Auto'],tag+'.log',work)
                print('MEASURED '+tag,flush=True)
    # All results are reconstructed separately by review.py, not pre-ranked here.
    print('ADAPTIVE_MEASUREMENTS_COMPLETE '+snapshot['head'],flush=True)
if __name__=='__main__':main()
