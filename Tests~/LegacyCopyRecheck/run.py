#!/usr/bin/env python3
"""Bounded long-batch legacy Copy replication; original operations/fixtures retained."""
import argparse, hashlib, json, math, os, platform, re, statistics, subprocess, resource, time
from pathlib import Path
HERE=Path(__file__).resolve().parent
ORIGINAL=HERE.parent/'Intersection'
COUNTS={'E1024.Copy.Reused':500000,'E1024.Query':100000000,'E1024.RemoveAdd':2000000,'E4.Copy.Reused.Supplemental':10000000}
def hashes(paths):return {str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}
def run(cmd,**kwargs):print('+ '+' '.join(map(str,cmd)),flush=True);return subprocess.run(list(map(str,cmd)),check=True,text=True,**kwargs)
def adapt(original):
 result=original
 for name in list(COUNTS)[:3]:
  pattern=r'(Measure\("'+re.escape(name)+r'",[^\n]*}, )\d+(\);)'
  result,n=re.subn(pattern,lambda m:m[1]+str(COUNTS[name])+m[2],result);assert n==1,name
 old='        Console.WriteLine("SINK\\t" + s_Sink);';assert result.count(old)==1;result=result.replace(old,'        LongBatchSmallCopy();\n'+old)
 start=original.index("    private static void Measure(");end=original.index("    private static void RunBenchmarks(",start)
 assert original[start:end]==result[start:end],"Original Measure must be byte-for-byte unchanged"
 return result

def parse(text):
 rows={};sink=None;environment=None
 for line in text.splitlines():
  p=line.split('\t')
  if p[0]=='RESULT':rows[p[1]]={'Ns':float(p[2]),'Bytes':float(p[3]),'Iterations':int(p[4])}
  elif p[0]=='SINK':assert sink is None;sink=int(p[1])
  elif p[0]=='ENV':environment=line
 expected={f'E{size}.Overlap{overlap}.{kind}' for size in (64,1024) for overlap in (0,50,100) for kind in ('Default.Reused','Default.New','Workspace.Reused')}|{'E1024.Ordered.Default.Reused','E1024.Copy.Reused','E1024.Union.New','E1024.Query','E1024.RemoveAdd','Sort.BCL.Segment','Sort.Specialized.Segment','E4.Copy.Reused.Supplemental'}
 assert rows.keys()==expected and sink is not None and environment
 for k,row in rows.items():
  row['WallMilliseconds']=row['Ns']*row['Iterations']/1e6;row['WallSource']='derived from original rounded RESULT ns/op';assert row['Ns']>0 and row['Bytes']>=0
  if k in COUNTS:assert row['Iterations']==COUNTS[k]
 return {'Environment':environment,'Checksum':sink,'Rows':rows}

