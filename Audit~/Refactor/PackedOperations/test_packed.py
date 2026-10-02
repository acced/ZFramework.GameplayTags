import copy,math,unittest
import run_packed as r
class Tests(unittest.TestCase):
 @classmethod
 def setUpClass(cls):
  cls.m={'samples':3,'target_ms':3.0,'attribution':{'baseline-hardware':{}}};rows=[]
  for meta in r.PLAN:
   a,b,w,out,h,u=r.shapes(meta,4);value=len(out)*40+17*16
   rows.append(dict(meta,digest=h,status='measured',error=None,iterations=1,units=16,samplesCompleted=3,ns=[100.,100.,100.],allocated_bytes=[0.,0.,0.],elapsed_ticks=[1600]*3,batch_ms=[.0016]*3,returned_values=[value]*3,gc_collections=[[0,0,0]]*3,iterationLimit=16384,estimatedBytesPerSweep=0,calibrationMs=3.,allocationCapBytes=32<<20,limitReached=False,timed_output_validated=True,zero_allocation_required=True,details=dict(actualRegistryCount=u,requestedStorage=meta['requestedStorage'],activeActors=16,retainedInstances=16,sourceInputDigest=h,actors=[dict(id=i,left=a,right=b,work=w,result=w) for i in range(16)])))
  cls.p=dict(runtime='.NET 8.0.31',architecture='X64',os='Linux',source='baseline',label='baseline-hardware',round=0,samples=3,targetMs=3.0,seed=20261002,gcMode='Batch',gcConcurrent='0',readyToRun='0',tieredCompilation='0',tieredPgo='0',validation_protocol='actual-final-actor-output-before-reset-packed-operations-v1',portableKernelsForced=False,assertions=1,stopwatchFrequency=1000000000,failures=0,vectorHardwareAccelerated=True,vectorIntWidth=8,serverGC=False,denseWordsPerPackedRecord=4,avx2Supported=True,arm64AdvSimdSupported=False,controls=dict(negative_copy_bytes=0,positive_new_array_bytes=4120),expectedKeys=[p['key'] for p in r.PLAN],executionKeys=[p['key'] for p in r.PLAN],rows=rows)
 def validate(self,p):return r.validate(p,self.m,'baseline-hardware','baseline','hardware',0)
 def reject(self,mut):
  p=copy.deepcopy(self.p);mut(p)
  with self.assertRaises((ValueError,KeyError,TypeError)):self.validate(p)
 def test_valid(self):self.assertEqual(len(self.validate(self.p)),372)
 def test_integer_serialized_target(self):
  p=copy.deepcopy(self.p);p['targetMs']=3;self.assertEqual(len(self.validate(p)),372)
 def test_backend_ratio(self):self.reject(lambda p:p.update(denseWordsPerPackedRecord=2))
 def test_missing_capability(self):self.reject(lambda p:p.pop('avx2Supported'))
 def test_fallback_capacity(self):
  for row in r.PLAN:
   if row['capacityPolicy']!='actual_result_records':continue
   a,b,w,out,h,u=r.shapes(row,4);self.assertLess(w['recordCapacity'],min(a['recordCount'],b['recordCount']) if row['operation']=='intersection.into' else a['recordCount'])
 def test_calibration_bad_limit(self):self.reject(lambda p:p['rows'][0].update(iterationLimit=0))
 def test_calibration_bad_cap(self):self.reject(lambda p:p['rows'][0].update(allocationCapBytes=1))
 def test_calibration_inconsistent_stop(self):self.reject(lambda p:p['rows'][0].update(calibrationMs=0.,limitReached=False))
 def test_calibration_false_limit(self):self.reject(lambda p:p['rows'][0].update(limitReached=True))
 def test_empty(self):self.reject(lambda p:p.update(rows=[]))
 def test_missing(self):self.reject(lambda p:p['rows'].pop())
 def test_duplicate(self):self.reject(lambda p:p['rows'].__setitem__(1,p['rows'][0]))
 def test_nan(self):self.reject(lambda p:p['rows'][0]['ns'].__setitem__(0,float('nan')))
 def test_return_nan(self):self.reject(lambda p:p['rows'][0]['returned_values'].__setitem__(0,float('nan')))
 def test_validation_string(self):self.reject(lambda p:p['rows'][0].update(timed_output_validated='false'))
 def test_shape_bool(self):self.reject(lambda p:p['rows'][0]['details']['actors'][0]['left'].update(members=True))
 def test_shape_wrongmode(self):self.reject(lambda p:p['rows'][0]['details']['actors'][0]['left'].update(storage='Dense'))
 def test_wrong_normalization(self):self.reject(lambda p:p['rows'][0]['ns'].__setitem__(0,101.))
 def test_bad_gc_control(self):self.reject(lambda p:p['controls'].update(positive_new_array_bytes=float('nan')))
 def test_wrong_checksum(self):self.reject(lambda p:p['rows'][0]['returned_values'].__setitem__(0,0))
 def test_nonzero_prepared(self):self.reject(lambda p:p['rows'][0]['allocated_bytes'].__setitem__(0,8.))
 def test_failed_wrongcount_retained(self):
  p=copy.deepcopy(self.p);p['rows'][0].update(status='failed',error='actual count wrong',timed_output_validated=False);p['rows'][0]['returned_values'][0]=0;p['failures']=1;self.assertEqual(self.validate(p)[0]['status'],'failed')
 def test_oracle_membership(self):
  for row in r.PLAN:
   x,y,u,i,d,h=r.fixture(row);self.assertEqual(len(i),min(len(x),len(y))*row['overlap']//100);self.assertEqual(len(u),len(x)+len(y)-len(i));self.assertEqual(set(d),set(x)-set(y))
 def test_portable_shape(self):
  for row in r.PLAN:
   a,b,w,out,h,u=r.shapes(row,1)
   self.assertGreaterEqual(w['capacity'],len(out));self.assertGreaterEqual(w['recordCapacity'],w['recordCount'])
if __name__=='__main__':unittest.main()
