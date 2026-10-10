#!/usr/bin/env python3
"""Bounded unchanged frozen harness diagnostic; isolated same-binary A/A or A/B."""
import argparse, hashlib, importlib.util, json, os, platform, subprocess
from pathlib import Path
HERE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('frozen_evolution',HERE.parent/'Evolution'/'run.py')
frozen=importlib.util.module_from_spec(spec);spec.loader.exec_module(frozen)
def selected(k):return k.startswith('n4/overlap') and k.endswith(('/intersection-reused','/workspace-reused'))
def main():
 p=argparse.ArgumentParser(description=__doc__)
 for name in ['before','after','out','iterations-file','dotnet']:p.add_argument('--'+name,type=Path,required=True)
 for name in ['before-revision','after-revision']:p.add_argument('--'+name,required=True)
 p.add_argument('--mode',choices=['aa','ab'],required=True);p.add_argument('--rounds',type=int,default=14);p.add_argument('--compile-only',action='store_true')
 args=p.parse_args();out=args.out.resolve();dotnet=args.dotnet.resolve()
 if out.exists() and any(out.iterdir()):p.error('Output must be new/empty')
 if args.rounds<1:p.error('rounds must be positive')
 out.mkdir(parents=True);sources={'before':args.before.resolve(),'after':args.after.resolve()}
 counts={}
 for line in args.iterations_file.read_text().splitlines():
  k,v=line.split('\t');assert k not in counts and int(v)>0;counts[k]=int(v)
 assert len(counts)==131 and sum(selected(k) for k in counts)==6
 assert all(v==65 for k,v in counts.items() if not selected(k))
 path=out/'iterations.tsv';path.write_bytes(args.iterations_file.read_bytes())
 files=[Path(__file__).resolve(),HERE.parent/'Evolution'/'Program.cs',HERE.parent/'Evolution'/'run.py',HERE.parent/'run.py',args.iterations_file.resolve()]
 for root in sources.values():files+=sorted((root/'Runtime').rglob('*.cs'))
 hashes=frozen.digest(files)
 env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_gcServer='0',COMPlus_TieredCompilation='0',COMPlus_gcServer='0')
 assemblies={label:frozen.compile_variant(dotnet,root,out/label) for label,root in sources.items()}
 binaryhashes=frozen.digest([p for assembly in assemblies.values() for p in assembly.parent.glob('*.dll')])
 manifest={'Protocol':'original-unchanged-single-process-guard-v1','Mode':args.mode,'DeclaredRevisions':{'before':args.before_revision,'after':args.after_revision},'SlotSources':{'before':'before','after':'before' if args.mode=='aa' else 'after'},'SourceHashes':hashes,'AssemblyHashes':binaryhashes,'HarnessSource':str(HERE.parent/'Evolution'/'Program.cs'),'Iterations':counts,'Rounds':args.rounds,'Commands':[],'Platform':platform.platform(),'Affinity':sorted(os.sched_getaffinity(0)),'DotnetInfo':subprocess.check_output([str(dotnet),'--info'],text=True),'Caveat':'Original harness and separate runtime assembly build unchanged; one process/registry, no producer thread. Nonselected measured iterations65 instead of full-matrix counts; original64-call warmup and retention cohorts retained. Supplemental only.'}
 cpu=Path('/proc/cpuinfo');manifest['CPUModel']=next((x.split(':',1)[1].strip() for x in cpu.read_text().splitlines() if x.startswith('model name')),'unknown')
 (out/'environment.json').write_text(json.dumps(manifest,indent=2))
 assert hashes==frozen.digest(files)
 if args.compile_only:return
 reports={'before':[],'after':[]}
 # Native inspection uses original single-process program in separate untimed runs.
 for label,assembly in assemblies.items():
  command=[str(dotnet),str(assembly),'bench','0',str(path)];manifest['Commands'].append(command)
  inspectenv=dict(env,COMPlus_JitDisasm='*SetIntersection* *ContainsExplicit* *Find* *BuildFromExplicitIds* *Benchmark* *Measure*',COMPlus_JitStdOutFile=str(out/f'jit-{label}.txt'))
  with (out/f'inspection-{label}.json').open('w') as stdout,(out/f'inspection-{label}.stderr.txt').open('w') as stderr:subprocess.run(command,env=inspectenv,stdout=stdout,stderr=stderr,check=True)
 for round in range(args.rounds):
  order=['before','after'] if round%2==0 else ['after','before']
  for label in order:
   assembly=assemblies['before' if args.mode=='aa' else label];command=[str(dotnet),str(assembly),'bench','0',str(path)];manifest['Commands'].append(command)
   dest=out/f'{label}-{round}.json'
   with dest.open('w') as stdout,(out/f'{label}-{round}.stderr.txt').open('w') as stderr:subprocess.run(command,env=env,stdout=stdout,stderr=stderr,check=True)
   d=json.loads(dest.read_text());assert len(d['Rows'])==155
   for row in d['Rows']:
    if row['Iterations']:assert row['Iterations']==counts[row['Id']]
   reports[label].append(d)
  assert reports['before'][-1]['Checksum']==reports['after'][-1]['Checksum']
  assert reports['before'][-1]['RegistryCount']==reports['after'][-1]['RegistryCount']
  print('Completed isolated round',round+1,'/',args.rounds,flush=True)
 assert hashes==frozen.digest(files)
 assert binaryhashes==frozen.digest([p for assembly in assemblies.values() for p in assembly.parent.glob('*.dll')])
 result=frozen.comparisons(reports);(out/'comparison.json').write_text(json.dumps([r for r in result if selected(r['Id'])],indent=2));(out/'all-rows-diagnostic.json').write_text(json.dumps(result,indent=2))
 manifest['Validation']='All155 rows retained, identical131 fixed counts, paired checksums/registry equal, all samples preserved.';(out/'environment.json').write_text(json.dumps(manifest,indent=2))
if __name__=='__main__':main()
