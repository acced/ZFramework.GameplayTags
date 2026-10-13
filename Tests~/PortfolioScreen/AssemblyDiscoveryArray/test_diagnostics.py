"""Synthetic Python-only projector checks. No compiler, target, or external I/O.

Run with PYTHONDONTWRITEBYTECODE=1 python3 -m unittest -v test_diagnostics.
Canaries are synthetic. Test code only imports/reads Python source and emits the
standard fixed unittest names and counts. It never reads captured target data.
"""
import ast
import hashlib
import json
from pathlib import Path
import sys
import unittest

sys.dont_write_bytecode = True
import diagnostics
from diagnostics import ARTIFACTS, CAPTURE_EXPECTED, FRAMES, MARKERS, TYPES, project_target


CANARY = 'PRIVATE_PROJECTION_CANARY'
PRIVATE_PATH = '/private/' + CANARY + '/target.cs'
PRIVATE_TYPE = 'Private.' + CANARY + 'Exception'
PRIVATE_URL = 'https://private.invalid/' + CANARY
NET = {'EnvironmentVersion': '8.0.31', 'MonoVersion': '', 'PointerSize': 8}
MONO = {'EnvironmentVersion': '4.0.30319.42000', 'MonoVersion': '6.8.0.105', 'PointerSize': 8}
KEYS = {
    'Variant', 'Backend', 'Kind', 'Shape', 'Attempted', 'Completed', 'ExitCode',
    'CommandShapeValid', 'RuntimeHeaderPresent', 'ValidatedRuntime', 'KnownPassMarkers',
    'Measurement', 'MeasurementParseValid', 'MeasurementRowCount', 'CounterRangeValid',
    'Semantics', 'SemanticsParseValid', 'SemanticsRowCount', 'CaptureExpected',
    'CaptureBoundaryValid', 'MetadataJsonShapeValid', 'ArtifactUtf8Valid', 'Artifacts',
    'KnownExceptionCodes', 'KnownHarnessFrames', 'UnclassifiedStderrPresent',
    'UnclassifiedStdoutPresent',
}


def encoded(value):
    return json.dumps(value, separators=(',', ':')).encode('utf-8')


def command(code=0, attempted=True, completed=True):
    return {'Attempted': attempted, 'Completed': completed, 'ExitCode': code}


def metadata():
    return {'schema': 'two-capacity-metadata-v1', 'assembly': PRIVATE_PATH,
            'selection': PRIVATE_URL, 'types': [{'id': PRIVATE_TYPE, 'nested': [CANARY]}],
            'excluded_types': [{'id': CANARY}]}


def measurement(n=1):
    return {'RecordType': 'measurement', 'Shape': 'n' + str(n),
            'DeclarationCount': n, 'AttributedAssemblyCount': 0 if n == 0 else 1,
            'ObservedCount': n, 'Ticks': 17, 'Frequency': 1000000000,
            'AllocatedBytes': 256, 'GC0': 0, 'GC1': 0, 'GC2': 0, 'PointerSize': 8,
            'OperationPass': True, 'OperationErrorType': '', **CAPTURE_EXPECTED}


def semantics(n=1):
    return {'RecordType': 'semantics', 'Shape': 'n' + str(n), 'SemanticsPass': True,
            'ErrorType': '', 'BindingPass': True, 'RegistryPass': True,
            'PostAttributeTypePass': True, 'AssemblyInventoryPass': True,
            'ObservedCount': n, 'SnapshotSHA256': 'a' * 64, 'InventorySHA256': 'b' * 64,
            'AssemblyName': 'Test', 'DeclarationCount': n}


