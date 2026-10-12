"""Pure fixed-schema projection. Never return unrecognized strings or raw diagnostics."""
import hashlib
import json
import re

MARKERS = {
    'NoneBeforeRegistry': 'PASS None and named-None before registry initialization',
    'CoreSummary': '7,595,855 assertions; 0 failed groups;',
    'Binding70': '{"Checks":70,"FailedGroups":0}',
    'Supplemental13': 'PASS binding-record supplemental frames: 13',
    'MetadataComplete': 'PASS core and binding suites, record layout, complete portable metadata capture; no performance measurement',
}
TYPES = ('System.Exception','System.NullReferenceException','System.InvalidOperationException','System.NotImplementedException','System.NotSupportedException','System.Reflection.TargetInvocationException','System.TypeInitializationException','System.TypeLoadException','System.MissingMethodException','System.MissingFieldException','System.ArgumentException','System.ArgumentNullException','System.OutOfMemoryException','System.IO.InvalidDataException','System.IO.IOException','System.IO.FileNotFoundException','System.IO.FileLoadException')
FRAMES = {
    'ENTRY': 'PortableBindingEntry.Main',
    'SEMANTIC_MAIN': 'BindingRecordCheck.Main',
    'SEMANTIC_NONE': 'BindingRecordCheck.NoneBeforeRegistry',
    'SEMANTIC_NAMES': 'BindingRecordCheck.NamesGrowthAndHiddenMethods',
    'SEMANTIC_QUEUED': 'BindingRecordCheck.QueuedUnbind',
    'SEMANTIC_SNAPSHOT': 'BindingRecordCheck.Snapshot',
    'MANAGED_SIZE': 'BindingRecordCheck.ManagedSize',
    'CORE_MAIN': 'CoreProgram.Main',
    'CORE_GROUP': 'CoreProgram.Run',
    'CORE_REGISTRATION': 'FixtureRegistration.Register',
    'BINDING_LIFECYCLE': 'Probe.Test',
    'BINDING_RELEASE': 'Probe.ReleaseProbe',
    'METADATA_MAIN': 'MetadataGate.Write',
    'METADATA_TYPE': 'MetadataGate.CaptureType',
    'METADATA_FIELD': 'MetadataGate.CaptureField',
    'METADATA_PROPERTY': 'MetadataGate.CaptureProperty',
    'METADATA_EVENT': 'MetadataGate.CaptureEvent',
    'METADATA_METHOD': 'MetadataGate.CaptureMethod',
    'METADATA_PARAMETER': 'MetadataGate.CaptureParameter',
    'METADATA_BODY': 'MetadataGate.CaptureBody',
    'METADATA_IL': 'MetadataGate.DecodeIL',
    'METADATA_INTERFACE': 'MetadataGate.InterfaceMap',
    'METADATA_ATTRIBUTES': 'MetadataGate.Attributes',
    'METADATA_REFERENCE': 'MetadataGate.MemberReference',
    'METADATA_TYPE_NAME': 'MetadataGate.FormatType',
    'METADATA_VALUE': 'MetadataGate.Value',
    'METADATA_SIGNATURE': 'MetadataGate+SignatureDecoder.',
    'METADATA_JSON': 'MetadataGate.Json',
}
COORDINATOR = {
    'TimeoutError: Fixed deadline': 'DEADLINE',
    'TimeoutError: Fixed deadline or termination': 'CANCELLATION',
    'RuntimeError: Command failed': 'COMMAND_NONZERO',
    'RuntimeError: Build artifact changed': 'ARTIFACT_INTEGRITY',
    'RuntimeError: Runtime tree changed': 'RUNTIME_INTEGRITY',
    'RuntimeError: Prepared input changed': 'INPUT_INTEGRITY',
    'RuntimeError: Prepared inventory changed': 'INPUT_INVENTORY',
    'RuntimeError: Tool input changed': 'TOOL_INTEGRITY',
    'RuntimeError: Tool inventory changed': 'TOOL_INVENTORY',
}
COMMANDS = ('archive-baseline','compile-original','compile-candidate','semantic-original')
ARTIFACTS = ('Stdout','Stderr','Snapshot','Layout','Metadata')

def file_info(data):
    return {'Present':data is not None,'Length':None if data is None else len(data),'SHA256':None if data is None else hashlib.sha256(data).hexdigest()}

