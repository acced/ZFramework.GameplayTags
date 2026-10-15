#!/usr/bin/env python3
"""Saved B1-pilot arithmetic only; never launches a process or adapts a sample."""
import hashlib,json,math,pathlib,sys
prior,out=map(pathlib.Path,sys.argv[1:3]);counts=json.loads((prior/'counts.json').read_text())
assert (out/'freeze.json').is_file(),'Freeze the repaired runner and prior evidence first'
for key,value in list(counts.items()):
    if not key.startswith('control-'):continue
    old=value.copy();calls=math.ceil(old['calls']*300/old['last_pilot_ms']);n=1 if '-s1-' in key else 1024
    assert 0<calls<2147483647 and (calls+64)*n*6<2**63
    counts[key]={'calls':calls,'baseline':'B1','target_ms':250,'derived_target_ms':300,'cap':2147483647,'capped':False,'adequate_baseline':None,'derivation':'ceil(prior_actual_calls*300/prior_actual_ms); no new pilot','prior':old,'predicted_ms':old['last_pilot_ms']*calls/old['calls']}
p=out/'counts.json'
with p.open('x') as f:f.write(json.dumps(counts,indent=2)+'\n')
with (out/'counts.sha256').open('x') as f:f.write(hashlib.sha256(p.read_bytes()).hexdigest()+'\n')
print('Four saved-pilot-derived control counts frozen; other40 unchanged. No measurements.')
