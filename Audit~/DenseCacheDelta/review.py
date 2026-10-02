from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/DenseBulk/review.py').read_text()
code=code.replace('from generate import EXPECTED,BASE,prior,fixed,bulk,block','from generate import EXPECTED,BASE,prior,fixed,bulk,block,bounded,monotone,stream,wrapped,delta')
code=code.replace("NAMES=('direct','split','fixed','bulk','combined')","NAMES=('monotone','cache32','delta32')")
start=code.index('PAIRS=');end=code.index('\ndef sha',start)
code=code[:start]+"PAIRS=(('cache32','monotone'),('delta32','cache32'),('delta32','monotone'))"+code[end:]
old="expected=dict(direct=direct,split=split,fixed=fixed(split),bulk=bulk(split),combined=bulk(fixed(split)))"
assert old in code
code=code.replace(old,"f=fixed(split);s=stream(bounded(bulk(f)));c=wrapped(s,True);expected=dict(monotone=monotone(f),cache32=c,delta32=delta(c))")
code=code.replace("assert len(m['jobs'])==30","assert len(m['jobs'])==6")
code=code.replace("'scale':48}","'scale':48,'focused':320}")
code=code.replace("('bench','pairs','scale')","('focused',)")
marker="        assert (out/'generated/BulkExtra.cs').read_bytes()==z.read('Audit~/DenseBulk/Extra.cs')"
code=code.replace(marker,marker+"\n        assert (out/'generated/UnequalExtra.cs').read_bytes()==z.read('Audit~/DenseCacheUnequal/Extra.cs')\n        assert (out/'generated/CacheExtra.cs').read_bytes()==z.read('Audit~/DenseCache/Extra.cs')\n        assert (out/'generated/StreamExtra.cs').read_bytes()==z.read('Audit~/DenseStream/Extra.cs')")
marker="        if 'bulkTests' in d:"
code=code.replace(marker,"        if 'cacheTests' in d: assert d['cacheTests']['failures']==0 and d['cacheTests']['cases']==544 and d['cacheTests']['preparedBytes']==0\n"+marker)
exec(compile(code,str(root/'Audit~/DenseBulk/review.py'),'exec'))
