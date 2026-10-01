#!/usr/bin/env python3
"""Independent post-run recheck. Never trusts summary medians or rewrites raw samples."""
import argparse,collections,csv,hashlib,io,json,math,pathlib,shutil,statistics,zipfile
EXPECTED_COMMIT='b46ef771ac832135b6cc8336303aabaa49e7523f'
EXPECTED_TREE='d60f42a6f1902a5b9c92006aef554b4fe743df73'
PIN_TREES={'auto':'f0131462eb38065f833f898b7123e41dda9e0978','micro':'f8164f05b6b3c3f0519f72c6c9cf35ff190dacc0','triad':'99e1b706b3162737afbcdc8c8ab847ea396337fc'}
KEYS=('stage','variant','contract','operation','members','definitions','distribution')
PRIMARY={'exact','union','copy_append','copy_remove','union_into','copy_append_reuse','copy_remove_reuse'}

def tree(z):
    root={}
    for entry in z.infolist():
        if entry.is_dir():continue
        p=pathlib.PurePosixPath(entry.filename)
        if p.is_absolute() or '..' in p.parts:raise ValueError('Unsafe ZIP path')
        node=root
        for part in p.parts[:-1]:node=node.setdefault(part,{})
        data=z.read(entry);mode=b'120000' if ((entry.external_attr>>16)&0o170000)==0o120000 else b'100755' if (entry.external_attr>>16)&0o111 else b'100644'
        node[p.name]=(mode,hashlib.sha1(b'blob '+str(len(data)).encode()+b'\0'+data).digest())
    def encode(node):
        payload=b''
        for name,item in sorted(node.items(),key=lambda p:(p[0]+('/' if isinstance(p[1],dict) else '')).encode()):
            mode,digest=(b'40000',encode(item)) if isinstance(item,dict) else item
            payload+=mode+b' '+name.encode()+b'\0'+digest
        return hashlib.sha1(b'tree '+str(len(payload)).encode()+b'\0'+payload).digest()
    return encode(root).hex()

