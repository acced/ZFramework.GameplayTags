#!/usr/bin/env python3
"""Post-run acceptance, not another benchmark: preserve every original sample."""
from pathlib import Path
import csv,hashlib,io,json,os,subprocess,sys,zipfile
HEAD='49731dcc487a3168b2007252efa56d51f608f31f'
ARCHIVES={
 'review':(11227147539,'f519c95c2c9c8ed527bb9f6428782f614c4827c99308527eb0bf7ed3347719ee'),
 'work':(11226209841,'d6313f909c83988068acc23291d9eff28f2ffd0dbd099871ed81011bcba84a6a')}
NAMES=('monotone','stream','delta32','safe32','batch32')
def sha(b):return hashlib.sha256(b).hexdigest()
def unpack(z,path):
 path=path.resolve();path.mkdir(parents=True,exist_ok=True)
 for n in z.namelist():
  p=(path/n).resolve()
  if p!=path and path not in p.parents:raise ValueError('unsafe archive path')
 z.extractall(path)
def table(rows,keys):
 lines=['|'+'|'.join(keys)+'|','|'+'|'.join('---' for _ in keys)+'|']
 for r in rows:
  vals=[]
  for k in keys:
   v=r.get(k,'');vals.append((f'{v:.3f}' if isinstance(v,float) else str(v)).replace('|','/'))
  lines.append('|'+'|'.join(vals)+'|')
 return '\n'.join(lines)+'\n'
