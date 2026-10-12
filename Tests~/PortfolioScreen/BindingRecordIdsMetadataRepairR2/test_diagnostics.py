"""Synthetic, Python-only sanitizer tests. Never launch a compiler or target.

Run explicitly with Python bytecode writes disabled. Test data is artificial.
"""
import hashlib
import json
import sys
import unittest

sys.dont_write_bytecode = True
from diagnostics import ARTIFACTS, FRAMES, MARKERS, TYPES, project_target


CANARY = 'PRIVATE_CANARY_DO_NOT_DISCLOSE'
PRIVATE_PATH = '/private/' + CANARY + '/target.cs'
PRIVATE_TYPE = 'Private.' + CANARY + 'Exception'
NET = {'EnvironmentVersion': '8.0.31', 'MonoVersion': '', 'PointerSize': 8}
MONO = {'EnvironmentVersion': '4.0.30319.42000', 'MonoVersion': '6.8.0.105', 'PointerSize': 8}
LAYOUT = {'PointerSize': 8, 'GameplayTagSize': 16, 'BindDataSize': 24}
KEYS = {
    'Variant', 'Backend', 'Attempted', 'ExitCode', 'RuntimeHeaderPresent',
    'ValidatedRuntime', 'KnownPassMarkers', 'ValidatedManagedLayout',
    'MetadataJsonShapeValid', 'Artifacts', 'KnownExceptionCodes',
    'KnownHarnessFrames', 'UnclassifiedStderrPresent',
}


def encoded(value):
    return json.dumps(value).encode('utf-8')


def fixtures(header=NET):
    return {
        'Stdout': ('PORTABLE_RUNTIME ' + json.dumps(header) + '\n'
                   + '\n'.join(MARKERS.values()) + '\n' + CANARY).encode('utf-8'),
        'Stderr': b'',
        'Snapshot': (PRIVATE_PATH + '\x00' + CANARY).encode('utf-8'),
        'Layout': encoded(LAYOUT),
        'Metadata': encoded({'schema': 'two-capacity-metadata-v1',
                             'types': [{'id': PRIVATE_TYPE, 'message': CANARY}],
                             'extra': PRIVATE_PATH}),
        CANARY: {'ignored': PRIVATE_PATH},
    }


def command(code=0):
    return {'Name': CANARY, 'ExitCode': code, 'Arguments': [PRIVATE_PATH],
            'Extra': {'message': CANARY}}


