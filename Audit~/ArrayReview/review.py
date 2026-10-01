#!/usr/bin/env python3
"""Cross-architecture review and exact-source deliverables; no new timings."""
import csv,hashlib,io,json,math,pathlib,shutil,subprocess,sys,time,zipfile
ROOT=pathlib.Path(__file__).resolve().parents[2]
RUN=36842179080
TESTED='4556d5dcde04afe32f07b7755b244762682ae93a'
REPO='acced/ZFramework.GameplayTags'
OUT=ROOT/'review-artifacts';OUT.mkdir(exist_ok=True)
RAW=OUT/'raw';RAW.mkdir(exist_ok=True)
DELIVER=OUT/'deliverables';DELIVER.mkdir(exist_ok=True)

def api(path):return json.loads(subprocess.check_output(['gh','api',path],text=True))
def git(*args):return subprocess.check_output(['git',*args],cwd=ROOT)
def save(path,data):path.write_text(json.dumps(data,ensure_ascii=False,indent=2))

for attempt in range(90):
    info=api(f'repos/{REPO}/actions/runs/{RUN}')
    if info['head_sha']!=TESTED:raise RuntimeError('Wrong performance commit')
    if info['status']=='completed':break
    if attempt%6==0:print('Awaiting required completed performance artifact',flush=True)
    time.sleep(10)
else:raise RuntimeError('Required performance run did not complete')
if info['conclusion']!='success':raise RuntimeError('Performance prerequisite not successful; do not publish approval')
artifacts=api(f'repos/{REPO}/actions/runs/{RUN}/artifacts')['artifacts']
summaries={};rows={};folders={};meta={}
for arch in ('x64','arm64'):
    name=f'micro-array-{arch}-{RUN}';selected=[a for a in artifacts if a['name']==name and not a['expired']]
    if len(selected)!=1:raise RuntimeError('Missing exact artifact '+name)
    item=selected[0];destination=RAW/(name+'.zip')
    with destination.open('wb') as f:subprocess.run(['gh','api',f'repos/{REPO}/actions/artifacts/{item["id"]}/zip'],stdout=f,check=True)
    sha=hashlib.sha256(destination.read_bytes()).hexdigest()
    if item.get('digest')!='sha256:'+sha:raise RuntimeError('Artifact digest mismatch')
    folder=OUT/arch;folder.mkdir(exist_ok=True);folders[arch]=folder
    with zipfile.ZipFile(destination) as z:
        for e in z.infolist():
            path=pathlib.PurePosixPath(e.filename)
            if path.is_absolute() or '..' in path.parts or ((e.external_attr>>16)&0o170000)==0o120000:raise RuntimeError('Unsafe artifact path')
        z.extractall(folder)
    # The review program is separately submitted after the measured commit.
    # Re-execute the measured independent checker, then add cross-CPU and mutation checks.
    checker=OUT/'raw-recheck.py';checker.write_bytes(git('show',TESTED+':Audit~/ArrayBuild/recheck.py'))
    subprocess.run([sys.executable,str(checker),'--root',str(folder)],check=True,stdout=(folder/'review-rerun.log').open('w'))
    r=folder/'results'
    summaries[arch]=json.loads((r/'independent-review.json').read_text())
    records=json.loads((r/'comparison.json').read_text());rows[arch]=records
    if summaries[arch]['head']!=TESTED:raise RuntimeError('Wrong tested source')
    for record in records:
        if record['variant'] in ('arrays','growth'):
            # The change removes work, not allocations or capacity guarantees.
            if record['value']['bytes']!=record['references']['kernel']['bytes']:raise RuntimeError('Unexpected allocation change '+str(record))
    for label in ('control','arrays','growth'):
        for suffix in ('tests','nohw-tests','builder-tests'):
            t=json.loads((r/(label+'-'+suffix+'.json')).read_text())
            if t.get('failures',t.get('failed',-1))!=0:raise RuntimeError('Failed tests')
    if json.loads((r/'portable-tests.json').read_text()).get('failures')!=0:raise RuntimeError('Portable suite failed')
    meta[arch]={'artifact_id':item['id'],'sha256':sha,'source':summaries[arch]}

