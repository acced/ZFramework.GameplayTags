#!/usr/bin/env python3
"""Validate protocol identity and real allocation controls; never subtract or discard samples."""
import argparse, json, pathlib, unittest, tempfile
from dense_results import check_rows, verify
PROTOCOL='settled-setup-v2'

def validate_payload(payload):
    if payload.get('measurement_protocol')!=PROTOCOL:raise RuntimeError('Mixed or missing measurement protocol')
    if payload.get('positive_control_bytes',0)<=0:raise RuntimeError('Inactive allocation counter')
    check_rows(payload)
    if any(row.get('measurement_protocol')!=PROTOCOL for row in payload['rows']):
        raise RuntimeError('Missing per-row protocol identity')

def validate_directory(directory):
    directory=pathlib.Path(directory)
    files=sorted(directory.glob('*-r*.json'))
    if not files:raise RuntimeError('No benchmark payloads')
    for path in files:validate_payload(json.loads(path.read_text()))
    summary=json.loads((directory/'summary.json').read_text())
    expected=summary['rounds']*9
    if len(files)!=expected:raise RuntimeError('Unexpected benchmark file count')
    summary['measurement_protocol']=PROTOCOL
    summary['setup_gc_policy']='After each scenario setup and warmup, collect/wait-finalizers/collect before the measured loop, identically for all implementations.'
    (directory/'summary.json').write_text(json.dumps(summary,indent=2))
    # This existing gate still rejects every nonzero prepared candidate sample.
    verify(directory)
    print('PROTOCOL: '+str(len(files))+' complete payloads, all positive controls and strict Dense allocation checks passed.',flush=True)

class GateTests(unittest.TestCase):
    def payload(self):
        return {'measurement_protocol':PROTOCOL,'positive_control_bytes':4120,'rows':[
            {'operation':'exact','size':i,'universe':1000,'distribution':'unit-test-only',
             'status':'measured','ns':[1.0]*7,'allocated_bytes':[0.0]*7,'inputDigest':'test-only',
             'measurement_protocol':PROTOCOL} for i in range(160)]}
    def test_missing_protocol(self):
        p=self.payload();p.pop('measurement_protocol')
        with self.assertRaises(RuntimeError):validate_payload(p)
    def test_inactive_control(self):
        p=self.payload();p['positive_control_bytes']=0
        with self.assertRaises(RuntimeError):validate_payload(p)
    def test_missing_or_invalid_sample(self):
        p=self.payload();p['rows'][0]['ns'][0]=float('nan')
        with self.assertRaises(RuntimeError):validate_payload(p)
        p=self.payload();p['rows'].pop()
        with self.assertRaises(RuntimeError):validate_payload(p)
    def test_nonzero_is_never_rounded_or_waived(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=pathlib.Path(tmp)
            (root/'summary.json').write_text(json.dumps({'rounds':1}))
            names=['main','optimize','alex','auto','flat-dense','candidate',
                   'prepared-auto','prepared-flat-dense','prepared-candidate']
            for name in names:(root/(name+'-r0.json')).write_text(json.dumps(self.payload()))
            verify(root)
            p=self.payload();p['rows'][47]['allocated_bytes'][3]=0.000001
            (root/'prepared-candidate-r0.json').write_text(json.dumps(p))
            with self.assertRaises(RuntimeError):verify(root)

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--results');parser.add_argument('--self-test',action='store_true')
    args=parser.parse_args()
    if args.self_test:
        result=unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(GateTests))
        if not result.wasSuccessful():raise SystemExit(1)
    if args.results:validate_directory(args.results)
    if not args.self_test and not args.results:parser.error('Select --self-test or --results')