def main():
 p=argparse.ArgumentParser(description=__doc__)
 for k in ['dotnet','before','after','out']:p.add_argument('--'+k,type=Path,required=True)
 for k in ['before-revision','after-revision']:p.add_argument('--'+k,required=True)
 p.add_argument('--mono',type=Path);p.add_argument('--mode',choices=['aa','ab'],required=True);p.add_argument('--rounds',type=int,default=14);p.add_argument('--padding',type=int,default=0);p.add_argument('--compile-only',action='store_true')
 args=p.parse_args();out=args.out.resolve();dotnet=args.dotnet.resolve();host=(args.mono or dotnet).resolve()
 for label in ('before','after'):
  if not re.fullmatch('[0-9a-f]{40}',getattr(args,label+'_revision')):p.error('Declared revisions must be full40 lowercase commit SHAs')
  if not list((getattr(args,label).resolve()/'Runtime').rglob('*.cs')):p.error('Nonempty Runtime C# source directory required: '+label)
 if out.exists() and any(out.iterdir()):p.error('Output must be new/empty')
 if args.rounds<1 or args.padding<0:p.error('positive rounds and nonnegative padding required')
 out.mkdir(parents=True);sources={'before':args.before.resolve(),'after':args.after.resolve()}
 checked=[Path(__file__).resolve(),HERE/'Supplement.cs',ORIGINAL/'Program.cs',ORIGINAL/'Benchmarks.cs',ORIGINAL/'GameplayTags.Intersection.Tests.csproj']
 for source in sources.values():checked+=sorted((source/'Runtime').rglob('*.cs'))
 initial=hashes(checked);generated=out/'Benchmarks.generated.cs';generated.write_text(adapt((ORIGINAL/'Benchmarks.cs').read_text()))
 project=(ORIGINAL/'GameplayTags.Intersection.Tests.csproj').read_text();project=project.replace('<Compile Include="Program.cs;Benchmarks.cs" />','<Compile Include="'+str(ORIGINAL/'Program.cs')+';'+str(generated)+';'+str(HERE/'Supplement.cs')+'" />')
 csproj=out/'GameplayTags.Intersection.Tests.csproj';csproj.write_text(project)
 env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_gcServer='0',COMPlus_TieredCompilation='0',COMPlus_gcServer='0')
 manifest={'Protocol':'legacy-copy-long-batch-v1','Mode':args.mode,'Sources':{k:str(v) for k,v in sources.items()},'DeclaredRevisions':{'before':args.before_revision,'after':args.after_revision},'SlotSources':{'before':'before','after':'before' if args.mode=='aa' else 'after'},'SourceHashes':initial,'GeneratedHashes':hashes([generated,csproj]),'ChosenCounts':COUNTS,'Rounds':args.rounds,'Padding':args.padding,'Commands':[],'ExecutionOrder':[],'RuntimeVersion':run([host,'--version'],capture_output=True).stdout,'DotnetInfo':run([dotnet,'--info'],capture_output=True).stdout,'Platform':platform.platform(),'Affinity':sorted(os.sched_getaffinity(0)),'Caveat':'Original merged-assembly build and all25 original fixtures/operations preserved. Only three predeclared row counts extended; small Copy appended after originals. Original120 warmup and separate allocation then timing passes retained. Original Measure byte-for-byte unchanged. No per-row CPU/GC instrumentation; external CPU/wall are whole-process only. Supplemental, not full acceptance.'}
 cpu=Path('/proc/cpuinfo');manifest['CPUModel']=next((x.split(':',1)[1].strip() for x in cpu.read_text().splitlines() if x.startswith('model name')),'unknown')
 assemblies={}
 for label,source in sources.items():
  build=out/(label+'-build');obj=out/(label+'-obj');cmd=[dotnet,'build',csproj,'-c','Release','-p:Baseline=false',f'-p:RuntimeRoot={source/"Runtime"}',f'-p:BaseIntermediateOutputPath={obj}/',f'-p:MSBuildProjectExtensionsPath={obj}/','-o',build]
  if args.mono:cmd+=['-p:MonoTarget=true']
  manifest['Commands'].append(list(map(str,cmd)));run(cmd,env=env)
  assemblies[label]=build/('GameplayTags.Intersection.Tests.exe' if args.mono else 'GameplayTags.Intersection.Tests.dll')
 binaries=[p for a in assemblies.values() for p in a.parent.iterdir() if p.suffix in ('.dll','.exe')];manifest['AssemblyHashes']=hashes(binaries)
 (out/'environment.json').write_text(json.dumps(manifest,indent=2));assert initial==hashes(checked)
 if args.compile_only:return
 for label,assembly in assemblies.items():
  cmd=[host,assembly];manifest['Commands'].append(list(map(str,cmd)));result=run(cmd,capture_output=True,env=env);(out/f'{label}-correctness.txt').write_text(result.stdout+result.stderr)
 reports={'before':[],'after':[]}
 for i in range(args.rounds):
  order=['before','after'] if i%2==0 else ['after','before'];manifest['ExecutionOrder'].append(order)
  for label in order:
   assembly=assemblies['before' if args.mode=='aa' else label];cmd=[host,assembly,'--bench',str(args.padding)];manifest['Commands'].append(list(map(str,cmd)))
   cpu0=resource.getrusage(resource.RUSAGE_CHILDREN);wall=time.perf_counter()
   result=subprocess.run(list(map(str,cmd)),capture_output=True,text=True,env=env,check=False)
   elapsed=time.perf_counter()-wall;cpu1=resource.getrusage(resource.RUSAGE_CHILDREN)
   (out/f'{label}-{i}.stdout.txt').write_text(result.stdout);(out/f'{label}-{i}.stderr.txt').write_text(result.stderr)
   process={'ExitCode':result.returncode,'Scope':'whole process including registration, all fixtures, warmup, allocation passes, timing passes and reporting; NOT per-row CPU','WallSeconds':elapsed,'UserCpuSeconds':cpu1.ru_utime-cpu0.ru_utime,'SystemCpuSeconds':cpu1.ru_stime-cpu0.ru_stime}
   (out/f'{label}-{i}.process.json').write_text(json.dumps(process,indent=2));result.check_returncode()
   report=parse(result.stdout);reports[label].append(report);(out/f'{label}-{i}.json').write_text(json.dumps(report,indent=2))
  assert reports['before'][-1]['Checksum']==reports['after'][-1]['Checksum'];assert reports['before'][-1]['Environment']==reports['after'][-1]['Environment']
  print('Complete pair',i+1,'/',args.rounds,flush=True)
 comparison=[]
 for case in COUNTS:
  a=[r['Rows'][case] for r in reports['before']];b=[r['Rows'][case] for r in reports['after']];logs=[math.log(x['Ns']/y['Ns']) for x,y in zip(a,b)];mean=statistics.mean(logs);width=3.012*statistics.stdev(logs)/len(logs)**.5 if len(logs)>=14 else float('inf');low=math.exp(mean-width) if math.isfinite(width) else None;high=math.exp(mean+width) if math.isfinite(width) else None;flags=[]
  if high is not None and high<1/1.03:flags.append('timing-regression')
  if low is not None and low>1.03:flags.append('timing-improvement')
  if any(r['WallMilliseconds']<100 for r in a+b):flags.append('short-batch-review')
  if statistics.median(r['Bytes'] for r in b)>statistics.median(r['Bytes'] for r in a):flags.append('allocation-regression')
  comparison.append({'Id':case,'BeforeNs':statistics.median(r['Ns'] for r in a),'AfterNs':statistics.median(r['Ns'] for r in b),'Approx99Lower':low,'Approx99Upper':high,'PairedGeometricSpeedup':math.exp(mean),'Flags':flags})
 assert initial==hashes(checked) and manifest['GeneratedHashes']==hashes([generated,csproj]) and manifest['AssemblyHashes']==hashes(binaries)
 (out/'comparison.json').write_text(json.dumps(comparison,indent=2));manifest['Validation']='All26 raw cases, original traversal, same counts and paired checksum/environment preserved; all process samples retained.';(out/'environment.json').write_text(json.dumps(manifest,indent=2))
if __name__=='__main__':main()