x=folders['x64']/'results/generated';a=folders['arm64']/'results/generated'
for name in ('KernelControlSet.cs','DirectArraySet.cs','DirectGrowthSet.cs'):
    if (x/name).read_bytes()!=(a/name).read_bytes():raise RuntimeError('Different candidate source across CPUs')
# Semantic single-variable checks on the exact generated files.
sys.path.insert(0,str(ROOT/'Audit~/ArrayBuild'))
from build_candidates import body
reference=(x/'KernelControlSet.cs').read_text()
for name in ('DirectArraySet','DirectGrowthSet'):
    t=(x/(name+'.cs')).read_text().replace(name,'KernelControlSet')
    if t[:t.index('        public KernelControlSet(')]!=reference[:reference.index('        public KernelControlSet(')]:raise RuntimeError('Extra per-set state')
    for sig in ('public bool HasTagExact(', 'private static int DenseUnion(', 'private static int DenseDifference('):
        if body(t,sig)!=body(reference,sig):raise RuntimeError('Changed unrelated hot path')

shutil.copy2(folders['x64']/'source.zip',DELIVER/'GameplayTags_MicroArray_TestedSource_4556d5d.zip')
sources=DELIVER/'CandidateSources';sources.mkdir(exist_ok=True)
for name in ('KernelControlSet.cs','DirectArraySet.cs','DirectGrowthSet.cs'):shutil.copy2(x/name,sources/name)
(sources/'DenseFusion.cs').write_bytes(git('show',TESTED+':Audit~/Fusion/DenseFusion.cs'))
(sources/'README.md').write_text('Experimental generated classes only, not a complete replacement Runtime. Use the tested repository and its audit runner for dependencies and controls. No raw ID storage or new per-instance fields. Do not promote from a lower individual timing.\n')
with (DELIVER/'All_Architectures.csv').open('w',newline='') as stream:
    writer=csv.writer(stream);first=True
    for arch in ('x64','arm64'):
        with (folders[arch]/'results/comparison.csv').open() as f:
            reader=csv.reader(f);header=next(reader)
            if first:writer.writerow(['architecture']+header);first=False
            for row in reader:writer.writerow([arch]+row)

lines=['# GameplayTags：微位图直接数组与扩容归并验证', '', '## 结论范围', '',
       '本轮实际测量提交：`'+TESTED+'`。x64 与 ARM64 分开测试，不拼接最好样本。',
       '只新增实验候选，生产 Runtime、Editor、序列化、Query 与包版本不变；没有合并或发布。',
       'DirectArraySet 将数组/内联选择移出循环，并按块复制尾段；DirectGrowthSet 另在需要扩容时直接向新数组归并，避免先复制旧前缀。',
       '两个候选都没有新增对象字段，查找与 Dense 方法保持不变，新建分配字节与融合内核对照逐行一致。', '',
       '## 正确性与独立复核', '',
       '原 Micro 完整套件、禁用硬件指令、独立编译 Standard 2.1 标量库均实际执行。新增回归覆盖不同重叠关系、同区间不同成员、保留数组但只剩单记录、结果实际容量、独立所有权、随机持续变更与零分配。',
       '所有断言包含循环和重复配置，不是独立测试用例总数。每平台的精确数字如下。']
for arch in ('x64','arm64'):
    r=folders[arch]/'results'
    tests={name:json.loads((r/(name+'.json')).read_text()) for name in ('control-tests','arrays-tests','growth-tests','control-builder-tests','arrays-builder-tests','growth-builder-tests','portable-tests')}
    lines+=['', '### '+arch, '', '```json',json.dumps({'verification':summaries[arch],'tests':tests},ensure_ascii=False,indent=2),'```']
lines+=['', '## 批量筛选', '',
        '每个候选独立排名。小集合对照为自适应 Auto、Micro、原融合内核；大集合另含强制 Dense。每轮至少领先 5% 是筛选标准，不是统计置信区间。', '',
        '| CPU / stage / candidate / contract | 测点 | 中位数低于所有对照 | 三轮均至少领先5% |','|---|---:|---:|---:|']
for arch in ('x64','arm64'):
    for label,values in sorted(summaries[arch]['bulk_gates'].items()):lines.append('| '+arch+'/'+label+' | '+' | '.join(map(str,values))+' |')
