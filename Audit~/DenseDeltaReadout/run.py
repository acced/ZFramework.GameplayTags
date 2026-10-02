#!/usr/bin/env python3
"""Post-run only. Download completed frozen evidence, repeat verifier, emit grounded tables."""
from pathlib import Path
import subprocess,os,json,zipfile,io,hashlib,csv,re,sys
RUN=37006439435
HEAD='49731dcc487a3168b2007252efa56d51f608f31f'
FAULT_ARTIFACT=11226505547
FAULT_DIGEST='6e0b0e6e136a3f7cb944babb84a20c16ed1c225bf6ace1a387ab4de8798bc90e'
NAMES=('monotone','stream','delta32','safe32','batch32')
def api(path):return subprocess.check_output(['gh','api',path])
def sha(data):return hashlib.sha256(data).hexdigest()
def extract(z,folder):
    folder=folder.resolve();folder.mkdir(parents=True,exist_ok=True)
    for name in z.namelist():
        p=(folder/name).resolve()
        assert p==folder or folder in p.parents
    z.extractall(folder)
def stats(rows):
    return dict(cases=len(rows),fast=sum(r['fast'] for r in rows),slow=sum(r['slow'] for r in rows),stable=sum(r['stable'] for r in rows),fastStable=sum(r['fast'] and r['stable'] for r in rows),slowStable=sum(r['slow'] and r['stable'] for r in rows))
def table(rows,keys):
    if not rows:return '(none)\n'
    result=['|'+'|'.join(keys)+'|','|'+'|'.join('---' for _ in keys)+'|']
    for row in rows:
        result.append('|'+'|'.join(f'{row[k]:.3f}' if isinstance(row[k],float) else str(row[k]).replace('|','/') for k in keys)+'|')
    return '\n'.join(result)+'\n'
