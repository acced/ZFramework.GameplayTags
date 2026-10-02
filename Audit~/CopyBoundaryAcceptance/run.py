#!/usr/bin/env python3
"""Frozen PR26 readout. No new benchmark or modified library compilation."""
from pathlib import Path
import subprocess,os,sys,json,zipfile,hashlib,csv,re
HEAD='98311092b7e8892e5632c2863a391187f4367cf8'
RUN=37024028909
MAIN=(11235249996,'7c6b8bf86c65b764c450c4c3a5153f7ec5ab3e79286cba3cb0e7a8db8f01a967')
TRACES={'x64':(11235932163,'605261ea00588acac8d9b2dc32907c080937d2fad3063a937c07a7def3422568'),
        'arm64':(11235567411,'58103e852010e0295fd615f632db3f3260c1d4aae7e6c85e168e6eeab2490548')}
NAMES=('control','eager','guarded','isolated','empty','boundary')
PAIRS=(('eager','control'),('guarded','eager'),('isolated','guarded'),('empty','guarded'),('boundary','empty'),('boundary','isolated'),('boundary','guarded'),('boundary','eager'),('boundary','control'),('empty','eager'),('empty','control'))
def sha(b):return hashlib.sha256(b).hexdigest()
def extract(z,folder):
    folder=folder.resolve();folder.mkdir(parents=True,exist_ok=True)
    for name in z.namelist():assert (folder/name).resolve().is_relative_to(folder)
    z.extractall(folder)
def write_csv(path,rows):
    with path.open('w',encoding='utf-8-sig',newline='') as f:
        if rows:
            w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
def stats(rows):
    return dict(cases=len(rows),fast=sum(r['fast'] for r in rows),slow=sum(r['slow'] for r in rows),stable=sum(r['stable'] for r in rows),fastStable=sum(r['fast'] and r['stable'] for r in rows),slowStable=sum(r['slow'] and r['stable'] for r in rows))
def table(rows,keys):
    if not rows:return '(none)\n'
    text=['|'+'|'.join(keys)+'|','|'+'|'.join('---' for _ in keys)+'|']
    for row in rows:text.append('|'+'|'.join(f'{row[k]:.3f}' if isinstance(row[k],float) else str(row[k]).replace('|','/') for k in keys)+'|')
    return '\n'.join(text)+'\n'
