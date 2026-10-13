"""Pure discovery diagnostic projection: no I/O, target execution, or acceptance.

Interface: project_target(variant, backend, command, artifacts, kind, shape).
variant is original|candidate; backend net|mono; kind semantic|cold; shape is
empty|retry for semantic or exact int 0|1|32 for cold. command is None or a dict
with Attempted: bool, Completed: bool, ExitCode: exact int|null. Its validity is
reported independently; unrecognized fields and all command text are discarded.
artifacts is a dict containing six fixed roles with bytes|null; missing roles
mean absent. No artifact content is exported, only Present/Length/SHA256.
ArtifactUtf8Valid reports bool|null for the five text roles, not binary Snapshot.

Only the first stdout line can be PORTABLE_RUNTIME. ValidatedRuntime is null
unless keys/types and the pinned x64 runtime are exact. Mono EnvironmentVersion
has 3-4 ASCII numeric components of 1-5 digits, each at most 65535. Every JSON
object rejects duplicate keys and nonfinite numbers. Runtime, measurement, and
semantics have exact keys; MetadataJsonShapeValid checks only the five-field
MetadataGate root/types shape and never implies metadata equivalence.

Cold rows are independently parsed, even after a bad exit or invalid UTF-8 in
another line/artifact. Row counts include identifiable malformed/duplicate rows;
a repeated record invalidates only that record. Missing or malformed rows are
null, never zero-valued. Measurement retains its 17 source fields except the
arbitrary OperationErrorType is replaced by ErrorObserved/KnownErrorCode/
UnclassifiedError. Semantics removes arbitrary ErrorType and validated constant
AssemblyName, adds the same error flags, and maps empty hashes to null. Only
lowercase 64-digit hexadecimal hashes survive. Counts and shape must match the
fixed scenario; negative counters survive and make CounterRangeValid false.
CaptureExpected contains the fixed three measurement-boundary flags, and
CaptureBoundaryValid independently checks the actual measurement flags.

Only finite exception codes, finite frame identifiers, booleans, numeric values,
validated runtime versions and hashes, and fixed labels are emitted. Codes
100-119 preserve R2 exactly; code120 is DiscoveryConstructorException. Unknown
stdout/stderr is represented only by booleans. No raw exception, assembly,
command, environment dump, arbitrary text, URL, or private path is exported.
"""
import hashlib
import json
import math
import re


