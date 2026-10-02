#!/usr/bin/env python3
"""Finalize completed evidence; never create new timing samples or mix runs."""
from pathlib import Path
import os,sys,json,subprocess,hashlib,zipfile,io,csv,re,shutil
out=Path(sys.argv[1]).resolve();out.mkdir(parents=True,exist_ok=True)
work=out/'work';work.mkdir(exist_ok=True)
small=out/'deliverable';small.mkdir(exist_ok=True)
repo=os.environ['GITHUB_REPOSITORY'];head='0716f38a9b00170ee7228a23e036474979140d21';run=37001986207
api=lambda p:subprocess.check_output(['gh','api',p])
def get(run,name,target,expected=None):
    status=json.loads(api(f'/repos/{repo}/actions/runs/{run}'));assert status['status']=='completed' and status['conclusion']=='success'
    rows=json.loads(api(f'/repos/{repo}/actions/runs/{run}/artifacts'))['artifacts']
    a=[a for a in rows if a['name']==name and not a['expired']];assert len(a)==1;a=a[0]
    data=api(f'/repos/{repo}/actions/artifacts/{a["id"]}/zip');h=hashlib.sha256(data).hexdigest();assert a['digest']=='sha256:'+h
    if expected:assert h==expected
    target.write_bytes(data)
    return dict(run=run,artifact=a['id'],sha256=h,bytes=len(data)),zipfile.ZipFile(io.BytesIO(data))
def extract(z,p):
    p=p.resolve();p.mkdir(parents=True,exist_ok=True)
    for n in z.namelist():assert (p/n).resolve()==p or p in (p/n).resolve().parents
    z.extractall(p)
def item(z,suffix):
    names=[n for n in z.namelist() if n==suffix or n.endswith('/'+suffix)];assert len(names)==1,(suffix,names)
    return z.read(names[0])
def readcsv(p):
    with p.open(encoding='utf-8-sig',newline='') as f:return list(csv.DictReader(f))
def savecsv(p,rows):
    with p.open('w',encoding='utf-8-sig',newline='') as f:
        writer=csv.DictWriter(f,fieldnames=list(rows[0]));writer.writeheader();writer.writerows(rows)
def table(rows,keys):
    return '\n'.join(['|'+'|'.join(keys)+'|','|'+'|'.join('---' for _ in keys)+'|']+['|'+'|'.join(str(r[k]) for k in keys)+'|' for r in rows])
