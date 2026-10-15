#!/usr/bin/env python3
"""Generate the declared finite cell grid. Does not execute .NET or measure."""
import json,pathlib
here=pathlib.Path(__file__).resolve().parent
cells=[]
def add(stage,op,n,tn,cap,shape='contained',context='retained'):
    key=f'{stage}-{op}-s{n}-t{tn}-c{cap}-{shape}-{context}'
    cells.append(dict(stage=stage,key=key,op=op,n=n,target=tn,capacity=cap,shape=shape,context=context,
                      classification='diagnostic' if context in ('isolated','cold-process','first-object') or op=='control' else 'whole-public-workflow'))
# C1: independent target count/capacity, not an exhaustive Cartesian grid.
for n,tn,cap in [(0,0,0),(0,1,1),(0,1024,2048),(1,0,0),(1,1,1),(1,1024,2048),(8,1024,2048),(64,64,128),(1024,1024,2048)]:
    add('C1','copy',n,tn,cap,'same-names' if n==tn else 'different')
for n in [1,1024]:
    add('C1','copy',n,0,0,'different','new')
    add('C1','copy',n,0,0,'different','first-object')
add('C1','copy',1,0,0,'different','cold-process')
# C2: all missing positions and representatives, retained counts plus selective growth.
for op in ['append','left','right']:
    for shape in ['first','middle','last','multiple','contained']:
        add('C2',op,1023,1024,2048,shape)
for op in ['append','right']:
    add('C2',op,7,8,16,'last')
    add('C2',op,1023,1024,1024,'last','new')
    add('C2',op,1023,1024,1024,'last','first-object')
    add('C2',op,1023,1024,2048,'last','isolated')
add('C2','append',1023,1024,2048,'last','cold-process')
for n in [1,1024]:
    add('C1','lifecycle',n,n,n,context='new')
    add('control','control',n,n,n)
    add('control','enumerate',n,n,n)
assert len(cells)==44 and len({c['key'] for c in cells})==44
(here/'cells.json').write_text(json.dumps(cells,indent=2)+'\n')
print(f'Prepared {len(cells)} cells; no measurements.')
