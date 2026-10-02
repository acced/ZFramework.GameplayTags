#!/usr/bin/env python3
"""Run fixed PR12 vs preparation-v2, then explicit conversion costs on the same CPU."""
import argparse,pathlib,sys,os,subprocess,json,hashlib,random,time,shutil
from xml.sax.saxutils import escape
ROOT=pathlib.Path(__file__).resolve().parents[2];HERE=ROOT/'Audit~/PreparationV2'
sys.path.insert(0,str(HERE));from generate import generate,PIN
SDK='8.0.425'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    ap=argparse.ArgumentParser();ap.add_argument('--output',required=True);ap.add_argument('--rounds',type=int,default=3);ap.add_argument('--smoke',action='store_true');args=ap.parse_args()
    if args.rounds<1:ap.error('rounds must be positive')
    out=pathlib.Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True);work=out/'build';work.mkdir(exist_ok=True)
    (work/'global.json').write_text(json.dumps({'sdk':{'version':SDK,'rollForward':'disable'}}))
    (work/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
    env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_ReadyToRun='0',DOTNET_NOLOGO='1',DOTNET_CLI_TELEMETRY_OPTOUT='1')
    if hasattr(os,'sched_setaffinity'):os.sched_setaffinity(0,{min(os.sched_getaffinity(0))})
    def run(cmd,log,extra=None):
        p=subprocess.run(list(map(str,cmd)),cwd=work,env=dict(env,**(extra or {})),stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,timeout=1800)
        (out/log).write_text(p.stdout)
        if p.returncode:raise RuntimeError(str(log)+'\n'+p.stdout[-18000:])
        return p.stdout
    actual=run(['dotnet','--version'],'sdk.txt').strip();assert actual==SDK,actual
    run(['dotnet','--info'],'environment.txt')
    run(['lscpu'],'cpu.txt')
    subprocess.check_call(['git','diff','--exit-code','84729cc3b6e1b4681328806df2d7c8a1d8f568f6','HEAD','--','Runtime','Editor','Samples~','Tests','package.json'],cwd=ROOT)
    files=generate(out/'generated');base=ROOT/'Runtime/RuntimeTagSet.cs'
    common=list((ROOT/'Runtime').rglob('*.cs'));common.remove(base)
    common += [ROOT/'Audit~/UnityStubs.cs',ROOT/'Audit~/Fusion/DenseFusion.cs']
    text=(ROOT/'Audit~/BulkConstruction/BulkProbe.cs').read_text()
    text=text.replace('new[]{10000,65536,262144}','new[]{10000,262144}').replace('new[]{0,1,8,32,128,1024,4096}','new[]{0,1,2,8,128,4096}')
    text=text.replace('new[]{0,2,3,4}','new[]{0,2}')
    text=text.replace('var result=Tests();','var result=new{basic=Tests(),extra=MoreTests()};')
    text=text.replace('        Matrix(args.Length>4&&args[4]=="smoke");','''#if PREPARATION_V2
        if(stage=="convert") ConversionMatrix(args.Length>4&&args[4]=="smoke"); else
#endif
        Matrix(args.Length>4&&args[4]=="smoke");''')
    text=text.replace('new{label,round,runtime=','new{label,round,stage,runtime=')
    text=text.replace('unordered includes private scratch, sorting and deduplication.','unordered construction includes all work; v2 Dense skips sort, v2 Array reuses owned final storage when raw input fits.')
    harness=out/'generated/PreparationProbe.cs';harness.write_text(text)
    builds={};sources={}
    def build(label,src,symbol='',framework='net8.0',refs=(),exe=True):
        folder=work/label;folder.mkdir(exist_ok=True);proj=folder/'Probe.csproj';assembly=label.replace('-','_')
        items=''.join('<Compile Include="'+escape(str(p))+'"/>' for p in src)
        items+=''.join('<Reference Include="'+escape(p.stem)+'"><HintPath>'+escape(str(p))+'</HintPath></Reference>' for p in refs)
        proj.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>'+framework+'</TargetFramework><AssemblyName>'+assembly+'</AssemblyName><OutputType>'+('Exe' if exe else 'Library')+'</OutputType><LangVersion>9.0</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><AllowUnsafeBlocks>true</AllowUnsafeBlocks><Optimize>true</Optimize><Deterministic>true</Deterministic><DefineConstants>'+symbol+'</DefineConstants></PropertyGroup><ItemGroup>'+items+'</ItemGroup></Project>')
        run(['dotnet','build',proj,'-c','Release','-o',folder/'bin'],label+'-build.log')
        return folder/'bin'/(assembly+'.dll')
    for version in ('v1','v2'):
        for kind in ('runtime','micro'):
            key=version+'-'+kind;symbol=('BULK_MICRO;' if kind=='micro' else '')+('PREPARATION_V2' if version=='v2' else '')
            src=common+([base,ROOT/'Audit~/MicroBitmapSet.cs'] if kind=='micro' else [])+files[version][kind]
            sources[key]=src
            dll=build(key,src+[harness,ROOT/'Audit~/BulkConstruction/BulkTests.cs',HERE/'Extras.cs'],symbol);builds[key]=dll
            print(run(['dotnet',dll,key,'tests',out/(key+'-tests.json')],key+'-tests.log').strip(),flush=True)
            if version=='v2':
                print(run(['dotnet',dll,key,'tests',out/(key+'-nohw-tests.json')],key+'-nohw-tests.log',{'DOTNET_EnableHWIntrinsic':'0'}).strip(),flush=True)
                portable=build(key+'-portable',src,symbol,'netstandard2.1',exe=False)
                hosttext=text.replace('reg.WordCount','((reg.Count+63)/64)').replace('settings.ReplaceAll(','SettingsForTest(settings,')
                helper='''    static void SettingsForTest(GameplayTagSettings s,List<GameplayTagDefinition> t,List<GameplayTagRedirect> r,List<GameplayTagSource> p)
    { typeof(GameplayTagSettings).GetMethod("ReplaceAll",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(s,new object[]{t,r,p}); }
'''
                hosttext=hosttext.replace('    static TagRegistry Registry(int leaves)',helper+'    static TagRegistry Registry(int leaves)')
                hostfile=out/'generated'/(key+'-portable-host.cs');hostfile.write_text(hosttext)
                host=build(key+'-host',[hostfile,ROOT/'Audit~/BulkConstruction/BulkTests.cs',HERE/'Extras.cs'],symbol,refs=[portable])
                print(run(['dotnet',host,key,'tests',out/(key+'-portable-tests.json')],key+'-portable-tests.log').strip(),flush=True)
    full=build('full-runtime',sources['v2-runtime']+list((ROOT/'Editor').rglob('*.cs'))+list((ROOT/'Samples~').rglob('*.cs'))+[ROOT/'Audit~/IntegerTests.cs',ROOT/'Audit~/IntegerTestSupport.cs'],'PREPARATION_V2')
    dest=out/'full-runtime';dest.mkdir(exist_ok=True);print(run(['dotnet',full,dest],'full-runtime.log').splitlines()[-1],flush=True)
    microtest=out/'generated/NormalizedMicroTests.cs';microtest.write_text((ROOT/'Audit~/MicroTests.cs').read_text().replace('MicroBitmapSet','DirectArraySet'))
    fullmicro=build('full-micro',sources['v2-micro']+[microtest],'BULK_MICRO;PREPARATION_V2')
    print(run(['dotnet',fullmicro,out/'full-micro.json'],'full-micro.log').splitlines()[-1],flush=True)
    jobs=[]
    for version in ('v1','v2'):
        for mode in ('array','dense','micro'):
            dll=builds[version+'-'+('micro' if mode=='micro' else 'runtime')]
            for suffix in ('','AA'):jobs.append((mode+'-'+version+suffix,'bench',dll))
    for mode in ('array','micro'):
        for suffix in ('','AA'):jobs.append((mode+'-v2'+suffix,'convert',builds['v2-'+('micro' if mode=='micro' else 'runtime')]))
    protected={str(p.relative_to(ROOT)):sha(p) for d in ('Runtime','Editor','Samples~','Tests') for p in (ROOT/d).rglob('*') if p.is_file()}
    manifest={'head':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),
        'tree':subprocess.check_output(['git','rev-parse','HEAD^{tree}'],cwd=ROOT,text=True).strip(),'base':PIN,'sdk':SDK,'cpu':os.uname().machine,
        'rounds':args.rounds,'smoke':args.smoke,'affinity':list(os.sched_getaffinity(0)),'protected':protected,
        'jobs':[{'label':l,'stage':s,'binary':str(d.relative_to(out)),'sha256':sha(d)} for l,s,d in jobs],
        'env':{k:v for k,v in env.items() if k.startswith('DOTNET_')},'production_promoted':False,'selector_implemented':False}
    (out/'manifest.json').write_text(json.dumps(manifest,indent=2))
    for r in range(args.rounds):
        order=jobs.copy();random.Random(20261023+r).shuffle(order)
        for label,stage,dll in order:
            name=label+'-'+stage+'-r'+str(r);t=time.monotonic();print('START '+name,flush=True)
            print(run(['dotnet',dll,label,stage,out/(name+'.json'),r]+(['smoke'] if args.smoke else []),name+'.log').strip(),round(time.monotonic()-t,2),flush=True)
    for mode in ('array','micro'):
        key='v2-'+('micro' if mode=='micro' else 'runtime')
        for stage in ('bench','convert'):
            name=mode+'-'+stage+'-codegen'
            code=run(['dotnet',builds[key],mode,stage,out/(name+'.json'),0,'smoke'],name+'.log',
                {'DOTNET_JitDisasm':'BulkProbe:ConstructionLoop BulkProbe:ConversionLoop GameplayTags.RuntimeTagSet:FromUnordered GameplayTags.RuntimeTagSet:ValidateAndFillSorted GameplayTags.Experiments.DirectArraySet:CopyAsStorage'})
            print('CODEGEN '+name+' '+json.dumps([l for l in code.splitlines() if 'Assembly listing' in l or 'Total bytes of code' in l]),flush=True)
    # Keep exact binaries for A/A identity recheck, but discard build caches only.
    for p in work.rglob('obj'):shutil.rmtree(p)
    print('PREPARATION_V2_COMPLETE '+manifest['head'],flush=True)
if __name__=='__main__':main()