def main():
 out=Path(sys.argv[1]).resolve();out.mkdir(parents=True,exist_ok=True)
 repo=os.environ['GITHUB_REPOSITORY'];meta={}
 for key,(aid,digest) in ARCHIVES.items():
  info=json.loads(subprocess.check_output(['gh','api',f'/repos/{repo}/actions/artifacts/{aid}']))
  assert not info['expired'] and info['digest']=='sha256:'+digest
  data=subprocess.check_output(['gh','api',f'/repos/{repo}/actions/artifacts/{aid}/zip'])
  assert sha(data)==digest
  (out/(key+'-original.zip')).write_bytes(data)
  with zipfile.ZipFile(io.BytesIO(data)) as z:unpack(z,out/key)
  meta[key]=dict(id=aid,sha256=digest,bytes=len(data))
 review=out/'review';summary=json.loads((review/'Summary.json').read_text());assert summary['head']==HEAD
 primary=out/'primary'
 with zipfile.ZipFile(review/'main-original.zip') as z:unpack(z,primary)
 collection=json.loads((primary/'COLLECTION.json').read_text())
 for arch in ('x64','arm64'):
  result=primary/arch/'results';before=json.loads((result/'recheck.json').read_text());assert before['head']==HEAD
  p=subprocess.run(['python3',str(primary/'source/Audit~/DenseDeltaSafety/review.py'),'--root',str(result)],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
  (out/(arch+'-recheck.log')).write_text(p.stdout)
  assert p.returncode==0,p.stdout[-10000:]
  assert before==json.loads((result/'recheck.json').read_text())
 assert (primary/'x64/results/source.zip').read_bytes()==(primary/'arm64/results/source.zip').read_bytes()==(review/'Source_Tested.zip').read_bytes()
 work=json.loads((out/'work/WORK_COUNTS.json').read_text());assert work['instrumented'] and not work['timed']
 core_hashes={}
 for n in NAMES:
  a=(primary/'x64/results/generated'/n/'DirectArraySet.cs').read_bytes()
  b=(primary/'arm64/results/generated'/n/'DirectArraySet.cs').read_bytes()
  assert a==b and sha(a)==work['original_core_hashes'][n]
  core_hashes[n]=sha(a)
  folder=out/'delivery/Generated'/n;folder.mkdir(parents=True,exist_ok=True)
  (folder/'DirectArraySet.cs').write_bytes(a)
  (folder/'BulkBuilders.cs').write_bytes((primary/'x64/results/generated'/n/'BulkBuilders.cs').read_bytes())
 # Retain the exact results and fault boundaries instead of aggregating them away.
 comparisons=[]
 with (review/'All_Comparisons.csv').open(encoding='utf-8-sig',newline='') as f:
  for row in csv.DictReader(f):
   r=dict(row);r['shape']=json.loads(row['shape'])
   for k in ('baseline_ns','candidate_ns','ratio','bytes'):r[k]=float(row[k])
   for k in ('universe','members'):r[k]=int(row[k])
   for k in ('faster5','slower5','AAstable'):r[k]=row[k]=='True'
   comparisons.append(r)
 selected=[]
 for r in comparisons:
  if r['candidate'] not in ('safe32','batch32') or r['control'] not in ('delta32','monotone','stream'):continue
  s=r['shape'];u=r['universe'];n=r['members'];stage=r['stage'];dist=r['distribution']
  keep=(stage=='bench' and u==262144 and n in (8,128,4096) and s['seed']==20261031 and dist in ('contiguous','scattered')) or (stage=='focused' and u==262144 and n in (31,32,33,64,65) and dist=='scattered') or (stage=='scale' and u==262144 and n==4096)
  if keep and r['operation'] in ('copy_append','reuse_copy_append') and r['left']=='Micro' and r['right']=='Dense':
   selected.append(dict(architecture=r['architecture'],candidate=r['candidate'],control=r['control'],stage=stage,members=n,distribution=dist,operation=r['operation'],baseline_us=r['baseline_ns']/1000,candidate_us=r['candidate_ns']/1000,bytes=r['bytes'],fast=r['faster5'],slow=r['slower5'],stable=r['AAstable']))
 groups=[r for r in summary['groups'] if r['candidate'] in ('safe32','batch32') and r['control'] in ('delta32','monotone','stream')]
 worst=sorted([r for r in comparisons if r['candidate'] in ('safe32','batch32') and r['slower5']],key=lambda r:r['ratio'],reverse=True)
 work_focus=[r for r in work['rows'] if r['variant'] in ('monotone','stream','delta32','safe32','batch32') and r['sizes'] in ('8/8','4096/4096','4096/2048')]
 fault=[{k:v for k,v in r.items() if k!='minimal'} for r in summary['faults']]
 tests={arch:{n:{k:v for k,v in d.items() if k in ('assertions','failures','batchTests','cacheTests')} for n,d in ds.items()} for arch,ds in summary['tests'].items()}
 final=dict(head=HEAD,artifacts=meta,source_hashes=core_hashes,archived_review_repeated_equal=True,work_source_matches_both_timed=True,counts=summary['counts'],faults=summary['faults'],groups=groups,focus=selected,work=work['rows'],tests=tests,codegen=summary['codegen'],stack=summary['stack'],AA=summary['AA'],regression_count=len(worst),production_promoted=False,nativeUnity='not_run')
 d=out/'delivery'
 for filename in ('Source_Tested.zip','All_Comparisons.csv','All_Architectures.csv','All_Regressions.json'):(d/filename).write_bytes((review/filename).read_bytes())
 (d/'Acceptance.json').write_text(json.dumps(final,indent=2))
 (d/'Work_Counts.json').write_text(json.dumps(work,indent=2))
 report='# PR24：冻结代码、故障一致性与分批代价复核\n\n'
 report+=f'计时提交 `{HEAD}`。本次不改算法、不重测耗时；重新下载固定原始ZIP并重跑归档review，两个架构结构化结果一致。工作量诊断原源码与两个被计时平台逐字节匹配。\n\n'
 report+='所有快/慢为原三轮含A/A最不利组合的5%筛选，不是置信区间。所有负面测点保留。不能用正常测试通过推断Unity、发布或所有故障路径正确。\n\n## 故障范围\n'
 report+=table(fault,['architecture','variant','cases','injected','scopedFailures','invalidTotal','invalidEmpty','invalidNonempty'])
 report+='\nscopedFailures只覆盖指定非空新助手；空目标旧CopyCore与safe32大源旧stream的观察必须单独保留。注入数组分配失败不是实际进程内存耗尽恢复测试。\n\n## 已修改路径与其他路径\n'
 report+=table(groups,['architecture','candidate','control','stage','group','cases','fast','slow','stable','fastStable','slowStable'])
 report+='\n## 指定同次输入完整耗时（微秒）\n'+table(selected,['architecture','candidate','control','stage','members','distribution','operation','baseline_us','candidate_us','bytes','fast','slow','stable'])
 report+='\n## 不计时的工作量\n'+table(work_focus,['variant','shape','sizes','wordReads','emits','writes','finds','suffixMoves','growthCopies'])
 report+='\n工作量从克隆之后开始计，仅覆盖追加；不是CPU周期、分配大小或完整API时间。suffixMoves含批量Array.Copy。不能将单次Dense遍历等同于目标只移动一次。\n\n## 交付边界\n同名生成类互斥编译。Source_Tested.zip无Git历史，再生成需固定提交完整历史。生产文件不变；没有合并、发布、全四指标领先或真实Unity/IL2CPP/Burst/手机验收。\n'
 (d/'验收复核.md').write_text(report)
 print('ACCEPTANCE_COUNTS '+json.dumps(summary['counts']),flush=True)
 print('ACCEPTANCE_FAULTS '+json.dumps(summary['faults']),flush=True)
 print('ACCEPTANCE_GROUPS '+json.dumps(groups),flush=True)
 # Keep logs compact; complete raw data stays in delivery.
 print('ACCEPTANCE_FOCUS '+json.dumps([r for r in selected if r['operation']=='copy_append' and r['control']=='monotone' and (r['stage']!='focused' or r['members'] in (32,33))]),flush=True)
 print('ACCEPTANCE_SAFE '+json.dumps([r for r in selected if r['candidate']=='safe32' and r['control']=='delta32' and r['stage']=='bench' and r['members']==8]),flush=True)
 print('ACCEPTANCE_WORK '+json.dumps(work_focus),flush=True)
 print('ACCEPTANCE_CODEGEN '+json.dumps(summary['codegen']),flush=True)
 print('ACCEPTANCE_WORST '+json.dumps([{k:r[k] for k in ('architecture','candidate','control','stage','operation','members','distribution','baseline_ns','candidate_ns','ratio','AAstable')} for r in worst[:12]]),flush=True)
 print('ACCEPTANCE_OK '+HEAD,flush=True)
if __name__=='__main__':main()
