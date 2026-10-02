#!/usr/bin/env python3
"""Recompute fixed measurements; lifecycle comparisons are not an accepted selector."""
import argparse, pathlib, json, hashlib, zipfile, csv, statistics, math, importlib.util

def load(path, name):
    spec=importlib.util.spec_from_file_location(name,path);m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m

def save(path, rows):
    with path.open('w',encoding='utf-8-sig',newline='') as f:
        if rows:
            w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--root',required=True);args=ap.parse_args()
    root=pathlib.Path(args.root);out=root/'results';m=json.loads((out/'manifest.json').read_text())
    repo=pathlib.Path(__file__).resolve().parents[2]
    old=load(repo/'Audit~/PreparationV2/review.py','v2_review_helpers')
    gen=load(repo/'Audit~/PreparationV3/generate.py','v3_source_helpers')
    with zipfile.ZipFile(root/'source.zip') as z:
        assert z.comment.decode().strip()==m['head']
        assert old.source_tree(z)==m['tree']
        for name,digest in m['protected'].items():assert hashlib.sha256(z.read(name)).hexdigest()==digest
    jobs={(j['stage'],j['label']):j for j in m['jobs']}
    for key,j in jobs.items():
        assert hashlib.sha256((out/j['binary']).read_bytes()).hexdigest()==j['sha256']
        if not key[1].endswith('AA'):assert jobs[(key[0],key[1]+'AA')]['sha256']==j['sha256']
    generated=out/'generated'
    for kind in ('runtime','micro'):
        name='RuntimeTagSet' if kind=='runtime' else 'DirectArraySet'
        v1=(generated/'controls/v1'/kind/'BulkBuilders.cs').read_text()
        v2=(generated/'controls/v2'/kind/'BulkBuilders.cs').read_text()
        v3=(generated/'v3'/kind/'BulkBuilders.cs').read_text()
        for sig in ('public void ResetFromSortedUnique(', 'private static void ValidateSortedHandles(', 'private void FillSortedHandles(', 'private void FillSortedIds('):
            assert gen.method(v1,sig)==gen.method(v3,sig)
        for sig in ('public static '+name+' FromSortedUnique(', 'public static '+name+' FromUnordered(', 'private void ValidateAndFillSorted('):
            assert gen.method(v2,sig)==gen.method(v3,sig)
        assert (generated/'controls/v2'/kind/(name+'.cs')).read_bytes()==(generated/'v3'/kind/(name+'.cs')).read_bytes()
    dims=('universe','members','distribution','seed','order','operation','strategy')
    store={};raw_count=samples=zero=0;environments=set()
    for job in m['jobs']:
        for rnd in range(m['rounds']):
            doc=json.loads((out/(job['label']+'-'+job['stage']+'-r'+str(rnd)+'.json')).read_text())
            assert (doc['label'],doc['stage'],doc['round'])==(job['label'],job['stage'],rnd)
            expected=({'bench':90,'convert':108,'frontier':144} if m['smoke'] else {'bench':720,'convert':432,'frontier':1728})[job['stage']]
            assert len(doc['rows'])==expected,(job,len(doc['rows']),expected)
            environments.add((doc['runtime'],doc['arch'],doc['os']))
            for row in doc['rows']:
                assert len(row['ns'])==len(row['allocated'])==len(row['gc'])==len(row['ms'])==7
                assert row['iterations']>0 and (row['label'],row['round'])==(job['label'],rnd)
                for ns,ms,b in zip(row['ns'],row['ms'],row['allocated']):
                    assert math.isfinite(ns) and ns>0 and b>=0
                    assert math.isclose(ns,ms*1e6/row['iterations'],rel_tol=1e-10,abs_tol=1e-6)
                if row['operation'] in ('prepared_reset','copy_into') or (job['stage']=='frontier' and row['operation']=='live_plus_0_unions' and row['strategy']=='keep'):
                    assert all(b==0 for b in row['allocated']);zero+=7
                assert len(set(row['allocated']))==1
                key=(job['stage'],job['label'])+tuple(row[k] for k in dims)
                entry=store.setdefault(key,{'rounds':{},'bytes':row['allocated'][0],'digest':row['digest'],
                    'shape':row['shape'],'buffer':row['resultBuffer'],'count':row['resultCount']})
                assert rnd not in entry['rounds']
                assert (entry['bytes'],entry['digest'],entry['shape'],entry['buffer'],entry['count'])==(row['allocated'][0],row['digest'],row['shape'],row['resultBuffer'],row['resultCount'])
                entry['rounds'][rnd]=statistics.median(row['ns']);raw_count+=1;samples+=7
    rows=[]
    for key,e in store.items():
        assert set(e['rounds'])==set(range(m['rounds']))
        e['medians']=[e['rounds'][r] for r in range(m['rounds'])];e['ns']=statistics.median(e['medians'])
        rows.append(dict(stage=key[0],label=key[1],**dict(zip(dims,key[2:])),ns=e['ns'],bytes=e['bytes'],
            round_medians=json.dumps(e['medians']),resultBuffer=e['buffer'],resultCount=e['count'],digest=e['digest'],shape=json.dumps(e['shape'],sort_keys=True)))
    def get(key,label=None,strategy=None):
        k=list(key)
        if label is not None:k[1]=label
        if strategy is not None:k[-1]=strategy
        return store[tuple(k)]
    def within(a,b):return all(.95<=x/y<=1.05 for x,y in zip(a['medians'],b['medians']))
    aa={}
    for key,e in store.items():
        if key[1].endswith('AA'):continue
        a=get(key,key[1]+'AA');g=aa.setdefault('/'.join((key[0],key[1],key[-1])),dict(cases=0,within5=0,persistent_shift5=0))
        g['cases']+=1;g['within5']+=within(e,a)
        ratios=[x/y for x,y in zip(e['medians'],a['medians'])]
        g['persistent_shift5']+=all(x>1.05 for x in ratios) or all(x<.95 for x in ratios)
    comparisons=[];groups={}
    def compare(key,e,baseline_label,baseline_strategy,comparison,same_storage=True):
        b=get(key,baseline_label,baseline_strategy);ba=get(key,baseline_label+'AA',baseline_strategy);a=get(key,key[1]+'AA')
        assert e['digest']==b['digest']==a['digest']==ba['digest'] and e['count']==b['count']
        if same_storage:assert e['buffer']==b['buffer']
        fast=all(max(x,xa)<=.95*min(y,ya) for x,xa,y,ya in zip(e['medians'],a['medians'],b['medians'],ba['medians']))
        slow=all(min(x,xa)>1.05*max(y,ya) for x,xa,y,ya in zip(e['medians'],a['medians'],b['medians'],ba['medians']))
        stable=within(e,a) and within(b,ba)
        d=dict(cpu=m['cpu'],comparison=comparison,stage=key[0],label=key[1],**dict(zip(dims,key[2:])),
            baseline_ns=b['ns'],candidate_ns=e['ns'],ratio=e['ns']/b['ns'],baseline_bytes=b['bytes'],candidate_bytes=e['bytes'],
            faster5=fast,slower5=slow,both_AA_within5=stable,baselineBuffer=b['buffer'],candidateBuffer=e['buffer'],resultCount=e['count'])
        comparisons.append(d)
        group='/'.join(str(d[k]) for k in ('comparison','label','order','operation','members'))
        g=groups.setdefault(group,dict(cases=0,faster5=0,slower5=0,stable=0,faster_and_stable=0))
        g['cases']+=1;g['faster5']+=fast;g['slower5']+=slow;g['stable']+=stable;g['faster_and_stable']+=fast and stable
    for key,e in store.items():
        label=key[1]
        if not label.endswith('-v3'):continue
        if key[0]=='bench' and key[-1]=='bulk':
            for version in ('v1','v2'):
                compare(key,e,label.replace('-v3','-'+version),'bulk','v3_vs_'+version)
            compare(key,e,label,'loop','v3_vs_loop')
            if key[-3]=='sorted_unique':assert e['bytes']==get(key,label.replace('-v3','-v1'))['bytes']
        if key[0]=='convert' and key[-1]=='direct':
            compare(key,e,label.replace('-v3','-v2'),'direct','v3_vs_v2_conversion')
            compare(key,e,label,'copyfrom','direct_vs_copyfrom')
        if key[0]=='frontier' and key[-1]!='keep':
            compare(key,e,label,'keep','frontier_'+key[-1]+'_vs_keep',False)
            if key[-1]=='direct':compare(key,e,label,'convert','frontier_direct_vs_convert')
    save(out/'All_Results.csv',rows);save(out/'Comparisons.csv',comparisons)
    frontier=[r for r in rows if r['stage']=='frontier'];save(out/'Frontier_All.csv',frontier)
    tests={}
    for p in out.glob('*-tests.json'):
        d=json.loads(p.read_text())
        assert d['basic']['failures']==0 and d['basic']['positiveAllocationBytes']>0
        assert d['extra'].get('failures',0)==0 and d['frontier']['failures']==0
        tests[p.name]={k:v.get('assertions',0) for k,v in d.items()}
    assert 'failures=0' in (out/'full-runtime.log').read_text()
    assert json.loads((out/'full-micro.json').read_text())['failures']==0
    codegen={p.name:[s for s in p.read_text().splitlines() if 'Assembly listing' in s or 'Total bytes of code' in s] for p in out.glob('*-codegen.log')}
    focus=[d for d in comparisons if d['universe']==262144 and d['distribution']=='scattered' and
        ((d['stage']=='bench' and d['seed']==20261002 and d['order']=='sorted_unique' and d['members'] in (0,1,2,8) and d['operation']=='prepared_reset' and d['comparison'] in ('v3_vs_v1','v3_vs_v2')) or
         (d['stage']=='convert' and d['seed']==20261023 and d['members']==0 and d['operation']=='new_convert' and d['comparison']=='v3_vs_v2_conversion'))]
    result=dict(head=m['head'],tree=m['tree'],sdk=m['sdk'],cpu=m['cpu'],environment=sorted(environments),
        rows=len(store),raw_rows=raw_count,timing_samples=samples,zero_allocation_samples=zero,
        source_and_samples_verified=True,protected_files=len(m['protected']),tests=tests,AA=aa,groups=groups,codegen=codegen,
        selector_implemented=False,production_promoted=False,native_unity='not_run')
    (out/'recheck.json').write_text(json.dumps(result,indent=2));(out/'focus.json').write_text(json.dumps(focus,indent=2))
    summary={k:v for k,v in result.items() if k not in ('tests','groups','codegen','AA')}
    report=['# Preparation v3: exact recombination and lifecycle frontier','',json.dumps(summary,indent=2),
        '', 'No pointwise winner is deployed. Existing live keep is allowed to retain identity; independent conversion/rebuild pays allocations. Live rebuild exports handles inside timing. Input-origin convert pays source construction AND conversion. Peer representation is fixed to original source for every strategy.',
        '', '## Focus (ns per complete operation)','', '| Comparison | Layout | N | Operation | v1/v2 ns | v3 ns | A/A stable |','|---|---|---:|---|---:|---:|---|']
    for d in focus:report.append('|%s|%s/%s|%s|%s|%.3f|%.3f|%s|'%(d['comparison'],d['label'],d['order'],d['members'],d['operation'],d['baseline_ns'],d['candidate_ns'],d['both_AA_within5']))
    report+=['','All raw samples, including negative/A/A-unstable rows, remain. Do not compare absolute ns with a different calibration/host. Linux .NET is not Unity/IL2CPP/Burst/device validation. No automatic selector or original-four-metric acceptance.','', '## Groups',json.dumps(groups,indent=2)]
    (out/'REPORT.md').write_text('\n'.join(report))
    # Compact logs; complete review and frontier remain in the artifact.
    totals={}
    for d in comparisons:
        k='/'.join((d['comparison'],d['label'],d['operation']))
        t=totals.setdefault(k,dict(cases=0,fast=0,slow=0,stable=0,fast_stable=0))
        t['cases']+=1;t['fast']+=d['faster5'];t['slow']+=d['slower5'];t['stable']+=d['both_AA_within5'];t['fast_stable']+=d['faster5'] and d['both_AA_within5']
    (out/'totals.json').write_text(json.dumps(totals,indent=2))
    print('V3_RECHECK '+json.dumps(summary));print('V3_TESTS '+json.dumps(tests))
    print('V3_TOTALS '+json.dumps(totals));print('V3_FOCUS '+json.dumps(focus))
if __name__=='__main__':main()
