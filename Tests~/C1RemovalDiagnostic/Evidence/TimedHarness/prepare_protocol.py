#!/usr/bin/env python3
"""Generate only the approved 12-cell diagnostic grid; never invokes .NET."""
import json,pathlib
here=pathlib.Path(__file__).resolve().parent
cells=[]
def add(group,op,n,target,capacity,shape,context,description):
    cells.append(dict(group=group,key=f'{group}-{op}-s{n}-t{target}-c{capacity}-{shape}-{context}',op=op,n=n,target=target,capacity=capacity,shape=shape,context=context,
      classification='preprepared-single-copy-diagnostic' if context=='preprepared-once' else 'whole-public-workflow',description=description))
add('primary','lifecycle',1024,1024,1024,'half-overlap','new','Unchanged C12 lifecycle through final Clear; shuffled Build starts capacity0; union1536/cap2048, intersection512/cap1024, cleared target cap1024.')
add('primary','copy',512,1024,1024,'lifecycle-intersection-refs','retained','CopyFrom(seed) restore + CopyFrom(source) + consumer; target1024/cap1024, source512/cap1024 shares intersection lhs representatives.')
add('diagnostic','copy',512,1024,1024,'lifecycle-intersection-refs','preprepared-once','Separate prepared-target diagnostic. CopyFrom(source)+consumer once per independently prepared target; no restore/build inside counters. Pool limit16384 targets,128MiB backing,144MiB pool-setup allocation ceiling.')
for n,target,capacity in [(0,0,0),(1,1,1),(0,1,1),(1,2,2)]:
    add('guard','copy',n,target,capacity,'same-names','retained','CopyFrom(seed) restore + CopyFrom(source) + consumer. Same-name source representatives; nonempty-to-empty and shrink-to-singleton are distinct guards.')
for op in ['append','right']:
    for context,capacity in [('new',1024),('retained',2048)]:
        add('guard',op,1023,1024,capacity,'last',context,'Unchanged C2 late miss at source position1022 value2045. New target build+growth stays inside counters, expected16496B/op and resultcap1025; retained restore stays inside counters, resultcap2048.')
add('guard','lifecycle',1,1,1,'full-overlap','new','Unchanged C12 singleton lifecycle through final Clear; input capacity metadata1; lifecycle Build starts capacity0, union1/intersection1.')
assert len(cells)==12 and len({c['key'] for c in cells})==12
(here/'cells.json').write_text(json.dumps(cells,indent=2)+'\n')
print('Prepared exactly12 cells; no .NET, builds, fixture execution, or timers.')
