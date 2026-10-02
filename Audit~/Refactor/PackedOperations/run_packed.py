#!/usr/bin/env python3
"""Source-only staging by default; execution requires the timing coordinator handoff."""
import argparse,datetime,functools,hashlib,io,json,math,os,shutil,statistics,subprocess,sys,tarfile,time
from pathlib import Path
from xml.sax.saxutils import escape
HERE=Path(__file__).resolve().parent
REFERENCE_COMMIT='2fc862b91f9c53362e3fb63c7c086f7501c54ba9'
SDK_VERSION='8.0.425'
SCHEMA='prepared-packed-operations-two-source-v1'
SOURCES=('baseline','candidate');BACKENDS=('hardware','portable');MODES=('Compressed','Sparse','Dense','Bulk');UNITS=16
OPS=('union.into','intersection.into','difference.into','append.reuse','remove.reuse','union.left_alias','union.right_alias','intersection.left_alias','intersection.right_alias','difference.left_alias','difference.right_alias')
CELLS=((65536,4096,4096,0,'clusters4'),(65536,4096,4096,50,'clusters4'),(65536,4096,4096,100,'clusters4'),(65536,4096,4096,0,'scattered'),(65536,4096,4096,50,'scattered'),(65536,4096,4096,100,'scattered'),(65536,4096,4096,5,'clusters4'),(65536,4096,4096,5,'scattered'),(65536,4096,4096,50,'contiguous'),(262144,16384,16384,50,'clusters4'),(262144,4096,128,50,'clusters4'),(262144,128,4096,50,'clusters4'))
TIGHT=(CELLS[1],CELLS[7],CELLS[10],CELLS[11])
def require(x,m):
 if not x:raise ValueError(m)
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def digest(v):return hashlib.sha256(json.dumps(v,sort_keys=True,separators=(',',':')).encode()).hexdigest()
def inv(root):return {str(p.relative_to(root)):sha(p) for p in sorted(Path(root).rglob('*')) if p.is_file()}
def integer(x,minimum=0,maximum=None):return type(x) is int and x>=minimum and (maximum is None or x<=maximum)
def finite(x):return type(x) in (int,float) and math.isfinite(x) and x>=0
def write(p,v):Path(p).write_text(json.dumps(v,indent=2,allow_nan=False)+'\n')
def read(p):
 def pairs(items):
  d={}
  for k,v in items:require(k not in d,'Duplicate JSON field');d[k]=v
  return d
 def bad(v):raise ValueError('Nonfinite JSON '+v)
 return json.loads(Path(p).read_text(),object_pairs_hook=pairs,parse_constant=bad)
def plan():
 rows=[]
 def add(cell,mode,op,tight=False):
  u,n,m,o,pat=cell;r=dict(universe=u,leftCount=n,rightCount=m,overlap=o,family='tight_capacity' if tight else 'declared_kernel_shape',pattern=pat,capacityPolicy='exact_union' if tight else 'upper',order='rotating16',requestedStorage=mode,operation=OPS[op])
  r['key']='/'.join(str(r[k]) for k in ('universe','leftCount','rightCount','overlap','family','pattern','capacityPolicy','order','requestedStorage','operation'));rows.append(r)
 for c in CELLS:
  for mode in MODES:
   for op in range(11 if mode=='Compressed' else 5):add(c,mode,op)
 for c in TIGHT:
  for op in (0,1,2,5,6,7,8,9,10):add(c,'Compressed',op,True)
 for c in TIGHT:
  for op in range(5):
   add(c,'Compressed',op,True);rows[-1]['family']='tight_records';rows[-1]['capacityPolicy']='actual_union_records';rows[-1]['key']=rows[-1]['key'].replace('/tight_capacity/','/tight_records/').replace('/exact_union/','/actual_union_records/')
 for cell,op in ((CELLS[7],1),(CELLS[2],2),(CELLS[0],1),(CELLS[5],2)):
  add(cell,'Compressed',op,True);rows[-1]['family']='small_filter_result';rows[-1]['capacityPolicy']='actual_result_records';rows[-1]['key']=rows[-1]['key'].replace('/tight_capacity/','/small_filter_result/').replace('/exact_union/','/actual_result_records/')
 require(len(rows)==372 and len({r['key'] for r in rows})==372,'Plan inventory');return rows