MARKERS = {
    'Empty': 'PASS empty: automatic discovery, exact registry snapshot, API typed arrays and public initialized fast paths',
    'Retry': 'PASS retry: automatic discovery, exact registry snapshot, API typed arrays and public initialized fast paths',
    'PublicCallerContexts': 'PASS public caller contexts; no native capture, forced preparation, reset, timing or allocation sample',
}
# Keep R2 codes100-119 in exactly this order.
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
    'DiscoveryConstructorException',
)
FRAMES = {
    'ENTRY': 'PortableDiscoveryEntry.Main',
    'SEMANTIC_MAIN': 'DiscoverySemanticCheck.Main',
    'SEMANTIC_EMPTY': 'DiscoverySemanticCheck.Empty',
    'SEMANTIC_RETRY': 'DiscoverySemanticCheck.ConstructorFailureThenMalformedThenSuccess',
    'SEMANTIC_SNAPSHOT': 'DiscoverySemanticCheck.Snapshot',
    'SEMANTIC_TYPED_ARRAY': 'DiscoverySemanticCheck.TypedArray',
    'SEMANTIC_FAST_PATHS': 'DiscoverySemanticCheck.FastPaths',
    'SEMANTIC_REFERENCES': 'DiscoverySemanticCheck.CheckReferences',
    'SEMANTIC_VALUE': 'DiscoverySemanticCheck.Value',
    'COLD_MAIN': 'DiscoveryArrayCheck.Main',
    'COLD_PREPARE': 'DiscoveryArrayCheck.Prepare',
    'COLD_ASSEMBLIES': 'DiscoveryArrayCheck.CheckLoadedAssemblies',
    'COLD_NATIVE_ALL': 'DiscoveryArrayCheck.NativeAll',
    'COLD_VALIDATE': 'DiscoveryArrayCheck.ValidateAndSnapshot',
    'COLD_SNAPSHOT': 'DiscoveryArrayCheck.WriteSnapshot',
    'COLD_VALUE': 'DiscoveryArrayCheck.Value',
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
_SIGNATURE_METHODS = ('Byte', 'Compressed', 'SignedCompressed', 'Read', 'Signature',
                      'TypeToken', 'Type', '.ctor')
ARTIFACTS = ('Stdout', 'Stderr', 'Snapshot', 'Inventory', 'Metadata', 'Trace')
_TEXT_ARTIFACTS = ('Stdout', 'Stderr', 'Inventory', 'Metadata', 'Trace')
_RUNTIME_KEYS = ('EnvironmentVersion', 'MonoVersion', 'PointerSize')
_COMMAND_KEYS = ('Attempted', 'Completed', 'ExitCode')
_METADATA_KEYS = ('schema', 'assembly', 'selection', 'types', 'excluded_types')
CAPTURE_EXPECTED = {
    'FirstBridgeJitIncluded': True,
    'PreBoundaryInventoryPrimed': True,
    'WholeHeapRetainedBytesMeasured': False,
}
_MEAS_INTS = ('DeclarationCount', 'AttributedAssemblyCount', 'ObservedCount',
              'Ticks', 'Frequency', 'AllocatedBytes', 'GC0', 'GC1', 'GC2', 'PointerSize')
_MEAS_KEYS = ('RecordType', 'Shape') + _MEAS_INTS + (
    'OperationPass', 'OperationErrorType') + tuple(CAPTURE_EXPECTED)
_SEM_BOOLS = ('SemanticsPass', 'BindingPass', 'RegistryPass',
              'PostAttributeTypePass', 'AssemblyInventoryPass')
_SEM_KEYS = ('RecordType', 'Shape', 'ErrorType', 'ObservedCount',
             'SnapshotSHA256', 'InventorySHA256', 'AssemblyName', 'DeclarationCount') + _SEM_BOOLS


class _Pairs(list):
    """Private top-level record classification without accepting duplicate keys."""


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('Duplicate JSON key')
        result[key] = value
    return result


def _reject_constant(value):
    raise ValueError('Nonfinite JSON number')


def _finite_float(value):
    result = float(value)
    if not math.isfinite(result):
        raise ValueError('Nonfinite JSON number')
    return result


def _json(data, pairs_hook=_unique_object):
    try:
        return json.loads(data, object_pairs_hook=pairs_hook, parse_constant=_reject_constant,
                          parse_float=_finite_float)
    except (ValueError, TypeError, RecursionError, UnicodeError):
        return None


def _decode(data):
    if data is None:
        return None
    try:
        return data.decode('utf-8')
    except UnicodeError:
        return None


def _runtime(line, backend):
    if line is None or not line.startswith('PORTABLE_RUNTIME '):
        return None
    value = _json(line[len('PORTABLE_RUNTIME '):])
    if not (type(value) is dict and set(value) == set(_RUNTIME_KEYS)
            and type(value['PointerSize']) is int and value['PointerSize'] == 8
            and type(value['EnvironmentVersion']) is str and type(value['MonoVersion']) is str):
        return None
    environment = value['EnvironmentVersion']
    if backend == 'net':
        valid = environment == '8.0.31' and value['MonoVersion'] == ''
    else:
        valid = (value['MonoVersion'] == '6.8.0.105'
                 and re.fullmatch(r'[0-9]{1,5}(?:\.[0-9]{1,5}){2,3}', environment) is not None
                 and all(int(part) <= 65535 for part in environment.split('.')))
    return {key: value[key] for key in _RUNTIME_KEYS} if valid else None


def _error(error_type):
    known = TYPES.index(error_type) + 100 if error_type in TYPES else None
    return {'ErrorObserved': error_type != '', 'KnownErrorCode': known,
            'UnclassifiedError': error_type != '' and known is None}


def _measurement(value, shape):
    if not (type(value) is dict and set(value) == set(_MEAS_KEYS)
            and value['RecordType'] == 'measurement' and value['Shape'] == 'n' + str(shape)
            and all(type(value[key]) is int for key in _MEAS_INTS)
            and value['DeclarationCount'] == shape
            and value['AttributedAssemblyCount'] == (0 if shape == 0 else 1)
            and value['PointerSize'] == 8
            and type(value['OperationPass']) is bool
            and type(value['OperationErrorType']) is str
            and all(type(value[key]) is bool for key in CAPTURE_EXPECTED)):
        return None
    result = {key: value[key] for key in _MEAS_KEYS if key != 'OperationErrorType'}
    result.update(_error(value['OperationErrorType']))
    return result


def _semantics(value, shape):
    if not (type(value) is dict and set(value) == set(_SEM_KEYS)
            and value['RecordType'] == 'semantics' and value['Shape'] == 'n' + str(shape)
            and type(value['ObservedCount']) is int
            and type(value['DeclarationCount']) is int and value['DeclarationCount'] == shape
            and all(type(value[key]) is bool for key in _SEM_BOOLS)
            and type(value['ErrorType']) is str and value['AssemblyName'] == 'Test'
            and all(type(value[key]) is str and (value[key] == '' or
                    re.fullmatch(r'[0-9a-f]{64}', value[key]) is not None)
                    for key in ('SnapshotSHA256', 'InventorySHA256'))):
        return None
    result = {key: value[key] for key in _SEM_KEYS if key not in ('ErrorType', 'AssemblyName')}
    for key in ('SnapshotSHA256', 'InventorySHA256'):
        result[key] = result[key] or None
    result.update(_error(value['ErrorType']))
    return result


def _record_tags(raw):
    pairs = _json(raw, _Pairs)
    if type(pairs) is _Pairs:
        return {value for key, value in pairs
                if key == 'RecordType' and type(value) is str
                and value in ('measurement', 'semantics')}
    # A truncated or invalid-UTF8 record still counts against its own row type.
    return {match.decode('ascii') for match in re.findall(
        rb'(?:^|[,{])\s*"RecordType"\s*:\s*"(measurement|semantics)"', raw)}


def _counter_range(measurement):
    if measurement is None:
        return None
    for key in _MEAS_INTS:
        value = measurement[key]
        maximum = (1 << 63) - 1 if key in ('Ticks', 'Frequency', 'AllocatedBytes') else (1 << 31) - 1
        if value < 0 or value > maximum or (key == 'Frequency' and value == 0):
            return False
    return True


def _stderr_projection(raw):
    exceptions, frames, unclassified = set(), set(), False
    for raw_line in (raw or b'').splitlines():
        line = _decode(raw_line)
        if line is None:
            unclassified = True
            continue
        if not line.strip():
            continue
        known = False
        for index, name in enumerate(TYPES):
            if re.search(r'(?<![A-Za-z0-9_.])' + re.escape(name) + r'(?=\s*:|\s*\()', line):
                exceptions.add(100 + index)
                known = True
        for key, token in FRAMES.items():
            tail = ('(?:' + '|'.join(re.escape(item) for item in _SIGNATURE_METHODS) + ')'
                    if key == 'METADATA_SIGNATURE' else '')
            if re.search(r'^\s*at\s+' + re.escape(token) + tail + r'(?=\s|\(|$)', line):
                frames.add(key)
                known = True
        unclassified = unclassified or not known
    return sorted(exceptions), [key for key in FRAMES if key in frames], unclassified


def project_target(variant, backend, command, artifacts, kind, shape):
    """Return the fixed JSON-safe schema described in the module docstring."""
    if type(variant) is not str or variant not in ('original', 'candidate'):
        raise ValueError('Unsupported variant')
    if type(backend) is not str or backend not in ('net', 'mono'):
        raise ValueError('Unsupported backend')
    if type(kind) is not str or kind not in ('semantic', 'cold'):
        raise ValueError('Unsupported kind')
    if not ((kind == 'semantic' and type(shape) is str and shape in ('empty', 'retry'))
            or (kind == 'cold' and type(shape) is int and shape in (0, 1, 32))):
        raise ValueError('Unsupported shape')
    if command is not None and type(command) is not dict:
        raise TypeError('Command must be a dict or None')
    if type(artifacts) is not dict:
        raise TypeError('Artifacts must be a dict')
    data = {key: artifacts.get(key) for key in ARTIFACTS}
    if any(value is not None and type(value) is not bytes for value in data.values()):
        raise TypeError('Artifacts must contain bytes or None')

    command_value = command or {}
    attempted = command_value.get('Attempted') is True
    completed = command_value.get('Completed') is True
    code = command_value.get('ExitCode')
    command_valid = (command is None or (set(command_value) == set(_COMMAND_KEYS)
        and type(command_value['Attempted']) is bool and type(command_value['Completed']) is bool
        and (code is None or type(code) is int)
        and (not completed or attempted) and (completed == (code is not None))))
    raw_lines = (data['Stdout'] or b'').splitlines()
    lines = [_decode(line) for line in raw_lines]
    header_present = bool(raw_lines and raw_lines[0].startswith(b'PORTABLE_RUNTIME '))
    runtime = _runtime(lines[0], backend) if lines else None
    markers = {key: marker in lines for key, marker in MARKERS.items()}
    records = {'measurement': [], 'semantics': []}
    unclassified_stdout = False
    for index, raw in enumerate(raw_lines):
        if not raw.strip():
            continue
        if index == 0 and header_present:
            continue
        line = lines[index]
        if line is not None and line in MARKERS.values():
            continue
        tags = _record_tags(raw) if kind == 'cold' else set()
        if not tags:
            unclassified_stdout = True
            continue
        value = _json(line)
        for tag in tags:
            parser = _measurement if tag == 'measurement' else _semantics
            parsed = parser(value, shape)
            records[tag].append(parsed)
            unclassified_stdout = unclassified_stdout or parsed is None
    measurement_rows, semantics_rows = records['measurement'], records['semantics']
    measurement = measurement_rows[0] if len(measurement_rows) == 1 else None
    semantics = semantics_rows[0] if len(semantics_rows) == 1 else None
    if len(measurement_rows) > 1 or len(semantics_rows) > 1:
        unclassified_stdout = True
    metadata = _json(_decode(data['Metadata']))
    metadata_valid = (type(metadata) is dict and set(metadata) == set(_METADATA_KEYS)
        and metadata['schema'] == 'two-capacity-metadata-v1'
        and type(metadata['assembly']) is str and type(metadata['selection']) is str
        and type(metadata['types']) is list and bool(metadata['types'])
        and all(type(item) is dict for item in metadata['types'])
        and type(metadata['excluded_types']) is list
        and all(type(item) is dict for item in metadata['excluded_types']))
    exceptions, frames, unclassified_stderr = _stderr_projection(data['Stderr'])
    return {
        'Variant': variant, 'Backend': backend, 'Kind': kind, 'Shape': shape,
        'Attempted': attempted, 'Completed': completed,
        'ExitCode': code if type(code) is int else None,
        'CommandShapeValid': command_valid,
        'RuntimeHeaderPresent': header_present, 'ValidatedRuntime': runtime,
        'KnownPassMarkers': markers,
        'Measurement': measurement, 'MeasurementParseValid': measurement is not None,
        'MeasurementRowCount': len(measurement_rows), 'CounterRangeValid': _counter_range(measurement),
        'Semantics': semantics, 'SemanticsParseValid': semantics is not None,
        'SemanticsRowCount': len(semantics_rows),
        'CaptureExpected': dict(CAPTURE_EXPECTED),
        'CaptureBoundaryValid': None if measurement is None else all(
            measurement[key] is value for key, value in CAPTURE_EXPECTED.items()),
        'MetadataJsonShapeValid': metadata_valid,
        'ArtifactUtf8Valid': {key: None if data[key] is None else _decode(data[key]) is not None
                              for key in _TEXT_ARTIFACTS},
        'Artifacts': {key: {'Present': value is not None,
                            'Length': None if value is None else len(value),
                            'SHA256': None if value is None else hashlib.sha256(value).hexdigest()}
                      for key, value in data.items()},
        'KnownExceptionCodes': exceptions, 'KnownHarnessFrames': frames,
        'UnclassifiedStderrPresent': unclassified_stderr,
        'UnclassifiedStdoutPresent': unclassified_stdout,
    }
