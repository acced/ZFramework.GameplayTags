#!/usr/bin/env python3
"""Read/verify the losslessly concatenated public per-call evidence; no target execution."""
import argparse,hashlib,json,pathlib,sys
here=pathlib.Path(__file__).resolve().parent
p=argparse.ArgumentParser();p.add_argument('--label',help='Print one original raw JSONL log');a=p.parse_args()
e=here/'Evidence';index=[json.loads(s) for s in (e/'call-index.jsonl').read_text().splitlines()];numeric=fixtures=0
sha=lambda b:hashlib.sha256(b).hexdigest()
terminal=(e/'terminal-records.jsonseq').read_bytes();seen=set();segments={};terminal_segments=[]
for row in index:
    assert row['label'] not in seen;seen.add(row['label'])
    data=(e/row['raw_file']).read_bytes();start=row['raw_offset'];raw=data[start:start+row['raw_length']]
    assert len(raw)==row['raw_length'] and sha(raw)==row['raw_sha256']
    segments.setdefault(row['raw_file'],[]).append((start,start+len(raw)))
    start=row['terminal_offset'];tb=terminal[start:start+row['terminal_length']]
    assert len(tb)==row['terminal_length'] and sha(tb)==row['terminal_sha256'];t=json.loads(tb)
    terminal_segments.append((start,start+len(tb)))
    assert t['status']==row['status'] and t['returncode']==row['returncode'] and t['elapsed_seconds']==row['elapsed_seconds']
    assert row['stderr_contents']=='' and row['returncode']==0
    records=[json.loads(s) for s in raw.splitlines()];numeric+=sum(r.get('kind')=='sample' for r in records);fixtures+=row['mode']=='validate'
    assert any(r.get('kind')=='validation' and r.get('status')=='PASS' for r in records)
    if a.label==row['label']:sys.stdout.buffer.write(raw)
for name,ranges in segments.items():
    ranges.sort();assert ranges[0][0]==0 and all(left[1]==right[0] for left,right in zip(ranges,ranges[1:])) and ranges[-1][1]==(e/name).stat().st_size
terminal_segments.sort();assert terminal_segments[0][0]==0 and all(left[1]==right[0] for left,right in zip(terminal_segments,terminal_segments[1:])) and terminal_segments[-1][1]==len(terminal)
assert sha((e/'counts.json').read_bytes())==(e/'counts.sha256').read_text().strip()
assert len(index)==377 and numeric==315 and fixtures==62
if a.label and a.label not in seen:raise SystemExit('Unknown label')
if not a.label:print(json.dumps({'status':'PASS','calls':len(index),'numeric_samples':numeric,'fixtures':fixtures,'raw_and_terminal_hashes_verified':True,'target_execution':False}))
