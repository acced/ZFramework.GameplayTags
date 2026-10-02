#!/usr/bin/env python3
"""Read ONLY completed pinned artifacts; rerun archived verifier; no new timing."""
import pathlib,sys,os,json,subprocess,hashlib,zipfile,io,csv,re,collections
ROOT=pathlib.Path(__file__).resolve().parents[2]
out=pathlib.Path(sys.argv[1]).resolve();out.mkdir(parents=True,exist_ok=True)
MAIN_RUN=36999746747
WORK_RUN=37000083091
HEAD='3854fda5c923e44cadb081e774d708030cf1e2a3'
repo=os.environ['GITHUB_REPOSITORY']
def api(url):return subprocess.check_output(['gh','api',url])
def download(run,name,dest):
    status=json.loads(api(f'/repos/{repo}/actions/runs/{run}'))
    assert status['status']=='completed' and status['conclusion']=='success',status['status']
    found=[a for a in json.loads(api(f'/repos/{repo}/actions/runs/{run}/artifacts'))['artifacts'] if a['name']==name and not a['expired']]
    assert len(found)==1
    a=found[0];data=api(f'/repos/{repo}/actions/artifacts/{a["id"]}/zip');h=hashlib.sha256(data).hexdigest()
    assert a['digest']=='sha256:'+h
    dest.write_bytes(data)
    return dict(id=a['id'],sha256=h,bytes=len(data),run=run),zipfile.ZipFile(io.BytesIO(data))
def extract(z,path):
    path=path.resolve();path.mkdir(parents=True,exist_ok=True)
    for e in z.infolist():
        p=(path/e.filename).resolve()
        assert p==path or path in p.parents,e.filename
    z.extractall(path)
def load_csv(path):
    with path.open(encoding='utf-8-sig',newline='') as f:return list(csv.DictReader(f))
def table(rows,keys):
    return '\n'.join(['|'+'|'.join(keys)+'|','|'+'|'.join('---' for k in keys)+'|']+['|'+'|'.join(str(r[k]) for k in keys)+'|' for r in rows])
main_meta,z=download(MAIN_RUN,f'dense-cache-reviewed-{MAIN_RUN}',out/'原始完整验证.zip')
extract(z,out/'main')
work_meta,wz=download(WORK_RUN,f'dense-cache-work-{WORK_RUN}',out/'原始工作量诊断.zip')
work=json.loads(wz.read('WORK_COUNTS.json'));assert work['timed'] is False
base=out/'main';source=base/'source';data=load_csv(base/'All_Comparisons.csv');values=load_csv(base/'All_Architectures.csv')
for r in data:
    for key in ('faster5','slower5','AAstable'):r[key]=r[key]=='True'
    for key in ('members','universe'):r[key]=int(r[key])
    for key in ('baseline_ns','candidate_ns','ratio','bytes'):r[key]=float(r[key])
    r['input']=json.loads(r['shape'])
