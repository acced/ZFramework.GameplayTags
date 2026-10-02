#!/usr/bin/env python3
"""Recompute all retained samples. Never filter slow samples or fit a selector."""
import argparse,pathlib,json,hashlib,zipfile,csv,statistics,math,collections

def sha(data):return hashlib.sha256(data).hexdigest()
def git_hash(kind,data):return hashlib.sha1(kind+b' '+str(len(data)).encode()+b'\0'+data).digest()
def source_tree(z):
    root={}
    for e in z.infolist():
        if e.is_dir():continue
        p=pathlib.PurePosixPath(e.filename)
        assert not p.is_absolute() and '..' not in p.parts
        d=root
        for part in p.parts[:-1]:d=d.setdefault(part,{})
        attr=e.external_attr>>16
        mode=b'120000' if (attr&0o170000)==0o120000 else b'100755' if attr&0o111 else b'100644'
        d[p.parts[-1]]=(mode,git_hash(b'blob',z.read(e)))
    def walk(d):
        parts=[]
        for name,value in sorted(d.items(),key=lambda kv:kv[0].encode()+(b'/' if isinstance(kv[1],dict) else b'')):
            mode,h=(b'40000',walk(value)) if isinstance(value,dict) else value
            parts.append(mode+b' '+name.encode()+b'\0'+h)
        return git_hash(b'tree',b''.join(parts))
    return walk(root).hex()
def save_csv(path,rows):
    with path.open('w',encoding='utf-8-sig',newline='') as f:
        if not rows:return
        w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
