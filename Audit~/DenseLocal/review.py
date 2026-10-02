#!/usr/bin/env python3
"""Rebuild all measurements from raw samples; include failures and unchanged paths."""
import pathlib,sys,argparse,json,zipfile,hashlib,statistics,csv,math,importlib.util,collections
HERE=pathlib.Path(__file__).resolve().parent;ROOT=HERE.parents[1]
sys.path.insert(0,str(HERE))
from generate import old,split_cursor,block
spec=importlib.util.spec_from_file_location('fixed_tree_review',ROOT/'Audit~/PreparationV2/review.py')
tree_review=importlib.util.module_from_spec(spec);spec.loader.exec_module(tree_review)
def sha(data):return hashlib.sha256(data).hexdigest()
def save(path,rows):
    with path.open('w',encoding='utf-8-sig',newline='') as f:
        if rows:
            w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
def main():
    ap=argparse.ArgumentParser();ap.add_argument('--root',required=True);args=ap.parse_args()
    root=pathlib.Path(args.root);out=root/'results';m=json.loads((out/'manifest.json').read_text())
    with zipfile.ZipFile(root/'source.zip') as archive:
        assert archive.comment.decode().strip()==m['head']
        assert tree_review.source_tree(archive)==m['tree']
        for name,digest in m['protected'].items():assert sha(archive.read(name))==digest
    for job in m['jobs']:assert sha((out/job['binary']).read_bytes())==job['sha256']
    g=out/'generated';original=(g/'pinned/control/DirectArraySet.cs').read_text()
    assert sha(original.encode())==old.EXPECTED['micro/DirectArraySet.cs']
    direct=old.dense_result(original)
    expected={'direct':direct,'shared':old.dense_result(old.cursor(original)),'split':split_cursor(direct)}
    for name,code in expected.items():
        assert (g/name/'DirectArraySet.cs').read_text()==code
        assert sha((g/name/'BulkBuilders.cs').read_bytes())==old.EXPECTED['micro/BulkBuilders.cs']
    for sig in ('private struct Records','public struct Enumerator','public bool HasTagExact(','public void RemoveTags('):
        assert block(expected['split'],sig)==block(direct,sig)
    data={};raw=zero=0;environment=set()
    zero_ops={'enumerate','exact_hit','exact_miss','union_into','reuse_copy_append','reuse_copy_remove','copy_from'}
    for job in m['jobs']:
        for rnd in range(m['rounds']):
            doc=json.loads((out/f'{job["label"]}-{job["stage"]}-r{rnd}.json').read_text())
            assert (doc['label'],doc['stage'],doc['round'])==(job['label'],job['stage'],rnd)
            environment.add((doc['runtime'],doc['arch']))
            for r in doc['rows']:
                assert r['label']==doc['label'] and r['round']==rnd
                assert len(r['ns'])==len(r['allocated'])==len(r['gc'])==7
                assert all(math.isfinite(n) and n>0 for n in r['ns'])
                assert all(b==r['bytesPerOp']*r['iterations'] for b in r['allocated'])
                if r['operation'] in zero_ops:assert r['bytesPerOp']==0;zero+=7
                key=(job['stage'],job['label'],r['operation'],r['output'],json.dumps(r['shape'],sort_keys=True))
                e=data.setdefault(key,dict(rounds={},bytes=r['bytesPerOp'],count=r['resultCount'],buffer=r['resultBuffer']))
                assert rnd not in e['rounds']
                assert (e['bytes'],e['count'],e['buffer'])==(r['bytesPerOp'],r['resultCount'],r['resultBuffer'])
                e['rounds'][rnd]=statistics.median(r['ns']);raw+=1
    allrows=[]
    for k,e in data.items():
        assert set(e['rounds'])==set(range(m['rounds']))
        e['times']=[e['rounds'][i] for i in range(m['rounds'])];e['ns']=statistics.median(e['times']);s=json.loads(k[4])
        allrows.append(dict(cpu=m['cpu'],stage=k[0],label=k[1],operation=k[2],output=k[3],universe=s['u'],members=s['n'],distribution=s['dist'],left=s['left'],right=s['right'],ns=e['ns'],bytes=e['bytes'],resultCount=e['count'],resultBuffer=e['buffer'],round_medians=json.dumps(e['times']),shape=k[4]))
    def get(k,name):return data[(k[0],name)+k[2:]]
    def within(a,b):return all(.95<=x/y<=1.05 for x,y in zip(a['times'],b['times']))
    comparisons=[];groups={};aa={}
    for k,e in data.items():
        if k[1].endswith('AA'):continue
        mate=get(k,k[1]+'AA');ag=aa.setdefault(k[0]+'/'+k[1],dict(cases=0,within5=0,persistent_shift=0))
        ag['cases']+=1;ag['within5']+=within(e,mate)
        ag['persistent_shift']+=all(x>1.05*y for x,y in zip(e['times'],mate['times'])) or all(x<.95*y for x,y in zip(e['times'],mate['times']))
        if k[1]=='direct':continue
        for ref in (['direct','shared'] if k[1]=='split' else ['direct']):
            baseline=get(k,ref);ba=get(k,ref+'AA')
            assert (e['bytes'],e['count'],e['buffer'])==(baseline['bytes'],baseline['count'],baseline['buffer']),'changed result/alloc contract'
            fast=all(max(x,xa)<.95*min(y,ya) for x,xa,y,ya in zip(e['times'],mate['times'],baseline['times'],ba['times']))
            slow=all(min(x,xa)>1.05*max(y,ya) for x,xa,y,ya in zip(e['times'],mate['times'],baseline['times'],ba['times']))
            stable=within(e,mate) and within(baseline,ba);s=json.loads(k[4])
            relation='single' if s['right']=='-' else 'same' if s['left']==s['right'] else 'mixed'
            target=k[0]=='bench' and s['n']>0 and ((k[2]=='copy_from' and s['left']=='Dense' and k[3]=='Micro') or (k[2] in ('fresh_union','union_into') and relation=='mixed' and k[3]=='Micro') or (k[2] in ('copy_append','reuse_copy_append') and s['left']=='Micro' and s['right']=='Dense'))
            row=dict(cpu=m['cpu'],candidate=k[1],control=ref,stage=k[0],operation=k[2],output=k[3],universe=s['u'],members=s['n'],distribution=s['dist'],left=s['left'],right=s['right'],relation=relation,target_path=target,baseline_ns=baseline['ns'],candidate_ns=e['ns'],ratio=e['ns']/baseline['ns'],bytes=e['bytes'],faster5=fast,slower5=slow,AAstable=stable,shape=k[4]);comparisons.append(row)
            group='/'.join((k[1]+'_vs_'+ref,k[0],k[2],relation,k[3]))
            q=groups.setdefault(group,dict(cases=0,fast=0,slow=0,stable=0,fast_stable=0))
            q['cases']+=1;q['fast']+=fast;q['slow']+=slow;q['stable']+=stable;q['fast_stable']+=fast and stable
    tests={}
    for p in out.glob('*tests.json'):
        d=json.loads(p.read_text());assert d['failures']==0
        if 'positiveAllocationBytes' in d:assert d['positiveAllocationBytes']>0
        if 'localTests' in d:
            assert d['localTests']['failures']==0 and d['localTests']['preparedBytes']==0
        tests[p.name]=d
    for name in ('direct','split'):
        assert tests[name+'-tests.json']['cursorBytes']==16
        assert tests[name+'-tests.json']['enumeratorBytes']==32
    assert 'failures=0' in (out/'runtime-full.log').read_text()
    save(out/'All_Results.csv',allrows);save(out/'Comparisons.csv',comparisons)
    focus=[r for r in comparisons if r['candidate']=='split' and r['control']=='direct' and r['universe']==262144 and r['members'] in (8,4096) and r['distribution'] in ('contiguous','scattered') and json.loads(r['shape'])['seed']==20261031 and r['operation'] in ('fresh_union','union_into','copy_from','copy_append','enumerate')]
    negative=[r for r in comparisons if r['candidate']=='split' and r['control']=='direct' and r['slower5']]
    target=[r for r in comparisons if r['candidate']=='split' and r['control']=='direct' and r['target_path']]
    stat=lambda rs:dict(cases=len(rs),fast=sum(r['faster5'] for r in rs),slow=sum(r['slower5'] for r in rs),stable=sum(r['AAstable'] for r in rs),fast_stable=sum(r['faster5'] and r['AAstable'] for r in rs))
    target_summary={op:stat([r for r in target if r['operation']==op]) for op in sorted({r['operation'] for r in target})}
    unchanged_enum={mode:stat([r for r in comparisons if r['candidate']=='split' and r['control']=='direct' and r['operation']=='enumerate' and r['left']==mode]) for mode in ('Micro','Dense')}
    result=dict(head=m['head'],tree=m['tree'],cpu=m['cpu'],sdk=m['sdk'],environment=sorted(environment),aggregate_rows=len(data),raw_rows=raw,timing_samples=7*raw,zero_allocation_samples=zero,source_verified=True,allocation_contracts_equal=True,tests=tests,groups=groups,AA=aa,target_summary=target_summary,unchanged_public_enumeration=unchanged_enum,production_promoted=False,nativeUnity='not_run')
    (out/'recheck.json').write_text(json.dumps(result,indent=2));(out/'focus.json').write_text(json.dumps(focus,indent=2));(out/'regressions.json').write_text(json.dumps(negative,indent=2))
    print('DENSE_LOCAL_RECHECK '+json.dumps({k:v for k,v in result.items() if k not in ('tests','groups','AA')}))
    print('DENSE_LOCAL_TESTS '+json.dumps({k:{n:v for n,v in d.items() if n in ('assertions','failures','cursorBytes','enumeratorBytes','localTests')} for k,d in tests.items()}))
    print('DENSE_LOCAL_AA '+json.dumps(aa))
    print('DENSE_LOCAL_FOCUS '+json.dumps([r for r in focus if r['target_path'] or r['operation']=='enumerate']))
    print('DENSE_LOCAL_REGRESSIONS '+json.dumps(negative))
if __name__=='__main__':main()
