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
def unique_object(pairs):
    result={}
    for key,value in pairs:
        if key in result:raise ValueError('Duplicate JSON key')
        result[key]=value
    return result
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
    ap.add_argument('--expected-seal', required=True); ap.add_argument('--deadline-seconds', type=int, choices=(300,), default=300)
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
    status = {'State':'incomplete','Stage':'initialization','Backend':args.backend,'BuildAttempts':0,'Builds':0,'SemanticTargets':0,'ColdTargets':0,'WarmTargets':0,'PilotTargets':0,'ColdRows':0,'WarmRows':0,'AcceptanceEvidence':False,'FrozenSHA256':seal}
    provenance = {'BaseRevision':pins['BaseRevision'],'FrozenSHA256':seal,'RuntimeSourceHashes':pins['RuntimeFiles'],'CountMapSHA256':sha(HERE/'count-map.json'),'ActualRuntimeVersions':[],'ToolchainManifestSHA256':None,'BuildArtifactHashes':{}}
    correctness = {'CoreAndQueuePass':False,'EqualSnapshotSHA256':None,'ManagedLayout':{},'MetadataGatePass':False,'RuntimeTypes':None,'RuntimeMethods':None,'UnchangedMetadataDifferences':None,'UnrelatedRawILFailures':None}
    cold, warm = [], []; summary = {'ColdPairs':[],'WarmPairs':[],'AcceptanceEvidence':False,'LatencyPassEstablished':False,'AutomaticContinuation':False}
    commands = []; tool_files = {}; tool_hashes = {}; artifacts = {}; built = []; sources_ready = False; support = None
    child_scope = False; active_proc = None; canceled = False; finalizing = False; projector = None
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
        if child_scope or finalizing:
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
        allowed |= {'warm-'+str(pair)+'-'+v for pair in range(4) for v in ORDER}
        if name not in allowed or any(c['Name']==name for c in commands): raise RuntimeError('Unplanned/repeated command')
        if name.startswith('compile-'):
            status['BuildAttempts'] += 1
            if status['BuildAttempts'] > 2: raise RuntimeError('Build budget')
        if name.startswith('semantic-'): status['SemanticTargets'] += 1
        if name.startswith('cold-'): status['ColdTargets'] += 1
        if name.startswith('warm-'): status['WarmTargets'] += 1
        if status['SemanticTargets']>2 or status['ColdTargets']>40 or status['WarmTargets']>8: raise RuntimeError('Target budget')
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
                    record['ExitCode']=active_proc.returncode; save(private/'commands.json',commands)
                active_proc=None; child_scope=False
            if canceled: raise TimeoutError('Canceled during cleanup')
        record['ExitCode']=code; save(private/'commands.json',commands); return code
    def runtime_lines(name):
        lines=(private/(name+'.stdout')).read_text().splitlines()
        if not lines or not lines[0].startswith('PORTABLE_RUNTIME '): raise ValueError('Missing runtime identity')
        value=json.loads(lines[0][len('PORTABLE_RUNTIME '):],object_pairs_hook=unique_object)
        if set(value)!={'EnvironmentVersion','MonoVersion','PointerSize'} or type(value['PointerSize']) is not int or value['PointerSize']!=8: raise ValueError('Unexpected runtime identity')
        if not re.fullmatch(r'\d+(?:\.\d+){2,3}',value['EnvironmentVersion']): raise ValueError('Runtime version format')
        if args.backend=='net' and (value['EnvironmentVersion']!=config['CLRVersion'] or value['MonoVersion']!=''): raise ValueError('CLR mismatch')
        if args.backend=='mono' and value['MonoVersion']!=config['MonoVersion']: raise ValueError('Mono mismatch')
        if value not in provenance['ActualRuntimeVersions']: provenance['ActualRuntimeVersions'].append(value)
        return lines[1:]
    def public_sample(kind,name,shape,pair,variant,code,index=0):
        record={'Id':shape,'Pair':pair,'Variant':variant,'ExitCode':code,'Valid':False,'StdoutSHA256':sha(private/(name+'.stdout')),'StderrSHA256':sha(private/(name+'.stderr'))}
        lines=(private/(name+'.stdout')).read_text().splitlines()
        if lines and lines[0].startswith('PORTABLE_RUNTIME '):lines=lines[1:]
        try:
            checked_lines=runtime_lines(name)
            lines=checked_lines
            record['RuntimeAndLineCountValid']=len(lines)==(2 if kind=='cold' else 8)
        except (ValueError,KeyError,TypeError,OverflowError): record['RuntimeAndLineCountValid']=False
        values={}
        for part,offset in (('Observation',0),('Semantics',1)):
            try:
                value=json.loads(lines[2*index+offset],object_pairs_hook=unique_object); values[part]=value
                record[part]=project_sample(kind,part,value)
            except (ValueError,KeyError,TypeError,OverflowError,IndexError):record[part+'ParseFailure']=True
        path=snapshot_file(private,kind,shape,pair,variant)
        record['SnapshotPresent']=path.is_file()
        if path.is_file():record['SnapshotSHA256']=sha(path);record['SnapshotBytes']=path.stat().st_size
        try:
            record['Valid']=bool('Observation' in record and 'Semantics' in record and code==0 and record['RuntimeAndLineCountValid'] and validate_sample(kind,values['Observation'],values['Semantics'],shape,variant,counts,config)
                and record.get('SnapshotBytes',0)>0 and values['Semantics']['SnapshotSHA256']==record['SnapshotSHA256'])
        except (ValueError,KeyError,TypeError,OverflowError):record['Valid']=False
        return record
    try:
        verify(); sys.dont_write_bytecode=True
        projector=load('portable_drained_diagnostics',HERE/'diagnostics.py').project_target
        if tuple((HERE/'upload-allowlist.txt').read_text().splitlines())!=PUBLIC: raise RuntimeError('Public allowlist mismatch')
        if list(counts)!=[r['Id'] for r in config['Rows']]: raise RuntimeError('Count map row mismatch')
        if counts != {'single':3655224,'chain9':906602,'chain1025':9309,'pending':547456}: raise RuntimeError('Frozen shared count map changed')
        if (config['MaxBuilds'],config['SemanticProcesses'],config['ColdProcesses'],config['WarmProcesses'],config['PilotProcesses']) != (2,2,40,8,0): raise RuntimeError('Fixed process budget changed')
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
        shutil.copy2(HERE/'candidate/GameplayTagCountContainer.cs',private/'candidate/Runtime/GameplayTagCountContainer.cs')
        sources_ready=True; verify()
        support=load('portable_drained_build_support',HERE/'build_support.py')
        tool_files=support.input_files(args.backend,dotnet,mono,nuget); tool_hashes={name:sha(path) for name,path in tool_files.items()}
        save(private/'tool-hashes.json',tool_hashes); provenance['ToolchainManifestSHA256']=digest(tool_hashes)
        status['Stage']='build'; executables={}
        for variant in ORDER:
            executables[variant]=support.build(private/variant,HERE/'Harness/Entry.cs',private/('build-'+variant),args.backend,dotnet,mono,nuget,run)
            status['Builds']+=1; verify(); built.append(variant); artifacts=artifact_tree(); verify()
        provenance['BuildArtifactHashes']=artifacts
        status['Stage']='semantic'
        for variant in ORDER:
            run('semantic-'+variant,executables[variant]+['semantic',private/(variant+'.snapshot.bin'),private/(variant+'.layout.json'),private/(variant+'.metadata.json'),private/(variant+'.raw-queue.txt'),variant,private/(variant+'.managed-layout.json')]); verify()
            lines=runtime_lines('semantic-'+variant)
            required=['7,595,855 assertions; 0 failed groups;',
                'PASS drained queue: finite chain, multicast snapshot, throw recovery, hierarchy fanout, nested batch/discard, full one-pending resize',
                'PASS unchanged core including BindingAndPooling; logical suffix and full storage/callback state; ordinary public callers exercised; no portable native capture; untimed only']
            if not all(any(marker in line for line in lines) for marker in required): raise RuntimeError('Semantic markers missing')
        if (private/'semantic-original.stdout').read_bytes()!=(private/'semantic-candidate.stdout').read_bytes(): raise RuntimeError('Semantic stdout differs')
        if (private/'original.snapshot.bin').read_bytes()!=(private/'candidate.snapshot.bin').read_bytes(): raise RuntimeError('Semantic snapshots differ')
        report=load('portable_drained_mechanism',HERE/'mechanism_compare.py').compare(private)
        layout={variant:{key:report['managed_layout'][variant][key] for key in ('PointerSize','GameplayTagSize','DeferredEventSize')} for variant in ORDER}
        correctness.update(CoreAndQueuePass=True,EqualSnapshotSHA256=sha(private/'original.snapshot.bin'),ManagedLayout=layout,MetadataGatePass=report['status']=='PASS',RuntimeTypes=report['types_compared'],RuntimeMethods=report['methods_compared'],UnchangedMetadataDifferences=len(report['metadata_differences']),UnrelatedRawILFailures=len(report['unrelated_raw_il_failures']),MechanismReportSHA256=sha(private/'mechanism-report.json'),QueueObservationCount={v:len(report['queue_observations'][v]) for v in ORDER},NativeCapturePerformed=False)
        if report['status']!='PASS': raise RuntimeError('Metadata/IL/layout/queue gate failed')
        status['Stage']='cold'
        for shape in config['ColdShapes']:
            for pair,order in enumerate(config['PairOrders']):
                for variant in order:
                    name='cold-'+shape+'-'+str(pair)+'-'+variant
                    code=run(name,executables[variant]+['cold',shape,private/(name+'.snapshot.bin'),variant],allow_failure=True); verify()
                    cold.append(public_sample('cold',name,shape,pair,variant,code)); status['ColdRows']=len(cold); save(private/'cold-observations.json',cold)
        summary['ColdPairs']=pairs(cold,'cold',config['ColdShapes'],private)
        summary['ColdSemanticsPass']=len(cold)==40 and all(r['Valid'] for r in cold) and all(r['SemanticEqual'] for r in summary['ColdPairs'])
        summary['ColdAllocationIncrease']=any(r.get('ByteDelta',0)>0 for r in summary['ColdPairs'])
        summary['EveryExpectedColdBenefitPairReducesBytes']=all(r.get('ByteDelta',0)<0 for r in summary['ColdPairs'] if r['Id'] in config['ExpectedAllocationBenefitShapes'])
        if not summary['ColdSemanticsPass'] or summary['ColdAllocationIncrease']:
            status['State']='blocked'; status['Stage']='cold-gate'; raise GateStop()
        status['Stage']='warm'
        for pair,order in enumerate(config['PairOrders']):
            for variant in order:
                name='warm-'+str(pair)+'-'+variant
                code=run(name,executables[variant]+['warm','timing']+[str(counts[row['Id']]) for row in config['Rows']]+[private/name,variant],allow_failure=True); verify()
                for index,row in enumerate(config['Rows']): warm.append(public_sample('warm',name,row['Id'],pair,variant,code,index))
                status['WarmRows']=len(warm);save(private/'warm-observations.json',warm)
        summary['WarmPairs']=pairs(warm,'warm',[r['Id'] for r in config['Rows']],private)
        summary['WarmSemanticsPass']=len(warm)==32 and all(r['Valid'] for r in warm) and all(r['SemanticEqual'] for r in summary['WarmPairs'])
        summary['WarmAllocationIncrease']=any(r.get('ByteDelta',0)>0 for r in summary['WarmPairs'])
        summary['WarmSlowerPairCounts']={row['Id']:sum(r.get('CandidateSlower',False) for r in summary['WarmPairs'] if r['Id']==row['Id']) for row in config['Rows']}
        summary['AnyWarmRowRepeatedlyAdverse']=any(n>=3 for n in summary['WarmSlowerPairCounts'].values())
        summary['ColdLatencyUnresolved']=True;summary['LatencyPassEstablished']=False
        if (status['Builds'],status['SemanticTargets'],status['ColdTargets'],status['WarmTargets'],status['PilotTargets'])!=(2,2,40,8,0): raise RuntimeError('Incomplete fixed sequence')
        verify(); status['State']='complete'; status['Stage']='completed';summary['Decision']='REQUIRES_REVIEW_NO_AUTOMATIC_CONTINUATION'
        if not summary['WarmSemanticsPass'] or summary['WarmAllocationIncrease']:
            status['State']='rejected'; status['Stage']='warm-gate';summary['Decision']='CLOSE_WARM_SEMANTIC_OR_ALLOCATION_FAILURE'
        elif summary['AnyWarmRowRepeatedlyAdverse']:
            status['State']='rejected'; status['Stage']='warm-cost-gate';summary['Decision']='CLOSE_REPEATED_OBSERVED_WARM_COST'
    except GateStop: pass
    except BaseException:
        status.setdefault('FailureStage',status['Stage'])
        (private/'failure.txt').write_text(traceback.format_exc())
    finally:
        finalizing=True # Defer cancellation through bounded report cleanup; record incomplete at the deadline.
        try: verify()
        except BaseException:
            status.setdefault('FailureStage',status['Stage'])
            status['State']='incomplete'; status['Stage']='final-integrity-or-deadline'
            with (private/'failure.txt').open('a') as stream: stream.write(traceback.format_exc())
        # Projector was loaded only after intact-input verification, before any subprocess.
        correctness['SemanticDiagnostics']=[]
        try:
            if projector is None:raise RuntimeError('Verified diagnostic projector unavailable')
            def blob(name):
                path=private/name
                return path.read_bytes() if path.is_file() else None
            for variant in ORDER:
                command=next((c for c in commands if c['Name']=='semantic-'+variant),None)
                data={key:blob(name) for key,name in {'Stdout':'semantic-'+variant+'.stdout','Stderr':'semantic-'+variant+'.stderr','Snapshot':variant+'.snapshot.bin','Layout':variant+'.managed-layout.json','Metadata':variant+'.metadata.json'}.items()}
                correctness['SemanticDiagnostics'].append(projector(variant,args.backend,command,data))
        except BaseException:
            status['State']='incomplete';status['Stage']='diagnostic-finalization';correctness['DiagnosticProjectionFailed']=True
        sequence=(status['Builds'],status['SemanticTargets'],status['ColdTargets'],status['WarmTargets'],status['PilotTargets'])
        complete=sequence==(2,2,40,8,0) and (len(cold),len(warm))==(40,32)
        cold_stop=sequence==(2,2,40,0,0) and (len(cold),len(warm))==(40,0)
        status['FixedSequenceValidated']=(status['State'] in ('complete','rejected') and complete) or (status['State']=='blocked' and cold_stop)
        if status['State'] in ('complete','rejected','blocked') and not status['FixedSequenceValidated']:status['State']='incomplete';status['Stage']='fixed-sequence'
        payloads={'status.json':status,'provenance.json':provenance,'config.json':dict(config,Backend=args.backend,RequestedDeadlineSeconds=args.deadline_seconds),'correctness.json':correctness,'count-map.json':count_map,'raw/cold.json':cold,'raw/warm.json':warm,'summary.json':summary}
        for name in PUBLIC: save(public/name,payloads[name])
        if set(tree(public))!=set(PUBLIC): raise RuntimeError('Unexpected public file')
        if canceled or time.monotonic()>=deadline:
            status['State']='incomplete'; status['Stage']='finalization-deadline'; save(public/'status.json',status)
        signal.setitimer(signal.ITIMER_REAL,0);finalizing=False
    print('Drained event queue portability screen: '+status['State']+'. Explicit public numeric/hash reports only; AcceptanceEvidence=false.')
    return 0 if status['State']=='complete' else 1

