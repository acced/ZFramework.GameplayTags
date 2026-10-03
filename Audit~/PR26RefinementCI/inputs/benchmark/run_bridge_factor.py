#!/usr/bin/env python3
from pathlib import Path
import argparse,hashlib,json,os,subprocess,time,platform
ROOT=Path(__file__).resolve().parent
SDK=ROOT.parent.parent/'comparison-materials/dotnet-sdk/dotnet'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 p=argparse.ArgumentParser();p.add_argument('--baseline',required=True);p.add_argument('--v2',required=True);p.add_argument('--v3',required=True);p.add_argument('--backend',choices=['default','nohw'],required=True);p.add_argument('--name',required=True);args=p.parse_args()
 plan=json.loads((ROOT/'bridge-factor-plan.json').read_text());root=ROOT/'results'/args.name
 if root.exists():raise RuntimeError('Run exists')
 root.mkdir(parents=True);variants=[args.baseline,args.v2,args.v3];identities={v:json.loads((ROOT/'builds'/v/'identity.json').read_text()) for v in variants}
 for ident in identities.values():
  assert sha(Path(ident['dll']))==ident['dll_sha256'];assert all(sha(Path(f))==h for f,h in ident['source_sha256'].items());assert all(sha(Path(f))==h for f,h in ident['dependency_sha256'].items())
 env=dict(os.environ,DOTNET_TieredCompilation='0',COMPlus_TieredCompilation='0',DOTNET_TieredPGO='0',COMPlus_TieredPGO='0',DOTNET_ReadyToRun='0',COMPlus_ReadyToRun='0',DOTNET_gcConcurrent='0',COMPlus_gcConcurrent='0')
 for prefix in ['DOTNET_','COMPlus_']:
  if args.backend=='nohw':env[prefix+'EnableHWIntrinsic']='0'
  else:env.pop(prefix+'EnableHWIntrinsic',None)
 manifest={'args':vars(args),'plan':plan,'plan_sha256':sha(ROOT/'bridge-factor-plan.json'),'identities':identities,'runner_path':str(Path(__file__).resolve()),'runner_sha256':sha(Path(__file__)),'started_utc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),'cpu_affinity':min(os.sched_getaffinity(0)),'hostname':platform.node(),'boot_id':Path('/proc/sys/kernel/random/boot_id').read_text().strip(),'env':{k:v for k,v in env.items() if k.startswith(('DOTNET_','COMPlus_'))},'runs':[]}
 def execute(variant,group,layout,mode,selection,label):
  ident=identities[variant];assert all(sha(Path(f))==h for f,h in ident['dependency_sha256'].items());assert Path('/proc/sys/kernel/random/boot_id').read_text().strip()==manifest['boot_id']
  command=['taskset','-c',str(manifest['cpu_affinity']),str(SDK),ident['dll'],layout,('legacy' if variant==args.baseline else 'direct'),str(mode)]
  started=time.time();result=subprocess.run(command,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
  (root/(label+'.stdout')).write_text(result.stdout);(root/(label+'.stderr')).write_text(result.stderr)
  entry=dict(label=label,variant=variant,group=group,layout=layout,command=command,returncode=result.returncode,elapsed_s=time.time()-started);manifest['runs'].append(entry);(root/'manifest.json').write_text(json.dumps(manifest,indent=2))
  if result.returncode:raise RuntimeError(label+': '+result.stderr[-2000:])
  data=json.loads(result.stdout);assert data['runtime_assembly_sha256']==ident['runtime_dll_sha256'];assert Path(data['runtime_assembly_path']).resolve()==(Path(ident['dll']).parent/'GameplayTags.dll').resolve();assert {r['id'] for r in data['rows']}==set(json.loads(selection.read_text()))
  (root/(label+'.json')).write_text(json.dumps(data,indent=2));entry['result_sha256']=sha(root/(label+'.json'));entry['stdout_sha256']=sha(root/(label+'.stdout'));(root/'manifest.json').write_text(json.dumps(manifest,indent=2));print(label,round(time.time()-started,2),'s',flush=True);return data
 for group,layout in sorted({(c['group'],c['layout']) for c in plan['cells']}):
  ids=[c['id'] for c in plan['cells'] if c['group']==group and c['layout']==layout];selection=root/f'selection-{group}-{layout}.json';selection.write_text(json.dumps(ids,indent=2))
  calibrations=[execute(v,group,layout,'calibrate',selection,f'calibrate-{group}-{layout}-{v}') for v in variants]
  counts={id:max(r['iterations'] for cal in calibrations for r in cal['rows'] if r['id']==id) for id in ids}
  for cal in calibrations:
   for row in cal['rows']:
    if row['allocation_bytes_per_op']>0:counts[row['id']]=min(counts[row['id']],int(64*1024*1024/row['allocation_bytes_per_op']))
  iterations=root/f'iterations-{group}-{layout}.json';iterations.write_text(json.dumps(counts,indent=2))
  for r in range(3):
   arms=[(args.baseline,'A'),(args.v2,'B'),(args.v3,'C'),(args.baseline,'AA')] if r%2==0 else [(args.baseline,'AA'),(args.v3,'C'),(args.v2,'B'),(args.baseline,'A')]
   for variant,arm in arms:execute(variant,group,layout,iterations,selection,f'r{r}-{group}-{layout}-{arm}')
 manifest['completed_utc']=time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime());(root/'manifest.json').write_text(json.dumps(manifest,indent=2))
if __name__=='__main__':main()