# Separate the incremental growth candidate from the direct-array loop change.
bulk={'union','copy_append','copy_remove','union_into','copy_append_reuse','copy_remove_reuse'}
increment={}
for arch,records in rows.items():
    indexed={tuple(r[k] for k in ('stage','variant','contract','operation','members','definitions','distribution')):r for r in records}
    for key,r in indexed.items():
        if key[1]!='growth' or key[3] not in bulk:continue
        baseline=indexed[(key[0],'arrays',*key[2:])]['value'];current=r['value']
        tag=arch+'/'+key[0]+'/'+key[2]+'/'+key[3]
        v=increment.setdefault(tag,{'cases':0,'lower_median':0,'margin_every_round':0,'slower_over_5pct_every_round':0})
        v['cases']+=1;v['lower_median']+=int(current['ns']<baseline['ns'])
        v['margin_every_round']+=int(all(c<=b*.95 for c,b in zip(current['round_medians'],baseline['round_medians'])))
        v['slower_over_5pct_every_round']+=int(all(c>b*1.05 for c,b in zip(current['round_medians'],baseline['round_medians'])))
    for stage,n,u in [('small',8,262144),('large',1024,10000)]:
        for distribution in ['scattered'] if stage=='small' else ['contiguous','scattered']:
            for contract in ('fresh','prepared'):
                lines+=['',f'## {arch} / {u} 定义 / {n} 成员 / {distribution} / {contract}', '',
                        '单位 ns/操作，括号内为 B/操作。Auto 列为自适应模式。', '',
                        '| 操作 | Auto | Micro | 融合内核对照 | 直接数组 | 扩容直接归并 |','|---|---:|---:|---:|---:|---:|']
                ops=['exact','union','copy_append','copy_remove'] if contract=='fresh' else ['exact','union_into','copy_append_reuse','copy_remove_reuse']
                for op in ops:
                    values=[indexed[(stage,label,contract,op,n,u,distribution)]['value'] for label in ('auto','micro','kernel','arrays','growth')]
                    lines.append('| '+op+' | '+' | '.join(f'{v["ns"]:.3f} ({v["bytes"]:g})' for v in values)+' |')
lines+=['', '## 扩容路径的独立增量', '', '以下仅比较 DirectGrowthSet 与 DirectArraySet；未发生扩容或没有修改算法的路径，不将计时波动解释为优化。', '',
        '```json',json.dumps(increment,ensure_ascii=False,indent=2),'```', '',
        '## 未完成的门槛', '',
        '没有实际 Unity Editor、Android/iOS IL2CPP 或 Burst 验证；ARM64 Linux .NET 不是手游原生验收。没有重跑所有历史 main/Alex/indexed/packed。原微位图 2^20 编号限制保留。',
        '关系/持续变更已做正确性回归，但其全耗时矩阵、长期常驻与峰值内存仍未补齐。完整目标是否通过须按全部同条件结果判断，不能把两候选的逐行赢家拼成一个实现。',
        '原始 ZIP、逐样本 JSON、完整 CSV、三份生成源码及全部回归日志一并保留。生产 Runtime 不因本报告自动替换。']
report='\n'.join(lines)+'\n';(DELIVER/'GameplayTags_MicroArray_验证报告.md').write_text(report)
save(DELIVER/'CrossArchitecture_Review.json',{'tested':TESTED,'artifacts':meta,'fresh_allocations_equal_control':True,
    'identical_generated_sources':True,'query_and_dense_methods_unchanged':True,'incremental_growth_comparison':increment,
    'production_promoted':False,'native_unity':'not_run'})
print('CROSS_REVIEW '+json.dumps({'artifacts':meta,'increment':increment},ensure_ascii=False),flush=True)
print(report,flush=True)
with zipfile.ZipFile(OUT/'GameplayTags_MicroArray_SourceAndEvidence.zip','w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for folder in (RAW,DELIVER):
        for p in sorted(folder.rglob('*')):
            if p.is_file():z.write(p,p.relative_to(OUT).as_posix())
print('BUNDLE_SHA256 '+hashlib.sha256((OUT/'GameplayTags_MicroArray_SourceAndEvidence.zip').read_bytes()).hexdigest(),flush=True)