def main():
    ap=argparse.ArgumentParser();ap.add_argument('--root',required=True);args=ap.parse_args()
    root=pathlib.Path(args.root);out=root/'results';m=json.loads((out/'manifest.json').read_text())
    with zipfile.ZipFile(root/'source.zip') as z:
        assert z.comment.decode().strip()==m['head']
        assert source_tree(z)==m['tree']
        for name,digest in m['protected'].items():assert sha(z.read(name))==digest,name
    for job in m['jobs']:assert sha((out/job['binary']).read_bytes())==job['sha256']
    rounds=m['rounds'];store={};raw_count=timings=zero_samples=0;env=set();allrows=[]
    dimensions=('universe','members','distribution','seed','order','operation','strategy')
    for job in m['jobs']:
        for rnd in range(rounds):
            p=out/(job['label']+'-'+job['stage']+'-r'+str(rnd)+'.json');doc=json.loads(p.read_text())
            assert doc['label']==job['label'] and doc['round']==rnd and doc['stage']==job['stage']
            env.add((doc['runtime'],doc['arch'],doc['os']))
            for row in doc['rows']:
                assert row['label']==doc['label'] and row['round']==rnd
                assert len(row['ns'])==len(row['allocated'])==len(row['ms'])==len(row['gc'])==7
                for ns,ms,b in zip(row['ns'],row['ms'],row['allocated']):
                    assert math.isfinite(ns) and ns>=0 and b>=0
                    assert math.isclose(ns,ms*1e6/row['iterations'],rel_tol=1e-10,abs_tol=1e-6)
                if row['operation'] in ('prepared_reset','copy_into'):
                    assert all(b==0 for b in row['allocated']);zero_samples+=7
                assert len(set(row['allocated']))==1,'allocation count changed inside row'
                key=(job['stage'],job['label'])+tuple(row[k] for k in dimensions)
                entry=store.setdefault(key,{'rounds':{},'bytes':row['allocated'][0],'digest':row['digest'],'shape':row['shape'],'buffer':row['resultBuffer'],'count':row['resultCount']})
                assert rnd not in entry['rounds']
                assert (entry['bytes'],entry['digest'],entry['shape'],entry['buffer'],entry['count'])==(row['allocated'][0],row['digest'],row['shape'],row['resultBuffer'],row['resultCount'])
                entry['rounds'][rnd]=statistics.median(row['ns']);raw_count+=1;timings+=7
    for key,e in store.items():
        assert set(e['rounds'])==set(range(rounds))
        e['medians']=[e['rounds'][r] for r in range(rounds)];e['ns']=statistics.median(e['medians'])
        d=dict(stage=key[0],label=key[1],**dict(zip(dimensions,key[2:])),ns=e['ns'],bytes=e['bytes'],round_medians=json.dumps(e['medians']),resultBuffer=e['buffer'],resultCount=e['count'],digest=e['digest'],shape=json.dumps(e['shape'],sort_keys=True))
        allrows.append(d)
    aa={}
    def matching(key,label,strategy=None):
        x=list(key);x[1]=label
        if strategy is not None:x[-1]=strategy
        return store[tuple(x)]
    def within(a,b):return all(.95<=x/y<=1.05 for x,y in zip(a['medians'],b['medians']))
    for key,e in store.items():
        if key[1].endswith('AA'):continue
        a=matching(key,key[1]+'AA');summary=aa.setdefault('/'.join((key[0],key[1],str(key[-1]))),dict(cases=0,within5_every_round=0,persistent_shift5=0))
        summary['cases']+=1;summary['within5_every_round']+=within(e,a)
        ratios=[x/y for x,y in zip(e['medians'],a['medians'])]
        summary['persistent_shift5']+=all(x>1.05 for x in ratios) or all(x<.95 for x in ratios)
    pairs=[];groups={}
    def compare(key,e,old,oldaa,aaNew,comparison):
        assert e['digest']==old['digest']==oldaa['digest']==aaNew['digest']
        assert e['buffer']==old['buffer'] and e['count']==old['count'],'different output contract'
        fast=all(max(x,xa)<=.95*min(y,ya) for x,xa,y,ya in zip(e['medians'],aaNew['medians'],old['medians'],oldaa['medians']))
        slow=all(min(x,xa)>1.05*max(y,ya) for x,xa,y,ya in zip(e['medians'],aaNew['medians'],old['medians'],oldaa['medians']))
        stable=within(e,aaNew) and within(old,oldaa)
        d=dict(cpu=m['cpu'],comparison=comparison,stage=key[0],label=key[1],**dict(zip(dimensions,key[2:])),
            baseline_ns=old['ns'],candidate_ns=e['ns'],ratio=e['ns']/old['ns'],baseline_bytes=old['bytes'],candidate_bytes=e['bytes'],faster5=fast,slower5=slow,both_AA_within5=stable,
            resultBuffer=e['buffer'],resultCount=e['count'],digest=e['digest'])
        pairs.append(d)
        gkey='/'.join(map(str,(comparison,key[1],d['order'],d['operation'],d['members'])))
        g=groups.setdefault(gkey,dict(cases=0,faster=0,slower=0,aa_within5=0));g['cases']+=1;g['faster']+=fast;g['slower']+=slow;g['aa_within5']+=stable
    for key,e in store.items():
        label=key[1]
        if not label.endswith('-v2'):continue
        aaNew=matching(key,label+'AA')
        if key[0]=='bench' and key[-1]=='bulk':
            oldLabel=label.replace('-v2','-v1')
            compare(key,e,matching(key,oldLabel),matching(key,oldLabel+'AA'),aaNew,'v2_vs_v1')
            compare(key,e,matching(key,label,'loop'),matching(key,label+'AA','loop'),aaNew,'v2_vs_loop')
            if key[-3]=='sorted_unique':assert e['bytes']==matching(key,oldLabel)['bytes']
        if key[0]=='convert' and key[-1]=='direct':
            compare(key,e,matching(key,label,'copyfrom'),matching(key,label+'AA','copyfrom'),aaNew,'direct_vs_copyfrom')
    save_csv(out/'All_Results.csv',allrows);save_csv(out/'Comparisons.csv',pairs)
    tests={}
    for p in out.glob('*-tests.json'):
        d=json.loads(p.read_text());assert d['basic']['failures']==0 and d['basic']['positiveAllocationBytes']>0
        assert d['extra'].get('failures',0)==0
        tests[p.name]=d
    assert 'failures=0' in (out/'full-runtime.log').read_text()
    assert json.loads((out/'full-micro.json').read_text())['failures']==0
    codegen={p.name:[s for s in p.read_text().splitlines() if 'Assembly listing' in s or 'Total bytes of code' in s] for p in out.glob('*-codegen.log')}
    result=dict(head=m['head'],tree=m['tree'],sdk=m['sdk'],cpu=m['cpu'],environment=sorted(env),rows=len(store),raw_rows=raw_count,timing_samples=timings,zero_allocation_samples=zero_samples,
        source_and_samples_verified=True,protected_files=len(m['protected']),tests=tests,AA=aa,groups=groups,codegen=codegen,selector_implemented=False,production_promoted=False,native_unity='not_run')
    (out/'recheck.json').write_text(json.dumps(result,indent=2))
    focus=[d for d in pairs if d['universe']==262144 and d['seed'] in (20261002,20261023) and
        ((d['members'] in (0,1,8) and d['distribution']=='scattered') or (d['members']==4096 and d['distribution'] in ('contiguous','scattered'))) and
        ((d['stage']=='bench' and d['order'] in ('sorted_unique','shuffled_unique') and d['comparison']=='v2_vs_v1') or
        (d['stage']=='convert' and d['operation']=='new_convert'))]
    (out/'focus.json').write_text(json.dumps(focus,indent=2))
    report=['# Preparation v2 — measured results', '',json.dumps({k:result[k] for k in ('head','sdk','cpu','rows','timing_samples','zero_allocation_samples')},indent=2),
        '', 'All times below are ns/complete operation. Fresh conversion reserves max(requested,Count); ordinary clone remains unchanged. No automatic selector or cross-family Array/Micro bridge is claimed. Three-round 5% gates are screens, not confidence intervals.',
        '', '| Comparison | Layout | Members | Locality/order | Operation | Control ns | New ns | Control B | New B | A/A stable |', '|---|---|---:|---|---|---:|---:|---:|---:|---|']
    for d in focus:report.append('|%s|%s|%s|%s/%s|%s|%.3f|%.3f|%g|%g|%s|'%(d['comparison'],d['label'],d['members'],d['distribution'],d['order'],d['operation'],d['baseline_ns'],d['candidate_ns'],d['baseline_bytes'],d['candidate_bytes'],d['both_AA_within5']))
    report+=['','All negative cases and raw samples are retained in Comparisons.csv and source JSON. Fresh validation may allocate before an invalid late handle is detected; no partial result escapes. Reset validates first. Ordinary queries/union/append/remove are not changed. Linux .NET is not Unity, IL2CPP, Burst or physical-device validation.','', '## A/A',json.dumps(aa,indent=2)]
    (out/'REPORT.md').write_text('\n'.join(report))
    print('V2_RECHECK '+json.dumps({k:v for k,v in result.items() if k not in ('tests','codegen','groups')}))
    print('V2_GROUPS '+json.dumps(groups));print('V2_FOCUS '+json.dumps(focus))
    print('V2_TESTS '+json.dumps(tests))
if __name__=='__main__':main()