def main():
    out=Path(sys.argv[1]).resolve();out.mkdir(parents=True,exist_ok=True)
    delivery=out/'delivery';delivery.mkdir(exist_ok=True)
    repo=os.environ['GITHUB_REPOSITORY']
    def download(pin,dest):
        aid,digest=pin
        data=subprocess.check_output(['gh','api',f'/repos/{repo}/actions/artifacts/{aid}/zip'])
        assert sha(data)==digest,(aid,'archive digest')
        dest.write_bytes(data);return data
    original=out/'main-original.zip';download(MAIN,original)
    root=out/'main'
    with zipfile.ZipFile(original) as z:extract(z,root)
    collection=json.loads((root/'COLLECTION.json').read_text())
    assert set(collection['architectures'])=={'x64','arm64'}
    trace_methods={};faults=[];tests={};counts={};aadata={};source=None
    for arch in ('x64','arm64'):
        results=root/arch/'results';m=json.loads((results/'manifest.json').read_text())
        assert m['head']==HEAD and m['rounds']==3 and not m['smoke']
        assert m['sdk']=='8.0.425'
        previous=json.loads((results/'recheck.json').read_text())
        p=subprocess.run(['python3',str(root/'source/Audit~/CopyBoundary/review.py'),'--root',str(results)],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
        (out/(arch+'-recheck.log')).write_text(p.stdout)
        assert p.returncode==0,p.stdout[-16000:]
        assert previous==json.loads((results/'recheck.json').read_text())
        assert previous=={k:v for k,v in collection['architectures'][arch].items() if k not in ('artifact','archive_sha256')}
        current=(results/'source.zip').read_bytes()
        if source is None:source=current
        else:assert source==current
        counts[arch]={k:previous[k] for k in ('aggregate_rows','raw_rows','timing_samples','zero_allocation_samples')}
        tests[arch]=previous['tests'];aadata[arch]=previous['AA']
        for name,d in previous['faults'].items():
            invalid=len(d['invalid'])
            assert d['cases']==d['injected']==3931 and d['normal']==3873
            if name=='control':assert invalid==3093 and d['minimalBad']==1
            else:assert invalid==0 and d['minimalBad']==0
            faults.append(dict(architecture=arch,variant=name,normal=d['normal'],injected=d['injected'],invalid=invalid))
        for name in NAMES:
            core=(results/'generated'/name/'DirectArraySet.cs').read_bytes()
            target=delivery/'generated'/name/'DirectArraySet.cs';target.parent.mkdir(parents=True,exist_ok=True)
            if arch=='x64':target.write_bytes(core);(target.parent/'BulkBuilders.cs').write_bytes((results/'generated'/name/'BulkBuilders.cs').read_bytes())
            else:assert target.read_bytes()==core
        archive=out/(arch+'-trace-original.zip');download(TRACES[arch],archive)
        trace_folder=delivery/('trace-'+arch)
        with zipfile.ZipFile(archive) as z:extract(z,trace_folder)
        trace=json.loads((trace_folder/'TRACE.json').read_text())
        assert trace['head']==HEAD and trace['architecture']==arch and trace['new_performance_samples']==0 and not trace['recompiled']
        trace_methods[arch]={}
        for record in trace['records']:
            name=record['name'];assert name in NAMES
            job=next(j for j in m['jobs'] if j['label']==name and j['stage']=='copy')
            assert record['binary_sha256']==job['sha256']
            assert record['core_sha256']==sha((results/'generated'/name/'DirectArraySet.cs').read_bytes())
            text=(trace_folder/(name+'.asm')).read_text();methods=[]
            for method,body,size in re.findall(r'Assembly listing for method ([^\n]+)\n(.*?); Total bytes of code (\d+)',text,re.S):
                methods.append(dict(method=method,bytes=int(size),calls=[line.strip() for line in body.splitlines() if re.search(r'\b(call|bl)\s',line)]))
            assert any('CopyCore(' in r['method'] for r in methods)
            if name in ('guarded','isolated','empty','boundary'):assert any('CopyDenseGrowing(' in r['method'] for r in methods)
            trace_methods[arch][name]=methods
    (delivery/'Source_Tested.zip').write_bytes(source)
    results_index={};rows=[]
    with (root/'All_Architectures.csv').open(encoding='utf-8-sig') as f:
        for item in csv.DictReader(f):
            r=dict(item);r.update(ns=float(r['ns']),bytes=float(r['bytes']),times=json.loads(r['round_medians']),members=int(r['members']),universe=int(r['universe']))
            key=(r['architecture'],r['stage'],r['label'],r['operation'],r['output'],r['shape'])
            assert key not in results_index;results_index[key]=r;rows.append(r)
    comparisons=[]
    def stable(a,b):return all(.95<=x/y<=1.05 for x,y in zip(a['times'],b['times']))
    for r in rows:
        if r['label'].endswith('AA'):continue
        for candidate,control in PAIRS:
            if r['label']!=candidate:continue
            def get(name):return results_index[(r['architecture'],r['stage'],name,r['operation'],r['output'],r['shape'])]
            ca,b,ba=get(candidate+'AA'),get(control),get(control+'AA')
            for x in (ca,b,ba):assert (x['bytes'],x['resultCount'],x['resultBuffer'])==(r['bytes'],r['resultCount'],r['resultBuffer'])
            fast=all(max(x,xa)<.95*min(y,ya) for x,xa,y,ya in zip(r['times'],ca['times'],b['times'],ba['times']))
            slow=all(min(x,xa)>1.05*max(y,ya) for x,xa,y,ya in zip(r['times'],ca['times'],b['times'],ba['times']))
            comparisons.append(dict(architecture=r['architecture'],candidate=candidate,control=control,stage=r['stage'],operation=r['operation'],output=r['output'],universe=r['universe'],members=r['members'],distribution=r['distribution'],left=r['left'],right=r['right'],baseline_ns=b['ns'],candidate_ns=r['ns'],ratio=r['ns']/b['ns'],bytes=r['bytes'],fast=fast,slow=slow,stable=stable(r,ca) and stable(b,ba),shape=r['shape']))
    # Existing comparisons must exactly match independently rederived screens.
    indexed={(r['architecture'],r['candidate'],r['control'],r['stage'],r['operation'],r['output'],r['shape']):r for r in comparisons}
    with (root/'All_Comparisons.csv').open(encoding='utf-8-sig') as f:
        for row in csv.DictReader(f):
            r=indexed[tuple(row[k] for k in ('architecture','candidate','control','stage','operation','output','shape'))]
            assert (r['fast'],r['slow'],r['stable'])==(row['faster5']=='True',row['slower5']=='True',row['AAstable']=='True')
            assert r['candidate_ns']==float(row['candidate_ns']) and r['baseline_ns']==float(row['baseline_ns'])
    groups=[]
    for arch in ('x64','arm64'):
        for candidate,control in PAIRS:
            chosen=[r for r in comparisons if (r['architecture'],r['candidate'],r['control'])==(arch,candidate,control)]
            def group(r):
                if r['stage']=='empty':return 'dirty' if r['distribution']=='dirty-target' else 'already_empty'
                if r['members']==0:return 'empty_source'
                if r['stage']=='copy':return 'fresh' if r['operation'] in ('fresh_copy','union_empty_fresh') else 'prepared'
                if r['operation']=='copy_from' and r['left']=='Dense' and r['output']=='Micro':return 'direct_nonempty_copy'
                return 'other_including_indirect_copy'
            for stage,label in sorted({(r['stage'],group(r)) for r in chosen}):
                part=[r for r in chosen if r['stage']==stage and group(r)==label]
                groups.append(dict(architecture=arch,candidate=candidate,control=control,stage=stage,group=label,**stats(part)))
    focus=[]
    for r in rows:
        if r['label']!='control':continue
        selected=(r['stage']=='copy' and r['universe']==262144 and r['operation'] in ('fresh_copy','prepared_copy') and ((r['members'] in (0,8) and r['distribution']=='scattered') or r['members']==4096)) or (r['stage']=='empty' and r['universe']==262144 and r['operation']=='empty_copy')
        if not selected:continue
        item={k:r[k] for k in ('architecture','stage','operation','universe','members','distribution','left','output','bytes')}
        for name in NAMES:item[name]=results_index[(r['architecture'],r['stage'],name,r['operation'],r['output'],r['shape'])]['ns']
        focus.append(item)
    regressions=sorted([r for r in comparisons if r['slow']],key=lambda r:r['ratio'],reverse=True)
    write_csv(delivery/'Groups.csv',groups);write_csv(delivery/'Focus_ns.csv',focus);write_csv(delivery/'All_Comparisons.csv',comparisons)
    (delivery/'All_Architectures.csv').write_bytes((root/'All_Architectures.csv').read_bytes())
    (delivery/'All_Regressions.json').write_text(json.dumps(regressions,indent=2))
    acceptance=dict(head=HEAD,run=RUN,main_artifact=MAIN[0],main_sha256=MAIN[1],trace_pins=TRACES,new_performance_samples=0,repeated_raw_review_equal=True,trace_binary_matches_both_timed_architectures=True,posthoc_extra_comparisons=['empty/eager','empty/control'],counts=counts,tests=tests,faults=faults,groups=groups,focus_ns=focus,codegen=trace_methods,AA=aadata,production_promoted=False,nativeUnity='not_run')
    (delivery/'Acceptance.json').write_text(json.dumps(acceptance,indent=2))
    report='# PR26：空源分支与调用边界——冻结证据独立验收\n\n'
    report+=f'实际计时提交 `{HEAD}`，主运行 {RUN}。本脚本不生成新性能样本。重新下载并执行原归档复核；两平台生成源码相同；额外机器码来自相同原二进制、不同同架构宿主，不冒充同一CPU地址布局。\n\n'
    report+='增加empty/eager、empty/control事后对照用于判断是否值得保留额外NoInlining，原始样本没有改变，也不重新拟合阈值。三轮5%含A/A最不利组合只是筛选，不是置信区间。\n\n## 完整分组\n'+table(groups,['architecture','candidate','control','stage','group','cases','fast','slow','stable','fastStable','slowStable'])
    report+='\n## 固定公开操作（ns）\n'+table(focus,['architecture','stage','operation','members','distribution','left','output','control','eager','guarded','isolated','empty','boundary','bytes'])
    report+='\n## 注入增长失败\n'+table(faults,['architecture','variant','normal','injected','invalid'])
    report+='\n每个候选保留旧对照失败与修正通过。注入覆盖复制/空源/同输入捷径，不是所有generic混合Union故障、真实内存压力或并发观察保证。完整正确性次数见Acceptance.json.tests。\n\n## 去留边界\n只在相同输入/生命周期内部比较；保留全部回退与A/A。公共registry/null检查仍在私有空源分支之前；脏Dense必须实际清零，没有借用空输入缓冲或缩容量。新实验只改CopyCore空返回和助手内联属性，不能把原四项全性能或Unity验收视为已完成。源码ZIP无Git历史，同名候选分别构建。生产保持不变。\n'
    (delivery/'验收结果.md').write_text(report)
    print('BOUNDARY_COUNTS '+json.dumps(counts),flush=True)
    print('BOUNDARY_FAULTS '+json.dumps(faults),flush=True)
    # Keep CI console compact; complete tables remain in archived files.
    mainpairs={('isolated','guarded'),('empty','guarded'),('boundary','empty'),('empty','eager'),('empty','control')}
    print('BOUNDARY_GROUPS '+json.dumps([r for r in groups if (r['candidate'],r['control']) in mainpairs]),flush=True)
    print('BOUNDARY_FOCUS '+json.dumps(focus),flush=True)
    brief={arch:{name:[dict(method=r['method'],bytes=r['bytes'],calls=r['calls']) for r in trace_methods[arch][name] if any(k in r['method'] for k in ('CopyCore(','CopyDenseToMicro(','CopyDenseGrowing('))] for name in NAMES} for arch in ('x64','arm64')}
    print('BOUNDARY_CODEGEN '+json.dumps(brief),flush=True)
    for candidate,control in (('empty','guarded'),('empty','eager'),('boundary','empty')):
        worst=[r for r in regressions if (r['candidate'],r['control'])==(candidate,control)][:10]
        print('BOUNDARY_WORST '+json.dumps([{k:r[k] for k in ('architecture','candidate','control','stage','operation','universe','members','distribution','left','right','output','baseline_ns','candidate_ns','ratio','stable')} for r in worst]),flush=True)
    print('BOUNDARY_ACCEPTANCE_OK '+HEAD,flush=True)
if __name__=='__main__':main()
