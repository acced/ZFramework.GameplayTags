#!/usr/bin/env python3
from pathlib import Path
import argparse, hashlib, json, os, subprocess, time, platform
ROOT=Path(__file__).resolve().parent
SDK=ROOT.parent.parent/'comparison-materials/dotnet-sdk/dotnet'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 p=argparse.ArgumentParser();p.add_argument('--baseline',default='baseline');p.add_argument('--candidate',required=True);p.add_argument('--group',choices=['query','builder','mixed','popcount','growth'],required=True);p.add_argument('--backend',choices=['default','nohw'],required=True);p.add_argument('--rounds',type=int,default=3);p.add_argument('--name',required=True);p.add_argument('--layouts',default='Micro,Dense,Auto');args=p.parse_args()
 run=ROOT/'results'/args.name
 if run.exists():raise RuntimeError('Run directory already exists; use a new name')
 run.mkdir(parents=True); cpu=min(os.sched_getaffinity(0)); variants=[args.baseline,args.candidate];identities={}
 for variant in variants:
  ident=json.loads((ROOT/'builds'/variant/'identity.json').read_text());identities[variant]=ident
  assert sha(Path(ident['dll']))==ident['dll_sha256']
  assert all(sha(Path(f))==v for f,v in ident['source_sha256'].items())
  assert all(sha(Path(f))==v for f,v in ident['dependency_sha256'].items())
  assert sha(Path(ident['runtime_dll']))==ident['runtime_dll_sha256']
 env=dict(os.environ,DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',DOTNET_CLI_HOME=str(ROOT/'home'),DOTNET_TieredCompilation='0',COMPlus_TieredCompilation='0',DOTNET_TieredPGO='0',COMPlus_TieredPGO='0',DOTNET_ReadyToRun='0',COMPlus_ReadyToRun='0',DOTNET_gcConcurrent='0',COMPlus_gcConcurrent='0')
 for k in ['DOTNET_EnableHWIntrinsic','COMPlus_EnableHWIntrinsic']:
  if args.backend=='nohw':env[k]='0'
  else:env.pop(k,None)
 manifest={'args':vars(args),'started_utc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),'cpu_affinity':cpu,'platform':platform.platform(),'hostname':platform.node(),'boot_id':Path('/proc/sys/kernel/random/boot_id').read_text().strip(),'cpuinfo':Path('/proc/cpuinfo').read_text().split('\n\n')[0],'identities':identities,'harness_path':identities[args.baseline].get('harness',str(ROOT/'Probe.cs')),'harness_sha256':sha(Path(identities[args.baseline].get('harness',str(ROOT/'Probe.cs')))),'runner_path':str(Path(__file__).resolve()),'runner_sha256':sha(Path(__file__)),'env':{k:v for k,v in env.items() if k.startswith(('DOTNET_','COMPlus_'))},'runs':[]}
 def execute(variant,layout,mode,label,group=None):
  assert Path('/proc/sys/kernel/random/boot_id').read_text().strip()==manifest['boot_id']
  ident=identities[variant];assert sha(Path(ident['dll']))==ident['dll_sha256']
  assert all(sha(Path(f))==v for f,v in ident['dependency_sha256'].items())
  cmd=['taskset','-c',str(cpu),str(SDK),ident['dll'],layout,group or args.group,str(mode)]
  started=time.time();r=subprocess.run(cmd,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
  (run/(label+'.stdout')).write_text(r.stdout);(run/(label+'.stderr')).write_text(r.stderr)
  manifest['runs'].append(dict(label=label,variant=variant,layout=layout,command=cmd,elapsed_s=time.time()-started,returncode=r.returncode,dll_sha256=ident['dll_sha256']))
  (run/'manifest.json').write_text(json.dumps(manifest,indent=2))
  if r.returncode:raise RuntimeError(label+' failed: '+r.stderr[-1500:])
  data=json.loads(r.stdout);assert data['runtime_assembly_sha256']==ident['runtime_dll_sha256'],'Loaded Runtime DLL differs';assert Path(data['runtime_assembly_path']).resolve()==(Path(ident['dll']).parent/'GameplayTags.dll').resolve(),'Wrong Runtime binding path';(run/(label+'.json')).write_text(json.dumps(data,indent=2));manifest['runs'][-1]['result_sha256']=sha(run/(label+'.json'));manifest['runs'][-1]['stdout_sha256']=sha(run/(label+'.stdout'));(run/'manifest.json').write_text(json.dumps(manifest,indent=2));print(label,round(time.time()-started,2),'s',flush=True);return data
 for layout in args.layouts.split(','):
  cals=[execute(v,layout,'calibrate',f'calibrate-{v}-{layout}') for v in variants]
  counts={row['id']:max(r['iterations'] for cal in cals for r in cal['rows'] if r['id']==row['id']) for row in cals[0]['rows']}
  # Fixed counts shared by variants and all rounds. Both variants must remain below budget.
  for cal in cals:
   for row in cal['rows']:
    if counts[row['id']]*row['allocation_bytes_per_op']>64*1024*1024:counts[row['id']]=min(counts[row['id']],int(64*1024*1024/max(row['allocation_bytes_per_op'],1)))
  countfile=run/f'iterations-{layout}.json';countfile.write_text(json.dumps(counts,indent=2))
  for round_index in range(args.rounds):
   schedule=[(args.baseline,'A'),(args.candidate,'B'),(args.baseline,'AA')] if round_index%2==0 else [(args.baseline,'AA'),(args.candidate,'B'),(args.baseline,'A')]
   for variant,label in schedule:execute(variant,layout,countfile,f'r{round_index}-{layout}-{label}')
 for variant in variants:execute(variant,'Micro','calibrate',f'first-factory-{variant}','first-factory')
 manifest['completed_utc']=time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime());(run/'manifest.json').write_text(json.dumps(manifest,indent=2))
if __name__=='__main__':main()
