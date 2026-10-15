#!/usr/bin/env python3
"""Lossless numerical flattening plus descriptive paired ratios, never acceptance."""
import csv,json,pathlib,statistics,sys
out=pathlib.Path(sys.argv[1]);counts=json.loads((out/'counts.json').read_text());rows=[]
for meta_path in sorted(out.glob('*.launch.json')):
    meta=json.loads(meta_path.read_text());label=meta['label'];raw=out/(label+'.jsonl')
    records=[]
    for line in raw.read_text().splitlines() if raw.exists() else []:
        try:records.append(json.loads(line))
        except json.JSONDecodeError:pass
    val=next((r for r in records if r.get('kind')=='validation'),{})
    for sample in (r for r in records if r.get('kind')=='sample'):
        c=counts.get(sample['key'],{})
        stage=label.split('-',1)[0]
        suffix='-'+meta['version']
        pair=label[:-len(suffix)].rsplit('-',1)[-1] if stage in ('C1','C2') else ''
        aa_label=label.rsplit('-',1)[-1] if stage=='aa' else ''
        if stage=='aa':pair=label.rsplit('-',2)[-2]
        rows.append(dict(sample,stage=stage,pair=pair,version=meta['version'],aa_label=aa_label,label=label,dll_sha256=meta['dll_sha256'],baseline_adequate=c.get('adequate_baseline'),capped=c.get('capped'),short=sample['elapsed_ms']<c.get('target_ms',250),validation=val.get('status','MISSING_OR_FAILED'),retained_count=val.get('retained_count'),retained_capacity=val.get('retained_capacity'),array_payload_bytes=val.get('array_payload_bytes')))
def write(name,data):
    p=out/name
    if p.exists():raise SystemExit('Refusing overwrite '+str(p))
    if not data:return
    fields=list(dict.fromkeys(k for row in data for k in row))
    with p.open('x',newline='') as f:
        w=csv.DictWriter(f,fields);w.writeheader();w.writerows(data)
write('raw-flat.csv',rows)
paired=[]
for r in rows:
    if r['stage'] not in ('aa','C1','C2'):continue
    refs=['A'] if r['stage']=='aa' else ['B1','B11','C1']
    for ref in refs:
        own=r['aa_label'] if r['stage']=='aa' else r['version']
        if own==ref:continue
        base=next((b for b in rows if b['stage']==r['stage'] and b['key']==r['key'] and b['pair']==r['pair'] and (b['aa_label'] if r['stage']=='aa' else b['version'])==ref),None)
        if base:
            paired.append(dict(stage=r['stage'],key=r['key'],pair=r['pair'],version=own,reference=ref,ns=r['ns_per_op'],reference_ns=base['ns_per_op'],ratio=r['ns_per_op']/base['ns_per_op'],bytes=r['bytes_per_op'],reference_bytes=base['bytes_per_op'],both_validated=r['validation']=='PASS' and base['validation']=='PASS',either_short=r['short'] or base['short']))
write('paired.csv',paired)
summary=[]
for group in sorted({(r['stage'],r['key'],r['version'],r['reference']) for r in paired}):
    rs=[r for r in paired if (r['stage'],r['key'],r['version'],r['reference'])==group];ratios=[r['ratio'] for r in rs]
    summary.append(dict(zip(['stage','key','version','reference'],group),samples=len(rs),median_ratio=statistics.median(ratios),min_ratio=min(ratios),max_ratio=max(ratios),faster_pairs=sum(v<1 for v in ratios),all_validated=all(r['both_validated'] for r in rs),any_short=any(r['either_short'] for r in rs)))
write('paired-summary.csv',summary)
print('Wrote numerical flattening and descriptive pair ratios. No acceptance inference.')
