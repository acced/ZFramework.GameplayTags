#!/usr/bin/env python3
"""Independent reconstruction of raw samples, controls, allocation and A/A uncertainty."""
import argparse,csv,hashlib,json,math,pathlib,statistics,zipfile,sys
ROOT=pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Audit~/ArrayBuild'))
from recheck import tree

def main():
    p=argparse.ArgumentParser();p.add_argument('--root',required=True);args=p.parse_args();root=pathlib.Path(args.root);out=root/'results'
    src=json.loads((out/'sources.json').read_text());host=json.loads((out/'host.json').read_text());cpu=host['machine']
    with zipfile.ZipFile(root/'source.zip') as z:
        assert tree(z)==src['tree']
        for label,ref in src['refs'].items():
            with zipfile.ZipFile(root/'references'/(label+'.zip')) as refz:
                assert tree(refz)==ref['tree']
                if label=='auto':
                    protected=[n for n in refz.namelist() if n.endswith('.cs') and n.startswith(('Runtime/','Editor/','Samples~/','Tests/'))]
                    for n in protected:assert z.read(n)==refz.read(n),n
    for name,digest in src['variants'].items():assert hashlib.sha256((out/'generated'/name/'RuntimeTagSet.cs').read_bytes()).hexdigest()==digest
    hashes=json.loads((out/'binary-sha256.json').read_text())
    for contract in ('fresh','prepared'):assert hashes['auto/'+contract]==hashes['autoAA/'+contract]
    raw={};nrows=nsamples=zero=0;digests={}
    for file in sorted(out.glob('*-r[0-9].json')):
        variant,contract,r=file.stem.split('-');r=int(r[1:])
        data=json.loads(file.read_text())
        for row in data['rows']:
            assert row['status']=='measured',row
            assert len(row['ns'])==len(row['allocated_bytes'])==len(row['gc0'])==len(row['gc1'])==len(row['gc2'])==7
            assert all(math.isfinite(x) and x>=0 for x in row['ns'])
            k=(variant,contract,row['operation'],row['size'],row['universe'],row['distribution'])
            assert r not in raw.setdefault(k,{})
            raw[k][r]=row;nrows+=1;nsamples+=7
            shape=k[3:];d=row['inputDigest'];assert shape not in digests or digests[shape]==d;digests[shape]=d
            if contract=='prepared':assert all(x==0 for x in row['allocated_bytes']),k;zero+=7
    values={}
    for k,rounds in raw.items():
        assert sorted(rounds)==[0,1,2]
        med=[statistics.median(rounds[r]['ns']) for r in range(3)]
        values[k]={'ns':statistics.median(med),'rounds':med,'bytes':statistics.median(x for row in rounds.values() for x in row['allocated_bytes']),
            'gc0':sum(sum(row['gc0']) for row in rounds.values()),'gc1':sum(sum(row['gc1']) for row in rounds.values()),'gc2':sum(sum(row['gc2']) for row in rounds.values()),
            'input_storage':rounds[0]['input_storage'],'output_storage':None if k[2].startswith('exact') else rounds[0].get('result_storage'),
            'input_buffer_bytes':rounds[0]['input_member_buffer_bytes'],'input_digest':rounds[0]['inputDigest']}
    aa={}
    for k,v in values.items():
        if k[0]!='auto':continue
        b=values[('autoAA',)+k[1:]]
        assert v['bytes']==b['bytes']
        ratios=[x/y for x,y in zip(v['rounds'],b['rounds'])]
        aa[k[1:]]={'ns':v['ns'],'aa_ns':b['ns'],'round_ratios':ratios,'every_round_within5':all(1/1.05<=x<=1.05 for x in ratios),
            'persistent_shift5':all(x>1.05 for x in ratios) or all(x<1/1.05 for x in ratios)}
    rows=[];gates={};primary={'exact','union','copy_append','copy_remove','union_into','copy_append_reuse','copy_remove_reuse'}
    for k,v in sorted(values.items()):
        refs={name:values[(name,)+k[1:]] for name in ('auto','autoAA','direct')}
        lower=v['ns']<min(r['ns'] for r in refs.values())
        margin=all(v['rounds'][i]<=.95*min(r['rounds'][i] for r in refs.values()) for i in range(3))
        auto=refs['auto'];faster=all(v['rounds'][i]<=.95*auto['rounds'][i] for i in range(3));slower=all(v['rounds'][i]>1.05*auto['rounds'][i] for i in range(3))
        if k[0] in ('dense','simd','copy','combined'):assert v['bytes']==auto['bytes'],('allocation changed',k,v['bytes'],auto['bytes'])
        row=dict(zip(('variant','contract','operation','members','definitions','distribution'),k));row.update(cpu=cpu,value=v,
            lower_controls=lower,margin_controls=margin,faster5_auto=faster,slower5_auto=slower,aa=aa[k[1:]])
        rows.append(row)
        if k[0] in ('dense','simd','copy','combined') and k[2] in primary:
            group=('small' if k[3]<=128 else 'large')+'/'+k[0]+'/'+k[1]
            g=gates.setdefault(group,{'cases':0,'lower_controls':0,'margin_controls':0,'faster5_auto':0,'slower5_auto':0,'AA_within5':0})
            for name,yes in [('cases',True),('lower_controls',lower),('margin_controls',margin),('faster5_auto',faster),('slower5_auto',slower),('AA_within5',aa[k[1:]]['every_round_within5'])]:g[name]+=int(yes)
    (out/'comparison.json').write_text(json.dumps(rows,indent=2))
    with (out/'all.csv').open('w',newline='') as f:
        fields=['cpu','variant','contract','operation','members','definitions','distribution','ns','r0','r1','r2','bytes','gc0','gc1','gc2','input_storage','output_storage','faster5_auto','slower5_auto','lower_controls','margin_controls','aa_within5']
        w=csv.DictWriter(f,fieldnames=fields);w.writeheader()
        for r in rows:
            v=r['value'];flat={n:r[n] for n in fields if n in r};flat.update({n:v[n] for n in fields if n in v});flat.update(r0=v['rounds'][0],r1=v['rounds'][1],r2=v['rounds'][2],aa_within5=r['aa']['every_round_within5']);w.writerow(flat)
    tests={}
    for log in out.glob('*-tests.log'):
        tests[log.name]=[line for line in log.read_text().splitlines() if line.startswith(('PASS','FAIL','INTEGER','PREPARED'))]
    verification={'head':src['head'],'tree':src['tree'],'cpu':cpu,'rows':len(rows),'raw_rows':nrows,'timing_samples':nsamples,'prepared_zero_samples':zero,
        'protected_cs_files_unchanged':len(protected),'candidate_allocations_equal_auto':True,'AA_binary_equal':True,
        'AA_cases':len(aa),'AA_every_round_within5':sum(x['every_round_within5'] for x in aa.values()),'AA_persistent_shift5':sum(x['persistent_shift5'] for x in aa.values()),
        'gates':gates,'tests':tests,'native_unity':'not_run','performance_release_approved':False}
    (out/'review.json').write_text(json.dumps(verification,indent=2))
    labels=['auto','autoAA','direct','dense','simd','copy','combined']
    lines=['# Unrestricted adaptive research — measured '+cpu,'','Tested commit: `'+src['head']+'`','',
        'All variants preserve the global registry, class API, capacity and representation policy. No production Runtime was changed. A/A uses the identical executable. Do not infer universal speed from a single matrix.','',
        f'A/A: {verification["AA_every_round_within5"]}/{len(aa)} rows stay within 5% in every round; {verification["AA_persistent_shift5"]} have a same-direction >5% shift in all rounds. These are diagnostic screens, not confidence intervals.','']
    for n,u in [(8,262144),(1024,10000),(4096,262144)]:
        for dist in (['scattered'] if n==8 else ['contiguous','scattered']):
            for contract in ('fresh','prepared'):
                lines += [f'## {u} definitions / {n} members / {dist} / {contract}','',
                    'ns/op (B/op); three process medians, seven retained samples per process. CPU data remain separate.','',
                    '| Operation | '+' | '.join(labels)+' |','|---|'+'---:|'*len(labels)]
                ops=['exact','union','copy_append','copy_remove'] if contract=='fresh' else ['exact','union_into','copy_append_reuse','copy_remove_reuse']
                for op in ops:
                    vs=[values[(label,contract,op,n,u,dist)] for label in labels]
                    lines.append('| '+op+' | '+' | '.join(f'{v["ns"]:.3f} ({v["bytes"]:g})' for v in vs)+' |')
    lines+=['','## Full gates','', '| stage/variant/contract | cases | lower all medians | >=5% all references every round | >=5% faster Auto | >5% slower Auto | A/A within5 |','|---|---:|---:|---:|---:|---:|---:|']
    for name,g in sorted(gates.items()):lines.append('| '+name+' | '+' | '.join(str(g[k]) for k in ('cases','lower_controls','margin_controls','faster5_auto','slower5_auto','AA_within5'))+' |')
    lines+=['','## Limits','', 'Linux .NET is not Unity/Mono/IL2CPP/Burst acceptance. Same half-overlap/half-subset workload, fixed seed, no full relation/churn timings or complete historical comparison. No object pooling or deferred Count. C# namespaces and public API preserved. Micro 2^20 restriction is not imposed on the new Auto-based candidates. Native wide-registry performance and retention peaks remain open.','',
        'The current Auto byte crossover is deliberately unchanged. Threshold tuning, inline storage, per-block Roaring, run containers, and global/local domain restriction are NOT implemented in this pilot.']
    (out/'REPORT.md').write_text('\n'.join(lines)+'\n')
    print('ADAPTIVE_REVIEW '+json.dumps(verification),flush=True)
    print('\n'.join(lines),flush=True)
if __name__=='__main__':main()
