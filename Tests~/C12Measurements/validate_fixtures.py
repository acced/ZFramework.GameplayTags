#!/usr/bin/env python3
"""Untimed calls=1, fresh-process fixture validation; not the semantic suite."""
import json,os,pathlib,subprocess,sys,time
manifest_path,out=map(pathlib.Path,sys.argv[1:3]);manifest=json.loads(manifest_path.read_text());here=pathlib.Path(__file__).resolve().parent;cells=json.loads((here/'cells.json').read_text());out.mkdir(parents=True,exist_ok=False)
env={k:v for k,v in os.environ.items() if not k.startswith(('DOTNET_','COMPlus_','CORECLR_','COR_'))};env.update(DOTNET_TieredCompilation='0',COMPlus_TieredCompilation='0')
count=0
for version,entry in manifest['versions'].items():
 for c in cells:
  label=version+'-'+c['key'];cmd=[manifest['dotnet'],entry['dll'],'validate',c['key'],c['op'],str(c['n']),str(c['target']),str(c['capacity']),c['shape'],c['context'],'1'];start=time.monotonic();code=None
  try:
   with (out/(label+'.jsonl')).open('x') as stdout,(out/(label+'.stderr')).open('x') as stderr:code=subprocess.run(cmd,stdout=stdout,stderr=stderr,env=env,timeout=30).returncode
  finally:
   (out/(label+'.terminal.json')).write_text(json.dumps(dict(version=version,key=c['key'],returncode=code,seconds=time.monotonic()-start))+'\n')
  if code:raise SystemExit('FAIL '+label)
  rows=[json.loads(l) for l in (out/(label+'.jsonl')).read_text().splitlines()];assert any(r.get('kind')=='validation' and r.get('status')=='PASS' for r in rows);count+=1
(out/'summary.json').write_text(json.dumps(dict(status='PASS',processes=count,cells=len(cells),calls=1,timing=False),indent=2)+'\n')
print('PASS',count,'untimed fixture processes')
