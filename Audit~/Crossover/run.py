#!/usr/bin/env python3
"""Crossover experiments: common public callers, forced layouts, independent outputs.
No timing from this new calibrated protocol may be merged with historical PR scores.
"""
import argparse,pathlib,sys,os,subprocess,json,hashlib,random,time
from xml.sax.saxutils import escape
ROOT=pathlib.Path(__file__).resolve().parents[2];HERE=ROOT/'Audit~/Crossover'
sys.path.insert(0,str(HERE));from prepare import generate

def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 ap=argparse.ArgumentParser();ap.add_argument('--output',required=True);ap.add_argument('--dotnet',default='dotnet');ap.add_argument('--smoke',action='store_true');ap.add_argument('--rounds',type=int,default=3);ap.add_argument('--stage',default='all');args=ap.parse_args()
 out=pathlib.Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True);work=out/'build';work.mkdir(exist_ok=True)
 (work/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
 # Do not alter fixed source. Assert the full RuntimeTagSet blob, not just a few fragments.
 base=ROOT/'Runtime/RuntimeTagSet.cs';b=base.read_bytes()
 assert hashlib.sha1(b'blob '+str(len(b)).encode()+b'\0'+b).hexdigest()=='d19c0c635e8a2ecc9b5408f9e3df8b53440a176c'
 env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_ReadyToRun='0',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1',DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1')
 if hasattr(os,'sched_setaffinity'):os.sched_setaffinity(0,{min(os.sched_getaffinity(0))})
 def run(cmd,log,extra=None):
  proc=subprocess.run(list(map(str,cmd)),cwd=work,env=dict(env,**(extra or {})),stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,timeout=1800)
  (out/log).write_text(proc.stdout)
  if proc.returncode:raise RuntimeError(str(log)+'\n'+proc.stdout[-12000:])
  return proc.stdout
 def build(label,sources,symbol=''):
  folder=work/label;folder.mkdir(exist_ok=True);proj=folder/'Probe.csproj'
  proj.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><LangVersion>9.0</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><AllowUnsafeBlocks>true</AllowUnsafeBlocks><Optimize>true</Optimize><Deterministic>true</Deterministic><DefineConstants>'+symbol+'</DefineConstants></PropertyGroup><ItemGroup>'+''.join('<Compile Include="'+escape(str(p))+'"/>' for p in sources)+'</ItemGroup></Project>')
  run([args.dotnet,'build',proj,'-c','Release','-o',folder/'bin'],label+'-build.log')
  return folder/'bin/Probe.dll'
 sources,micro=generate(out/'generated');common=list((ROOT/'Runtime').rglob('*.cs'));common.remove(base)
 common += [ROOT/'Audit~/UnityStubs.cs',ROOT/'Audit~/Fusion/DenseFusion.cs']
 builds={}
 for label in ('original','binary','linear','vector','bounded'):
  builds[label]=build(label,common+[out/'generated'/label/'RuntimeTagSet.cs',HERE/'Crossover.cs',HERE/'Extras.cs'])
 builds['micro']=build('micro',common+[base,ROOT/'Audit~/MicroBitmapSet.cs',micro,HERE/'Crossover.cs',HERE/'Extras.cs'],'MICRO')
 (out/'environment.txt').write_text(run([args.dotnet,'--info'],'dotnet.log'))
 (out/'manifest.json').write_text(json.dumps({'baseline':'40696c95b6c53e95ef6300c995f01c9b443f0a3e','runtime_blob':'d19c0c635e8a2ecc9b5408f9e3df8b53440a176c','affinity':list(os.sched_getaffinity(0)) if hasattr(os,'sched_getaffinity') else [],'machine':os.uname().machine,'rounds':args.rounds,'smoke':args.smoke,'env':{k:v for k,v in env.items() if k.startswith('DOTNET_')},'binary_sha256':{k:sha(v) for k,v in builds.items()},'sources':{str(p.relative_to(ROOT)):sha(p) for p in HERE.glob('*') if p.is_file()},'copy_contract':'Micro copy preserves physical record capacity; patch archived','selection_policy':'none fitted; forced curves are diagnostics, not an adaptive product'},indent=2))
 for label,dll in builds.items():
  print(run([args.dotnet,dll,label,'tests',out/(label+'-tests.json')],label+'-tests.log').strip(),flush=True)
  if label in ('vector','bounded','micro'):print(run([args.dotnet,dll,label,'tests','unused'],label+'-nohw.log',{'DOTNET_EnableHWIntrinsic':'0'}).strip(),flush=True)
 # Original full public API / hierarchy / FrozenQuery suite for every changed Runtime.
 full_common=common+list((ROOT/'Editor').rglob('*.cs'))+list((ROOT/'Samples~').rglob('*.cs'))+[ROOT/'Audit~/IntegerTests.cs',ROOT/'Audit~/IntegerTestSupport.cs']
 for label in ('binary','linear','vector','bounded'):
  dll=build(label+'-full',full_common+[out/'generated'/label/'RuntimeTagSet.cs']);dest=out/(label+'-full');dest.mkdir(exist_ok=True)
  print(run([args.dotnet,dll,dest],label+'-full.log').splitlines()[-1],flush=True)
 jobs=[]
 if args.stage in ('all','query'):
  jobs += [(label,'query',builds[label]) for label in ('original','binary','linear','vector','bounded')]
  jobs += [('binaryAA','query',builds['binary'])]
 if args.stage in ('all','extras'):jobs += [('binary','ratio',builds['binary']),('binary','inline',builds['binary'])]
 if args.stage in ('all','sets'):jobs += [(name,'sets',builds['binary'] if name in ('array','dense') else builds['original'] if name=='auto' else builds['micro']) for name in ('auto','array','dense','micro')]
 for r in range(args.rounds):
  order=jobs.copy();random.Random(20261001+r).shuffle(order)
  for label,stage,dll in order:
   tag=f'{label}-{stage}-r{r}';start=time.monotonic()
   print('START '+tag,flush=True)
   print(run([args.dotnet,dll,label,stage,out/(tag+'.json'),r]+(['smoke'] if args.smoke else []),tag+'.log').strip(),round(time.monotonic()-start,2),flush=True)
 # Disassemble the exact measured caller, not an unmeasured copy of the algorithm.
 for label,stage in [('binary','query'),('vector','query'),('bounded','query'),('dense','sets')]:
  dll=builds[label] if label in builds else builds['binary']
  run([args.dotnet,dll,label,stage,out/(label+'-codegen.json'),0,'smoke'],label+'-codegen.log',{'DOTNET_JitDisasm':'Crossover:QueryLoop Crossover:OperationLoop GameplayTags.Experiments.DenseFusion:*'})
 print('DONE',flush=True)
if __name__=='__main__':main()