# Exact projection allowlists: numerical results survive post-measurement semantic failure.
COLD_NUM=('TargetTagId','ExpectedEvents','SetupTargetMutations','Ticks','Frequency','AllocatedBytes','GC0','GC1','GC2','Events','PublicAdds','MaxDepth','Checksum','PointerSize')
WARM_NUM=('Iterations','Ticks','Frequency','Bytes','Gc0','Gc1','Gc2','Checksum','N','Warmups','BeforeCycles','EventsPerCycle','CycleChecksum','EventsTotalBefore','PointerSize')
COLD_SEM=('StorageCount','ExplicitCount','EventCapacity','QueueCount','QueueNext')
WARM_SEM=('EventCapacity','QueueCount','QueueNext','CompletedCycles','EventsTotal','ChecksumTotal')
def project_sample(kind,part,value):
    nums=(COLD_NUM if kind=='cold' else WARM_NUM) if part=='Observation' else (COLD_SEM if kind=='cold' else WARM_SEM)
    if any(type(value[k]) is not int for k in nums): raise ValueError('Invalid numerical field')
    result={k:value[k] for k in nums}
    flags=(('OperationPass','FirstBridgeJitIncluded','PreBoundaryTargetReflection','WholeHeapRetainedBytesMeasured') if kind=='cold' else ('OperationPass','Control')) if part=='Observation' else (('SemanticsPass',) if kind=='cold' else ('SemanticsPass','Empty','IdentityPass'))
    text_keys={'RecordType','Shape' if kind=='cold' else 'Id','OperationErrorType' if part=='Observation' else 'ErrorType'}
    if kind=='warm':text_keys.add('Mode')
    if part=='Semantics':text_keys.add('SnapshotSHA256')
    if kind=='warm' and part=='Semantics':text_keys.add('AssemblyName')
    if set(value)!=set(nums)|set(flags)|text_keys:raise ValueError('Unexpected numerical/semantic schema')
    for key in flags:
        if type(value[key]) is not bool: raise ValueError('Invalid boolean field')
        result[key]=value[key]
    key='OperationErrorType' if part=='Observation' else 'ErrorType'
    if type(value[key]) is not str:raise ValueError('Invalid error field')
    result['ErrorObserved']=value[key]!=''
    if part=='Semantics':
        if type(value['SnapshotSHA256']) is not str or not re.fullmatch('[a-f0-9]{64}|',value['SnapshotSHA256']):raise ValueError('Invalid snapshot hash')
        result['SnapshotSHA256']=value['SnapshotSHA256']
    return result

