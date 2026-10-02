#!/usr/bin/env python3
"""Download exact completed artifacts, repeat verification, package final source/evidence."""
import os, sys, pathlib, subprocess, json, hashlib, zipfile, csv, io, shutil
ROOT=pathlib.Path(__file__).resolve().parents[2]
def gh(path):return subprocess.check_output(['gh','api',path])
def main():
    out=pathlib.Path(sys.argv[1]).resolve();out.mkdir(parents=True,exist_ok=True)
    repo=os.environ['GITHUB_REPOSITORY'];run=os.environ['GITHUB_RUN_ID']
    docs=json.loads(gh('/repos/'+repo+'/actions/runs/'+run+'/artifacts?per_page=100'))['artifacts']
    allrows=[];pairs=[];frontiers=[];manifest={};code=None;head=None
    for cpu in ('x64','arm64'):
        name='preparation-v3-'+cpu+'-'+run
        matches=[d for d in docs if d['name']==name and not d['expired']]
        assert len(matches)==1,matches
        item=matches[0];raw=gh('/repos/'+repo+'/actions/artifacts/'+str(item['id'])+'/zip')
        digest=hashlib.sha256(raw).hexdigest();assert item['digest']=='sha256:'+digest
        (out/(cpu+'-raw.zip')).write_bytes(raw)
        folder=out/cpu;folder.mkdir(exist_ok=True)
        with zipfile.ZipFile(io.BytesIO(raw)) as z:
            for p in z.namelist():
                path=pathlib.PurePosixPath(p);assert not path.is_absolute() and '..' not in path.parts
            z.extractall(folder)
        subprocess.check_call([sys.executable,str(ROOT/'Audit~/PreparationV3/review.py'),'--root',str(folder)],stdout=(out/(cpu+'-independent-review.log')).open('w'))
        result=json.loads((folder/'results/recheck.json').read_text())
        generation=folder/'results/generated'
        actual={str(p.relative_to(generation)):p.read_bytes() for p in generation.rglob('*.cs') if 'portable-host' not in p.name}
        if code is None:code=actual;head=result['head']
        else:assert actual==code and head==result['head']
        manifest[cpu]=dict(artifact=item['id'],sha256=digest,head=result['head'],tree=result['tree'],rows=result['rows'],samples=result['timing_samples'],zero_samples=result['zero_allocation_samples'])
        for path,target in [('All_Results.csv',allrows),('Comparisons.csv',pairs),('Frontier_All.csv',frontiers)]:
            rows=list(csv.DictReader((folder/'results'/path).open(encoding='utf-8-sig')))
            target.extend(dict(architecture=cpu,**row) for row in rows)
        print('V3_TOTALS_'+cpu+' '+(folder/'results/totals.json').read_text().replace('\n',' '))
        targets=[dict(architecture=cpu,**r) for r in json.loads((folder/'results/focus.json').read_text())]
        print('V3_FOCUS_'+cpu+' '+json.dumps(targets))
    for name,rows in [('All_Architectures.csv',allrows),('All_Comparisons.csv',pairs),('Frontier_Architectures.csv',frontiers)]:
        with (out/name).open('w',encoding='utf-8-sig',newline='') as f:
            w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
    shutil.copy2(out/'x64/source.zip',out/'GameplayTags_PreparationV3_TestedSource.zip')
    focus=[r for r in frontiers if r['universe']=='262144' and r['seed']=='20261107' and r['members'] in ('8','4096') and
        r['distribution'] in ('contiguous','scattered') and r['operation'] in ('input_plus_16_unions','live_plus_16_unions') and not r['label'].endswith('AA')]
    print('V3_FRONTIER '+json.dumps([{k:r[k] for k in ('architecture','label','members','distribution','order','operation','strategy','ns','bytes')} for r in focus]))
    manifest=dict(head=head,cross_architecture_sources_equal=True,architectures=manifest,
        aggregate_rows=len(allrows),comparison_rows=len(pairs),frontier_rows=len(frontiers),selector_implemented=False,production_promoted=False)
    (out/'collection.json').write_text(json.dumps(manifest,indent=2))
    report=['# Preparation v3：构造重组与真实生命周期比较','',
        '已测提交：`'+head+'`。源码、两平台未经筛选的原始数据、同二进制 A/A 与下载后重算均保留。',
        '', '本轮把 PR13 的新建路径与 PR12 的完整 Reset 调用链重新编译为一个实际候选；空源转换在验证和目标分配之后退出。不是按单项成绩拼装的虚构实现。',
        '', '生命周期 input 从已解析有序输入开始；live 从已有集合开始。live/direct 包含导出句柄数组的分配与枚举。每组所有策略使用同一份源表示的对端，因此混合操作不能逃避。keep/live/0 不克隆，明确是保持原对象，不参与独立结果构造排名。',
        '', '没有自动选择器、Array↔Micro 桥接或四项全面领先结论。每个集合只持有其已有的一种成员表示；没有新增实例字段。真实 Unity、IL2CPP、Burst 和移动设备没有运行。',
        '', '| 平台 | 家族 | N | 分布 | 方向 | 工作 | 策略 | ns/完整工作 | B/完整工作 |','|---|---|---:|---|---|---|---|---:|---:|']
    for r in focus:report.append('|%s|%s|%s|%s|%s|%s|%s|%.3f|%s|'%(r['architecture'],r['label'],r['members'],r['distribution'],r['order'],r['operation'],r['strategy'],float(r['ns']),r['bytes']))
    report+=['','各平台 REPORT.md、recheck.json、totals.json 给出全部回退与 A/A。0.5ms 校准、8MiB 分配上限、7样本×3进程轮次，SDK8.0.425/.NET8；不能与其他校准/机器的历史绝对值混用。所有慢样本保留。三轮5%是筛选而不是置信区间。']
    (out/'验证说明.md').write_text('\n'.join(report))
    print('V3_COLLECTION '+json.dumps(manifest))
if __name__=='__main__':main()
