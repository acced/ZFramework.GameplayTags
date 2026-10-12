#!/usr/bin/env python3
"""Small four-pair registry rejection screen; never an acceptance gate."""
import argparse,hashlib,io,json,os,pathlib,re,shutil,signal,statistics,subprocess,tarfile,time
ROOT=pathlib.Path(__file__).resolve().parent
BASE='f7c99546dbf029d70896c0f05e5119b19ffd6d26'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def save(p,v):p.write_text(json.dumps(v,indent=2)+'\n')
def main():
 p=argparse.ArgumentParser();p.add_argument('--backend',choices=['net','mono'],required=True);p.add_argument('--dotnet',type=pathlib.Path,required=True);p.add_argument('--mono',type=pathlib.Path);p.add_argument('--nuget',type=pathlib.Path,required=True);p.add_argument('--git-root',type=pathlib.Path,required=True);p.add_argument('--out',type=pathlib.Path,required=True);p.add_argument('--work',type=pathlib.Path,required=True);a=p.parse_args()
 a.dotnet=a.dotnet.resolve();a.nuget=a.nuget.resolve();a.git_root=a.git_root.resolve();a.out=a.out.resolve();a.work=a.work.resolve();a.mono=a.mono.resolve() if a.mono else None
 a.out.mkdir(parents=True,exist_ok=False);a.work.mkdir(parents=True,exist_ok=False);(a.out/'raw').mkdir();deadline=time.monotonic()+600
 env=dict(os.environ,DOTNET_CLI_HOME=str(a.work/'cli-home'),DOTNET_TieredCompilation='0',DOTNET_gcServer='0',COMPlus_TieredCompilation='0',COMPlus_gcServer='0')
 pinned={str(x.relative_to(ROOT)):sha(x) for x in ROOT.rglob('*') if x.is_file() and '__pycache__' not in x.parts};binary_pins={};commands=[]
 def check():
  assert all(sha(ROOT/k)==v for k,v in pinned.items()),'Package changed'
  assert all(sha(pathlib.Path(k))==v for k,v in binary_pins.items()),'Source/toolchain/binary changed'
 def run(cmd,name):
  check();remaining=deadline-time.monotonic()
  if remaining<=0:raise TimeoutError('whole-screen-deadline')
  so=a.work/(name+'.stdout');se=a.work/(name+'.stderr');started=time.monotonic();status='failed';code=None
  with so.open('wb') as o,se.open('wb') as e:
   child=subprocess.Popen([str(x) for x in cmd],stdout=o,stderr=e,env=env,start_new_session=True)
   try:
    code=child.wait(timeout=min(180,remaining));status='completed' if code==0 else 'failed'
    if code:
     try:os.killpg(child.pid,signal.SIGKILL)
     except ProcessLookupError:pass
   except BaseException:
    try:os.killpg(child.pid,signal.SIGKILL)
    except ProcessLookupError:pass
    child.wait();status='timeout-or-interrupted';raise
   finally:
    commands.append({'Name':name,'Status':status,'ExitCode':code,'WallSeconds':time.monotonic()-started,'StdoutSHA256':sha(so),'StderrSHA256':sha(se)})
    save(a.out/'status.json',{'Status':'running','Commands':commands,'AcceptanceEvidence':False})
  if code:raise RuntimeError('child-failed')
  check();return so.read_bytes()
 try:
  sdk=a.dotnet.parent/'sdk/8.0.425';assert sdk.is_dir();compiler=sdk/'Roslyn/bincore/csc.dll'
  runtime=a.dotnet.parent/'shared/Microsoft.NETCore.App/8.0.31';assert runtime.is_dir()
  identity=json.loads((ROOT/'source-identities.json').read_text());assert identity['Baseline']==BASE
  archive=run(['git','-C',a.git_root,'archive',BASE,'Runtime'],'archive')
  for variant in ['before','candidate']:
   source=a.work/variant;source.mkdir()
   with tarfile.open(fileobj=io.BytesIO(archive)) as tar:
    assert all(not x.name.startswith('/') and '..' not in pathlib.PurePosixPath(x.name).parts for x in tar.getmembers());tar.extractall(source,filter='data')
   if variant=='candidate':shutil.copyfile(ROOT/'GameplayTagRegistrationContext.candidate.cs',source/'Runtime/GameplayTagRegistrationContext.cs')
   observed={str(f.relative_to(source)):sha(f) for f in (source/'Runtime').rglob('*') if f.is_file()};assert observed==identity['Files'][variant]
   binary_pins.update({str(source/k):v for k,v in observed.items()})
  if a.backend=='mono':
   assert a.mono
   fw=a.nuget/'microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2'
   packages=[a.nuget/x for x in ['system.memory/4.5.5/lib/net461/System.Memory.dll','system.buffers/4.5.1/lib/net461/System.Buffers.dll','system.runtime.compilerservices.unsafe/4.5.3/lib/net461/System.Runtime.CompilerServices.Unsafe.dll','system.numerics.vectors/4.5.0/lib/net46/System.Numerics.Vectors.dll']]
   refs=[fw/x for x in ['mscorlib.dll','System.dll','System.Core.dll','System.Runtime.Serialization.dll']]+packages
  else:
   packages=[];refs=sorted((a.dotnet.parent/'packs/Microsoft.NETCore.App.Ref/8.0.31/ref/net8.0').glob('*.dll'));assert refs
  binary_pins.update({str(x):sha(x) for x in refs+[compiler,a.dotnet]+([a.mono] if a.mono else [])})
  version=run([a.mono,'--version'] if a.mono else [a.dotnet,'--version'],'runtime-version').decode()
  mono_version=re.search(r'Mono JIT compiler version ([0-9.]+)',version).group(1) if a.mono else None
  cpu='unknown'
  for line in pathlib.Path('/proc/cpuinfo').read_text().splitlines():
   if line.startswith('model name'):cpu=line.split(':',1)[1].strip();break
  provenance={'Baseline':BASE,'CandidateKind':'f7-plus-immediate-parent-registration','SourceHashes':identity['Files'],'PackageHashes':pinned,'CompilerSDK':'8.0.425','CoreCLR':'8.0.31','Mono':mono_version,'CPUModel':cpu,'Backend':a.backend,'TieredCompilation':False,'ServerGC':False,'Rounds':4,'AcceptanceEvidence':False,'Comparisons':'exact f7 operation versus patched same operation; no prior candidate composition','ReferenceHashes':{str(x.relative_to(a.nuget)) if a.backend=='mono' else x.name:sha(x) for x in refs},'CompilerSHA256':sha(compiler)}
  cmds={};correct={}
  for v in ['before','candidate']:
   build=a.work/(v+'-build');build.mkdir();asm=build/('Registry.exe' if a.mono else 'Registry.dll')
   for x in packages:shutil.copyfile(x,build/x.name)
   files=sorted(x for x in (a.work/v/'Runtime').rglob('*.cs') if x.name!='GameObjectGameplayTagContainer.cs')+[ROOT/'Program.cs']
   flags=['-nologo','-optimize+','-langversion:9.0','-nullable:disable','-deterministic+','-target:exe','-out:"'+str(asm)+'"']+['-r:"'+str(x)+'"' for x in refs]+['"'+str(x)+'"' for x in files]
   rsp=build/'compile.rsp';rsp.write_text('\n'.join(flags));run([a.dotnet,compiler,'@'+str(rsp)],'compile-'+v)
   save(asm.with_suffix('.runtimeconfig.json'),{'runtimeOptions':{'tfm':'net8.0','rollForward':'Disable','framework':{'name':'Microsoft.NETCore.App','version':'8.0.31'},'configProperties':{'System.Runtime.TieredCompilation':False,'System.GC.Server':False}}})
   binary_pins.update({str(x):sha(x) for x in build.iterdir() if x.is_file()});cmds[v]=[a.mono or a.dotnet,asm]
   lines=run(cmds[v]+['--correctness'],'correctness-'+v).decode().splitlines();assert len(lines)==9 and lines[-1]=='CASE 8 EX System.NullReferenceException'
   assert all(re.fullmatch('CASE '+str(i)+' [0-9A-F]{16}',line) for i,line in enumerate(lines[:-1]));correct[v]=lines
  assert correct['before']==correct['candidate'];save(a.out/'correctness.json',correct)
  mp=ROOT/('iterations-'+a.backend+'.tsv');expected={s.split('\t')[0]:int(s.split('\t')[1]) for s in mp.read_text().splitlines()};assert len(expected)==12
  provenance['Iterations']=expected;provenance['MapSHA256']=sha(mp);provenance['AssemblyHashes']={v:sha(cmds[v][1]) for v in cmds};save(a.out/'provenance.json',provenance)
  runs={v:[] for v in cmds}
  for i in range(4):
   for v in (['before','candidate'] if i%2==0 else ['candidate','before']):
    target=a.work/(v+'-'+str(i)+'.json');run(cmds[v]+['--out',target,'--variant',v,'--round',str(i),'--samples','1','--iterations-file',mp],v+'-'+str(i))
    d=json.loads(target.read_text());assert d['ServerGC'] is False and d['TieredCompilation'] is False
    if a.backend=='net':assert d['Runtime']=='.NET 8.0.31' and d['RuntimeTieringSwitch'] is False
    else:assert d['Runtime'].startswith('Standalone Mono '+mono_version+' ')
    rows=d['Measurements'];ids=[r['CaseId'] for r in rows];assert len(ids)==12 and len(set(ids))==12 and set(ids)==set(expected)
    assert all(r['IterationsPerSample']==expected[r['CaseId']] for r in rows);runs[v].append({r['CaseId']:r for r in rows})
    public={k:d[k] for k in ['Variant','Round','Runtime','PointerSizeBytes','TieredCompilation','RuntimeTieringSwitch','ServerGC','Workload','Measurements']};public['Runtime']=('.NET 8.0.31' if a.backend=='net' else 'Standalone Mono '+mono_version);save(a.out/'raw'/(v+'-'+str(i)+'.json'),public)
  summary=[]
  for key in expected:
   b=[x[key] for x in runs['before']];c=[x[key] for x in runs['candidate']]
   for x,y in zip(b,c):
    for f in ['FixtureChecksum','OutputChecksum','IterationsPerSample','HierarchyPathSlots','FrozenArrayPayloadBytes','Checksum']:assert x[f]==y[f]
   summary.append({'Id':key,'BeforeNs':[x['NanosecondsPerOperation'] for x in b],'AfterNs':[x['NanosecondsPerOperation'] for x in c],'PairedAfterBefore':[y['NanosecondsPerOperation']/x['NanosecondsPerOperation'] for x,y in zip(b,c)],'BeforeBytes':[x['AllocatedBytesPerOperation'] for x in b],'AfterBytes':[x['AllocatedBytesPerOperation'] for x in c],'BeforeBatchMs':[x['MeasuredBatchMilliseconds'] for x in b],'AfterBatchMs':[x['MeasuredBatchMilliseconds'] for x in c],'AcceptanceEvidence':False})
  check()
  if time.monotonic()>deadline:raise TimeoutError('whole-screen-deadline')
  save(a.out/'summary.json',summary);save(a.out/'status.json',{'Status':'screen-completed-not-accepted','Commands':commands,'AcceptanceEvidence':False})
 except BaseException as e:
  save(a.out/'status.json',{'Status':'screen-failed-or-incomplete','ErrorType':type(e).__name__,'Commands':commands,'AcceptanceEvidence':False});raise SystemExit(1)
if __name__=='__main__':main()