PLAN=plan();BY_KEY={r['key']:r for r in PLAN}
@functools.lru_cache(None)
def candidates(u,pattern,seed,n):
 leaves=tuple(2+i+i//64 for i in range(u))
 if pattern=='contiguous':
  start=(u-2*n)//3;return leaves[start:]+leaves[:start]
 if pattern=='clusters4':return tuple(leaves[(i%4)*(u//4)+i//4] for i in range(u))
 a=list(leaves);state=seed
 for i in range(len(a)-1,0,-1):
  bound=i+1;threshold=((1<<32)-bound)%bound
  while True:
   state=(state^(state<<13))&0xffffffff;state^=state>>17;state=(state^(state<<5))&0xffffffff
   if state>=threshold:break
  j=state%bound;a[i],a[j]=a[j],a[i]
 return tuple(a)
@functools.lru_cache(None)
def fixture_base(u,n,m,o,pattern):
 x=tuple(sorted(candidates(u,pattern,20261002+n,n)[:n]));owned=set(x);shared=min(n,m)*o//100
 y=[x[i*n//max(1,shared)] for i in range(shared)]
 for v in candidates(u,pattern,20261002+n+m+29,m):
  if len(y)==m:break
  if v not in owned:y.append(v)
 y=tuple(sorted(y));other=set(y);union=tuple(sorted(owned|other));inter=tuple(v for v in x if v in other);diff=tuple(v for v in x if v not in other);h=hashlib.sha256((','.join(map(str,x))+'|'+','.join(map(str,y))).encode()).hexdigest()
 return x,y,union,inter,diff,h
def fixture(r):return fixture_base(r['universe'],r['leftCount'],r['rightCount'],r['overlap'],r['pattern'])
def layout(u,ids,mode,ratio):
 if mode!='Bulk':return mode
 n=len(ids);records=len({i>>4 for i in ids});words=(u+63)//64
 if n>=64 and 4*records<=n:return 'Dense' if words<=ratio*records else 'Compressed'
 return 'Dense' if n>=64 and words<=2*n else 'Sparse'
def shapes(r,ratio):
 x,y,union,inter,diff,h=fixture(r);u=r['universe']+r['universe']//64+1;maxrecords=(u+15)//16;filter_out=inter if r['operation']=='intersection.into' else diff;reserve=len({i>>4 for i in filter_out}) if r['capacityPolicy']=='actual_result_records' else len({i>>4 for i in union}) if r['capacityPolicy']=='actual_union_records' else len(union) if r['capacityPolicy']=='exact_union' else len(x)+len(y);mode=r['requestedStorage'];a=layout(u,x,mode,ratio);b=layout(u,y,mode,ratio)
 work=mode
 if mode=='Bulk':work='Dense' if 'Dense' in (a,b) else 'Compressed' if 'Compressed' in (a,b) else 'Dense' if len(x)+len(y)>=64 and (u+63)//64<=2*(len(x)+len(y)) else 'Sparse'
 out=inter if r['operation'] in ('intersection.into','intersection.left_alias','intersection.right_alias') else diff if r['operation'] in ('difference.into','remove.reuse','difference.left_alias','difference.right_alias') else union
 def shape(ids,storage,capacity):
  count=len(ids);records=len({i>>4 for i in ids}) if storage=='Compressed' else 0
  slots=min(capacity,maxrecords) if storage=='Compressed' else capacity
  reserved=u if storage=='Dense' or storage=='Compressed' and slots==maxrecords else slots
  return dict(storage=storage,members=count,capacity=max(count,reserved) if storage=='Compressed' else reserved,bufferBytes=8*((u+63)//64) if storage=='Dense' else 4*slots,recordCount=records,recordCapacity=slots if storage=='Compressed' else 0,reservedMemberCapacity=reserved)
 sa=shape(x,a,len({i>>4 for i in x}) if mode=='Bulk' and a=='Compressed' else len(x));sb=shape(y,b,len({i>>4 for i in y}) if mode=='Bulk' and b=='Compressed' else len(y));sw=shape(out,work,reserve)
 return sa,sb,sw,out,h,u

def validate(p,m,label,source,backend,round_id):
 for k,v in dict(source=source,label=label,round=round_id,samples=m['samples'],targetMs=m['target_ms'],seed=20261002,gcMode='Batch',gcConcurrent='0',readyToRun='0',tieredCompilation='0',tieredPgo='0',validation_protocol='actual-final-actor-output-before-reset-packed-operations-v1',portableKernelsForced=backend=='portable').items():require((finite(p.get(k)) if k=='targetMs' else type(p.get(k)) is type(v)) and p[k]==v,'Payload '+k)
 for k,v in m['attribution'][source+'-'+backend].items():require(p.get(k)==v,'Attribution '+k)
 for k in ('assertions','stopwatchFrequency'):require(integer(p.get(k),1),'Invalid '+k)
 require(integer(p.get('failures')),'Failures type');require(type(p.get('vectorHardwareAccelerated')) is bool and integer(p.get('vectorIntWidth'),1) and type(p.get('serverGC')) is bool and integer(p.get('denseWordsPerPackedRecord'),1),'Capability types')
 require(type(p.get('runtime')) is str and p['runtime'].startswith('.NET 8.') and p.get('architecture') in ('X64','Arm64') and type(p.get('os')) is str,'Host cohort types')
 require(type(p.get('avx2Supported')) is bool and type(p.get('arm64AdvSimdSupported')) is bool,'Explicit backend capability types')
 expected_ratio=1 if backend=='portable' else 4 if p['avx2Supported'] else 2 if p['arm64AdvSimdSupported'] else 1
 require(p['denseWordsPerPackedRecord']==expected_ratio,'Backend ratio contradicts compile mode/capabilities')
 c=p['controls'];require(integer(c.get('negative_copy_bytes')) and c['negative_copy_bytes']==0 and integer(c.get('positive_new_array_bytes'),4096),'Allocation controls')
 keys=[r['key'] for r in PLAN];require(p['expectedKeys']==keys and p['executionKeys']==(keys if round_id%2==0 else list(reversed(keys))),'Plan/order differs')
 rows=p['rows'];require(len(rows)==372 and [r['key'] for r in rows]==p['executionKeys'],'Missing/duplicate/order rows')
 for r in rows:
  expected=BY_KEY[r['key']]
  for k,v in expected.items():require(type(r.get(k)) is type(v) and r[k]==v,'Row metadata '+k)
  sa,sb,sw,out,h,u=shapes(r,p['denseWordsPerPackedRecord']);require(r['digest']==h,'Input digest')
  require(r['status'] in ('measured','failed') and integer(r['samplesCompleted'],0,m['samples']),'Row status/count');count=r['samplesCompleted']
  require(integer(r.get('iterationLimit')) and integer(r.get('estimatedBytesPerSweep')) and finite(r.get('calibrationMs')) and integer(r.get('allocationCapBytes'),1) and type(r.get('limitReached')) is bool,'Calibration metadata')
  fields=('ns','allocated_bytes','elapsed_ticks','batch_ms','returned_values','gc_collections');require(all(isinstance(r.get(k),list) and len(r[k])==count for k in fields),'Partial sample length')
  if count:require(integer(r['iterations'],1) and r['units']==UNITS,'Denominator')
  for i in range(count):
   require(finite(r['ns'][i]) and finite(r['allocated_bytes'][i]) and finite(r['batch_ms'][i]) and integer(r['elapsed_ticks'][i]),'Raw sample')
   require(integer(r['returned_values'][i],-(1<<31),(1<<31)-1),'Return type');require(type(r['gc_collections'][i]) is list and len(r['gc_collections'][i])==3 and all(integer(v) for v in r['gc_collections'][i]),'GC sample')
   require(math.isclose(r['ns'][i],r['elapsed_ticks'][i]*1e9/p['stopwatchFrequency']/(r['iterations']*UNITS),rel_tol=1e-12,abs_tol=1e-9),'Timing denominator');require(math.isclose(r['batch_ms'][i],r['elapsed_ticks'][i]*1000/p['stopwatchFrequency'],rel_tol=1e-12,abs_tol=1e-9),'Batch duration')
  if r['status']=='failed':require(isinstance(r.get('error'),str) and r['error'],'Missing failure');continue
  require(count==m['samples'] and r['timed_output_validated'] is True,'Actual unvalidated')
  expected_limit=max(1,min(1<<14,(32<<20)//r['estimatedBytesPerSweep'])) if r['estimatedBytesPerSweep'] else 1<<14
  require(r['allocationCapBytes']==32<<20 and r['iterationLimit']==expected_limit and 1<=r['iterations']<=expected_limit,'Calibration cap/limit formula')
  require(r['limitReached'] is (r['iterations']==expected_limit),'Calibration limitReached mismatch')
  require(r['calibrationMs']>=m['target_ms'] or r['limitReached'],'Calibration stopped before target/limit')
  require(r['iterations']==expected_limit or r['iterations']&(r['iterations']-1)==0,'Calibration doubling schedule')
  expected_value=((len(out)*40+17*UNITS)*r['iterations'])&0xffffffff
  if expected_value>=1<<31:expected_value-=1<<32
  require(all(v==expected_value for v in r['returned_values']),'Wrong actual checksum');require(r['zero_allocation_required'] is True and all(v==0 for v in r['allocated_bytes']),'Prepared allocation contract')
  d=r['details'];require(d['actualRegistryCount']==u and d['sourceInputDigest']==h and d['requestedStorage']==r['requestedStorage'] and d['retainedInstances']==UNITS and d['activeActors']==UNITS,'Fixture details');require(len(d['actors'])==UNITS,'Actor count')
  for i,a in enumerate(d['actors']):
   require(integer(a['id']) and a['id']==i,'Actor identity type')
   for name in ('left','right','work','result'):
    sh=a.get(name);require(type(sh) is dict and set(sh)==set(sa) and type(sh.get('storage')) is str and all(integer(sh.get(k)) for k in sa if k!='storage'),'Shape types')
   require(a['id']==i and a['left']==sa and a['right']==sb and a['work']==sw and a['result']==sw,'Actual forced/policy shape/count/capacity mismatch')
 require(p['failures']==sum(r['status']=='failed' for r in rows),'Failure total');return rows

def pct(v,q):
 v=sorted(v);at=(len(v)-1)*q;i=int(at);return v[i]+(v[min(i+1,len(v)-1)]-v[i])*(at-i)
def aggregate(out,m):
 for source,inventory in m['source_files'].items():require(inv(out/'sources'/source)==inventory,'Source changed')
 for f,h in m['support_files'].items():require(sha(out/'sources'/f)==h,'Support changed')
 for at in m['attribution'].values():require(sha(at['executablePath'])==at['executableSha256'],'Executable changed')
 exits=read(out/'process-exits.json');require(len(exits)==24 and len({(x['label'],x['round']) for x in exits})==24,'Missing/duplicate outcomes');all_rows={};failures=[];cohorts={};count=assertions=0
 for backend in BACKENDS:
  for source in SOURCES:
   for alias in ('','-AA'):
    label=source+'-'+backend+alias
    for round_id in range(3):
     ex=next(x for x in exits if x['label']==label and x['round']==round_id)
     if ex['exit_code']!=0:failures.append(dict(ex,kind='process_exit'))
     path=out/f'{label}-r{round_id}.json'
     if not path.exists():failures.append(dict(ex,kind='missing_payload'));continue
     try:p=read(path)
     except Exception as e:failures.append(dict(ex,kind='parse',error=str(e)));continue
     failures.extend(dict(label=label,round=round_id,key=r.get('key'),kind='row',error=r.get('error')) for r in p.get('rows',[]) if r.get('status')=='failed')
     try:rows=validate(p,m,label,source,backend,round_id)
     except Exception as e:failures.append(dict(ex,kind='invalid_payload',error=str(e)));continue
     identity=[p[k] for k in ('runtime','architecture','os','serverGC','stopwatchFrequency','vectorHardwareAccelerated','vectorIntWidth','denseWordsPerPackedRecord','avx2Supported','arm64AdvSimdSupported')]
     if backend in cohorts and identity!=cohorts[backend]:
      failures.append(dict(ex,kind='cohort_mismatch',observed=identity,expected=cohorts[backend]));continue
     if backend not in cohorts:cohorts[backend]=identity
     assertions+=p['assertions'];count+=sum(r['samplesCompleted'] for r in rows)
     for r in rows:all_rows.setdefault((backend,r['key']),{}).setdefault(label,[]).append(r)
 comparisons=[]
 if not failures:
  require(len(all_rows)==2*372,'Incomplete matrix')
  for (backend,key),variants in sorted(all_rows.items()):
   row=dict(BY_KEY[key],backend=backend,variants={},ratios={},paired_round_ratios={})
   for label,ps in variants.items():
    vals=[statistics.median(p['ns']) for p in ps];row['variants'][label]=dict(median_ns=statistics.median(vals),round_medians_ns=vals,round_p95_ns=[pct(p['ns'],.95) for p in ps],round_max_ns=[max(p['ns']) for p in ps],round_allocated_bytes=[statistics.median(p['allocated_bytes']) for p in ps],round_batch_ms=[p['batch_ms'] for p in ps],round_calibration_ms=[p['calibrationMs'] for p in ps],details=ps[0]['details'])
   pairs=[(s+'-'+backend+'-AA',s+'-'+backend) for s in SOURCES]+[(s+'-'+backend+a,d+'-'+backend+b) for i,s in enumerate(SOURCES) for d in SOURCES[:i] for a in ('','-AA') for b in ('','-AA')]
   for a,b in pairs:
    x=row['variants'][a];y=row['variants'][b];row['ratios'][a+'/'+b]=x['median_ns']/y['median_ns'];row['paired_round_ratios'][a+'/'+b]=[v/w for v,w in zip(x['round_medians_ns'],y['round_medians_ns'])]
   comparisons.append(row)
 summary=dict(status='failed' if failures else 'passed',failures=failures,processes=len(exits),rows=sum(len(v) for ps in all_rows.values() for v in ps.values()),samples=count,assertions=assertions,hosts=cohorts,scope='Prepared public operations only; all final actor outputs validated before reset, earlier operations contribute Count checksums.16 rotating actors. Full source A/A and partial/failing samples retained. No timing claims from short batches; all rankings withheld on failure.')
 write(out/'comparison.json',comparisons);write(out/'summary.json',summary);return summary


def git(repo, *args):
    """Use local Git objects only; never fetch, mutate, or execute a checkout."""
    result = subprocess.run(['git', '-C', str(repo), *args], check=True,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    return result.stdout


def checked_inventory(path):
    require(path.is_dir(), 'Missing Runtime directory: ' + str(path))
    require(not any(p.is_symlink() for p in path.rglob('*')),
            'Source snapshots do not follow symbolic links')
    result = inv(path)
    require(result and any(name.endswith('.cs') for name in result), 'Empty Runtime source inventory')
    return result


def verify_frozen(out, manifest):
    require(manifest.get('schema') == SCHEMA, 'Refuse another benchmark study')
    require(manifest.get('reference_commit') == REFERENCE_COMMIT, 'Wrong baseline commit')
    require(manifest.get('sources') == list(SOURCES) and manifest.get('backends') == list(BACKENDS),
            'Wrong source/backend matrix')
    require(manifest.get('rounds') == 3 and manifest.get('samples') == 7 and
            manifest.get('target_ms') == 3.0 and manifest.get('expected_rows') == 372 and
            manifest.get('expected_processes') == 24, 'Wrong declared protocol')
    for source in SOURCES:
        require(checked_inventory(out/'sources'/source/'Runtime') ==
                {name[len('Runtime/'):]: value for name, value in manifest['source_files'][source].items()},
                'Frozen Runtime changed: ' + source)
        require(inv(out/'sources'/source) == manifest['source_files'][source], 'Frozen source inventory changed')
    for name, expected in manifest['support_files'].items():
        require(sha(out/'sources'/name) == expected, 'Frozen support changed: ' + name)
    require(read(out/'sources/plan.json')['rows'] == PLAN, 'Frozen plan differs from the executable oracle')


def stage(args):
    repo = (args.repo if args.repo is not None else HERE.parents[2]).expanduser().resolve()
    require((repo/'Runtime').is_dir(), '--repo must name the checkout containing Runtime')
    require(git(repo, 'rev-parse', '--show-toplevel').decode().strip() == str(repo),
            '--repo must name the Git repository root')
    out = args.output.expanduser().resolve()
    require(not out.is_relative_to(repo/'Runtime'), 'Evidence must be outside Runtime')
    out.mkdir(parents=True, exist_ok=True)
    require(not any(out.iterdir()), 'Choose a new empty evidence directory; earlier evidence is preserved')
    before = checked_inventory(repo/'Runtime')
    head = git(repo, 'rev-parse', 'HEAD').decode().strip()
    require(git(repo, 'rev-parse', REFERENCE_COMMIT+'^{commit}').decode().strip() == REFERENCE_COMMIT,
            'Exact baseline commit is unavailable in local Git objects')
    archive = git(repo, 'archive', '--format=tar', REFERENCE_COMMIT, 'Runtime')
    sources = out/'sources'
    (sources/'baseline').mkdir(parents=True)
    # Avoid tar.extractall: only ordinary files/directories below Runtime are admitted.
    with tarfile.open(fileobj=io.BytesIO(archive), mode='r:') as tar:
        for member in tar.getmembers():
            relative = Path(member.name)
            require(not relative.is_absolute() and '..' not in relative.parts and
                    relative.parts and relative.parts[0] == 'Runtime', 'Unsafe baseline archive path')
            destination = sources/'baseline'/relative
            if member.isdir():
                destination.mkdir(parents=True, exist_ok=True)
            else:
                require(member.isfile(), 'Baseline archive contains a nonregular entry')
                destination.parent.mkdir(parents=True, exist_ok=True)
                destination.write_bytes(tar.extractfile(member).read())
    checked_inventory(sources/'baseline/Runtime')
    shutil.copytree(repo/'Runtime', sources/'candidate/Runtime')
    require(checked_inventory(sources/'candidate/Runtime') == before == checked_inventory(repo/'Runtime'),
            'Candidate Runtime changed while staging; preserve this directory and stage a new one')
    require(git(repo, 'rev-parse', 'HEAD').decode().strip() == head,
            'Candidate HEAD changed while staging')
    for name in ('PackedOperationBench.cs', 'run_packed.py', 'plan.json'):
        shutil.copy2(HERE/name, sources/name)
    for name in ('UnityStubs.cs', 'NuGet.Config'):
        shutil.copy2(repo/'Audit~'/name, sources/name)
    write(sources/'global.json', {'sdk': {'version': SDK_VERSION, 'rollForward': 'disable'}})
    support = ('PackedOperationBench.cs', 'run_packed.py', 'plan.json', 'UnityStubs.cs', 'NuGet.Config', 'global.json')
    manifest = dict(schema=SCHEMA, status='staged', reference_commit=REFERENCE_COMMIT,
                    baseline_git_archive_sha256=hashlib.sha256(archive).hexdigest(),
                    candidate_provenance=dict(repository=str(repo), head=head,
                        branch=git(repo, 'rev-parse', '--abbrev-ref', 'HEAD').decode().strip(),
                        runtime_status=git(repo, 'status', '--porcelain=v1', '--', 'Runtime').decode(),
                        runtime_diff_sha256=hashlib.sha256(git(repo, 'diff', '--binary', 'HEAD', '--', 'Runtime')).hexdigest(),
                        note='Candidate is the snapshotted worktree Runtime, including uncommitted/untracked files; its complete SHA256 inventory is authoritative.'),
                    source_files={source: inv(sources/source) for source in SOURCES},
                    support_files={name: sha(sources/name) for name in support},
                    samples=7, target_ms=3.0, rounds=3, backends=list(BACKENDS), sources=list(SOURCES),
                    expected_rows=372, expected_processes=24, sdk_required=SDK_VERSION,
                    scope='Prepared public operation comparison; staging performs no .NET invocation. Candidate correctness/release verification is a separate prerequisite.')
    verify_frozen(out, manifest)
    write(out/'manifest.json', manifest)
    print('Staged source snapshots without .NET execution:', out)
    return manifest


def resolve_dotnet(requested):
    found = shutil.which(requested)
    path = Path(found if found else requested).expanduser().resolve()
    require(path.is_file(), 'Cannot find dotnet; pass --dotnet /path/to/dotnet')
    return path


def execute(args):
    require(args.timing_owner_released,
            'Run only in a quiet CPU window; pass --timing-owner-released after coordinating it')
    out = args.output.expanduser().resolve()
    manifest = read(out/'manifest.json')
    require(manifest.get('status') == 'staged', 'Do not restart an admitted cohort; stage a new directory')
    verify_frozen(out, manifest)
    sources = out/'sources'
    dotnet = resolve_dotnet(args.dotnet)
    env = {key: value for key, value in os.environ.items() if not key.startswith(('DOTNET_', 'COMPlus_'))}
    env.update(DOTNET_CLI_HOME=str(out/'dotnet-home'), DOTNET_TieredCompilation='0',
               DOTNET_TieredPGO='0', DOTNET_ReadyToRun='0', DOTNET_gcConcurrent='0',
               DOTNET_NOLOGO='1', DOTNET_CLI_TELEMETRY_OPTOUT='1')
    if hasattr(os, 'sched_getaffinity') and hasattr(os, 'sched_setaffinity'):
        previous = sorted(os.sched_getaffinity(0))
        selected = args.cpu if args.cpu is not None else previous[0]
        require(selected in previous, 'Requested CPU is outside the allowed affinity set')
        os.sched_setaffinity(0, {selected})
        require(os.sched_getaffinity(0) == {selected}, 'Failed to establish affinity')
        manifest['cpu_affinity'] = dict(supported=True, previous=previous, selected=selected)
    else:
        require(args.cpu is None, '--cpu is unsupported on this operating system')
        manifest['cpu_affinity'] = dict(supported=False, selected=None,
            qualification='This operating system does not expose sched affinity; no pinning claim.')
    manifest.update(status='executing', dotnet_executable=str(dotnet),
                    environment={key: value for key, value in env.items() if key.startswith('DOTNET_')})
    write(out/'manifest.json', manifest)

    def process(command, label):
        print('RUN', label, flush=True)
        started = datetime.datetime.now(datetime.timezone.utc).isoformat()
        result = subprocess.run(list(map(str, command)), cwd=sources, env=env, text=True,
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=3600)
        (out/(label+'.log')).write_text(result.stdout)
        record = dict(name=label, start_utc=started,
                      end_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                      exit_code=result.returncode, command=list(map(str, command)))
        with (out/'execution-times.jsonl').open('a') as stream:
            stream.write(json.dumps(record)+'\n')
        return result

    sdk = process([dotnet, '--version'], 'sdk')
    require(sdk.returncode == 0 and sdk.stdout.strip() == SDK_VERSION, 'Pinned SDK mismatch')
    manifest['sdk'] = sdk.stdout.strip()
    manifest['attribution'] = {}
    for backend in BACKENDS:
        for source in SOURCES:
            label = source+'-'+backend
            build = out/'build'/label
            build.mkdir(parents=True)
            project = build/'PackedOperations.csproj'
            symbols = 'GAMEPLAYTAGS_FORCE_PORTABLE' if backend == 'portable' else ''
            compile_files = (sources/source/'Runtime/**/*.cs', sources/'UnityStubs.cs', sources/'PackedOperationBench.cs')
            project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>9.0</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><AllowUnsafeBlocks>false</AllowUnsafeBlocks><CheckForOverflowUnderflow>false</CheckForOverflowUnderflow><UseSharedCompilation>false</UseSharedCompilation><ImportDirectoryBuildProps>false</ImportDirectoryBuildProps><ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets><DefineConstants>'+symbols+'</DefineConstants></PropertyGroup><ItemGroup>'+''.join('<Compile Include="'+escape(str(path))+'"/>' for path in compile_files)+'</ItemGroup></Project>')
            result = process([dotnet, 'build', project, '-c', 'Release', '--configfile', sources/'NuGet.Config',
                              '-o', build/'bin', '-nodeReuse:false'], label+'-build')
            require(result.returncode == 0, 'Build failed: '+label)
            dll = build/'bin/PackedOperations.dll'
            manifest['attribution'][label] = dict(executablePath=str(dll), executableSha256=sha(dll),
                runtimeSourceRoot=str(sources/source), sourceInventorySha256=digest(manifest['source_files'][source]),
                harnessSha256=sha(sources/'PackedOperationBench.cs'), runnerSha256=sha(sources/'run_packed.py'))
    write(out/'manifest.json', manifest)
    exits = []
    jobs = [(source+'-'+backend+alias, source, backend)
            for alias in ('', '-AA') for backend in BACKENDS for source in SOURCES]
    for round_id in range(3):
        order = jobs[round_id:]+jobs[:round_id]
        if round_id % 2:
            order = list(reversed(order))
        for label, source, backend in order:
            while (out/'PAUSE').exists():
                time.sleep(.25)
            attribution = manifest['attribution'][source+'-'+backend]
            result = process([dotnet, 'exec', attribution['executablePath'], out/f'{label}-r{round_id}.json',
                              source, label, round_id, manifest['samples'], manifest['target_ms'],
                              attribution['runtimeSourceRoot'], attribution['sourceInventorySha256'],
                              attribution['harnessSha256'], attribution['runnerSha256']], label+'-r'+str(round_id))
            exits.append(dict(label=label, source=source, backend=backend, round=round_id, exit_code=result.returncode))
            write(out/'process-exits.json', exits)
    summary = aggregate(out, manifest)
    manifest['status'] = 'complete' if summary['status'] == 'passed' else 'failed'
    manifest['completed_utc'] = datetime.datetime.now(datetime.timezone.utc).isoformat()
    write(out/'manifest.json', manifest)
    print(json.dumps(summary, indent=2))
    return int(summary['status'] != 'passed')


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('stage', 'run', 'summarize'), nargs='?', default='stage')
    parser.add_argument('--repo', type=Path, help='Checkout root; default is this script\'s repository')
    parser.add_argument('--output', type=Path, required=True, help='New evidence directory for stage; frozen directory for run/summarize')
    parser.add_argument('--dotnet', default='dotnet', help='Installed dotnet executable or command name; default searches PATH')
    parser.add_argument('--cpu', type=int, help='Allowed CPU to pin on supported operating systems')
    parser.add_argument('--timing-owner-released', action='store_true', help='Acknowledge a coordinated quiet CPU window for run')
    args = parser.parse_args(argv)
    if args.action == 'stage':
        stage(args)
        return 0
    if args.action == 'run':
        return execute(args)
    out = args.output.expanduser().resolve()
    manifest = read(out/'manifest.json')
    verify_frozen(out, manifest)
    summary = aggregate(out, manifest)
    print(json.dumps(summary, indent=2))
    return int(summary['status'] != 'passed')


if __name__ == '__main__':
    raise SystemExit(main())
