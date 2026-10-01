#!/usr/bin/env python3
"""Read-only cross-CPU recheck and delivery for the fixed single-bit measurement run."""
import csv,hashlib,io,json,os,pathlib,statistics,subprocess,time,urllib.request,zipfile
ROOT=pathlib.Path(__file__).resolve().parents[2]
RUN=36849030443
HEAD='ce7dc4373321e9abafcd642b7008a47799939f60'
TREE='e6b339174bc0bffe3f991adb9e8f9d4a72b4bba0'
API='https://api.github.com/repos/acced/ZFramework.GameplayTags/'
DEST=ROOT/'single-review'
DEST.mkdir(exist_ok=True)
DEL=DEST/'deliverables';DEL.mkdir(exist_ok=True)

def fetch(path,binary=False):
    req=urllib.request.Request(API+path,headers={'Accept':'application/vnd.github+json','User-Agent':'singleton-evidence-review'})
    req.add_unredirected_header('Authorization','Bearer '+os.environ['GH_TOKEN'])
    with urllib.request.urlopen(req,timeout=90) as response:data=response.read()
    return data if binary else json.loads(data)

for attempt in range(50):
    run=fetch('actions/runs/'+str(RUN))
    assert run['head_sha']==HEAD
    if run['status']=='completed':
        if run['conclusion']!='success':raise RuntimeError('Measurement failed; no performance approval')
        break
    print('Required measurement artifacts not complete',flush=True);time.sleep(30)
