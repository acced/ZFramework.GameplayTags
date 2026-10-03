#!/usr/bin/env python3
from pathlib import Path
import argparse,json,math,statistics,csv
from analyze import sha,require,check_row,percentile
MATCH_FIELDS=['id','group','op','count','case_name','iterations','unit','input_hash','expected_checksum_per_iteration','query_nodes','query_ranges','legacy_storage','direct_storage','direct_reserved_capacity','direct_payload_bytes']
RUNTIME_FIELDS=['layout','group','runtime','architecture','registry_count','vector_accelerated','avx2','gc_latency']
ROOT=Path(__file__).resolve().parent
med=statistics.median

def verdict(ratios,threshold,valid):
 return 'invalid-sample' if not valid else 'clear-improvement' if all(x<1-threshold for x in ratios) else 'clear-regression' if all(x>1+threshold for x in ratios) else 'uncertain-or-small'
def main():
 p=argparse.ArgumentParser();p.add_argument('name');args=p.parse_args();root=ROOT/'results'/args.name;m=json.loads((root/'manifest.json').read_text())
 require(m.get('completed_utc'),'Incomplete factor run');require(sha(Path(m['runner_path']))==m['runner_sha256'],'Runner changed');require(sha(ROOT/'bridge-factor-plan.json')==m['plan_sha256'],'Plan changed')
 planned={(c['group'],c['layout'],c['id']):c['role'] for c in m['plan']['cells']};require(len(planned)==54,'Wrong bridge factor plan')
 for ident in m['identities'].values():
  require(sha(Path(ident['dll']))==ident['dll_sha256'],'Host changed');require(sha(Path(ident['runtime_dll']))==ident['runtime_dll_sha256'],'Runtime changed')
  for file,digest in ident['source_sha256'].items():require(sha(Path(file))==digest,'Source changed')
  for file,digest in ident['dependency_sha256'].items():require(sha(Path(file))==digest,'Dependency changed')
  b=ident['admitted_build_binding'];s=ident['admitted_source_manifest'];require(sha(Path(b['path']))==b['sha256'] and sha(Path(s['path']))==s['sha256'],'Compiler manifest changed');binding=json.loads(Path(b['path']).read_text());sources=json.loads(Path(s['path']).read_text());require(binding['runtime_sha256']==ident['runtime_dll_sha256'],'Compiler DLL mismatch');require(str(Path(binding['source'])/'Runtime')==ident['snapshot'],'Compiler root mismatch');require(all(ident['source_sha256'].get(f)==sources[f] for f in binding['runtime_sources']),'Compiler source mismatch')
 records={};seen=set();runtime={}
 for entry in m['runs']:
  label=entry['label'];require(label not in seen,'Duplicate process');seen.add(label);require(entry['returncode']==0,'Failed process');require(sha(root/(label+'.json'))==entry['result_sha256'] and sha(root/(label+'.stdout'))==entry['stdout_sha256'],'Raw result changed')
  if not label.startswith('r'):continue
  rn,group,layout,arm=label.split('-');r=int(rn[1:]);require(r in range(3) and arm in ['A','AA','B','C'],'Wrong round/arm')
  expected_variant=m['args']['baseline'] if arm in ['A','AA'] else m['args']['v2'] if arm=='B' else m['args']['v3'];require(entry['variant']==expected_variant,'Wrong variant arm')
  ident=m['identities'][expected_variant];d=json.loads((root/(label+'.json')).read_text());require(d['runtime_assembly_sha256']==ident['runtime_dll_sha256'],'Observed Runtime mismatch');require(Path(d['runtime_assembly_path']).resolve()==(Path(ident['dll']).parent/'GameplayTags.dll').resolve(),'Wrong Runtime path');require(d['group']==group and d['layout']==layout,'Wrong group/layout')
  require(d['gc_latency']=='Batch','Wrong GC mode');require(d['caller']==('legacy' if arm in ['A','AA'] else 'direct'),'Wrong public caller')
  if m['args']['backend']=='nohw':require(not d['vector_accelerated'] and not d['avx2'],'Wrong nohw backend')
  else:require(m['env'].get('DOTNET_EnableHWIntrinsic') is None,'Default backend overridden')
  meta={k:d[k] for k in RUNTIME_FIELDS}
  if (group,layout) in runtime:require(runtime[(group,layout)]==meta,'Runtime differs')
  else:runtime[(group,layout)]=meta
  expected={id for g,l,id in planned if g==group and l==layout};require(len(d['rows'])==len(expected),'Wrong row count');seenrows=set();counts=json.loads((root/f'iterations-{group}-{layout}.json').read_text());require(set(counts)==expected,'Wrong iteration plan')
  for row in d['rows']:
   require(row['id'] not in seenrows,'Duplicate row');seenrows.add(row['id']);require(row['iterations']==counts[row['id']],'Fixed iteration mismatch');check_row(row);records.setdefault((group,layout,row['id']),{})[(r,arm)]=row
  require(seenrows==expected,'Wrong row identities')
 require(set(records)==set(planned),'Incomplete factor matrix')
 rows=[]
 for key,values in records.items():
  require(set(values)=={(r,a) for r in range(3) for a in ['A','AA','B','C']},'Incomplete factor arms')
  b_ratios=[];c_ratios=[];cb=[];bcross=[];ccross=[];drifts=[];tail=[];p95=[];cbtails=[];cbp95s=[];valid=True;minimum=math.inf;details=[]
  for r in range(3):
   a,aa,b,c=[values[(r,x)] for x in ['A','AA','B','C']];require(all(a[f]==aa[f]==b[f]==c[f] for f in MATCH_FIELDS),'Unmatched data/output/contracts')
   samples=[[s['ns_per_op'] for s in row['samples']] for row in [a,aa,b,c]];at,aat,bt,ct=map(med,samples);br=bt/math.sqrt(at*aat);cr=ct/math.sqrt(at*aat);cbr=ct/bt;drift=aat/at
   b_ratios.append(br);c_ratios.append(cr);cb.append(cbr);bcross.extend([bt/at,bt/aat]);ccross.extend([ct/at,ct/aat]);drifts.append(abs(drift-1));tail.extend([max(samples[3])/max(xs) for xs in samples[:2]]);p95.extend([percentile(samples[3],.95)/percentile(xs,.95) for xs in samples[:2]])
   cbtail=max(samples[3])/max(samples[2]);cbp95=percentile(samples[3],.95)/percentile(samples[2],.95);cbtails.append(cbtail);cbp95s.append(cbp95)
   for row in [a,aa,b,c]:valid &= check_row(row);minimum=min(minimum,min(s['elapsed_ms'] for s in row['samples']))
   details.append(dict(round=r,baseline_ns=at,aa_ns=aat,v2_ns=bt,v3_ns=ct,v2_ratio=br,v3_ratio=cr,v3_over_v2=cbr,v3_over_v2_p95=cbp95,v3_over_v2_max=cbtail,v2_over_a=bt/at,v2_over_aa=bt/aat,v3_over_a=ct/at,v3_over_aa=ct/aat,aa_ratio=drift))
  threshold=max(.03,max(drifts));group,layout,id=key
  rows.append(dict(group=group,layout=layout,id=id,role=planned[key],valid=valid,minimum_sample_ms=minimum,v2_ratio_median=med(b_ratios),v3_ratio_median=med(c_ratios),v3_over_v2_median=med(cb),v3_crossing_min=min(ccross),v3_crossing_max=max(ccross),aa_drift_max=max(drifts),v3_p95_tail_ratio_max=max(p95),v3_max_tail_ratio_max=max(tail),v3_v2_p95_tail_ratio_max=max(cbp95s),v3_v2_max_tail_ratio_max=max(cbtails),v2_verdict=verdict(bcross,threshold,valid),v3_verdict=verdict(ccross,threshold,valid),versus_v2_verdict=verdict(cb,threshold,valid),baseline_bytes=med(s['bytes_per_op'] for s in values[(0,'A')]['samples']),v2_bytes=med(s['bytes_per_op'] for s in values[(0,'B')]['samples']),v3_bytes=med(s['bytes_per_op'] for s in values[(0,'C')]['samples']),rounds=details))
 summary=dict(name=args.name,backend=m['args']['backend'],scope='54-cell fixed-actor/hot-query bridge factor; baseline A/A is legacy public API, v2 is old-v3 Direct bridge, v3 is split Direct bridge. Representation differences remain explicit.',rows=rows,counts={field:{v:sum(r[field]==v for r in rows) for v in ['clear-improvement','clear-regression','uncertain-or-small','invalid-sample']} for field in ['v2_verdict','v3_verdict','versus_v2_verdict']})
 (root/'summary.json').write_text(json.dumps(summary,indent=2))
 with (root/'summary.csv').open('w') as f:
  fields=[k for k in rows[0] if k!='rounds'];w=csv.DictWriter(f,fieldnames=fields);w.writeheader();w.writerows({k:r[k] for k in fields} for r in rows)
 print(json.dumps({'name':args.name,'counts':summary['counts'],'min_ms':min(r['minimum_sample_ms'] for r in rows)},indent=2))
if __name__=='__main__':main()
