from pathlib import Path
root=Path(__file__).resolve().parents[2]
# Run the unchanged full raw verifier with this fixture's explicit dimensions.
code=(root/'Audit~/DenseBulk/review.py').read_text()
code=code.replace('from generate import EXPECTED,BASE,prior,fixed,bulk,block','from generate import parent\nEXPECTED,BASE,prior,fixed,bulk,block,bounded,monotone,stream,wrapped=(parent.EXPECTED,parent.BASE,parent.prior,parent.fixed,parent.bulk,parent.block,parent.bounded,parent.monotone,parent.stream,parent.wrapped)')
code=code.replace("NAMES=('direct','split','fixed','bulk','combined')","NAMES=('fixed','monotone','stream','dispatch','cache32')")
start=code.index('PAIRS=');end=code.index('\ndef sha',start)
code=code[:start]+"PAIRS=(('dispatch','stream'),('cache32','dispatch'),('cache32','stream'),('cache32','fixed'),('cache32','monotone'))"+code[end:]
old="expected=dict(direct=direct,split=split,fixed=fixed(split),bulk=bulk(split),combined=bulk(fixed(split)))"
code=code.replace(old,"f=fixed(split);s=stream(bounded(bulk(f)));expected=dict(fixed=f,monotone=monotone(f),stream=s,dispatch=wrapped(s,False),cache32=wrapped(s,True))")
code=code.replace("assert len(m['jobs'])==30","assert len(m['jobs'])==10")
code=code.replace("'scale':48}","'scale':48,'unequal':128}")
code=code.replace("('bench','pairs','scale')","('unequal',)")
marker="        assert (out/'generated/BulkExtra.cs').read_bytes()==z.read('Audit~/DenseBulk/Extra.cs')"
code=code.replace(marker,marker+"\n        assert (out/'generated/UnequalExtra.cs').read_bytes()==z.read('Audit~/DenseCacheUnequal/Extra.cs')\n        assert (out/'generated/CacheExtra.cs').read_bytes()==z.read('Audit~/DenseCache/Extra.cs')\n        assert (out/'generated/StreamExtra.cs').read_bytes()==z.read('Audit~/DenseStream/Extra.cs')")
exec(compile(code,str(root/'Audit~/DenseBulk/review.py'),'exec'))
