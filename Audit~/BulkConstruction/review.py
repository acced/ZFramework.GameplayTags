#!/usr/bin/env python3
"""Reconstruct all bulk samples independently. No sample removal, fitting or winner splicing."""
import argparse,pathlib,json,hashlib,zipfile,re,math,statistics,csv
from collections import defaultdict
FIELDS=('universe','members','distribution','seed','order','operation','strategy')
def git_tree(z):
    root={}
    for e in z.infolist():
        if e.is_dir():continue
        path=pathlib.PurePosixPath(e.filename)
        if path.is_absolute() or '..' in path.parts:raise ValueError('Unsafe source archive')
        node=root
        for part in path.parts[:-1]:node=node.setdefault(part,{})
        data=z.read(e);mode=b'100755' if (e.external_attr>>16)&0o111 else b'100644'
        node[path.name]=(mode,hashlib.sha1(b'blob '+str(len(data)).encode()+b'\0'+data).digest())
    def encode(node):
        data=bytearray()
        for name,value in sorted(node.items(),key=lambda kv:(kv[0]+('/' if isinstance(kv[1],dict) else '')).encode()):
            mode,digest=(b'40000',encode(value)) if isinstance(value,dict) else value
            data.extend(mode+b' '+name.encode()+b'\0'+digest)
        return hashlib.sha1(b'tree '+str(len(data)).encode()+b'\0'+data).digest()
    return encode(root).hex()
def dump_csv(path,rows):
    with path.open('w',encoding='utf-8-sig',newline='') as f:
        writer=csv.DictWriter(f,fieldnames=list(rows[0]));writer.writeheader();writer.writerows(rows)