def project(status, commands, artifacts, failure):
    stdout=(artifacts.get('Stdout') or b'').decode('utf-8',errors='replace')
    stderr=(artifacts.get('Stderr') or b'').decode('utf-8',errors='replace')
    lines=stdout.splitlines(); markers={key:any(marker in line for line in lines) for key,marker in MARKERS.items()}
    runtime=None; header_present=bool(lines and lines[0].startswith('PORTABLE_RUNTIME '))
    if header_present:
        try:
            value=json.loads(lines[0][len('PORTABLE_RUNTIME '):])
            if set(value)=={'EnvironmentVersion','MonoVersion','PointerSize'} and type(value['PointerSize']) is int and value['PointerSize']==8 and value['MonoVersion']=='6.8.0.105' and type(value['EnvironmentVersion']) is str and re.fullmatch(r'\d+(?:\.\d+){2,3}',value['EnvironmentVersion']):
                runtime={key:value[key] for key in ('EnvironmentVersion','MonoVersion','PointerSize')}
        except (ValueError,TypeError,KeyError): pass
    layout=None
    try:
        value=json.loads(artifacts.get('Layout') or b'null')
        keys=('PointerSize','GameplayTagSize','BindDataSize')
        if isinstance(value,dict) and set(value)==set(keys) and all(type(value[k]) is int and value[k]>0 for k in keys) and value['PointerSize']==8: layout={k:value[k] for k in keys}
    except (ValueError,TypeError,KeyError): pass
    metadata_valid=False
    try:
        value=json.loads(artifacts.get('Metadata') or b'null')
        metadata_valid=isinstance(value,dict) and value.get('schema')=='two-capacity-metadata-v1' and isinstance(value.get('types'),list) and bool(value['types'])
    except (ValueError,TypeError): pass
    exceptions=[name for name in TYPES if re.search(r'(?<![A-Za-z0-9_.])'+re.escape(name)+r'(?=\s*:|\s*\()',stderr)]
    frames=[key for key,token in FRAMES.items() if any(re.search(r'^\s*at\s+'+re.escape(token),line) for line in stderr.splitlines())]
    fixed_commands=[]; unknown_command=False
    for item in commands:
        name=item.get('Name')
        if name not in COMMANDS: unknown_command=True; continue
        code=item.get('ExitCode'); fixed_commands.append({'Name':name,'ExitCode':code if type(code) is int else None})
    counts={key:status.get(key) if type(status.get(key)) is int and status[key]>=0 else None for key in ('BuildAttempts','Builds','SemanticTargets','ColdTargets','WarmTargets','PilotTargets')}
    target=next((x['ExitCode'] for x in fixed_commands if x['Name']=='semantic-original'),None)
    budget=not unknown_command and [x['Name'] for x in fixed_commands]==list(COMMANDS) and tuple(counts.values())==(2,2,1,0,0,0)
    coordinator_complete=status.get('State')=='complete' and status.get('Stage')=='completed-original-semantic-diagnostic'
    if not coordinator_complete or not budget or target is None: outcome='INCOMPLETE_DIAGNOSTIC'
    elif target!=0: outcome='TARGET_NONZERO_EXIT'
    elif runtime is None: outcome='HEADER_VALIDATION_FAILURE'
    elif not all(markers.values()) or layout is None or not metadata_valid or not artifacts.get('Snapshot'): outcome='ZERO_EXIT_UNVERIFIED'
    else: outcome='NON_REPRODUCTION'
    failure_lines=failure.splitlines(); codes=sorted({code for line,code in COORDINATOR.items() if line in failure_lines})
    return {'Outcome':outcome,'AcceptanceEvidence':False,'RootCauseEstablished':False,'CoordinatorComplete':coordinator_complete,'Counts':counts,'ExpectedBudgetObserved':budget,'Commands':fixed_commands,'UnknownCommandObserved':unknown_command,'SemanticExitCode':target,'RuntimeHeaderPresent':header_present,'ValidatedRuntime':runtime,'KnownPassMarkers':markers,'ValidatedManagedLayout':layout,'MetadataJsonShapeValid':metadata_valid,'Artifacts':{key:file_info(artifacts.get(key)) for key in ARTIFACTS},'KnownExceptionTypes':exceptions,'KnownHarnessFrames':frames,'UnclassifiedStderrPresent':bool(stderr) and not exceptions,'CoordinatorFailureCodes':codes or (['UNCLASSIFIED'] if failure else [])}