def fixtures(header=NET, kind='semantic', shape='empty', rows=None):
    if rows is None:
        rows = ([MARKERS['Empty' if shape == 'empty' else 'Retry'].encode(),
                 MARKERS['PublicCallerContexts'].encode()] if kind == 'semantic'
                else [encoded(measurement(shape)), encoded(semantics(shape))])
    return {'Stdout': b'PORTABLE_RUNTIME ' + encoded(header) + b'\n' + b'\n'.join(rows) + b'\n',
            'Stderr': b'', 'Snapshot': b'\x00\xff' + PRIVATE_PATH.encode(),
            'Inventory': (PRIVATE_PATH + '\n' + PRIVATE_URL).encode(),
            'Metadata': encoded(metadata()), 'Trace': CANARY.encode()}


def cold(rows=None, n=1, code=0, header=NET, backend='net', **artifacts):
    data = fixtures(header, 'cold', n, rows)
    data.update(artifacts)
    return project_target('original', backend, command(code), data, 'cold', n)


class ProjectionTests(unittest.TestCase):
    def clean(self, result):
        serialized = json.dumps(result, sort_keys=True, allow_nan=False)
        for forbidden in (CANARY, PRIVATE_PATH, PRIVATE_TYPE, PRIVATE_URL,
                          'AssemblyName', 'OperationErrorType', 'ErrorType',
                          'Arguments', 'SignatureBytes', 'DecoderOffset') + TYPES:
            self.assertNotIn(forbidden, serialized)
        self.assertEqual(set(result), KEYS)
        self.assertEqual(set(result['Artifacts']), set(ARTIFACTS))
        self.assertEqual(set(result['KnownPassMarkers']), set(MARKERS))
        for info in result['Artifacts'].values():
            self.assertEqual(set(info), {'Present', 'Length', 'SHA256'})
        self.assertEqual(result['CaptureExpected'], CAPTURE_EXPECTED)

    def test_semantic_both_modes_variants_backends(self):
        for backend, header in (('net', NET), ('mono', MONO)):
            for variant in ('original', 'candidate'):
                for mode in ('empty', 'retry'):
                    with self.subTest(backend=backend, variant=variant, mode=mode):
                        result = project_target(variant, backend, command(), fixtures(header, shape=mode), 'semantic', mode)
                        self.assertEqual(result['ValidatedRuntime'], header)
                        self.assertTrue(result['CommandShapeValid'])
                        self.assertTrue(result['Attempted'])
                        self.assertTrue(result['Completed'])
                        self.assertEqual(result['ExitCode'], 0)
                        self.assertTrue(result['MetadataJsonShapeValid'])
                        self.assertTrue(result['KnownPassMarkers']['Empty' if mode == 'empty' else 'Retry'])
                        self.assertFalse(result['KnownPassMarkers']['Retry' if mode == 'empty' else 'Empty'])
                        self.assertTrue(result['KnownPassMarkers']['PublicCallerContexts'])
                        self.assertFalse(result['UnclassifiedStdoutPresent'])
                        self.assertIsNone(result['Measurement'])
                        self.clean(result)

    def test_cold_three_shapes_and_independent_records(self):
        for n in (0, 1, 32):
            for backend, header in (('net', NET), ('mono', MONO)):
                with self.subTest(n=n, backend=backend):
                    result = cold(n=n, backend=backend, header=header)
                    self.assertTrue(result['MeasurementParseValid'])
                    self.assertTrue(result['SemanticsParseValid'])
                    self.assertTrue(result['CounterRangeValid'])
                    self.assertTrue(result['CaptureBoundaryValid'])
                    self.assertEqual(result['MeasurementRowCount'], 1)
                    self.assertEqual(result['SemanticsRowCount'], 1)
                    self.assertEqual(len(result['Measurement']), 19)
                    self.assertEqual(len(result['Semantics']), 14)
                    self.assertEqual(result['Measurement']['DeclarationCount'], n)
                    self.assertEqual(result['Measurement']['AttributedAssemblyCount'], 0 if n == 0 else 1)
                    self.clean(result)

    def test_nonzero_exit_preserves_measurement_and_runtime(self):
        result = cold(code=17, Stderr=('System.IO.InvalidDataException: ' + PRIVATE_PATH).encode())
        self.assertEqual(result['ExitCode'], 17)
        self.assertEqual(result['ValidatedRuntime'], NET)
        self.assertEqual(result['Measurement']['Ticks'], 17)
        self.assertTrue(result['MeasurementParseValid'])
        self.assertEqual(result['KnownExceptionCodes'], [113])
        self.clean(result)

    def test_malformed_semantics_preserves_numeric_measurement(self):
        cases = [b'{"RecordType":"semantics",', encoded(dict(semantics(), Extra=CANARY)),
                 encoded(dict(semantics(), RegistryPass=1)), b'completely malformed ' + CANARY.encode()]
        for row in cases:
            with self.subTest(row_length=len(row)):
                result = cold([encoded(measurement()), row])
                self.assertEqual(result['Measurement']['AllocatedBytes'], 256)
                self.assertFalse(result['SemanticsParseValid'])
                self.assertIsNone(result['Semantics'])
                self.assertTrue(result['UnclassifiedStdoutPresent'])
                self.clean(result)

    def test_missing_or_malformed_measurement_is_never_zero(self):
        for row in (None, b'{"RecordType":"measurement",', b'{}', b'[]', b'null',
                    encoded(dict(measurement(), Extra=CANARY))):
            rows = [encoded(semantics())] if row is None else [row, encoded(semantics())]
            result = cold(rows)
            self.assertIsNone(result['Measurement'])
            self.assertFalse(result['MeasurementParseValid'])
            self.assertIsNone(result['CounterRangeValid'])
            self.assertTrue(result['SemanticsParseValid'])
            self.clean(result)

    def test_duplicate_records_invalidate_only_their_own_record(self):
        m, s = encoded(measurement()), encoded(semantics())
        for rows, valid_m, valid_s in (([m, m, s], False, True), ([m, s, s], True, False),
                                      ([m, b'{"RecordType":"measurement",', s], False, True),
                                      ([m, s, b'{"RecordType":"semantics",'], True, False)):
            result = cold(rows)
            self.assertEqual(result['MeasurementParseValid'], valid_m)
            self.assertEqual(result['SemanticsParseValid'], valid_s)
            self.assertEqual(result['MeasurementRowCount'], 1 if valid_m else 2)
            self.assertEqual(result['SemanticsRowCount'], 1 if valid_s else 2)
            self.clean(result)

    def test_duplicate_and_extra_json_keys_are_rejected(self):
        for label, good, other in (('measurement', measurement(), encoded(semantics())),
                                    ('semantics', semantics(), encoded(measurement()))):
            prefix = 'Measurement' if label == 'measurement' else 'Semantics'
            for bad in (encoded(good)[:-1] + b',"RecordType":"' + label.encode() + b'"}',
                        encoded(good)[:-1] + b',"private":"' + CANARY.encode() + b'"}',
                        encoded(good).replace(b'"ObservedCount":1', b'"ObservedCount":1,"ObservedCount":1')):
                result = cold([bad, other])
                self.assertIsNone(result[prefix])
                self.assertFalse(result[prefix + 'ParseValid'])
                self.clean(result)
        result = cold([b'{"RecordType":"measurement","RecordType":"semantics"}'])
        self.assertEqual(result['MeasurementRowCount'], 1)
        self.assertEqual(result['SemanticsRowCount'], 1)
        self.assertIsNone(result['Measurement'])
        self.assertIsNone(result['Semantics'])

    def test_signed_counter_anomalies_remain_visible(self):
        for key in ('Ticks', 'Frequency', 'AllocatedBytes', 'GC0', 'GC1', 'GC2', 'ObservedCount'):
            for bad in (-1, (1 << 64)):
                row = dict(measurement(), **{key: bad})
                result = cold([encoded(row), encoded(semantics())])
                self.assertTrue(result['MeasurementParseValid'])
                self.assertEqual(result['Measurement'][key], bad)
                self.assertFalse(result['CounterRangeValid'])
                self.clean(result)
        result = cold([encoded(dict(measurement(), Frequency=0))])
        self.assertFalse(result['CounterRangeValid'])

    def test_counter_and_semantic_types_do_not_accept_bools_as_ints(self):
        for key in diagnostics._MEAS_INTS:
            for bad in (True, False, 1.0, '1', None):
                result = cold([encoded(dict(measurement(), **{key: bad})), encoded(semantics())])
                self.assertIsNone(result['Measurement'])
                self.assertTrue(result['SemanticsParseValid'])
        for key in ('ObservedCount', 'DeclarationCount'):
            result = cold([encoded(measurement()), encoded(dict(semantics(), **{key: True}))])
            self.assertIsNone(result['Semantics'])
            self.assertTrue(result['MeasurementParseValid'])
        for key in ('OperationPass',) + tuple(CAPTURE_EXPECTED):
            result = cold([encoded(dict(measurement(), **{key: 1}))])
            self.assertIsNone(result['Measurement'])
        for key in diagnostics._SEM_BOOLS:
            result = cold([encoded(dict(semantics(), **{key: 1}))])
            self.assertIsNone(result['Semantics'])

    def test_fixed_scenario_counts_pointer_and_boundary_flags(self):
        for changes in ({'Shape': 'n32'}, {'DeclarationCount': 32},
                        {'AttributedAssemblyCount': 0}, {'PointerSize': 4}):
            self.assertIsNone(cold([encoded(dict(measurement(), **changes))])['Measurement'])
        for changes in ({'Shape': 'n32'}, {'DeclarationCount': 32}, {'AssemblyName': CANARY}):
            self.assertIsNone(cold([encoded(dict(semantics(), **changes))])['Semantics'])
        for key in CAPTURE_EXPECTED:
            result = cold([encoded(dict(measurement(), **{key: not CAPTURE_EXPECTED[key]}))])
            self.assertTrue(result['MeasurementParseValid'])
            self.assertFalse(result['CaptureBoundaryValid'])
            self.assertEqual(result['Measurement'][key], not CAPTURE_EXPECTED[key])
            self.clean(result)

    def test_error_names_are_exact_finite_codes_or_unknown_flags(self):
        for index, name in enumerate(TYPES):
            result = cold([encoded(dict(measurement(), OperationErrorType=name, OperationPass=False)),
                           encoded(dict(semantics(), ErrorType=name, SemanticsPass=False))])
            for key in ('Measurement', 'Semantics'):
                self.assertTrue(result[key]['ErrorObserved'])
                self.assertEqual(result[key]['KnownErrorCode'], 100 + index)
                self.assertFalse(result[key]['UnclassifiedError'])
            self.clean(result)
        for name in (PRIVATE_TYPE, PRIVATE_PATH, PRIVATE_URL, 'Private.System.Exception', 'System.Exception: ' + CANARY):
            result = cold([encoded(dict(measurement(), OperationErrorType=name)),
                           encoded(dict(semantics(), ErrorType=name))])
            for key in ('Measurement', 'Semantics'):
                self.assertTrue(result[key]['ErrorObserved'])
                self.assertTrue(result[key]['UnclassifiedError'])
                self.assertIsNone(result[key]['KnownErrorCode'])
            self.clean(result)
        for bad in (None, True, 100, [], {}):
            self.assertIsNone(cold([encoded(dict(measurement(), OperationErrorType=bad))])['Measurement'])
            self.assertIsNone(cold([encoded(dict(semantics(), ErrorType=bad))])['Semantics'])

    def test_hashes_are_lowercase_hex_or_absent_null(self):
        result = cold([encoded(dict(semantics(), SnapshotSHA256='', InventorySHA256=''))])
        self.assertTrue(result['SemanticsParseValid'])
        self.assertIsNone(result['Semantics']['SnapshotSHA256'])
        self.assertIsNone(result['Semantics']['InventorySHA256'])
        for key in ('SnapshotSHA256', 'InventorySHA256'):
            for bad in ('A' * 64, 'z' * 64, 'a' * 63, 'a' * 65, PRIVATE_URL, None, 0, True):
                result = cold([encoded(measurement()), encoded(dict(semantics(), **{key: bad}))])
                self.assertIsNone(result['Semantics'])
                self.assertTrue(result['MeasurementParseValid'])
                self.clean(result)

    def test_runtime_header_exact_keys_pins_types_and_bounds(self):
        bad_headers = [dict(MONO, EnvironmentVersion=value) for value in
                       (CANARY, '', '4', '4.0', '4.0.1.2.3', '4.0.1\n', '\u0664.0.1',
                        '4.0.65536', '4.0.000001', 4, None, True)]
        bad_headers += [dict(MONO, PointerSize=value) for value in (4, 0, '8', 8.0, True)]
        bad_headers += [dict(MONO, MonoVersion=value) for value in ('', '6.8.0.106', CANARY, None)]
        bad_headers += [dict(MONO, Extra=CANARY), {}, [], None]
        for value in bad_headers:
            result = cold(header=value, backend='mono')
            self.assertTrue(result['RuntimeHeaderPresent'])
            self.assertIsNone(result['ValidatedRuntime'])
            self.assertTrue(result['MeasurementParseValid'])
            self.clean(result)
        for value in (dict(NET, EnvironmentVersion='8.0.30'), dict(NET, MonoVersion='6.8.0.105'), MONO):
            self.assertIsNone(cold(header=value)['ValidatedRuntime'])
        for raw in (b'PORTABLE_RUNTIME {bad\n', b'PORTABLE_RUNTIME ' + encoded(NET)[:-1] + b',"PointerSize":8}\n'):
            result = cold(Stdout=raw + encoded(measurement()))
            self.assertTrue(result['RuntimeHeaderPresent'])
            self.assertIsNone(result['ValidatedRuntime'])
            self.assertTrue(result['MeasurementParseValid'])
        for raw in (None, b'', b'prefix\n' + fixtures()['Stdout']):
            result = cold(Stdout=raw)
            self.assertFalse(result['RuntimeHeaderPresent'])
            self.assertIsNone(result['ValidatedRuntime'])

    def test_invalid_utf8_is_local_and_never_replacement_decoded(self):
        m = encoded(measurement())
        for data in ({'Stderr': b'\xff' + PRIVATE_PATH.encode()},
                     {'Inventory': b'\xff'}, {'Metadata': b'\xff'}, {'Trace': b'\xff'},
                     {'Stdout': b'PORTABLE_RUNTIME ' + encoded(NET) + b'\n' + m + b'\n\xff\n'},
                     {'Stdout': b'PORTABLE_RUNTIME \xff\n' + m}):
            result = cold(**data)
            self.assertTrue(result['MeasurementParseValid'])
            for key in data:
                self.assertFalse(result['ArtifactUtf8Valid'][key])
            self.clean(result)
        result = cold([encoded(measurement()), b'{"RecordType":"semantics","ErrorType":"\xff"}'])
        self.assertTrue(result['MeasurementParseValid'])
        self.assertFalse(result['SemanticsParseValid'])
        self.assertEqual(result['SemanticsRowCount'], 1)

    def test_absent_empty_incomplete_and_completed_are_distinct(self):
        absent = project_target('original', 'net', None, {}, 'semantic', 'empty')
        self.assertFalse(absent['Attempted'])
        self.assertFalse(absent['Completed'])
        self.assertIsNone(absent['ExitCode'])
        self.assertTrue(absent['CommandShapeValid'])
        for info in absent['Artifacts'].values():
            self.assertEqual(info, {'Present': False, 'Length': None, 'SHA256': None})
        for valid in (command(None, False, False), command(None, True, False), command(0), command(17)):
            result = project_target('candidate', 'net', valid, {key: b'' for key in ARTIFACTS}, 'semantic', 'retry')
            self.assertTrue(result['CommandShapeValid'])
            self.assertEqual(result['Attempted'], valid['Attempted'])
            self.assertEqual(result['Completed'], valid['Completed'])
            for info in result['Artifacts'].values():
                self.assertEqual(info, {'Present': True, 'Length': 0, 'SHA256': hashlib.sha256(b'').hexdigest()})
            self.clean(result)
        for invalid in ({}, command(0, False, True), command(None), command(0, True, False),
                        dict(command(), Extra=CANARY), command(True), command('0'),
                        command(0, 1, True), command(0, True, 1)):
            result = project_target('original', 'net', invalid, {}, 'semantic', 'empty')
            self.assertFalse(result['CommandShapeValid'])
            if type(invalid.get('ExitCode')) is not int:
                self.assertIsNone(result['ExitCode'])
            self.clean(result)
        self.clean(absent)

    def test_metadata_shape_is_strict_and_not_equivalence(self):
        good = metadata()
        self.assertTrue(cold(Metadata=encoded(good))['MetadataJsonShapeValid'])
        for bad in (None, b'', b'\xff', b'{bad', b'null', b'[]', b'{}',
                    encoded(dict(good, schema=CANARY)), encoded(dict(good, types=[])),
                    encoded(dict(good, types=[CANARY])), encoded(dict(good, excluded_types=[CANARY])),
                    encoded(dict(good, assembly=1)), encoded(dict(good, selection=False)),
                    encoded(dict(good, Extra=CANARY)),
                    encoded(good)[:-1] + b',"schema":"two-capacity-metadata-v1"}',
                    encoded(good).replace(b'"nested":[', b'"id":"duplicate","nested":['),
                    encoded(good).replace(b'"nested":[', b'"bad":1e999,"nested":[')):
            result = cold(Metadata=bad)
            self.assertFalse(result['MetadataJsonShapeValid'])
            self.assertTrue(result['MeasurementParseValid'])
            self.clean(result)

    def test_exception_code_mapping_and_fixed_frames(self):
        expected = ('System.Exception', 'System.NullReferenceException',
            'System.InvalidOperationException', 'System.NotImplementedException',
            'System.NotSupportedException', 'System.Reflection.TargetInvocationException',
            'System.TypeInitializationException', 'System.TypeLoadException',
            'System.MissingMethodException', 'System.MissingFieldException',
            'System.ArgumentException', 'System.ArgumentNullException',
            'System.OutOfMemoryException', 'System.IO.InvalidDataException',
            'System.IO.IOException', 'System.IO.FileNotFoundException',
            'System.IO.FileLoadException', 'System.ArgumentOutOfRangeException',
            'System.IndexOutOfRangeException', 'System.BadImageFormatException',
            'DiscoveryConstructorException')
        self.assertEqual(TYPES, expected)
        stderr = '\n'.join(name + ': ' + PRIVATE_URL for name in TYPES)
        stderr += '\n' + '\n'.join(' at ' + token + ('Read' if key == 'METADATA_SIGNATURE' else '')
                                     + ' (' + PRIVATE_PATH + ')' for key, token in FRAMES.items())
        result = cold(Stderr=stderr.encode())
        self.assertEqual(result['KnownExceptionCodes'], list(range(100, 121)))
        self.assertEqual(result['KnownHarnessFrames'], list(FRAMES))
        self.assertFalse(result['UnclassifiedStderrPresent'])
        self.clean(result)

    def test_unknown_stderr_frames_paths_urls_text_never_escape(self):
        stderr = ('Private.System.Exception: ' + PRIVATE_PATH + '\n' + PRIVATE_TYPE + ': ' + PRIVATE_URL
            + '\n at Private.Hidden (' + PRIVATE_PATH + ')\n at MetadataGate.WritePrivate ()'
            + '\n at MetadataGate+SignatureDecoder.DynamicPrivate ()'
            + '\n at DiscoveryArrayCheck.NativeAllPrivate ()\n' + CANARY)
        result = cold(Stderr=stderr.encode())
        self.assertEqual(result['KnownExceptionCodes'], [])
        self.assertEqual(result['KnownHarnessFrames'], [])
        self.assertTrue(result['UnclassifiedStderrPresent'])
        self.clean(result)
        result = cold([encoded(measurement()), encoded(semantics()), PRIVATE_URL.encode(), PRIVATE_PATH.encode()])
        self.assertTrue(result['UnclassifiedStdoutPresent'])
        self.assertTrue(result['MeasurementParseValid'])
        self.clean(result)

    def test_pass_markers_require_exact_whole_lines(self):
        rows = [b'prefix ' + value.encode() + b' ' + CANARY.encode() for value in MARKERS.values()]
        result = project_target('original', 'net', command(), fixtures(rows=rows), 'semantic', 'empty')
        self.assertFalse(any(result['KnownPassMarkers'].values()))
        self.assertTrue(result['UnclassifiedStdoutPresent'])
        self.clean(result)

    def test_binary_artifacts_only_lengths_hashes_and_fixed_roles(self):
        data = b'\xff\x00\xfe' + PRIVATE_PATH.encode()
        artifacts = {key: data for key in ARTIFACTS}
        artifacts[CANARY] = {'private': PRIVATE_URL}
        result = project_target('candidate', 'net', command(), artifacts, 'cold', 32)
        for info in result['Artifacts'].values():
            self.assertEqual(info, {'Present': True, 'Length': len(data), 'SHA256': hashlib.sha256(data).hexdigest()})
        self.clean(result)

    def test_input_labels_and_artifact_types_are_guarded(self):
        for variant, backend, kind, shape in ((CANARY, 'net', 'cold', 1), ('original', CANARY, 'cold', 1),
            ('original', 'net', CANARY, 1), ('original', 'net', 'cold', True),
            ('original', 'net', 'cold', '1'), ('original', 'net', 'cold', 2),
            ('original', 'net', 'semantic', CANARY)):
            with self.assertRaises(ValueError) as caught:
                project_target(variant, backend, None, {}, kind, shape)
            self.assertNotIn(CANARY, str(caught.exception))
        for bad in ('text', bytearray(b'bytes'), 0, False):
            with self.assertRaises(TypeError):
                project_target('original', 'net', None, {'Snapshot': bad}, 'cold', 1)
        for bad in ('command', 0, False, []):
            with self.assertRaises(TypeError):
                project_target('original', 'net', bad, {}, 'cold', 1)

    def test_nonfinite_numbers_and_nonobject_rows_are_rejected(self):
        for bad in (b'NaN', b'Infinity', b'-Infinity', b'1e999'):
            result = cold([encoded(measurement()).replace(b'"Ticks":17', b'"Ticks":' + bad), encoded(semantics())])
            self.assertIsNone(result['Measurement'])
            self.assertTrue(result['SemanticsParseValid'])
            self.clean(result)
        for bad in (b'[]', b'null', b'42', b'"text"', b'[{}]'):
            result = cold([bad])
            self.assertIsNone(result['Measurement'])
            self.assertIsNone(result['Semantics'])

    def test_projection_module_has_no_io_imports_or_calls(self):
        source = Path(diagnostics.__file__).read_text(encoding='utf-8')
        tree = ast.parse(source)
        imports = {node.names[0].name for node in ast.walk(tree) if isinstance(node, ast.Import)}
        self.assertEqual(imports, {'hashlib', 'json', 'math', 're'})
        self.assertFalse(any(isinstance(node, ast.ImportFrom) for node in ast.walk(tree)))
        for node in ast.walk(tree):
            if isinstance(node, ast.Call) and isinstance(node.func, ast.Name):
                self.assertNotIn(node.func.id, {'open', 'print', 'input', 'exec', 'eval', '__import__'})


if __name__ == '__main__':
    unittest.main()
