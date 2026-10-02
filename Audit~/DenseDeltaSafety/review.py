#!/usr/bin/env python3
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/DenseBulk/review.py').read_text()
code=code.replace('from generate import EXPECTED,BASE,prior,fixed,bulk,block','from generate import EXPECTED,BASE,prior,fixed,bulk,block,variants')
code=code.replace("NAMES=('direct','split','fixed','bulk','combined')","NAMES=('monotone','stream','delta32','safe32','batch32')")
start=code.index('PAIRS=');end=code.index('\ndef sha',start)
code=code[:start]+"PAIRS=(('safe32','delta32'),('safe32','monotone'),('batch32','monotone'),('batch32','stream'),('batch32','delta32'),('batch32','safe32'))"+code[end:]
old="expected=dict(direct=direct,split=split,fixed=fixed(split),bulk=bulk(split),combined=bulk(fixed(split)))"
assert code.count(old)==1
code=code.replace(old,'expected=variants(direct)')
code=code.replace("assert len(m['jobs'])==30","assert len(m['jobs'])==50")
code=code.replace("'scale':48}","'scale':48,'focused':320,'holdout':96}")
code=code.replace("('bench','pairs','scale')","('bench','pairs','scale','focused','holdout')")
marker="        assert (out/'generated/BulkExtra.cs').read_bytes()==z.read('Audit~/DenseBulk/Extra.cs')"
assert marker in code
code=code.replace(marker,marker+"\n        for a,b in (('StreamExtra.cs','Audit~/DenseStream/Extra.cs'),('CacheExtra.cs','Audit~/DenseCache/Extra.cs'),('UnequalExtra.cs','Audit~/DenseCacheUnequal/Extra.cs'),('BatchExtra.cs','Audit~/DenseDeltaSafety/Extra.cs')): assert (out/'generated'/a).read_bytes()==z.read(b)")
marker="        if 'bulkTests' in d:"
code=code.replace(marker,"        if 'cacheTests' in d: assert d['cacheTests']['failures']==0 and d['cacheTests']['cases']==544 and d['cacheTests']['preparedBytes']==0\n        if 'batchTests' in d: assert d['batchTests']['failures']==0 and d['batchTests']['cases']==176\n"+marker)
marker="    tests={}"
fault='''    faults={};hashes=json.loads((out/'FaultSources.json').read_text())
    for name in NAMES:
        d=json.loads((out/(name+'-fault.json')).read_text())
        assert d['label']==name and d['scopedFailures']==0 and d['exceptions']>0
        if name=='delta32':assert d['minimalBad']==1
        original=(g/name/'DirectArraySet.cs').read_text()
        assert 'AllocationFault' not in original
        instrumented=original.replace('var next = new Entry[size];','AllocationFault.BeforeAllocation(size); var next = new Entry[size];',1)
        assert (g/('FaultCore-'+name+'.cs')).read_text()==instrumented
        assert hashes[name]['original']==sha(original.encode())
        assert hashes[name]['instrumented']==sha(instrumented.encode())
        assert hashes[name]['binary']==sha((out/'build'/(name+'-fault')/'bin'/(name+'-fault.dll')).read_bytes())
        faults[name]=d
'''
assert marker in code;code=code.replace(marker,fault+marker,1)
code=code.replace('tests=tests,AA=aa,summary=summary','tests=tests,faults=faults,AA=aa,summary=summary')
code=code.replace("if k not in ('tests','AA')","if k not in ('tests','faults','AA')")
exec(compile(code,str(root/'Audit~/DenseBulk/review.py'),'exec'))