else:raise RuntimeError('Measurement incomplete')
arts=fetch('actions/runs/'+str(RUN)+'/artifacts')['artifacts']
reviews={};allrows=[];compiled={};sources={};increments={};tables={}
for cpu in ('x64','arm64'):
    item=next(x for x in arts if x['name']=='singleton-'+cpu+'-'+str(RUN))
    data=fetch('actions/artifacts/'+str(item['id'])+'/zip',True)
    digest=hashlib.sha256(data).hexdigest();assert item['digest']=='sha256:'+digest
    rawpath=DEST/(cpu+'.zip');rawpath.write_bytes(data)
    folder=DEST/cpu;folder.mkdir(exist_ok=True)
    with zipfile.ZipFile(io.BytesIO(data)) as z:
        for entry in z.infolist():
            p=pathlib.PurePosixPath(entry.filename)
            if p.is_absolute() or '..' in p.parts or ((entry.external_attr>>16)&0o170000)==0o120000:raise RuntimeError('Unsafe archive')
        z.extractall(folder)
    assert (folder/'SOURCE_COMMIT.txt').read_text().strip()==HEAD
    assert (folder/'SOURCE_TREE.txt').read_text().strip()==TREE
    # Execute the independent raw checker anew on downloaded bytes, not its existing success JSON.
    check=subprocess.run(['python3',str(ROOT/'Audit~/SingleBit/recheck.py'),'--root',str(folder)],cwd=ROOT,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
    (DEST/(cpu+'-recheck.log')).write_text(check.stdout)
    if check.returncode:raise RuntimeError(check.stdout)
    out=folder/'results';review=json.loads((out/'SINGLETON_REVIEW.json').read_text())
    reviews[cpu]={'artifact_id':item['id'],'sha256':digest,'independent':json.loads((out/'independent-review.json').read_text()),'tests':review['tests']}
    source=(folder/'source.zip').read_bytes();sources[cpu]=source
    gen=out/'generated'
    compiled[cpu]={name:(gen/name).read_bytes() for name in ('DirectControlSet.cs','SingleRemoveSet.cs','SingleBitSet.cs','single-bit-manifest.json')}
    comp=json.loads((out/'comparison.json').read_text());tables[cpu]=comp
    increments[cpu]=json.loads((out/'singleton-increments.json').read_text())
    for row in csv.DictReader((out/'comparison.csv').open()):allrows.append(dict(cpu=cpu,**row))
    (DEL/(cpu+'-increments.json')).write_text(json.dumps(increments[cpu],indent=2))
assert sources['x64']==sources['arm64']
assert compiled['x64']==compiled['arm64']
(DEL/'TestedSource_ce7dc43.zip').write_bytes(sources['x64'])
for name,value in compiled['x64'].items():(DEL/name).write_bytes(value)
# Both targets use the same Dense backend; provide it with literal candidates.
with zipfile.ZipFile(io.BytesIO(sources['x64'])) as z:(DEL/'DenseFusion.cs').write_bytes(z.read('Audit~/Fusion/DenseFusion.cs'))
with (DEL/'All_Architectures.csv').open('w',newline='') as f:
    w=csv.DictWriter(f,fieldnames=list(allrows[0]));w.writeheader();w.writerows(allrows)
(DEL/'CrossReview.json').write_text(json.dumps(reviews,indent=2))
lines=['# Count == used：单置位记录路径验证','','实际计时提交：`'+HEAD+'`；基线是 PR8 DirectArray，而不是之前被淘汰的扩容归并。',
       '', '三种实现：DirectControlSet 保留原样；SingleRemoveSet 只简化删除；SingleBitSet 再简化并集/追加的重复位计数。',
       '全部仅进入数组 Micro 路径，不新增字段、表示、缓存或容量策略；公开验证、Count、所有权和别名合同不变。Dense 和查询源码未改。',
       '', '## 不变量及正确性边界','',
       '有效记录掩码非零，因此 Count == used 当且仅当每条记录一位。删除保持此性质，可用存活记录数作为 Count；并集不保持，必须用两侧 Count 减重复成员数。',
       '至少一侧为单置位时，每个区间的交集仅可能是零位或一位；同区间不同位绝不能误判成同一成员。',
       '', '## 测量边界','',
       'Linux .NET 8/Release，x64、ARM64 分开，CPU affinity、关闭分层编译，三轮进程、每轮七个样本。沿用 PR8 规模/迭代/GC 准备，只增加不计时的输入单置位属性。',
       '所有独立新建、真实复制后修改、初始化、写入与准确计数均计入；预分配另列。三轮5%仅为筛选，不是统计置信区间。',
       '实际 Unity/IL2CPP/Burst 未运行；没有重跑所有历史 main/Alex 实现。窄微位图编号范围仍为 2^20。生产代码未迁移或合并。',
       '', '## 本轮正确性及原始数据复核','']
for cpu,value in reviews.items():
    lines+=['### '+cpu,'','```json',json.dumps(value,ensure_ascii=False,indent=2),'```','']
    comp=tables[cpu]
    for stage,members,definitions,dist in [('small',8,262144,'scattered'),('small',32,262144,'scattered'),('small',128,262144,'scattered'),('small',8,262144,'contiguous')]:
        for contract in ('fresh','prepared'):
            labels=['auto','micro','direct','remove','single']
            ops=['exact','union','copy_append','copy_remove'] if contract=='fresh' else ['exact','union_into','copy_append_reuse','copy_remove_reuse']
            idx={(r['variant'],r['operation']):r['value'] for r in comp if r['stage']==stage and r['members']==members and r['definitions']==definitions and r['distribution']==dist and r['contract']==contract}
            lines+=['',f'## {cpu}: {definitions}定义/{members}成员/{dist}/{contract}','','ns/操作，括号内 B/操作。','', '| 操作 | '+' | '.join(labels)+' |','|---|'+'---:|'*len(labels)]
            for op in ops:lines.append('| '+op+' | '+' | '.join(f'{idx[(label,op)]["ns"]:.3f} ({idx[(label,op)]["bytes"]:g})' for label in labels)+' |')
            if members==8 and dist=='scattered':
                print('SINGLE_FOCUS '+json.dumps({'cpu':cpu,'contract':contract,'values':{label:{op:idx[(label,op)] for op in ops} for label in labels}}),flush=True)
    lines+=['','## '+cpu+' 相对 DirectArray 的增量（只统计小集合批量）','','| 路径/候选/合同/操作/输入A | 测点 | 中位数较低 | 三轮均快5% | 三轮均慢5% |','|---|---:|---:|---:|---:|']
    compact={}
    for name,g in sorted(increments[cpu].items()):
        if not name.startswith('small/') or '/exact' in name:continue
        lines.append('| '+name+' | '+' | '.join(str(g[k]) for k in ('cases','lower_median','faster5_every_round','slower5_every_round'))+' |')
        compact[name]=g
    print('SINGLE_INCREMENT '+json.dumps({'cpu':cpu,'rows':compact}),flush=True)
lines+=['','## 判断规则','',
        '不因数学正确就宣布性能领先。查询、Dense 以及删除专用候选的并集算法未改，其波动不能算成该算法新收益。',
        'singleA/multiA 仅描述输入A成员按区间是否单置位，空集合真是空集上的全称命题；专用路径还要求数组存储和操作非空。Union/Append也可能因B单置位而进入。',
        '所有不利样本都保留，不能把两个候选逐行最优成绩拼接为一个产品。最终去留应结合双平台专用路径收益和通用路径回退。']
report='\n'.join(lines)+'\n';(DEL/'单置位验证报告.md').write_text(report)
print('SINGLE_CROSS_REVIEW '+json.dumps(reviews),flush=True)
with zipfile.ZipFile(DEST/'GameplayTags_SingleBit_SourceAndEvidence.zip','w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for p in sorted(DEL.iterdir()):
        if p.is_file():z.write(p,'deliverables/'+p.name)
    for cpu in ('x64','arm64'):z.write(DEST/(cpu+'.zip'),'raw/'+cpu+'.zip')
print('SINGLE_BUNDLE_SHA256 '+hashlib.sha256((DEST/'GameplayTags_SingleBit_SourceAndEvidence.zip').read_bytes()).hexdigest(),flush=True)
