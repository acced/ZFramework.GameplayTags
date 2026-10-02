#!/usr/bin/env python3
"""Repeat frozen checks, including the whole 2x2 experiment and unaffected paths."""
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/CopyCommit/review.py').read_text()
tail="exec(compile(code,str(root/'Audit~/DenseBulk/review.py'),'exec'))"
assert code.count(tail)==1
patch='''
code=code.replace("NAMES=('control','eager','checkpoint')","NAMES=('control','eager','guarded','isolated','empty','boundary')")
a=code.index('PAIRS=');b=code.index('\\ndef sha',a)
code=code[:a]+"PAIRS=(('eager','control'),('guarded','eager'),('isolated','guarded'),('empty','guarded'),('boundary','empty'),('boundary','isolated'),('boundary','guarded'),('boundary','eager'),('boundary','control'))"+code[b:]
code=code.replace("assert len(m['jobs'])==12","assert len(m['jobs'])==36")
code=code.replace("'copy':192}","'copy':192,'empty':96}")
code=code.replace("('bench','copy')","('bench','copy','empty')")
code=code.replace("'union_empty_into'}","'union_empty_into','empty_copy','empty_clear_append','empty_same_union'}")
marker="        assert (out/'generated/CopyExtra.cs').read_bytes()==z.read('Audit~/CopyCommit/Extra.cs')"
assert code.count(marker)==1
code=code.replace(marker,marker+"\\n        assert (out/'generated/BoundaryExtra.cs').read_bytes()==z.read('Audit~/CopyBoundary/Extra.cs')")
marker="            assert d['bulkTests']['growthCases']==1920"
assert code.count(marker)==1
code=code.replace(marker,"            assert d['boundaryTests']['cases']==96 and d['boundaryTests']['failures']==0\\n"+marker,1)
'''
code=code.replace(tail,patch+'\n'+tail,1)
exec(compile(code,str(root/'Audit~/CopyCommit/review.py'),'exec'))
