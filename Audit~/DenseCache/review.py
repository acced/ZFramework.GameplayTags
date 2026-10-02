#!/usr/bin/env python3
"""Reconstruct every sample; compare fixed alternatives without deleting regressions."""
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/DenseBulk/review.py').read_text()
code=code.replace('from generate import EXPECTED,BASE,prior,fixed,bulk,block','from generate import EXPECTED,BASE,prior,fixed,bulk,block,bounded,monotone,stream,wrapped')
code=code.replace("NAMES=('direct','split','fixed','bulk','combined')","NAMES=('fixed','monotone','stream','dispatch','cache32')")
start=code.index('PAIRS=');end=code.index('\ndef sha',start)
code=code[:start]+"PAIRS=(('monotone','fixed'),('stream','fixed'),('dispatch','stream'),('cache32','dispatch'),('cache32','stream'),('cache32','monotone'),('cache32','fixed'))"+code[end:]
old="expected=dict(direct=direct,split=split,fixed=fixed(split),bulk=bulk(split),combined=bulk(fixed(split)))"
assert old in code
code=code.replace(old,"f=fixed(split);s=stream(bounded(bulk(f)));expected=dict(fixed=f,monotone=monotone(f),stream=s,dispatch=wrapped(s,False),cache32=wrapped(s,True))")
code=code.replace("assert len(m['jobs'])==30","assert len(m['jobs'])==40")
marker="        assert (out/'generated/BulkExtra.cs').read_bytes()==z.read('Audit~/DenseBulk/Extra.cs')"
assert marker in code
code=code.replace(marker,marker+"\n        assert (out/'generated/StreamExtra.cs').read_bytes()==z.read('Audit~/DenseStream/Extra.cs')\n        assert (out/'generated/CacheExtra.cs').read_bytes()==z.read('Audit~/DenseCache/Extra.cs')",1)
marker="        if 'bulkTests' in d:"
assert marker in code
code=code.replace(marker,"        if 'streamTests' in d: assert d['streamTests']['failures']==0 and d['streamTests']['cases']==1175\n        if 'cacheTests' in d: assert d['cacheTests']['failures']==0 and d['cacheTests']['cases']==544 and d['cacheTests']['preparedBytes']==0\n"+marker,1)
code=code.replace("'scale':48}","'scale':48,'boundary':192}")
code=code.replace("('bench','pairs','scale')","('bench','pairs','scale','boundary')")
exec(compile(code,str(root/'Audit~/DenseBulk/review.py'),'exec'))
