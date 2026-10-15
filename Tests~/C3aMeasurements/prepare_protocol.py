#!/usr/bin/env python3
"""Generate only the 31 declared public-workflow cells; never invokes .NET."""
import json,pathlib
here=pathlib.Path(__file__).resolve().parent
cells=[]
def add(group,op,n,target,capacity,shape,context,description):
    key=f'{group}-{op}-l{target}-r{n}-c{capacity}-{shape}-{context}' if group=='core' else f'{group}-{op}-s{n}-t{target}-c{capacity}-{shape}-{context}'
    cells.append(dict(group=group,key=key,op=op,n=n,target=target,capacity=capacity,shape=shape,context=context,
                      classification='whole-public-workflow',description=description))
for n in [0,1,8,1024]:
    shapes=['both-empty'] if n==0 else ['lhs-before-rhs','full-overlap'] if n==1 else ['half-overlap','full-overlap','lhs-before-rhs','rhs-before-lhs']
    for shape in shapes:
        for op,context,cap in [('union','new',2*n),('setunion','retained',2*n)]:
            boundary='Factory Union(lhs,rhs) + consumer; fresh result allocated at capacity lhs.Count+rhs.Count inside counters.' if op=='union' else 'CopyFrom(lhs) restore + SetUnion(lhs,rhs) + consumer; result is distinct from both inputs.'
            add('core',op,n,n,cap,shape,context,boundary)
for n,shape in [(1,'full-overlap'),(1024,'half-overlap')]:
    add('guard', 'lifecycle',n,n,n,shape,'new','Unchanged C12 whole lifecycle; capacity field describes input setup, while lifecycle target Build(shuffled,0) starts at zero.')
add('guard','copy',512,1024,1024,'lifecycle-intersection-refs','retained','CopyFrom(seed) restore + CopyFrom(source) + consumer; seed evens 0..2046, source evens 1024..2046 shares lhs representatives and retains capacity1024.')
for n in [0,1]:
    add('guard','copy',n,n,n,'same-names','retained','Unchanged C12 retained same-count copy sentinel, including CopyFrom(seed) restoration; empty is 0-to-0, singleton is 1-to-1.')
for op in ['append','right']:
    for context,capacity in [('new',1024),('retained',2048)]:
        add('guard',op,1023,1024,capacity,'last',context,'Unchanged C12 late-miss geometry; source last sorted position1022 is missing value2045. New builds target; retained restores via CopyFrom(seed), inside counters.')
assert len(cells)==31 and len({c['key'] for c in cells})==31
(here/'cells.json').write_text(json.dumps(cells,indent=2)+'\n')
print('Prepared exactly31 cells; no .NET, builds, fixture execution, or timers.')
