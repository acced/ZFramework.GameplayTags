#!/usr/bin/env python3
"""Quick independent diagnostic using exactly the main runner's fault build logic."""
from pathlib import Path
import json,hashlib
ROOT=Path(__file__).resolve().parents[2]
wrapper=ROOT/'Audit~/DenseDeltaSafety/run.py'
ns={'__file__':str(wrapper),'__name__':'fault_preparation'}
text=wrapper.read_text();prefix=text[:text.index('exec(compile(')]
exec(compile(prefix,str(wrapper),'exec'),ns)
code=ns['code']
marker="    oldtest=out/'generated/FullMicroTests.cs'"
assert code.count(marker)==1
insert='''    summaries={}
    for n in files:
        f=json.loads((out/(n+'-fault.json')).read_text())
        summaries[n]={k:v for k,v in f.items() if k!='observations'}
        summaries[n]['observedInvalid']=len(f['observations'])
        summaries[n]['minimalObservations']=[r for r in f['observations'] if r['shape']=='minimal']
    (out/'FaultOnlySummary.json').write_text(json.dumps(dict(head=head,tree=tree,faults=summaries,sources=fault_hashes,timingPerformed=False),indent=2))
    print('FAULT_ONLY_SUMMARY '+json.dumps(summaries),flush=True)
    return
'''
code=code.replace(marker,insert+marker,1)
ns={'__file__':str(wrapper),'__name__':'__main__'}
exec(compile(code,str(wrapper),'exec'),ns)
