#!/usr/bin/env python3
from pathlib import Path
import argparse,csv,json,statistics,math,hashlib
ROOT=Path(__file__).resolve().parent
med=statistics.median
BUDGET=64*1024*1024
EXPECTED={'query':37,'builder':30,'mixed':75,'popcount':16,'growth':24,'diag-query':4,'diag-builder':1}
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def require(test,msg):
 if not test:raise ValueError(msg)
def percentile(values,p):
 v=sorted(values);x=(len(v)-1)*p;lo=int(x);hi=min(lo+1,len(v)-1);return v[lo]+(v[hi]-v[lo])*(x-lo)
def expected_keys(group):
 out=[]
 def add(shape,op,n,flavor,peer='same',pn=0):out.append(f'{group}/{shape}/{n}/{op}/{flavor}/{peer}/{pn}')
 if group=='diag-query':
  for f in ['hit-first','hit-middle','hit-last']:add('scatter','query64',2,f)
  add('scatter','query64',8,'balanced')
 if group=='diag-builder':add('cluster','build',4096,'sorted')
 if group=='query':
  for n in [0,1,2,8,32,64]:
   for f in (['miss-low','miss-high'] if n==0 else ['hit-first','hit-middle','hit-last','miss-bit','miss-low','miss-high','balanced']):add('scatter','query64',n,f)
 if group=='builder':
  for n in [0,1,2]:
   for f in ['sorted','shuffled']:add('scatter','build',n,f)
  for n in [8,64,4096]:
   for shape in ['cluster','scatter']:
    for f in ['sorted','shuffled','reversed','duplicates']:add(shape,'build',n,f)
 if group=='growth':
  for n in [8,4096]:
   for shape in ['cluster','scatter']:
    for peer in ['micro','dense']:
     for op in ['fresh-micro-union','tight-micro-union','upper-micro-union']:add(shape,op,n,'equal',peer,n)
 if group=='popcount':
  for n in [8,4096]:
   for shape in ['cluster','scatter']:
    for peer in ['same','opposite']:
     for op in ['union','restore-remove']:add(shape,op,n,'equal',peer,n)
 if group=='mixed':
  for n in [8,4096]:
   for shape in ['cluster','scatter']:
    for peer in ['same','opposite']:
     for op in ['union','restore-append','restore-remove','copy']:add(shape,op,n,'equal',peer,n)
  for shape in ['cluster','scatter']:
   for peer in ['same','opposite']:
    for op in ['union','restore-append','restore-remove']:
     for rev in [False,True]:add(shape,op,8 if rev else 4096,'small-large' if rev else 'large-small',peer,4096 if rev else 8)
  for peer in ['same','opposite']:
   for op in ['union','restore-append','restore-remove','copy']:add('random',op,4096,'equal',peer,4096)
  add('random','charged-build-union64',4096,'shuffled','same',4096)
  for f in ['clean','dirty']:
   for n in [0,8,4096]:add('scatter','empty-copy',n,f)
  for n in [8,4096]:
   for shape in ['cluster','scatter']:add(shape,'charged-build-union64',n,'shuffled','same',n)
 return set(out)
def check_row(row):
 samples=row['samples'];require(len(samples)==7,'Not seven samples');require([s['sample'] for s in samples]==list(range(7)),'Sample duplication/order')
 require(row['iterations']>0,'Iteration count invalid')
 valid=True
 for s in samples:
  for k in ['elapsed_ms','ns_per_op']:require(isinstance(s[k],(float,int)) and math.isfinite(s[k]) and s[k]>0,'Nonfinite/nonpositive '+k)
  for k in ['bytes_per_op','total_bytes']:require(isinstance(s[k],(float,int)) and math.isfinite(s[k]) and s[k]>=0,'Invalid allocation')
  require(math.isclose(s['ns_per_op'],s['elapsed_ms']*1e6/row['iterations'],rel_tol=1e-10),'Elapsed/iteration disagreement')
  require(math.isclose(s['bytes_per_op'],s['total_bytes']/row['iterations'],rel_tol=1e-10),'Allocation/iteration disagreement')
  require(len(s['gcs'])==3 and all(isinstance(v,int) and v>=0 for v in s['gcs']),'Invalid collection count')
  if row['op'] not in ['build','charged-build-union64','fresh-micro-union']:require(s['total_bytes']==0,'Prepared allocation violation')
  actual=s['elapsed_ms']>=1 and s['total_bytes']<=BUDGET;valid &= actual
  require(s['valid']==(s['elapsed_ms']>=1),'Sample validity flag disagrees')
 require(row['valid']==all(s['elapsed_ms']>=1 for s in samples),'Row validity flag disagrees')
 return valid
