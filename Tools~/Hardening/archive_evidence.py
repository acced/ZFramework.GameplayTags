"""Import an existing qualification artifact; never execute tests or alter Runtime."""
import csv, hashlib, json, pathlib, sys
root = pathlib.Path(__file__).resolve().parents[2]
source = pathlib.Path(sys.argv[1]).resolve()
out = root / 'Tests~/Hardening/Evidence/qualification'
expected = {
 'raw.csv':'f15e8a3a76f27ffcb9fc40163f349c6db8c963fe79718a96b625fd80d877b86a',
 'pilots.csv':'ca8430af81de7f424dcaa56208f8a199b4bda14f32bf00b9ee12602c6b68f9a1',
 'comparison.csv':'776cb8596a0332ee20550c59b6272379cf00cab3ddcb6941f0f356c7a1c227a8',
 'identity.json':'9b426189e7b722266973c2133dec203db1aff81a361e30021c3721fbac18916f'}
sha=lambda b:hashlib.sha256(b).hexdigest()
if out.exists():
 for n,h in expected.items():
  if sha((out/n).read_bytes())!=h:raise SystemExit('Archived evidence changed: '+n)
 raise SystemExit(0)
if (source/'tested-commit.txt').read_text().strip()!='9ef7d84ee66013c9ab87c2aea8608e6f56a44be8':raise SystemExit('Wrong tested commit')
contents={n:(source/'performance'/n).read_bytes() for n in expected}
for n,h in expected.items():
 if sha(contents[n])!=h:raise SystemExit('Artifact identity mismatch: '+n)
identity=json.loads(contents['identity.json'])
for version,folder in [('hardened',root/'Runtime'),('uploaded',root/'Tests~/Hardening/Baseline/Runtime')]:
 actual={p.name:sha(p.read_bytes()) for p in folder.glob('*.cs')}
 if actual!=identity['source_sha256'][version]:raise SystemExit('Runtime source identity mismatch: '+version)
rows=list(csv.DictReader(contents['raw.csv'].decode().splitlines()))
pilots=list(csv.DictReader(contents['pilots.csv'].decode().splitlines()))
if len(rows)!=224 or len(pilots)!=303 or any(r['status']!='PASS' for r in rows+pilots):raise SystemExit('Invalid numerical record set')
out.mkdir(parents=True)
for n,b in contents.items():(out/n).write_bytes(b)
summary={}
for p in sorted((source/'validation').glob('*.log')):
 lines=[]
 for line in p.read_text().splitlines():
  if 'warning RS2008' in line:lines.append('warning RS2008: historical source-generator release-tracking warning')
  elif line.startswith(('PASS','FAIL','RESULT','Registry checks','Source generator checks')) or 'assertions;' in line:lines.append(line)
 if lines:summary[p.name]=lines
(out/'validation-summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n')
(out/'import-receipt.json').write_text(json.dumps({'qualification_run':38720019332,'artifact_id':11964578187,'tested_commit':identity['source_commit'],'original_artifact_sha256':'fc30da277230ed990754d7e79a6ae34ee334d9e4fc7d524b23ae862c71cc135c','unchanged_numerical_files':expected,'raw_formal_rows':len(rows),'pilot_rows':len(pilots),'short_formal_rows':sum(r['short']=='True' for r in rows),'runtime_identity_verified':True,'new_measurement_performed':False},indent=2)+'\n')
print('PASS archived byte-identical qualification data and verified both Runtime inputs')