checks={};codegen={};snapshots={}
for arch in ('x64','arm64'):
    folder=base/arch/'results';before=json.loads((folder/'recheck.json').read_text());assert before['head']==HEAD and not before['smoke']
    p=subprocess.run(['python3',str(source/'Audit~/DenseCache/review.py'),'--root',str(folder)],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
    (out/(arch+'-repeat-review.log')).write_text(p.stdout)
    assert p.returncode==0,p.stdout[-5000:]
    assert json.loads((folder/'recheck.json').read_text())==before
    checks[arch]=before
    for n,h in work['original_core_hashes'].items():
        assert hashlib.sha256((folder/f'generated/{n}/DirectArraySet.cs').read_bytes()).hexdigest()==h
    codegen[arch]={}
    for log in folder.glob('*-codegen.log'):
        text=log.read_text();matches=[];current=None
        for line in text.splitlines():
            if 'Assembly listing for method ' in line:current=line.split('Assembly listing for method ',1)[1]
            if current and 'Total bytes of code ' in line:
                matches.append(dict(method=current,bytes=int(re.search(r'Total bytes of code (\d+)',line)[1])));current=None
        codegen[arch][log.name]=matches
        if log.name=='cache32-codegen.log':
            parts=text.split('; Assembly listing for method ')
            small=[p for p in parts if p.startswith('GameplayTags.Experiments.DirectArraySet:AppendDenseSmall(')]
            assert len(small)==1,'missing measured small-helper disassembly'
            (out/(arch+'-cached-helper-disassembly.txt')).write_text('; Assembly listing for method '+small[0])
            snapshots[arch]=[line for line in small[0].splitlines() if re.search(r'\b(sub|add|push|pop|stp|ldp)\b',line) and ('sp' in line or 'push' in line or 'pop' in line)][:28]

def stat(rows):
    return dict(cases=len(rows),fast=sum(r['faster5'] for r in rows),slow=sum(r['slower5'] for r in rows),AAstable=sum(r['AAstable'] for r in rows),fastStable=sum(r['faster5'] and r['AAstable'] for r in rows),slowStable=sum(r['slower5'] and r['AAstable'] for r in rows))
def changed(r):return r['operation'] in ('copy_append','reuse_copy_append') and r['left']=='Micro' and r['right']=='Dense' and r['members']>0
pairs=[('dispatch','stream'),('cache32','dispatch'),('cache32','stream'),('cache32','fixed'),('cache32','monotone')]
groups=[]
for arch in ('x64','arm64'):
    for cand,control in pairs:
        for stage in ('bench','boundary','scale','pairs'):
            base_rows=[r for r in data if r['architecture']==arch and r['candidate']==cand and r['control']==control and r['stage']==stage]
            for category in ('eligible','fallback','other'):
                def category_of(r):
                    if not changed(r):return 'other'
                    return 'eligible' if r['input'].get('rightMembers',r['members'])<=32 else 'fallback'
                rows=[r for r in base_rows if category_of(r)==category]
                if rows:groups.append(dict(architecture=arch,candidate=cand,control=control,stage=stage,group=category,**stat(rows)))
focus=[]
for r in values:
    s=json.loads(r['shape'])
    if r['label'].endswith('AA') or r['operation'] not in ('copy_append','reuse_copy_append') or s['left']!='Micro' or s['right']!='Dense':continue
    keep=(r['stage']=='bench' and s['u']==262144 and s['seed']==20261031 and ((s['n']==8 and s['dist']=='scattered') or s['n']==4096))
    keep|=(r['stage']=='boundary' and s['n'] in (31,32,33) and s['dist']=='scattered' and r['operation']=='copy_append')
    if keep:focus.append(dict(architecture=r['architecture'],stage=r['stage'],operation=r['operation'],universe=s['u'],members=s['n'],distribution=s['dist'],variant=r['label'],ns=float(r['ns']),bytes=float(r['bytes']),shape=r['shape']))
compact={}
for r in focus:
    key=tuple(r[k] for k in ('architecture','stage','operation','universe','members','distribution','shape'))
    item=compact.setdefault(key,{k:r[k] for k in ('architecture','stage','operation','universe','members','distribution')})
    item[r['variant']]=round(r['ns']/1000,4);item['bytes']=r['bytes']
focused=list(compact.values())
worst=[]
for arch in ('x64','arm64'):
    for cand,control in pairs:
        for scope in ('changed','other'):
            rows=[r for r in data if r['architecture']==arch and r['candidate']==cand and r['control']==control and r['slower5'] and (changed(r) if scope=='changed' else not changed(r))]
            worst.extend(sorted(rows,key=lambda r:r['ratio'],reverse=True)[:6])
result=dict(head=HEAD,main=main_meta,work=work_meta,groups=groups,focus=focused,worst=worst,codegen=codegen,stack_prologue=snapshots,checks=checks,work_hashes_match_both_platforms=True,all_negative_samples_retained=True,production_promoted=False,nativeUnity='not_run')
(out/'Readout.json').write_text(json.dumps(result,indent=2))
(out/'Source_Tested.zip').write_bytes((base/'Source_Tested.zip').read_bytes())
(out/'All_Comparisons.csv').write_bytes((base/'All_Comparisons.csv').read_bytes())
(out/'All_Architectures.csv').write_bytes((base/'All_Architectures.csv').read_bytes())
(out/'WorkCounts.json').write_text(json.dumps(work,indent=2))
text=['# PR22 栈记录缓存：已完成数据复核','',f'实际计时提交 `{HEAD}`；报告不改计时代码。完整运行 {MAIN_RUN}；不计时诊断 {WORK_RUN}。','',
'fixed/monotone/stream为原控制；dispatch只加入同等保护与调用层；cache32为128B局部栈载荷候选。32是实验存储边界，不能按本报告事后挑行调整。调用者仍可能支付旧fallback成本；源Count33但记录少也不缓存。真实Unity与四项全部领先均未验收。','',
'## 目标与边界（微秒/完整操作）','',table(focused,['architecture','stage','operation','universe','members','distribution','fixed','monotone','stream','dispatch','cache32','bytes']),'',
'## 分组验收','', '快慢为每轮主值与同二进制A/A最不利组合超过5%的筛选，不是置信区间。eligible按源成员数<=32；fallback为真正修改入口但源成员更多；other保留其他路径，不删除未改源码的回退。','',table(groups,['architecture','candidate','control','stage','group','cases','fast','slow','AAstable','fastStable','slowStable']),'',
'## 持续回退样本','',table(worst,['architecture','candidate','control','stage','operation','universe','members','distribution','baseline_ns','candidate_ns','ratio','AAstable']),'',
'## 测试与复算','']
for arch,c in checks.items():
    text.append(f'{arch}: {c["aggregate_rows"]}汇总，{c["timing_samples"]}耗时样本，{c["zero_allocation_samples"]}零分配样本。源码树/生成/二进制/Count/容量/每轮中位数与原核对一致。')
    for n,d in c['tests'].items():text.append(f'- {n}: {d.get("assertions")} assertions, failures={d["failures"]}; cache={d.get("cacheTests")}')
text+=['','## 机器码与栈代价','', '128B只指32条uint记录的载荷，不是整个栈帧。以下为实际计时构建反汇编，完整小助手方法另存txt；不要将push、固定frame与动态stackalloc重复或遗漏计算。','', '```json',json.dumps(dict(codegen=codegen,stack_prologue=snapshots),indent=2),'```','',
'## 工作量，不是CPU测时','',table(work['rows'],['variant','shape','sizes','wordReads','emits','writes','finds','suffixMoves','growthCopies','cacheReads','cacheWrites']),'',
'插桩二进制只用于工作量，不进入性能计时。原源码SHA256已对照两个平台计时生成代码。读取字数减少不能自行推导ns。','',
'首轮6be01622在来源保护检查失败：旧PR15 direct仅供溯源，未具有后加的私有混合方法；3854fda5将对应方法比较限定到实际五个编译候选。缓存算法、已有共同源码保护不变。失败产物不参与本表。','',
'保留全部GC及慢样本；SDK8.0.425/.NET8.0.31/Release、单CPU affinity、关闭tiering和ReadyToRun。未执行真实Unity/IL2CPP/Burst/手机、多角色冷缓存、长期内存和全历史竞争矩阵，不批准生产替换。']
(out/'验收报告.md').write_text('\n'.join(text))
print('CACHE_REPORT_GROUPS',json.dumps(groups))
print('CACHE_REPORT_FOCUS',json.dumps(focused))
print('CACHE_REPORT_WORST',json.dumps(worst))
print('CACHE_REPORT_CODEGEN',json.dumps(dict(methods=codegen,stack=snapshots)))
print('CACHE_REPORT_TESTS',json.dumps({a:{n:{k:v for k,v in t.items() if k in ('assertions','failures','cacheTests','streamTests','cursorBytes','enumeratorBytes')} for n,t in c['tests'].items()} for a,c in checks.items()}))
print('CACHE_REPORT_VERIFIED',json.dumps(dict(head=HEAD,main=main_meta,work=work_meta)))
