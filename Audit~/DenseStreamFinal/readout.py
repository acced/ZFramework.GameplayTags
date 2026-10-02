#!/usr/bin/env python3
"""Post-run source identity and complete changed/unchanged-path readout; no rerun/tuning."""
import pathlib,sys,subprocess,zipfile,io,hashlib,json,csv
out=pathlib.Path(sys.argv[1]);out.mkdir(parents=True,exist_ok=True)
SOURCES={11220923545:'7e58904680a2ddde73fe284fababdf069e78131badf80d717a1f03b4c4203296',11221096296:'c8760c48510c49a0804fb59cc686ce91c8c43b4a38b450417d73736675445713'}
archives={}
for ident,digest in SOURCES.items():
    raw=subprocess.check_output(['gh','api',f'/repos/acced/ZFramework.GameplayTags/actions/artifacts/{ident}/zip'])
    assert hashlib.sha256(raw).hexdigest()==digest
    archives[ident]=zipfile.ZipFile(io.BytesIO(raw))
main=archives[11220923545];diag=archives[11221096296]
meta=json.loads(main.read('COLLECTION.json'));counts=json.loads(diag.read('WORK_COUNTS.json'))
assert counts['instrumented'] and not counts['timed']
assert meta['architectures']['x64']['head']==meta['architectures']['arm64']['head']=='0d097a1546c51ea3676ef35817428f346da46293'
for arch in ('x64','arm64'):
    manifest=json.loads(main.read(f'{arch}/results/manifest.json'))
    assert manifest['source_kind']=='git' and manifest['head']==meta['architectures'][arch]['head']
    for variant,digest in counts['original_core_hashes'].items():
        assert hashlib.sha256(main.read(f'{arch}/results/generated/{variant}/DirectArraySet.cs')).hexdigest()==digest
    with zipfile.ZipFile(io.BytesIO(main.read(f'{arch}/results/source.zip'))) as z:
        for path,digest in manifest['source_files'].items():assert hashlib.sha256(z.read(path)).hexdigest()==digest
rows=list(csv.DictReader(io.StringIO(main.read('All_Comparisons.csv').decode('utf-8-sig'))))
for r in rows:
    for k in ('faster5','slower5','AAstable'):r[k]=r[k].lower()=='true'
    for k in ('baseline_ns','candidate_ns','ratio','bytes'):r[k]=float(r[k])
    for k in ('universe','members'):r[k]=int(r[k])
    r['parsed_shape']=json.loads(r['shape'])
def changed(r):return r['stage'] in ('bench','scale') and r['operation'] in ('copy_append','reuse_copy_append') and (r['left'],r['right'],r['output'])==('Micro','Dense','Micro') and r['members']!=0
comparisons=[('monotone','fixed'),('stream','bounded'),('stream','fixed'),('stream','monotone')]
def stat(rs):return dict(cases=len(rs),fast=sum(r['faster5'] for r in rs),slow=sum(r['slower5'] for r in rs),stable=sum(r['AAstable'] for r in rs),fast_stable=sum(r['faster5'] and r['AAstable'] for r in rs),slow_stable=sum(r['slower5'] and r['AAstable'] for r in rs))
groups=[]
for arch in ('x64','arm64'):
    for candidate,control in comparisons:
        for stage in ('bench','scale'):
            for group in ('changed_nonempty_append','other_operations'):
                rs=[r for r in rows if r['architecture']==arch and (r['candidate'],r['control'],r['stage'])==(candidate,control,stage) and changed(r)==(group=='changed_nonempty_append')]
                groups.append(dict(architecture=arch,candidate=candidate,control=control,stage=stage,group=group,**stat(rs)))
by_size=[]
for arch in ('x64','arm64'):
    for candidate,control in comparisons:
        for n in (1,8,128,4096):
            rs=[r for r in rows if r['architecture']==arch and (r['candidate'],r['control'],r['stage'],r['members'])==(candidate,control,'bench',n) and changed(r)]
            by_size.append(dict(architecture=arch,candidate=candidate,control=control,members=n,**stat(rs)))
