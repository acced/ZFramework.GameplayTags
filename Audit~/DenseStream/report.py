#!/usr/bin/env python3
"""Emit evidence, not an oracle-picked winner. Optional pinned PR19 reconstruction."""
import pathlib,json,csv,sys,subprocess,hashlib,zipfile,io,math,shutil,re
PRIOR_ARTIFACT=11219297736
PRIOR_DIGEST='53ff84048fd5a997e1091514c518c6b9143512d80e3342d5f2c5320fb6c9dcd2'

def extract(z,root):
    root=root.resolve();root.mkdir(parents=True,exist_ok=True)
    for n in z.namelist():
        p=(root/n).resolve()
        if p!=root and root not in p.parents:raise ValueError('unsafe ZIP path')
    z.extractall(root)

def loadprior(out):
    data=subprocess.check_output(['gh','api',f'/repos/acced/ZFramework.GameplayTags/actions/artifacts/{PRIOR_ARTIFACT}/zip'])
    assert hashlib.sha256(data).hexdigest()==PRIOR_DIGEST
    out.mkdir(parents=True,exist_ok=True);(out/'Original_PR19_Reviewed.zip').write_bytes(data)
    with zipfile.ZipFile(io.BytesIO(data)) as z:extract(z,out)
    meta=json.loads((out/'COLLECTION.json').read_text())
    assert meta['architectures']['x64']['head']=='cffe3c54ec881352ea727a30371122c378bf4839'
    for arch in ('x64','arm64'):
        results=out/arch/'results'
        previous=json.loads((results/'recheck.json').read_text())
        p=subprocess.run(['python3',str(out/'source/Audit~/DenseBounded/review.py'),'--root',str(results)],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
        (out/(arch+'-new-recheck.log')).write_text(p.stdout)
        if p.returncode:raise RuntimeError(p.stdout[-16000:])
        assert previous==json.loads((results/'recheck.json').read_text())
    (out/'NEW_RECHECK.json').write_text(json.dumps(dict(artifact=PRIOR_ARTIFACT,sha256=PRIOR_DIGEST,repeat_equal=True,local_execution=False),indent=2))

def main():
    out=pathlib.Path(sys.argv[1]).resolve();prior='--prior' in sys.argv
    if prior:loadprior(out)
    meta=json.loads((out/'COLLECTION.json').read_text())
    with (out/'All_Comparisons.csv').open(encoding='utf-8-sig',newline='') as f:rows=list(csv.DictReader(f))
    for r in rows:
        for k in ('faster5','slower5','AAstable'):r[k]=r[k].lower()=='true'
        for k in ('baseline_ns','candidate_ns','ratio','bytes'):r[k]=float(r[k])
        for k in ('universe','members'):r[k]=int(r[k])
        r['parsed_shape']=json.loads(r['shape'])
    head=meta['architectures']['x64']['head']
    candidate='bounded' if prior else 'stream'
    controls=('combined','fixed') if prior else ('bounded','fixed','monotone')
    text=['# '+('PR19 有界反向追加：完整双平台复核' if prior else '按输入顺序消除追加工作：完整双平台实验'),'',f'实际计时提交：`{head}`。统计来自完整同次构建和原始样本，不是历史绝对耗时拼接。','',
          '五百分比为三轮筛选，不是统计置信区间；A/A不通过的读数仍保留。完整负例在All_Regressions.json；零分配不等于零执行成本。没有生产迁移、通用选择器或Unity/IL2CPP/Burst/手机验收。','',
          '## 完整比较', '|平台|候选/对照|阶段|测点|持续快5%|持续慢5%|A/A稳定|快且稳定|','|---|---|---|---:|---:|---:|---:|---:|']
    stats=[]
    for arch in ('x64','arm64'):
        for c in sorted(set(r['candidate'] for r in rows)):
            for control in sorted(set(r['control'] for r in rows if r['candidate']==c)):
                for stage in ('bench','pairs','scale'):
                    rs=[r for r in rows if r['architecture']==arch and r['candidate']==c and r['control']==control and r['stage']==stage]
                    row=dict(architecture=arch,candidate=c,control=control,stage=stage,cases=len(rs),fast=sum(r['faster5'] for r in rs),slow=sum(r['slower5'] for r in rs),stable=sum(r['AAstable'] for r in rs),fast_stable=sum(r['faster5'] and r['AAstable'] for r in rs))
                    stats.append(row);text.append('|'+ '|'.join(map(str,[arch,c+'/'+control,stage,row['cases'],row['fast'],row['slow'],row['stable'],row['fast_stable']]))+'|')
    focus=[r for r in rows if r['candidate']==candidate and r['control'] in controls and r['stage']=='scale' and r['universe']==262144 and r['operation']=='copy_append']
    text+=['','## 同输入完整复制后追加','单位微秒；包含真实克隆、增长、写出和准确计数。未使用Union代替。','|平台|候选/对照|分布|成员|对照μs|候选μs|B/op|快5%|慢5%|A/A|','|---|---|---|---:|---:|---:|---:|---|---|---|']
    for r in focus:
        text.append(f"|{r['architecture']}|{r['candidate']}/{r['control']}|{r['distribution']}|{r['members']}|{r['baseline_ns']/1000:.3f}|{r['candidate_ns']/1000:.3f}|{r['bytes']:g}|{r['faster5']}|{r['slower5']}|{r['AAstable']}|")
    text+=['','## 新候选中位数最不利项（不按稳定性删除）','|平台|对照|阶段/操作|U/N/分布|比值|持续慢5%|A/A|','|---|---|---|---|---:|---|---|']
    worst=[]
    for arch in ('x64','arm64'):
        for control in controls:
            rs=sorted([r for r in rows if r['architecture']==arch and r['candidate']==candidate and r['control']==control],key=lambda r:r['ratio'],reverse=True)[:5]
            worst.extend(rs)
            for r in rs:text.append(f"|{arch}|{control}|{r['stage']}/{r['operation']}|{r['universe']}/{r['members']}/{r['distribution']}|{r['ratio']:.3f}|{r['slower5']}|{r['AAstable']}|")
    text+=['','## 正确性、来源和测量边界']
    testsummary={};codegen={}
    for arch,m in meta['architectures'].items():
        text.append(f"- {arch}: SDK {m['sdk']}, {m['environment']}; 汇总{m['aggregate_rows']}, 原始{m['raw_rows']}, 耗时样本{m['timing_samples']}, 零分配样本{m['zero_allocation_samples']}。source_verified={m['source_verified']}, allocation_contracts_equal={m['allocation_contracts_equal']}。")
        testsummary[arch]={}
        for n,t in m['tests'].items():
            v={k:t[k] for k in ('assertions','failures','bulkTests','streamTests','localTests','cursorBytes','enumeratorBytes') if k in t}
            testsummary[arch][n]=v
        text.append('测试断言包含掩码穷举、循环及重复配置，不代表相同数量的独立案例。原Runtime通过不等于实验Micro已完成FrozenQuery整库迁移。')
        codegen[arch]={}
        for p in (out/arch/'results').glob('*-codegen.log'):
            body=p.read_text();sizes=[]
            for section in body.split('; Assembly listing for method ')[1:]:
                found=re.search(r'; Total bytes of code (\d+)',section)
                if found:sizes.append(dict(method=section.splitlines()[0],bytes=int(found.group(1))))
            codegen[arch][p.name]=sizes
    text+=['','完整代码大小、专项测试、A/A以及所有反例保留于Summary.json及原始产物。没有以没有GC回收替代零分配，也没有删除慢样本。']
    (out/'结果报告.md').write_text('\n'.join(text),encoding='utf-8')
    result=dict(head=head,prior=prior,statistics=stats,focus=focus,worst=worst,tests=testsummary,codegen=codegen)
    (out/'Summary.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
    print('REPORT_HEAD',head)
    print('REPORT_STATS',json.dumps(stats))
    print('REPORT_FOCUS',json.dumps([{k:r[k] for k in ('architecture','candidate','control','operation','distribution','members','baseline_ns','candidate_ns','bytes','faster5','slower5','AAstable')} for r in focus]))
    print('REPORT_WORST',json.dumps([{k:r[k] for k in ('architecture','control','stage','operation','universe','members','distribution','ratio','slower5','AAstable')} for r in worst]))
    print('REPORT_TESTS',json.dumps(testsummary))
    print('REPORT_CODEGEN',json.dumps(codegen))
if __name__=='__main__':main()
