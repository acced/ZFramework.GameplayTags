"""Reference deployment guard tests; no .NET build or execution is performed."""
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

SPEC = importlib.util.spec_from_file_location('refactor_verify', Path(__file__).with_name('verify.py'))
VERIFY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFY)


class ReferenceBindingTests(unittest.TestCase):
    def fixture(self, root):
        intended = root / 'runtime' / 'GameplayTags.dll'
        host = root / 'host' / 'GameplayTags.Tests.dll'
        intended.parent.mkdir(); host.parent.mkdir()
        intended.write_bytes(b'intended runtime bytes'); host.write_bytes(b'host bytes')
        return intended, host, host.parent / intended.name, root / 'bindings.json'

    def test_matching_copy_records_both_hashes(self):
        with tempfile.TemporaryDirectory() as folder:
            intended, host, deployed, report = self.fixture(Path(folder))
            deployed.write_bytes(intended.read_bytes())
            result = VERIFY.verify_reference_bindings(host, [intended], report)
            self.assertTrue(result['passed'])
            self.assertEqual(result, json.loads(report.read_text()))
            expected = hashlib.sha256(intended.read_bytes()).hexdigest()
            self.assertEqual(result['references'][0]['intended_sha256'], expected)
            self.assertEqual(result['references'][0]['deployed_sha256'], expected)

    def test_same_named_wrong_copy_is_retained_and_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            intended, host, deployed, report = self.fixture(Path(folder))
            deployed.write_bytes(b'wrong hardware runtime')
            with self.assertRaisesRegex(RuntimeError, 'before execution'):
                VERIFY.verify_reference_bindings(host, [intended], report)
            row = json.loads(report.read_text())['references'][0]
            self.assertFalse(row['matches']); self.assertNotEqual(row['intended_sha256'], row['deployed_sha256'])
            self.assertEqual(deployed.read_bytes(), b'wrong hardware runtime')

    def test_missing_intended_or_deployed_file_is_retained_and_rejected(self):
        for missing in ('intended', 'deployed', 'both'):
            with self.subTest(missing=missing), tempfile.TemporaryDirectory() as folder:
                intended, host, deployed, report = self.fixture(Path(folder))
                if missing == 'intended': deployed.write_bytes(intended.read_bytes())
                if missing in ('intended', 'both'): intended.unlink()
                with self.assertRaisesRegex(RuntimeError, 'before execution'):
                    VERIFY.verify_reference_bindings(host, [intended], report)
                self.assertFalse(json.loads(report.read_text())['passed'])

    def run_simulated_pipeline(self, output, inject_wrong_copy):
        executed = []
        def fake_process(command, **kwargs):
            if command[1] == 'build':
                project = ET.parse(command[2]).getroot()
                assembly = project.findtext('PropertyGroup/AssemblyName')
                target = Path(command[command.index('-o') + 1]); target.mkdir(parents=True)
                (target / (assembly + '.dll')).write_bytes(str(target).encode())
                for hint in project.findall('.//Reference/HintPath'):
                    intended = Path(hint.text)
                    wrong = inject_wrong_copy and target.parent.name == 'RefactorPortableTests'
                    (target / intended.name).write_bytes(b'wrong same-name runtime' if wrong else intended.read_bytes())
            elif command[1] == 'exec':
                executed.append(Path(command[2]).parent.parent.name)
            else:
                self.fail('Unexpected external command')
            return subprocess.CompletedProcess(command, 0, stdout='simulated command\n')
        with patch.object(sys, 'argv', ['verify.py', '--output', str(output)]), \
             patch.object(VERIFY, 'verify_package', return_value={}), \
             patch.object(VERIFY, 'compile_documentation', return_value={}), \
             patch.object(VERIFY, 'compile_split_assemblies', return_value={}), \
             patch.object(VERIFY.subprocess, 'run', side_effect=fake_process):
            if inject_wrong_copy:
                with self.assertRaisesRegex(RuntimeError, 'before execution'): VERIFY.main()
            else: VERIFY.main()
        return executed

    def test_pipeline_never_executes_misbound_host(self):
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder) / 'output'
            executed = self.run_simulated_pipeline(output, True)
            self.assertNotIn('RefactorPortableTests', executed)
            self.assertFalse((output / 'RefactorPortableTests.log').exists())
            self.assertFalse((output / 'verification.json').exists())
            self.assertFalse(json.loads((output / 'RefactorPortableTests-reference-bindings.json').read_text())['passed'])

    def test_pipeline_records_all_five_reference_hosts(self):
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder) / 'output'
            executed = self.run_simulated_pipeline(output, False)
            summary = json.loads((output / 'verification.json').read_text())
            self.assertEqual(len(executed), 15)
            self.assertEqual(len(summary['reference_dll_checks']), 5)
            self.assertTrue(all(row['passed'] for row in summary['reference_dll_checks']))


if __name__ == '__main__': unittest.main()