meta={}
meta['primary'],primary=get(36999746747,'dense-cache-reviewed-36999746747',work/'primary-original.zip','756502b52381929bd4282f2f21c5d263eeaff156269ee4e4aff59e694bb43a75')
meta['primary_readout'],pz=get(37000421022,'dense-cache-readout-37000421022',work/'primary-readout.zip','cf9669e566744f46147bd6c6a959eae04c2039953e9aace5af7ffc40f9828552')
primary_report=json.loads(item(pz,'Readout.json'))
(small/'Primary_Readout.json').write_text(json.dumps(primary_report,indent=2))
(small/'主实验验收.md').write_bytes(item(pz,'验收报告.md'))
(small/'Source_Primary_3854fda.zip').write_bytes(primary.read('Source_Tested.zip'))
meta['unequal_readout'],uz=get(37001167349,'dense-cache-unequal-reviewed-37001167349',work/'unequal-reviewed-original.zip')
unequal=json.loads(item(uz,'UNEQUAL_READOUT.json'))
(small/'Unequal_Readout.json').write_text(json.dumps(unequal,indent=2))
(small/'不等规模验收.md').write_bytes(item(uz,'不等规模验收.md'))
meta['work'],wz=get(37000083091,'dense-cache-work-37000083091',work/'counter-original.zip','a62d2b4cb7d6a47ef5d21385997f5255256468711d91e18216aa9d7f6f7cc934')
(small/'WorkCounts.json').write_bytes(item(wz,'WORK_COUNTS.json'))
checks={};allrows=[];comparisons=[];codegen={};stacks={};reference=None
for arch in ('x64','arm64'):
    meta['delta_'+arch],z=get(run,f'dense-cache-delta-{arch}-{run}',work/(arch+'-delta-original.zip'))
    extract(z,work/arch);r=work/arch/'results';s=(r/'source.zip').read_bytes()
    if reference is None:
        reference=s
        with zipfile.ZipFile(io.BytesIO(s)) as src:extract(src,work/'source')
    else:assert s==reference
    before=json.loads((r/'recheck.json').read_text());assert before['head']==head and not before['smoke']
    p=subprocess.run(['python3',str(work/'source/Audit~/DenseCacheDelta/review.py'),'--root',str(r)],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
    (small/(arch+'-delta-independent-review.log')).write_text(p.stdout);assert p.returncode==0,p.stdout[-5000:]
    assert json.loads((r/'recheck.json').read_text())==before
    checks[arch]=before
    for n in ('cache32','monotone'):
        assert (r/f'generated/{n}/DirectArraySet.cs').read_bytes()==primary.read(f'{arch}/results/generated/{n}/DirectArraySet.cs')
    source=r/'generated/delta32';dest=small/'Generated_Delta32';dest.mkdir(exist_ok=True)
    for filename in ('DirectArraySet.cs','BulkBuilders.cs'):
        contents=(source/filename).read_bytes()
        if (dest/filename).exists():assert (dest/filename).read_bytes()==contents
        (dest/filename).write_bytes(contents)
    for row in readcsv(r/'All_Results.csv'):
        shape=json.loads(row['shape']);row.update(architecture=arch,sourceMembers=shape.get('rightMembers',shape['n']),fixture='unequal' if shape['dist'].startswith(('prefix-','interior-')) or shape['n'] in (128,4096) else 'boundary')
        allrows.append(row)
    for row in readcsv(r/'Comparisons.csv'):
        shape=json.loads(row['shape'])
        row.update(architecture=arch,targetMembers=shape['n'],sourceMembers=shape.get('rightMembers',shape['n']),fixture='unequal' if shape['dist'].startswith(('prefix-','interior-')) or shape['n'] in (128,4096) else 'boundary')
        for k in ('faster5','slower5','AAstable'):row[k]=row[k]=='True'
        for k in ('baseline_ns','candidate_ns','ratio','bytes'):row[k]=float(row[k])
        comparisons.append(row)
    codegen[arch]={}
    for log in r.glob('*-codegen.log'):
        text=log.read_text();parts=text.split('; Assembly listing for method ');methods=[]
        for part in parts[1:]:
            m=re.search(r'Total bytes of code (\d+)',part)
            if m:methods.append(dict(method=part.splitlines()[0],bytes=int(m[1])))
            if part.startswith('GameplayTags.Experiments.DirectArraySet:AppendDenseSmall('):
                (small/(arch+'-'+log.stem+'-small-helper.txt')).write_text('; Assembly listing for method '+part)
                if log.name=='delta32-codegen.log':stacks[arch]=[line for line in part.splitlines() if re.search(r'\b(sub|add|push|pop|stp|ldp)\b',line) and ('sp' in line or 'push' in line or 'pop' in line)][:28]
        codegen[arch][log.name]=methods
(small/'Source_Delta_0716f38.zip').write_bytes(reference)
savecsv(small/'Delta_All_Results.csv',allrows);savecsv(small/'Delta_All_Comparisons.csv',comparisons)
def stat(rows):return dict(cases=len(rows),fast=sum(r['faster5'] for r in rows),slow=sum(r['slower5'] for r in rows),AAstable=sum(r['AAstable'] for r in rows),fastStable=sum(r['faster5'] and r['AAstable'] for r in rows),slowStable=sum(r['slower5'] and r['AAstable'] for r in rows))
groups=[]
for arch in ('x64','arm64'):
    for control in ('cache32','monotone'):
        for fixture in ('boundary','unequal'):
            for eligible in (True,False):
                rows=[r for r in comparisons if r['architecture']==arch and r['candidate']=='delta32' and r['control']==control and r['fixture']==fixture and (r['sourceMembers']<=32)==eligible]
                groups.append(dict(architecture=arch,control=control,fixture=fixture,group='cache' if eligible else 'fallback',**stat(rows)))
focus=[]
for r in comparisons:
    if r['candidate']!='delta32' or int(r['universe'])!=262144 or r['operation']!='copy_append':continue
    s=json.loads(r['shape'])
    keep=r['fixture']=='boundary' and r['targetMembers'] in (8,31,32,33) and s['dist']=='scattered'
    keep|=r['fixture']=='unequal' and r['targetMembers']==4096 and r['sourceMembers'] in (1,8,32) and s['dist'] in ('prefix-subset','prefix-newbits','interior-subset','tail')
    if keep:focus.append({k:r[k] for k in ('architecture','control','fixture','targetMembers','sourceMembers','distribution','baseline_ns','candidate_ns','bytes','faster5','slower5','AAstable')})
worst=[]
for arch in ('x64','arm64'):
    for control in ('cache32','monotone'):
        rows=[r for r in comparisons if r['architecture']==arch and r['candidate']=='delta32' and r['control']==control and r['slower5']]
        worst.extend(sorted(rows,key=lambda r:r['ratio'],reverse=True)[:8])
result=dict(head=head,run=run,metadata=meta,checks=checks,groups=groups,focus=focus,regressions=[r for r in comparisons if r['slower5']],codegen=codegen,stack=stacks,controls_equal_primary=True,original_sources_equal_across_architectures=True,primary_full_public_performance_retested_for_delta=False,nativeUnity='not_run',production_promoted=False)
(small/'Delta_Readout.json').write_text(json.dumps(result,indent=2))
primarycounts={a:{k:c[k] for k in ('head','tree','aggregate_rows','raw_rows','timing_samples','zero_allocation_samples')} for a,c in primary_report['checks'].items()}
text=['# PR22 有界缓存与缺失记录修正：最终证据索引','', '三批计时分开：主实验3854fda5，数量不等输入90d6656a，修正版0716f38a。源码/结果不混用，不把不同运行最快项拼成赢家。','',
'## 完成范围','', '原cache32有完整公开矩阵；delta32已通过完整继承正确性，但性能只验证192缓存边界+128不等规模。没有复测delta32全2520公开操作、192双方准备或大型源4096成员；因此不能宣布修正版全面验收。公共Query/克隆/Remove和迭代器未修改，真实性能仍需要整包验证。','',
'## 修正了什么','', '相同键在首遍原位OR并累计原交集，只缓存缺失键；没有缺失记录时不做反向归并。最后缺失键之后的旧尾段一次移动，剩余前缀与缺失键反向归并。相等键分支不再需要，因为缓存键在旧目标中不存在。原容量阶梯/准确Count/独立所有权/别名保持。NoInlining和128B栈载荷不变，不新增集合字段。','',
'## 修正版分组结果','', '快慢是每轮至少5%且含同二进制A/A最不利组合的筛选，不是置信区间；eligible按源Count<=32。未通过门槛不代表等速，全部原始样本与不利结果保留。','',table(groups,['architecture','control','fixture','group','cases','fast','slow','AAstable','fastStable','slowStable']),'',
'## 修正版目标读数（ns/完整克隆后追加）','',table(focus,['architecture','control','fixture','targetMembers','sourceMembers','distribution','baseline_ns','candidate_ns','bytes','faster5','slower5','AAstable']),'',
'## 持续不利结果','',table(worst,['architecture','control','fixture','universe','targetMembers','sourceMembers','distribution','operation','baseline_ns','candidate_ns','ratio','AAstable']),'',
'## 正确性和证据','', '逐架构逐候选测试量含循环/重复配置，不是独立案例数。复核重新执行归档内review并要求结构化结果完全一致；两个控制核心必须与主实验逐字节一致。所有原始ZIP、全矩阵CSV和源码归档完整保留。','']
for arch,c in checks.items():
    text.append(f'{arch}: {c["aggregate_rows"]}汇总，{c["raw_rows"]}原始行，{c["timing_samples"]}耗时样本，{c["zero_allocation_samples"]}零分配样本。')
    for n,t in c['tests'].items():text.append(f'- {n}: {t.get("assertions")} assertions, failures={t["failures"]}; cache={t.get("cacheTests")}')
text+=['','## 实际方法代码与栈','', '128B只指记录载荷，实际frame保存寄存器和局部变量另计。主缓存x64自身栈指针降低216B（不含调用方及返回地址），ARM64自身frame降低240B；修正版对应反汇编如下，不能直接套用主版本数字。','', '```json',json.dumps(dict(codegen=codegen,stack=stacks),indent=2),'```','',
'## 主版本和反例来源','', '主实验缓存的8分散目标相对stream降低耗时，但与单调单遍基本接近；完整边界仍有同键新位回退，32/33fallback突变；数量不等补测确认大目标前缀写回成本。详见主实验验收.md、不等规模验收.md及其JSON。x64主实验A/A普遍不稳定，不能宣称那张表获得稳定全平台收益。','', '```json',json.dumps(primarycounts,indent=2),'```','',
'首次6be01622来源保护断言失败后，只修正不参与构建的旧direct方法比对范围；主计时3854fda没有改缓存算法。之后不等规模及delta为独立明确版本，不把旧成绩改名。','',
'真实Unity/IL2CPP/Burst/手机、冷缓存角色轮换、长期峰值内存、Micro/FrozenQuery生产集成与全部历史竞争者尚未执行。不合并、不发布，不以0B托管分配掩盖栈与代码成本。','',
'## 包结构','', 'Source_Primary_3854fda.zip 与Source_Delta_0716f38.zip为确切Git tree源码快照（无Git历史）；Generated_Delta32是单一修正候选，仍为实验同名类型，不要与其他版本共同编译。完整再生成须检出固定Git历史。主包中的remote保留原始CI压缩包。小包没有全部原始性能样本，不能替代完整证据。']
(small/'最终验收.md').write_text('\n'.join(text))
(small/'Evidence_Index.json').write_text(json.dumps(meta,indent=2))
for kind in ('source-report','full'):
    path=out/('GameplayTags_PR22_'+kind+'.zip')
    with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
        for p in sorted(small.rglob('*')):
            if p.is_file():z.write(p,str(p.relative_to(small)))
        if kind=='full':
            for filename in ('primary-original.zip','unequal-reviewed-original.zip','counter-original.zip','x64-delta-original.zip','arm64-delta-original.zip'):
                z.write(work/filename,'remote/'+filename)
    print('FINAL_DELIVERY',json.dumps(dict(file=path.name,bytes=path.stat().st_size,sha256=hashlib.sha256(path.read_bytes()).hexdigest())))
print('DELTA_GROUPS',json.dumps(groups))
print('DELTA_FOCUS',json.dumps(focus))
print('DELTA_WORST',json.dumps([{k:r[k] for k in ('architecture','control','fixture','targetMembers','sourceMembers','distribution','operation','baseline_ns','candidate_ns','ratio','AAstable')} for r in worst]))
print('DELTA_CODEGEN',json.dumps(dict(codegen=codegen,stack=stacks)))
print('PRIMARY_COUNTS',json.dumps(primarycounts))
print('UNEQUAL_GROUPS',json.dumps(unequal['groups']))
print('DELTA_TESTS',json.dumps({a:{k:c[k] for k in ('head','tree','aggregate_rows','timing_samples','zero_allocation_samples','tests')} for a,c in checks.items()}))
