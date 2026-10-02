#!/usr/bin/env python3
"""Reconstruct all medians and same-binary A/A, without discarding slow samples."""
import pathlib,sys,json,hashlib,zipfile,statistics,csv,math,argparse,importlib.util
HERE=pathlib.Path(__file__).resolve().parent;sys.path.insert(0,str(HERE))
from generate import EXPECTED,BASE,prior,fixed,bulk,block
NAMES=('direct','split','fixed','bulk','combined')
PAIRS=(('split','direct'),('fixed','split'),('bulk','split'),('combined','fixed'),('combined','bulk'),('combined','split'),('fixed','direct'),('bulk','direct'),('combined','direct'))
def sha(b):return hashlib.sha256(b).hexdigest()
def save(path,rows):
    with path.open('w',encoding='utf-8-sig',newline='') as f:
        if rows:
            w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
def main():
    ap=argparse.ArgumentParser();ap.add_argument('--root',required=True);args=ap.parse_args()
    out=pathlib.Path(args.root);m=json.loads((out/'manifest.json').read_text())
    assert m['base']==BASE and not m['tests_only']
    with zipfile.ZipFile(out/'source.zip') as z:
        assert {e.filename for e in z.infolist() if not e.is_dir()}==set(m['source_files'])
        if m['source_kind']=='git':
            spec=importlib.util.spec_from_file_location('tree_review',HERE.parents[1]/'Audit~/PreparationV2/review.py')
            tree_review=importlib.util.module_from_spec(spec);spec.loader.exec_module(tree_review)
            assert z.comment.decode().strip()==m['head']
            assert tree_review.source_tree(z)==m['tree']
        for name,digest in m['source_files'].items():assert sha(z.read(name))==digest,name
        for name,digest in m['protected'].items():assert sha(z.read(name))==digest,name
        assert (out/'generated/LocalExtra.cs').read_bytes()==z.read('Audit~/DenseLocal/Extra.cs')
        assert (out/'generated/BulkExtra.cs').read_bytes()==z.read('Audit~/DenseBulk/Extra.cs')
    g=out/'generated';direct=(g/'direct/DirectArraySet.cs').read_text()
    assert sha(direct.encode())==EXPECTED['direct']
    split=prior.split_cursor(direct);assert sha(split.encode())==EXPECTED['split']
    expected=dict(direct=direct,split=split,fixed=fixed(split),bulk=bulk(split),combined=bulk(fixed(split)))
    for name,code in expected.items():
        assert (g/name/'DirectArraySet.cs').read_text()==code,name
        assert sha((g/name/'BulkBuilders.cs').read_bytes())==EXPECTED['builder']
        for sig in ('private struct Records','public struct Enumerator','public bool HasTagExact(','private void ReserveEntries(','public void RemoveTags('):
            assert block(code,sig)==block(direct,sig)
    tests={}
    for name in [n+'-tests' for n in NAMES]+[n+'-full-tests' for n in NAMES]+['nohw-tests','portable-tests']:
        d=json.loads((out/(name+'.json')).read_text());assert d['failures']==0
        if 'bulkTests' in d:
            assert d['bulkTests']['growthCases']==1920 and d['bulkTests']['failures']==0
            assert d['localTests']['preparedBytes']==0 and d['positiveAllocationBytes']>0
            assert d['cursorBytes']==16 and d['enumeratorBytes']==32
        tests[name]=d
    assert 'failures=0' in (out/'runtime-full.log').read_text()
    assert len(m['jobs'])==30
    for job in m['jobs']:
        assert sha((out/job['binary']).read_bytes())==job['sha256']
        assert job['binary']==next(j['binary'] for j in m['jobs'] if j['label']==job['label'].removesuffix('AA') and j['stage']==job['stage'])
    data={};raw=zero=0;environment=set()
    zero_ops={'enumerate','exact_hit','exact_miss','union_into','reuse_copy_append','reuse_copy_remove','copy_from'}
    for job in m['jobs']:
        for rnd in range(m['rounds']):
            d=json.loads((out/f'{job["label"]}-{job["stage"]}-r{rnd}.json').read_text())
            assert (d['label'],d['stage'],d['round'])==(job['label'],job['stage'],rnd)
            expected_count={'bench':126 if m['smoke'] else 2520,'pairs':16 if m['smoke'] else 192,'scale':48}[job['stage']]
            assert len(d['rows'])==expected_count,(job,len(d['rows']))
            environment.add((d['runtime'],d['arch']))
            for r in d['rows']:
                assert r['label']==d['label'] and r['round']==rnd
                assert len(r['ns'])==len(r['allocated'])==len(r['gc'])==7
                assert all(math.isfinite(t) and t>0 for t in r['ns'])
                assert all(b==r['bytesPerOp']*r['iterations'] for b in r['allocated'])
                if r['operation'] in zero_ops:assert r['bytesPerOp']==0;zero+=7
                key=(d['stage'],d['label'],r['operation'],r['output'],json.dumps(r['shape'],sort_keys=True))
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
    aa={};comparisons=[]
    for k,e in data.items():
        if k[1].endswith('AA'):continue
        a=get(k,k[1]+'AA');q=aa.setdefault(k[0]+'/'+k[1],dict(cases=0,within5=0,persistent_shift=0))
        q['cases']+=1;q['within5']+=within(e,a)
        q['persistent_shift']+=all(x>1.05*y for x,y in zip(e['times'],a['times'])) or all(x<.95*y for x,y in zip(e['times'],a['times']))
        for candidate,ref in PAIRS:
            if candidate!=k[1]:continue
            b=get(k,ref);ba=get(k,ref+'AA');s=json.loads(k[4])
            assert (e['bytes'],e['count'],e['buffer'])==(b['bytes'],b['count'],b['buffer']),'allocation/count/capacity contract'
            fast=all(max(x,xa)<.95*min(y,ya) for x,xa,y,ya in zip(e['times'],a['times'],b['times'],ba['times']))
            slow=all(min(x,xa)>1.05*max(y,ya) for x,xa,y,ya in zip(e['times'],a['times'],b['times'],ba['times']))
            comparisons.append(dict(cpu=m['cpu'],candidate=candidate,control=ref,stage=k[0],operation=k[2],output=k[3],universe=s['u'],members=s['n'],distribution=s['dist'],left=s['left'],right=s['right'],baseline_ns=b['ns'],candidate_ns=e['ns'],ratio=e['ns']/b['ns'],bytes=e['bytes'],faster5=fast,slower5=slow,AAstable=within(e,a) and within(b,ba),shape=k[4]))
    def stat(rs):return dict(cases=len(rs),fast=sum(r['faster5'] for r in rs),slow=sum(r['slower5'] for r in rs),stable=sum(r['AAstable'] for r in rs),fast_stable=sum(r['faster5'] and r['AAstable'] for r in rs))
    summary={c+'/'+b+'/'+stage:stat([r for r in comparisons if r['candidate']==c and r['control']==b and r['stage']==stage]) for c,b in PAIRS for stage in ('bench','pairs','scale')}
    result=dict(head=m['head'],tree=m['tree'],source_kind=m['source_kind'],base=BASE,cpu=m['cpu'],sdk=m['sdk'],smoke=m['smoke'],environment=sorted(environment),aggregate_rows=len(data),raw_rows=raw,timing_samples=7*raw,zero_allocation_samples=zero,source_verified=True,allocation_contracts_equal=True,tests=tests,AA=aa,summary=summary,production_promoted=False,nativeUnity='not_run')
    save(out/'All_Results.csv',allrows);save(out/'Comparisons.csv',comparisons)
    (out/'recheck.json').write_text(json.dumps(result,indent=2))
    (out/'regressions.json').write_text(json.dumps([r for r in comparisons if r['slower5']],indent=2))
    print('DENSE_BULK_RECHECK '+json.dumps({k:v for k,v in result.items() if k not in ('tests','AA')}))
if __name__=='__main__':main()
