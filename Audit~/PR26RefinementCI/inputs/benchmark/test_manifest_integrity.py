#!/usr/bin/env python3
"""Exercise reporter rejection paths on a disposable copy of a completed run."""
import json,shutil,tempfile,sys,hashlib
from pathlib import Path
from analyze import analyze
source=Path('results')/sys.argv[1]
with tempfile.TemporaryDirectory(prefix='pr26-integrity-') as d:
 root=Path(d)/'run';shutil.copytree(source,root)
 manifest=json.loads((root/'manifest.json').read_text());sample_label=next(r['label'] for r in manifest['runs'] if r['label'].startswith('r'))
 sample_path=root/(sample_label+'.json');original=sample_path.read_bytes()
 def save(m): (root/'manifest.json').write_text(json.dumps(m))
 def rejected(name,edit_manifest=None,edit_data=None):
  m=json.loads(json.dumps(manifest));sample_path.write_bytes(original)
  if edit_manifest:edit_manifest(m)
  if edit_data:
   data=json.loads(original);edit_data(data);sample_path.write_text(json.dumps(data))
   next(r for r in m['runs'] if r['label']==sample_label)['result_sha256']=hashlib.sha256(sample_path.read_bytes()).hexdigest()
  save(m)
  try:analyze(root)
  except (ValueError,KeyError):print('PASS',name);return
  raise AssertionError('accepted '+name)
 analyze(root);print('PASS intact manifest')
 rejected('missing arm',lambda m:m['runs'].pop(next(i for i,r in enumerate(m['runs']) if r['label']==sample_label)))
 rejected('duplicate run',lambda m:m['runs'].append(m['runs'][0]))
 rejected('failed process',lambda m:m['runs'][0].update(returncode=1))
 rejected('wrong DLL binding',lambda m:m['runs'][0].update(dll_sha256='0'*64))
 rejected('wrong harness hash',lambda m:m.update(harness_sha256='0'*64))
 rejected('duplicate row',edit_data=lambda data:data['rows'].__setitem__(-1,data['rows'][0]))
 rejected('wrong runtime',edit_data=lambda data:data.update(runtime='unmatched'))
 rejected('wrong fixed iterations',edit_data=lambda data:data['rows'][0].update(iterations=1))
 rejected('missing sample',edit_data=lambda data:data['rows'][0]['samples'].pop())
 rejected('wrong source membership metadata',edit_data=lambda data:data['rows'][0].update(input_hash='unmatched'))
