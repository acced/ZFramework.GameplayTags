#!/usr/bin/env python3
"""Re-download and independently recompute exact CPU artifacts; no new timings."""
import pathlib,os,sys,subprocess,json,zipfile,io,hashlib,shutil,csv
ROOT=pathlib.Path(__file__).resolve().parents[2]
out=pathlib.Path(sys.argv[1]).resolve();out.mkdir(parents=True,exist_ok=True)
repo=os.environ['GITHUB_REPOSITORY'];runid=os.environ['GITHUB_RUN_ID']
def api(p):return subprocess.check_output(['gh','api',p])
meta=json.loads(api('/repos/'+repo+'/actions/runs/'+runid+'/artifacts?per_page=100'))
artifacts={a['name']:a for a in meta['artifacts']};records={};generated={};heads=set();tables=[];reports=[]
for cpu in ('x64','arm64'):
    a=artifacts['preparation-v2-'+cpu+'-'+runid]
    raw=api('/repos/'+repo+'/actions/artifacts/'+str(a['id'])+'/zip');digest=hashlib.sha256(raw).hexdigest()
    if a.get('digest'):assert a['digest']=='sha256:'+digest
    (out/(cpu+'-original.zip')).write_bytes(raw);folder=out/cpu;folder.mkdir(exist_ok=True)
    with zipfile.ZipFile(io.BytesIO(raw)) as z:
        for e in z.infolist():
            p=pathlib.PurePosixPath(e.filename)
            assert not p.is_absolute() and '..' not in p.parts and ((e.external_attr>>16)&0o170000)!=0o120000
        z.extractall(folder)
    before=json.loads((folder/'results/recheck.json').read_text())
    run=subprocess.run([sys.executable,str(ROOT/'Audit~/PreparationV2/review.py'),'--root',str(folder)],capture_output=True,text=True)
    (out/(cpu+'-recheck.log')).write_text(run.stdout+run.stderr)
    if run.returncode:raise RuntimeError(run.stdout[-5000:]+run.stderr[-5000:])
    after=json.loads((folder/'results/recheck.json').read_text());assert after==before
    heads.add(after['head']);records[cpu]={'artifact_id':a['id'],'sha256':digest,'head':after['head'],'tree':after['tree'],'samples':after['timing_samples']}
    generated[cpu]={str(p.relative_to(folder/'results/generated')):hashlib.sha256(p.read_bytes()).hexdigest() for p in (folder/'results/generated').rglob('*.cs')}
    reports.append((folder/'results/REPORT.md').read_text())
    with (folder/'results/Comparisons.csv').open(encoding='utf-8-sig',newline='') as f:tables+=list(csv.DictReader(f))
assert len(heads)==1 and generated['x64']==generated['arm64']
shutil.copy2(out/'x64/source.zip',out/'GameplayTags_PreparationV2_TestedSource.zip')
with (out/'All_Comparisons.csv').open('w',encoding='utf-8-sig',newline='') as f:
    w=csv.DictWriter(f,fieldnames=list(tables[0]));w.writeheader();w.writerows(tables)
(out/'COLLECTION.json').write_text(json.dumps(records,indent=2))
(out/'验证报告.md').write_text('''# Preparation v2：构造分流与显式转换

源码与两平台的全部测量、反例、原始样本、源码树、二进制身份和独立复核。
候选仅在 Audit 实验构建中生成；生产 Runtime/Editor/Query 没有被替换。
本轮修复候选：空/单成员路径；新建结果边验证边写入；Dense 乱序直接置位；Array 借用自己的最终缓冲排序。
新增 CopyAsStorage 返回独立集合，不在现有对象中自动转换；标准复制构造器仍保留源容量。
显式转换只覆盖 Array↔Dense 和 Micro↔Dense，不冒称统一的 Array/Micro/Dense 选择器。
调用方输入校验、结果准确计数、容量、复制和分配成本没有被省略。
所有具体性能结论以本包同次数据为准；三轮5%只是筛选，不是置信区间。
未运行 Unity/IL2CPP/Burst/真机，不是发布包。多个同名生成类不可同时导入 Unity。

'''+ '\n\n'.join(reports))
print('V2_COLLECTION '+json.dumps({'head':next(iter(heads)),'cross_cpu_sources_equal':True,'comparisons':len(tables),'artifacts':records,'production_promoted':False}))
