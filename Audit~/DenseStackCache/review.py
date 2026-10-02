#!/usr/bin/env python3
"""Reconstruct raw medians and contracts; no slow-sample filtering."""
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/DenseBulk/review.py').read_text()
code=code.replace('from generate import EXPECTED,BASE,prior,fixed,bulk,block','from generate import EXPECTED,BASE,prior,fixed,bulk,block,bounded,monotone,stream,cached')
code=code.replace("NAMES=('direct','split','fixed','bulk','combined')","NAMES=('fixed','monotone','stream','cache32')")
start=code.index('PAIRS=');end=code.index('\ndef sha',start)
code=code[:start]+"PAIRS=(('monotone','fixed'),('stream','fixed'),('stream','monotone'),('cache32','stream'),('cache32','monotone'),('cache32','fixed'))"+code[end:]
old="expected=dict(direct=direct,split=split,fixed=fixed(split),bulk=bulk(split),combined=bulk(fixed(split)))"
assert old in code
code=code.replace(old,"f=fixed(split);s=stream(bounded(bulk(f)));expected=dict(fixed=f,monotone=monotone(f),stream=s,cache32=cached(s))")
code=code.replace("assert len(m['jobs'])==30","assert len(m['jobs'])==32")
code=code.replace("'scale':48}","'scale':48,'boundary':180}")
code=code.replace("('bench','pairs','scale')","('bench','pairs','scale','boundary')")
marker="        assert (out/'generated/BulkExtra.cs').read_bytes()==z.read('Audit~/DenseBulk/Extra.cs')"
assert marker in code
code=code.replace(marker,marker+"\n        assert (out/'generated/StreamExtra.cs').read_bytes()==z.read('Audit~/DenseStream/Extra.cs')\n        assert (out/'generated/StackExtra.cs').read_bytes()==z.read('Audit~/DenseStackCache/Extra.cs')",1)
marker="        if 'bulkTests' in d:"
code=code.replace(marker,"        if 'streamTests' in d: assert d['streamTests']['failures']==0 and d['streamTests']['cases']==1175\n        if 'stackTests' in d: assert d['stackTests']['failures']==0 and d['stackTests']['cases']==780\n"+marker,1)
exec(compile(code,str(root/'Audit~/DenseBulk/review.py'),'exec'))
