"""Synthetic report-integrity tests; these fixtures are never performance evidence."""
import copy,json,tempfile,unittest
from pathlib import Path
from types import SimpleNamespace
import bulk_workload as bulk

class BulkReportTests(unittest.TestCase):
    jobs=[('original-hardware-Auto','original','hardware','Auto'),('current-hardware-Auto','current','hardware','Auto'),('current-hardware-Bulk','current','hardware','Bulk')]
    args=SimpleNamespace(rounds=1,suite='smoke',samples=3,target_ms=.5)
    def fixture(self,root):
        for label,implementation,flavor,mode in self.jobs:
            row={'operation':'fresh.lifecycle.h1','universe':65536,'members':128,'rightMembers':128,'overlap':50,'leftPattern':'contiguous','rightPattern':'contiguous','sharingPattern':'spread','digest':'same',
                'status':'measured','iterations':100,'units':1,'ns':[10,20,30],'elapsed_ticks':[1000,2000,3000],'batch_ms':[.001,.002,.003],
                'allocated_bytes':[0,0,0],'returned_values':[100,100,100],'gc_collections':[[0,0,0]]*3,'completed_samples':3,'timed_output_validated':True,'zero_allocation_required':True,'details':{},'scope':'test'}
            matrix=[]
            for k in sorted(bulk.expected_keys('smoke')):
                item=copy.deepcopy(row);item.update(dict(zip(bulk.KEYS,k)));matrix.append(item)
            payload={'label':label,'round':0,'implementation':implementation,'strategy':mode,'suite':'smoke','samples':3,'targetMs':.5,'seed':20261001,'gcMode':'Batch','portableKernelsForced':False,
                'validation_protocol':'actual-timed-output-before-reset-BatchGC-v1','runtime':'.NET 8','architecture':'X64','serverGC':False,'stopwatchFrequency':1000000000,
                'controls':{'negative_copy_bytes':0,'positive_new_array_bytes':4120},'failures':0,'rows':matrix}
            (root/(label+'-r0.json')).write_text(json.dumps(payload))
    def mutate(self,root,fn):
        p=root/'current-hardware-Bulk-r0.json';d=json.loads(p.read_text());fn(d);p.write_text(json.dumps(d))
    def test_valid_and_ratios(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);self.fixture(root);s=bulk.summarize(root,self.jobs,self.args)
            self.assertEqual(s['status'],'passed');self.assertEqual(s['policy_groups']['hardware/fresh.lifecycle.h1']['median_ratio'],1)
    def test_batch_gc_required(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);self.fixture(root);self.mutate(root,lambda d:d.update(gcMode='Interactive'))
            with self.assertRaisesRegex(ValueError,'gcMode'):bulk.summarize(root,self.jobs,self.args)
    def test_negative_allocation_control(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);self.fixture(root);self.mutate(root,lambda d:d['controls'].update(negative_copy_bytes=8))
            with self.assertRaisesRegex(ValueError,'controls'):bulk.summarize(root,self.jobs,self.args)
    def test_raw_normalization(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);self.fixture(root);self.mutate(root,lambda d:d['rows'][0]['ns'].__setitem__(0,11))
            with self.assertRaisesRegex(ValueError,'normalization'):bulk.summarize(root,self.jobs,self.args)
    def test_duplicate_and_missing(self):
        for change,message in ((lambda d:d['rows'].append(copy.deepcopy(d['rows'][0])),'Duplicate'),(lambda d:d.update(rows=[]),'Empty')):
            with tempfile.TemporaryDirectory() as temp:
                root=Path(temp);self.fixture(root);self.mutate(root,change)
                with self.assertRaisesRegex(ValueError,message):bulk.summarize(root,self.jobs,self.args)
    def test_every_matrix_empty_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);self.fixture(root)
            for p in root.glob('*-r0.json'):
                d=json.loads(p.read_text());d['rows']=[];p.write_text(json.dumps(d))
            with self.assertRaisesRegex(ValueError,'Empty benchmark matrix'):bulk.summarize(root,self.jobs,self.args)
    def test_uniformly_missing_group_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);self.fixture(root)
            for p in root.glob('*-r0.json'):
                d=json.loads(p.read_text());d['rows']=[r for r in d['rows'] if r['operation']!='existing.lifecycle.h32'];p.write_text(json.dumps(d))
            with self.assertRaisesRegex(ValueError,'Missing or extra row'):bulk.summarize(root,self.jobs,self.args)
    def test_nan_positive_control_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);self.fixture(root);self.mutate(root,lambda d:d['controls'].update(positive_new_array_bytes=float('nan')))
            with self.assertRaisesRegex(ValueError,'finite nonnegative integer'):bulk.summarize(root,self.jobs,self.args)
    def test_declared_inventory_sizes(self):
        self.assertEqual(len(bulk.expected_keys('smoke')),660)
        self.assertEqual(len(bulk.expected_keys('full')),4224)
        self.assertEqual(len(bulk.expected_keys('boundary')),768)
        self.assertEqual(len(bulk.expected_keys('regression')),352)
    def add_aa_attribution(self,root):
        base='current-hardware-Auto';alias=base+'-AA'
        source=json.loads((root/(base+'-r0.json')).read_text());source['label']=alias
        (root/(alias+'-r0.json')).write_text(json.dumps(source))
        jobs=self.jobs+[(alias,'current','hardware','Auto')]
        attribution={}
        for label,implementation,flavor,mode in jobs:
            entry={'executablePath':'/synthetic/'+implementation+'.dll','executableSha256':implementation+'-dll',
                'runtimeSourceRoot':'/synthetic/'+implementation,'sourceInventorySha256':implementation+'-source',
                'harnessSha256':'same-harness','runnerSha256':'same-runner'}
            attribution[label]=entry
            path=root/(label+'-r0.json');payload=json.loads(path.read_text())
            payload.update(attribution_protocol='source-and-executable-v1',vectorHardwareAccelerated=True,vectorIntWidth=8,denseWordsPerPackedRecord=4 if implementation=='current' else None,portable_backend_scope='Explicit dense backend only',**entry);path.write_text(json.dumps(payload))
        (root/'manifest.json').write_text(json.dumps({'attribution_protocol':'source-and-executable-v1','job_attribution':attribution}))
        return jobs
    def test_identical_aa_retains_round_ratios_and_tails(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);self.fixture(root);jobs=self.add_aa_attribution(root)
            result=bulk.summarize(root,jobs,self.args)
            group=result['aa_controls']['hardware']['groups']['all_rows']
            self.assertEqual(group['expected_rows'],660);self.assertEqual(group['measured_rows'],660)
            self.assertEqual(group['median_ratio'],1);self.assertEqual(group['paired_round_ratio_max'],1)
            row=json.loads((root/'comparison.json').read_text())[0]
            self.assertEqual(row['variants']['current-hardware-Auto-AA']['round_p95_batch_mean_ns'],[30])
    def test_aa_different_executable_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);self.fixture(root);jobs=self.add_aa_attribution(root)
            p=root/'manifest.json';d=json.loads(p.read_text());d['job_attribution']['current-hardware-Auto-AA']['executableSha256']='different';p.write_text(json.dumps(d))
            with self.assertRaisesRegex(ValueError,'identical executable'):bulk.summarize(root,jobs,self.args)
    def test_payload_source_attribution_mismatch_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);self.fixture(root);jobs=self.add_aa_attribution(root)
            self.mutate(root,lambda d:d.update(sourceInventorySha256='other-source'))
            with self.assertRaisesRegex(ValueError,'sourceInventorySha256 attribution mismatch'):bulk.summarize(root,jobs,self.args)
    def test_aa_backend_capability_mismatch_is_rejected(self):
        for field,value in [('vectorHardwareAccelerated',False),('vectorIntWidth',4),('denseWordsPerPackedRecord',1),('portable_backend_scope','different')]:
            with self.subTest(field=field),tempfile.TemporaryDirectory() as temp:
                root=Path(temp);self.fixture(root);jobs=self.add_aa_attribution(root)
                p=root/'current-hardware-Auto-AA-r0.json';d=json.loads(p.read_text());d[field]=value;p.write_text(json.dumps(d))
                with self.assertRaisesRegex(ValueError,'capability mismatch'):bulk.summarize(root,jobs,self.args)
    def test_attributed_capabilities_require_present_strict_types(self):
        for field,value in [('vectorHardwareAccelerated',1),('vectorIntWidth',8.0),('vectorIntWidth',True),('vectorIntWidth',0),('denseWordsPerPackedRecord',None),('denseWordsPerPackedRecord',True),('portable_backend_scope',None),('portable_backend_scope',' ')]:
            with self.subTest(field=field,value=value),tempfile.TemporaryDirectory() as temp:
                root=Path(temp);self.fixture(root);jobs=self.add_aa_attribution(root)
                self.mutate(root,lambda d:d.update({field:value}))
                with self.assertRaisesRegex(ValueError,'capability'):bulk.summarize(root,jobs,self.args)
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);self.fixture(root);jobs=self.add_aa_attribution(root)
            self.mutate(root,lambda d:d.pop('vectorHardwareAccelerated'))
            with self.assertRaisesRegex(ValueError,'Missing backend capability'):bulk.summarize(root,jobs,self.args)
    def test_timed_output_validation_requires_actual_true(self):
        for value in ('false',1,None,False):
            with self.subTest(value=value),tempfile.TemporaryDirectory() as temp:
                root=Path(temp);self.fixture(root);self.mutate(root,lambda d:d['rows'][0].update(timed_output_validated=value))
                with self.assertRaisesRegex(ValueError,'Actual output not validated'):bulk.summarize(root,self.jobs,self.args)
    def test_returned_checksums_require_bounded_integers(self):
        for value in (float('nan'),float('inf'),True,1.0,'1',1<<31,-(1<<31)-1):
            with self.subTest(value=value),tempfile.TemporaryDirectory() as temp:
                root=Path(temp);self.fixture(root);self.mutate(root,lambda d:d['rows'][0].update(returned_values=[value]*3))
                with self.assertRaisesRegex(ValueError,'bounded signed integer'):bulk.summarize(root,self.jobs,self.args)
    def test_raw_field_types_are_strict(self):
        for field,value,message in [('iterations',True,'denominator'),('elapsed_ticks',[1000.0,2000,3000],'tick'),('batch_ms',[True,.002,.003],'duration'),('gc_collections',[[0,0,False]]*3,'GC collection'),('ns',[True,20,30],'normalization'),('allocated_bytes',[False,0,0],'allocation'),('zero_allocation_required',1,'requirement boolean')]:
            with self.subTest(field=field),tempfile.TemporaryDirectory() as temp:
                root=Path(temp);self.fixture(root);self.mutate(root,lambda d:d['rows'][0].update({field:value}))
                with self.assertRaisesRegex(ValueError,message):bulk.summarize(root,self.jobs,self.args)
    def test_aa_failure_is_unranked_but_retained(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);self.fixture(root);jobs=self.add_aa_attribution(root)
            p=root/'current-hardware-Auto-AA-r0.json';d=json.loads(p.read_text());d['failures']=1;d['rows'][0].update(status='failed',error='injected',completed_samples=1);p.write_text(json.dumps(d))
            result=bulk.summarize(root,jobs,self.args)
            group=result['aa_controls']['hardware']['groups']['all_rows']
            self.assertEqual(result['status'],'failed');self.assertEqual(group['expected_rows'],660)
            self.assertEqual(group['measured_rows'],659);self.assertEqual(len(group['failed_keys']),1)
    def test_failed_rows_stay_visible(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);self.fixture(root)
            self.mutate(root,lambda d:(d.update(failures=1),d['rows'][0].update(status='failed',error='control',completed_samples=1)))
            s=bulk.summarize(root,self.jobs,self.args);self.assertEqual(s['status'],'failed');self.assertEqual(len(s['failures']),1)
if __name__=='__main__':unittest.main()
