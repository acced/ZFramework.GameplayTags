#!/usr/bin/env python3
"""Execute five fixed compiled variants; exact inputs, ownership, growth and A/A."""
import pathlib,sys,os,subprocess,json,hashlib,random,shutil,argparse,zipfile
from xml.sax.saxutils import escape
HERE=pathlib.Path(__file__).resolve().parent;ROOT=HERE.parents[1]
sys.path.insert(0,str(HERE))
from generate import generate,BASE

def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    ap=argparse.ArgumentParser();ap.add_argument('--output',required=True)
    ap.add_argument('--rounds',type=int,default=3);ap.add_argument('--sdk',default='8.0.425')
    ap.add_argument('--snapshot',help='Prior verified platform artifact root; offline source provenance only')
    ap.add_argument('--tests-only',action='store_true');ap.add_argument('--smoke',action='store_true')
    args=ap.parse_args();assert args.rounds>=1
    out=pathlib.Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True)
    work=out/'build';work.mkdir(exist_ok=True)
    (work/'global.json').write_text(json.dumps({'sdk':{'version':args.sdk,'rollForward':'disable'}}))
    (work/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
    env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_ReadyToRun='0',DOTNET_NOLOGO='1',DOTNET_CLI_TELEMETRY_OPTOUT='1')
    if hasattr(os,'sched_setaffinity'):os.sched_setaffinity(0,{min(os.sched_getaffinity(0))})
    def run(cmd,log,extra=None):
        p=subprocess.run(list(map(str,cmd)),cwd=work,env=dict(env,**(extra or {})),stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,timeout=2400)
        (out/log).write_text(p.stdout)
        if p.returncode:raise RuntimeError(log+'\n'+p.stdout[-16000:])
        return p.stdout
    assert run(['dotnet','--version'],'sdk.txt').strip()==args.sdk
    run(['dotnet','--info'],'environment.txt');run(['lscpu'],'cpu.txt')
    protected={str(p.relative_to(ROOT)):sha(p) for d in ('Runtime','Editor','Samples~','Tests') for p in (ROOT/d).rglob('*') if p.is_file()}
    protected['package.json']=sha(ROOT/'package.json')
    if args.snapshot:
        old=pathlib.Path(args.snapshot).resolve();previous=json.loads((old/'results/manifest.json').read_text())
        assert previous['head']==BASE and previous['protected']==protected
        head=tree=None;kind='offline archived source plus new experiment';pinned=old/'results/generated'
    else:
        subprocess.check_call(['git','diff','--exit-code',BASE,'HEAD','--','Runtime','Editor','Samples~','Tests','package.json'],cwd=ROOT)
        head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
        tree=subprocess.check_output(['git','rev-parse','HEAD^{tree}'],cwd=ROOT,text=True).strip()
        kind='git';pinned=None
    if head is not None:
        subprocess.check_call(['git','diff','--exit-code','HEAD'],cwd=ROOT)
        subprocess.check_call(['git','archive','--format=zip','--output',str(out/'source.zip'),'HEAD'],cwd=ROOT)
        with zipfile.ZipFile(out/'source.zip') as z:
            source_files={e.filename:hashlib.sha256(z.read(e)).hexdigest() for e in z.infolist() if not e.is_dir()}
    else:
        source_files={str(p.relative_to(ROOT)):sha(p) for p in ROOT.rglob('*') if p.is_file() and out not in p.parents and '.git' not in p.parts and '__pycache__' not in p.parts}
        with zipfile.ZipFile(out/'source.zip','w',zipfile.ZIP_DEFLATED) as z:
            for name in sorted(source_files):z.write(ROOT/name,name)
    files=generate(out/'generated',pinned)
    probe=(ROOT/'Audit~/MixedOperations/Probe.cs').read_text().replace('internal static class MixedProbe','internal static partial class MixedProbe')
    marker='        long start=GC.GetAllocatedBytesForCurrentThread();\n        GC.KeepAlive(new byte[128]);'
    assert probe.count(marker)==1
    probe=probe.replace(marker,'        var localTests=LocalCursorTests();\n        var bulkTests=BulkTests();\n'+marker,1)
    probe=probe.replace('cursorBytes,enumeratorBytes=','bulkTests,localTests,cursorBytes,enumeratorBytes=',1)
    marker='''                            foreach(bool fresh in new[]{true,false})
                            {'''
    assert probe.count(marker)==1
    probe=probe.replace(marker,'''                            if (rm == Layout.Micro)
                            {
                                CopyLoop(a,output,1); Verify(output,x);
                                Measure(shape0,"copy_from",dest.ToString(),rep=>CopyLoop(a,output,rep),true);
                                Verify(output,x);
                            }
'''+marker,1)
    probe=probe.replace('if(stage=="pairs") Pairs(smoke); else Matrix(smoke);','if(stage=="scale") AppendScale(); else if(stage=="pairs") Pairs(smoke); else Matrix(smoke);')
    probefile=out/'generated/Probe.cs';probefile.write_text(probe)
    extras=[]
    for src,name in ((ROOT/'Audit~/DenseLocal/Extra.cs','LocalExtra.cs'),(HERE/'Extra.cs','BulkExtra.cs')):
        dest=out/'generated'/name;dest.write_bytes(src.read_bytes());extras.append(dest)
    common=list((ROOT/'Runtime').rglob('*.cs'))+[ROOT/'Audit~/UnityStubs.cs',ROOT/'Audit~/Fusion/DenseFusion.cs',ROOT/'Audit~/MicroBitmapSet.cs']
    def build(name,src,framework='net8.0',exe=True,refs=(),symbol='BULK_MICRO'):
        folder=work/name;folder.mkdir(exist_ok=True)
        items=''.join('<Compile Include="'+escape(str(p))+'"/>' for p in src)
        items+=''.join('<Reference Include="'+p.stem+'"><HintPath>'+escape(str(p))+'</HintPath></Reference>' for p in refs)
        project=folder/'Probe.csproj'
        project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>'+framework+'</TargetFramework><OutputType>'+('Exe' if exe else 'Library')+'</OutputType><AssemblyName>'+name+'</AssemblyName><LangVersion>9.0</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><AllowUnsafeBlocks>true</AllowUnsafeBlocks><Optimize>true</Optimize><Deterministic>true</Deterministic><DefineConstants>'+symbol+'</DefineConstants></PropertyGroup><ItemGroup>'+items+'</ItemGroup></Project>')
        run(['dotnet','build',project,'-c','Release','-o',folder/'bin'],name+'-build.log')
        return folder/'bin'/(name+'.dll')
    oldtest=out/'generated/FullMicroTests.cs';oldtest.write_text((ROOT/'Audit~/MicroTests.cs').read_text().replace('MicroBitmapSet','DirectArraySet'))
    dlls={}
    for name in files:
        src=common+files[name];dll=build(name,src+[probefile]+extras);dlls[name]=dll
        print(run(['dotnet',dll,name,'tests',out/(name+'-tests.json'),0],name+'-tests.log').strip(),flush=True)
        full=build(name+'-full',src+[oldtest]);print(run(['dotnet',full,out/(name+'-full-tests.json')],name+'-full-tests.log').strip(),flush=True)
    print(run(['dotnet',dlls['combined'],'combined','tests',out/'nohw-tests.json',0],'nohw-tests.log',{'DOTNET_EnableHWIntrinsic':'0'}).strip(),flush=True)
    portable=build('portable',common+files['combined'],framework='netstandard2.1',exe=False)
    hosttext=probe.replace('s.ReplaceAll(','ReplaceSettings(s,')
    helper='''
    static void ReplaceSettings(GameplayTagSettings s,List<GameplayTagDefinition> d,List<GameplayTagRedirect> r,List<GameplayTagSource> p)
    {typeof(GameplayTagSettings).GetMethod("ReplaceAll",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(s,new object[]{d,r,p});}
'''
    hosttext=hosttext.replace('    static TagRegistry Registry(',helper+'    static TagRegistry Registry(',1)
    hostfile=out/'generated/PortableHost.cs';hostfile.write_text(hosttext)
    host=build('portable-host',[hostfile]+extras,refs=[portable])
    print(run(['dotnet',host,'portable','tests',out/'portable-tests.json',0],'portable-tests.log').strip(),flush=True)
    runtimefull=build('runtime-full',list((ROOT/'Runtime').rglob('*.cs'))+list((ROOT/'Editor').rglob('*.cs'))+list((ROOT/'Samples~').rglob('*.cs'))+[ROOT/'Audit~/UnityStubs.cs',ROOT/'Audit~/IntegerTests.cs',ROOT/'Audit~/IntegerTestSupport.cs'],symbol='')
    fulldir=out/'runtime-full-results';fulldir.mkdir(exist_ok=True)
    print(run(['dotnet',runtimefull,fulldir],'runtime-full.log').splitlines()[-1],flush=True)
    jobs=[(name+suffix,stage,dll) for name,dll in dlls.items() for suffix in ('','AA') for stage in ('bench','pairs','scale')]
    manifest=dict(head=head,tree=tree,source_kind=kind,base=BASE,sdk=args.sdk,cpu=os.uname().machine,rounds=args.rounds,smoke=args.smoke,tests_only=args.tests_only,affinity=list(os.sched_getaffinity(0)),protected=protected,source_files=source_files,
        jobs=[dict(label=l,stage=s,binary=str(d.relative_to(out)),sha256=sha(d)) for l,s,d in jobs],environment={k:v for k,v in env.items() if k.startswith('DOTNET_')},production_promoted=False)
    (out/'manifest.json').write_text(json.dumps(manifest,indent=2))
    if args.tests_only:return
    for rnd in range(args.rounds):
        order=jobs.copy();random.Random(2026100219+rnd).shuffle(order)
        for label,stage,dll in order:
            key=f'{label}-{stage}-r{rnd}';print('START '+key,flush=True)
            print(run(['dotnet',dll,label,stage,out/(key+'.json'),rnd]+(['smoke'] if args.smoke else []),key+'.log').strip(),flush=True)
    for name,dll in dlls.items():
        run(['dotnet',dll,name,'bench',out/(name+'-codegen.json'),0,'smoke'],name+'-codegen.log',{'DOTNET_JitDisasm':'MixedProbe:UnionLoop MixedProbe:CopyLoop MixedProbe:ModifyLoop MixedProbe:EnumerateLoop *DirectArraySet:CopyDenseToMicro *DirectArraySet:UnionDenseToMicro *DirectArraySet:AppendDenseToMicro *DenseRecordCursor:MoveNext *DenseReverseCursor:MoveNext *Records:MoveNext'})
    for p in work.rglob('obj'):shutil.rmtree(p)
    print('DENSE_BULK_COMPLETE '+str(head),flush=True)
if __name__=='__main__':main()