failures=[r for r in rows if r['slower5'] and r['candidate'] in ('stream','monotone')]
focus=[r for r in rows if r['stage']=='bench' and changed(r) and r['universe']==262144 and r['members'] in (8,4096) and r['distribution'] in ('scattered','contiguous') and r['parsed_shape']['seed']==20261031 and (r['candidate'],r['control']) in comparisons]
# Keep all failures in the attachment; print one worst sustained row per actual operation/direction.
worst=[]
for arch in ('x64','arm64'):
    for candidate,control in comparisons:
        for kind in ('changed','other'):
            rs=[r for r in failures if r['architecture']==arch and (r['candidate'],r['control'])==(candidate,control) and changed(r)==(kind=='changed')]
            worst.extend(sorted(rs,key=lambda r:r['ratio'],reverse=True)[:4])
result=dict(head='0d097a1546c51ea3676ef35817428f346da46293',artifact_sha256=SOURCES,work_source_hashes=counts['original_core_hashes'],all_measured_and_diagnostic_sources_equal=True,main_source_hashes_verified=True,groups=groups,by_size=by_size,focus=focus,failures=failures,work_counts=counts['rows'],AA={a:m['AA'] for a,m in meta['architectures'].items()})
(out/'Final_Readout.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
(out/'Source_Tested.zip').write_bytes(main.read('Source_Tested.zip'))
(out/'All_Comparisons.csv').write_bytes(main.read('All_Comparisons.csv'))
(out/'工作量报告.md').write_bytes(diag.read('工作量报告.md'))
text=['# PR20 目标追加与其他路径分开验收','', '从已完成的主产物和独立工作量产物重新下载，校验两份ZIP SHA256、两平台源码及四种核心原文件哈希。插桩前核心与两平台被计时代码逐字节一致。没有重新计时或根据数据调整算法。','',
'Changed group仅非空Micro目标接受Dense追加。其他路径包括未修改查询/删除/并集以及空输入；计时偏移不因代码未改而删除。bench为旧完整公共矩阵，scale为单独前插/尾插/子集/交错矩阵。快/慢要求三轮和同二进制A/A同时跨过5%，该门槛仍非置信区间。','',
'|平台|候选/对照|阶段|分组|测点|快|慢|快且AA稳定|慢且AA稳定|','|---|---|---|---|---:|---:|---:|---:|---:|']
for g in groups:text.append('|'+ '|'.join(str(x) for x in (g['architecture'],g['candidate']+'/'+g['control'],g['stage'],g['group'],g['cases'],g['fast'],g['slow'],g['fast_stable'],g['slow_stable']))+'|')
text+=['','全部按数量分组、失败项、原始输入摘要与A/A见Final_Readout.json；完整比较见All_Comparisons.csv。Source_Tested.zip为实际主计时提交的完整源码快照，Git历史另需检出。工作量计数不是时间/缓存计数，不能用它替换完整公开操作。主验证的两份未改原ZIP和所有二进制仍在主产物11220923545中。','', '真实Unity/IL2CPP/Burst/手机/冷多角色/长期峰值内存尚未验证。没有逐行挑选Auto、生产迁移或合并。']
(out/'分组验收报告.md').write_text('\n'.join(text))
compact=lambda rs:[{k:v for k,v in r.items() if k not in ('shape','parsed_shape')} for r in rs]
print('FINAL_SOURCE_IDENTITY',json.dumps(result['work_source_hashes']))
print('FINAL_GROUPS',json.dumps(groups))
print('FINAL_SIZES',json.dumps(by_size))
print('FINAL_FOCUS',json.dumps(compact(focus)))
print('FINAL_WORST_SUSTAINED',json.dumps(compact(worst)))
print('FINAL_AA',json.dumps(result['AA']))
