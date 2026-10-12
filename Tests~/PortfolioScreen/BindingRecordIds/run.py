#!/usr/bin/env python3
"""Fixed portable rejection screen. Only explicit numeric/hash reports are public."""
import argparse
import hashlib
import importlib.util
import io
import json
import math
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tarfile
import time
import traceback

HERE = Path(__file__).resolve().parent
PUBLIC = ('status.json', 'provenance.json', 'config.json', 'correctness.json', 'count-map.json', 'raw/cold.json', 'raw/warm.json', 'summary.json')
ORDER = ('original', 'candidate')
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def digest(value): return hashlib.sha256(json.dumps(value, sort_keys=True).encode()).hexdigest()
def save(path, value): path.write_text(json.dumps(value, indent=2, allow_nan=False)+'\n')
def tree(path): return {str(p.relative_to(path)): sha(p) for p in sorted(path.rglob('*')) if p.is_file()}
def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module); return module
class GateStop(Exception): pass

def main():
    started = time.monotonic()
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--backend', choices=('net','mono'), required=True)
    ap.add_argument('--dotnet', default='dotnet'); ap.add_argument('--mono', default='mono')
    ap.add_argument('--nuget', type=Path, required=True); ap.add_argument('--git-root', type=Path, required=True)
    ap.add_argument('--out', type=Path, required=True); ap.add_argument('--work', type=Path, required=True)
    ap.add_argument('--expected-seal', required=True); ap.add_argument('--deadline-seconds', type=int, default=300)
    args = ap.parse_args()
    if not 0 < args.deadline_seconds <= 300: ap.error('Deadline must be 1-300 seconds; no extension')
    frozen_bytes = (HERE/'frozen.json').read_bytes(); seal = hashlib.sha256(frozen_bytes).hexdigest()
    if seal != args.expected_seal: ap.error('Reviewed seal mismatch')
    frozen = json.loads(frozen_bytes); pins = json.loads((HERE/'source-pins.json').read_text())
    config = json.loads((HERE/'config.json').read_text()); count_map = json.loads((HERE/'count-map.json').read_text())
    counts = {r['Id']: r['Iterations'] for r in count_map['Rows']}
    public, private = args.out.resolve(), args.work.resolve()
    if public == private or public in private.parents or private in public.parents: ap.error('Separate nonnested new output/work directories required')
    if HERE == public or HERE in public.parents or HERE == private or HERE in private.parents: ap.error('Outputs must be outside package')
    public.mkdir(parents=True, exist_ok=False); (public/'raw').mkdir(); private.mkdir(parents=True, exist_ok=False)
    dotnet = Path(shutil.which(args.dotnet) or args.dotnet).resolve(); mono = Path(shutil.which(args.mono) or args.mono).resolve()
    nuget = args.nuget.resolve(); deadline = started + args.deadline_seconds
    status = {'State':'incomplete','Stage':'initialization','Backend':args.backend,'BuildAttempts':0,'Builds':0,'SemanticTargets':0,'ColdTargets':0,'WarmTargets':0,'PilotTargets':0,'AcceptanceEvidence':False,'FrozenSHA256':seal}
    provenance = {'BaseRevision':pins['BaseRevision'],'FrozenSHA256':seal,'RuntimeSourceHashes':pins['RuntimeFiles'],'CountMapSHA256':sha(HERE/'count-map.json'),'ActualRuntimeVersions':[],'ToolchainManifestSHA256':None,'BuildArtifactHashes':{}}
    correctness = {'CoreAndBindingPass':False,'EqualSnapshotSHA256':None,'ManagedLayout':{},'MetadataGatePass':False,'RuntimeTypes':None,'RuntimeMethods':None,'UnchangedMetadataDifferences':None,'UnrelatedRawILFailures':None}
    cold, warm = [], []; summary = {'ColdPairs':[],'WarmPairs':[],'AcceptanceEvidence':False,'LatencyPassEstablished':False,'AutomaticContinuation':False}
    commands = []; tool_files = {}; tool_hashes = {}; artifacts = {}; built = []; sources_ready = False; support = None
    child_scope = False; active_proc = None; canceled = False
    child_env = {k:v for k,v in os.environ.items() if not k.startswith(('DOTNET_Jit','COMPlus_Jit'))}
    child_env['DOTNET_ROLL_FORWARD'] = 'Disable'
    for prefix in ('DOTNET_','COMPlus_'):
        child_env[prefix+'ReadyToRun']='1'; child_env[prefix+'TieredCompilation']='0'; child_env[prefix+'gcServer']='0'
    def artifact_tree(): return {v:tree(private/('build-'+v)) for v in built}
    def verify():
        if time.monotonic() >= deadline: raise TimeoutError('Fixed deadline')
        if (HERE/'frozen.json').read_bytes() != frozen_bytes: raise RuntimeError('Frozen identity changed')
        if {str(p.relative_to(HERE)) for p in HERE.rglob('*') if p.is_file()} != set(frozen['Files']) | {'frozen.json'}: raise RuntimeError('Prepared inventory changed')
        if {name:sha(HERE/name) for name in frozen['Files']} != frozen['Files']: raise RuntimeError('Prepared input changed')
        if sources_ready and any(tree(private/v) != pins['RuntimeFiles'][v] for v in ORDER): raise RuntimeError('Runtime tree changed')
        if support is not None and support.input_files(args.backend,dotnet,mono,nuget) != tool_files: raise RuntimeError('Tool inventory changed')
        if {name:sha(path) for name,path in tool_files.items()} != tool_hashes: raise RuntimeError('Tool input changed')
        if artifacts and artifact_tree() != artifacts: raise RuntimeError('Build artifact changed')
    def alarm(*unused):
        nonlocal canceled
        canceled = True
        if child_scope:
            if active_proc is not None:
                try: os.killpg(active_proc.pid,signal.SIGKILL)
                except ProcessLookupError: pass
            return
        raise TimeoutError('Fixed deadline or termination')
    signal.signal(signal.SIGALRM,alarm); signal.signal(signal.SIGTERM,alarm)
    signal.setitimer(signal.ITIMER_REAL,max(0.001,deadline-time.monotonic()))
    def run(name,command,allow_failure=False):
        nonlocal child_scope,active_proc
        verify()
        allowed = {'archive-baseline','compile-original','compile-candidate'} | {'semantic-'+v for v in ORDER}
        allowed |= {'cold-'+shape+'-'+str(pair)+'-'+v for shape in config['ColdShapes'] for pair in range(4) for v in ORDER}
        allowed |= {'warm-'+row['Id']+'-'+str(pair)+'-'+v for row in config['Rows'] for pair in range(4) for v in ORDER}
        if name not in allowed or any(c['Name']==name for c in commands): raise RuntimeError('Unplanned/repeated command')
        if name.startswith('compile-'):
            status['BuildAttempts'] += 1
            if status['BuildAttempts'] > 2: raise RuntimeError('Build budget')
        if name.startswith('semantic-'): status['SemanticTargets'] += 1
        if name.startswith('cold-'): status['ColdTargets'] += 1
        if name.startswith('warm-'): status['WarmTargets'] += 1
        if status['SemanticTargets']>2 or status['ColdTargets']>32 or status['WarmTargets']>56: raise RuntimeError('Target budget')
        record={'Name':name,'Arguments':list(map(str,command))}; commands.append(record); save(private/'commands.json',commands)
        with (private/(name+'.stdout')).open('wb') as stdout,(private/(name+'.stderr')).open('wb') as stderr:
            child_scope=True
            try:
                if canceled or time.monotonic()>=deadline: raise TimeoutError('Canceled before spawn')
                active_proc=subprocess.Popen(record['Arguments'],stdout=stdout,stderr=stderr,env=child_env,start_new_session=True)
                remaining=deadline-time.monotonic()
                if canceled or remaining<=0: raise TimeoutError('Canceled during spawn')
                code=active_proc.wait(timeout=remaining)
                if canceled: raise TimeoutError('Canceled during child')
                if code!=0 and not allow_failure: raise RuntimeError('Command failed')
            finally:
                if active_proc is not None:
                    try: os.killpg(active_proc.pid,signal.SIGKILL)
                    except ProcessLookupError: pass
                    active_proc.wait()
                active_proc=None; child_scope=False
            if canceled: raise TimeoutError('Canceled during cleanup')
        record['ExitCode']=code; save(private/'commands.json',commands); return code
    def runtime_lines(name):
        lines=(private/(name+'.stdout')).read_text().splitlines()
        if not lines or not lines[0].startswith('PORTABLE_RUNTIME '): raise ValueError('Missing runtime identity')
        value=json.loads(lines[0][len('PORTABLE_RUNTIME '):])
        if set(value)!={'EnvironmentVersion','MonoVersion','PointerSize'} or value['PointerSize']!=8: raise ValueError('Unexpected runtime identity')
        if not re.fullmatch(r'\d+(?:\.\d+){2,3}',value['EnvironmentVersion']): raise ValueError('Runtime version format')
        if args.backend=='net' and (value['EnvironmentVersion']!=config['CLRVersion'] or value['MonoVersion']!=''): raise ValueError('CLR mismatch')
        if args.backend=='mono' and value['MonoVersion']!=config['MonoVersion']: raise ValueError('Mono mismatch')
        if value not in provenance['ActualRuntimeVersions']: provenance['ActualRuntimeVersions'].append(value)
        return lines[1:]
    def public_sample(kind,name,shape,pair,variant,code):
        record={'Id':shape,'Pair':pair,'Variant':variant,'ExitCode':code,'Valid':False,'StdoutSHA256':sha(private/(name+'.stdout')),'StderrSHA256':sha(private/(name+'.stderr'))}
        try:
            lines=runtime_lines(name)
            if len(lines)!=1: raise ValueError('Exactly one sample required')
            value=json.loads(lines[0]); record['Observation']=project_sample(kind,value)
            record['Valid']=code==0 and validate_sample(kind,value,shape,counts,config,args.backend)
            if kind=='cold': record['Valid']=record['Valid'] and value['ManagedRecordSize']==correctness['ManagedLayout'][variant]['BindDataSize']
        except (ValueError,KeyError,TypeError,OverflowError): record['ParseOrValidationFailure']=True
        return record
    try:
        verify(); sys.dont_write_bytecode=True
        if tuple((HERE/'upload-allowlist.txt').read_text().splitlines())!=PUBLIC: raise RuntimeError('Public allowlist mismatch')
        if list(counts)!=[r['Id'] for r in config['Rows']]: raise RuntimeError('Count map row mismatch')
        for r in count_map['Rows']:
            n=r['OriginalIterations']*250*r['StopwatchFrequency']; d=1000*min(r['OriginalFourTicks'])
            if r['Iterations']!=max(r['OriginalIterations'],(n+d-1)//d): raise RuntimeError('Frozen original-only count derivation mismatch')
        status['Stage']='materialize-runtime'
        run('archive-baseline',['git','-C',args.git_root.resolve(),'archive',pins['BaseRevision'],'Runtime'])
        (private/'original').mkdir()
        with tarfile.open(fileobj=io.BytesIO((private/'archive-baseline.stdout').read_bytes())) as archive:
            for member in archive:
                path=Path(member.name)
                if path.is_absolute() or '..' in path.parts or not path.parts or path.parts[0]!='Runtime': raise RuntimeError('Unsafe archive path')
                dest=private/'original'/path
                if member.isdir(): dest.mkdir(parents=True,exist_ok=True)
                elif member.isfile() and member.name in pins['RuntimeFiles']['original']:
                    dest.parent.mkdir(parents=True,exist_ok=True); dest.write_bytes(archive.extractfile(member).read())
                else: raise RuntimeError('Unexpected archive member')
        shutil.copytree(private/'original',private/'candidate')
        shutil.copy2(HERE/'candidate/GameplayTagContainerBinds.cs',private/'candidate/Runtime/GameplayTagContainerBinds.cs')
        sources_ready=True; verify()
        support=load('portable_binding_build_support',HERE/'build_support.py')
        tool_files=support.input_files(args.backend,dotnet,mono,nuget); tool_hashes={name:sha(path) for name,path in tool_files.items()}
        save(private/'tool-hashes.json',tool_hashes); provenance['ToolchainManifestSHA256']=digest(tool_hashes)
        status['Stage']='build'; executables={}
        for variant in ORDER:
            executables[variant]=support.build(private/variant,HERE/'Harness/Entry.cs',private/('build-'+variant),args.backend,dotnet,mono,nuget,run)
            status['Builds']+=1; verify(); built.append(variant); artifacts=artifact_tree(); verify()
        provenance['BuildArtifactHashes']=artifacts
        status['Stage']='semantic'
        for variant in ORDER:
            run('semantic-'+variant,executables[variant]+['semantic',private/(variant+'.snapshot.bin'),private/(variant+'.layout.json'),private/(variant+'.metadata.json')]); verify()
            lines=runtime_lines('semantic-'+variant)
            required=['7,595,855 assertions; 0 failed groups;','{"Checks":70,"FailedGroups":0}','PASS binding-record supplemental frames: 13','PASS None and named-None before registry initialization']
            if not all(any(marker in line for line in lines) for marker in required): raise RuntimeError('Semantic markers missing')
        if (private/'semantic-original.stdout').read_bytes()!=(private/'semantic-candidate.stdout').read_bytes(): raise RuntimeError('Semantic stdout differs')
        if (private/'original.snapshot.bin').read_bytes()!=(private/'candidate.snapshot.bin').read_bytes(): raise RuntimeError('Semantic snapshots differ')
        report=load('portable_binding_mechanism',HERE/'mechanism_compare.py').compare(private)
        layout={variant:{key:report['managed_layout'][variant][key] for key in ('PointerSize','GameplayTagSize','BindDataSize')} for variant in ORDER}
        correctness.update(CoreAndBindingPass=True,EqualSnapshotSHA256=sha(private/'original.snapshot.bin'),ManagedLayout=layout,MetadataGatePass=report['status']=='PASS',RuntimeTypes=report['types_compared'],RuntimeMethods=report['methods_compared'],UnchangedMetadataDifferences=len(report['metadata_differences']),UnrelatedRawILFailures=len(report['unrelated_raw_il_failures']),MechanismReportSHA256=sha(private/'mechanism-report.json'))
        if report['status']!='PASS': raise RuntimeError('Metadata/IL/layout gate failed')
        status['Stage']='cold'
        for shape in config['ColdShapes']:
            for pair,order in enumerate(config['PairOrders']):
                for variant in order:
                    name='cold-'+shape+'-'+str(pair)+'-'+variant
                    code=run(name,executables[variant]+['cold',shape],allow_failure=True); verify()
                    cold.append(public_sample('cold',name,shape,pair,variant,code)); save(private/'cold-observations.json',cold)
        summary['ColdPairs']=pairs(cold,'cold',config['ColdShapes'])
        summary['ColdSemanticsPass']=len(cold)==32 and all(r['Valid'] for r in cold) and all(r['SemanticEqual'] for r in summary['ColdPairs'])
        summary['ColdAllocationIncrease']=any(r.get('ByteDelta',0)>0 for r in summary['ColdPairs'])
        if not summary['ColdSemanticsPass'] or summary['ColdAllocationIncrease']:
            status['State']='blocked'; status['Stage']='cold-gate'; raise GateStop()
        status['Stage']='warm'
        for row in config['Rows']:
            shape=row['Id']
            for pair,order in enumerate(config['PairOrders']):
                for variant in order:
                    name='warm-'+shape+'-'+str(pair)+'-'+variant
                    code=run(name,executables[variant]+['warm','timing',shape,str(counts[shape])],allow_failure=True); verify()
                    warm.append(public_sample('warm',name,shape,pair,variant,code)); save(private/'warm-observations.json',warm)
        summary['WarmPairs']=pairs(warm,'warm',[r['Id'] for r in config['Rows']])
        summary['WarmSemanticsPass']=len(warm)==56 and all(r['Valid'] for r in warm) and all(r['SemanticEqual'] for r in summary['WarmPairs'])
        summary['WarmAllocationIncrease']=any(r.get('ByteDelta',0)>0 for r in summary['WarmPairs'])
        if (status['Builds'],status['SemanticTargets'],status['ColdTargets'],status['WarmTargets'])!=(2,2,32,56): raise RuntimeError('Incomplete fixed sequence')
        verify(); status['State']='complete'; status['Stage']='completed'
        if not summary['WarmSemanticsPass'] or summary['WarmAllocationIncrease']: status['State']='rejected'; status['Stage']='warm-gate'
    except GateStop: pass
    except BaseException:
        (private/'failure.txt').write_text(traceback.format_exc())
    finally:
        try: verify()
        except BaseException:
            status['State']='incomplete'; status['Stage']='final-integrity-or-deadline'
            with (private/'failure.txt').open('a') as stream: stream.write(traceback.format_exc())
        payloads={'status.json':status,'provenance.json':provenance,'config.json':dict(config,Backend=args.backend,RequestedDeadlineSeconds=args.deadline_seconds),'correctness.json':correctness,'count-map.json':count_map,'raw/cold.json':cold,'raw/warm.json':warm,'summary.json':summary}
        for name in PUBLIC: save(public/name,payloads[name])
        if set(tree(public))!=set(PUBLIC): raise RuntimeError('Unexpected public file')
        if time.monotonic()>=deadline:
            status['State']='incomplete'; status['Stage']='finalization-deadline'; save(public/'status.json',status)
        signal.setitimer(signal.ITIMER_REAL,0)
    print('Binding record portability screen: '+status['State']+'. Explicit public numeric/hash reports only; AcceptanceEvidence=false.')
    return 0 if status['State']=='complete' else 1

# Exact sample schemas are projection allowlists, never passthrough target objects.
COLD_NUM=('Count','Ticks','Frequency','AllocatedBytes','GC0','GC1','GC2','ImmediateEvents','ImmediateChecksum','ProbeEvents','ProbeChecksum','ExternalEvents','ExternalChecksum','BindingCapacity','ManagedRecordSize','OwnedRecordArrayPayloadBytes','CallbackCapacityBefore','CallbackCapacityAfterBind','PointerSize')
WARM_NUM=('Iterations','Ticks','StopwatchFrequency','BatchMs','Ns','Bytes','Events','Checksum','ExternalEvents','ExternalChecksum','Gc0','Gc1','Gc2','OwnedSubscriptions','ExternalSubscriptions','TagCount','ServerGC','TieringConfigured','ReadyToRunRequested','TieringRequested')+tuple(p+s for p in ('BeforeProbe','AfterProbe') for s in ('Events','Checksum','ExternalEvents','ExternalChecksum'))
def project_sample(kind,value):
    nums=COLD_NUM if kind=='cold' else WARM_NUM
    if any(type(value[k]) not in (int,float) or not math.isfinite(value[k]) for k in nums): raise ValueError('Invalid numerical field')
    result={k:value[k] for k in nums}
    if kind=='cold':
        for key in ('SemanticsPass','FirstBridgeJitIncluded','WholeHeapRetainedBytesMeasured'):
            if type(value[key]) is not bool: raise ValueError('Invalid boolean field')
            result[key]=value[key]
        result['ErrorObserved']=bool(value.get('ErrorType'))
    return result
def validate_sample(kind,v,name,counts,config,backend):
    gc=('GC0','GC1','GC2') if kind=='cold' else ('Gc0','Gc1','Gc2')
    if any(type(v[k]) is not int or v[k]<0 for k in gc): return False
    if kind=='cold':
        count=5 if name=='growth-five' else 1; present=name=='present'; external=name=='external'
        return v['Shape']==name and v['SemanticsPass'] and not v['ErrorType'] and v['Count']==count and v['PointerSize']==8 and v['ManagedRecordSize']>0 and v['BindingCapacity']==(8 if count==5 else 4) and v['OwnedRecordArrayPayloadBytes']==v['BindingCapacity']*v['ManagedRecordSize'] and v['Ticks']>0 and v['Frequency']>0 and v['AllocatedBytes']>=0 and v['ImmediateEvents']==count and v['ImmediateChecksum']==count*(3 if present else 1) and v['ProbeEvents']==count*3 and v['ProbeChecksum']==count*(7 if present else 5) and v['ExternalEvents']==(4 if external else 0) and v['FirstBridgeJitIncluded'] and not v['WholeHeapRetainedBytesMeasured']
    row=next(r for r in config['Rows'] if r['Id']==name)
    if v['Mode']!='timing' or v['Id']!=name or v['Iterations']!=counts[name] or v['Ticks']<=0 or v['StopwatchFrequency']<=0 or v['Ns']<=0 or v['BatchMs']<=0 or v['Bytes']<0: return False
    if backend=='net' and (v['RuntimeVersion'],v['ServerGC'],v['TieringConfigured'],v['ReadyToRunRequested'],v['TieringRequested'])!=('8.0.31',0,0,1,0): return False
    if backend=='mono' and (v['ServerGC']!=0 or v['ReadyToRunRequested']!=-1 or v['TieringRequested']!=-1 or v['TieringConfigured'] not in (-1,0)): return False
    return v['OwnedSubscriptions']==0 and v['ExternalSubscriptions']==row['ExternalListeners'] and v['TagCount']==int(row['Present']) and v['Events']==v['Iterations']*row['Binds'] and v['Checksum']==v['Iterations']*row['Binds']*(3 if row['Present'] else 1) and v['ExternalEvents']==(4*v['Iterations'] if name=='direct-dispatch' else 0) and all(v['BeforeProbe'+s]==v['AfterProbe'+s] for s in ('Events','Checksum','ExternalEvents','ExternalChecksum'))
def pairs(records,kind,names):
    output=[]
    keys=('Count','ImmediateEvents','ImmediateChecksum','ProbeEvents','ProbeChecksum','ExternalEvents','ExternalChecksum','BindingCapacity','CallbackCapacityBefore','CallbackCapacityAfterBind','PointerSize') if kind=='cold' else ('Iterations','Events','Checksum','ExternalEvents','ExternalChecksum','OwnedSubscriptions','ExternalSubscriptions','TagCount')+tuple(p+s for p in ('BeforeProbe','AfterProbe') for s in ('Events','Checksum','ExternalEvents','ExternalChecksum'))
    for name in names:
        for pair in range(4):
            found={r['Variant']:r for r in records if r['Id']==name and r['Pair']==pair}; item={'Id':name,'Pair':pair,'Complete':False,'SemanticEqual':False}
            if set(found)==set(ORDER) and all('Observation' in r for r in found.values()):
                a,b=found['original']['Observation'],found['candidate']['Observation']; item['Complete']=True
                item['SemanticEqual']=all(found[v]['Valid'] for v in ORDER) and all(a[k]==b[k] for k in keys)
                bytekey='AllocatedBytes' if kind=='cold' else 'Bytes'; item['OriginalBytes']=a[bytekey]; item['CandidateBytes']=b[bytekey]; item['ByteDelta']=b[bytekey]-a[bytekey]
                oldns=a['Ticks']*1e9/a['Frequency'] if kind=='cold' and a['Frequency']>0 else a.get('Ns',0)
                newns=b['Ticks']*1e9/b['Frequency'] if kind=='cold' and b['Frequency']>0 else b.get('Ns',0)
                item.update(OriginalNs=oldns,CandidateNs=newns,CandidateOverOriginal=newns/oldns if oldns>0 else None,CandidateSlower=newns>oldns)
                if kind=='warm': item.update(OriginalBatchMs=a['BatchMs'],CandidateBatchMs=b['BatchMs'],AnyBatchBelowTarget=min(a['BatchMs'],b['BatchMs'])<250)
            output.append(item)
    return output

if __name__=='__main__': raise SystemExit(main())
