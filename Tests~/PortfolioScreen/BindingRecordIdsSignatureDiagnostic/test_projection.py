"""Python-only sanitizer/adapter checks. No compiler, runtime or subprocess imports/calls."""
import ast
import json
from pathlib import Path
import sys
sys.dont_write_bytecode=True
from projection import project, MARKERS
from run import adapt

status={'State':'complete','Stage':'completed-original-semantic-diagnostic','BuildAttempts':2,'Builds':2,'SemanticTargets':1,'ColdTargets':0,'WarmTargets':0,'PilotTargets':0}
commands=[{'Name':name,'ExitCode':0,'Arguments':['/canary/DO_NOT_DISCLOSE']} for name in ('archive-baseline','compile-original','compile-candidate','semantic-original')]
header='PORTABLE_RUNTIME '+json.dumps({'EnvironmentVersion':'4.0.30319.42000','MonoVersion':'6.8.0.105','PointerSize':8})
stdout=(header+'\n'+'\n'.join(MARKERS.values())+'\n').encode()
artifacts={'Stdout':stdout,'Stderr':b'','Snapshot':b'snapshot','Layout':b'{"PointerSize":8,"GameplayTagSize":16,"BindDataSize":24}','Metadata':b'{"schema":"two-capacity-metadata-v1","types":[{}]}'}
r=project(status,commands,artifacts,''); assert r['Outcome']=='NON_REPRODUCTION' and r['ValidatedRuntime']['MonoVersion']=='6.8.0.105'
commands[-1]['ExitCode']=1
r=project(status,commands,dict(artifacts,Stderr=b'System.NotSupportedException: /canary/DO_NOT_DISCLOSE\n  at MetadataGate.CaptureParameter (/canary/DO_NOT_DISCLOSE)\n'),'')
assert r['Outcome']=='TARGET_NONZERO_EXIT' and r['SemanticExitCode']==1 and r['KnownExceptionCodes']==[104] and 'METADATA_PARAMETER' in r['KnownHarnessFrames']
assert 'DO_NOT_DISCLOSE' not in json.dumps(r) and '/canary' not in json.dumps(r)
r=project(status,commands,dict(artifacts,Stderr=b'Private.DO_NOT_DISCLOSE: /canary/DO_NOT_DISCLOSE\n at Private.DO_NOT_DISCLOSE (secret)'),'Private.DO_NOT_DISCLOSE: /canary/DO_NOT_DISCLOSE')
assert r['KnownExceptionCodes']==[] and r['KnownHarnessFrames']==[] and r['UnclassifiedStderrPresent'] and r['CoordinatorFailureCodes']==['UNCLASSIFIED']
assert 'DO_NOT_DISCLOSE' not in json.dumps(r) and '/canary' not in json.dumps(r)
commands[-1]['ExitCode']=0
r=project(status,commands,dict(artifacts,Stdout=b'PORTABLE_RUNTIME {"EnvironmentVersion":"/canary/DO_NOT_DISCLOSE","MonoVersion":"6.8.0.105","PointerSize":8}\n',Layout=b'{"PointerSize":8,"GameplayTagSize":16,"BindDataSize":24,"Secret":"DO_NOT_DISCLOSE"}'),'')
assert r['Outcome']=='HEADER_VALIDATION_FAILURE' and r['ValidatedRuntime'] is None and r['ValidatedManagedLayout'] is None and 'DO_NOT_DISCLOSE' not in json.dumps(r)
r=project(status,commands,dict(artifacts,Stdout=(header+'\n').encode()),'')
assert r['Outcome']=='ZERO_EXIT_UNVERIFIED'
r=project({},[],{},'TimeoutError: Fixed deadline\n')
assert r['Outcome']=='INCOMPLETE_DIAGNOSTIC' and r['SemanticExitCode'] is None and r['CoordinatorFailureCodes']==['DEADLINE']
r=project(status,commands+[{'Name':'DO_NOT_DISCLOSE','Arguments':['/canary/DO_NOT_DISCLOSE']}],artifacts,'')
assert r['Outcome']=='INCOMPLETE_DIAGNOSTIC' and r['UnknownCommandObserved'] and 'DO_NOT_DISCLOSE' not in json.dumps(r)
if len(sys.argv)==2:
    source=Path(sys.argv[1]).read_text(); adapted=adapt(source); ast.parse(adapted)
    assert adapted.count("status['Stage']='completed-original-semantic-diagnostic'")==1
    assert "run('semantic-original',executables['original']" in adapted
    assert "record['ExitCode']=active_proc.returncode" in adapted
    assert adapted.index('raise GateStop()',adapted.index("status['Stage']='semantic'"))<adapted.index("status['Stage']='cold'")
print('PASS Python-only fixed projection and source-adapter tests; no compiler/target execution')

from projection import signature_projection
record={'MethodToken':0x06000001,'LocalSignatureToken':0x11000001,'SignatureLength':4,'SignatureSHA256':'a'*64,'DecoderOffset':4,'DecoderStage':8,'LastElementKind':17,'LastEncodedType':44,'LastResolvedToken':0x0200000b,'TypeArgumentCount':0,'MethodArgumentCount':None,'ExceptionCode':1}
assert signature_projection(json.dumps(record))==record
r=project(status,commands,dict(artifacts,Stderr=('SIGNATURE_DIAGNOSTIC '+json.dumps(record)+'\n').encode()),'')
assert r['SignatureDiagnosticValid'] and r['SignatureDiagnostic']['MethodArgumentCount'] is None and r['SignatureDiagnostic']['TypeArgumentCount']==0
for bad in (dict(record,SignatureBytes=[7,1,17,44]),dict(record,SignatureSHA256='/canary/DO_NOT_DISCLOSE'),dict(record,ExceptionCode=99),dict(record,DecoderOffset=5)):
    assert signature_projection(json.dumps(bad)) is None
absent=dict(record,SignatureLength=None,SignatureSHA256=None,DecoderOffset=None,DecoderStage=1,LastElementKind=None,LastEncodedType=None,LastResolvedToken=None)
assert signature_projection(json.dumps(absent))==absent
assert signature_projection(json.dumps(dict(absent,DecoderOffset=0))) is None
print('PASS numeric/hash-only signature projection; null and zero remain distinct')
