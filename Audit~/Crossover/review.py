#!/usr/bin/env python3
"""Reconstruct raw samples; forced-mode frontiers are diagnostics, never Auto results."""
import pathlib,json,statistics,math,csv,re,collections,sys,hashlib

def review(root):
 root=pathlib.Path(root);manifest=json.loads((root/'manifest.json').read_text());groups=collections.defaultdict(list);retained=[];digests={};rawcount=samples=zero=0
 for p in sorted(root.glob('*-r*.json')):
  if not re.fullmatch(r'.+-(query|sets|ratio|inline)-r\d+\.json',p.name):continue
  data=json.loads(p.read_text());seen=set()
  for row in data['rows']:
   if row['stage']=='retained_inline':retained.append(row);continue
   key=tuple(row[k] for k in ('variant','stage','op','universe','members','distribution','seed','relation'))
   assert key not in seen,p.name;seen.add(key)
   assert row['variant']==data['variant'] and row['round']==data['round']
   assert len(row['ns'])==len(row['allocated'])==7
   assert all(math.isfinite(x) and x>=0 for x in row['ns']+row['allocated'])
   canonical=key[1:];d=row['digest'];assert canonical not in digests or digests[canonical]==d,canonical;digests[canonical]=d
   if row['stage']=='query' or 'reuse' in row['op'] or row['op']=='union_into' or row['op'].endswith('_prepared'):
    assert all(x==0 for x in row['allocated']),key;zero+=7
   groups[key].append(row);rawcount+=1;samples+=7
 values=[];index={}
 for key,rows in sorted(groups.items()):
  rows.sort(key=lambda r:r['round']);assert [r['round'] for r in rows]==list(range(manifest['rounds'])),key
  med=[statistics.median(r['ns']) for r in rows]
  item=dict(zip(('variant','stage','operation','universe','members','distribution','seed','relation'),key))
  item.update(median_ns=statistics.median(med),round_medians=med,bytes_per_unit=statistics.median(x for r in rows for x in r['allocated']),
      min_sample_ms=min(x for r in rows for x in r['ms']),max_sample_ms=max(x for r in rows for x in r['ms']),
      generation_collections=[sum(g[k] for r in rows for g in r['gc']) for k in range(3)],details=rows[0]['details'])
  index[key]=item;values.append(item)
 def csvwrite(path,items):
  if not items:return
  with path.open('w',newline='',encoding='utf-8-sig') as f:
   w=csv.DictWriter(f,fieldnames=list(items[0]));w.writeheader();w.writerows(items)
 csvwrite(root/'All_Recomputed.csv',values)
 aa=[]
 for key,a in index.items():
  if key[:2]!=('binary','query'):continue
  b=index[('binaryAA',*key[1:])];ratios=[x/y for x,y in zip(a['round_medians'],b['round_medians'])]
  aa.append(dict(operation=a['operation'],members=a['members'],seed=a['seed'],ratios=ratios,within5=all(.95<=x<=1.05 for x in ratios),persistent5=all(x>1.05 for x in ratios)or all(x<.95 for x in ratios)))
 csvwrite(root/'AA_Query.csv',aa)
 frontiers=[]
 for key,a in index.items():
  if key[:2]!=('array','sets'):continue
  choices={name:index[(name,*key[1:])] for name in ('array','dense','micro')}
  best=min(choices,key=lambda n:choices[n]['median_ns'])
  frontiers.append(dict(operation=a['operation'],universe=a['universe'],members=a['members'],distribution=a['distribution'],seed=a['seed'],relation=a['relation'],
       array_ns=choices['array']['median_ns'],dense_ns=choices['dense']['median_ns'],micro_ns=choices['micro']['median_ns'],
       array_bytes=choices['array']['bytes_per_unit'],dense_bytes=choices['dense']['bytes_per_unit'],micro_bytes=choices['micro']['bytes_per_unit'],
       diagnostic_fastest=best,warning='NOT an executed adaptive selector'))
 csvwrite(root/'Forced_Mode_Frontier.csv',frontiers)
 csvwrite(root/'Retained_Inline.csv',retained)
 codegen={}
 for p in root.glob('*-codegen.log'):
  t=p.read_text();heads=[l for l in t.splitlines() if 'Assembly listing' in l or 'Total bytes of code' in l]
  instructions=[l for l in t.splitlines() if re.search(r'\b(vpcmpeqd|vptest|vpshufb|vpsadbw|popcnt|cnt|uaddlp|addv|cmeq|umaxv)\b',l.lower())]
  codegen[p.name]={'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'method_headers':heads,'instruction_examples':instructions[:30]}
 for name in ('original','binary','linear','vector','bounded','micro'):
  assert '"failed":0' in (root/(name+'-tests.log')).read_text(),name
 for name in ('binary','linear','vector','bounded'):
  assert 'failures=0' in (root/(name+'-full.log')).read_text(),name
 result={'rows':len(values),'raw_rows':rawcount,'timing_samples':samples,'zero_allocation_samples':zero,'machine':manifest['machine'],
    'query_AA_rows':len(aa),'query_AA_within5':sum(x['within5'] for x in aa),'query_AA_persistent5':sum(x['persistent5'] for x in aa),'codegen':codegen,
    'forced_frontiers':len(frontiers),'native_unity':'not_run','deterministic_adaptive_policy':'not_implemented_or_approved',
    'new_protocol':'1ms calibration, 16MiB allocation cap, not directly comparable to historical fixed-iteration timings'}
 (root/'Recheck.json').write_text(json.dumps(result,indent=2));(root/'Recomputed.json').write_text(json.dumps(values,indent=2))
 print(json.dumps(result,indent=2));return result
if __name__=='__main__':review(sys.argv[1])