def main():
    out=Path(sys.argv[1]).resolve();out.mkdir(parents=True,exist_ok=True)
    repo=os.environ['GITHUB_REPOSITORY'];artifacts=json.loads(api(f'/repos/{repo}/actions/runs/{RUN}/artifacts'))['artifacts']
    item=next(a for a in artifacts if a['name']==f'dense-delta-safety-reviewed-{RUN}' and not a['expired'])
    data=api(f'/repos/{repo}/actions/artifacts/{item["id"]}/zip');digest=sha(data);assert item['digest']=='sha256:'+digest
    (out/'main-original.zip').write_bytes(data)
    root=out/'main'
    with zipfile.ZipFile(io.BytesIO(data)) as z:extract(z,root)
    c=json.loads((root/'COLLECTION.json').read_text())
    assert c['architectures']['x64']['head']==c['architectures']['arm64']['head']==HEAD
    for arch in ('x64','arm64'):
        results=root/arch/'results';old=json.loads((results/'recheck.json').read_text())
        p=subprocess.run(['python3',str(root/'source/Audit~/DenseDeltaSafety/review.py'),'--root',str(results)],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
        (out/(arch+'-additional-recheck.log')).write_text(p.stdout)
        assert p.returncode==0,p.stdout[-16000:]
        assert old==json.loads((results/'recheck.json').read_text())
    fault=api(f'/repos/{repo}/actions/artifacts/{FAULT_ARTIFACT}/zip');assert sha(fault)==FAULT_DIGEST
    (out/'fault-original.zip').write_bytes(fault)
    with zipfile.ZipFile(io.BytesIO(fault)) as z:
        fsum=json.loads(z.read('results/FaultOnlySummary.json'))
    for arch in ('x64','arm64'):
        for name in NAMES:
            core=(root/arch/'results/generated'/name/'DirectArraySet.cs').read_bytes()
            assert sha(core)==fsum['sources'][name]['original']
    comparisons=[]
    with (root/'All_Comparisons.csv').open(encoding='utf-8-sig') as f:
        for row in csv.DictReader(f):
            r=dict(row,shape=json.loads(row['shape']))
            r.update(fast=row['faster5']=='True',slow=row['slower5']=='True',stable=row['AAstable']=='True')
            for k in ('universe','members'):r[k]=int(row[k])
            for k in ('baseline_ns','candidate_ns','bytes','ratio'):r[k]=float(row[k])
            comparisons.append(r)
    groups=[]
    pairs=sorted({(r['candidate'],r['control']) for r in comparisons})
    def changed(r):return r['operation'] in ('copy_append','reuse_copy_append') and r['left']=='Micro' and r['right']=='Dense' and r['members']>0
    def rhs(r):return r['shape'].get('sourceMembers',r['shape'].get('rightMembers',r['members']//2 if r['stage']=='scale' and r['distribution']=='subset' else r['members']))
    for arch in ('x64','arm64'):
        for cand,control in pairs:
            for stage in ('bench','pairs','scale','focused','holdout'):
                rs=[r for r in comparisons if (r['architecture'],r['candidate'],r['control'],r['stage'])==(arch,cand,control,stage)]
                for label,part in (('changed', [r for r in rs if changed(r)]),('other',[r for r in rs if not changed(r)])):
                    if part:groups.append(dict(architecture=arch,candidate=cand,control=control,stage=stage,group=label,**stats(part)))
    focus=[]
    with (root/'All_Architectures.csv').open(encoding='utf-8-sig') as f:
        for r in csv.DictReader(f):
            if r['label'].endswith('AA') or r['operation'] not in ('copy_append','reuse_copy_append') or r['left']!='Micro' or r['right']!='Dense':continue
            s=json.loads(r['shape']);stage=r['stage'];n=int(r['members']);u=int(r['universe']);seed=s['seed'];dist=r['distribution']
            selected=(stage=='bench' and u==262144 and n in (8,128,4096) and seed==20261031 and dist in ('contiguous','scattered')) or (stage=='focused' and u==262144 and n in (31,32,33,64,65) and dist=='scattered') or (stage=='scale' and u==262144 and n==4096)
            if selected:focus.append(dict(architecture=r['architecture'],stage=stage,members=n,sourceMembers=s.get('rightMembers',n),distribution=dist,operation=r['operation'],variant=r['label'],us=float(r['ns'])/1000,bytes=float(r['bytes']),round_medians=json.loads(r['round_medians'])))
    faults=[];tests={};codegen={};stack={}
    for arch in ('x64','arm64'):
        a=c['architectures'][arch];tests[arch]=a['tests'];codegen[arch]={};stack[arch]={}
        for name,d in a['faults'].items():
            faults.append(dict(architecture=arch,variant=name,cases=d['cases'],injected=d['exceptions'],scopedFailures=d['scopedFailures'],invalidTotal=len(d['observations']),invalidEmpty=sum(r['targetBefore']==0 for r in d['observations']),invalidNonempty=sum(r['targetBefore']>0 for r in d['observations']),minimal=[r for r in d['observations'] if r['shape']=='minimal']))
        for name in NAMES:
            text=(root/arch/'results'/(name+'-codegen.log')).read_text()
            methods=[];frames={}
            for match in re.finditer(r'Assembly listing for method ([^\n]+)\n(.*?); Total bytes of code (\d+)',text,re.S):
                method,body,size=match.group(1),match.group(2),int(match.group(3))
                if any(t in method for t in ('AppendDense','MergeMissing')):
                    methods.append(dict(method=method,bytes=size))
                    frames[method]=[line.strip() for line in body.splitlines() if re.search(r'\b(push|pop)\s|sub\s+(rsp|sp),|stp.*\[sp|add\s+sp,',line)]
            codegen[arch][name]=methods;stack[arch][name]=frames
    worst=sorted([r for r in comparisons if r['slow']],key=lambda r:r['ratio'],reverse=True)
    summary=dict(head=HEAD,run=RUN,artifact=item['id'],main_sha256=digest,main_bytes=len(data),fault_sha256=FAULT_DIGEST,extra_recheck_equal=True,diagnostic_core_matches_timing=True,groups=groups,focus=focus,faults=faults,tests=tests,codegen=codegen,stack=stack,AA={arch:c['architectures'][arch]['AA'] for arch in ('x64','arm64')},counts={arch:{k:c['architectures'][arch][k] for k in ('aggregate_rows','raw_rows','timing_samples','zero_allocation_samples')} for arch in ('x64','arm64')},worst=worst[:60],nativeUnity='not_run',production_promoted=False)
    (out/'Summary.json').write_text(json.dumps(summary,indent=2))
    (out/'Source_Tested.zip').write_bytes((root/'Source_Tested.zip').read_bytes())
    (out/'All_Comparisons.csv').write_bytes((root/'All_Comparisons.csv').read_bytes())
    (out/'All_Architectures.csv').write_bytes((root/'All_Architectures.csv').read_bytes())
    (out/'All_Regressions.json').write_bytes((root/'All_Regressions.json').read_bytes())
    report='# PR24 故障一致性与固定缓冲分批：冻结原始数据读出\n\n'
    report+=f'计时提交 `{HEAD}`；主运行 {RUN}；原收集包 SHA256 `{digest}`。再次运行原归档review，输出一致；独立故障诊断五份原核心与两平台计时代码哈希一致。\n\n'
    report+='下表快/慢是三轮含同二进制A/A最不利组合的5%筛选，不是置信区间；没有排除不稳定或不利样本。只检查注入的数组增长失败，不代表真实内存耗尽可恢复。\n\n## 故障结果\n'
    report+=table(faults,['architecture','variant','cases','injected','scopedFailures','invalidTotal','invalidEmpty','invalidNonempty'])
    report+='\nscopedFailures为新助手范围；invalidEmpty保留旧CopyCore问题，safe32大源保留旧stream问题。不能将零新增失败称作整库异常安全。\n\n## 完整分组\n'
    report+=table(groups,['architecture','candidate','control','stage','group','cases','fast','slow','stable','fastStable','slowStable'])
    report+='\n## 固定输入耗时（微秒）\n'+table(focus,['architecture','stage','members','distribution','operation','variant','us','bytes'])
    report+='\n## 边界\n五个单独编译候选，不按行挑赢家。safe32只修小助手Count；batch32固定128B载荷分批，无源数量阈值，但可能重复搬移目标尾部。缓存容量不是全部栈帧；实际指令列在Summary.json。正常所有权/容量/分配相同，异常只保证指定非空路径的有效部分结果。Unity/IL2CPP/Burst/手机、冷角色与长期内存、历史全部库对照未运行；生产代码未迁移。\n'
    (out/'结果读出.md').write_text(report)
    for key in ('counts','faults','groups','focus','codegen'):
        print('READOUT_'+key.upper()+' '+json.dumps(summary[key]),flush=True)
    print('READOUT_WORST '+json.dumps([{k:r[k] for k in ('architecture','candidate','control','stage','operation','members','distribution','baseline_ns','candidate_ns','ratio','stable')} for r in worst[:30]]),flush=True)
if __name__=='__main__':main()
