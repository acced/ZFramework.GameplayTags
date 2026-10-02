#!/usr/bin/env python3
"""Exact supplemental run only: never splice timing samples into primary results."""
import pathlib,sys,os,json,hashlib,zipfile,io,subprocess,csv
out=pathlib.Path(sys.argv[1]).resolve();out.mkdir(parents=True,exist_ok=True)
repo=os.environ['GITHUB_REPOSITORY'];run=37000994169;mainrun=36999746747
expected_head='90d6656ab0de9f86a487a733a3983814d48b4364'
def api(path):return subprocess.check_output(['gh','api',path])
def extract(z,p):
    p=p.resolve();p.mkdir(parents=True,exist_ok=True)
    for n in z.namelist():assert (p/n).resolve()==p or p in (p/n).resolve().parents
    z.extractall(p)
def rows(path):
    with path.open(encoding='utf-8-sig',newline='') as f:return list(csv.DictReader(f))
status=json.loads(api(f'/repos/{repo}/actions/runs/{run}'));assert status['conclusion']=='success' and status['head_sha']==expected_head
artifacts=json.loads(api(f'/repos/{repo}/actions/runs/{run}/artifacts'))['artifacts']
meta={};allrows=[];reference=None
for arch in ('x64','arm64'):
    a=[a for a in artifacts if a['name']==f'dense-cache-unequal-{arch}-{run}' and not a['expired']]
    assert len(a)==1;a=a[0];data=api(f'/repos/{repo}/actions/artifacts/{a["id"]}/zip');digest=hashlib.sha256(data).hexdigest();assert a['digest']=='sha256:'+digest
    (out/(arch+'-original.zip')).write_bytes(data)
    with zipfile.ZipFile(io.BytesIO(data)) as z:extract(z,out/arch)
    result=out/arch/'results';source=(result/'source.zip').read_bytes()
    if reference is None:
        reference=source
        with zipfile.ZipFile(io.BytesIO(source)) as z:extract(z,out/'source')
    else:assert source==reference
    before=json.loads((result/'recheck.json').read_text());assert before['head']==expected_head and not before['smoke']
    p=subprocess.run(['python3',str(out/'source/Audit~/DenseCacheUnequal/review.py'),'--root',str(result)],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
    (out/(arch+'-repeat-review.log')).write_text(p.stdout)
    assert p.returncode==0,p.stdout[-4000:]
    assert json.loads((result/'recheck.json').read_text())==before
    for r in rows(result/'Comparisons.csv'):
        for key in ('faster5','slower5','AAstable'):r[key]=r[key]=='True'
        for key in ('baseline_ns','candidate_ns','ratio','bytes'):r[key]=float(r[key])
        s=json.loads(r['shape']);r.update(architecture=arch,targetMembers=s['n'],sourceMembers=s['rightMembers'])
        allrows.append(r)
    meta[arch]=dict(artifact_id=a['id'],sha256=digest,review=before)
# Primary artifacts are independently completed and must contain identical candidate cores.
assert json.loads(api(f'/repos/{repo}/actions/runs/{mainrun}'))['conclusion']=='success'
a=[a for a in json.loads(api(f'/repos/{repo}/actions/runs/{mainrun}/artifacts'))['artifacts'] if a['name']==f'dense-cache-reviewed-{mainrun}' and not a['expired']];assert len(a)==1;a=a[0]
data=api(f'/repos/{repo}/actions/artifacts/{a["id"]}/zip');assert a['digest']=='sha256:'+hashlib.sha256(data).hexdigest()
hashes={}
with zipfile.ZipFile(io.BytesIO(data)) as z:
    for n in ('fixed','monotone','stream','dispatch','cache32'):
        core=(out/f'x64/results/generated/{n}/DirectArraySet.cs').read_bytes()
        assert core==(out/f'arm64/results/generated/{n}/DirectArraySet.cs').read_bytes()
        assert core==z.read(f'x64/results/generated/{n}/DirectArraySet.cs')==z.read(f'arm64/results/generated/{n}/DirectArraySet.cs')
        hashes[n]=hashlib.sha256(core).hexdigest()
def stat(rs):return dict(cases=len(rs),fast=sum(r['faster5'] for r in rs),slow=sum(r['slower5'] for r in rs),stable=sum(r['AAstable'] for r in rs),slowStable=sum(r['slower5'] and r['AAstable'] for r in rs))
groups=[]
for arch in ('x64','arm64'):
    for control in ('dispatch','stream','fixed','monotone'):
        for group in ('eligible','fallback'):
            rs=[r for r in allrows if r['architecture']==arch and r['candidate']=='cache32' and r['control']==control and ((r['sourceMembers']<=32)==(group=='eligible'))]
            groups.append(dict(architecture=arch,control=control,group=group,**stat(rs)))
focus=[r for r in allrows if r['candidate']=='cache32' and r['control'] in ('monotone','stream') and int(r['universe'])==262144 and r['targetMembers']==4096 and r['sourceMembers'] in (1,8,32,33) and r['operation']=='copy_append']
worst=sorted([r for r in allrows if r['slower5']],key=lambda r:r['ratio'],reverse=True)
result=dict(head=expected_head,run=run,metadata=meta,groups=groups,focus=focus,regressions=worst,primary_source_hashes_equal=hashes,performance_datasets_separate=True)
(out/'UNEQUAL_READOUT.json').write_text(json.dumps(result,indent=2))
(out/'Source_Supplemental.zip').write_bytes(reference)
with (out/'All_Unequal_Comparisons.csv').open('w',encoding='utf-8-sig',newline='') as f:
    writer=csv.DictWriter(f,fieldnames=list(allrows[0]));writer.writeheader();writer.writerows(allrows)
text=['# PR22 小源、大目标：独立补充验收','',f'提交{expected_head}，运行{run}。完整方法与主实验逐字节相同，计时数据分开，不拼接ns。主U/分布/容量不同不得直接比较绝对值。','',
'测试source1/8/32/33，target128/4096，U10000/262144，prefix-subset/prefix-newbits/interior-subset/tail，新建和复用。源数量为33的行走fallback。每个平台每个候选128行，A/A两标签、三轮七样本。','',
'```json',json.dumps(dict(groups=groups,focus=focus,regressions=worst),indent=2),'```','',
'源码树/生成/二进制/原始中位数/分配/容量及A/A均重新复算，并与首次结果相同。原五个完整专项和Micro套件由此补充运行再次执行。0B只指准备容量充足路径；128B栈载荷另计。未执行真实Unity/IL2CPP/手机，不宣称普适胜出。']
(out/'不等规模验收.md').write_text('\n'.join(text))
print('UNEQUAL_GROUPS',json.dumps(groups))
print('UNEQUAL_FOCUS',json.dumps(focus))
print('UNEQUAL_WORST',json.dumps(worst[:24]))
print('UNEQUAL_VERIFIED',json.dumps(dict(head=expected_head,run=run,metadata={a:{k:v for k,v in d.items() if k!='review'} for a,d in meta.items()},primary_source_hashes_equal=hashes)))