def mdtable(headers,rows):
    return '\n'.join(['| '+' | '.join(headers)+' |','| '+' | '.join(['---']*len(headers))+' |']+['| '+' | '.join(str(v) for v in row)+' |' for row in rows])

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--input',required=True);ap.add_argument('--output',required=True);a=ap.parse_args()
    base=pathlib.Path(a.input);out=pathlib.Path(a.output);out.mkdir(parents=True,exist_ok=True)
    folders=sorted(p.parent for p in base.glob('*/SOURCE_COMMIT.txt'))
    assert len(folders)==2,folders
    reports=[];checks=[];generated={};allscore=[]
    for folder in folders:
        assert (folder/'SOURCE_COMMIT.txt').read_text().strip()==EXPECTED_COMMIT
        assert (folder/'SOURCE_TREE.txt').read_text().strip()==EXPECTED_TREE
        result=folder/'fusion';env=json.loads((result/'fusion-tests.json').read_text());arch=env['architecture']
        assert arch in ('X64','Arm64') and arch not in [c['architecture'] for c in checks]
        with zipfile.ZipFile(folder/'source.zip') as z:
            assert tree(z)==EXPECTED_TREE
            source=z.read('Audit~/Fusion/DenseFusion.cs')
            with zipfile.ZipFile(folder/'references/auto.zip') as auto:
                protected=[n for n in auto.namelist() if not n.endswith('/') and (n.startswith(('Runtime/','Editor/','Tests/','Samples~/')) or n=='package.json')]
                for n in protected:assert z.read(n)==auto.read(n),n
            if not (out/'TestedSource_b46ef77.zip').exists():shutil.copyfile(folder/'source.zip',out/'TestedSource_b46ef77.zip')
            elif (out/'TestedSource_b46ef77.zip').read_bytes()!=(folder/'source.zip').read_bytes():
                # Git ZIP metadata could differ; actual tree equality above is the source contract.
                pass
        for name,expected in PIN_TREES.items():
            with zipfile.ZipFile(folder/('references/'+name+'.zip')) as z:assert tree(z)==expected
        current={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (result/'generated').glob('Fused*Set.cs')}
        if generated:assert generated==current
        generated=current
        candidate_dir=out/'IntegratedCandidate';candidate_dir.mkdir(exist_ok=True)
        for n in current:shutil.copyfile(result/'generated'/n,candidate_dir/n)
        (candidate_dir/'DenseFusion.cs').write_bytes(source)
        for n in ('fusion-tests.json','fusion-tests-nohw.json','FusedKernelSet-tests.json','FusedKernelSet-tests-nohw.json','FusedBitmapSet-tests.json','FusedBitmapSet-tests-nohw.json','portable-full-tests.json'):
            t=json.loads((result/n).read_text());assert t['failures']==0,(n,t)
        groups=collections.defaultdict(dict);rawrows=samples=zeros=0;shape_digests={}
        for f in sorted(result.glob('*-r[012].json')):
            if not f.name.startswith(('small-','large-')):continue
            stage,variant,contract,r=f.stem.split('-');r=int(r[1:]);payload=json.loads(f.read_text())
            for v in payload['rows']:
                assert v['status']=='measured' and len(v['ns'])==7 and len(v['allocated_bytes'])==7
                assert all(math.isfinite(x) and x>=0 for x in v['ns'])
                k=(stage,variant,contract,v['operation'],v['size'],v['universe'],v['distribution'])
                assert r not in groups[k];groups[k][r]=v
                shape=(stage,v['size'],v['universe'],v['distribution'])
                if shape in shape_digests:assert shape_digests[shape]==v['inputDigest']
                shape_digests[shape]=v['inputDigest'];rawrows+=1;samples+=7
                if contract=='prepared':assert all(b==0 for b in v['allocated_bytes']);zeros+=7
        rows={}
        for k,v in groups.items():
            assert set(v)=={0,1,2};med=[statistics.median(v[r]['ns']) for r in range(3)]
            rows[k]={'ns':statistics.median(med),'round_medians':med,'bytes':statistics.median(b for r in range(3) for b in v[r]['allocated_bytes'])}
        comparisons=json.loads((result/'comparison.json').read_text());assert len(rows)==len(comparisons)
        score=collections.defaultdict(lambda:[0,0,0])
        for c in comparisons:
            k=tuple(c[n] for n in KEYS);v=rows[k]
            assert math.isclose(v['ns'],c['value']['ns'],rel_tol=1e-12) and v['bytes']==c['value']['bytes']
            assert all(math.isclose(x,y,rel_tol=1e-12) for x,y in zip(v['round_medians'],c['value']['round_medians']))
            controls=['auto','micro'] if k[0]=='small' else ['auto_dense','micro_dense','previous_avx']
            refs=[rows[(k[0],label)+k[2:]] for label in controls]
            lower=v['ns']<min(r['ns'] for r in refs)
            margin=all(v['round_medians'][i]<=.95*min(r['round_medians'][i] for r in refs) for i in range(3))
            assert lower==c['lower_than_controls'] and margin==c['margin_every_round']
            if k[3] in PRIMARY:
                s=score[(k[0],k[1],k[2],'query' if k[3]=='exact' else 'bulk')];s[0]+=1;s[1]+=lower;s[2]+=margin
        for k,v in score.items():allscore.append(dict(zip(('stage','variant','contract','category'),k),architecture=arch,cases=v[0],lower_medians=v[1],every_round_margin=v[2]))
        with (out/(arch+'-recomputed.csv')).open('w',newline='') as f:
            wr=csv.writer(f);wr.writerow([*KEYS,'ns','bytes','round0','round1','round2'])
            for k,v in sorted(rows.items()):wr.writerow([*k,v['ns'],v['bytes'],*v['round_medians']])
        diag=collections.defaultdict(list)
        for r in range(3):
            for c in json.loads((result/('common-r'+str(r)+'.json')).read_text())['rows']:
                diag[tuple(c[n] for n in ('variant','operation','distribution','protocol'))].append(c)
        declared=json.loads((result/'common-summary.json').read_text());assert len(declared)==len(diag)
        for c in declared:
            vs=diag[tuple(c[n] for n in ('variant','operation','distribution','protocol'))];assert len(vs)==3
            assert math.isclose(statistics.median(statistics.median(v['ns']) for v in vs),c['ns'],rel_tol=1e-12)
            assert c['bytes']==statistics.median(b for v in vs for b in v['allocated_bytes'])
            for g in ('gen0','gen1','gen2'):assert c['gc_collections'][g]==sum(sum(v[g]) for v in vs)
        checks.append({'architecture':arch,'runtime':env['runtime'],'backend':env['backend'],'source_tree':EXPECTED_TREE,'protected_files':len(protected),'rows_recomputed':len(rows),'raw_rows':rawrows,'timing_samples':samples,'zero_prepared_samples':zeros,'diagnostic_cases':len(diag),'test_assertions':env['assertions'],'test_failures':0})
        header='## '+arch+' / '+env['runtime']+' / '+env['backend']+'\n\n'
        header+='同一架构的同一次测量；单位 ns/op。三轮，每轮七个样本，保留全部原始数据。\n\n'
        small=[]
        for op in ('exact','union','copy_append','copy_remove'):
            vals=[rows[('small',v,'fresh',op,8,262144,'scattered')] for v in ('auto','micro','simd','integrated')]
            small.append([op,*[f"{v['ns']:.3f}" for v in vals]])
        header+='### 262,144 个显式定义 / 8 个分散成员 / 新建结果\n\n'+mdtable(['operation','Auto','Micro','SIMD-only','Integrated'],small)+'\n\n'
        large=[]
        for dist in ('contiguous','scattered'):
            for op in ('union','copy_append','copy_remove'):
                vals=[rows[('large',v,'fresh',op,1024,10000,dist)] for v in ('auto_dense','micro_dense','previous_avx','kernel_only','integrated_dense')]
                large.append([dist,op,*[f"{v['ns']:.3f}" for v in vals]])
        header+='### 10,000 定义 / 1,024 成员 / 新建结果（Dense 强制模式）\n\n'+mdtable(['distribution','operation','Auto Dense','Micro Dense','Previous vector','New kernel','Integrated'],large)+'\n\n'
        selected=[]
        for (stage,var,contract,category),v in score.items():
            if var in ('kernel_only','integrated_dense','integrated'):selected.append([stage,var,contract,category,*v])
        header+='### 分操作的门槛\n\n'+mdtable(['stage','variant','contract','category','cases','lower medians','5% every round'],selected)+'\n\n'
        drows=[]
        for c in declared:
            if c['operation']=='union_fresh':drows.append([c['variant'],c['distribution'],c['protocol'],f"{c['ns']:.3f}",c['bytes'],c['gc_collections']['gen0'],c['gc_collections']['gen1'],c['gc_collections']['gen2']])
        header+='### 共同调用器诊断（不同测试程序，不与上表混用或相减）\n\n'+mdtable(['variant','distribution','protocol','ns','B/op','Gen0','Gen1','Gen2'],drows)+'\n\n'
        markers=json.loads((result/'instruction-markers.json').read_text())
        header+='反汇编指令标记：`'+json.dumps(markers)+'`。\n\n'
        reports.append(header)
        shutil.copytree(folder,out/'raw'/arch,dirs_exist_ok=True)
    (out/'IndependentRecheck.json').write_text(json.dumps({'measured_commit':EXPECTED_COMMIT,'generated_sources':generated,'hosts':checks,'source_and_raw_recomputed':True,'production_promoted':False,'native_unity':'not_run'},indent=2))
    with (out/'GatesByOperation.csv').open('w',newline='') as f:
        wr=csv.DictWriter(f,fieldnames=list(allscore[0]));wr.writeheader();wr.writerows(allscore)
    intro='# SIMD + Dense 后续重构：双架构验证\n\n测量提交：`'+EXPECTED_COMMIT+'`。这是已实现的整合候选，**不是生产 Runtime 替换版**。\n\n'
    intro+='保留原 Micro 有序成员位图与 SIMD 查询，新增专门 OR/AND/AND-NOT 循环、循环外 AVX2 常量、双累加器、ARM64 NEON 计数及 Standard 2.1 标量后端。比较 kernel-only 与增加独立 Dense Union 快路径的整合版。构造、复制、写出、Count 和分配均计入，预分配合同另外测。没有恢复整数成员列表，没有对象池或写时复制。\n\n'
    intro+='x64 和 ARM64 分开计量；这些是 Linux 上实际执行的 .NET 程序，不是 Unity、Android/iOS IL2CPP 或 Burst。Standard 2.1 标量库已单独编译并运行原完整语义套件；为了测试内部配置入口，友元声明只注入临时测试库，不改变生产公开 API。\n\n'
    intro+='每架构：Dense 内核 4,864,106 次断言、硬件禁用后同套再次通过；两个整合变体各通过原 5,392,728 次断言并在无硬件指令下重跑；独立 Standard 2.1 库也运行同一套。重复执行不算新增独立覆盖，断言数不等于案例数。\n\n'
    intro+='原有矩阵：小集合0/1/7/8/9/31/32/33/127/128/129成员，大集合128/1024/4096成员；三种注册表规模、连续/分散分布。小集合对照Auto/Micro；大集合对照强制Dense的Auto、Micro和上一版向量内核。没有重跑全部历史外部库，不能宣称全库排行榜第一。5%逐轮规则是筛选规则，不是统计置信区间。\n\n'
    (out/'验证结果.md').write_text(intro+'\n'.join(reports)+'## 审核边界\n\n请按完整CSV审核未领先项；不得按行选择不同配置的最佳结果拼成一个实现。共同调用器、GC准备协议变化仅用于定位，不能从完整操作耗时中扣除分配或GC。原告警能否稳定重现、额外构造分支是否值得保留，应以这里的对照和后续更长测量判断。无自动合并或发布。\n',encoding='utf-8')
    print('INDEPENDENT_RECHECK '+json.dumps(checks),flush=True)
    print('OPERATION_GATES '+json.dumps(allscore),flush=True)
    print(intro+'\n'.join(reports),flush=True)
if __name__=='__main__':main()
