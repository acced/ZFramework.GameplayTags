#!/usr/bin/env python3
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/DenseBulk/review.py').read_text()
code=code.replace('from generate import EXPECTED,BASE,prior,fixed,bulk,block','from generate import EXPECTED,BASE,prior,fixed,bulk,block,bounded,monotone,stream')
code=code.replace("NAMES=('direct','split','fixed','bulk','combined')","NAMES=('fixed','bounded','monotone','stream')")
start=code.index('PAIRS=');end=code.index('\ndef sha',start)
code=code[:start]+"PAIRS=(('bounded','fixed'),('monotone','fixed'),('stream','bounded'),('stream','fixed'),('stream','monotone'))"+code[end:]
old="expected=dict(direct=direct,split=split,fixed=fixed(split),bulk=bulk(split),combined=bulk(fixed(split)))"
assert old in code
code=code.replace(old,"f=fixed(split);b=bounded(bulk(f));expected=dict(fixed=f,bounded=b,monotone=monotone(f),stream=stream(b))")
code=code.replace("assert len(m['jobs'])==30","assert len(m['jobs'])==24")
marker="        assert (out/'generated/BulkExtra.cs').read_bytes()==z.read('Audit~/DenseBulk/Extra.cs')"
assert marker in code
code=code.replace(marker,marker+"\n        assert (out/'generated/StreamExtra.cs').read_bytes()==z.read('Audit~/DenseStream/Extra.cs')",1)
marker="        if 'bulkTests' in d:"
assert marker in code
code=code.replace(marker,"        if 'streamTests' in d: assert d['streamTests']['failures']==0 and d['streamTests']['cases']==1175\n"+marker,1)
exec(compile(code,str(root/'Audit~/DenseBulk/review.py'),'exec'))
