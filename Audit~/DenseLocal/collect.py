#!/usr/bin/env python3
"""Redownload both completed archives, repeat source/sample verification, package evidence."""
import pathlib,subprocess,os,json,hashlib,zipfile,io,csv,shutil,sys
ROOT=pathlib.Path(__file__).resolve().parents[2]
def safe_extract(z,path):
    path=path.resolve();path.mkdir(parents=True,exist_ok=True)
    for name in z.namelist():
        p=(path/name).resolve()
        if p!=path and path not in p.parents:raise ValueError('unsafe archive path')
    z.extractall(path)
def main():
    out=pathlib.Path(sys.argv[1]).resolve();out.mkdir(parents=True,exist_ok=True)
    repo=os.environ['GITHUB_REPOSITORY'];runid=os.environ['GITHUB_RUN_ID']
    api=lambda url:subprocess.check_output(['gh','api',url])
    items=json.loads(api(f'/repos/{repo}/actions/runs/{runid}/artifacts'))['artifacts']
    combined=[];compared=[];meta={};reference_source=None;reference_core=None
    focus=[];negatives=[]
    for cpu in ('x64','arm64'):
        name=f'dense-local-{cpu}-{runid}'
        matches=[a for a in items if a['name']==name and not a['expired']]
        assert len(matches)==1,name
        item=matches[0];data=api(f'/repos/{repo}/actions/artifacts/{item["id"]}/zip')
        digest=hashlib.sha256(data).hexdigest()
        assert item['digest']=='sha256:'+digest
        (out/(cpu+'-original.zip')).write_bytes(data)
        folder=out/cpu
        with zipfile.ZipFile(io.BytesIO(data)) as z:safe_extract(z,folder)
        source=(folder/'source.zip').read_bytes()
        if reference_source is None:reference_source=source
        else:assert reference_source==source
        code=(folder/'results/generated/split/DirectArraySet.cs').read_bytes()
        if reference_core is None:reference_core=code
        else:assert reference_core==code
        source_folder=out/'source'
        if not source_folder.exists():
            with zipfile.ZipFile(io.BytesIO(source)) as z:safe_extract(z,source_folder)
        before=json.loads((folder/'results/recheck.json').read_text())
        done=subprocess.run(['python3',str(source_folder/'Audit~/DenseLocal/review.py'),'--root',str(folder)],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
        (out/(cpu+'-independent-recheck.log')).write_text(done.stdout)
        if done.returncode:raise RuntimeError(done.stdout[-16000:])
        after=json.loads((folder/'results/recheck.json').read_text());assert before==after
        for filename,bucket in (('All_Results.csv',combined),('Comparisons.csv',compared)):
            with (folder/'results'/filename).open(encoding='utf-8-sig',newline='') as f:
                bucket.extend(dict(architecture=cpu,**r) for r in csv.DictReader(f))
        local_focus=json.loads((folder/'results/focus.json').read_text())
        focus.extend(dict(architecture=cpu,**r) for r in local_focus)
        local_bad=json.loads((folder/'results/regressions.json').read_text())
        negatives.extend(dict(architecture=cpu,**r) for r in local_bad)
        meta[cpu]=dict(artifact=item['id'],sha256=digest,head=after['head'],tree=after['tree'],
            aggregate_rows=after['aggregate_rows'],raw_rows=after['raw_rows'],timing_samples=after['timing_samples'],
            zero_allocation_samples=after['zero_allocation_samples'],target_summary=after['target_summary'],
            unchanged_public_enumeration=after['unchanged_public_enumeration'],AA=after['AA'],
            tests={k:{key:value for key,value in t.items() if key in ('assertions','failures','cursorBytes','enumeratorBytes','localTests')} for k,t in after['tests'].items()},
            regressions=len(local_bad),stable_regressions=sum(r['AAstable'] for r in local_bad))
        print('DENSE_LOCAL_COLLECT_'+cpu+' '+json.dumps(meta[cpu]),flush=True)
    assert meta['x64']['head']==meta['arm64']['head']
    for name,rows in (('All_Architectures.csv',combined),('All_Comparisons.csv',compared)):
        with (out/name).open('w',encoding='utf-8-sig',newline='') as f:
            w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
    (out/'Source_Tested.zip').write_bytes(reference_source)
    (out/'Focus.json').write_text(json.dumps(focus,indent=2))
    (out/'All_Regressions.json').write_text(json.dumps(negatives,indent=2))
    (out/'COLLECTION.json').write_text(json.dumps(dict(architectures=meta,cross_architecture_sources_equal=True,
        aggregate_rows=len(combined),comparison_rows=len(compared),selector_implemented=False,production_promoted=False),indent=2))
    report=['# Dense 专用遍历隔离：自动复核摘要','',f'实际测量提交：`{meta["x64"]["head"]}`。',
        'direct=PR15直接Dense结果；shared=PR15共享游标组合；split=本轮操作局部Dense游标。',
        '三个版本各自独立构建，结果均来自本次同一平台对照。后续分析不可用逐行赢家拼成虚构版本。','',
        '## 固定大注册表目标（ns／完整公开操作）',
        '|平台|操作|成员|分布|左/右|输出|direct|split|每轮快5%|A/A稳定|',
        '|---|---|---:|---|---|---|---:|---:|---|---|']
    for r in focus:
        if not r['target_path']:continue
        if r['members']==8 and r['distribution']!='scattered':continue
        report.append(f'|{r["architecture"]}|{r["operation"]}|{r["members"]}|{r["distribution"]}|{r["left"]}/{r["right"]}|{r["output"]}|{r["baseline_ns"]:.3f}|{r["candidate_ns"]:.3f}|{r["faster5"]}|{r["AAstable"]}|')
    report+=['','## 限制','',
        '三轮5%为筛选而非统计置信区间；A/A不稳定与所有慢样本完整保留。',
        'source.zip、原始平台ZIP、生成C#、二进制和反汇编均保留，收集任务下载后重新逐项复算。',
        'split不改变公共Records或Enumerator；私有游标只存在于混合操作辅助方法的局部变量中。',
        '生产Runtime、Array/Dense家族和FrozenQuery未迁移，真实Unity/IL2CPP/Burst/手机未运行。',
        'Micro16原有编号上限及宽编码编译限制未改变。没有新的自动选择器、全库排名第一或发布批准。',
        'All_Regressions.json保存全部持续回退。All_Architectures.csv保存所有模式，不只目标子组。']
    (out/'验证摘要.md').write_text('\n'.join(report)+'\n',encoding='utf-8')
    print('DENSE_LOCAL_SELECTED '+json.dumps([r for r in focus if r['target_path'] and (r['members']!=8 or r['distribution']=='scattered')]),flush=True)
    print('DENSE_LOCAL_STABLE_REGRESSIONS '+json.dumps([r for r in negatives if r['AAstable']]),flush=True)
if __name__=='__main__':main()
