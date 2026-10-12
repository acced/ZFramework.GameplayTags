"""Pure per-target projection; no I/O, raw diagnostics, or acceptance decision.

Runtime validation is independent of target exit status. Header and layout
objects reject extra or duplicate keys. MetadataJsonShapeValid checks only the
fixed schema tag and a nonempty list of type objects, not metadata equivalence.
UnclassifiedStderrPresent means at least one nonblank stderr line lacks a known
exception marker or harness frame; recognized lines may still contain messages.
"""
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
# Codes 100 through 116 preserve the previous fixed projection's numbering.
TYPES = (
    'System.Exception', 'System.NullReferenceException',
    'System.InvalidOperationException', 'System.NotImplementedException',
    'System.NotSupportedException', 'System.Reflection.TargetInvocationException',
    'System.TypeInitializationException', 'System.TypeLoadException',
    'System.MissingMethodException', 'System.MissingFieldException',
    'System.ArgumentException', 'System.ArgumentNullException',
    'System.OutOfMemoryException', 'System.IO.InvalidDataException',
    'System.IO.IOException', 'System.IO.FileNotFoundException',
    'System.IO.FileLoadException', 'System.ArgumentOutOfRangeException',
    'System.IndexOutOfRangeException', 'System.BadImageFormatException',
)
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
ARTIFACTS = ('Stdout', 'Stderr', 'Snapshot', 'Layout', 'Metadata')
_RUNTIME_KEYS = ('EnvironmentVersion', 'MonoVersion', 'PointerSize')
_LAYOUT_KEYS = ('PointerSize', 'GameplayTagSize', 'BindDataSize')


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('Duplicate JSON key')
        result[key] = value
    return result


def _json(data):
    try:
        return json.loads(data, object_pairs_hook=_unique_object)
    except (ValueError, TypeError, RecursionError):
        return None


def project_target(variant, backend, command, artifacts):
    """Project original/candidate and net/mono from a command and five byte blobs.

    command is a dict or None. Its presence alone sets Attempted; only an exact
    int ExitCode survives. Name, Arguments, and all extra input fields are ignored.
    Artifacts are bytes or None. Missing keys are absent, not zero-length files.
    Only a first-line PORTABLE_RUNTIME header is considered a runtime header.
    """
    if type(variant) is not str or variant not in ('original', 'candidate'):
        raise ValueError('Unsupported variant')
    if type(backend) is not str or backend not in ('net', 'mono'):
        raise ValueError('Unsupported backend')
    if command is not None and not isinstance(command, dict):
        raise TypeError('Command must be a dict or None')
    if not isinstance(artifacts, dict):
        raise TypeError('Artifacts must be a dict')
    data = {key: artifacts.get(key) for key in ARTIFACTS}
    if any(value is not None and type(value) is not bytes for value in data.values()):
        raise TypeError('Artifacts must contain bytes or None')

    stdout = (data['Stdout'] or b'').decode('utf-8', errors='replace')
    stderr = (data['Stderr'] or b'').decode('utf-8', errors='replace')
    lines = stdout.splitlines()
    header_present = bool(lines and lines[0].startswith('PORTABLE_RUNTIME '))
    runtime = None
    if header_present:
        value = _json(lines[0][len('PORTABLE_RUNTIME '):])
        if (isinstance(value, dict) and set(value) == set(_RUNTIME_KEYS)
                and type(value['PointerSize']) is int and value['PointerSize'] == 8
                and type(value['EnvironmentVersion']) is str
                and type(value['MonoVersion']) is str):
            valid = (value['EnvironmentVersion'] == '8.0.31' and value['MonoVersion'] == '') if backend == 'net' else (
                value['MonoVersion'] == '6.8.0.105'
                and re.fullmatch(r'[0-9]+(?:\.[0-9]+){2,3}', value['EnvironmentVersion']) is not None)
            if valid:
                runtime = {key: value[key] for key in _RUNTIME_KEYS}

    layout = None
    value = _json(data['Layout'])
    if (isinstance(value, dict) and set(value) == set(_LAYOUT_KEYS)
            and all(type(value[key]) is int and value[key] > 0 for key in _LAYOUT_KEYS)
            and value['PointerSize'] == 8):
        layout = {key: value[key] for key in _LAYOUT_KEYS}
    value = _json(data['Metadata'])
    metadata_valid = (isinstance(value, dict)
                      and value.get('schema') == 'two-capacity-metadata-v1'
                      and isinstance(value.get('types'), list) and bool(value['types'])
                      and all(isinstance(item, dict) for item in value['types']))

    markers = {key: any(marker in line for line in lines) for key, marker in MARKERS.items()}
    exceptions, frames, unclassified = set(), set(), False
    for line in stderr.splitlines():
        if not line.strip():
            continue
        known = False
        for index, name in enumerate(TYPES):
            if re.search(r'(?<![A-Za-z0-9_.])' + re.escape(name) + r'(?=\s*:|\s*\()', line):
                exceptions.add(100 + index)
                known = True
        for key, token in FRAMES.items():
            tail = r'[A-Za-z_][A-Za-z0-9_]*(?=\s|\(|$)' if token.endswith('.') else r'(?=\s|\(|$)'
            if re.search(r'^\s*at\s+' + re.escape(token) + tail, line):
                frames.add(key)
                known = True
        unclassified = unclassified or not known

    code = command.get('ExitCode') if command is not None else None
    return {
        'Variant': variant,
        'Backend': backend,
        'Attempted': command is not None,
        'ExitCode': code if type(code) is int else None,
        'RuntimeHeaderPresent': header_present,
        'ValidatedRuntime': runtime,
        'KnownPassMarkers': markers,
        'ValidatedManagedLayout': layout,
        'MetadataJsonShapeValid': metadata_valid,
        'Artifacts': {key: {
            'Present': value is not None,
            'Length': None if value is None else len(value),
            'SHA256': None if value is None else hashlib.sha256(value).hexdigest(),
        } for key, value in data.items()},
        'KnownExceptionCodes': sorted(exceptions),
        'KnownHarnessFrames': [key for key in FRAMES if key in frames],
        'UnclassifiedStderrPresent': unclassified,
    }
