#!/usr/bin/env python3
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/DenseBulk/review.py').read_text()
code=code.replace('from generate import EXPECTED,BASE,prior,fixed,bulk,block','from generate import BASE,block,parent,run_merge\nEXPECTED=parent.EXPECTED\nprior=parent.prior\nfixed=parent.fixed')
code=code.replace("NAMES=('direct','split','fixed','bulk','combined')","NAMES=('monotone','stream','rotate','buffer32')")
a=code.index('PAIRS=');b=code.index('\ndef sha',a)
code=code[:a]+"PAIRS=(('rotate','monotone'),('rotate','stream'),('buffer32','monotone'),('buffer32','stream'),('buffer32','rotate'))"+code[b:]
old='expected=dict(direct=direct,split=split,fixed=fixed(split),bulk=bulk(split),combined=bulk(fixed(split)))'
assert old in code
code=code.replace(old,"f=parent.fixed(split);mono=parent.monotone(f);s=parent.stream(parent.bounded(parent.bulk(f)));expected=dict(monotone=mono,stream=s,rotate=run_merge(mono,0),buffer32=run_merge(mono,32))")
code=code.replace("assert len(m['jobs'])==30","assert len(m['jobs'])==24")
code=code.replace("{'bench':126 if m['smoke'] else 2520,'pairs':16 if m['smoke'] else 192,'scale':48}","{'focus':120,'scale':48,'holdout':64}")
code=code.replace("for stage in ('bench','pairs','scale')","for stage in ('focus','scale','holdout')")
marker="        assert (out/'generated/BulkExtra.cs').read_bytes()==z.read('Audit~/DenseBulk/Extra.cs')"
code=code.replace(marker,marker+"\n        assert (out/'generated/StreamExtra.cs').read_bytes()==z.read('Audit~/DenseStream/Extra.cs')\n        assert (out/'generated/RunsExtra.cs').read_bytes()==z.read('Audit~/DenseRuns/Extra.cs')\n        for template in ('Append.cs.txt','Merge.cs.txt'): assert (HERE/template).read_bytes()==z.read('Audit~/DenseRuns/'+template)")
marker="        if 'bulkTests' in d:"
code=code.replace(marker,"        if 'runTests' in d: assert d['runTests']['failures']==0 and d['runTests']['cases']==630\n        if 'streamTests' in d: assert d['streamTests']['failures']==0 and d['streamTests']['cases']==1175\n"+marker)
exec(compile(code,str(root/'Audit~/DenseBulk/review.py'),'exec'))
