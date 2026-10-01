"""Synthetic data checks for report integrity, not runtime-performance evidence."""
import copy
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace
import run

class ReportIntegrityTests(unittest.TestCase):
    variants = [f'{v}-{m}' for v in ('original','candidate') for m in ('Auto','Sparse','Dense')]
    @staticmethod
    def metadata(label, mode, only_new_apis=False):
        return {'variant':label,'requestedStorage':mode,'runtime':'.NET 8.0.31','architecture':'X64',
            'gcLatencyMode':'Batch','gcConcurrent':'0','seed':run.BENCHMARK_SEED,'portableKernelsForced':label=='candidate_portable',
            'onlyNewApis':only_new_apis,'suite':'smoke','samples':3,'targetMs':1}
    @staticmethod
    def change(path, mutate):
        payload=json.loads(path.read_text());mutate(payload);path.write_text(json.dumps(payload))
    def fixture(self, root):
        for variant in self.variants:
            for round_id in range(3):
                scale = 2 if variant.startswith('candidate') else 1
                row = {'stage':'bulk','operation':'union.into','universe':65536,'leftCount':8,'rightCount':8,
                    'distribution':'scattered','overlap':50,'relation':'equal','digest':'same-input',
                    'status':'measured','ns':[scale*(10+round_id),scale*(20+round_id),scale*(30+round_id)],
                    'allocated_bytes':[0,0,0],'details':{'leftBufferBytes':32}}
                label,mode=variant.rsplit('-',1)
                (root/f'{variant}-r{round_id}.json').write_text(json.dumps({**self.metadata(label,mode),'rows':[row]}))
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
            self.change(root/'candidate-Auto-r0.json',lambda data:data.update(rows=[]))
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
    def test_incomplete_forced_layouts_are_unranked(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory); self.fixture(root)
            for mode in ('Sparse','Dense'):
                for round_id in range(3):
                    p=root/f'candidate-{mode}-r{round_id}.json';data=json.loads(p.read_text());data['rows'][0]['status']='failed';data['rows'][0]['error']='control';p.write_text(json.dumps(data))
            rows,failures=run.aggregate(root,self.variants,3)
            self.assertEqual(len(failures),6)
            self.assertIsNone(rows[0]['fastest_forced_candidate'])
    def api_fixture(self, root):
        self.fixture(root)
        base=json.loads((root/'candidate-Auto-r0.json').read_text())['rows'][0]
        for label in ('candidate','candidate_portable'):
            for mode in ('Auto','Sparse','Dense'):
                for round_id in range(3):
                    rows=[]
                    for operation in ('difference.direct_new','difference.direct_into'):
                        row=copy.deepcopy(base);row['stage']='candidate_only';row['operation']=operation;rows.append(row)
                    (root/f'{label}-newapis-{mode}-r{round_id}.json').write_text(json.dumps({**self.metadata(label,mode,True),'candidate_only_rows':rows}))
    def test_candidate_api_matrix_checks_all_labels_and_modes(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory); self.api_fixture(root)
            # Missing in *every* round of one mode was previously invisible to per-mode validation.
            for round_id in range(3):
                p=root/f'candidate_portable-newapis-Sparse-r{round_id}.json';data=json.loads(p.read_text());data['candidate_only_rows'].pop();p.write_text(json.dumps(data))
            with self.assertRaisesRegex(ValueError,'matrix differs across modes'):
                run.candidate_api_summary(root,[],SimpleNamespace(portable=True,rounds=3))
    def test_candidate_api_digest_checks_across_modes(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory); self.api_fixture(root)
            for round_id in range(3):
                p=root/f'candidate_portable-newapis-Sparse-r{round_id}.json';data=json.loads(p.read_text());data['candidate_only_rows'][0]['digest']='changed';p.write_text(json.dumps(data))
            with self.assertRaisesRegex(ValueError,'fixture differs across modes'):
                run.candidate_api_summary(root,[],SimpleNamespace(portable=True,rounds=3))
    def test_candidate_api_complete_matrix_passes(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.api_fixture(root)
            self.assertEqual(run.candidate_api_summary(root,[],SimpleNamespace(portable=True,rounds=3)),[])
            result=json.loads((root/'candidate-api-comparison.json').read_text())
            self.assertEqual(len(result),12)
    def test_failed_row_stays_visible_and_cannot_rank(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory); self.fixture(root)
            p=root/'candidate-Auto-r0.json'; data=json.loads(p.read_text()); data['rows'][0]['status']='failed'; data['rows'][0]['error']='control'; p.write_text(json.dumps(data))
            rows,failures=run.aggregate(root,self.variants,3)
            self.assertEqual(len(failures),1)
            self.assertNotIn('Auto',rows[0]['candidate_over_original'])
            self.assertEqual(rows[0]['variants']['candidate-Auto']['status'],'failed')

    def test_common_payload_labels_and_protocol_flags_are_checked(self):
        changes={'variant':'original','requestedStorage':'Dense','portableKernelsForced':True,
                 'onlyNewApis':True,'seed':0}
        for field,value in changes.items():
            with self.subTest(field=field), tempfile.TemporaryDirectory() as directory:
                root=Path(directory);self.fixture(root)
                self.change(root/'candidate-Auto-r0.json',lambda p:p.update({field:value}))
                with self.assertRaisesRegex(ValueError,'metadata mismatch: '+field): run.aggregate(root,self.variants,3)

    def test_common_payload_metadata_is_required(self):
        for field in self.metadata('candidate','Auto'):
            with self.subTest(field=field), tempfile.TemporaryDirectory() as directory:
                root=Path(directory);self.fixture(root)
                self.change(root/'candidate-Auto-r0.json',lambda p:p.pop(field))
                with self.assertRaisesRegex(ValueError,'metadata'): run.aggregate(root,self.variants,3)

    def test_common_cohort_rejects_mixed_host_or_protocol(self):
        for field,value in {'runtime':'.NET 9.0','architecture':'Arm64','suite':'full','samples':7,'targetMs':2}.items():
            with self.subTest(field=field), tempfile.TemporaryDirectory() as directory:
                root=Path(directory);self.fixture(root)
                self.change(root/'candidate-Auto-r0.json',lambda p:p.update({field:value}))
                with self.assertRaisesRegex(ValueError,'cohort mismatch: '+field): run.aggregate(root,self.variants,3)

    def test_common_aa_and_portable_labels_are_validated(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.fixture(root)
            variants=self.variants+['original_aa-'+m for m in ('Auto','Sparse','Dense')]+['candidate_portable-'+m for m in ('Auto','Sparse','Dense')]
            for label in ('original_aa','candidate_portable'):
                for mode in ('Auto','Sparse','Dense'):
                    for round_id in range(3):
                        payload=json.loads((root/f'original-{mode}-r{round_id}.json').read_text())
                        payload.update(self.metadata(label,mode))
                        (root/f'{label}-{mode}-r{round_id}.json').write_text(json.dumps(payload))
            self.assertEqual(run.aggregate(root,variants,3)[1],[])
            self.change(root/'candidate_portable-Auto-r0.json',lambda p:p.update(portableKernelsForced=False))
            with self.assertRaisesRegex(ValueError,'portableKernelsForced'): run.aggregate(root,variants,3)

    def test_payload_protocol_matches_requested_cli(self):
        for field,value in {'suite':'full','samples':7,'target_ms':2}.items():
            with self.subTest(field=field), tempfile.TemporaryDirectory() as directory:
                root=Path(directory);self.fixture(root)
                args=SimpleNamespace(suite='smoke',samples=3,target_ms=1);setattr(args,field,value)
                with self.assertRaisesRegex(ValueError,'requested protocol'): run.aggregate(root,self.variants,3,args=args)

    def test_candidate_api_payload_labels_and_flags_are_checked(self):
        for field,value in {'variant':'candidate','requestedStorage':'Dense','portableKernelsForced':False,'onlyNewApis':False,'seed':0}.items():
            with self.subTest(field=field), tempfile.TemporaryDirectory() as directory:
                root=Path(directory);self.api_fixture(root)
                self.change(root/'candidate_portable-newapis-Auto-r0.json',lambda p:p.update({field:value}))
                with self.assertRaisesRegex(ValueError,'metadata mismatch: '+field):
                    run.candidate_api_summary(root,[],SimpleNamespace(portable=True,rounds=3))

    def test_candidate_api_cohort_links_to_common_even_if_all_api_files_match(self):
        for field,value in {'runtime':'.NET 9.0','architecture':'Arm64','suite':'full','samples':7,'targetMs':2}.items():
            with self.subTest(field=field), tempfile.TemporaryDirectory() as directory:
                root=Path(directory);self.api_fixture(root)
                for path in root.glob('*-newapis-*.json'): self.change(path,lambda p:p.update({field:value}))
                with self.assertRaisesRegex(ValueError,'cohort mismatch: '+field):
                    run.candidate_api_summary(root,[],SimpleNamespace(portable=True,rounds=3))

    def test_mixed_validation_protocols_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.api_fixture(root)
            self.change(root/'candidate-Auto-r0.json',lambda p:p.update(validation_protocol='actual-timed-output-before-reset-v2'))
            with self.assertRaisesRegex(ValueError,'cohort mismatch: validation_protocol'): run.aggregate(root,self.variants,3)
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.api_fixture(root)
            for p in root.glob('*-newapis-*.json'): self.change(p,lambda p:p.update(validation_protocol='actual-timed-output-before-reset-v2'))
            with self.assertRaisesRegex(ValueError,'cohort mismatch: validation_protocol'):
                run.candidate_api_summary(root,[],SimpleNamespace(portable=True,rounds=3))

    def test_v2_rows_require_actual_output_validation_evidence(self):
        for field,value in (('timed_output_validated',False),('timed_output_validated',1),
                            ('returned_values',[]),('returned_values',[1,1]),('returned_values',[1,1,'bad'])):
            with self.subTest(field=field,value=value), tempfile.TemporaryDirectory() as directory:
                root=Path(directory);self.fixture(root)
                for p in root.glob('*.json'):
                    def make_v2(payload):
                        payload['validation_protocol']='actual-timed-output-before-reset-v2'
                        payload['rows'][0].update(timed_output_validated=True,returned_values=[1,1,1])
                    self.change(p,make_v2)
                self.change(root/'candidate-Auto-r0.json',lambda p:p['rows'][0].update({field:value}))
                with self.assertRaisesRegex(ValueError,'timed'): run.aggregate(root,self.variants,3)
        for field in ('timed_output_validated','returned_values'):
            with self.subTest(missing=field), tempfile.TemporaryDirectory() as directory:
                root=Path(directory);self.fixture(root)
                for p in root.glob('*.json'):
                    self.change(p,lambda p:p.update(validation_protocol='actual-timed-output-before-reset-v2'))
                with self.assertRaisesRegex(ValueError,'timed-output'): run.aggregate(root,self.variants,3)

    def test_uniform_v2_passes_and_summary_preserves_protocol(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.api_fixture(root)
            for p in root.glob('*.json'):
                def make_v2(payload):
                    payload['validation_protocol']='actual-timed-output-before-reset-v2'
                    for row in payload.get('rows',payload.get('candidate_only_rows',[])):
                        row.update(timed_output_validated=True,returned_values=[1,1,1])
                self.change(p,make_v2)
            argv=['run.py','--original',str(root),'--output',str(root),'--summarize-only','--suite','smoke','--samples','3']
            with patch('sys.argv',argv): self.assertEqual(run.main(),0)
            self.assertEqual(json.loads((root/'summary.json').read_text())['validation_protocol'],'actual-timed-output-before-reset-v2')

    legacy_variants=['original','candidate','main','optimize','alex']
    def legacy_fixture(self,root):
        (root/'environment.json').write_text(json.dumps({'environment':{'DOTNET_gcConcurrent':'0'}}))
        for variant in self.legacy_variants:
            for round_id in range(3):
                row={'operation':'union','size':8,'universe':10000,'distribution':'scattered',
                    'inputDigest':'same-input','status':'measured','ns':[10+round_id]*7,'allocated_bytes':[0]*7}
                payload={'variant':variant,'runtime':'.NET 8.0.31','architecture':'X64','seed':run.LEGACY_SEED,'rows':[row]}
                (root/f'legacy-{variant}-r{round_id}.json').write_text(json.dumps(payload))

    def test_legacy_complete_matrix_passes_and_aggregates_process_medians(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.legacy_fixture(root)
            rows,failures=run.legacy_summary(root,self.legacy_variants,3)
            self.assertEqual(failures,[])
            self.assertEqual(rows[0]['variants']['candidate']['median_ns'],11)
            self.assertEqual(rows[0]['candidate_over_best_legacy'],1)
            self.assertEqual(json.loads((root/'legacy-failures.json').read_text()),{'candidate':[],'reference':[]})

    def test_legacy_duplicate_rows_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.legacy_fixture(root)
            self.change(root/'legacy-main-r0.json',lambda p:p['rows'].append(copy.deepcopy(p['rows'][0])))
            with self.assertRaisesRegex(ValueError,'Duplicate legacy'): run.legacy_summary(root,self.legacy_variants,3)

    def test_legacy_sample_validation(self):
        cases=[('ns',float('nan'),'Invalid timing'),('ns',float('inf'),'Invalid timing'),
            ('ns',0,'Invalid timing'),('allocated_bytes',-1,'Invalid allocation'),
            ('allocated_bytes',float('nan'),'Invalid allocation')]
        for field,value,message in cases:
            with self.subTest(field=field,value=value), tempfile.TemporaryDirectory() as directory:
                root=Path(directory);self.legacy_fixture(root)
                self.change(root/'legacy-main-r0.json',lambda p:p['rows'][0][field].__setitem__(0,value))
                with self.assertRaisesRegex(ValueError,message): run.legacy_summary(root,self.legacy_variants,3)
        for field in ('ns','allocated_bytes'):
            with self.subTest(field=field), tempfile.TemporaryDirectory() as directory:
                root=Path(directory);self.legacy_fixture(root)
                self.change(root/'legacy-main-r0.json',lambda p:p['rows'][0][field].pop())
                with self.assertRaisesRegex(ValueError,'Wrong raw sample count'): run.legacy_summary(root,self.legacy_variants,3)

    def test_legacy_failed_candidate_and_reference_remain_separate_and_unranked(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.legacy_fixture(root)
            for variant in ('candidate','original','main','alex'):
                self.change(root/f'legacy-{variant}-r0.json',lambda p:p['rows'][0].update(status='failed',error='control'))
            rows,failures=run.legacy_summary(root,self.legacy_variants,3)
            self.assertEqual(len(failures),4)
            self.assertNotIn('candidate_over_best_legacy',rows[0])
            inventory=json.loads((root/'legacy-failures.json').read_text())
            self.assertEqual(len(inventory['candidate']),1)
            self.assertEqual(len(inventory['reference']),3)
            self.assertEqual({f['variant'] for f in inventory['reference']},{'original','main','alex'})
            self.assertTrue(all(f['error']=='control' and f['round']==0 for f in failures))

    def test_legacy_failed_reference_prevents_best_legacy_rank(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.legacy_fixture(root)
            self.change(root/'legacy-main-r0.json',lambda p:p['rows'][0].update(status='failed',error='reference'))
            rows,failures=run.legacy_summary(root,self.legacy_variants,3)
            self.assertEqual(len(failures),1)
            self.assertNotIn('candidate_over_best_legacy',rows[0])

    def test_legacy_metadata_is_checked(self):
        for field,value in {'variant':'candidate','seed':0,'runtime':'.NET 9.0','architecture':'Arm64'}.items():
            with self.subTest(field=field), tempfile.TemporaryDirectory() as directory:
                root=Path(directory);self.legacy_fixture(root)
                self.change(root/'legacy-main-r0.json',lambda p:p.update({field:value}))
                with self.assertRaisesRegex(ValueError,'Legacy payload'): run.legacy_summary(root,self.legacy_variants,3)

    def test_legacy_cohort_links_to_common(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.fixture(root);self.legacy_fixture(root)
            for p in root.glob('legacy-*.json'): self.change(p,lambda p:p.update(architecture='Arm64'))
            with self.assertRaisesRegex(ValueError,'Legacy payload cohort mismatch: architecture'):
                run.legacy_summary(root,self.legacy_variants,3)

    def test_summarize_only_propagates_legacy_failures_to_summary_and_exit(self):
        for requested in (True,False):
            with self.subTest(legacy_flag=requested), tempfile.TemporaryDirectory() as directory:
                root=Path(directory);self.api_fixture(root);self.legacy_fixture(root)
                self.change(root/'legacy-alex-r0.json',lambda p:p['rows'][0].update(status='failed',error='legacy-only-failure'))
                argv=['run.py','--original',str(root),'--output',str(root),'--summarize-only','--suite','smoke','--samples','3']
                if requested: argv.append('--legacy')
                with patch('sys.argv',argv): self.assertEqual(run.main(),1)
                summary=json.loads((root/'summary.json').read_text())
                self.assertEqual(summary['managed_benchmark_status'],'failed')
                self.assertEqual(summary['legacy_benchmark_status'],'failed')
                self.assertEqual(len(summary['managed_benchmark_failures']),1)
                self.assertEqual(summary['legacy_benchmark_failures']['candidate'],[])
                self.assertEqual(summary['legacy_benchmark_failures']['reference'][0]['variant'],'alex')

    def test_summarize_only_success_and_absent_legacy_are_explicit(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.api_fixture(root)
            argv=['run.py','--original',str(root),'--output',str(root),'--summarize-only','--suite','smoke','--samples','3']
            with patch('sys.argv',argv): self.assertEqual(run.main(),0)
            summary=json.loads((root/'summary.json').read_text())
            self.assertEqual(summary['managed_benchmark_status'],'passed')
            self.assertEqual(summary['legacy_benchmark_status'],'not_run')

    def test_entire_failed_layout_still_produces_failure_summary(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.api_fixture(root)
            for p in root.glob('candidate-*-r*.json'):
                if 'newapis' not in p.name: self.change(p,lambda data:data['rows'][0].update(status='failed',error='control'))
            argv=['run.py','--original',str(root),'--output',str(root),'--summarize-only','--suite','smoke','--samples','3']
            with patch('sys.argv',argv): self.assertEqual(run.main(),1)
            summary=json.loads((root/'summary.json').read_text())
            self.assertEqual(len(summary['managed_benchmark_failures']),9)
            self.assertIsNone(summary['layouts']['Dense']['median_ratio'])

    def test_child_environment_forces_batch_gc_despite_parent_setting(self):
        with patch.dict('os.environ',{'DOTNET_gcConcurrent':'1'}):
            self.assertEqual(run.runtime_environment(Path('/tmp'))['DOTNET_gcConcurrent'],'0')

    def test_new_payloads_reject_missing_or_nonbatch_gc_metadata(self):
        cases=[('gcLatencyMode','Interactive'),('gcConcurrent','1'),('gcConcurrent',0)]
        for field,value in cases:
            with self.subTest(field=field,value=value), tempfile.TemporaryDirectory() as directory:
                root=Path(directory);self.fixture(root)
                self.change(root/'candidate-Auto-r0.json',lambda p:p.update({field:value}))
                with self.assertRaisesRegex(ValueError,'GC protocol mismatch'): run.aggregate(root,self.variants,3)
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.fixture(root)
            for p in root.glob('*.json'):
                self.change(p,lambda p:(p.pop('gcLatencyMode'),p.pop('gcConcurrent')))
            with self.assertRaisesRegex(ValueError,'Missing GC metadata'): run.aggregate(root,self.variants,3)

    def test_historical_gc_requires_uniform_missing_metadata(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.fixture(root)
            self.change(root/'candidate-Auto-r0.json',lambda p:(p.pop('gcLatencyMode'),p.pop('gcConcurrent')))
            with self.assertRaisesRegex(ValueError,'cohort mismatch: gcProtocol'):
                run.aggregate(root,self.variants,3,args=SimpleNamespace(historical_gc=True))
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.fixture(root)
            self.change(root/'candidate-Auto-r0.json',lambda p:p.pop('gcConcurrent'))
            with self.assertRaisesRegex(ValueError,'Missing GC metadata'):
                run.aggregate(root,self.variants,3,args=SimpleNamespace(historical_gc=True))

    def test_candidate_api_gc_must_match_common_cohort(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.api_fixture(root)
            for p in root.glob('*-newapis-*.json'):
                self.change(p,lambda p:(p.pop('gcLatencyMode'),p.pop('gcConcurrent')))
            with self.assertRaisesRegex(ValueError,'cohort mismatch: gcProtocol'):
                run.candidate_api_summary(root,[],SimpleNamespace(portable=True,rounds=3,historical_gc=True))

    def test_historical_reaggregation_preserves_unrecorded_gc_and_failed_rows(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.api_fixture(root);self.legacy_fixture(root)
            (root/'environment.json').write_text(json.dumps({'environment':{'DOTNET_TieredCompilation':'0'}}))
            for p in root.glob('*-r*.json'):
                def historical(payload):
                    payload.pop('gcLatencyMode',None);payload.pop('gcConcurrent',None)
                self.change(p,historical)
            self.change(root/'candidate-Auto-r0.json',lambda p:p['rows'][0].update(status='failed',error='historical prepared allocation'))
            argv=['run.py','--original',str(root),'--output',str(root),'--summarize-only','--historical-gc','--suite','smoke','--samples','3']
            with patch('sys.argv',argv): self.assertEqual(run.main(),1)
            summary=json.loads((root/'summary.json').read_text())
            self.assertEqual(summary['gcProtocol'],'historical-unrecorded')
            self.assertIsNone(summary['gcLatencyMode']);self.assertIsNone(summary['gcConcurrent'])
            self.assertEqual(summary['legacy_gc_metadata']['source'],'historical-unrecorded')
            rows=json.loads((root/'comparison.json').read_text())
            self.assertNotIn('Auto',rows[0]['candidate_over_original'])
            self.assertEqual(summary['managed_benchmark_failures'][0]['error'],'historical prepared allocation')

    def test_legacy_launch_gc_metadata_required_for_new_runs(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.legacy_fixture(root)
            (root/'environment.json').write_text(json.dumps({'environment':{}}))
            with self.assertRaisesRegex(ValueError,'legacy GC launch metadata'): run.legacy_summary(root,self.legacy_variants,3)
            self.assertEqual(run.legacy_summary(root,self.legacy_variants,3,SimpleNamespace(historical_gc=True))[1],[])

    def test_batch_summary_records_observed_and_legacy_launch_metadata_separately(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.api_fixture(root);self.legacy_fixture(root)
            argv=['run.py','--original',str(root),'--output',str(root),'--summarize-only','--suite','smoke','--samples','3']
            with patch('sys.argv',argv): self.assertEqual(run.main(),0)
            summary=json.loads((root/'summary.json').read_text())
            self.assertEqual(summary['gcProtocol'],'BatchGC')
            self.assertEqual(summary['gcLatencyMode'],'Batch')
            self.assertEqual(summary['gcConcurrent'],'0')
            self.assertEqual(summary['legacy_gc_metadata'],{'source':'runner_environment_only','gcConcurrent':'0','gcLatencyMode':'not_recorded_by_legacy_harness'})

class SourceAttributionTests(unittest.TestCase):
    @staticmethod
    def git(root,*args):
        return subprocess.check_output(['git','-c','user.name=Attribution Test','-c','user.email=attribution@example.invalid',
            '-c','commit.gpgsign=false','-c','core.hooksPath=/dev/null',*args],cwd=root,stderr=subprocess.DEVNULL).decode().strip()
    def fixture(self,root):
        self.git(root,'init','-q')
        (root/'Runtime/Nested').mkdir(parents=True)
        (root/'Runtime/Set.cs').write_text('original bytes\n')
        (root/'Runtime/Nested/Set.cs.meta').write_text('original metadata\n')
        self.git(root,'add','Runtime');self.git(root,'commit','-qm','Original fixture')
        return self.git(root,'rev-parse','HEAD')
    @staticmethod
    def snapshot(root,destination):
        shutil.copytree(root/'Runtime',destination/'Runtime')
        return destination
    def test_clean_checkout_proves_exact_runtime_tree(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);head=self.fixture(root);snapshot=self.snapshot(root,root/'frozen')
            result=run.source_attribution(root,snapshot)
            self.assertEqual(result['enclosing_checkout_head'],head)
            self.assertEqual(result['proven_runtime_commit'],head)
            self.assertEqual(result['proven_runtime_tree'],self.git(root,'rev-parse','HEAD:Runtime'))
            self.assertTrue(result['source_root_is_checkout_root'])
            self.assertEqual(result['source_attribution_protocol'],'exact-runtime-tree-v1')
            self.assertNotIn('git_head',result)
    def test_nested_archive_proves_baseline_not_enclosing_head(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);baseline=self.fixture(root);archive=self.snapshot(root,root/'archived-original')
            (root/'Runtime/Set.cs').write_text('current bytes\n');self.git(root,'add','Runtime');self.git(root,'commit','-qm','Current fixture')
            head=self.git(root,'rev-parse','HEAD');snapshot=self.snapshot(archive,root/'frozen')
            result=run.source_attribution(archive,snapshot,(baseline,))
            self.assertEqual(result['enclosing_checkout_root'],str(root.resolve()))
            self.assertEqual(result['enclosing_checkout_head'],head)
            self.assertEqual(result['proven_runtime_commit'],baseline)
            self.assertNotEqual(result['proven_runtime_commit'],head)
            self.assertFalse(result['source_root_is_checkout_root'])
            unproven=run.source_attribution(archive,snapshot)
            self.assertIsNone(unproven['proven_runtime_commit'])
    def test_dirty_missing_or_extra_runtime_files_are_not_attributed(self):
        for change in ('changed','missing','extra'):
            with self.subTest(change=change),tempfile.TemporaryDirectory() as temp:
                root=Path(temp);head=self.fixture(root);snapshot=self.snapshot(root,root/'frozen')
                if change=='changed':(snapshot/'Runtime/Set.cs').write_text('dirty bytes\n')
                elif change=='missing':(snapshot/'Runtime/Nested/Set.cs.meta').unlink()
                else:(snapshot/'Runtime/Extra.cs').write_text('untracked input\n')
                result=run.source_attribution(root,snapshot,(head,))
                self.assertEqual(result['enclosing_checkout_head'],head)
                self.assertIsNone(result['proven_runtime_commit'])
                self.assertIsNone(result['proven_runtime_tree'])
    def test_frozen_bytes_are_checked_instead_of_current_working_copy(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);head=self.fixture(root);snapshot=self.snapshot(root,root/'frozen')
            (root/'Runtime/Set.cs').write_text('changed after freezing\n')
            result=run.source_attribution(root,snapshot)
            self.assertEqual(result['proven_runtime_commit'],head)
            current=self.snapshot(root,root/'current-frozen')
            self.assertIsNone(run.source_attribution(root,current)['proven_runtime_commit'])
    def test_missing_candidate_ref_cannot_supply_provenance(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);head=self.fixture(root);snapshot=self.snapshot(root,root/'frozen')
            result=run.source_attribution(root,snapshot,('missing-ref-for-test',))
            self.assertEqual(result['proven_runtime_commit'],head)
            self.assertEqual(result['runtime_commit_candidates_checked'],[head])
    def test_source_outside_git_remains_snapshot_only(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);(root/'Runtime').mkdir();(root/'Runtime/Set.cs').write_text('no git\n')
            snapshot=self.snapshot(root,root/'frozen');result=run.source_attribution(root,snapshot)
            self.assertIsNone(result['enclosing_checkout_root']);self.assertIsNone(result['enclosing_checkout_head'])
            self.assertIsNone(result['proven_runtime_commit']);self.assertIsNone(result['proven_runtime_tree'])

if __name__=='__main__': unittest.main()
