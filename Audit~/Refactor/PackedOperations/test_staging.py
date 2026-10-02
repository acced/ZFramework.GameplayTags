"""Source-only repository/provenance checks. These never invoke dotnet."""
import argparse
import copy
import hashlib
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import run_packed as runner


class StagingTests(unittest.TestCase):
    def test_default_is_stage_only(self):
        with mock.patch.object(runner, 'stage') as stage, mock.patch.object(runner, 'execute') as execute:
            self.assertEqual(runner.main(['--output', 'unused-evidence']), 0)
            stage.assert_called_once()
            execute.assert_not_called()

    def test_run_requires_cpu_handoff_before_subprocess(self):
        with mock.patch.object(runner.subprocess, 'run') as process:
            with self.assertRaisesRegex(ValueError, 'quiet CPU window'):
                runner.execute(argparse.Namespace(timing_owner_released=False))
            process.assert_not_called()

    def test_declared_two_source_process_matrix(self):
        self.assertEqual(runner.SOURCES, ('baseline', 'candidate'))
        jobs = [(source, backend, alias, round_id) for source in runner.SOURCES
                for backend in runner.BACKENDS for alias in ('', '-AA') for round_id in range(3)]
        self.assertEqual(len(jobs), 24)
        self.assertEqual(len(set(jobs)), 24)
        self.assertEqual(len(runner.PLAN), 372)

    def test_git_archive_and_worktree_snapshot_without_dotnet(self):
        repo = runner.HERE.parents[2]
        if not (repo/'.git').exists():
            self.skipTest('Source archive integration requires a Git checkout')
        with tempfile.TemporaryDirectory(prefix='packed-stage-') as directory:
            out = Path(directory)/'evidence'
            original_run = runner.subprocess.run

            def git_only(command, *args, **kwargs):
                self.assertEqual(command[0], 'git')
                return original_run(command, *args, **kwargs)

            with mock.patch.object(runner.subprocess, 'run', side_effect=git_only):
                manifest = runner.stage(argparse.Namespace(repo=repo, output=out))
            self.assertEqual(manifest['status'], 'staged')
            self.assertEqual(manifest['expected_processes'], 24)
            self.assertFalse((out/'build').exists())
            runner.verify_frozen(out, manifest)
            for relative, expected in manifest['source_files']['baseline'].items():
                blob = runner.git(repo, 'show', runner.REFERENCE_COMMIT+':'+relative)
                self.assertEqual(hashlib.sha256(blob).hexdigest(), expected)
            altered = copy.deepcopy(manifest)
            altered['expected_processes'] = 36
            with self.assertRaisesRegex(ValueError, 'declared protocol'):
                runner.verify_frozen(out, altered)
            (out/'sources/plan.json').write_text('{}\n')
            with self.assertRaisesRegex(ValueError, 'Frozen support changed'):
                runner.verify_frozen(out, manifest)


if __name__ == '__main__':
    unittest.main()