def validate_sample(kind,a,s,name,variant,counts,config):
    key='Shape' if kind=='cold' else 'Id'
    if a['RecordType']!='measurement' or s['RecordType']!='semantics' or a[key]!=name or s[key]!=name:return False
    if a['OperationPass'] is not True or a['OperationErrorType']!='' or s['SemanticsPass'] is not True or s['ErrorType']!='' or a['PointerSize']!=8:return False
    gc=('GC0','GC1','GC2') if kind=='cold' else ('Gc0','Gc1','Gc2')
    if any(a[k]<0 for k in gc) or a['Ticks']<=0 or a['Frequency']<=0 or s['QueueCount']!=0 or s['QueueNext']!=0:return False
    if kind=='cold':
        n=config['ExpectedEvents'][name];pending=name=='full-with-pending'
        checksum=sum(i*(65537*i+1) for i in range(1,10)) if pending else sum(i*(65537+i) for i in range(1,n+1))
        return a['AllocatedBytes']>=0 and a['TargetTagId']==(8 if pending else 1) and a['Events']==a['ExpectedEvents']==n and a['PublicAdds']==(2 if pending else n) and a['MaxDepth']==1 and a['Checksum']==checksum and a['SetupTargetMutations']==0 and a['FirstBridgeJitIncluded'] is True and a['PreBoundaryTargetReflection'] is False and a['WholeHeapRetainedBytesMeasured'] is False and s['StorageCount']==(9 if pending else 1) and s['ExplicitCount']==(2 if pending else 1) and s['EventCapacity']==config['ExpectedEventCapacity'][variant][name]
    row=next(r for r in config['Rows'] if r['Id']==name);n=counts[name]
    return a['Mode']==s['Mode']=='timing' and a['Iterations']==n and a['Bytes']>=0 and a['N']==row['N'] and a['Control'] is row['Control'] and a['Warmups']==a['BeforeCycles']==128 and a['EventsPerCycle']==row['EventsPerCycle'] and a['CycleChecksum']==row['CycleChecksum'] and a['Checksum']==n*row['CycleChecksum'] and a['EventsTotalBefore']==128*row['EventsPerCycle'] and s['Empty'] is True and s['IdentityPass'] is True and s['AssemblyName']=='Test' and s['EventCapacity']==row['EventCapacityOriginal' if variant=='original' else 'EventCapacityCandidate'] and s['CompletedCycles']==128+n and s['EventsTotal']==(128+n)*row['EventsPerCycle'] and s['ChecksumTotal']==(128+n)*row['CycleChecksum']