class ProjectionTests(unittest.TestCase):
    def clean(self, result):
        text = json.dumps(result, sort_keys=True)
        for forbidden in (CANARY, PRIVATE_PATH, PRIVATE_TYPE, 'Arguments',
                          'SIGNATURE_DIAGNOSTIC', 'SignatureDiagnostic',
                          'SignatureBytes', 'DecoderOffset') + TYPES:
            self.assertNotIn(forbidden, text)
        self.assertEqual(set(result), KEYS)
        self.assertEqual(set(result['Artifacts']), set(ARTIFACTS))
        self.assertEqual(set(result['KnownPassMarkers']), set(MARKERS))
        for info in result['Artifacts'].values():
            self.assertEqual(set(info), {'Present', 'Length', 'SHA256'})

    def test_valid_headers_for_both_backends_and_variants(self):
        for backend, header in (('net', NET), ('mono', MONO)):
            for variant in ('original', 'candidate'):
                with self.subTest(backend=backend, variant=variant):
                    result = project_target(variant, backend, command(), fixtures(header))
                    self.assertEqual(result['Variant'], variant)
                    self.assertEqual(result['Backend'], backend)
                    self.assertTrue(result['Attempted'])
                    self.assertEqual(result['ExitCode'], 0)
                    self.assertTrue(result['RuntimeHeaderPresent'])
                    self.assertEqual(result['ValidatedRuntime'], header)
                    self.assertTrue(all(result['KnownPassMarkers'].values()))
                    self.assertEqual(result['ValidatedManagedLayout'], LAYOUT)
                    self.assertTrue(result['MetadataJsonShapeValid'])
                    self.assertFalse(result['UnclassifiedStderrPresent'])
                    self.clean(result)

    def test_failed_target_keeps_independent_valid_runtime(self):
        for backend, header in (('net', NET), ('mono', MONO)):
            result = project_target('original', backend, command(17), dict(
                fixtures(header), Stderr=('System.IO.InvalidDataException: ' + PRIVATE_PATH
                                         + '\n at MetadataGate.DecodeIL (' + CANARY + ')\n').encode()))
            self.assertEqual(result['ExitCode'], 17)
            self.assertEqual(result['ValidatedRuntime'], header)
            self.assertEqual(result['KnownExceptionCodes'], [113])
            self.assertEqual(result['KnownHarnessFrames'], ['METADATA_IL'])
            self.assertFalse(result['UnclassifiedStderrPresent'])
            self.clean(result)

    def test_exception_codes_are_finite_and_stable(self):
        stderr = '\n'.join(name + ': ' + CANARY for name in TYPES).encode()
        result = project_target('candidate', 'net', command(1), dict(fixtures(), Stderr=stderr))
        self.assertEqual(result['KnownExceptionCodes'], list(range(100, 120)))
        self.assertEqual(TYPES[13], 'System.IO.InvalidDataException')
        self.assertEqual(TYPES[17:], ('System.ArgumentOutOfRangeException',
                                     'System.IndexOutOfRangeException', 'System.BadImageFormatException'))
        self.assertFalse(result['UnclassifiedStderrPresent'])
        self.clean(result)

    def test_unknown_exception_and_extra_fields_do_not_escape(self):
        stderr = ('System.ArgumentOutOfRangeException: ' + CANARY + '\n'
                  + PRIVATE_TYPE + ': ' + PRIVATE_PATH + '\n'
                  + ' at Private.Hidden (' + PRIVATE_PATH + ')\n'
                  + 'SIGNATURE_DIAGNOSTIC {"SignatureBytes":[1],"DecoderOffset":1,"Secret":"'
                  + CANARY + '"}\n').encode()
        result = project_target('candidate', 'net', command(1), dict(fixtures(), Stderr=stderr))
        self.assertEqual(result['KnownExceptionCodes'], [117])
        self.assertEqual(result['KnownHarnessFrames'], [])
        self.assertTrue(result['UnclassifiedStderrPresent'])
        self.clean(result)

    def test_only_fixed_frame_codes_survive(self):
        stderr = '\n'.join(' at ' + token + ('Read' if token.endswith('.') else '')
                           + ' (' + PRIVATE_PATH + ')' for token in FRAMES.values()).encode()
        result = project_target('original', 'mono', command(1), dict(fixtures(MONO), Stderr=stderr))
        self.assertEqual(result['KnownHarnessFrames'], list(FRAMES))
        self.assertFalse(result['UnclassifiedStderrPresent'])
        self.clean(result)
        result = project_target('original', 'net', command(1), {
            'Stderr': b' at MetadataGate.WritePrivate (hidden)\nPrivate.System.Exception: hidden\n'})
        self.assertEqual(result['KnownHarnessFrames'], [])
        self.assertEqual(result['KnownExceptionCodes'], [])
        self.assertTrue(result['UnclassifiedStderrPresent'])

    def test_bad_headers_never_echo_input(self):
        common = [dict(MONO, EnvironmentVersion=value) for value in (
            CANARY, '', '4', '4.0', '4.0.1.2.3', '4.0.1\n', '4.0.beta', '\u0664.0.1', 4, None, True)]
        common += [dict(MONO, PointerSize=value) for value in (4, 0, '8', 8.0, True)]
        common += [dict(MONO, MonoVersion=value) for value in ('', '6.8.0.106', CANARY, None)]
        common += [dict(MONO, Extra=CANARY), {}, [], None]
        for value in common:
            with self.subTest(value=value):
                result = project_target('original', 'mono', command(), fixtures(value))
                self.assertTrue(result['RuntimeHeaderPresent'])
                self.assertIsNone(result['ValidatedRuntime'])
                self.clean(result)
        for value in (dict(NET, EnvironmentVersion='8.0.30'), dict(NET, MonoVersion='6.8.0.105'),
                      dict(NET, EnvironmentVersion=8), dict(NET, Extra=CANARY), MONO):
            self.assertIsNone(project_target('original', 'net', command(), fixtures(value))['ValidatedRuntime'])
        for data in (b'PORTABLE_RUNTIME {bad\n',
                     b'PORTABLE_RUNTIME {"EnvironmentVersion":"8.0.31","MonoVersion":"","PointerSize":8,"PointerSize":8}\n'):
            result = project_target('original', 'net', command(), {'Stdout': data})
            self.assertTrue(result['RuntimeHeaderPresent'])
            self.assertIsNone(result['ValidatedRuntime'])
        self.assertIsNone(project_target('original', 'mono', command(), fixtures(NET))['ValidatedRuntime'])

    def test_header_absence_and_first_line_rule(self):
        for data in (None, b'', b'ordinary output\n', b'prefix\n' + fixtures()['Stdout']):
            result = project_target('original', 'net', command(), {'Stdout': data})
            self.assertFalse(result['RuntimeHeaderPresent'])
            self.assertIsNone(result['ValidatedRuntime'])

    def test_absent_and_zero_exit_length_layout_are_distinct(self):
        absent = project_target('original', 'net', None, {})
        self.assertFalse(absent['Attempted'])
        self.assertIsNone(absent['ExitCode'])
        self.assertIsNone(absent['ValidatedManagedLayout'])
        self.assertFalse(absent['MetadataJsonShapeValid'])
        self.assertFalse(any(absent['KnownPassMarkers'].values()))
        for info in absent['Artifacts'].values():
            self.assertEqual(info, {'Present': False, 'Length': None, 'SHA256': None})
        zero = project_target('original', 'net', command(0), {key: b'' for key in ARTIFACTS})
        self.assertTrue(zero['Attempted'])
        self.assertEqual(zero['ExitCode'], 0)
        for info in zero['Artifacts'].values():
            self.assertEqual(info, {'Present': True, 'Length': 0,
                                   'SHA256': hashlib.sha256(b'').hexdigest()})
        for code in (None, '0', 0.0, False, True, {'value': CANARY}):
            self.assertIsNone(project_target('original', 'net', command(code), {})['ExitCode'])
        self.assertTrue(project_target('original', 'net', {}, {})['Attempted'])
        for value in (None, {}, dict(LAYOUT, Extra=CANARY), dict(LAYOUT, PointerSize=4),
                      dict(LAYOUT, GameplayTagSize=0), dict(LAYOUT, BindDataSize=-1),
                      dict(LAYOUT, BindDataSize=True), dict(LAYOUT, BindDataSize='24')):
            self.assertIsNone(project_target('original', 'net', command(), {'Layout': encoded(value)})['ValidatedManagedLayout'])
        self.clean(absent)
        self.clean(zero)

    def test_metadata_shape_never_projects_type_or_message(self):
        for data in (None, b'', b'{bad', b'null', b'[]', b'{}',
                     encoded({'schema': CANARY, 'types': [{}]}),
                     encoded({'schema': 'two-capacity-metadata-v1', 'types': []}),
                     encoded({'schema': 'two-capacity-metadata-v1', 'types': [CANARY]})):
            result = project_target('candidate', 'net', command(), {'Metadata': data})
            self.assertFalse(result['MetadataJsonShapeValid'])
            self.clean(result)

    def test_binary_artifacts_are_only_lengths_and_hashes(self):
        data = b'\xff\x00\xfe' + PRIVATE_PATH.encode()
        result = project_target('candidate', 'net', command(), {key: data for key in ARTIFACTS})
        for info in result['Artifacts'].values():
            self.assertEqual(info, {'Present': True, 'Length': len(data),
                                   'SHA256': hashlib.sha256(data).hexdigest()})
        self.clean(result)

    def test_input_labels_and_artifact_types_are_restricted(self):
        for variant, backend in ((CANARY, 'net'), ('original', CANARY), ('ORIGINAL', 'net')):
            with self.assertRaises(ValueError) as caught:
                project_target(variant, backend, None, {})
            self.assertNotIn(CANARY, str(caught.exception))
        for bad in ('text', bytearray(b'bytes'), 0, False):
            with self.assertRaises(TypeError):
                project_target('original', 'net', None, {'Snapshot': bad})


if __name__ == '__main__':
    unittest.main()
