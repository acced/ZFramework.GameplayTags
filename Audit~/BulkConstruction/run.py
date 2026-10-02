#!/usr/bin/env python3
"""Bulk construction on pinned classes; same-binary bulk A/A, no adaptive selector."""
import argparse, pathlib, sys, os, subprocess, json, hashlib, random, time, shutil
from xml.sax.saxutils import escape
ROOT=pathlib.Path(__file__).resolve().parents[2]; HERE=ROOT/'Audit~/BulkConstruction'
sys.path.insert(0,str(HERE)); from generate import generate, PIN
SDK='8.0.425'
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    ap=argparse.ArgumentParser();ap.add_argument('--output',required=True);ap.add_argument('--rounds',type=int,default=3);ap.add_argument('--smoke',action='store_true');args=ap.parse_args()
    if args.rounds<1:ap.error('positive rounds required')
    out=pathlib.Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True);work=out/'build';work.mkdir(exist_ok=True)
    (work/'global.json').write_text(json.dumps({'sdk':{'version':SDK,'rollForward':'disable'}}))
    (work/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
    env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_ReadyToRun='0',DOTNET_NOLOGO='1',DOTNET_CLI_TELEMETRY_OPTOUT='1')
    if hasattr(os,'sched_setaffinity'):os.sched_setaffinity(0,{min(os.sched_getaffinity(0))})
    def run(cmd,log,extra=None):
        proc=subprocess.run(list(map(str,cmd)),cwd=work,env=dict(env,**(extra or {})),stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,timeout=1800)
        (out/log).write_text(proc.stdout)
        if proc.returncode:raise RuntimeError(str(log)+' failed\n'+proc.stdout[-15000:])
        return proc.stdout
    actual=run(['dotnet','--version'],'sdk-version.txt').strip()
    if actual!=SDK:raise RuntimeError('Wrong SDK: '+actual)
    run(['dotnet','--info'],'environment.txt')
    subprocess.check_call(['git','diff','--exit-code','84729cc3b6e1b4681328806df2d7c8a1d8f568f6','HEAD','--','Runtime','Editor','Samples~','Tests','package.json'],cwd=ROOT)
    files=generate(out/'generated');runtimebase=ROOT/'Runtime/RuntimeTagSet.cs'
    common=list((ROOT/'Runtime').rglob('*.cs'));common.remove(runtimebase)
    common+=[ROOT/'Audit~/UnityStubs.cs',ROOT/'Audit~/Fusion/DenseFusion.cs']
    def build(label,sources,symbol='',framework='net8.0',references=(),exe=True):
        folder=work/label;folder.mkdir(exist_ok=True);project=folder/'Probe.csproj';assembly=label.replace('-','_')
        items=''.join('<Compile Include="'+escape(str(p))+'"/>' for p in sources)
        for ref in references:items+='<Reference Include="'+escape(ref.stem)+'"><HintPath>'+escape(str(ref))+'</HintPath></Reference>'
        project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>'+framework+'</TargetFramework><AssemblyName>'+assembly+'</AssemblyName><OutputType>'+('Exe' if exe else 'Library')+'</OutputType><LangVersion>9.0</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><AllowUnsafeBlocks>true</AllowUnsafeBlocks><Optimize>true</Optimize><Deterministic>true</Deterministic><DefineConstants>'+symbol+'</DefineConstants></PropertyGroup><ItemGroup>'+items+'</ItemGroup></Project>')
        run(['dotnet','build',project,'-c','Release','-o',folder/'bin'],label+'-build.log')
        return folder/'bin'/(assembly+'.dll')
    source={'runtime':common+files['runtime'],'micro':common+[runtimebase,ROOT/'Audit~/MicroBitmapSet.cs']+files['micro']}
    builds={}
    for kind in ('runtime','micro'):
        symbol='BULK_MICRO' if kind=='micro' else ''
        builds[kind]=build(kind,source[kind]+[HERE/'BulkProbe.cs',HERE/'BulkTests.cs'],symbol)
        print(run(['dotnet',builds[kind],kind,'tests',out/(kind+'-tests.json')],kind+'-tests.log').strip(),flush=True)
        print(run(['dotnet',builds[kind],kind,'tests',out/(kind+'-nohw-tests.json')],kind+'-nohw-tests.log',{'DOTNET_EnableHWIntrinsic':'0'}).strip(),flush=True)
        portable=build(kind+'-portable',source[kind],symbol,'netstandard2.1',exe=False)
        hostfolder=work/(kind+'-portable-host');hostfolder.mkdir(exist_ok=True)
        # Only untimed settings setup crosses an internal test-fixture boundary via reflection.
        text=(HERE/'BulkProbe.cs').read_text().replace('reg.WordCount','((reg.Count + 63) / 64)')
        text=text.replace('settings.ReplaceAll(', 'SettingsForTest(settings,')
        helper='''    static void SettingsForTest(GameplayTagSettings settings, List<GameplayTagDefinition> tags,
        List<GameplayTagRedirect> redirects, List<GameplayTagSource> sources)
    {
        typeof(GameplayTagSettings).GetMethod("ReplaceAll", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(settings, new object[] { tags, redirects, sources });
    }
'''
        text=text.replace('    static TagRegistry Registry(int leaves)',helper+'    static TagRegistry Registry(int leaves)')
        harness=hostfolder/'BulkProbe.cs';harness.write_text(text)
        host=build(kind+'-portable-host',[harness,HERE/'BulkTests.cs'],symbol,references=[portable])
        print(run(['dotnet',host,kind,'tests',out/(kind+'-portable-tests.json')],kind+'-portable-tests.log').strip(),flush=True)
    full=build('full-runtime',source['runtime']+list((ROOT/'Editor').rglob('*.cs'))+list((ROOT/'Samples~').rglob('*.cs'))+[ROOT/'Audit~/IntegerTests.cs',ROOT/'Audit~/IntegerTestSupport.cs'])
    dest=out/'full-runtime';dest.mkdir(exist_ok=True)
    print(run(['dotnet',full,dest],'full-runtime.log').splitlines()[-1],flush=True)
    tests=work/'NormalizedMicroTests.cs';tests.write_text((ROOT/'Audit~/MicroTests.cs').read_text().replace('MicroBitmapSet','DirectArraySet'))
    fullmicro=build('full-micro',source['micro']+[tests],'BULK_MICRO')
    print(run(['dotnet',fullmicro,out/'full-micro.json'],'full-micro.log').splitlines()[-1],flush=True)
    head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
    protected={str(p.relative_to(ROOT)):sha(p) for folder in ('Runtime','Editor','Samples~','Tests') for p in (ROOT/folder).rglob('*') if p.is_file()}
    specs={label:builds['micro' if label.startswith('micro') else 'runtime'] for label in ('array','arrayAA','dense','denseAA','micro','microAA')}
    (out/'manifest.json').write_text(json.dumps({'head':head,'base':PIN,'sdk':actual,'cpu':os.uname().machine,'rounds':args.rounds,'smoke':args.smoke,
        'affinity':list(os.sched_getaffinity(0)) if hasattr(os,'sched_getaffinity') else [],'env':{k:v for k,v in env.items() if k.startswith('DOTNET_')},
        'binaries':{k:{'path':str(v.relative_to(out)),'sha256':sha(v)} for k,v in specs.items()},'protected':protected,
        'contract':'forced layouts; strict sorted/general construction, pre-sized repeated Add; no selector; unchanged standalone set operations'},indent=2))
    for r in range(args.rounds):
        order=list(specs);random.Random(20261002+r).shuffle(order)
        for label in order:
            name=label+'-r'+str(r);t=time.monotonic();print('START '+name,flush=True)
            text=run(['dotnet',specs[label],label,'bench',out/(name+'.json'),r]+(['smoke'] if args.smoke else []),name+'.log')
            print(text.strip(),round(time.monotonic()-t,2),flush=True)
    for label in ('array','dense','micro'):
        print(run(['dotnet',specs[label],label,'bench',out/(label+'-codegen.json'),0,'smoke'],label+'-codegen.log',
            {'DOTNET_JitDisasm':'BulkProbe:ConstructionLoop GameplayTags.RuntimeTagSet:FromSortedUnique GameplayTags.RuntimeTagSet:FillSortedHandles GameplayTags.Experiments.DirectArraySet:FromSortedUnique GameplayTags.Experiments.DirectArraySet:FillSortedHandles'}).strip(),flush=True)
    # Keep exact project files and portable fixture; omit caches and binary blobs from deliverables.
    projects=out/'projects';projects.mkdir(exist_ok=True)
    for p in work.rglob('*.csproj'):shutil.copy2(p,projects/(p.parent.name+'.csproj'))
    for p in work.glob('*-portable-host/BulkProbe.cs'):shutil.copy2(p,projects/(p.parent.name+'.cs'))
    shutil.rmtree(work);print('BULK_COMPLETE '+head,flush=True)
if __name__=='__main__':main()
