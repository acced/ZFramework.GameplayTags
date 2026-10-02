#!/usr/bin/env python3
"""Explain exact changed paths, both controls, runtime/stack/code costs and failures."""
import pathlib,json,csv,re,sys
from collections import defaultdict

def main():
    root=pathlib.Path(sys.argv[1]);collection=json.loads((root/'COLLECTION.json').read_text())
    with (root/'All_Comparisons.csv').open(encoding='utf-8-sig',newline='') as f:rows=list(csv.DictReader(f))
    for r in rows:
        r['shape']=json.loads(r['shape'])
        for key in ('faster5','slower5','AAstable'):r[key]=r[key]=='True'
        for key in ('baseline_ns','candidate_ns','ratio','bytes'):r[key]=float(r[key])
    groups=defaultdict(list)
    for r in rows:
        if r['candidate']!='cache32':continue
        s=r['shape'];group='unchanged_operation'
        if r['operation'] in ('copy_append','reuse_copy_append') and s['left']=='Micro' and s['right']=='Dense' and s['n']!=0:
            right=s.get('rightMembers',s['n'])
            group='cache_eligible' if right<=32 else 'fallback'
        elif r['stage']=='pairs':group='paid_pair_lifecycle'
        groups[(r['architecture'],r['control'],r['stage'],group)].append(r)
    summaries=[]
    for k,rs in sorted(groups.items()):
        summaries.append(dict(zip(('architecture','control','stage','group'),k),cases=len(rs),fast=sum(r['faster5'] for r in rs),slow=sum(r['slower5'] for r in rs),stable=sum(r['AAstable'] for r in rs),fast_stable=sum(r['faster5'] and r['AAstable'] for r in rs),slow_stable=sum(r['slower5'] and r['AAstable'] for r in rs)))
    focus=[r for r in rows if r['candidate']=='cache32' and r['control'] in ('stream','monotone','fixed') and r['operation']=='copy_append' and r['shape']['u']==262144 and ((r['stage']=='bench' and r['shape']['n'] in (8,128,4096) and r['shape']['dist']=='scattered' and r['shape']['left']=='Micro' and r['shape']['right']=='Dense' and r['shape']['seed']==20261031) or (r['stage']=='boundary' and r['shape']['n'] in (31,32,33) and r['shape']['dist'] in ('scattered','large_left')))]
    codegen={}
    for arch in ('x64','arm64'):
        codegen[arch]={}
        for p in (root/arch/'results').glob('*-codegen.log'):
            text=p.read_text();records=[]
            for seg in text.split('; Assembly listing for method ')[1:]:
                lines=seg.splitlines();size=re.search(r'Total bytes of code (\d+)',seg)
                records.append(dict(method=lines[0],code_bytes=int(size[1]) if size else None,frame_instructions=[line.strip() for line in lines if re.search(r'\b(sub\s+rsp|sub\s+sp|stp\s+x29|add\s+rbp|lea\s+rbp)',line)]))
            codegen[arch][p.name]=records
    result=dict(commit=collection['architectures']['x64']['head'],groups=summaries,focus=focus,codegen=codegen,threshold=32,threshold_fitted=False,stack_payload_bytes_micro16=128,production_promoted=False,nativeUnity='not_run')
    (root/'Cache_Readout.json').write_text(json.dumps(result,indent=2))
    table=['|平台|对照|阶段|路径组|测点|持续快|持续慢|A/A稳定|快且稳定|','|---|---|---|---|---:|---:|---:|---:|---:|']
    for r in summaries:table.append('|'+ '|'.join(str(r[k]) for k in ('architecture','control','stage','group','cases','fast','slow','stable','fast_stable'))+'|')
    text='# Dense 小源栈缓存：原始数据分组\n\n计时提交：`'+str(result['commit'])+'`。全部对照重新计时；三轮5%为筛选，不是置信区间。eligible按实际源成员数<=32判定，操作为Micro接收非空Dense；fallback仍需计入新增调用分派。boundary的n表示指定源规模，large_left实际左侧4096成员，详见shape.leftMembers。\n\n'+'\n'.join(table)+'\n\n## 所有目标样本\n\n```json\n'+json.dumps(focus,ensure_ascii=False,indent=2)+'\n```\n\n栈缓存载荷128B不等于完整栈帧，实际方法反汇编见Cache_Readout.json及原始日志。全部不利数据在All_Regressions.json。未运行Unity/IL2CPP/Burst/手机，不是发布批准。\n'
    (root/'分组结果.md').write_text(text)
    print('CACHE_READOUT_HEAD',result['commit']);print('CACHE_READOUT_GROUPS',json.dumps(summaries))
    print('CACHE_READOUT_FOCUS',json.dumps(focus))
if __name__=='__main__':main()
