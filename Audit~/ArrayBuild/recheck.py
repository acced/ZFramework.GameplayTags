#!/usr/bin/env python3
"""Second process: reconstruct archives and recompute all aggregates directly from samples."""
import argparse,hashlib,json,math,pathlib,statistics,zipfile

def tree(z):
    root={}
    for item in z.infolist():
        if item.is_dir():continue
        parts=pathlib.PurePosixPath(item.filename).parts
        if item.filename.startswith('/') or '..' in parts:raise ValueError('Unsafe source path')
        node=root
        for part in parts[:-1]:node=node.setdefault(part,{})
        if parts[-1] in node:raise ValueError('Duplicate archive path')
        content=z.read(item);st=item.external_attr>>16
        mode=b'120000' if st&0o170000==0o120000 else b'100755' if st&0o111 else b'100644'
        node[parts[-1]]=(mode,hashlib.sha1(b'blob '+str(len(content)).encode()+b'\0'+content).digest())
    def node_hash(node):
        data=b''
        for name,value in sorted(node.items(),key=lambda p:(p[0]+('/' if isinstance(p[1],dict) else '')).encode()):
            mode,digest=(b'40000',node_hash(value)) if isinstance(value,dict) else value
            data+=mode+b' '+name.encode()+b'\0'+digest
        return hashlib.sha1(b'tree '+str(len(data)).encode()+b'\0'+data).digest()
    return node_hash(root).hex()

def main():
    p=argparse.ArgumentParser();p.add_argument('--root',required=True);args=p.parse_args()
    root=pathlib.Path(args.root);out=root/'results'
    sources=json.loads((out/'sources.json').read_text())
    with zipfile.ZipFile(root/'source.zip') as source:
        assert tree(source)==sources['tree']==(root/'SOURCE_TREE.txt').read_text().strip()
        assert sources['head']==(root/'SOURCE_COMMIT.txt').read_text().strip()
        for label,reference in sources['references'].items():
            with zipfile.ZipFile(root/'references'/(label+'.zip')) as z:
                assert tree(z)==reference['tree'],label
                if label=='auto':
                    protected=[n for n in z.namelist() if not n.endswith('/') and (n.startswith(('Runtime/','Editor/','Samples~/','Tests/')) or n=='package.json')]
                    for name in protected:assert source.read(name)==z.read(name),name
    for name,digest in sources['generated'].items():assert hashlib.sha256((out/'generated'/name).read_bytes()).hexdigest()==digest,name
    raw={};raw_rows=timings=allocations=0
    for file in sorted(out.glob('*-r[0-9]*.json')):
        stage,variant,contract,r=file.stem.split('-');r=int(r[1:])
        if stage not in ('small','large'):continue
        data=json.loads(file.read_text())
        for row in data['rows']:
            assert row['status']=='measured'
            key=(stage,variant,contract,row['operation'],row['size'],row['universe'],row['distribution'])
            assert r not in raw.setdefault(key,{})
            assert len(row['ns'])==len(row['allocated_bytes'])==7
            assert all(math.isfinite(x) and x>=0 for x in row['ns'])
            raw[key][r]=row;raw_rows+=1;timings+=7
            if contract=='prepared':assert all(x==0 for x in row['allocated_bytes']);allocations+=7
    computed={}
    for key,rounds in raw.items():
        assert sorted(rounds)==[0,1,2]
        values=[statistics.median(rounds[r]['ns']) for r in range(3)]
        computed[key]=(statistics.median(values),values,statistics.median(x for row in rounds.values() for x in row['allocated_bytes']),rounds[0]['inputDigest'])
    comparison=json.loads((out/'comparison.json').read_text());assert len(comparison)==len(computed)
    groups={}
    bulk={'union','copy_append','copy_remove','union_into','copy_append_reuse','copy_remove_reuse'}
    for row in comparison:
        key=tuple(row[k] for k in ('stage','variant','contract','operation','members','definitions','distribution'))
        value= computed[key];stored=row['value']
        assert math.isclose(value[0],stored['ns'],rel_tol=1e-12)
        assert value[1]==stored['round_medians'] and value[2]==stored['bytes'] and value[3]==stored['input_digest']
        refs=[]
        for label,saved in row['references'].items():
            ref=computed[(key[0],label,*key[2:])];refs.append(ref)
            assert ref[3]==value[3] and math.isclose(ref[0],saved['ns'],rel_tol=1e-12)
        win=value[0]<min(x[0] for x in refs)
        margin=all(value[1][r]<=.95*min(x[1][r] for x in refs) for r in range(3))
        assert win==row['lower_than_controls'] and margin==row['margin_controls_every_round']
        if key[1] in ('arrays','growth') and key[3] in bulk:
            name='/'.join(key[:3]);g=groups.setdefault(name,[0,0,0]);g[0]+=1;g[1]+=int(win);g[2]+=int(margin)
    report={'head':sources['head'],'tree':sources['tree'],'machine':json.loads((out/'host.json').read_text())['machine'],
            'verified_reference_trees':sources['references'],'production_files_unchanged':len(protected),
            'recomputed_rows':len(computed),'raw_rows':raw_rows,'timing_samples':timings,'prepared_allocation_samples_at_zero':allocations,
            'summary_and_rankings_equal':True,'bulk_gates':groups,'production_promoted':False,'native_unity':'not_run'}
    (out/'independent-review.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
    lines=['# Array builder experiment — independently recomputed', '', 'Tested commit: `'+sources['head']+'`', 'Machine: '+report['machine'], '',
           'Production files unchanged; native Unity not executed. No all-workload claim.', '',
           '| Stage/candidate/contract | Cases | Lower medians vs controls | >=5% every round |','|---|---:|---:|---:|']
    for name,g in sorted(groups.items()):lines.append('| '+name+' | '+' | '.join(map(str,g))+' |')
    for stage,n,u in [('small',8,262144),('large',1024,10000)]:
        for dist in ['scattered'] if stage=='small' else ['contiguous','scattered']:
            for contract in ('fresh','prepared'):
                labels=['auto','micro','kernel','arrays','growth']
                lines+=['',f'## {stage}: {u} definitions, {n} members, {dist}, {contract}', '',
                        'Times are ns/op, median of process medians. B/op in parentheses. All costs remain inside timing.', '',
                        '| Operation | '+' | '.join(labels)+' |','|---|'+'---:|'*len(labels)]
                ops=['exact','union','copy_append','copy_remove'] if contract=='fresh' else ['exact','union_into','copy_append_reuse','copy_remove_reuse']
                for op in ops:
                    values=[computed[(stage,label,contract,op,n,u,dist)] for label in labels]
                    lines.append('| '+op+' | '+' | '.join(f'{v[0]:.3f} ({v[2]:g})' for v in values)+' |')
    (out/'RESULTS.md').write_text('\n'.join(lines)+'\n')
    print('INDEPENDENT_RECHECK '+json.dumps(report),flush=True)
    print('\n'.join(lines),flush=True)

if __name__=='__main__':main()
