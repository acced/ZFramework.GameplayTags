#!/usr/bin/env python3
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/DenseBulk/review.py').read_text()
code=code.replace('from generate import EXPECTED,BASE,prior,fixed,bulk,block','from generate import EXPECTED,BASE,prior,fixed,bulk,block,variants')
code=code.replace("NAMES=('direct','split','fixed','bulk','combined')","NAMES=('control','eager','checkpoint')")
a=code.index('PAIRS=');b=code.index('\ndef sha',a)
code=code[:a]+"PAIRS=(('eager','control'),('checkpoint','control'),('checkpoint','eager'))"+code[b:]
old="expected=dict(direct=direct,split=split,fixed=fixed(split),bulk=bulk(split),combined=bulk(fixed(split)))"
assert old in code;code=code.replace(old,'expected=variants(direct)',1)
code=code.replace("assert len(m['jobs'])==30","assert len(m['jobs'])==12")
code=code.replace("'scale':48}","'scale':48,'copy':192}")
code=code.replace("('bench','pairs','scale')","('bench','copy')")
code=code.replace("'copy_from'}","'copy_from','prepared_copy','empty_append','union_same_into','union_empty_into'}")
marker="        assert (out/'generated/BulkExtra.cs').read_bytes()==z.read('Audit~/DenseBulk/Extra.cs')"
assert marker in code
code=code.replace(marker,marker+"\n        assert (out/'generated/CopyExtra.cs').read_bytes()==z.read('Audit~/CopyCommit/Extra.cs')")
marker='    tests={}'
fault='''    faults={};hashes=json.loads((out/'FaultSources.json').read_text())
    for name in NAMES:
        d=json.loads((out/(name+'-fault.json')).read_text())
        assert d['label']==name and d['failures']==0 and d['injected']==d['cases'] and d['cases']>0
        if name=='control': assert d['minimalBad']==1 and len(d['invalid'])>0
        else: assert d['minimalBad']==0 and not d['invalid']
        original=(g/name/'DirectArraySet.cs').read_text()
        assert 'CopyFault' not in original
        instrumented=original.replace('var next = new Entry[size];','CopyFault.Before(); var next = new Entry[size];',1)
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
