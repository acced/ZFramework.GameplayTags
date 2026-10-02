#!/usr/bin/env python3
"""Download and independently re-audit exact x64/ARM64 artifacts; do not rerun timings."""
import os,sys,pathlib,json,hashlib,subprocess,zipfile,csv,io,shutil
ROOT=pathlib.Path(__file__).resolve().parents[2]
out=pathlib.Path(sys.argv[1]).resolve();out.mkdir(parents=True,exist_ok=True)
repo=os.environ['GITHUB_REPOSITORY'];run_id=os.environ['GITHUB_RUN_ID']
def api(path):return subprocess.check_output(['gh','api',path])
meta=json.loads(api('/repos/'+repo+'/actions/runs/'+run_id+'/artifacts?per_page=100'))
records={a['name']:a for a in meta['artifacts']};reports={};heads=set();trees=set();generated={};allrows=[];checks={}
for cpu in ('x64','arm64'):
    name='bulk-'+cpu+'-'+run_id;a=records[name]
    raw=api('/repos/'+repo+'/actions/artifacts/'+str(a['id'])+'/zip');digest=hashlib.sha256(raw).hexdigest()
    if a.get('digest'):assert a['digest']=='sha256:'+digest
    (out/(name+'.zip')).write_bytes(raw);target=out/cpu;target.mkdir(exist_ok=True)
    with zipfile.ZipFile(io.BytesIO(raw)) as z:
        for e in z.infolist():
            p=pathlib.PurePosixPath(e.filename)
            if p.is_absolute() or '..' in p.parts or ((e.external_attr>>16)&0o170000)==0o120000:raise ValueError('Unsafe artifact')
        z.extractall(target)
    before=json.loads((target/'results/recheck.json').read_text())
    proc=subprocess.run([sys.executable,str(ROOT/'Audit~/BulkConstruction/review.py'),'--root',str(target)],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
    (out/(cpu+'-independent-review.log')).write_text(proc.stdout)
    if proc.returncode:raise RuntimeError(proc.stdout[-15000:])
    after=json.loads((target/'results/recheck.json').read_text());assert before==after
    heads.add(after['head']);trees.add(after['tree']);checks[cpu]=dict(artifact_id=a['id'],archive_sha256=digest,review=after)
    generated[cpu]={str(p.relative_to(target/'results/generated')):hashlib.sha256(p.read_bytes()).hexdigest() for p in (target/'results/generated').rglob('*.cs')}
    reports[cpu]=(target/'results/REPORT.md').read_text()
    with (target/'results/All_Results.csv').open(encoding='utf-8-sig',newline='') as f:
        for row in csv.DictReader(f):row['artifact_cpu']=cpu;allrows.append(row)
assert len(heads)==len(trees)==1 and generated['x64']==generated['arm64']
shutil.copy2(out/'x64/source.zip',out/'GameplayTags_BulkConstruction_TestedSource.zip')
with (out/'All_Architectures.csv').open('w',encoding='utf-8-sig',newline='') as f:
    writer=csv.DictWriter(f,fieldnames=list(allrows[0]));writer.writeheader();writer.writerows(allrows)
(out/'COLLECTION.json').write_text(json.dumps(checks,indent=2))
intro='''# 批量构造：双平台源码与验证证据

此包针对有序已解析标签的直接构造、覆盖式重建，以及乱序输入的排序去重构造。
严格验证、独立存储、准确 Count、容量和临时工作区成本均未省略。
只增加实验 API；查询、独立并集、复制后追加、复制后删除的原内核没有改写。
它不是已经实现的自适应选择器，也不能以构造收益宣称四项原指标全部领先。

主源码：GameplayTags_BulkConstruction_TestedSource.zip。
生成后的互斥实现：各 CPU/results/generated/runtime 与 micro。
它们用于不同编译目标，不应与生产同名类一起导入 Unity。
所有原始样本、A/A、独立复核、机器码及同环境对照完整保留。
Linux x64/ARM64 .NET 测试不等于 Unity/IL2CPP/Burst 或真机验收。

'''
(out/'批量构造验证报告.md').write_text(intro+'\n\n'.join(reports.values()))
print('BULK_COLLECTION '+json.dumps({'head':next(iter(heads)),'tree':next(iter(trees)),'cross_cpu_sources_equal':True,
    'rows':len(allrows),'artifacts':{k:{'id':v['artifact_id'],'sha256':v['archive_sha256']} for k,v in checks.items()},'production_promoted':False}))