MATCH_FIELDS=['id','group','shape','count','peer_count','flavor','op','peer','iterations','unit','input_hash','peer_hash','input_length','peer_input_length','expected_checksum_per_iteration','output_count','left_layout','right_layout','output_layout','input_payload_bytes','peer_payload_bytes','reserved_capacity','output_payload_bytes']
RUNTIME_FIELDS=['layout','group','runtime','architecture','vector_accelerated','vector_uint_lanes','avx2','tiered_compilation','hw_intrinsic','gc_latency','gc_server','ready_to_run','tiered_pgo','gc_concurrent','registry_count']
def analyze(root):
 manifest=json.loads((root/'manifest.json').read_text());args=manifest['args'];layouts=args['layouts'].split(',');records={};runtimes={};seen=set();rows_expected={};iterations_expected={}
 require(args['rounds']>=3,'Fewer than three rounds')
 require(manifest.get('completed_utc'),'Incomplete run manifest')
 require(sha(Path(manifest.get('harness_path',str(ROOT/'Probe.cs'))))==manifest['harness_sha256'],'Harness changed since run');require(sha(Path(manifest.get('runner_path',str(ROOT/'run.py'))))==manifest['runner_sha256'],'Runner changed since run')
 for variant,ident in manifest['identities'].items():
  require(sha(Path(ident['dll']))==ident['dll_sha256'],'DLL changed')
  if ident.get('cross_assembly_calls'):
   require(sha(Path(ident['runtime_dll']))==ident['runtime_dll_sha256'],'Runtime DLL changed')
   if 'admitted_build_binding' in ident:
    admitted=ident['admitted_build_binding'];inventory=ident['admitted_source_manifest'];require(sha(Path(admitted['path']))==admitted['sha256'],'Compiler binding changed');require(sha(Path(inventory['path']))==inventory['sha256'],'Compiler source manifest changed');binding=json.loads(Path(admitted['path']).read_text());compiled=json.loads(Path(inventory['path']).read_text());require(binding['runtime_sha256']==ident['runtime_dll_sha256'],'Compiler DLL identity mismatch');require(str(Path(binding['source'])/'Runtime')==ident['snapshot'],'Compiler source root mismatch');require(all(ident['source_sha256'].get(f)==compiled[f] for f in binding['runtime_sources']),'Compiler source hashes mismatch')
   for file,digest in ident['dependency_sha256'].items():require(sha(Path(file))==digest,'Bound dependency changed')
  for file,digest in ident['source_sha256'].items():require(sha(Path(file))==digest,'Compiled source changed: '+file)
 for layout in layouts:
  counts=json.loads((root/f'iterations-{layout}.json').read_text());require(len(counts)==EXPECTED[args['group']],'Wrong planned row count');require(set(counts)==expected_keys(args['group']),'Workload plan changed');rows_expected[layout]=set(counts);iterations_expected[layout]=counts
 for meta in manifest['runs']:
  label=meta['label'];require(label not in seen,'Duplicate run label');seen.add(label);require(meta['returncode']==0,'Failed process')
  require(meta['dll_sha256']==manifest['identities'][meta['variant']]['dll_sha256'],'DLL binding mismatch')
  require(sha(root/(label+'.json'))==meta['result_sha256'],'Raw JSON changed');require(sha(root/(label+'.stdout'))==meta['stdout_sha256'],'Raw stdout changed')
  if not label.startswith('r'):continue
  r,layout,arm=label.split('-');roundnum=int(r[1:]);require(layout in layouts and 0<=roundnum<args['rounds'] and arm in ['A','AA','B'],'Unexpected run arm');require(meta['variant']==(args['candidate'] if arm=='B' else args.get('baseline','baseline')),'Wrong variant arm')
  data=json.loads((root/(label+'.json')).read_text());require(data['layout']==layout and data['group']==args['group'],'Process layout/group mismatch');require(len(data['rows'])==EXPECTED[args['group']],'Missing/extra rows')
  ident=manifest['identities'][meta['variant']]
  if ident.get('cross_assembly_calls'):
   require(data['runtime_assembly_sha256']==ident['runtime_dll_sha256'],'Observed Runtime binding mismatch')
   require(Path(data['runtime_assembly_path']).resolve()==(Path(ident['dll']).parent/'GameplayTags.dll').resolve(),'Observed Runtime path mismatch')
  seenrows=set()
  for row in data['rows']:
   require(row['id'] not in seenrows,'Duplicate row');seenrows.add(row['id']);require(row['iterations']==iterations_expected[layout][row['id']],'Iterations differ from fixed plan');check_row(row)
   records.setdefault((layout,row['id']),{})[(roundnum,arm)]=row
  require(seenrows==rows_expected[layout],'Wrong row identities')
  if args['backend']=='nohw':require(not data['vector_accelerated'] and not data['avx2'] and data['hw_intrinsic']=='0','NoHW backend violated')
  else:require(data['hw_intrinsic'] is None,'Default backend overridden')
  require(data['tiered_compilation']=='0' and data['ready_to_run']=='0' and data['tiered_pgo']=='0' and data['gc_concurrent']=='0','Runtime controls differ')
  require(data['gc_latency']=='Batch','Concurrent GC observed')
  runtime={k:data[k] for k in RUNTIME_FIELDS}
  if layout in runtimes:require(runtimes[layout]==runtime,'Runtime/backend mismatch')
  else:runtimes[layout]=runtime
 expectedarms={(r,a) for r in range(args['rounds']) for a in ['A','AA','B']};rows=[]
 for (layout,id),group in records.items():
  require(set(group)==expectedarms,'Incomplete rounds/arms')
  ratios=[];crossings=[];drifts=[];tails=[];p95s=[];valid=True;alloca=[];allocb=[];sample_mins=[];per_round=[]
  for r in range(args['rounds']):
   aa,a,b=group[(r,'AA')],group[(r,'A')],group[(r,'B')]
   require(all(a[k]==b[k]==aa[k] for k in MATCH_FIELDS),'Input/output/contract mismatch '+id)
   ats=[s['ns_per_op'] for s in a['samples']];aats=[s['ns_per_op'] for s in aa['samples']];bts=[s['ns_per_op'] for s in b['samples']]
   at,aat,bt=med(ats),med(aats),med(bts);ratio=bt/math.sqrt(at*aat);drift=aat/at
   ba=bt/at;baa=bt/aat;p95a=percentile(bts,.95)/percentile(ats,.95);p95aa=percentile(bts,.95)/percentile(aats,.95);maxa=max(bts)/max(ats);maxaa=max(bts)/max(aats)
   ratios.append(ratio);crossings.extend([ba,baa]);drifts.append(drift);tails.extend([maxa,maxaa]);p95s.extend([p95a,p95aa]);per_round.append(dict(round=r,baseline_ns=at,aa_ns=aat,candidate_ns=bt,ratio=ratio,candidate_over_a=ba,candidate_over_aa=baa,aa_ratio=drift,p95_over_a=p95a,p95_over_aa=p95aa,max_over_a=maxa,max_over_aa=maxaa))
   for x in [a,aa,b]:valid &= check_row(x);sample_mins.extend(s['elapsed_ms'] for s in x['samples'])
   alloca.extend(s['bytes_per_op'] for x in [a,aa] for s in x['samples']);allocb.extend(s['bytes_per_op'] for s in b['samples'])
  drift=max(abs(x-1) for x in drifts);threshold=max(.03,drift)
  verdict='invalid-sample' if not valid else 'clear-improvement' if all(x<1-threshold for x in crossings) else 'clear-regression' if all(x>1+threshold for x in crossings) else 'uncertain-or-small'
  rows.append(dict(layout=layout,id=id,valid=valid,minimum_sample_ms=min(sample_mins),candidate_ratio_median=med(ratios),paired_ratio_min=min(ratios),paired_ratio_max=max(ratios),crossing_min=min(crossings),crossing_max=max(crossings),aa_drift_max=drift,p95_tail_ratio_max=max(p95s),max_tail_ratio_max=max(tails),baseline_bytes=med(alloca),candidate_bytes=med(allocb),capacity_equal=True,payload_equal=True,verdict=verdict,rounds=per_round))
 require(len(rows)==EXPECTED[args['group']]*len(layouts),'Incomplete overall matrix')
 return dict(name=root.name,backend=args['backend'],group=args['group'],candidate=args['candidate'],rows=rows,counts={v:sum(r['verdict']==v for r in rows) for v in ['clear-improvement','clear-regression','uncertain-or-small','invalid-sample']},limitations=['Single host .NET8 experiment; does not establish Unity/IL2CPP/Burst performance','No significance claim; three paired process rounds with baseline A/A','Payload excludes object and array headers; allocations include them','All B/A and B/AA crossings and p95/max batch tails retained; geometric bracketing is descriptive only'])
def main():
 p=argparse.ArgumentParser();p.add_argument('name');args=p.parse_args();root=ROOT/'results'/args.name;summary=analyze(root);rows=summary['rows']
 (root/'summary.json').write_text(json.dumps(summary,indent=2))
 with (root/'summary.csv').open('w') as f:
  fields=[k for k in rows[0] if k!='rounds'];w=csv.DictWriter(f,fieldnames=fields);w.writeheader();w.writerows({k:r[k] for k in fields} for r in rows)
 print(json.dumps({'name':args.name,'counts':summary['counts'],'min_ms':min(r['minimum_sample_ms'] for r in rows),'allocation_reductions':sum(r['candidate_bytes']<r['baseline_bytes'] for r in rows)},indent=2))
if __name__=='__main__':main()