def snapshot_file(private,kind,name,pair,variant):
    prefix='cold-'+name+'-'+str(pair)+'-'+variant if kind=='cold' else 'warm-'+str(pair)+'-'+variant+'-'+name
    return private/(prefix+'.snapshot.bin')

def pairs(records,kind,names,private):
    output=[]
    for name in names:
        for pair in range(4):
            found={r['Variant']:r for r in records if r['Id']==name and r['Pair']==pair};item={'Id':name,'Pair':pair,'Complete':False,'SemanticEqual':False}
            if set(found)==set(ORDER) and all('Observation' in r for r in found.values()):
                a,b=found['original']['Observation'],found['candidate']['Observation'];item['Complete']=True
                paths={v:snapshot_file(private,kind,name,pair,v) for v in ORDER}
                equal=all(p.is_file() and p.stat().st_size>0 for p in paths.values()) and paths['original'].read_bytes()==paths['candidate'].read_bytes()
                item['SnapshotsEqual']=equal;item['SemanticEqual']=equal and all(found[v]['Valid'] for v in ORDER)
                bytekey='AllocatedBytes' if kind=='cold' else 'Bytes';item.update(OriginalBytes=a[bytekey],CandidateBytes=b[bytekey],ByteDelta=b[bytekey]-a[bytekey])
                oldns=a['Ticks']*1e9/a['Frequency'] if a['Frequency']>0 else 0;newns=b['Ticks']*1e9/b['Frequency'] if b['Frequency']>0 else 0
                if kind=='warm':oldns=oldns/a['Iterations'] if a['Iterations']>0 else 0;newns=newns/b['Iterations'] if b['Iterations']>0 else 0
                gc=('GC0','GC1','GC2') if kind=='cold' else ('Gc0','Gc1','Gc2')
                item.update(OriginalNs=oldns,CandidateNs=newns,CandidateOverOriginal=newns/oldns if oldns>0 else None,CandidateSlower=newns>oldns,OriginalGC=[a[k] for k in gc],CandidateGC=[b[k] for k in gc],OriginalEventCapacity=found['original'].get('Semantics',{}).get('EventCapacity'),CandidateEventCapacity=found['candidate'].get('Semantics',{}).get('EventCapacity'))
                if kind=='warm':
                    oldms=a['Ticks']*1000/a['Frequency'] if a['Frequency']>0 else 0;newms=b['Ticks']*1000/b['Frequency'] if b['Frequency']>0 else 0
                    item.update(OriginalBatchMs=oldms,CandidateBatchMs=newms,AnyBatchBelowTarget=min(oldms,newms)<250,OriginalBytesPerCycle=a['Bytes']/a['Iterations'] if a['Iterations']>0 else None,CandidateBytesPerCycle=b['Bytes']/b['Iterations'] if b['Iterations']>0 else None)
            output.append(item)
    return output

if __name__=='__main__': raise SystemExit(main())
