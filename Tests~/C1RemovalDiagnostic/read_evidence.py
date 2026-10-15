#!/usr/bin/env python3
"""Verify public raw shards and recreate every CSV with the frozen Python summarizer. No .NET."""
import argparse,collections,csv,hashlib,json,math,pathlib,subprocess,sys,tempfile
here=pathlib.Path(__file__).resolve().parent
p=argparse.ArgumentParser();p.add_argument('--label',help='Print one byte-identical original stdout record');a=p.parse_args()
e=here/'Evidence';sha=lambda b:hashlib.sha256(b).hexdigest();load=lambda p:json.loads(p.read_text())
index=[json.loads(s) for s in (e/'call-index.jsonl').read_text().splitlines()]
terminal=(e/'terminal-records.jsonseq').read_bytes();stderr=(e/'stderr-records.bin').read_bytes();segments=collections.defaultdict(list);seen=set();samples=[];fixtures=0
identity=load(e/'IDENTITY.json');counts=load(e/'counts.json')
assert sha((e/'counts.json').read_bytes())==(e/'counts.sha256').read_text().strip()
for name,expected in identity['measured_harness'].items():assert sha((e/'TimedHarness'/name).read_bytes())==expected,name
for line in (e/'TimedHarness/measurement-inputs.sha256').read_text().splitlines():
 expected,name=line.split('  ',1);assert sha((e/'TimedHarness'/name).read_bytes())==expected,name
with tempfile.TemporaryDirectory(prefix='c1-public-verify-') as temp:
 t=pathlib.Path(temp);(t/'counts.json').write_bytes((e/'counts.json').read_bytes())
 for row in index:
  label=row['label'];assert label not in seen;seen.add(label)
  data=(e/row['raw_file']).read_bytes();start=row['raw_offset'];raw=data[start:start+row['raw_length']]
  assert len(raw)==row['raw_length'] and sha(raw)==row['raw_sha256'];segments[row['raw_file']].append((start,start+len(raw)))
  start=row['terminal_offset'];tb=terminal[start:start+row['terminal_length']];assert len(tb)==row['terminal_length'] and sha(tb)==row['terminal_sha256'];segments['terminal-records.jsonseq'].append((start,start+len(tb)));tr=json.loads(tb)
  start=row['stderr_offset'];eb=stderr[start:start+row['stderr_length']];assert len(eb)==row['stderr_length'] and sha(eb)==row['stderr_sha256'];segments['stderr-records.bin'].append((start,start+len(eb)))
  assert eb.decode()==row['stderr_contents']==''
  assert tr['status']==row['status']=='exited' and tr['returncode']==row['returncode']==0 and tr['elapsed_seconds']==row['elapsed_seconds']
  records=[json.loads(s) for s in raw.splitlines()];val=next(r for r in records if r.get('kind')=='validation');assert val['status']=='PASS'
  fixtures+=row['mode']=='validate'
  for sample in (r for r in records if r.get('kind')=='sample'):
   assert sample['calls']==row['calls'] and sample['short_sample']==(sample['elapsed_ms']<250)
   assert math.isclose(sample['elapsed_ms'],sample['ticks']*1000/sample['frequency'],rel_tol=1e-14)
   assert math.isclose(sample['ns_per_op'],sample['ticks']*1e9/sample['frequency']/sample['calls'],rel_tol=1e-14)
   assert math.isclose(sample['bytes_per_op'],sample['bytes']/sample['calls'],rel_tol=1e-14)
   if label.startswith('compare-'):assert sample['calls']==counts[sample['key']]['calls']
   samples.append((row,sample))
  (t/(label+'.launch.json')).write_text(json.dumps(row));(t/(label+'.jsonl')).write_bytes(raw);(t/(label+'.terminal.json')).write_bytes(tb)
  if a.label==label:sys.stdout.buffer.write(raw)
 for name,ranges in segments.items():
  ranges.sort();assert ranges[0][0]==0 and all(x[1]==y[0] for x,y in zip(ranges,ranges[1:])) and ranges[-1][1]==(e/name).stat().st_size,name
 subprocess.run([sys.executable,str(e/'TimedHarness/summarize.py'),str(t)],check=True,stdout=subprocess.PIPE)
 csvs={p.name:p.read_bytes() for p in t.glob('*.csv')};assert csvs=={p.name:p.read_bytes() for p in e.glob('*.csv')},'Descriptive CSV mismatch'
formal=[s for r,s in samples if r['label'].startswith('compare-')];pilots=[s for r,s in samples if r['label'].startswith('pilot-')]
assert len(index)==145 and len(samples)==121 and fixtures==24 and len(formal)==96 and len(pilots)==25
assert sum(s['short_sample'] for s in formal)==8 and sum(s['short_sample'] for s in pilots)==14
assert all(s['classification']=='preprepared-single-copy-diagnostic' for s in formal if s['short_sample'])
if a.label and a.label not in seen:raise SystemExit('Unknown label')
if not a.label:print(json.dumps({'status':'PASS','invocations':len(index),'numeric_samples':len(samples),'fixtures':fixtures,'pilots':len(pilots),'formal':len(formal),'exact_raw_terminal_stderr_bytes_verified':True,'frozen_harness_hashes_verified':True,'all_numerical_CSVs_recreated_byte_identically':True,'target_execution':False}))
