"""Synthetic data checks for report integrity, not runtime-performance evidence."""
import copy
import json
from pathlib import Path
import tempfile
import unittest
import run

class ReportIntegrityTests(unittest.TestCase):
    variants = [f'{v}-{m}' for v in ('original','candidate') for m in ('Auto','Sparse','Dense')]
    def fixture(self, root):
        for variant in self.variants:
            for round_id in range(3):
                scale = 2 if variant.startswith('candidate') else 1
                row = {'stage':'bulk','operation':'union.into','universe':65536,'leftCount':8,'rightCount':8,
                    'distribution':'scattered','overlap':50,'relation':'equal','digest':'same-input',
                    'status':'measured','ns':[scale*(10+round_id),scale*(20+round_id),scale*(30+round_id)],
                    'allocated_bytes':[0,0,0],'details':{'leftBufferBytes':32}}
                (root/f'{variant}-r{round_id}.json').write_text(json.dumps({'rows':[row]}))
    def test_process_medians_and_all_samples_retained(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory); self.fixture(root)
            rows,failures=run.aggregate(root,self.variants,3)
            self.assertEqual(failures,[])
            self.assertEqual(rows[0]['candidate_over_original']['Auto'],2)
            self.assertEqual(rows[0]['variants']['original-Auto']['round_medians_ns'],[20,21,22])
            self.assertEqual(rows[0]['variants']['original-Auto']['max_batch_mean_ns'],32)
    def test_changed_fixture_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory); self.fixture(root)
            p=root/'candidate-Auto-r0.json'; data=json.loads(p.read_text()); data['rows'][0]['digest']='changed'; p.write_text(json.dumps(data))
            with self.assertRaisesRegex(ValueError,'Input fixture mismatch'): run.aggregate(root,self.variants,3)
    def test_missing_row_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory); self.fixture(root)
            (root/'candidate-Auto-r0.json').write_text('{"rows":[]}')
            with self.assertRaisesRegex(ValueError,'Missing or extra'): run.aggregate(root,self.variants,3)
    def test_duplicate_row_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory); self.fixture(root)
            p=root/'candidate-Auto-r0.json'; data=json.loads(p.read_text()); data['rows']*=2; p.write_text(json.dumps(data))
            with self.assertRaisesRegex(ValueError,'Duplicate'): run.aggregate(root,self.variants,3)
    def test_nonfinite_samples_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory); self.fixture(root)
            p=root/'candidate-Auto-r0.json'; data=json.loads(p.read_text()); data['rows'][0]['ns'][0]=float('nan'); p.write_text(json.dumps(data))
            with self.assertRaisesRegex(ValueError,'Invalid timing sample'): run.aggregate(root,self.variants,3)
    def test_wrong_sample_counts_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory); self.fixture(root)
            p=root/'candidate-Auto-r0.json'; data=json.loads(p.read_text()); data['rows'][0]['allocated_bytes'].pop(); p.write_text(json.dumps(data))
            with self.assertRaisesRegex(ValueError,'Wrong raw sample count'): run.aggregate(root,self.variants,3)
    def test_failed_row_stays_visible_and_cannot_rank(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory); self.fixture(root)
            p=root/'candidate-Auto-r0.json'; data=json.loads(p.read_text()); data['rows'][0]['status']='failed'; data['rows'][0]['error']='control'; p.write_text(json.dumps(data))
            rows,failures=run.aggregate(root,self.variants,3)
            self.assertEqual(len(failures),1)
            self.assertNotIn('Auto',rows[0]['candidate_over_original'])
            self.assertEqual(rows[0]['variants']['candidate-Auto']['status'],'failed')

if __name__=='__main__': unittest.main()
