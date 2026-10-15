#!/usr/bin/env python3
"""C12-derived lossless numerical flattening and four-pair descriptive summaries."""
import csv,json,pathlib,statistics,sys
out=pathlib.Path(sys.argv[1]);counts=json.loads((out/'counts.json').read_text()) if (out/'counts.json').exists() else {};rows=[]
for meta_path in sorted(out.glob('*.launch.json')):
    meta=json.loads(meta_path.read_text());label=meta['label'];raw=out/(label+'.jsonl');records=[]
    for line in raw.read_text().splitlines() if raw.exists() else []:
        try:records.append(json.loads(line))
        except json.JSONDecodeError:pass
    val=next((r for r in records if r.get('kind')=='validation'),{})
    terminal_path=out/(label+'.terminal.json');terminal=json.loads(terminal_path.read_text()) if terminal_path.exists() else {}
    for sample in (r for r in records if r.get('kind')=='sample'):
        c=counts.get(sample['key'],{});stage=label.split('-',1)[0]
        pair=label[:-len('-'+meta['version'])].rsplit('-',1)[-1] if stage=='compare' else ''
        extras={k:v for k,v in val.items() if k not in ('kind','key','status')}
        rows.append(dict(sample,**extras,stage=stage,pair=pair,version=meta['version'],label=label,dll_sha256=meta['dll_sha256'],baseline_adequate=c.get('adequate_baseline'),capped=c.get('capped'),short=sample['elapsed_ms']<250,validation=val.get('status','MISSING_OR_FAILED'),terminal_status=terminal.get('status','MISSING'),returncode=terminal.get('returncode')))
def write(name,data):
    p=out/name
    if p.exists():raise SystemExit('Refusing overwrite '+str(p))
    if not data:return
    fields=list(dict.fromkeys(k for row in data for k in row))
    with p.open('x',newline='') as f:
        w=csv.DictWriter(f,fields);w.writeheader();w.writerows(data)
write('raw-flat.csv',rows)
write('allocation-anomalies.csv',[r for r in rows if r['bytes_per_op']!=int(r['bytes_per_op'])])
paired=[]
for r in rows:
    if r['stage']!='compare' or r['version']!='C3a':continue
    base=next((b for b in rows if b['stage']=='compare' and b['key']==r['key'] and b['pair']==r['pair'] and b['version']=='current792'),None)
    if base:
        paired.append(dict(key=r['key'],pair=r['pair'],version='C3a',reference='current792',ns=r['ns_per_op'],reference_ns=base['ns_per_op'],ratio=r['ns_per_op']/base['ns_per_op'],elapsed_ms=r['elapsed_ms'],reference_elapsed_ms=base['elapsed_ms'],bytes_per_op=r['bytes_per_op'],reference_bytes_per_op=base['bytes_per_op'],both_validated=r['validation']=='PASS' and base['validation']=='PASS',either_short=r['short'] or base['short']))
write('paired.csv',paired)
summary=[]
for key in sorted({r['key'] for r in paired}):
    rs=[r for r in paired if r['key']==key];ratios=[r['ratio'] for r in rs]
    summary.append(dict(key=key,pairs=len(rs),median_ratio=statistics.median(ratios),min_ratio=min(ratios),max_ratio=max(ratios),faster_pairs=sum(v<1 for v in ratios),slower_pairs=sum(v>1 for v in ratios),equal_pairs=sum(v==1 for v in ratios),all_validated=all(r['both_validated'] for r in rs),any_short=any(r['either_short'] for r in rs)))
write('paired-summary.csv',summary)
absolute=[]
for key,version in sorted({(r['key'],r['version']) for r in rows if r['stage']=='compare'}):
    rs=[r for r in rows if r['stage']=='compare' and r['key']==key and r['version']==version];v=[r['ns_per_op'] for r in rs]
    absolute.append(dict(key=key,version=version,samples=len(rs),min_ns=min(v),median_ns=statistics.median(v),max_ns=max(v),short_samples=sum(r['short'] for r in rs),all_validated=all(r['validation']=='PASS' for r in rs)))
write('absolute-summary.csv',absolute)
print('All available numeric samples retained; summaries are descriptive, never acceptance.')