def main():
    parser=argparse.ArgumentParser();parser.add_argument('--root',required=True);args=parser.parse_args()
    root=pathlib.Path(args.root);out=root/'results';m=json.loads((out/'manifest.json').read_text());rounds=m['rounds']
    assert m['sdk']=='8.0.425' and not m['smoke']
    with zipfile.ZipFile(root/'source.zip') as z:
        tree=git_tree(z);assert tree==(root/'SOURCE_TREE.txt').read_text().strip()
        assert z.comment.decode()==m['head']==(root/'SOURCE_COMMIT.txt').read_text().strip()
        for path,digest in m['protected'].items():assert hashlib.sha256(z.read(path)).hexdigest()==digest,path
    for label in ('array','dense','micro'):
        assert m['binaries'][label]==m['binaries'][label+'AA']
    groups=defaultdict(list);digests={};raw_count=samples=zero_samples=0
    for file in sorted(out.glob('*-r*.json')):
        match=re.fullmatch(r'(array|dense|micro)(AA)?-r(\d+)\.json',file.name)
        if not match:continue
        data=json.loads(file.read_text());label=match[1]+(match[2] or '');r=int(match[3]);assert data['label']==label
        seen=set()
        for row in data['rows']:
            assert row['round']==r and row['label']==label
            key=(label,)+tuple(row[k] for k in FIELDS);assert key not in seen;seen.add(key)
            assert len(row['ns'])==len(row['allocated'])==len(row['ms'])==len(row['gc'])==7
            assert all(math.isfinite(v) and v>=0 for v in row['ns']+row['allocated'])
            for ns,ms in zip(row['ns'],row['ms']):assert math.isclose(ns*row['iterations']/1e6,ms,rel_tol=1e-9,abs_tol=1e-9)
            if row['operation']=='prepared_reset':
                assert all(v==0 for v in row['allocated']),key;zero_samples+=7
            canonical=tuple(row[k] for k in FIELDS if k!='strategy')
            digest=(row['digest'],row['shape']['peerDigest'])
            if canonical in digests:assert digests[canonical]==digest
            digests[canonical]=digest
            groups[key].append(row);raw_count+=1;samples+=7
    aggregates=[];index={}
    for key,rows in sorted(groups.items()):
        rows.sort(key=lambda r:r['round']);assert [r['round'] for r in rows]==list(range(rounds)),key
        med=[statistics.median(r['ns']) for r in rows]
        value=dict(zip(('label',)+FIELDS,key));value.update(cpu=m['cpu'],median_ns=statistics.median(med),
            round_medians=med,bytes_per_op=statistics.median(x for r in rows for x in r['allocated']),
            digest=rows[0]['digest'],shape=rows[0]['shape'],resultBuffer=rows[0]['resultBuffer'],resultCount=rows[0]['resultCount'])
        assert all(r['resultBuffer']==value['resultBuffer'] and r['resultCount']==value['resultCount'] for r in rows)
        aggregates.append(value);index[key]=value
    def at(label,row,strategy=None):
        return index[(label,)+tuple(strategy if k=='strategy' and strategy else row[k] for k in FIELDS)]
    comparisons=[];summary=defaultdict(lambda:{'cases':0,'faster5_both_controls_every_round':0,'slower5_both_controls_every_round':0,'both_AA_within5_every_round':0})
    aa=defaultdict(lambda:{'cases':0,'within5_every_round':0,'persistent_shift5':0})
    for row in aggregates:
        label=row['label']
        if label.endswith('AA'):continue
        twin=at(label+'AA',row)
        ratios=[a/b for a,b in zip(row['round_medians'],twin['round_medians'])]
        within=all(1/1.05<=v<=1.05 for v in ratios)
        persistent=all(v>1.05 for v in ratios) or all(v<1/1.05 for v in ratios)
        key=label+'/'+row['strategy'];aa[key]['cases']+=1;aa[key]['within5_every_round']+=within;aa[key]['persistent_shift5']+=persistent
        if row['strategy']!='bulk':continue
        control=at(label,row,'loop');control_aa=at(label+'AA',row,'loop')
        assert row['resultCount']==control['resultCount'] and row['resultBuffer']==control['resultBuffer']
        # Sorted building and composition have identical allocations, not a trimmed-capacity advantage.
        if row['order']=='sorted_unique':assert row['bytes_per_op']==control['bytes_per_op'],row
        fast=all(row['round_medians'][i]<=.95*min(control['round_medians'][i],control_aa['round_medians'][i]) for i in range(rounds))
        slow=all(row['round_medians'][i]>1.05*max(control['round_medians'][i],control_aa['round_medians'][i]) for i in range(rounds))
        control_within=all(1/1.05<=a/b<=1.05 for a,b in zip(control['round_medians'],control_aa['round_medians']))
        pair={k:row[k] for k in ('cpu','label')+FIELDS if k!='strategy'}
        pair.update(loop_ns=control['median_ns'],bulk_ns=row['median_ns'],loop_bytes=control['bytes_per_op'],bulk_bytes=row['bytes_per_op'],
            ratio=row['median_ns']/control['median_ns'],faster5=fast,slower5=slow,AA_within5=within and control_within,
            blocks16=row['shape']['blocks16'],runs=row['shape']['runs'],dense_words=row['shape']['words'],input_count=row['shape']['inputCount'])
        comparisons.append(pair);key='/'.join((label,row['order'],row['operation']));g=summary[key]
        g['cases']+=1;g['faster5_both_controls_every_round']+=fast;g['slower5_both_controls_every_round']+=slow;g['both_AA_within5_every_round']+=within and control_within
    tests={}
    for path in out.glob('*-tests.json'):
        result=json.loads(path.read_text());assert result['failures']==0;tests[path.name]=result
    full=(out/'full-runtime.log').read_text();assert 'failures=0' in full
    micro=json.loads((out/'full-micro.json').read_text())
    # Stored full Micro JSON shape is kept verbatim; executable success and log are both required.
    assert 'PASS' in (out/'full-micro.log').read_text()
    codegen={}
    for label in ('array','dense','micro'):
        log=(out/(label+'-codegen.log')).read_text()
        assert 'Assembly listing for method BulkProbe:ConstructionLoop' in log
        codegen[label]=[line for line in log.splitlines() if 'Assembly listing' in line or 'Total bytes of code' in line]
    result={'head':m['head'],'tree':tree,'sdk':m['sdk'],'cpu':m['cpu'],'rows':len(aggregates),'raw_rows':raw_count,
        'timing_samples':samples,'prepared_zero_allocation_samples':zero_samples,'protected_files':len(m['protected']),
        'AA':dict(aa),'groups':dict(summary),'tests':tests,'full_runtime':full.splitlines()[-1],
        'full_micro':micro,'codegen':codegen,'source_and_raw_recomputed':True,'selector_implemented':False,'production_promoted':False,'native_unity':'not_run'}
    (out/'recheck.json').write_text(json.dumps(result,indent=2))
    (out/'Aggregates.json').write_text(json.dumps(aggregates,indent=2))
    dump_csv(out/'Paired_Comparisons.csv',comparisons)
    flat=[]
    for r in aggregates:
        item={k:v for k,v in r.items() if k not in ('round_medians','shape')}
        for i,v in enumerate(r['round_medians']):item['round'+str(i)+'_ns']=v
        item.update(r['shape']);flat.append(item)
    dump_csv(out/'All_Results.csv',flat)
    report=['# Bulk construction — '+m['cpu'],'','Measured commit: `'+m['head']+'`; compiler SDK '+m['sdk']+'.',
        '','Strict sorted-input validation and complete materialization are timed. Unordered includes scratch allocation, sorting and deduplication. Prepared reset is separately measured. Standalone Union/CopyAppend/CopyRemove kernels were NOT changed.',
        '','## Full paired screens','', '| Layout / input / operation | Cases | Faster >=5% each round | Slower >5% each round | Both A/A stable |','|---|---:|---:|---:|---:|']
    for key,g in sorted(summary.items()):report.append('| '+key+' | '+str(g['cases'])+' | '+str(g['faster5_both_controls_every_round'])+' | '+str(g['slower5_both_controls_every_round'])+' | '+str(g['both_AA_within5_every_round'])+' |')
    focus=[]
    for u,n,d in ((262144,8,'scattered'),(262144,4096,'contiguous'),(262144,4096,'clusters4'),(262144,4096,'scattered')):
        report+=['','## '+str(u)+' definitions / '+str(n)+' members / '+d,'','ns per complete operation, seed 20261002.','', '| Layout / input / operation | Repeated Add | Bulk | Loop B/op | Bulk B/op |','|---|---:|---:|---:|---:|']
        for r in comparisons:
            if (r['universe'],r['members'],r['distribution'],r['seed'])==(u,n,d,20261002) and (r['order']=='sorted_unique' or r['order']=='shuffled_unique'):
                report.append('| '+r['label']+'/'+r['order']+'/'+r['operation']+' | '+format(r['loop_ns'],'.3f')+' | '+format(r['bulk_ns'],'.3f')+' | '+str(r['loop_bytes'])+' | '+str(r['bulk_bytes'])+' |');focus.append(r)
    report+=['','## Limits','', 'No fitted Auto policy, conversion or new representation. No standalone four-metric victory claim. SDK is explicitly pinned; historical absolute timings are not merged. All samples, allocation counts and GC deltas remain. A/A covers the construction measurements but does not eliminate every layout/host bias. Three-round 5% is a screen, not statistical confidence. Linux .NET and a Standard 2.1 library hosted by .NET8 are not Unity/IL2CPP/Burst/mobile acceptance.']
    (out/'REPORT.md').write_text('\n'.join(report)+'\n')
    print('BULK_RECHECK '+json.dumps({k:v for k,v in result.items() if k not in ('tests','full_micro','codegen')}))
    print('BULK_FOCUS '+json.dumps(focus))
    print('BULK_TEST_SUMMARY '+json.dumps({k:{'assertions':v['assertions'],'failures':v['failures'],'errorBytes':v['errorBytesPerInvalidInput']} for k,v in tests.items()}))
if __name__=='__main__':main()
